using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Skills;
using Skua.Core.Utils;
using System.Text.Json;

namespace Skua.Core.Skills;

public class AdvancedSkillContainer : ObservableRecipient, IAdvancedSkillContainer, IDisposable
{
    private List<AdvancedSkill> _loadedSkills = new();
    private readonly string _defaultSkillsSetsPath;
    private readonly string _userSkillsSetsPath;
    private CancellationTokenSource? _saveCts;
    private Task? _saveTask;
    private AdvancedSkillsConfigJson? _jsonConfig;
    private string? _loadedFilePath;

    public List<AdvancedSkill> LoadedSkills
    {
        get => _loadedSkills;
        set => SetProperty(ref _loadedSkills, value, true);
    }

    public AdvancedSkillContainer()
    {
        _defaultSkillsSetsPath = ClientFileSources.SkuaAdvancedSkillsFile;
        _userSkillsSetsPath = Path.Combine(ClientFileSources.SkuaDIR, "UserAdvancedSkills.json");

        string rootDefaultSkills = Path.Combine(AppContext.BaseDirectory, "AdvancedSkills.json");
        if (File.Exists(rootDefaultSkills) && !File.Exists(_defaultSkillsSetsPath))
        {
            File.Copy(rootDefaultSkills, _defaultSkillsSetsPath, true);
        }
        LoadSkills();
    }

    public void Add(AdvancedSkill skill)
    {
        _loadedSkills.Add(skill);
        Save();
    }

    public void Remove(AdvancedSkill skill)
    {
        _loadedSkills.Remove(skill);
        Save();
    }

    public void TryOverride(AdvancedSkill skill)
    {
        if (!_loadedSkills.Contains(skill))
        {
            _loadedSkills.Add(skill);
            Save();
            return;
        }

        int index = _loadedSkills.IndexOf(skill);
        _loadedSkills[index] = skill;
        Save();
    }

    private void _CopyDefaultSkills()
    {
        IGetScriptsService getScripts = Ioc.Default.GetRequiredService<IGetScriptsService>();
        if (!File.Exists(_defaultSkillsSetsPath))
            getScripts.UpdateSkillSetsFile().GetAwaiter().GetResult();

        if (File.Exists(_userSkillsSetsPath))
            File.Delete(_userSkillsSetsPath);

        File.Copy(_defaultSkillsSetsPath, _userSkillsSetsPath);
    }

    public async void SyncSkills()
    {
        try
        {
            _saveCts?.Cancel();
            await (_saveTask ?? Task.CompletedTask);
            _saveCts?.Dispose();
            _saveCts = new CancellationTokenSource();

            await Task.Factory.StartNew(() =>
            {
                _CopyDefaultSkills();
                LoadSkills();
            }, _saveCts.Token);
        }
        catch {/* ignored */}
    }

    public void LoadSkills()
    {
        LoadedSkills.Clear();
        _jsonConfig = null;

        string jsonPath = Path.ChangeExtension(_userSkillsSetsPath, ".json");
        _loadedFilePath = jsonPath;

        if (File.Exists(jsonPath))
        {
            string fileContent = File.ReadAllText(jsonPath);
            LoadFromJson(fileContent);
        }
        else if (File.Exists(_userSkillsSetsPath))
        {
            string fileContent = File.ReadAllText(_userSkillsSetsPath);
            LoadFromText(fileContent);
        }
        else
        {
            _CopyDefaultSkills();
            if (File.Exists(jsonPath))
            {
                string fileContent = File.ReadAllText(jsonPath);
                LoadFromJson(fileContent);
            }
            else if (File.Exists(_userSkillsSetsPath))
            {
                string fileContent = File.ReadAllText(_userSkillsSetsPath);
                LoadFromText(fileContent);
            }
        }

        OnPropertyChanged(nameof(LoadedSkills));
        Broadcast(new(), _loadedSkills, nameof(LoadedSkills));
    }

