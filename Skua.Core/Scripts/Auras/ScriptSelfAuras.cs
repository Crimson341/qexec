using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models.Auras;
using Skua.Core.Flash;

namespace Skua.Core.Scripts;

public partial class ScriptSelfAuras : IScriptSelfAuras
{
    private readonly Lazy<IFlashUtil> _lazyFlash;
    private IFlashUtil Flash => _lazyFlash.Value;

    public ScriptSelfAuras(Lazy<IFlashUtil> lazyFlash)
    {
        _lazyFlash = lazyFlash;
    }

    public List<Aura> Auras
    {
        get
        {
            string? auraData = Flash.Call("getSubjectAuras", nameof(SubjectType.Self));
            return JsonConvert.DeserializeObject<List<Aura>>(auraData) ?? new List<Aura>();
        }
    }

    public Aura? GetAura(string auraName)
    {
        return Auras.FirstOrDefault(a => a.Name.Equals(auraName, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasActiveAura(string auraName)
    {
        return GetAura(auraName) != null;
    }

    [JsonCallBinding("auraTest")]
    private string _auraTest;

    public bool TryGetAura(string auraName, out Aura? aura)
    {
        if (HasActiveAura(auraName))
        {
            aura = GetAura(auraName);
            return true;
        }
        aura = null;
        return false;
    }

    public int GetAuraValue(string auraName)
    {
        return Flash.Call<int>("GetAurasValue", nameof(SubjectType.Self), auraName);
    }

    public bool HasAuraWithMinStacks(string auraName, int minStacks)
    {
        return GetAuraValue(auraName) >= minStacks;
    }

    public int GetAuraSecondsRemaining(string auraName)
    {
        return Flash.Call<int>("GetAuraSecondsRemaining", nameof(SubjectType.Self), auraName);
    }

    public bool HasAnyActiveAura(params string[] auraNames)
    {
        return Flash.Call<bool>("HasAnyActiveAura", nameof(SubjectType.Self), string.Join(",", auraNames));
    }

    public bool HasAllActiveAuras(params string[] auraNames)
    {
        return Flash.Call<bool>("HasAllActiveAuras", nameof(SubjectType.Self), string.Join(",", auraNames));
    }

    public int GetTotalAuraStacks(string auraNamePattern)
    {
        return Flash.Call<int>("GetTotalAuraStacks", nameof(SubjectType.Self), auraNamePattern);
    }
}