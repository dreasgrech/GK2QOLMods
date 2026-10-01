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

        // Idle donkeys without a cart: their view switched off (SetActive(false), the game's own way of hiding a Wgo),
        // which also stops the game's own per-frame work on it. Off: frozen instead (animators and renderers off, the
        // view active). Carts are always frozen: switching a cart's view on again re-initialises its conditional drawer
        // (which cart meshes show), which the cargo display relies on.
        internal static bool IdleSwitchesOff = true;

        // For the [perf] line: how many are in each level, and views made / released in the last second.
        internal static int ActiveCount, IdleCount, AsleepCount;
        internal static int Spawned, Released, Parked, Reused;
        internal static int MaxSpawnsInAFrame;
        internal static int ReusedTotal, ParkedTotal, SpawnedTotal, ReleasedTotal; // never reset (the per-second ones are)
    }

    // Released views kept for the next donkey that needs one (docs/mounts-performance.md, "View reuse"): building a view
    // cost 1 to 4 ms (the game's Wgo.Spawn, then the parts), every time a donkey woke while the keeper rode through a
    // herd. A released view is put back to the prefab's state (ReleaseView, as for the game's pool) and switched off with
    // SetActive(false), the game's own way of hiding a Wgo (Spawn leaves one inactive; its chunk culling switches them).
    // Taken again by a donkey of the same scene, kind and size: its place and facing set, hidden for the frame its parts
    // are prepared in, switched on. Only donkeys without a cart: a cart's view holds the cargo inventory in its data.
    internal static class ViewPool
    {
        internal const int MaxPerKey = 24;
        private static readonly Dictionary<string, Stack<Wgo>> parked = new Dictionary<string, Stack<Wgo>>(System.StringComparer.Ordinal);

        internal static int Count
        {
            get
            {
                int n = 0;
                foreach (Stack<Wgo> s in parked.Values) n += s.Count;
                return n;
            }
        }

        internal static string Key(string sceneId, string wgoId, float scale) => sceneId + "|" + wgoId + "|" + scale.ToString("F2");

        internal static bool HasRoom(string key) => !parked.TryGetValue(key, out Stack<Wgo> s) || s.Count < MaxPerKey;

        internal static void Park(string key, Wgo v)
        {
            if (!parked.TryGetValue(key, out Stack<Wgo> s))
            {
                parked[key] = s = new Stack<Wgo>();
            }
            s.Push(v);
        }

        internal static bool TryTake(string key, out Wgo v)
        {
            v = null;
            if (!parked.TryGetValue(key, out Stack<Wgo> s))
            {
                return false;
            }
            while (s.Count > 0)
            {
                Wgo w = s.Pop();
                if (w != null && w) // a view whose scene unloaded is gone (the game destroyed it with the scene)
                {
                    v = w;
                    return true;
                }
            }
            return false;
        }

        // Leaving the game, another game loaded, the mod switched off: the parked views go for good.
        internal static void DestroyAll()
        {
            foreach (Stack<Wgo> s in parked.Values)
            {
                foreach (Wgo w in s)
                {
                    try
                    {
                        if (w != null && w) Object.Destroy(w.gameObject);
                    }
                    catch
                    {
                    }
                }
            }
            parked.Clear();
        }
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

        // Made once: List.Sort wraps a comparison in a comparer object on every call on this runtime.
        private static readonly IComparer<(Instance I, float D)> NearestFirst = Comparer<(Instance I, float D)>.Create((a, b) => a.D.CompareTo(b.D));

        private static void ProcessSpawnQueue(PlayerController pc)
        {
            if (spawnQueue.Count == 0)
            {
                return;
            }
            spawnQueue.Sort(NearestFirst);
            int n = Mathf.Max(1, Lod.SpawnsPerFrame);
            int made = 0;
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
                        made++;
                    }
                }
            }
            spawnQueue.Clear();
            Lod.MaxSpawnsInAFrame = Mathf.Max(Lod.MaxSpawnsInAFrame, made); // the self-tests' spawn cap check
        }

        internal sealed partial class Instance
        {
            internal Tier tier = Tier.Active;
            private float nextTierCheck;
            private float nextAsleepTick;
            private readonly List<Behaviour> quietAnimators = new List<Behaviour>();
            private readonly List<bool> quietKeepState = new List<bool>(); // each one's own keepAnimatorStateOnDisable
            private readonly List<Renderer> quietRenderers = new List<Renderer>();

            // Check the level at once on the next frame (moved by the API or the panel, caught up, came to the keeper).
            internal void TierNow() => nextTierCheck = 0f;

            // Two frames after waking: the view's animator's own Direction parameter next to the facing wanted, in the
            // log ([wake]), so a wrong facing after waking shows as a measurement, not only if someone happens to see it.
            private int wakeCheckFrame = -1;

            internal void CheckWake()
            {
                if (wakeCheckFrame < 0 || Time.frameCount < wakeCheckFrame)
                {
                    return;
                }
                wakeCheckFrame = -1;
                if (donkey == null || !donkey)
                {
                    return;
                }
                try
                {
                    foreach (Animator a in donkey.GetComponentsInChildren<Animator>())
                    {
                        if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null)
                        {
                            continue;
                        }
                        foreach (AnimatorControllerParameter p in a.parameters)
                        {
                            if (p.name == "Direction" && p.type == AnimatorControllerParameterType.Float)
                            {
                                DiagLog($"[wake] {Describe()}{(mounted ? " (ridden)" : "")}: animator Direction {a.GetFloat(p.nameHash):F0}, wanted {(riderFacingX > 0f ? "right" : "left")} (data {donkey.Data.MovableDirection.x:F0}).");
                                return;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            // The ridden one, every frame (MountRegistry.Tick): fully awake. Riding never goes through StandTick, where the
            // level is checked, so a donkey mounted while idle kept its frozen, undrawn view: the keeper rode an invisible
            // donkey (found 2026-10-01, the showcase's H on a donkey standing off screen).
            internal void WakeForRide()
            {
                CheckWake();
                if (tier == Tier.Active && quietAnimators.Count == 0 && quietRenderers.Count == 0 && !switchedOff)
                {
                    return;
                }
                tier = Tier.Active;
                QuietAnimators(false);
                nextTierCheck = 0f;
            }

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
                // A foal is never below its parent: while the parent is active (ridden, near the keeper, on screen) the
                // foal trots after it as before, also off screen. Frozen or asleep by its own distance it stopped
                // following, and woke only next to the keeper or by a jump to its parent (the user, 2026-10-01).
                Instance p = Parent;
                if (p != null && p.tier == Tier.Active && p.SceneId == SceneId && IsRegistered(p))
                {
                    return Tier.Active;
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

            // For the panel's Details row: the level, why it is at it (WantTier's reasons, in its order), and what its view
            // does there.
            internal string LevelText()
            {
                string why;
                PlayerData pd = MainGame.Instance != null ? MainGame.PlayerData : null;
                Instance p = Parent;
                switch (tier)
                {
                    case Tier.Active:
                        why = !Lod.On ? "levels are off"
                            : mounted ? "ridden"
                            : Ephemeral ? "the summoned donkey"
                            : ReferenceEquals(this, TuningWindow.Editing) ? "edited on the panel"
                            : pd != null && SceneId != pd.currentGameSceneId ? "in another scene: its scene decides"
                            : p != null && p.tier == Tier.Active && p.SceneId == SceneId ? $"its parent {p.FullId} is active"
                            : $"within {Lod.ActiveRadius:F0} of the keeper or on screen";
                        break;
                    case Tier.Idle:
                        why = $"off screen, within {Lod.SleepRadius:F0}";
                        break;
                    default:
                        why = $"farther than {Lod.SleepRadius:F0}; wakes within {Lod.WakeRadius:F0}";
                        break;
                }
                string view = donkey == null || !donkey ? (tier == Tier.Asleep ? "no view (released)" : "no view yet")
                    : switchedOff ? "view switched off"
                    : quietAnimators.Count > 0 || quietRenderers.Count > 0 ? "view frozen"
                    : "view on";
                return $"Level: {tier.ToString().ToLowerInvariant()} ({why}); {view}.";
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
                    using (Perf.Measure(Perf.S.Release))
                    {
                        ParkOrDestroyView(); // cargo, colours, settings stay with the instance; the view is kept if it can be
                    }
                    Lod.Released++;
                    Lod.ReleasedTotal++;
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
            // Idle with the view switched off (Lod.IdleSwitchesOff): the game's own per-frame work on it stops too.
            private bool switchedOff;

            private void QuietAnimators(bool quiet)
            {
                if (!quiet && switchedOff)
                {
                    switchedOff = false;
                    if (donkey != null && donkey)
                    {
                        donkey.gameObject.SetActive(true);
                        // Switched on, the game's WgoPart.OnEnable runs its ConditionalDrawer again, whose rule for an empty
                        // inventory switches the empty cart body (donkey_cart_empty_sway) on: hidden again, as PrepareParts
                        // does for a donkey without a cart (only those are ever switched off). Found reading the drawer
                        // rules (GK2-carry-facts.txt) before testing, 2026-10-01.
                        foreach (Transform t in donkey.GetComponentsInChildren<Transform>(includeInactive: true))
                        {
                            if (System.Array.IndexOf(CartPartNames, t.name) >= 0 && t.gameObject.activeSelf)
                            {
                                t.gameObject.SetActive(false);
                            }
                        }
                        // The game's AnimationComponent.OnEnable puts its own skin layers back: the colours are checked on this
                        // frame's Colors tick, so a donkey with its own colours doesn't enter the screen in the game's for 0.2 s.
                        ColorsRig.CheckNow();
                        // Switched off, its animator lost its state and parameters (and the game's OnDisable set Idle):
                        // the stand pose (with its walk rate) and the facing are sent again.
                        lastMirrored = (AnimationState)(-1);
                        try
                        {
                            donkey.MainWgoPart?.AnimationComponent?.SetDirection(new Vector2(riderFacingX, 0f));
                        }
                        catch
                        {
                        }
                        wakeCheckFrame = Time.frameCount + 2;
                    }
                    return;
                }
                if (quiet && Lod.IdleSwitchesOff && !HasCart && HasDonkey && !mounted && donkey != null && donkey)
                {
                    if (!switchedOff && quietAnimators.Count == 0 && quietRenderers.Count == 0)
                    {
                        switchedOff = true;
                        donkey.gameObject.SetActive(false);
                    }
                    return;
                }
                if (!quiet)
                {
                    bool any = quietAnimators.Count > 0;
                    for (int k = 0; k < quietAnimators.Count; k++)
                    {
                        Behaviour a = quietAnimators[k];
                        if (a == null || !a) continue;
                        a.enabled = true;
                        if (a is Animator an && k < quietKeepState.Count)
                        {
                            an.keepAnimatorStateOnDisable = quietKeepState[k]; // the pooled part as the game made it
                        }
                    }
                    quietAnimators.Clear();
                    quietKeepState.Clear();
                    if (any)
                    {
                        lastMirrored = (AnimationState)(-1); // the stand pose is sent again on its next frame
                        // The facing too, straight to the animation: one set while the animator was off (the showcase's H
                        // turning a frozen donkey towards the keeper) never reached it, and the data, already holding the
                        // new facing, doesn't tell it again: mounted, the donkey faced away until the first turn
                        // (2026-10-01).
                        try
                        {
                            donkey?.MainWgoPart?.AnimationComponent?.SetDirection(new Vector2(riderFacingX, 0f));
                        }
                        catch
                        {
                        }
                        wakeCheckFrame = Time.frameCount + 2; // and checked: the animator's own facing (CheckWake)
                    }
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
                        // An Animator switched off forgets its state and parameters by default: woken again, the donkey
                        // stood in the controller's default facing while the keeper sat the other way (found 2026-10-01).
                        quietKeepState.Add(a.keepAnimatorStateOnDisable);
                        a.keepAnimatorStateOnDisable = true;
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
                if (p == null || !IsRegistered(p) || p.SceneId != SceneId || p.IsRidden || !p.HasView)
                {
                    return;
                }
                if (FlatDistance(Position, p.Position) > 3f && ParentStillFor() >= 1.5f)
                {
                    CatchUp("its parent stands somewhere else");
                    TierNow();
                }
            }

            // Asleep: the view to the pool (ViewPool) when another donkey can use it, else released for good as before.
            private void ParkOrDestroyView()
            {
                Wgo v = donkey;
                string key = v != null && v ? ViewPool.Key(SceneId, WgoId, ViewScale) : null;
                bool can = key != null && !mounted && !riderAttached && HasDonkey && !HasCart && !Ephemeral
                    && InventoryEmpty(v) && ViewPool.HasRoom(key);
                if (!can)
                {
                    DestroyDonkey();
                    return;
                }
                ReleaseView(); // back to the prefab's state, as for the game's own pool
                donkey = null;
                partsReady = false;
                cartParts.Clear();
                hiddenRenderers.Clear();
                seatPending = false;
                bodySprite = null;
                donkeyBodyRenderer = null;
                try
                {
                    v.gameObject.SetActive(false);
                    ViewPool.Park(key, v);
                    Lod.Parked++;
                    Lod.ParkedTotal++;
                }
                catch
                {
                    try { Object.Destroy(v.gameObject); } catch { }
                }
            }

            private static bool InventoryEmpty(Wgo v)
            {
                try
                {
                    Inventory inv = v.Data?.Inventory;
                    if (inv?.Data?.Inventory == null)
                    {
                        return true;
                    }
                    foreach (Item it in inv.Data.Inventory)
                    {
                        if (it != null && !it.IsEmpty) return false;
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            // TrySpawn: a parked view of this scene, kind and size instead of a new one, when there is one.
            private bool TakeParkedView(GameScene scene, string wgoId)
            {
                if (mounted || !HasDonkey || HasCart || Ephemeral)
                {
                    return false;
                }
                if (!ViewPool.TryTake(ViewPool.Key(scene.Id, wgoId, ViewScale), out Wgo v))
                {
                    return false;
                }
                donkey = v;
                v.Data.Position = VisualConsts.GetRoundedPosXYZ(Position, PixelSize());
                v.Data.MovableDirection = new Vector2(riderFacingX, 0f);
                hiddenRenderers.Clear();
                HideRenderersNow();          // shown again one frame after PrepareParts, as a new view is
                revealFrame = int.MaxValue;
                v.gameObject.SetActive(true);
                Lod.Reused++;
                Lod.ReusedTotal++;
                return true;
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
                    using (Perf.Measure(Perf.S.SpawnView))
                    {
                        spawned = TrySpawn(pc, scene);
                    }
                }
                if (spawned)
                {
                    spawnFailures = 0;
                    Lod.Spawned++;
                    Lod.SpawnedTotal++;
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
