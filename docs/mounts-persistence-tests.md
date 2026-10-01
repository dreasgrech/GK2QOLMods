# GK2 Mounts persistence: in-game test plan

These are the step-by-step tests for donkeys and carts kept with a save (API 0.10.0, design in
[`mounts-persistence.md`](mounts-persistence.md)). Written 2026-10-01 against Steam build 25601286.

**Status:**
- **T1 passed** on 2026-10-01 with API 0.9.0: 5 of 5 back, 2 cargo items back on the cart. Run it again on 0.10.0.
- **T6:** the fix is in with 0.10.0 (a foal comes back in its own scene).
- **Part J** (several mods, the herd test mod) is new with 0.10.0.
- Everything else is open.

**After updating to 0.10.0:** the side file moved to format 2. The first load logs `has format 1, this version reads
2; nothing restored`, so spawn your test donkeys once more, then sleep. From then on the file is format 2.

**Installed for these tests:** `GK2Mounts.dll`, `GK2Showcase.dll` and the test mod `GK2MountsHerd.dll` in
`BepInEx\plugins`. The first two are deployed by every build. The herd mod is only for testing and isn't deployed by
default (since 2026-10-01, so normal play doesn't get its keys): build it with `dotnet build -c Release -p:Deploy=true`
in `examples/GK2MountsHerd` while the game is closed. Remove it afterwards (that is test J5).

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
| `Mounts: game ready (the game started, N frame(s) and S s after its save appeared)` | The ready step: the restore is done and mods may spawn |
| `... is dormant (<mod> isn't installed); kept in the file, back when its mod is.` | A kept donkey whose mod is missing |
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

### T6. A foal left behind in another scene *(fixed in 0.10.0)*

1. Spawn a family. Ride the parent through a door to another scene and **sleep straight away**, before the foals
   catch up.
   - **In the side file:** the foal blocks have the **old** scene, the parent the new one.
2. Reload.
   - **Expected:** the foals come back in their own (old) scene and catch up with the parent as usual. They are not
     in the parent's scene at the old scene's coordinates, which is what 0.9.0 did.

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

Covered in detail by J5: the herd mod removed and put back.
- **Expected since 0.10.0:** the instances are **dormant**, not spawned. The panel lists them under their mod, marked
  NOT INSTALLED, and they come back when the mod does.

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

---

## J. Several mods at once (the herd test mod)

The test mod `GK2MountsHerd` (GUID `dreas.gk2.mounts.example.herd`) keeps its own herd alongside the panel's and the
showcase's donkeys. Its log lines start with `Herd:`.

| Key | What it does |
|---|---|
| K | Spawns the missing herd donkeys #1–#5 around the keeper (keys `herd-1`…`herd-5`, each with a name). When all are there: calls them to the keeper |
| Shift+K | Removes the herd and its foals |
| U | A herd foal for the nearest grown donkey of **another** mod |
| Shift+U | A report in the log: every instance of every mod with its number, key, kept, and the herd's data |

### J1. The ready gate and the herd's own checks

1. Start the game and load your save.
   - **Log, in this order:**
     1. `Herd: a spawn while the game is still loading was refused, as it should be.` It comes before the ready line,
        with the core's `could not spawn: Not until the game is ready` next to it.
     2. `Mounts: game ready (the game started, ...)`. The words "the game started", not "fallback": the game's own
        signal arrived.
     3. `Herd: game ready: 0 of 5 herd donkeys back [], 0 foal(s). Mods in the world: ...`.
   - **Write down** the frames and seconds in the ready line, to compare with later loads.

### J2. Same numbers, different mods

1. On the panel, spawn three donkeys. The rows show `#1`, `#2`, `#3` in the **GK2 Mounts panel** group.
2. Press K. Five herd donkeys appear around the keeper.
   - **The panel:**
     - a second group, **GK2 Herd (test)**, with `#1`–`#5`, keys `'herd-1'`…, and "Mods' data: herd.name = Bella" and so on;
     - both groups have a #1, #2 and #3;
     - the markers over the donkeys read `Mounts panel #1` and `Herd (test) #1`, each group in its own colour.
3. Press Shift+U.
   - **Log:** a report listing both mods' instances, each `owner#id` once.

### J3. Everything comes back with its own number

1. With J2's donkeys, ride one of the herd off a bit and dismount. Put cargo on a panel cart if you have one.
2. Sleep, then reload.
   - **Log:**
     - `Persistence: <slot>: N of N ... back`;
     - `Herd: InstancesRestored: N back, 5 of them the herd's`;
     - `Herd: game ready: 5 of 5 herd donkeys back [#1 Bella, #2 Moss, #3 Turnip, #4 Clover, #5 Barley]`, with no
       KEY MISMATCH;
     - `Herd: asking for #1 again was refused, as it should be.`
   - **The panel:** the same numbers in both groups as before the save. The herd's names are kept.
3. Press K: no new donkeys; the five come to the keeper (`0 spawned, 5 called`).

### J4. A foal across mods

1. Stand near a **panel** donkey without a cart and press U.
   - **Log:** `Herd: foal ...herd#6 'Pip' for dreas.gk2.mounts.panel#N (another mod's)`.
   - **The panel:**
     - the foal is in the herd group, "foal of Mounts panel #N";
     - the panel donkey's row says "parent of Herd (test) #6".
2. Sleep, then reload.
   - **Expected:** the foal is back next to its panel parent, with the same numbers on both. It follows the parent
     when you ride the parent off.
3. Remove the panel parent (Remove on its row).
   - **Expected:** the herd's foal goes with it (it was spawned with `RemoveWithParent`, the default).

### J5. A mod removed: dormant, then back

1. With the herd and a herd foal on a panel donkey, sleep and quit.
2. Move `GK2MountsHerd.dll` out of `BepInEx\plugins`. Start and load.
   - **Log:** one line per herd instance, `... is dormant (dreas.gk2.mounts.example.herd isn't installed); kept in
     the file, back when its mod is.`
   - **In the world:** no herd donkeys. The panel's donkeys are there.
   - **The panel:**
     - a group with the herd's GUID, marked NOT INSTALLED, listing each herd instance as dormant;
     - the foal on the panel donkey is dormant too: its own mod is missing, though its parent is there.
3. Sleep, then open `Steam_1.gk2mounts.dat`: the herd's records are still in it, unchanged.
4. **The panel's herd group header** reads `0 in the world, 0 kept with saves, 6 dormant` (5 donkeys and the foal).
5. Quit, put the DLL back, start and load.
   - **Expected:** the herd is back, with its numbers, names and the foal on its panel parent.
     `Herd: game ready: 5 of 5 ...`.

### J6. Forget a dormant record

1. Repeat J5 up to step 2, then press **Forget** on one dormant herd donkey.
2. Sleep, put the DLL back, and load.
   - **Expected:** that one doesn't come back; the other four do. Its cargo, if any, lies where it stood.

### J7. Numbers are never handed out twice

1. Press Shift+K (the herd goes), then K: it is spawned again as #1–#5, because the herd asks for those numbers
   itself.
2. On the panel, note the highest number in the panel group (say #3). Remove #3, spawn another donkey.
   - **Expected:** it is #4, not #3. Sleep, reload, spawn one more: #5. The counter survived the save.

### J8. Only the owner changes "kept" and the key; any mod may add its own data

The herd checks this by itself on every load, on the first instance of another mod.
1. Load.
   - **Log:**
     - `Herd: changing <owner#id>'s keep and key was refused, as it should be (Only its owner (...) can change that.)`;
     - `Herd: kept its own data on <owner#id>: herd.seen = N`.
   - **The panel:** that donkey's row shows "Mods' data: Herd (test).seen = N".
2. Sleep, then reload.
   - **Expected:** `herd.seen = N+1`. The herd's data on another mod's donkey was kept with the save.
3. On the panel, press "Don't keep with saves" on a herd donkey.
   - **Expected:** it works, since the panel is the core's and may change anything. Press it again to keep the
     donkey.

### J9. The showcase finds its donkey after a load

1. Press H (the showcase): its donkey comes and you ride. Dismount somewhere and press G to drive, then leave the cart
   standing (J, the hitch key) and ride off.
2. Sleep, then reload.
   - **Log:** `Found the kept donkey dreas.gk2.showcase#N and cart dreas.gk2.showcase#M.`
     - The cart's number can be higher than you'd guess: hitching ends the free cart's instance (its number is
       retired), and leaving the cart standing makes a new one with the next number and the key `cart`.
   - **Expected:**
     - **H** brings the **same** donkey (the same #N as before the save), not a new one;
     - **G** brings the same cart;
     - the panel's showcase group never grows past those two.

## Results

| Test | Result | Date | Notes |
|---|---|---|---|
| T1 | passed | 2026-10-01 | 5 of 5, 2 cargo items; foal #5 was saved next to an obstacle (harmless log line) |
| T2 | passed | 2026-10-01 | own colour (CoatColorize FF00FF) kept across loads |
| T4 | passed | 2026-10-01 | removed panel#2 without sleeping: back after the reload |
| T5, T6 | not run | 2026-10-01 | no other scene unlocked yet (the house is part of RuinedTemple) |
| T7, T8 | passed | 2026-10-01 | panel#2 left its cart as panel#7 with 2 wood; panel#1 hitched it; both kept |
| T9 | passed | 2026-10-01 | panel#6 (not kept) with a hitched kept cart: both gone after the reload |
| T19–T21, T24, T25 | passed | 2026-10-01 | one load, all faults at once: x read as 0; no scene: skipped; unknown setting: skipped; 1 cargo not found; foals of a deleted parent went with it; a duplicate record skipped |
| T22 | passed | 2026-10-01 | after a fix: a cut-off record is now skipped (it would have spawned from defaults at 0,0,0) |
| T23 | passed | 2026-10-01 | format 3: nothing restored |
| T26 | passed | 2026-10-01 | save time off by 1 s: ignored, nothing restored |
| T16 | passed | 2026-10-01 | slept without the mods (save 3:55:06 PM), then loaded with them: the side file of 3:19:18 PM ignored, nothing restored; the save restored from a copy afterwards |
| T12–T14 | open | | need a second save slot (a new game) |
| T3 | passed | 2026-10-01 | two sleeps in a row: 63 kept each time, no .tmp left |
| T10 | passed | 2026-10-01 | panel#1 (cart with 2 wood, parent of the herd foal Fig) not kept: both gone after the reload; the save logs "kept, but its parent isn't" |
| T28 | passed (load), slow (play) | 2026-10-01 | 63 restored, ready in 28 frames / 3.5 s as with 12; but with ~65 roaming the game is very choppy: optimisation pass needed (foal churn: 372 of 410 foal log lines are can't-follow / caught-up cycles) |
| T11, T15, T17, T18, T29 | open | | T27 covered by J5 |
| J1 | passed | 2026-10-01 | Early spawn refused twice (main menu and load); "the game started", 28 frames / 3.2 s |
| J2 | passed | 2026-10-01 | panel #1–#5 and herd #1–#5 side by side |
| J3 | passed | 2026-10-01 | 13 of 13 back with own numbers and keys, 2 cargo on the cart, herd 5 of 5 with names, #1 clash refused; own colour kept |
| J4 | passed | 2026-10-01 | herd foals on panel#1 and showcase#1 both came back with their parents |
| J5 | passed | 2026-10-01 | 7 dormant (5 donkeys, 2 foals), NOT INSTALLED group; all back with the DLL |
| J6 | passed | 2026-10-01 | herd#3 forgotten: 4 of 5 back |
| J7 | passed | 2026-10-01 | after removing panel#3 the next was panel#6 |
| J8, J9 | partly | 2026-10-01 | the showcase found its kept donkey (no new spawn); the cart part and J8 not run |
| Panel | fixed | 2026-10-01 | Forget and Remove were pushed off the right edge; Forget now under its text, Keep on the second row |
