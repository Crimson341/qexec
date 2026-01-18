using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Messaging;
using Skua.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Skua.Core.Scripts;

public partial class ScriptManager : ObservableObject, IScriptManager, IDisposable
{
    private static readonly Regex _versionRegex = new(@"^/\*[\s\S]*?version:\s*(\d+\.\d+\.\d+\.\d+)[\s\S]*?\*/", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly string _skuaDIR = ClientFileSources.SkuaDIR;
    private static readonly string _cacheScriptsDir = Path.Combine(ClientFileSources.SkuaScriptsDIR, "Cached-Scripts");
    public ScriptManager(
        ILogService logger,
        Lazy<IScriptInterface> scriptInterface,
        Lazy<IScriptHandlers> handlers,
        Lazy<IScriptSkill> skills,
        Lazy<IScriptDrop> drops,
        Lazy<IScriptWait> wait,
        Lazy<IAuraMonitorService> auraMonitorService)
    {
        _lazyBot = scriptInterface;
        _lazyHandlers = handlers;
        _lazySkills = skills;
        _lazyDrops = drops;
        _lazyWait = wait;
        _lazyAuraMonitor = auraMonitorService;
        _logger = logger;
    }

    private readonly Lazy<IScriptInterface> _lazyBot;
    private readonly Lazy<IScriptHandlers> _lazyHandlers;
    private readonly Lazy<IScriptSkill> _lazySkills;
    private readonly Lazy<IScriptDrop> _lazyDrops;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IAuraMonitorService> _lazyAuraMonitor;
    private readonly ILogService _logger;

    private IScriptHandlers Handlers => _lazyHandlers.Value;
    private IScriptSkill Skills => _lazySkills.Value;
    private IScriptDrop Drops => _lazyDrops.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IAuraMonitorService AuraMonitor => _lazyAuraMonitor.Value;

    private Thread _currentScriptThread;
    private readonly object _threadLock = new();
    private bool _stoppedByScript;
    private bool _runScriptStoppingBool;
    private readonly Dictionary<string, bool> _configured = new();
    private readonly List<string> _refCache = new();
    private readonly List<string> _includedFiles = new();
    private ScriptLoadContext? _currentLoadContext;

    [ObservableProperty]
    private bool _scriptRunning = false;

    [ObservableProperty]
    private string _loadedScript = string.Empty;

    [ObservableProperty]
    private string _compiledScript = string.Empty;

    public IScriptOptionContainer? Config { get; set; }

    public CancellationTokenSource? ScriptCts { get; private set; }

    public bool ShouldExit => ScriptCts?.IsCancellationRequested ?? false;

    public async Task<Exception?> StartScript()
    {
        if (ScriptRunning)
        {
            _logger.ScriptLog("Script already running.");
            return new Exception("Script already running.");
        }

        try
        {
            await _lazyBot.Value.Auto.StopAsync();

            UnloadPreviousScript();

            string scriptContent = File.ReadAllText(LoadedScript);
            object? script = await Task.Run(() => Compile(scriptContent));

            LoadScriptConfig(script);
            bool needsConfig = _configured.TryGetValue(Config!.Storage, out bool b) && !b;
            ManualResetEventSlim scriptReady = new(false);

            Handlers.Clear();
            _runScriptStoppingBool = false;

            _currentScriptThread = new(() =>
            {
                Exception? exception = null;
                ScriptCts = new();
                scriptReady.Set();

                try
                {
                    script?.GetType().GetMethod("ScriptMain")?.Invoke(script, new object[] { _lazyBot.Value });
                }
                catch (Exception e)
                {
                    Exception actualException = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;

                    if ((actualException is not OperationCanceledException || !_stoppedByScript) && (e is not TargetInvocationException || !_stoppedByScript))
                    {
                        exception = e;
                        Trace.WriteLine($"Error while running script:\r\nMessage: {(e.InnerException is not null ? e.InnerException.Message : e.Message)}\r\nStackTrace: {(e.InnerException is not null ? e.InnerException.StackTrace : e.StackTrace)}");

                        StrongReferenceMessenger.Default.Send<ScriptErrorMessage, int>(new(e), (int)MessageChannels.ScriptStatus);
                        _runScriptStoppingBool = true;
                    }
                }
                finally
                {
                    _stoppedByScript = false;
                    if (_runScriptStoppingBool)
                    {
                        StrongReferenceMessenger.Default.Send<ScriptStoppingMessage, int>((int)MessageChannels.ScriptStatus);
                        try
                        {
                            switch (Task.Run(async () => await StrongReferenceMessenger.Default.Send<ScriptStoppingRequestMessage, int>(new(exception), (int)MessageChannels.ScriptStatus)).GetAwaiter().GetResult())
                            {
                                case true:
                                    Trace.WriteLine("Script finished successfully.");
                                    break;

                                case false:
                                    Trace.WriteLine("Script finished early or with errors.");
                                    break;

                                default:
                                    break;
                            }
                        }
                        catch { }
                    }

                    script = null;
                    Skills.Stop();
                    Drops.Stop();

                    AuraMonitor.StopMonitoring();
                    UnloadPreviousScript();
                    ScriptCts?.Dispose();
                    ScriptCts = null;
                    StrongReferenceMessenger.Default.Send<ScriptStoppedMessage, int>((int)MessageChannels.ScriptStatus);
                    ScriptRunning = false;
                }
            })
            {
                Name = "Script Thread",
                IsBackground = true
            };

            lock (_threadLock)
            {
                _currentScriptThread.Start();
                ScriptRunning = true;
            }

            StrongReferenceMessenger.Default.Send<ScriptStartedMessage, int>((int)MessageChannels.ScriptStatus);

            if (needsConfig)
            {
                _ = Task.Run(() =>
                {
                    scriptReady.Wait();
                    Config!.Configure();
                    _configured[Config!.Storage] = true;
                });
            }

            return null;
        }
        catch (Exception e)
        {
            ScriptRunning = false;
            return e;
        }
    }

    public async Task RestartScriptAsync()
    {
        Trace.WriteLine("Restarting script");
        await StopScript(false);
        await Task.Run(async () =>
        {
            await Task.Delay(5000);
            await StartScript();
        });
    }

    public async ValueTask StopScript(bool runScriptStoppingEvent = true)
    {
        _runScriptStoppingBool = runScriptStoppingEvent;
        _stoppedByScript = true;
        ScriptCts?.Cancel();

        if (Thread.CurrentThread.Name == "Script Thread")
        {
            ScriptCts?.Token.ThrowIfCancellationRequested();
            return;
        }

        await Wait.ForTrueAsync(() => ScriptCts == null, 30).ConfigureAwait(false);

        Thread? thread;
        lock (_threadLock)
        {
            thread = _currentScriptThread;
        }

        if (thread.IsAlive)
        {
            await Task.Run(() =>
            {
                if (!thread.Join(TimeSpan.FromSeconds(5)))
                {
                    _logger?.ScriptLog("Script thread did not exit within timeout.");
                }
            }).ConfigureAwait(false);
        }

        OnPropertyChanged(nameof(ScriptRunning));
    }

    [RequiresUnreferencedCode("This method may require code that cannot be statically analyzed for trimming. Use with caution.")]
    public object? Compile(string source)
    {
        CheckScriptVersionRequirement(source);

        Stopwatch sw = Stopwatch.StartNew();
        _includedFiles.Clear();
        HashSet<string> references = GetReferences();
        string final = ProcessSources(source, ref references);

        ScriptLoadContext loadContext = new();
        _currentLoadContext = loadContext;

        List<string> compiledIncludes = CompileIncludedFiles(references, loadContext);
        references.UnionWith(compiledIncludes);

        int cacheHash = ComputeCacheHash(final, _includedFiles);
        CompiledScript = final;
        string scriptName = Path.GetFileNameWithoutExtension(LoadedScript);

        Compiler compiler = Ioc.Default.GetRequiredService<Compiler>();
        compiler.AddDefaultReferencesAndNamespaces();
        compiler.AllowReferencesInCode = true;

        if (references.Count > 0)
            compiler.AddAssemblies(references.ToArray());

        dynamic? assembly = compiler.CompileClass(final, cacheHash, loadContext, scriptName);

        sw.Stop();
        Trace.WriteLine($"Script compilation took {sw.ElapsedMilliseconds}ms.");

        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);

        return compiler.Error
            ? throw new ScriptCompileException(compiler.ErrorMessage, compiler.GeneratedClassCodeWithLineNumbers)
            : (object?)assembly;
    }

