using Newtonsoft.Json.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.Models.Quests;

namespace Skua.Mac;

public record AchievementDef(string Id, string Title, string Detail, string Image, string[] ItemNames, int[] ItemIds, int[] StoryQuests, string Script);
public record AchievementAward(string Id, long EarnedAt, string Reason);
public record AchievementState(string Id, string Title, string Detail, string Image, bool Earned, long? EarnedAt, string Reason, string Location, string Script, bool CanRun);

public sealed class Achievements(IScriptInterface bot, GearOwnership ownership, string storePath, string scriptsRoot)
{
    // Planner goals, Become OP / official class bots, and Gear of Awe / BLoD / SDKA farms.
    // Item IDs and story quests come from QuestData. Script paths are official BrenoHenrike bots
    // this host can already launch from Quests / gear / generated catalog farms.
    public static readonly AchievementDef[] Catalog = [
        Entry("vhl", "Void Highlord", "Solo class from the Nation grind. Awarded when the class is in inventory or bank.", ["Void Highlord", "Void Highlord (IoDA)"], [38259], [], "Nation/VHL/0VoidHighlord.cs"),
        Entry("order", "Lord of Order", "Support class. Awarded when the class is owned or The Final Challenge (7165) is complete.", ["Lord of Order", "Lord Of Order"], [50741], [7165], "Dailies/LordOfOrder.cs"),
        Entry("paladin", "ArchPaladin", "Survival class from the Good story line. Awarded when the class is owned.", ["ArchPaladin"], [], [], "Good/ArchPaladin.cs"),
        Entry("revenant", "Legion Revenant", "Farming class. Awarded when owned or Legion Fealty 4 (6900) is complete.", ["Legion Revenant"], [48571], [6900], "Legion/Revenant/0LegionRevenant.cs"),
        Entry("dragon", "Dragon of Time", "Solo class from the Dragon of Time quest chain. Awarded when the class is owned.", ["Dragon of Time"], [56722], [], "Other/Classes/DragonOfTime.cs"),
        Entry("nsod", "Necrotic Sword of Doom", "Endgame weapon. Awarded when the sword is in inventory or bank.", ["Necrotic Sword of Doom"], [30629], [], "Evil/NSoD/0NecroticSwordOfDoom.cs"),
        Entry("awe", "Cape of Awe", "Progression cape from Gear of Awe. Awarded when the cape is owned.", ["Cape of Awe"], [], [], "Good/GearOfAwe/CapeOfAwe.cs"),
        Entry("blade-awe", "Blade of Awe", "Classic farm weapon. Awarded when the blade is owned.", ["Blade of Awe"], [17585], [], "Good/GearOfAwe/BladeOfAwe.cs"),
        Entry("chaos-avenger", "Chaos Avenger", "Chaos-story class. Awarded when owned or Chaos Avenger Class (8301) is complete.", ["Chaos Avenger"], [], [8301], ""),
        Entry("archmage", "ArchMage", "Caster class. Awarded when owned or ArchMage's Ascension (8918) is complete.", ["ArchMage"], [73287], [8918], "Other/Classes/ArchMage/0ArchMage.cs"),
        Entry("lightcaster", "LightCaster", "Light-path caster. Awarded when owned or LightCaster Class (6495) is complete.", ["LightCaster"], [38153], [6495], "Other/Classes/LightCaster.cs"),
        Entry("scarlet", "Scarlet Sorceress", "Upgrade from Blood Sorceress. Awarded when owned or quest 6236 is complete.", ["Scarlet Sorceress"], [42992], [6236], "Other/Classes/ScarletSorceress.cs"),
        Entry("blood-sorceress", "Blood Sorceress", "Starter caster that upgrades into Scarlet Sorceress. Awarded when the class is owned.", ["Blood Sorceress"], [36298], [], "Other/Classes/BloodSorceress.cs"),
        Entry("lightmage", "LightMage", "Light-path prerequisite for LightCaster. Awarded when the class is owned.", ["LightMage"], [44838], [], "Other/Classes/LightMage.cs"),
        Entry("dragon-shinobi", "Dragon Shinobi", "Yokai ninja class. Awarded when the class is owned.", ["Dragon Shinobi"], [], [], "Other/Classes/DragonShinobi.cs"),
        Entry("dragonslayer", "Dragonslayer", "Early dragon-hunt class. Awarded when the class is owned.", ["Dragonslayer"], [], [], "Other/Classes/Dragonslayer.cs"),
        Entry("dsg", "Dragonslayer General", "Upgraded dragon-hunt class. Awarded when the class is owned.", ["Dragonslayer General"], [], [], "Other/Classes/DragonslayerGeneral.cs"),
        Entry("frost-spirit", "Frost Spirit Reaver", "Ice-path reaver class. Awarded when the class is owned.", ["Frost Spirit Reaver"], [], [], "Other/Classes/FrostSpiritReaver.cs"),
        Entry("kings-echo", "King's Echo", "Late-game caster class. Awarded when the class is owned.", ["King's Echo"], [], [], "Other/Classes/KingsEcho.cs"),
        Entry("lich", "Lich", "Undead caster. Awarded when owned or Lich Class (10339) is complete.", ["Lich"], [94824], [10339], "Other/Classes/Lich.cs"),
        Entry("martial-artist", "Martial Artist", "Unarmed combat class. Awarded when the class is owned.", ["Martial Artist"], [], [], "Other/Classes/MartialArtist.cs"),
        Entry("necromancer", "Necromancer", "Undead summoner class. Awarded when the class is owned.", ["Necromancer"], [], [], "Other/Classes/Necromancer.cs"),
        Entry("proto", "Proto Sartorium", "Tutorial-era class farm. Awarded when the class is owned.", ["Proto Sartorium"], [], [], "Other/Classes/ProtoSartorium.cs"),
        Entry("sentinel", "Sentinel", "Guardian class. Awarded when the class is owned.", ["Sentinel"], [], [], "Other/Classes/Sentinel.cs"),
        Entry("storms", "Sovereign of Storms", "Storm-path class. Awarded when the class is owned.", ["Sovereign of Storms"], [], [], "Other/Classes/SovereignOfStorms.cs"),
        Entry("vdk", "Verus DoomKnight", "DoomKnight upgrade. Awarded when the class is owned.", ["Verus DoomKnight"], [], [], "Other/Classes/VerusDoomKnight.cs"),
        Entry("arcana", "Arcana Invoker", "Invoker class. Uses the non-insignia official farm. Awarded when the class is owned.", ["Arcana Invoker"], [], [], "Other/Classes/ArcanaInvoker[Non-Insignia].cs"),
        Entry("arachnomancer", "Arachnomancer", "Reputation class. Awarded when the class is owned.", ["Arachnomancer"], [], [], "Other/Classes/REP-based/Arachnomancer.cs"),
        Entry("bard", "Bard", "Support reputation class. Awarded when the class is owned.", ["Bard"], [4945], [], "Other/Classes/REP-based/Bard.cs"),
        Entry("chaos-slayer", "Chaos Slayer", "Chaos reputation class. Awarded when owned or Slayer of Chaos? (2277) is complete.", ["Chaos Slayer", "ChaosSlayer"], [13487], [2277], "Other/Classes/REP-based/ChaosSlayer.cs"),
        Entry("deathknight", "DeathKnight", "Undead reputation class. Awarded when the class is owned.", ["DeathKnight"], [], [], "Other/Classes/REP-based/DeathKnight.cs"),
        Entry("dracomancer", "Elemental Dracomancer", "Dragon reputation class. Awarded when the class is owned.", ["Elemental Dracomancer"], [], [], "Other/Classes/REP-based/ElementalDracomancer.cs"),
        Entry("inversionist", "Eternal Inversionist", "Reputation class. Awarded when the class is owned.", ["Eternal Inversionist"], [], [], "Other/Classes/REP-based/EternalInversionist.cs"),
        Entry("evolved-shaman", "Evolved Shaman", "Upgraded shaman reputation class. Awarded when the class is owned.", ["Evolved Shaman"], [], [], "Other/Classes/REP-based/EvolvedShaman.cs"),
        Entry("glacial", "Glacial Berserker", "Ice reputation class. Awarded when the class is owned.", ["Glacial Berserker"], [], [], "Other/Classes/REP-based/GlacialBerserker.cs"),
        Entry("horc", "Horc Evader", "Horc reputation class. Awarded when the class is owned.", ["Horc Evader"], [], [], "Other/Classes/REP-based/HorcEvader.cs"),
        Entry("chunin", "Imperial Chunin", "Yokai reputation class. Awarded when the class is owned.", ["Imperial Chunin"], [], [], "Other/Classes/REP-based/ImperialChunin.cs"),
        Entry("lycan", "Lycan", "Lycan reputation class. Awarded when the class is owned.", ["Lycan"], [], [], "Other/Classes/REP-based/Lycan.cs"),
        Entry("master-ranger", "Master Ranger", "Ranged reputation class. Awarded when the class is owned.", ["Master Ranger"], [], [], "Other/Classes/REP-based/MasterRanger.cs"),
        Entry("battle-mage", "Royal Battle Mage", "Caster reputation class. Awarded when the class is owned.", ["Royal Battle Mage"], [], [], "Other/Classes/REP-based/RoyalBattleMage.cs"),
        Entry("shaman", "Shaman", "Reputation class. Awarded when the class is owned.", ["Shaman"], [], [], "Other/Classes/REP-based/Shaman.cs"),
        Entry("stonecrusher", "StoneCrusher", "Earth reputation class. Awarded when the class is owned.", ["StoneCrusher", "Stone Crusher"], [], [], "Other/Classes/REP-based/StoneCrusher.cs"),
        Entry("thief-hours", "Thief of Hours", "Time reputation class. Awarded when the class is owned.", ["Thief of Hours"], [], [], "Other/Classes/REP-based/ThiefOfHours.cs"),
        Entry("troll-spellsmith", "Troll Spellsmith", "Troll reputation class. Awarded when the class is owned.", ["Troll Spellsmith"], [], [], "Other/Classes/REP-based/TrollSpellsmith.cs"),
        Entry("good-paladin", "Paladin", "Good-path class used before ArchPaladin. Awarded when the class is owned.", ["Paladin"], [319], [], "Good/Paladin.cs"),
        Entry("silver-paladin", "Silver Exalted Paladin", "Late Good armor class. Awarded when owned or The Final Scroll (7586) is complete.", ["Silver Exalted Paladin"], [55465], [7586], "Good/SilverExaltedPaladin.cs"),
        Entry("cryomancer", "Cryomancer", "Daily ice class. Awarded when the class is owned.", ["Cryomancer"], [], [], "Dailies/Cryomancer.cs"),
        Entry("pyromancer", "Pyromancer", "Daily fire class. Awarded when the class is owned.", ["Pyromancer"], [], [], "Dailies/Pyromancer.cs"),
        Entry("shadowscythe", "ShadowScythe General", "Daily ShadowScythe class. Awarded when the class is owned.", ["ShadowScythe General"], [], [], "Dailies/ShadowScytheGeneral.cs"),
        Entry("ynr", "Yami no Ronin", "Legion sword class. Awarded when owned or Yami no Ronin (7408) is complete.", ["Yami no Ronin", "Yami No Ronin"], [53841], [7408], "Legion/YamiNoRonin/0YamiNoRonin.cs"),
        Entry("swordmaster", "SwordMaster", "Legion class. Awarded when owned or SwordMaster Class (7415) is complete.", ["SwordMaster"], [53837], [7415], "Legion/SwordMaster.cs"),
        Entry("ildc", "Infinite Legion Dark Caster", "Legion caster. Awarded when the class is owned.", ["Infinite Legion Dark Caster"], [], [], "Legion/InfiniteLegionDarkCaster.cs"),
        Entry("rustbucket", "Rustbucket", "Early class farm. Awarded when owned or ProtoSartorium Parts (126) is complete.", ["Rustbucket"], [610], [126], "Other/Classes/Rustbucket.cs"),
        Entry("mecha", "MechaJouster", "Jouster class. Awarded when owned or Quest to Unlock Jouster Class (3355) is complete.", ["MechaJouster", "Mecha Jouster"], [22606], [3355], "Other/Classes/MechaJouster.cs"),
        Entry("blaze-binder", "Blaze Binder", "Daily fire class. Awarded when the class is owned.", ["Blaze Binder"], [], [], "Other/Classes/Daily-Classes/BlazeBinder.cs"),
        Entry("blod", "Blinding Light of Destiny", "Good-path endgame axe. Awarded when the weapon is owned.", ["Blinding Light of Destiny", "The Blinding Light of Destiny"], [14467], [], "Good/BLoD/0TheBlindingLightofDestiny.cs"),
        Entry("slod", "Sanctified Light of Destiny", "BLoD upgrade. Awarded when owned or A Newer, Shinier Axe (8112) is complete.", ["Sanctified Light of Destiny"], [59255], [8112], "Good/BLoD/1SanctifiedLightofDestiny.cs"),
        Entry("armor-awe", "Armor of Awe", "Gear of Awe chest piece. Awarded when the armor is owned.", ["Armor of Awe"], [29374], [], "Good/GearOfAwe/ArmorOfAwe.cs"),
        Entry("helm-awe", "Helm of Awe", "Gear of Awe helm. Awarded when the helm is owned.", ["Helm of Awe"], [29376], [], "Good/GearOfAwe/HelmOfAwe.cs"),
        Entry("awescended", "Awescended", "Combined Gear of Awe set. Awarded when the armor is owned.", ["Awescended"], [58189], [], "Good/GearOfAwe/Awescended.cs"),
        Entry("sdka", "Sepulchure's DoomKnight Armor", "Evil-path armor. Awarded when owned or Summoning Sepulchure’s Armor (2187) is complete.", ["Sepulchure's DoomKnight Armor"], [14474], [2187], "Evil/SDKA/0SepulchureDoomKnightArmor.cs"),
        Entry("soh", "Sepulchure's Original Helm", "Evil-path helm. Awarded when owned or Sepulchure’s Helm (6555) is complete.", ["Sepulchure's Original Helm"], [45253], [6555], "Evil/SepulchuresOriginalHelm.cs")
    ];

