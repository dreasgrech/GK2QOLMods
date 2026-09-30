# GK2 Mounts: foals finding their way around obstacles

This was investigated on 2026-09-30.

**Built 2026-09-30, not yet tested in game.**
- **Code:** `src/GK2Mounts/FoalRoute.cs` (the search) and `Foals.cs` (`FollowWithRoute`, `StartRoute`).
- **Settings** (`[Shuffle]`): `ShuffleFollowRoutes` (on) and `ShuffleRouteRange` (8). They're on the panel's
  Instances tab.
- **Decisions:** as recommended below. Routes are on by default, reach 8 units, and are also used to rejoin within
  that range.
- **Log:** each plan writes `[foal] ... works out a way round (why)`, then `[foal] ... route to its parent: found N
  corner(s) | none (why); T cell tests in F frame(s), X ms (Y µs per test)`, the first 30 per foal.

**In-game test:** follow the parent round a fence, a log pile and a workbench in the yard.
- The status line shows "working out a way round", then "trotting round an obstacle, corner i of n".
- Send the `route` lines; they give the cost per cell test for tuning the budget.

**The problem:** foals following their parent go only in straight lines (docs/mounts-foals.md). Every fence, log
pile or workbench between them and the parent stops them. They then wait until the straight line is clear, or catch
up out of sight once the parent stands still. In the yard that happens a lot.

**The request:** investigate option 2 from the discussion, the mod's own route finder on a small grid built with the
test the foals already use.

## What the log says about the stops

Every "blocked" stop in the log is a real collider on the keeper-blocking layer 8, which is called `Obstacle`:

| Count | Collider |
|---|---|
| 33 | `DecomposedConvexMeshCollider` (Obstacle) |
| 20 | `Collider` (Obstacle) |
| 10 | `Obstacle` (Obstacle) |

None were ground or height problems once the slope limits were fixed. So the grid needs nothing new: a cell is
walkable exactly when `FreeAt` (the capsule test) and `GroundHeight` say so today. The route agrees with the checks
the trot already makes, so a route the planner finds can't then be refused by the trot's ahead check.

## Why not the game's own path finding

- **The NPC recast navmesh** (`GlobalNavigationManager.CalculatePath`) doesn't cover everywhere the keeper walks. The
  first shuffle build, which required it, never took a step.
- **The keeper's grid graph** (graph 3, `PlayerLocalAreaMovement.RescanPlayerGraph`) is 44 × 44 cells of 0.1 around
  the keeper, 4.4 units across. It is rescanned for his click-to-move. It is too small and belongs to the keeper; a
  foal's search there would fight his.
- **Adding a graph of our own to `AstarPath.active`** would shift the game's graph indices, which its code uses by
  number (`graphs[3]`, `graphs[12]`, the masks in `LazyConsts.Navigation`). That is out.
- **Our own small A\*** needs no game code at all. It uses the same test as everything else the foals do.

## Design

### When it runs

It runs only while a foal follows (`ShufflePhase.Following`) **and** the straight line to its target is blocked:

- the ahead check fails;
- or rejoining finds no straight way to any slot.

A foal in the open never plans: nothing changes there.

### The grid

- **Cell size:** 0.25 world units. A foal's body capsule has radius 0.25, and the fence gaps the keeper walks through
  are wider than 0.5.
- **Area:** a box around the foal and its target, 3 units larger on every side, capped at 16 × 16 units (64 × 64
  cells). A target further than the cap allows gets a route to the edge of the box nearest to it, and the next plan
  goes on from there.
- **Cells are tested on demand** during the search, and each result is kept for that search.
  - A cell is walkable when there is ground (`GroundHeight`, navmesh or raycast) and `FreeAt` finds nothing on layer
    8. The foal's own view and the keeper's rigidbody are ignored, as now.
  - A step between two cells is allowed when their heights differ by at most `MaxRisePerSample` (0.45).
  - A diagonal step is allowed only if both side cells are walkable, so it never cuts a fence corner.
- **Cost:** octile distance.
  - Steps along the screen's up/down cost 1.3 times as much. The route then prefers diagonals and sideways legs,
    matching the "not straight up or down" rule wherever it has a choice.
  - The heuristic is octile distance to the goal, scaled the same way, so it stays admissible.

### Budget (so it never hitches)

- **Cost of one cell test:** a navmesh `GetNearest`, sometimes a raycast, and one `OverlapCapsuleNonAlloc`. That is
  about 10–30 µs; **measure it** (below).
- **Cost of a search:** a route around a fence opens a few hundred cells. A dead end can open all 4096, which is
  about 40–120 ms if done in one frame. That is too much.
