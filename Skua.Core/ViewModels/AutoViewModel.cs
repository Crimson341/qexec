using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models.Items;
using Skua.Core.Models.Skills;

namespace Skua.Core.ViewModels;

public partial class AutoViewModel : BotControlViewModelBase, IDisposable
{
    private CancellationTokenSource? _autoCts;

    public AutoViewModel(IScriptAuto auto, IScriptInventory inventory, IAdvancedSkillContainer advancedSkills)
        : base("Auto Attack")
    {
        StrongReferenceMessenger.Default.Register<AutoViewModel, StopAutoMessage>(this, async (r, m) => await r.StopAutoAsync());
        StrongReferenceMessenger.Default.Register<AutoViewModel, StartAutoAttackMessage>(this, async (r, m) => await r.StartAutoAttack());
        StrongReferenceMessenger.Default.Register<AutoViewModel, StartAutoHuntMessage>(this, async (r, m) => await r.StartAutoHunt());

        Auto = auto;
        _inventory = inventory;
        _advancedSkills = advancedSkills;
        StopAutoAsyncCommand = new AsyncRelayCommand(StopAutoAsync);
    }

    private readonly IScriptInventory _inventory;
    private readonly IAdvancedSkillContainer _advancedSkills;

    [ObservableProperty]
    private ClassUseMode? _selectedClassMode;

    [ObservableProperty]
    private string? _selectedClassModeString;

    async partial void OnSelectedClassStringChanged(string? value)
    {
        await EquipSelectedClassAsync();
    }

    async partial void OnSelectedClassModeStringChanged(string? value)
    {
        await LoadSelectedClassMode();
    }

    public IScriptAuto Auto { get; }
    public List<string>? PlayerClasses => _inventory.Items?.Where(i => i.Category == ItemCategory.Class).Select(i => i.Name).ToList();

    [ObservableProperty]
    private List<string> _currentClassModeStrings = new();

    [ObservableProperty]
    private string? _selectedClassString;

    public string? SelectedClass
    {
        get { return _selectedClassString; }
        set
        {
            if (SetProperty(ref _selectedClassString, value) && value is not null)
            {
                CurrentClassModes = new();
                CurrentClassModeStrings = new List<string>();
                CurrentClassModes.AddRange(_advancedSkills.LoadedSkills.Where(s => s.ClassName == _selectedClassString).Select(s => s.ClassUseMode));

                var classModes = _advancedSkills.GetAvailableClassModes();
                if (classModes.TryGetValue(_selectedClassString, out var modes))
                {
                    CurrentClassModeStrings = new List<string>(modes.OrderBy(x => x));
                }

                OnPropertyChanged(nameof(CurrentClassModes));

                if (CurrentClassModes.Any())
                {
                    SelectedClassMode = CurrentClassModes.First();
                }
                if (CurrentClassModeStrings.Count > 0 && SelectedClassModeString == null)
                {
                    SelectedClassModeString = CurrentClassModeStrings.First();
                }
                
                OnSelectedClassStringChanged(_selectedClassString);
            }
        }
    }
    private async Task EquipSelectedClassAsync()
    {
        try
        {
            await Task.Run(() => _inventory.EquipItem(_selectedClassString)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    public List<ClassUseMode>? CurrentClassModes { get; private set; }
    public IAsyncRelayCommand StopAutoAsyncCommand { get; }

    [RelayCommand]
    private void ReloadClasses()
    {
        OnPropertyChanged(nameof(PlayerClasses));

        CurrentClassModes = null;
        CurrentClassModeStrings = new List<string>();
        SelectedClass = null;
        SelectedClassMode = null;
        SelectedClassModeString = null;
    }

    private async Task LoadSelectedClassMode()
    {
        if (string.IsNullOrEmpty(SelectedClassModeString))
            return;

        var skill = _advancedSkills.GetClassModeSkills(_selectedClassString, SelectedClassModeString);
        if (skill != null)
        {
            SelectedClassMode = skill.ClassUseMode;
        }
    }

    [RelayCommand]
    private async Task StartAutoHunt()
    {
        _autoCts?.Cancel();
        _autoCts?.Dispose();
        _autoCts = new CancellationTokenSource();

        if (_selectedClassString is not null && _selectedClassMode is not null)
        {
            await Task.Factory.StartNew(
                () => Auto.StartAutoHunt(_selectedClassString, (ClassUseMode)_selectedClassMode),
                _autoCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            return;
        }

        await Task.Factory.StartNew(
            () => Auto.StartAutoHunt(),
            _autoCts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    [RelayCommand]
    private async Task StartAutoAttack()
    {
        _autoCts?.Cancel();
        _autoCts?.Dispose();
        _autoCts = new CancellationTokenSource();

        if (_selectedClassString is not null && _selectedClassMode is not null)
        {
            await Task.Factory.StartNew(
                () => Auto.StartAutoAttack(_selectedClassString, (ClassUseMode)_selectedClassMode),
                _autoCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            return;
        }

        await Task.Factory.StartNew(
            () => Auto.StartAutoAttack(),
            _autoCts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private async Task StopAutoAsync()
    {
        _autoCts?.Cancel();
        await Auto.StopAsync();
        _autoCts?.Dispose();
        _autoCts = null;
    }

    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _autoCts?.Cancel();
                _autoCts?.Dispose();
                StrongReferenceMessenger.Default.UnregisterAll(this);
            }

            _disposed = true;
        }
    }

    ~AutoViewModel()
    {
        Dispose(false);
    }
}