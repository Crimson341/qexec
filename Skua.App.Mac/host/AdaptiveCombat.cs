using Skua.Core;
using Skua.Core.Interfaces;
using Skua.Core.Models.Skills;

namespace Skua.Mac;

public sealed class AdaptiveCombat(IScriptInterface bot, IAdvancedSkillContainer profiles, Rpc rpc)
{
    private CancellationTokenSource? cancellation;
    private Task? loop;
    public bool Enabled => loop is { IsCompleted: false };
    public void Start()
    {
        if (Enabled) return;
        if (!bot.Player.LoggedIn || !bot.Player.Alive) throw new InvalidOperationException("Log in with a living character before enabling auto attack.");
        cancellation?.Dispose();
        cancellation = new();
        loop = Run(cancellation.Token);
    }
    public async Task Stop()
    {
        cancellation?.Cancel();
        if (loop != null) await loop;
        cancellation?.Dispose(); cancellation = null; loop = null;
    }
    private async Task Run(CancellationToken token)
    {
        string? lastKey = null;
        bool defending = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!bot.Player.LoggedIn || !bot.Player.Alive) break;
                string name = bot.Player.CurrentClass?.Name ?? "Unknown";
                int hp = bot.Player.Health, maximum = bot.Player.MaxHealth;
                defending = CombatPolicy.NeedsDefense(hp, maximum, defending);
                var target = bot.Player.Target;
                if (target == null || !target.Alive)
                    target = bot.Monsters.CurrentMonsters.FirstOrDefault(m => m.Alive && m.Cell == bot.Player.Cell);
                bool boss = target != null && CombatPolicy.IsBoss(target.MaxHP, maximum);
                var profile = CombatPolicy.Select(profiles.LoadedSkills, name, defending, boss);
                string key = name + ":" + (profile?.ClassUseMode.ToString() ?? "Basic");
                if (key != lastKey)
                {
                    bot.Skills.Stop();
                    token.ThrowIfCancellationRequested();
                    if (profile != null) bot.Skills.LoadAdvanced(name, false, profile.ClassUseMode);
                    else bot.Skills.LoadAdvanced("0", 100, SkillUseMode.UseIfAvailable);
                    bot.Skills.Start();
                    lastKey = key;
                }
                token.ThrowIfCancellationRequested();
                if (target != null && !bot.Combat.StopAttacking) bot.Combat.Attack(target.MapID);
                rpc.Send(new { type = "auto-status", enabled = true, className = name,
                    mode = profile?.ClassUseMode.ToString() ?? "Basic attack only", health = hp, maximum,
                    target = target?.Name ?? "Waiting for a target", encounter = boss ? "Boss estimate" : "Mob",
                    defense = defending, supported = profile != null });
                await Task.Delay(750, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { rpc.Send(new { type = "log", kind = "Error", message = "Auto attack stopped: " + ex.GetBaseException().Message }); }
        finally
        {
            try { bot.Skills.Stop(); bot.Combat.CancelAutoAttack(); }
            catch (Exception ex) { rpc.Send(new { type = "log", kind = "Error", message = "Combat cleanup: " + ex.GetBaseException().Message }); }
            rpc.Send(new { type = "auto-status", enabled = false });
        }
    }
}
