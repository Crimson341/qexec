using Microsoft.CodeAnalysis.CSharp;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.Models.Quests;
using System.Text;

namespace Skua.Mac;

public sealed record QuestReturnPoint(string Map,string Cell,string Pad);

public sealed class ActiveQuestMaker(IScriptInterface bot, GearFinder finder, string scriptsRoot)
{
    readonly object locationLock=new();
    readonly Dictionary<int,QuestReturnPoint> acceptedAt=new();
    HashSet<int>? seenQuests;
    public object Snapshot() {
        var active=bot.Player.LoggedIn?bot.Quests.Active.ToArray():Array.Empty<Quest>();
        lock(locationLock) {
            if(!bot.Player.LoggedIn){acceptedAt.Clear();seenQuests=null;}
            else {
                var ids=active.Select(q=>q.ID).ToHashSet();
                foreach(int old in acceptedAt.Keys.Where(id=>!ids.Contains(id)).ToArray())acceptedAt.Remove(old);
                if(seenQuests!=null)foreach(int id in ids.Except(seenQuests))acceptedAt[id]=new(bot.Map.Name,bot.Player.Cell,bot.Player.Pad);
                seenQuests=ids;
            }
        }
        return new {type="active-quests",quests=active.Select(DescribeQuest).ToArray()};
    }
    object DescribeQuest(Quest q) => Describe(q.ID,q.Name,q.Requirements,
        (id,temp)=>temp?bot.TempInv.GetQuantity(id):bot.Inventory.GetQuantity(id)+(bot.Bank.Loaded?bot.Bank.GetQuantity(id):0),
        Try(()=>bot.Quests.CanCompleteFullCheck(q.ID),q.Status=="c"),
        Try(()=>bot.Quests.IsDailyComplete(q)),
        !Try(()=>bot.Quests.IsUnlocked(q),true),
        q.Upgrade,Try(()=>bot.Player.IsMember,true),
        q.SimpleRewards.Where(r=>r.Type==2).Select(r=>new {id=r.ID,name=q.Rewards.FirstOrDefault(i=>i.ID==r.ID)?.Name??"Item #"+r.ID}));
    static bool Try(Func<bool> check, bool fallback=false) { try { return check(); } catch { return fallback; } }
    public static object Describe(int id,string name,IEnumerable<ItemBase> requirements,Func<int,bool,int> quantity,bool ready,bool dailyDone,bool locked,bool member,bool isMember,IEnumerable<object> rewards)
    {
        var objectives=requirements.Where(r=>r.ID>0 && r.Quantity>0 && !string.IsNullOrWhiteSpace(r.Name))
            .Select(r=>new {id=r.ID,name=r.Name,have=Math.Max(0,quantity(r.ID,r.Temp)),need=r.Quantity,temporary=r.Temp}).ToArray();
        bool blocked=!ready && (dailyDone || locked || (member && !isMember));
        return new {id,name,ready,dailyDone,member,locked,blocked,objectives,rewards=rewards.ToArray()};
    }
    public object OpenInGame(int questId)
    {
        if(!bot.Player.LoggedIn) throw new InvalidOperationException("Log in first.");
        var quest=bot.Quests.Active.SingleOrDefault(q=>q.ID==questId) ?? throw new InvalidOperationException("This quest is no longer accepted. Refresh the ledger.");
        bot.Quests.Load(questId);
        return new {type="active-quest-opened",id=questId,message=quest.Name+" opened in game.",opened=true};
    }
    public object? AutoDoForReward(int itemId)
    {
        if(itemId<=0 || !bot.Player.LoggedIn) return null;
        var matches=bot.Quests.Active.Where(q=>q.Rewards.Any(r=>r.ID==itemId)).ToArray();
        if(matches.Length!=1) return null;
        var quest=matches[0];
        var choices=quest.SimpleRewards.Where(r=>r.Type==2).Select(r=>r.ID).ToArray();
        if(choices.Length>0 && !choices.Contains(itemId)) return null;
        return new {type="catalog-autodo",quest=DescribeQuest(quest),reward=choices.Length==0?-1:itemId};
    }

