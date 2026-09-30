# GK2 Mounts API: guide for mod authors

GK2 Mounts (`GK2Mounts.dll`, GUID `dreas.gk2.mounts`) is a library. It gives Graveyard Keeper 2 a rideable
donkey: ridden on its back (**Ride**), or pulling its cart (**Cart**), with cargo, a lantern and sounds. On its own it
does nothing: **your mod decides when the keeper may mount, what the ride can do and how it looks.** The showcase mod
(`src/GK2Showcase`) is the playable version for players and uses nothing but this API.

API version **0.4.0** (`Mounts.ApiVersion`). Everything public is in the namespace `GK2Mounts.Api`. The exact
surface is `tests/ApiSurface/GK2Mounts.Api.txt`.

## Setup

1. Reference `GK2Mounts.dll` (from the release zip, or the installed `BepInEx/plugins/GK2Mounts.dll`) with
   `Private=false`, so you never ship a copy of it.
2. Declare the dependency, then check the API version at startup:

```csharp
[BepInPlugin("you.gk2.mymod", "My Mod", "1.0.0")]
[BepInDependency("dreas.gk2.mounts", "0.1.0")]           // hard: your mod needs the core
public class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        if (Mounts.ApiVersion < new Version(0, 4, 0)) { Logger.LogError("GK2 Mounts too old."); return; }
        Mounts.AddMountRule("you.gk2.mymod", style => /* ... */ null);
    }
}
```

If the mount is only an extra for your mod, use a soft dependency instead, and only touch `GK2Mounts.Api` from
code that runs when the plugin is present (`Chainloader.PluginInfos.ContainsKey("dreas.gk2.mounts")`). The pattern
is in `docs/gk2-mod-framework.md`.

**Keys:** if you read a BepInEx `KeyboardShortcut` for a gameplay key, don't use `KeyboardShortcut.IsDown()`. It
also requires that no other key at all is held, so it never fires while the player walks (W/A/S/D). Check
`UnityInput.Current.GetKeyDown(shortcut.MainKey)` plus `GetKey` for each of `shortcut.Modifiers`, as the showcase
does.

**Rules of the road:**
- **Main thread only:** call from Update, from event handlers, and from Harmony patches of game code.
- **Owner id:** pass your plugin GUID wherever it's asked for. It's how the core tells mods apart in rules, rides,
  overrides and the log.
- **You can add restrictions but never remove the core's.** Co-op, indoors, "busy" (work, fishing, combat,
  cutscenes), build mode, the save hand-back and the mount-object check always apply. `TryMount` returns their
  reason like any other.
- `Mounts.IsAvailable` is false when the player switched the core off (`General.Enabled`) or it disabled itself
  after a game update. Every call then refuses or does nothing; nothing throws.

## Riding

| Call | What it does |
|---|---|
| `MountResult TryMount(owner, style, showMessage = true)` | Summons the donkey under the keeper. `result.Ok`, or `result.Reason`, which is the text of the keeper's thought bubble; `showMessage: false` keeps it quiet. |
| `void Dismount(reason = null)` | Ends the ride. Cargo goes back to the keeper's hands, or to the ground if his hands are full. |
| `MountResult SwitchStyle(style, showMessage = true)` | Remounts in place in the other style. The new style's rules are asked first, and if one refuses, the ride goes on unchanged. |
| `IsMounted`, `Style`, `Owner` | State. `Owner` is the id of the mod that started the ride, kept across style switches. |
| `RingBell()`, `Bray()` | Sounds, subject to the `Sounds` and `Bell` features. |

**While mounted, the keeper's hands stay empty.** Only the cart carries:
- in Cart style, pickups land on the cart (2 slots);
- in Ride style, or with `Cargo` switched off, the game refuses pickups, and ground items show no outline;
- whatever he holds when mounting goes onto the cart, or is set down first.

Reason: the riding pose can't show a held item (see `src/GK2Mounts/PROBE-FINDINGS.md`, Round 14).

## Rules

Every rule is a function the core asks when the answer matters, so read the game's state inside it. You don't need
to track changes.

| Call | Asked | Return |
|---|---|---|
| `AddMountRule(owner, Func<MountStyle, string>)` | before every mount and style switch | `null` to allow, or the refusal text |
| `AddStayRule(owner, Func<string>)` | about twice a second while riding | `null` to keep riding, or the text shown as the ride ends |
| `AddFeatureRule(owner, MountFeature, Func<bool>)` | at mount, about twice a second while riding, on `RefreshRules()` | `false` switches that feature off |

- Every mod's rules must allow; one refusal wins.
- A rule that throws is logged once and treated as allowing.
- Each call returns an `IDisposable`: dispose it to remove the rule. `RemoveRules(owner)` removes all of yours.
- `RefreshRules()` asks the feature and stay rules now, e.g. right after your mod unlocked something.
- `IsFeatureAllowed(feature)` gives the combined answer of all rules.

