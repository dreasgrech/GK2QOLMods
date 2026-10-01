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

## Still open

- **The per-frame work, about 0.5 ms with 63:** Shuffle, Pose and Ground checks. Not visible at the cap. Worth a pass
  if slower PCs or hundreds of donkeys matter.
- **The `[shuffle] frame +N` turn diagnostics** read the body before the frame's own position write. They can't tell
  whether a turn jumps on screen.
