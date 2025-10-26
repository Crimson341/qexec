using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Utils;
using System.Collections.Immutable;

namespace Skua.Core.ViewModels;

public partial class JumpViewModel : BotControlViewModelBase
{
    public JumpViewModel(IMapService mapService)
        : base("Jump")
    {
        _mapService = mapService;
        Pads = _mapService.Pads;
    }

    private readonly IMapService _mapService;
    private bool _suppressAutoJump = false;

    [ObservableProperty]
    private string _selectedCell = string.Empty;

    partial void OnSelectedCellChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && !_suppressAutoJump)
        {
            // Set default pad to Left if not already set
            if (string.IsNullOrEmpty(SelectedPad))
            {
                SelectedPad = "Left";
            }
            // Auto-jump when cell is selected
            JumpTo();
        }
    }

    [ObservableProperty]
    private string _selectedPad = string.Empty;

    [ObservableProperty]
    private RangedObservableCollection<string> _cells = new();

    public ImmutableList<string> Pads { get; }

    [RelayCommand]
    private void GetCurrent()
    {
        _suppressAutoJump = true;
        var (cell, pad) = _mapService.GetCurrentCell();
        SelectedCell = cell;
        SelectedPad = pad;
        _suppressAutoJump = false;
    }

    [RelayCommand]
    private void JumpTo()
    {
        _mapService.Jump(SelectedCell, SelectedPad);
    }

    [RelayCommand]
    public void UpdateCells()
    {
        Cells.ReplaceRange(_mapService.Cells);
    }
}