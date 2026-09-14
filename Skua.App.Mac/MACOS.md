# qexec macOS development port

This is an experimental macOS port of the maintained Skua 1.4.4.4 core
(`auqw/Skua`, upstream commit `e069dc1`), built on Apple Silicon with a native
.NET 10 scripting process and an Intel Flash renderer running through Rosetta.
It is not yet a feature-complete or verified gameplay release.

## Current verification

- The packaged `build/qexec.app` launches on macOS 26.3 / Apple Silicon.
- The existing Artix Mac Flash plugin executes ActionScript 3 through Rosetta.
- A diagnostic SWF completed a real Flash → C# → Flash round trip.
- The maintained Skua SWF loads the live AQW login screen.
- A C# smoke test read `Player.LoggedIn` from the live game successfully.
- The native file chooser opens the existing script collection.
- Automated tests compile and run a C# script, resolve a Windows-style include
  containing a static helper, exercise the Flash RPC transport with a simulated
  renderer, and stop a running script.
- XML/RPC tests cover escaping, invariant numbers, sparse arrays, out-of-order
  replies, timeouts, and disconnection.

The current build uses a trusted local desktop SWF. Flash polls a command queue
in the application page and returns typed results to the native C# engine.
The game login screen and connection have been observed; the new generated quest farms still need live completion validation. CoreBots compilation
has been verified both directly and through the real script include loader.

## Run