    private void LoadFromText(string textContent)
    {
        foreach (string line in textContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] parts = line.Split(new[] { '=' }, 4);
            switch (parts.Length)
            {
                case 3:
                    _loadedSkills.Add(new AdvancedSkill(parts[1].Trim(), parts[2].Trim(), 250, parts[0].Trim(), "WaitForCooldown"));
                    break;

                case 4:
                    {
                        bool waitForCooldown = int.TryParse(parts[3].RemoveLetters(), out int result);
                        _loadedSkills.Add(new AdvancedSkill(parts[1].Trim(), parts[2].Trim(), waitForCooldown ? result : 250, parts[0].Trim(), waitForCooldown ? SkillUseMode.WaitForCooldown : SkillUseMode.UseIfAvailable));
                        break;
                    }
            }
        }
    }

    private void LoadFromJson(string jsonContent)
    {
        try
        {
            _jsonConfig = JsonSerializer.Deserialize<AdvancedSkillsConfigJson>(jsonContent);
            if (_jsonConfig == null)
                return;

            foreach (var classEntry in _jsonConfig)
            {
                string className = classEntry.Key;
                foreach (var modeEntry in classEntry.Value)
                {
                    string classUseMode = modeEntry.Key;
                    var skillMode = modeEntry.Value;

                    string skillsStr = ConvertSkillsToString(skillMode.Skills);

                    _loadedSkills.Add(new AdvancedSkill(
                        className,
                        skillsStr,
                        skillMode.SkillTimeout,
                        classUseMode,
                        skillMode.SkillUseMode == "UseIfAvailable" ? SkillUseMode.UseIfAvailable : SkillUseMode.WaitForCooldown
                    ));
                }
            }
        }
        catch
        {
            LoadFromText(jsonContent);
        }
    }

    private string ConvertSkillsToString(List<AdvancedSkillJson> skills)
    {
        var parts = new List<string>();
        foreach (var skill in skills)
        {
            var skillStr = skill.SkillId.ToString();
            if (skill.Rules?.Count > 0)
            {
                skillStr += " " + ConvertRulesToString(skill.Rules);
            }
            parts.Add(skillStr);
        }
        return string.Join(" | ", parts);
    }

    private string ConvertRulesToString(List<SkillRuleJson> rules)
    {
        var ruleParts = new List<string>();
        var multiAuraRules = rules.Where(r => r.Type == "MultiAura").ToList();
        var singleAuraRules = rules.Where(r => r.Type == "Aura").ToList();
        var otherRules = rules.Where(r => r.Type != "MultiAura" && r.Type != "Aura").ToList();

        foreach (var rule in otherRules)
        {
            switch (rule.Type)
            {
                case "Health":
                    ruleParts.Add($"H{(rule.Comparison == "greater" ? ">" : "<")}{rule.Value}");
                    break;

                case "Mana":
                    ruleParts.Add($"M{(rule.Comparison == "greater" ? ">" : "<")}{rule.Value}");
                    break;

                case "Wait":
                    ruleParts.Add($"WW{rule.Timeout}");
                    break;

                case "Skip":
                    ruleParts.Add("S");
                    break;
            }
        }

        if (singleAuraRules.Count > 1)
        {
            foreach (var rule in singleAuraRules)
            {
                ruleParts.Add($"MA{(rule.Comparison == "greater" ? ">" : "<")}\"{rule.AuraName}\" {rule.Value}{(rule.AuraTarget == "target" ? " TARGET" : "")}&");
            }
        }
        else if (singleAuraRules.Count == 1)
        {
            var rule = singleAuraRules[0];
            ruleParts.Add($"A{(rule.Comparison == "greater" ? ">" : "<")}\"{rule.AuraName}\" {rule.Value}{(rule.AuraTarget == "target" ? " TARGET" : "")}");
        }

        if (multiAuraRules.Count > 0)
        {
            int operatorIndex = multiAuraRules.First().Timeout ?? 0;
            string opChar = operatorIndex switch
            {
                1 => ":",
                _ => "&"
            };

            foreach (var rule in multiAuraRules)
            {
                ruleParts.Add($"MA{(rule.Comparison == "greater" ? ">" : "<")}\"{rule.AuraName}\" {rule.Value}{(rule.AuraTarget == "target" ? " TARGET" : "")}{opChar}");
            }
        }

        return string.Join(" ", ruleParts);
    }

    public Dictionary<string, List<string>> GetAvailableClassModes()
    {
        if (_jsonConfig == null)
        {
            string jsonPath = Path.ChangeExtension(_userSkillsSetsPath, ".json");
            if (File.Exists(jsonPath))
            {
                string fileContent = File.ReadAllText(jsonPath);
                _jsonConfig = JsonSerializer.Deserialize<AdvancedSkillsConfigJson>(fileContent);
            }
        }

        var result = new Dictionary<string, List<string>>();
        if (_jsonConfig != null)
        {
            foreach (var classEntry in _jsonConfig)
            {
                result[classEntry.Key] = classEntry.Value.Keys.ToList();
            }
        }
        return result;
    }

    public AdvancedSkill? GetClassModeSkills(string className, string mode)
    {
        return _loadedSkills.FirstOrDefault(s => s.ClassName == className && s.ClassUseMode.ToString() == mode);
    }

    public void ResetSkillsSets()
    {
        SyncSkills();
    }

    public async void Save()
    {
        _saveCts?.Cancel();
        await (_saveTask ?? Task.CompletedTask);
        _saveCts?.Dispose();
        _saveCts = new CancellationTokenSource();

        _saveTask = Task.Factory.StartNew(() =>
        {
            try
            {
                string jsonPath = _loadedFilePath ?? Path.ChangeExtension(_userSkillsSetsPath, ".json");
                if (!jsonPath.EndsWith(".json"))
                    jsonPath = Path.ChangeExtension(jsonPath, ".json");
                SaveToJson(jsonPath);

                if (!_saveCts.Token.IsCancellationRequested)
                {
                    LoadSkills();
                }
            }
            catch
            {
            }
        }, _saveCts.Token);
    }

    private void SaveToJson(string filePath)
    {
        var config = new AdvancedSkillsConfigJson();

        foreach (var skill in _loadedSkills)
        {
            if (!config.ContainsKey(skill.ClassName))
                config[skill.ClassName] = new Dictionary<string, SkillModeJson>();

            var skillMode = new SkillModeJson
            {
                SkillUseMode = skill.SkillUseMode == SkillUseMode.UseIfAvailable ? "UseIfAvailable" : "WaitForCooldown",
                SkillTimeout = skill.SkillTimeout,
                Skills = AdvancedSkillsParser.ParseSkillString(skill.Skills)
            };

            config[skill.ClassName][skill.ClassUseMode.ToString()] = skillMode;
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        string json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(filePath, json);
    }

    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _saveCts?.Cancel();
            try
            {
                _saveTask?.Wait(1000);
            }
            catch { }
            _saveCts?.Dispose();
            _loadedSkills.Clear();
        }

        _disposed = true;
    }

    ~AdvancedSkillContainer()
    {
        Dispose(false);
    }
}