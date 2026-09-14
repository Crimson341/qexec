using Skua.Core.Interfaces;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Skills;

namespace Skua.Core.Scripts;

/// <summary>Hunts one quest objective with adaptive class profiles and a no-progress stop.</summary>
public static class QuestHunt
{
    public static readonly TimeSpan DefaultStall = TimeSpan.FromMinutes(3);

    public static int Owned(int inventory, int bank, int temp, bool temporary) =>
        Math.Max(0, temporary ? temp : inventory + Math.Max(0, bank));

    public static bool Stalled(int have, int need, int lastHave, DateTime lastGain, DateTime now, TimeSpan timeout) =>
        have < need && have <= lastHave && now - lastGain >= timeout;

    public static bool HasMonster(IEnumerable<Monster> map, string name) =>
        map.Any(m => string.Equals((m.Name ?? "").Trim(), (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Join key: strip a trailing -roomNumber so battleon-100000 matches battleon. Empty is never a match.</summary>
    public static string MapKey(string? map)
    {
        if (string.IsNullOrWhiteSpace(map)) return "";
        string name = map.Trim();
        int dash = name.LastIndexOf('-');
        if (dash > 0 && dash < name.Length - 1 && name[(dash + 1)..].All(char.IsDigit))
            return name[..dash];
        return name;
    }

    public static bool SameMap(string? a, string? b)
    {
        string left = MapKey(a), right = MapKey(b);
        return left.Length > 0 && right.Length > 0 &&
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public static void JoinIfNeeded(IScriptInterface bot, string? map)
    {
        string dest = MapKey(map);
        if (dest.Length == 0) return;
        if (SameMap(bot.Map.Name, dest) && bot.Map.Loaded) return;
        bot.Map.Join(dest);
    }

    public static string HuntName(IEnumerable<Monster> map, string name)
    {
        var live = map.FirstOrDefault(m =>
            string.Equals((m.Name ?? "").Trim(), (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        return live?.Name is { Length: > 0 } n ? n : name;
    }

    public static string? HuntCell(IEnumerable<Monster> map, string name) =>
        map.FirstOrDefault(m => m.Alive &&
            string.Equals((m.Name ?? "").Trim(), (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase))?.Cell;

    public static void JumpIfNeeded(IScriptInterface bot, string monster)
    {
        string? cell = HuntCell(bot.Monsters.MapMonsters, monster);
        if (string.IsNullOrWhiteSpace(cell) ||
            string.Equals(bot.Player.Cell, cell, StringComparison.OrdinalIgnoreCase))
            return;
        bot.Map.Jump(cell, "Left");
    }

    public static (string Map, string Monster)? NextRoute(string map, string monster, IReadOnlyList<(string Map, string Monster)>? alternates, int tried)
    {
        if (alternates == null || tried < 0 || tried >= alternates.Count) return null;
        var next = alternates[tried];
        if (string.IsNullOrWhiteSpace(next.Map) || string.IsNullOrWhiteSpace(next.Monster)) return null;
        if (SameMap(next.Map, map) && string.Equals((next.Monster ?? "").Trim(), (monster ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            return tried + 1 < alternates.Count ? NextRoute(map, monster, alternates, tried + 1) : null;
        return next;
    }

    public static IEnumerable<AdvancedSkill> Profiles(IScriptInterface bot) =>
        bot.Skills is ScriptSkill skills ? skills.AdvancedSkillContainer.LoadedSkills : Array.Empty<AdvancedSkill>();

    public static string ApplySkills(IScriptInterface bot, IEnumerable<AdvancedSkill> profiles, bool defending, string? lastKey, out bool nowDefending)
    {
        string name = bot.Player.CurrentClass?.Name ?? "Unknown";
        int hp = bot.Player.Health, maximum = bot.Player.MaxHealth;
        nowDefending = CombatPolicy.NeedsDefense(hp, maximum, defending);
        var target = bot.Player.Target;
        if (target == null || !target.Alive)
            target = bot.Monsters.CurrentMonsters.FirstOrDefault(m => m.Alive && m.Cell == bot.Player.Cell);
        bool boss = target != null && CombatPolicy.IsBoss(target.MaxHP, maximum);
        var profile = CombatPolicy.Select(profiles, name, nowDefending, boss);
        string key = name + ":" + (profile?.ClassUseMode.ToString() ?? "Basic");
        if (key != lastKey)
        {
            bot.Skills.Stop();
            if (profile != null) bot.Skills.LoadAdvanced(name, false, profile.ClassUseMode);
            else bot.Skills.LoadAdvanced("0", 100, SkillUseMode.UseIfAvailable);
            bot.Skills.Start();
        }
        return key;
    }

    public static void Monster(IScriptInterface bot, int questId, string monster, int itemId, string itemName, int quantity, bool temporary, TimeSpan? stallTimeout = null, IReadOnlyList<(string Map, string Monster)>? alternates = null, string? map = null)
    {
        if (questId <= 0 || itemId <= 0 || quantity <= 0 || string.IsNullOrWhiteSpace(monster) || string.IsNullOrWhiteSpace(itemName))
            throw new ArgumentException("Invalid hunt objective.");
        TimeSpan timeout = stallTimeout ?? DefaultStall;
        int Have() => temporary ? bot.TempInv.GetQuantity(itemId) : bot.Inventory.GetQuantity(itemId);
        int have = Have();
        if (have >= quantity) return;
        int lastHave = have;
        var lastGain = DateTime.UtcNow;
        bool defending = false;
        string? lastKey = null;
        var profiles = Profiles(bot).ToList();
        string currentMap = MapKey(map);
        string currentMonster = monster;
        int tried = 0;
        JoinIfNeeded(bot, currentMap);
        currentMonster = HuntName(bot.Monsters.MapMonsters, currentMonster);
        bool sawMonster = HasMonster(bot.Monsters.MapMonsters, currentMonster);
        bot.Log("Hunting " + currentMonster + " for " + itemName + " (" + have + "/" + quantity + ").");
        JumpIfNeeded(bot, currentMonster);
        while (have < quantity)
        {
            if (bot.ShouldExit) return;
            if (!bot.Player.LoggedIn) throw new InvalidOperationException("Disconnected while hunting " + itemName + ".");
            if (!bot.Quests.IsInProgress(questId)) throw new InvalidOperationException("Quest was abandoned; stopping.");
            if (currentMap.Length > 0 && !SameMap(bot.Map.Name, currentMap))
                JoinIfNeeded(bot, currentMap);
            if (Stalled(have, quantity, lastHave, lastGain, DateTime.UtcNow, timeout))
            {
                var next = NextRoute(bot.Map.Name, currentMonster, alternates, tried);
                if (next == null)
                    throw new InvalidOperationException("No progress on " + itemName + " (" + have + "/" + quantity + ") after hunting " + currentMonster + ". Check the map and monster, then retry Auto-do.");
                tried++;
                bot.Log("Quest step: hunt " + itemName + " — trying documented alternate " + next.Value.Monster + " in /" + next.Value.Map);
                currentMap = MapKey(next.Value.Map);
                JoinIfNeeded(bot, currentMap);
                currentMonster = HuntName(bot.Monsters.MapMonsters, next.Value.Monster);
                lastGain = DateTime.UtcNow;
                sawMonster = HasMonster(bot.Monsters.MapMonsters, currentMonster);
                JumpIfNeeded(bot, currentMonster);
                continue;
            }
            lastKey = ApplySkills(bot, profiles, defending, lastKey, out defending);
            if (HasMonster(bot.Monsters.MapMonsters, currentMonster)) sawMonster = true;
            else if (!sawMonster && DateTime.UtcNow - lastGain >= TimeSpan.FromSeconds(30))
            {
                var next = NextRoute(bot.Map.Name, currentMonster, alternates, tried);
                if (next == null)
                    throw new InvalidOperationException(currentMonster + " was not found on this map. Stopped without turning in.");
                tried++;
                bot.Log("Quest step: hunt " + itemName + " — " + currentMonster + " missing; trying /" + next.Value.Map);
                currentMap = MapKey(next.Value.Map);
                JoinIfNeeded(bot, currentMap);
                currentMonster = HuntName(bot.Monsters.MapMonsters, next.Value.Monster);
                lastGain = DateTime.UtcNow;
                sawMonster = HasMonster(bot.Monsters.MapMonsters, currentMonster);
                JumpIfNeeded(bot, currentMonster);
                continue;
            }
            JumpIfNeeded(bot, currentMonster);
            using var slice = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { bot.Hunt.Monster(HuntName(bot.Monsters.MapMonsters, currentMonster), slice.Token); }
            catch (OperationCanceledException) { }
            have = Have();
            if (have > lastHave) { lastHave = have; lastGain = DateTime.UtcNow; bot.Log(itemName + " " + have + "/" + quantity); }
        }
    }
}
