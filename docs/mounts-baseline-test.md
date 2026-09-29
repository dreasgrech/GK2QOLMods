# GK2 Mounts: in-game baseline before the API split

Purpose: record how today's single mod behaves, so the split into core and showcase (`docs/mounts-api-design.md`)
can be checked against it. Build: Mounts 0.1.0 as installed on 2026-09-30, including the F11 panel hide.

**Before starting:**
- Commit the saves repo. The save test sleeps, and sleeping writes the save.
- Keep the game at the default H key and the Cart style unless a step says otherwise.
- After the session, close the game and tell Claude. The "check" lines are read from `BepInEx/LogOutput.log`.

## A. Panel hide (F11)

1. Mount (H). Press **F11**: the panel disappears. Press it again: it's back. The title reads
   "GK2 Mounts tuning (F11 hides)".
   - Check: this isn't logged, so it's the user's observation.

## B. Sounds (Sounds tab; the "Last sound" line shows each sound's id)

2. **Hoofsteps per ground:** ride slowly over grass, dirt/road, stone or paving, wood (a bridge or planks) and
   sand if there is some. For each ground, note the "Last sound" id.
   - Check: the ids differ per ground, and there's no `Sounds: the game has no sound` warning.
3. **Wheels (Cart style):** they roll while walking and stop when standing.
4. **Graze:** stand still until the donkey grazes; the eating sound plays.
5. **Brays:** wait for one on its own. Also press "Play" for the bray on the Sounds tab.
6. **Bell:** press the bell's Play button on the Sounds tab (the bell key is None by default).
   - In Cart style it also rings when the donkey sets off after standing still 4 s, but by default only with cargo
     on the cart (`BellOnSetOff`, `BellOnlyWithCargo`). Try once with an item loaded.
7. **Cargo thump:** load an item onto the cart; a wooden thump plays.
8. **Keeper's footsteps:** while riding you should hear no footsteps from the keeper, only hoofs.

## C. Lantern (Cart style, Lantern tab)

9. With **Mirror off**, ride left and right on flat ground and on a slope. Then do the same with Mirror on.
   - Look for: the lamp hangs straight from its hook, and there's no second lamp or offset glass.
10. **At night:** walk and stand, check the light, the glow and the lit glass, and watch the dusk-to-night change.
    - Check: `LampHang: lamp hangs from its hook …` at attach, and no `Lantern:` warnings.

## D. Cargo and save safety (Cart style)

11. **Two items, then set down:** pick up two items (e.g. wood, stone). They land on the cart. Set one down with the
    interact key facing empty ground.
    - Check: `Cargo: picked up … straight onto the cart (1/2)`, then `(2/2)`, then
      `Cargo: set … down in front of the keeper`.
12. **River dump with two items:** carry two bodies, or a body and another item, on the cart and throw the body into
    the river.
    - Check: `Cargo: … put into <river object> from the cart (1 left)`. The other item stays on the cart and looks
      right.
13. **Full cart plus a zombie:** with the cart full (2/2), pick up a zombie.
    - It should go onto the keeper's head as in the normal game, nothing duplicated, nothing dropped twice.
    - Check: no `LOST`, and no second drop of the same item.
14. **Cart full, a third item:** pick up one more item. It's put down in front of the keeper.
    - Check: `Cargo: cart full, … dropped in front of the keeper`.
15. **Cargo sheet refuses a loaded cart:** with items on the cart, press "Photograph all cargo combinations" on the
    Debug tab.
    - It should refuse with "set the cargo down first (the sheet needs an empty cart)", and nothing is dropped.
16. **Save (sleep):** with two items on the cart, go to bed and sleep. After waking, both are on the cart again.
    - Check: `Cargo: unloaded on save: 2 in hand, …`, then `Cargo: … moved from the hands onto the cart (save)`, and
      no `LOST`.
17. **Closing with cargo:** with an item on the cart, quit to the desktop (don't sleep first).
    - Check: `Cargo: the game is closing with 1 item(s) on the cart; not handed back …`.
    - On the next start, the save from step 16 loads as it was.

## E. Slope steeper than the pitch cap

18. Find the steepest slope you know and ride the cart across it slowly.
    - The cart pitches up to 20° (`CartPitchMaxDegrees`) and then stops pitching. Look for anything that sinks into
      the ground or detaches. The Riding tab's slope status line shows the angle.

## Results, round 1 (2026-09-30)

The log has no warnings or errors from any mod.
- **Parts A, B, C, E passed**, from the user's observations (they aren't logged):
  - the F11 hide;
  - all the sounds: hoofsteps changing per ground, wheels, graze, brays, bell and thump, with no keeper footsteps;
  - the lantern with Mirror on and off, on slopes and at night;
  - the steep slope.
- **11 passed:** `body_corpse` and `wood` onto the cart (1/2, 2/2), then set down one by one to 0/2.
- **12 passed:** `body_corpse taken off the cart by the game (1 left)`, with the wood staying on the cart. A river
  throw goes through the game's own head-item removal, so the log says "taken off the cart by the game", not "put
  into". Once, at the bank, the cart logged `[cart] no ground sample … cart=NaN (raycast:Ground/none)`, meaning
  there was no ground under it (water). It held its height (applied 0.000); harmless.
- **13 and 14 not done:** zombies aren't unlocked in the user's game yet.
- **15 passed:** `[sheet] set the cargo down first (the sheet needs an empty cart)`.
- **16 not possible:** sleeping while mounted can't be done (see below).
- **17 passed, but only in Unity's `Player.log`**
  (`%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Player.log`):
  `Cargo: the game is closing with 2 item(s) on the cart; not handed back …`, then `Dismounted (plugin unloaded)`.
  - BepInEx's `LogOutput.log` doesn't have them. Its `DiskLogListener` flushes on a 2-second timer, and nothing
    disposes or flushes it when the game quits, so lines from the last moment before exit are lost.
  - **Lesson for all tests:** read quit-time lines from `Player.log`.

**Why step 16 can't be reached** (most likely; to confirm with the user): the only save point is waking from sleep.
If the bed is indoors, then with `DismountIndoors` on (the default) the keeper is always dismounted before reaching
it, and dismounting hands the cargo back. So in normal play, **cargo never exists during a save**. The `SaveSystem.Save` prefix and postfix are a
safety net that ordinary play doesn't reach.

**Noise to clean up before release:** every cargo change re-measures the cart parts and logs 5 lines ("shafts x …",
three "pitch node …", "cart parts follow …").

## Not in this round

- **Enabled switched off mid-ride:** BepInEx reads the cfg only at startup and the panel has no Enabled row, so it
  can't be switched during a ride. It becomes testable once the API exists (a mod or the dev panel can call it).
