using System.Collections.Generic;
using UnityEngine;

namespace GK2Mounts
{
    // How much each standing instance does, by how close it is to the keeper (docs/mounts-performance.md, "Levels").
    // Measured 2026-10-01: at 363 donkeys the game spent about 11 ms of a 13 ms frame updating, animating and culling
    // their views (the mod 2 ms), most of them far away or off screen; and spawning 50 views in one frame hitched.
    //   Active  ridden, edited on the panel, within ActiveRadius of the keeper, or on screen (with a margin): everything.
    //   Idle    in the keeper's scene, off screen, up to SleepRadius: the view stays (no pop-in when the camera turns)
    //           but stands frozen: no shuffle, no posing, its animators off.
    //   Asleep  beyond SleepRadius (awake again inside WakeRadius): the view is released, the same release a scene
    //           change does (cargo, colours and settings stay with the instance); once a second a foal checks whether
    //           its parent stands still somewhere and catches up with it.
    // An instance in a scene that isn't loaded has no view anyway (MountInstance, the half-second viewless tick).
    // New views are made at most Lod.SpawnsPerFrame a frame, nearest first (Mount.ProcessSpawnQueue).
    internal static class Lod
    {
        // The panel's Debug tab changes these for the session (defaults measured to keep the yard lively).
        internal static bool On = true;
        internal static float ActiveRadius = 15f;
        internal static float SleepRadius = 35f;   // 45 kept 138 idle views alive at 363 donkeys (2026-10-01)
        internal static float WakeRadius = 30f;
        internal static float ScreenMargin = 0.3f;   // a fraction of the screen beyond each edge that still counts as on screen
        internal static int SpawnsPerFrame = 1;    // one view costs 1 to 4 ms to make; 3 a frame made 12 ms frames

        // For the [perf] line: how many are in each level, and views made / released in the last second.
        internal static int ActiveCount, IdleCount, AsleepCount;
        internal static int Spawned, Released;
    }

    internal static partial class Mount
    {
        internal enum Tier
        {
            Active,
            Idle,
            Asleep,
        }

        // Views asked for this frame; the registry makes the nearest few after the frame's instance loop.
        private static readonly List<(Instance I, float D)> spawnQueue = new List<(Instance, float)>();

        internal static void QueueSpawn(Instance i, float distance) => spawnQueue.Add((i, distance));

        private static void ProcessSpawnQueue(PlayerController pc)
        {
            if (spawnQueue.Count == 0)
            {
                return;
            }
            spawnQueue.Sort((a, b) => a.D.CompareTo(b.D));
            int n = Mathf.Max(1, Lod.SpawnsPerFrame);
            for (int k = 0; k < spawnQueue.Count && n > 0; k++)
            {
                Instance i = spawnQueue[k].I;
                if (!IsRegistered(i))
                {
                    continue;
                }
                using (Bind(i))
                {
                    if (i.SpawnQueued(pc))
                    {
                        n--;
                    }
                }
            }
            spawnQueue.Clear();
        }

        internal sealed partial class Instance
        {
            internal Tier tier = Tier.Active;
            private float nextTierCheck;
            private float nextAsleepTick;
            private readonly List<Behaviour> quietAnimators = new List<Behaviour>();
            private readonly List<Renderer> quietRenderers = new List<Renderer>();

            // Check the level at once on the next frame (moved by the API or the panel, caught up, came to the keeper).
            internal void TierNow() => nextTierCheck = 0f;

            private Tier WantTier(PlayerController pc)
            {
                if (!Lod.On || mounted || Ephemeral || ReferenceEquals(this, TuningWindow.Editing))
                {
                    return Tier.Active;
                }
                string keeperScene = pc.PlayerData.currentGameSceneId;
                if (SceneId != keeperScene)
                {
                    return Tier.Active; // another scene: no view anyway; the scene's own loading decides
                }
                Vector3 k = pc.PlayerData.position.Value;
                float dx = Position.x - k.x, dz = Position.z - k.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                // Leaving a level needs a little more than entering it, so a donkey at a border doesn't flicker.
                float activeR = tier == Tier.Active ? Lod.ActiveRadius + 3f : Lod.ActiveRadius;
                float margin = tier == Tier.Active ? Lod.ScreenMargin + 0.1f : Lod.ScreenMargin;
                if (d <= activeR || OnScreen(BodyPoint(), margin))
                {
                    return Tier.Active;
                }
                float sleepR = tier == Tier.Asleep ? Lod.WakeRadius : Lod.SleepRadius;
                return d > sleepR ? Tier.Asleep : Tier.Idle;
            }

