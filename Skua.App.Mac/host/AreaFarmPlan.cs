using Microsoft.CodeAnalysis.CSharp;
using Skua.Core.Models.Shops;
using System.Text;

namespace Skua.Mac;

public sealed record AreaFarmNode(int ID,string Name,string Kind,string Map="",string Monster="",int Shop=0,int ShopItem=0,int Cost=0,int Quest=0,int Reward=-1,bool Temporary=false,int Quantity=1,IReadOnlyList<AreaFarmNode>? Children=null);
public static class AreaFarmPlan
{
    public static string Recipe(ShopItem item)=>string.Join(";",item.Requirements.OrderBy(r=>r.ID).Select(r=>$"{r.ID}:{r.Quantity}"));
    public static string Generate(AreaFarmNode root) {
        string Q(string s)=>SymbolDisplay.FormatLiteral(s,true);
        var methods=new StringBuilder();int serial=0;bool needsBank=false;
        string Emit(AreaFarmNode n) {
            int number=serial++;string method="Acquire"+number;needsBank |= n.Kind=="shop";
            var children=(n.Children??[]).Select(c=>(Node:c,Method:Emit(c))).ToArray();
            string key=n.ID>0?n.ID.ToString():Q(n.Name), inv=n.Temporary?"bot.TempInv":"bot.Inventory";
            var body=new StringBuilder();
            body.AppendLine($"void {method}(int quantity) {{ Check(); if({inv}.Contains({key},quantity)) return;");
            if(!n.Temporary)body.AppendLine($"if(bot.Bank.Loaded && bot.Bank.Contains({key})) core.Unbank({key}); if({inv}.Contains({key},quantity)) return;");
            body.AppendLine($"bot.Log({Q("Acquiring "+n.Name)});");
            if(n.Kind=="owned")body.AppendLine("throw new InvalidOperationException(\"A material previously owned is no longer available. Refresh the plan.\");");
            else if(n.Kind=="drop")body.AppendLine($"core.AddDrop({Q(n.Name)}); core.HuntMonster({Q(n.Map)},{Q(n.Monster)},{Q(n.Name)},quantity,{n.Temporary.ToString().ToLowerInvariant()});");
            else if(n.Kind=="pickup")body.AppendLine($"core.Join({Q(n.Map)}); Skua.Core.Scripts.QuestMapPickup.Acquire(bot,{n.Quest},{n.ID},{Q(n.Name)},quantity,{n.Temporary.ToString().ToLowerInvariant()});");
            else if(n.Kind is "shop" or "quest") {
                body.AppendLine($"for(int attempt=0; !{inv}.Contains({key},quantity) && attempt<1000;attempt++) {{ Check();");
                if(n.Kind=="quest")body.AppendLine($"if(!bot.Quests.EnsureAccept({n.Quest})) throw new InvalidOperationException(\"Quest could not be accepted; check its prerequisites.\");");
                foreach(var child in children)body.AppendLine($"{child.Method}({child.Node.Quantity});");
                if(n.Kind=="shop") {
                    string recipe=string.Join(";",children.OrderBy(c=>c.Node.ID).Select(c=>$"{c.Node.ID}:{c.Node.Quantity}"));
                    body.AppendLine($"core.Join({Q(n.Map)}); bot.Shops.Load({n.Shop}); if(!bot.Shops.IsLoaded || bot.Shops.ID!={n.Shop}) throw new InvalidOperationException(\"Shop did not load.\"); var item=bot.Shops.Items.SingleOrDefault(i=>i.ID=={n.ID} && i.ShopItemID=={n.ShopItem}); if(item==null) throw new InvalidOperationException(\"Selected item is no longer in the shop.\");");
                    body.AppendLine($"if(item.Coins && item.Cost>0 || item.Cost!={n.Cost} || string.Join(\";\",item.Requirements.OrderBy(r=>r.ID).Select(r=>r.ID+\":\"+r.Quantity))!={Q(recipe)}) throw new InvalidOperationException(\"Shop price or recipe changed. Refresh the plan.\"); if(item.Upgrade && !bot.Player.IsMember) throw new InvalidOperationException(\"Membership required.\"); if(bot.Player.Gold<item.Cost) throw new InvalidOperationException(\"Not enough gold.\"); int before=bot.Inventory.GetQuantity({key}); bot.Shops.BuyItem(item.ID,item.ShopItemID,1); if(bot.Inventory.GetQuantity({key})<=before) throw new InvalidOperationException(\"Purchase did not succeed; check reputation, level and shop access.\");");
                } else body.AppendLine($"if(!bot.Quests.CanComplete({n.Quest}) || !bot.Quests.EnsureComplete({n.Quest},{n.Reward})) throw new InvalidOperationException(\"Material quest could not be completed.\");");
                body.AppendLine("}");
            } else throw new InvalidOperationException("Unsupported acquisition step.");
            body.AppendLine($"Check(); if(!{inv}.Contains({key},quantity)) throw new InvalidOperationException({Q("Item quantity not acquired: "+n.Name)}); }}");
            methods.Append(body);return method;
        }
        string start=Emit(root);
        return "// Generated for the selected area item and its verified material sources.\n//cs_include Scripts/CoreBots.cs\nusing System; using System.Linq; using Skua.Core.Interfaces;\npublic class GeneratedAreaFarm { public void ScriptMain(IScriptInterface bot) { var core=CoreBots.Instance; void Check(){ if(bot.ShouldExit) throw new OperationCanceledException(\"Farm stopped.\"); if(!bot.Player.LoggedIn) throw new InvalidOperationException(\"Log in again before farming.\"); }\n"+methods+
            (needsBank ? "core.SetOptions(); " : "")+"try { "+(needsBank?"if(!bot.Bank.Loaded) bot.Bank.Load(); if(!bot.Bank.Loaded) throw new InvalidOperationException(\"Bank could not be verified. Retry before consuming merge materials.\"); ":"")+"bot.Skills.StartAdvanced(bot.Player.CurrentClass?.Name ?? \"generic\",false); "+start+"("+root.Quantity+"); bot.Log(\"Selected item acquired.\"); } finally {core.SetOptions(false);} } }";
    }
}

// Reserve shared inventory once while planning multiple branches of a merge tree.
public sealed class AreaFarmStock
{
    readonly Dictionary<int,int> reserved=new();
    public int Missing(int id,int quantity,int owned) {
        if(id<=0)return quantity;
        int available=Math.Max(0,owned-reserved.GetValueOrDefault(id));
        reserved[id]=reserved.GetValueOrDefault(id)+Math.Min(quantity,available);
        return Math.Max(0,quantity-available);
    }
}
