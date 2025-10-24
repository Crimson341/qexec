using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Auras;
using Skua.Core.Messaging;
using Skua.Core.Models.Auras;
using Skua.Core.Models.Monsters;

namespace Skua.Core.Scripts.Helpers;

/// <summary>
/// Helper for managing ultra boss mechanics
/// </summary>
public class UltraBossHelper : IUltraBossHelper, IDisposable
{
    private readonly IMessenger _messenger;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly Lazy<IScriptCombat> _lazyCombat;
    private bool _isEnabled;
    private bool _isCounterAttackActive;
    private Monster? _previousTarget;
    private bool _disposed;
    public UltraBossHelper(
        IMessenger messenger, 
        Lazy<IScriptPlayer> player, 
        Lazy<IScriptCombat> combat)
    {
        _messenger = messenger;
        _lazyPlayer = player;
        _lazyCombat = combat;
    }

    private IScriptPlayer Player => _lazyPlayer.Value;
    private IScriptCombat Combat => _lazyCombat.Value;

    public bool IsCounterAttackEnabled => _isEnabled;
    public bool IsCounterAttackActive => _isCounterAttackActive;


    public void EnableCounterAttack()
    {
        if (_isEnabled)
            return;

        _isEnabled = true;
        _messenger.Register<UltraBossHelper, CounterAttackMessage, int>(
            this,
            (int)MessageChannels.GameEvents,
            OnCounterAttack);
    }

    public void DisableCounterAttack()
    {
        if (!_isEnabled)
            return;

        _isEnabled = false;
        _isCounterAttackActive = false;
        _previousTarget = null;
        _messenger.Unregister<CounterAttackMessage, int>(this, (int)MessageChannels.GameEvents);
        Combat.StopAttacking = false;
    }

    public void SetAttacksStopped(bool shouldStop)
    {
        Combat.StopAttacking = shouldStop;
        if (shouldStop)
        {
            return;
        }

        _isCounterAttackActive = false;
        _previousTarget = null;
    }

    private static void OnCounterAttack(UltraBossHelper recipient, CounterAttackMessage message)
    {
        if (!recipient._isEnabled)
            return;

        if (message.Faded)
        {
            recipient._isCounterAttackActive = false;
            recipient.Combat.StopAttacking = false;
            if (recipient._previousTarget is null)
            {
                return;
            }

            recipient.Combat.Attack(recipient._previousTarget.MapID);
            recipient._previousTarget = null;
        }
        else
        {
            recipient._isCounterAttackActive = true;
            recipient.Combat.StopAttacking = true;
            recipient._previousTarget = recipient.Player.Target;
            recipient.Combat.CancelAutoAttack();
            recipient.Combat.CancelTarget();
        }
    }

    public (bool hasPositive, bool hasNegative, bool hasReversed) AnalyzeChargeMechanics(
        IScriptSelfAuras auras,
        string positiveChargeName,
        string negativeChargeName,
        string? reversedSuffix = null)
    {
        bool positiveCharge = auras.HasActiveAura(positiveChargeName);
        bool negativeCharge = auras.HasActiveAura(negativeChargeName);
        bool hasReversed = false;

        if (!string.IsNullOrEmpty(reversedSuffix))
        {
            hasReversed = auras.HasAnyActiveAura(
                positiveChargeName + reversedSuffix,
                negativeChargeName + reversedSuffix);
        }

        return (positiveCharge, negativeCharge, hasReversed);
    }

    public bool CheckAuraThreshold(IScriptSelfAuras auras, string auraName, int threshold, string comparison = ">=")
    {
        int value = auras.GetAuraValue(auraName);
        return comparison switch
        {
            "<" => value < threshold,
            ">" => value > threshold,
            "<=" => value <= threshold,
            ">=" => value >= threshold,
            "==" => value == threshold,
            "!=" => value != threshold,
            _ => throw new ArgumentException($"Invalid comparison operator: {comparison}")
        };
    }

    public List<Aura> GetAurasMatchingPattern(List<Aura> auras, string namePattern)
    {
        if (namePattern.Contains('*'))
        {
            Regex regex = new Regex(
                "^" + namePattern.Replace("*", ".*") + "$",
                RegexOptions.IgnoreCase);
            return auras.Where(a => regex.IsMatch(a.Name)).ToList();
        }
        else
        {
            return auras.Where(a => a.Name.Equals(namePattern, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    public bool ShouldUseSkill(IScriptSelfAuras selfAuras, Dictionary<string, Func<int, bool>> conditions)
    {
        return conditions.All(condition =>
        {
            int auraValue = selfAuras.GetAuraValue(condition.Key);
            return condition.Value(auraValue);
        });
    }

    public bool ShouldUseSkill(IScriptTargetAuras targetAuras, Dictionary<string, Func<int, bool>> conditions)
    {
        return conditions.All(condition =>
        {
            int auraValue = targetAuras.GetAuraValue(condition.Key);
            return condition.Value(auraValue);
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisableCounterAttack();
        _disposed = true;
    }
}