    private HashSet<string> GetReferences()
    {
        HashSet<string> references = new();
        if (_refCache.Count == 0 && Directory.Exists(ClientFileSources.SkuaPluginsDIR))
        {
            foreach (string file in Directory.EnumerateFiles(ClientFileSources.SkuaPluginsDIR, "*.dll"))
            {
                string path = Path.Combine(ClientFileSources.SkuaDIR, file);
                if (CanLoadAssembly(path))
                {
                    _refCache.Add(path);
                    references.Add(path);
                }
            }
        }
        else
        {
            references.UnionWith(_refCache);
        }

        return references;
    }

    private string ProcessSources(string source, ref HashSet<string> references)
    {
        Span<Range> lineRanges = stackalloc Range[256];
        int lineCount = source.AsSpan().Split(lineRanges, '\n');
        if (lineCount > lineRanges.Length)
        {
            lineRanges = new Range[lineCount];
            lineCount = source.AsSpan().Split(lineRanges, '\n');
        }

        List<string> linesToRemove = new();
        ReadOnlySpan<char> sourceSpan = source.AsSpan();

        for (int i = 0; i < lineCount; i++)
        {
            ReadOnlySpan<char> line = sourceSpan[lineRanges[i]].Trim();

            if (line.StartsWith("using"))
                break;

            if (!line.StartsWith("//cs_"))
                continue;

            string lineStr = new(line);
            string[] parts = lineStr.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            string cmd = parts[0][5..];
            switch (cmd)
            {
                case "ref":
                    string local = Path.Combine(_skuaDIR, parts[1]);
                    if (File.Exists(local))
                        references.Add(local);
                    else if (File.Exists(parts[1]))
                        references.Add(parts[1]);
                    break;

                case "include":
                    string localSource = Path.Combine(_skuaDIR, parts[1]);
                    if (File.Exists(localSource))
                        _includedFiles.Add(localSource);
                    else if (File.Exists(parts[1]))
                        _includedFiles.Add(parts[1]);
                    break;
            }
            linesToRemove.Add(lineStr);
        }

        if (linesToRemove.Count == 0)
            return source.Trim();

        StringBuilder sb = new(source);
        foreach (string line in linesToRemove)
        {
            sb.Replace(line + "\n", "");
            sb.Replace(line + "\r\n", "");
            sb.Replace(line, "");
        }
        return sb.ToString().Trim();
    }

