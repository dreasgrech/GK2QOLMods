# GK2 Mounts: keeping donkeys and carts across saves

This was investigated on 2026-10-01 against the Steam build 25601286 (decompiled `SaveSystem`, `SaveSlotData`,
`GameSave`, `EnergySystem`, `Item`, `DropData`).

**API 0.10.0 (several mods, per-mod ids, GameReady, dormant records, per-mod data) is described in "Several mods:
what was decided and built" below.** The rest of this block describes 0.9.0.

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

## Several mods: what was decided and built (API 0.10.0, 2026-10-01)

**Status:** built the same day, not yet tested in game. The investigation and the options are in "Several mods
keeping their donkeys" and "Identity" below; this section is what was chosen and why. The test mod is
`examples/GK2MountsHerd`, and the tests are part J of [`mounts-persistence-tests.md`](mounts-persistence-tests.md).

**Not yet released, so no compatibility is kept.**
- The side file moved to **format 2**. The format 1 file from the first build is ignored once ("has format 1"), and
  the next save writes format 2.
- `MountHandle.Id`, `Persist` and `Key` changed meaning or form.

### The decisions, with the data behind each

| Question | Decision | The data |
|---|---|---|
| Identity | **Owner plus a number per owner** (`owner#id`). Two mods can both have a #2, and the number is the same after every load | Before this, `MountHandle.Id` was one session counter shared by every mod, and everyone's numbers were new after a load. The showcase spawned a fresh donkey each session because it couldn't find its old one |
| When mods may spawn | **After `Mounts.GameReady`**, which comes once per loaded game, right after the restore. Spawning before it is refused | **The game's own signal:** `MainGame.OnGameStarted` is a public static `Action` the game invokes at the end of `StartGame`, after `AwaitGameplaySceneLoadAsync`. So the scenes and the drops are there; the first build restored earlier, on the first frame with a scene. **Order:** every mod using the API declares `[BepInDependency("dreas.gk2.mounts")]`, and BepInEx loads dependents after their dependency, so our subscription (in our `Awake`) runs before theirs. **The race:** another mod's `Update` runs before our `LateUpdate` in the same frame, so a mod could otherwise spawn before the restore and take a kept donkey's number |
| Reusing numbers | **Never by the counter.** Each owner's next number is saved (`next` header lines). A mod may still ask for a free number itself (`MountSpawn.Id`) | A mod that keeps "#3 is the miller's donkey" in its own data must never find a different donkey under #3. It costs one int per owner in the file |
| One file or one per mod | **One file per slot** | Cross-mod links exist (a foal on another mod's donkey, a cart hitched across mods, settings by several mods on one donkey). One file is written in one step. The cloud limits (1000 files, 1 GB) don't decide it |
| An owner not installed | **Dormant:** not spawned, written back unchanged, number and key reserved, back with the mod. The panel lists them with "Forget" | Installed plugins are `BepInEx.Bootstrap.Chainloader.PluginInfos`, read at the restore. **Dormant cargo** lies on the ground as the save has it. **A corpse** dropped there gets the game's `corpse_auto_destroy_timer` (`DropData.TryStartAutoDestroyTimer`), so corpse cargo of a dormant cart can be removed by the game. Other items have no timer |
| Who changes `Persist` and `Key` | **The owner only**, plus the core and its panel: `SetPersist(ownerId, …)`, `SetKey(ownerId, …)` | Another mod keeping a donkey whose owner respawns its own each session would make two |
| A mod's own data | **Yes:** `SetData` / `GetData` / `DataKeys`, by mod id, saved as `data` lines | A mod can then keep a name, a home or a mood without its own file. Each mod has its own namespace, and any mod may add data to any donkey |

### What it does

- **Identity:**
  - every log line and the panel use `owner#id`, e.g. `dreas.gk2.mounts.example.herd#2`;
  - `Mounts.Find(owner, id)`, `Find(owner, key)` and `InstancesOf(owner)`;
  - `MountHandle.FullId`.
- **Keys** are unique per owner. A second spawn or `SetKey` with a key in use (by a live or a dormant donkey) is
  refused.
- **Numbers:** `MountSpawn.Id = n` is refused when the owner has #n, live or dormant. With 0, the owner's counter
  skips every number in use.
- **The ready step** (`MountRegistry.BecomeReady`):
  1. on the frame after the game's `OnGameStarted`, the counters and dormant records are reset;
  2. the side file is restored;
  3. `IsGameReady` turns true;
  4. `InstancesRestored`, then `GameReady`, fire.
  - The log line: `Mounts: game ready (the game started, N frame(s) and S s after its save appeared)`.
  - **Fallback:** without the game's signal, 90 s after the save appeared in a scene (logged as a warning).
  - A restore that throws still makes the game ready, so spawning is never blocked for a whole session.
- **Restore:** by identity, so the record order doesn't matter. Parents come back first, then children.
  - A foal comes back in **its own** saved scene, also when its parent is elsewhere. It catches up from there, which
    fixes T6.
  - A child whose parent is dormant is dormant too.
  - A child whose parent isn't in the file goes, or becomes an orphan if `RemoveWithParent` is false.
- **Cargo reclaim:** by GUID, and only from within 3 units of where the cart stood. An item the keeper carried
  elsewhere stays his, which matters for dormant carts whose items lie on the ground for a while.
- **Text in the file** is escaped (`\t`, `\n`, `\\`), so keys, names and data can hold any characters.
- **Hitching across mods:** the donkey's instance goes on. The free cart's data is merged into the donkey's without
  replacing the donkey's own values, and its key and number end.
- **The panel's Instances tab:**
  - the list is grouped by mod: its name, GUID, "NOT INSTALLED" if so, and counts in the world, kept and dormant;
  - rows show `#id` within their group, and "Mods' data" when there is any;
  - dormant records are listed with "Forget";
  - links to another mod's donkey read `Herd #2`;
  - world markers read `Showcase #1`, in a colour per mod that is the same as the group header.
- **The showcase:** keeps its donkey and cart with saves (`KeepWithSaves`, default on), under the keys `donkey` and
  `cart`. On `GameReady` it finds them by key instead of spawning new ones. A cart it leaves standing gets the key
  `cart`.
- **The test mod `examples/GK2MountsHerd`** (`dreas.gk2.mounts.example.herd`):
  - **K:** spawns the herd #1–#5, keys `herd-1`…`herd-5`, each with the data `name`. The panel's first spawns get
    #1–#5 too, under their own owner.
  - **Shift+K:** removes the herd.
  - **U:** a herd foal for the nearest donkey of another mod, a cross-mod link.
  - **Shift+U:** a report in the log.
  - **Checks it runs by itself:** a spawn while the game is still loading must be refused, and after `GameReady`,
    asking again for #1 must be refused. Its lines start with `Herd:`.

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

**The game's exact cloud rule** (read from Steam's app cache, `Steam\appcache\appinfo.vdf`, app 4358690, section
`ufs`):

