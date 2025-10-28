using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Auras;

namespace Skua.Core.Skills;

public class AdvancedSkillCommand
{
    private readonly IFlashUtil? _flash;

    public AdvancedSkillCommand()
    { }

    public AdvancedSkillCommand(IFlashUtil flash)
    {
        _flash = flash;
    }

    public Dictionary<int, int> Skills { get; set; } = new();
    public List<UseRule[]> UseRules { get; set; } = new();
    private int _index = 0;

    public (int, int) GetNextSkill()
    {
        int skill = Skills[_index];
        int index = _index;
        ++_index;
        if (_index >= Skills.Count)
            _index = 0;
        return (index, skill);
    }

    public bool? ShouldUse(IScriptPlayer player, IScriptSelfAuras self , IScriptTargetAuras target, int skillIndex, bool canUse)
    {
        if (UseRules.Count == 0 || UseRules[skillIndex].First().Rule == SkillRule.None)
            return true;

        bool shouldUse = true;
        foreach (UseRule useRule in UseRules[skillIndex])
        {
            if (!player.Alive)
                return false;

            switch (useRule.Rule)
            {
                case SkillRule.Health:
                    shouldUse = HealthUseRule(player, useRule.Greater, useRule.Value, useRule.IsPercentage);
                    break;

                case SkillRule.Mana:
                    shouldUse = ManaUseRule(player, useRule.Greater, useRule.Value, useRule.IsPercentage);
                    break;

                case SkillRule.Aura:
                    if (useRule.MultiAuraChecks.Count > 0)
                        shouldUse = MultiAuraUseRule(player, self, target, useRule.MultiAuraChecks, useRule.MultiAuraOperator);
                    else
                        shouldUse = AuraUseRule(player, self, target, useRule.AuraTarget, useRule.Greater, useRule.Value, useRule.AuraName);
                    break;

                case SkillRule.PartyHealth:
                    shouldUse = PartyHealthUseRule(player, useRule.Greater, useRule.Value, useRule.IsPercentage);
                    break;

                case SkillRule.Wait:
                    if (useRule.ShouldSkip && !canUse)
                        return null;
                    Task.Delay(useRule.Value).Wait();
                    break;

                case SkillRule.None:
                    break;
            }

            if (useRule.ShouldSkip && !shouldUse)
                return null;

            if (!shouldUse)
                break;
        }
        return shouldUse;
    }

    private bool HealthUseRule(IScriptPlayer player, bool greater, int health, bool isPercentage = true)
    {
        if (player.Health == 0)
            return false;

        if (isPercentage)
        {
            if (player.MaxHealth == 0)
                return false;
            int ratio = (int)(player.Health / (double)player.MaxHealth * 100.0);
            return greater ? ratio >= health : ratio <= health;
        }
        else
        {
            return greater ? player.Health >= health : player.Health <= health;
        }
    }

    private bool ManaUseRule(IScriptPlayer player, bool greater, int mana, bool isPercentage = true)
    {
        if (isPercentage)
        {
            if (player.MaxMana == 0)
                return false;
            int ratio = (int)(player.Mana / (double)player.MaxMana * 100.0);
            return greater ? ratio >= mana : ratio <= mana;
        }
        else
        {
            return greater ? player.Mana >= mana : player.Mana <= mana;
        }
    }

