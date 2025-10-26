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
        List<UseRule> rules = new();
        bool shouldSkip = useRule.Last() == 's';
        
        int pos = 0;
        while (pos < useRule.Length)
        {
            if (char.IsWhiteSpace(useRule[pos]))
            {
                pos++;
                continue;
            }

            if (useRule[pos] == 'h' || useRule[pos] == 'H')
            {
                pos++;
                bool isGreater = pos < useRule.Length && useRule[pos] == '>';
                if (isGreater || (pos < useRule.Length && useRule[pos] == '<'))
                    pos++;
                int numStart = pos;
                while (pos < useRule.Length && char.IsDigit(useRule[pos]))
                    pos++;
                if (pos > numStart)
                    rules.Add(new UseRule(SkillRule.Health, isGreater, int.Parse(useRule.Substring(numStart, pos - numStart)), shouldSkip));
                continue;
            }

            if (useRule[pos] == 'm' || useRule[pos] == 'M')
            {
                pos++;
                bool isGreater = pos < useRule.Length && useRule[pos] == '>';
                if (isGreater || (pos < useRule.Length && useRule[pos] == '<'))
                    pos++;
                int numStart = pos;
                while (pos < useRule.Length && char.IsDigit(useRule[pos]))
                    pos++;
                if (pos > numStart)
                    rules.Add(new UseRule(SkillRule.Mana, isGreater, int.Parse(useRule.Substring(numStart, pos - numStart)), shouldSkip));
                continue;
            }

            if (useRule[pos] == 'p' || useRule[pos] == 'P')
            {
                pos++;
                bool isGreater = pos < useRule.Length && useRule[pos] == '>';
                if (isGreater || (pos < useRule.Length && useRule[pos] == '<'))
                    pos++;
                int numStart = pos;
                while (pos < useRule.Length && char.IsDigit(useRule[pos]))
                    pos++;
                if (pos > numStart)
                    rules.Add(new UseRule(SkillRule.PartyHealth, isGreater, int.Parse(useRule.Substring(numStart, pos - numStart)), shouldSkip));
                continue;
            }

            if (useRule[pos] == 'a' || useRule[pos] == 'A')
            {
                pos++;
                bool isGreater = false;
                if (pos < useRule.Length && useRule[pos] == '>')
                {
                    isGreater = true;
                    pos++;
                }
                else if (pos < useRule.Length && useRule[pos] == '<')
                {
                    pos++;
                }

                string auraName = "";
                int auraValue = 0;

                if (pos < useRule.Length && useRule[pos] == '"')
                {
                    pos++;
                    int nameStart = pos;
                    while (pos < useRule.Length && useRule[pos] != '"')
                    {
                        if (useRule[pos] == '\\' && pos + 1 < useRule.Length && useRule[pos + 1] == '"')
                            pos += 2;
                        else
                            pos++;
                    }
                    string rawName = useRule.Substring(nameStart, pos - nameStart);
                    auraName = rawName.Replace("\\\"" , "\"").Trim();
                    if (pos < useRule.Length && useRule[pos] == '"')
                        pos++;
                }
                else
                {
                    int nameStart = pos;
                    while (pos < useRule.Length && useRule[pos] != ' ' && !char.IsDigit(useRule[pos]))
                        pos++;
                    auraName = useRule.Substring(nameStart, pos - nameStart);
                }

                while (pos < useRule.Length && useRule[pos] == ' ')
                    pos++;

                int numStart = pos;
                while (pos < useRule.Length && char.IsDigit(useRule[pos]))
                    pos++;
                if (pos > numStart)
                    auraValue = int.Parse(useRule.Substring(numStart, pos - numStart));

                while (pos < useRule.Length && useRule[pos] == ' ')
                    pos++;

                string auraTarget = "self";
                if (pos < useRule.Length && char.IsLetter(useRule[pos]))
                {
                    int targetStart = pos;
                    while (pos < useRule.Length && char.IsLetter(useRule[pos]))
                        pos++;
                    if (useRule.Substring(targetStart, pos - targetStart).Contains("TARGET", StringComparison.OrdinalIgnoreCase))
                        auraTarget = "target";
                }

                if (!string.IsNullOrEmpty(auraName))
                    rules.Add(new UseRule(SkillRule.Aura, isGreater, auraValue, shouldSkip, auraTarget, auraName));
                continue;
            }

            if (useRule[pos] == 'w' || useRule[pos] == 'W')
            {
                pos++;
                if (pos < useRule.Length && useRule[pos] == 'w')
                    pos++;
                int numStart = pos;
                while (pos < useRule.Length && char.IsDigit(useRule[pos]))
                    pos++;
                if (pos > numStart)
                    rules.Add(new UseRule(SkillRule.Wait, true, int.Parse(useRule.Substring(numStart, pos - numStart)), shouldSkip));
                continue;
            }

            if (useRule[pos] == 's' || useRule[pos] == 'S')
            {
                pos++;
                continue;
            }

            pos++;
        }

        return rules.Count == 0 ? new[] { new UseRule(SkillRule.None) } : rules.ToArray();
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