    public bool NeedsBank(int questId)
    {
        var quest=bot.Quests.Active.SingleOrDefault(q=>q.ID==questId) ?? throw new InvalidOperationException("This quest is no longer accepted.");
        return !bot.Quests.CanCompleteFullCheck(questId) && quest.Requirements.Any(r=>!r.Temp && !bot.Inventory.Contains(r.ID,r.Quantity));
    }

    public async Task<string> CreateAsync(int questId,int rewardId,Action<string>? progress=null,CancellationToken cancellation=default)
    {
        var quest=bot.Quests.Active.SingleOrDefault(q=>q.ID==questId) ?? throw new InvalidOperationException("This quest is no longer accepted. Refresh the list.");
        QuestReturnPoint returnTo;
        lock(locationLock)returnTo=acceptedAt.GetValueOrDefault(questId)??new(bot.Map.Name,bot.Player.Cell,bot.Player.Pad);
        var choices=quest.SimpleRewards.Where(r=>r.Type==2).Select(r=>r.ID).ToArray();
        if(choices.Length>0 && !choices.Contains(rewardId)) throw new InvalidOperationException("Choose a reward for this quest.");
        if(choices.Length==0 && rewardId!=-1) throw new InvalidOperationException("This quest has no selectable reward.");
        if(Try(()=>bot.Quests.IsDailyComplete(quest))) throw new InvalidOperationException("This daily quest is already completed today.");
        if(!Try(()=>bot.Quests.IsUnlocked(quest),true)) throw new InvalidOperationException("This quest is locked.");
        if(quest.Upgrade && !Try(()=>bot.Player.IsMember,true)) throw new InvalidOperationException("This quest requires membership.");
        bool ready=bot.Quests.CanCompleteFullCheck(questId);
        var requirements=ready ? new List<ItemBase>() : quest.Requirements;
        bool Owned(ItemBase req) => req.Temp ? bot.TempInv.Contains(req.ID,req.Quantity) : bot.Inventory.Contains(req.ID,req.Quantity);
        var missing=requirements.Where(r=>!Owned(r)).ToList();
        ThrowIfManualObjective(questId,missing);
        var bankOwned=requirements.Where(r=>!r.Temp && bot.Bank.Loaded && bot.Bank.Contains(r.ID,r.Quantity)).Select(r=>r.ID).ToHashSet();
        var routes=finder.Drops.Concat(AcceptedQuestRoutes.Find(scriptsRoot,questId,missing)).ToList();
        var pickups=AcceptedQuestRoutes.FindPickups(scriptsRoot,questId,requirements);
        var verified=AcceptedQuestRoutes.Verified(questId,missing);
        routes.AddRange(verified.Drops);pickups.AddRange(verified.Pickups);
        var unresolved=missing.Where(r=>!bankOwned.Contains(r.ID) && !pickups.Any(p=>p.Temporary==r.Temp && string.Equals(p.Item,r.Name,StringComparison.OrdinalIgnoreCase)) && !routes.Any(d=>d.Temporary==r.Temp && string.Equals(d.Item,r.Name,StringComparison.OrdinalIgnoreCase))).ToList();
        if(unresolved.Count>0) {
            var signature=string.Join(";",quest.Requirements.Select(r=>$"{r.ID}:{r.Name}:{r.Quantity}:{r.Temp}"));
            IReadOnlyList<AreaLink> questSources=[];
            try {questSources=(await new AreaDiscovery().Map(bot.Map.Name,cancellation))?.Quests??[];}
            catch(Exception e) when(!cancellation.IsCancellationRequested && e is HttpRequestException or TimeoutException or OperationCanceledException) { }
            var plan=await new QuestWikiResolver().ResolvePlan(quest.Name,quest.Rewards.OrderByDescending(r=>r.ID==rewardId).Select(r=>r.Name),unresolved,progress,cancellation,questSources.Select(q=>q.Path),bot.Map.Name);
            routes.AddRange(plan.Drops);
            foreach(var pickup in plan.Pickups) {
                var req=unresolved.SingleOrDefault(r=>r.Temp==pickup.Temporary && string.Equals(r.Name,pickup.Item,StringComparison.OrdinalIgnoreCase));
                var known=req==null?null:AcceptedQuestRoutes.FindPickups(scriptsRoot,questId,new[]{req}).FirstOrDefault(p=>p.Map==pickup.Map);
                pickups.Add(known??pickup);
            }
            var current=bot.Quests.Active.SingleOrDefault(q=>q.ID==questId);
            if(!bot.Player.LoggedIn || current==null || signature!=string.Join(";",current.Requirements.Select(r=>$"{r.ID}:{r.Name}:{r.Quantity}:{r.Temp}")))
                throw new InvalidOperationException("Accepted quest changed during source lookup. Refresh and retry.");
            missing=missing.Where(r=>!Owned(r)).ToList();
        }
        cancellation.ThrowIfCancellationRequested();
        progress?.Invoke("Writing a new script with verified objective steps…");
        string code=Generate(questId,rewardId,missing,routes,(name,id)=>GearRoutes.Shops(scriptsRoot,name,id).FirstOrDefault(),bankOwned,pickups,bot.Bank.Loaded,returnTo);
        string dir=Path.Combine(scriptsRoot,"Generated-Quests");Directory.CreateDirectory(dir);
        string file=Path.Combine(dir,"Quest-"+questId+"-"+Guid.NewGuid().ToString("N")+".cs");File.WriteAllText(file,string.Join("\n",routes.Select(r=>r.Evidence).Concat(pickups.Select(p=>p.Evidence)).Where(e=>e.StartsWith("http")).Distinct().Select(e=>"// Verified source: "+e.Replace("\n"," ").Replace("\r"," ")))+"\n"+code);return file;
    }
    // Story/DeathsRealm.cs explicitly requires player kills for this objective.
    // Keep this scoped to the verified quest and item, and allow an already-owned objective through.
    public static void ThrowIfManualObjective(int questId,IEnumerable<ItemBase> missing)
    {
        if(questId==2423 && missing.Any(r=>r.ID==13949))
            throw new InvalidOperationException("Hero Souls requires 5 player kills in /doomarena or /bludrutbrawl (PvP), according to the Death's Realm story script. Automatic PvP quest completion is not supported yet. Complete those kills, then click Auto-do this quest again to turn it in. No script was started.");
    }
    public static string ReturnCode(QuestReturnPoint? point) {
        if(point==null || string.IsNullOrWhiteSpace(point.Map))return "";
        string Q(string value)=>SymbolDisplay.FormatLiteral(value,true);
        string cell=string.IsNullOrWhiteSpace(point.Cell)?"Enter":point.Cell,pad=string.IsNullOrWhiteSpace(point.Pad)?"Spawn":point.Pad;
        return "if(bot.ShouldExit) return; bot.Log(\"Quest step: return\"); core.Join("+Q(point.Map)+","+Q(cell)+","+Q(pad)+"); if(bot.ShouldExit) return; if(!bot.Player.LoggedIn || bot.Map.Name!="+Q(point.Map)+" || bot.Player.Cell!="+Q(cell)+") throw new InvalidOperationException(\"Could not return to the quest location. Quest was not turned in.\");\n";
    }
    public static string Generate(int questId,int rewardId,IEnumerable<ItemBase> requirements,IEnumerable<GearDrop> drops,Func<string,int,GearShop?> shopFor,ISet<int>? bankOwned=null,IEnumerable<QuestPickup>? pickups=null,bool bankAvailable=true,QuestReturnPoint? returnTo=null)
    {
        requirements=requirements.ToList();
        ThrowIfManualObjective(questId,requirements);
        string Q(string s)=>SymbolDisplay.FormatLiteral(s,true);
        var steps=new List<string>();var blocked=new List<string>();bool needsBank=false;
        foreach(var r in requirements) {
            if(r.ID<=0 || r.Quantity<=0 || string.IsNullOrWhiteSpace(r.Name)) { blocked.Add("Incomplete objective data (#"+r.ID+")");continue; }
            needsBank |= !r.Temp && bankAvailable;
            string inventory=r.Temp ? "bot.TempInv" : "bot.Inventory";
            var drop=drops.FirstOrDefault(d=>d.Temporary==r.Temp && string.Equals(d.Item,r.Name,StringComparison.OrdinalIgnoreCase));
            string guard="if(!bot.Quests.IsInProgress("+questId+")) throw new InvalidOperationException(\"Quest was abandoned; stopping.\"); if(bot.ShouldExit) return;\n";
            string unbank=r.Temp ? "" : "if(bot.Bank.Loaded && bot.Bank.Contains("+r.ID+")) { bot.Log("+Q("Quest step: unbank "+r.Name)+"); core.Unbank("+r.ID+"); for(int sync=0;sync<20 && !bot.Inventory.Contains("+r.ID+","+r.Quantity+") && !bot.ShouldExit;sync++) System.Threading.Thread.Sleep(100); }\n";
            string action;
            if(!r.Temp && bankOwned?.Contains(r.ID)==true) action="";
            else if(pickups?.FirstOrDefault(p=>p.Temporary==r.Temp && string.Equals(p.Item,r.Name,StringComparison.OrdinalIgnoreCase)) is QuestPickup pickup)
                action="bot.Log("+Q("Quest step: pickup "+r.Name)+"); core.Join("+Q(pickup.Map)+"); Skua.Core.Scripts.QuestMapPickup."+(pickup.MapItemID>0?"AcquireKnown":"Acquire")+"(bot,"+questId+","+r.ID+","+Q(r.Name)+","+r.Quantity+","+r.Temp.ToString().ToLowerInvariant()+(pickup.MapItemID>0?","+pickup.MapItemID:"")+");";
            else if(drop!=null) action="bot.Log("+Q("Quest step: hunt "+r.Name)+"); core.Join("+Q(drop.Map)+"); Skua.Core.Scripts.QuestHunt.Monster(bot,"+questId+","+Q(drop.Monster)+","+r.ID+","+Q(r.Name)+","+r.Quantity+","+r.Temp.ToString().ToLowerInvariant()+");";
            else if(bankAvailable && !r.Temp && shopFor(r.Name,r.ID) is GearShop shop) {
                action="bot.Log("+Q("Quest step: shop "+r.Name)+"); core.Join("+Q(shop.Map)+"); bot.Shops.Load("+shop.ShopId+"); if(!bot.Shops.IsLoaded || bot.Shops.ID!="+shop.ShopId+") throw new InvalidOperationException(\"Required shop could not load.\");\n"+
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
        foreach(string step in steps) code.AppendLine(step);
        code.AppendLine("if(bot.ShouldExit) return; if(!bot.Quests.IsInProgress("+questId+")) throw new InvalidOperationException(\"Quest was abandoned.\"); if(!bot.Quests.CanCompleteFullCheck("+questId+")) throw new InvalidOperationException(\"Quest requirements changed or remain incomplete.\");\n"+ReturnCode(returnTo)+"bot.Log(\"Quest step: turn-in\"); if(!bot.Quests.EnsureComplete("+questId+","+rewardId+")) throw new InvalidOperationException(\"Quest turn-in failed.\"); bot.Log(\"Selected quest completed once.\");\n} finally {core.SetOptions(false);} } }");
        return code.ToString();
    }
}
