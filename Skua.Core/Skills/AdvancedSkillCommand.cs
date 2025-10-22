using Skua.Core.Interfaces;

namespace Skua.Core.Skills;

public class AdvancedSkillCommand
{
    private readonly IFlashUtil? _flash;

    public AdvancedSkillCommand() { }

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

    public bool? ShouldUse(IScriptPlayer player, int skillIndex, bool canUse)
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
                    shouldUse = AuraUseRule(player, useRule.AuraTarget, useRule.ComparisonMode, useRule.Value, useRule.AuraName);
                    break;

                case SkillRule.Wait:
                    if (useRule.ShouldSkip && !canUse)
                        return null;
                    Task.Delay(useRule.Value).Wait();
                    break;

                case SkillRule.PartyHealth:
                    shouldUse = PartyHealthUseRule(player, useRule.Greater, useRule.Value, useRule.IsPercentage);
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

    private bool AuraUseRule(IScriptPlayer player, string auraTarget, int comparisonMode, int count, string auraName = "")
    {
        if (auraTarget.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            if (player.Auras == null || player.Auras.Length == 0)
                return false;
            
            int totalStacks;
            if (string.IsNullOrEmpty(auraName))
                totalStacks = player.Auras.Sum(a => Convert.ToInt32(a.Value ?? 1));
            else
                totalStacks = player.Auras
                    .Where(a => a.Name.Equals(auraName, StringComparison.OrdinalIgnoreCase))
                    .Sum(a => Convert.ToInt32(a.Value ?? 1));
            
            return comparisonMode switch
            {
                0 => totalStacks > count,
                1 => totalStacks < count,
                2 => totalStacks >= count,
                3 => totalStacks <= count,
                _ => false
            };
        }
        else if (auraTarget.Equals("mob", StringComparison.OrdinalIgnoreCase))
        {
            if (!player.HasTarget || player.Target?.Auras == null || player.Target.Auras.Count == 0)
                return false;
            
            int totalStacks;
            if (string.IsNullOrEmpty(auraName))
                totalStacks = player.Target.Auras.Sum(a => Convert.ToInt32(a.Value ?? 1));
            else
                totalStacks = player.Target.Auras
                    .Where(a => a.Name.Equals(auraName, StringComparison.OrdinalIgnoreCase))
                    .Sum(a => Convert.ToInt32(a.Value ?? 1));
            
            return comparisonMode switch
            {
                0 => totalStacks > count,
                1 => totalStacks < count,
                2 => totalStacks >= count,
                3 => totalStacks <= count,
                _ => false
            };
        }
        
        return false;
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
    Wait,
    PartyHealth
}

public struct UseRule
{
    public UseRule(SkillRule rule)
    {
        Rule = rule;
    }

    public UseRule(SkillRule rule, bool greater, int value, bool shouldSkip)
    {
        Rule = rule;
        Greater = greater;
        Value = value;
        ShouldSkip = shouldSkip;
    }

    public UseRule(SkillRule rule, bool greater, int value, bool shouldSkip, string auraTarget, string auraName = "", int comparisonMode = 0, int partyMemberIndex = -1, bool isPercentage = true)
    {
        Rule = rule;
        Greater = greater;
        Value = value;
        ShouldSkip = shouldSkip;
        AuraTarget = auraTarget;
        AuraName = auraName;
        ComparisonMode = comparisonMode;
        PartyMemberIndex = partyMemberIndex;
        IsPercentage = isPercentage;
    }

    /// <summary>
    /// <list type="bullet">
    /// <item><see langword="null"/> = Wait</item>
    /// <item><see langword="true"/> = Health</item>
    /// <item><see langword="false"/> = Mana</item>
    /// </list>
    /// </summary>
    public readonly SkillRule Rule = SkillRule.None;

    /// <summary>
    /// <list type="bullet">
    /// <item><see langword="true"/> = Greater than</item>
    /// <item><see langword="false"/> = Less than</item>
    /// </list>
    /// </summary>
    public readonly bool Greater = default;

    public readonly int Value = default;
    public readonly bool ShouldSkip = default;
    public readonly string AuraTarget = "self";
    public readonly string AuraName = "";
    public readonly int ComparisonMode = 0;
    public readonly int PartyMemberIndex = -1;
    public readonly bool IsPercentage = true;
}
