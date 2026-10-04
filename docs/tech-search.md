# Design: searching the tech tree

Question: can a mod add a search to the Tech Tree page, so a player can find a tech by name (or by what it unlocks)
without paging through six tabs? Studied 2026-10-04 against game 1.008, Steam build 25676698 (Assembly-CSharp MVID
`f7d46132-…`, LazyBearTechnology `f67c6993-…`), by reading `TechTreePageWidget.cs`, `TechDef.cs`,
`TechTreeElementWidget.cs`, `TechTreeElementBaseWidget.cs`, `CharacterWindow.cs`, `KnowledgeSystem.cs`,
`LinkedEntityWidgetData.cs`, `UITooltip.cs` (`ShowLinkedEntity`), `UIBugReportWindow.cs`, and in LazyBearTechnology
`LazyInput.cs`, `LazyUI.cs`, `LazyButton.cs`, `LazyScrollRect.cs`, `GamepadNavigationController.cs` and
`GamepadNavigationItem.cs`.

**Verdict: feasible, small, and display-only.** The game already has a public "show this tab and scroll to this
tech" call, so the mod only adds a text box and a result list. No save data is touched and no game rule changes.

Implemented as **GK2 Tech Search** (`src/GK2TechSearch`, GUID `dreas.gk2.techsearch`).

## How the tech tree page works

- The Character window (`CharacterWindow`) has five pages; the Tech Tree page is a `TechTreePageWidget`, reachable
  through the public getter `CharacterWindow.TechTreePageWidget`. There is one instance; its `Init()` runs once and
  creates the six sub-tab buttons (`TechTreeTab`: Building, Metallurgy, Farming, Theology, Anatomy, Cooking).
- Opening the page calls `Draw(null)` then `DisplayLastTab()` (`CharacterWindow.SwitchPage`). Leaving it calls
  `Hide()`.
- **`public void DisplayTab(TechTreeTab tab, string focusOnTech = "")`** (`TechTreePageWidget.cs:150`) rebuilds
  the tab's cards from `GameBalance.Me.techDefs`, and when `focusOnTech` is a tech id on that tab it gives that card
  the gamepad focus and calls `scrollRect.ScrollToTargetInstant(card, Horizontal)`. Nothing in the game passes a
  `focusOnTech` (every caller uses the default), but it's the whole "jump to" we need, and it's what a tab click
  runs anyway.
  - Side effect, the same as any tab click: `UnlockReputationTechIfAvailable` on every tech (reputation techs whose
    price is met unlock themselves). The mod adds no new path to it; it only triggers what a tab click would.
  - `DisplayTab` does **not** check whether the tab is unlocked (`KnowledgeSystem.IsTechTabLocked`). The tab
    buttons are hidden for locked tabs (`Redraw`), so the mod must never jump into one: the search index skips
    them.
- Cards are pooled (`UIPrefabsPooler`) and virtualised (`LazyScrollRect` keeps `LazyScrollableElement`s; a card's
  `Widget` exists only while it's in view). The private field `TechTreePageWidget.scrollRect` gives the list
  (`DisplayingElements`, each with `Data` = `TechTreeElementBaseWidgetData` holding the `TechDef`).
- Each card is a `TechTreeElementBaseWidget` with a protected `Image selection`, the frame shown while the mouse is
  over the card (`OnSelect`/`OnDeselect`). `OnDeselect` runs on pointer exit, on `Init` and on `OnDisable` (so on
  release to the pool), so turning the frame on for a found card leaves nothing behind in a pooled card.
  - The gamepad focus that `DisplayTab` sets doesn't help with the mouse: `ReinitItems` runs only with a gamepad,
    so with mouse and keyboard `SetFocusedItem` falls back to item 0. The mod shows the frame itself.

### Tech states and what the player may see

`TechDef.TechState` (`TechDef.cs:104`): `Unlocked`, `Hidden` (a "?" card: no name, no price, no unlocks shown),
`Visible` (parents not done) and `Available`. **Hidden techs are never matched**, or the search would spoil what
the game deliberately hides. Locked tabs are skipped for the same reason.

### Text the search looks at

- The tech's name: `LLBase.L(techDef.id)`, exactly what the card header shows (`TechTreeElementWidget.Redraw`).
- What it unlocks (on by default, `SearchUnlocks`): `techDef.linkedEntityWidgetDatas`, the same list the card shows
  as icons, filled by `TechDef.InitTechs()` from the page's `Init()`. For each, `GetLinkedEntityHeader()`
  (`LinkedEntityWidgetData.cs:212`) is the title of that icon's tooltip (`UITooltip.ShowLinkedEntity`): the item,
  building, town building, perk or alchemy result name. It's a pure localisation lookup (`ItemDef.GetHeader`,
  `BuildingDef.GetHeader`, … all `LLBase.L(id)`).
- Matching: rich-text tags stripped, then a case-insensitive substring match (`OrdinalIgnoreCase`; works for any
  script). Every word typed must appear (in the name or in one unlock), so `oven 2` style queries narrow down.
