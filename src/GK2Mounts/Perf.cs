using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace GK2Mounts
{
    // What the mod costs per frame, by part (docs/mounts-performance.md). Stopwatch timestamps around each part,
    // summed per frame, averaged over one second: the average and the worst frame of that second, and how often each
    // part ran per frame. Parts nest (Shuffle is inside Stand, Line inside Shuffle and Follow), so each number includes
    // the parts inside it. The frame time itself is Unity's (Time.unscaledDeltaTime), so the game's own share is the
    // frame minus ours. A timestamp costs about 25 ns: a few thousand a frame are well under 0.1 ms.
    internal static class Perf
    {
        internal enum S
        {
            Tick,       // Mount.Tick, everything per frame outside the panel
            Ride,       // the ridden instance's frame
            Stand,      // every standing instance's frame (StandTick), views spawned and prepared included
            Spawn,      // spawning and preparing views (TrySpawn, PrepareParts)
            Shuffle,    // the idle shuffle (ShuffleTick), foals' following included
            Follow,     // foals' following (FollowParentTick)
            Route,      // route searches (FoalRouteSearch steps)
            Line,       // straight-line walkability tests (LineWalkable: ground and collider samples)
            Ground,     // ground height tests (GroundHeight: navmesh, else raycast)
            Free,       // collider tests (FreeAt: OverlapCapsule)
            Pose,       // posing a standing view (Stand)
            Lantern,    // lantern rigs of standing carts
            Colors,     // colour rigs
            Controls,   // the keeper's ride / hitch prompt (InstanceControls)
            Panel,      // the dev panel (OnGUI: every event of the frame), without its window's contents
            Window,     // the panel window's contents (IMGUI runs them after OnGUI returns, so they are not in Panel)
            Markers,    // the panel's world markers
            Count,
        }

        private static readonly string[] Names = Enum.GetNames(typeof(S));
        private static readonly long[] frameTicks = new long[(int)S.Count];
        private static readonly int[] frameCalls = new int[(int)S.Count];
        private static readonly double[] sumMs = new double[(int)S.Count];
        private static readonly double[] maxMs = new double[(int)S.Count];
        private static readonly long[] sumCalls = new long[(int)S.Count];
        private static int frames;
        private static double frameSumMs, frameMaxMs;
        private static float windowStart = -1f;
        private static int lastFrame = -1;
        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        // The last full second.
        internal static readonly double[] AvgMs = new double[(int)S.Count];
        internal static readonly double[] WorstMs = new double[(int)S.Count];
        internal static readonly double[] CallsPerFrame = new double[(int)S.Count];
        internal static double Fps, FrameAvgMs, FrameWorstMs;
        internal static int Instances, Views, Foals;
        internal static int LogLinesPerSecond;
        internal static string SummaryText = "Measuring (one second)...";

        // The panel's switches (session only): a summary line in the log every 10 s, and the two experiments
        // (Mount.Instance.ApplyPerfExperiment).
        internal static bool LogEvery10s, ExpNoShuffle, ExpHideViews;
        private static float nextLog;
        private static int logLines;

        internal readonly struct Scope : IDisposable
        {
            private readonly int s;
            private readonly long t0;

            internal Scope(S s)
            {
                this.s = (int)s;
                t0 = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                frameTicks[s] += Stopwatch.GetTimestamp() - t0;
                frameCalls[s]++;
            }
        }

        internal static Scope Measure(S s) => new Scope(s);

        // A log line the mod wrote (counted for the panel: writing the log costs too).
        internal static void NoteLogLine() => logLines++;

        // Once per frame, first thing in Mount.Tick: closes the previous frame.
        internal static void BeginFrame()
        {
            int f = Time.frameCount;
            if (f == lastFrame)
            {
                return;
            }
            lastFrame = f;
            float now = Time.unscaledTime;
            if (windowStart < 0f)
            {
                windowStart = now;
                Clear();
                return;
            }
            double frameMs = Time.unscaledDeltaTime * 1000.0;
            frames++;
            frameSumMs += frameMs;
            frameMaxMs = Math.Max(frameMaxMs, frameMs);
            for (int i = 0; i < (int)S.Count; i++)
            {
                double ms = frameTicks[i] * MsPerTick;
                sumMs[i] += ms;
                maxMs[i] = Math.Max(maxMs[i], ms);
                sumCalls[i] += frameCalls[i];
                frameTicks[i] = 0;
                frameCalls[i] = 0;
            }
            if (now - windowStart >= 1f && frames > 0)
            {
                for (int i = 0; i < (int)S.Count; i++)
                {
                    AvgMs[i] = sumMs[i] / frames;
                    WorstMs[i] = maxMs[i];
                    CallsPerFrame[i] = (double)sumCalls[i] / frames;
                }
                FrameAvgMs = frameSumMs / frames;
                FrameWorstMs = frameMaxMs;
                Fps = frames / (now - windowStart);
                LogLinesPerSecond = (int)Math.Round(logLines / (now - windowStart));
                CountInstances();
                SummaryText = Summary(oneLine: false); // the panel shows this one (built once a second, not per event)
                windowStart = now;
                Clear();
                if (LogEvery10s && now >= nextLog)
                {
                    nextLog = now + 10f;
                    Plugin.Log.LogInfo("[perf] " + Summary(oneLine: true));
                }
            }
        }

        private static void Clear()
        {
            frames = 0;
            frameSumMs = frameMaxMs = 0;
            logLines = 0;
            Array.Clear(sumMs, 0, sumMs.Length);
            Array.Clear(maxMs, 0, maxMs.Length);
            Array.Clear(sumCalls, 0, sumCalls.Length);
        }

        private static void CountInstances()
        {
            int n = 0, v = 0, f = 0;
            foreach (Mount.Instance i in Mount.All)
            {
                n++;
                if (i.Ready) v++;
                if (i.Parent != null) f++;
            }
            Instances = n;
            Views = v;
            Foals = f;
        }

        // Ours: the per-frame work (Tick) plus the panel (Panel contains Markers; Window is measured on its own).
        internal static double OursAvgMs => AvgMs[(int)S.Tick] + AvgMs[(int)S.Panel] + AvgMs[(int)S.Window];

        internal static string Summary(bool oneLine)
        {
            var sb = new StringBuilder();
            string sep = oneLine ? "; " : "\n";
            sb.Append($"{Fps:F0} fps, frame {FrameAvgMs:F1} ms (worst {FrameWorstMs:F0}); mod {OursAvgMs:F2} ms ({(FrameAvgMs > 0 ? OursAvgMs / FrameAvgMs * 100 : 0):F0}% of the frame); {Instances} instance(s), {Views} view(s), {Foals} foal(s); {LogLinesPerSecond} log line(s)/s");
            for (int i = 0; i < (int)S.Count; i++)
            {
                if (AvgMs[i] < 0.005 && CallsPerFrame[i] < 0.01)
                {
                    continue;
                }
                sb.Append(sep).Append($"{Names[i]} {AvgMs[i]:F2} ms (worst {WorstMs[i]:F1}, {CallsPerFrame[i]:F1}x)");
            }
            return sb.ToString();
        }
    }
}