    public static AchievementDef Entry(string id, string title, string detail, string[] itemNames, int[] itemIds, int[] storyQuests, string script)
        => new(id, title, detail, id + ".png", itemNames, itemIds, storyQuests, script);

    public static string StoreFile(string root) => Path.Combine(root, "achievements.json");

    public static bool SafeScript(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || relative.Contains(".."))
            return false;
        if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = relative.Split('/');
        return parts.Length > 0 && parts.All(part => part.Length > 0 && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    public static string ResolvePath(string id, string scriptsRoot, Func<string, bool> exists)
    {
        var def = Catalog.FirstOrDefault(d => d.Id == id) ?? throw new ArgumentException("Unknown achievement. Recheck.");
        if (!SafeScript(def.Script)) throw new InvalidOperationException("This milestone has no mapped bot.");
        string root = Path.GetFullPath(scriptsRoot);
        string path = Path.GetFullPath(Path.Combine(root, def.Script));
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("Invalid script path.");
        if (!exists(path)) throw new FileNotFoundException("The mapped bot is not installed.");
        return path;
    }

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
        IReadOnlyDictionary<string, AchievementAward> persisted,
        Func<string, bool>? scriptExists = null)
    {
        var inv = inventory.ToList();
        var stored = bank.ToList();
        return Catalog.Select(def => {
            persisted.TryGetValue(def.Id, out var prior);
            string location = Locate(def, inv, stored, bankLoaded);
            bool story = def.StoryQuests.Any(storyComplete);
            bool mapped = SafeScript(def.Script);
            bool installed = mapped && (scriptExists?.Invoke(def.Script) ?? false);
            string reason;
            if (prior != null)
            {
                reason = prior.Reason;
                return State(def, true, prior.EarnedAt, reason, location is "Inventory" or "Bank" ? location : "Saved", false);
            }
            if (location is "Inventory" or "Bank")
            {
                reason = location;
                return State(def, true, null, reason, location, false);
            }
            if (story)
            {
                reason = "Story";
                return State(def, true, null, reason, "Story", false);
            }
            if (location == "Unknown")
                reason = "Bank check needed";
            else if (mapped && scriptExists != null && !installed)
                reason = "Bot not installed";
            else
                reason = "Not earned yet";
            bool canRun = !string.IsNullOrEmpty(def.Script) && installed && location == "Missing";
            return State(def, false, null, reason, location, canRun);
        }).ToList();
    }

