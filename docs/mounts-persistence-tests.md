# GK2 Mounts persistence: in-game test plan

These are the step-by-step tests for donkeys and carts kept with a save (API 0.9.0, design in
[`mounts-persistence.md`](mounts-persistence.md)). Written 2026-10-01 against Steam build 25601286.

**Status:**
- **T1 passed** on 2026-10-01: 5 of 5 back, 2 cargo items back on the cart.
- **T6 is expected to fail** until the foal scene fix is in.
- Everything else is open.

## Before you start

### Where things are

| What | Path |
|---|---|
| Game saves | `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\` (`Steam_1.dat`, `.info`, `Steam_1_backup_N.*`) |
| Side files | that folder, `<slot>.gk2mounts.dat` (text inside) |
| Mod log | `C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2\BepInEx\LogOutput.log` (rewritten each launch) |
| Panel | F5 in game, **Instances** tab. Its "Saves:" line tells what the last write or restore did |

### A safe way back between tests

The save folder is a git repository, so every test can start from a known save:

1. Before the first test, with the game closed, commit the save folder as it is, side file included:
   `git -C "%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2" add -A`, then commit.
2. To reset after a test (game closed): `git checkout -- .` and `git clean -fd GK2Mounts` in that folder.
3. Steam Cloud is on for this folder (`steam_autocloud.vdf`). If Steam offers to download a "newer" cloud save after
   a reset, choose **local**.

### The log lines that matter

Search the log for `Persistence:`. Each test below lists the lines it should show.

| Line | Meaning |
|---|---|
| `N donkey(s)/cart(s) with M cargo item(s) kept with <slot> (<path>)` | Written after a save |
| `nothing to keep with <slot>; its side file was removed` | A save with nothing kept |
| `<slot>: N of N donkey(s)/cart(s) back, M cargo item(s) back on their carts` | Restored on load |
| `..., K not found on the ground` | Cargo items the restore couldn't find |
| `... ignored, nothing restored` | Stale file (another save time) |
| `could not be spawned again: ...` | A record that failed, with the reason |
| `setting X = Y wasn't restored` | A setting that no longer exists or can't be read |

### What "a save" means here

The game saves only on waking from sleep. Every "Sleep" below means: go to bed, sleep, wake up. "Reload" means:
quit to the main menu and load the same save, unless a test says otherwise.

---

## A. The basic round trip

### T1. Everything comes back *(passed 2026-10-01)*

1. On the panel, spawn a **Donkey**, a **Donkey with its cart** and **A donkey family** (2 foals).
2. Ride the donkey with the cart, pick up two different big items so they lie on the cart, ride off a bit and
   dismount.
3. Sleep.
   - **Log:** `5 donkey(s)/cart(s) with 2 cargo item(s) kept with Steam_1`.
4. Reload.
   - **Expected:** all five stand where they stood, facing the same way. The cargo is **on the cart**.
   - **Log:** `Steam_1: 5 of 5 ... back, 2 cargo item(s) back on their carts`, and no "not found".
5. **Look on the ground at the cart:** there is no second copy of either item.
6. The family: the foals are small, can't be ridden, and shuffle and follow their parent.

### T2. Their own settings survive

1. Pick one donkey on the panel, **Edit its settings**, and give it its own colour (Colours tab) and switch on its
   idle shuffle.
2. Sleep, then reload.
   - **Expected:** that donkey has its colour and shuffles. The others don't.
   - **In the side file:** its block has `set` lines for those keys.

### T3. Two saves in a row

1. With kept donkeys around, sleep, then move one ("Move here") and sleep again.
2. Reload.
   - **Expected:** the moved donkey is at its new place.
   - **In the save folder:** `Steam_1.gk2mounts.dat` and no `Steam_1.gk2mounts.dat.tmp`.

### T4. Quit without saving

1. After a save with kept donkeys, move one, remove another, and spawn a new one. **Don't sleep.**
2. Quit to the menu and reload.
   - **Expected:** exactly the state of the last save. The moved one is back at its old place, the removed one is
     back, and the new one is gone.

---

## B. Scenes

### T5. A donkey left in another scene

