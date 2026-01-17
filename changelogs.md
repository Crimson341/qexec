# Skua 1.3.3.2
## Released: January 14, 2026

# Quest.txt is now QuestData.json and it updates from scripts [repo ](https://github.com/auqw/Scripts/blob/Skua/QuestData.json) now

**Full Changelog**: https://github.com/auqw/Skua/compare/1.3.3.1...1.3.3.2

---

# Skua 1.3.3.1
## Released: January 10, 2026


# Features/Changes

### Auto-Attack | Auto-Hunt
  - `Manual MapIDs` now work properly, and will attack MID[index 0], then MID[index 1]. If MID[index 0] respawns while MID[index 1] is alive, it will swap mobs 👍 

### Packet Interceptor 
  - Packet logging when `Log packets` is unchecked is now fixed

### Compiler Changes/Script Caching
  - Scripts get cached to `%APPDATA%/Skua/Scripts/Cached-Scripts`
  - This improves startup time for re-running scripts (assuming `auqw/Scripts` isn't updating as you are running them)
  - There are still planned changes to the compiler

### Planned changes for the compiler
  - Currently, the compiler takes each script we use for a certain script into one single file, then compiles that into the final running script.
  - Example: `0NecroticSwordOfDoom.cs` uses `CoreNSOD.cs` and that needs. `CoreBots.cs` (`0NecroticSwordOfDoom`>`CoreNSOD`>`CoreBots`)
  - This way of compiling for cached scripts is terrible. Anytime any script in that flow changes, it'll need to recompile everything.
  - So, to remedy this, I want to change the compiler to compile each script separately

### Minor Optimizations

### UI Changes
  - Accounts with tags will now align correctly
  - Whenever you get the `443` error for scripts, a pop-up will open saying
    - "Unable to connect to GitHub."
    - "Please check your internet connection and try again."
    - "If the problem persists, GitHub may be temporarily unavailable."

**Full Changelog**: https://github.com/auqw/Skua/compare/1.3.3.0...1.3.3.1

---

# Skua 1.3.3.0
## Released: December 20, 2025

### Changes
 - [***"Downloaded 1 Scripts" popup***](https://imgur.com/2GnQUbI) will no longer appear when *no* scripts are actually downloaded.
   - Minor Skill improvements ( don't ask, I don't remember).
   - Added "Dodge" class Use Mode back, as before this, it was crashing the client when trying to save as a non-existent mode.
 - Auras returning `NULL` during long sessions
   - Flash function `rebuildAuraArray` - filters out null/invalid auras for all the "get aura" functions
- Helpers > Runtime;
   - If a quest is registered, automatically enable `Pick Drops`
### ***__New Feature:__***
 - [Account Tags](https://imgur.com/x4FpfUz) & conditional checks to go along with it (for us coders).

**Full Changelog**: https://github.com/auqw/Skua/compare/1.3.2.0...1.3.3.0

---

# Skua 1.3.2.0
## Released: December 18, 2025

### Fixes
 - Auto > Attack/Hunt;
   - Targeting now works properly to what you click on and doesn't stray from it.
 - More Aura Fixes for the same issue as last time ( hopefully we're good now)
 - Some fixes to Advanced skills
### Additions
 - Tools > Grabber > Inventory > Sell button; 
   - Fixed it selling "all" of [item]
 - Helpers > runtime;
   - Quests can now have an optional `RewardID` ( for those "choose reward" quests), reward + accept/requirement's id will also be added.
   - Turn-ins will now use multi-turn-in. ( more turn-ins at once, before it did it one at a time)
 - `Bot.Quests.RegisterQuests();` can now also accept reward ids alongside the id... 
 E.G.:
 ```cs
 Bot.Quests.RegisterQuests((1,1), (2,3));
 ```
 - Helpers > Current Drops;
   - Search function added.
 - Faster AA[0] for CSH/CSS/other
 - Auto > Attack/Hunt;
   - Faster target swapping
   - You can insert a `MonsterMapID` array ( e.g., 1,2,3), and it'll attack them in order, going back to the beginning of the order if and when it respawns.

---

# Skua 1.3.1.0
## Released: December 08, 2025

## Fixes
1. Hopefully fixed a random crash from auras
2. Hopefully Fixed a false positive from `Skua.WPF.dll`
4. HP, Mana, and PartyHeal percentage/absolute check actually works and saves now
5. Jump panel causing hitches when you jump cells
6. Party Heal actually exists and saves now
7. "Search Scripts" would sometimes cause hitches; this **should be fixed**

## Changes
1. Login backgrounds now saves and loads from `Skua.Settings.json` instead of the separate file `background-config.json` (you will need to re-set your background)
2. The last server you selected in the manager will now save, and next time you open the manager, it'll re-select it

### Minor changes (not important)
1. Added a flash trust file for skua
2. Centralized version change in `Directory.Build.props`

**Full Changelog**: https://github.com/auqw/Skua/compare/1.3.0.3...1.3.1.0

---

# Skua 1.3.0.3
## Released: December 03, 2025

- Packet Interceptor: 
  - now connects to the correct proxy port
- Auras de-serialization:
  -  *should* be fixed

**Full Changelog**: https://github.com/auqw/Skua/compare/1.3.0.2...1.3.0.3

---

# Skua 1.3.0.2
## Released: November 22, 2025

Fixed regex error

Added Wearing bool to ItemBase to check what items we're wearing (It's good for CoreBots not removing cosmetics)

**Full Changelog**: https://github.com/auqw/Skua/compare/1.3.0.1...1.3.0.2

---

# Skua 1.3.0.1
## Released: November 21, 2025

Fixed the interceptor, only using port 5588, which caused us not to be able to connect to servers that didn't use that port
updated most nuget packages which intern could help performance

**Full Changelog**: https://github.com/auqw/Skua/compare/1.3.0.0...1.3.0.1

---

# Skua 1.3.0.0
## Released: November 10, 2025

## What's Changed
* Aura support for scripts and advskills
* Rounded Corners for Windows 11 users
* More memory leaks have been fixed
* Update InventoryItem.cs by @SharpTheNightmare in https://github.com/auqw/Skua/pull/5
* Canuseskill skill check by @SharpTheNightmare in https://github.com/auqw/Skua/pull/6
* CollectionViewer will not have full priority by @SharpTheNightmare in https://github.com/auqw/Skua/pull/7
* Forced skill.auto to false by @SharpTheNightmare in https://github.com/auqw/Skua/pull/11
* Added ProcID And updated Documentation by @SharpTheNightmare in https://github.com/auqw/Skua/pull/12
* added wikilinks (limited) by @SharpTheNightmare in https://github.com/auqw/Skua/pull/18
* added death reset to advskills by @SharpTheNightmare in https://github.com/auqw/Skua/pull/19
* `%LOCALAPPDATA%` config files have moved to `%APPDATA%`. The whole config system had to be written from scratch, so now the problem is that sometimes something randomly goes wrong and resets the config that just will not happen (`%APPDATA%\Skua\ManagerSettings.json` and `%APPDATA%\Skua\ClientSettings.json`)

### If you know how to get your accounts from the `Skua.Manager` config folder, the new format is

From this
```xml
<string>DisplayerName{=}AccName{=}Password</string>
```
to 
```json
"DisplayName{=}AccName{=}Password"
```
e.g., new config for multiple

```json
"ManagedAccounts": [
    "User1{=}User1{=}Password1",
    "User2{=}User2{=}Password2",
    "User3{=}User3{=}Password3"
  ],
```
## TATO JOINED SKUA TEAM!!!!

**Full Changelog**: https://github.com/auqw/Skua/compare/1.2.5.4...1.3.0.0

---

