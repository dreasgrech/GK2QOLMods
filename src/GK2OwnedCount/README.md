# GK2 Owned Count

BepInEx plugin for Graveyard Keeper 2: every item tooltip gets a line with how many of that item you own.

```
Owned: 120  (all grades: 131)
on you 4 · in storage 116 (5 chests)
```

"On you" is your inventory (bags included) and the tool belt. "In storage" is every storage chest in every area,
the same set Global Storage draws from. "All grades" appears for graded items (the ones with a star, e.g. Bones of
different skull grades) and sums the whole family. Display only: nothing in the game changes.

Version 1.0.4, built and audited against Steam game build 25752683, game 1.009 (Assembly-CSharp MVID `f7d46132-…`,
LazyBearTechnology `f67c6993-…`; re-audit in `docs/game-update-25676698.md`). 1.0.3 was the same code for 25601286, and 1.0.1 was the same code for 25533739.
Feasibility study: [`docs/owned-count-tooltip.md`](../../docs/owned-count-tooltip.md).

## Optional: GK2 Mod Framework

With [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) (0.1.x, by SuperMan4eg) installed,
the mod appears in the game's **Mods** menu (main menu and ESC menu):
- a Status row: Active, Off, or disabled after a game update;
- the seven General and Storage switches below, which apply as soon as you change them.

The Safety and Debug settings stay in the cfg only. Without the framework, the same DLL works exactly as before;
it isn't a requirement, and there's nothing extra to install. How this is done, and the tests behind it:
[`docs/gk2-mod-framework.md`](../../docs/gk2-mod-framework.md).

## Settings (`BepInEx\config\dreas.gk2.ownedcount.cfg`)

Read at game start. The General and Storage ones are also read on every tooltip, so a change made in the Mods menu
applies at once.

| Key | Default | Meaning |
|---|---|---|
| `General.Enabled` | true | Master switch. |
| `General.OnlyOnOwnTooltip` | true | Only on an item's own tooltip (inventory, chest, vendor, craft result). Off: also on items embedded in other tooltips (craft ingredient rows, alchemy, dialogue answers). |
| `General.ShowBreakdown` | true | Append "on you N · in storage N (chests)". |
| `General.ShowPerArea` | false | Second line with the count per area, largest first. |
| `General.IncludeOtherGrades` | true | "all grades N" for graded items. |
| `Storage.IncludeFuelContainers` | false | Count fuel containers (furnace wood piles) as storage. |
| `Storage.IncludeConveyorStorage` | false | Count conveyor chests as storage. |
| `Safety.DisableOnGameUpdate` | true | Do nothing if the game's code changed since the audit. |
| `Debug.DumpItemDefs` | false | Once the game data is loaded, write every item definition to `BepInEx\GK2-itemdefs.csv` (id, name, type, stack size, grade, star, groups). |

## How it works

One Harmony postfix on `UITooltip.AddItemWidgets`, the private method that assembles every item tooltip in the game
into a list of lines. The postfix appends a separator and our line(s). The counts come from public, read-only calls:

- `Item.GetTotalCountInInventory(id)` on the player's inventory and tool belt (it walks bags itself);
- the same call on each storage chest, discovered the way Global Storage does it (active container zones →
  objects that open in the multi-inventory, non-zero size, not hidden; fuel containers and conveyor chests by setting),
  cached per frame;
- `GameBalance.starGroupItemsCache`, the game's own map of graded families, for "all grades".

Private members used (resolved at startup; any missing one disables the mod): `UITooltip.instance`,
`UITooltip.smallDescriptionTextStyle`, and the patch target `UITooltip.AddItemWidgets`.

Not counted, by design: zombie inventories, station craft slots (items in a running craft are committed), vendor
stock, items lying on the ground.

## Safety

- Nothing is written: no game method changes behaviour, no save data is touched. The only effect is one or two extra
  lines in a UI list.
- Errors inside the postfix are caught and logged once; the tooltip then shows as vanilla.
- No per-frame work (the debug dump polls until the game data exists, then stops).
- MVID guard and all-or-nothing patching as in the other mods.

## In-game test list

1. Log shows `GK2 Owned Count 1.0.3 loaded.` **once**, no errors.
2. Hover an item in your inventory: the line appears under the game's own lines; numbers match what you see.
3. Put some of it in a chest in another area; the storage count and chest count change accordingly.
4. Hover a graded item (Bones with a star): "all grades" shows the family total; the plain "Owned" shows only that grade.
5. Hover in the chest window, the vendor window and a craft result: line present. Craft ingredient rows: no line
   unless `OnlyOnOwnTooltip` is off.
6. Main menu / no save loaded: tooltips unchanged, no errors.
7. Set `Debug.DumpItemDefs = true`, restart, load a save: `BepInEx\GK2-itemdefs.csv` appears with all items; set it
   back to false.
8. With GK2 Mod Framework installed:
   - the log shows `Registered GK2 mod [dreas.gk2.ownedcount] … status=Compatible`;
   - Mods → GK2 Owned Count shows Status Active and seven switches;
   - untick "Show the owned count": the tooltip line goes and Status reads Off;
   - Reset brings it back.
9. With the framework removed: the mod loads as normal, and the log has no warning or error, and nothing mentioning
   the framework.

## Verification done before release

- `dotnet build`: 0 errors, 0 warnings from this code.
- Cecil scan (`tests/CecilScan-OwnedCount`): all IL references into the game assemblies resolve; the two private
  fields exist with the expected types; the patch target is a single method with a body.
- Round 1 in-game (1.0.0): line correct in inventory and chest tooltips; "all grades" missing for Bones, fixed in 1.0.1 (see below).
- Round 2 in-game (1.0.1): both Bones grades show `Owned: 1  (all grades: 2)` with the breakdown on its own line; no errors in the log.
- 1.0.3, new checks, now in the shared `tests/FrameworkScan`:
  - every `[HarmonyPatch]` class is listed in `Plugin.PatchClasses`;
  - `Plugin` has no method Unity would call by name;
  - only `FrameworkLink.Register` and its mod class touch `GK2.Framework`, `Register` is NoInlining, and the
    framework dependency is soft;
  - the framework-isolation check was shown to fail on a deliberately planted violation.
- Round 3 in-game (1.0.3 code, still numbered 1.0.2, 2026-09-30): tests 8 and 9 passed.
  - Run A (framework installed): registered Compatible, and the menu edits, Off status and Reset all worked, with no
    warnings.
  - Run B (framework removed): a clean log, and the tooltip was checked in game.

## Notes from testing

**1.0.0 → 1.0.1.** The "all grades" total relied on the game's `starGroupItemsCache`, which is keyed by the item id
before the colon (`pumpkin:1..3` share one group), so body parts such as `bones_1_1:2` were alone in theirs and the
total never showed. The family is now the union of that star group and the item's `gr_*` item groups
(`gr_bones` holds all six Bones grades, `gr_stories` all stories, and so on). The tooltip line was also split into two
shorter lines because the game's tooltip is narrow.

**1.0.2 → 1.0.3.** Optional GK2 Mod Framework integration (`FrameworkLink.cs`).
- Patches are applied class by class (`Plugin.PatchClasses`), not with `PatchAll(assembly)`. That call would list
  the framework-typed class, fail when the framework is absent, and make Harmony log a warning.
- During the work, a helper named `Start()` was called a second time by Unity on the first frame and patched
  everything twice. Since HarmonyX doesn't de-duplicate, the tooltip code ran twice. It was renamed to `StartMod`
  before release, and the scan now guards against it.
