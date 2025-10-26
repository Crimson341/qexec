using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

public class AdvancedSkillsConverter
{
    static void Main(string[] args)
    {
        string inputFile = "Skua.App.WPF\\AdvancedSkills.txt";
        string outputFile = "Skua.App.WPF\\AdvancedSkills.json";

        if (args.Length > 0)
            inputFile = args[0];
        if (args.Length > 1)
            outputFile = args[1];

        try
        {
            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"Error: Input file '{inputFile}' not found.");
                return;
            }

            string textContent = File.ReadAllText(inputFile);
            var config = ParseTextToJson(textContent);
            
            string json = SerializeToJson(config);
            File.WriteAllText(outputFile, json);
            
            Console.WriteLine($"Successfully converted '{inputFile}' to '{outputFile}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static Dictionary<string, Dictionary<string, object>> ParseTextToJson(string textContent)
    {
        var config = new Dictionary<string, Dictionary<string, object>>();
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
                config[className] = new Dictionary<string, object>();

            var modeEntry = new Dictionary<string, object>
            {
                ["skillUseMode"] = skillUseMode,
                ["skillTimeout"] = skillTimeout,
                ["skills"] = ParseSkills(skillsStr)
            };

            config[className][classUseMode] = modeEntry;
        }

        return config;
    }

    static (string className, string classUseMode, string skillsStr, int skillTimeout, string skillUseMode)? ParseLine(string line)
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

    static List<Dictionary<string, object>> ParseSkills(string skillsStr)
    {
        var skills = new List<Dictionary<string, object>>();
        var skillEntries = skillsStr.Split('|');

        foreach (var skillEntry in skillEntries)
        {
            var trimmed = skillEntry.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (!int.TryParse(trimmed.AsSpan(0, 1), out int skillId))
                continue;

            var skill = new Dictionary<string, object> { ["skillId"] = skillId };

            if (trimmed.Length > 1)
            {
                string rulesPart = trimmed[1..].Trim();
                skill["rules"] = ParseRules(rulesPart);
            }

            skills.Add(skill);
        }

        return skills;
    }

    static List<Dictionary<string, object>> ParseRules(string rulesPart)
    {
        var rules = new List<Dictionary<string, object>>();
        bool skipOnMatch = rulesPart.EndsWith('S') || rulesPart.EndsWith('s');
        
        int pos = 0;
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
                    rules.Add(new Dictionary<string, object>
                    {
                        ["type"] = "Wait",
                        ["timeout"] = timeout,
                        ["skipOnMatch"] = skipOnMatch
                    });
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
                    rules.Add(new Dictionary<string, object>
                    {
                        ["type"] = "Health",
                        ["value"] = value,
                        ["comparison"] = comparison,
                        ["skipOnMatch"] = skipOnMatch
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
                    rules.Add(new Dictionary<string, object>
                    {
                        ["type"] = "Mana",
                        ["value"] = value,
                        ["comparison"] = comparison,
                        ["skipOnMatch"] = skipOnMatch
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

                string auraName = "";
                int auraValue = 0;

                if (lastDigitStart >= 0 && lastDigitEnd > lastDigitStart)
                {
                    auraName = rulesPart.Substring(nameStart, lastDigitStart - nameStart).Trim();
                    auraValue = int.Parse(rulesPart.Substring(lastDigitStart, lastDigitEnd - lastDigitStart));
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
                    rules.Add(new Dictionary<string, object>
                    {
                        ["type"] = "Aura",
                        ["auraName"] = auraName,
                        ["auraTarget"] = auraTarget,
                        ["value"] = auraValue,
                        ["comparison"] = comparison,
                        ["skipOnMatch"] = skipOnMatch
                    });
                }

                while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                    pos++;
            }
            else if (char.ToUpper(rulesPart[pos]) == 'S')
            {
                rules.Add(new Dictionary<string, object>
                {
                    ["type"] = "Skip",
                    ["skipOnMatch"] = skipOnMatch
                });
                pos++;

                while (pos < rulesPart.Length && rulesPart[pos] == ' ')
                    pos++;
            }
            else
            {
                pos++;
            }
        }

        if (rules.Count == 0)
        {
            rules.Add(new Dictionary<string, object> { ["type"] = "None" });
        }

        return rules;
    }

    static string SerializeToJson(Dictionary<string, Dictionary<string, object>> config)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(config, options);
    }
}
