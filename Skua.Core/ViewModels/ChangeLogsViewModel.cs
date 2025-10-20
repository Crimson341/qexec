using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Utils;
using static Skua.Core.Utils.ValidatedHttpExtensions;

namespace Skua.Core.ViewModels;

public class ChangeLogsViewModel : BotControlViewModelBase
{
    private string _markDownContent = "Loading content...";
    private bool _hasLoadedOnce = false;

    public ChangeLogsViewModel() : base("Change Logs", 460, 500)
    {
        _markDownContent = string.Empty;

        OpenDonationLink = new RelayCommand(() => Ioc.Default.GetRequiredService<IProcessService>().OpenLink("https://ko-fi.com/sharpthenightmare"));
    }

    public IRelayCommand OpenDonationLink { get; }

    public string MarkdownDoc
    {
        get { return _markDownContent; }
        set { SetProperty(ref _markDownContent, value); }
    }

    private async Task GetChangeLogsContent()
    {
        try
        {
            MarkdownDoc = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, "auqw/Skua/refs/heads/master/changelogs.md").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Show error message with exception details for debugging
            MarkdownDoc = $"### Unable to Load Changelog\r\n\r\nError: {ex.Message}\r\n\r\nThis might be due to:\r\n- No internet connection\r\n- GitHub service issues\r\n- Repository access problems\r\n\r\nPlease check your internet connection and try again later.\r\n\r\nYou can also view the latest releases at: [Skua Releases](https://github.com/auqw/Skua/releases)";
        }
    }

    private async Task RefreshChangelogContent()
    {
        MarkdownDoc = "Refreshing changelog...";
        await GetChangeLogsContent();
    }

    public async void OnActivated()
    {
        MarkdownDoc = "Loading changelog...";
        await GetChangeLogsContent();
        _hasLoadedOnce = true;
    }
}