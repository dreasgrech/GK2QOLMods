# Changelog

Changes to the mods in this repository, per mod, newest first. Every change is added under **Unreleased** of its mod
when it is made; at a public release that heading becomes the version and date, and the entry is the Nexus changelog
text for that version. Released versions are never reused.

Versions when this changelog was started (2026-10-16): Global Storage 1.1.5, Built Count 1.0.6, Move Building 1.1.6,
Owned Count 1.0.4, Tech Search 1.0.0. Earlier changes are in git history and each mod's README.

## Global Storage

### Unreleased

- No LINQ (it allocates garbage in a Unity game, 2026-10-25): the storages' order (fuel first, then the ones added,
  then by size) is sorted with a plain comparison, the same order as before; the dependency check at start (shared with every mod here) sorts and looks up without LINQ; the manifest it embeds is unchanged.

## Built Count

### Unreleased

- No LINQ (it allocates garbage in a Unity game, 2026-10-25): the dependency check at start (shared with every mod here) sorts and looks up without LINQ; the manifest it embeds is unchanged.

## Move Building

### Unreleased

- No LINQ (it allocates garbage in a Unity game, 2026-10-25): the occupied-dock check and the walk through the
  scene's zones by priority are plain loops, the same results; the dependency check at start (shared with every mod here) sorts and looks up without LINQ; the manifest it embeds is unchanged.

## Owned Count

### Unreleased

- No LINQ (it allocates garbage in a Unity game, 2026-10-25): the dependency check at start (shared with every mod here) sorts and looks up without LINQ; the manifest it embeds is unchanged.

## Tech Tree Search

### 1.0.1 (2026-10-29)

The first version on Nexus Mods (the zip extracts into the game folder: `BepInEx\plugins\GK2TechTreeSearch\`).
Nothing changes for players.

- Renamed from GK2 Tech Search to GK2 Tech Tree Search: the file is `GK2TechTreeSearch.dll`, the ID `dreas.gk2.techtreesearch`.
  Your settings are copied from the old `dreas.gk2.techsearch.cfg` on the first start. If the old `GK2TechSearch.dll` is
  still in `BepInEx\plugins`, this one stays off and the log says so: delete the old file.

- No LINQ (it allocates garbage in a Unity game, 2026-10-25): the dependency check at start (shared with every mod here) sorts and looks up without LINQ; the manifest it embeds is unchanged.

### 1.0.0 (2026-10-04)

- First release, on GitHub (`dist/`): the search box, searching what techs unlock, the jump, the Mods-menu rows with GK2 Mod Framework.

## Development

- No LINQ in any mod here from now on: `Directory.Build.targets` adds the banned-API analyzer to every netstandard2.1
  project (`BannedSymbols.txt`: `System.Linq.Enumerable`, `Queryable`, `ParallelEnumerable`), error RS0030 at the
  line; `tests/NoLinqScan` checks every built DLL and runs with the release scans. The .NET test tools may use it.