    public void LoadScriptConfig(object? script)
    {
        if (script is null)
            return;

        IScriptOptionContainer opts = Config = Ioc.Default.GetRequiredService<IScriptOptionContainer>();
        Type t = script.GetType();
        FieldInfo? storageField = t.GetField("OptionsStorage");
        FieldInfo? optsField = t.GetField("Options");
        FieldInfo? multiOptsField = t.GetField("MultiOptions");
        FieldInfo? dontPreconfField = t.GetField("DontPreconfigure");
        if (multiOptsField is not null)
        {
            string[] optFieldNames = (string[])multiOptsField.GetValue(script)!;
            List<FieldInfo> multiOpts = new(optFieldNames.Length);
            foreach (string optField in optFieldNames)
            {
                FieldInfo? field = t.GetField(optField);
                if (field != null)
                    multiOpts.Add(field);
            }
            foreach (FieldInfo opt in multiOpts)
            {
                List<IOption> parsedOpt = (List<IOption>)opt.GetValue(script)!;
                parsedOpt.ForEach(o => o.Category = opt.Name.Replace('_', ' '));
                opts.MultipleOptions.Add(opt.Name, parsedOpt);
            }
        }
        if (optsField is not null)
            opts.Options.AddRange((List<IOption>)optsField.GetValue(script)!);
        if (storageField is not null)
            opts.Storage = (string)storageField.GetValue(script)!;
        if (dontPreconfField is not null)
            _configured[opts.Storage] = (bool)dontPreconfField.GetValue(script)!;
        else if (optsField is not null)
            _configured[opts.Storage] = false;

        opts.SetDefaults();
        opts.Load();
    }

