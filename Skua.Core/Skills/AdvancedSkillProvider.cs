using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Auras;
using Skua.Core.Messaging;
using Skua.Core.Utils;

namespace Skua.Core.Skills;

public class AdvancedSkillProvider : ISkillProvider
{
    private readonly IScriptPlayer _player;
    private readonly IScriptSelfAuras _self;
    private readonly IScriptTargetAuras _target;
    private readonly IScriptCombat _combat;
    private readonly IFlashUtil _flash;
    private readonly AdvancedSkillCommand _currentCommand;

    public AdvancedSkillProvider(IScriptPlayer player, IScriptSelfAuras self, IScriptTargetAuras target, IScriptCombat combat, IFlashUtil flash)
    {
        _player = player;
        _self = self;
        _target = target;
        _combat = combat;
        _flash = flash;
        _currentCommand = new AdvancedSkillCommand(flash);
    }

    private readonly UseRule[] _none = new[] { new UseRule(SkillRule.None) };

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
            if (stringRules[i].Contains('h') || (stringRules[i].Contains('p') && stringRules[i].Contains("any")))
            {
                if (stringRules[i].Contains("pany"))
                {
                    rules[i] = new UseRule(SkillRule.PartyHealth, stringRules[i].Contains('>'), int.Parse(stringRules[i].RemoveLetters()), shouldSkip);
                }
                else
                {
                    rules[i] = new UseRule(SkillRule.Health, stringRules[i].Contains('>'), int.Parse(stringRules[i].RemoveLetters()), shouldSkip);
                }
                continue;
            }

            if (stringRules[i].Contains('m'))
            {
                rules[i] = new UseRule(SkillRule.Mana, stringRules[i].Contains('>'), int.Parse(stringRules[i].RemoveLetters()), shouldSkip);
                continue;
            }

            if (stringRules[i].Contains('a'))
            {
                string auraRule = stringRules[i];

                int pos = 1;
                if (pos < auraRule.Length && auraRule[pos] == '>')
                {
                    pos++;
                }
                else if (pos < auraRule.Length && auraRule[pos] == '<')
                {
                    pos++;
                }

                int nameEnd = pos;
                int lastNonSpaceIdx = pos;
                while (nameEnd < auraRule.Length && !char.IsDigit(auraRule[nameEnd]))
                {
                    if (auraRule[nameEnd] != ' ')
                        lastNonSpaceIdx = nameEnd;
                    nameEnd++;
                }

                string auraName = auraRule.Substring(pos, lastNonSpaceIdx - pos + 1).Trim();
                pos = nameEnd;

                int numStart = pos;
                while (pos < auraRule.Length && char.IsDigit(auraRule[pos]))
                    pos++;

                if (pos <= numStart)
                    continue;

                int auraValue = int.Parse(auraRule.Substring(numStart, pos - numStart));

                while (pos < auraRule.Length && auraRule[pos] == ' ')
                    pos++;

                string auraTarget = "self";
                if (pos < auraRule.Length && char.IsLetter(auraRule[pos]))
                {
                    int targetEnd = pos;
                    while (targetEnd < auraRule.Length && char.IsLetter(auraRule[targetEnd]))
                        targetEnd++;
                    if (auraRule.Substring(pos, targetEnd - pos).Contains("TARGET", StringComparison.OrdinalIgnoreCase))
                        auraTarget = "target";
                }

                rules[i] = new UseRule(SkillRule.Aura, auraRule.Contains('>'), auraValue, shouldSkip, auraTarget, auraName);
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
        return _currentCommand.ShouldUse(_player, _self, _target, skillIndex, canUse);
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