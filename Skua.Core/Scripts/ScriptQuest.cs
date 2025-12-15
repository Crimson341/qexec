using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Newtonsoft.Json;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Models.Quests;
using Skua.Core.Utils;

namespace Skua.Core.Scripts;

public partial class ScriptQuest : ObservableRecipient, IScriptQuest
{
    public ScriptQuest(
        Lazy<IFlashUtil> flash,
        Lazy<IScriptWait> wait,
        Lazy<IScriptOption> options,
        Lazy<IScriptPlayer> player,
        Lazy<IScriptSend> send,
        Lazy<IScriptInventory> inventory,
        Lazy<IScriptTempInv> tempInv,
        Lazy<IScriptInventoryHelper> invHelper,
        Lazy<IScriptLite> lite,
        Lazy<IScriptMap> map,
        Lazy<IScriptDrop> drop)
    {
        _lazyFlash = flash;
        _lazyWait = wait;
        _lazyOptions = options;
        _lazyPlayer = player;
        _lazySend = send;
        _lazyInventory = inventory;
        _lazyInvHelper = invHelper;
        _lazyLite = lite;
        _lazyMap = map;
        _lazyDrop = drop;
    }

    private readonly Lazy<IFlashUtil> _lazyFlash;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IScriptOption> _lazyOptions;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly Lazy<IScriptSend> _lazySend;
    private readonly Lazy<IScriptInventory> _lazyInventory;
    private readonly Lazy<IScriptInventoryHelper> _lazyInvHelper;
    private readonly Lazy<IScriptLite> _lazyLite;
    private readonly Lazy<IScriptMap> _lazyMap;
    private readonly Lazy<IScriptDrop> _lazyDrop;
    private IFlashUtil Flash => _lazyFlash.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IScriptOption Options => _lazyOptions.Value;
    private IScriptPlayer Player => _lazyPlayer.Value;
    private IScriptSend Send => _lazySend.Value;
    private IScriptInventory Inventory => _lazyInventory.Value;
    private IScriptInventoryHelper InvHelper => _lazyInvHelper.Value;
    private IScriptLite Lite => _lazyLite.Value;
    private IScriptMap Map => _lazyMap.Value;
    private IScriptDrop Drop => _lazyDrop.Value;

    private Thread? _questThread;
    private CancellationTokenSource? _questsCTS;
    private readonly object _cacheLockObj = new();
    private volatile bool _cacheLoaded = false;

    public int RegisterCompleteInterval { get; set; } = 2000;

    [ObjectBinding("world.questTree", Default = "new()")]
    private Dictionary<int, Quest> _quests = new();

    public List<Quest> Tree => Quests.Values.ToList() ?? new();
    public List<Quest> Active => Tree.FindAll(x => x.Active);
    public List<Quest> Completed => Tree.FindAll(x => x.Status == "c");
    public List<QuestData> Cached { get; set; } = new();
    public Dictionary<int, QuestData> CachedDictionary { get; set; } = new();
    private SynchronizedList<int> _registered = new();
    private Dictionary<int, int> _registeredRewards = new();
    private Dictionary<int, int> _questCompleteCooldowns = new();
    private Dictionary<int, int> _questAcceptCooldowns = new();
    private readonly object _registeredLock = new();
    public IEnumerable<int> Registered => _registered.Items;
    public IReadOnlyDictionary<int, int> RegisteredRewards => _registeredRewards;

    public void Load(params int[] ids)
    {
        if (ids.Length < 30)
        {
            Flash.CallGameFunction("world.showQuests", ids.Select(id => id.ToString()).Join(','), "q");
            return;
        }

        foreach (int[] idChunks in ids.Chunk(30))
        {
            Flash.CallGameFunction("world.showQuests", idChunks.Select(id => id.ToString()).Join(','), "q");
            Thread.Sleep(Options.ActionDelay);
        }
    }

    public Quest? EnsureLoad(int id)
    {
        Wait.ForTrue(() => Tree.Contains(x => x.ID == id), () => Load(id), 20, 1000);
        return Tree.Find(q => q.ID == id)!;
    }

    public bool TryGetQuest(int id, out Quest? quest)
    {
        return (quest = Tree.Find(x => x.ID == id)) != null;
    }

