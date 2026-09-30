# GK2 Mounts API: guide for mod authors

GK2 Mounts (`GK2Mounts.dll`, GUID `dreas.gk2.mounts`) is a library. It gives Graveyard Keeper 2 a rideable
donkey: ridden on its back (**Ride**), or pulling its cart (**Cart**), with cargo, a lantern and sounds. On its own it
does nothing: **your mod decides when the keeper may mount, what the ride can do and how it looks.** The showcase mod
(`src/GK2Showcase`) is the playable version for players and uses nothing but this API.

API version **0.5.0** (`Mounts.ApiVersion`). Everything public is in the namespace `GK2Mounts.Api`. The exact
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

`Mounts.Tuning(MountStyle.Ride / Cart)` holds a style's 96 look and sound values. `Mounts.Behaviour` holds the
behaviour switches, which apply to both styles, and `Mounts.Shuffle` how the idle shuffle steps. All are
`MountTuning`, addressed by the value's cfg key.

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
- `IdleShuffle` (0.7.0, off in the core): donkeys standing in the world **without a cart** take a few slow steps now
  and then, so they don't stand perfectly still. See "Idle shuffle" below.

While mounted the keeper always faces only left or right, like the donkey. That isn't a setting.

**Tuning keys per style.** Find the right values with the core's dev panel: set `Debug.TuningWindow = true` in
`dreas.gk2.mounts.cfg`. Every row there is one of these keys.

