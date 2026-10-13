# Feasibility: "how many of this do I own" in the item tooltip

Question: can a mod show, in an item's tooltip, the total the player owns across inventory and all storages?
Studied 2026-09-27 against game build 25533739 by reading `UITooltip.cs`, `UIItemCell.cs`, `Item.cs`,
`Inventory.cs`, `MultiInventory.cs`, `PlayerData.cs` and the storage discovery already audited for Global Storage.

**Verdict: feasible, small, and read-only.** One Harmony postfix, no game state changed, no save data touched.
Estimated size about 200 lines plus the usual guard. Lower risk than any of the three existing mods.

## Where the tooltip is built

Every item tooltip in the game passes through one private static method:

```
UITooltip.AddItemWidgets(List<LazyWidgetDataBase> widgetData, ItemDef itemDef, bool addHeader = true,
                         string headerPrefix = "", bool notShowStudyWidget = false, bool nowShowCraftedAt = false,
                         IReadOnlyList<ItemDef> qualityVariants = null)                         (UITooltip.cs:1403)
```

Callers (UITooltip.cs): inventory and chest cells (`ShowItemCell`, 490), craft output cells (553), craft ingredient
rows (635), alchemy formula items (668), vendor order items (458), dialogue answer items (443, 449) and two more
entity widgets (692, 695, 734). It fills a list of widget data (`UITooltipTextWidgetData`, `UITooltipSeparatorWidgetData`)
that the caller then hands to `ShowAtTargetAndCalculateOffsets`. The game itself appends extra lines to that same
list after the call (for example the red "extra tooltip" line in `ShowItemCell`, 492-501).

A **postfix on `AddItemWidgets`** therefore sees the finished list and the `ItemDef`, and can append one line. That
covers every context at once. `addHeader` tells whether the tooltip is the item's own (true) or the item is embedded
in another tooltip (false); the line can be limited to the former if the embedded contexts get too busy.

The text widget is constructed as `new UITooltipTextWidgetData(string text, TextAlignmentOptions, TextStyle)`. The
styles live in private instance fields of the private static `UITooltip.instance` (`smallDescriptionTextStyle`,
`descriptionTextStyle`, …); both are reachable with `AccessTools.FieldRefAccess`, the same way Built Count reads
its label. `TextStyle.ApplyStyleToString` is public, and the game's own icon syntax (`"skull".FontIcon()`) is usable
in the string.

## Where the numbers come from

All read-only, all public:

| Source | API |
|---|---|
| Player inventory, including bags | `MainGame.PlayerData.Inventory.Data.GetTotalCountInInventory(itemId)` (Item.cs:870; walks bags) |
| Tool belt | `PlayerData.toolBeltInventory` (PlayerData.cs:25), same call |
| Every storage chest in every area | the discovery Global Storage already uses (`GlobalStorage.GetAll()`: active container zones → `wgoDataList` → objects with `OpenInMultiInventory`, non-zero size, not hidden; fuel containers and conveyor chests optional). Per inventory: `Inventory.Data.GetTotalCountInInventory(itemId)`. |
| Per-chest breakdown | the same loop, keeping (zone id, count) pairs |

The discovery loop is cached per frame; a tooltip only runs on hover. Cost is a few dozen inventories × a few
dozen items, far below anything the game does per frame.

**Graded variants** (see `storage-and-stacking.md`): `GameBalance.Me.starGroupItemsCache[starGroupId]` gives all
definitions of one graded family (the game uses it in `Inventory.RemoveItemByStarGroup`). So the line can show both
"this exact grade" and "all grades", e.g. `Owned: 12 (all Bones grades: 31)`.

## Proposed line

Appended after the game's own lines, small description style:

```
Owned: 120  ·  on you 4  ·  in storage 116 (5 chests)
```

Options:
- `Layout`: one line (above) or two (second line lists per-area counts: "yard 80 · cellar 36").
- `IncludeOtherGrades` for star-group families.
- `OnlyOnOwnTooltip`: skip the embedded contexts (craft ingredient rows, dialogue answers).
- The storage set follows the same three switches as Global Storage (fuel containers, conveyor chests), so both mods
  agree on what "storage" means; the discovery code would be copied, not shared, to keep the plugins independent.

## Risk assessment

| Area | Risk | Why |
|---|---|---|
| Save data | none | Nothing is written; only `GetTotalCountInInventory` reads. |
| Game logic | none | No game method's behaviour changes; the postfix only appends to a UI list. |
| UI | low | A wrong style reference would show unstyled text; a thrown exception inside the postfix is caught and logged once, and the tooltip shows as vanilla. Tooltip height grows by one line; the game's placement code already handles variable heights. |
| Performance | negligible | Hover-driven, per-frame cache, integer sums. |
| Game updates | as the others | the dependency check (`docs/dependency-check.md`); `AddItemWidgets` is private, so its name and signature are verified at startup with the same all-or-nothing pattern. |
| Interaction with Global Storage / Built Count / Move Building | none | Different patch targets; Global Storage patches `MultiInventory` and craft pickers, not the tooltip. |

The one design question is *which* inventories count as "owned". The proposal: player + tool belt + the storage set
Global Storage uses. Deliberately excluded: zombie inventories, station craft slots and inputs (items in a running
craft are committed), vendor stock, and dropped items on the ground. These can be config switches later if wanted.

## Effort

- Plugin skeleton (guard, config): reuse from Built Count.
- Discovery: copy of the audited `GlobalStorage.GetAll` (about 60 lines).
- Postfix + text building: about 60 lines.
- Verification: Cecil scan for `AddItemWidgets`, the two private fields, `GetTotalCountInInventory`; in-game check of
  each tooltip context (inventory, chest, craft ingredient, vendor, dialogue) and a graded item.

Half a day including the in-game test list. Suggested plugin name: **GK2 Owned Count** (`dreas.gk2.ownedcount`).
