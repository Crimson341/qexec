using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Core.Scripts;

public class ScriptAccounts : IScriptAccounts
{
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly ISettingsService _settingsService;
    private IScriptPlayer Player => _lazyPlayer.Value;

    public ScriptAccounts(Lazy<IScriptPlayer> player, ISettingsService settingsService)
    {
        _lazyPlayer = player;
        _settingsService = settingsService;
    }

    public List<string> GetTags()
    {
        string? username = Player.Username;
        if (string.IsNullOrEmpty(username))
            return new List<string>();

        return GetTags(username);
    }

    public List<string> GetTags(string username)
    {
        var accounts = _settingsService.Get<Dictionary<string, AccountData>>("ManagedAccounts");
        if (accounts == null)
            return new List<string>();

        if (accounts.TryGetValue(username, out var accountData))
            return accountData.Tags.ToList();

        return new List<string>();
    }

    public bool HasTag(string tag)
    {
        string? username = Player.Username;
        if (string.IsNullOrEmpty(username))
            return false;

        return HasTag(username, tag);
    }

    public bool HasTag(string username, string tag)
    {
        var tags = GetTags(username);
        return tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    }

    public bool AddTag(string tag)
    {
        string? username = Player.Username;
        if (string.IsNullOrEmpty(username))
            return false;

        return AddTag(username, tag);
    }

    public bool AddTag(string username, string tag)
    {
        var accounts = _settingsService.Get<Dictionary<string, AccountData>>("ManagedAccounts");
        if (accounts == null)
            accounts = new Dictionary<string, AccountData>(StringComparer.OrdinalIgnoreCase);

        if (!accounts.TryGetValue(username, out var accountData))
            return false;

        if (accountData.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            return false;

        accountData.Tags.Add(tag);
        _settingsService.Set("ManagedAccounts", accounts);
        return true;
    }

    public bool RemoveTag(string tag)
    {
        string? username = Player.Username;
        if (string.IsNullOrEmpty(username))
            return false;

        return RemoveTag(username, tag);
    }

    public bool RemoveTag(string username, string tag)
    {
        var accounts = _settingsService.Get<Dictionary<string, AccountData>>("ManagedAccounts");
        if (accounts == null)
            return false;

        if (!accounts.TryGetValue(username, out var accountData))
            return false;

        var existingTag = accountData.Tags.FirstOrDefault(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (existingTag == null)
            return false;

        accountData.Tags.Remove(existingTag);
        _settingsService.Set("ManagedAccounts", accounts);
        return true;
    }

    public void SetTags(params string[] tags)
    {
        string? username = Player.Username;
        if (string.IsNullOrEmpty(username))
            return;

        SetTags(username, tags);
    }

    public bool SetTags(string username, params string[] tags)
    {
        var accounts = _settingsService.Get<Dictionary<string, AccountData>>("ManagedAccounts");
        if (accounts == null)
            accounts = new Dictionary<string, AccountData>(StringComparer.OrdinalIgnoreCase);

        if (!accounts.TryGetValue(username, out var accountData))
            return false;

        accountData.Tags = tags.ToList();
        _settingsService.Set("ManagedAccounts", accounts);
        return true;
    }

    public void ClearTags()
    {
        string? username = Player.Username;
        if (string.IsNullOrEmpty(username))
            return;

        ClearTags(username);
    }

    public bool ClearTags(string username)
    {
        var accounts = _settingsService.Get<Dictionary<string, AccountData>>("ManagedAccounts");
        if (accounts == null)
            return false;

        if (!accounts.TryGetValue(username, out var accountData))
            return false;

        accountData.Tags.Clear();
        _settingsService.Set("ManagedAccounts", accounts);
        return true;
    }
}
