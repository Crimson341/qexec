const {test} = require('node:test');
const assert = require('node:assert/strict');
const {readFileSync,writeFileSync,mkdtempSync,rmSync} = require('node:fs');
const {spawnSync} = require('node:child_process');
const {tmpdir} = require('node:os');
const path = require('node:path');

test('actual CoreStory preload handles missing source and loads Lord of Order quest IDs', {timeout:30000}, () => {
  const scripts = path.resolve(__dirname,'../compat/scripts');
  const story = readFileSync(path.join(scripts,'CoreStory.cs'),'utf8');
  const method = story.slice(story.indexOf('    public void PreLoad('), story.indexOf('    private int PreviousQuestID'));
  const dir = mkdtempSync(path.join(tmpdir(),'skua-preload-'));
  try {
    writeFileSync(path.join(dir,'Test.csproj'),'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>');
    writeFileSync(path.join(dir,'lord.txt'),readFileSync(path.join(__dirname,'fixtures/LordOfOrder.cs.txt')));
    writeFileSync(path.join(dir,'Program.cs'), `using System.Runtime.CompilerServices;
using System.Dynamic;
var story = new CoreStory();
foreach (var source in new[] { "", "public class Other\\n{\\n}", "public class LordOfOrder\\n{", "public class LordOfOrder\\n{\\n    public void GetLoO() {}\\n}", "public class LordOfOrder\\n{\\n    public void GetLoO()\\n    {\\n}" }) {
    story.Core.Source = source;
    story.PreLoad(new LordOfOrder(), "GetLoO");
    if (story.Bot.Quests.Loaded.Count != 0) throw new Exception("Malformed source loaded quests");
}
story.Core.Source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../..", "lord.txt"));
story.PreLoad(new LordOfOrder(), "GetLoO");
if (!story.Bot.Quests.Loaded.Contains(7156) || !story.Bot.Quests.Loaded.Contains(7165)) throw new Exception("Lord of Order quest IDs not loaded");
Console.WriteLine("PRELOAD_OK");
class LordOfOrder {}
class CoreStory {
 public FakeCore Core = new(); public FakeBot Bot = new();
${method}
}
class FakeCore {
 public string Source = ""; public int LoadedQuestLimit = 1000;
 public string[] CompiledScript() => Source.Replace("\\r", "").Split('\\n');
 public void Logger(string message) {} public void Sleep(int value) {}
}
class FakeBot { public FakeQuests Quests = new(); public FakeFlash Flash = new(); }
class FakeFlash { public void SetGameObject(string name, object value) {} }
class FakeQuest { public int ID {get;set;} }
class FakeQuests { public List<FakeQuest> Tree = new(); public List<int> Loaded = new(); public void Load(int[] ids) => Loaded.AddRange(ids); }
`);
    const result = spawnSync(process.env.SKUA_DOTNET || '/usr/local/share/dotnet/dotnet', ['run','--project',dir,'--verbosity','quiet'], {encoding:'utf8',timeout:25000});
    assert.equal(result.status,0,result.stdout + result.stderr);
    assert.match(result.stdout,/PRELOAD_OK/);
  } finally { rmSync(dir,{recursive:true,force:true}); }
});
