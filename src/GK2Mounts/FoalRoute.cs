using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace GK2Mounts
{
    // A route around obstacles for a foal following its parent (docs/mounts-foals-pathing.md): A* on a small grid of
    // 0.25 cells around the foal and its target, a cell walkable exactly when the foal's own checks say so (ground under
    // it, nothing of the keeper-blocking layer 8 in the body capsule), so a route agrees with the trot's ahead check.
    // The game's graphs are not used: the NPC navmesh does not cover everywhere the keeper walks, the keeper's grid graph
    // is his (4.4 units, rescanned for click-to-move), and adding a graph would shift the indices the game uses.
    //
    // The search is incremental (Step, resumed every frame) under a budget of cell tests shared by all foals each
    // frame, so a dead end never hitches a frame. The result is string-pulled on the grid to a few corners.
    internal sealed class FoalRouteSearch
    {
        internal const float Cell = 0.25f;
        internal const int FrameBudget = 120;      // cell tests per frame, all foals together
        internal const int SearchBudget = 2500;    // cell tests per search, then "no route"
        private const float UpDownCost = 1.3f;     // steps along the screen's up/down cost more: sideways legs preferred
        private const int ExpansionsPerStep = 400;

        private static int budgetFrame = -1;
        private static int budgetLeft;

        internal enum State { Running, Found, NoRoute }

        internal State Result { get; private set; } = State.Running;
        internal List<Vector3> Corners { get; private set; }
        internal Vector3 Goal { get; }
        internal int Tests { get; private set; }
        internal int Frames { get; private set; }
        internal double TestMs { get; private set; }
        internal string Why { get; private set; }

        private readonly Func<Vector3, float> groundAt;       // NaN: no ground
        private readonly Func<Vector3, bool> freeAt;
        private readonly float maxRise;
        private readonly Vector3 origin;                      // world position of cell (0, 0)'s corner
        private readonly int w, h;
        private readonly sbyte[] cellState;                   // 0 unknown, 1 free, 2 blocked
        private readonly float[] cellHeight;
        private readonly float[] g;
        private readonly int[] from;
        private readonly bool[] closed;
        private readonly List<int> open = new List<int>();    // binary heap on f
        private readonly float[] f;
        private readonly int start, goal;
        private readonly Vector3 goalPoint;
        private readonly float startY;
        private readonly Stopwatch watch = new Stopwatch();

        // start and goal are body points; range: how far from the start the grid may reach (either way).
        internal FoalRouteSearch(Vector3 startPoint, Vector3 goalPoint, float range, float maxRise,
            Func<Vector3, float> groundAt, Func<Vector3, bool> freeAt)
        {
            this.groundAt = groundAt;
            this.freeAt = freeAt;
            this.maxRise = maxRise;
            startY = startPoint.y;
            range = Mathf.Clamp(range, 1f, 8f);
            // The box around both ends, 3 larger, within 'range' of the start.
            float minX = Mathf.Max(Mathf.Min(startPoint.x, goalPoint.x) - 3f, startPoint.x - range);
            float maxX = Mathf.Min(Mathf.Max(startPoint.x, goalPoint.x) + 3f, startPoint.x + range);
            float minZ = Mathf.Max(Mathf.Min(startPoint.z, goalPoint.z) - 3f, startPoint.z - range);
            float maxZ = Mathf.Min(Mathf.Max(startPoint.z, goalPoint.z) + 3f, startPoint.z + range);
            origin = new Vector3(minX, 0f, minZ);
            w = Mathf.Max(2, Mathf.CeilToInt((maxX - minX) / Cell));
            h = Mathf.Max(2, Mathf.CeilToInt((maxZ - minZ) / Cell));
            int n = w * h;
            cellState = new sbyte[n];
            cellHeight = new float[n];
            g = new float[n];
            f = new float[n];
            from = new int[n];
            closed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                g[i] = float.MaxValue;
                from[i] = -1;
            }
            // A goal outside the box: the route leads to the box's edge nearest to it, and the next plan goes on.
            Vector3 gp = goalPoint;
            gp.x = Mathf.Clamp(gp.x, minX + Cell * 0.5f, maxX - Cell * 0.5f);
            gp.z = Mathf.Clamp(gp.z, minZ + Cell * 0.5f, maxZ - Cell * 0.5f);
            this.goalPoint = gp;
            Goal = gp;
            start = Index(startPoint);
            goal = Index(gp);
            // The start is where the foal stands: walkable whatever the test says (it may brush something).
            cellState[start] = 1;
            float sg = groundAt(startPoint);
            cellHeight[start] = float.IsNaN(sg) ? startY : sg;
            g[start] = 0f;
            f[start] = Heuristic(start);
            Push(start);
        }

        private int Index(Vector3 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x - origin.x) / Cell), 0, w - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((p.z - origin.z) / Cell), 0, h - 1);
            return z * w + x;
        }

        private Vector3 Center(int i)
        {
            int x = i % w, z = i / w;
            return new Vector3(origin.x + (x + 0.5f) * Cell, cellHeight[i], origin.z + (z + 0.5f) * Cell);
        }

        private float Heuristic(int i)
        {
            int x = i % w, z = i / w;
            int gx = goal % w, gz = goal / w;
            float dx = Mathf.Abs(x - gx), dz = Mathf.Abs(z - gz) * UpDownCost;
            // Octile with the up/down axis scaled: admissible for the weighted step costs below.
            return (Mathf.Max(dx, dz) + (1.41421356f - 1f) * Mathf.Min(dx, dz)) * Cell;
        }

        // Free (and its height known)? Tests an unknown cell, which costs one from the budget; false when out of budget
        // this frame (outOfBudget set).
        private bool Free(int i, ref bool outOfBudget)
        {
            if (cellState[i] != 0)
            {
                return cellState[i] == 1;
            }
            if (!TakeBudget())
            {
                outOfBudget = true;
                return false;
            }
            watch.Start();
            Vector3 c = Center(i);
            c.y = startY;
            float gh = groundAt(c);
            bool free = !float.IsNaN(gh) && freeAt(new Vector3(c.x, gh, c.z));
            watch.Stop();
            Tests++;
            cellHeight[i] = float.IsNaN(gh) ? startY : gh;
            cellState[i] = (sbyte)(free ? 1 : 2);
            return free;
        }

        private bool TakeBudget()
        {
            if (budgetFrame != Time.frameCount)
            {
                budgetFrame = Time.frameCount;
                budgetLeft = FrameBudget;
            }
            if (budgetLeft <= 0 || Tests >= SearchBudget)
            {
                return false;
            }
            budgetLeft--;
            return true;
        }

        // One frame's work. Returns the state; Corners is set when Found.
        internal State Step()
        {
            if (Result != State.Running)
            {
                return Result;
            }
            Frames++;
            int expansions = 0;
            while (open.Count > 0 && expansions < ExpansionsPerStep)
            {
                int cur = open[0];
                if (closed[cur])
                {
                    Pop();
                    continue;
                }
                if (cur == goal || FlatDist(Center(cur), goalPoint) <= 0.5f && cellState[cur] == 1 && cur != start)
                {
                    Finish(cur);
                    return Result;
                }
                // Test all neighbours first (may run out of budget: then resume this cell next frame).
                bool outOfBudget = false;
                int cx = cur % w, cz = cur / w;
                for (int dz = -1; dz <= 1 && !outOfBudget; dz++)
                {
                    for (int dx = -1; dx <= 1 && !outOfBudget; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        Free(nz * w + nx, ref outOfBudget);
                    }
                }
                if (outOfBudget)
                {
                    if (Tests >= SearchBudget)
                    {
                        Fail($"no route within {SearchBudget} cell tests");
                    }
                    return Result; // next frame
                }
                Pop();
                closed[cur] = true;
                expansions++;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        int ni = nz * w + nx;
                        if (closed[ni] || cellState[ni] != 1) continue;
                        if (Mathf.Abs(cellHeight[ni] - cellHeight[cur]) > maxRise) continue;
                        // No diagonal past a blocked corner (it would cut a fence end).
                        if (dx != 0 && dz != 0 && (cellState[cz * w + nx] != 1 || cellState[nz * w + cx] != 1)) continue;
                        float step = dx != 0 && dz != 0 ? Mathf.Sqrt(1f + UpDownCost * UpDownCost) : dz != 0 ? UpDownCost : 1f;
                        float ng = g[cur] + step * Cell;
                        if (ng < g[ni])
                        {
                            g[ni] = ng;
                            from[ni] = cur;
                            f[ni] = ng + Heuristic(ni);
                            Push(ni);
                        }
                    }
                }
            }
            if (open.Count == 0)
            {
                Fail("no way around inside the area");
            }
            return Result;
        }

        private void Fail(string why)
        {
            Result = State.NoRoute;
            Why = why;
            TestMs = watch.Elapsed.TotalMilliseconds;
        }

        private void Finish(int end)
        {
            var cells = new List<int>();
            for (int i = end; i >= 0; i = from[i])
            {
                cells.Add(i);
            }
            cells.Reverse();
            // String-pulling on the grid: from each corner, the furthest later cell in a clear line of known free cells.
            var corners = new List<Vector3>();
            int a = 0;
            while (a < cells.Count - 1)
            {
                int b = cells.Count - 1;
                while (b > a + 1 && !ClearLine(cells[a], cells[b]))
                {
                    b--;
                }
                corners.Add(Center(cells[b]));
                a = b;
            }
            if (corners.Count == 0)
            {
                corners.Add(Center(end));
            }
            Corners = corners;
            Result = State.Found;
            TestMs = watch.Elapsed.TotalMilliseconds;
        }

        // Every cell a straight line from a's centre to b's crosses is free (a quarter-cell walk), with no rise above
        // maxRise between neighbours. A cell's own test is the full body capsule, so the cell under each sample is
        // enough. Cells the search never tested (off the path) are tested here, outside the frame budget: a pull costs
        // a few dozen tests once per route (with only known cells, nearly every pull past a neighbour failed and a
        // route had a corner in every cell).
        private bool ClearLine(int a, int b)
        {
            Vector3 pa = Center(a), pb = Center(b);
            float len = FlatDist(pa, pb);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / (Cell * 0.25f)));
            int prev = a;
            for (int s = 1; s <= steps; s++)
            {
                int c = Index(Vector3.Lerp(pa, pb, s / (float)steps));
                if (c == prev)
                {
                    continue;
                }
                if (!FreeUnbudgeted(c) || Mathf.Abs(cellHeight[c] - cellHeight[prev]) > maxRise)
                {
                    return false;
                }
                prev = c;
            }
            return true;
        }

        private bool FreeUnbudgeted(int i)
        {
            if (cellState[i] != 0)
            {
                return cellState[i] == 1;
            }
            watch.Start();
            Vector3 c = Center(i);
            c.y = startY;
            float gh = groundAt(c);
            bool free = !float.IsNaN(gh) && freeAt(new Vector3(c.x, gh, c.z));
            watch.Stop();
            Tests++;
            cellHeight[i] = float.IsNaN(gh) ? startY : gh;
            cellState[i] = (sbyte)(free ? 1 : 2);
            return free;
        }

        private static float FlatDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------ binary heap on f

        private void Push(int i)
        {
            open.Add(i);
            int c = open.Count - 1;
            while (c > 0)
            {
                int p = (c - 1) / 2;
                if (f[open[p]] <= f[open[c]]) break;
                (open[p], open[c]) = (open[c], open[p]);
                c = p;
            }
        }

        private void Pop()
        {
            int last = open.Count - 1;
            open[0] = open[last];
            open.RemoveAt(last);
            int c = 0;
            while (true)
            {
                int l = 2 * c + 1, r = l + 1, m = c;
                if (l < open.Count && f[open[l]] < f[open[m]]) m = l;
                if (r < open.Count && f[open[r]] < f[open[m]]) m = r;
                if (m == c) break;
                (open[m], open[c]) = (open[c], open[m]);
                c = m;
            }
        }
    }
}
