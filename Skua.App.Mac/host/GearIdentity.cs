using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Skua.Mac;

public sealed class GearIdentity(string questFile)
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
    static GearIdentity() { Client.DefaultRequestHeaders.UserAgent.ParseAdd("SkuaMac/0.1"); Client.DefaultRequestHeaders.Accept.ParseAdd("text/html"); }
    private Dictionary<int,string>? names;
    public static string SlotLabel(string slot) => slot switch {
        "ar" => "Class", "co" => "Armor", "he" => "Helm", "ba" => "Cape", "pe" => "Pet", "Weapon" => "Weapon", "am" => "Amulet", "mi" => "Misc", "ho" => "House", _ => slot };
    public static Dictionary<string,string> ParsePage(string html, string player)
    {
        string Clean(string text) => WebUtility.HtmlDecode(Regex.Replace(text,"<[^>]*>","",RegexOptions.None,TimeSpan.FromSeconds(1))).Trim();
        var heading = Regex.Match(html,@"<h1\b[^>]*>(.*?)</h1>",RegexOptions.IgnoreCase | RegexOptions.Singleline,TimeSpan.FromSeconds(1));
        if (!string.Equals(Clean(heading.Groups[1].Value),player,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Character page did not match the selected player.");
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(html,@"<label>\s*(Class|Weapon|Armor|Helm|Cape|Pet|Misc):\s*</label>\s*<a\b[^>]*>(.*?)</a>",RegexOptions.IgnoreCase | RegexOptions.Singleline,TimeSpan.FromSeconds(1)))
        {
            string name = Clean(match.Groups[2].Value);
            if (name.Length is > 0 and <= 200) result[match.Groups[1].Value] = name;
        }
        return result;
    }
    public void ResolveLocal(JObject data)
    {
        if (names == null)
        {
            names = new();
            if (File.Exists(questFile))
                foreach (var obj in JArray.Parse(File.ReadAllText(questFile)).Descendants().OfType<JObject>())
                    if (int.TryParse((string?)obj["ItemID"],out int id) && id > 0 && (string?)obj["sName"] is { Length: > 0 } name)
                        names.TryAdd(id,name);
        }
        foreach (var item in (data["items"] as JArray ?? new()).OfType<JObject>())
        {
            item["label"] = SlotLabel((string?)item["slot"] ?? "Item");
            if (!string.IsNullOrWhiteSpace((string?)item["name"])) { item["identitySource"] ??= "Game"; continue; }
            if (int.TryParse((string?)item["id"],out int id) && names.TryGetValue(id,out string? name))
            { item["name"] = name; item["identitySource"] = "Local item ID"; }
        }
    }
    public async Task Resolve(JObject data)
    {
        ResolveLocal(data);
        var unresolved = (data["items"] as JArray ?? new()).OfType<JObject>().Where(i=>string.IsNullOrWhiteSpace((string?)i["name"])).ToList();
        if (unresolved.Count == 0 || data["error"] != null) return;
        string player = (string?)data["player"] ?? "";
        if (string.IsNullOrWhiteSpace(player) || player.Length > 64) return;
        try
        {
            using var response = await Client.GetAsync("https://account.aq.com/CharPage?id=" + Uri.EscapeDataString(player));
            response.EnsureSuccessStatusCode();
            string html = await response.Content.ReadAsStringAsync();
            if (html.Length > 2_000_000) throw new InvalidDataException("Character page is too large.");
            var page = ParsePage(html,player);
            int resolved = 0;
            foreach (var item in unresolved)
                if (page.TryGetValue((string)item["label"]!,out string? name))
                { item["name"] = name; item["identitySource"] = "Character page · may lag equipment changes"; resolved++; }
            data["identityNote"] = resolved > 0 ? "Resolved " + resolved + " names from the official character page. Page names may lag equipment changes; unlisted slots keep their item IDs." : "The official character page does not name the remaining slots; their item IDs are retained.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
        { data["identityNote"] = "Character lookup unavailable: " + ex.Message; }
    }
}
