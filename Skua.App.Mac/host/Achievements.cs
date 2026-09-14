using Newtonsoft.Json.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;

namespace Skua.Mac;

public record AchievementDef(string Id, string Title, string Detail, string Image, string[] ItemNames, int[] ItemIds, int[] StoryQuests);
public record AchievementAward(string Id, long EarnedAt, string Reason);
public record AchievementState(string Id, string Title, string Detail, string Image, bool Earned, long? EarnedAt, string Reason, string Location);

public sealed class Achievements(IScriptInterface bot, GearOwnership ownership, string storePath)
{
    // Planner goals plus Become OP classes and the Blade of Awe farm. Item IDs and
    // story quests come from QuestData / the LoO fixture this host already ships.
    public static readonly AchievementDef[] Catalog = [
        new("vhl", "Void Highlord", "Solo class from the Nation grind. Awarded when the class is in inventory or bank.", "vhl.png", ["Void Highlord", "Void Highlord (IoDA)"], [38259], []),
        new("order", "Lord of Order", "Support class. Awarded when the class is owned or The Final Challenge (7165) is complete.", "order.png", ["Lord of Order", "Lord Of Order"], [50741], [7165]),
        new("paladin", "ArchPaladin", "Survival class from the Good story line. Awarded when the class is owned.", "paladin.png", ["ArchPaladin"], [], []),
        new("revenant", "Legion Revenant", "Farming class. Awarded when owned or Legion Fealty 4 (6900) is complete.", "revenant.png", ["Legion Revenant"], [48571], [6900]),
        new("dragon", "Dragon of Time", "Solo class from the Dragon of Time quest chain. Awarded when the class is owned.", "dragon.png", ["Dragon of Time"], [56722], []),
        new("nsod", "Necrotic Sword of Doom", "Endgame weapon. Awarded when the sword is in inventory or bank.", "nsod.png", ["Necrotic Sword of Doom"], [30629], []),
        new("awe", "Cape of Awe", "Progression cape from Gear of Awe. Awarded when the cape is owned.", "awe.png", ["Cape of Awe"], [], []),
        new("blade-awe", "Blade of Awe", "Classic farm weapon. Awarded when the blade is owned.", "blade-awe.png", ["Blade of Awe"], [17585], []),
        new("chaos-avenger", "Chaos Avenger", "Chaos-story class. Awarded when owned or Chaos Avenger Class (8301) is complete.", "chaos-avenger.png", ["Chaos Avenger"], [], [8301]),
        new("archmage", "ArchMage", "Caster class. Awarded when owned or ArchMage's Ascension (8918) is complete.", "archmage.png", ["ArchMage"], [73287], [8918]),
        new("lightcaster", "LightCaster", "Light-path caster. Awarded when owned or LightCaster Class (6495) is complete.", "lightcaster.png", ["LightCaster"], [38153], [6495]),
        new("scarlet", "Scarlet Sorceress", "Upgrade from Blood Sorceress. Awarded when owned or quest 6236 is complete.", "scarlet.png", ["Scarlet Sorceress"], [42992], [6236])
    ];

    public static string StoreFile(string root) => Path.Combine(root, "achievements.json");

