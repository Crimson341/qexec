using System.Diagnostics;
using System.Text.RegularExpressions;
using Skua.Core.Interfaces;

namespace Skua.Core.Scripts;

/// <summary>Discovers map pickup calls from the current map, without a prewritten farming script.</summary>
public static class QuestMapPickup
{
    public sealed record PickupCall(int ID,int QuestID);
    public static IReadOnlyList<PickupCall> Parse(string source)
    {
        source=Regex.Replace(source,
            """
            "(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|/\*[\s\S]*?\*/|//[^\r\n]*
            """, "");
        var result=new List<PickupCall>();
        // Inspect balanced function bodies so neighboring handlers cannot supply a quest ID.
        foreach(Match function in Regex.Matches(source,@"\bfunction\s+\w+\s*\([^)]*\)[^{]*\{")) {
            int start=function.Index+function.Length,depth=1,end=start;char quote='\0';
            for(;end<source.Length && depth>0;end++) {
                char c=source[end];
                if(quote!='\0') {if(c=='\\') end++;else if(c==quote) quote='\0';continue;}
                if(c=='\'' || c=='"') {quote=c;continue;}
                if(c=='{') depth++;else if(c=='}') depth--;
            }
            if(depth!=0) continue;
            string body=source[start..(end-1)];
            var quests=Regex.Matches(body,@"isQuestInProgress\s*\(\s*(\d+)\s*\)",RegexOptions.IgnoreCase).Select(m=>int.Parse(m.Groups[1].Value)).Distinct().ToArray();
            if(quests.Length>1) continue;
            foreach(Match call in Regex.Matches(body,@"\bgetMapItem\s*\(\s*(\d+)\s*\)",RegexOptions.IgnoreCase))
                result.Add(new(int.Parse(call.Groups[1].Value),quests.SingleOrDefault()));
            // Timeline instances use obj.mapItem and obj.questNum instead of a direct call.
            foreach(Match assignment in Regex.Matches(body,@"(?<obj>[\w.]+)\.mapItem\s*=\s*(?<id>\d+)\s*;",RegexOptions.IgnoreCase)) {
                string obj=Regex.Escape(assignment.Groups["obj"].Value);
                var ids=Regex.Matches(body,obj+@"\.(?:questNum|intQuest)\s*=\s*(\d+)\s*;",RegexOptions.IgnoreCase).Select(m=>int.Parse(m.Groups[1].Value)).Distinct().ToArray();
                if(ids.Length==1) result.Add(new(int.Parse(assignment.Groups["id"].Value),ids[0]));
            }
        }
        return result.Where(r=>r.ID>0).Distinct().ToArray();
    }
    public static int Select(IEnumerable<PickupCall> calls,int questId)
    {
        var all=calls.Distinct().ToArray();
        var exact=all.Where(c=>c.QuestID==questId).Select(c=>c.ID).Distinct().ToArray();
        if(exact.Length==1) return exact[0];
        var free=all.Where(c=>c.QuestID==0).Select(c=>c.ID).Distinct().ToArray();
        if(exact.Length==0 && free.Length==1) return free[0];
        throw new InvalidOperationException("Map pickup discovery found multiple or no matching buttons. No unverified pickup ID was sent.");
    }
    static async Task<int> Discover(string filePath,int questId,CancellationToken token)
    {
        string jar=Path.Combine(AppContext.BaseDirectory,"FFDec","ffdec.jar");
        if(!File.Exists(jar)) throw new InvalidOperationException("Map discovery tools are missing from this installation.");
        string java=new[]{Path.Combine(AppContext.BaseDirectory,"jre","bin","java"),"/opt/homebrew/opt/openjdk/bin/java","/usr/local/opt/openjdk/bin/java"}.FirstOrDefault(File.Exists) ?? "java";
        if(string.IsNullOrWhiteSpace(filePath) || filePath.Contains("..") || !Regex.IsMatch(filePath,@"^[a-zA-Z0-9_./-]+\.swf$")) throw new InvalidOperationException("Map file path is unavailable or invalid.");
        string work=Path.Combine(Path.GetTempPath(),"qexec-map-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        try {
            using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)};
            using var response=await http.GetAsync("https://game.aq.com/game/gamefiles/maps/"+filePath,HttpCompletionOption.ResponseHeadersRead,token);
            response.EnsureSuccessStatusCode();
            await using(var input=await response.Content.ReadAsStreamAsync(token))
            await using(var output=File.Create(Path.Combine(work,"map.swf"))) {
                var buffer=new byte[8192];int n;
                while((n=await input.ReadAsync(buffer,token))>0) {if(output.Length+n>30_000_000) throw new InvalidOperationException("Map file exceeds discovery size limit.");await output.WriteAsync(buffer.AsMemory(0,n),token);}
            }
            var info=new ProcessStartInfo(java){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
            foreach(var arg in new[]{"-Djava.awt.headless=true","-jar",jar,"-export","script",work,Path.Combine(work,"map.swf")}) info.ArgumentList.Add(arg);
            using var process=Process.Start(info) ?? throw new InvalidOperationException("Cannot start map discovery.");
            using var stop=token.Register(()=>{try {if(!process.HasExited) process.Kill(true);}catch { }});
            var stdout=process.StandardOutput.ReadToEndAsync(token);var stderr=process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);await Task.WhenAll(stdout,stderr);
            if(process.ExitCode!=0) throw new InvalidOperationException("Map discovery could not decompile this map.");
            return Select(Directory.EnumerateFiles(work,"*.as",SearchOption.AllDirectories).SelectMany(f=>Parse(File.ReadAllText(f))),questId);
        } finally {try {Directory.Delete(work,true);} catch { }}
    }
    public static void Acquire(IScriptInterface bot,int questId,int itemId,string itemName,int quantity,bool temporary)
    {
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        bot.Log("Discovering map pickup ID for "+itemName+" from "+bot.Map.Name+"…");
        var task=Discover(bot.Map.FilePath,questId,cancellation.Token);
        while(!task.IsCompleted) {
            if(bot.ShouldExit || !bot.Player.LoggedIn || !bot.Quests.IsInProgress(questId)) {cancellation.Cancel();try {task.GetAwaiter().GetResult();} catch(OperationCanceledException) { } return;}
            Thread.Sleep(100);
        }
        int pickupId=task.GetAwaiter().GetResult();bot.Log("Verified map pickup #"+pickupId+"; acquiring "+itemName+".");
        bool Owned()=>temporary ? bot.TempInv.Contains(itemId,quantity) : bot.Inventory.Contains(itemId,quantity);
        for(int tries=0;tries<Math.Min(100,quantity+10) && !Owned();tries++) {
            if(bot.ShouldExit || !bot.Player.LoggedIn || !bot.Quests.IsInProgress(questId)) return;
            bot.Map.GetMapItem(pickupId);
            if(!temporary) bot.Drops.Pickup(itemId);
            for(int i=0;i<10 && !Owned() && !bot.ShouldExit;i++) Thread.Sleep(100);
        }
        if(!Owned()) throw new InvalidOperationException("Map pickup did not produce the required item ID/quantity: "+itemName+". Stopped without turning in.");
    }
}
