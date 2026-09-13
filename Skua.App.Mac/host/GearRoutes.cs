using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Net;
using System.Text.RegularExpressions;

namespace Skua.Mac;

public record GearShop(string Map, int ShopId, string Evidence);
public static class GearRoutes
{
    public static List<(string File,string Class)> FullFarms(string root,string name)
    {
        var found=new List<(string,string)>();
        foreach(var file in Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)) {
            if((file.Contains(Path.DirectorySeparatorChar+"Generated-Area"+Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar+"Generated-Gear"+Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar+"Generated-Quests"+Path.DirectorySeparatorChar)) || new FileInfo(file).Length>2_000_000) continue;
            string text=File.ReadAllText(file);
            var metadata=Regex.Match(text,@"\A\s*/\*(.*?)\*/",RegexOptions.Singleline);
            var title=Regex.Match(metadata.Groups[1].Value,@"(?m)^name:\s*([^\r\n]+)");
            if(!string.Equals(title.Groups[1].Value.Trim(),name,StringComparison.OrdinalIgnoreCase)) continue;
            var tree=CSharpSyntaxTree.ParseText(text).GetRoot();
            foreach(var type in tree.DescendantNodes().OfType<ClassDeclarationSyntax>()) {
                if(type.Modifiers.Any(m=>m.IsKind(SyntaxKind.SealedKeyword)) || type.Members.OfType<ConstructorDeclarationSyntax>().Any()) continue;
                if(!type.Members.OfType<MethodDeclarationSyntax>().Any(m=>m.Identifier.ValueText=="ScriptMain" && m.ParameterList.Parameters.Count==1 && m.Modifiers.Any(x=>x.IsKind(SyntaxKind.PublicKeyword)))) continue;
                found.Add((Path.GetRelativePath(root,file).Replace('\\','/'),type.Identifier.ValueText));
            }
        }
        return found.Take(3).ToList();
    }
    public static List<GearShop> Shops(string root, string name, int itemId)
    {
        var result = new List<GearShop>();
        foreach (var file in Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)) {
            if ((file.Contains(Path.DirectorySeparatorChar+"Generated-Area"+Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar+"Generated-Gear"+Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar+"Generated-Quests"+Path.DirectorySeparatorChar)) || new FileInfo(file).Length>2_000_000) continue;
            string text=File.ReadAllText(file);
            if (!text.Contains(name,StringComparison.OrdinalIgnoreCase)) continue;
            var tree=CSharpSyntaxTree.ParseText(text).GetRoot();
            var calls=tree.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
            foreach(var call in calls) {
                if (call.Expression is not MemberAccessExpressionSyntax method) continue;
                string methodName=method.Name.Identifier.ValueText;
                var args=call.ArgumentList.Arguments;
                ExpressionSyntax? Arg(string key,int index) => args.FirstOrDefault(a=>a.NameColon?.Name.Identifier.ValueText==key)?.Expression ?? (args.Count>index && args[index].NameColon==null ? args[index].Expression : null);
                string? Str(ExpressionSyntax? e) => e is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.StringLiteralExpression) ? l.Token.ValueText : null;
                int Num(ExpressionSyntax? e) => e is LiteralExpressionSyntax l && int.TryParse(l.Token.ValueText,out int n) ? n : 0;
                string? map=Str(Arg("map",0)); int shop=Num(Arg("shopID",1));
                if (map == null || !Regex.IsMatch(map,@"^[a-zA-Z0-9_-]+$") || shop<=0) continue;
                bool direct=methodName=="BuyItem" && (string.Equals(Str(Arg("name",2)),name,StringComparison.OrdinalIgnoreCase) || itemId>0 && Num(Arg("itemID",2))==itemId);
                bool merge=methodName=="StartBuyAllMerge" && tree.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Any(o=> {
                    var a=o.ArgumentList?.Arguments;
                    return o.Type.ToString()=="Option<bool>" && a is {Count: >=2} && string.Equals(Str(a.Value[1].Expression),name,StringComparison.OrdinalIgnoreCase) && (itemId<=0 || Str(a.Value[0].Expression)==itemId.ToString());
                });
                if (direct || merge) result.Add(new(map,shop,Path.GetRelativePath(root,file)+":"+(call.GetLocation().GetLineSpan().StartLinePosition.Line+1)));
            }
        }
        return result.DistinctBy(s=>(s.Map.ToLowerInvariant(),s.ShopId)).Take(10).ToList();
    }
}

public record GearWebResult(string Url,string Summary,string? Map);
public static class GearWeb
{
    private static readonly HttpClient Http = new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(12)};
    static GearWeb() { Http.DefaultRequestHeaders.UserAgent.ParseAdd("SkuaMac/0.1"); }
    public static string Clean(string html) => WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]*>"," ")).Trim();
    public static string Field(string html,string label) {
        var match=Regex.Match(html,"<strong>\\s*"+Regex.Escape(label)+":?\\s*</strong>(.*?)(?:<br\\s*/?>|</p>)",RegexOptions.Singleline|RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }
    private static List<string> Links(string html) => Regex.Matches(html,"href=[\"'](/[a-zA-Z0-9:_-]+)[\"']").Select(m=>m.Groups[1].Value).Distinct().ToList();
    private static async Task<string> Page(string path) {
        using var response=await Http.GetAsync("http://aqwwiki.wikidot.com"+path);
        response.EnsureSuccessStatusCode();
        string html=await response.Content.ReadAsStringAsync();
        if (html.Length>2_000_000) throw new InvalidDataException("Wiki response too large.");
        return html;
    }
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
        } catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or InvalidDataException) {
            return new(url,"Live wiki lookup failed: "+ex.Message+". No map or shop ID was invented.",null);
        }
    }
}
