using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models.Quests;
using Skua.Core.Utils;

namespace Skua.Core.ViewModels;

public partial class LoaderViewModel : BotControlViewModelBase, IManagedWindow
{
    public LoaderViewModel(IScriptShop shops, IScriptQuest quests, IQuestDataLoaderService questLoader, IClipboardService clipboardService)
        : base("Loader", 550, 270)
    {
        _shops = shops;
        _quests = quests;
        _questLoader = questLoader;
        _clipboardService = clipboardService;
    }

    private CancellationTokenSource? _loaderCTS;
    private readonly IScriptShop _shops;
    private readonly IScriptQuest _quests;
    private readonly IQuestDataLoaderService _questLoader;
    private readonly IClipboardService _clipboardService;

    [ObservableProperty]
    private string _progressReport = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private string _inputIDs = string.Empty;

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private RangedObservableCollection<QuestData> _questIDs = new();

    [RelayCommand(CanExecute = nameof(AllDigits))]
    private void Load()
    {
        if (SelectedIndex == 0 && int.TryParse(InputIDs, out int id))
        {
            Task.Factory.StartNew(() => _shops.Load(id));
            return;
        }
        if (SelectedIndex == 1)
        {
            string[] parts = InputIDs.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int[] questIds = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                questIds[i] = int.Parse(parts[i]);
            Task.Factory.StartNew(() => _quests.Load(questIds));
        }
    }

    private bool AllDigits()
    {
        return InputIDs.Replace(",", "").Replace(" ", "").All(c => int.TryParse(c + "", out int i));
    }

    [RelayCommand]
    private void LoadQuests(IList<object>? items)
    {
        if (items is null)
            return;
        int[] questIds = new int[items.Count];
        for (int i = 0; i < items.Count; i++)
            questIds[i] = ((QuestData)items[i]).ID;
        _quests.Load(questIds);
    }

    [RelayCommand]
    private void CopyQuestsNames(IList<object>? items)
    {
        if (items is null)
            return;

        List<string> names = new(items.Count);
        foreach (object item in items)
            names.Add(((QuestData)item).Name);
        _clipboardService.SetText(string.Join(",", names));
    }

    [RelayCommand]
    private void CopyQuestsIDs(IList<object>? items)
    {
        if (items is null)
            return;

        List<int> ids = new(items.Count);
        foreach (object item in items)
            ids.Add(((QuestData)item).ID);
        _clipboardService.SetText(string.Join(",", ids));
    }

    [RelayCommand]
    private async Task UpdateQuests(bool getAll)
    {
        _loaderCTS = new();
        QuestIDs.Clear();
        
        // Clear cache to ensure fresh data when updating
        if (getAll)
            _questLoader.ClearCache();
            
        Progress<string> progress = new(progress =>
        {
            IsLoading = true;
            ProgressReport = progress;
        });
        List<QuestData> questData = await _questLoader.UpdateAsync("QuestData.json", getAll, progress, _loaderCTS.Token);
        QuestIDs.Clear();
        QuestIDs.AddRange(questData);
        IsLoading = false;
        ProgressReport = string.Empty;
        _loaderCTS.Dispose();
        _loaderCTS = null;
    }

    [RelayCommand]
    private async Task GetQuests()
    {
        IsLoading = true;
        ProgressReport = "Getting quests";
        QuestIDs.Clear();
        QuestIDs.AddRange(await _questLoader.GetFromFileAsync("QuestData.json"));
        ProgressReport = string.Empty;
        IsLoading = false;
    }

    [RelayCommand]
    private void CancelQuestLoad()
    {
        if (_loaderCTS is not null)
        {
            _loaderCTS.Cancel();
            ProgressReport = "Cancelling task...";
        }
    }
}