    public bool Accept(int id)
    {
        if (Options.SafeTimings)
            Wait.ForActionCooldown(GameActions.AcceptQuest);
        Flash.CallGameFunction("world.acceptQuest", id);
        if (Options.SafeTimings)
            Wait.ForQuestAccept(id);
        return IsInProgress(id);
    }

    public void Accept(params int[] ids)
    {
        foreach (int t in ids)
        {
            Accept(t);
            Thread.Sleep(Options.ActionDelay);
        }
    }

    public bool EnsureAccept(int id)
    {
        for (int i = 0; i < Options.QuestAcceptAndCompleteTries; i++)
        {
            Accept(id);
            if (IsInProgress(id))
                break;
            Thread.Sleep(Options.ActionDelay);
        }
        return IsInProgress(id);
    }

    public void EnsureAccept(params int[] ids)
    {
        foreach (int t in ids)
        {
            EnsureAccept(t);
            Thread.Sleep(Options.ActionDelay);
        }
    }

    public bool Complete(int id, int itemId = -1, bool special = false)
    {
        if (Options.SafeTimings)
            Wait.ForActionCooldown(GameActions.TryQuestComplete);
        Flash.CallGameFunction("world.tryQuestComplete", id, itemId, special);
        if (Options.SafeTimings)
            Wait.ForQuestComplete(id);
        return !IsInProgress(id);
    }

    public void Complete(params int[] ids)
    {
        foreach (int t in ids)
        {
            Complete(t);
            Thread.Sleep(Options.ActionDelay);
        }
    }

    public bool EnsureComplete(int id, int itemId = -1, bool special = false)
    {
        _EnsureComplete(id, itemId, special);
        return !IsInProgress(id);
    }

    private void _EnsureComplete(int id, int itemId = -1, bool special = false)
    {
        if (id == 0)
            return;
        for (int i = 0; i < Options.QuestAcceptAndCompleteTries; i++)
        {
            Complete(id, itemId, special);
            if (!IsInProgress(id))
                break;
            Thread.Sleep(Options.ActionDelay);
        }
    }

    public void EnsureComplete(params int[] ids)
    {
        foreach (int t in ids)
        {
            EnsureComplete(t);
            Thread.Sleep(Options.ActionDelay);
        }
    }

    [MethodCallBinding("world.isQuestInProgress", GameFunction = true)]
    private bool _isInProgress(int id) => false;

    public bool UpdateQuest(int id)
    {
        Quest? quest = EnsureLoad(id);
        if (quest is null)
            return false;
        Send.ClientPacket("{\"t\":\"xt\",\"b\":{\"r\":-1,\"o\":{\"cmd\":\"updateQuest\",\"iValue\":" + quest.Value + ",\"iIndex\":" + quest.Slot + "}}}", "json");
        return true;
    }

    public void UpdateQuest(int value, int slot)
    {
        Send.ClientPacket("{\"t\":\"xt\",\"b\":{\"r\":-1,\"o\":{\"cmd\":\"updateQuest\",\"iValue\":" + value + ",\"iIndex\":" + slot + "}}}", "json");
    }

    public bool CanComplete(int id)
    {
        return Completed.Contains(q => q.ID == id);
    }

    public bool CanCompleteFullCheck(int id)
    {
        if (CanComplete(id))
            return true;

        Quest? quest = EnsureLoad(id);
        if (quest is null)
            return false;
        
        // Check if quest data is fully loaded (Requirements should not be null)
        if (quest.Requirements is null || quest.AcceptRequirements is null)
            return false;
            
        List<ItemBase> requirements = new();
        requirements.AddRange(quest.Requirements);
        requirements.AddRange(quest.AcceptRequirements);
        if (requirements.Count == 0)
            return true;
        return requirements.All(item => InvHelper.Check(item.ID, item.Quantity, false));
    }

    public bool IsDailyComplete(int id)
    {
        Quest? quest = EnsureLoad(id);
        return quest is not null && IsDailyComplete(quest);
    }

    public bool IsDailyComplete(Quest quest)
    {
        return Flash.CallGameFunction<int>("world.getAchievement", quest.Field, quest.Index) > 0;
    }

    public bool IsUnlocked(int id)
    {
        Quest? quest = EnsureLoad(id);
        return quest is not null && IsUnlocked(quest);
    }

