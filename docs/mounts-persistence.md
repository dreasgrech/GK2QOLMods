# GK2 Mounts: keeping donkeys and carts across saves

This was investigated on 2026-10-01 against the Steam build 25601286 (decompiled `SaveSystem`, `SaveSlotData`,
`GameSave`, `EnergySystem`, `Item`, `DropData`).

**Built 2026-10-01 as a proof of concept (API 0.9.0).** Test 1 passed in game the same day: 5 of 5 back in
RuinedTemple, 2 cargo items back on the cart, the foals with their parent and their own settings. Tests 2–7 are
still open.
- **Code:** `src/GK2Mounts/Persistence.cs` (writing and restoring). Also:
  - `SaveSystem_Save_Patch` wraps the game's success callback, and a new `SaveSystem_Remove_Patch` postfix deletes
    the side file;
  - `MountRegistry.Tick` restores on each newly loaded game;
  - `Mount.Spawn` takes a scene, `snap`, `persist` and `key`.
- **Decisions:** the recommended ones below.
  - Opt-in per spawn; the panel's spawns opt in.
  - An owner mod that is gone: its instances are restored anyway, under its owner id.
  - Cargo goes back on the carts.
  - Their own settings are kept.
  - The file sits next to the saves.
- **Side file:** `<save folder>/<slot>.gk2mounts.dat` (moved there the same day, see "Steam Cloud" below), plain tab-separated text, not JSON, so no serializer
  is needed.
  - The setting values use BepInEx's own `TomlTypeConverter`, as the cfg does.
  - It is written to a `.tmp` file first, then swapped in.
- **Fixed along the way:** loading another save from inside a game used to set the old game's cargo down in the new
  world (`ForceCleanup` while the new game was already live). That cleanup now drops nothing.
- **Panel:** the Instances tab has a "Saves:" line with what the last write or restore did. Each row says "kept
  with saves" or "not kept with saves", and has a Keep / Don't keep button.
- **Not done:** a kept free cart hitched to a donkey that isn't kept isn't kept any more. Its cargo is safe on the
  ground after a reload.

## Steam Cloud (measured 2026-10-01)

**Steam Auto-Cloud syncs only part of the save folder.** Steam's own record of what it synced
(`Steam\userdata\<account>\4358690\remotecache.vdf`) listed only the top-level `.dat` and `.info` files.
- **Not synced:** the first build's `GK2Mounts\` subfolder, the `Mods\` folder and other files.
- **What happened:** on a second install the saves arrived and the donkeys didn't.

**The test that settled it:** a copy of the side file, named `Steam_1.gk2mounts.dat` and placed beside
`Steam_1.dat`.
- After one game session it showed up in `remotecache.vdf` with its size.
- It also appeared on the other install.

**So the side file now lives there:** `<save folder>/<slot>.gk2mounts.dat`.
- **Safe for the game,** from its save code (`JsonFileSerializer.LoadAll`, `OdinBinaryFileSerializer`):
  - the save list is built from `*.info` files only;
  - a `.dat` is opened only by its exact slot name;
  - backups are `<slot>_backup_N.dat`, made only for the slot being saved.

  So the game never lists, loads, overwrites or deletes ours.
- **The temporary file** of a write is `<slot>.gk2mounts.dat.tmp`. It doesn't end in `.dat`, so it isn't synced.
- **Cloud timing:** if the save syncs and the side file doesn't, or the other way round, the stale check ignores the
  side file. Nothing comes back, and nothing doubles.

## In-game test list

The full step-by-step plan, with the edge cases (29 tests), is [`mounts-persistence-tests.md`](mounts-persistence-tests.md).
The short list:

1. **The basic round trip:**
   - Spawn a donkey, a donkey with its cart (put cargo on it) and a family from the panel.
   - Sleep. The log says `Persistence: N donkey(s)/cart(s) with M cargo item(s) kept with <slot>`.
   - Quit to the menu and load that save. They stand where they stood, the cargo is on the cart (not on the
     ground), and the foals are with their parent. The log says `Persistence: <slot>: N of N ... back`.
   - **This test checks a timing assumption.** The restore runs on the first frame of the loaded game. If at that
     moment the other scenes or the drops aren't ready, you'll see one of these:
     - "could not be spawned again: There is no scene ...";
     - "not found on the ground";
     - a crate on the ground next to a cart that also carries it.
   - The fix then is to take the cargo back when the cart's view is first prepared, not on that first frame.
2. **Another scene:** leave a donkey outside, go indoors, sleep there, and load. Back outside, the donkey is where
   you left it.
3. **Own settings:** give one donkey its own colour on the panel. After a save and load it still has it.
4. **Not kept:** press "Don't keep" on one, then save and load. It is gone, and any cargo it carried lies on the
   ground.
5. **Stale file:**
   - Save and quit, then move `GK2Mounts.dll` (and the showcase) out of `BepInEx\plugins`. Load the save, sleep,
     and quit.
   - With `General.Enabled` off instead, the patches still run: that save deletes the side file, since nothing is
     kept, rather than leaving a stale one. That is safe too.
   - Turn the mod back on and load. The log says `... ignored, nothing restored`, and the cargo is on the ground
     once, not twice.
