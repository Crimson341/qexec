using System.Diagnostics;
using Newtonsoft.Json.Linq;
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
    public static void ValidateMapBytes(byte[] bytes) {
        if(bytes.Length<8 || bytes.Length>30_000_000 || bytes[1]!=(byte)'W' || bytes[2]!=(byte)'S' || bytes[0] is not ((byte)'F' or (byte)'C' or (byte)'Z'))
            throw new InvalidOperationException("The loaded map did not provide a valid SWF file.");
    }
    public static async Task<byte[]> CaptureMapBytes(Func<string,object[],string?> call,string map,string filePath,CancellationToken token) {
        var info=JObject.Parse(call("beginMapBytes",[])??"{}");
        int snapshot=(int?)info["token"]??0;
        try {
            int length=(int?)info["length"]??0;
            string expected=Path.GetFileName(filePath);
            if(snapshot<=0 || length<8 || length>30_000_000 || (string?)info["map"]!=map ||
               !Uri.TryCreate((string?)info["url"],UriKind.Absolute,out var url) || string.IsNullOrEmpty(expected) || !string.Equals(Path.GetFileName(url.AbsolutePath),expected,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Loaded SWF identity does not match this map. Wait for the map to finish loading and retry.");
            var bytes=new byte[length];
            for(int offset=0;offset<length;offset+=65536) {
                token.ThrowIfCancellationRequested();
                int count=Math.Min(65536,length-offset);
                string hex=call("readMapBytes",new object[]{snapshot,offset,count})??"";
                if(hex.Length!=count*2)throw new InvalidOperationException("Incomplete loaded-map data. Retry discovery.");
                Convert.FromHexString(hex).CopyTo(bytes,offset);
                await Task.Yield();
            }
            token.ThrowIfCancellationRequested();ValidateMapBytes(bytes);return bytes;
        } finally {if(snapshot>0) {try {call("endMapBytes",new object[]{snapshot});}catch { }}}
    }
    public static async Task<IReadOnlyList<string>> ReadMapScripts(IScriptInterface bot,CancellationToken token) {
        string map=bot.Map.Name,file=bot.Map.FilePath;
        byte[] bytes=await CaptureMapBytes((name,args)=>bot.Flash.Call(name,args),map,file,token);
        if(bot.Map.Name!=map || bot.Map.FilePath!=file)throw new InvalidOperationException("Map changed during discovery. Retry in the new area.");
        return await DecompileMapBytes(bytes,token);
    }
    public static async Task<IReadOnlyList<string>> DecompileMapBytes(byte[] bytes,CancellationToken token)
    {
        ValidateMapBytes(bytes);
        string jar=Path.Combine(AppContext.BaseDirectory,"FFDec","ffdec.jar");
        if(!File.Exists(jar)) throw new InvalidOperationException("Map discovery tools are missing from this installation.");
        string java=new[]{Path.Combine(AppContext.BaseDirectory,"jre","bin","java"),"/opt/homebrew/opt/openjdk/bin/java","/usr/local/opt/openjdk/bin/java"}.FirstOrDefault(File.Exists) ?? "java";
        string work=Path.Combine(Path.GetTempPath(),"qexec-map-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        try {
            await File.WriteAllBytesAsync(Path.Combine(work,"map.swf"),bytes,token);
            var info=new ProcessStartInfo(java){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
            foreach(var arg in new[]{"-Djava.awt.headless=true","-jar",jar,"-export","script",work,Path.Combine(work,"map.swf")}) info.ArgumentList.Add(arg);
            using var process=Process.Start(info) ?? throw new InvalidOperationException("Cannot start map discovery.");
            using var stop=token.Register(()=>{try {if(!process.HasExited) process.Kill(true);}catch { }});
            var stdout=process.StandardOutput.ReadToEndAsync(token);var stderr=process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);await Task.WhenAll(stdout,stderr);
            if(process.ExitCode!=0) throw new InvalidOperationException("Map discovery could not decompile this map.");
            return Directory.EnumerateFiles(work,"*.as",SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();
        } finally {try {Directory.Delete(work,true);} catch { }}
    }
    public static void Acquire(IScriptInterface bot,int questId,int itemId,string itemName,int quantity,bool temporary)
    {
        bool Owned()=>temporary ? bot.TempInv.Contains(itemId,quantity) : bot.Inventory.Contains(itemId,quantity);
        if(Owned())return;
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        bot.Log("Discovering map pickup ID for "+itemName+" from "+bot.Map.Name+"…");
        var task=ReadMapScripts(bot,cancellation.Token);
        while(!task.IsCompleted) {
            if(Owned()) {cancellation.Cancel();try {task.GetAwaiter().GetResult();}catch(OperationCanceledException) { }return;}
            if(bot.ShouldExit || !bot.Player.LoggedIn || !bot.Quests.IsInProgress(questId)) {cancellation.Cancel();try {task.GetAwaiter().GetResult();} catch(OperationCanceledException) { } return;}
            Thread.Sleep(100);
        }
        int pickupId=Select(task.GetAwaiter().GetResult().SelectMany(Parse),questId);bot.Log("Verified map pickup #"+pickupId+"; acquiring "+itemName+".");
        AcquireKnown(bot,questId,itemId,itemName,quantity,temporary,pickupId);
    }
    public static void AcquireKnown(IScriptInterface bot,int questId,int itemId,string itemName,int quantity,bool temporary,int pickupId)
    {
        if(pickupId<=0 || itemId<=0 || quantity<=0)throw new ArgumentException("Invalid pickup objective.");
        bool Owned()=>temporary ? bot.TempInv.Contains(itemId,quantity) : bot.Inventory.Contains(itemId,quantity);
        if(Owned())return;
        string map=bot.Map.Name;
        var cells=bot.Map.Cells.Where(c=>Regex.IsMatch(c,@"^(?:r|room|field)\d+$",RegexOptions.IgnoreCase)).Distinct().ToArray();
        for(int tries=0;tries<Math.Min(100,Math.Max(quantity+10,cells.Length*2)) && !Owned();tries++) {
            if(bot.ShouldExit || !bot.Player.LoggedIn || !bot.Quests.IsInProgress(questId)) return;
            if(bot.Map.Name!=map)throw new InvalidOperationException("Map changed while collecting pickups.");
            if(tries>0 && cells.Length>0) {string cell=cells[(tries-1)%cells.Length];bot.Log("Looking for "+itemName+" in "+cell+"…");bot.Map.Jump(cell,"Left");}
            bot.Map.GetMapItem(pickupId);
            if(!temporary) bot.Drops.Pickup(itemId);
            for(int i=0;i<10 && !Owned() && !bot.ShouldExit;i++) Thread.Sleep(100);
        }
        if(!Owned()) throw new InvalidOperationException("Map pickup did not produce the required item ID/quantity: "+itemName+". Stopped without turning in.");
    }
}
