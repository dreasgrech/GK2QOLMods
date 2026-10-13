# Built plugins

Release builds (`-p:ForDist=true`: no debug directory), one per mod, checked with `python tools/check-dist.py`. The
steps are in [`docs/release-checklist.md`](../docs/release-checklist.md). Copy the ones you want into
`Graveyard Keeper 2\BepInEx\plugins\`.

| File | Version | Game build |
|---|---|---|
| `GK2GlobalStorage.dll` | 1.1.5 | 25752683 (1.009 hotfix), built 2026-10-13 |
| `GK2BuiltCount.dll` | 1.0.6 | 25752683 (1.009 hotfix), built 2026-10-13 |
| `GK2MoveBuilding.dll` | 1.1.6 | 25752683 (1.009 hotfix), built 2026-10-13 |
| `GK2OwnedCount.dll` | 1.0.4 | 25752683 (1.009 hotfix), built 2026-10-13 |
| `GK2TechSearch.dll` | 1.0.0 | 25752683 (1.009 hotfix), built 2026-10-13 |

- **The donkey mods** (formerly GK2 Mounts and the showcase) are in [GK2DonkeyAPI](https://github.com/dreasgrech/GK2DonkeyAPI).
- **Game updates:** each plugin disables itself on a different game build until it has been re-audited and rebuilt.

**The Mods menu:** Global Storage, Built Count, Move Building, Owned Count and Tech Search also show in the game's
Mods menu when [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) is installed. The framework
is optional, and it isn't included here.
