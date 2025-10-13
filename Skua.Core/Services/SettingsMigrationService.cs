using System.Text.Json;

namespace Skua.Core.Services;

public static class SettingsMigrationService
{
    /// <summary>
    /// Migrates settings from .NET's default hash-based folder structure to clean directories
    /// </summary>
    /// <param name="appName">The application name (e.g., "Skua" or "Skua.Manager")</param>
    /// <param name="targetPath">The target path where settings should be stored</param>
    public static void MigrateSettings(string appName, string targetPath)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var searchPattern = $"{appName}_*";
            
            // Find directories that match the hash-based pattern
            var hashDirectories = Directory.GetDirectories(localAppData, searchPattern)
                .Where(d => Path.GetFileName(d).Contains("_Url_") || Path.GetFileName(d).Contains("_"))
                .OrderByDescending(d => Directory.GetCreationTime(d)) // Get the most recent one
                .ToList();

            if (!hashDirectories.Any())
            {
                // Console.WriteLine($"No hash-based settings directories found for {appName}");
                return;
            }

            var mostRecentDir = hashDirectories.First();
            var userConfigFile = Path.Combine(mostRecentDir, "user.config");

            if (!File.Exists(userConfigFile))
            {
                // Console.WriteLine($"No user.config found in {mostRecentDir}");
                return;
            }

            // Console.WriteLine($"Migrating settings from {mostRecentDir} to {targetPath}");

            // Parse the old XML config and convert to JSON
            var settings = ParseUserConfig(userConfigFile);
            
            if (settings.Count == 0)
            {
                // No settings to migrate
                return;
            }
            
            // Ensure target directory exists
            Directory.CreateDirectory(targetPath);
            
            // Save as JSON
            var targetFile = Path.Combine(targetPath, "settings.json");
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(targetFile, json);

            // Console.WriteLine($"Successfully migrated {settings.Count} settings to {targetFile}");

            // Optionally, you can backup the old directory instead of deleting it
            // Directory.Move(mostRecentDir, mostRecentDir + ".backup");
        }
        catch (Exception ex)
        {
            // Silently handle migration errors to prevent app startup failures
            // Console.WriteLine($"Error migrating settings for {appName}: {ex.Message}");
        }
    }

    private static Dictionary<string, object> ParseUserConfig(string configFile)
    {
        var settings = new Dictionary<string, object>();
        
        try
        {
            var xml = File.ReadAllText(configFile);
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            
            var settingElements = doc.Descendants("setting");
            
            foreach (var element in settingElements)
            {
                var name = element.Attribute("name")?.Value;
                var value = element.Element("value")?.Value;
                
                if (name != null && value != null)
                {
                    // Try to convert common types
                    if (bool.TryParse(value, out var boolValue))
                        settings[name] = boolValue;
                    else if (int.TryParse(value, out var intValue))
                        settings[name] = intValue;
                    else if (double.TryParse(value, out var doubleValue))
                        settings[name] = doubleValue;
                    else
                        settings[name] = value;
                }
            }
        }
        catch (Exception ex)
        {
            // Silently handle parsing errors
            // Console.WriteLine($"Error parsing user config: {ex.Message}");
        }

        return settings;
    }

    /// <summary>
    /// Run migration for both Skua applications
    /// </summary>
    public static void MigrateAllSettings()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        
        // Migrate Skua.Manager settings
        var managerPath = Path.Combine(localAppData, "Skua.Manager");
        MigrateSettings("Skua.Manager", managerPath);
        
        // Migrate Skua app settings
        var appPath = Path.Combine(localAppData, "Skua");
        MigrateSettings("Skua.App.WPF", appPath);
    }
}