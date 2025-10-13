using Skua.Core.Interfaces;
using System.Text.Json;
using System.Collections.Specialized;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System;
using System.IO;

namespace Skua.App.WPF.Services;

public class StringCollectionConverter : JsonConverter<StringCollection>
{
    public override StringCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StringCollection collection = new();
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    string? value = reader.GetString();
                    if (value != null)
                        collection.Add(value);
                }
            }
        }
        return collection;
    }

    public override void Write(Utf8JsonWriter writer, StringCollection value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string? item in value)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }
}

public class SettingsService : ISettingsService
{
    private readonly string _settingsFile;
    private readonly string _managerSettingsFile;
    private Dictionary<string, object> _settings;
    private readonly object _lock = new();

    public SettingsService()
    {
        string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skua");
        _settingsFile = Path.Combine(settingsPath, "ClientSettings.json");

        string managerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skua");
        Directory.CreateDirectory(managerPath);
        _managerSettingsFile = Path.Combine(managerPath, "ManagerSettings.json");

        Directory.CreateDirectory(settingsPath);
        _settings = LoadSettings();

        InitializeDefaults();
    }

    public T? Get<T>(string key)
    {
        if (IsSharedSetting(key))
        {
            return GetFromManagerSettings<T>(key);
        }

        lock (_lock)
        {
            try
            {
                if (_settings.TryGetValue(key, out object? value))
                {
                    if (value is JsonElement jsonElement)
                    {
                        return JsonSerializer.Deserialize<T>(jsonElement.GetRawText(), GetJsonOptions());
                    }

                    if (typeof(T).IsAssignableFrom(value.GetType()))
                    {
                        return (T)value;
                    }

                    if (typeof(T) == typeof(StringCollection))
                    {
                        if (value is StringCollection stringCollection)
                        {
                            return (T)(object)stringCollection;
                        }
                        if (value is IEnumerable<string> stringList)
                        {
                            StringCollection newCollection = new();
                            foreach (string item in stringList)
                            {
                                newCollection.Add(item);
                            }
                            return (T)(object)newCollection;
                        }
                    }

                    try
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    catch
                    {
                    }
                }
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
        if (IsSharedSetting(key))
        {
            SetInManagerSettings(key, value);
            return;
        }

        lock (_lock)
        {
            _settings[key] = value;
            SaveSettings();
        }
    }

    private JsonSerializerOptions GetJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new StringCollectionConverter());
        return options;
    }