- The index is rebuilt for every query from the game's live data (about two hundred techs), so a language change
  or a newly revealed tech needs nothing special.

## Typing without setting off game keys

The game reads its keys through `LazyInput` (Rewired), not through uGUI. Typing "m" into a text box would otherwise
open the map, "i" the inventory, Esc close the window. The game's own text window, the bug report
(`UIBugReportWindow.LateUpdate`, line 168), solves it like this, and the mod does the same:

- while the field `isFocused`: remember `LazyInput.IsInputActive()`, `LazyInput.SetInputActivity(false)`;
- when the field loses focus (or the page hides, or the box is destroyed): restore the remembered value;
- Esc while focused is read directly (`Input.GetKeyDown(KeyCode.Escape)`).

`LazyInput.Update` returns before clearing its pressed-keys set while input is off (`LazyInput.cs:187`), so the
mouse click that focused the field would stay "pressed" for the whole time. The mod also calls the public
`LazyInput.ClearAllKeysDown()` when it switches input off.

Mouse clicks on uGUI elements still work while `LazyInput` is off (the EventSystem handles them), so the result rows
are plain uGUI `Button`s. They have no `GamepadNavigationItem`, so the page's gamepad navigation never picks them up.

## The box

- Built once, in a postfix on `TechTreePageWidget.Init`, as a child of the page, with `LayoutElement.ignoreLayout`
  so the page's own layout/fitter never moves it.
- Made of the game's own parts where possible: the text box is a copy of the bug report window's title field
  (`UIBugReportWindow.titleInput`), found with `Resources.FindObjectsOfTypeAll` so the window is never loaded or
  opened by the mod. If it isn't there, a plain `TMP_InputField` is built with the page's own font (from
  `redSpheresLabel`). The log says which one was used.
- Where: the empty strip right of the sub-tab buttons (see the screenshot in the request). Placement is by anchor
  and offset settings (`Layout.*`), and the first open logs the page's and the tab strip's rectangles, so the place
  can be set from measured numbers.
- Shown only with mouse and keyboard; hidden while a gamepad is active (no text entry on a pad).
- A hotkey (`General.FocusKey`, default Ctrl+F, the usual "find") puts the cursor in the box while the Tech Tree
  page is open. It's read with BepInEx's `KeyboardShortcut.IsDown()`, only while the page is shown and the box isn't
  focused yet. Clicking the box works too.

## Results and jumping

- Up to `MaxResults` rows under the box: `Tech name · Tab` and, when the match came from an unlock, `→ unlock name`.
  Techs already researched are marked with a tick.
- Enter jumps to the highlighted row (the first by default), Up/Down move the highlight, a click on a row jumps too.
- Jumping: `DisplayTab(def.tab, def.id)`, then the card's `selection` frame is switched on so it stands out until the
  mouse passes over it. `DisplayTab` runs the visibility pass *before* it scrolls, so a card scrolled into view may
  have no widget yet; `LazyScrollableElement.ForceVisibilityAndGetWidget()` runs the list's normal "became visible"
  step for it (`GetTechWidget`), nothing else. The tab button follows (`DisplayTab` sets it).
- Esc leaves the box and keeps the text (TMP's own Escape handling, with `restoreOriginalTextOnEscape` off); the next
  Esc closes the window as usual. The Esc that leaves the box never reaches the game: `LazyInput` is still off that
  frame.
- A press on a result row takes the cursor out of the box before the click lands, so the list also stays open while
  the mouse is over it.

## Patches

| Target | Kind | Why |
|---|---|---|
| `TechTreePageWidget.Init()` | postfix | build the box once |
| `TechTreePageWidget.Redraw()` | postfix | show/hide the box for gamepad vs mouse, keep it on top |
| `TechTreePageWidget.Hide()` | postfix | close the result list, give `LazyInput` back |

Private members (resolved at startup, any missing one disables the mod): `TechTreePageWidget.scrollRect`,
`TechTreePageWidget.redSpheresLabel`, `TechTreePageWidget.tabButtonPrefab`, `TechTreeElementBaseWidget.selection`,
`UIBugReportWindow.titleInput` (optional: a missing one only means the plain fallback box).

## Risk assessment

| Area | Risk | Why |
|---|---|---|
| Save data | none | Nothing is written. `DisplayTab` is the game's own tab switch. |
| Game logic | none | No game method's behaviour changes; three postfixes that only add or hide UI. |
| Keys | low | The game's own pattern for text entry; input is given back on unfocus, on page hide and on destroy. |
| Spoilers | none by design | Hidden techs and locked tabs are never in the results. |
| UI | low | The box is a child of the page; a failure to build it is logged once and the page works as vanilla. |
| Game updates | as the others | MVID guard, all-or-nothing patching, `Access.Verify()`. |
| Other mods | none | No other mod of ours patches the tech tree. |

## Not in v1

- Dimming the cards that don't match on the current tab. Cards are pooled, so every change needs an undo on release;
  the jump plus the frame covers the need for now.
- Gamepad text entry.
