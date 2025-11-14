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

