# GK2 QOL Mods

Quality-of-life BepInEx plugins for Graveyard Keeper 2. One feature per plugin; each is its own project and DLL.

| Mod | Version | What it does |
|---|---|---|
| [GK2GlobalStorage](src/GK2GlobalStorage/) | 1.1.2 | Crafting, building and alchemy take materials from chests in every area, not only the one you stand in. Zombies keep using local storage. |
| [GK2BuiltCount](src/GK2BuiltCount/) | 1.0.3 | Build desk menus show how many of each building already exist in that area, e.g. "Sawhorse (2)". |
| [GK2MoveBuilding](src/GK2MoveBuilding/) | 1.1.3 | Pick up a built building and put it down elsewhere in the same area. It stays the same object: contents, crafts and links intact. |

## Layout

```
src/     one folder per mod: source, .csproj, README / findings / audit notes
tests/   verification tools run against the built DLLs and the installed game (see tests/README.md)
dist/    the built DLLs as installed and tested
```

Each mod folder has its own README (or FINDINGS) with usage, settings, how it works, the audit notes and the
in-game test list.

## Building

- Requirements: .NET SDK 8, the game installed at `C:\Games\Graveyard Keeper 2` with BepInEx 5.4.23.x (the `.csproj`
  files reference the game's `Managed` folder and `BepInEx\core` from there; change `GameDir` if yours differs).
- `dotnet build src/<Mod>/<Mod>.csproj -c Release` builds and copies the DLL into `BepInEx\plugins`. The copy only
  works while the game is closed (BepInEx keeps plugin DLLs locked). `-p:Deploy=false` skips the copy (Built Count
  and Move Building).

## Safety

- Every plugin checks the game assembly's module version id at startup and disables itself after a game update
  until it has been re-audited and rebuilt (`Safety.DisableOnGameUpdate`, on by default).
- Patches are applied all-or-nothing; a failure unpatches everything and the game runs unmodified.
- Move Building's save-safety audit, including the fault-injection test results, is in
  [src/GK2MoveBuilding/SAVE-SAFETY.md](src/GK2MoveBuilding/SAVE-SAFETY.md).

## Game build

Current audited game build: 25533739 (Assembly-CSharp MVID `97f92f0d-a093-49fe-ba4d-74aa62fae81e`).
