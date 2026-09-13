using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Skua.Core.Models.Items;

namespace Skua.Mac;

public static class AcceptedQuestRoutes
{
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

    private static List<GearDrop> Unambiguous(List<GearDrop> routes) => routes
        .GroupBy(r => (r.Item, r.Temporary))
        .Where(g => g.Select(r => (r.Map, r.Monster)).Distinct().Count() == 1)
        .Select(g => g.First()).ToList();
}
