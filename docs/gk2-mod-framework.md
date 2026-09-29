# GK2 Mod Framework (third-party) — investigation

Investigated 2026-09-29 from the download at `C:\Users\User\Downloads\GK2 Mod Framework\` (version 0.1.16),
by reading its docs and decompiling `GK2.Framework.dll` (ilspycmd, against the Steam build 25601286).
Not installed in the game.

## What it is

- A BepInEx 5 plugin by SuperMan4eg, MIT licence, GUID `ru.superman4eg.gk2.framework`.
  Nexus: https://www.nexusmods.com/graveyardkeeper2/mods/42 — source: https://github.com/SuperMan4eg/GK2-Mod-Framework
- For players: a native-style **Mods** button in the main menu and the ESC menu, listing the mods that register with
  it, with their version, compatibility state, dependencies, an Enable/Disable control, a generated Settings page,
  "Copy report" (to the clipboard) and "Quit to apply" for restart-required changes.
- For mod authors: `FrameworkApi.RegisterMod(mod, Config)` from `Awake()`, a lifecycle (`OnRegister`, `OnEnable`,
  `OnDisable`, `OnGameStarted`, `OnReturnedToMainMenu`), settings helpers that create real BepInEx `ConfigEntry<T>`s
  (toggle, int/float slider, dropdown, enum, keybind, text, action button, read-only), conditional visibility,
  JSON localization, and build-compatibility reporting.
- Mods that don't register with it keep running as normal. They just don't appear in its list.

## What the DLL actually does (decompiled, 6.5k lines)

- **Harmony patches**, all UI:
  - postfixes on `UIMainMenuWindow.Init/Open` and `UIGamePauseWindow.Init/Open` (they inject the Mods button);
  - prefix and postfix on `UIExtensions.RefreshContentFitterAndDisable` (button layout);
  - prefix and postfix on `GamepadNavigationController.Navigate`, which act only when the controller is named
    `GK2ModsMenuWindow`;
  - logging-only prefixes on `LazyButton.OnPointerClick` and `GamepadNavigationItem.Focus/Select`, which act only on
    its own `GK2ModsButton`.
- **Game events**: it adds handlers to `MainGame.OnGameStarted`, `OnGoToMainMenu`, `OnGamePaused` and
  `OnGameUnpaused` (it adds to them and doesn't replace them).
- **Build check**: it takes the SHA-256 of `Assembly-CSharp.dll` and compares it with four known hashes (the demo and
  three release builds). Our Steam build 25601286 is `37A17C02…1134`, which is not in the list, so the global status
  shows **Unknown**. Its README says as much: only the menu and Copy report were checked on 25601286.
- **Checked and not found**:
  - no network code;
  - no `Assembly.Load` and no process start;
  - no file writes other than through BepInEx config;
  - nothing touching saves (`SaveSystem` or `GameSave`).
- **The only calls with side effects**: "Copy report" writes to the clipboard, and "Quit to apply" calls
  `Application.Quit()` (the game doesn't save on quit, and its dialog warns about that).
- **Enabled entries**: for a registered mod it binds `[Framework] Enabled` into **that mod's own config file**. The
  optional-integration mode (`frameworkManagesEnabledState: false`) skips this.
- None of its patch targets overlap ours. We patch no menu, navigation or button methods.

We haven't checked that this DLL was built from the public GitHub source. It was decompiled and read, and it
matches its docs.

## How it relates to our mods

| | Framework | Ours |
|---|---|---|
| Game-update safety | SHA-256 of Assembly-CSharp; mods with `requiresKnownBuild` are blocked on unknown builds unless they run their own contract check and call `ConfirmCurrentBuildCompatibility` | MVID guard on Assembly-CSharp **and** LazyBearTechnology, `Safety.DisableOnGameUpdate`, re-audit per update (`tests/GameDiff`) |
| Settings UI | a generated menu inside the game UI, with gamepad support | only the BepInEx cfg (plus Mounts' own IMGUI panel on F8) |
| Enable/disable | its own Enabled entry, or the mod's (optional mode) | `General.Enabled` in each mod |

The overlap is small. The main thing the framework offers us is an **in-game settings menu for players** of the four
small mods. Today they have to edit the `.cfg` by hand or use BepInEx ConfigurationManager.

Our settings, and which would fit its controls:
- **Built Count**: `Enabled` and `DisableOnGameUpdate`. There's almost nothing to show.
- **Global Storage**: 7 bools. All fit.
- **Owned Count**: 9 bools. All fit.
- **Move Building**: bools, a string (`MoveLabel`) and a `KeyboardShortcut` all fit. `MovableColor` (a `Color`) has
  no control type in the framework, so it would stay cfg-only. `Debug.FaultInjection` should stay out of the menu.
- **Mounts**: 76 entries in two style sets, with a live panel whose sliders show their effect as you ride. The
  framework page doesn't suit that. The most it would get is `Enabled`, the keys, and an action button "Open the
  tuning panel".

## If we integrate: the pattern to use

The framework documents an **optional bridge**, and it's the only pattern that fits our rules:
- The main plugin stays framework-free. Nothing changes in it, so a player without the framework is unaffected.
- A separate small plugin, e.g. `GK2OwnedCount.Framework.dll` with GUID `dreas.gk2.ownedcount.framework`, has hard
  dependencies on the main plugin and on `ru.superman4eg.gk2.framework`. BepInEx skips it silently when the framework
  is absent.
- The bridge registers the main plugin's own `ConfigFile`. Registering the same Section/Key/type returns the existing
  `ConfigEntry`, so the menu edits the real values. It uses `frameworkManagesEnabledState: false`, so there's no
  second Enabled switch and nothing is added to our cfgs.
- It uses `requiresKnownBuild: false`, because our main plugin already disables itself through its MVID guard. A
  read-only row can show our own "active / disabled: game updated" state, so the framework's "Unknown build" isn't
  the only word the player sees.
- Cost: one small extra project and DLL per mod, plus a dependency on a third party's 0.x API ("preview; breaking
  changes may occur before 1.0").

Not done: installing it, a run in game, and choosing which mods get a bridge.

## Plan

**A. Install it and try it (no code of ours changes)**
1. With the game closed, confirm `git status` in the Steam folder is clean and keep copies of our six cfgs, so we
   can compare them afterwards.
2. Copy `BepInEx/plugins/GK2.Framework.dll` and `BepInEx/plugins/GK2.Framework/` (Localization) into the game.
   Leave out `Templates/`, `docs/` and the text files.
3. Launch once, then read `LogOutput.log`:
   - `GK2_FRAMEWORK_READY … Unknown`;
   - all five of our plugins still log `loaded`, with no new errors or warnings;
   - no Harmony conflicts.
4. In game (the user):
   - the main-menu Mods button, its empty-list message, and the gear with Framework Settings (window scale);
   - the ESC-menu Mods button, then back into the game;
   - Mounts: ride, open F8, and open ESC while mounted;
   - Move Building: one move;
   - the Owned Count tooltip and the Built Count numbers;
   - Global Storage crafting;
   - sleep once (a save).
5. Afterwards:
   - the only new cfg should be `ru.superman4eg.gk2.framework.cfg`, and our cfgs should match the copies;
   - `git status` in the Steam folder should show only the framework's files as new.

**Result of A (2026-09-29): passed.**
- The Mods button appeared in the main menu and the ESC menu. The empty list and the Framework Settings page
  displayed correctly (screenshots from the user).
- Log: `GK2_FRAMEWORK_READY … Unknown`, all five of our mods `loaded`, and **no errors or warnings at all**.
  Mount and dismount worked, and so did the lamp hang and Global Storage's storage discovery.
- The framework logs about 700 diagnostic lines per session (button, component and field dumps). This is noise,
  not errors.
- Our six cfgs and `BepInEx.cfg` are byte-identical to before. The only new cfg is
  `ru.superman4eg.gk2.framework.cfg`.

**B. Decide**
6. Decide:
   - which mods get a bridge;
   - the naming: `GK2<Mod>.Framework.dll`, GUID `dreas.gk2.<mod>.framework`;
   - the packaging: both DLLs in one zip (a documented option) or the bridge as a separate optional download;
   - the framework version range: `[0.1.0, 0.2.0)`.

**C. The first bridge (Owned Count)**
7. Build setup: a `FrameworkDll` property in `Directory.Build.props`, pointing at the installed DLL.
   `Private=false`, so we never ship it.
8. The main plugin exposes only what the bridge needs (`Instance`, its `ConfigFile`, the guard state). Nothing else
   changes, and it still has no reference to the framework.
9. New `src/GK2OwnedCount.Framework`:
   - register the main `ConfigFile`, with `frameworkManagesEnabledState: false` and `requiresKnownBuild: false`;
   - one row per setting, same Section/Key/type, with player-facing names, sections and order;
   - Debug entries left out;
   - a read-only status row: active, or disabled because the game updated.
10. Test in game:
    - a change in the menu reaches the cfg and takes effect;
    - Reset works;
    - remove the framework: the log shows the bridge skipped and the main mod unaffected;
    - put it back: everything works again.

**D. The other mods, one at a time, each tested like step 10**
11. Global Storage.
12. Move Building: no row for `MovableColor` or `FaultInjection`; keep an eye on the keybind capture.
13. Built Count: maybe only a status row, or no bridge at all.
14. Mounts: only `Enabled`, the keys, and a button "Open tuning panel". The panel stays the real UI.

**E. Release**
15. Versions and `dist/`, both READMEs (install with or without the framework), a test-notes entry, and a note in
    the game-update routine that bridges depend on the framework's 0.x API too.
