using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.GitHub;
using Skua.Core.Utils;
using static Skua.Core.Utils.ValidatedHttpExtensions;

namespace Skua.Core.Services;

public partial class GetScriptsService : ObservableObject, IGetScriptsService
{
    private readonly IDialogService _dialogService;
    private const string _rawScriptsJsonUrl = "auqw/Scripts/refs/heads/Skua/scripts.json";
    private const string _skillsSetsRawUrl = "auqw/Scripts/refs/heads/Skua/Skills/AdvancedSkills.txt";

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
}