| Setting | Value |
|---|---|
| Patterns | `*.dat` and `*.info` in `WinAppDataLocalLow/Lazy Bear Games/Graveyard Keeper 2`, no subfolders (not recursive) |
| Platforms | Windows only. Linux and the Steam Deck run the Windows build under Proton, so the same rule applies there |
| Quota | 1,000,000,000 bytes |
| Max files | 1000 |

A save slot uses 8 files (the save, its `.info` and three backups of each) and about 48 MB. So ten slots are about
80 files and 0.5 GB. Our files are a few KB each.

## Several mods keeping their donkeys (investigated 2026-10-01; what was built is above)

**The question:** several mods use the API, and each keeps its own donkeys with the save. One file for all, or one
file per mod?

### What already works

**Several mods already work today.** The side file holds every kept instance, whoever owns it, and each record
carries its owner's id (`owner`).
- **On load:** each comes back under its own owner.
- **Events:** `InstancesRestored` gives every mod the full list, and each picks its own by `Owner` and `Key`.
- **Removed mods:** a mod that is removed or added doesn't touch the others' records.

### One file or one per mod

**What decides it: instances can be shared between mods.** The API allows that today:
- A **foal** can have a parent of another owner (`MountSpawn.Parent` takes any donkey).
- **Hitching** joins two instances into one. A donkey of mod A takes a free cart of mod B, the cart's instance goes,
  and its cart settings move onto A's donkey with B still named as their owner.
- **Own settings** on one instance can come from several mods: `InstanceTuning.Set(ownerId, ...)` records who set
  each key.

So one instance's record can hold data of several mods, and a parent link can point at another mod's donkey.

