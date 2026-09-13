using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Skua.Mac;

// Only the parent process has access to these anonymous pipes. No listening socket.
public sealed class Rpc : IDisposable
{
    private readonly TextWriter output;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JToken>> pending = new();
    private long sequence;
    private bool closed;
    public Rpc(TextWriter output) => this.output = output;
    public void Send(object value)
    {
        lock (output)
        {
            if (closed) throw new IOException("Desktop host disconnected.");
            output.WriteLine(JsonConvert.SerializeObject(value));
            output.Flush();
        }
    }
    public JToken Request(string kind, object data, TimeSpan? timeout = null)
    {
        long id = Interlocked.Increment(ref sequence);
        var source = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = source;
        try
        {
            Send(new { type = "request", id, kind, data });
            return source.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        }
        finally { pending.TryRemove(id, out _); }
    }
    public void Reply(JObject message)
    {
        if (!pending.TryRemove((long)message["id"]!, out var source)) return;
        if (message["error"]?.Type == JTokenType.String)
            source.TrySetException(new InvalidOperationException((string)message["error"]!));
        else source.TrySetResult(message["value"] ?? JValue.CreateNull());
    }
    public void Dispose()
    {
        lock (output) closed = true;
        foreach (var source in pending.Values)
            source.TrySetException(new IOException("Desktop host disconnected."));
        pending.Clear();
    }
}
