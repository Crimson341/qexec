using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.Models.Skills;
using System.Text;

namespace Skua.Core.ViewModels;

public partial class CBOClassSelectViewModel : ObservableObject, IManageCBOptions
{
    private const string CurrentClassOption = "[Current]";

    public CBOClassSelectViewModel(IScriptInventory inventory, IAdvancedSkillContainer advancedSkills, IScriptPlayer player)
    {
        _inventory = inventory;
        _advancedSkills = advancedSkills;
        _player = player;
    }

    public List<string> PlayerClasses { get; private set; } = new();

    private string? _selectedSoloClass;

    public string? SelectedSoloClass
    {
        get { return _selectedSoloClass; }
        set
        {
            if (SetProperty(ref _selectedSoloClass, value) && value is not null)
            {
                SoloUseModes = new();
                SoloModeStrings = new();
                string classToUse = value;

                if (value == CurrentClassOption)
                {
                    classToUse = _player.CurrentClass?.Name ?? string.Empty;
                    if (string.IsNullOrEmpty(classToUse))
                    {
                        SoloUseModes.Add(ClassUseMode.Base);
                    }
                    else
                    {
                        var skillModes = _advancedSkills.LoadedSkills.Where(s => s.ClassName == classToUse).Select(s => s.ClassUseMode).Distinct().ToList();
                        if (skillModes.Count > 0)
                        {
                            SoloUseModes.AddRange(skillModes);
                        }
                        else
                        {
                            SoloUseModes.Add(ClassUseMode.Base);
                            EnsureSkillEntryExists(classToUse);
                        }
                    }
                }
                else
                {
                    SoloUseModes.AddRange(_advancedSkills.LoadedSkills.Where(s => s.ClassName == classToUse).Select(s => s.ClassUseMode));
                }

                var classModes = _advancedSkills.GetAvailableClassModes();
                if (classModes.TryGetValue(classToUse, out var modes))
                {
                    SoloModeStrings.AddRange(modes.OrderBy(x => x));
                }

                OnPropertyChanged(nameof(SoloUseModes));
                OnPropertyChanged(nameof(SoloModeStrings));
                if (SelectedSoloUseMode is null)
                    SelectedSoloUseMode = SoloUseModes.FirstOrDefault();
                if (SelectedSoloModeString is null && SoloModeStrings.Count > 0)
                    SelectedSoloModeString = SoloModeStrings.FirstOrDefault();
            }
        }
    }

    public List<ClassUseMode> SoloUseModes { get; private set; } = new();

    public List<string> SoloModeStrings { get; private set; } = new();

    [ObservableProperty]
    private ClassUseMode? _selectedSoloUseMode;

    [ObservableProperty]
    private string? _selectedSoloModeString;

