using System.Net;
using System.Text.RegularExpressions;

namespace Skua.Mac;

public record GearShop(string Map, int ShopId, string Evidence);
public static class GearRoutes
{
    public static List<(string File,string Class)> FullFarms(string root,string name)
        => ScriptEvidence.Scan(root)
            .SelectMany(script => script.FullFarms
                .Where(farm => string.Equals(farm.Title,name,StringComparison.OrdinalIgnoreCase))
                .Select(farm => (script.RelativePath, farm.Class)))
            .Take(3).ToList();
    public static List<GearShop> Shops(string root, string name, int itemId)
    {
        var result = new List<GearShop>();
        foreach (var script in ScriptEvidence.Scan(root))
        {
            bool mergeMatch = script.BoolOptions.Any(option =>
                string.Equals(option.OptionName,name,StringComparison.OrdinalIgnoreCase)
                && (itemId<=0 || option.OptionId==itemId.ToString()));
            foreach (var shop in script.Shops)
            {
                bool direct = shop.Method=="BuyItem" && (string.Equals(shop.ItemName,name,StringComparison.OrdinalIgnoreCase) || itemId>0 && shop.ItemId==itemId);
                if (direct || shop.Method=="StartBuyAllMerge" && mergeMatch)
                    result.Add(new(shop.Map,shop.ShopId,script.RelativePath+":"+shop.Line));
            }
        }
        return result.DistinctBy(s=>(s.Map.ToLowerInvariant(),s.ShopId)).Take(10).ToList();
    }
}

public record GearWebResult(string Url,string Summary,string? Map);
public static class GearWeb
{
    public static string Clean(string html) => WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]*>"," ")).Trim();
    public static string Field(string html,string label) {
        var match=Regex.Match(html,"<strong>\\s*"+Regex.Escape(label)+":?\\s*</strong>(.*?)(?:<br\\s*/?>|</p>)",RegexOptions.Singleline|RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }
    private static List<string> Links(string html) => Regex.Matches(html,"href=[\"'](/[a-zA-Z0-9:_-]+)[\"']").Select(m=>m.Groups[1].Value).Distinct().ToList();
    private static Task<string> Page(string path) => QuestWikiResolver.Download(path);
    public static async Task<GearWebResult> Lookup(string name) {
        string slug=Regex.Replace(name.ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-');
        string path="/"+slug; string url="http://aqwwiki.wikidot.com"+path;
        try {
            string html=await Page(path);
            string title=Clean(Regex.Match(html,"<div[^>]*id=[\"']page-title[\"'][^>]*>(.*?)</div>",RegexOptions.Singleline).Groups[1].Value);
            if (!string.Equals(title,name,StringComparison.OrdinalIgnoreCase)) return new(url,"Wiki page did not match this exact item name. No route was guessed.",null);
            string location=Field(html,"Location"), rarity=Clean(Field(html,"Rarity"));
            string summary="Wiki location: "+Clean(location)+". "+(rarity.Length>0 ? "Rarity: "+rarity+". " : "")+"Price / requirements: "+Clean(Field(html,"Price"))+". ";
            // Only follow links actually present in the item's location field. Bounded to four page requests.
            foreach(string link in Links(location).Take(3)) {
                string linked;
                try { linked=await Page(link); } catch(HttpRequestException) { continue; } catch(TaskCanceledException) { continue; }
                string note=Clean(Field(linked,"Note"));
                if(note.Length>0) summary+="Source requirement: "+note+". ";
                string map=Clean(Field(linked,"Map Name"));
                if (Regex.IsMatch(map,@"^[a-zA-Z0-9_-]{1,60}$")) return new(url,summary+"Generated travel route only; use the listed NPC/shop. Shop ID and acquisition prerequisites still need verification.",map);
            }
            return new(url,summary+"No verified map or shop ID found. This may require an account unlock, a rare source, or additional quest research.",null);
        } catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException) {
            return new(url,"Live wiki lookup failed: "+ex.Message+". No map or shop ID was invented.",null);
        }
    }
}
