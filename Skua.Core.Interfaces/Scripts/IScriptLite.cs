namespace Skua.Core.Interfaces;

/// <summary>
/// Defines an interface for managing and querying various gameplay and user interface options, including animation
/// toggles, UI visibility, and advanced option retrieval and assignment.
/// </summary>
/// <remarks>
/// Implementations of this interface provide programmatic access to a range of settings that control
/// visual effects, user interface elements, and gameplay behaviors. These options are typically used to customize the
/// user experience, optimize performance, or automate certain actions. The interface also includes generic methods for
/// retrieving and setting advanced options by name, allowing for flexible extension and integration with option panels.
/// </remarks>
public interface IScriptLite
{
    /// <summary>
    /// Gets or sets a value indicating whether the character select screen is currently active.
    /// </summary>
    bool CharacterSelectScreen { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the custom drops user interface is enabled.
    /// </summary>
    bool CustomDropsUI { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether debugging features are enabled.
    /// </summary>
    bool Debugger { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether debug packet information is enabled.
    /// </summary>
    bool DebugPacket { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the damage strobe visual effect is disabled.
    /// </summary>
    bool DisableDamageStrobe { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether monster animations are disabled.
    /// </summary>
    bool DisableMonsterAnimation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the red warning indicator is disabled.
    /// </summary>
    bool DisableRedWarning { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether self-animations are disabled.
    /// </summary>
    bool DisableSelfAnimation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether skill animations are disabled.
    /// </summary>
    /// <remarks>
    /// Set this property to <see langword="true"/> to prevent skill animations from playing during
    /// skill execution. This can be useful for improving performance or for accessibility purposes.
    /// </remarks>
    bool DisableSkillAnimation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether weapon animations are disabled.
    /// </summary>
    /// <remarks>
    /// Set this property to <see langword="true"/> to prevent weapon animations from playing. This
    /// can be useful for scenarios where visual effects should be suppressed, such as in performance-critical
    /// situations or when animations are not desired.
    /// </remarks>
    bool DisableWeaponAnimation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether monster positions are frozen during gameplay.
    /// </summary>
    bool FreezeMonsterPosition { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether players are hidden from view.
    /// </summary>
    bool HidePlayers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user interface should be hidden.
    /// </summary>
    bool HideUI { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether monsters are invisible.
    /// </summary>
    bool InvisibleMonsters { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a quest should be accepted again after completion.
    /// </summary>
    bool ReacceptQuest { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the monster type is displayed.
    /// </summary>
    bool ShowMonsterType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether background rendering is smoothed.
    /// </summary>
    bool SmoothBackground { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether dead entities should be automatically untargeted.
    /// </summary>
    bool UntargetDead { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the player should avoid self targeting.
    /// </summary>
    bool UntargetSelf { get; set; }

    /// <summary>
    /// Gets the current value of an AQLite option (Advanced Options panel).
    /// </summary>
    /// <typeparam name="T">Type of the value to be retrieved.</typeparam>
    /// <param name="optionName">Name of the option to be retrieved.</param>
    /// <returns>The value <typeparamref name="T"/> of the specified option.</returns>
    T? Get<T>(string optionName);

    /// <summary>
    /// Sets the value of an AQLite option (Advanced Options panel) to the specified value.
    /// </summary>
    /// <typeparam name="T">Type of the value that will be set.</typeparam>
    /// <param name="optionName">Name of the options to be set.</param>
    /// <param name="value">Value that will be set to the specified option.</param>
    void Set<T>(string optionName, T value);
}