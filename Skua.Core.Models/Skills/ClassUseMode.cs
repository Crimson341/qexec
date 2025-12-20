namespace Skua.Core.Models.Skills;

public enum ClassUseMode
{
    Base,
    Atk,
    Def,
    Farm,
    Solo,
    Supp,
    Dodge,
    Ultra
}

public static class ClassUseModeExtensions
{
    public static string[] ToArray() => Enum.GetNames(typeof(ClassUseMode));
}