using Skua.Core.Interfaces;

// Select this file from the Mac app after the game reports that it has loaded.
// It reads the login state once and performs no account or game actions.
public class LiveReadOnly
{
    public void ScriptMain(IScriptInterface bot)
    {
        bot.Log("Mac smoke test: Skua " + bot.Version);
        bot.Log("Player.LoggedIn = " + bot.Player.LoggedIn);
        bot.Log("Live game API read completed.");
    }
}
