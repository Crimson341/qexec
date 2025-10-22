using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Skua.Core.ViewModels;

public partial class SkillRulesViewModel : ObservableRecipient
{
    public SkillRulesViewModel()
    { }

    public SkillRulesViewModel(SkillRulesViewModel rules)
    {
        _useRuleBool = rules.UseRuleBool;
        _waitUseValue = rules.WaitUseValue;
        _healthGreaterThanBool = rules.HealthGreaterThanBool;
        _healthUseValue = rules.HealthUseValue;
        _manaGreaterThanBool = rules.ManaGreaterThanBool;
        _manaUseValue = rules.ManaUseValue;
        _auraComparisonMode = rules.AuraComparisonMode;
        _auraUseValue = rules.AuraUseValue;
        _auraTargetIndex = rules.AuraTargetIndex;
        _auraName = rules.AuraName;
        _skipUseBool = rules.SkipUseBool;
    }

    [ObservableProperty]
    private bool _useRuleBool;

    [ObservableProperty]
    private bool _healthGreaterThanBool = true;

    private int _healthUseValue;

    public int HealthUseValue
    {
        get { return _healthUseValue; }
        set
        {
            if (value is < 0 or > 100)
                return;
            SetProperty(ref _healthUseValue, value);
        }
    }

    [ObservableProperty]
    private bool _manaGreaterThanBool = true;

    private int _manaUseValue;

    public int ManaUseValue
    {
        get { return _manaUseValue; }
        set
        {
            if (value is < 0 or > 100)
                return;
            SetProperty(ref _manaUseValue, value);
        }
    }

    [ObservableProperty]
    private int _waitUseValue;

    [ObservableProperty]
    private bool _skipUseBool;

    [ObservableProperty]
    private int _auraComparisonMode = 0; // 0: >, 1: <, 2: >=, 3: <=
    
    public bool AuraGreaterThanBool => _auraComparisonMode == 0 || _auraComparisonMode == 2;
    public bool AuraStrictComparison => _auraComparisonMode == 0 || _auraComparisonMode == 1;
    
    public string AuraComparisonSymbol => _auraComparisonMode switch
    {
        0 => ">",
        1 => "<",
        2 => ">=",
        3 => "<=",
        _ => ">"
    };

    private int _auraUseValue;

    public int AuraUseValue
    {
        get { return _auraUseValue; }
        set
        {
            if (value < 0)
                return;
            SetProperty(ref _auraUseValue, value);
        }
    }

    [ObservableProperty]
    private int _auraTargetIndex = 0;

    [ObservableProperty]
    private string _auraName = string.Empty;

    [RelayCommand]
    private void CycleAuraComparison()
    {
        AuraComparisonMode = (AuraComparisonMode + 1) % 4;
        OnPropertyChanged(nameof(AuraComparisonSymbol));
        OnPropertyChanged(nameof(AuraGreaterThanBool));
        OnPropertyChanged(nameof(AuraStrictComparison));
    }

    [RelayCommand]
    private void ResetUseRules()
    {
        UseRuleBool = false;
        HealthGreaterThanBool = true;
        HealthUseValue = 0;
        ManaGreaterThanBool = true;
        ManaUseValue = 0;
        WaitUseValue = 0;
        AuraComparisonMode = 0;
        AuraUseValue = 0;
        AuraTargetIndex = 0;
        AuraName = string.Empty;
        SkipUseBool = false;
    }
}
