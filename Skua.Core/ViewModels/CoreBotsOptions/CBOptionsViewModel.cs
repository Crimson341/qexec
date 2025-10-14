using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using System.Text;

namespace Skua.Core.ViewModels;

public class CBOptionsViewModel : ObservableObject, IManageCBOptions
{
    private readonly IDialogService _dialogService;

    public CBOptionsViewModel(List<DisplayOptionItemViewModelBase> options, IDialogService dialogService)
    {
        Options = options;
        _dialogService = dialogService;
        DefaultValues = new();
        foreach (DisplayOptionItemViewModelBase option in Options)
            DefaultValues.Add(option.Tag, option.Value!);
    }

    public List<DisplayOptionItemViewModelBase> Options { get; }

    private Dictionary<string, object> DefaultValues { get; }

    public StringBuilder Save(StringBuilder builder)
    {
        foreach (DisplayOptionItemViewModelBase option in Options)
        {
            if (option.Tag == "PrivateRooms" && !(bool)option.Value && _dialogService.ShowMessageBox("Whilst we do offer the option, we highly recommend staying in private rooms while botting. Bot in public at your own risk.\r\n Confirm the use of Public Rooms?", "Public Room Warning", true) == false)
            {
                builder.AppendLine($"{option.Tag}: {true}");
                continue;
            }

            if (option.Tag == "PrivateRoomNr" && long.TryParse(option.Value?.ToString(), out long room) && room > int.MaxValue && _dialogService.ShowMessageBox($"Private room number cannot be greater than {int.MaxValue}", "Room Number Warning", true) == false)
            {
                continue;
            }
            builder.AppendLine($"{option.Tag}: {option.Value}");
        }

        return builder;
    }

    public void SetValues(Dictionary<string, string> values)
    {
        foreach (DisplayOptionItemViewModelBase option in Options)
        {
            if (values.TryGetValue(option.Tag, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                try
                {
                    if (option.DisplayType == typeof(int))
                    {
                        if (long.TryParse(value, out long longValue))
                        {
                            option.Value = (int)Math.Clamp(longValue, int.MinValue, int.MaxValue);
                        }
                        else
                        {
                            option.Value = 100000;
                        }
                    }
                    else
                    {
                        option.Value = Convert.ChangeType(value, option.DisplayType);
                        continue;
                    }
                }
                catch (Exception)
                {
                    option.Value = DefaultValues[option.Tag];
                }
            }
            option.Value = DefaultValues[option.Tag];
        }
    }
}