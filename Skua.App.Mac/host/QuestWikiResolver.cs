using HtmlAgilityPack;
using Skua.Core.Models.Items;
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace Skua.Mac;

// Reads structured wiki evidence, never executable code. Live IDs remain authoritative.
public sealed record QuestPickup(string Map,string Item,bool Temporary,string Evidence,int MapItemID=0);
public sealed record QuestResolution(IReadOnlyList<GearDrop> Drops,IReadOnlyList<QuestPickup> Pickups);
public sealed class QuestWikiResolver(Func<string,Task<string>>? loader=null,Func<string,Task<IReadOnlyList<string>>>? searcher=null)
{
    static readonly HttpClient Client=new(new SocketsHttpHandler { AllowAutoRedirect=false, PooledConnectionLifetime=TimeSpan.FromMinutes(5) }) { Timeout=TimeSpan.FromSeconds(10) };
    static readonly ConcurrentDictionary<string,Lazy<Task<(DateTime At,string Html)>>> Pages=new(StringComparer.Ordinal);
    static string Text(HtmlNode n)=>Normalize(WebUtility.HtmlDecode(n.InnerText));
    static string Normalize(string s)=>Regex.Replace(s.Replace('’','\''),@"\s+"," ").Trim();
    static bool Same(string a,string b)=>string.Equals(Normalize(a),Normalize(b),StringComparison.OrdinalIgnoreCase);
    static bool HasClass(HtmlNode n,string c)=>n.GetAttributeValue("class","").Split(' ').Contains(c);
    public static string WikiPath(string href)
    {
        if(!Uri.TryCreate(new Uri("https://aqwwiki.wikidot.com"),href,out var uri) || uri.Host!="aqwwiki.wikidot.com" || (uri.Scheme!="https" && uri.Scheme!="http") || !uri.IsDefaultPort || uri.UserInfo!="" || !Regex.IsMatch(uri.AbsolutePath,@"^/[a-z0-9][a-z0-9:-]*$"))
            throw new InvalidOperationException("Unsupported wiki source link.");
        return uri.AbsolutePath;
    }
    internal static async Task<string> Download(string path)
    {
        if(Pages.TryGetValue(path,out var existing)) {
            try {
                var hit=await existing.Value;
                if(DateTime.UtcNow-hit.At<TimeSpan.FromMinutes(30)) return hit.Html;
            } catch { /* Expired or failed entries are fetched again. */ }
            Pages.TryRemove(path,out _);
        }
        var lazy=Pages.GetOrAdd(path,_=>new Lazy<Task<(DateTime At,string Html)>>(()=>Fetch(path)));
        try {
            var page=await lazy.Value;
            if(Pages.Count>256)
                foreach(var key in Pages.Keys.Take(64).ToArray())
                    Pages.TryRemove(key,out _);
            return page.Html;
        } catch { Pages.TryRemove(path,out _); throw; }
    }
    static async Task<(DateTime At,string Html)> Fetch(string path)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request=new HttpRequestMessage(HttpMethod.Get,"http://aqwwiki.wikidot.com"+path);
        request.Headers.UserAgent.ParseAdd("qexec/0.1");
        using var response=await Client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        response.EnsureSuccessStatusCode();
        using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
        using var buffer=new MemoryStream();var chunk=new byte[8192];int count;
        while((count=await stream.ReadAsync(chunk,deadline.Token))>0) { if(buffer.Length+count>2_000_000) throw new InvalidOperationException("Wiki page is too large.");buffer.Write(chunk,0,count); }
        return (DateTime.UtcNow,System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }
    static IEnumerable<HtmlNode> Field(HtmlNode scope,string label)
    {
        var heading=scope.Descendants("strong").FirstOrDefault(n=>Same(Text(n).TrimEnd(':'),label));
        if(heading==null) yield break;
        for(var n=heading.NextSibling;n!=null && n.Name!="br" && n.Name!="strong";n=n.NextSibling) yield return n;
    }
    static IEnumerable<HtmlNode> Locations(HtmlNode scope)
    {
        foreach(string label in new[]{"Location","Locations"}) {
            var heading=scope.Descendants("strong").FirstOrDefault(n=>Same(Text(n).TrimEnd(':'),label));
            if(heading==null) continue;
            var links=Links(Field(scope,label)).ToArray();
            // Wikidot renders plural fields as a standalone paragraph followed by a list.
            // Only use its immediate list; never scan forward into drops or notes.
            if(links.Length==0 && heading.ParentNode?.Name=="p" && Same(Text(heading.ParentNode).TrimEnd(':'),label)) {
                var next=heading.ParentNode.NextSibling;
                while(next!=null && (next.NodeType==HtmlNodeType.Comment || next.NodeType==HtmlNodeType.Text && string.IsNullOrWhiteSpace(next.InnerText))) next=next.NextSibling;
                if(next?.Name is "ul" or "ol") links=next.Descendants("a").ToArray();
            }
            foreach(var link in links) {
                var row=link.Ancestors("li").FirstOrDefault() ?? link.ParentNode;
                // A documented seasonal/rare/member-only alternative is not an always-available route.
                if(row!=null && row.Descendants("img").Any(img=>Regex.IsMatch(img.GetAttributeValue("src",""),@"(?:seasonal|rare|legend)(?:small|large)\.png",RegexOptions.IgnoreCase))) continue;
                yield return link;
            }
        }
    }
    static HtmlNode? QuestSection(HtmlNode root,string quest)
    {
        var matches=new List<HtmlNode>();
        foreach(var nav in root.Descendants("ul").Where(n=>HasClass(n,"yui-nav"))) {
            var tabs=nav.Elements("li").ToArray();
            var content=nav.ParentNode!.Elements("div").FirstOrDefault(n=>HasClass(n,"yui-content"));
            for(int i=0;i<tabs.Length;i++) if(Same(Text(tabs[i]),quest) && content?.Elements("div").ElementAtOrDefault(i) is HtmlNode section) matches.Add(section);
        }
        return matches.Count==1 ? matches[0] : null;
    }
    public async Task<IReadOnlyList<GearDrop>> Resolve(string quest,IEnumerable<string> rewards,IReadOnlyList<ItemBase> requirements)
        => (await ResolvePlan(quest,rewards,requirements)).Drops;

