using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Skua.Core.Models;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Westwind.Scripting;

namespace Skua.Core;

/// <summary>
/// Slightly modified compiler based on Westwind.Scripting (https://github.com/RickStrahl/Westwind.Scripting)
/// </summary>
public class Compiler : CSharpScriptExecution
{
    private const int _maxCachedAssemblies = 50;
    private static readonly string _cacheDirectory = Path.Combine(ClientFileSources.SkuaScriptsDIR, "Cached-Scripts");
    private static readonly TimeSpan _cacheExpiration = TimeSpan.FromDays(3);
    private static readonly TimeSpan _cleanupThrottle = TimeSpan.FromMinutes(5);
    private static DateTime _lastCleanupTime = DateTime.MinValue;
    private static readonly object _cleanupLock = new();

    /// <summary>
    /// This method compiles a class and hands back a
    /// dynamic reference to that class that you can
    /// call members on.
    ///
    /// Must have include parameterless ctor()
    /// </summary>
    /// <param name="code">Fully self-contained C# class</param>
    /// <param name="cacheHash">Optional hash for disk cache lookup</param>
    /// <param name="loadContext">Optional AssemblyLoadContext for loading the assembly</param>
    /// <param name="scriptName">Optional script name for cache file naming</param>
    /// <returns>Instance of that class or null</returns>
    public new dynamic? CompileClass(string code, int? cacheHash = null, ScriptLoadContext? loadContext = null, string? scriptName = null)
    {
        Type? type = CompileClassToType(code, cacheHash, loadContext, scriptName);
        if (type == null)
            return null;

        GeneratedClassName = type.Name;
        GeneratedNamespace = type.Namespace;

        return CreateInstance();
    }

    /// <summary>
    /// This method compiles a class and hands back a
    /// dynamic reference to that class that you can
    /// call members on.
    /// </summary>
    /// <param name="code">Fully self-contained C# class</param>
    /// <param name="cacheHash">Optional hash for disk cache lookup</param>
    /// <param name="loadContext">Optional AssemblyLoadContext for loading the assembly</param>
    /// <param name="scriptName">Optional script name for cache file naming</param>
    /// <returns>Instance of that class or null</returns>
    public new Type? CompileClassToType(string code, int? cacheHash = null, ScriptLoadContext? loadContext = null, string? scriptName = null)
    {
        int hash = cacheHash ?? code.GetHashCode();

        GeneratedClassCode = code;

        if (loadContext != null || !CachedAssemblies.ContainsKey(hash))
        {
            string? cachedAssemblyPath = TryLoadFromDiskCache(hash, scriptName);

            if (cachedAssemblyPath != null)
            {
                try
                {
                    if (loadContext != null)
                    {
                        using FileStream stream = File.OpenRead(cachedAssemblyPath);
                        Assembly = loadContext.LoadFromStream(stream);
                    }
                    else
                    {
                        Assembly = Assembly.LoadFrom(cachedAssemblyPath);
                    }
                }
                catch
                {
                    try
                    {
                        File.Delete(cachedAssemblyPath);
                    }
                    catch
                    {
                    }
                    cachedAssemblyPath = null;
                }
            }

            if (cachedAssemblyPath == null)
            {
                string diskCachePath = GetDiskCachePath(hash, scriptName);
                if (!CompileAssemblyToDisk(code, diskCachePath))
                    return null;

                if (loadContext != null)
                {
                    using FileStream stream = File.OpenRead(diskCachePath);
                    Assembly = loadContext.LoadFromStream(stream);
                }
                else
                {
                    Assembly = Assembly.LoadFrom(diskCachePath);
                }
            }

            if (loadContext == null)
            {
                if (CachedAssemblies.Count >= _maxCachedAssemblies)
                {
                    int oldestKey = CachedAssemblies.Keys.First();
                    CachedAssemblies.Remove(oldestKey, out _);
                }

                CachedAssemblies[hash] = Assembly;
            }
        }
        else
        {
            Assembly = CachedAssemblies[hash];
        }

        return Assembly.ExportedTypes.First();
    }