**Features:**

| Feature | When off |
|---|---|
| `Cargo` | The cart carries nothing. Switching it off mid-ride puts the cargo on the ground. |
| `Lantern` | No lantern pole or lamp on the cart. |
| `LanternLight` | The lamp is dark: no lights, glow or lit glass. |
| `Sounds` | No mount sounds (hoofs, wheels, brays, graze, bell, cargo thump). |
| `Bell` | No bell (on setting off, or `RingBell`). |
| `StayThroughDoors` | The ride ends at a door that loads another scene. A house door isn't a scene change. |

To keep the player out of the cart style altogether, there's no feature; use a mount rule, which can say why:
`AddMountRule(id, s => s == MountStyle.Cart && !unlocked ? "No cart yet." : null)`.

## Events

- `Mounted(MountStyle)`: after the donkey is summoned, and after a style switch.
- `Dismounted(string reason)`: after the ride ended, with the cargo already handed back. Reasons include
  `"dismounted"`, `"style change"`, `"indoors"` and a stay rule's text.
- `CargoLoaded(string itemId)` and `CargoUnloaded(string itemId)`: an item went onto the cart or left it (e.g.
  `"wood"`, `"body_corpse"`). Nothing is reported for the core's own bookkeeping, such as a door re-summon or the
  hand-back around a save.

A handler that throws is logged and skipped.

## Tuning and behaviour

`Mounts.Tuning(MountStyle.Ride / Cart)` holds a style's 94 look and sound values. `Mounts.Behaviour` holds the four
behaviour switches, which apply to both styles. Both are `MountTuning`, addressed by the value's cfg key.

```csharp
MountTuning cart = Mounts.Tuning(MountStyle.Cart);
cart.Set("you.gk2.mymod", "SpeedMultiplier", 2.5f);   // returns the value applied, clamped to the allowed range
cart.Set("you.gk2.mymod", "Pose", RiderPose.Walk);
float now = cart.Get<float>("SpeedMultiplier");       // the value in effect
Mounts.Behaviour.Set("you.gk2.mymod", "DismountIndoors", false);
Mounts.ClearTuning("you.gk2.mymod");                  // all your overrides, every section
```

- **Session only:** a `Set` overrides the player's value until `Clear(key)`, `Reset()`, `ClearTuning(owner)` or the
  end of the session. It's never written to the player's cfg.
- **Type-checked:** a wrong type throws `ArgumentException`, naming the expected type.
- **Visible in the dev panel:** an overridden row is cyan and locked, and shows the value in effect.
- **When it applies:** while mounted in that style, most values apply at once. Values the donkey is built with (the
  cart and lantern geometry measured at summon) apply from the next mount or style switch.
- **Discovering keys:** `Keys`, `Has(key)`, `TypeOf(key)`, `IsOverridden(key)` and `OverriddenBy(key)`.

**Behaviour** (`Mounts.Behaviour`, section "General"):
- `StayMountedThroughDoors`
- `DismountIndoors`
- `ShowMessages`

While mounted the keeper always faces only left or right, like the donkey. That isn't a setting.

**Tuning keys per style.** Find the right values with the core's dev panel: set `Debug.TuningWindow = true` in
`dreas.gk2.mounts.cfg`. Every row there is one of these keys.

