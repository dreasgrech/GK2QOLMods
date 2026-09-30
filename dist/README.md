# Built plugins

The DLLs as installed and tested, one per mod. Copy them into `Graveyard Keeper 2\BepInEx\plugins\`.

| File | Version | Game build |
|---|---|---|
| `GK2GlobalStorage.dll` | 1.1.4 | 25601286 |
| `GK2BuiltCount.dll` | 1.0.5 | 25601286 |
| `GK2MoveBuilding.dll` | 1.1.5 | 25601286 |
| `GK2OwnedCount.dll` | 1.0.3 | 25601286 |
| `GK2Showcase.dll` (placeholder name) | 0.1.0 | 25601286 |
| `GK2Mounts.dll` | 0.1.0 (API 0.5.0) | 25601286 |

- **Install the showcase together with `GK2Mounts.dll`.** It needs it; BepInEx logs "missing dependencies"
  otherwise.
- **GK2 Mounts alone does nothing.** It's the library the showcase, and other mods, build on.
- **Release builds:** both were built with `-p:ForDist=true`, so no source path is baked in, and the dev panel has
  no "Make these the mod defaults" button. The dev panel is off by default.
- **Game updates:** each plugin disables itself on a different game build until it has been re-audited and rebuilt.

**The Mods menu:** Global Storage, Built Count, Move Building, Owned Count and the showcase also show in the game's
Mods menu when [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) is installed. The framework
is optional, and it isn't included here.