| | One file per slot (today) | One file per mod per slot (`<slot>.gk2mounts.<modid>.dat`) |
|---|---|---|
| Cross-mod foals and hitched carts | Simple: links are positions in the same file | Needs ids that work across files, and an order to load the files in |
| Settings of mod B on mod A's donkey | Stay with the donkey | Must be split out, or lost |
| Writing | One file, swapped in at once | N files: a failure partway leaves a mix of old and new files |
| Cloud | 1 file per slot | N per slot. Fine under 1000, but each file syncs, or fails to, on its own; the stale check is per file |
| A broken record | Only that record is skipped (the parser is per record) | Only that mod's file |
| Deleting a slot | 1 file | Every `<slot>.gk2mounts.*.dat` |
| A user removing one mod's donkeys by hand | Edit the file | Delete its file |

**Recommendation: keep one file per slot.** Separate files would buy only a hand-delete convenience, and they'd break
the cross-mod links the API already allows. Isolation between mods comes from the format instead: each record is
parsed on its own, and unknown lines are skipped. A mod can also always clear its own donkeys through the API, by
removing them before the next save.

### What multi-mod support still needs

1. **Who may change `Persist`.** Today any mod can set `MountHandle.Persist` on another mod's donkey.
   - **The problem:** if mod A keeps mod B's donkey while B spawns its donkeys again each session, there are two
     after a load.
   - **The fix:** only the owner, or the panel, may change it. This means `SetPersist(ownerId, bool)`, which refuses
     other owners, in place of the setter.
2. **What happens when an owner mod isn't installed.** Today its donkeys come back anyway, without whatever that mod
   did with them: rules, behaviour, its own UI. There are two ways to handle it:
   - **(a) Dormant (recommended).**
     - Records of an owner that isn't loaded (`BepInEx.Bootstrap.Chainloader.PluginInfos`) aren't spawned, but are
       carried into the next side file unchanged. When the mod comes back, so do its donkeys.
     - The core's own ids and the panel's are always loaded.
     - **Their cargo:** it stays where the save has it, on the ground at the cart. When the mod comes back, the
       items are reclaimed only if still lying within about 2 units of the cart, so an item the keeper carried off
       and dropped elsewhere stays his.
     - **The panel** lists dormant records, with "Forget".
   - **(b) Spawned anyway**, as today, under the missing owner's id, removable from the panel.
3. **A mod's own data on an instance.** Mods will want to save more than the core knows, such as a name, mood or
   home, without a file of their own.
   - **The API:** `MountHandle.SetData(ownerId, key, string value)`, `GetData(ownerId, key)` and
     `ClearData(ownerId, key)`. The data is scoped by owner, so two mods can't collide.
   - **The file:** saved as `data <owner> <key> <value>` lines. Any mod may attach data to any instance; a naming
     mod could name the panel's donkeys.
   - **Compatibility:** the current reader skips lines it doesn't know, so format 1 stays. A file written with data
     and read by this build only loses the data.
4. **Restored per owner.** `InstancesRestored` already gives each mod the full list. A filter (`Mounts.InstancesOf(ownerId)`)
   is a convenience, not a need.
5. **A key that repeats within one owner** is allowed. The docs should say keys should be unique per owner, since
   the restore gives every match.

### Identity: each mod numbers its own donkeys (the proposal; built as decided above)

**The request:** two mods each have five donkeys numbered 1 to 5. Both should keep their own numbers, across loads
too, and the panel should show which mod each one belongs to.

**Today:**
- **`MountHandle.Id`** is one counter for the whole session, shared by every mod. Mod A gets #1–#5 and mod B #6–#10,
  or interleaved, depending on who spawns first.
