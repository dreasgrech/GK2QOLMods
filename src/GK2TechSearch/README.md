# GK2 Tech Search

BepInEx plugin for Graveyard Keeper 2: a search box on the Tech Tree page. Type part of a tech's name, or of something
it unlocks, and jump straight to it, on whichever tab it is.

```
[ garden                  ]
✓ Garden Improvement I   Farming
  Garden Improvement II  Farming
  <tech>  <tab> · <unlock name>     <- a tech matched through what it unlocks
```
(Illustration of the layout; real rows depend on your save.)

Enter (or a click) switches to the tech's tab, scrolls to it and frames its card. Hidden techs (the "?" cards) and
tabs you haven't opened yet are never shown, so the search spoils nothing. Display only: nothing in the game changes.

Version 1.0.0, built and audited against Steam game build 25910882, game 1.010 (Assembly-CSharp MVID `5ec8094e-…`,
LazyBearTechnology `6cd86a0b-…`). Design study: [`docs/tech-search.md`](../../docs/tech-search.md).

- GUID: `dreas.gk2.techsearch`
- Needs: BepInEx 5.4.23.x, installed in the game folder.

## Using it

- Open the Tech Tree (Character window → Tech Tree). The box is at the top right of the page.
- Click it, or press **Ctrl+F**, and type. Several words narrow it down (`garden ii`).
- **Up/Down** pick a result, **Enter** or a click jumps to it. **Esc** leaves the box; the next Esc closes the window
  as usual.
- While the box has the cursor, the game's keys are paused (letters don't open the map or the inventory), the same
  way the game's own bug report window does it.
- Techs you've already researched are listed in green. A result that matched through an unlock shows that unlock's name
  after the tab name.