    private Dictionary<string, object> LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFile))
            {
                string json = File.ReadAllText(_settingsFile);
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json, GetJsonOptions()) ?? new Dictionary<string, object>();
            }
        }
        catch (Exception)
        {
        }
        return new Dictionary<string, object>();
    }

    private void SaveSettings()
    {
        try
        {
            string json = JsonSerializer.Serialize(_settings, GetJsonOptions());
            File.WriteAllText(_settingsFile, json);
        }
        catch (Exception)
        {
        }
    }

    private bool IsSharedSetting(string key)
    {
        if (key is "DefaultBackground" or "UserThemes" or "DefaultThemes" or "CurrentTheme" or "UserGitHubToken" or "ApplicationVersion")
            return true;

        return false;
    }

    private T? GetFromManagerSettings<T>(string key)
    {
        try
        {
            if (!File.Exists(_managerSettingsFile))
            {
                return default;
            }

            string json = File.ReadAllText(_managerSettingsFile);
            Dictionary<string, JsonElement>? managerSettings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, GetJsonOptions());

            if (managerSettings != null && managerSettings.TryGetValue(key, out JsonElement value))
            {
                // Handle StringCollection specially
                if (typeof(T) == typeof(StringCollection))
                {
                    List<string> strings = JsonSerializer.Deserialize<List<string>>(value.GetRawText(), GetJsonOptions()) ?? new List<string>();
                    StringCollection collection = new();
                    collection.AddRange(strings.ToArray());
                    return (T)(object)collection;
                }

                return JsonSerializer.Deserialize<T>(value.GetRawText(), GetJsonOptions());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading shared setting '{key}' from manager: {ex.Message}");
        }

        return default;
    }

    private void SetInManagerSettings<T>(string key, T value)
    {
        try
        {
            Dictionary<string, object> managerSettings;

            if (File.Exists(_managerSettingsFile))
            {
                string json = File.ReadAllText(_managerSettingsFile);
                managerSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(json, GetJsonOptions()) ?? new Dictionary<string, object>();
            }
            else
            {
                managerSettings = new Dictionary<string, object>();
            }

            managerSettings[key] = value;

            string updatedJson = JsonSerializer.Serialize(managerSettings, GetJsonOptions());
            File.WriteAllText(_managerSettingsFile, updatedJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing shared setting '{key}' to manager: {ex.Message}");
        }
    }

    private void InitializeDefaults()
    {
        InitializeSharedDefaults();

        if (!_settings.ContainsKey("AnimationFrameRate"))
        {
            _settings["AnimationFrameRate"] = 30;
        }

        if (!_settings.ContainsKey("CheckAdvanceSkillSetsUpdates"))
        {
            _settings["CheckAdvanceSkillSetsUpdates"] = true;
        }

        if (!_settings.ContainsKey("AutoUpdateAdvanceSkillSetsUpdates"))
        {
            _settings["AutoUpdateAdvanceSkillSetsUpdates"] = true;
        }

        if (!_settings.ContainsKey("AutoUpdateBotScripts"))
        {
            _settings["AutoUpdateBotScripts"] = true;
        }

        if (!_settings.ContainsKey("CheckBotScriptsUpdates"))
        {
            _settings["CheckBotScriptsUpdates"] = true;
        }

        if (!_settings.ContainsKey("IgnoreGHAuth"))
        {
            _settings["IgnoreGHAuth"] = false;
        }

        if (!_settings.ContainsKey("UseLocalVSC"))
        {
            _settings["UseLocalVSC"] = true;
        }

        if (!_settings.ContainsKey("UserOptions"))
        {
            _settings["UserOptions"] = new StringCollection();
        }

        if (!_settings.ContainsKey("FastTravels"))
        {
            StringCollection fastTravels = new()
            {
                "Tercessuinotlim,tercessuinotlim,Enter,Spawn", "Nulgath,tercessuinotlim,Boss2,Right",
                "VHL & Taro,tercessuinotlim,Taro,Left"
            };
            _settings["FastTravels"] = fastTravels;
        }

        if (!_settings.ContainsKey("DefaultFastTravels"))
        {
            StringCollection fastTravels = new()
            {
                "Tercessuinotlim,tercessuinotlim,Enter,Spawn", "Nulgath,tercessuinotlim,Boss2,Right",
                "VHL & Taro,tercessuinotlim,Taro,Left"
            };
            _settings["DefaultFastTravels"] = fastTravels;
        }

        if (!_settings.ContainsKey("HotKeys"))
        {
            StringCollection hotKeys = new()
            {
                "ToggleScript|F10",
                "LoadScript|F9",
                "OpenBank|F2",
                "OpenConsole|F3",
                "ToggleAutoAttack|F4",
                "ToggleAutoHunt|F5"
            };
            _settings["HotKeys"] = hotKeys;
        }

        if (!_settings.ContainsKey("CustomBackgroundPath"))
        {
            _settings["CustomBackgroundPath"] = string.Empty;
        }

        if (!_settings.ContainsKey("UpgradeRequired"))
        {
            _settings["UpgradeRequired"] = true;
        }
    }

    private void InitializeSharedDefaults()
    {
        string[] sharedKeys = { "DefaultBackground", "UserThemes", "DefaultThemes", "CurrentTheme", "UserGitHubToken", "ApplicationVersion" };
        bool removedAny = false;

        foreach (string key in sharedKeys)
        {
            lock (_lock)
            {
                if (_settings.ContainsKey(key))
                {
                    _settings.Remove(key);
                    removedAny = true;
                }
            }
        }

        if (removedAny)
        {
            SaveSettings();
        }
    }
}