using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.GitHub;
using Skua.Core.Utils;
using static Skua.Core.Utils.ValidatedHttpExtensions;
using System.Text;

namespace Skua.Core.Services;

public partial class GetScriptsService : ObservableObject, IGetScriptsService
{
    private readonly IDialogService _dialogService;
    private const string _rawScriptsJsonUrl = "auqw/Scripts/refs/heads/Skua/scripts.json";
    private const string _skillsSetsRawUrl = "auqw/Scripts/refs/heads/Skua/Skills/AdvancedSkills.txt";
    private const string _repoOwner = "auqw";
    private const string _repoName = "Scripts";
    private const string _repoBranch = "Skua";

    [ObservableProperty]
    private RangedObservableCollection<ScriptInfo> _scripts = new();

    public GetScriptsService(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public async ValueTask<List<ScriptInfo>> GetScriptsAsync(IProgress<string>? progress, CancellationToken token)
    {
        if (_scripts.Any())
            return _scripts.ToList();

        await GetScripts(progress, false, token);

        return _scripts.ToList();
    }

    public async Task RefreshScriptsAsync(IProgress<string>? progress, CancellationToken token)
    {
        await GetScripts(progress, true, token);
    }

    private async Task GetScripts(IProgress<string>? progress, bool refresh, CancellationToken token)
    {
        try
        {
            Scripts.Clear();

            progress?.Report("Fetching scripts...");
            List<ScriptInfo> scripts = await GetScriptsInfo(refresh, token);

            progress?.Report($"Found {scripts.Count} scripts.");
            _scripts.AddRange(scripts);

            progress?.Report($"Fetched {scripts.Count} scripts.");
            OnPropertyChanged(nameof(Scripts));
        }
        catch (TaskCanceledException)
        {
            progress?.Report("Task Cancelled.");
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Something went wrong when retrieving scripts.\r\nPlease, try again later.\r\n Error: {ex}", "Search Scripts Error");
        }
    }

    private async Task<List<ScriptInfo>> GetScriptsInfo(bool refresh, CancellationToken token)
    {
        if (_scripts.Count != 0 && !refresh)
            return _scripts.ToList();

        using (HttpResponseMessage response = await ValidatedHttpExtensions.GetAsync(HttpClients.GitHubRaw, _rawScriptsJsonUrl, token))
        {
            string content = await response.Content.ReadAsStringAsync(token);
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidDataException("scripts.json is empty or null");
                
            var scripts = JsonConvert.DeserializeObject<List<ScriptInfo>>(content);
            if (scripts == null || !scripts.Any())
                throw new InvalidDataException("scripts.json contains no valid scripts");
                
            return scripts;
        }
    }

    public async Task DownloadScriptAsync(ScriptInfo info)
    {
        DirectoryInfo parent = Directory.GetParent(info.LocalFile)!;
        if (!parent.Exists)
            parent.Create();

        string script = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, info.DownloadUrl);
        await File.WriteAllTextAsync(info.LocalFile, script);
    }

