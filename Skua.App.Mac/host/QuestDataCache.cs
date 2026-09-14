using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace Skua.Mac;

internal static class QuestDataCache
{
    static readonly ConcurrentDictionary<string, (DateTime Written, JArray Data)> Cache = new(StringComparer.Ordinal);

    public static JArray Load(string path)
    {
        var written = File.GetLastWriteTimeUtc(path);
        if (Cache.TryGetValue(path, out var hit) && hit.Written == written)
            return hit.Data;
        var data = JArray.Parse(File.ReadAllText(path));
        Cache[path] = (written, data);
        return data;
    }
}
