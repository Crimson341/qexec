using System.Diagnostics;
using System.Threading.Channels;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Skua.Core;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Mac;

string? diagnosticRoot = null;
if ((args.Contains("--self-test") || args.Contains("--compile-check")) && Environment.GetEnvironmentVariable("SKUA_DATA_DIR") is null)
{
    diagnosticRoot = Path.Combine(Path.GetTempPath(), "skua-selftest-" + Guid.NewGuid().ToString("N"));
    Environment.SetEnvironmentVariable("SKUA_DATA_DIR", diagnosticRoot);
}
using var protocolOutput = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
using var protocolInput = new StreamReader(Console.OpenStandardInput(), System.Text.Encoding.UTF8);
Console.SetOut(Console.Error); // A script's Console.WriteLine must not corrupt the protocol.
using var rpc = new Rpc(protocolOutput);
try
{
    foreach (var path in new[] { ClientFileSources.SkuaDIR, ClientFileSources.SkuaScriptsDIR,
        ClientFileSources.SkuaOptionsDIR, ClientFileSources.SkuaPluginsDIR, ClientFileSources.SkuaThemesDIR })
        Directory.CreateDirectory(path);
    foreach (string file in new[] { "AdvancedSkills.json", "QuestData.json" })
    {
        string destination = Path.Combine(ClientFileSources.SkuaDIR, file);
        string source = Path.Combine(AppContext.BaseDirectory, "defaults", file);
        if (!File.Exists(destination) && File.Exists(source))
        {
            try { File.Copy(source, destination); }
            catch (IOException) when (File.Exists(destination)) { /* Another client installed the default first. */ }
        }
    }

    var services = new ServiceCollection().AddCommonServices().AddCompiler().AddScriptableObjects();
    services.AddSingleton(rpc);
    services.AddSingleton<ISettingsService, MacSettings>();
    services.AddSingleton<IDialogService, MacDialogs>();
    services.AddSingleton<IDispatcherService, MacDispatcher>();
    services.AddSingleton<IClipboardService, MacClipboard>();
    services.AddSingleton<ISoundService, MacSound>();
    services.AddSingleton<ILogService, MacLog>();
    services.AddSingleton<MacFlash>();
    services.AddSingleton<IFlashUtil>(s => s.GetRequiredService<MacFlash>());
    await using var provider = services.BuildServiceProvider();
    Ioc.Default.ConfigureServices(provider);
    Console.Error.WriteLine("Initializing Flash transport.");
    var flash = provider.GetRequiredService<MacFlash>();
    Console.Error.WriteLine("Initializing script manager and settings.");
    var manager = provider.GetRequiredService<IScriptManager>();
    Console.Error.WriteLine("Initializing game API.");
    var bot = provider.GetRequiredService<IScriptInterface>();
    Console.Error.WriteLine("Game API initialized.");
    Trace.Listeners.Add(new Skua.Core.Services.DebugListener(provider.GetRequiredService<ILogService>()));
    var lifetime = new object();
    StrongReferenceMessenger.Default.Register<object, ScriptStoppedMessage, int>(lifetime,
        (int)MessageChannels.ScriptStatus, (_, _) => rpc.Send(new { type = "status", running = false }));
    StrongReferenceMessenger.Default.Register<object, ScriptErrorMessage, int>(lifetime,
        (int)MessageChannels.ScriptStatus, (_, _) => rpc.Send(new { type = "log", kind = "Error", message = "Script failed. See the diagnostic log." }));

    if(args.Contains("--active-quest-check")) {
        var code=ActiveQuestMaker.Generate(42,-1,new[]{new Skua.Core.Models.Items.ItemBase{ID=123,Name="Test Fang",Temp=true,Quantity=3}},new[]{new GearDrop("forest","Wolf","Test Fang",true,"fixture")},(_,_)=>null);
        manager.SetLoadedScript(Path.Combine(ClientFileSources.SkuaScriptsDIR,"GeneratedQuestCheck.cs"));
        var compiled=manager.Compile(code);
        if(compiled?.GetType().Name!="GeneratedAcceptedQuest") throw new InvalidOperationException("Quest script failed to compile.");
        rpc.Send(new{type="active-quest-check",success=true});return;
    }
    int routeCheck = Array.IndexOf(args,"--route-check");
    if(routeCheck>=0 && routeCheck+1<args.Length) {
        var finder=new GearFinder(ClientFileSources.SkuaScriptsDIR);
        var result=JObject.FromObject(await finder.Lookup(args[routeCheck+1],routeCheck+2<args.Length && int.TryParse(args[routeCheck+2],out int checkId) ? checkId : 0));
        foreach(var plan in (JArray)result["sources"]!) {
            manager.SetLoadedScript(Path.Combine(ClientFileSources.SkuaScriptsDIR,"GeneratedGearCheck.cs"));
            var compiledRoute=manager.Compile((string)plan["Code"]!);
            if(compiledRoute==null || !compiledRoute.GetType().Name.StartsWith("Generated")) throw new InvalidOperationException("Generated route entry point was not compiled.");
            if(args.Contains("--save")) plan["SavedPath"]=finder.Resolve((string)plan["Id"]!);
            plan["Code"]="Compiled successfully: "+compiledRoute.GetType().Name;
        }
        rpc.Send(result);return;
    }
    int identityCheck = Array.IndexOf(args,"--identity-check");
    if (identityCheck >= 0 && identityCheck+1 < args.Length)
    {
        var data = JObject.FromObject(new {player=args[identityCheck+1],items=new[]{new{slot="ar",id=74195,name=""},new{slot="Weapon",id=99764,name=""}}});
        await new GearIdentity(ClientFileSources.SkuaQuestsFile).Resolve(data);
        rpc.Send(data); return;
    }
    if (args.Contains("--catalog-check"))
    {
        var catalog = new QuestCatalog(ClientFileSources.SkuaQuestsFile,new GearFinder(ClientFileSources.SkuaScriptsDIR));
        var empty = Array.Empty<Skua.Core.Models.Items.InventoryItem>();
        var result = JObject.FromObject(catalog.Query("{}",empty,empty,true));
        rpc.Send(new { type="catalog-check",total=(int?)result["total"],matches=(int?)result["matches"] });
        return;
    }
    int gearCheck = Array.IndexOf(args, "--gear-plan-check");
    if (gearCheck >= 0)
    {
        if (gearCheck + 1 >= args.Length) throw new ArgumentException("--gear-plan-check requires an item name.");
        var finder = new GearFinder(ClientFileSources.SkuaScriptsDIR);
        var plans = finder.Find(args[gearCheck + 1]);
        if (plans.Count == 0) throw new InvalidOperationException("No supported farming plan.");
        // Compile without executing any game action or persisting a generated script.
        manager.SetLoadedScript(Path.Combine(ClientFileSources.SkuaScriptsDIR,"GeneratedGearCheck.cs"));
        var compiled = manager.Compile(plans[0].Code);
        if (compiled == null) throw new InvalidOperationException("Generated script did not compile.");
        rpc.Send(new { type = "gear-plan-check", success = true, description = plans[0].Description });
        return;
    }

    int scriptCheck = Array.IndexOf(args, "--script-check");
    if (scriptCheck >= 0)
    {
        if (scriptCheck + 1 >= args.Length) throw new ArgumentException("--script-check requires a C# source path.");
        string sourcePath = Path.GetFullPath(args[scriptCheck + 1]);
        manager.SetLoadedScript(sourcePath);
        var compiled = manager.Compile(File.ReadAllText(sourcePath));
        if (compiled == null) throw new InvalidOperationException("Script did not compile.");
        rpc.Send(new { type = "script-check", name = compiled.GetType().Name, success = true });
        return;
    }

    int compileCheck = Array.IndexOf(args, "--compile-check");
    if (compileCheck >= 0)
    {
        if (compileCheck + 1 >= args.Length) throw new ArgumentException("--compile-check requires a C# source path.");
        var compiler = provider.GetRequiredService<Compiler>();
        Type? compiled = compiler.CompileClassToType(File.ReadAllText(args[compileCheck + 1]));
        if (compiler.Error || compiled is null) throw new Exception(compiler.ErrorMessage);
        rpc.Send(new { type = "compile-check", name = compiled.Name, success = true });
        return;
    }
    if (args.Contains("--self-test"))
    {
        var compiler = provider.GetRequiredService<Compiler>();
        dynamic instance = compiler.CompileClass("public class Probe { public string Run() => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(); }")!;
        if (compiler.Error) throw new Exception(compiler.ErrorMessage);
        rpc.Send(new { type = "self-test", architecture = (string)instance.Run(), core = bot.GetType().FullName, success = true });
        return;
    }

    var adaptive = new AdaptiveCombat(bot, provider.GetRequiredService<IAdvancedSkillContainer>(), rpc);
    var gearFinder = new GearFinder(ClientFileSources.SkuaScriptsDIR);
    var activeQuestMaker = new ActiveQuestMaker(bot,gearFinder,ClientFileSources.SkuaScriptsDIR);
    var gearIdentity = new GearIdentity(ClientFileSources.SkuaQuestsFile);
    var gearOwnership = new GearOwnership(bot, flash);
    var questPlanner = new QuestPlanner(bot, ClientFileSources.SkuaScriptsDIR, gearOwnership);
    var questCatalog = new QuestCatalog(ClientFileSources.SkuaQuestsFile, gearFinder);
    using var catalogGate = new SemaphoreSlim(1);
    using var gearLookupGate = new SemaphoreSlim(1);
    bool gameReady = false;
    var eventQueue = Channel.CreateBounded<JObject>(new BoundedChannelOptions(1024) { SingleReader = true, SingleWriter = true });
    _ = Task.Run(async () =>
    {
        await foreach (var message in eventQueue.Reader.ReadAllAsync())
        {
            try
            {
                string name = (string)message["name"]!;
                if (name == "requestLoadGame")
                {
                    Volatile.Write(ref gameReady, false);
                    flash.Ready = true;
                    flash.Call("loadClient");
                }
                else
                {
                    object[] values = message["args"]!.Select(v => v.Type == JTokenType.String ? (object)v.Value<string>()! : v.ToObject<object>()!).ToArray();
                    flash.Emit(name, values);
                    if (name == "loaded")
                    {
                        Volatile.Write(ref gameReady, true);
                        rpc.Send(new { type = "game-ready" });
                    }
                }
            }
            catch (Exception ex) { rpc.Send(new { type = "log", kind = "Error", message = ex.GetBaseException().Message }); }
        }
    });
    using var commandGate = new SemaphoreSlim(1);
    string? watchedQuestScript = null;
    int watchedQuestId = 0;
    async Task Command(JObject message)
    {
        if ((string?)message["command"] == "active-quests")
        {
            try {
                rpc.Send(activeQuestMaker.Snapshot());
                if(manager.ScriptRunning && manager.LoadedScript==watchedQuestScript && bot.Player.Playing && !bot.Quests.IsInProgress(watchedQuestId)) {
                    await commandGate.WaitAsync();
                    try {
                        if(manager.ScriptRunning && manager.LoadedScript==watchedQuestScript && !bot.Quests.IsInProgress(watchedQuestId)) {
                            await manager.StopScript();
                            rpc.Send(new {type="active-quest-error",message="Selected quest is no longer active. Auto-do stopped."});
                        }
                    } finally {commandGate.Release();}
                }
            }
            catch(Exception ex) {rpc.Send(new {type="active-quests-error",message=ex.GetBaseException().Message});}
            return;
        }
        if ((string?)message["command"] == "gear-find")
        {
            await gearLookupGate.WaitAsync();
            try {
                string value=(string?)message["value"] ?? "";
                JObject request=value.StartsWith("{") ? JObject.Parse(value) : new JObject { ["name"]=value };
                rpc.Send(await gearFinder.Lookup((string?)request["name"] ?? "",Math.Max(0,(int?)request["id"] ?? 0)));
            } catch(Exception ex) { rpc.Send(new {type="gear-error",message=ex.GetBaseException().Message}); }
            finally { gearLookupGate.Release(); }
            return;
        }
        if ((string?)message["command"] == "quest-catalog")
        {
            await catalogGate.WaitAsync();
            try {
                if (!bot.Player.LoggedIn) throw new InvalidOperationException("Log in before checking catalog ownership.");
                bool bankLoaded = await gearOwnership.LoadBank();
                var inventory = bot.Inventory.Items.ToList();
                var bank = bankLoaded ? bot.Bank.Items.ToList() : new List<Skua.Core.Models.Items.InventoryItem>();
                var result = await Task.Run(() => questCatalog.Query((string?)message["value"] ?? "",inventory,bank,bankLoaded));
                rpc.Send(result);
            } catch (Exception ex) { rpc.Send(new { type="quest-catalog-error", message=ex.GetBaseException().Message }); }
            finally { catalogGate.Release(); }
            return;
        }
        if ((string?)message["command"] == "quest-refresh")
        {
            var news = QuestPlanner.News();
            try { rpc.Send(await questPlanner.Scan()); }
            catch (Exception ex) { rpc.Send(new { type = "quest-error", message = ex.GetBaseException().Message }); }
            rpc.Send(await news);
            return;
        }
        // Read-only web resolution must not hold the script start/stop gate.
        if ((string?)message["command"] == "gear-inspect")
        {
            try {
                var equippedGear = JObject.Parse(flash.Call("inspectGear") ?? "{}");
                await gearOwnership.Annotate(equippedGear);
                await gearIdentity.Resolve(equippedGear);
                rpc.Send(new { type = "gear-player", data = equippedGear });
            }
            catch (Exception ex) { rpc.Send(new { type = "gear-error", message = ex.GetBaseException().Message }); }
            return;
        }
        await commandGate.WaitAsync();
        try
        {
            switch ((string?)message["command"])
            {
                case "active-quest-go":
                    if(manager.ScriptRunning || adaptive.Enabled) throw new InvalidOperationException("Stop the current script or auto attack first.");
                    if(!gameReady || !bot.Player.LoggedIn) throw new InvalidOperationException("Log in first.");
                    var questRequest=JObject.Parse((string?)message["value"] ?? "{}");
                    if(!bot.Quests.CanComplete((int?)questRequest["id"] ?? 0) && !await gearOwnership.LoadBank()) throw new InvalidOperationException("Bank could not be checked. Retry before generating a quest farm.");
                    string activeScript=activeQuestMaker.Create((int?)questRequest["id"] ?? 0,(int?)questRequest["reward"] ?? -1);
                    watchedQuestScript=activeScript; watchedQuestId=(int?)questRequest["id"] ?? 0;
                    manager.SetLoadedScript(activeScript);
                    rpc.Send(new {type="selected",path=activeScript});
                    rpc.Send(new {type="active-quest-starting",path=activeScript});
                    var activeError=await manager.StartScript(); if(activeError!=null) throw activeError;
                    rpc.Send(new {type="status",running=manager.ScriptRunning});
                    break;
                case "quest-go":
                    if (manager.ScriptRunning || adaptive.Enabled) throw new InvalidOperationException("Stop the current script or auto attack first.");
                    if (!Volatile.Read(ref gameReady) || !bot.Player.LoggedIn) throw new InvalidOperationException("Log in before questing.");
                    string questScript = await questPlanner.Resolve((string?)message["value"] ?? "");
                    manager.SetLoadedScript(questScript);
                    rpc.Send(new { type = "selected", path = questScript });
                    Exception? questError = await manager.StartScript();
                    if (questError != null) throw questError;
                    rpc.Send(new { type = "quest-started", running = manager.ScriptRunning });
                    rpc.Send(new { type = "status", running = manager.ScriptRunning });
                    break;
                case "gear-go":
                    if (manager.ScriptRunning || adaptive.Enabled) throw new InvalidOperationException("Stop the current script or auto attack first.");
                    if (!Volatile.Read(ref gameReady) || !bot.Player.LoggedIn) throw new InvalidOperationException("Log in before farming.");
                    var selectedSource=gearFinder.SourceFor((string?)message["value"] ?? "");
                    if(selectedSource.RequiresMissing) await gearOwnership.RequireMissing(selectedSource.Item,selectedSource.ItemId);
                    string gearScript = gearFinder.Resolve((string?)message["value"] ?? "");
                    manager.SetLoadedScript(gearScript);
                    rpc.Send(new { type = "selected", path = gearScript });
                    rpc.Send(new { type="gear-starting",message="Running generated script: "+selectedSource.Name });
                    Exception? gearError = await manager.StartScript();
                    if (gearError != null) throw gearError;
                    rpc.Send(new { type = "status", running = manager.ScriptRunning });
                    break;
                case "load":
                    if (manager.ScriptRunning) throw new InvalidOperationException("Stop the current script first.");
                    string path = Path.GetFullPath((string)message["path"]!);
                    if (Path.GetExtension(path) != ".cs" || !File.Exists(path)) throw new ArgumentException("Select an existing C# script.");
                    manager.SetLoadedScript(path);
                    rpc.Send(new { type = "selected", path });
                    break;
                case "auto-start":
                    if (!Volatile.Read(ref gameReady)) throw new InvalidOperationException("Wait for the game to load.");
                    if (manager.ScriptRunning) throw new InvalidOperationException("Stop the script before enabling auto attack.");
                    adaptive.Start();
                    break;
                case "auto-stop":
                    await adaptive.Stop();
                    break;
                case "start":
                    if (adaptive.Enabled) throw new InvalidOperationException("Turn off auto attack before running a script.");
                    if (!Volatile.Read(ref gameReady)) throw new InvalidOperationException("Wait for AQW to finish loading.");
                    if (string.IsNullOrEmpty(manager.LoadedScript)) throw new InvalidOperationException("Choose a script first.");
                    Exception? error = await manager.StartScript();
                    if (error is not null) throw error;
                    rpc.Send(new { type = "status", running = manager.ScriptRunning });
                    break;
                case "stop":
                    await manager.StopScript();
                    rpc.Send(new { type = "status", running = manager.ScriptRunning });
                    break;
                default: throw new ArgumentException("Unknown host command.");
            }
        }
        catch (Exception ex) {
            rpc.Send(new { type = "log", kind = "Error", message = ex.GetBaseException().Message });
            if (((string?)message["command"])?.StartsWith("active-quest") == true)
                rpc.Send(new {type="active-quest-error",message=ex.GetBaseException().Message});
            if (((string?)message["command"])?.StartsWith("quest-") == true)
                rpc.Send(new { type = "quest-error", message = ex.GetBaseException().Message });
            if (((string?)message["command"])?.StartsWith("gear-") == true)
                rpc.Send(new { type = "gear-error", message = ex.GetBaseException().Message });
        }
        finally { commandGate.Release(); }
    }
    rpc.Send(new { type = "ready", architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), scripts = ClientFileSources.SkuaScriptsDIR });
    while (await protocolInput.ReadLineAsync() is { } line)
    {
        if (line.Length > 8 * 1024 * 1024) throw new InvalidDataException("Protocol message exceeds limit.");
        var message = JObject.Parse(line);
        switch ((string?)message["type"])
        {
            case "reply": rpc.Reply(message); break;
            case "event":
                if (!eventQueue.Writer.TryWrite(message)) throw new IOException("Game event queue overflow.");
                break;
            case "command": _ = Task.Run(() => Command(message)); break;
        }
    }
    rpc.Dispose();
    manager.ScriptCts?.Cancel();
    eventQueue.Writer.TryComplete();
    StrongReferenceMessenger.Default.UnregisterAll(lifetime);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
finally
{
    if (diagnosticRoot is not null && Directory.Exists(diagnosticRoot)) Directory.Delete(diagnosticRoot, true);
    Environment.Exit(Environment.ExitCode);
}