| Group | Keys (type) |
|---|---|
| Speed and seat | `SpeedMultiplier` (float 0.5–4), `Depth`, `CenterOffset`, `SeatOffset` (float); `RiderOffsetPx`, `TorsoOffsetPx`, `SeatAdjustPx` (int); `AutoSeat`, `Bob`, `SnapToGround` (bool) |
| Pose | `Pose` (`RiderPose`: Walk, Idle, FishingIdle, FocusedWalk); `FreezePoseWhenIdle`, `PoseSpeedFollowsDonkey` (bool); `PoseSpeed`, `IdleFidgetEverySeconds`, `IdleFidgetSpeed`, `IdleCrawlSpeed` (float) |
| Legs | `NearLeg` (bool), `LegSource` (`LegSource`: Body, Drawn), `LegHeightPx`, `LegAdjustXPx`, `LegAdjustYPx` (int) |
| Cart on slopes | `CartFollowsGround` (bool), `CartGroundMaxShift`, `CartPitchMaxDegrees` (float), `CartSlope` (`CartSlopeStyle`: HalfLift, ShaftsTilt, WholeCartPitch) |
| Cargo | `CargoDrawCorpse`, `CargoSwaysWithCart`, `CargoSetDownBehindCart` (bool); `CargoBedDepth`, `CargoSideGap`, `CargoOverhang`, `CargoNestle`, `CargoSetDownGap` (float) |
| Lantern pole | `LanternAlong`, `LanternUp`, `LanternAcross`, `LanternTilt`, `LanternLean`, `LanternSwayDegrees`, `LanternSwaySeconds`, `LanternScale` (float); `LanternShow`, `LanternFlip`, `LanternHangs` (bool) |
| Lantern light | `LanternLightOn`, `LanternLightShadows`, `LanternSelfLightOn`, `LanternGlowOn`, `LanternGlassOn` (bool); `LanternLightIntensity`, `LanternLightRange`, `LanternLightDayDim`, `LanternSelfLightIntensity`, `LanternSelfLightRange`, `LanternGlowAlpha`, `LanternGlowScale` (float); `LanternLightColor` (string, hex `RRGGBB`) |
| Sounds | `SoundsOn`, `HoofOn`, `HoofSurfaceAware`, `GrazeOn`, `BrayWhileWalking`, `BrayInSfxChannel`, `CargoThumpOn`, `WheelOn`, `WheelPitchFollowsSpeed`, `BellOnSetOff`, `BellOnlyWithCargo` (bool); `SoundVolume` and every `…Volume`, `…Pitch`, `HoofPitchVariation`, `HoofPhase`, `WheelPhase`, `BrayOnMountChance`, `BrayIdleEverySeconds`, `BellSetOffIdleSeconds` (float); `HoofStepsPerCycle`, `WheelEveryCycles` (int); `HoofSoundId`, `GrazeSoundId`, `BraySoundId`, `CargoThumpSoundId`, `WheelSoundId`, `BellSoundId` (string, the game's LazyAudio ids) |

## Colours

`Mounts.Colors` recolours the donkey, the cart and the lantern: one set for both styles, overridable per session like
tuning (same `MountTuning`, same rules: session only, cyan in the panel, `ClearTuning` clears it). How it works in
the game is in `docs/mounts-colors.md`.

```csharp
MountTuning colors = Mounts.Colors;
colors.Set("you.gk2.mymod", "CoatColorize", "3050C0");            // a blue donkey, its shading kept
colors.Set("you.gk2.mymod", "HarnessSwaps", "885923=C02020;D68E38=FF6060"); // two exact colours of the harness
colors.Set("you.gk2.mymod", "CartBodyHue", 40f);
foreach (MountColorRegion r in Mounts.ColorRegions)
{
    Logger.LogInfo($"{r.Key} ({r.Area}): {r.Name}, {r.SourceColors.Count} colours, e.g. key {r.KeyOf("Hue")}");
}
```

**Regions** (`Mounts.ColorRegions`, `Key` / `Name` / `Area` / `SourceColors`, the art's exact colours as hex):
- Donkey: `Coat`, `Dark` (outline, mane, hooves), `Muzzle` (muzzle and belly), `Eye`, `Harness`.
- Cart: `CartBody`, `Shafts`, `Straps`, `Hooks`, `Wheels` (3D rims and the wheel sprites), `Hubs`.
- Lantern: `Pole`, `Lamp`, `Glass` (the lamp's painted glass).

**Per region**, key = region key + setting (`MountColorRegion.SettingNames`, `KeyOf`):

| Setting | Type | Meaning |
|---|---|---|
| `Hue` | float −180..180 | hue shift, degrees |
| `Saturation`, `Brightness`, `Contrast` | float 0..3 | multipliers (1 = unchanged) |
| `Colorize` | string hex `RRGGBB`, `""` = off | this colour's hue and saturation, each pixel's own brightness |
| `ColorizeAmount` | float 0..1 | how much of `Colorize` |
| `Tint` | string hex, `""` = off | multiplied in |
| `Overlay` | string hex, `""` = off | a flat colour mixed over the result |
| `OverlayAmount` | float 0..1 | how much of `Overlay` |
| `Swaps` | string `SRC=DST;SRC=DST` | exact replacements of source colours; a swapped colour ignores the rest |

Order: a swap wins; else hue, saturation, brightness, contrast, colorize, tint, overlay.

**Globals:** `Enabled` (bool), `SpriteMethod` (`SpriteColorMethod.Palette` exact, or `Lut`, a fallback),
`GlowColor`, `LitGlassTint`, `CharacterLightColor` (hex, `""` = the game's). The lamp's main light colour is the
Cart tuning key `LanternLightColor`.

Changes show within a frame while mounted. A colour that isn't one of a region's `SourceColors` as a swap source
does nothing.

## Donkeys and carts in the world (0.6.0)

Besides the one summoned under the keeper (`TryMount`), the core keeps any number of donkeys and carts standing in
the world. Each one has its own settings. The design and every choice behind it are in
[`mounts-instances.md`](mounts-instances.md).

```csharp
MountHandle donkey = Mounts.SpawnDonkey("you.gk2.mymod", pos, withCart: false, facingRight: true);
MountHandle cart = Mounts.SpawnCart("you.gk2.mymod", pos2);
donkey.Tuning.Set("you.gk2.mymod", "SpeedMultiplier", 2.5f);     // this donkey only
donkey.Tuning.Set("you.gk2.mymod", "CoatColorize", "C040FF");    // any key of Tuning, Behaviour or Colors
Mounts.Hitch(donkey, cart);                                       // cart, cargo and cart settings join the donkey
MountHandle left = Mounts.Unhitch(donkey);                        // the cart stays standing, a free cart again
```

**Spawning and reading**

| Member | Does |
|---|---|
| `Mounts.SpawnDonkey(owner, position, withCart = false, facingRight = true)` | A standing donkey (with its cart) in the keeper's scene; the ground height is found under `position`. Null outside a game or while the core is off. |
| `Mounts.SpawnCart(owner, position, facingRight = true)` | A cart on its own: the donkey prefab with the donkey's renderers off, the shafts resting on the ground. |
| `Mounts.Instances` | Every one standing, in spawn order (not the `TryMount` summon). |
| `Mounts.Riding` | The ridden one, or null (on foot, or riding the `TryMount` summon). |

**Hitching**

| Member | Does |
|---|---|
| `Mounts.Hitch(donkey, cart)` | The donkey takes the free cart, which is gone afterwards. Its cargo and its cart keys come too. A standing donkey moves to the cart; a ridden one is put there with the keeper. |
| `Mounts.Unhitch(donkey)`, `Mounts.UnhitchRidden()` | The cart stays standing where it is, as a new free cart with the cargo and the cart keys; that cart is returned. The ride goes on in Ride style. |

**`MountHandle`**

| Member | Does |
|---|---|
| `Id`, `Owner`, `Exists`, `HasDonkey`, `HasCart`, `IsRidden` | Identity and state. |
| `SceneId`, `Position`, `FacingRight` | Where it stands. |
| `CargoIds` | What is on its cart. |
| `Tuning` | Its own settings (`InstanceTuning`: `Get`, `Set`, `Clear`, `Reset`, `IsSet`, `SetBy`, `Keys`). Keys are those of `Mounts.Tuning(style)`, `Mounts.Behaviour`, `Mounts.Shuffle` and `Mounts.Colors`; a style key applies in whichever style it is in. This donkey's value wins over everyone's (cfg and session overrides). |
| `Ride(showMessage)` | The keeper gets on and is put on the seat. The same checks and rules as `TryMount`. |
| `MoveTo(position, facingRight)` | Moves a standing one. |
| `IsReady` | Its view is loaded in the keeper's scene: `Ride` works. It takes a few frames after a spawn, `ComeToKeeper`, a hitch or an unhitch. |
| `ComeToKeeper(facingRight)` | It comes to the keeper, into his scene, to where a ride would put it, so `Ride` right after moves no one. The showcase calls its donkey this way. |
| `SetDownCargo()` | Everything on its cart is set down on the ground at the cart. Call it before `Remove` to keep the cargo. |
| `Remove()` | Gone, with anything on its cart; a ridden one is dismounted first. |

**Events:** `InstanceSpawned`, `InstanceRemoved`, `Hitched(donkey, oldCart)`, `Unhitched(donkey, newCart)`. The
ride's own `Mounted` and `Dismounted` come for instances too.

**Behaviour and limits**

- **Dismounting** an instance (any dismount, the showcase's key included) leaves it standing, cart and cargo
  included. `SwitchStyle` on a ridden instance: to Ride unhitches, and to Cart is refused (hitch a cart).
- **Carts:** the cargo goes onto the ridden cart only; a standing or free cart takes no pickups.
- **Keys that travel with the cart:** `Cart*`, `Cargo*`, `Lantern*`, the Cart and Lantern colour regions, and
  `GlowColor`, `LitGlassTint`, `CharacterLightColor`.
- **Saves:** instances aren't saved. At each save their cargo is set down at their cart (in the save) and taken
  back right after, so a reload finds the items where the cart stood. After a game restart or load, the mod that
  spawned them spawns them again; `Instances` gives everything a mod needs to keep its own record.
- **In-game controls:** `General.InstanceControls` (on) makes the interact key ride a standing donkey and hitch a
  free cart. `General.HitchKey` (J) unhitches, and hitches too.

## Idle shuffle (0.7.0)

A standing donkey without a cart takes a few slow steps now and then, inside a small box around its home spot.

- **Switching it on:**

  ```csharp
  Mounts.Behaviour.Set("you.gk2.mymod", "IdleShuffle", true);       // every standing donkey without a cart
  donkey.Tuning.Set("you.gk2.mymod", "IdleShuffle", true);           // or just this one (a MountHandle)
  donkey.Tuning.Set("you.gk2.mymod", "ShuffleAreaWidth", 0.6f);      // its own box, speed, pauses, ...
  ```

- **Who moves:** only a donkey with no cart, not ridden and not the `TryMount` summon. A donkey with a cart and a free
  cart never move. It starts when its cart is unhitched and stops when one is hitched.
- **Its home spot** is where it was spawned, left by a dismount, or last moved (`MoveTo`, `ComeToKeeper`). It never
  drifts away from it.
- **Never straight up or down:** every step is within `ShuffleMaxAngle` of the screen's horizontal, and the donkey
  always faces the way it walks. It turns around first when there is no room ahead, or by `ShuffleTurnChance`.
- **Ground:** each step checks for level ground and nothing in the way along its line. Nothing in the way means no
  keeper-blocking collider, the game's own test for where the keeper can stand.
- **It holds still** while the keeper is within `ShuffleKeeperDistance`, so riding it doesn't chase a moving target,
  and while the game is paused.
- **No game events:** the core moves the donkey itself. The game's `MovementComponent` isn't used, because it fires a
  global "go-to finished" event under the definition id `npc_donkey`, which quests listen for.
- **`MountHandle.IsStepping`** is true during a step.

**`Mounts.Shuffle` keys** (section "Shuffle", all float): `ShuffleAreaWidth` (1, sideways either way),
`ShuffleAreaDepth` (0.5, up/down either way), `ShuffleStepMin` / `ShuffleStepMax` (0.3 / 0.9), `ShuffleSpeed` (0.5
units/s), `ShuffleAnimSpeed` (1 = the walk cycle matches the ground), `ShufflePauseMin` / `ShufflePauseMax` (5 / 15
s), `ShuffleMaxAngle` (35°), `ShuffleTurnChance` (0.25), `ShuffleKeeperDistance` (2).

## Foals and spawning with every property (0.8.0)

```csharp
MountHandle mother = Mounts.SpawnDonkey("you.gk2.mymod", at);
MountHandle foal = Mounts.SpawnFoal("you.gk2.mymod", mother);        // size 0.6, not rideable, shuffles and follows
MountHandle big = Mounts.Spawn("you.gk2.mymod", new MountSpawn { Position = at2, Scale = 1.4f, Rideable = false });
```

- **`MountSpawn` fields:**
  - `Position`, `FacingRight`, `Donkey` (false: a free cart), `WithCart`.
  - `Scale` (0.2–2).
  - `Rideable`.
  - `Parent`, and `RemoveWithParent` (default true).
- **Scale other than 1:** only for a donkey without a cart that can't be ridden. The ride and the cart are made for a
  full-size donkey.
- **A not-rideable instance** has no interact prompt, and `Ride` and `Hitch` refuse it.
- **A child's idle-shuffle home is its parent.**
  - While the way is clear it trots after the parent (`ShuffleFollowSpeed`), within `ShuffleFollowMaxDistance`.
  - Otherwise it waits, shuffling where it is, and catches up out of sight once the parent stands again (into the
    parent's scene too).
  - No chains: a child can't be a parent. Up to 6 children per parent.
  - The child's shuffle must be on for it: `SpawnFoal` switches it on for the foal only.
- **`SpawnFoal`** also gives the foal its own shuffle settings (smaller, quicker steps, a wider area around the
  parent), owned by you and changeable through `MountHandle.Tuning`.
- **`Mounts.ClearTuning(yourId)`** also clears the foal's own `IdleShuffle` set by your `SpawnFoal`. The foal then
  stands still; set it again if you clear your tuning mid-session.
- **On `MountHandle`:** `Scale`, `IsRideable`, `Parent`, `Children`. `IsStepping` is also true while trotting after
  the parent.

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
- 0.5.0: colours (`Mounts.Colors`, `Mounts.ColorRegions`, `MountColorRegion`, `SpriteColorMethod`); the style keys `CargoSetDownBehindCart`, `CargoSetDownGap`.
- 0.6.0: donkeys and carts in the world (`SpawnDonkey`, `SpawnCart`, `Instances`, `Riding`, `Hitch`, `Unhitch`, `UnhitchRidden`, `MountHandle`, `InstanceTuning`, and the events `InstanceSpawned`, `InstanceRemoved`, `Hitched`, `Unhitched`); `SwitchStyle` on a ridden instance unhitches or refuses.
- 0.7.0: the idle shuffle (`Mounts.Behaviour` key `IdleShuffle`, `Mounts.Shuffle`, `MountHandle.IsStepping`).
- 0.8.0: `Mounts.Spawn(ownerId, MountSpawn)`, `Mounts.SpawnFoal`, `MountSpawn`, `MountHandle.Scale`, `IsRideable`,
  `Parent`, `Children`; the `Mounts.Shuffle` keys `ShuffleFollowSpeed`, `ShuffleFollowMaxDistance`, `ShuffleFollowGap` (how close to the parent it settles, 0.9), `ShuffleFollowAlways` (bool: follow however far, stopping only when blocked).