    public static string CharacterKey(string username)
    {
        var trimmed = username.Trim();
        if (trimmed.Length == 0 || trimmed.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new ArgumentException("Character name is missing or invalid.");
        return trimmed.ToLowerInvariant();
    }

    public static string Locate(AchievementDef def, IEnumerable<InventoryItem> inventory, IEnumerable<InventoryItem> bank, bool bankLoaded)
    {
        string fallback = "Missing";
        foreach (var id in def.ItemIds)
        {
            var byId = GearOwnership.Locate(id, "", inventory, bank, bankLoaded);
            if (byId is "Inventory" or "Bank") return byId;
            if (byId == "Unknown") fallback = "Unknown";
        }
        foreach (var name in def.ItemNames)
        {
            var byName = GearOwnership.Locate(0, name, inventory, bank, bankLoaded);
            if (byName is "Inventory" or "Bank") return byName;
            if (byName == "Unknown") fallback = "Unknown";
        }
        return fallback;
    }

    public static List<AchievementState> Evaluate(
        IEnumerable<InventoryItem> inventory,
        IEnumerable<InventoryItem> bank,
        bool bankLoaded,
        Func<int, bool> storyComplete,
        IReadOnlyDictionary<string, AchievementAward> persisted)
    {
        var inv = inventory.ToList();
        var stored = bank.ToList();
        return Catalog.Select(def => {
            persisted.TryGetValue(def.Id, out var prior);
            string location = Locate(def, inv, stored, bankLoaded);
            bool story = def.StoryQuests.Any(storyComplete);
            string reason;
            if (prior != null)
            {
                reason = prior.Reason;
                return new AchievementState(def.Id, def.Title, def.Detail, def.Image, true, prior.EarnedAt, reason, location is "Inventory" or "Bank" ? location : "Saved");
            }
            if (location is "Inventory" or "Bank")
            {
                reason = location;
                return new AchievementState(def.Id, def.Title, def.Detail, def.Image, true, null, reason, location);
            }
            if (story)
            {
                reason = "Story";
                return new AchievementState(def.Id, def.Title, def.Detail, def.Image, true, null, reason, "Story");
            }
            reason = location == "Unknown" ? "Bank check needed" : "Not earned yet";
            return new AchievementState(def.Id, def.Title, def.Detail, def.Image, false, null, reason, location);
        }).ToList();
    }

    public static Dictionary<string, AchievementAward> MergeAwards(
        IReadOnlyDictionary<string, AchievementAward> persisted,
        IEnumerable<AchievementState> current,
        long now)
    {
        var next = persisted.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (var state in current.Where(s => s.Earned))
        {
            if (next.ContainsKey(state.Id)) continue;
            next[state.Id] = new AchievementAward(state.Id, state.EarnedAt ?? now, state.Reason);
        }
        return next;
    }

    public static JObject LoadStore(string path)
    {
        if (!File.Exists(path)) return new JObject { ["characters"] = new JObject() };
        try
        {
            var parsed = JObject.Parse(File.ReadAllText(path));
            if (parsed["characters"] is not JObject) parsed["characters"] = new JObject();
            return parsed;
        }
        catch (Exception)
        {
            return new JObject { ["characters"] = new JObject() };
        }
    }

    public static Dictionary<string, AchievementAward> ReadAwards(JObject store, string character)
    {
        var awards = new Dictionary<string, AchievementAward>(StringComparer.Ordinal);
        if (store["characters"] is not JObject characters) return awards;
        if (characters[character] is not JObject row || row["awards"] is not JObject bag) return awards;
        foreach (var property in bag.Properties())
        {
            if (property.Value is not JObject award) continue;
            long earnedAt = (long?)award["earnedAt"] ?? 0;
            string reason = (string?)award["reason"] ?? "Saved";
            if (earnedAt <= 0 || string.IsNullOrWhiteSpace(property.Name)) continue;
            awards[property.Name] = new AchievementAward(property.Name, earnedAt, reason);
        }
        return awards;
    }

    public static void WriteAwards(JObject store, string character, IReadOnlyDictionary<string, AchievementAward> awards)
    {
        var characters = (JObject)store["characters"]!;
        var bag = new JObject();
        foreach (var award in awards.Values.OrderBy(a => a.Id))
            bag[award.Id] = new JObject { ["earnedAt"] = award.EarnedAt, ["reason"] = award.Reason };
        characters[character] = new JObject { ["awards"] = bag };
    }

    public static void SaveStore(string path, JObject store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        string temp = path + ".tmp";
        File.WriteAllText(temp, store.ToString());
        File.Move(temp, path, true);
    }

    public static object Payload(string character, bool bankLoaded, string note, IReadOnlyList<AchievementState> items, IReadOnlyCollection<string> newlyEarned)
    {
        return new {
            type = "achievements",
            character,
            bankLoaded,
            note,
            earned = items.Count(i => i.Earned),
            total = items.Count,
            newlyEarned = newlyEarned.ToArray(),
            items
        };
    }

    public bool StoryComplete(int questId)
    {
        try { return bot.Quests.HasBeenCompleted(questId); }
        catch (Exception) { return false; }
    }

    public async Task<object> Scan()
    {
        if (!bot.Player.LoggedIn)
            return Payload("", false, "Log in to check inventory and story progress.", Evaluate([], [], true, _ => false, new Dictionary<string, AchievementAward>()), []);
        string character;
        try { character = CharacterKey(bot.Player.Username); }
        catch (ArgumentException)
        {
            return Payload("", false, "Character name unavailable. Log in again, then Recheck.", Evaluate([], [], true, _ => false, new Dictionary<string, AchievementAward>()), []);
        }
        bool loaded = await ownership.LoadBank();
        List<InventoryItem> inventory;
        List<InventoryItem> bank;
        try { inventory = ownership.ReadInventory(); }
        catch (Exception ex) { return new { type = "achievements-error", message = "Inventory read failed: " + ex.GetBaseException().Message }; }
        try { bank = loaded ? ownership.ReadBank() : []; }
        catch (Exception)
        {
            loaded = false;
            bank = [];
        }
        if (Catalog.SelectMany(d => d.StoryQuests).Distinct().ToArray() is { Length: > 0 } quests)
        {
            try { bot.Quests.Load(quests); }
            catch (Exception) { /* Story checks then report incomplete; inventory awards still apply. */ }
        }
        var store = LoadStore(storePath);
        var persisted = ReadAwards(store, character);
        var evaluated = Evaluate(inventory, bank, loaded, StoryComplete, persisted);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var merged = MergeAwards(persisted, evaluated, now);
        var newly = merged.Keys.Where(id => !persisted.ContainsKey(id)).ToArray();
        if (newly.Length > 0)
        {
            WriteAwards(store, character, merged);
            SaveStore(storePath, store);
        }
        var stamped = Evaluate(inventory, bank, loaded, StoryComplete, merged);
        string note = loaded
            ? "Inventory, bank, and story progress checked."
            : "Inventory checked; bank unavailable. Story quests were still read. Missing-item awards wait for a bank check.";
        return Payload(bot.Player.Username.Trim(), loaded, note, stamped, newly);
    }
}
