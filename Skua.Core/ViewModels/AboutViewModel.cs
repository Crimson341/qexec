using CommunityToolkit.Mvvm.Input;
using Skua.Core.Utils;
using System.Diagnostics;

namespace Skua.Core.ViewModels;

public class AboutViewModel : BotControlViewModelBase
{
    private string _markDownContent = "Loading content...";

    public AboutViewModel() : base("About")
    {
        _markDownContent = string.Empty;

        Task.Run(async () => await GetAboutContent());

        NavigateCommand = new RelayCommand<string>(NavigateToUrl);
    }

    public string MarkdownDoc
    {
        get => _markDownContent; set => SetProperty(ref _markDownContent, value);
    }

    public IRelayCommand NavigateCommand { get; }

    private void NavigateToUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return;

        try
        {
            // Handle relative file paths that start with "./"
            if (url.StartsWith("./"))
            {
                // Get the relative path without the "./" prefix
                string relativePath = url.Substring(2);

                // Combine with the current directory to get the full path
                string fullPath = System.IO.Path.Combine(Environment.CurrentDirectory, relativePath);

                // Check if the file exists
                if (System.IO.File.Exists(fullPath))
                {
                    Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                }
                else
                {
                    // If file doesn't exist locally, try to open it from the GitHub repository
                    string githubUrl = $"https://raw.githubusercontent.com/auqw/Skua/refs/heads/master/{relativePath}";
                    Process.Start(new ProcessStartInfo(githubUrl) { UseShellExecute = true });
                }
            }
            else
            {
                // Handle regular URLs
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch
        {
            /* ignored */
        }
    }

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