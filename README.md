# ETS2 Profile Manager

Modern (WinUI 3) tool to manage save game profiles for *Euro Truck Simulator 2* and *American Truck Simulator*.

## Features

### Profile management
- List ETS2 + ATS profiles (username, game, folder ID, last access)
- Copy profile to a new one, rename, delete (all with safety backups)
- Backup to / restore from zip (restore validates the zip and backs up current state first)
- Decrypt `profile.sii` for manual editing, open profile path in Explorer
- Multi-home discovery: finds profiles under `Documents` on any drive (e.g. custom `-homedir` setups), not just `C:`

### Settings sync (wheel painkiller)
Game settings live per-profile, so a tuned wheel has to be reconfigured for every profile. Pick a **source** profile, **target** profiles (same game only), and **setting groups**:

| File | Groups |
| ---- | ------ |
| `controls.sii` | Force feedback, Steering tuning, Deadzones & invert, Axes & devices ⚠, Button & key bindings ⚠, Other constants |
| `config_local.cfg` | Shifting & transmission, Pedals & brake, Wheel range & camera |
| `config.cfg` | Driving aids & stability |

Live diff preview (`key: old → new` per target), explicit confirmation, auto-backup of every target before writing, atomic writes with re-validation. `controls.sii` indices are never renumbered.

### Save editor
Edit money, XP, ADR classes and driver skills per save slot. Requires decryptable saves: set `uset g_save_format "2"` in `config.cfg`, load the game and save once.

### Steam Cloud profiles
Cloud profiles (`steam_profiles/`) appear tagged and can be **sync targets** for settings, backed up, and opened in Explorer. Copy/rename/delete/save-edit/restore refuse them — Steam owns those files. Save editing is additionally impossible: Cloud saves live server-side, so there is no local `game.sii`; uncheck Steam Cloud in-game to materialize the profile locally, then edit it here.

### Safety model (revert options)
- Every mutation (sync, rename, save-edit, delete) takes a timestamped backup to `<gamehome>/ets2-profile-manager-backups` first (keeps last 10 per profile)
- Sync/restore/rename/save-edit **refuse** to write if the backup failed
- Restore validates the zip (`profile.sii` at root) and backs up current state before overwriting
- Game-running guard on all mutating operations

## Requirements
- Windows 10 1809+ / 11, x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (releases are framework-dependent)
- Steam Cloud **disabled** per profile (profile selection → Edit profile → uncheck Steam Cloud):

![](./Profilesettings.png)

## Install
Download `ets2-profile-manager-win-x64.zip` from [Releases](../../releases), extract anywhere, run `ets2-profile-manager.exe`. No installer, no admin rights, no telemetry — everything runs locally.

## Build from source
```
dotnet build ets2-profile-manager.sln -c Release
```
Output: `ets2-profile-manager/bin/Release/net8.0-windows10.0.19041.0/win-x64/ets2-profile-manager.exe`. Needs .NET 8 SDK.

Releases are cut by pushing a tag: `git tag v1.2.0 && git push origin v1.2.0` — see `.github/workflows/release.yml`.

## Privacy
No usage tracking. All operations run locally; nothing is sent anywhere.

## Credits
- Original tool: [elpatron68/TruckSim-PM](https://github.com/elpatron68/TruckSim-PM) (WTFPL) — profile management concept and `SII_Decrypt` integration
- [playhaux/ETS2ATS-Profile-Manager](https://github.com/playhaux/ETS2ATS-Profile-Manager) (Apache-2.0, itself a fork of the above) — save-game editor engine (money/XP/skills regex logic) and rename flow patterns, ported without its telemetry
- Decryption engine: [SII_Decrypt](https://github.com/TheLazyTomcat/SII_Decrypt) by TheLazyTomcat (MPL-2.0, embedded binary)
- UI: [WinUI 3 / Windows App SDK](https://github.com/microsoft/WindowsAppSDK) (MIT)

## 3rd party licenses
| Project | License |
| ------- | ------- |
| [SII_Decrypt](https://github.com/TheLazyTomcat/SII_Decrypt) | [MPL-2.0](https://www.mozilla.org/MPL/2.0/) |
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) | [MIT](https://opensource.org/license/mit) |

License of this project: same as the original — do what you want (WTFPL-2.0, see original README history).
