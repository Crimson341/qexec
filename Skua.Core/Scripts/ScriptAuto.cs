using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Skills;

namespace Skua.Core.Scripts;

public partial class ScriptAuto : ObservableObject, IScriptAuto
{
    public ScriptAuto(
        ILogService logger,
        Lazy<IScriptPlayer> player,
        Lazy<IScriptDrop> drops,
        Lazy<IScriptSkill> skills,
        Lazy<IScriptBoost> boosts,
        Lazy<IScriptOption> options,
        Lazy<IScriptMonster> monsters,
        Lazy<IScriptKill> kill,
        Lazy<IScriptWait> wait,
        Lazy<IScriptCombat> lazyCombat,
        Lazy<IScriptMap> lazyMap)
    {
        _logger = logger;
        _lazyPlayer = player;
        _lazyDrops = drops;
        _lazySkills = skills;
        _lazyBoosts = boosts;
        _lazyOptions = options;
        _lazyMonsters = monsters;
        _lazyKill = kill;
        _lazyWait = wait;
        _lazyCombat = lazyCombat;
        _lazyMap = lazyMap;
        _lastHuntTick = Environment.TickCount;
    }

    private readonly ILogService _logger;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly Lazy<IScriptDrop> _lazyDrops;
    private readonly Lazy<IScriptSkill> _lazySkills;
    private readonly Lazy<IScriptBoost> _lazyBoosts;
    private readonly Lazy<IScriptOption> _lazyOptions;
    private readonly Lazy<IScriptKill> _lazyKill;
    private readonly Lazy<IScriptMonster> _lazyMonsters;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IScriptCombat> _lazyCombat;
    private readonly Lazy<IScriptMap> _lazyMap;
    private IScriptPlayer Player => _lazyPlayer.Value;
    private IScriptDrop Drops => _lazyDrops.Value;
    private IScriptSkill Skills => _lazySkills.Value;
    private IScriptBoost Boosts => _lazyBoosts.Value;
    private IScriptOption Options => _lazyOptions.Value;
    private IScriptKill Kill => _lazyKill.Value;
    private IScriptMonster Monsters => _lazyMonsters.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IScriptCombat Combat => _lazyCombat.Value;
    private IScriptMap Map => _lazyMap.Value;

    [ObservableProperty]
    private bool _isRunning;

    private Task? _autoTask;
    private CancellationTokenSource? _ctsAuto;

    public void StartAutoAttack(string? className = null, ClassUseMode classUseMode = ClassUseMode.Base, int[]? manualMapIDs = null)
    {
        _ctsAuto = new CancellationTokenSource();
        _DoActionAuto(hunt: false, className, classUseMode, manualMapIDs);
    }

    public void StartAutoHunt(string? className = null, ClassUseMode classUseMode = ClassUseMode.Base, int[]? manualMapIDs = null)
    {
        _ctsAuto = new CancellationTokenSource();
        _DoActionAuto(hunt: true, className, classUseMode, manualMapIDs);
    }

    public void Stop()
    {
        if (_ctsAuto is null)
        {
            IsRunning = false;
            return;
        }

        _ctsAuto?.Cancel();
        _autoTask?.Wait();
        Wait.ForTrue(() => _ctsAuto is null, 20);
        _autoTask?.Dispose();
        IsRunning = false;
    }

    public async ValueTask StopAsync()
    {
        if (_ctsAuto is null)
        {
            IsRunning = false;
            return;
        }

        _ctsAuto?.Cancel();
        await Wait.ForTrueAsync(() => _ctsAuto is null && (_autoTask?.IsCompleted ?? true), 40);
        _autoTask?.Dispose();
        IsRunning = false;
    }

    private void _DoActionAuto(bool hunt, string? className = null, ClassUseMode classUseMode = ClassUseMode.Base, int[]? manualMapIDs = null)
    {
        if (_autoTask is { IsCompleted: false })
            return;

        if (!Player.LoggedIn)
            return;

        CheckDropsandBoosts();
        Options.InfiniteRange = true;
        if (className is not null)
            Skills.StartAdvanced(className, true, classUseMode);
        else
            Skills.StartAdvanced(Player.CurrentClass?.Name ?? "Generic", true);

        _autoTask = Task.Run(async () =>
        {
            try
            {
                if (hunt)
                    await _Hunt(_ctsAuto!.Token, manualMapIDs);
                else
                    await _Attack(_ctsAuto!.Token, manualMapIDs);
            }
            catch { /* ignored */ }
            finally
            {
                Drops.Stop();
                Skills.Stop();
                Boosts.Stop();
                Options.InfiniteRange = false;
                _ctsAuto?.Dispose();
                _ctsAuto = null;
                IsRunning = false;
            }
        });
        IsRunning = true;
    }

    private string _target = "";
    private int _targetMapID = -1;
    private int[]? _priorityMapIDs = null;

