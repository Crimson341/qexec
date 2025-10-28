namespace Skua.Core.Skills;

using Skua.Core.Models.Skills;
using System.Text.Json;

public static class AdvancedSkillsParser
{
    public static AdvancedSkillsConfigJson ParseTextToJson(string textContent)
    {
        var config = new AdvancedSkillsConfigJson();
        var lines = textContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entry = ParseLine(line);
            if (entry == null)
                continue;

            var (className, classUseMode, skillsStr, skillTimeout, skillUseMode) = entry.Value;

            if (!config.ContainsKey(className))
                config[className] = new Dictionary<string, SkillModeJson>();

            var modeEntry = new SkillModeJson
            {
                SkillUseMode = skillUseMode,
                SkillTimeout = skillTimeout,
                Skills = ParseSkills(skillsStr)
            };

            config[className][classUseMode] = modeEntry;
        }

        return config;
    }

    private static (string className, string classUseMode, string skillsStr, int skillTimeout, string skillUseMode)? ParseLine(string line)
    {
        var parts = line.Split(new[] { '=' }, 4);
        if (parts.Length < 3)
            return null;

        string classUseMode = parts[0].Trim();
        string className = parts[1].Trim();
        string skillsStr = parts[2].Trim();

        int skillTimeout = 250;
        string skillUseMode = "UseIfAvailable";

        if (parts.Length == 4)
        {
            string lastPart = parts[3].Trim();

            if (lastPart.Equals("Use if Available", StringComparison.OrdinalIgnoreCase))
            {
                skillUseMode = "UseIfAvailable";
            }
            else if (int.TryParse(new string(lastPart.Where(char.IsDigit).ToArray()), out int timeout))
            {
                skillTimeout = timeout;
                skillUseMode = "WaitForCooldown";
            }
        }

        return (className, classUseMode, skillsStr, skillTimeout, skillUseMode);
    }

    private static List<AdvancedSkillJson> ParseSkills(string skillsStr)
    {
        var skills = new List<AdvancedSkillJson>();
        var skillEntries = skillsStr.Split('|');

        foreach (var skillEntry in skillEntries)
        {
            var trimmed = skillEntry.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (!int.TryParse(trimmed.AsSpan(0, 1), out int skillId))
                continue;

            var skill = new AdvancedSkillJson { SkillId = skillId };

            if (trimmed.Length > 1)
            {
                string rulesPart = trimmed[1..].Trim();
                bool hasSkipFlag = rulesPart.EndsWith('S') || rulesPart.EndsWith('s');
                var rules = ParseRules(rulesPart, out string multiAuraOperator);
                skill.Rules = rules?.Count > 0 ? rules : null;
                skill.SkipOnMatch = hasSkipFlag;

                bool hasMultiAura = rules?.Any(r => r.Type == "MultiAura") ?? false;
                skill.MultiAura = hasMultiAura;
            }

            skills.Add(skill);
        }

        return skills;
    }

    private static List<SkillRuleJson> ParseRules(string rulesPart, out string multiAuraOperator)
    {
        var rules = new List<SkillRuleJson>();
        multiAuraOperator = "AND";
        bool skipOnMatch = rulesPart.EndsWith('S') || rulesPart.EndsWith('s');

        int pos = 0;
        var singleAuraRules = new List<SkillRuleJson>();
        var multiAuraRules = new List<SkillRuleJson>();
        while (pos < rulesPart.Length)
        {
            if (char.ToUpper(rulesPart[pos]) == 'W' && pos + 1 < rulesPart.Length && char.ToUpper(rulesPart[pos + 1]) == 'W')
            {
                pos += 2;
                int numStart = pos;
                while (pos < rulesPart.Length && char.IsDigit(rulesPart[pos]))
                    pos++;

                if (pos > numStart)
                {
                    int timeout = int.Parse(rulesPart.Substring(numStart, pos - numStart));
                    var waitRule = new SkillRuleJson
                    {
                        Type = "Wait"
                    };
                    if (timeout > 0)
                        waitRule.Timeout = timeout;
                    rules.Add(waitRule);
                }

                while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                    pos++;
            }
            else if (char.ToUpper(rulesPart[pos]) == 'H')
            {
                pos++;
                string comparison = "less";
                if (pos < rulesPart.Length && rulesPart[pos] == '>')
                {
                    comparison = "greater";
                    pos++;
                }
                else if (pos < rulesPart.Length && rulesPart[pos] == '<')
                {
                    pos++;
                }

                int numStart = pos;
                while (pos < rulesPart.Length && char.IsDigit(rulesPart[pos]))
                    pos++;

                if (pos > numStart)
                {
                    int value = int.Parse(rulesPart.Substring(numStart, pos - numStart));
                    rules.Add(new SkillRuleJson
                    {
                        Type = "Health",
                        Value = value,
                        Comparison = comparison
                    });
                }

                while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                    pos++;
            }
            else if (char.ToUpper(rulesPart[pos]) == 'M')
            {
                pos++;
                string comparison = "less";
                if (pos < rulesPart.Length && rulesPart[pos] == '>')
                {
                    comparison = "greater";
                    pos++;
                }
                else if (pos < rulesPart.Length && rulesPart[pos] == '<')
                {
                    pos++;
                }

                int numStart = pos;
                while (pos < rulesPart.Length && char.IsDigit(rulesPart[pos]))
                    pos++;

                if (pos > numStart)
                {
                    int value = int.Parse(rulesPart.Substring(numStart, pos - numStart));
                    rules.Add(new SkillRuleJson
                    {
                        Type = "Mana",
                        Value = value,
                        Comparison = comparison
                    });
                }

                while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                    pos++;
            }
            else if (char.ToUpper(rulesPart[pos]) == 'A')
            {
                pos++;
                string comparison = "less";
                if (pos < rulesPart.Length && rulesPart[pos] == '>')
                {
                    comparison = "greater";
                    pos++;
                }
                else if (pos < rulesPart.Length && rulesPart[pos] == '<')
                {
                    pos++;
                }

                string auraName = "";
                int auraValue = 0;

                if (pos < rulesPart.Length && rulesPart[pos] == '"')
                {
                    pos++;
                    int nameStart = pos;
                    while (pos < rulesPart.Length && rulesPart[pos] != '"')
                    {
                        if (rulesPart[pos] == '\\' && pos + 1 < rulesPart.Length && rulesPart[pos + 1] == '"')
                            pos += 2;
                        else
                            pos++;
                    }
                    string rawName = rulesPart.Substring(nameStart, pos - nameStart);
                    auraName = rawName.Replace("\\\"", "\"").Trim();
                    if (pos < rulesPart.Length && rulesPart[pos] == '"')
                        pos++;

                    while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                        pos++;

                    int valueStart = pos;
                    while (pos < rulesPart.Length && char.IsDigit(rulesPart[pos]))
                        pos++;
                    if (valueStart < pos)
                        auraValue = int.Parse(rulesPart.Substring(valueStart, pos - valueStart));
                }
                else
                {
                    int nameStart = pos;
                    int lastDigitStart = -1;
                    int lastDigitEnd = -1;

                    while (pos < rulesPart.Length && rulesPart[pos] != ' ')
                    {
                        if (char.IsDigit(rulesPart[pos]))
                        {
                            if (lastDigitStart == -1)
                                lastDigitStart = pos;
                            lastDigitEnd = pos + 1;
                        }
                        else if (lastDigitStart != -1 && lastDigitEnd == pos)
                        {
                            lastDigitStart = -1;
                        }
                        pos++;
                    }

                    while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                        pos++;

                    int valueStart = pos;
                    while (pos < rulesPart.Length && char.IsDigit(rulesPart[pos]))
                        pos++;

                    if (lastDigitStart >= 0 && lastDigitEnd > lastDigitStart)
                    {
                        auraName = rulesPart.Substring(nameStart, lastDigitStart - nameStart).Trim();
                        auraValue = int.Parse(rulesPart.Substring(lastDigitStart, lastDigitEnd - lastDigitStart));
                    }
                    else if (valueStart < pos)
                    {
                        auraName = rulesPart.Substring(nameStart, valueStart - nameStart).Trim();
                        auraValue = int.Parse(rulesPart.Substring(valueStart, pos - valueStart));
                    }
                }

                while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                    pos++;

                string auraTarget = "self";
                if (pos < rulesPart.Length && char.IsLetter(rulesPart[pos]))
                {
                    int targetStart = pos;
                    while (pos < rulesPart.Length && char.IsLetter(rulesPart[pos]))
                        pos++;

                    string targetStr = rulesPart.Substring(targetStart, pos - targetStart);
                    if (targetStr.Contains("TARGET", StringComparison.OrdinalIgnoreCase))
                        auraTarget = "target";
                }

                if (!string.IsNullOrEmpty(auraName))
                {
                    var auraRule = new SkillRuleJson
                    {
                        Type = "Aura",
                        Value = auraValue,
                        Comparison = comparison
                    };
                    if (!string.IsNullOrEmpty(auraName))
                        auraRule.AuraName = auraName;
                    if (!string.IsNullOrEmpty(auraTarget))
                        auraRule.AuraTarget = auraTarget;
                    singleAuraRules.Add(auraRule);
                }

                while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                    pos++;
            }
            else if (pos + 1 < rulesPart.Length && char.ToUpper(rulesPart[pos]) == 'M' && char.ToUpper(rulesPart[pos + 1]) == 'A')
            {
                pos += 2;
                string comparison = "less";
                if (pos < rulesPart.Length && rulesPart[pos] == '>')
                {
                    comparison = "greater";
                    pos++;
                }
                else if (pos < rulesPart.Length && rulesPart[pos] == '<')
                {
                    pos++;
                }

                string auraName = "";
                int auraValue = 0;

                if (pos < rulesPart.Length && rulesPart[pos] == '"')
                {
                    pos++;
                    int nameStart = pos;
                    while (pos < rulesPart.Length && rulesPart[pos] != '"')
                    {
                        if (rulesPart[pos] == '\\' && pos + 1 < rulesPart.Length && rulesPart[pos + 1] == '"')
                            pos += 2;
                        else
                            pos++;
                    }
                    string rawName = rulesPart.Substring(nameStart, pos - nameStart);
                    auraName = rawName.Replace("\\\"", "\"").Trim();
                    if (pos < rulesPart.Length && rulesPart[pos] == '"')
                        pos++;
                }

                while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                    pos++;

                int valueStart = pos;
                while (pos < rulesPart.Length && char.IsDigit(rulesPart[pos]))
                    pos++;
                if (valueStart < pos)
                    auraValue = int.Parse(rulesPart.Substring(valueStart, pos - valueStart));

                while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                    pos++;

                string auraTarget = "self";
                if (pos < rulesPart.Length && char.IsLetter(rulesPart[pos]))
                {
                    int targetStart = pos;
                    while (pos < rulesPart.Length && char.IsLetter(rulesPart[pos]))
                        pos++;

                    string targetStr = rulesPart.Substring(targetStart, pos - targetStart);
                    if (targetStr.Contains("TARGET", StringComparison.OrdinalIgnoreCase))
                        auraTarget = "target";
                }

                string operatorIndex = "AND";
                if (pos < rulesPart.Length && (rulesPart[pos] == '&' || rulesPart[pos] == ':'))
                {
                    operatorIndex = rulesPart[pos] switch
                    {
                        ':' => "OR",
                        _ => "AND"
                    };
                    multiAuraOperator = operatorIndex;
                    pos++;
                }

                if (!string.IsNullOrEmpty(auraName))
                {
                    var rule = new SkillRuleJson
                    {
                        Type = "MultiAura",
                        AuraName = auraName,
                        AuraTarget = auraTarget,
                        Value = auraValue,
                        Comparison = comparison
                    };
                    if (multiAuraRules.Count == 0)
                        rule.MultiAuraOperator = multiAuraOperator;
                    multiAuraRules.Add(rule);
                }

                while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                    pos++;
            }
            else if (char.ToUpper(rulesPart[pos]) == 'S')
            {
                pos++;

                while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                    pos++;
            }
            else
            {
                pos++;
            }
        }

        rules.AddRange(singleAuraRules);
        rules.AddRange(multiAuraRules);

        if (rules.Count == 0)
        {
            rules.Add(new SkillRuleJson { Type = "None" });
        }

        return rules;
    }

    public static string ConvertJsonToJson(AdvancedSkillsConfigJson config)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(config, options);
    }

    public static List<AdvancedSkillJson> ParseSkillString(string skillsStr)
    {
        return ParseSkills(skillsStr);
    }
}