    static AchievementState State(AchievementDef def, bool earned, long? earnedAt, string reason, string location, bool canRun)
        => new(def.Id, def.Title, def.Detail, def.Image, earned, earnedAt, reason, location, def.Script, canRun);

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

    /// <summary>
    /// Story progress from the live quest tree or QuestData.json. Never calls
    /// <c>IScriptQuest.Load</c> / <c>world.showQuests</c> (that opens the Flash Available Quests panel).
    /// </summary>
    public static bool StoryCompleteQuiet(int questId, Quest? treeQuest, QuestData? cached, Func<int, int, bool> slotCompleted)
    {
        if (questId <= 0) return false;
        if (treeQuest != null && treeQuest.ID == questId)
            return treeQuest.Slot < 0 || slotCompleted(treeQuest.Slot, treeQuest.Value);
        if (cached != null && cached.ID == questId)
            return cached.Slot < 0 || slotCompleted(cached.Slot, cached.Value);
        return false;
    }

    public bool StoryComplete(int questId)
    {
        try
        {
            bot.Quests.TryGetQuest(questId, out var live);
            try { bot.Quests.LoadCachedQuests(); } catch { /* QuestData.json missing; inventory awards still apply. */ }
            QuestData? cached = null;
            try { bot.Quests.CachedDictionary.TryGetValue(questId, out cached); } catch { }
            return StoryCompleteQuiet(questId, live, cached, (slot, value) =>
                bot.Quests.HasBeenCompleted(new Quest { ID = questId, Slot = slot, Value = value, Name = "" }));
        }
        catch (Exception) { return false; }
    }

