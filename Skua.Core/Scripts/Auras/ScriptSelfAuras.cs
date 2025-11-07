using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models.Auras;
using Skua.Core.Flash;

namespace Skua.Core.Scripts;

public partial class ScriptSelfAuras : IScriptSelfAuras
{
    private readonly Lazy<IFlashUtil> _lazyFlash;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private IFlashUtil Flash => _lazyFlash.Value;
    private IScriptPlayer Player => _lazyPlayer.Value;

    public ScriptSelfAuras(Lazy<IFlashUtil> lazyFlash, Lazy<IScriptPlayer> lazyPlayer)
    {
        _lazyFlash = lazyFlash;
        _lazyPlayer = lazyPlayer;
    }

    public List<Aura> Auras
    {
        get
        {
            string auraData = Flash.Call("GetPlayerAura", Player.Username.ToLower()) ?? "[]";
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

    public string GetPlayerAura(string playerName)
    {
        return Flash.Call("GetPlayerAura", playerName) ?? "[]";
    }

    public float GetAuraValue(string auraName)
    {
        return Flash.Call<float>("GetAurasValue", nameof(SubjectType.Self), auraName);
    }

    public bool HasAnyActiveAura(params string[] auraNames)
    {
        return Flash.Call<bool>("HasAnyActiveAura", nameof(SubjectType.Self), string.Join(",", auraNames));
    }

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
}