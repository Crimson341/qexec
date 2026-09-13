using System.Globalization;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Skua.Core.Flash;
using Skua.Core.Interfaces;

namespace Skua.Mac;

public sealed class MacFlash(Rpc rpc, Lazy<IScriptManager> manager) : IFlashUtil
{
    public event FlashCallHandler? FlashCall;
    public bool Ready { get; set; }
    public void InitializeFlash() => rpc.Send(new { type = "reload" });
    public void Emit(string name, object[] args) => FlashCall?.Invoke(name, args);
    public string? Call(string function, params object[] args) => Call<string>(function, args);
    public T? Call<T>(string function, params object[] args)
    {
        object? result = Call(function, typeof(T), args);
        return result is null ? default : (T)result;
    }
    public object? Call(string function, Type type, params object[] args)
    {
        // Cancellation stops script work, but cleanup must still restore the game view.
        if (Thread.CurrentThread.Name == "Script Thread" &&
            manager.Value is not Skua.Core.Scripts.ScriptManager { IsCleaningUp: true })
            manager.Value.ScriptCts?.Token.ThrowIfCancellationRequested();
        if (!Ready) throw new InvalidOperationException("The game renderer is not ready.");
        JToken value = rpc.Request("flash", new { function, args });
        if (value.Type is JTokenType.Null or JTokenType.Undefined)
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        return value.ToObject(type);
    }
    public object FromFlashXml(XElement element) => FlashXml.Decode(element)!;
    public IFlashObject<T> CreateFlashObject<T>(string path) => new FlashObject<T>(Call<int>("lnkCreate", path), this);
    public void Dispose() { Ready = false; FlashCall = null; }
}
