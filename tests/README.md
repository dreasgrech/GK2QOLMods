# Verification tools

Console projects that check the built mod DLLs against the installed game (`C:\Games\Graveyard Keeper 2`) without
launching it. Each is run after every build and after every game update. All paths inside are absolute; adjust the
`GameDir`-style paths in the `.csproj` and `Program.cs` if the game or repo lives elsewhere.

| Project | Mod | What it checks | Run |
|---|---|---|---|
| `StaticScan-GlobalStorage` | Global Storage | MetadataLoadContext suite: every game member the mod relies on exists with the expected shape (22 checks). | `dotnet run -c Release` |
| `CecilScan-GlobalStorage` | Global Storage | Mono.Cecil: the zombie and conveyor code paths still call the base methods the mod distinguishes. | `dotnet run -c Release` |
| `CecilScan-BuiltCount` | Built Count | Mono.Cecil: `UIBuildingWidget.Redraw`, the `nameLabel`/`data` fields, MVID. | `dotnet run -c Release` |
| `HarmonyHarness-BuiltCount` | Built Count | .NET Framework 4.8 process that loads the game assemblies and applies the mod's patches with HarmonyX: MVID match, patch classes construct, `PatchAll` succeeds, lists the patched methods. | `dotnet build -c Release` then run `bin\Release\net48\HarmonyHarness-BuiltCount.exe` |
| `CecilScan-MoveBuilding` | Move Building | Mono.Cecil: every IL reference into the game assemblies resolves; every by-name reflection target (fields, methods, property setters) exists with the expected type; every patch target is a single non-abstract method. | `dotnet run -c Release` |

Why no Harmony harness for Global Storage and Move Building: both touch game interfaces with default interface
methods (`IWorker` and others), which the .NET Framework runtime can't load, and BepInEx 5's Harmony build doesn't
initialise on .NET 8. Those two are verified by the Cecil/static scans plus in-game testing.

In-game test lists live in each mod's README. Move Building's fault-injection test for the save-restore path is
described in `src/GK2MoveBuilding/SAVE-SAFETY.md`.
