# GK2 Owned Count

BepInEx plugin for Graveyard Keeper 2: every item tooltip gets a line with how many of that item you own.

```
Owned: 120  (all grades: 131)
on you 4 · in storage 116 (5 chests)
```

"On you" is your inventory (bags included) and the tool belt. "In storage" is every storage chest in every area,
the same set Global Storage draws from. "All grades" appears for graded items (the ones with a star, e.g. Bones of
different skull grades) and sums the whole family. Display only: nothing in the game changes.

Version 1.0.1, built and audited against game build 25533739 (Assembly-CSharp MVID `97f92f0d-…`).
Feasibility study: [`docs/owned-count-tooltip.md`](../../docs/owned-count-tooltip.md).

## Settings (`BepInEx\config\anvil.gk2.ownedcount.cfg`, read at game start)

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

1. Log shows `GK2 Owned Count 1.0.1 loaded.`, no errors.
2. Hover an item in your inventory: the line appears under the game's own lines; numbers match what you see.
3. Put some of it in a chest in another area; the storage count and chest count change accordingly.
4. Hover a graded item (Bones with a star): "all grades" shows the family total; the plain "Owned" shows only that grade.
5. Hover in the chest window, the vendor window and a craft result: line present. Craft ingredient rows: no line
   unless `OnlyOnOwnTooltip` is off.
6. Main menu / no save loaded: tooltips unchanged, no errors.
7. Set `Debug.DumpItemDefs = true`, restart, load a save: `BepInEx\GK2-itemdefs.csv` appears with all items; set it
   back to false.

## Verification done before release

- `dotnet build`: 0 errors, 0 warnings from this code.
- Cecil scan (`tests/CecilScan-OwnedCount`): all IL references into the game assemblies resolve; the two private
  fields exist with the expected types; the patch target is a single method with a body.
- Round 1 in-game (1.0.0): line correct in inventory and chest tooltips; "all grades" missing for Bones, fixed in 1.0.1 (see below).
- Round 2 in-game (1.0.1): both Bones grades show `Owned: 1  (all grades: 2)` with the breakdown on its own line; no errors in the log.

## Notes from testing

**1.0.0 → 1.0.1.** The "all grades" total relied on the game's `starGroupItemsCache`, which is keyed by the item id
before the colon (`pumpkin:1..3` share one group), so body parts such as `bones_1_1:2` were alone in theirs and the
total never showed. The family is now the union of that star group and the item's `gr_*` item groups
(`gr_bones` holds all six Bones grades, `gr_stories` all stories, and so on). The tooltip line was also split into two
shorter lines because the game's tooltip is narrow.