    private async Task _Attack(CancellationToken token, int[]? manualMapIDs = null)
    {
        Trace.WriteLine("Auto attack started.");
        Player.SetSpawnPoint();

        _priorityMapIDs = manualMapIDs;
        
        if (manualMapIDs?.Length > 0)
        {
            // Use manually specified MapIDs with priority
            _target = $"MapIDs:[{string.Join(", ", manualMapIDs)}]";
            _targetMapID = -1; // Will be determined by priority logic
        }
        else if (Player.HasTarget && !Combat.StopAttacking)
        {
            _target = Player.Target?.Name ?? "*";
            _targetMapID = Player.Target?.MapID ?? -1;
        }
        else if (!Combat.StopAttacking)
        {
            _target = "*";
            _targetMapID = -1;
        }

        _logger.ScriptLog($"[Auto Attack] Attacking {_target}");

        // Fast path flag for wildcard mode (no manual IDs, no specific target, target is "*")
        bool fastWildcard = (_priorityMapIDs is null || _priorityMapIDs.Length == 0) && _targetMapID <= 0 && (string.IsNullOrEmpty(_target) || _target == "*");

        while (!token.IsCancellationRequested)
        {
            if (_priorityMapIDs?.Length > 0)
            {
                // Priority MapID targeting - kill first target completely before switching
                int? currentTarget = null;
                
                // Always prefer the first priority if it exists
                var firstMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _priorityMapIDs[0]);
                bool firstAlive = firstMonster != null && firstMonster.Alive;
                _logger.ScriptLog($"[Priority Debug] Checking MapID {_priorityMapIDs[0]}: Monster={firstMonster?.Name}, Alive={firstAlive}");
                
                if (firstAlive)
                {
                    currentTarget = _priorityMapIDs[0];
                    _logger.ScriptLog($"[Priority Debug] Targeting first priority: {currentTarget}");
                }
                else
                {
                    _logger.ScriptLog($"[Priority Debug] First priority {_priorityMapIDs[0]} is dead, checking alternatives...");
                    // First priority is dead, find next available
                    for (int i = 1; i < _priorityMapIDs.Length; i++)
                    {
                        var altMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _priorityMapIDs[i]);
                        bool altAlive = altMonster != null && altMonster.Alive;
                        _logger.ScriptLog($"[Priority Debug] Checking alternative MapID {_priorityMapIDs[i]}: Monster={altMonster?.Name}, Alive={altAlive}");
                        if (altAlive)
                        {
                            currentTarget = _priorityMapIDs[i];
                            _logger.ScriptLog($"[Priority Debug] Switched to alternative: {currentTarget}");
                            break;
                        }
                    }
                }
                
                if (currentTarget.HasValue)
                {
                    if (Combat.Attack(currentTarget.Value))
                        Kill.Monster(currentTarget.Value, token);

                    // Immediately re-evaluate priorities (no extra delay)
                    continue;
                }
                // No valid target found right now, short yield
                Thread.Sleep(50);
            }
            else if (_targetMapID > 0)
            {
                // Attack specific monster by MapID only
                if (Monsters.Exists(_targetMapID))
                {
                    if (Combat.Attack(_targetMapID))
                        Kill.Monster(_targetMapID, token);
                }
                Thread.Sleep(Options.ActionDelay);
            }
            else if (_target != "*" && _target != "")
            {
                // Target is a player (like yourself)
                Combat.AttackPlayer(_target);
                Thread.Sleep(Options.ActionDelay);
            }
            else
            {
                if (fastWildcard)
                {
                    // Fast wildcard: only alive monsters, break after first successful engage
                    foreach (var monster in Monsters.MapMonsters)
                    {
                        if (token.IsCancellationRequested)
                            return;
                        if (!monster.Alive)
                            continue;
                        if (!Combat.Attack(monster.MapID))
                            continue;
                        Kill.Monster(monster.MapID, token);
                        break;
                    }
                    continue;
                }

                // Generic wildcard fallback
                List<Monster> monsters = Monsters.CurrentMonsters;
                foreach (Monster monster in monsters)
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (!Combat.Attack(monster.MapID))
                        continue;

                    Kill.Monster(monster.MapID, token);
                }
            }
        }
        Trace.WriteLine("Auto attack stopped.");
    }

    private int _lastHuntTick;

    private async Task _Hunt(CancellationToken token, int[]? manualMapIDs = null)
    {
        Trace.WriteLine("Auto hunt started.");

        _priorityMapIDs = manualMapIDs;
        
        if (manualMapIDs?.Length > 0)
        {
            // Use manually specified MapIDs with priority
            _target = $"MapIDs:[{string.Join(", ", manualMapIDs)}]";
            _targetMapID = -1; // Will be determined by priority logic
        }
        else if (Player.HasTarget && !Combat.StopAttacking)
        {
            _target = Player.Target?.Name ?? "*";
            _targetMapID = Player.Target?.MapID ?? -1;
        }
        else if (!Combat.StopAttacking)
        {
            List<string> monsters = Monsters.CurrentMonsters.Select(m => m.Name).ToList();
            _target = string.Join('|', monsters);
            _targetMapID = -1;
        }

        _logger.ScriptLog($"[Auto Hunt] Hunting for {_target}");
        
        if (_priorityMapIDs?.Length > 0)
        {
            // Hunt with priority MapIDs
            while (!token.IsCancellationRequested)
            {
                // Find highest priority available monster - first priority takes precedence
                int? currentTarget = null;
                
                // Always prefer the first priority if it exists AND is alive
                var firstMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _priorityMapIDs[0]);
                bool firstAlive = firstMonster != null && firstMonster.Alive;
                _logger.ScriptLog($"[Hunt Priority Debug] Checking MapID {_priorityMapIDs[0]}: Monster={firstMonster?.Name}, Alive={firstAlive}");
                
                if (firstAlive)
                {
                    currentTarget = _priorityMapIDs[0];
                    _logger.ScriptLog($"[Hunt Priority Debug] Targeting first priority: {currentTarget}");
                }
                else
                {
                    _logger.ScriptLog($"[Hunt Priority Debug] First priority {_priorityMapIDs[0]} is dead, checking alternatives...");
                    // First priority is dead, find next available
                    for (int i = 1; i < _priorityMapIDs.Length; i++)
                    {
                        var altMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _priorityMapIDs[i]);
                        bool altAlive = altMonster != null && altMonster.Alive;
                        _logger.ScriptLog($"[Hunt Priority Debug] Checking alternative MapID {_priorityMapIDs[i]}: Monster={altMonster?.Name}, Alive={altAlive}");
                        if (altAlive)
                        {
                            currentTarget = _priorityMapIDs[i];
                            _logger.ScriptLog($"[Hunt Priority Debug] Switched to alternative: {currentTarget}");
                            break;
                        }
                    }
                }
                
                if (currentTarget.HasValue)
                {
                    // Hunt the current priority target
                    List<string> cells = Monsters.GetLivingMonsterDataLeafCells(currentTarget.Value);
                    
                    foreach (string cell in cells)
                    {
                        if (token.IsCancellationRequested)
                            break;
                            
                        if (Player.Cell != cell && !token.IsCancellationRequested)
                        {
                            if (Environment.TickCount - _lastHuntTick < Options.HuntDelay)
                                Thread.Sleep(Options.HuntDelay - Environment.TickCount + _lastHuntTick);
                            Map.Jump(cell, "Left");
                            _lastHuntTick = Environment.TickCount;
                        }
                        
                        if (Monsters.Exists(currentTarget.Value) && !token.IsCancellationRequested)
                        {
                        if (!Combat.Attack(currentTarget.Value))
                                continue;
                            Kill.Monster(currentTarget.Value, token);
                            // Immediately re-check priorities (no delay)
                            break;
                        }
                        
                        Thread.Sleep(200);
                    }
                }
                else
                {
                    // No priority targets available, wait a bit
                    Thread.Sleep(500);
                }
            }
        }
        else if (_targetMapID > 0)
        {
            // Hunt specific monster by MapID only
            while (!token.IsCancellationRequested)
            {
                List<string> cells = Monsters.GetLivingMonsterDataLeafCells(_targetMapID);
                
                foreach (string cell in cells)
                {
                    if (token.IsCancellationRequested)
                        break;
                        
                    if (Player.Cell != cell && !token.IsCancellationRequested)
                    {
                        if (Environment.TickCount - _lastHuntTick < Options.HuntDelay)
                            Thread.Sleep(Options.HuntDelay - Environment.TickCount + _lastHuntTick);
                        Map.Jump(cell, "Left");
                        _lastHuntTick = Environment.TickCount;
                    }
                    
                    if (Monsters.Exists(_targetMapID) && !token.IsCancellationRequested)
                    {
                        if (!Combat.Attack(_targetMapID))
                            continue;
                        Thread.Sleep(Options.ActionDelay);
                        Kill.Monster(_targetMapID, token);
                        return;
                    }
                    
                    Thread.Sleep(200);
                }
            }
        }
        else
        {
            // Hunt all monsters (wildcard mode)
            string[] names = _target.Split('|');
            List<string> cells = names.SelectMany(n => Monsters.GetLivingMonsterDataLeafCells(n)).Distinct().ToList();
            
            while (!token.IsCancellationRequested)
            {
                for (int i = cells.Count - 1; i >= 0; i--)
                {
                    if (Player.Cell != cells[i] && !token.IsCancellationRequested)
                    {
                        if (Environment.TickCount - _lastHuntTick < Options.HuntDelay)
                            Thread.Sleep(Options.HuntDelay - Environment.TickCount + _lastHuntTick);
                        Map.Jump(cells[i], "Left");
                        _lastHuntTick = Environment.TickCount;
                    }

                    foreach (string mon in names)
                    {
                        if (token.IsCancellationRequested)
                            break;

                        if (Monsters.Exists(mon) && !token.IsCancellationRequested)
                        {
                            if (!Combat.Attack(mon))
                            {
                                cells.RemoveAt(i);
                                continue;
                            }
                            Thread.Sleep(Options.ActionDelay);
                            Kill.Monster(mon, token);
                            break;
                        }

                        cells.RemoveAt(i);
                    }
                }
            }
        }
        Trace.WriteLine("Auto hunt stopped.");
    }

    private void CheckDropsandBoosts()
    {
        if (Drops.ToPickup.Any())
            Drops.Start();

        if (Boosts.UsingBoosts)
            Boosts.Start();
    }
}