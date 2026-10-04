# GK2 Quality Of Life Mods

Quality-of-life BepInEx plugins for **Graveyard Keeper 2**. One feature per plugin; each is its own project and DLL,
so you can install any subset.

| Mod | Version | In one line |
|---|---|---|
| [Global Storage](#global-storage) | 1.1.5 | Craft and build with materials from chests in every area, not just the one you're standing in. |
| [Built Count](#built-count) | 1.0.6 | Build menus show how many of each building you already have in that area. |
| [Move Building](#move-building) | 1.1.6 | Pick up a built building and put it down somewhere else, contents and all. |
| [Owned Count](#owned-count) | 1.0.4 | Item tooltips show how many of that item you own across inventory and every chest. |
| [Tech Search](#tech-search) | 1.0.0 (in testing, not in `dist/` yet) | A search box on the Tech Tree page: find a tech by name, or by what it unlocks, and jump to it. |

**Looking for the donkey?** The rideable donkey and cart (GK2 Saddle Up!, and the GK2 Donkey API it's built on) moved
to their own repo on 2026-10-02, with their history: [GK2DonkeyAPI](https://github.com/dreasgrech/GK2DonkeyAPI). Until
then they were GK2 Showcase and GK2 Mounts here.

## Installing

These are code mods and need **BepInEx 5** (the 5.4.23.x, x64, Mono build). The game's own `Mods` folder
(`%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Mods`) only loads translation and voice packs
and cannot load code, so it is not used.

1. **Back up your saves** first: `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2`. The mods are
   audited not to corrupt saves (see [Safety](#safety)), but a backup costs nothing.
2. **Install BepInEx 5** (`BepInEx_win_x64_5.4.23.x.zip` from the BepInEx GitHub releases). Unzip it into the game
   folder, the one that contains `GraveyardKeeper2.exe`, so that `winhttp.dll`, `doorstop_config.ini` and a `BepInEx\`
   folder sit next to the exe.
3. **Run the game once** and quit. BepInEx creates `BepInEx\plugins`, `BepInEx\config` and `BepInEx\LogOutput.log`.
   If none appear, BepInEx isn't being loaded (usually a wrong zip, or the files landed in a subfolder).
4. **Copy the DLLs you want** from [`dist/`](dist/) into `BepInEx\plugins\`. Any combination works; they don't depend
   on each other.
5. **Start the game.** Each mod writes one line to `BepInEx\LogOutput.log` when it loads, for example
   `GK2 Move Building 1.1.6 loaded.` Settings appear in `BepInEx\config\dreas.gk2.<mod>.cfg` after the first run
   and are read at game start.

To **uninstall** a mod, delete its DLL from `BepInEx\plugins`. To remove BepInEx entirely, delete `winhttp.dll`,
`doorstop_config.ini` and the `BepInEx` folder. Saves made with the mods load fine without them: none of the mods adds
anything to the save format.

Notes:
- The game detects mod loaders and mentions them in its built-in bug reports. If you report a bug to the developers,
  reproduce it without mods first.
- Steam may re-verify files after an update; BepInEx's files are not part of the game's manifest and survive that,
  but check the log after any game update (see [Game updates](#game-updates)).

## The mods

### Global Storage

`GK2GlobalStorage.dll`

The unmodified game lets a workbench, build desk or alchemy table use materials from chests **in the same area** as
that station. With this mod, they draw from chests in **all** areas, so you don't have to carry stone from the
quarry chest to the yard. Items are taken from the chest that has them, nearest pool first, exactly as the game does
within one area.
<br/><br/>

<img width="2560" height="1440" alt="image" src="https://github.com/user-attachments/assets/c9d72151-cb13-4a66-be5f-59c820bec989" />
<i>The red boxes boxes are drawn here to highlight the amount of materials which is the total in all my storages</i>
<br/><br/>

- Zombies are left alone: a zombie working a station still only uses its own area's storage, so their logistics
  are unchanged.
- Settings: `IncludeBuildAndAlchemy` (build desks, town buildings, alchemy, gardens, graves, autopsy pickers and
  requirement tooltips, on by default); `IncludeFuelContainers` and `IncludeConveyorStorage` (off by default so
  remote furnaces and conveyor lines aren't starved); `IncludeZombieOrderDelivery` (off); `VerboseLogging`.
- Optional: with [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) installed, its settings
  also appear in the game's Mods menu. The same DLL works without it.

### Built Count

`GK2BuiltCount.dll`

<img width="2560" height="1440" alt="image" src="https://github.com/user-attachments/assets/82272da5-871d-4d77-8b4d-3458d8c98e79" />
<i>The red boxes boxes are drawn here to highlight the total amount of the specific building built in this area</i>
<br/><br/>


- Every row in a build desk's menu shows how many of that building already stand in the desk's area, as
`Sawhorse (2)`. Rows with none built look unchanged. Buildings the game limits (`x/max`) keep the game's own label.
- Display only; nothing else changes.
- Optional: with [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) installed, it appears in
  the game's Mods menu with its on/off switch. The same DLL works without it.

### Move Building

`GK2MoveBuilding.dll`

Adds a **Move** row next to **Remove** in the build desk menu. Pick it and click a building: the game's own build
preview appears for it, you place it (rotate as usual, overlapping its old spot is fine), and the building is
relocated. You stay in move mode for the next one; Back returns to the menu. Alternatively hold **Left Ctrl** in the
game's normal Remove mode and click.

<p align="center">
<img width="720" height="678" alt="image" src="https://github.com/user-attachments/assets/fca2f71b-1339-4d87-9896-561eb2e6cad3" />
</p>

<img width="2560" height="1440" alt="Graveyard Keeper 2 9_27_2026 3_32_51 PM" src="https://github.com/user-attachments/assets/f928602e-ef05-4e2c-9e4d-9af1294fedeb" />

<img width="2560" height="1440" alt="Graveyard Keeper 2 9_27_2026 3_32_59 PM" src="https://github.com/user-attachments/assets/4d0e7c24-fab4-4abb-9c1c-16ad02842f62" />

<img width="2560" height="1440" alt="Graveyard Keeper 2 9_27_2026 3_33_02 PM" src="https://github.com/user-attachments/assets/36a624d8-143f-47a7-b3ed-67003eda30c4" />
<br/><br/>


- The building stays **the same object**: chest contents, fuel, running crafts, upgrades and everything that refers
  to it (orders, porters, quests) are untouched. Nothing is paid or refunded and no build or demolition scripts run.
- In move mode, movable buildings are tinted light blue (stronger under the cursor); buildings that can't be moved
  keep the game's red, and the keeper tells you why when you click one (zombie working in it, conveyor, grave,
  garden bed, villagers' building, other area).
- Same area only. Workbench extensions (tool racks etc.) re-link by position after a move, as when first placed.
- Settings: the Move row and its label, the modifier key, stay-in-move-mode, the highlight and its colour.
- Optional: with [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) installed, its settings
  (all except the colour) also appear in the game's Mods menu. The same DLL works without it.

### Owned Count

`GK2OwnedCount.dll` · GUID `dreas.gk2.ownedcount` · [source and README](src/GK2OwnedCount/)

Every item tooltip gets a line such as `Owned: 120 · on you 4 · in storage 116 (5 chests)`. "On you" is your
inventory, bags and tool belt; "in storage" is every storage chest in every area, the same set Global Storage draws
from. Graded items (the starred ones, e.g. Bones of different skull grades) also show `all grades N`.

- Display only: nothing in the game changes, nothing is written.
- Settings: own-tooltip only or also embedded items, breakdown on/off, a per-area second line, other grades,
  whether fuel containers and conveyor chests count as storage. A debug switch dumps every item definition
  (stack sizes, grades) to a CSV.
- Optional: with [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) installed, its settings
  also appear in the game's Mods menu. The same DLL works without it.

### Tech Search

`GK2TechSearch.dll` · GUID `dreas.gk2.techsearch` · [source and README](src/GK2TechSearch/)

A search box at the top right of the Tech Tree page. Type part of a tech's name, or of something it unlocks (an item,
building or perk on its card), and the matches are listed with their tab; Enter or a click switches to that tab,
scrolls to the tech and frames its card. Ctrl+F puts the cursor in the box.

- Hidden techs (the "?" cards) and tabs you haven't opened yet are never listed: no spoilers.
- While you type, the game's keys are paused (the same way the game's own bug report window does it), so letters
  don't open the map or the inventory.
- Display only: nothing in the game changes, nothing is written. Jumping is the game's own tab switch.
- Settings: on/off, search unlocks too, number of results, the key, the box's place and size.
- Optional: with [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) installed, its main settings
  also appear in the game's Mods menu. The same DLL works without it.

## Safety

- **No save-format changes.** The mods add no serialized types or fields. Saves made with them load without them.
- **Game-update guard.** Every plugin checks the module version ids of the game's code (`Assembly-CSharp.dll`) and
  engine library (`LazyBearTechnology.dll`) at startup and disables itself after
  a game update until it has been re-audited and rebuilt (`Safety.DisableOnGameUpdate`, on by default). You'll see
  `Game code changed since this mod was audited ... Mod is DISABLED` in the log.
- **All-or-nothing patching.** If any patch fails to apply, all of that mod's patches are removed and the game runs
  unmodified.
- **Move Building** is transactional: an interrupted move puts the building back exactly as it was, verified in-game
  with fault injection. It refuses to run in co-op.

## Game updates

After a game patch the mods disable themselves (see above) until the game's code has been compared against the
audited build and the mods rebuilt with the new module version ids (`tests/GameDiff` lists what changed; see
`tests/README.md`). Until then the game runs exactly as unmodified. Audited build: Steam 25676698 (game 1.008; `docs/game-update-25676698.md`).
Don't set `DisableOnGameUpdate=false` unless you accept running unaudited.
