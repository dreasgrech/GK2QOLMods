# Verification tools

Console projects that check the built mod DLLs against the installed game without launching it. The game folder
comes from `GameDir` in the repo's `Directory.Build.props` (the Steam install); `tests/Directory.Build.props` passes
it to every test (`GamePaths.cs`). Each is run after every build and after every game update.

**After a game update, first:** `dotnet run -c Release -- "<previous game folder>"` in `GameDiff` lists every
member of `Assembly-CSharp` and `LazyBearTechnology` that was removed, added or changed (full list in
`%TEMP%\gamediff.txt`). Check it against every patch target and every game member the mods use, then run the
scans below, then update the audited MVIDs and version strings.

| Project | Mod | What it checks | Run |
|---|---|---|---|
| `GameDiff` | all | Member-by-member diff of two game installs (`Assembly-CSharp`, `LazyBearTechnology`): removed / added / changed, compiler-generated removals counted apart. | `dotnet run -c Release -- "<old game folder>" ["<new game folder>"]` |
| `StaticScan-GlobalStorage` | Global Storage | MetadataLoadContext suite: every game member the mod relies on exists with the expected shape (24 checks). | `dotnet run -c Release` |
| `CecilScan-GlobalStorage` | Global Storage | Mono.Cecil: the zombie and conveyor code paths still call the base methods the mod distinguishes. | `dotnet run -c Release` |
| `CecilScan-BuiltCount` | Built Count | Mono.Cecil: `UIBuildingWidget.Redraw`, the `nameLabel`/`data` fields, MVID. | `dotnet run -c Release` |
| `HarmonyHarness-BuiltCount` | Built Count | .NET Framework 4.8 process that loads the game assemblies and applies the mod's patches with HarmonyX: MVID match, patch classes construct, `PatchAll` succeeds, lists the patched methods. | `dotnet build -c Release` then run `bin\Release\net48\HarmonyHarness-BuiltCount.exe` |
| `CecilScan-MoveBuilding` | Move Building | Mono.Cecil: every IL reference into the game assemblies resolves; every by-name reflection target (fields, methods, property setters) exists with the expected type; every patch target is a single non-abstract method. | `dotnet run -c Release` |
| `CecilScan-OwnedCount` | Owned Count | Mono.Cecil: every IL reference into the game assemblies resolves; `UITooltip.instance`, `smallDescriptionTextStyle` and the `AddItemWidgets` patch target exist. | `dotnet run -c Release` |
| `FrameworkScan` | Owned Count, Global Storage (every mod with the optional Mods-menu integration) | Mono.Cecil, on each mod's built DLL: every `[HarmonyPatch]` class is in `Plugin.PatchClasses`, and `Plugin` never calls `PatchAll(Assembly)`; `Plugin` has no method Unity calls by name (Start, OnEnable, …); `GK2.Framework` is used only by `FrameworkLink.Register` (NoInlining) and the classes nested in `FrameworkLink`; there's a soft `BepInDependency` on the framework. See `docs/gk2-mod-framework.md`. Mutation-tested: a planted framework field and a missing patch class each fail it. | `dotnet run -c Release [ModName ...]` |
| `CecilScan-Mounts` | Mounts | Mono.Cecil: every IL reference into the game assemblies resolves; the `CreateScript` patch target and the `Wgo`/`WgoData`/animation members the mount calls exist as single overloads. | `dotnet run -c Release` |

Why no Harmony harness for Global Storage and Move Building: both touch game interfaces with default interface
methods (`IWorker` and others), which the .NET Framework runtime can't load, and BepInEx 5's Harmony build doesn't
initialise on .NET 8. Those two are verified by the Cecil/static scans plus in-game testing.

In-game test lists live in each mod's README. Move Building's fault-injection test for the save-restore path is
described in `src/GK2MoveBuilding/SAVE-SAFETY.md`.
