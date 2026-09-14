using Skua.Core.Models.Skills;

namespace Skua.Core;

public static class CombatPolicy
{
    public static bool NeedsDefense(int health, int maximum, bool defending) => maximum > 0 &&
        (double)health / maximum < (defending ? 0.65 : 0.40);
    public static bool IsBoss(int targetMaximum, int playerMaximum) =>
        targetMaximum >= Math.Max(100000L, (long)playerMaximum * 20);
    public static AdvancedSkill? Select(IEnumerable<AdvancedSkill> profiles, string className, bool defense, bool boss)
    {
        var available = profiles.Where(p => string.Equals(p.ClassName, className, StringComparison.OrdinalIgnoreCase)).ToList();
        var modes = defense ? new[] { ClassUseMode.Def, ClassUseMode.Dodge, ClassUseMode.Base, ClassUseMode.Solo, ClassUseMode.Farm }
            : boss ? new[] { ClassUseMode.Solo, ClassUseMode.Base, ClassUseMode.Atk }
            : new[] { ClassUseMode.Farm, ClassUseMode.Base, ClassUseMode.Atk };
        return modes.Select(mode => available.FirstOrDefault(p => p.ClassUseMode == mode)).FirstOrDefault(p => p != null);
    }
}