namespace GK2Mounts
{
    // The panel's two experiments (session only): they tell the mod's share of a slow frame from the game's.
    //   ExpNoShuffle  standing donkeys skip their idle shuffle (and foals their following): the mod's movement work off.
    //   ExpHideViews  every standing donkey's view stops rendering and animating (forceRenderingOff, Animator off):
    //                 what drawing and animating the views costs the game. Undone when switched off.
    internal static partial class Mount
    {
        internal sealed partial class Instance
        {
            private Wgo expView;
            private readonly System.Collections.Generic.List<UnityEngine.Renderer> expRenderers = new System.Collections.Generic.List<UnityEngine.Renderer>();
            private readonly System.Collections.Generic.List<UnityEngine.Behaviour> expAnimators = new System.Collections.Generic.List<UnityEngine.Behaviour>();

            internal void ApplyPerfExperiment()
            {
                bool want = Perf.ExpHideViews && donkey != null && donkey;
                if (want && ReferenceEquals(expView, donkey))
                {
                    return;
                }
                if (!want && expView == null)
                {
                    return;
                }
                foreach (UnityEngine.Renderer r in expRenderers)
                {
                    if (r != null && r) r.forceRenderingOff = false;
                }
                foreach (UnityEngine.Behaviour a in expAnimators)
                {
                    if (a != null && a) a.enabled = true;
                }
                expRenderers.Clear();
                expAnimators.Clear();
                expView = null;
                if (!want)
                {
                    return;
                }
                foreach (UnityEngine.Renderer r in donkey.GetComponentsInChildren<UnityEngine.Renderer>(includeInactive: true))
                {
                    if (!r.forceRenderingOff)
                    {
                        r.forceRenderingOff = true;
                        expRenderers.Add(r);
                    }
                }
                foreach (UnityEngine.Animator a in donkey.GetComponentsInChildren<UnityEngine.Animator>(includeInactive: true))
                {
                    if (a.enabled)
                    {
                        a.enabled = false;
                        expAnimators.Add(a);
                    }
                }
                expView = donkey;
            }
        }
    }
}
