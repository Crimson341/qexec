namespace Skua.Core.Models.Skills;

using System.Text.Json.Serialization;

public class AdvancedSkillJson
{
    [JsonPropertyName("skillId")]
    public int SkillId { get; set; }

    [JsonPropertyName("rules")]
    public List<SkillRuleJson>? Rules { get; set; } = new();
}

public class SkillRuleJson
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "None";

    [JsonPropertyName("value")]
    public int? Value { get; set; }

    [JsonPropertyName("comparison")]
    public string? Comparison { get; set; }

    [JsonPropertyName("auraName")]
    public string? AuraName { get; set; }

    [JsonPropertyName("auraTarget")]
    public string? AuraTarget { get; set; }

    [JsonPropertyName("skipOnMatch")]
    public bool SkipOnMatch { get; set; }

    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }
}

public class SkillModeJson
{
    [JsonPropertyName("skillUseMode")]
    public string SkillUseMode { get; set; } = "UseIfAvailable";

    [JsonPropertyName("skillTimeout")]
    public int SkillTimeout { get; set; } = 250;

    [JsonPropertyName("skills")]
    public List<AdvancedSkillJson> Skills { get; set; } = new();
}

public class AdvancedSkillsConfigJson : Dictionary<string, Dictionary<string, SkillModeJson>>
{
}
