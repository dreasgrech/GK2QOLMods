# Save-safety audit of GK2 Move Building 1.1.2

Question: can the mod corrupt a player's save in any way? Audited 2026-09-27 against game build 25533739 by reading
every write the mod makes and the game's serialization and load code. "Corrupt" here means: a save the game can't
load, or persistent state the game never repairs (an object missing from lists it belongs in, dangling references,
duplicated entries, invalid values).

## How the game saves

- Odin binary serialization of the `GameSave` object graph (`OdinBinaryFileSerializer.Serialize`), synchronous, on
  the main thread. The file is written as `.new`, the old one becomes a backup, then the new one is moved into place.
  A crash mid-write can't leave a truncated main file.
- Nothing about saving is threaded, so a save can never observe the mod half-way through a move: every data change
  the mod makes happens synchronously inside one frame, in a Harmony prefix of the build-key handler.

## Every saved field the mod writes, and why each is safe

| Saved field | How it's changed | Safety |
|---|---|---|
| `WgoData.position` | assignment (the game's setter) | Only after the game's own preview said the spot is valid; same value the game would give a new building there. |
| `WgoPartData.rotationIndex` | assignment | Only when the preview is the same object type (same part asset) and reports a rotation the part supports (`CanBeRotated`). |
| `WgoData.worldZoneDataId` | `WorldZoneData.RemoveWgoData` / `TryAddWgoData` (game methods) | Set by the game code; must resolve to the original zone or the move is rolled back. |
| `WorldZoneData.wgoDataList`, `customQualityWgoDataList` | same two game methods | Same steps the game's own save repair `SaveFixContext.MoveWgo` performs. Persisted, not recomputed at load, hence the rollback below. |
| `WorldZoneData.maxReachedQuality` | the game's `TryCallOnMaxQualityChangedExpressions` inside `TryAddWgoData` | Only increases when total quality exceeds the recorded max, exactly as when the player builds. |
| `WgoData.workbenchParents`, `attachedWorkbenchExtensions` | `Remove…`/`Add…` (game methods), both sides always together | Never a one-sided (dangling) link: the mod removes both ends; the game's `TryRegisterWorkbenchExtensionDelayed` adds both ends. |
| `WgoData.gdPointsData`, `WorldData.gdPointsData.scenePoints` | the game's `RegisterGDPointsFromBakedData` → `RewriteGdPointsData` | Old entries removed, new ones added, same as the first spawn. On failure the mod clears the list and resets `gdPointsRegistered` so the next load registers them afresh. |

Nothing else in the save is touched. The mod never calls `RemoveWgoDataFromGameScene`, `AddWgoData`,
`ChangeWgoData` or `ReplaceWgoData`, never creates or destroys a `WgoData`, never changes `UniqueId`, `id`,
`WorldId`, inventories, crafts, workers, game-res flags, orders or zombie state. The preview object is the game's own
temporary object and is never added to the scene.

## What it never does

- No new serialized types or fields (a mod-specific type in the graph would make the save unloadable without the mod).
- No writes outside the one moved object and the zone lists it belongs to.
- No background work between moves.

## Failure handling (the part that matters)

An interrupted move used to be the one way to leave persistent inconsistency: an exception after the object left
its zone list but before it re-joined would leave it in no zone, and zone membership is saved as-is. Since 1.1.1:

1. All data-layer steps (cut links, leave zone, set position/rotation, re-join zone) run in one `try`.
2. Any exception, or landing in a different zone, calls `Restore`: remove the object's id from **every** zone list in
   the scene (a zone add can fail after the list entry but before the back-reference is set), reset position and
   rotation, re-add to the original zone, verify the list contains it, re-run the game's link routine.
3. If even the restore fails, the log says so with the object's id and asks for a reload of the last save; the game
   hasn't saved in between, so the last save is intact.
4. View-only steps (part state, nav cut refresh, chunk registration) run afterwards in their own `try`; a failure
   there is visual and logged.
5. The two patch entry points (pick-up click, place click) catch everything, so no exception reaches the game's
   update loop and its exception window.

Refusals happen before anything is written: eligibility (`CanMove`) is side-effect free and is also what the
highlighter uses.

## Load-time behaviour

- `WorldZoneData.PrepareForGame` and `WorldZone.AddWgosOnGameSceneStart` iterate the saved `wgoDataList`; the
  moved object is in exactly one list, its position matches the zone's rectangle (checked by `TryAddWgoData`).
- Navigation cuts are rebuilt at load from the zone list and the saved position, so they match the new spot.
- Workbench links are saved as re-established; no dangling ids (the game's `UnlinkRemainingWorkbenchExtensions`
  would tolerate them anyway).
- `SaveFixContext` indexes objects by `UniqueId`; unchanged, so no "duplicate uniqueId" warnings.

## Compared with the reference mod

It removes the old object from the scene and adds a new one, so its saved side effects include a new id (every
saved reference to the old one dangles until it patches them), `OnRemove` side effects (town quality, NPC groups,
delayed events), and, on its same-object paths, `ChangeId`'s rebuilt inventory and HP. Its version history includes a
routine to rescue corpses its earlier release left inside garden beds. None of those paths exist here.

## Save triggers and co-op (checked 1.1.2)

- `SaveSystem.Save` is synchronous: `PrepareToSave` → `OnBeforeSerialize` → Odin serialize → write `.new` → swap.
  The only in-game caller is `EnergySystem` (going to sleep), plus the menu. Nothing periodic or threaded.
- The game has co-op netcode. Its own removal (`RemoveWgoDataFromGameScene`) is a `[NetworkMethod]`; our zone
  writes are not replicated, so a move in a co-op session would desync the two saves. Since 1.1.2 `CanMove` refuses
  in co-op, using the same test the game's bug reporter uses (`LazyNetwork.NetworkManager.IsCoopGame`).

## Residual risks (honest list)

- **Game update.** A changed game method could behave differently; the MVID guard disables the mod until re-audited.
- **Another mod** patching the same zone/link methods could interleave. None known; Global Storage and Built Count
  don't touch them.
- **Restore failing after a failure** (double fault) is logged with a reload instruction; the last save is untouched.
- **Zombie pathing state** (a porter mid-walk to the old spot) is runtime, not saved, and resolves by itself.

## Fault-injection test (1.1.3)

`Debug.FaultInjection` in the config (present only with the empty marker file `BepInEx\config\dreas.gk2.dev`, since
2026-10-13) throws inside the guarded block at a named step: `AfterLinksCut`,
`AfterZoneRemove`, `AfterPosition`, `AfterZoneAdd`, or `WrongZone` (pretends the landing zone differs). The config is
re-read at every move, so the value can be changed between moves without restarting. The move is refused ("The move
failed and was undone.") and the mod logs `Move restore check for <id>: PASS/FAIL` with position, rotation, zone,
the number of zone lists holding the id (must be 1, with 1 entry) and whether the object is still in the world; a
second line a few frames later checks that the workbench links are back to the count before the move. Results are
recorded below once run.

### Results (2026-09-27, game build 25533739, mod 1.1.3, user's save)

| Case | Object | Restore check | Links check |
|---|---|---|---|
| AfterLinksCut | sluice_box, furnace_1 | PASS (position, rotation, zone yard, 1 list / 1 entry, in world) | PASS (0/0) |
| WrongZone | sluice_box (already added to the zone at the new spot) | PASS | PASS (0/0) |
| AfterZoneAdd | sluice_box (already added to the zone at the new spot) | PASS | PASS (0/0) |
| normal move after clearing the setting | sluice_box | moved, then moved back | n/a |

AfterZoneRemove and AfterPosition were not run separately; their state is a strict subset of AfterZoneAdd's
(object out of its zone, position changed, not yet re-added), and the same `Restore` handles all of them.
Closed by the tool_rack runs below: a faulted move on a building with workbench links, links check with a
non-zero count.
| AfterZoneAdd (x2) | tool_rack, linked to its workbench | PASS | PASS (1 now, 1 before) |
| WrongZone (x3) | tool_rack | PASS | PASS (1 now, 1 before) |

The linked case closes the last open point: the links are cut before the move and re-made by the game's own routine
after the restore, ending with the same count. Every restore in every case left exactly one zone list entry, the old
position and rotation, and the object in the world. Conclusion: an interrupted move cannot leave the save inconsistent.
