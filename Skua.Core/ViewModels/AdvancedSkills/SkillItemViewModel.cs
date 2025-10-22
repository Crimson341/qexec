using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Utils;
using System.Text;

namespace Skua.Core.ViewModels;

public class SkillItemViewModel : ObservableObject
{
    public SkillItemViewModel(int skill, bool useRule, int waitValue, bool healthGreaterThanBool, int healthValue, bool manaGreaterThanBool, int manaValue, bool skipBool)
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
            AuraComparisonMode = 0
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
            AuraComparisonMode = useRules.AuraComparisonMode,
            AuraUseValue = useRules.AuraUseValue,
            AuraTargetIndex = useRules.AuraTargetIndex,
            AuraName = useRules.AuraName,
            SkipUseBool = useRules.SkipUseBool
        };
        _displayString = ToString();
    }

    public SkillItemViewModel(string skill)
    {
        string[] skillRules = skill[1..].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        Skill = int.Parse(skill.AsSpan(0, 1));
        bool useRule = false, healthGreater = false, manaGreater = false, skip = false;
        int waitVal = 0, healthVal = 0, manaVal = 0, auraVal = 0, auraTargetIndex = 0, auraComparisonMode = 0;
        string auraName = string.Empty;
        for (int i = 0; i < skillRules.Length; i++)
        {
            if (skillRules[i].Contains('W'))
            {
                useRule = true;
                waitVal = int.Parse(skillRules[i].RemoveLetters());
            }
            else if (skillRules[i].Contains('H'))
            {
                useRule = true;
                if (skillRules[i].Contains('>'))
                    healthGreater = true;
                healthVal = int.Parse(skillRules[i].RemoveLetters());
            }
            else if (skillRules[i].Contains('M') && !skillRules[i].Contains('A'))
            {
                useRule = true;
                if (skillRules[i].Contains('>'))
                    manaGreater = true;
                manaVal = int.Parse(skillRules[i].RemoveLetters());
            }
            else if (skillRules[i].Contains('A'))
            {
                useRule = true;
                auraComparisonMode = 0;
                if (skillRules[i].Contains('>'))
                    auraComparisonMode = 0;
                else if (skillRules[i].Contains('<'))
                    auraComparisonMode = 1;
                else if (skillRules[i].Contains('E'))
                    auraComparisonMode = 2;
                else if (skillRules[i].Contains('L'))
                    auraComparisonMode = 3;
                
                int firstDigitIndex = 0;
                while (firstDigitIndex < skillRules[i].Length && !char.IsDigit(skillRules[i][firstDigitIndex]))
                    firstDigitIndex++;
                
                int lastDigitIndex = skillRules[i].Length - 1;
                while (lastDigitIndex >= 0 && !char.IsDigit(skillRules[i][lastDigitIndex]))
                    lastDigitIndex--;
                
                if (firstDigitIndex < skillRules[i].Length && lastDigitIndex >= 0 && firstDigitIndex <= lastDigitIndex)
                {
                    string beforeNumber = skillRules[i].Substring(0, firstDigitIndex);
                    string nameAndComparator = beforeNumber.Substring(1);
                    
                    auraVal = int.Parse(skillRules[i].Substring(firstDigitIndex, lastDigitIndex - firstDigitIndex + 1));
                    string remainder = skillRules[i].Substring(lastDigitIndex + 1);
                    
                    auraName = nameAndComparator;
                    if (remainder.Contains("MOB", StringComparison.OrdinalIgnoreCase))
                        auraTargetIndex = 1;
                    if (remainder.Contains("E") || remainder.Contains("L"))
                        auraComparisonMode = remainder.Contains("E") ? 2 : 3;
                }
            }

            if (skillRules[i].Contains('S'))
                useRule = skip = true;
        }
        _useRules = new SkillRulesViewModel()
        {
            UseRuleBool = useRule,
            WaitUseValue = waitVal,
            HealthGreaterThanBool = healthGreater,
            HealthUseValue = healthVal,
            ManaGreaterThanBool = manaGreater,
            ManaUseValue = manaVal,
            AuraComparisonMode = auraComparisonMode,
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
            string target = UseRules.AuraTargetIndex == 1 ? "Mob" : "Self";
            bob.Append($" - [Aura ({target})");
            if (!string.IsNullOrEmpty(UseRules.AuraName))
                bob.Append($" '{UseRules.AuraName}'");
            bob.Append($" {UseRules.AuraComparisonSymbol} ");
            bob.Append(UseRules.AuraUseValue);
            bob.Append("]");
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
        if (UseRules.AuraUseValue != 0 || !string.IsNullOrEmpty(UseRules.AuraName))
        {
            string target = UseRules.AuraTargetIndex == 1 ? "MOB" : string.Empty;
            string name = string.IsNullOrEmpty(UseRules.AuraName) ? string.Empty : UseRules.AuraName;
            char compareChar = UseRules.AuraComparisonMode switch
            {
                0 => '>',
                1 => '<',
                2 => 'E',
                3 => 'L',
                _ => '>'
            };
            bob.Append($" A{compareChar}{name}{UseRules.AuraUseValue}{target}");
        }
        if (UseRules.SkipUseBool)
            bob.Append('S');
        return bob.ToString();
    }
}