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
- ~~It uses `requiresKnownBuild: false`~~ **Changed 2026-10-02 (game 1.008):** with `false` the framework never
  judges the mod, so its list said "Compatible" for all our mods while their own guard had switched them off. Now
  every bridge uses `requiresKnownBuild: true` and calls `context.ConfirmCurrentBuildCompatibility` in `OnRegister`
  only when the mod's `Status` is "Active" (the showcase: only while GK2 Mounts runs). On a game update the list then
  says "Unknown build" (shown as an error), and the read-only Status row in Settings gives our own reason. Settings
  stay reachable either way. One limit: if the framework ever lists the current build's hash as known, it reports
  "Compatible" without asking, whatever our guard says.
- **No framework dependency in the list (2026-10-02):** the bridges used to list the framework as a dependency with
  the range `[0.1.0, 0.2.0)`. The menu printed it with no "(optional)", which read as if our mods needed the
  framework. Now they list none, so the menu says "None", like Grave Zoom. A framework whose API changed makes
  `Register` fail inside `TryRegister`'s catch, and the mod runs on without its menu entry.
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

**Decided: one DLL per mod, with no separate bridge** (the user's choice, 2026-09-29). A separate bridge DLL would
make BepInEx log an **error** for players without the framework ("Could not load … missing dependencies",
`Chainloader` → `Logger.LogError`). Instead each mod carries its framework code, which only runs when the framework
is loaded:
- a soft `BepInDependency` on `ru.superman4eg.gk2.framework` (load order only);
- `FrameworkLink.TryRegister` checks `Chainloader.PluginInfos`, then calls the `NoInlining` `Register`, inside a
  try/catch;
- only `Register` and the nested `Gk2ModBase` class reference framework types. Mono loads `GK2.Framework.dll` only
  when such a method is compiled;
- patches go in by class (`Plugin.PatchClasses`) and not with `PatchAll(assembly)`. The latter calls
  `Assembly.GetTypes()`, which throws for the unloadable nested class, and HarmonyX then logs a warning.
  BepInEx's Cecil scan catches `AssemblyResolutionException`, so it needs no change;
- `tests/FrameworkScan` checks, for every integrated mod, that every `[HarmonyPatch]` class is listed, that no code outside
  `FrameworkLink.Register` and its mod class references `GK2.Framework` (in base types, fields, signatures, locals
  or IL), and that `NoInlining` and the soft dependency are in place. A mutation test (a framework-typed field
  added to `Plugin`) made it fail as it should;
- we build against `lib/GK2.Framework/GK2.Framework.dll`, a pinned 0.1.16 copy (MIT, with its LICENSE), and never
  ship it.

This goes **against the framework's own advice**. PUBLIC_API and OPTIONAL_INTEGRATION warn that referencing it
directly "can still require" the DLL. That's a deliberate choice: we checked it against the loader code of BepInEx
5.4.23.5 and HarmonyX, and it is proven only by an in-game run with the framework removed (the log must show no
`TypeLoad`, `FileNotFound`, `ReflectionTypeLoad`, `GetTypesFromAssembly` or `Could not load` lines).

**Run A (framework installed), 2026-09-30:**
- `Registered GK2 mod [dreas.gk2.ownedcount] 1.0.2 by dreas; status=Compatible`, loaded after the framework (the
  soft dependency), with no warnings or errors.
- The settings page was opened twice. The cfg ended unchanged (untick, then Reset).
- **Bug found:** the refactor named a helper `Start()`. Unity calls that on every MonoBehaviour's first frame, so
  the plugin checked the game build and **patched a second time** ("loaded." twice in the log). HarmonyX doesn't
  de-duplicate (`PatchInfo.Add` concatenates), so the tooltip patch ran twice.
  - Fix: renamed it to `StartMod`.
  - The scan (now `tests/FrameworkScan`) fails if `Plugin` has a method named after a Unity message (Start, OnEnable, OnDisable,
    FixedUpdate, LateUpdate, OnGUI, OnApplicationQuit).

**Run A again, after the fix:** "loaded." appears once, with the registration and no warnings or errors. The
settings page was opened three times, and the cfg ended unchanged.

**Run B (framework removed), 2026-09-30: passed.**
- "5 plugins to load", and all five of ours `loaded`, Owned Count once.
- The log has no line mentioning `TypeLoad`, `FileNotFound`, `ReflectionTypeLoad`, `GetTypesFromAssembly`,
  `Could not load`, `GK2.Framework` or `superman4eg`, and no warning, error or exception at all.
- The user checked the tooltip in game.

**So the single-DLL approach works with the framework both installed and absent.** The framework went back
afterwards (byte-identical).

**Owned Count 1.0.3 is released:** in `dist/` (byte-identical to the installed and built DLL), with its README,
the root and dist READMEs, and tests/README updated.

**Global Storage 1.1.4 is released** (2026-09-30).
- In the menu: Status plus 5 switches.
- Run A and Run B both passed. Details are in `src/GK2GlobalStorage/FINDINGS.md`, "v1.1.4".
- Its checks moved into the shared `tests/FrameworkScan`, which now covers both mods.

**Move Building 1.1.5 is released** (2026-09-30). The menu has Status plus 6 settings, and it's the first mod to
use the framework's **text** and **keybind** controls.
- A text field saves only on Enter or when the field loses focus. Both controls log `GK2_MOD_SETTING_CHANGED`,
  which is how the tests were verified.
- The key capture can't set `None`, because Esc cancels it. It stores a combination as main key plus modifiers,
  and our mod reads only the main key. The row's menu description therefore says so.
- The per-move `Config.Reload()` is compatible with menu edits. BepInEx saves on every change, and Reload updates
  the same entry objects.
- Run A passed:
  - "Relocate" was saved and shown;
  - Left Alt was captured and then used for a move;
  - the tint toggled;
  - the log was clean.
- Run B passed with a clean log and three moves.

**Built Count 1.0.5 is released** (2026-09-30).
- The menu has Status plus "Show built counts".
- Its HarmonyX harness now loads the mod without `GK2.Framework.dll` and checks that only the framework class fails
  to load. The old harness (`GetTypes()`, then `PatchAll(asm)`) failed with `ReflectionTypeLoadException`, which is
  the failure `PatchClasses` exists to avoid.
- Run A passed, with all four mods in the Mods menu. Run B passed with a clean log.

**All four released mods are now in the Mods menu.** Mounts remains; it isn't released yet, and its panel is the
real UI.

**Rebuilds and git:** each DLL records the git commit it was built from (SourceLink's `+<hash>` in
AssemblyInformationalVersion). So a rebuild after a commit differs byte-for-byte even when the code hasn't
changed. `dist/` keeps the copy that was tested in game.

The build's MSB3277 System.IO.Compression warning is older than this change: Global Storage, which has no framework
reference, shows it too.

**B. Decide** (superseded for packaging by the decision above)
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