    /// <summary>
    /// <para>
    /// Compiles a class and creates an assembly from the compiled class.</para>
    /// <para>
    /// Assembly is stored on the `.Assembly` property. Use `noLoad()`
    /// to bypass loading of the assembly
    /// </para>
    /// <para>Must include parameterless ctor()</para>
    /// </summary>
    /// <param name="source">Source code</param>
    /// <param name="noLoad">if set doesn't load the assembly (useful only when OutputAssembly is set)</param>
    /// <returns></returns>
    public new bool CompileAssembly(string source, bool noLoad = false)
    {
        ClearErrors();

        SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(source.Trim());

        CSharpCompilation compilation = CSharpCompilation.Create(GeneratedClassName + ".cs")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                        optimizationLevel: OptimizationLevel.Release))
            .WithReferences(References)
            .AddSyntaxTrees(tree);

        if (SaveGeneratedCode)
            GeneratedClassCode = tree.ToString();

        bool isFileAssembly = false;
        Stream? codeStream = null;
        if (string.IsNullOrEmpty(OutputAssembly))
        {
            codeStream = new MemoryStream(); // in-memory assembly
        }
        else
        {
            codeStream = new FileStream(OutputAssembly, FileMode.Create, FileAccess.Write);
            isFileAssembly = true;
        }

        using (codeStream)
        {
            EmitResult? compilationResult = null;
            if (CompileWithDebug)
            {
                DebugInformationFormat debugOptions = CompileWithDebug ? DebugInformationFormat.Embedded : DebugInformationFormat.Pdb;
                compilationResult = compilation.Emit(codeStream, options: new EmitOptions(debugInformationFormat: debugOptions));
            }
            else
                compilationResult = compilation.Emit(codeStream);

            // Compilation Error handling
            if (!compilationResult.Success)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Diagnostic diag in
                    compilationResult.Diagnostics
                        .Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    sb.AppendLine(diag.ToString());
                }

                ErrorType = ExecutionErrorTypes.Compilation;
                ErrorMessage = sb.ToString();
                SetErrors(new ApplicationException(ErrorMessage));
                return false;
            }

            if (!noLoad)
            {
                Assembly = !isFileAssembly ? Assembly.Load(((MemoryStream)codeStream).ToArray()) : Assembly.LoadFrom(OutputAssembly);
            }
        }

        return true;
    }

    private void ClearErrors()
    {
        LastException = null;
        Error = false;
        ErrorMessage = null;
        ErrorType = ExecutionErrorTypes.None;
    }

    private void SetErrors(Exception ex)
    {
        Error = true;
        LastException = ex.GetBaseException();
        ErrorMessage = LastException.Message;

        if (ThrowExceptions)
            throw LastException;
    }

    /// <summary>
    /// Clears the cached assemblies to free memory
    /// </summary>
    public static void ClearAssemblyCache()
    {
        CachedAssemblies?.Clear();
    }

    private string? TryLoadFromDiskCache(int hash, string? scriptName = null)
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                return null;

            TryRunCleanup();

            string fileName = string.IsNullOrEmpty(scriptName) ? $"{hash}.dll" : $"{hash}-{scriptName}.dll";
            string cachedPath = Path.Combine(_cacheDirectory, fileName);

            if (File.Exists(cachedPath))
            {
                try
                {
                    AssemblyName.GetAssemblyName(cachedPath);
                    return cachedPath;
                }
                catch
                {
                    try
                    {
                        File.Delete(cachedPath);
                    }
                    catch
                    {
                    }
                    return null;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetDiskCachePath(int hash, string? scriptName = null)
    {
        if (!Directory.Exists(_cacheDirectory))
            Directory.CreateDirectory(_cacheDirectory);

        if (!string.IsNullOrEmpty(scriptName))
        {
            DeleteOldVersions(scriptName, hash);
        }

        string fileName = string.IsNullOrEmpty(scriptName) ? $"{hash}.dll" : $"{hash}-{scriptName}.dll";
        return Path.Combine(_cacheDirectory, fileName);
    }

    private bool CompileAssemblyToDisk(string source, string outputPath)
    {
        ClearErrors();

        SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(source.Trim());

        CSharpCompilation compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(outputPath))
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                        optimizationLevel: OptimizationLevel.Release))
            .WithReferences(References)
            .AddSyntaxTrees(tree);

        if (SaveGeneratedCode)
            GeneratedClassCode = tree.ToString();

        using FileStream codeStream = new(outputPath, FileMode.Create, FileAccess.Write);
        EmitResult? compilationResult = null;
        if (CompileWithDebug)
        {
            const DebugInformationFormat debugOptions = DebugInformationFormat.Embedded;
            compilationResult = compilation.Emit(codeStream, options: new EmitOptions(debugInformationFormat: debugOptions));
        }
        else
        {
            compilationResult = compilation.Emit(codeStream);
        }

        if (!compilationResult.Success)
        {
            StringBuilder sb = new();
            foreach (Diagnostic diag in
                compilationResult.Diagnostics
                    .Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error))
            {
                sb.AppendLine(diag.ToString());
            }

            ErrorType = ExecutionErrorTypes.Compilation;
            ErrorMessage = sb.ToString();
            SetErrors(new ApplicationException(ErrorMessage));
            return false;
        }

        return true;
    }

    private static void TryRunCleanup()
    {
        if ((DateTime.Now - _lastCleanupTime) < _cleanupThrottle)
            return;

        if (!Monitor.TryEnter(_cleanupLock))
            return;

        try
        {
            if ((DateTime.Now - _lastCleanupTime) < _cleanupThrottle)
                return;

            Task.Run(() => CleanupOldCachedAssemblies());
            _lastCleanupTime = DateTime.Now;
        }
        finally
        {
            Monitor.Exit(_cleanupLock);
        }
    }

    private static void CleanupOldCachedAssemblies()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                return;

            string[] filePaths = Directory.GetFiles(_cacheDirectory, "*.dll");
            List<FileInfo> files = new(filePaths.Length);

            foreach (string path in filePaths)
            {
                files.Add(new FileInfo(path));
            }

            DateTime now = DateTime.Now;
            HashSet<FileInfo> filesToDelete = new();

            foreach (FileInfo file in files)
            {
                if ((now - file.LastAccessTime) > _cacheExpiration)
                {
                    filesToDelete.Add(file);
                }
            }

            Dictionary<string, List<FileInfo>> scriptGroups = new();
            foreach (FileInfo file in files)
            {
                if (filesToDelete.Contains(file))
                    continue;

                string scriptName = GetScriptNameFromCacheFile(file.Name);
                if (!scriptGroups.ContainsKey(scriptName))
                {
                    scriptGroups[scriptName] = new List<FileInfo>();
                }
                scriptGroups[scriptName].Add(file);
            }

            foreach (List<FileInfo> group in scriptGroups.Values)
            {
                if (group.Count > 1)
                {
                    FileInfo newest = group.OrderByDescending(f => f.LastWriteTimeUtc).First();
                    foreach (FileInfo file in group)
                    {
                        if (file != newest)
                        {
                            filesToDelete.Add(file);
                        }
                    }
                }
            }

            if (files.Count - filesToDelete.Count >= _maxCachedAssemblies)
            {
                List<FileInfo> remaining = files.Except(filesToDelete).OrderBy(f => f.LastAccessTime).ToList();
                int toRemove = remaining.Count - _maxCachedAssemblies + 1;

                for (int i = 0; i < toRemove && i < remaining.Count; i++)
                {
                    filesToDelete.Add(remaining[i]);
                }
            }

            foreach (FileInfo file in filesToDelete)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static string GetScriptNameFromCacheFile(string fileName)
    {
        ReadOnlySpan<char> nameWithoutExt = Path.GetFileNameWithoutExtension(fileName.AsSpan());
        int firstDashIndex = nameWithoutExt.IndexOf('-');

        return firstDashIndex <= 0 ? string.Empty : new string(nameWithoutExt[(firstDashIndex + 1)..]);
    }

    private static void DeleteOldVersions(string scriptName, int currentHash)
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                return;

            foreach (string file in Directory.GetFiles(_cacheDirectory, $"*-{scriptName}.dll"))
            {
                string fileName = Path.GetFileName(file);
                string cachedScriptName = GetScriptNameFromCacheFile(fileName);

                if (cachedScriptName != scriptName)
                    continue;

                ReadOnlySpan<char> fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName.AsSpan());
                int dashIndex = fileNameWithoutExt.IndexOf('-');

                if (dashIndex > 0 && int.TryParse(fileNameWithoutExt[..dashIndex], out int fileHash) && fileHash != currentHash)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
    }
}