    public bool IsUnlocked(Quest quest)
    {
        return quest.Slot < 0 || Flash.CallGameFunction<int>("world.getQuestValue", quest.Slot) >= quest.Value - 1;
    }

    public bool HasBeenCompleted(int id)
    {
        Quest? quest = EnsureLoad(id);
        return quest is not null && HasBeenCompleted(quest);
    }

    public bool HasBeenCompleted(Quest quest)
    {
        return quest.Slot < 0 || Flash.CallGameFunction<int>("world.getQuestValue", quest.Slot) >= quest.Value;
    }

    public bool IsAvailable(int id)
    {
        Quest? quest = EnsureLoad(id);
        return quest is not null
               && !IsDailyComplete(quest)
               && IsUnlocked(quest)
               && (!quest.Upgrade || Player.IsMember)
               && Player.Level >= quest.Level
               && (quest.RequiredClassID <= 0 || Flash.CallGameFunction<int>("world.myAvatar.getCPByID", quest.RequiredClassID) >= quest.RequiredClassPoints)
               && (quest.RequiredFactionId <= 1 || Flash.CallGameFunction<int>("world.myAvatar.getRep", quest.RequiredFactionId) >= quest.RequiredFactionRep)
               && quest.AcceptRequirements.All(r => Inventory.Contains(r.Name, r.Quantity));
    }

    public void RegisterQuests(params int[] ids)
    {
        RegisterQuests(ids.Select(id => (id, -1)).ToArray());
    }

    public void RegisterQuests(params (int questId, int rewardId)[] quests)
    {
        if (quests.Length == 0)
            return;
        
        lock (_registeredLock)
        {
            // Register quests immediately (without requirements)
            foreach (var (questId, rewardId) in quests)
            {
                if (!_registered.Items.Contains(questId))
                {
                    _registered.Add(questId);
                }
                _registeredRewards[questId] = rewardId;
                
                // Add reward item to drops if specified
                if (rewardId > 0)
                {
                    Drop.Add(rewardId);
                }
            }
        }
        
        OnPropertyChanged(nameof(Registered));
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<IEnumerable<int>>(this, nameof(Registered), Registered, Registered));
        
        // Load quest data and add requirements in background
        Task.Run(() =>
        {
            foreach (var (questId, rewardId) in quests)
            {
                try
                {
                    Quest? questData = EnsureLoad(questId);
                    if (questData != null)
                    {
                        // Add Requirements
                        if (questData.Requirements != null && questData.Requirements.Any())
                        {
                            int[] requirementIds = questData.Requirements
                                .Where(r => r != null && !r.Temp)
                                .Select(r => r.ID)
                                .ToArray();
                            if (requirementIds.Any())
                                Drop.Add(requirementIds);
                        }
                        
                        // Add AcceptRequirements
                        if (questData.AcceptRequirements != null && questData.AcceptRequirements.Any())
                        {
                            int[] acceptReqIds = questData.AcceptRequirements
                                .Where(r => r != null && !r.Temp)
                                .Select(r => r.ID)
                                .ToArray();
                            if (acceptReqIds.Any())
                                Drop.Add(acceptReqIds);
                        }
                    }
                }
                catch { /* Quest failed to load, ignore */ }
            }
        });
        
