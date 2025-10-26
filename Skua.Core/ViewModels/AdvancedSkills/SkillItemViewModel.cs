using CommunityToolkit.Mvvm.ComponentModel;
using System.Text;

namespace Skua.Core.ViewModels;

public class SkillItemViewModel : ObservableObject
{
    public SkillItemViewModel(int skill, bool useRule, int waitValue, bool healthGreaterThanBool, int healthValue, bool manaGreaterThanBool, bool auraGreaterThanBool, int manaValue, bool skipBool)
    {
        Skill = skill;
        _useRules = new SkillRulesViewModel()
        {
            UseRuleBool = useRule,
            WaitUseValue = waitValue,
            HealthGreaterThanBool = healthGreaterThanBool,
            HealthUseValue = healthValue,
            ManaGreaterThanBool = manaGreaterThanBool,
            ManaUseValue = manaValue,
            SkipUseBool = skipBool,
            AuraGreaterThanBool = auraGreaterThanBool
        };
        _displayString = ToString();
    }

    public SkillItemViewModel(int skill, SkillRulesViewModel useRules)
    {
        Skill = skill;
        _useRules = new SkillRulesViewModel()
        {
            UseRuleBool = useRules.UseRuleBool,
            WaitUseValue = useRules.WaitUseValue,
            HealthGreaterThanBool = useRules.HealthGreaterThanBool,
            HealthUseValue = useRules.HealthUseValue,
            ManaGreaterThanBool = useRules.ManaGreaterThanBool,
            ManaUseValue = useRules.ManaUseValue,
            AuraGreaterThanBool = useRules.AuraGreaterThanBool,
            AuraUseValue = useRules.AuraUseValue,
            AuraTargetIndex = useRules.AuraTargetIndex,
            AuraName = useRules.AuraName,
            SkipUseBool = useRules.SkipUseBool
        };
        _displayString = ToString();
    }

