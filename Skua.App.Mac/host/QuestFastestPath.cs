using Skua.Core.Models.Items;

namespace Skua.Mac;

/// <summary>Ranks documented wiki/guide routes. Never invents a map or monster.</summary>
public static class QuestFastestPath
{
    public static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool IsFarmable(bool once, bool dailyDone, bool locked = false) =>
        !once && !dailyDone && !locked;

    public static bool IsWiki(string? evidence) =>
        !string.IsNullOrWhiteSpace(evidence) && evidence.Contains("aqwwiki.wikidot.com", StringComparison.OrdinalIgnoreCase);

    public static bool IsGuide(string? evidence) =>
        !string.IsNullOrWhiteSpace(evidence) &&
        (evidence.Contains(".cs:", StringComparison.OrdinalIgnoreCase) ||
         evidence.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
         evidence.Contains("/Story/", StringComparison.OrdinalIgnoreCase) ||
         evidence.Contains("Story/", StringComparison.OrdinalIgnoreCase));

    public static int Score(string map, string? currentMap, IEnumerable<string>? preferredMaps, string? evidence)
    {
        int score = 0;
        if (Same(map, currentMap)) score += 100;
        if (preferredMaps != null)
            foreach (string preferred in preferredMaps)
                if (Same(preferred, map)) { score += 40; break; }
        if (IsWiki(evidence)) score += 25;
        if (IsGuide(evidence)) score += 20;
        return score;
    }

    public static GearDrop? PickDrop(IReadOnlyList<GearDrop> candidates, string? currentMap, IEnumerable<string>? preferredMaps = null)
    {
        if (candidates == null || candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];
        return candidates
            .OrderByDescending(c => Score(c.Map, currentMap, preferredMaps, c.Evidence))
            .ThenBy(c => c.Map, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Monster, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    public static QuestPickup? PickPickup(IReadOnlyList<QuestPickup> candidates, string? currentMap, IEnumerable<string>? preferredMaps = null)
    {
        if (candidates == null || candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];
        return candidates
            .OrderByDescending(c => Score(c.Map, currentMap, preferredMaps, c.Evidence))
            .ThenBy(c => c.Map, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    public static IReadOnlyList<GearDrop> SelectDrops(IEnumerable<GearDrop> all, string? currentMap, IEnumerable<string>? preferredMaps = null)
    {
        var preferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(currentMap)) preferred.Add(currentMap);
        if (preferredMaps != null)
            foreach (string map in preferredMaps)
                if (!string.IsNullOrWhiteSpace(map)) preferred.Add(map);
        var selected = new List<GearDrop>();
        foreach (var group in all.GroupBy(d => (d.Item ?? "").Trim().ToLowerInvariant() + "\n" + d.Temporary))
        {
            var distinct = group.GroupBy(d => (d.Map ?? "").Trim().ToLowerInvariant() + "\n" + (d.Monster ?? "").Trim().ToLowerInvariant()).Select(g => g.First()).ToList();
            var pick = PickDrop(distinct, currentMap, preferred);
            if (pick == null) continue;
            var alternates = distinct.Where(d => !Same(d.Map, pick.Map) || !Same(d.Monster, pick.Monster)).ToList();
            selected.Add(pick with { Alternates = alternates.Count > 0 ? alternates : null });
            preferred.Add(pick.Map);
        }
        return selected;
    }

    public static IReadOnlyList<QuestPickup> SelectPickups(IEnumerable<QuestPickup> all, string? currentMap, IEnumerable<string>? preferredMaps = null)
    {
        var preferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(currentMap)) preferred.Add(currentMap);
        if (preferredMaps != null)
            foreach (string map in preferredMaps)
                if (!string.IsNullOrWhiteSpace(map)) preferred.Add(map);
        var selected = new List<QuestPickup>();
        foreach (var group in all.GroupBy(p => (p.Item ?? "").Trim().ToLowerInvariant() + "\n" + p.Temporary))
        {
            var distinct = group.GroupBy(p => (p.Map ?? "").Trim().ToLowerInvariant() + "\n" + p.MapItemID).Select(g => g.First()).ToList();
            var pick = PickPickup(distinct, currentMap, preferred);
            if (pick == null) continue;
            selected.Add(pick);
            preferred.Add(pick.Map);
        }
        return selected;
    }

    public static IReadOnlyList<ItemBase> OrderSteps(IEnumerable<ItemBase> requirements, Func<ItemBase, string?> mapOf, string? currentMap)
    {
        return requirements
            .Select((item, index) => (item, index, map: mapOf(item) ?? ""))
            .OrderBy(step => Same(step.map, currentMap) ? 0 : 1)
            .ThenBy(step => step.map, StringComparer.OrdinalIgnoreCase)
            .ThenBy(step => step.index)
            .Select(step => step.item)
            .ToList();
    }
}

public static class ActiveQuestRun
{
    public static int QuestId { get; private set; }
    public static int RewardId { get; private set; }
    public static bool Farmable { get; private set; }
    public static string Name { get; private set; } = "";
    public static bool Completed { get; private set; }

    public static void Watch(int questId, int rewardId, bool farmable, string name)
    {
        QuestId = questId;
        RewardId = rewardId;
        Farmable = farmable;
        Name = name ?? "";
        Completed = false;
    }

    public static void Clear()
    {
        QuestId = 0;
        RewardId = 0;
        Farmable = false;
        Name = "";
        Completed = false;
    }

    public static void NoteLog(string message)
    {
        if (!string.IsNullOrWhiteSpace(message) &&
            message.StartsWith("Selected quest completed", StringComparison.Ordinal))
            Completed = true;
    }

    public static object Finished() => new
    {
        type = "active-quest-finished",
        id = QuestId,
        reward = RewardId,
        farmable = Farmable,
        name = Name,
        message = Farmable
            ? Name + " is a farming quest. Do the same route again?"
            : Name + " completed."
    };
}