    private static bool CanLoadAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int ComputeCacheHash(string source, List<string> includedFiles)
    {
        using var sha256 = SHA256.Create();
        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
        byte[] sourceHash = sha256.ComputeHash(sourceBytes);
        using var ms = new MemoryStream();
        ms.Write(sourceHash, 0, sourceHash.Length);

        foreach (string file in includedFiles.OrderBy(f => f))
        {
            if (File.Exists(file))
            {
                byte[] pathBytes = Encoding.UTF8.GetBytes(file);
                ms.Write(pathBytes, 0, pathBytes.Length);

                long ticks = File.GetLastWriteTimeUtc(file).Ticks;
                byte[] ticksBytes = BitConverter.GetBytes(ticks);
                ms.Write(ticksBytes, 0, ticksBytes.Length);
            }
        }

        byte[] combinedHash = sha256.ComputeHash(ms.ToArray());
        return BitConverter.ToInt32(combinedHash, 0);
    }

    private void CheckScriptVersionRequirement(string source)
    {
        Match match = _versionRegex.Match(source);
        if (match.Success)
        {
            string requiredVersionStr = match.Groups[1].Value;
            Version? currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

            if (Version.TryParse(requiredVersionStr, out Version? requiredVersion) && currentVersion != null && currentVersion < requiredVersion)
            {
                throw new ScriptVersionException(requiredVersionStr, currentVersion.ToString());
            }
        }
    }

    private List<string> CompileIncludedFiles(HashSet<string> references, ScriptLoadContext loadContext)
    {
        ConcurrentDictionary<string, string> compiledPaths = new();
        object lockObj = new();

        // Build dependency graph and precompute cache info in parallel
        ConcurrentDictionary<string, List<string>> dependencyGraph = new();
        ConcurrentDictionary<string, (string source, string fileName, int hash, string cachePath)> fileInfoCache = new();

        string cacheDir = _cacheScriptsDir;

        ConcurrentBag<string> validCachedFiles = new();

        Parallel.ForEach(_includedFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, includedFile =>
        {
            string includeSource = File.ReadAllText(includedFile);
            CheckScriptVersionRequirement(includeSource);
            List<string> deps = ExtractIncludeDependencies(includeSource);
            dependencyGraph[includedFile] = deps;

            string includeFileName = Path.GetFileNameWithoutExtension(includedFile);
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(includeSource));
            int includeHash = BitConverter.ToInt32(hashBytes, 0);
            string compiledPath = Path.Combine(cacheDir, $"{includeHash}-{includeFileName}.dll");

            if (File.Exists(compiledPath))
            {
                try
                {
                    AssemblyName.GetAssemblyName(compiledPath);
                    validCachedFiles.Add(includedFile);
                    compiledPaths[includedFile] = compiledPath;
                    fileInfoCache[includedFile] = (string.Empty, includeFileName, includeHash, compiledPath);
                    return;
                }
                catch
                {
                    try
                    {
                        File.Delete(compiledPath);
                    }
                    catch
                    {
                    }
                }
            }

            fileInfoCache[includedFile] = (includeSource, includeFileName, includeHash, compiledPath);
        });

        HashSet<string> processed = new(validCachedFiles);
        HashSet<string> includedFilesSet = new(_includedFiles);

        while (processed.Count < _includedFiles.Count)
        {
            List<string> readyToCompile = new();
            foreach (string file in _includedFiles)
            {
                if (processed.Contains(file))
                    continue;

                List<string> deps = dependencyGraph[file];
                bool allDepsReady = true;
                foreach (string dep in deps)
                {
                    if (includedFilesSet.Contains(dep) && !processed.Contains(dep))
                    {
                        allDepsReady = false;
                        break;
                    }
                }

                if (allDepsReady)
                    readyToCompile.Add(file);
            }

            if (readyToCompile.Count == 0)
                break;

            Parallel.ForEach(readyToCompile, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
            {
                if (!validCachedFiles.Contains(file))
                {
                    CompileIncludeRecursive(file, references, loadContext, compiledPaths, lockObj, fileInfoCache);
                }
            });

            foreach (string file in readyToCompile)
                processed.Add(file);
        }

        fileInfoCache.Clear();