1. Leave a kept donkey and a kept loaded cart outside.
2. Go indoors through a door that loads another scene (your house) and sleep there.
   - **Log at the save:** `Cargo: #N ...: 2 item(s) set down at the cart for the save`. The scene isn't loaded, so
     the drops go to that scene's queue.
3. Reload. You wake up indoors.
   - **Log:** `N of N ... back` and no "There is no scene". There may be "not found on the ground" for queued cargo;
     note it if so.
4. Go outside.
   - **Expected:** the donkey and cart stand where you left them, the cargo is on the cart, and nothing lies on the
     ground beside it.

### T6. A foal left behind in another scene *(expected to fail until fixed)*

1. Spawn a family. Ride the parent through a door to another scene and **sleep straight away**, before the foals
   catch up.
   - **In the side file:** the foal blocks have the **old** scene, the parent the new one.
2. Reload.
   - **Today's result:** the foals appear in the parent's scene at coordinates from the old scene. They could be
     inside a wall or far off.
   - **Expected after the fix:** they come back in their own scene and catch up with the parent as usual.

---

## C. Carts and hitching

### T7. A cart left standing

1. Ride a kept **donkey with its cart**, press the hitch key (J) to leave the cart standing, and load it with cargo
   first if you can.
2. Separately, with the showcase: summon a cart ride (G) and unhitch it. The showcase's donkey goes at dismount,
   but its cart stays.
3. Sleep, then reload.
   - **Expected:** both free carts are back with their cargo, shafts on the ground. Each row says "kept with saves".

### T8. A donkey with a cart it picked up

1. Ride a kept donkey without a cart up to a kept free cart and press E to hitch it.
2. Sleep, then reload.
   - **Expected:** one donkey with its cart, the cart's cargo on it.

### T9. The known gap: a kept cart on a donkey that isn't kept

1. Press **Don't keep** on a donkey without a cart, then hitch a kept, loaded free cart to it.
2. Sleep, then reload.
   - **Expected:** the pair is gone, and its cargo lies on the ground where it stood. Nothing is lost or doubled.
   - That is the documented behaviour, not a failure.

---

## D. Not kept, and opting out

### T10. "Don't keep"

1. On a kept donkey with cargo, press **Don't keep**. The row says "not kept with saves".
2. Sleep, then reload.
   - **Expected:** it's gone, and its cargo lies on the ground where its cart stood.
3. On a kept parent with foals, press **Don't keep**: the foals turn "not kept" too.

### T11. Nothing kept at all