    private bool PartyHealthUseRule(IScriptPlayer player, bool greater, int health, bool isPercentage = true)
    {
        if (_flash == null)
            return false;
        try
        {
            dynamic[]? players = _flash.GetGameObject<dynamic[]>("world.players");
            if (players == null || players.Length == 0)
                return false;

            foreach (dynamic targetPlayer in players)
            {
                string? targetCell = targetPlayer.strFrame;
                if (string.IsNullOrEmpty(targetCell) || targetCell != player.Cell)
                    continue;

                int targetHealth = targetPlayer.dataLeaf.intHP;
                int targetMaxHealth = targetPlayer.dataLeaf.intHPMax;

                if (targetHealth == 0 || (isPercentage && targetMaxHealth == 0))
                    continue;

                if (isPercentage)
                {
                    int ratio = (int)(targetHealth / (double)targetMaxHealth * 100.0);
                    if (greater ? ratio >= health : ratio <= health)
                        return true;
                }
                else
                {
                    if (greater ? targetHealth >= health : targetHealth <= health)
                        return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private bool AuraUseRule(IScriptPlayer player, IScriptSelfAuras self, IScriptTargetAuras target, string auraTarget, bool greater, int count, string auraName = "")
    {
        int totalStacks = 0;

        if (auraTarget.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            if (self.Auras != null && self.Auras.Count > 0)
            {
                totalStacks = string.IsNullOrEmpty(auraName)
                    ? self.Auras.Sum(a => Convert.ToInt32(a.Value ?? 1))
                    : self.Auras
                        .Where(a => a.Name.Equals(auraName, StringComparison.OrdinalIgnoreCase))
                        .Sum(a => Convert.ToInt32(a.Value ?? 1));
            }
        }

        if (auraTarget.Equals("target", StringComparison.OrdinalIgnoreCase))
        {
            if (!player.HasTarget)
                return false;

            if (target.Auras != null && target.Auras.Count > 0)
            {
                totalStacks = string.IsNullOrEmpty(auraName)
                    ? target.Auras.Sum(a => Convert.ToInt32(a.Value ?? 1))
                    : target.Auras
                        .Where(a => a.Name.Equals(auraName, StringComparison.OrdinalIgnoreCase))
                        .Sum(a => Convert.ToInt32(a.Value ?? 1));
            }
        }

        return greater ? totalStacks > count : totalStacks < count;
    }

    private bool MultiAuraUseRule(IScriptPlayer player, IScriptSelfAuras self, IScriptTargetAuras target, List<AuraCheck> checks, MultiAuraOperator op)
    {
        if (checks == null || checks.Count == 0)
            return true;
        else if (op == MultiAuraOperator.Or)
        {
            foreach (var check in checks)
            {
                int stacks = GetAuraStacks(player, self, target, check.AuraTarget, check.AuraName);
                if (check.Greater ? stacks >= check.StackCount : stacks <= check.StackCount)
                    return true;
            }
            return false;
        }
        else
        {
            foreach (var check in checks)
            {
                int stacks = GetAuraStacks(player, self, target, check.AuraTarget, check.AuraName);
                if (!(check.Greater ? stacks >= check.StackCount : stacks <= check.StackCount))
                    return false;
            }
            return true;
        }
    }

    private int GetAuraStacks(IScriptPlayer player, IScriptSelfAuras self, IScriptTargetAuras target, string auraTarget, string auraName)
    {
        if (auraTarget.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            if (self.Auras != null && self.Auras.Count > 0)
            {
                return self.Auras
                    .Where(a => a.Name.Equals(auraName, StringComparison.OrdinalIgnoreCase))
                    .Sum(a => Convert.ToInt32(a.Value ?? 1));
            }
        }
        else if (auraTarget.Equals("target", StringComparison.OrdinalIgnoreCase))
        {
            if (!player.HasTarget)
                return 0;

            if (target.Auras != null && target.Auras.Count > 0)
            {
                return target.Auras
                    .Where(a => a.Name.Equals(auraName, StringComparison.OrdinalIgnoreCase))
                    .Sum(a => Convert.ToInt32(a.Value ?? 1));
            }
        }

        return 0;
    }

    public void Reset()
    {
        _index = 0;
    }
}

public enum SkillRule
{
    None,
    Health,
    Mana,
    Aura,
    PartyHealth,
    Wait
}

public struct AuraCheck
{
    public string AuraName { get; set; }
    public int StackCount { get; set; }
    public bool Greater { get; set; }
    public string AuraTarget { get; set; }

    public AuraCheck()
    {
        AuraName = "";
        StackCount = 0;
        Greater = true;
        AuraTarget = "self";
    }
}

public enum MultiAuraOperator
{
    And,
    Or,
}

public struct UseRule
{
    public SkillRule Rule { get; set; }
    public bool Greater { get; set; }
    public int Value { get; set; }
    public bool ShouldSkip { get; set; }
    public string AuraTarget { get; set; }
    public string AuraName { get; set; }
    public int PartyMemberIndex { get; set; }
    public bool IsPercentage { get; set; }
    public List<AuraCheck> MultiAuraChecks { get; set; }
    public MultiAuraOperator MultiAuraOperator { get; set; }

    public UseRule()
    {
        Rule = SkillRule.None;
        Greater = default;
        Value = default;
        ShouldSkip = default;
        AuraTarget = "self";
        AuraName = "";
        PartyMemberIndex = -1;
        IsPercentage = true;
        MultiAuraChecks = new();
        MultiAuraOperator = MultiAuraOperator.And;
    }

    public UseRule(SkillRule rule)
    {
        Rule = rule;
        Greater = default;
        Value = default;
        ShouldSkip = default;
        AuraTarget = "self";
        AuraName = "";
        PartyMemberIndex = -1;
        IsPercentage = true;
        MultiAuraChecks = new();
        MultiAuraOperator = MultiAuraOperator.And;
    }

    public UseRule(SkillRule rule, bool greater, int value, bool shouldSkip)
    {
        Rule = rule;
        Greater = greater;
        Value = value;
        ShouldSkip = shouldSkip;
        AuraTarget = "self";
        AuraName = "";
        PartyMemberIndex = -1;
        IsPercentage = true;
        MultiAuraChecks = new();
        MultiAuraOperator = MultiAuraOperator.And;
    }

    public UseRule(SkillRule rule, bool greater, int value, bool shouldSkip, string auraTarget, string auraName = "", int partyMemberIndex = -1, bool isPercentage = true)
    {
        Rule = rule;
        Greater = greater;
        Value = value;
        ShouldSkip = shouldSkip;
        AuraTarget = auraTarget;
        AuraName = auraName;
        PartyMemberIndex = partyMemberIndex;
        IsPercentage = isPercentage;
        MultiAuraChecks = new();
        MultiAuraOperator = MultiAuraOperator.And;
    }

    public UseRule(SkillRule rule, bool shouldSkip, List<AuraCheck> auraChecks, MultiAuraOperator op = MultiAuraOperator.And)
    {
        Rule = rule;
        Greater = default;
        Value = default;
        ShouldSkip = shouldSkip;
        AuraTarget = "self";
        AuraName = "";
        PartyMemberIndex = -1;
        IsPercentage = true;
        MultiAuraChecks = auraChecks;
        MultiAuraOperator = op;
    }
}
