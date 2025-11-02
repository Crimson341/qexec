namespace Skua.Core.Interfaces;

/// <summary>
/// Defines functionality for managing auras that are applied to the player.
/// </summary>
/// <remarks>
/// This interface extends <see cref="IScriptAuras"/> to provide operations specific to self-applied
/// auras. Implementations may offer additional methods or properties for handling auras that affect the player itself,
/// as opposed to those applied to others.
/// </remarks>
public interface IScriptSelfAuras : IScriptAuras
{
}