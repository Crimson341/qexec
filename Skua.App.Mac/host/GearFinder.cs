using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Skua.Core.Models;

namespace Skua.Mac;

public sealed record GearSource(string Id, string Name, string Description, string File, string Code, string Item, int ItemId = 0, string Action = "Go — farm item", bool RequiresMissing = true);
public sealed record GearDrop(string Map, string Monster, string Item, bool Temporary, string Evidence);
public sealed class GearFinder(string scriptsRoot, string? questsPath = null)
{
    private readonly Lazy<List<GearDrop>> indexedDrops = new(() => IndexDrops(scriptsRoot));
    public List<GearDrop> Drops => indexedDrops.Value;
    private static List<GearDrop> IndexDrops(string scriptsRoot)
    {
        var drops = new List<GearDrop>();
        foreach (string file in Directory.EnumerateFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetRelativePath(scriptsRoot,file).Split(Path.DirectorySeparatorChar).Any(part=>part is "Generated-Gear" or "Generated-Quests" or "Generated-Area")) continue;
            if (new FileInfo(file).Length > 2_000_000) continue;
            string text = File.ReadAllText(file);
            if (!text.Contains("HuntMonster(")) continue;
            var root = CSharpSyntaxTree.ParseText(text).GetRoot();
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call.Expression is not MemberAccessExpressionSyntax member || member.Name.Identifier.ValueText != "HuntMonster") continue;
                var args = call.ArgumentList.Arguments;
                ExpressionSyntax? Arg(string name, int index) => args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == name)?.Expression
                    ?? (args.Count > index && args[index].NameColon == null ? args[index].Expression : null);
                string? Text(string name,int index) => Arg(name,index) is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) ? literal.Token.ValueText : null;
                string? map = Text("map",0), monster = Text("monster",1), drop = Text("item",2);
                var temp = Arg("isTemp",4);
                if (map == null || monster == null || drop == null || (temp != null && temp is not LiteralExpressionSyntax)) continue;
                if (temp != null && !temp.IsKind(SyntaxKind.TrueLiteralExpression) && !temp.IsKind(SyntaxKind.FalseLiteralExpression)) continue;
                bool temporary = temp == null || temp.IsKind(SyntaxKind.TrueLiteralExpression);
                int line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                drops.Add(new(map,monster,drop,temporary,Path.GetRelativePath(scriptsRoot,file) + ":" + line));
            }
        }
        return drops;
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, GearSource> allowed = new();
    private static string Quote(string value) => SymbolDisplay.FormatLiteral(value, true);
    public List<GearSource> Find(string item, int itemId = 0)
    {
        allowed.Clear();
        if (string.IsNullOrWhiteSpace(item) || item.Length > 200) throw new ArgumentException("Select an item with a known name.");
        var drops = Drops;
        var results = new List<GearSource>();
        void Add(string description, IEnumerable<(GearDrop Drop,int Quantity)> steps, int quest = 0, int reward = -1)
        {
            var list = steps.ToList();
            string id = Guid.NewGuid().ToString("N");
            string code = Generate(item,list,quest,reward,itemId);
            var source = new GearSource(id,"Farm " + item,description,string.Join("\n",list.Select(s=>s.Drop.Evidence).Distinct()),code,item,itemId);
            results.Add(source); allowed[id] = source;
        }
        // Inventory drops only. Temporary quest items are not wearable rewards.
        foreach (var drop in drops.Where(d => !d.Temporary && d.Item.Equals(item,StringComparison.OrdinalIgnoreCase))
            .DistinctBy(d => (d.Map,d.Monster)).Take(10))
            Add("Monster drop: " + drop.Monster + " in /join " + drop.Map + ". Stop after owning one. Source: local script call; map access may be required.",new[]{(drop,1)});
        string questFile = questsPath ?? ClientFileSources.SkuaQuestsFile;
        if (File.Exists(questFile))
        foreach (var quest in JArray.Parse(File.ReadAllText(questFile)).OfType<JObject>())
        {
            var reward = ((quest["Rewards"] as JArray) ?? new()).Concat((quest["SimpleRewards"] as JArray) ?? new())
                .FirstOrDefault(r => (itemId <= 0 || (int?)r["ItemID"] == itemId) && string.Equals((string?)r["sName"],item,StringComparison.OrdinalIgnoreCase));
            if (reward == null || (int?)reward["ItemID"] is not int rewardId || rewardId <= 0) continue;
            // A typed reward recipe can safely carry the prerequisite chain that raw drop extraction cannot.
            int knownQuestId=(int?)quest["ID"] ?? 0;
            if(knownQuestId>0) foreach(var recipe in GearQuestRecipes.Find(scriptsRoot,knownQuestId,rewardId)) {
                string planId=Guid.NewGuid().ToString("N");
                var plan=new GearSource(planId,"Acquire "+item+" — "+(string?)quest["Name"],"Generated acquisition script for quest #"+knownQuestId+" and reward #"+rewardId+". Selects the exact reward and runs the installed quest routine, including its prerequisite quests, farming, and required shop purchases. Account access and resource requirements still apply.",recipe.File,GearQuestRecipes.Generate(recipe),item,rewardId,"Create script & farm reward",true);
                results.Add(plan);allowed[planId]=plan;
            }
            // Do not guess story, membership, currency, or item prerequisites.
            if ((bool?)quest["Upgrade"] == true || (quest["AcceptRequirements"] as JArray)?.Count > 0) continue;
            var requirements = quest["Requirements"] as JArray;
            if (requirements == null || requirements.Count == 0) continue;
            var steps = new List<(GearDrop Drop,int Quantity)>();
            foreach (var req in requirements)
            {
                string? name = (string?)req["sName"];
                bool temp = (string?)req["bTemp"] == "1";
                var sources = drops.Where(d => d.Temporary == temp && string.Equals(d.Item,name,StringComparison.OrdinalIgnoreCase)).DistinctBy(d=>(d.Map,d.Monster)).ToList();
                // Multiple possible sources need user resolution before automation.
                if (sources.Count != 1 || (int?)req["iQty"] is not int quantity || quantity <= 0) { steps.Clear(); break; }
                steps.Add((sources[0],quantity));
            }
            if (steps.Count != requirements.Count || (int?)quest["ID"] is not int questId || questId <= 0) continue;
            Add("Quest: " + (string?)quest["Name"] + " (#" + questId + "). " + string.Join("; ",steps.Select(s=>$"{s.Quantity} × {s.Drop.Item} from {s.Drop.Monster} in /join {s.Drop.Map}")) + ". Stops if the quest cannot be accepted or completed; repeats until the selected reward is owned. Quest metadata is local and may be outdated.",steps,questId,rewardId);
        }
        foreach(var recipe in GearRoutes.FullFarms(scriptsRoot,item)) {
            string id=Guid.NewGuid().ToString("N");
            string identity=itemId>0 ? itemId.ToString() : Quote(item);
            string code="// Generated exact-goal wrapper. Reuses the installed full farming recipe and its options.\n//cs_include Scripts/"+recipe.File+"\nusing System;\nusing Skua.Core.Interfaces;\npublic class GeneratedFullGearFarm : "+recipe.Class+" { public new void ScriptMain(IScriptInterface bot) {\nif(bot.Inventory.Contains("+identity+")) return; if(!bot.Bank.Loaded) bot.Bank.Load(); if(!bot.Bank.Loaded) throw new InvalidOperationException(\"Bank check failed.\"); if(bot.Bank.Contains("+identity+")) return;\nbase.ScriptMain(bot); if(!bot.ShouldExit && !bot.Inventory.Contains("+identity+") && !bot.Bank.Contains("+identity+")) bot.Log(\"Goal is not owned yet. Check daily limits, requirements, and the activity log.\"); } }";
            var plan=new GearSource(id,"Full farm for "+item,"Generated item-ID guard and full farming wrapper around an exact-title installed recipe. Reuses that recipe's prerequisites and options; may take multiple sessions or daily resets.",recipe.File,code,item,itemId,"Go — full farming route",true);
            results.Add(plan);allowed[id]=plan;
        }
        foreach(var shop in GearRoutes.Shops(scriptsRoot,item,itemId)) {
            string id=Guid.NewGuid().ToString("N");
            var plan=new GearSource(id,"Open shop for "+item,"Travel to /"+shop.Map+", open shop #"+shop.ShopId+", verify the selected item and list its current merge requirements. Opens the shop for review; does not purchase or spend currency.",shop.Evidence,GenerateShop(item,itemId,shop),item,itemId,"Go — open shop",false);
            results.Add(plan); allowed[id]=plan;
            string farmId=Guid.NewGuid().ToString("N");
            var farmPlan=new GearSource(farmId,"Prepare merge materials for "+item,"Reads this item's live shop recipe, checks inventory and bank, and farms missing materials with unambiguous local monster-drop routes. If any missing material lacks a route, it lists the blockers before farming. Returns to the shop for purchase; does not spend currency.",shop.Evidence,GenerateMerge(item,itemId,shop,Drops),item,itemId,"Go — prepare merge materials",true);
            results.Add(farmPlan);allowed[farmId]=farmPlan;
        }
        return results;
    }
    public async Task<object> Lookup(string item,int itemId)
    {
        var results=await Task.Run(()=>Find(item,itemId));
        string note;
        string? url=null;
        if(results.Count==0) {
            var web=await GearWeb.Lookup(item); note=web.Summary;url=web.Url;
            if(web.Map is string map) {
                string id=Guid.NewGuid().ToString("N");
                string code="//cs_include Scripts/CoreBots.cs\nusing Skua.Core.Interfaces;\npublic class GeneratedItemRoute { public void ScriptMain(IScriptInterface bot) { CoreBots.Instance.Join("+Quote(map)+"); bot.Log("+Quote(web.Summary)+"); } }";
                var plan=new GearSource(id,"Travel to source for "+item,note,url,code,item,itemId,"Go — travel to source",false);
                results.Add(plan);allowed[id]=plan;
            }
        } else note="Generated "+results.Count+" route(s) for "+item+(itemId>0 ? " (#"+itemId+")" : "")+". Review the action: farming, opening a shop, and travel have different outcomes.";
        return new {type="gear-sources",sources=results,message=note,url};
    }
    public GearSource SourceFor(string id) => allowed.TryGetValue(id,out var source) ? source : throw new ArgumentException("Find this item's sources again.");
    private static string GenerateShop(string item,int itemId,GearShop shop)
    {
        string match=itemId>0 ? "i.ID == "+itemId : "string.Equals(i.Name,"+Quote(item)+",StringComparison.OrdinalIgnoreCase)";
        return "// Generated item-specific shop route. No purchase is made.\n//cs_include Scripts/CoreBots.cs\nusing System;\nusing System.Linq;\nusing Skua.Core.Interfaces;\npublic class GeneratedShopRoute { public void ScriptMain(IScriptInterface bot) {\n"+
            "CoreBots.Instance.Join("+Quote(shop.Map)+"); if(bot.ShouldExit) return;\nbot.Shops.Load("+shop.ShopId+");\nif(!bot.Shops.IsLoaded || bot.Shops.ID != "+shop.ShopId+") throw new InvalidOperationException(\"Shop unavailable: check map access and story prerequisites.\");\n"+
            "var item=bot.Shops.Items.FirstOrDefault(i=>"+match+");\nif(item==null) throw new InvalidOperationException(\"Selected item ID not found in this shop. The route may be outdated or the inspected player changed gear.\");\n"+
            "bot.Log(\"Opened shop for \"+item.Name+\" (#\"+item.ID+\"). Review the price and purchase in the shop.\");\nforeach(var req in item.Requirements) bot.Log(\"Merge requires \"+req.Quantity+\" x \"+req.Name+\" (#\"+req.ID+\")\");\n} }";
    }
    private static string GenerateMerge(string item,int itemId,GearShop shop,List<GearDrop> drops)
    {
        var routes=drops.Where(d=>!d.Temporary).GroupBy(d=>d.Item,StringComparer.OrdinalIgnoreCase)
            .Where(g=>g.DistinctBy(d=>(d.Map,d.Monster)).Count()==1).Select(g=>g.First()).ToList();
        var code=new System.Text.StringBuilder("// Generated from live merge requirements and indexed drop evidence.\n//cs_include Scripts/CoreBots.cs\nusing System;\nusing System.Linq;\nusing System.Collections.Generic;\nusing Skua.Core.Interfaces;\npublic class GeneratedMergeMaterials { public void ScriptMain(IScriptInterface bot) { var core=CoreBots.Instance;\n");
        code.AppendLine("var farm=new Dictionary<string,Action<int>>(StringComparer.OrdinalIgnoreCase) {");
        foreach(var route in routes) code.AppendLine("["+Quote(route.Item)+"] = qty => core.HuntMonster("+Quote(route.Map)+","+Quote(route.Monster)+","+Quote(route.Item)+",qty,false),");
        code.AppendLine("};");
        code.AppendLine("core.Join("+Quote(shop.Map)+"); if(bot.ShouldExit) return; bot.Shops.Load("+shop.ShopId+");");
        code.AppendLine("if(!bot.Shops.IsLoaded || bot.Shops.ID!="+shop.ShopId+") throw new InvalidOperationException(\"Shop unavailable. Check story/map access.\");");
        code.AppendLine("var target=bot.Shops.Items.FirstOrDefault(i=>"+(itemId>0 ? "i.ID=="+itemId : "string.Equals(i.Name,"+Quote(item)+",StringComparison.OrdinalIgnoreCase)")+"); if(target==null) throw new InvalidOperationException(\"Exact selected item is not in this shop.\");");
        code.AppendLine("if(!bot.Bank.Loaded) bot.Bank.Load(); if(!bot.Bank.Loaded) throw new InvalidOperationException(\"Bank verification failed.\");");
        code.AppendLine("var missing=target.Requirements.Where(r=>bot.Inventory.GetQuantity(r.ID)+bot.Bank.GetQuantity(r.ID)<r.Quantity).ToList(); var blocked=missing.Where(r=>!farm.ContainsKey(r.Name)).ToList(); if(blocked.Count>0) { foreach(var r in blocked) bot.Log(\"Unresolved material route: \"+r.Quantity+\" x \"+r.Name+\" (#\"+r.ID+\")\"); throw new InvalidOperationException(\"Some merge materials require a quest, shop, or unlock route. No material farming started.\"); }");
        code.AppendLine("core.SetOptions(); try { foreach(var r in target.Requirements) { if(bot.ShouldExit) return; if(bot.Bank.Contains(r.ID)) core.Unbank(r.ID); } bot.Skills.StartAdvanced(bot.Player.CurrentClass?.Name ?? \"generic\",false); foreach(var r in missing) { if(bot.ShouldExit) return; core.AddDrop(r.Name); farm[r.Name](r.Quantity); if(!core.CheckInventory(r.ID,r.Quantity,false)) throw new InvalidOperationException(\"Material ID/quantity check failed: \"+r.Name); } } finally {core.SetOptions(false);} if(bot.ShouldExit) return; core.Join("+Quote(shop.Map)+"); bot.Shops.Load("+shop.ShopId+"); bot.Log(\"Material farming completed. Review the shop requirements and price before purchasing.\"); } }");
        return code.ToString();
    }
    public string ItemFor(string id) => allowed.TryGetValue(id, out var source) ? source.Item : throw new ArgumentException("Find the item again before farming.");
    public string Resolve(string id)
    {
        if (!allowed.TryGetValue(id,out var source)) throw new ArgumentException("Find this item's sources again before generating.");
        string directory = Path.Combine(scriptsRoot,"Generated-Gear"); Directory.CreateDirectory(directory);
        string file = Path.Combine(directory,"Gear-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(file,source.Code);
        return file;
    }
    private static string Generate(string item,List<(GearDrop Drop,int Quantity)> steps,int quest,int reward,int itemId)
    {
        var code = new System.Text.StringBuilder();
        string identity=itemId>0 ? itemId.ToString() : Quote(item);
        code.AppendLine("// Generated for one selected item. Source plan is shown in Inspect gear.");
        code.AppendLine("//cs_include Scripts/CoreBots.cs");
        code.AppendLine("using System;\nusing Skua.Core.Interfaces;\npublic class GeneratedGearFarm {\n public void ScriptMain(IScriptInterface bot) {\n var core = CoreBots.Instance;");
        code.AppendLine($" if (bot.Inventory.Contains({identity})) return;\n if (!bot.Bank.Loaded) bot.Bank.Load();\n if (!bot.Bank.Loaded) throw new InvalidOperationException(\"Cannot verify bank contents.\");\n if (bot.Bank.Contains({identity})) return;\n core.SetOptions();\n try {{\n core.AddDrop({Quote(item)});\n bot.Skills.StartAdvanced(bot.Player.CurrentClass?.Name ?? \"generic\", false);");
        if (quest > 0) code.AppendLine($" while (!bot.ShouldExit && !core.CheckInventory({identity}, 1, false)) {{\n if (!bot.Quests.EnsureAccept({quest})) throw new InvalidOperationException(\"Quest unavailable: check prerequisites.\");");
        foreach (var step in steps)
            code.AppendLine($" if (bot.ShouldExit) return;\n core.HuntMonster({Quote(step.Drop.Map)}, {Quote(step.Drop.Monster)}, {Quote(step.Drop.Item)}, {step.Quantity}, {step.Drop.Temporary.ToString().ToLowerInvariant()});");
        if (quest > 0) code.AppendLine($" if (bot.ShouldExit) return;\n if (!bot.Quests.EnsureComplete({quest}, {reward})) throw new InvalidOperationException(\"Quest turn-in failed.\");\n bot.Sleep(1000);\n }}");
        if(itemId>0) code.AppendLine(" if(!bot.ShouldExit && !bot.Inventory.Contains("+itemId+") && !bot.Bank.Contains("+itemId+")) throw new InvalidOperationException(\"Expected item ID was not acquired. Check the source and inspected equipment.\");");
        code.AppendLine(" } finally { core.SetOptions(false); }\n }\n}");
        return code.ToString();
    }
}
