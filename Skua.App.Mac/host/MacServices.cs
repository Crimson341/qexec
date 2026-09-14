using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.ViewModels;

namespace Skua.Mac;

public sealed class MacSettings : Skua.Core.Services.UnifiedSettingsService, ISettingsService
{
    public MacSettings()
    {
        Initialize(AppRole.Client);
        SetApplicationVersion();
    }
}

public sealed class MacLog(Rpc rpc) : ILogService
{
    private readonly ConcurrentDictionary<LogType, ConcurrentQueue<string>> logs = new();
    private void Add(LogType kind, string message)
    {
        var queue = logs.GetOrAdd(kind, _ => new());
        queue.Enqueue(message);
        while (queue.Count > 1000) queue.TryDequeue(out _);
        rpc.Send(new { type = "log", kind = kind.ToString(), message });
        if (kind == LogType.Script && message.StartsWith("Quest step:", StringComparison.Ordinal))
            rpc.Send(new { type = "active-quest-progress", message });
    }
    public void DebugLog(string message) => Add(LogType.Debug, message);
    public void ScriptLog(string message) => Add(LogType.Script, message);
    public void FlashLog(string message) => Add(LogType.Flash, message);
    public void ClearLog(LogType logType) => logs.GetOrAdd(logType, _ => new()).Clear();
    public List<string> GetLogs(LogType logType) => logs.GetOrAdd(logType, _ => new()).ToList();
}

public sealed class MacDialogs(Rpc rpc) : IDialogService
{
    public bool? ShowDialog<T>(T model) where T : class => ShowDialog(model, _ => { });
    public bool? ShowDialog<T>(T model, string title) where T : class => ShowDialog(model);
    public bool? ShowDialog<T>(T model, Action<T> callback) where T : class
    {
        if (model is not OptionContainerViewModel options)
            throw new NotSupportedException($"The Mac port does not yet support the {typeof(T).Name} dialog.");
        var fields = options.Options.Select((o, i) => new { id = i, name = o.Option.DisplayName, description = o.Option.Description,
            value = o.Type.IsEnum ? o.SelectedValue : o.Value?.ToString(), choices = o.EnumValues, boolean = o.Type == typeof(bool) });
        JToken result = rpc.Request("options", new { fields }, TimeSpan.FromHours(1));
        if (result.Type == JTokenType.Null) throw new OperationCanceledException("Script configuration was cancelled.");
        foreach (var field in result.Children<JObject>())
        {
            var option = options.Options[(int)field["id"]!];
            string value = (string)field["value"]!;
            if (option.Type.IsEnum)
            {
                if (!option.EnumValues!.Contains(value)) throw new ArgumentException("Invalid option selection.");
                option.SelectedValue = value;
            }
            else option.Value = Convert.ChangeType(value, option.Type);
        }
        callback(model);
        return true;
    }
    public void ShowMessageBox(string message, string caption) => ShowMessageBox(message, caption, "OK");
    public bool? ShowMessageBox(string message, string caption, bool yesAndNo)
    {
        var result = ShowMessageBox(message, caption, yesAndNo ? new[] { "Yes", "No" } : new[] { "OK" });
        return result.Value < 0 ? null : result.Value == 0;
    }
    public DialogResult ShowMessageBox(string message, string caption, params string[] buttons)
    {
        int index = rpc.Request("dialog", new { message, caption, buttons }, TimeSpan.FromHours(1)).Value<int>();
        return index < 0 || index >= buttons.Length ? DialogResult.Cancelled : new(buttons[index], index);
    }
}

public sealed class MacDispatcher : IDispatcherService { public void Invoke(Action action) => action(); }
public sealed class MacSound(Rpc rpc) : ISoundService
{
    public void Beep() => rpc.Send(new { type = "beep" });
    public void Beep(int frequency, int duration) => Beep();
}
public sealed class MacClipboard(Rpc rpc) : IClipboardService
{
    public string GetText() => rpc.Request("clipboard-read", new { }).Value<string>()!;
    public void SetText(string text) => rpc.Request("clipboard-write", new { text });
    public void SetData(string format, object data) => throw new NotSupportedException("Only text clipboard data is supported on Mac.");
    public object GetData(string format) => throw new NotSupportedException("Only text clipboard data is supported on Mac.");
}