    static string MonsterName(string value)=>Regex.Replace(value,@"(?:\s+\((?:Monster|Level \d+|Version \d+|\d+)\))+$","",RegexOptions.IgnoreCase);
    internal static async Task<IReadOnlyList<string>> Search(string quest)
    {
        // Search provides candidate URLs only. Quest/objective evidence is verified on the wiki.
        string query=Uri.EscapeDataString("site:aqwwiki.wikidot.com \""+quest+"\"");
        foreach(string url in new[]{"https://www.google.com/search?q="+query,"https://www.bing.com/search?q="+query}) {
            try {
                var paths=await SearchHtml(url);
                if(paths.Count>0) return paths;
            } catch(Exception ex) when(ex is HttpRequestException or TimeoutException or OperationCanceledException or TaskCanceledException) { }
        }
        return [];
    }
    static async Task<IReadOnlyList<string>> SearchHtml(string url)
    {
        using var response=await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        string html=await response.Content.ReadAsStringAsync();
        if(html.Length>2_000_000) throw new InvalidOperationException("Quest search response is too large.");
        var doc=new HtmlDocument();doc.LoadHtml(html);var paths=new List<string>();
        foreach(var a in doc.DocumentNode.Descendants("a")) {
            string href=WebUtility.HtmlDecode(a.GetAttributeValue("href",""));
            if(href.StartsWith("/url?")) {
                var queryParts=href[5..].Split('&').Select(part=>part.Split('=',2));
                href=Uri.UnescapeDataString(queryParts.FirstOrDefault(p=>p.Length==2 && (p[0]=="q" || p[0]=="url"))?.ElementAtOrDefault(1) ?? "");
            }
            href=WebUtility.HtmlDecode(Uri.UnescapeDataString(href));
            if(!Uri.TryCreate(href,UriKind.Absolute,out var uri)) {
                var encoded=Regex.Match(href,@"https?%3A%2F%2Faqwwiki\.wikidot\.com%2F[a-z0-9%:-]+",RegexOptions.IgnoreCase);
                if(encoded.Success) uri=new Uri(Uri.UnescapeDataString(encoded.Value));
                else continue;
            }
            if(uri.Host!="aqwwiki.wikidot.com") continue;
            try {paths.Add(WikiPath(uri.AbsoluteUri));}catch(InvalidOperationException) { }
        }
        return paths.Distinct().Take(6).ToArray();
    }
    static bool ValidMap(string? map)=>!string.IsNullOrWhiteSpace(map) && Regex.IsMatch(map,@"^[a-zA-Z0-9_-]+$");
    static bool IsPickupHint(string text)=>Regex.IsMatch(text,@"\b(?:Click(?:ing)?|Walk to Screen)\b",RegexOptions.IgnoreCase);
    static HtmlNode[] RequiredEntries(HtmlNode list,ItemBase item)
    {
        HtmlNode[] Match(string pattern)=>list.Elements("li").Where(li=>{
            string head=Normalize(string.Concat(li.ChildNodes.TakeWhile(n=>n.Name!="ul").Select(n=>WebUtility.HtmlDecode(n.InnerText))));
            return Regex.IsMatch(head,pattern,RegexOptions.IgnoreCase);
        }).ToArray();
        var exact=Match("^"+Regex.Escape(Normalize(item.Name))+@"\s+x"+item.Quantity+@"\b");
        if(exact.Length==1) return exact;
        var named=Match("^"+Regex.Escape(Normalize(item.Name))+@"\b");
        if(named.Length!=1) return exact;
        string head=Normalize(string.Concat(named[0].ChildNodes.TakeWhile(n=>n.Name!="ul").Select(n=>WebUtility.HtmlDecode(n.InnerText))));
        var shown=Regex.Match(head,@"\sx(\d+)\b",RegexOptions.IgnoreCase);
        return !shown.Success || shown.Groups[1].Value==item.Quantity.ToString() ? named : exact;
    }
    static IEnumerable<HtmlNode> QuestLocationLinks(HtmlNode section)
    {
        var page=section.OwnerDocument?.DocumentNode ?? section;
        foreach(string label in new[]{"Quest Location","Quest Locations"})
            foreach(var link in Links(Field(page,label))) yield return link;
        var navset=section.Ancestors("div").FirstOrDefault(n=>HasClass(n,"yui-navset"));
        for(var prev=navset?.PreviousSibling;prev!=null;prev=prev.PreviousSibling) {
            var fields=Field(prev,"Quest Location").ToArray();
            var plural=prev.Descendants("strong").FirstOrDefault(n=>Same(Text(n).TrimEnd(':'),"Quest Locations"));
            if(plural!=null) {
                var listNode=prev.SelectSingleNode("following-sibling::ul[1]");
                if(listNode!=null) foreach(var a in listNode.Descendants("a")) yield return a;
                yield break;
            }
            if(fields.Length==0) continue;
            foreach(var a in fields.SelectMany(n=>n.Name=="a" ? new[]{n} : n.Descendants("a"))) yield return a;
            yield break;
        }
    }
    static IEnumerable<HtmlNode> Links(IEnumerable<HtmlNode> nodes)=>nodes.SelectMany(n=>n.Name=="a" ? new[]{n} : n.Descendants("a"));
    static string Slug(string name)=>"/"+Regex.Replace(Normalize(name).ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-');

    static async Task<IReadOnlyList<GearDrop>> PermanentDrops(ItemBase item,string itemPath,Func<string,Task<HtmlNode>> page)
    {
        if(item.Temp) return [];
        itemPath=WikiPath(itemPath);
        var detail=await page(itemPath);
        var title=detail.SelectSingleNode("//*[@id='page-title']");
        if(title==null || !Same(Text(title),item.Name)) return [];
        var body=detail.SelectSingleNode("//*[@id='page-content']") ?? detail;
        var sources=body.Descendants("li").Where(n=>Regex.IsMatch(Text(n),@"^Dropped by\b",RegexOptions.IgnoreCase)).SelectMany(n=>n.Descendants("a")).DistinctBy(n=>n.GetAttributeValue("href","")).Take(5);
        var result=new List<GearDrop>();
        foreach(var source in sources) {
            string monsterPath=WikiPath(source.GetAttributeValue("href",""));var monster=await page(monsterPath);
            var monsterTitle=monster.SelectSingleNode("//*[@id='page-title']");
            if(monsterTitle==null || !Same(MonsterName(Text(source)),MonsterName(Text(monsterTitle)))) continue;
            // A normal inventory drop is listed under Items Dropped, with no quest-specific backlink.
            foreach(var heading in monster.Descendants("strong").Where(n=>Same(Text(n).TrimEnd(':'),"Items Dropped"))) {
                var drops=heading.ParentNode?.SelectSingleNode("following-sibling::ul[1]");
                if(drops==null || !drops.Descendants("a").Any(a=>Same(Text(a),item.Name) && WikiPath(a.GetAttributeValue("href",""))==itemPath)) continue;
                var scope=heading.Ancestors("div").FirstOrDefault(n=>n.ParentNode!=null && HasClass(n.ParentNode,"yui-content")) ?? monster.SelectSingleNode("//*[@id='page-content']");
                if(scope==null) continue;
                var locations=Locations(scope).ToArray();
                foreach(var location in locations.Take(4)) {
                    string mapPath=WikiPath(location.GetAttributeValue("href",""));var map=await page(mapPath);
                    string mapName=Normalize(string.Concat(Field(map,"Map Name").Select(Text)));
                    if(!Regex.IsMatch(mapName,@"^[a-zA-Z0-9_-]+$")) continue;
                    result.Add(new(mapName,MonsterName(Text(monsterTitle)),item.Name,false,"http://aqwwiki.wikidot.com"+itemPath+" -> "+monsterPath+" -> "+mapPath));
                }
            }
        }
        return result.DistinctBy(d=>(d.Map,d.Monster)).Take(1).ToArray();
    }

    public async Task<QuestResolution> ResolvePlan(string quest,IEnumerable<string> rewards,IReadOnlyList<ItemBase> requirements,Action<string>? progress=null,CancellationToken cancellation=default,IEnumerable<string>? questSources=null,string? pickupMap=null)
    {
        var pages=new Dictionary<string,HtmlNode>();var started=DateTime.UtcNow;
        async Task<HtmlNode> Page(string href) {
            cancellation.ThrowIfCancellationRequested();
            string path=WikiPath(href);
            if(pages.TryGetValue(path,out var cached)) return cached;
            if(pages.Count>=40 || DateTime.UtcNow-started>TimeSpan.FromSeconds(90)) throw new InvalidOperationException("Quest source lookup limit reached. Retry the quest.");
            string html="";
            for(int attempt=0;;attempt++) {
                try {html=await (loader??Download)(path).WaitAsync(cancellation);break;}
                catch(Exception ex) when(attempt<2 && !cancellation.IsCancellationRequested &&
                    (ex is TimeoutException || ex is OperationCanceledException ||
                     ex is HttpRequestException http && (http.StatusCode==null || http.StatusCode==HttpStatusCode.RequestTimeout || http.StatusCode==HttpStatusCode.TooManyRequests || (int?)http.StatusCode>=500))) {
                    progress?.Invoke("Retrying temporary source failure ("+(attempt+1)+"/2): "+path);
                    await Task.Delay(300*(attempt+1),cancellation);
                }
            }
            var doc=new HtmlDocument();doc.LoadHtml(html);pages[path]=doc.DocumentNode;return doc.DocumentNode;
        }
        var sources=(questSources??[]).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool SkipMissing(Exception ex)=>ex is HttpRequestException or KeyNotFoundException
            || (ex is InvalidOperationException && ex.Message.Contains("lookup limit",StringComparison.OrdinalIgnoreCase)==false);
        async Task AddMapGuides(string map) {
            foreach(string path in AreaDiscovery.MapPaths(map).Distinct(StringComparer.OrdinalIgnoreCase)) {
                try {var parsed=AreaDiscovery.ParseMap(await Page(path),map,path);if(parsed!=null){sources.AddRange(parsed.Quests.Select(q=>q.Path));return;}}
                catch(Exception ex) when(!cancellation.IsCancellationRequested && SkipMissing(ex)) { }
            }
            if(loader!=null && searcher==null) return;
            try {
                foreach(string path in (await (searcher??Search)(map).WaitAsync(cancellation)).Take(5)) {
                    try {var parsed=AreaDiscovery.ParseMap(await Page(path),map,path);if(parsed!=null){sources.AddRange(parsed.Quests.Select(q=>q.Path));return;}}
                    catch(Exception ex) when(!cancellation.IsCancellationRequested && SkipMissing(ex)) { }
                }
            } catch(Exception ex) when(!cancellation.IsCancellationRequested && (ex is HttpRequestException || ex is OperationCanceledException || ex is TimeoutException)) { }
        }
        if(!string.IsNullOrWhiteSpace(pickupMap)) {
            progress?.Invoke("Finding quest guides for /"+pickupMap+"…");
            await AddMapGuides(pickupMap);
        }
        HtmlNode? section=null;string questPath="";
        foreach(string candidate in sources.Take(12)) {
            try {var found=QuestSection(await Page(candidate),quest);if(found!=null){section=found;questPath=WikiPath(candidate);break;}}
            catch(HttpRequestException) { }
        }
        progress?.Invoke("Finding wiki/guide evidence for "+quest+"…");
        foreach(string reward in (section!=null?Array.Empty<string>():requirements.Where(r=>!r.Temp).Select(r=>r.Name).Concat(rewards).Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct().Take(6))) {
            string slug="/"+Regex.Replace(Normalize(reward).ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-');
            HtmlNode page;
            try { page=await Page(slug); } catch(Exception ex) when(!cancellation.IsCancellationRequested && SkipMissing(ex)) { continue; }
            var title=page.SelectSingleNode("//*[@id='page-title']");if(title==null || !Same(Text(title),reward)) continue;
            var links=(page.SelectSingleNode("//*[@id='page-content']") ?? page).Descendants("a").Where(a=>Same(Text(a),quest)).DistinctBy(a=>a.GetAttributeValue("href","").Split('#')[0]).ToArray();
            if(links.Length!=1) continue;
            questPath=WikiPath(links[0].GetAttributeValue("href",""));
            section=QuestSection(await Page(questPath),quest);if(section!=null) break;
        }
        if(section==null && (loader==null || searcher!=null)) {
            progress?.Invoke("Searching wiki/guides for the accepted quest…");
            var queries=new[]{quest}.Concat(requirements.Select(r=>r.Name)).Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct().Take(4);
            foreach(string query in queries) {
                IReadOnlyList<string> candidates=[];
                try {candidates=await (searcher??Search)(query!).WaitAsync(cancellation);}
                catch(Exception ex) when(!cancellation.IsCancellationRequested && (ex is HttpRequestException || ex is OperationCanceledException || ex is TimeoutException)) {
                    progress?.Invoke("Quest search unavailable; trying the required material sources directly…");
                    break;
                }
                foreach(string candidate in candidates) {
                    HtmlNode candidatePage;
                    try {candidatePage=await Page(candidate);}
                    catch(HttpRequestException) {continue;}
                    var found=QuestSection(candidatePage,quest);
                    if(found!=null) {section=found;questPath=WikiPath(candidate);break;}
                    foreach(var link in candidatePage.Descendants("a").Where(a=>Same(Text(a),quest)).Take(3)) {
                        string path=WikiPath(link.GetAttributeValue("href",""));
                        found=QuestSection(await Page(path),quest);
                        if(found!=null) {section=found;questPath=path;break;}
                    }
                    if(section!=null) break;
                }
                if(section!=null) break;
            }
        }
        if(section==null) {
            var recovered=new List<GearDrop>();
            foreach(var material in requirements.Where(r=>!r.Temp)) {
                progress?.Invoke("Quest page unavailable; tracing the material source for "+material.Name+"…");
                try {recovered.AddRange(await PermanentDrops(material,Slug(material.Name),Page));}
                catch(HttpRequestException ex) when(ex.StatusCode==HttpStatusCode.NotFound) { }
            }
            return new(recovered,[]);
        }
        var label=section.Descendants("strong").FirstOrDefault(n=>Same(Text(n).TrimEnd(':'),"Items Required"));
        var list=label?.ParentNode?.SelectSingleNode("following-sibling::ul[1]");if(list==null) return new([],[]);
        var questMaps=new List<string>();
        if(ValidMap(pickupMap)) questMaps.Add(pickupMap!);
        foreach(var link in QuestLocationLinks(section).Take(4)) {
            try {
                string mapPath=WikiPath(link.GetAttributeValue("href",""));
                var mapPage=await Page(mapPath);
                string map=Normalize(string.Concat(Field(mapPage,"Map Name").Select(Text)));
                if(ValidMap(map) && !questMaps.Any(m=>Same(m,map))) questMaps.Add(map);
            } catch(HttpRequestException) { }
        }
        var results=new List<GearDrop>();var pickups=new List<QuestPickup>();
        foreach(var item in requirements) {
            progress?.Invoke("Resolving "+item.Name+" (#"+item.ID+")…");
            var entries=RequiredEntries(list,item);
            if(entries.Length!=1) continue;
            var direct=entries[0].Elements("a").FirstOrDefault(a=>Same(Text(a),item.Name));
            if(!item.Temp) {
                progress?.Invoke("Tracing inventory material → monster drop → map: "+item.Name+"…");
                try {results.AddRange(await PermanentDrops(item,direct?.GetAttributeValue("href","") ?? Slug(item.Name),Page));}
                catch(HttpRequestException ex) when(ex.StatusCode==HttpStatusCode.NotFound) { }
            }
            if(direct!=null) {
                try {
                    string itemPath=WikiPath(direct.GetAttributeValue("href",""));var detail=await Page(itemPath);
                    var itemTitle=detail.SelectSingleNode("//*[@id='page-title']");
                    string price=Normalize(string.Concat(Field(detail,"Price").Select(Text)));
                    if(itemTitle!=null && Same(Text(itemTitle),item.Name) && IsPickupHint(price)) {
                        var locations=Locations(detail).ToArray();
                        foreach(var location in locations.Take(3)) {
                            string mapPath=WikiPath(location.GetAttributeValue("href",""));var mapPage=await Page(mapPath);
                            string map=Normalize(string.Concat(Field(mapPage,"Map Name").Select(Text)));
                            if(ValidMap(map)) pickups.Add(new(map,item.Name,item.Temp,"http://aqwwiki.wikidot.com"+itemPath+" -> "+mapPath));
                        }
                    }
                } catch(HttpRequestException) { }
            }
            if(IsPickupHint(string.Join(" ",entries[0].ChildNodes.Select(Text)))) {
                var locationLinks=entries[0].Descendants("a").ToList();
                if(locationLinks.Count==0) locationLinks.AddRange(QuestLocationLinks(section));
                foreach(var link in locationLinks.Take(3)) {
                    try {
                        string mapPath=WikiPath(link.GetAttributeValue("href",""));var mapPage=await Page(mapPath);
                        string map=Normalize(string.Concat(Field(mapPage,"Map Name").Select(Text)));
                        if(ValidMap(map) && (locationLinks.Count<=1 || pickupMap==null || Same(map,pickupMap)))
                            pickups.Add(new(map,item.Name,item.Temp,"http://aqwwiki.wikidot.com"+questPath+" -> "+mapPath));
                    } catch(Exception ex) when(!cancellation.IsCancellationRequested && SkipMissing(ex)) { }
                }
                foreach(string map in questMaps.Where(ValidMap))
                    if(!pickups.Any(p=>Same(p.Item,item.Name) && p.Temporary==item.Temp))
                        pickups.Add(new(map,item.Name,item.Temp,"http://aqwwiki.wikidot.com"+questPath+" -> /"+map));
            }
            var dropSources=entries[0].Descendants("li").Where(n=>Regex.IsMatch(Text(n),@"^Dropped by\b",RegexOptions.IgnoreCase)).SelectMany(n=>n.Descendants("a")).ToArray();
            var candidates=new List<GearDrop>();
            foreach(var source in dropSources.Take(6)) {
                string monsterName=MonsterName(Text(source));
                if(string.IsNullOrWhiteSpace(monsterName)) continue;
                string monsterPath="";
                try {monsterPath=WikiPath(source.GetAttributeValue("href",""));} catch(InvalidOperationException) { }
                if(monsterPath.Length>0) {
                    try {
                        var monster=await Page(monsterPath);
                        var title=monster.SelectSingleNode("//*[@id='page-title']");
                        if(title!=null && Same(MonsterName(Text(title)),monsterName)) {
                            var requiredLevel=Regex.Match(Text(source),@"\(Level (\d+)\)");
                            var backlinks=monster.Descendants("li").Where(li=>Text(li).StartsWith(item.Name+" (Dropped during",StringComparison.OrdinalIgnoreCase) && li.Descendants("a").Any(a=>Same(Text(a),quest) && WikiPath(a.GetAttributeValue("href",""))==questPath)).ToArray();
                            foreach(var drop in backlinks) {
                                var scope=drop.Ancestors("div").FirstOrDefault(n=>n.ParentNode!=null && HasClass(n.ParentNode,"yui-content")) ?? monster.SelectSingleNode("//*[@id='page-content']") ?? monster;
                                string level=Normalize(string.Concat(Field(scope,"Level").Select(Text)));
                                if(requiredLevel.Success && level.Length>0 && level!=requiredLevel.Groups[1].Value) continue;
                                foreach(var location in Locations(scope).Take(4)) {
                                    try {
                                        string mapPath=WikiPath(location.GetAttributeValue("href",""));var mapPage=await Page(mapPath);
                                        string map=Normalize(string.Concat(Field(mapPage,"Map Name").Select(n=>WebUtility.HtmlDecode(n.InnerText))));
                                        if(ValidMap(map)) candidates.Add(new GearDrop(map,monsterName,item.Name,item.Temp,"https://aqwwiki.wikidot.com"+questPath+" -> "+monsterPath+" -> "+mapPath));
                                    } catch(Exception ex) when(!cancellation.IsCancellationRequested && SkipMissing(ex)) { }
                                }
                            }
                        }
                    } catch(Exception ex) when(!cancellation.IsCancellationRequested && SkipMissing(ex)) { }
                }
                // Quest-page "Dropped by" plus the documented quest map is enough. Do not require a matching monster-page backlink.
                foreach(string map in questMaps.Where(ValidMap))
                    candidates.Add(new GearDrop(map,monsterName,item.Name,item.Temp,"https://aqwwiki.wikidot.com"+questPath+(monsterPath.Length>0?" -> "+monsterPath:"")+" -> /"+map));
            }
            var distinct=candidates.DistinctBy(d=>(d.Map,d.Monster)).ToArray();
            if(distinct.Length>0) {
                var preferred=results.Select(d=>d.Map).Concat(pickups.Select(p=>p.Map)).Concat(questMaps);
                var chosen=QuestFastestPath.PickDrop(distinct,pickupMap,preferred) ?? distinct[0];
                var alternates=distinct.Where(d=>!QuestFastestPath.Same(d.Map,chosen.Map) || !QuestFastestPath.Same(d.Monster,chosen.Monster)).ToList();
                results.Add(chosen with { Alternates = alternates.Count>0 ? alternates : null });
            }
        }
        return new(results,QuestFastestPath.SelectPickups(pickups,pickupMap,results.Select(d=>d.Map)).ToArray());
    }
}
