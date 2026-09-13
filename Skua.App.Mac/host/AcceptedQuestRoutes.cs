using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Skua.Core.Models.Items;

namespace Skua.Mac;

public static class AcceptedQuestRoutes
{
    // Verified against Aranx's quest page, Underworld Hound (1), and
    // Story/QueenofMonsters/CoreQOM.cs (pickup 3694), 2026-09-13.
    public static QuestResolution Verified(int questId,IEnumerable<ItemBase> requirements) {
        var drops=new List<GearDrop>();var pickups=new List<QuestPickup>();
        if(questId!=4500)return new(drops,pickups);
        const string evidence="https://aqwwiki.wikidot.com/aranx-s-quests";
        foreach(var item in requirements) {
            if(!item.Temp)continue;
            if(item.ID==30997 && item.Name=="Plant Found" && item.Quantity==3)
                pickups.Add(new("lostruins",item.Name,true,evidence,3694));
            if(item.ID==30998 && item.Name=="Underworld Hound Defeated" && item.Quantity==5)
                drops.Add(new("lostruins","Underworld Hound",item.Name,true,evidence+" -> https://aqwwiki.wikidot.com/underworld-hound-1"));
        }
        return new(drops,pickups);
    }
    // Only literal, exact quest IDs and unambiguous map/monster pairs are usable.
    public static List<GearDrop> Find(string root, int questId, IEnumerable<ItemBase> requirements)
    {
        var routes = new List<GearDrop>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "Generated-") || new FileInfo(file).Length > 2_000_000) continue;
            string source = File.ReadAllText(file);
            if (!source.Contains(questId.ToString()) || !source.Contains("KillQuest")) continue;
            routes.AddRange(Parse(source, questId, requirements, Path.GetRelativePath(root, file)));
        }
        return Unambiguous(routes);
    }

    public static List<GearDrop> Parse(string source, int questId, IEnumerable<ItemBase> requirements, string evidence)
    {
        var routes = new List<GearDrop>();
        var syntax=CSharpSyntaxTree.ParseText(source).GetRoot();
        if(syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(c=>c.Expression.ToString()=="Story.MapItemQuest" && c.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax q && q.Token.Value is int id && id==questId))return routes;
        foreach (var call in CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax member || member.Expression.ToString() != "Story" || member.Name.Identifier.ValueText != "KillQuest") continue;
            var args = call.ArgumentList.Arguments;
            if (args.Count < 3 || args.Take(3).Any(a => a.NameColon != null)) continue;
            if (args[0].Expression is not LiteralExpressionSyntax id || id.Token.Value is not int value || value != questId) continue;
            if (args[1].Expression is not LiteralExpressionSyntax map || map.Token.Value is not string mapName || string.IsNullOrWhiteSpace(mapName)) continue;
            if (args[2].Expression is not LiteralExpressionSyntax monster || monster.Token.Value is not string monsterName || string.IsNullOrWhiteSpace(monsterName)) continue;
            routes.AddRange(requirements.Where(r=>r.Temp).Select(r => new GearDrop(mapName, monsterName, r.Name, r.Temp, evidence)));
        }
        return routes;
    }

    public static List<QuestPickup> FindPickups(string root,int questId,IReadOnlyList<ItemBase> requirements) {
        var routes=new List<QuestPickup>();
        foreach(var file in Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)) {
            if(file.Contains(Path.DirectorySeparatorChar+"Generated-") || new FileInfo(file).Length>2_000_000)continue;
            string source=File.ReadAllText(file);
            if(!source.Contains("MapItemQuest") || !source.Contains(questId.ToString()))continue;
            routes.AddRange(ParsePickups(source,questId,requirements,Path.GetRelativePath(root,file)));
        }
        return routes.Select(r=>(r.Map,r.MapItemID)).Distinct().Count()==1 ? routes.Take(1).ToList() : [];
    }
    public static List<QuestPickup> ParsePickups(string source,int questId,IReadOnlyList<ItemBase> requirements,string evidence) {
        // An unnamed pickup can only be assigned when the entire quest has one temporary objective.
        if(requirements.Count!=1 || !requirements[0].Temp)return [];
        var req=requirements[0];var result=new List<QuestPickup>();
        foreach(var call in CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if(call.Expression is not MemberAccessExpressionSyntax member || member.Expression.ToString()!="Story" || member.Name.Identifier.ValueText!="MapItemQuest")continue;
            var args=call.ArgumentList.Arguments;
            if(args.Count<3 || args.Take(4).Any(a=>a.NameColon!=null))continue;
            if(args[0].Expression is not LiteralExpressionSyntax id || id.Token.Value is not int quest || quest!=questId)continue;
            if(args[1].Expression is not LiteralExpressionSyntax map || map.Token.Value is not string name || !System.Text.RegularExpressions.Regex.IsMatch(name,@"^[a-zA-Z0-9_]+$"))continue;
            if(args[2].Expression is not LiteralExpressionSyntax pickup || pickup.Token.Value is not int pickupId || pickupId<=0)continue;
            int amount=1;
            if(args.Count>3) {if(args[3].Expression is not LiteralExpressionSyntax count || count.Token.Value is not int value)continue;amount=value;}
            if(amount!=req.Quantity)continue;
            result.Add(new(name,req.Name,true,evidence,pickupId));
        }
        return result;
    }

    private static List<GearDrop> Unambiguous(List<GearDrop> routes) => routes
        .GroupBy(r => (r.Item, r.Temporary))
        .Where(g => g.Select(r => (r.Map, r.Monster)).Distinct().Count() == 1)
        .Select(g => g.First()).ToList();
}
