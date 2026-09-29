# Mounts as an API: design investigation

Status: **proposal**, 2026-09-30. Nothing here is built yet. Open questions are at the end.

## Goal (the user's plan)

Split today's single GK2 Mounts plugin into two mods:
1. **GK2 Mounts (core):** a library that other mod authors build on. It offers everything the tuning panel can do
   today, but controlled from code. Authors can then, for example:
   - tie the mount into the story;
   - allow the cart only after a tech;
   - allow mounting only after a quest.
2. **A showcase mod:** a playable mount built on the core, like today's mod but more streamlined. Its first
   version has one key that summons the donkey to ride, and a second key that summons it with its cart.

Decisions made (2026-09-30):
- **The core alone does nothing visible:** no key, no donkey, no menu entry for players.
- **The API is C# only.** Authors write a BepInEx plugin that references `GK2Mounts.dll`. There are no rule or
  data files.
- **Donkey only for now.** Names stay generic where that's cheap (`Mount`, not `Donkey`), so another mount could be
  added later.

## What exists today

`src/GK2Mounts/`: 17 files, about 9,500 lines, 76 settings. The pieces that matter for the split:

| Piece | Where | What it does |
|---|---|---|
| Mount / dismount | `Mount.cs` (`Tick`, `TryMount`, `Dismount`, `WhyCannotMount`, `WhyMustDismount`) | summons a temporary `npc_donkey` view under the keeper, follows him, and handles doors and scene changes |
| Input | `Mount.Tick` reads `General.MountKey`; `General.BellKey` | the **only** place a key starts anything |
| Two styles | `MountStyle.Ride` / `Cart`, each with its own `StyleSettings` set | seat, pose, legs, speed, cart slope, cargo packing, lantern, sounds |
| Cargo | `Cargo.cs`, `CargoLayout.cs`, `HeadReroute.cs` (7 patches) | the cart holds carried items, pickups go onto it, and they're handed back before every save |
| Lantern | `Lantern.cs`, `LampHang.cs` | pole pose, the hanging lamp, lights, glow |
| Sounds | `MountSounds.cs` (2 patches) | hoofsteps, graze, brays, wheels, bell, cargo thump |
| Safety | `WhyCannotMount`, the `SaveSystem.Save` prefix and postfix, the MVID guard | co-op, indoors, saves, the side-effect guard on `MountWgoId` |
| Tuning panel | `TuningWindow.cs` (1,000 lines), `Defaults.cs` | a dev tool: live sliders, defaults written back to the repo |
| Probes and dumps | `CarryProbe`, `HeadProbe`, `LanternProbe`, `ScriptDump`, `CargoSheet` | investigation tools; not gameplay |

The two refusal functions already return a **reason text**, which the thought bubble shows: "Not indoors.",
"I'm busy.", "Not while building.", and so on. An author's rule plugs in at exactly that point, so "You haven't
learned to drive a cart yet" appears the same way as the game's own reasons.

## Proposed split

### Core: `GK2Mounts.dll`, GUID `dreas.gk2.mounts` (unchanged)

Keeps everything that makes a mount work and be safe:
- mounting and dismounting, following, doors, styles, cart, cargo and cart-as-hands, lantern, sounds;
- all the art calibration (the 76 tuned values) and its defaults, which are ours and measured against the donkey's
  art;
- every safety rule. Authors can **add** restrictions but never remove the core's: co-op, indoors, the save
  hand-back, and the `MountWgoId` side-effect guard stay;
- the patches, as today. They're always installed and do nothing unless someone is mounted, which is already how
  they're written.

Loses:
- `General.MountKey` and `General.BellKey` (they move to the showcase);
- the "press H" description texts.

Nothing happens until a mod calls `TryMount`.

It **keeps `General.Enabled`** as the player's kill switch, for when a mod built on it misbehaves.
`Mounts.IsAvailable` is false when that switch is off **or** the game-update guard has disabled the core.

**API tuning never writes the player's cfg.** Today every tuning value is a BepInEx `ConfigEntry`, and BepInEx
saves on every change. If `Mounts.Tuning(Cart).SpeedMultiplier = 2f` wrote that entry, a consumer mod's values would
land in `dreas.gk2.mounts.cfg` and stay there after the mod is uninstalled, and two mods would churn the file.
- So API tuning is a **session-only override layer** on top of the core's values. The core's values are its
  defaults plus whatever the dev panel saved.
