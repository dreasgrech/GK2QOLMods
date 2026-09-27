# Storage and item stacking in Graveyard Keeper 2

Investigation, 2026-09-27, game build 25533739. Read from the decompiled game (`Item.cs`, `ItemDef.cs`,
`Inventory.cs`, `UITooltip.cs`, `UIItemCell.cs`, `UIBaseChestWindowData.cs`) and a raw scan of the balance asset.
Purpose: understand why chests end up with duplicates that "should" stack, why some same-named items don't stack,
and what a storage helper mod could do about it.

## 1. How stacking works

- **Two items stack if, and only if, they have the same item id** (`Item.CanAddItemCount`: `if (id != sourceItem.id) return 0`).
  Nothing else is compared: not durability, not bag contents, not any per-item property. Properties are only carried
  when the item is single-stack (see below).
- **Every item definition has its own stack size**, `ItemDef.stackCount`, parsed from the balance sheet column
  `stack_count`. A stack holds at most that many; the remainder goes into a new slot. The full table for build
  25533739 (814 items, dumped from the running game by Owned Count's `Debug.DumpItemDefs`) is in
  [`itemdefs-25533739.csv`](itemdefs-25533739.csv). Distribution:

  | Stack size | Items | Typical |
  |---|---|---|
  | 1 | 274 | tools, weapons, armour, bags, bows, rods, books, sermons (69), talismans, corpses, supply boxes |
  | 10 | 26 | collars and a few others |
  | 20 | 191 | body parts (all grades), bait, embalming fluids, many materials |
  | 25 / 30 | 3 / 138 | materials |
  | 50 | 120 | materials |
  | 99 / 100 | 51 / 4 | bulk materials |
  | 999 | 5 | resources such as faith |

  So a chest full of "20-stack" body parts fills up six times faster than one holding "99-stack" nails, which is
  part of why storage sprawls.
- **Single-stack items** (`stackCount == 1`: tools, bags, corpses) each take a slot. Only they carry per-item data
  (`SerializedItemProperty`: durability, bag contents, fuel state, chest filters), which is transferred on split.
- **Order of filling** (`Item.AddItemToInventory`): bags that already contain the item → existing stacks with room →
  new slots → other bags. So the game does top up existing stacks before opening new ones, *within one inventory*.
- **Chest filters** are enforced at the add step (`CanAddItemCountToInventory` returns 0 when a chest has a
  white-list without the item or a black-list with it). Any mod that moves items must go through this call.
- **Auto-expanding inventories** (`AutoExpandSerializedItemProperty`) grow up to 1000 slots as they fill.

Consequence for the player: duplicates across chests are never merged automatically, because the game only ever
looks inside the one inventory you drop into. The zone-wide "pool" that crafting uses (`MultiInventory`) counts
across chests, but nothing consolidates them.

## 2. The "Bones" that don't stack

They are **different items**. The balance data contains graded variants (from the dump):

```
id            name   stack  quality  red  white
bones_0_0:1   Bones   20      1       0    0
bones_0_1:1   Bones   20      1       0    1
bones_1_1:2   Bones   20      2       1    1
bones_2_1:2   Bones   20      2       2    1
bones_1_3:3   Bones   20      3       1    3
bones_3_1:3   Bones   20      3       3    1
skull_0_0:1 … skull_3_2:3   (same pattern)
```

The id encodes red and white skull values (`ItemDef.redSkulls` / `whiteSkulls`), and the `:n` suffix is the star
level (`quality`, 1 to 3) drawn on the icon. All six body parts (bones, skull, skin, heart, guts, brain) come in six
grades each; 169 items in total are graded ("Star" quality type), including three-grade families of prayers,
pumpkins, seeds and wooden parts. Related crafts
exist for every grade: `extract_bones_x_y`, `insert_bones_x_y`, `change_bones_x_y`, and the organ improvers
`organ_improver_white_bones` / `organ_improver_red_bones`. So grade is gameplay-relevant (it's what you improve and
what goes back into a body), and merging grades would be wrong.

What you see in the UI:
- **Star badge**: items with `qualityType == Star` show `item_star_<quality>` (`UIItemCell.cs:305-309`). Both bones
  in the screenshot carry a star, meaning both are graded variants.
- **Tooltip "Skulls: ☠"**: added by `UITooltip.AddItemEffectWidgets` when the definition has non-zero skull values.
- **Tooltip "Crafted at: Autopsy Table I, II"**: the item's craft list. The grade-0 variant is what the autopsy
  produces directly; the graded one came out of an organ-improvement craft, so its tooltip leads with its stat.
- All variants share the display name "Bones", which is the confusing part. A mod could append the grade to the
  name (e.g. "Bones ☠1") or to the tooltip header; that is a display-only change.

## 3. What the game already offers against storage chaos

- **Chest window shows every chest of the area as tabs** (`UIBaseChestWindowData`: the second panel is a
  `MultiInventoryWidgetData` over the zone's chests, one selected at a time).
- **"Move all similar" button** on the player's inventory header (`InventoryHeaderWidget.moveAllSimilarItemsToBagBtn`):
  moves every player item that the **selected** chest already contains into that chest
  (`Inventory.TakeAllItemsExistingInMeFromOtherInventory`). It is per chest and only for items already present.
- **Sort**: `Inventory.Sort(Comparison<Item>)` exists in code but is only used for the vendor's inventory.
- **Global Storage** (ours) already makes crafting pull from every area, so the *crafting* problem is solved; what
  remains is the human problem of knowing where things are and keeping chests tidy.

## 4. Options for a storage helper mod

Ranked by value for effort. All of them move items only through the game's own `Inventory` API
(`CanAddItemCountToInventory`, `AddItemToInventory`, `RemoveItems`, or the existing take-matching call), which fires
the game's inventory events and respects chest filters and stack sizes.

| # | Feature | What it does | Effort | Risk |
|---|---|---|---|---|
| A | **Quick stack to all chests** | One button in the chest window: every item in your inventory that some chest *in this area* (option: any area) already holds goes into that chest, filling existing stacks first. The vanilla button does this for one chest; this does it for all, in a fixed order (chest with the largest existing count first). | Small | Low: same operation as the vanilla button, repeated |
| B | **Consolidate duplicates** | For each item id stored in two or more chests of the area, move the smaller piles into the chest that holds the most, as far as its stack sizes and free slots allow. Single-stack items and filtered chests are skipped. Reports "merged 14 kinds, freed 9 slots". | Medium | Low-medium: many moves in one click; each is `CanAdd` → `Remove` → `Add`, verified by a before/after count per item id, aborting on any mismatch |
| C | **Where is it?** | Hovering an item (or a tooltip line) shows "In storage: 34 here in 3 chests, 12 in other areas", and the chest tabs holding it get a marker; optionally the chests in the world get a tint (the same tint Move Building uses). | Medium | None to saves (read-only) |
| D | **Grade in the name** | Graded variants show their grade in the name/tooltip header ("Bones ☠1", "Skull ☠2 ☠"), so identical-looking items are distinguishable at a glance. | Small | None (display) |
| E | **Sort a chest** | Button ordering a chest's items by type, then name, then grade. | Small | Low (game's own `Sort`) |

Not proposed: automatic consolidation on every deposit (surprising, and it would fight the player's own layout),
and merging grades (gameplay-relevant).

Suggested first release: **A + B + D** as one plugin, "GK2 Storage Helper", with C as the second release.
Per the one-feature-per-plugin rule this is one feature: "keep storage tidy"; C is a separate, read-only feature and
could be its own plugin if preferred.

## 5. Safety considerations for A and B

- Inventories are save data. Only the game's API is used; no item is created or destroyed. Each move is bounded by
  what `CanAddItemCountToInventory` reports, and the whole operation checks `sum(count) per item id` before and
  after; a mismatch is logged and the operation stops (items are never lost, at worst a move is left unfinished).
- Chest filters, bag rules and stack sizes are enforced by the game code the mod calls.
- Nothing runs unless the player presses the button. No per-frame work.
- Same MVID guard and all-or-nothing patching as the other mods.

## 6. The stack-size table

Resolved: Owned Count's `Debug.DumpItemDefs` writes `BepInEx\GK2-itemdefs.csv` from the running game
(`GameBalance.Me.itemDefs`). The dump for build 25533739 is committed as [`itemdefs-25533739.csv`](itemdefs-25533739.csv)
(id, name, type, stack size, quality, quality type, red/white skulls, fuel flag, item groups). Re-dump after each game
update; the storage helper's ordering rules should read stack sizes from the game at runtime, not from this file.