        return compiledPaths.Values.ToList();
    }


    private void CompileIncludeRecursive(
        string includedFile,
        HashSet<string> references,
        ScriptLoadContext loadContext,
        ConcurrentDictionary<string, string> compiledPaths,
        object lockObj,
        ConcurrentDictionary<string, (string source, string fileName, int hash, string cachePath)> fileInfoCache)
    {
        if (compiledPaths.ContainsKey(includedFile))
            return;

        try
        {
            var info = fileInfoCache[includedFile];
            string includeSource = info.source;
            string includeFileName = info.fileName;
            int includeHash = info.hash;
            string compiledPath = info.cachePath;

            HashSet<string> includeReferences = new(references);
            lock (lockObj)
            {
                includeReferences.UnionWith(compiledPaths.Values);
            }

            string processedInclude = ProcessIncludeDirectives(includeSource, ref includeReferences);

            Compiler includeCompiler = Ioc.Default.GetRequiredService<Compiler>();
            includeCompiler.AddDefaultReferencesAndNamespaces();
            includeCompiler.AllowReferencesInCode = true;

            if (includeReferences.Count > 0)
                includeCompiler.AddAssemblies(includeReferences.ToArray());

            dynamic? assembly = includeCompiler.CompileClass(processedInclude, includeHash, loadContext, includeFileName);

            if (includeCompiler.Error)
            {
                throw new ScriptCompileException(
                    $"Error compiling included file '{includedFile}':\n{includeCompiler.ErrorMessage}",
                    includeCompiler.GeneratedClassCodeWithLineNumbers);
            }

            if (File.Exists(compiledPath))
            {
                compiledPaths[includedFile] = compiledPath;
            }
        }
        catch (Exception ex) when (ex is not ScriptCompileException)
        {
            throw new ScriptCompileException($"Failed to compile included file '{includedFile}': {ex.Message}", string.Empty);
        }
    }

    private List<string> ExtractIncludeDependencies(string source)
    {
        List<string> dependencies = new();
        ReadOnlySpan<char> sourceSpan = source.AsSpan();

        int start = 0;
        int newlinePos;
        while ((newlinePos = sourceSpan[start..].IndexOf('\n')) >= 0)
        {
            ReadOnlySpan<char> line = sourceSpan[start..(start + newlinePos)].Trim();

            if (line.StartsWith("using"))
                break;

            if (line.StartsWith("//cs_include "))
            {
                string lineStr = new(line);
                string[] parts = lineStr.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string includePath = parts[1];
                    string localPath = Path.Combine(ClientFileSources.SkuaDIR, includePath);
                    if (File.Exists(localPath))
                        dependencies.Add(localPath);
                    else if (File.Exists(includePath))
                        dependencies.Add(includePath);
                }
            }

            start += newlinePos + 1;
        }

        return dependencies;
    }


    private string ProcessIncludeDirectives(string source, ref HashSet<string> references)
    {
        List<string> linesToRemove = new();
        ReadOnlySpan<char> sourceSpan = source.AsSpan();

        int start = 0;
        int newlinePos;
        while ((newlinePos = sourceSpan[start..].IndexOf('\n')) >= 0)
        {
            ReadOnlySpan<char> line = sourceSpan[start..(start + newlinePos)].Trim();

            if (line.StartsWith("using"))
                break;

            if (!line.StartsWith("//cs_"))
            {
                start += newlinePos + 1;
                continue;
            }

            string lineStr = new(line);
            string[] parts = lineStr.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                start += newlinePos + 1;
                continue;
            }

            string cmd = parts[0][5..];
            if (cmd == "ref")
            {
                string local = Path.Combine(ClientFileSources.SkuaDIR, parts[1]);
                if (File.Exists(local))
                    references.Add(local);
                else if (File.Exists(parts[1]))
                    references.Add(parts[1]);
            }

            linesToRemove.Add(new string(sourceSpan[start..(start + newlinePos + 1)]));
            start += newlinePos + 1;
        }

        if (linesToRemove.Count == 0)
            return source.Trim();

        StringBuilder sb = new(source);
        foreach (string lineToRemove in linesToRemove)
        {
            sb.Replace(lineToRemove, "");
        }
        return sb.ToString().Trim();
    }

    private void UnloadPreviousScript()
    {
        ScriptLoadContext? context = _currentLoadContext;
        _currentLoadContext = null;

        if (context is null)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                var weak = new WeakReference(context);
                context.Unload();

                for (int i = 0; i < 3 && weak.IsAlive; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(50);
                }
            }
            catch
            {
            }
        });
    }

    public void SetLoadedScript(string path)
    {
        LoadedScript = path;
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_threadLock)
        {
            thread = _currentScriptThread;
        }

        if (thread?.IsAlive == true)
        {
            ScriptCts?.Cancel();
            if (!thread.Join(TimeSpan.FromSeconds(5)))
            {
                _logger?.ScriptLog("Script thread did not exit during disposal.");
            }
        }
        ScriptCts?.Dispose();
    }
}