# GK2 Mounts: performance

Measured 2026-10-01 on the Steam build 25601286. The save had 63 instances: 10 families of a donkey and 4 foals, plus
other donkeys and carts; 43 of them foals. The game runs at a frame cap of about 117 fps (8.5 ms) on this PC.

## The profiler (`src/GK2Mounts/Perf.cs`)

**What it measures:** Stopwatch timestamps around each part of the mod's frame.
- Each part's time is summed per frame and averaged over one second.
- For each part it reports the average, the worst frame of that second, and how often it ran per frame.
- Parts nest, so each number includes the parts inside it: Tick contains Stand, which contains Shuffle, Follow, Line,
  Ground and Free.
- **Panel** is the panel's own OnGUI code. **Window** is the window's contents, which IMGUI runs after OnGUI returns,
  so they aren't in Panel.

**Where to read it:**
- The panel's Debug tab, "Performance".
- `[perf]` lines in the log every 10 s, once that switch is on. They keep coming with the panel closed.

**Two experiments,** both for the session only:
- **No shuffle:** switches off the mod's movement work.
- **Hide standing donkeys:** switches off their drawing and animation, to show the game's own cost of displaying them.

## Findings

| Situation | Frame | The mod |
|---|---|---|
| Panel closed, 63 roaming | 8.5 ms (117 fps, the cap) | 0.75 ms: Shuffle 0.35 (Follow 0.15), Pose 0.16, Colors 0.16 |
| **Instances tab open (before)** | **32–37 ms (27–31 fps)** | Panel 0.74 ms, plus about 27 ms in the window contents (not measured then) |
| Instances tab open (after pass 1) | 8.5–9.1 ms | Window 0.9 ms folded, 1.7 ms with rows opened |
| No shuffle / hidden views | 8.5 ms | Movement 0.39 ms; hiding changed nothing at the cap |

**What made the game choppy: the Instances tab.**
- **The cause:** it laid out about 63 rows of IMGUI controls with long wrapped text, fresh status strings and a
  nearest-cart search per row, several times a frame.
- **Not the donkeys:** with the panel closed, 63 roaming donkeys keep the game at its cap.

## Changes

- **Pass 1:**
  - **The Instances tab:**
    - groups fold, and a group of more than 6 starts folded;
    - each donkey is one compact line with a Details button;
    - full controls appear only on opened rows and on the donkey being edited.
  - **Colours:** sprites are checked five times a second instead of every frame (0.16 → 0.02 ms).
  - **The log:** foal and shuffle lines are capped at 4 a second, and the next line says how many were skipped. It
    had been 10–19 lines a second.
- **Pass 2:**
  - **World markers:** text and size are cached (0.56 → 0.05 ms).
  - **The Debug tab's summary** is built once a second.
  - **Writing a standing view's position and facing only when they changed** was tried and reverted. It saved 0.02 ms,
    and every write is kept as it was.
- **Foal behaviour found during the measuring:** foals disappearing and sprinting in crowded families.
  - **The data:** every failed step pick was "outside the area" or "near another donkey", never ground. Yet three in a
    row counted as wedged, and that teleported the foal back to its parent, even in view.
  - **Fixes:**
    - only picks blocked by the world count as wedged;
    - a step back towards home is allowed when the foal is outside its area;
    - a catch-up happens only out of sight, unless truly wedged;
    - within about 1 unit of its spot a foal walks at about 1 u/s, and trots at full speed only from 4 units out.

## Many donkeys scattered around the world (2026-10-01)

**The test:** the herd test mod's scatter key (L) adds 50 donkeys at random up to 60 units around the keeper. They're
not kept with the save. The game was still at its 117 fps cap, because uncapping hadn't taken.

| Donkeys | Frame | The mod |
|---|---|---|
| 63 | 8.5 ms (the cap) | 0.5 ms |
| 163 | 8.5–9.1 ms | 0.8–1.0 ms |
| 363 | 12.3–14.1 ms (71–81 fps) | 1.9–2.0 ms (Shuffle 1.0, Pose 0.45) |

**What it shows:**
- **The game's share is the large one:** at 363, about 11 ms of a 13 ms frame went to the game updating, animating and
  culling 363 donkey views.
- **Why the game can't cull them itself:** our views are spawned outside the game's chunk system
  (`ignoreChunkRegistration: true`, then `UpdateChunkVisibility(true)`), so its distance culling never hides them.
