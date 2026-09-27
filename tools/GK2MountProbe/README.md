# GK2 Mount Probe

Development-only BepInEx plugin used while designing GK2 Mounts. Not for players, not in `dist/`. It never changes
saved data: everything it spawns is a temp object like the game's build preview, with the definition's attached
flow script suppressed.

Archived output: `report-round1-RuinedTemple.txt` (full dump), `log-round2.txt`, `log-round3.txt`. Conclusions are in
[`src/GK2Mounts/PROBE-FINDINGS.md`](../../src/GK2Mounts/PROBE-FINDINGS.md).

## What it does

About 6 s after a save is loaded (once per load; `F8` repeats) it writes `BepInEx\GK2-mount-probe.txt`: camera
setup; the player's controller, physics body, view and animation fields (private serialized ones included), the full
player hierarchy with every sprite renderer, collider, Rigidbody and Animator (clips, parameters, layers); the
`WGODef` of `npc_donkey`, `cow`, `npc_donkey_test`; their existing instances; then temp-spawns them in a row to the
right of the player, dumps each prefab the same way and destroys them after 25 s.

## Hotkeys (all logged to `BepInEx\LogOutput.log` with `[speed]`, `[follow]`, `[seat]`, `[state]` prefixes)

| Key | Experiment |
|---|---|
| `F9` | Toggle `SpeedMultiplier` 1.0 ↔ 1.75. Once pressed, measured speed (u/s, per axis, max rigidbody velocity) is logged every second at either setting. |
| `F10` | Toggle a temp `npc_donkey` that follows the player: position, facing, walk/idle mirrored; colliders off; cart hidden by default (`FollowWithCart`). |
| `F11` | Cycle the follower's depth offset (-0.3 … +0.3, positive = away from camera). |
| `F7` | Toggle facing-aware centering (`FollowCenterOffset`, 0.48). |
| `F12` | Show/hide the cart, wheels and lantern. |
| `PageUp` / `PageDown` | Move the rider (player view) up/down by 0.05 via a `PlayerView.LateUpdate` postfix. |

State-machine, animation-state and control-flag transitions are logged as they happen (`LogStateChanges`).
Config: `BepInEx\config\anvil.gk2.mountprobe.cfg`.
