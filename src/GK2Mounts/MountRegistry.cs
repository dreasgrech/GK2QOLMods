using System;
using System.Collections.Generic;
using BepInEx;
using GK2Mounts.Api;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Mounts
{
    // The static front of the mount: the registry of instances (Mount.Instance, MountInstance.cs), which one is ridden,
    // which one is bound, and the per-frame loop. docs/mounts-instances.md has the design.
    //
    // Binding: the static classes (Mount, Cargo, Lantern, LampHang, Colors) forward to the bound instance, and
    // Tuned<T>.Value reads the bound instance's overrides first. The bound instance is the ridden one, except inside
    // a Bind scope, which the mod opens around everything it does to another instance (its frame, spawning, a panel
    // or API action). Everything the game calls into (pickups, the interact key, the pose patches) runs with the
    // ridden one bound.
    internal static partial class Mount
    {
        private static readonly List<Instance> instances = new List<Instance>();
        private static Instance bound;

        // Each owner numbers its own instances (docs/mounts-persistence.md, "Identity"): owner#id is the identity, two
        // mods can both have a #2. The next number per owner is kept with the save, so a number is never handed out
        // twice in a save's life (a mod may still ask for one itself, MountSpawn.Id). byFullId: Find(owner, id).
        private static readonly Dictionary<string, int> nextIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Instance> byFullId = new Dictionary<string, Instance>(StringComparer.Ordinal);

        internal static string FullIdOf(string owner, int id) => owner + "#" + id;

        // In use by a live instance, or held by a dormant record of that owner.
        internal static bool IdTaken(string owner, int id) => byFullId.ContainsKey(FullIdOf(owner, id)) || IsDormant(owner, id);

        internal static int NextId(string owner)
        {
            int n = nextIds.TryGetValue(owner, out int v) ? v : 1;
            while (IdTaken(owner, n))
            {
                n++;
            }
            nextIds[owner] = n + 1;
            return n;
        }

        internal static void NoteIdUsed(string owner, int id)
        {
            if (!nextIds.TryGetValue(owner, out int v) || v <= id)
            {
                nextIds[owner] = id + 1;
            }
        }

        internal static Instance FindInstance(string owner, int id)
        {
            return owner != null && byFullId.TryGetValue(FullIdOf(owner, id), out Instance i) ? i : null;
        }

        internal static Instance FindInstance(string owner, string key)
        {
            if (owner == null || string.IsNullOrEmpty(key))
            {
                return null;
            }
            foreach (Instance i in instances)
            {
                if (!i.Ephemeral && i.OwnerId == owner && i.Key == key)
                {
                    return i;
                }
            }
            return null;
        }

        internal static bool KeyTaken(string owner, string key) => FindInstance(owner, key) != null || DormantKeyTaken(owner, key);

        // Every instance, the ridden and the ephemeral summon included.
        internal static IReadOnlyList<Instance> All => instances;

        // Bumped when the set of instances or any parent link changes: the caches built from them (Children, the API's
        // lists) are made again on their next use.
        internal static int structureVersion;

        // This frame's instances by scene (built once at the top of the frame): the per-frame neighbour lookups
        // (NearOther, the keeper's prompt) walk only the keeper's scene, not every instance in the world.
        private static readonly Dictionary<string, List<Instance>> byScene = new Dictionary<string, List<Instance>>(StringComparer.Ordinal);
        private static readonly List<Instance> noInstances = new List<Instance>();

        internal static List<Instance> InScene(string sceneId)
        {
            return sceneId != null && byScene.TryGetValue(sceneId, out List<Instance> l) ? l : noInstances;
        }

        private static void BuildByScene()
        {
            foreach (List<Instance> l in byScene.Values)
            {
                l.Clear();
            }
            foreach (Instance i in instances)
            {
                if (i.SceneId == null)
                {
                    continue;
                }
                if (!byScene.TryGetValue(i.SceneId, out List<Instance> l))
                {
                    byScene[i.SceneId] = l = new List<Instance>();
                }
                l.Add(i);
            }
        }

        internal static Instance Ridden { get; private set; }

        // Set by the keeper-animation prefixes while mounted (Patches.cs), read by WhyMustDismount while a script holds
        // control, cleared when it gives control back: what the script staged with the keeper.
        internal static string ScriptStaged;

        internal static void NoteScriptStaged(string what)
        {
            PlayerController pc = MainGame.Instance != null ? MainGame.PlayerController : null;
            if (pc != null && !pc.IsControlEnabledByType(TakenControlType.ByFlow) && ScriptStaged == null)
            {
                ScriptStaged = what;
            }
        }

        internal static Instance Current => bound ?? Ridden;

        internal static bool IsRegistered(Instance i) => i != null && instances.Contains(i);

        internal readonly struct Binding : IDisposable
        {
            private readonly Instance previous;

            internal Binding(Instance previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                bound = previous;
            }
        }

        internal static Binding Bind(Instance i)
        {
            var b = new Binding(bound);
            bound = i;
            return b;
        }

        // Runs a for every instance, each bound in turn (bind false: only for work that reads no settings). One
        // instance failing does not stop the others.
        internal static void ForEachInstance(Action<Instance> a, bool bind = true)
        {
            foreach (Instance i in instances.ToArray())
            {
                try
                {
                    if (bind)
                    {
                        using (Bind(i))
                        {
                            a(i);
                        }
                    }
                    else
                    {
                        a(i);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Mount: {i.Describe()}: {e.Message}");
                }
            }
        }

        internal static void Register(Instance i)
        {
            if (i.Handle == null)
            {
                i.Handle = new MountHandle(i);
            }
            instances.Add(i);
            byFullId[i.FullId] = i;
            structureVersion++;
            if (!i.Ephemeral)
            {
                Mounts.RaiseInstanceSpawned(i.Handle);
            }
        }

        internal static void Unregister(Instance i)
        {
            if (!instances.Remove(i))
            {
                return;
            }
            if (byFullId.TryGetValue(i.FullId, out Instance same) && same == i)
            {
                byFullId.Remove(i.FullId);
            }
            structureVersion++;
            if (Ridden == i)
            {
                Ridden = null;
            }
            if (!i.Ephemeral)
            {
                Mounts.RaiseInstanceRemoved(i.Handle);
            }
        }

        // ------------------------------------------------------------------ forwarders (the ridden or bound instance)

        internal static bool SuppressScripts => Instance.SuppressScripts;
        public static bool IsMounted => Ridden != null;
        public static MountStyle CurrentStyle => Ridden != null ? Ridden.CurrentStyle : MountStyle.Ride;
        internal static StyleSettings S => Current != null ? Current.S : Plugin.For(MountStyle.Ride);
        public static bool SideView => Ridden != null;
        public static AnimationState RiderPoseState => (AnimationState)(int)S.Pose.Value;
        public static Vector2 RiderFacing(Vector2 requested) => Ridden != null ? Ridden.RiderFacing(requested) : requested;
        internal static Wgo Donkey => Ridden?.Donkey;
        internal static bool FacingRight => Ridden == null || Ridden.FacingRight;
        internal static Vector3 TorsoShiftWorld => Instance.TorsoShiftWorld;
        public static bool PoseBlocksMovement => Instance.PoseBlocksMovement;
        internal static float BackHeightPx(Sprite donkeyBody) => Instance.BackHeightPx(donkeyBody);
        internal static void Say(string text) => Instance.Say(text);
        public static string CartSlopeStatus() => OnRidden(r => r.CartSlopeStatus()) ?? "";
        public static void RefreshSpeed() => OnRidden(r => { r.RefreshSpeed(); return ""; });
        public static void RefreshSeat() => OnRidden(r => { r.RefreshSeat(); return ""; });
        public static void RefreshLeg() => OnRidden(r => { r.RefreshLeg(); return ""; });
        public static void ApplyPoseNow() => OnRidden(r => { r.ApplyPoseNow(); return ""; });
        public static void LogGeometry() => OnRidden(r => { r.LogGeometry(); return ""; });

        private static string OnRidden(Func<Instance, string> f)
        {
            Instance r = Ridden;
            if (r == null)
            {
                return null;
            }
            using (Bind(r))
            {
                return f(r);
            }
        }
        internal static void OnCargoChanged() => Current?.OnCargoChanged();
        internal static bool TryCartRear(out Vector3 rear, out Vector3 back, out string detail)
        {
            rear = back = Vector3.zero;
            detail = "";
            return Ridden != null && Ridden.TryCartRear(out rear, out back, out detail);
        }

        // ------------------------------------------------------------------ per frame

        private static object lastGameSave;

        // The game ready for mods (Mounts.GameReady): the save whose kept donkeys are back. startedSave: the game said
        // it has started (MainGame.OnGameStarted, after its scenes loaded). Until both, spawning is refused, so no mod
        // takes a number a kept donkey of this save has, and nothing is spawned into a world still loading.
        private static object readySave, startedSave;
        private static float newSaveSeenAt;
        private static int newSaveSeenFrame;

        // Without the game's own signal (a game update, a load that failed half-way) the game is ready this long
        // after its save appeared in a scene. Long: a slow PC can still be loading scenes well after that moment, and
        // the signal comes once they are loaded.
        private const float GameReadyFallbackSeconds = 90f;

        internal static bool IsGameReady
        {
            get
            {
                try
                {
                    return readySave != null && MainGame.Instance != null && ReferenceEquals(MainGame.Instance.GameSave, readySave)
                        && Instance.IsInGame(out _, out _);
                }
                catch
                {
                    return false;
                }
            }
        }

        // MainGame.OnGameStarted (subscribed by Plugin): fires after the game has loaded its scenes; ours runs first, as
        // every mod using the API loads after GK2 Mounts (BepInDependency).
        internal static void OnGameStarted()
        {
            try
            {
                startedSave = MainGame.Instance != null ? MainGame.Instance.GameSave : null;
            }
            catch
            {
            }
        }

        private static void BecomeReady(object save, bool started)
        {
            float waited = Time.unscaledTime - newSaveSeenAt;
            int frames = Time.frameCount - newSaveSeenFrame;
            if (!started)
            {
                Plugin.Log.LogWarning($"Mounts: the game never said it had started (MainGame.OnGameStarted) {waited:F0} s after its save appeared; going ahead.");
            }
            nextIds.Clear();
            ClearDormant();
            List<MountHandle> restored = null;
            try
            {
                restored = RestoreFromSideFile();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Persistence: restoring the kept donkeys and carts failed: " + e);
            }
            readySave = save; // before the events: their handlers may spawn
            Plugin.Log.LogInfo($"Mounts: game ready ({(started ? "the game started" : "fallback")}, {frames} frame(s) and {waited:F1} s after its save appeared); mods may spawn now.");
            if (restored != null && restored.Count > 0)
            {
                Mounts.RaiseInstancesRestored(restored);
            }
            Mounts.RaiseGameReady();
        }

        internal static void ClearDormant() => dormant.Clear();

        public static void Tick()
        {
            Perf.BeginFrame();
            using (Perf.Measure(Perf.S.Tick))
            {
                TickInner();
            }
        }

        private static void TickInner()
        {
            if (!Instance.IsInGame(out PlayerController pc, out GameScene scene))
            {
                bool gone;
                try
                {
                    gone = MainGame.Instance == null || MainGame.PlayerData == null || MainGame.Instance.GameSave == null;
                }
                catch
                {
                    gone = true;
                }
                InstanceControls.Clear();
                if (gone)
                {
                    if (instances.Count > 0)
                    {
                        ForceCleanup("left the game");
                    }
                }
                else if (Ridden != null)
                {
                    // No current scene for a moment (a scene loading): the ride ends as it always did; the instances
                    // standing in the world stay, their views come back with their scenes.
                    Plugin.Log.LogInfo("Mount: no current scene; the ride ends, the donkeys and carts in the world stay.");
                    EndRideOnly("left the game");
                }
                return;
            }
            // A game was loaded (from the menu, or another save without passing it): the old game's instances go at
            // once. Once the game has started, the ones kept with this save come back (Persistence.cs) and mods are
            // told they may spawn (Mounts.GameReady).
            object save = MainGame.Instance.GameSave;
            if (!ReferenceEquals(save, lastGameSave))
            {
                if (lastGameSave != null && instances.Count > 0)
                {
                    // Their cargo belongs to the old game: setting it down here would put it into the new one.
                    ForceCleanup("another game was loaded", setDownCargo: false);
                }
                lastGameSave = save;
                newSaveSeenAt = Time.unscaledTime;
                newSaveSeenFrame = Time.frameCount;
            }
            if (!ReferenceEquals(save, readySave))
            {
                bool started = ReferenceEquals(save, startedSave);
                if (!started && Time.unscaledTime - newSaveSeenAt < GameReadyFallbackSeconds)
                {
                    return; // still loading: nothing of this game exists yet, and nothing may be spawned
                }
                BecomeReady(save, started);
            }

            BuildByScene();
            if (Ridden == null && Time.frameCount <= Instance.gearHideUntilFrame)
            {
                // A pose's animator state switches its gear on every frame and needs a frame or two to be left after
                // a dismount; keep that gear hidden until it has (see PoseGearLayers).
                Instance.HidePoseGear(pc, Instance.lastRiderPose);
            }
            if (Ridden != null)
            {
                Instance r = Ridden;
                using (Bind(r))
                {
                    try
                    {
                        using (Perf.Measure(Perf.S.Ride))
                        {
                            r.Tick(pc, scene);
                        }
                    }
                    catch (Exception e)
                    {
                        // The ride ends (as it always did on an error); instances standing in the world are not touched,
                        // and a ridden one stays registered and stands again where it was.
                        Plugin.Log.LogError($"Mount update failed; dismounting. {e}");
                        EndRideOnly("error");
                    }
                }
            }
            foreach (Instance i in instances.ToArray())
            {
                if (i.IsRidden || i.Ephemeral)
                {
                    continue;
                }
                using (Bind(i))
                {
                    try
                    {
                        using (Perf.Measure(Perf.S.Stand))
                        {
                            i.StandTick(pc);
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Mount: {i.Describe()}: frame update failed: {e.Message}");
                    }
                }
            }
            using (Perf.Measure(Perf.S.Spawn))
            {
                ProcessSpawnQueue(pc); // the views asked for this frame, nearest first, a few a frame (Tiers.cs)
            }
            using (Perf.Measure(Perf.S.Controls))
            {
                InstanceControls.Tick(pc);
            }
        }

        // The ride ends the forced way (error, no scene): the ridden instance's view goes; a registered one stays
        // registered and is spawned again where it was, the summon is gone with its cargo handed back.
        private static void EndRideOnly(string reason)
        {
            Instance r = Ridden;
            if (r == null)
            {
                return;
            }
            using (Bind(r))
            {
                try
                {
                    r.ForceCleanup(reason);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Mount: ending the ride failed: " + e.Message);
                }
            }
            if (Ridden == r)
            {
                Ridden = null;
            }
        }

        // ------------------------------------------------------------------ the old summon and the API

        // Mounts.TryMount: an ephemeral instance under the keeper. Null when mounted, else the reason.
        internal static string TryMountFromApi(MountStyle style, bool showMessage)
        {
            if (Ridden != null)
            {
                return "Already mounted.";
            }
            if (!IsGameReady)
            {
                return "Not yet: the game is still loading.";
            }
            var i = new Instance(NextId(Plugin.Guid), Plugin.Guid, ephemeral: true, hasDonkey: true, hasCart: style == MountStyle.Cart);
            Register(i);
            string reason;
            using (Bind(i))
            {
                reason = i.TryMountFromApi(style, showMessage);
            }
            if (reason != null)
            {
                Unregister(i);
            }
            return reason;
        }

        internal static void DismountFromApi(string reason)
        {
            Instance r = Ridden;
            if (r == null)
            {
                return;
            }
            using (Bind(r))
            {
                r.DismountFromApi(reason);
            }
        }

        internal static void OnFeatureChanged(MountFeature feature, bool allowed)
        {
            ForEachInstance(i => i.OnFeatureChanged(feature, allowed));
        }

        internal static void OnTuningChanged(MountStyle? style)
        {
            if (!style.HasValue)
            {
                Colors.MarkDirty(); // a Colors (or behaviour) override: rebuilt at the next frame
            }
            ForEachInstance(i => i.OnTuningChanged(style));
        }

        // Leaving the game, the mod switched off, an error: every instance goes (the ride ends first).
        public static void ForceCleanup(string reason, bool setDownCargo = true)
        {
            Instance r = Ridden;
            if (r != null)
            {
                using (Bind(r))
                {
                    try
                    {
                        r.ForceCleanup(reason);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning("Mount: cleanup of the ride failed: " + e.Message);
                    }
                }
            }
            Ridden = null;
            InstanceControls.Clear();
            bool inGame = Instance.IsInGame(out _, out _);
            foreach (Instance i in instances.ToArray())
            {
                using (Bind(i))
                {
                    try
                    {
                        if (setDownCargo && inGame && !i.Ephemeral && i.CargoRig.Count > 0 && !Plugin.Quitting)
                        {
                            Vector3 at = i.CartPoint();
                            foreach (Item it in i.CargoRig.ItemsNow())
                            {
                                if (!MainGame.Instance.dropSystem.DropItem(it, i.SceneId, at))
                                {
                                    Plugin.Log.LogWarning($"Cargo: {it.id} on {i.Describe()} could not be set down ({reason}); it is LOST.");
                                }
                            }
                            Plugin.Log.LogInfo($"Cargo: {i.Describe()}'s cargo set down where it stood ({reason}).");
                        }
                        i.Discard();
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Mount: removing {i.Describe()} failed: {e.Message}");
                    }
                }
                Unregister(i);
            }
            if (instances.Count > 0)
            {
                instances.Clear();
            }
            byFullId.Clear();
            structureVersion++;
            lent.Clear();
        }

        internal static void OnVisualPartsLoaded(Wgo wgo)
        {
            Instance i = OwnerOf(wgo);
            if (i == null)
            {
                return;
            }
            using (Bind(i))
            {
                i.OnVisualPartsLoaded(wgo);
            }
        }

        // Wgo.OnDestroy prefix: any Wgo; an instance whose view it is puts everything back first.
        internal static void OnWgoDestroyed(Wgo wgo)
        {
            Instance i = OwnerOf(wgo);
            if (i == null)
            {
                return;
            }
            using (Bind(i))
            {
                i.OnViewDestroyedByGame();
            }
        }

        // The instance whose view this is (reference comparison: works while the object is being destroyed). Never
        // the game's own donkey: its Wgo is not one the mod spawned.
        private static Instance OwnerOf(Wgo wgo)
        {
            if (wgo == null)
            {
                return null;
            }
            foreach (Instance i in instances)
            {
                if (ReferenceEquals(i.Donkey, wgo))
                {
                    return i;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ spawning (API, panel)

        // What to spawn (Mounts.Spawn, the panel, the restore). The foal properties (Foals.cs): the view's scale, whether
        // it can be ridden, the parent it stays with and whether it goes when the parent does. SceneId: another scene
        // than the keeper's (null: his, or a child's parent's). Snap: the height from the ground under the point, only
        // possible in the keeper's scene (a restore keeps the saved height). Persist and Key: kept with the save
        // (Persistence.cs). Id: the owner's number asked for (0: its next). Register false: the caller registers it
        // once it is complete. Restoring: the restore itself, before the game is ready for mods.
        internal sealed class SpawnArgs
        {
            internal string Owner;
            internal Vector3 Position;
            internal bool HasDonkey = true, HasCart, FacingRight = true, Rideable = true, RemoveWithParent = true;
            internal bool Snap = true, Persist, Register = true, Restoring;
            internal float Scale = 1f;
            internal Instance Parent;
            internal string SceneId, Key;
            internal int Id;
        }

        // A donkey, a donkey with its cart, or a free cart. Null when refused (why says so). The combinations the ride
        // code can't take are refused here.
        internal static Instance Spawn(SpawnArgs a, out string why)
        {
            why = null;
            string ownerId = a.Owner;
            Vector3 position = a.Position;
            bool hasDonkey = a.HasDonkey, hasCart = a.HasCart, rideable = a.Rideable;
            float scale = a.Scale;
            Instance parent = a.Parent;
            if (string.IsNullOrEmpty(ownerId))
            {
                why = "An owner id is required.";
                return null;
            }
            if (!a.Restoring && !IsGameReady)
            {
                why = "Not until the game is ready (Mounts.GameReady): the save's kept donkeys come back first.";
                return null;
            }
            if (a.Id < 0)
            {
                why = "An id is a number from 1 up (0: the next free one).";
                return null;
            }
            if (a.Id > 0 && IdTaken(ownerId, a.Id))
            {
                why = $"{ownerId} already has #{a.Id}{(IsDormant(ownerId, a.Id) ? " (kept with the save, dormant)" : "")}.";
                return null;
            }
            if (!string.IsNullOrEmpty(a.Key) && KeyTaken(ownerId, a.Key))
            {
                why = $"{ownerId} already has one with the key '{a.Key}'.";
                return null;
            }
            if (float.IsNaN(scale) || scale < 0.2f || scale > 2f)
            {
                why = "Scale must be between 0.2 and 2.";
                return null;
            }
            if (Mathf.Abs(scale - 1f) > 0.001f && (rideable || hasCart || !hasDonkey))
            {
                why = "Only a donkey without a cart that can't be ridden can have another scale (the ride and the cart are made for a full-size donkey).";
                return null;
            }
            if (parent != null)
            {
                if (!IsRegistered(parent) || parent.Ephemeral || !parent.HasDonkey)
                {
                    why = "The parent must be a donkey in the world (not a cart, not the summon).";
                    return null;
                }
                if (parent.Parent != null)
                {
                    why = "The parent has a parent itself (no chains).";
                    return null;
                }
                if (!hasDonkey)
                {
                    why = "A cart can't have a parent.";
                    return null;
                }
            }
            if (!Instance.IsInGame(out PlayerController pc, out GameScene scene))
            {
                why = "Not in a game.";
                return null;
            }
            // The summon's own checks on the definition (General.MountWgoId), before any WgoData is built from it.
            string wgoId = (Plugin.MountWgoId.Value ?? "").Trim();
            WGODef def = GameBalance.Me?.GetDataOrNull<WGODef>(wgoId);
            if (def == null)
            {
                why = "There is no such animal (" + wgoId + ").";
                return null;
            }
            why = Instance.WhyDefinitionTouchesTheSave(def);
            if (why != null)
            {
                why = "Can't use a " + wgoId + ": " + why;
                return null;
            }
            string inScene = !string.IsNullOrEmpty(a.SceneId) ? a.SceneId : parent != null ? parent.SceneId : scene.Id;
            if (inScene != scene.Id)
            {
                GameSceneData sd = null;
                try
                {
                    sd = MainGame.WorldData.GetGameSceneDataById(inScene);
                }
                catch
                {
                }
                if (sd == null)
                {
                    why = "There is no scene " + inScene + ".";
                    return null;
                }
            }
            bool snapHere = a.Snap && inScene == scene.Id;
            int id = a.Id > 0 ? a.Id : NextId(ownerId);
            NoteIdUsed(ownerId, id);
            var i = new Instance(id, ownerId, ephemeral: false, hasDonkey: hasDonkey, hasCart: hasCart)
            {
                Position = snapHere ? SnapToGround(position) : position,
                SceneId = inScene,
                ViewScale = scale,
                Rideable = rideable,
                Parent = parent,
                RemoveWithParent = a.RemoveWithParent,
                Persist = a.Persist,
                Key = string.IsNullOrEmpty(a.Key) ? null : a.Key,
            };
            using (Bind(i))
            {
                i.MoveTo(i.Position, a.FacingRight ? 1f : -1f, snapHere);
            }
            if (a.Register)
            {
                Register(i);
            }
            if (!OwnerPresent(ownerId))
            {
                Plugin.Log.LogWarning($"Mount: {ownerId} is not a loaded plugin's GUID (a typo?); its donkeys would go dormant after a reload.");
            }
            DiagLog($"Mount: {(a.Restoring ? "restored" : "spawned")} {i.Describe()}{(i.Key != null ? $" '{i.Key}'" : "")} at {i.Position:F2} in {i.SceneId}{(scale != 1f ? $", scale {scale:F2}" : "")}{(!rideable ? ", not rideable" : "")}{(parent != null ? $", child of {parent.FullId}" : "")}{(a.Persist ? ", kept with saves" : "")}.");
            return i;
        }


        internal static void Remove(Instance i, string why)
        {
            if (!IsRegistered(i))
            {
                return;
            }
            if (i.IsRidden)
            {
                DismountFromApi(why);
            }
            using (Bind(i))
            {
                i.Discard();
            }
            Unregister(i);
            Plugin.Log.LogInfo($"Mount: removed {i.Describe()} ({why}); {i.CargoRig.Count} item(s) on it are gone with it.");
            // Its children go with it (MountSpawn.RemoveWithParent), or stay as orphans shuffling where they are.
            foreach (Instance c in new List<Instance>(All))
            {
                if (c.Parent != i)
                {
                    continue;
                }
                if (c.RemoveWithParent)
                {
                    Remove(c, $"its parent {i.FullId} was removed");
                }
                else
                {
                    c.Parent = null;
                }
            }
        }

        // The ground under a point (the navigation mesh, else the ground raycast); the point's own height if neither.
        internal static Vector3 SnapToGround(Vector3 p)
        {
            float g = Instance.GroundHeight(p, out _);
            if (!float.IsNaN(g))
            {
                p.y = g;
            }
            return p;
        }

        internal static GameScene LoadedScene(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            try
            {
                foreach (GameScene s in LazySingleton<GameSceneManager>.Instance.LoadedGameScenes)
                {
                    if (s != null && s.Id == id)
                    {
                        return s;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        // A setting that belongs to the cart (it travels with the cart when hitched and unhitched): the Cart*, Cargo*
        // and Lantern* keys, the colours of the Cart and Lantern regions and the lamp's three colours.
        internal static bool IsCartKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            if (key.StartsWith("Cart", StringComparison.Ordinal) || key.StartsWith("Cargo", StringComparison.Ordinal) || key.StartsWith("Lantern", StringComparison.Ordinal))
            {
                return true;
            }
            if (key == "GlowColor" || key == "LitGlassTint" || key == "CharacterLightColor")
            {
                return true;
            }
            foreach (MountColorRegion r in Colors.RegionList)
            {
                if (r.Area != "Donkey" && key.StartsWith(r.Key, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ saves

        // Right before a save: every instance's cargo (the old summon's is handed back by the save patch as before)
        // is added as ground drops at its cart, so the save has it. Right after, those drops are taken away again and
        // the items stay on the cart. A scene that is not loaded takes the drops into its queue, from where they are
        // taken back the same way (GameSceneData.RemoveDrop clears both lists). A stack would be dropped as copies,
        // which stay on the ground and leave the cart.
        private static readonly List<(Instance I, DropData D, string Scene)> lent = new List<(Instance, DropData, string)>();

        internal static void LendCargoToSave()
        {
            lent.Clear();
            foreach (Instance i in instances.ToArray())
            {
                if (i.Ephemeral || i.CargoRig.Count == 0)
                {
                    continue;
                }
                using (Bind(i))
                {
                    Vector3 at = i.CartPoint();
                    GameSceneData sd = null;
                    try
                    {
                        sd = MainGame.WorldData.GetGameSceneDataById(i.SceneId);
                    }
                    catch
                    {
                    }
                    int kept = 0, left = 0;
                    foreach (Item it in i.CargoRig.ItemsNow())
                    {
                        try
                        {
                            if (!MainGame.Instance.dropSystem.DropItem(it, i.SceneId, at))
                            {
                                Plugin.Log.LogWarning($"Cargo: the game refused to put {it.id} of {i.Describe()} into the save; it is not in this save.");
                                continue;
                            }
                            // The same Item (a single big item is dropped as itself): in the scene's drops, or in its queue
                            // when the scene is not loaded. Not found: split into copies (a stack), which stay dropped.
                            DropData d = sd?.droppedItems.Find(x => ReferenceEquals(x.Item, it)) ?? sd?.queuedDrops.Find(x => ReferenceEquals(x.Item, it));
                            if (d != null)
                            {
                                lent.Add((i, d, i.SceneId));
                                kept++;
                            }
                            else
                            {
                                i.CargoRig.Forget(it); // split into copies: those live on the ground now
                                left++;
                            }
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogWarning($"Cargo: putting {it.id} of {i.Describe()} into the save failed: {e.Message}");
                        }
                    }
                    Plugin.Log.LogInfo($"Cargo: {i.Describe()}: {kept} item(s) set down at the cart for the save{(left > 0 ? $", {left} left on the ground (dropped as copies)" : "")}.");
                }
            }
        }

        // The GUIDs of the items an instance lent to the save being written (Persistence.cs keeps them).
        internal static List<string> LentGuidsOf(Instance i)
        {
            var l = new List<string>();
            foreach (var x in lent)
            {
                if (x.I == i && x.D?.Item?.UniqueId != null)
                {
                    l.Add(x.D.Item.UniqueId.Id);
                }
            }
            return l;
        }

        internal static void TakeCargoBackFromSave()
        {
            foreach (var l in lent)
            {
                try
                {
                    MainGame.Instance.dropSystem.RemoveDrop(l.D, l.Scene);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Cargo: taking {l.D?.Item?.id} back onto {l.I.Describe()} after the save failed; it stays on the ground: {e.Message}");
                    try
                    {
                        using (Bind(l.I))
                        {
                            l.I.CargoRig.Forget(l.D.Item);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            if (lent.Count > 0)
            {
                Plugin.Log.LogInfo($"Cargo: {lent.Count} item(s) back on their carts after the save.");
            }
            lent.Clear();
        }
    }
}
