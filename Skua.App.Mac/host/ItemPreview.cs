using System.Collections.Concurrent;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace Skua.Mac;

public sealed record ItemPicture(string Name,string[] Images,string Note);
public sealed class ItemPreview(Func<string,Task<string>>? loader=null)
{
    readonly SemaphoreSlim gate=new(3);
    readonly ConcurrentDictionary<string,Lazy<Task<ItemPicture>>> pending=new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string,(DateTime At,ItemPicture Picture)> cache=new(StringComparer.OrdinalIgnoreCase);
    public async Task<ItemPicture> Get(string name) {
        name=name.Trim();
        if(name.Length==0 || name.Length>200)return new(name,[],"Item name unavailable.");
        if(cache.TryGetValue(name,out var hit) && DateTime.UtcNow-hit.At<TimeSpan.FromMinutes(hit.Picture.Images.Length>0?60:2))return hit.Picture;
        var task=pending.GetOrAdd(name,key=>new Lazy<Task<ItemPicture>>(()=>Load(key)));
        try{return await task.Value;}finally{pending.TryRemove(name,out _);}
    }
    async Task<ItemPicture> Load(string name) {
        await gate.WaitAsync();
        try {
            ItemPicture picture;
            try {
                var doc=new HtmlDocument();doc.LoadHtml(await (loader??QuestWikiResolver.Download)(AreaDiscovery.Slug(name)));
                var images=Parse(doc.DocumentNode,name);
                if(images.Length==0) {
                    var found=new List<string>();
                    foreach(var path in Candidates(doc.DocumentNode,name).Take(4)) {
                        var item=new HtmlDocument();item.LoadHtml(await (loader??QuestWikiResolver.Download)(path));
                        found.AddRange(Parse(item.DocumentNode,name));
                    }
                    images=found.Distinct().Take(8).ToArray();
                }
                picture=new(name,images,images.Length>0?"Wiki appearance preview":"No verified item image available.");
            } catch(Exception e) when(e is HttpRequestException or TaskCanceledException or InvalidOperationException) {picture=new(name,[],"Image source unavailable. Try again shortly.");}
            if(cache.Count>=500)
                foreach(var stale in cache.OrderBy(entry=>entry.Value.At).Take(100).Select(entry=>entry.Key).ToArray())
                    cache.TryRemove(stale,out _);
            cache[name]=(DateTime.UtcNow,picture);return picture;
        } finally {gate.Release();}
    }
    public static string[] Parse(HtmlNode root,string name) {
        var title=root.SelectSingleNode("//*[@id='page-title']");
        if(title==null || !MatchesTitle(AreaDiscovery.Text(title),name))return [];
        var content=root.SelectSingleNode("//*[@id='page-content']");
        if(content==null)return [];
        return content.Descendants("img")
            // AQW artwork is commonly a direct child of page-content, not image-container.
            .Where(img=>!img.GetAttributeValue("src","").Contains("/image-tags/",StringComparison.OrdinalIgnoreCase))
            .Where(img=>!int.TryParse(img.GetAttributeValue("width",""),out int width) || width>=80)
            .Where(img=>!int.TryParse(img.GetAttributeValue("height",""),out int height) || height>=80)
            .Select(img=>SafeURL(img.GetAttributeValue("src","")))
            .OfType<string>().Distinct().Take(8).ToArray();
    }
    public static bool MatchesTitle(string title,string name) {
        if(AreaDiscovery.Same(title,name))return true;
        var match=Regex.Match(title,@"^(.*?)\s+\((Sword|Dagger|Axe|Mace|Polearm|Staff|Bow|Wand|Gun|Armor|Class|Helm|Cape|Pet|House|Floor Item|Wall Item|AC|Non-AC|Legend|Non-Legend)\)$",RegexOptions.IgnoreCase);
        return match.Success && AreaDiscovery.Same(match.Groups[1].Value,name);
    }
    public static string[] Candidates(HtmlNode root,string name) {
        var content=root.SelectSingleNode("//*[@id='page-content']");
        return (content?.Descendants("a")??[]).Where(a=>MatchesTitle(AreaDiscovery.Text(a),name))
            .Select(a=> {try{return QuestWikiResolver.WikiPath(a.GetAttributeValue("href",""));}catch(InvalidOperationException){return null;}})
            .OfType<string>().Distinct().ToArray();
    }
    public static string? SafeURL(string source) {
        if(!Uri.TryCreate(new Uri("https://aqwwiki.wikidot.com"),source,out var uri) || !uri.IsDefaultPort || uri.UserInfo!="" || uri.Scheme is not ("http" or "https"))return null;
        if(uri.Host!="i.imgur.com" && uri.Host!="aqwwiki.wikidot.com" && uri.Host!="aqwwiki.wdfiles.com")return null;
        return new UriBuilder(uri){Scheme="https",Port=-1}.Uri.AbsoluteUri;
    }
}
