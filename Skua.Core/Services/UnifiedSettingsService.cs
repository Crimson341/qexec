using Skua.Core.Models;
using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skua.Core.Services;

public class UnifiedSettingsService
{
    private readonly string _clientSettingsFile;
    private readonly string _managerSettingsFile;
    private readonly object _lock = new();
    private SettingsRoot _root = SettingsRoot.CreateDefaults();
    private AppRole _currentRole;
    private bool _initialized = false;
    private readonly Mutex _fileMutex;

    public UnifiedSettingsService()
    {
        _clientSettingsFile = Path.Combine(ClientFileSources.SkuaDIR, "ClientSettings.json");
        _managerSettingsFile = Path.Combine(ClientFileSources.SkuaDIR, "ManagerSettings.json");

        Directory.CreateDirectory(ClientFileSources.SkuaDIR);

        _fileMutex = new Mutex(false, @"Global\Skua.Settings.IO");
    }

    public void Initialize(AppRole role)
    {
        lock (_lock)
        {
            if (_initialized)
                return;

            _currentRole = role;

            try
            {
                _fileMutex.WaitOne();

                if (!File.Exists(ClientFileSources.SkuaSettingsDIR))
                {
                    if (!MigrateOldSettings())
                    {
                        _root = SettingsRoot.CreateDefaults();
                    }
                }
                else
                {
                    LoadSettings();
                }

                EnsureRoleDefaults();
                _initialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing settings: {ex.Message}");
                _root = SettingsRoot.CreateDefaults();
                _initialized = true;
            }
            finally
            {
                _fileMutex.ReleaseMutex();
            }
        }
    }

    public T? Get<T>(string key)
    {
        lock (_lock)
        {
            try
            {
                var clientProp = FindPropertyByJsonName(_root.Client.GetType(), key);
                if (clientProp?.GetValue(_root.Client) is object clientVal)
                    if (typeof(T).IsAssignableFrom(clientVal.GetType()))
                        return (T)clientVal;

                var managerProp = FindPropertyByJsonName(_root.Manager.GetType(), key);
                if (managerProp?.GetValue(_root.Manager) is object managerVal)
                    if (typeof(T).IsAssignableFrom(managerVal.GetType()))
                        return (T)managerVal;

                var sharedProp = FindPropertyByJsonName(_root.Shared.GetType(), key);
                if (sharedProp?.GetValue(_root.Shared) is object sharedVal)
                    if (typeof(T).IsAssignableFrom(sharedVal.GetType()))
                        return (T)sharedVal;
            }
            catch (Exception)
            {
            }

            return default;
        }
    }

    public T Get<T>(string key, T defaultValue)
    {
        T? value = Get<T>(key);
        return value is null || value.Equals(default(T)) ? defaultValue : value;
    }

    public void Set<T>(string key, T value)
    {
        lock (_lock)
        {
            try
            {
                var sharedProp = FindPropertyByJsonName(_root.Shared.GetType(), key);
                if (sharedProp != null)
                {
                    sharedProp.SetValue(_root.Shared, value);
                }
                else if (_currentRole == AppRole.Client)
                {
                    var clientProp = FindPropertyByJsonName(_root.Client.GetType(), key);
                    if (clientProp != null)
                        clientProp.SetValue(_root.Client, value);
                }
                else if (_currentRole == AppRole.Manager)
                {
                    var managerProp = FindPropertyByJsonName(_root.Manager.GetType(), key);
                    if (managerProp != null)
                        managerProp.SetValue(_root.Manager, value);
                }

                SaveSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting value for key '{key}': {ex.Message}");
            }
        }
    }

    public SharedSettings GetShared() => _root.Shared;

    public ClientSettings GetClient() => _root.Client;

    public ManagerSettings GetManager() => _root.Manager;