- Only the dev panel writes the cfg.
- `Reset()` drops the overrides.
- The cfg keeps holding our calibration, not per-mod gameplay choices.

### Public API (namespace `GK2Mounts.Api`; everything else stays internal)

A first sketch, to be refined when building it:

```csharp
public static class Mounts
{
    public static Version ApiVersion { get; }          // separate from the mod version; semver
    public static bool IsAvailable { get; }            // false when the game-update guard disabled the core

    // state
    public static bool IsMounted { get; }
    public static MountStyle Style { get; }            // Ride / Cart
    public static string Owner { get; }                // id of the mod that started the current ride

    // actions (all go through the core's own safety checks)
    public static MountResult TryMount(string ownerId, MountStyle style); // result: ok, or the refusal reason
    public static void Dismount(string reason = null);
    public static MountResult SwitchStyle(MountStyle style);             // remount in the other style
    public static void RingBell();  public static void Bray();
    public static void SetDownLastCargo();  public static void UnloadCargo();

    // rules added by any mod; all must allow (a single refusal wins, and its text is shown)
    public static IDisposable AddMountRule(string ownerId, Func<MountStyle, string> whyNot);   // null = allowed
    public static IDisposable AddStayRule(string ownerId, Func<string> whyDismount);           // polled ~2x/s while riding
    public static IDisposable AddFeatureRule(string ownerId, MountFeature feature, Func<bool> allowed);
    public static void RefreshRules();                                                          // re-check features now

    // tuning: the same values as the panel, typed, per style; with Reset() to the mod defaults
    public static MountTuning Tuning(MountStyle style);

    // events
    public static event Action<MountStyle> Mounted;
    public static event Action<string> Dismounted;          // reason
    public static event Action<string> CargoLoaded;         // item id
    public static event Action<string> CargoUnloaded;
    public static event Action BellRung;
}

public enum MountFeature { Cart, Cargo, PickupsToCart, Lantern, LanternLight, Sounds, StayThroughDoors, Bell }
```

**How several mods share it:**
- **Rules** combine with AND, and each carries its owner id, so the log and panel can say who refused.
- **Rides:** the mod that started the ride is its `Owner`. Any mod may dismount it; that's a deliberate, simple
  rule.
- **Tuning** is last-writer-wins. Mods setting it are expected to own the whole ride (the showcase) or to set
  single values on purpose.

**Why rules are functions, not flags:** gating on the game's progress is read at the moment it matters (at mount
time, and periodically while riding). So an author never has to track when a tech gets unlocked. `RefreshRules()` is
for authors who want an immediate change, e.g. from `KnowledgeSystem.OnTechUnlocked`.

### Showcase mod: name to be decided, e.g. `GK2 Donkey` / `dreas.gk2.donkey`

- It has a hard `BepInDependency` on `dreas.gk2.mounts` with a minimum version.
  - A player who installs only the showcase gets BepInEx's "Could not load … missing dependencies" error. Unlike the
    framework case, that's the right outcome: the showcase can't work without the core.
  - The two DLLs ship in **one zip** so the error doesn't happen in practice.
- Version 1:
  - `RideKey` (default H): summons the donkey to ride, or dismounts;
  - `CartKey` (default G): summons the donkey with its cart, or dismounts;
  - the other key while mounted switches style;
  - `BellKey`;
  - `ShowMessages`.
- The player-facing settings (`StayMountedThroughDoors`, `DismountIndoors`, `SideViewOnly`, speed) become its
  settings, passed to the core through the API.
- It's the natural place for the GK2 Mod Framework integration: a Status row, the keys and the switches. The core
  has nothing for players to see.
- Later versions (the parked ideas): earn the donkey, the cart as an upgrade, summoning from a place, upkeep. Each
  is just a rule it adds through the API.

## Gating on game progress: what the game offers

This comes from a survey of the decompiled source, build 25601286. Everything is reached from
`MainGame.Instance.GameSave` (saved), and all content ids are data (a `GameBalance` ScriptableObject), listed at
runtime with `GameBalance.Me.GetDataCollection<T>()` or `GameBalance.Me.techDefs` and the like.