        OnPropertyChanged(nameof(Registered));
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<IEnumerable<int>>(this, nameof(Registered), Registered, Registered));
        if (!_questThread?.IsAlive ?? true)
        {
            _questThread = new(async () =>
            {
                _questsCTS = new();
                try
                {
                    await _Poll(_questsCTS.Token);
                }
                catch { /* ignored */ }
                _questsCTS?.Dispose();
                _questsCTS = null;
            })
            {
                Name = "Quest Thread"
            };
            _questThread.Start();
        }
    }

    public void UnregisterQuests(params int[] ids)
    {
        _registered.Remove(ids.Contains);
        foreach (int id in ids)
        {
            _registeredRewards.Remove(id);
            _questCompleteCooldowns.Remove(id);
            _questAcceptCooldowns.Remove(id);
        }
        OnPropertyChanged(nameof(Registered));
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<IEnumerable<int>>(this, nameof(Registered), Registered, Registered));
    }

    public void UnregisterAllQuests()
    {
        _registered.Clear();
        _registeredRewards.Clear();
        _questCompleteCooldowns.Clear();
        _questAcceptCooldowns.Clear();
        OnPropertyChanged(nameof(Registered));
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<IEnumerable<int>>(this, nameof(Registered), Registered, Registered));
        if (_questThread?.IsAlive ?? false)
        {
            _questsCTS?.Cancel();
            Wait.ForTrue(() => _questsCTS is null, 20);
        }
    }

    private async Task _Poll(CancellationToken token)
    {
        _lastComplete = Environment.TickCount;
        while (!token.IsCancellationRequested)
        {
            if (Player.Playing)
                await _CompleteQuest(_registered.Items, token).ConfigureAwait(false);
            await Task.Delay(RegisterCompleteInterval, token);
        }
    }

    private int _lastComplete;

    private async Task _CompleteQuest(IEnumerable<int> registered, CancellationToken token)
    {
        foreach (int quest in registered)
        {
            // Check cooldown - don't try to complete if we just tried within the last 3 seconds
            int currentTick = Environment.TickCount;
            if (_questCompleteCooldowns.TryGetValue(quest, out int lastAttempt))
            {
                if (currentTick - lastAttempt < 3000)
                    continue;
            }
            
            if (!IsInProgress(quest))
            {
                // Check accept cooldown - don't try to accept if we just tried within the last 2 seconds
                if (_questAcceptCooldowns.TryGetValue(quest, out int lastAccept))
                {
                    if (currentTick - lastAccept < 2000)
                        continue;
                }
                
                Wait.ForActionCooldown(GameActions.AcceptQuest);
                Accept(quest);
                _questAcceptCooldowns[quest] = Environment.TickCount;
                await Task.Delay(Options.ActionDelay, token);
            }
            
            if (!CanComplete(quest))
                continue;

            Quest? questData = Tree.Find(q => q.ID == quest);
            if (questData == null)
                continue;
                
            int turnIns = questData.Once || !string.IsNullOrEmpty(questData.Field) ? 1 :
                Flash.CallGameFunction<int>("world.maximumQuestTurnIns", quest);
            
            int rewardId = _registeredRewards.TryGetValue(quest, out int reward) ? reward : -1;
                
            Wait.ForActionCooldown(GameActions.TryQuestComplete);
            
            Send.Packet($"%xt%zm%tryQuestComplete%{Map.RoomID}%{quest}%{rewardId}%false%{turnIns}%wvz%");
            
            // Set cooldown immediately after sending packet
            _questCompleteCooldowns[quest] = Environment.TickCount;
            
            await Task.Delay(Options.ActionDelay, token);
            
            // Only re-accept if ReacceptQuest is disabled (otherwise game will auto-accept)
            if (!IsInProgress(quest) && !Lite.ReacceptQuest)
            {
                // Check accept cooldown - don't try to accept if we just tried within the last 2 seconds
                if (_questAcceptCooldowns.TryGetValue(quest, out int lastAccept))
                {
                    if (Environment.TickCount - lastAccept < 2000)
                        continue;
                }
                
                Wait.ForActionCooldown(GameActions.AcceptQuest);
                Accept(quest);
                _questAcceptCooldowns[quest] = Environment.TickCount;
                await Task.Delay(Options.ActionDelay, token);
            }
            _lastComplete = Environment.TickCount;
        }
    }

    public void LoadCachedQuests()
    {
        if (_cacheLoaded)
            return;

        lock (_cacheLockObj)
        {
            if (_cacheLoaded)
                return;

            string skuaQuestFile = File.ReadAllText(ClientFileSources.SkuaQuestsFile);
            Cached = JsonConvert.DeserializeObject<List<QuestData>>(skuaQuestFile) ?? new();
            CachedDictionary = Cached.ToDictionary(x => x.ID, x => x);
            _cacheLoaded = true;
        }
    }

    public List<QuestData> GetCachedQuests(int start, int count)
    {
        if (!_cacheLoaded)
            LoadCachedQuests();

        return Cached.Skip(start).Take(count).ToList();
    }

    public List<QuestData> GetCachedQuests(params int[] ids)
    {
        if (!_cacheLoaded)
            LoadCachedQuests();

        List<QuestData> quests = new();
        foreach (int id in ids)
        {
            if (CachedDictionary.TryGetValue(id, out QuestData? value))
                quests.Add(value);
        }
        return quests;
    }
}