6. **Delete the slot** in the game's menu: its `.gk2mounts.dat` goes too.
7. **Load another save from inside a game**, if the game's menu offers it: no cargo from the first game appears in
   the second.

**What the file looks like:** open `<save folder>/<slot>.gk2mounts.dat` (text, despite the extension). The save folder is the game's
persistentDataPath, usually `%USERPROFILE%\AppData\LocalLow\<company>\Graveyard Keeper 2\`. The log prints the full
path at each save.

**The questions:**
1. Does the mod do anything special around a save today?
2. Can a mod that uses the API save the full state of every spawned donkey, with its position?
3. Is that feasible?

## 1. What happens around a save today

**Only the cargo is handled.** The donkeys and carts themselves are never saved, on purpose:
- every view is a temp `WgoData` (`isTempObject`), which no save contains;
- `WhyDefinitionTouchesTheSave` refuses any donkey definition that would leave a mark in the save.

The mod leaves nothing of its own in the game's save files. Uninstalling it can't break a save.

**Around each save** (`SaveSystem_Save_Patch`):
- **Before:** the ridden summon's cargo goes back to the keeper (hands, then the ground). Every standing instance's
  cargo is dropped at its cart, in its scene, or in that scene's drop queue when the scene isn't loaded. So the
  save has every item (`LendCargoToSave`).
- **After:** those drops are taken back onto the carts (`TakeCargoBackFromSave`).

**On loading a save:** `MountRegistry.Tick` sees a new `GameSave` and removes every instance ("another game was
loaded"). The cargo lies on the ground where each cart stood.

**When the game saves:** there is exactly one caller of `SaveSystem.Save`: `EnergySystem.StopSleeping`, on waking.
- **No autosaves:** `isAutoSave` is never set to true.
- **No save on quitting.**
- **Never mid-ride:** sleeping already ends the ride, so at a save `Ridden` is always null. Nothing about a ride
  needs saving.

## 2. Can a mod using the API do it today?

**Partly.** It can record nearly everything, but it can't put all of it back.

**What a mod can read** from `Mounts.Instances`:

| State | From | Can it be restored? |
|---|---|---|
| Donkey, cart, or both (hitched) | `HasDonkey`, `HasCart` | Yes, with `Spawn` |
| Position, facing | `Position`, `FacingRight` | Only in the keeper's current scene (see below) |
| Scene | `SceneId` | **No.** `Spawn` always uses the keeper's scene; `MountSpawn` has no scene field |
| Size, rideable | `Scale`, `IsRideable` | Yes |
| Foals | `Parent`, `Children` | Yes, but `RemoveWithParent` can't be read |
| Its own settings and colours | `Tuning.Keys`, `IsSet`, `Get<T>` | Yes, with `Tuning.Set`. The type comes from `Mounts.Tuning(style)`, `Behaviour`, `Shuffle` or `Colors` `.TypeOf(key)`, as `InstanceTuning` has none |
| Cargo | `CargoIds` | **No.** There is no call that puts a drop onto a standing cart, and `CargoIds` gives item kinds, not the GUIDs needed to find the right drops |
| Owner | `Owner` | Yes, by spawning under the same owner |
| Its id | `Id` | No: the ids are new after a load. A mod has to map them itself |

**What a mod would have to do itself, outside the API:**
- **Know when the game saves:** use the game's own public events `SaveSystem.OnSaveWriteStarted` and
  `OnSaveWriteEnded`, or patch `SaveSystem.Save`.
  - The write can fail (`HasEnoughFreeDiskSpaceForSave`, the serializer).
  - Only `callbackSuccessful`, or `SaveSystem.RelevantSlot` being set, tells success apart.
- **Know which save:** `MainGame.Instance.SaveSlotData.slotName`.
  - Demo saves get a new name inside `Save`, so it has to be read after the write.
- **Know when to put them back:** after the load *and* after the core's own cleanup has run. The API doesn't say
  when that is.
- **Keep its own file:** per slot, and detect when it is stale (see below).

**A gap in the docs:** `mounts-instances.md` says "the mod that spawned them spawns them again". That is true only
for donkeys in the scene the keeper wakes up in.

## 3. Is it feasible?

**Yes.** Following the rule that the API does the heavy work, the core should do it, not each mod. Nothing new is
needed from the game, and the game's save stays untouched.

### Where the data goes: a side file per save slot

- **Not inside `GameSave`.** It is an Odin-serialized game class with no room for mod data. Storing anything in it,
  for example game resources on `PlayerData`, is exactly the mark in the save the mod refuses to leave.
- **A JSON file per slot, keyed by `slotName`,** written right after a successful save.
- **Stale files are detected:**
  - **The problem:** the slot can be saved again without the mod (uninstalled, or disabled after a game update), or
    restored from Steam Cloud or a backup.
  - **The check:** the side file records the slot's `saveDateTime` and `day`, which `GameSave.PrepareToSave` sets
    on every save. On load they must match the loaded slot's. If not, the file is ignored and the log says so.
  - **Why it matters most:** this check is what makes the feature safe. Without it, a reload could bring back
    donkeys, and claim cargo, from a different point in time.
- **Deleting a slot** (`SaveSystem.Remove`): a postfix deletes its side file.

**Location (a decision):**
- **Next to the saves** (`SaveSystem.SaveFolder`, the game's persistentDataPath): it moves with the saves.
  - Whether Steam Cloud syncs extra files there depends on the game's cloud settings, which can't be seen from here.
  - If it doesn't sync, a cloud-restored save on another PC has no side file, and the stale check makes that harmless.
- **The BepInEx folder** (`BepInEx/config/GK2Mounts-saves/`): it never touches the game's folder, but it isn't
  synced and stays on this PC.

### What is saved per instance

- **Shape:** owner, donkey/cart/both, scene id, position, facing, scale, rideable.
- **Foals:** the parent (by its index in the file) and `RemoveWithParent`.
- **Its own setting overrides** (`InstanceTuning`): key, value and who set it.
  - **A decision:** today these are documented as session only.
- **Cargo:** the `Item.UniqueId` GUID of each item lent to the save.
  - `Item.uniqueId` is a `[SerializeField]`, so it is kept in the save and is the same after a load.
  - The `lent` list already holds each `DropData`, so this is a few lines.
- **Not saved:** anything moment to moment (shuffle phase, foal following, routes). After the load they start
  standing, which they do anyway.
- **Not needed:** the ride. It never exists at a save time.

### Putting them back

- **When:** the existing `lastGameSave` change in `MountRegistry.Tick` is the moment.
  - It already fires once the new game is live and the keeper is in a scene, just after the cleanup.
  - That is better than `SaveSystem.OnSaveLoadingEnded`, which comes before the world is set up.
- **Order:** read the side file for `MainGame.Instance.SaveSlotData`, then spawn parents before foals.
- **Spawning into any scene:** the internal `Spawn` gets a scene id.
  - Instances already live without a view in scenes that aren't loaded; their view comes when the scene does.
  - `SnapToGround` can't test an unloaded scene, so a restored position is kept as saved. The ground snap happens
    when the view first spawns.
- **Cargo:** the opposite of `TakeCargoBackFromSave`.
  - Find each saved GUID among that scene's `droppedItems` and `queuedDrops`, remove the drop, and put the item on
    the cart.
  - An item the keeper picked up, or that is gone, is skipped and logged.
  - Matching by GUID can't take a different drop of the same kind.
- **Telling owners:** `InstanceSpawned` fires for each one. A new event, `InstancesRestored`, gives the owner its
  handles after a load so it can map its old ids to the new ones.
  - **A new optional `Key`** (a string the owner gives at spawn, saved with the instance) makes that mapping easy.
    The showcase could tag its donkey "showcase:main".

### API changes (0.9.0)

- **Spawning:**
  - `MountSpawn.SceneId` (optional, default the keeper's scene).
  - `MountSpawn.Persist` (bool) and `MountSpawn.Key` (string).
- **Reading:** `MountHandle.RemoveWithParent`, `Persist`, `Key`.
- **Events:** `Mounts.InstancesRestored` (`Action<IReadOnlyList<MountHandle>>`).
- **A decision:** what persists without being asked.
  - Instances whose owner never opted in are not kept, so a mod that spawns its own each session doesn't get
    doubles.
  - Instances whose owner mod isn't installed any more: kept or dropped.

### Risks

- **A mod that already respawns its donkeys itself** (the showcase does on each session) must switch to `Persist`.
  Otherwise there are two of everything. This is why the opt-in matters.
- **A changed setting key:** an override saved under a key that no longer exists is skipped and logged.
- **Cargo grabbed in between:** the keeper can't touch the drops before the restore. It runs on the first in-game
  frame, before he can move.
- **Unloaded scenes:** queued drops are found in `queuedDrops`, as the lending already relies on.

## Open decisions for the user

1. **Who persists:**
   - **(a, recommended) Opt-in per spawn** (`Persist = true`). The panel's spawns opt in.
   - (b) Everything, always.
2. **Instances of an owner mod that is gone:**
   - **(a, recommended) Kept, owned by the core,** and listed in the panel so they can be removed.
   - (b) Dropped at load. Their cargo then stays on the ground.
3. **Cargo back on the carts after a load:**
   - **Recommended: yes, in the first version.** The GUID path is mostly built already.
   - Or: later, with the items left lying on the ground until then.
4. **Per-instance setting overrides:**
   - **Recommended: saved for persistent instances.** A donkey keeps its colours and size settings.
   - Or: session only, as documented today.
5. **Side file location:**
   - **Recommended: next to the saves,** moving with them, with the stale check covering the cloud case.
   - Or: the BepInEx config folder.