    public SkillItemViewModel(string skill)
    {
        Skill = int.Parse(skill.AsSpan(0, 1));
        string rest = skill[1..].Trim();
        bool useRule = false, healthGreater = false, manaGreater = false, auraGreater = false, skip = false;
        int waitVal = 0, healthVal = 0, manaVal = 0, auraVal = 0, auraTargetIndex = 0;
        string auraName = string.Empty;

        if (string.IsNullOrEmpty(rest))
        {
            _useRules = new SkillRulesViewModel();
            _displayString = ToString();
            return;
        }

        int pos = 0;
        while (pos < rest.Length)
        {
            if (pos + 1 < rest.Length && rest[pos] == 'W' && rest[pos + 1] == 'W')
            {
                useRule = true;
                pos += 2;
                int numStart = pos;
                while (pos < rest.Length && char.IsDigit(rest[pos]))
                    pos++;
                if (pos > numStart)
                    waitVal = int.Parse(rest.Substring(numStart, pos - numStart));
                while (pos < rest.Length && rest[pos] == ' ')
                    pos++;
            }
            else if (rest[pos] == 'H')
            {
                useRule = true;
                pos++;
                if (pos < rest.Length && rest[pos] == '>')
                {
                    healthGreater = true;
                    pos++;
                }
                else if (pos < rest.Length && rest[pos] == '<')
                {
                    pos++;
                }
                int numStart = pos;
                while (pos < rest.Length && char.IsDigit(rest[pos]))
                    pos++;
                if (pos > numStart)
                    healthVal = int.Parse(rest.Substring(numStart, pos - numStart));
                while (pos < rest.Length && rest[pos] == ' ')
                    pos++;
            }
            else if (rest[pos] == 'M')
            {
                useRule = true;
                pos++;
                if (pos < rest.Length && rest[pos] == '>')
                {
                    manaGreater = true;
                    pos++;
                }
                else if (pos < rest.Length && rest[pos] == '<')
                {
                    pos++;
                }
                int numStart = pos;
                while (pos < rest.Length && char.IsDigit(rest[pos]))
                    pos++;
                if (pos > numStart)
                    manaVal = int.Parse(rest.Substring(numStart, pos - numStart));
                while (pos < rest.Length && rest[pos] == ' ')
                    pos++;
            }
            else if (rest[pos] == 'A')
            {
                useRule = true;
                pos++;
                if (pos < rest.Length && rest[pos] == '>')
                {
                    auraGreater = true;
                    pos++;
                }
                else if (pos < rest.Length && rest[pos] == '<')
                {
                    pos++;
                }

                if (pos < rest.Length && rest[pos] == '"')
                {
                    pos++;
                    int nameStart = pos;
                    while (pos < rest.Length && rest[pos] != '"')
                    {
                        if (rest[pos] == '\\' && pos + 1 < rest.Length && rest[pos + 1] == '"')
                            pos += 2;
                        else
                            pos++;
                    }
                    string rawName = rest.Substring(nameStart, pos - nameStart);
                    auraName = rawName.Replace("\\\"" , "\"").Trim();
                    if (pos < rest.Length && rest[pos] == '"')
                        pos++;

                    while (pos < rest.Length && rest[pos] == ' ')
                        pos++;

                    int valueStart = pos;
                    while (pos < rest.Length && char.IsDigit(rest[pos]))
                        pos++;
                    if (valueStart < pos)
                        auraVal = int.Parse(rest.Substring(valueStart, pos - valueStart));
                }
                else
                {
                    int nameStart = pos;
                    int lastDigitStart = -1;
                    int lastDigitEnd = -1;

                    while (pos < rest.Length && rest[pos] != ' ')
                    {
                        if (char.IsDigit(rest[pos]))
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

                    while (pos < rest.Length && rest[pos] == ' ')
                        pos++;

                    int valueStart = pos;
                    while (pos < rest.Length && char.IsDigit(rest[pos]))
                        pos++;

                    if (lastDigitStart >= 0 && lastDigitEnd > lastDigitStart)
                    {
                        auraName = rest.Substring(nameStart, lastDigitStart - nameStart).Trim();
                        auraVal = int.Parse(rest.Substring(lastDigitStart, lastDigitEnd - lastDigitStart));
                    }
                    else if (valueStart < pos)
                    {
                        auraName = rest.Substring(nameStart, valueStart - nameStart).Trim();
                        auraVal = int.Parse(rest.Substring(valueStart, pos - valueStart));
                    }
                }

                while (pos < rest.Length && rest[pos] == ' ')
                    pos++;

                if (pos < rest.Length && char.IsLetter(rest[pos]))
                {
                    int targetEnd = pos;
                    while (targetEnd < rest.Length && char.IsLetter(rest[targetEnd]))
                        targetEnd++;
                    if (rest.Substring(pos, targetEnd - pos).Contains("TARGET", StringComparison.OrdinalIgnoreCase))
                        auraTargetIndex = 1;
                    pos = targetEnd;
                }

                while (pos < rest.Length && rest[pos] == ' ')
                    pos++;
            }
            else if (rest[pos] == 'S')
            {
                useRule = skip = true;
                pos++;
                while (pos < rest.Length && rest[pos] == ' ')
                    pos++;
            }
            else
            {
                pos++;
            }
        }
        _useRules = new SkillRulesViewModel()
        {
            UseRuleBool = useRule,
            WaitUseValue = waitVal,
            HealthGreaterThanBool = healthGreater,
            HealthUseValue = healthVal,
            ManaGreaterThanBool = manaGreater,
            ManaUseValue = manaVal,
            AuraGreaterThanBool = auraGreater,
            AuraUseValue = auraVal,
            AuraTargetIndex = auraTargetIndex,
            AuraName = auraName,
            SkipUseBool = skip
        };
        _displayString = ToString();
    }

    private SkillRulesViewModel _useRules;

    public SkillRulesViewModel UseRules
    {
        get => _useRules;
        set
        {
            _useRules = value;
            DisplayString = ToString();
        }
    }

    public int Skill { get; }

    private string _displayString;

    public string DisplayString
    {
        get => _displayString;
        set => SetProperty(ref _displayString, value);
    }

    public override string ToString()
    {
        StringBuilder bob = new();
        bob.Append(Skill);

        if (!UseRules.UseRuleBool)
            return bob.ToString();

        if (UseRules.WaitUseValue != 0)
            bob.Append($" - [Wait for {UseRules.WaitUseValue}]");

        if (UseRules.HealthUseValue != 0)
        {
            bob.Append(" - [Health");
            _ = UseRules.HealthGreaterThanBool ? bob.Append(" > ") : bob.Append(" < ");
            bob.Append(UseRules.HealthUseValue);
            bob.Append("%]");
        }

        if (UseRules.ManaUseValue != 0)
        {
            bob.Append(" - [Mana");
            _ = UseRules.ManaGreaterThanBool ? bob.Append(" > ") : bob.Append(" < ");
            bob.Append(UseRules.ManaUseValue);
            bob.Append("%]");
        }

        if (UseRules.AuraUseValue != 0 || !string.IsNullOrEmpty(UseRules.AuraName))
        {
            string target = UseRules.AuraTargetIndex == 1 ? "Target" : "Self";
            bob.Append($" - [Aura ({target})");
            if (!string.IsNullOrEmpty(UseRules.AuraName))
                bob.Append($" '{UseRules.AuraName}'");
            _ = UseRules.AuraGreaterThanBool ? bob.Append(" > ") : bob.Append(" < ");
            bob.Append(UseRules.AuraUseValue);
            bob.Append(']');
        }

        if (UseRules.SkipUseBool)
            bob.Append(" - [Skip if not available]");

        return bob.ToString();
    }

    public string Convert()
    {
        StringBuilder bob = new();
        bob.Append(Skill);
        if (!UseRules.UseRuleBool)
            return bob.ToString();
        if (UseRules.WaitUseValue != 0)
            bob.Append($" WW{UseRules.WaitUseValue}");
        if (UseRules.HealthUseValue != 0)
            bob.Append($" H{(UseRules.HealthGreaterThanBool ? ">" : "<")}{UseRules.HealthUseValue}");
        if (UseRules.ManaUseValue != 0)
            bob.Append($" M{(UseRules.ManaGreaterThanBool ? ">" : "<")}{UseRules.ManaUseValue}");
        if (!string.IsNullOrEmpty(UseRules.AuraName))
        {
            string target = UseRules.AuraTargetIndex == 1 ? " TARGET" : string.Empty;
            bob.Append($" A{(UseRules.AuraGreaterThanBool ? ">" : "<")}\"{UseRules.AuraName}\" {UseRules.AuraUseValue}{target}");
        }
        if (UseRules.SkipUseBool)
            bob.Append('S');
        return bob.ToString();
    }
}