    private bool MigrateOldSettings()
    {
        try
        {
            bool clientExists = File.Exists(_clientSettingsFile);
            bool managerExists = File.Exists(_managerSettingsFile);

            if (!clientExists && !managerExists)
                return false;

            SettingsRoot newRoot = SettingsRoot.CreateDefaults();

            if (clientExists && managerExists)
            {
                var clientData = LoadOldFile(_clientSettingsFile);
                var managerData = LoadOldFile(_managerSettingsFile);

                MergeClientSettings(newRoot, clientData);
                MergeManagerSettings(newRoot, managerData);
                MergeSharedSettings(newRoot, managerData);
            }
            else if (clientExists)
            {
                var clientData = LoadOldFile(_clientSettingsFile);
                MergeClientSettings(newRoot, clientData);
                MergeSharedSettings(newRoot, clientData);
            }
            else
            {
                var managerData = LoadOldFile(_managerSettingsFile);
                MergeManagerSettings(newRoot, managerData);
                MergeSharedSettings(newRoot, managerData);
            }

            _root = newRoot;
            SaveSettings();

            string backupFile = ClientFileSources.SkuaSettingsDIR + ".bak";
            if (File.Exists(ClientFileSources.SkuaSettingsDIR) && !File.Exists(backupFile))
            {
                try { File.Copy(ClientFileSources.SkuaSettingsDIR, backupFile, overwrite: false); } catch { }
            }

            if (clientExists)
            {
                try { File.Delete(_clientSettingsFile); } catch { }
            }
            if (managerExists)
            {
                try { File.Delete(_managerSettingsFile); } catch { }
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Migration failed: {ex.Message}");
            return false;
        }
    }

    private Dictionary<string, object> LoadOldFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var options = GetJsonOptions();
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json, options) ?? new();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading old settings file {filePath}: {ex.Message}");
        }

        return new();
    }

    private void MergeClientSettings(SettingsRoot newRoot, Dictionary<string, object> oldData)
    {
        if (oldData.TryGetValue("AnimationFrameRate", out var val))
            if (int.TryParse(val?.ToString(), out int framerate))
                newRoot.Client.AnimationFrameRate = framerate;

        if (oldData.TryGetValue("CheckAdvanceSkillSetsUpdates", out val))
            if (bool.TryParse(val?.ToString(), out bool check))
                newRoot.Client.CheckAdvanceSkillSetsUpdates = check;

        if (oldData.TryGetValue("AutoUpdateAdvanceSkillSetsUpdates", out val))
            if (bool.TryParse(val?.ToString(), out bool autoUpdate))
                newRoot.Client.AutoUpdateAdvanceSkillSetsUpdates = autoUpdate;

        if (oldData.TryGetValue("AutoUpdateBotScripts", out val))
            if (bool.TryParse(val?.ToString(), out bool autoUpdateScripts))
                newRoot.Client.AutoUpdateBotScripts = autoUpdateScripts;

        if (oldData.TryGetValue("CheckBotScriptsUpdates", out val))
            if (bool.TryParse(val?.ToString(), out bool checkScripts))
                newRoot.Shared.CheckBotScriptsUpdates = checkScripts;

        if (oldData.TryGetValue("IgnoreGHAuth", out val))
            if (bool.TryParse(val?.ToString(), out bool ignoreAuth))
                newRoot.Client.IgnoreGHAuth = ignoreAuth;

        if (oldData.TryGetValue("UseLocalVSC", out val))
            if (bool.TryParse(val?.ToString(), out bool useLocal))
                newRoot.Client.UseLocalVSC = useLocal;

        if (oldData.TryGetValue("UserOptions", out val))
            newRoot.Client.UserOptions = ConvertToStringCollection(val);

        if (oldData.TryGetValue("FastTravels", out val))
            newRoot.Client.FastTravels = ConvertToStringCollection(val);

        if (oldData.TryGetValue("DefaultFastTravels", out val))
            newRoot.Client.DefaultFastTravels = ConvertToStringCollection(val);

        if (oldData.TryGetValue("HotKeys", out val))
            newRoot.Client.HotKeys = ConvertToStringCollection(val);

        if (oldData.TryGetValue("CustomBackgroundPath", out val))
            newRoot.Client.CustomBackgroundPath = val?.ToString() ?? string.Empty;

        if (oldData.TryGetValue("UpgradeRequired", out val))
            if (bool.TryParse(val?.ToString(), out bool upgrade))
                newRoot.Client.UpgradeRequired = upgrade;
    }

    private void MergeManagerSettings(SettingsRoot newRoot, Dictionary<string, object> oldData)
    {
        if (oldData.TryGetValue("CheckClientUpdates", out var val))
            if (bool.TryParse(val?.ToString(), out bool check))
                newRoot.Manager.CheckClientUpdates = check;

        if (oldData.TryGetValue("CheckClientPrereleases", out val))
            if (bool.TryParse(val?.ToString(), out bool checkPre))
                newRoot.Manager.CheckClientPrereleases = checkPre;

        if (oldData.TryGetValue("ClientDownloadPath", out val))
            newRoot.Manager.ClientDownloadPath = val?.ToString() ?? string.Empty;

        if (oldData.TryGetValue("DeleteZipFileAfter", out val))
            if (bool.TryParse(val?.ToString(), out bool deleteZip))
                newRoot.Manager.DeleteZipFileAfter = deleteZip;

        if (oldData.TryGetValue("ChangeLogActivated", out val))
            if (bool.TryParse(val?.ToString(), out bool changeLog))
                newRoot.Manager.ChangeLogActivated = changeLog;

        if (oldData.TryGetValue("syncTheme", out val))
            if (bool.TryParse(val?.ToString(), out bool syncTheme))
                newRoot.Manager.SyncTheme = syncTheme;

        if (oldData.TryGetValue("ManagedAccounts", out val))
            newRoot.Manager.ManagedAccounts = ConvertToStringCollection(val);
    }

    private void MergeSharedSettings(SettingsRoot newRoot, Dictionary<string, object> oldData)
    {
        if (oldData.TryGetValue("DefaultBackground", out var val))
            newRoot.Shared.DefaultBackground = val?.ToString() ?? "Generic2.swf";

        if (oldData.TryGetValue("UserThemes", out val))
            newRoot.Shared.UserThemes = ConvertToStringCollection(val);

        if (oldData.TryGetValue("DefaultThemes", out val))
            newRoot.Shared.DefaultThemes = ConvertToStringCollection(val);

        if (oldData.TryGetValue("CurrentTheme", out val))
            newRoot.Shared.CurrentTheme = val?.ToString() ?? "Skua,Dark,#FF607D8B,#FF607D8B,#FF000000,#FF000000,true,4.5,Medium,All";

        if (oldData.TryGetValue("UserGitHubToken", out val))
            newRoot.Shared.UserGitHubToken = val?.ToString() ?? string.Empty;

        if (oldData.TryGetValue("ApplicationVersion", out val))
            newRoot.Shared.ApplicationVersion = val?.ToString() ?? "1.3.0.0";

        if (oldData.TryGetValue("CheckBotScriptsUpdates", out val))
            if (bool.TryParse(val?.ToString(), out bool checkScripts))
                newRoot.Shared.CheckBotScriptsUpdates = checkScripts;
    }

    private System.Collections.Specialized.StringCollection ConvertToStringCollection(object? value)
    {
        var collection = new System.Collections.Specialized.StringCollection();

        if (value == null)
            return collection;

        if (value is StringCollection sc)
            return sc;

        if (value is System.Collections.Specialized.StringCollection syssc)
        {
            foreach (string item in syssc)
                collection.Add(item);
            return collection;
        }

        if (value is JsonElement je)
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(je.GetRawText());
                if (list != null)
                {
                    foreach (var item in list)
                        collection.Add(item);
                }
            }
            catch { }
            return collection;
        }

        if (value is IEnumerable<string> enumerable)
        {
            foreach (var item in enumerable)
                collection.Add(item);
            return collection;
        }

        return collection;
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(ClientFileSources.SkuaSettingsDIR))
            {
                string json = File.ReadAllText(ClientFileSources.SkuaSettingsDIR);
                var options = GetJsonOptions();
                var loaded = JsonSerializer.Deserialize<SettingsRoot>(json, options);

                if (loaded != null)
                {
                    _root = loaded;
                    if (_root.FormatVersion == 0)
                        _root.FormatVersion = 1;

                    _root.Shared ??= new SharedSettings();
                    _root.Client ??= new ClientSettings();
                    _root.Manager ??= new ManagerSettings();

                    _root.Shared.InitializeDefaults();
                    _root.Client.InitializeDefaults();
                    _root.Manager.InitializeDefaults();
                }
                else
                {
                    _root = SettingsRoot.CreateDefaults();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading settings: {ex.Message}");
            BackupCorruptFile();
            _root = SettingsRoot.CreateDefaults();
        }
    }

    private void SaveSettings()
    {
        try
        {
            _fileMutex.WaitOne();

            string tempFile = ClientFileSources.SkuaSettingsDIR + ".tmp";

            var options = GetJsonOptions();
            string json = JsonSerializer.Serialize(_root, options);
            File.WriteAllText(tempFile, json);

            if (File.Exists(ClientFileSources.SkuaSettingsDIR))
            {
                File.Delete(ClientFileSources.SkuaSettingsDIR);
            }

            File.Move(tempFile, ClientFileSources.SkuaSettingsDIR, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
        finally
        {
            _fileMutex.ReleaseMutex();
        }
    }

    private void EnsureRoleDefaults()
    {
        if (_currentRole == AppRole.Client)
        {
            _root.Client.InitializeDefaults();
        }
        else if (_currentRole == AppRole.Manager)
        {
            _root.Manager.InitializeDefaults();
        }

        _root.Shared.InitializeDefaults();
    }

    private void BackupCorruptFile()
    {
        try
        {
            if (File.Exists(ClientFileSources.SkuaSettingsDIR))
            {
                string backupPath = ClientFileSources.SkuaSettingsDIR + ".corrupt";
                File.Copy(ClientFileSources.SkuaSettingsDIR, backupPath, overwrite: true);
                File.Delete(ClientFileSources.SkuaSettingsDIR);
            }
        }
        catch { }
    }

    private JsonSerializerOptions GetJsonOptions()
    {
        var options = new JsonSerializerOptions()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new StringCollectionJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    private System.Reflection.PropertyInfo? FindPropertyByJsonName(Type type, string jsonName)
    {
        var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var prop in properties)
        {
            var jsonAttr = prop.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                .FirstOrDefault() as System.Text.Json.Serialization.JsonPropertyNameAttribute;

            if (jsonAttr != null && jsonAttr.Name == jsonName)
                return prop;
        }
        return null;
    }

    public void Dispose()
    {
        _fileMutex?.Dispose();
    }
}