- With a gamepad active the box is hidden (there's no text entry on a pad).

## Optional: GK2 Mod Framework

With [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) (0.1.x, by SuperMan4eg) installed,
the mod appears in the game's **Mods** menu (main menu and ESC menu):
- a Status row: Active, Off, or disabled after a game update;
- "Search box on the Tech Tree", "Also search what techs unlock" and "Key to start a search".

`MaxResults`, the Layout settings and `Safety.StopOnChangedGameCode` stay in the cfg. Without the framework, the same
DLL works exactly as before. Details: [`docs/gk2-mod-framework.md`](../../docs/gk2-mod-framework.md).

## Settings (`BepInEx\config\dreas.gk2.techsearch.cfg`)

Read at game start. The General ones are read on every search or page open, so a change in the Mods menu applies the
next time the Tech Tree page opens.

| Key | Default | Meaning |
|---|---|---|
| `General.Enabled` | true | Show the search box. |
| `General.SearchUnlocks` | true | Also match what a tech unlocks (items, buildings, perks on its card). |
| `General.MaxResults` | 8 | Rows in the result list (1-20). Further matches are counted in a last row. |
| `General.FocusKey` | LeftControl + F | Puts the cursor in the box while the Tech Tree page is open. `None`: no key. |
| `Layout.BoxOffset` | (-8, -13) | Box position from the page's top right corner (in the strip right of the tab buttons). |
| `Layout.BoxSize` | (130, 22) | Box width and height. |
| `Layout.ListWidth` | 280 | Width of the result list under the box, right edges aligned. |
| `Safety.StopOnChangedGameCode` | false | At start the mod compares every game member it uses with the installed game (`docs/dependency-check.md`). A member that is gone always stops the mod; one whose code changed runs it with a warning, or stops it too with true. |
| `Safety.LastDependencyCheck` | | Written by the mod: the cached result of that comparison for the current game build. |

## How it works

- **The box** is added once to the Tech Tree page (postfix on `TechTreePageWidget.Init`). It's a copy of the game's
  own text box from the bug report window, if that window is loaded; otherwise a plain box in the page's font. The
  log says which.
- **Search:** over `GameBalance.Me.techDefs`, skipping hidden techs and locked tabs. The name is what the card shows
  (`LLBase.L(id)`); the unlocks are the card's icons, named as their tooltips name them
  (`LinkedEntityWidgetData.GetLinkedEntityHeader`). Case-insensitive; every word must appear. Name matches come first,
  then unlock matches, each in tab order, left to right.
- **Jump:** the game's own `TechTreePageWidget.DisplayTab(tab, techId)`, the call a tab click makes, which also
  scrolls to the tech. Then the card's own hover frame is switched on.
- **Keys:** while the box has the cursor, `LazyInput.SetInputActivity(false)`, given back when it loses the cursor,
  when the page hides and when the box is destroyed (the game's bug report window does the same).

Patches: postfixes on `TechTreePageWidget.Init`, `Redraw` and `Hide`. Private members used (resolved at startup; a
missing one disables the mod): `TechTreePageWidget.scrollRect`, `redSpheresLabel`, `tabButtonPrefab`,
`TechTreeElementBaseWidget.selection`; optional: `UIBugReportWindow.titleInput`.

## Safety

- Nothing is written: no save data, no game rule. Jumping is the game's own tab switch, with the same side effects as
  clicking the tab (reputation techs whose price is met unlock themselves, as on any tab switch).
- Errors are caught and logged once; the Tech Tree then works as vanilla.
- No per-frame work outside the open Tech Tree page (the box only runs while the page is shown).
- Dependency check (`docs/dependency-check.md`) and all-or-nothing patching as in the other mods.

## In-game test list

1. Log shows `GK2 Tech Search 1.0.0 loaded.` **once**, no warnings or errors.
2. Open the Tech Tree: the box is there. The log has one `[measure]` line (page and tab-strip rectangles) and one line
   saying which text box was used. If the box sits badly, send the `[measure]` line: the place is set from it.
3. Click the box and type `oven`: results appear under it; letters don't open the map/inventory/quests; the character
   doesn't move.
4. Enter: the right tab opens, scrolled to the tech, its card framed. Try one on another tab and one far right on a
   long tab.
5. Up/Down move the highlight; a click on a row jumps too.
6. Type something that only an unlock has (e.g. an item a tech unlocks): the tech is listed with that item's name.
7. A "?" (hidden) tech's real name, and techs on a tab you don't have yet, never appear.
8. Esc in the box leaves it (keys work again: I/M/Esc behave normally); the next Esc closes the window.
9. Ctrl+F on the Tech Tree page puts the cursor in the box; Ctrl+F elsewhere does nothing new.
10. Close the window with the mouse while typing: keys work normally afterwards.
11. Switch to a gamepad: the box is gone the next time the page opens. Also: a controller plugged in but you play
    with the mouse. The box follows the game's own "gamepad active" state (`LazyInput.IsGamepadActive`), the one
    that shows its gamepad button hints; if the box is missing while you use the mouse, say so.
12. Researched techs show in green in the list (round 1 showed the game font has no ✓, so 1.0.0 now uses colour).
13. `Enabled = false`: no box.
14. With GK2 Mod Framework installed: Status Active, the three settings; untick "Search box on the Tech Tree" and
    reopen the page: no box, Status reads Off. With it removed: a clean log.

## Verification done

- `dotnet build -c Release`: 0 errors; no warnings from this code (only the MSB3277 about the game's newer
  `System.IO.Compression`, which every mod here gets and doesn't use).
- `tests/CecilScan-TechSearch`: all 27 IL references into the game assemblies resolve; the four private fields (and
  the optional `titleInput`) exist with the expected types; `Init`, `Redraw`, `Hide` and `DisplayTab(tab, id)` are
  single methods with bodies. ALL PASS.
- `tests/FrameworkScan GK2TechSearch`: the three patch classes are in `PatchClasses`, no Unity message names on
  `Plugin`, `GK2.Framework` only in `FrameworkLink`, `Register` NoInlining, soft dependency. ALL PASS.
- Round 1 in game (2026-10-04): loads clean, registers Compatible with the framework, the plain box was used (the bug
  report window wasn't loaded), search and jump work. Findings: the box was far too big for the page (`[measure]`:
  page 640 x 385 units, tab buttons end about 165 right of centre) and `tiny_font` has no ✓. Fixed: box 130 x 22 in
  the strip right of the tabs, the list has its own width (280), researched techs are green, no ✓/… glyphs.
- Round 2 in game (2026-10-04): clean log, box fits. Finding: the list (right under the box) covered the lower edge of
  the open sub-tab, which hangs below the box. Fixed: the list now starts under the tab strip, measured live from its
  rectangle (`[measure]` logs the drop).
- Round 3 in game (2026-10-04): the list clears the open tab; the user signed it off. Released to `dist/`.

## Build, install, uninstall

- **Build and deploy** (game closed): `dotnet build -c Release` in this folder.
- **Build only** (game running): `dotnet build -c Release -p:Deploy=false`.
- **Uninstall:** delete `BepInEx\plugins\GK2TechSearch.dll` in the game folder, and optionally
  `BepInEx\config\dreas.gk2.techsearch.cfg`.
