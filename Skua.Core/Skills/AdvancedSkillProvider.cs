using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Utils;

namespace Skua.Core.Skills;

public class AdvancedSkillProvider : ISkillProvider
{
    private readonly IScriptPlayer _player;
    private readonly IScriptCombat _combat;
    private readonly AdvancedSkillCommand _currentCommand = new();
    private readonly UseRule[] _none = new[] { new UseRule(SkillRule.None) };

    public AdvancedSkillProvider(IScriptPlayer player, IScriptCombat combat)
    {
        _player = player;
        _combat = combat;
    }

    public bool ResetOnTarget { get; set; } = false;

    public (int, int) GetNextSkill()
    {
        return _currentCommand.GetNextSkill();
    }

    public void Load(string skills)
    {
        int index = 0;
        foreach (string command in skills.ToLower().Split('|').Select(s => s.Trim()).ToList())
        {
            if (int.TryParse(command.AsSpan(0, 1), out int skill))
            {
                _currentCommand.Skills.Add(index, skill);
                _currentCommand.UseRules.Add(command.Length <= 1 ? _none : ParseUseRule(command[1..]));
                ++index;
            }
        }
    }

    private UseRule[] ParseUseRule(string useRule)
    {
        string[] stringRules = useRule.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        UseRule[] rules = new UseRule[stringRules.Length];

        bool shouldSkip = useRule.Last() == 's';
        for (int i = 0; i < stringRules.Length; i++)
        {
            if (stringRules[i].Contains('h'))
            {
                rules[i] = new UseRule(SkillRule.Health, stringRules[i].Contains('>'), int.Parse(stringRules[i].RemoveLetters()), shouldSkip);
                continue;
            }

            if (stringRules[i].Contains('m') && !stringRules[i].Contains('a'))
            {
                rules[i] = new UseRule(SkillRule.Mana, stringRules[i].Contains('>'), int.Parse(stringRules[i].RemoveLetters()), shouldSkip);
                continue;
            }

            if (stringRules[i].Contains('a'))
            {
                string auraRule = stringRules[i];
                
                int firstDigitIndex = 0;
                while (firstDigitIndex < auraRule.Length && !char.IsDigit(auraRule[firstDigitIndex]))
                    firstDigitIndex++;
                
                int lastDigitIndex = auraRule.Length - 1;
                while (lastDigitIndex >= 0 && !char.IsDigit(auraRule[lastDigitIndex]))
                    lastDigitIndex--;
                
                if (firstDigitIndex >= auraRule.Length || lastDigitIndex < 0 || firstDigitIndex > lastDigitIndex)
                    continue;
                
                int auraValue = int.Parse(auraRule.Substring(firstDigitIndex, lastDigitIndex - firstDigitIndex + 1));
                
                string remainder = auraRule[(lastDigitIndex + 1)..] ?? string.Empty;
                string auraTarget = remainder.Contains("MOB", StringComparison.OrdinalIgnoreCase) ? "mob" : "self";
                string auraName = remainder.Replace("MOB", "", StringComparison.OrdinalIgnoreCase).Replace("S", "", StringComparison.OrdinalIgnoreCase).Trim();
                
                int comparisonMode = auraRule.Contains('>') ? 0 : (auraRule.Contains('<') ? 1 : (auraRule.Contains(">=") ? 2 : 3));
                rules[i] = new UseRule(SkillRule.Aura, auraRule.Contains('>'), auraValue, shouldSkip, auraTarget, auraName, comparisonMode);
                continue;
            }

            if (stringRules[i].Contains('w'))
            {
                rules[i] = new UseRule(SkillRule.Wait, true, int.Parse(stringRules[i].RemoveLetters()), shouldSkip);
            }
        }

        return rules;
    }

    public void Save(string file)
    {
    }

    public void OnTargetReset()
    {
        if (ResetOnTarget && !_player.HasTarget)
            _currentCommand.Reset();
    }

    public bool? ShouldUseSkill(int skillIndex, bool canUse)
    {
        return _currentCommand.ShouldUse(_player, skillIndex, canUse);
    }

    public void Stop()
    {
        _combat.CancelAutoAttack();
        _combat.CancelTarget();
        _currentCommand.Reset();
    }

    public void OnPlayerDeath()
    {
        _currentCommand.Reset();
    }
}
