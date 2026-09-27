# GK2 Mounts (in design)

Goal: a rideable mount for Graveyard Keeper 2. The game has no mount concept of its own; see
[`FEASIBILITY.md`](FEASIBILITY.md) for what exists, how the player and NPC visuals work, the proposed design
(temporary donkey view glued to the player + speed multiplier + raised rider) and the risk assessment.

Status log:

- **2026-09-27** Feasibility study done. Probe plugin written (`Probe/`) to collect the runtime facts the design
  still needs. No mount plugin yet.
- **2026-09-27** Probe round 1 ran (report archived in `Probe/`). Findings in [`PROBE-FINDINGS.md`](PROBE-FINDINGS.md),
  including one design change: the donkey's attached flow script starts for every new `WgoData` and must be
  suppressed for the temp mount. Hotkey experiments still to run (round 2).

## Probe (`Probe/GK2MountProbe.dll`)

A development-only BepInEx plugin. It is not part of `dist/` and is not meant for players. It never changes saved
data: everything it spawns is a temp object exactly like the game's build preview, and the definition's attached
flow script is suppressed for those spawns (see the findings for why).

What it does after a save is loaded (about 6 s later, once per load; `F8` repeats it):

1. Writes `BepInEx\GK2-mount-probe.txt` with: camera setup; the player's controller, physics body, view and
   animation component fields (including private serialized ones), the player's full GameObject hierarchy with
   every component, sprite renderer (sprite, sorting layer/order, shader), collider and Rigidbody setting, the
   player Animator (clips, parameters, layers); the `WGODef` of `npc_donkey`, `cow` and `npc_donkey_test`; every
   existing instance of those in the world and its view if loaded.
2. Spawns those three as temporary samples in a row to the right of the player, waits 90 frames for their prefabs,
   and dumps each one the same way (variations such as with/without cart, animation component, animator clips and
   parameters, hierarchy). They stay for 25 s so you can screenshot them, then are destroyed.

Hotkey experiments (all logged to `BepInEx\LogOutput.log`, prefixed `[speed]`, `[follow]`, `[seat]`, `[state]`):

| Key | Experiment |
|---|---|
| `F9` | Toggle `SpeedMultiplier` 1.0 ↔ 1.75. Walk in a straight line; measured speed (units/s) is logged every second. Tells us how the force-based movement responds and how it feels. |
| `F10` | Toggle a temporary `npc_donkey` that follows the player: position, facing, walk/idle mirrored from the player. Colliders disabled. Shows whether the sprites sort correctly and how the animation looks. |
| `F11` | Cycle the follower's depth offset (-0.3 … +0.3, positive = behind the player). |
| `F12` | Show/hide the follower's cart, wheels and lantern (the prefab has no cart-less variation, so the parts are toggled by name). Spawns cart-less by default (`FollowWithCart`). |
| `PageUp` / `PageDown` | Raise/lower the rider (player view) by 0.05 while the follower is active. Tests the `PlayerView.LateUpdate` postfix approach. |

All keys and the ids/values are in `BepInEx\config\anvil.gk2.mountprobe.cfg`. State-machine and animation-state
transitions are logged as they happen (`LogStateChanges`), so doing things while the follower is active (open a
chest, work a bench, climb a ladder, draw a weapon, sleep, teleport) shows exactly which transitions a real mount
must react to.

What to send back: `GK2-mount-probe.txt`, `LogOutput.log`, and screenshots of the samples and of the follower
with a few depth/seat settings.

Remove `GK2MountProbe.dll` from `BepInEx\plugins` when done; it has no purpose in normal play.