- **The mod's own cost:** about 5 µs per donkey per frame.
- **Pressing L hitched:** 50 views made in one frame, a 20 ms frame.

### Pass A (no behaviour change)

- **Families:** `Children()` and the sibling index are cached until a parent link or the set of instances changes
  (`Mount.structureVersion`; `Parent` is a property that bumps it).
- **Neighbour lookups:** `NearOther` walks only this frame's instances of its own scene (`Mount.InScene`).
- **The API's lists:** `Mounts.Instances` and `InstancesOf` are cached per change. A new list is made per change, so a
  list a caller holds stays a snapshot, as before.
- **Unloaded scenes:** instances there tick twice a second instead of every frame.

### Pass B: levels (`src/GK2Mounts/Tiers.cs`)

| Level | When | What it does |
|---|---|---|
| Active | ridden, edited on the panel, within 15 of the keeper, or on screen (30% margin beyond each edge) | everything |
| Idle | the keeper's scene, off screen, up to 35 | the view stays (no pop-in when the camera turns), frozen: no shuffle, no posing, animators off, renderers left out of drawing |
| Asleep | beyond 35 (awake again inside 30) | the view is released through the mod's scene-change path; cargo, colours, settings and data stay with the instance. A foal goes to its still parent once a second |

- **When levels are checked:** four times a second per instance, staggered, and at once after a move (MoveTo,
  ComeToKeeper, a catch-up).
- **Hysteresis:** leaving Active needs 3 more units and 10% more margin.
- **New views:** at most 1 per frame, nearest first (`Mount.ProcessSpawnQueue`). That covers spawns from the API, a
  restore at load, and waking up.
- **Panel:** every number is tunable for the session on the Debug tab ("Levels"), with a switch to turn levels off.
  The `[perf]` line has the counts per level and the views made and released per second. Compact rows say "idle (off
  screen)" or "asleep (far)".
- **For API users:** an asleep donkey has no view, so `IsReady` is false and `Ride` refuses. `MoveTo` or
  `ComeToKeeper` brings it near, and it wakes on the next frame.

### Measured, uncapped (2026-10-01; the NVIDIA cap removed)

| 363 donkeys, the same spot | Frame | The mod |
|---|---|---|
| Levels off | 64–72 fps (13.8–15.6 ms) | 2.1–2.3 ms |
| Levels on (45 / 38, 3 views a frame) | 96–98 fps (10.2–10.4 ms); 144–158 with fewer in view | 0.83–0.87 ms |
| 63 donkeys, baseline | 200 fps (5.0 ms) | 0.40 ms |

- **Levels on:** about 4 ms off the frame. At that spot 101 were active, 138 idle and 124 asleep.
- **Scattering 50:** still a 22 ms frame. Making views cost up to 12 ms in one frame, and the log took 114 lines a second,
  two per new view.
- **After that:**
  - one new view per frame;
  - the per-view lines (spawned, colours attached, skipped script) share the 4-a-second log budget;
  - idle views also stop rendering;
  - asleep beyond 35, awake again within 30.

  Measured in the next run:

| 363 donkeys, the same spot | Frame | The mod (per-frame work) |
|---|---|---|
| Levels off | 61–70 fps (14.3–16.4 ms) | 1.9–2.2 ms |
| Levels on (35 / 30, 1 view a frame) | 100–104 fps (9.6–10.0 ms) | 0.74–0.79 ms |
| 63 donkeys at that spot, after removing the 300 | 110 fps (9.1 ms) | 0.31 ms |

- **What 300 extra scattered donkeys cost with levels on:** about 0.6 ms over the 63-donkey frame at the same spot.
  Without levels they cost about 6 ms.
- **The levels at 363 donkeys:** 92 active, 60 idle, 211 asleep, so 152 views (239 with the 45 / 38 ring).
- **Scattering 50:** the worst frame of that second fell from 22 to 13–20 ms. Making a view now costs at most about
  4 ms (one a frame), and the log stays at 4 lines a second.
- **Switching levels off:** all 211 asleep views come back at once, one a frame (63 a second).
- **In game:** the user saw no donkeys popping in or freezing at the screen edges while walking and riding through the 363.
- **The panel:** the "mod" figure of 1.7 ms in these lines includes about 1 ms for the open Debug tab. With the panel
  closed, the per-frame work is Tick, 0.75 ms.

