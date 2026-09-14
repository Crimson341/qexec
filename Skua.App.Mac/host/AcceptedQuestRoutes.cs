using Microsoft.CodeAnalysis;
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
        var all=requirements.ToList();
        var routes = new List<GearDrop>();
        foreach (var script in ScriptEvidence.Scan(root))
            routes.AddRange(FromStory(script, questId, all).Drops);
        return Unambiguous(routes);
    }

    public static List<GearDrop> Parse(string source, int questId, IEnumerable<ItemBase> requirements, string evidence)
        => ParseMixed(source,questId,requirements.ToList(),evidence).Drops.ToList();

    public static List<QuestPickup> FindPickups(string root,int questId,IReadOnlyList<ItemBase> requirements) {
        var routes=new List<QuestPickup>();
        foreach(var script in ScriptEvidence.Scan(root))
            routes.AddRange(FromStory(script,questId,requirements).Pickups);
        return routes.GroupBy(r=>r.Item,StringComparer.OrdinalIgnoreCase)
            .Where(g=>g.Select(r=>(r.Map,r.MapItemID)).Distinct().Count()==1)
            .Select(g=>g.First()).ToList();
    }

    internal static QuestResolution FromStory(ParsedScript script,int questId,IReadOnlyList<ItemBase> requirements)
    {
        var temps=requirements.Where(r=>r.Temp && r.ID>0 && !string.IsNullOrWhiteSpace(r.Name) && r.Quantity>0).ToList();
        var assigned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pickups=new List<QuestPickup>();
        foreach(var call in script.MapItems.Where(c=>c.QuestId==questId)) {
            var matches=temps.Where(r=>r.Quantity==call.Amount && !assigned.Contains(r.Name)).ToList();
            if(matches.Count!=1)continue;
            assigned.Add(matches[0].Name);
            pickups.Add(new(call.Map,matches[0].Name,true,call.Evidence,call.MapItemId));
        }
        var remaining=temps.Where(r=>!assigned.Contains(r.Name)).ToList();
        var drops=new List<GearDrop>();
        foreach(var call in script.KillQuests.Where(c=>c.QuestId==questId))
            drops.AddRange(remaining.Select(r=>new GearDrop(call.Map,call.Monster,r.Name,r.Temp,call.Evidence)));
        return new(drops,pickups);
    }
    public static List<QuestPickup> ParsePickups(string source,int questId,IReadOnlyList<ItemBase> requirements,string evidence)
        => ParseMixed(source,questId,requirements,evidence).Pickups.ToList();

    // MapItemQuest amount matches the unique unassigned temp with that quantity.
    // KillQuest then receives only leftover temps — never a pickup already claimed.
    public static QuestResolution ParseMixed(string source,int questId,IReadOnlyList<ItemBase> requirements,string evidence)
    {
        var syntax=CSharpSyntaxTree.ParseText(source).GetRoot();
        var temps=requirements.Where(r=>r.Temp && r.ID>0 && !string.IsNullOrWhiteSpace(r.Name) && r.Quantity>0).ToList();
        var assigned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pickups=new List<QuestPickup>();
        foreach(var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if(!StoryCall(call,"MapItemQuest",out var args))continue;
            if(args.Count<3 || args.Take(4).Any(a=>a.NameColon!=null))continue;
            if(args[0].Expression is not LiteralExpressionSyntax id || id.Token.Value is not int quest || quest!=questId)continue;
            if(args[1].Expression is not LiteralExpressionSyntax map || map.Token.Value is not string name || !System.Text.RegularExpressions.Regex.IsMatch(name,@"^[a-zA-Z0-9_]+$"))continue;
            if(args[2].Expression is not LiteralExpressionSyntax pickup || pickup.Token.Value is not int pickupId || pickupId<=0)continue;
            int amount=1;
            if(args.Count>3) {if(args[3].Expression is not LiteralExpressionSyntax count || count.Token.Value is not int value)continue;amount=value;}
            var matches=temps.Where(r=>r.Quantity==amount && !assigned.Contains(r.Name)).ToList();
            if(matches.Count!=1)continue;
            assigned.Add(matches[0].Name);
            pickups.Add(new(name,matches[0].Name,true,evidence,pickupId));
        }
        var remaining=temps.Where(r=>!assigned.Contains(r.Name)).ToList();
        var drops=new List<GearDrop>();
        foreach(var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if(!StoryCall(call,"KillQuest",out var args))continue;
            if(args.Count<3 || args.Take(3).Any(a=>a.NameColon!=null))continue;
            if(args[0].Expression is not LiteralExpressionSyntax id || id.Token.Value is not int value || value!=questId)continue;
            if(args[1].Expression is not LiteralExpressionSyntax map || map.Token.Value is not string mapName || string.IsNullOrWhiteSpace(mapName))continue;
            if(args[2].Expression is not LiteralExpressionSyntax monster || monster.Token.Value is not string monsterName || string.IsNullOrWhiteSpace(monsterName))continue;
            drops.AddRange(remaining.Select(r=>new GearDrop(mapName,monsterName,r.Name,r.Temp,evidence)));
        }
        return new(drops,pickups);
    }

    static bool StoryCall(InvocationExpressionSyntax call,string name,out SeparatedSyntaxList<ArgumentSyntax> args)
    {
        args=default;
        if(call.Expression is not MemberAccessExpressionSyntax member || member.Expression.ToString()!="Story" || member.Name.Identifier.ValueText!=name)return false;
        args=call.ArgumentList.Arguments;return true;
    }

    // Keep every documented map/monster pair. Ranking picks the fastest; extras become hunt alternates.
    private static List<GearDrop> Unambiguous(List<GearDrop> routes) => routes
        .GroupBy(r => string.Join('\n', (r.Item ?? "").ToLowerInvariant(), r.Temporary, (r.Map ?? "").ToLowerInvariant(), (r.Monster ?? "").ToLowerInvariant()))
        .Select(g => g.First()).ToList();
}