    // Keep enum and string mode selections in sync so saves persist the user's choice.
    partial void OnSelectedSoloModeStringChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(typeof(ClassUseMode), value, true, out object? result))
            SelectedSoloUseMode = (ClassUseMode)result!;
    }
    partial void OnSelectedSoloUseModeChanged(ClassUseMode? value)
    {
        string? mode = value?.ToString();
        if (!string.IsNullOrEmpty(mode) && SelectedSoloModeString != mode)
            SelectedSoloModeString = mode;
    }

    [ObservableProperty]
    private bool _useSoloEquipment;

    private string? _selectedFarmClass;

    public string? SelectedFarmClass
    {
        get { return _selectedFarmClass; }
        set
        {
            if (SetProperty(ref _selectedFarmClass, value) && value is not null)
            {
                FarmUseModes = new();
                FarmModeStrings = new();
                string classToUse = value;

                if (value == CurrentClassOption)
                {
                    classToUse = _player.CurrentClass?.Name ?? string.Empty;
                    if (string.IsNullOrEmpty(classToUse))
                    {
                        FarmUseModes.Add(ClassUseMode.Base);
                    }
                    else
                    {
                        var skillModes = _advancedSkills.LoadedSkills.Where(s => s.ClassName == classToUse).Select(s => s.ClassUseMode).Distinct().ToList();
                        if (skillModes.Count > 0)
                        {
                            FarmUseModes.AddRange(skillModes);
                        }
                        else
                        {
                            FarmUseModes.Add(ClassUseMode.Base);
                            EnsureSkillEntryExists(classToUse);
                        }
                    }
                }
                else
                {
                    FarmUseModes.AddRange(_advancedSkills.LoadedSkills.Where(s => s.ClassName == classToUse).Select(s => s.ClassUseMode));
                }

                var classModes = _advancedSkills.GetAvailableClassModes();
                if (classModes.TryGetValue(classToUse, out var modes))
                {
                    FarmModeStrings.AddRange(modes.OrderBy(x => x));
                }

                OnPropertyChanged(nameof(FarmUseModes));
                OnPropertyChanged(nameof(FarmModeStrings));
                if (SelectedFarmUseMode is null)
                    SelectedFarmUseMode = FarmUseModes.FirstOrDefault();
                if (SelectedFarmModeString is null && FarmModeStrings.Count > 0)
                    SelectedFarmModeString = FarmModeStrings.FirstOrDefault();
            }
        }
    }

    public List<ClassUseMode> FarmUseModes { get; private set; } = new();

    public List<string> FarmModeStrings { get; private set; } = new();

    [ObservableProperty]
    private ClassUseMode? _selectedFarmUseMode;

    [ObservableProperty]
    private string? _selectedFarmModeString;

    // Keep enum and string mode selections in sync so saves persist the user's choice.
    partial void OnSelectedFarmModeStringChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(typeof(ClassUseMode), value, true, out object? result))
            SelectedFarmUseMode = (ClassUseMode)result!;
    }
    partial void OnSelectedFarmUseModeChanged(ClassUseMode? value)
    {
        string? mode = value?.ToString();
        if (!string.IsNullOrEmpty(mode) && SelectedFarmModeString != mode)
            SelectedFarmModeString = mode;
    }

    [ObservableProperty]
    private bool _useFarmEquipment;

    private string? _selectedDodgeClass;

    public string? SelectedDodgeClass
    {
        get { return _selectedDodgeClass; }
        set
        {
            if (SetProperty(ref _selectedDodgeClass, value) && value is not null)
            {
                DodgeUseModes = new();
                DodgeModeStrings = new();
                string classToUse = value;

                if (value == CurrentClassOption)
                {
                    classToUse = _player.CurrentClass?.Name ?? string.Empty;
                    if (string.IsNullOrEmpty(classToUse))
                    {
                        DodgeUseModes.Add(ClassUseMode.Base);
                    }
                    else
                    {
                        var skillModes = _advancedSkills.LoadedSkills.Where(s => s.ClassName == classToUse).Select(s => s.ClassUseMode).Distinct().ToList();
                        if (skillModes.Count > 0)
                        {
                            DodgeUseModes.AddRange(skillModes);
                        }
                        else
                        {
                            DodgeUseModes.Add(ClassUseMode.Base);
                            EnsureSkillEntryExists(classToUse);
                        }
                    }
                }
                else
                {
                    DodgeUseModes.AddRange(_advancedSkills.LoadedSkills.Where(s => s.ClassName == classToUse).Select(s => s.ClassUseMode));
                }

                var classModes = _advancedSkills.GetAvailableClassModes();
                if (classModes.TryGetValue(classToUse, out var modes))
                {
                    DodgeModeStrings.AddRange(modes.OrderBy(x => x));
                }

                OnPropertyChanged(nameof(DodgeUseModes));
                OnPropertyChanged(nameof(DodgeModeStrings));
                if (SelectedDodgeUseMode is null)
                    SelectedDodgeUseMode = DodgeUseModes.FirstOrDefault();
                if (SelectedDodgeModeString is null && DodgeModeStrings.Count > 0)
                    SelectedDodgeModeString = DodgeModeStrings.FirstOrDefault();
            }
        }
    }

    public List<ClassUseMode> DodgeUseModes { get; private set; } = new();

    public List<string> DodgeModeStrings { get; private set; } = new();

    [ObservableProperty]
    private ClassUseMode? _selectedDodgeUseMode;

    [ObservableProperty]
    private string? _selectedDodgeModeString;

    // Keep enum and string mode selections in sync so saves persist the user's choice.
    partial void OnSelectedDodgeModeStringChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(typeof(ClassUseMode), value, true, out object? result))
            SelectedDodgeUseMode = (ClassUseMode)result!;
    }
    partial void OnSelectedDodgeUseModeChanged(ClassUseMode? value)
    {
        string? mode = value?.ToString();
        if (!string.IsNullOrEmpty(mode) && SelectedDodgeModeString != mode)
            SelectedDodgeModeString = mode;
    }

    [ObservableProperty]
    private bool _useDodgeEquipment;

    private string? _selectedBossClass;

    public string? SelectedBossClass
    {
        get { return _selectedBossClass; }
        set
        {
            if (SetProperty(ref _selectedBossClass, value) && value is not null)
            {
                BossUseModes = new();
                BossModeStrings = new();
                string classToUse = value;

                if (value == CurrentClassOption)
                {
                    classToUse = _player.CurrentClass?.Name ?? string.Empty;
                    if (string.IsNullOrEmpty(classToUse))
                    {
                        BossUseModes.Add(ClassUseMode.Base);
                    }
                    else
                    {
                        var skillModes = _advancedSkills.LoadedSkills.Where(s => s.ClassName == classToUse).Select(s => s.ClassUseMode).Distinct().ToList();
                        if (skillModes.Count > 0)
                        {
                            BossUseModes.AddRange(skillModes);
                        }
                        else
                        {
                            BossUseModes.Add(ClassUseMode.Base);
                            EnsureSkillEntryExists(classToUse);
                        }
                    }
                }
                else
                {
                    BossUseModes.AddRange(_advancedSkills.LoadedSkills.Where(s => s.ClassName == classToUse).Select(s => s.ClassUseMode));
                }

                var classModes = _advancedSkills.GetAvailableClassModes();
                if (classModes.TryGetValue(classToUse, out var modes))
                {
                    BossModeStrings.AddRange(modes.OrderBy(x => x));
                }

                OnPropertyChanged(nameof(BossUseModes));
                OnPropertyChanged(nameof(BossModeStrings));
                if (SelectedBossUseMode is null)
                    SelectedBossUseMode = BossUseModes.FirstOrDefault();
                if (SelectedBossModeString is null && BossModeStrings.Count > 0)
                    SelectedBossModeString = BossModeStrings.FirstOrDefault();
            }
        }
    }

    public List<ClassUseMode> BossUseModes { get; private set; } = new();

    public List<string> BossModeStrings { get; private set; } = new();

    [ObservableProperty]
    private ClassUseMode? _selectedBossUseMode;

    [ObservableProperty]
    private string? _selectedBossModeString;

    // Keep enum and string mode selections in sync so saves persist the user's choice.
    partial void OnSelectedBossModeStringChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(typeof(ClassUseMode), value, true, out object? result))
            SelectedBossUseMode = (ClassUseMode)result!;
    }
    partial void OnSelectedBossUseModeChanged(ClassUseMode? value)
    {
        string? mode = value?.ToString();
        if (!string.IsNullOrEmpty(mode) && SelectedBossModeString != mode)
            SelectedBossModeString = mode;
    }

    [ObservableProperty]
    private bool _useBossEquipment;

    private readonly IScriptInventory _inventory;
    private readonly IAdvancedSkillContainer _advancedSkills;
    private readonly IScriptPlayer _player;

    private void EnsureSkillEntryExists(string className)
    {
        if (!_advancedSkills.LoadedSkills.Any(s => s.ClassName == className))
        {
            var newSkill = new AdvancedSkill(className, "1 | 2 | 3 | 4", 100, ClassUseMode.Base, SkillUseMode.UseIfAvailable);
            _advancedSkills.Add(newSkill);
        }
    }

    [RelayCommand]
    private void ReloadClasses()
    {
        PlayerClasses = new List<string> { CurrentClassOption };
        PlayerClasses.AddRange(_inventory.Items?.Where(i =>
            (i.Category == ItemCategory.Class) && (i.EnhancementLevel > 0)
        ).Select(i => i.Name) ?? Enumerable.Empty<string>());

        OnPropertyChanged(nameof(PlayerClasses));

        SoloUseModes = new();
        SelectedSoloClass = null;
        SelectedSoloUseMode = null;

        FarmUseModes = new();
        SelectedFarmClass = null;
        SelectedFarmUseMode = null;

        DodgeUseModes = new();
        SelectedDodgeClass = null;
        SelectedDodgeUseMode = null;

        BossUseModes = new();
        SelectedBossClass = null;
        SelectedBossUseMode = null;
    }

    public StringBuilder Save(StringBuilder builder)
    {
        string soloClass = SelectedSoloClass == CurrentClassOption ? CurrentClassOption : SelectedSoloClass;
        string farmClass = SelectedFarmClass == CurrentClassOption ? CurrentClassOption : SelectedFarmClass;
        string dodgeClass = SelectedDodgeClass == CurrentClassOption ? CurrentClassOption : SelectedDodgeClass;
        string bossClass = SelectedBossClass == CurrentClassOption ? CurrentClassOption : SelectedBossClass;
        
        builder.AppendLine($"SoloClassSelect: {soloClass}");
        builder.AppendLine($"SoloEquipCheck: {UseSoloEquipment}");
        builder.AppendLine($"SoloModeSelect: {SelectedSoloUseMode}");
        builder.AppendLine($"FarmClassSelect: {farmClass}");
        builder.AppendLine($"FarmEquipCheck: {UseFarmEquipment}");
        builder.AppendLine($"FarmModeSelect: {SelectedFarmUseMode}");
        builder.AppendLine($"DodgeClassSelect: {dodgeClass}");
        builder.AppendLine($"DodgeEquipCheck: {UseDodgeEquipment}");
        builder.AppendLine($"DodgeModeSelect: {SelectedDodgeUseMode}");
        builder.AppendLine($"BossClassSelect: {bossClass}");
        builder.AppendLine($"BossEquipCheck: {UseBossEquipment}");
        builder.AppendLine($"BossModeSelect: {SelectedBossUseMode}");

        return builder;
    }

    public void SetValues(Dictionary<string, string> values)
    {
        ReloadClasses();
        
        if (values.ContainsKey("SoloClassSelect"))
        {
            string soloClassValue = values["SoloClassSelect"];
            if (soloClassValue != CurrentClassOption && !PlayerClasses.Contains(soloClassValue))
            {
                PlayerClasses.Add(soloClassValue);
                OnPropertyChanged(nameof(PlayerClasses));
            }
            SelectedSoloClass = soloClassValue;
            if (values.TryGetValue("SoloEquipCheck", out string? check))
            {
                UseSoloEquipment = Convert.ToBoolean(check);
            }
            else
                UseSoloEquipment = false;
            if (values.TryGetValue("SoloModeSelect", out string? mode) && !string.IsNullOrWhiteSpace(mode))
            {
                SelectedSoloUseMode = Enum.TryParse(typeof(ClassUseMode), mode, true, out object? result) ? (ClassUseMode)result! : ClassUseMode.Base;
            }
            else
                SelectedSoloUseMode = ClassUseMode.Base;
        }
        else
        {
            SelectedSoloClass = string.Empty;
            UseSoloEquipment = false;
            SelectedSoloUseMode = ClassUseMode.Base;
        }

        if (values.ContainsKey("FarmClassSelect"))
        {
            string farmClassValue = values["FarmClassSelect"];
            if (farmClassValue != CurrentClassOption && !PlayerClasses.Contains(farmClassValue))
            {
                PlayerClasses.Add(farmClassValue);
                OnPropertyChanged(nameof(PlayerClasses));
            }
            SelectedFarmClass = farmClassValue;
            UseFarmEquipment = values.TryGetValue("FarmEquipCheck", out string? check) && Convert.ToBoolean(check);
            SelectedFarmUseMode = values.TryGetValue("FarmModeSelect", out string? mode) && !string.IsNullOrWhiteSpace(mode)
                ? Enum.TryParse(typeof(ClassUseMode), mode, true, out object? result) ? (ClassUseMode)result! : ClassUseMode.Base
                : ClassUseMode.Base;
        }
        else
        {
            SelectedFarmClass = string.Empty;
            UseFarmEquipment = false;
            SelectedFarmUseMode = ClassUseMode.Base;
        }

        if (values.ContainsKey("DodgeClassSelect"))
        {
            string dodgeClassValue = values["DodgeClassSelect"];
            if (dodgeClassValue != CurrentClassOption && !PlayerClasses.Contains(dodgeClassValue))
            {
                PlayerClasses.Add(dodgeClassValue);
                OnPropertyChanged(nameof(PlayerClasses));
            }
            SelectedDodgeClass = dodgeClassValue;
            UseDodgeEquipment = values.TryGetValue("DodgeEquipCheck", out string? check) && Convert.ToBoolean(check);
            SelectedDodgeUseMode = values.TryGetValue("DodgeModeSelect", out string? mode) && !string.IsNullOrWhiteSpace(mode)
                ? Enum.TryParse(typeof(ClassUseMode), mode, true, out object? result) ? (ClassUseMode)result! : ClassUseMode.Base
                : ClassUseMode.Base;
        }
        else
        {
            SelectedDodgeClass = string.Empty;
            UseDodgeEquipment = false;
            SelectedDodgeUseMode = ClassUseMode.Base;
        }

        if (values.ContainsKey("BossClassSelect"))
        {
            string bossClassValue = values["BossClassSelect"];
            if (bossClassValue != CurrentClassOption && !PlayerClasses.Contains(bossClassValue))
            {
                PlayerClasses.Add(bossClassValue);
                OnPropertyChanged(nameof(PlayerClasses));
            }
            SelectedBossClass = bossClassValue;
            UseBossEquipment = values.TryGetValue("BossEquipCheck", out string? check) && Convert.ToBoolean(check);
            SelectedBossUseMode = values.TryGetValue("BossModeSelect", out string? mode) && !string.IsNullOrWhiteSpace(mode)
                ? Enum.TryParse(typeof(ClassUseMode), mode, true, out object? result) ? (ClassUseMode)result! : ClassUseMode.Base
                : ClassUseMode.Base;
        }
        else
        {
            SelectedBossClass = string.Empty;
            UseBossEquipment = false;
            SelectedBossUseMode = ClassUseMode.Base;
        }
    }
}