            private static bool OnScreen(Vector3 world, float margin)
            {
                try
                {
                    if (CameraSystem.Instance == null)
                    {
                        return true;
                    }
                    Vector3 s = CameraSystem.WorldToScreenPoint(world);
                    float mx = Screen.width * margin, my = Screen.height * margin;
                    return s.z > 0f && s.x > -mx && s.x < Screen.width + mx && s.y > -my && s.y < Screen.height + my;
                }
                catch
                {
                    return true;
                }
            }

            // Four times a second (spread over the instances), at once after TierNow. Returns the level now.
            internal Tier UpdateTier(PlayerController pc)
            {
                if (Time.unscaledTime < nextTierCheck)
                {
                    return tier;
                }
                nextTierCheck = Time.unscaledTime + 0.25f + Random.value * 0.05f;
                Tier want = WantTier(pc);
                if (want == tier)
                {
                    return tier;
                }
                Tier was = tier;
                tier = want;
                if (want != Tier.Active)
                {
                    StopShuffle(); // the step in progress ends where it is
                }
                if (want == Tier.Asleep && donkey != null && donkey)
                {
                    QuietAnimators(false);
                    DestroyDonkey(); // the scene-change release: cargo, colours, settings stay with the instance
                    Lod.Released++;
                }
                else if (want == Tier.Idle)
                {
                    QuietAnimators(true);
                }
                else if (want == Tier.Active)
                {
                    QuietAnimators(false);
                }
                if (was == Tier.Asleep)
                {
                    nextViewlessTick = 0f; // the view is asked for on this frame
                }
                return tier;
            }

            // Idle: the view's animators stop and its renderers are left out of drawing (it stands still where nobody sees
            // it; the game then skips it when culling too); both back when it is active again. Only what was on is
            // switched off, so the reveal after a spawn and a free cart's hidden donkey keep their own renderers.
            private void QuietAnimators(bool quiet)
            {
                if (!quiet)
                {
                    foreach (Behaviour a in quietAnimators)
                    {
                        if (a != null && a) a.enabled = true;
                    }
                    quietAnimators.Clear();
                    foreach (Renderer r in quietRenderers)
                    {
                        if (r != null && r) r.forceRenderingOff = false;
                    }
                    quietRenderers.Clear();
                    return;
                }
                if (donkey == null || !donkey || quietAnimators.Count > 0 || quietRenderers.Count > 0)
                {
                    return;
                }
                foreach (Animator a in donkey.GetComponentsInChildren<Animator>(includeInactive: true))
                {
                    if (a.enabled)
                    {
                        a.enabled = false;
                        quietAnimators.Add(a);
                    }
                }
                foreach (Renderer r in donkey.GetComponentsInChildren<Renderer>(includeInactive: true))
                {
                    if (!r.forceRenderingOff)
                    {
                        r.forceRenderingOff = true;
                        quietRenderers.Add(r);
                    }
                }
            }

            // Asleep, once a second: a foal whose parent stands still elsewhere in the scene goes to it (out of sight,
            // so nobody sees the jump), and wakes if that is near the keeper.
            private void AsleepTick()
            {
                if (Time.unscaledTime < nextAsleepTick)
                {
                    return;
                }
                nextAsleepTick = Time.unscaledTime + 1f;
                Instance p = Parent;
                if (p == null || !IsRegistered(p) || p.SceneId != SceneId || p.IsRidden || !p.Ready)
                {
                    return;
                }
                if (FlatDistance(Position, p.Position) > 3f && ParentStillFor() >= 1.5f)
                {
                    CatchUp("its parent stands somewhere else");
                    TierNow();
                }
            }

            // The registry's spawn queue made room for this one: its view now.
            internal bool SpawnQueued(PlayerController pc)
            {
                GameScene scene = LoadedScene(SceneId);
                if (scene == null || (donkey != null && donkey))
                {
                    return false;
                }
                bool spawned;
                using (Perf.Measure(Perf.S.Spawn))
                {
                    spawned = TrySpawn(pc, scene);
                }
                if (spawned)
                {
                    spawnFailures = 0;
                    Lod.Spawned++;
                    // (A new view: TrySpawn forgot the old view's quieted parts; an idle one is quieted once its parts
                    // are ready, StandTick.)
                }
                else
                {
                    spawnFailures++;
                    nextSpawnTry = Time.unscaledTime + Mathf.Min(60f, Mathf.Pow(2f, spawnFailures - 1));
                }
                return true;
            }
        }
    }
}