| Group | Keys (type) |
|---|---|
| Speed and seat | `SpeedMultiplier` (float 0.5–4), `Depth`, `CenterOffset`, `SeatOffset` (float); `RiderOffsetPx`, `TorsoOffsetPx`, `SeatAdjustPx` (int); `AutoSeat`, `Bob`, `SnapToGround` (bool) |
| Pose | `Pose` (`RiderPose`: Walk, Idle, FishingIdle, FocusedWalk); `FreezePoseWhenIdle`, `PoseSpeedFollowsDonkey` (bool); `PoseSpeed`, `IdleFidgetEverySeconds`, `IdleFidgetSpeed`, `IdleCrawlSpeed` (float) |
| Legs | `NearLeg` (bool), `LegSource` (`LegSource`: Body, Drawn), `LegHeightPx`, `LegAdjustXPx`, `LegAdjustYPx` (int) |
| Cart on slopes | `CartFollowsGround` (bool), `CartGroundMaxShift`, `CartPitchMaxDegrees` (float), `CartSlope` (`CartSlopeStyle`: HalfLift, ShaftsTilt, WholeCartPitch) |
| Cargo | `CargoDrawCorpse`, `CargoSwaysWithCart` (bool); `CargoBedDepth`, `CargoSideGap`, `CargoOverhang`, `CargoNestle` (float) |
| Lantern pole | `LanternAlong`, `LanternUp`, `LanternAcross`, `LanternTilt`, `LanternLean`, `LanternSwayDegrees`, `LanternSwaySeconds`, `LanternScale` (float); `LanternShow`, `LanternFlip`, `LanternHangs` (bool) |
| Lantern light | `LanternLightOn`, `LanternLightShadows`, `LanternSelfLightOn`, `LanternGlowOn`, `LanternGlassOn` (bool); `LanternLightIntensity`, `LanternLightRange`, `LanternLightDayDim`, `LanternSelfLightIntensity`, `LanternSelfLightRange`, `LanternGlowAlpha`, `LanternGlowScale` (float); `LanternLightColor` (string, hex `RRGGBB`) |
| Sounds | `SoundsOn`, `HoofOn`, `HoofSurfaceAware`, `GrazeOn`, `BrayWhileWalking`, `BrayInSfxChannel`, `CargoThumpOn`, `WheelOn`, `WheelPitchFollowsSpeed`, `BellOnSetOff`, `BellOnlyWithCargo` (bool); `SoundVolume` and every `…Volume`, `…Pitch`, `HoofPitchVariation`, `HoofPhase`, `WheelPhase`, `BrayOnMountChance`, `BrayIdleEverySeconds`, `BellSetOffIdleSeconds` (float); `HoofStepsPerCycle`, `WheelEveryCycles` (int); `HoofSoundId`, `GrazeSoundId`, `BraySoundId`, `CargoThumpSoundId`, `WheelSoundId`, `BellSoundId` (string, the game's LazyAudio ids) |

## Gating on the game's progress

All of this is the game's own state, reached through `MainGame.Instance.GameSave` (reference `Assembly-CSharp.dll`).
Content ids are data, listed at runtime with `GameBalance.Me.techDefs`, `questDefs`, `buildingDefs`, `craftDefs` and
so on. The full survey is in `docs/mounts-api-design.md`.

| Progress | Read it in a rule | React at once (optional, then `Mounts.RefreshRules()`) |
|---|---|---|
| Tech tree | `GameSave.knowledgeSystem.IsTechUnlocked("fishing_3")` | static `KnowledgeSystem.OnTechUnlocked += (id, silent) => ...` |
| Quests | `GameSave.questSystemData.IsQuestInStatus(id, QuestStatus.Completed)` | per save: `questSystemData.OnQuestCompleted` |
| Story flags | `MainGame.PlayerData.GetRes("donkey_ready") > 0f` | per save: `PlayerData.OnGameResChanged` |
| Buildings, crafts | `knowledgeSystem.unlockedBuildings.Contains(id)` / `unlockedCrafts` | none |
| Days played | `GameSave.environmentData.Day` | static `EnvironmentEngine.OnNewDayStarted` |

- **Before a save is loaded,** `GameSave` is null or empty, so a rule that reads it should treat that as "not yet"
  (as the example does). Rules are asked again at mount time.
- **Per-save events:** the save object is replaced on every load, so subscribe to its events again in
  `MainGame.OnGameStarted`. The static events are subscribed once.
- **Never evaluate the game's expression strings (`LazyExpression`) as conditions.** The same engine can unlock
  techs, start quests and set flags, so evaluating one can change the save.
- **The story's own donkey** probably arrives with the `21_base_donkey_*` quests, and the flag `donkey_ready` is set
  by its script. Both come from scans of the game data; confirm them by listing `GameBalance.Me.questDefs` in your
  game before relying on them.

## Examples in this repo

- **`examples/GK2MountsTechGate`:** the cart needs a tech (a mount rule with a reason) and the lamp's light needs a
  tech (a feature rule). It refreshes on `OnTechUnlocked` and logs your unlocked techs at every game start.
- **`src/GK2Showcase`:** the playable mount. It owns the keys (ride H, cart G, bell) and hands its behaviour
  settings to the core with `Mounts.Behaviour`.
- **`tools/GK2MountsApiProbe`:** a dev test of every part of the API on F-keys: F6 mount, F7 switch style, F8 tuning,
  F9 a blocking rule, F10 the features one by one.

## Versioning

- `ApiVersion` is separate from the mod version.
- New members bump the minor version, and a change to existing members bumps the major.
- **Before 1.0**, a breaking change bumps the minor, as semver allows for 0.x, and it's listed here:
  - **0.4.0:** `Mounts.BuiltInKeyOwner` was removed (the core has no keys), and `MountTuning.Style` became
    `MountStyle?` (null for `Behaviour`).
- `tests/ApiSurface` fails on any change to the public surface, so every change is a reviewed diff.

History:
- 0.1.0: mount, dismount, switch style, and the mount and stay rules.
- 0.2.0: features and cargo events.
- 0.3.0: tuning.
- 0.4.0: behaviour, and the core without keys.
