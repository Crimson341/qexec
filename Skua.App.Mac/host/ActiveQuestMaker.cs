using Microsoft.CodeAnalysis.CSharp;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.Models.Quests;
using System.Text;

namespace Skua.Mac;

public sealed class ActiveQuestMaker(IScriptInterface bot, GearFinder finder, string scriptsRoot)
{
    public object Snapshot() => new { type="active-quests", quests=bot.Player.LoggedIn ? bot.Quests.Active.Select(q=>new {
        id=q.ID,name=q.Name,ready=q.Status=="c",
        rewards=q.SimpleRewards.Where(r=>r.Type==2).Select(r=>new {id=r.ID,name=q.Rewards.FirstOrDefault(i=>i.ID==r.ID)?.Name ?? "Item #"+r.ID}).ToArray()
    }).ToArray() : [] };

    public bool NeedsBank(int questId)
    {
        var quest=bot.Quests.Active.SingleOrDefault(q=>q.ID==questId) ?? throw new InvalidOperationException("This quest is no longer accepted.");
        return !bot.Quests.CanComplete(questId) && quest.Requirements.Any(r=>!r.Temp && !bot.Inventory.Contains(r.ID,r.Quantity));
    }

    public async Task<string> CreateAsync(int questId,int rewardId,Action<string>? progress=null,CancellationToken cancellation=default)
    {
        var quest=bot.Quests.Active.SingleOrDefault(q=>q.ID==questId) ?? throw new InvalidOperationException("This quest is no longer accepted. Refresh the list.");
        var choices=quest.SimpleRewards.Where(r=>r.Type==2).Select(r=>r.ID).ToArray();
        if(choices.Length>0 && !choices.Contains(rewardId)) throw new InvalidOperationException("Choose a reward for this quest.");
        if(choices.Length==0 && rewardId!=-1) throw new InvalidOperationException("This quest has no selectable reward.");
        bool ready=bot.Quests.CanComplete(questId);
        var requirements=ready ? new List<ItemBase>() : quest.Requirements;
        bool Owned(ItemBase req) => req.Temp ? bot.TempInv.Contains(req.ID,req.Quantity) : bot.Inventory.Contains(req.ID,req.Quantity);
        var missing=requirements.Where(r=>!Owned(r)).ToList();
        var bankOwned=requirements.Where(r=>!r.Temp && bot.Bank.Loaded && bot.Bank.Contains(r.ID,r.Quantity)).Select(r=>r.ID).ToHashSet();
        var routes=finder.Drops.Concat(AcceptedQuestRoutes.Find(scriptsRoot,questId,missing)).ToList();
        var pickups=new List<QuestPickup>();
        var unresolved=missing.Where(r=>!bankOwned.Contains(r.ID) && !routes.Any(d=>d.Temporary==r.Temp && string.Equals(d.Item,r.Name,StringComparison.OrdinalIgnoreCase))).ToList();
        if(unresolved.Count>0) {
            var signature=string.Join(";",quest.Requirements.Select(r=>$"{r.ID}:{r.Name}:{r.Quantity}:{r.Temp}"));
            var plan=await new QuestWikiResolver().ResolvePlan(quest.Name,quest.Rewards.OrderByDescending(r=>r.ID==rewardId).Select(r=>r.Name),unresolved,progress,cancellation);
            routes.AddRange(plan.Drops);pickups.AddRange(plan.Pickups);
            var current=bot.Quests.Active.SingleOrDefault(q=>q.ID==questId);
            if(!bot.Player.LoggedIn || current==null || signature!=string.Join(";",current.Requirements.Select(r=>$"{r.ID}:{r.Name}:{r.Quantity}:{r.Temp}")))
                throw new InvalidOperationException("Accepted quest changed during source lookup. Refresh and retry.");
            missing=missing.Where(r=>!Owned(r)).ToList();
        }
        cancellation.ThrowIfCancellationRequested();
        progress?.Invoke("Writing a new script with verified objective steps…");
        string code=Generate(questId,rewardId,missing,routes,(name,id)=>GearRoutes.Shops(scriptsRoot,name,id).FirstOrDefault(),bankOwned,pickups,bot.Bank.Loaded);
        string dir=Path.Combine(scriptsRoot,"Generated-Quests");Directory.CreateDirectory(dir);
        string file=Path.Combine(dir,"Quest-"+questId+"-"+Guid.NewGuid().ToString("N")+".cs");File.WriteAllText(file,string.Join("\n",routes.Select(r=>r.Evidence).Concat(pickups.Select(p=>p.Evidence)).Where(e=>e.StartsWith("http")).Distinct().Select(e=>"// Verified source: "+e.Replace("\n"," ").Replace("\r"," ")))+"\n"+code);return file;
    }
    public static string Generate(int questId,int rewardId,IEnumerable<ItemBase> requirements,IEnumerable<GearDrop> drops,Func<string,int,GearShop?> shopFor,ISet<int>? bankOwned=null,IEnumerable<QuestPickup>? pickups=null,bool bankAvailable=true)
    {
        string Q(string s)=>SymbolDisplay.FormatLiteral(s,true);
        var steps=new List<string>();var blocked=new List<string>();bool needsBank=false;
        foreach(var r in requirements) {
            if(r.ID<=0 || r.Quantity<=0 || string.IsNullOrWhiteSpace(r.Name)) { blocked.Add("Incomplete objective data (#"+r.ID+")");continue; }
            needsBank |= !r.Temp && bankAvailable;
            string inventory=r.Temp ? "bot.TempInv" : "bot.Inventory";
            var drop=drops.FirstOrDefault(d=>d.Temporary==r.Temp && string.Equals(d.Item,r.Name,StringComparison.OrdinalIgnoreCase));
            string guard="if(!bot.Quests.IsInProgress("+questId+")) throw new InvalidOperationException(\"Quest was abandoned; stopping.\"); if(bot.ShouldExit) return;\n";
            string unbank=r.Temp ? "" : "if(bot.Bank.Loaded && bot.Bank.Contains("+r.ID+")) core.Unbank("+r.ID+");\n";
            string action;
            if(!r.Temp && bankOwned?.Contains(r.ID)==true) action="";
            else if(pickups?.FirstOrDefault(p=>p.Temporary==r.Temp && string.Equals(p.Item,r.Name,StringComparison.OrdinalIgnoreCase)) is QuestPickup pickup)
                action="core.Join("+Q(pickup.Map)+"); Skua.Core.Scripts.QuestMapPickup.Acquire(bot,"+questId+","+r.ID+","+Q(r.Name)+","+r.Quantity+","+r.Temp.ToString().ToLowerInvariant()+");";
            else if(drop!=null) action="core.HuntMonster("+Q(drop.Map)+","+Q(drop.Monster)+","+Q(r.Name)+","+r.Quantity+","+r.Temp.ToString().ToLowerInvariant()+");";
            else if(bankAvailable && !r.Temp && shopFor(r.Name,r.ID) is GearShop shop) {
                action="core.Join("+Q(shop.Map)+"); bot.Shops.Load("+shop.ShopId+"); if(!bot.Shops.IsLoaded || bot.Shops.ID!="+shop.ShopId+") throw new InvalidOperationException(\"Required shop could not load.\");\n"+
                "var item=bot.Shops.Items.FirstOrDefault(i=>i.ID=="+r.ID+"); if(item==null) throw new InvalidOperationException(\"Required item not in shop.\"); if(item.Coins && item.Cost>0) throw new InvalidOperationException(\"Objective requires a premium-currency purchase. Buy it manually before retrying.\");\n"+
                "if(item.Requirements.Any(x=>!core.CheckInventory(x.ID,x.Quantity))) throw new InvalidOperationException(\"Shop ingredient has unresolved merge requirements.\");\n"+
                "bot.Shops.BuyItem(item.ID,item.ShopItemID,"+r.Quantity+"-bot.Inventory.GetQuantity("+r.ID+"));";
            } else { blocked.Add(r.Name+" (#"+r.ID+", x"+r.Quantity+")");continue; }
            steps.Add(guard+unbank+"if(!"+inventory+".Contains("+r.ID+","+r.Quantity+")) { "+(r.Temp ? "" : "core.AddDrop("+Q(r.Name)+");")+action+" }\nif(!"+inventory+".Contains("+r.ID+","+r.Quantity+")) throw new InvalidOperationException("+Q("Objective ID/quantity not acquired: "+r.Name)+");");
        }
        if(blocked.Count>0) throw new InvalidOperationException("Cannot generate a complete script yet. Missing objective routes: "+string.Join("; ",blocked)+". No script was started.");
        var code=new StringBuilder("// Generated to complete exactly one accepted quest, once.\n//cs_include Scripts/CoreBots.cs\nusing System;\nusing System.Linq;\nusing Skua.Core.Interfaces;\npublic class GeneratedAcceptedQuest { public void ScriptMain(IScriptInterface bot) { var core=CoreBots.Instance;\n");
        code.AppendLine("if(!bot.Quests.IsInProgress("+questId+")) throw new InvalidOperationException(\"Quest is no longer accepted.\");\n"+(needsBank ? "core.SetOptions(); " : "")+"try {");
        if(!bankAvailable && requirements.Any(r=>!r.Temp)) code.AppendLine("bot.Log(\"Bank could not be verified. Checking inventory and farming only free quest objectives; no purchases.\");");
        if(needsBank) code.AppendLine("if(!bot.Bank.Loaded) bot.Bank.Load(); if(!bot.Bank.Loaded) throw new InvalidOperationException(\"Bank check failed for permanent quest materials.\");");
        if(steps.Count>0) code.AppendLine("bot.Skills.StartAdvanced(bot.Player.CurrentClass?.Name ?? \"generic\",false);");
        foreach(string step in steps) code.AppendLine(step);
        code.AppendLine("if(bot.ShouldExit) return; if(!bot.Quests.IsInProgress("+questId+")) throw new InvalidOperationException(\"Quest was abandoned.\"); if(!bot.Quests.CanComplete("+questId+")) throw new InvalidOperationException(\"Quest requirements changed or remain incomplete.\");\nif(!bot.Quests.EnsureComplete("+questId+","+rewardId+")) throw new InvalidOperationException(\"Quest turn-in failed.\"); bot.Log(\"Selected quest completed once.\");\n} finally {core.SetOptions(false);} } }");
        return code.ToString();
    }
}
