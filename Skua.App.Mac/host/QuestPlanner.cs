using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;

namespace Skua.Mac;

public record QuestGoal(string Id, string Item, string Role, int Level, string Script, string Detail);
public record QuestSuggestion(string Id, string Item, string Detail, string Ownership, string Action, bool CanRun, int Priority);
public record QuestNews(string Title, string Date, string Url);

public sealed class QuestPlanner(IScriptInterface bot, string scriptsRoot, GearOwnership ownership)
{
    // Deliberately curated: a title similarity is not sufficient to choose a farming bot.
    public static readonly QuestGoal[] Goals = [
        new("awe", "Cape of Awe", "Boost", 40, "Good/GearOfAwe/CapeOfAwe.cs", "Progression boost goal. Includes the Binky encounter and Awe requirements."),
        new("order", "Lord of Order", "Support", 80, "Dailies/LordOfOrder.cs", "Support class goal. Daily quest progress takes multiple days."),
        new("paladin", "ArchPaladin", "Survival", 80, "Good/ArchPaladin.cs", "Survival and support option for difficult encounters. Script handles its quest chain."),
        new("vhl", "Void Highlord", "Solo", 80, "Nation/VHL/0VoidHighlord.cs", "Solo combat goal with a long Nation resource grind."),
        new("revenant", "Legion Revenant", "Farming", 80, "Legion/Revenant/0LegionRevenant.cs", "Multi-target farming goal. Requires Legion access and substantial prerequisites."),
        new("dragon", "Dragon of Time", "Solo", 90, "Other/Classes/DragonOfTime.cs", "Alternative combat class goal with a substantial quest chain."),
        new("nsod", "Necrotic Sword of Doom", "Weapon", 100, "Evil/NSoD/0NecroticSwordOfDoom.cs", "Long-term endgame weapon goal. Review the script options and resource requirements.")
    ];
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    public static bool PirateVerified(DateTime today) => today.Date >= new DateTime(2026,9,4) && today.Date <= new DateTime(2026,9,18);
    public static List<QuestSuggestion> Recommend(IEnumerable<InventoryItem> inventory, IEnumerable<InventoryItem> bank, bool bankLoaded, int level, Func<string,bool> scriptExists)
    {
        var inv = inventory.ToList(); var stored = bank.ToList();
        var locations = Goals.ToDictionary(g => g.Id, g => GearOwnership.Locate(0,g.Item,inv,stored,bankLoaded));
        var roles = Goals.Where(g => locations[g.Id] is "Inventory" or "Bank").Select(g => g.Role).ToHashSet();
        return Goals.Select(g => {
            string location = locations[g.Id]; bool exists = scriptExists(g.Script);
            string action = location is "Inventory" or "Bank" ? "Owned · " + location : location == "Unknown" ? "Bank check needed" : level < g.Level ? "Suggested at level " + g.Level : !exists ? "Bot not installed" : "Go";
            int priority = location != "Missing" ? 4 : level < g.Level ? 3 : roles.Contains(g.Role) ? 2 : 1;
            string reason = roles.Contains(g.Role) ? "You already own a catalog option for this role. " : "Adds a " + g.Role.ToLowerInvariant() + " option to your catalog goals. ";
            return new QuestSuggestion(g.Id,g.Item,reason+g.Detail,location,action,action == "Go",priority);
        }).OrderBy(g => g.Priority).ToList();
    }
    public async Task<object> Scan()
    {
        if (!bot.Player.LoggedIn) throw new InvalidOperationException("Log in to analyze your character.");
        bool loaded = await ownership.LoadBank();
        var inventory = bot.Inventory.Items.ToList();
        var bank = loaded ? bot.Bank.Items.ToList() : [];
        if (!bot.Player.LoggedIn) throw new InvalidOperationException("Character disconnected. Refresh after login.");
        int level = bot.Player.Level;
        return new { type = "quest-plan", level, equipped = inventory.Where(i => i.Equipped).Select(i => i.Name).ToArray(),
            bankLoaded = loaded, goals = Recommend(inventory,bank,loaded,level,s => File.Exists(Path.Combine(scriptsRoot,s))),
            eventAvailable = PirateVerified(DateTime.UtcNow) && File.Exists(Path.Combine(scriptsRoot,"Seasonal/TalkLikeaPirateDay/DoomPirateStory.cs")),
            eventDetail = PirateVerified(DateTime.UtcNow) ? "Talk Like a Pirate Day maps have returned. Doom Pirate story bot is available; it completes story quests, not every event reward. Verified September 13; review window ends September 18, 2026." : "No currently verified event bot mapping. Check the latest announcements below." };
    }
    public async Task<string> Resolve(string id)
    {
        if (id == "event-pirate") {
            if (!PirateVerified(DateTime.UtcNow)) throw new InvalidOperationException("This event mapping needs a fresh availability review.");
            return Existing("Seasonal/TalkLikeaPirateDay/DoomPirateStory.cs");
        }
        var goal = Goals.SingleOrDefault(g => g.Id == id) ?? throw new ArgumentException("Unknown quest goal. Refresh the planner.");
        if (bot.Player.Level < goal.Level) throw new InvalidOperationException("This goal is suggested at level " + goal.Level + ".");
        await ownership.RequireMissing(goal.Item);
        return Existing(goal.Script);
    }
    private string Existing(string relative)
    {
        string path = Path.Combine(scriptsRoot,relative);
        if (!File.Exists(path)) throw new FileNotFoundException("The mapped bot is not installed.");
        return path;
    }
    public static List<QuestNews> ParseNews(string html)
    {
        var result = new List<QuestNews>();
        foreach (Match m in Regex.Matches(html,@"<p\s+class=""date""\s*>(.*?)</p>\s*<h2>\s*<a\s+href=""(/gamedesignnotes/[^""<>]+)""[^>]*>(.*?)</a>",RegexOptions.Singleline | RegexOptions.IgnoreCase)) {
            if (!DateTime.TryParse(WebUtility.HtmlDecode(m.Groups[1].Value),CultureInfo.GetCultureInfo("en-US"),DateTimeStyles.None,out var date)) continue;
            result.Add(new(WebUtility.HtmlDecode(Regex.Replace(m.Groups[3].Value,"<[^>]+>","")),date.ToString("yyyy-MM-dd"),"https://www.aq.com"+m.Groups[2].Value));
            if (result.Count == 5) break;
        }
        return result;
    }
    public static async Task<object> News()
    {
        try {
            var posts = ParseNews(await Http.GetStringAsync("https://www.aq.com/gamedesignnotes/"));
            if (posts.Count == 0) throw new InvalidOperationException("News format could not be read.");
            return new { type = "quest-news", posts, message = "Latest official announcements, fetched " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC. Publication dates do not confirm an event is still active. New events need a verified bot mapping before Go is enabled." };
        } catch (Exception) { return new { type = "quest-news", posts = Array.Empty<QuestNews>(), message = "Official news unavailable. Retry Refresh; current-event availability has not been inferred from cached news." }; }
    }
}