- **After a load, everyone's numbers are new** (`Mount.NextId`).
- **`Key`** (a string, optional, not checked for repeats) is the only stable handle a mod has.
- **The showcase** works around this by spawning a fresh donkey every session (`GK2Showcase/Plugin.cs`, "Spawned the
  keeper's donkey #N"). With persistence it would get one more each session unless it finds its old one.

**Proposal: an instance is (owner, id), and the id belongs to the owner.**

| | |
|---|---|
| **Identity** | `Owner` (the mod's BepInEx GUID) plus `Id`, a number counted per owner. Written `owner#id`, e.g. `dreas.gk2.showcase#1`. Two mods can both have #2 |
| **Stable** | The id is saved in the side file and the same donkey has the same id after every load. It never changes while the instance exists |
| **Chosen or given** | `MountSpawn.Id = 3` asks for that number. Refused if that owner already has a #3, or one is reserved (below). Left at 0: the core gives the owner's next number |
| **Never reused** | Each owner's counter is saved too (a header line `next <owner> <n>`), so a removed #3 isn't handed out again. A mod that wrote "#3 is the miller's donkey" into its own data never finds another donkey under that number. A mod that wants to reuse can ask for the id itself |
| **Lookup** | `Mounts.Find(ownerId, id)` and `Mounts.Find(ownerId, key)`; `Mounts.InstancesOf(ownerId)` |
| **Keys** | Stay as names (`Key`), unique per owner. A second spawn with a key the owner already uses is refused, so `Find(owner, key)` has one answer |
| **The summon** (TryMount, never kept) | Owner `dreas.gk2.mounts`, its own numbers |
| **A cart left by unhitching** | The donkey's owner, that owner's next number. The `Unhitched` event gives it |
| **Hitching across owners** | The donkey's instance goes on with its own id. The free cart's instance ends (`InstanceRemoved` with its id, so its owner knows), as today |
| **Logs and `ToString`** | `owner#id`, e.g. `Mount: spawned dreas.gk2.showcase#1 donkey ...`. A short name in the panel |

**Ids reserved before the restore.** A mod may spawn on the same first frame the restore runs, before it, and ask for
an id a kept donkey of that save already has. There are two ways to prevent that:
- **(a, recommended) A ready signal.** A new event, `Mounts.GameReady`, comes once per loaded game, right after the
  restore, even when nothing came back. Spawning before it is refused ("not until the save's kept donkeys are back").
  Mods do their per-game spawning in `GameReady`, and check `Find(owner, id)` first. One rule for every mod, and no
  race.
- **(b) Reserving early.** The side file is read as the game loads, on `SaveSystem.OnSaveLoadingEnded`, and its ids
  are reserved, so early spawns get other numbers. That needs no new contract, but a mod could still end up with a
  duplicate donkey under a new number.

**Links in the file by identity, not position.** A foal's parent is saved as `parent <owner> <id>` instead of an
index.
- The restore spawns parents first (a sort), so the order in the file stops mattering, and links across mods are
  plain.
- **It keeps the storage choice open:** with stable `owner#id` everywhere, one file or one file per mod would be
  only a storage detail. One file stays the recommendation (above).

**The panel:**
- **The list is grouped by mod,** under a header with the mod's name from its BepInEx metadata, its GUID, and its
  counts: in the world, kept, dormant. For example, `GK2 Showcase (dreas.gk2.showcase): 2 in the world, 1 kept`. The
  panel's own spawns and the core's are groups too.
- **Each row** shows `#2` within its group. The world markers show `Showcase #2`, with a colour per mod and the same
  colour on the group header.
- **Dormant records** (owner not installed) are listed in their mod's group, greyed, with "Forget".
- **An owner id that isn't a loaded plugin** (a typo in a mod's GUID) is marked in its header. The log warns once at
  its first spawn.

**What it changes in the API (0.10.0):**
- **Breaking:** `MountHandle.Id` now means the per-owner id. Before 1.0 a breaking change bumps the minor and is
  listed under "Versioning". Only the showcase reads it, in a log line.
- **New:**
  - `MountSpawn.Id`;
  - `Mounts.Find(owner, id)`, `Find(owner, key)` and `InstancesOf(owner)`;
  - the `Mounts.GameReady` event;
  - `MountHandle.FullId` (`owner#id`).
- **Inside:**
  - the registry indexes instances by (owner, id);
  - `Describe()` and every log line use `owner#id`;
  - the side file gets the `next` header lines and id-based `parent` lines, and moves to format 2. Format 1 files
    still read, with ids given in file order.
- **The showcase** becomes the example: it keeps its donkey (`Persist`, `Id = 1`), and in `GameReady` it uses
  `Find(Guid, 1)` and spawns only if there's none.

**Scale:** lookups are dictionary hits. Each owner's counter is one int, and every number is a few bytes in the file.
Nothing grows with the number of mods except the list itself.

**Cost:** about two days with the per-owner ids (above), about one without them.
- Items 1 and 3, and the ids: one API change, to 0.10.0.
- Item 2: changes to the restore and the write; the dormant records are kept in memory between the load and the
  next save.
- Tests: a second test plugin that keeps its own donkeys alongside the panel's.

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