Download the latest Apple Silicon build from [GitHub Releases](https://github.com/Crimson341/qexec/releases/latest), or use in-app **Update** on a packaged `qexec.app`: it downloads that zip, replaces this app, and relaunches (it does not open GitHub). Pushes to `main` run `.github/workflows/macos-release.yml`, which calls `./build-macos.sh` on a GitHub-hosted Mac and publishes a non-prerelease zip so `/releases/latest` resolves. Extract it, move `qexec.app` to Applications, and follow the included setup instructions. The application is ad-hoc signed and not notarized. OpenJDK is required for automatic map pickup inspection; Homebrew OpenJDK is detected automatically. A manual rebuild is available from the Actions tab (**Mac app release** → Run workflow).

Open `build/qexec.app`. The app includes its .NET runtime; an SDK is not needed
to run the packaged build. Apple Silicon requires Rosetta. The renderer uses the
Flash plugin from an existing installation of the official Artix Game Launcher:

```
/Applications/Artix Game Launcher.app/Contents/Resources/plugins/PepperFlashPlayer.plugin
```

Flash must trust the bundled bridge for local desktop hosting. Preview the exact
SWF and configuration paths with `./configure-flash-trust.sh`. If you accept
granting this SWF local-file and network access, install the entry with
`./configure-flash-trust.sh --enable`. It names one file, not the whole folder.
The entry is stored only in Skua Mac's Pepper profile under Application Support:
`Skua Mac/Pepper Data/Shockwave Flash/WritableRoot/#Security/FlashPlayerTrust/SkuaMac.cfg`.
Chrome and system-wide Flash settings are not changed. The app itself does not install this trust entry.
Moving the app requires updating the entry. Removing the generated `SkuaMac.cfg`
revokes this added trust. An existing different entry is never overwritten.

The proprietary Flash plugin is not redistributed in this repository or bundle.
The legacy Electron 11.5.0 and Flash runtimes are unsupported and have known
security limitations. This development app should not be treated as a modern,
security-supported browser. Navigation and new windows are blocked, renderer
Node integration is disabled, and desktop actions run in the main process.
These boundaries do not remove vulnerabilities in the legacy renderer.

Choose a `.cs` script with **⌘O**, run with **⌘R**, and stop with **⌘.** once the
game has loaded. The left Quest Ledger displays accepted quests, script selection, and connection status. Run/Stop controls and elapsed time sit below the game.
The activity dock below the game displays engine, game, and script messages.
Top navigation opens the game, scripts, activity, progression planner, or gear inspector.
The desktop app stores settings/data in `~/Library/Application Support/Skua Mac/data`
and reuses scripts in `~/Documents/Skua/Scripts`. Mac compiled-script caches are
separate from Windows caches. Missing bundled quest/skill data is installed
without overwriting existing data. Use only scripts you trust: C# scripts execute
with the user's normal filesystem and network permissions.

## Build from source

Requirements: macOS, .NET 10 SDK, Node.js/npm, Java, and the Artix Mac plugin.
The Flash compiler is pinned in `tools/package-lock.json`. If Java is not on the
PATH, use `JAVA_HOME`, `SKUA_JAVA`, or Homebrew's `openjdk` installation.

```sh
cd Skua.App.Mac
./build-macos.sh
```

The script downloads pinned npm dependencies, compiles the ActionScript bridge,
publishes a self-contained native host for the build Mac's architecture, and
packages the Intel renderer in an ad-hoc-signed `.app`. Move an earlier generated
`build/qexec.app` aside before rebuilding. The app is not notarized.

For development:

```sh
dotnet build host -c Release -p:SkuaPortable=true
./build-flash.sh
npm ci --prefix desktop --arch=x64
SKUA_SWF="$PWD/build/qexec.app/Contents/Resources/app/assets/skua.swf" npm start --prefix desktop
```

Optional environment overrides: `SKUA_DATA_DIR` (data root), `SKUA_SCRIPTS_DIR` (script collection), `SKUA_FLASH_PLUGIN`
(plugin path), `SKUA_SWF` (bridge SWF), `SKUA_HOST` (host executable or DLL),
`SKUA_DOTNET` (.NET executable for DLL development launches), `SKUA_JAVA` (Java).
The app's lifecycle/error log is at
`~/Library/Application Support/Skua Mac/startup.log`; raw game packets are not
written to that diagnostic file.

## Tests

```sh
dotnet build host -c Release -p:SkuaPortable=true
npm test --prefix desktop
dotnet run --project tests/BridgeTests.csproj -p:SkuaPortable=true
dotnet host/bin/Release/net10.0/Skua.Mac.Host.dll --self-test
dotnet host/bin/Release/net10.0/Skua.Mac.Host.dll --compile-check /path/to/CoreBots.cs
SKUA_TEST_COREBOTS=/path/to/CoreBots.cs npm test --prefix desktop
```

Host integration tests and the default self-test use temporary data directories.
The self-test compiles a class using the real core compiler and reports process
architecture. The compile check does not instantiate the class or run its script.
The optional CoreBots integration test compiles that actual include, then runs
only a synthetic probe against a simulated renderer.

## Design and remaining work

The existing Windows projects retain their Windows target by default.
`SkuaPortable=true` selects `net10.0` for the reusable core projects and omits
the Windows CoreHook. Mac adapters supply Flash, dialogs, settings, clipboard,
logging, and dispatch. Newline JSON over child-process stdio connects the host
to the desktop shell. The shell loads its local UI and SWF directly from the app
bundle; there is no HTTP server or command API.

The initial UI includes script selection, run/stop, logs, simple message dialogs,
and script options. WPF windows, the script manager UI, accounts UI, plugins,
specialized dialogs, Windows clipboard formats, and Windows-dependent scripts
have not been ported. The native backend version matches maintained upstream;
it does not pretend to implement unsupported Windows features.

The compiler defines `SKUA_MAC` for Mac scripts. The local CoreBots copy uses this
symbol to omit its Windows-only April Fools routines and WinForms imports; the
same changes are recorded in `compat/CoreBots.mac.patch`. A script repository
update may replace that local change. The compiler explicitly references DNS,
IP address, HTTP, and process assemblies instead of depending on which assemblies
happen to have loaded at startup.

Remaining milestones are representative farming scripts and reconnection behavior.
Moving generated data out of Documents resolved the observed startup pause.

## Adaptive auto attack

The right sidebar's **Auto attack → Enable** uses the currently equipped class,
updates when equipment changes, and attacks the selected living monster or a
living monster in the current cell. Disable it before running a script; the host
rejects simultaneous script and auto-attack control. Death or logout turns it off.

Combos come from the installed advanced skill profiles, including their existing
health, mana, and aura rules. Below 40% health, Def/Dodge profiles are preferred;
normal mode resumes at 65%. When no defensive profile exists, a class-specific
Base/Solo/Farm profile is used. Healing is dependent on those profiles, not inferred
from skill names. Unknown classes use basic attacks only.

A target with maximum health at least 100,000 and at least 20 times the player's
maximum health is treated as a boss estimate and prefers Solo; other targets prefer
Farm. This is a heuristic, not verified boss identification or encounter mechanics.
The panel reports the actual selected profile, target, and player health.

## Inspect gear and generate a farm

Select a player in-game, open **Inspect gear**, then **Inspect selected player**.
For an equipped item with a known name, **Find source** builds item-specific plans.
Review the source description and expandable generated code, then **Go — generate
and farm** saves a new C# file under `Scripts/Generated-Gear` and runs it.

Supported plans are direct permanent monster drops extracted from literal local
HuntMonster calls, and quest rewards with all objectives resolved to unambiguous
local drop sources. Scripts stop after one selected item is owned, preserve quest
objective quantities and temporary flags, and stop on failed acceptance or turn-in.
They reuse CoreBots as a helper library, not an existing farm's entry point.

This is local source evidence, not live verification of item availability. Merge
shops, unresolved prerequisites, unavailable item names, and unknown sources remain
unsupported. Generated scripts do not spend currency or guess missing locations.
Script/auto-attack conflicts are rejected. Live inspection and farming need in-game
verification. `--gear-plan-check ITEM` compiles a plan without executing game actions.

Missing equipment names now resolve first from local quest item IDs, then from
the selected player's official HTTPS character page. Character-page names are
matched by slot, not cached as item-ID facts, and labelled because the page may lag
recent equipment changes. Player identity is checked; network failures allow retry.
Public-page lookups do not hold the script start/stop gate.

Inspect gear checks your own inventory and bank, labels owned items, and disables
farming for them. Unavailable bank data is shown as unknown and blocks farming.
Go rechecks ownership and newly generated scripts independently check inventory
and bank before farming, without withdrawing an already-owned reward.
