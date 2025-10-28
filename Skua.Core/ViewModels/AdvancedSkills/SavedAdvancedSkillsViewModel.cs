using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models.Skills;
using System.Collections.ObjectModel;

namespace Skua.Core.ViewModels;

public class ClassModeItem
{
    public string ClassName { get; set; } = "";
    public string Mode { get; set; } = "";
    public override string ToString() => $"{ClassName} ({Mode})";
}

public partial class SavedAdvancedSkillsViewModel : ObservableRecipient
{
    public SavedAdvancedSkillsViewModel(IAdvancedSkillContainer advancedSkillContainer)
    {
        _advancedSkillContainer = advancedSkillContainer;
        RefreshSkillsCommand = new RelayCommand(_advancedSkillContainer.LoadSkills);
        ResetSkillsSetCommand = new RelayCommand(_advancedSkillContainer.ResetSkillsSets);
    }

    protected override void OnActivated()
    {
        Messenger.Register<SavedAdvancedSkillsViewModel, SaveAdvancedSkillMessage>(this, SaveSkill);
        Messenger.Register<SavedAdvancedSkillsViewModel, PropertyChangedMessage<List<AdvancedSkill>>>(this, AdvancedSkillsChanged);
    }

    private readonly IAdvancedSkillContainer _advancedSkillContainer;

    [ObservableProperty]
    private AdvancedSkill? _selectedSkill;

    [ObservableProperty]
    private ObservableCollection<string> _availableClasses = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableModes = new();

    [ObservableProperty]
    private string? _selectedClassName;

    [ObservableProperty]
    private string? _selectedMode;

    private ObservableCollection<AdvancedSkill>? _loadedSkills;

    public ObservableCollection<AdvancedSkill> LoadedSkills
    {
        get
        {
            _loadedSkills ??= new ObservableCollection<AdvancedSkill>(_advancedSkillContainer.LoadedSkills);
            return _loadedSkills;
        }
        set
        {
            _loadedSkills = value;
            OnPropertyChanged(nameof(LoadedSkills));
        }
    }

    public IRelayCommand RefreshSkillsCommand { get; }
    public IRelayCommand ResetSkillsSetCommand { get; }

    [RelayCommand]
    private void LoadAvailableClasses()
    {
        AvailableClasses.Clear();
        var classModeDictionary = _advancedSkillContainer.GetAvailableClassModes();
        foreach (var className in classModeDictionary.Keys.OrderBy(x => x))
        {
            AvailableClasses.Add(className);
        }
    }

    partial void OnSelectedClassNameChanged(string? value)
    {
        if (value == null)
        {
            AvailableModes.Clear();
            return;
        }

        AvailableModes.Clear();
        var classModeDictionary = _advancedSkillContainer.GetAvailableClassModes();
        if (classModeDictionary.TryGetValue(value, out var modes))
        {
            foreach (var mode in modes.OrderBy(x => x))
            {
                AvailableModes.Add(mode);
            }
        }
        SelectedMode = null;
    }

    [RelayCommand]
    private void LoadSelectedClassMode()
    {
        if (string.IsNullOrEmpty(SelectedClassName) || string.IsNullOrEmpty(SelectedMode))
            return;

        var skill = _advancedSkillContainer.GetClassModeSkills(SelectedClassName, SelectedMode);
        if (skill != null)
        {
            SelectedSkill = new AdvancedSkill(skill.ClassName, skill.Skills, skill.SkillTimeout, skill.ClassUseMode, skill.SkillUseMode);
        }
        else
        {
            SelectedSkill = null;
        }
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedSkill is null)
            return;

        _advancedSkillContainer.Remove(SelectedSkill);
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (SelectedSkill is null)
            return;

        Messenger.Send<EditAdvancedSkillMessage>(new(SelectedSkill));
    }


    private void SaveSkill(SavedAdvancedSkillsViewModel recipient, SaveAdvancedSkillMessage message)
    {
        recipient._advancedSkillContainer.TryOverride(message.AdvSkill);
        Task.Run(async () =>
        {
            await Task.Delay(500);
            recipient._loadedSkills = null;
            recipient.OnPropertyChanged(nameof(recipient.LoadedSkills));
        });
    }

    private void AdvancedSkillsChanged(SavedAdvancedSkillsViewModel recipient, PropertyChangedMessage<List<AdvancedSkill>> message)
    {
        if (message.PropertyName == nameof(IAdvancedSkillContainer.LoadedSkills))
        {
            var currentSkill = recipient.SelectedSkill;
            recipient._loadedSkills = null;
            recipient.OnPropertyChanged(nameof(recipient.LoadedSkills));
            
            if (currentSkill != null)
            {
                var refreshedSkill = recipient._advancedSkillContainer.GetClassModeSkills(currentSkill.ClassName, currentSkill.ClassUseMode.ToString());
                if (refreshedSkill != null)
                {
                    recipient.SelectedSkill = refreshedSkill;
                }
            }
        }
    }
}