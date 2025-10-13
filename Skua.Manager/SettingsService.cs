using Skua.Core.Interfaces;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System;
using System.Collections.Specialized;
using System.Text.Json.Serialization;

namespace Skua.Manager;

public class StringCollectionConverter : JsonConverter<StringCollection>
{
    public override StringCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StringCollection collection = new StringCollection();
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
    private Dictionary<string, object> _settings;
    private readonly object _lock = new();

    public SettingsService()
    {
        string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skua");
        _settingsFile = Path.Combine(settingsPath, "ManagerSettings.json");

        Directory.CreateDirectory(settingsPath);
        _settings = LoadSettings();

        InitializeDefaults();
    }

    public T? Get<T>(string key)
    {
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

                    if (value is T value1)
                    {
                        return value1;
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
            return default(T);
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

    private void InitializeDefaults()
    {
        InitializeSharedDefaults();

        if (!_settings.ContainsKey("AnimationFramerate"))
        {
            _settings["AnimationFramerate"] = 30;
        }

        if (!_settings.ContainsKey("CheckClientUpdates"))
        {
            _settings["CheckClientUpdates"] = true;
        }

        if (!_settings.ContainsKey("CheckClientPrereleases"))
        {
            _settings["CheckClientPrereleases"] = false;
        }

        if (!_settings.ContainsKey("ClientDownloadPath") || string.IsNullOrEmpty(_settings["ClientDownloadPath"]?.ToString()))
        {
            _settings["ClientDownloadPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skua");
        }

        if (!_settings.ContainsKey("DeleteZipFileAfter"))
        {
            _settings["DeleteZipFileAfter"] = false;
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

        if (!_settings.ContainsKey("ChangeLogActivated"))
        {
            _settings["ChangeLogActivated"] = false;
        }

        if (!_settings.ContainsKey("syncTheme"))
        {
            _settings["syncTheme"] = true;
        }

        if (!_settings.ContainsKey("ManagedAccounts"))
        {
            _settings["ManagedAccounts"] = new StringCollection();
        }

        if (!_settings.ContainsKey("CustomBackgroundPath"))
        {
            _settings["CustomBackgroundPath"] = string.Empty;
        }

        if (!_settings.ContainsKey("UpgradeRequired"))
        {
            _settings["UpgradeRequired"] = true;
        }

        SaveSettings();
    }

    private void InitializeSharedDefaults()
    {
        if (!_settings.ContainsKey("UserThemes"))
        {
            _settings["UserThemes"] = new StringCollection();
        }

        if (!_settings.ContainsKey("DefaultBackground") || string.IsNullOrEmpty(_settings["DefaultBackground"]?.ToString()))
        {
            _settings["DefaultBackground"] = "Generic2.swf";
        }

        if (!_settings.ContainsKey("UserGitHubToken") || string.IsNullOrEmpty(_settings["UserGitHubToken"]?.ToString()))
        {
            _settings["UserGitHubToken"] = string.Empty;
        }

        if (!_settings.ContainsKey("DefaultThemes"))
        {
            var themes = new StringCollection();
            themes.AddRange(new[]
            {
                "Skua,Dark,#FF607D8B,#FF607D8B,#FF000000,#FF000000,true,4.5,Medium,All",
                "RBot,Light,#FF9C934E,#FF9C934E,#FF000000,#FF000000",
                "Grimoire,Dark,#FFCC1F41,#FFCC1F41,#FFFFFFFF,#FFFFFFFF",
                "Purple,Dark,#FF9651D6,#FF9651D6,#FFFFFFFF,#FFFFFFFF,true,4.5,Medium,All",
                "Phonk,Dark,#FFFE27D7,#FF607D8B,#FF000000,#FF000000,true,4.5,Medium,All"
            });
            _settings["DefaultThemes"] = themes;
        }

        if (!_settings.ContainsKey("CurrentTheme") || string.IsNullOrEmpty(_settings["CurrentTheme"]?.ToString()))
        {
            _settings["CurrentTheme"] = "Skua,Dark,#FF607D8B,#FF607D8B,#FF000000,#FF000000,true,4.5,Medium,All";
        }

        if (!_settings.ContainsKey("ApplicationVersion") || string.IsNullOrEmpty(_settings["ApplicationVersion"]?.ToString()))
        {
            _settings["ApplicationVersion"] = "1.3.0.0";
        }
    }
}