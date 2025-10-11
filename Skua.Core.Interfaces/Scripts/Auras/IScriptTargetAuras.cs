namespace Skua.Core.Interfaces.Auras;

/// <summary>
/// Represents a collection of aura-related operations that can be performed on a target entity.
/// </summary>
/// <remarks>
/// This interface extends <see cref="IScriptAuras"/> to provide aura management functionality specific
/// to the target entity. Implementations may offer additional context or behaviors relevant to the target entity within a
/// scripting environment.
/// </remarks>
public interface IScriptTargetAuras : IScriptAuras
{
}