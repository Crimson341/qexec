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

    public string Create(int questId,int rewardId)
    {
        var quest=bot.Quests.Active.SingleOrDefault(q=>q.ID==questId) ?? throw new InvalidOperationException("This quest is no longer accepted. Refresh the list.");
        var choices=quest.SimpleRewards.Where(r=>r.Type==2).Select(r=>r.ID).ToArray();
        if(choices.Length>0 && !choices.Contains(rewardId)) throw new InvalidOperationException("Choose a reward for this quest.");
        if(choices.Length==0 && rewardId!=-1) throw new InvalidOperationException("This quest has no selectable reward.");
        bool ready=bot.Quests.CanComplete(questId);
        var requirements=ready ? new List<ItemBase>() : quest.Requirements;
        bool Owned(ItemBase req) => req.Temp ? bot.TempInv.Contains(req.ID,req.Quantity) : bot.Inventory.Contains(req.ID,req.Quantity);
        var bankOwned=requirements.Where(r=>!r.Temp && bot.Bank.Loaded && bot.Bank.Contains(r.ID,r.Quantity)).Select(r=>r.ID).ToHashSet();
        string code=Generate(questId,rewardId,requirements.Where(r=>!Owned(r)),finder.Drops,(name,id)=>GearRoutes.Shops(scriptsRoot,name,id).FirstOrDefault(),bankOwned);
        string dir=Path.Combine(scriptsRoot,"Generated-Quests");Directory.CreateDirectory(dir);
        string file=Path.Combine(dir,"Quest-"+questId+"-"+Guid.NewGuid().ToString("N")+".cs");File.WriteAllText(file,code);return file;
    }
    public static string Generate(int questId,int rewardId,IEnumerable<ItemBase> requirements,IEnumerable<GearDrop> drops,Func<string,int,GearShop?> shopFor,ISet<int>? bankOwned=null)
    {
        string Q(string s)=>SymbolDisplay.FormatLiteral(s,true);
        var steps=new List<string>();var blocked=new List<string>();
        foreach(var r in requirements) {
            if(r.ID<=0 || r.Quantity<=0 || string.IsNullOrWhiteSpace(r.Name)) { blocked.Add("Incomplete objective data (#"+r.ID+")");continue; }
            string inventory=r.Temp ? "bot.TempInv" : "bot.Inventory";
            var drop=drops.FirstOrDefault(d=>d.Temporary==r.Temp && string.Equals(d.Item,r.Name,StringComparison.OrdinalIgnoreCase));
            string guard="if(!bot.Quests.IsInProgress("+questId+")) throw new InvalidOperationException(\"Quest was abandoned; stopping.\"); if(bot.ShouldExit) return;\n";
            string unbank=r.Temp ? "" : "if(bot.Bank.Contains("+r.ID+")) core.Unbank("+r.ID+");\n";
            string action;
            if(!r.Temp && bankOwned?.Contains(r.ID)==true) action="";
            else if(drop!=null) action="core.HuntMonster("+Q(drop.Map)+","+Q(drop.Monster)+","+Q(r.Name)+","+r.Quantity+","+r.Temp.ToString().ToLowerInvariant()+");";
            else if(!r.Temp && shopFor(r.Name,r.ID) is GearShop shop) {
                action="core.Join("+Q(shop.Map)+"); bot.Shops.Load("+shop.ShopId+"); if(!bot.Shops.IsLoaded || bot.Shops.ID!="+shop.ShopId+") throw new InvalidOperationException(\"Required shop could not load.\");\n"+
                "var item=bot.Shops.Items.FirstOrDefault(i=>i.ID=="+r.ID+"); if(item==null) throw new InvalidOperationException(\"Required item not in shop.\"); if(item.Coins && item.Cost>0) throw new InvalidOperationException(\"Objective requires a premium-currency purchase. Buy it manually before retrying.\");\n"+
                "if(item.Requirements.Any(x=>!core.CheckInventory(x.ID,x.Quantity))) throw new InvalidOperationException(\"Shop ingredient has unresolved merge requirements.\");\n"+
                "bot.Shops.BuyItem(item.ID,item.ShopItemID,"+r.Quantity+"-bot.Inventory.GetQuantity("+r.ID+"));";
            } else { blocked.Add(r.Name+" (#"+r.ID+", x"+r.Quantity+")");continue; }
            steps.Add(guard+unbank+"if(!"+inventory+".Contains("+r.ID+","+r.Quantity+")) { "+(r.Temp ? "" : "core.AddDrop("+Q(r.Name)+");")+action+" }\nif(!"+inventory+".Contains("+r.ID+","+r.Quantity+")) throw new InvalidOperationException("+Q("Objective ID/quantity not acquired: "+r.Name)+");");
        }
        if(blocked.Count>0) throw new InvalidOperationException("Cannot generate a complete script yet. Missing objective routes: "+string.Join("; ",blocked)+". No script was started.");
        var code=new StringBuilder("// Generated to complete exactly one accepted quest, once.\n//cs_include Scripts/CoreBots.cs\nusing System;\nusing System.Linq;\nusing Skua.Core.Interfaces;\npublic class GeneratedAcceptedQuest { public void ScriptMain(IScriptInterface bot) { var core=CoreBots.Instance;\n");
        code.AppendLine("if(!bot.Quests.IsInProgress("+questId+")) throw new InvalidOperationException(\"Quest is no longer accepted.\");\ncore.SetOptions(); try {");
        if(steps.Count>0) code.AppendLine("if(!bot.Bank.Loaded) bot.Bank.Load(); if(!bot.Bank.Loaded) throw new InvalidOperationException(\"Bank check failed.\"); bot.Skills.StartAdvanced(bot.Player.CurrentClass?.Name ?? \"generic\",false);");
        foreach(string step in steps) code.AppendLine(step);
        code.AppendLine("if(bot.ShouldExit) return; if(!bot.Quests.IsInProgress("+questId+")) throw new InvalidOperationException(\"Quest was abandoned.\"); if(!bot.Quests.CanComplete("+questId+")) throw new InvalidOperationException(\"Quest requirements changed or remain incomplete.\");\nif(!bot.Quests.EnsureComplete("+questId+","+rewardId+")) throw new InvalidOperationException(\"Quest turn-in failed.\"); bot.Log(\"Selected quest completed once.\");\n} finally {core.SetOptions(false);} } }");
        return code.ToString();
    }
}