    public async Task ManagerDownloadScriptAsync(ScriptInfo info)
    {
        DirectoryInfo parent = Directory.GetParent(info.ManagerLocalFile)!;
        if (!parent.Exists)
            parent.Create();

        string script = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, info.DownloadUrl);
        await File.WriteAllTextAsync(info.ManagerLocalFile, script);
    }

    public async Task<int> DownloadAllWhereAsync(Func<ScriptInfo, bool> pred)
    {
        IEnumerable<ScriptInfo> toUpdate = _scripts.Where(pred);
        int count = toUpdate.Count();
        await Task.WhenAll(toUpdate.Select(s => DownloadScriptAsync(s)));
        return count;
    }

    public async Task<int> ManagerDownloadAllWhereAsync(Func<ScriptInfo, bool> pred)
    {
        IEnumerable<ScriptInfo> toUpdate = _scripts.Where(pred);
        int count = toUpdate.Count();
        await Task.WhenAll(toUpdate.Select(s => ManagerDownloadScriptAsync(s)));
        return count;
    }

    public async Task DeleteScriptAsync(ScriptInfo info)
    {
        await Task.Run(() =>
        {
            try
            {
                File.Delete(info.LocalFile);
            }
            catch { }
        });
    }

    public long GetSkillsSetsTextFileSize()
    {
        string rootSkillsSetsFile = Path.Combine(AppContext.BaseDirectory, "AdvancedSkills.txt");
        if (!File.Exists(ClientFileSources.SkuaAdvancedSkillsFile))
        {
            if (File.Exists(rootSkillsSetsFile))
                File.Copy(rootSkillsSetsFile, ClientFileSources.SkuaAdvancedSkillsFile, true);
            else
                return -1;
        }

        FileInfo file = new(ClientFileSources.SkuaAdvancedSkillsFile);
        if (file.Exists)
            return file.Length;

        return -1;
    }

    public async Task<long> CheckAdvanceSkillSetsUpdates()
    {
        try
        {
            string content = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, _skillsSetsRawUrl);
            return content.Length;
        }
        catch
        {
            return -1;
        }
    }

    public async Task<bool> UpdateSkillSetsFile()
    {
        try
        {
            string content = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, _skillsSetsRawUrl);
            await File.WriteAllTextAsync(ClientFileSources.SkuaAdvancedSkillsFile, content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> GetLastCommitShaAsync(CancellationToken token)
    {
        try
        {
            string url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/commits/{_repoBranch}";
            using var response = await HttpClients.MakeGitHubApiRequestAsync(url);
            string content = await response.Content.ReadAsStringAsync(token);
            var commit = JsonConvert.DeserializeObject<GitHubCommit>(content);
            return commit?.Sha;
        }
        catch
        {
            return null;
        }
    }

    private async Task<HashSet<string>> GetChangedFilesAsync(string oldSha, string newSha, CancellationToken token)
    {
        try
        {
            string url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/compare/{oldSha}...{newSha}";
            using var response = await HttpClients.MakeGitHubApiRequestAsync(url);
            string content = await response.Content.ReadAsStringAsync(token);
            var compare = JsonConvert.DeserializeObject<GitHubCompare>(content);
            
            if (compare?.Files == null)
                return new HashSet<string>();

            return compare.Files
                .Where(f => f.Status != "removed")
                .Select(f => f.FileName)
                .ToHashSet();
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Error getting changed files: {ex.Message}", "Debug Info");
            return new HashSet<string>();
        }
    }

    private string? GetStoredCommitSha()
    {
        try
        {
            if (File.Exists(ClientFileSources.SkuaScriptsCommitFile))
                return File.ReadAllText(ClientFileSources.SkuaScriptsCommitFile).Trim();
        }
        catch { }
        return null;
    }

    private async Task StoreCommitShaAsync(string sha)
    {
        try
        {
            await File.WriteAllTextAsync(ClientFileSources.SkuaScriptsCommitFile, sha);
        }
        catch { }
    }

    public IEnumerable<ScriptInfo> GetOutdatedScripts()
    {
        return _scripts.Where(s => s.Outdated).ToList();
    }

    public async Task<int> IncrementalUpdateScriptsAsync(IProgress<string>? progress, CancellationToken token)
    {
        try
        {
            progress?.Report("Checking for updates...");
            
            string? currentSha = await GetLastCommitShaAsync(token);
            if (string.IsNullOrEmpty(currentSha))
            {
                progress?.Report("Failed to get latest commit. Performing full refresh...");
                await RefreshScriptsAsync(progress, token);
                return 0;
            }

            string? storedSha = GetStoredCommitSha();
            if (string.IsNullOrEmpty(storedSha))
            {
                progress?.Report("First time setup. Downloading all scripts...");
                await RefreshScriptsAsync(progress, token);
                await StoreCommitShaAsync(currentSha);
                return _scripts.Count;
            }

            if (storedSha == currentSha)
            {
                progress?.Report("Scripts are up to date.");
                return 0;
            }

            progress?.Report("Fetching changed files...");
            var changedFiles = await GetChangedFilesAsync(storedSha, currentSha, token);
            
            if (changedFiles.Count == 0)
            {
                progress?.Report("No script changes detected.");
                await StoreCommitShaAsync(currentSha);
                return 0;
            }

            progress?.Report($"Found {changedFiles.Count} changed files. Updating...");
            
            List<ScriptInfo> scripts = await GetScriptsInfo(true, token);
            var scriptsToUpdate = scripts.Where(s => changedFiles.Contains(s.FilePath)).ToList();
            
            int updated = 0;
            foreach (var script in scriptsToUpdate)
            {
                if (token.IsCancellationRequested)
                    break;
                    
                try
                {
                    await ManagerDownloadScriptAsync(script);
                    updated++;
                    progress?.Report($"Updated {updated}/{scriptsToUpdate.Count}: {script.Name}");
                }
                catch (Exception ex)
                {
                    progress?.Report($"Failed to update {script.Name}: {ex.Message}");
                }
            }

            await StoreCommitShaAsync(currentSha);
            progress?.Report($"Update complete. {updated} scripts updated.");
            return updated;
        }
        catch (TaskCanceledException)
        {
            progress?.Report("Update cancelled.");
            return 0;
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessageBox($"Error during incremental update: {ex.Message}\r\nFalling back to full refresh.", "Update Error");
            await RefreshScriptsAsync(progress, token);
            return 0;
        }
    }
}