| Progress | Query | Change event | Example ids |
|---|---|---|---|
| **Tech tree** | `GameSave.knowledgeSystem.IsTechUnlocked(id)` | static `KnowledgeSystem.OnTechUnlocked(id, silent)` (fires before the tech's rewards are applied) | `fishing_3`, `resurrection_advanced`, `wood_basic` |
| **Quests** | `GameSave.questSystemData.IsQuestInStatus(id, QuestStatus.Completed)` | per save: `OnQuestStarted` / `OnQuestCompleted` / `OnQuestCanceled` (re-subscribe after each load) | `6_intro_guards_burial`, `92_doctor_cure_tell` |
| **Unlocked buildings / crafts** | `knowledgeSystem.unlockedBuildings` / `unlockedCrafts`, `.Contains(id)` | none (Harmony postfix on `UnlockBuilding` / `UnlockCraft`) | listed at runtime |
| **Story flags** (the scripts' `$name`) | `MainGame.PlayerData.GetRes(name)` | `PlayerData.OnGameResChanged` (no arguments; per save) | `donkey_ready`, `global_lock_on_drop_bodies`, `tech_red` |
| **World flags** (`%name`) | `WorldData.GetGameRes(id)` | none | |
| **Talents / inspirations** | `TalentSystemCache.Instance.inspirations[id].curLevel` | per save: `talentSystemData.OnInspiration*` | |
| **Days played** | `GameSave.environmentData.Day` | static `EnvironmentEngine.OnNewDayStarted(day)` | |
| Perks | `perkSystemData.HasPerk(id)` | per save: `OnPerkAdded` / `OnPerkRemoved` | mostly timed buffs; poor as gates |
| Achievements | `AchievementsSystem.Instance.IsCompleted(id)` | none | coarse only |

- **Save load:** `MainGame.OnGameStarted` (static) fires after every load or new game. The whole `GameSave` is
  replaced, so per-save events must be subscribed again there.
- **"Milestones"** in the game's code are fast-travel teleport points, not a progression system.
- **The game's own donkey:**
  - It's the story's corpse-delivery donkey (`npc_donkey`, driven by its flow script), unlocked by what are
    probably the `21_base_donkey_*` quests (`21_base_donkey_nun`, `21_base_donkey_complete`, …).
  - The flag `donkey_ready` is the one a third-party mod checks.
  - Those ids come from binary scans and are unconfirmed. Before relying on them, list
    `GameBalance.Me.questDefs` for "donkey" at runtime.
  - A natural first story gate for the showcase would be "the donkey has joined you".

**Optional convenience helpers** in the API are thin wrappers over the queries above, so authors don't need to know
the save layout:

```csharp
MountConditions.TechUnlocked("fishing_3");   MountConditions.QuestCompleted("21_base_donkey_complete");
MountConditions.PlayerFlag("donkey_ready", atLeast: 1);   MountConditions.DaysPlayed(atLeast: 5);
```

**Not offered: evaluating the game's expression strings** (`LazyExpression`). The engine can **change** the game:
`AddTech`, `StartQuest`, `SetPPar` and assignments are all expression functions. So a "condition" string could
alter the save, which is the reason Built Count never evaluates one.

## Keeping the API stable

- Only `GK2Mounts.Api` is public. `ApiVersion` follows semver: new members bump the minor version, and a change to
  existing members bumps the major version.
- Authors declare `[BepInDependency("dreas.gk2.mounts", "1.0.0")]`, or a soft dependency plus `Mounts.IsAvailable`
  if the mount is optional for them.
- A new test (`tests/ApiSurface`) writes the public API with Mono.Cecil and compares it with a committed snapshot.
  Any change shows up as a diff to review, so a break can't slip out unnoticed.
- The game-update guard still applies. After a game update, `IsAvailable` is false and `TryMount` returns a
  refusal, so consumer mods don't crash.
- The same single-DLL rules as the other mods (`PatchClasses`, no `Start()`). Any future framework link goes in
  the showcase.

## Suggested order of work

1. **An in-game baseline of today's mod**, from the open test list: sounds, save safety, the lantern at night, the
   river dump with two items. The split must not change behaviour, so we need to know what "working" looks like
   first.
2. **The API facade inside the current DLL.** `Mount.Tick`'s key handling becomes a call to `Mounts.TryMount`, and
   the refusal functions consult the rule lists. Behaviour is unchanged, and it's tested in game.
   - **Built and tested in game (2026-09-30):**
     - `src/GK2Mounts/Api/Mounts.cs`: `MountStyle` (moved here), `MountResult`, and `Mounts` with `ApiVersion` 0.1.0,
       `IsAvailable`, `IsMounted`, `Style`, `Owner`, `TryMount`, `Dismount`, `SwitchStyle`, `RingBell`, `Bray`,
       mount and stay rules, and the `Mounted` / `Dismounted` events.
     - The H key and the panel's style button go through the API.
     - The rules are asked after the core's checks. A rule that throws allows, and is logged once.
     - Stay rules are polled at most twice a second, and their text is said when they end a ride.
     - Test tools:
       - `tests/ApiSurface` holds the API snapshot;
       - `tools/GK2MountsApiProbe` (dev only, never shipped) uses the API like an author's mod: F6 mounts or
         dismounts with the probe as owner, F7 switches style, F9 toggles a refusal rule.
   - **In-game result (read from `Player.log`), all as expected:**
     - Every event carried the right owner: `dreas.gk2.mounts.key` for H, `dreas.gk2.mountsapiprobe` for F6, and the
       owner was kept across the F7 and panel style switches.
     - The reasons were right: `dismounted`, `style change`, `indoors`, and the stay rule's text.
     - F9 refused mounting ("The API probe says no.") and ended a ride within half a second.
     - Cargo on and off worked as before.
     - No warnings. The only exception was the game's own, at shutdown (`UICraftHintWidget.Hide` during
       `GameScene.OnDestroy`, with no mod frames in the stack).
3. **Feature rules wired in:** cart, cargo, pickups to cart, lantern, sounds, doors and bell each check their rule.
4. **Tuning through `MountTuning`:** typed values that read the session override first, then the `StyleSettings`
   entry. Setting one from the API writes only the override. The panel edits the entries, as today.
5. **The showcase project:** keys, player settings and the framework link move there. The core loses its keys.
   In-game test with the two keys.
6. **API docs** (`docs/mounts-api.md`, with a small example plugin: "cart only after tech X") and the `ApiSurface`
   test.
7. **Release both:** core and showcase in `dist/`, and a root README section for players (install both) and for
   authors (reference the core).

## Answered (2026-09-30)

- **The animal:** the donkey ("horse" was a slip).
- **The tuning panel:** stays in the **core as a dev tool** for authors, off by default and switched on in the cfg.
  Before release, the "Make these the mod defaults" button and the baked repo path have to go.
- **Showcase name:** not decided yet; the user is still thinking of one. `GK2Showcase` is a placeholder until then.
- **Showcase keys:** ride on **H**, cart on **G**.
  - Checked against the game's default keyboard bindings, stored in the settings (the registry value `settings`,
    `defaultKeyboardKeybindings`): W A S D, Space, Esc, Tab, E, F, I, M, P, Q, R, T, 1–4, the arrow keys, Left Shift
    and the mouse buttons.
  - G and H are unbound. The user's current bindings are the same as the defaults.
  - F1–F12 can't be bound at all (`UIGameBindingElement.IsForbiddenKey`).

## Open questions

1. **Where the tuning panel lives:** answered above; kept here for the reasoning.
   - in the core as a dev tool for authors, off by default and switched on in the cfg (my suggestion);
   - in its own dev plugin;
   - in the showcase.

   Wherever it goes, it needs work before it can ship:
   - Its "Make these the mod defaults" button writes into the repo, through the source path baked into the build
     (`GK2MountsSourceDir`).
   - A released panel must drop that button and the baked path. Both are already on the Mounts release checklist.
   - The release panel would then only save to the player's cfg.
2. **Tuning values for players:** should the showcase expose any of the 76 tuning values to players, or only
   gameplay settings (keys, doors, indoors, side view, speed)?
3. **The showcase name:** to be decided by the user.
