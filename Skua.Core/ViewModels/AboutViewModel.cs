using CommunityToolkit.Mvvm.Input;
using Skua.Core.Utils;
using System.Diagnostics;
using static Skua.Core.Utils.ValidatedHttpExtensions;

namespace Skua.Core.ViewModels;

public class AboutViewModel : BotControlViewModelBase
{
    private string _markDownContent = "Loading content...";

    public AboutViewModel() : base("About")
    {
        _markDownContent = string.Empty;

        Task.Run(async () => await GetAboutContent());

        NavigateCommand = new RelayCommand<string>(url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
    }

    public string MarkdownDoc
    {
        get { return _markDownContent; }
        set { SetProperty(ref _markDownContent, value); }
    }

    public IRelayCommand NavigateCommand { get; }

    private async Task GetAboutContent()
    {
        try
        {
            MarkdownDoc = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, "auqw/Skua/refs/heads/master/readme.md").ConfigureAwait(false);
        }
        catch
        {
            MarkdownDoc = "### No content found. Please check your internet connection.";
        }
    }
}