1. Remove every kept donkey (or press Don't keep on all of them), then sleep.
   - **Log:** `nothing to keep with Steam_1; its side file was removed`. `Steam_1.gk2mounts.dat` is gone.
2. Reload: nothing comes back, and the "Saves:" line says "no side file".

---

## E. Other saves and new games

### T12. Load another save

Needs a second slot: start a new game, sleep once, and you have `Steam_2`.

1. In `Steam_1`, have kept donkeys with cargo.
2. Load `Steam_2`: from the main menu, and if the in-game menu can load saves, from there too.
   - **Expected:** no donkeys, and none of `Steam_1`'s cargo items appear anywhere in `Steam_2`. This checks the
     cleanup fix.
3. Load `Steam_1` again: its donkeys are back.

### T13. A new game

1. After playing `Steam_1` with kept donkeys, start a **new game**.
   - **Expected:** no donkeys. After its first sleep, a side file appears only if you spawned something kept in it.

### T14. A slot deleted in the game's menu

1. In the load menu, delete a slot that has a side file.
   - **Expected:** its `.gk2mounts.dat` is gone. **Log:** `save <slot> was deleted; so was its side file`.

### T15. A slot deleted outside the game, its name reused

1. With the game closed, delete `Steam_2.dat` and `Steam_2.info` in Explorer, but leave `Steam_2.gk2mounts.dat`.
2. Start a new game. It takes the free name `Steam_2`. Sleep, then reload.
   - **Expected:** the old file's donkeys do **not** appear.
     - Either the first sleep replaced the file (nothing kept, so it's removed), or a reload before that sleep
       finds a file from another save time.
   - **Log:** `... ignored, nothing restored`, or no side file at all.

---

## F. Stale side files

### T16. Played without the mod in between

1. With kept donkeys and cargo, sleep and quit.
2. Move `GK2Mounts.dll` and `GK2Showcase.dll` out of `BepInEx\plugins`.
3. Start the game, load `Steam_1` (no donkeys; the cargo lies on the ground), sleep, quit.
4. Put both DLLs back. Start and load `Steam_1`.
   - **Log:** `... belongs to the save of <time> (day N), but Steam_1 was saved <later time> ...; ignored, nothing
     restored`.
   - **Expected:** no donkeys, and the cargo is on the ground once, not on a cart and the ground both.

### T17. Switched off with `General.Enabled`

1. Set `General.Enabled = false` in `dreas.gk2.mounts.cfg` with kept donkeys around. They vanish and their cargo
   is set down.
2. Sleep.
   - **Log:** `nothing to keep ...; its side file was removed`. The patches still run with the mod switched off,
     and nothing is kept.
3. Switch it back on and reload: nothing comes back, and the cargo is on the ground.

### T18. A game backup restored

The folder holds `Steam_1_backup_N` files.

1. Reset to a known save with git, or restore an older backup over `Steam_1` (game closed).
2. Load it.
   - **Expected:** the side file is restored only if its save time matches that save's `.info`. With git, check out
     both the save and its `.gk2mounts.dat` from the same commit and they match. With only the save from a backup, it
     doesn't match and is ignored.

---

## G. The side file itself

For each test, close the game, edit `Steam_1.gk2mounts.dat` in a text editor, load, and check the log.
Reset with git afterwards.

| # | Edit | Expected |
|---|---|---|
| T19 | Change one `position` to nonsense (`abc	1	2`) | That one stands at 0 on the bad axis, or nearby; no crash. Note where it appears |
| T20 | Change a `scene` to `NoSuchScene` | That one is skipped: `could not be spawned again: There is no scene NoSuchScene` |
| T21 | Rename a `set` key (`IdleShuffle` → `IdleShuffleX`) | `setting IdleShuffleX = true wasn't restored`; the donkey comes back without it |
| T22 | Delete everything after the first `end` | Only the first one comes back; no crash |
| T23 | Change `format	1` to `format	2` | `has format 2, this version reads 1; nothing restored` |
| T24 | Change a `cargo` GUID by one letter | `1 not found on the ground`; that item stays on the ground, the rest is on the cart |
| T25 | Delete the parent's block of a family | The foals are skipped (`a foal whose parent didn't come back`) |
| T26 | Change `saved` by one second | `ignored, nothing restored` |

---

## H. Owners and scale

### T27. The owner mod is gone

1. Through a test mod, or the GK2MountsTechGate example, spawn something with `Persist = true`. Sleep.
2. Remove that mod and reload.
   - **Expected:** it comes back. Its panel row says "by <that mod's GUID>", and it can be removed from the panel.

### T28. Many at once

1. Spawn about 10 families with 4 foals each, plus 5 donkeys with loaded carts. Sleep, then reload.
   - **Expected:** all back. The load shows no visible hitch beyond the game's own, and the log has no
     `route ... none` storm right after the load.

---

## I. Other machines

### T29. Steam Cloud, a second PC or the Steam Deck

The game runs there as the Windows build under Proton. BepInEx needs the launch option
`WINEDLLOVERRIDES="winhttp=n,b" %command%`.

1. On this PC, sleep with kept donkeys, quit, and let Steam finish syncing.
2. On the other machine, check whether `Steam_1.gk2mounts.dat` arrived in its save folder (it did on 2026-10-01, a second Windows install). Under Proton it
   is inside the game's compatdata prefix, in `AppData/LocalLow/Lazy Bear Games/Graveyard Keeper 2/`.
3. Load `Steam_1` there.
   - **If the file synced:** everything comes back, as in T1.
   - **If not:** no donkeys, the cargo is on the ground, and nothing is doubled. Steam's Auto-Cloud rules for the
     game don't include our folder, and that is the limit for a mod that keeps out of the game's save.

---

## Results

| Test | Result | Date | Notes |
|---|---|---|---|
| T1 | passed | 2026-10-01 | 5 of 5, 2 cargo items; foal #5 was saved next to an obstacle (harmless log line) |
| T2–T29 | open | | |