## Memory and leaks (2026-10-01)

**What the `[perf]` line shows:**
- **Allocations:** the mod's managed allocations per frame (`GC.GetTotalMemory` around Tick and the panel window), and
  per part, as "+N B", with the dev panel on.
- **The game:** its garbage collections per second and its managed heap.
- **Live counters for every Unity object the mod makes and must destroy:** colour textures (count and KB), colour
  materials, hanging lamps, meshes, cargo holders, and shared colour textures (`Live` in Perf.cs).
- **Slow frames:** a separate line for any frame over 20 ms, with each part of the mod's time in it.

**The measurements:** the scatter test twice, and riding through 363 donkeys.

| | 63 before | 363 | 63 after the 1st removal | 63 after the 2nd cycle |
|---|---|---|---|---|
| Textures | 427 (39 MB) | 1,197–1,400 (110–128 MB) | 441 (40 MB) | 441 (40 MB) |
| Materials | 244 | 616–684 | 252 | 252 |
| Lamps / meshes / cargo holders | 4 / 8 / 4 | 4 / 8 / 4 | 4 / 8 / 4 | 4 / 8 / 4 |

- **No leak:** every counter returns to the same level after each cycle. The game's heap moves between 440 and 520 MB
  without drifting.
- **Colour textures were the big item:** each donkey kept its own copies, about 350 KB, also while asleep. That's
  128 MB at 363, nearly all identical default colours. Building them allocated 700–800 KB of garbage per new view.
  - **The fix:** `Colors.SharedTextures`, keyed by group and content, counted by users; the last user destroys the
    texture. A shared texture is never filled in place.
- **Per-frame garbage:** 6–17 KB a frame (1–2 MB/s at 120 fps). The game collected about every 15 s.
  - A status string rebuilt every frame for every donkey near the keeper is now cached.
  - The per-part allocation figures are there to find the rest.
- **Riding through the herd:** views were made and released at up to 11–14 a second. Making a view took 2–5 ms in its
  worst frame (the view 1.7–2.4, parts up to 1.4, colours up to 1.2). Most slow frames while riding (21–34 ms) had the
  mod at about 1 ms, so they were the game's own.
- **Found by the ride:** a donkey mounted while idle stayed invisible, and then faced the wrong way.
  - **Invisible:** riding never runs the level check, so the ridden one is now woken every frame.
  - **Facing:** an Animator switched off forgets its state and parameters by default. It now keeps them
    (`keepAnimatorStateOnDisable`), and the stand pose is sent again on waking.

## View reuse (2026-10-01, `ViewPool` in Tiers.cs)

**How it works:**
- **Parking:** a view released because its donkey fell asleep is put back to the prefab's state (`ReleaseView`, as for
  the game's own part pool), switched off with `SetActive(false)` (the game's own way of hiding a Wgo), and parked by
  scene, kind and size, at most 24 each.
- **Reuse:** the next donkey of that scene, kind and size takes it. It gets its place and facing, stays hidden for the
  frame its parts are prepared in, and is switched on.
- **Excluded:** carts (their data holds the cargo inventory), ridden donkeys and the summon.

**The game's code behind this:** `Wgo.Spawn` leaves a new view inactive, and `RefreshVisuals` (chunk culling) switches
views on and off with `SetActive`. `WgoPart.OnEnable` and `OnDisable` initialise and deinitialise its drawer, and
`AnimationComponentBase.OnDisable` resets to Idle.

**Riding through 363 donkeys:**

| | Before (new views only) | With reuse |
|---|---|---|
| Views made while riding | 11–15/s, all new | 4–18/s, 4–16 of them reused |
| Worst Spawn in a second | 4.6–4.9 ms | 1.5–3.1 ms |
| Worst making of the view itself | 1.7–2.4 ms | 0.8–1.6 ms (switching the parked view on) |

- **What a reused view still costs:** preparing its parts and colours, up to about 0.6 and 0.3 ms.
- **When many fall asleep at once:** only as many as fit are parked. The rest are destroyed as before; one second
  released 53 and parked 15.

## Still open

- **The per-frame work, about 0.5 ms with 63:** Shuffle, Pose and Ground checks. Not visible at the cap. Worth a pass
  if slower PCs or hundreds of donkeys matter.
- **The `[shuffle] frame +N` turn diagnostics** read the body before the frame's own position write. They can't tell
  whether a turn jumps on screen.
