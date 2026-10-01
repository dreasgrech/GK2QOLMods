# GK2 Mounts: in-game self-tests

These are scripted checks that run inside the game (`src/GK2Mounts/Dev/SelfTest.cs`). They were written on 2026-10-01
so that the levels and view reuse can be tested without someone watching for each detail.

**What they check:** every bug found by hand that day is a scenario. Each one checks the state that decides what is
seen:
- the animator's own `Direction` parameter: 0 is right, -180 is left;
- which cart parts are switched on;
- whose material each sprite wears;
- the counters.

A fix can't pass because the case happened not to show.

## How to run

1. Load a save and stand outdoors with open ground around the keeper. The tests need ground 3, 22, 26 and 42–50 units
   away.
2. Open the panel (F11), go to the **Debug** tab, then **Self-tests**, and press **Run self-tests**. It takes about 4
   minutes.
3. **Don't touch the keys while it runs.** The tests move the keeper, ride and dismount. **Abort** stops after the
   current step.
4. **Results:** `[test] PASS`, `FAIL`, `WARN` and `INFO` lines in `BepInEx\LogOutput.log`, and a summary,
   `[test] done: N passed, M failed, K warning(s)`. The panel shows the summary and the last failures and warnings.

**Safety:**
- **Test donkeys** belong to `dreas.gk2.mounts.selftest`. They're never kept with the save and are removed after each
  scenario.
- **The keeper** is put back where he stood, facing the same way, after every scenario.
- **Nothing is saved.**
- **The level settings** are restored at the end.

**Guards against false results:**
- **Spots:** need ground and room for the keeper (nothing on the obstacle layer).
- **After every move,** the runner reads back where the keeper is. If he was pushed away from a blocked spot, the
  scenario gives a WARN and skips.
- **The leak counts** are taken only once the levels have settled: no view made or released, and no level changed for
  2 s.

**A WARN** means a scenario couldn't set itself up at this spot (no ground there, or a point still on screen). It isn't
a failure of the mod. Try another spot.

## The scenarios

| Scenario | What it does | What it checks |
|---|---|---|
| The per-mod API | two test owners, each with a #1 and the same key | each mod's own numbers; a taken number or key refused; `Find` by number and key; `InstancesOf`; a removed number not handed out again; another mod's `SetPersist` and `SetKey` refused, its own mod's allowed; data per mod, set and cleared independently |
| Kept-with-save text, round trip | a kept parent with a foal, a donkey with its cart and a free cart, with keys, own settings and a value holding a tab, a line break and a backslash; written to the side file's text and read back in memory (no file, no save) | the header and every field of every record come back the same: owner, number, key, kind, scene, exact position, facing, size, rideable, parent, removeWithParent, settings, data |
| Hitch and unhitch | a donkey takes a free cart and leaves it again | one instance with the cart, the cart's data merged in, cart parts on; then a new free cart with the donkey's owner and kept setting, and the donkey's cart parts off |
| Shared colour textures | two donkeys with the same colours, then one with another | the second adds no texture, another colour adds its own, and all removed gives the starting count back |
| One new view per frame | 20 donkeys at once around the keeper | all get views within 10 s, at most `SpawnsPerFrame` made in any frame |
| Waking (view switched off) | a donkey with its own colour goes idle as the keeper moves 26 away, gets a new facing while idle, then the keeper comes back | idle: view switched off, `IsReady` false, `Ride` refused; woken: the animator faces the new way, no cart part on, its own colours on, nothing frozen; riding it works, and it's drawn and faces the ride's way |
| Waking (view frozen) | the same with `IdleSwitchesOff` off, and then a ride started on the frozen donkey directly | idle: frozen but on; woken as above; ridden from frozen: drawn, animated and facing the ride's way (the invisible ridden donkey) |
| Mounted while idle | a donkey frozen off screen facing away is brought to the keeper and ridden in the same frame (the showcase's H) | the same-frame ride waits (refused); ready within 3 s; ridden, it's drawn and faces the keeper's way |
| Reused views | a donkey and a 0.6 foal fall asleep (keeper 42–50 away), are removed, and two new ones are spawned | both views parked; parked views taken again (by anyone: the world's own donkeys waking with the keeper take them too); full size and foal size each where they belong; no cart parts; facing as spawned |
| Foals keep up off screen | a parent with two foals (following however far) is ridden 22 away | the foals stay active, and reach the parent by trotting, with no catch-up jumps (a jump gives WARN) |
| No leaks | 80 donkeys (8 with carts) spawned and removed, twice | instances, colour materials, colour textures (also shared), lamps, meshes and cargo holders all back at the start |
| Frame time by mode | 300 donkeys around the keeper; 3 s each of: levels with idle switched off, idle frozen, levels off, levels on again | INFO lines only: the frame time and the mod's share in each mode, and what levels and switching off save at this spot |

**After every scenario that moves the keeper:** every donkey in the world with its view on has a view of its own size,
and its cart parts match whether it has a cart. That covers reused views taken by any donkey, not only the test's.

**At the end of the run:** every one of the world's own donkeys (not the tests') is still there, with the same owner,
number, scene, kind, keep setting and key. No test donkey is left behind.

## Not automated

- **Persistence through a real save and load.** That needs sleeping and the menu, and the tests don't touch the real
  save. See `mounts-persistence-tests.md`.
- **Whether it looks right overall.** A screenshot page per check could be added, like the cargo sheet.
- **How riding feels:** hitches while the player rides.

## Runs

| Date | Result | Notes |
|---|---|---|
| 2026-10-01, run 1 | 74 passed, 0 failed, 1 warning | the frozen ride skipped: the donkey stayed active (see run 2) |
| 2026-10-01, run 2 | 74 passed, 0 failed, 1 warning | the reason: after the ride it stood 22 from the keeper's far spot, 37% of the screen past the edge, inside the 40% an active donkey needs to leave. The test's far spot is now taken 30 from the donkey |
| 2026-10-01, run 3 | 71 passed, 1 failed, 1 warning | the frozen ride passed. The "both took a parked view" failure was the test's assumption: the world's own donkeys woke with the keeper and took parked views too. Reuse is now counted for anyone, and every donkey with a view is checked for size and cart parts |
| 2026-10-01, run 4 | **81 passed, 0 failed, 0 warnings** | levels save 5.7 ms a frame with 300 test donkeys (14.1 → 8.4 ms); switched off versus frozen: no measurable difference here |