    public async Task<string> Resolve(string id)
    {
        var def = Catalog.FirstOrDefault(d => d.Id == id) ?? throw new ArgumentException("Unknown achievement. Recheck.");
        if (def.ItemNames.Length > 0)
            await ownership.RequireMissing(def.ItemNames[0], def.ItemIds.FirstOrDefault());
        return ResolvePath(id, scriptsRoot, File.Exists);
    }

    public async Task<object> Scan()
    {
        bool Installed(string script) => File.Exists(Path.Combine(scriptsRoot, script));
        if (!bot.Player.LoggedIn)
            return Payload("", false, "Log in to check inventory and story progress.", Evaluate([], [], true, _ => false, new Dictionary<string, AchievementAward>(), Installed), []);
        string character;
        try { character = CharacterKey(bot.Player.Username); }
        catch (ArgumentException)
        {
            return Payload("", false, "Character name unavailable. Log in again, then Recheck.", Evaluate([], [], true, _ => false, new Dictionary<string, AchievementAward>(), Installed), []);
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
        try { bot.Quests.LoadCachedQuests(); }
        catch (Exception) { /* Story checks then report incomplete; inventory awards still apply. */ }
        var store = LoadStore(storePath);
        var persisted = ReadAwards(store, character);
        var evaluated = Evaluate(inventory, bank, loaded, StoryComplete, persisted, Installed);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var merged = MergeAwards(persisted, evaluated, now);
        var newly = merged.Keys.Where(id => !persisted.ContainsKey(id)).ToArray();
        if (newly.Length > 0)
        {
            WriteAwards(store, character, merged);
            SaveStore(storePath, store);
        }
        var stamped = Evaluate(inventory, bank, loaded, StoryComplete, merged, Installed);
        string note = loaded
            ? "Inventory, bank, and story progress checked."
            : "Inventory checked; bank unavailable. Story quests were still read. Missing-item awards wait for a bank check.";
        return Payload(bot.Player.Username.Trim(), loaded, note, stamped, newly);
    }
}
