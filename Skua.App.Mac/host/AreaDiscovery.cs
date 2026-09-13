using HtmlAgilityPack;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Net;
using System.Text.RegularExpressions;

namespace Skua.Mac;

public record AreaLink(string Name,string Path);
public record AreaShop(string Key,string Name,int ID,string Map,string Path);
public record AreaDrop(string Name,string Path,bool Temporary,string Quest);
public record AreaMonster(string Key,string Name,string Path,int HP,string Cell);
public record AreaPage(string Map,string Path,IReadOnlyList<AreaLink> Monsters,IReadOnlyList<AreaLink> Shops,IReadOnlyList<AreaLink> Quests);
public sealed class AreaDiscovery(Func<string,Task<string>>? loader=null,Func<string,Task<IReadOnlyList<string>>>? searcher=null)
{
    readonly Dictionary<string,(DateTime At,HtmlNode Page)> pageCache=new();
    readonly Dictionary<string,(DateTime At,AreaPage Page)> mapCache=new();
    internal static string Text(HtmlNode n)=>Regex.Replace(WebUtility.HtmlDecode(n.InnerText),@"\s+"," ").Trim();
    internal static bool Same(string a,string b)=>string.Equals(a.Trim(),b.Trim(),StringComparison.OrdinalIgnoreCase);
    internal static string Slug(string name)=>"/"+Regex.Replace(name.ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-');
    public async Task<HtmlNode> Page(string path,CancellationToken ct) {
        ct.ThrowIfCancellationRequested();path=QuestWikiResolver.WikiPath(path);
        if(pageCache.TryGetValue(path,out var cached) && DateTime.UtcNow-cached.At<TimeSpan.FromMinutes(5))return cached.Page;
        string html="";
        for(int attempt=0;;attempt++) {
            try {html=await (loader??QuestWikiResolver.Download)(path).WaitAsync(ct);break;}
            catch(Exception e) when(attempt<2 && !ct.IsCancellationRequested && (e is TimeoutException or TaskCanceledException || e is HttpRequestException h && (h.StatusCode==null || h.StatusCode==HttpStatusCode.TooManyRequests || (int?)h.StatusCode>=500))) {await Task.Delay(300*(attempt+1),ct);}
        }
        if(html.Length>2_000_000) throw new InvalidOperationException("Area source page is too large.");
        var doc=new HtmlDocument();doc.LoadHtml(html);if(pageCache.Count>=128)pageCache.Clear();pageCache[path]=(DateTime.UtcNow,doc.DocumentNode);return doc.DocumentNode;
    }
    internal static IEnumerable<HtmlNode> Field(HtmlNode root,string label) {
        var scope=root.SelectSingleNode("//*[@id='page-content']") ?? root;
        var heading=scope.Descendants("strong").FirstOrDefault(n=>Same(Text(n).TrimEnd(':'),label));
        if(heading==null) return [];
        var nodes=new List<HtmlNode>();
        for(var n=heading.NextSibling;n!=null && n.Name!="br" && n.Name!="strong";n=n.NextSibling) nodes.Add(n);
        if(nodes.Any(n=>n.Name=="a" || n.Descendants("a").Any()) || nodes.Any(n=>!string.IsNullOrWhiteSpace(Text(n)))) return nodes;
        var next=heading.ParentNode?.NextSibling;
        while(next!=null && (next.NodeType==HtmlNodeType.Comment || next.NodeType==HtmlNodeType.Text && string.IsNullOrWhiteSpace(next.InnerText))) next=next.NextSibling;
        return next?.Name is "ul" or "ol" ? new[]{next} : [];
    }
    internal static IReadOnlyList<AreaLink> Links(IEnumerable<HtmlNode> nodes)=>nodes.SelectMany(n=>n.Name=="a"?new[]{n}:n.Descendants("a")).Select(a=> {
        try{return new AreaLink(Text(a),QuestWikiResolver.WikiPath(a.GetAttributeValue("href","")));}catch(InvalidOperationException){return null;}
    }).OfType<AreaLink>().Distinct().ToArray();
    public static AreaPage? ParseMap(HtmlNode root,string map,string path) {
        string found=string.Concat(Field(root,"Map Name").Select(Text)).Trim();
        if(!Same(found,map)) return null;
        return new(map,path,Links(Field(root,"Monsters")),Links(Field(root,"Shops")),Links(Field(root,"Quests")));
    }
    public async Task<AreaPage?> Map(string map,CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        if(mapCache.TryGetValue(map,out var cached) && DateTime.UtcNow-cached.At<TimeSpan.FromMinutes(5))return cached.Page;
        string directPath=Same(map,"lostruins")?"/lost-ruins":Slug(map);
        try {var direct=ParseMap(await Page(directPath,ct),map,directPath);if(direct!=null){mapCache[map]=(DateTime.UtcNow,direct);return direct;}}catch(HttpRequestException) { }
        foreach(var path in (await (searcher??QuestWikiResolver.Search)(map).WaitAsync(ct)).Take(5)) {
            try {var found=ParseMap(await Page(path,ct),map,path);if(found!=null){mapCache[map]=(DateTime.UtcNow,found);return found;}}catch(HttpRequestException) { }
        }
        return null;
    }
    public async Task<IReadOnlyList<AreaDrop>> Drops(string path,string monster,CancellationToken ct) {
        var root=await Page(path,ct);
        string title=Text(root.SelectSingleNode("//*[@id='page-title']") ?? root);
        static string Base(string s)=>Regex.Replace(s,@"(?:\s+\((?:Monster|Level \d+|Version \d+|\d+)\))+$","",RegexOptions.IgnoreCase);
        if(!Same(Base(title),Base(monster))) throw new InvalidOperationException("Monster page does not match the selected monster.");
        var result=new List<AreaDrop>();
        foreach(bool temp in new[]{false,true}) {
            foreach(var li in Field(root,temp?"Temporary Items Dropped":"Items Dropped").SelectMany(n=>n.Name=="ul"?n.Elements("li"):Array.Empty<HtmlNode>())) {
                string text=Text(li);var link=Links(li.ChildNodes).FirstOrDefault();
                string name=Regex.Replace(text,@"\s*\(Dropped during.*$","").Trim();
                // Wiki rarity icons have no text; strip only documented quest suffixes.
                result.Add(new(name,temp?"":link?.Path??"",temp,temp?link?.Name??"":""));
            }
        }
        return result.Where(d=>d.Name.Length>0).Distinct().Take(250).ToArray();
    }
    public static IReadOnlyList<int> MapShopIds(IEnumerable<string> scripts) {
        return scripts.SelectMany(source=> {
        string code=Regex.Replace(source,
            """
            "(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|/\*[\s\S]*?\*/|//[^\r\n]*
            """, "");
            return Regex.Matches(code,@"\b(?:loadShop|sendLoadShopRequest)\s*\(\s*(\d+)\s*\)").Select(m=>int.TryParse(m.Groups[1].Value,out var id)?id:0);
        }).Where(id=>id>0).Distinct().Take(30).ToArray();
    }
    public static IReadOnlyList<AreaShop> IndexShops(string root,string map) {
        var result=new List<AreaShop>();
        foreach(var file in Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)) {
            if(file.Contains("Generated-") || new FileInfo(file).Length>2_000_000)continue;
            string text=File.ReadAllText(file);if(!text.Contains(map,StringComparison.OrdinalIgnoreCase))continue;
            foreach(var call in CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()) {
                if(call.Expression is not MemberAccessExpressionSyntax member || member.Name.Identifier.ValueText is not ("StartBuyAllMerge" or "BuyItem"))continue;
                var args=call.ArgumentList.Arguments;
                ExpressionSyntax? Arg(string name,int i)=>args.FirstOrDefault(a=>a.NameColon?.Name.Identifier.ValueText==name)?.Expression ?? (args.Count>i && args[i].NameColon==null?args[i].Expression:null);
                if(Arg("map",0) is not LiteralExpressionSyntax m || m.Token.Value is not string route || !Same(route,map))continue;
                if(Arg("shopID",1) is not LiteralExpressionSyntax id || id.Token.Value is not int shop || shop<=0)continue;
                string name=Regex.Replace(Path.GetFileNameWithoutExtension(file),"([a-z])([A-Z])","$1 $2");
                result.Add(new(shop.ToString(),name,shop,map,""));
            }
        }
        return result.DistinctBy(s=>s.ID).OrderBy(s=>s.Name).Take(100).ToArray();
    }
}