- **The search is incremental:** an open list and a closed set kept on the foal, resumed each frame.
  - **Per frame:** at most 120 cell tests for **all** foals together. A queue gives each foal's search its turn, so
    ten foals don't cost ten times as much per frame.
  - **Per search:** at most 2500 cell tests. Past that, "no route", and the foal waits as it does today.
- **Planning rate:** at most once a second per foal, and not again while its previous plan is still valid.

### Smoothing and walking the route

- **String-pulling:** from each waypoint, the furthest later waypoint in a clear straight line (`LineWalkable`)
  becomes the next corner. This reduces the grid's staircase to a few corners, typically 1–3 around a fence.
- **Walking:** the trot moves to each corner in turn, with the same speed, slow-down, facing hysteresis (a sideways
  part over 0.35 before it turns) and ahead check as now.
- **Up/down legs:** a leg that is nearly straight up or down keeps the last facing. The foal slides that stretch in
  side view, the same way the keeper does riding up the screen. The cost weighting keeps these legs short.
- **Re-planning:**
  - when the parent's slot has moved more than 1.5 from the route's end (the parent kept going);
  - when the ahead check fails on the route (something moved in the way, e.g. a dropped item);
  - when the foal hasn't got 0.3 closer to the next corner in 2 s (it is stuck).
- **When a straight line opens up again**, the route is dropped and the foal goes straight to the target.

### When there's no route

It is treated as lost, exactly as now. The foal shuffles where it is and tries to rejoin with a new plan, with the
existing backoff (0.5 s doubling to 8 s). It catches up out of sight once the parent stands still. Doors and other
scenes don't change.

## What changes in the code

- **New `FoalRoute.cs`** (partial `Instance`):
  - the grid (a flat array keyed by cell, with walkable/unknown/blocked and a height);
  - the incremental A* state and the string-puller;
  - the shared per-frame budget and queue (static).
- **`Foals.cs`:**
  - `FollowStep` gets a waypoint list and walks it;
  - a blocked straight line starts a plan instead of losing the parent at once. The foal stands and waits while the
    plan runs, usually a frame or three;
  - rejoining plans when no straight way exists.
- **`IdleShuffle.cs`:** `FreeAt` and the height test are shared, unchanged.
- **Settings** (`[Shuffle]`, Tuned, so they can be set per foal):
  - `ShuffleFollowRoutes` (bool, default on): go around obstacles when following. Off gives today's behaviour.
  - `ShuffleRouteRange` (default 8): how far around the foal a route may go. It sets the grid box and is capped at
    16.
- **API:** nothing new. `IsStepping` stays true while walking a route.
- **Panel:**
  - the foal's status line shows "routing…", then "following a route (3 corners)", and "no route" with the cells
    tested;
  - optionally a dev draw of the route as markers at its corners, like the `#N` markers.
- **Log:** one `[foal] route` line per plan: cells tested, frames taken, corners, and the result.

## Risks and limits

- **Clearance:** the plan is for the foal's body point with radius 0.25. The root, half a scaled body to the side,
  isn't planned, as today. A foal may brush a fence with its rear end. The test for the root could be added per cell
  if it shows.
- **Moving obstacles:** carried items, NPCs and the keeper. The grid only lives for one search, and re-planning
  handles changes.
- **Thin colliders:** anything thinner than a cell could fall between samples. The capsule has a radius of 0.25, a
  whole cell's width, so nothing on layer 8 can.
- **Very large families:** the shared budget keeps the frame cost flat. With many foals routing at once, each just
  waits longer for its route.
- **Facing on corners:** a corner that swings the direction to the other side turns the foal around (the one-frame
  turn). Around a fence that may be one or two turns, and it looks natural.

## Measure first (in game, once built)

1. **The cost of one cell test,** from the `[foal] route` lines: µs per test over a few plans. If it is well under
   20 µs, raise the per-frame budget.
2. **How often plans run** in the yard (lines per minute), and how many cells a typical fence detour tests.
3. **Whether 0.25 cells find the fence gaps** the keeper uses. If not, try 0.2; the cap would then be 80 × 80.

## Open decisions for the user

1. **Default for `ShuffleFollowRoutes`:** on (recommended), or off with the showcase or the panel switching it on?
2. **Range:** 8 units around the foal (recommended), or larger at a higher cost?
3. **Routes for the rejoin too?** Should a lost foal also plan back to its parent from far away (costlier), or only
   within the range? Recommended: only within the range. Beyond it, the catch-up out of sight does the job.
