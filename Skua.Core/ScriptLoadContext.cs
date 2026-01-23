using Skua.Core.Models;
using System.Reflection;
using System.Runtime.Loader;

namespace Skua.Core;

public class ScriptLoadContext : AssemblyLoadContext
{
    private static readonly string _cacheDirectory = Path.Combine(ClientFileSources.SkuaScriptsDIR, "Cached-Scripts");

    public ScriptLoadContext() : base(isCollectible: true)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == null)
            return null;

        if (!Directory.Exists(_cacheDirectory))
            return null;

        if (Unloading != null)
            return null;

        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch
        {
        }

        if (Unloading != null)
            return null;

        string[] matchingFiles = Directory.GetFiles(_cacheDirectory, $"*-{assemblyName.Name}.dll");

        if (matchingFiles.Length > 0)
        {
            string latestFile = matchingFiles.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
            using FileStream stream = File.OpenRead(latestFile);
            return LoadFromStream(stream);
        }

        return null;
    }
}