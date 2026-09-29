using System;
using System.Collections.Generic;
using BepInEx;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Mounts
{
    // The mount: a temporary donkey view glued to the keeper, a speed multiplier, and a lowered rider sprite.
    //
    // Nothing here touches saved data. The donkey is a WgoData with isTempObject (the build preview mechanism): it is
    // never added to WorldData or any zone list, its attached flow script is suppressed (see Patches.cs), and it is
    // torn down with Object.Destroy like the preview. SpeedMultiplier lives on the PlayerPhysicalBody MonoBehaviour,
    // the seat offset on the sprite root transform; both are reverted on dismount.
    internal static class Mount
    {
        internal static bool SuppressScripts;

        // Cart, wheel and lantern parts of the donkey prefab (names from the probe report).
        private static readonly string[] CartPartNames =
        {
            "wheel_left", "wheel_right", "light_sway",
            "donkey_cart_wheel_f", "donkey_cart_wheel_b", "donkey_cart_bars",
            "donkey_cart_empty_sway", "donkey_cart_body_sway", "donkey_cart_lantern_sway",
            Cargo.ContainerName, // the cargo meshes' holder, lifted with the cart on slopes
        };

        private static bool mounted;
        private static Wgo donkey;
        private static bool partsReady;
        private static MountStyle activeStyle;

        private static int gearHideUntilFrame = -1; // frames after a dismount during which the rod stays hidden

        // The donkey's renderers are kept off for the first frame after its parts appear: the facing is written to
        // the animator that frame, but the animator mirrors the sprites one evaluation later, so the prefab's default
        // facing (left) would show for one frame when the keeper faces right. Only renderers that were on are
        // restored, so nothing the game keeps off is switched on.
        private static readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private static int revealFrame = -1;

        private static void HideDonkeyForAFrame()
        {
            hiddenRenderers.Clear();
            foreach (Renderer r in donkey.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (r.enabled)
                {
                    r.enabled = false;
                    hiddenRenderers.Add(r);
                }
            }
            revealFrame = UnityEngine.Time.frameCount + 1;
        }

        private static void RevealDonkeyIfDue()
        {
            if (hiddenRenderers.Count == 0 || UnityEngine.Time.frameCount <= revealFrame)
            {
                return;
            }
            foreach (Renderer r in hiddenRenderers)
            {
                if (r != null && r)
                {
                    r.enabled = true;
                }
            }
            hiddenRenderers.Clear();
        }

        // The mounted donkey, for the Debug tab's cargo investigation (null while on foot).
        internal static Wgo Donkey => mounted ? donkey : null;

        // Cargo was added or removed: the prefab's own drawer may have swapped the empty and the loaded cart body, so
        // the list of lifted parts is rebuilt from the parts active now. Every part goes back to its prefab position
        // first, so the current lift is not baked into the new base positions.
        internal static void OnCargoChanged()
        {
            if (donkey == null || !partsReady || activeStyle != MountStyle.Cart)
            {
                return;
            }
            foreach (CartPart p in cartParts)
            {
                if (p.T != null)
                {
                    p.T.localPosition = p.Base;
                    if (p.T.name == Cargo.ContainerName)
                    {
                        p.T.localRotation = Quaternion.identity; // the sway rotation is re-applied next frame
                    }
                }
            }
            Cargo.RefreshViews();
            cartParts.Clear();
            CollectCartParts();
        }
        private static float facingSign = 1f; // +1 when the donkey shows its right-facing sprite, -1 for left
        private static AnimationState lastMirrored = (AnimationState)(-1);
        private static Transform bodySprite; // the donkey's "bdy" sprite; its offset from the root is what centering cancels
        private static SpriteRenderer donkeyBodyRenderer;
        private static SpriteRenderer keeperBodyRenderer;
        private static bool seatPending;
        private static float groundDelta; // donkey ground height minus the keeper's, world units (slopes)
        private static string lastDonkeyGroundSource = "-", lastKeeperGroundSource = "-";
        private static string lastCartGroundSource = "-";
        private static float lastCartGround;
        private static Vector4 lastGroundSample;

        // Cart parts, each lifted to the ground under itself (CartFollowsGround). The cart is not a sprite: wheels,
        // bars, cart body and lantern are separate Object3D meshes (plus two wheel sprites) that extend up to 1.2
        // units behind the donkey along the ground. A flat, upright donkey sprite touches the ground only along its
        // bottom row, so it never shows a slope; the cart's rigid extent does, and the terrain mesh depth-tests
        // over the parts that end up below it. Base = the prefab's local position, Sample = where the part's
        // renderer sits (parent-local), Weight = how much of the ground difference it takes (bars: half).
        private sealed class CartPart
        {
            public Transform T;
            public Vector3 Base;
            public Vector3 Sample;
            public float Weight;
            public float LastLift;
            public float LastGround;
            public string LastSource = "-";
        }
        private static readonly System.Collections.Generic.List<CartPart> cartParts = new System.Collections.Generic.List<CartPart>();
        private static float lastCartRefGround = float.NaN;
        private static CartPart cartBodyPart; // the part whose ground sets the whole cart's shift

        // Ground height at a point, the way the game's WgoMovementAdjustComponent finds it: the scene's navigation
        // mesh first (nearest walkable node as seen from above, accepted if it is under the point), the ground raycast
        // (walkable layers 11/12) only as a fallback. NaN when neither knows.
        private static float GroundHeight(Vector3 p, out string source)
        {
            try
            {
                Pathfinding.RecastGraph graph = MainGame.PlayerController?.SceneRecastGraph;
                if (graph != null)
                {
                    Pathfinding.NearestNodeConstraint c = Pathfinding.NearestNodeConstraint.Walkable;
                    c.distanceMetric = Pathfinding.DistanceMetric.ClosestAsSeenFromAbove();
                    Pathfinding.NNInfo nn = graph.GetNearest(p, c);
                    Vector3 np = nn.position;
                    float dx = np.x - p.x, dz = np.z - p.z;
                    if (dx * dx + dz * dz < 0.05f * 0.05f)
                    {
                        source = "navmesh";
                        return np.y;
                    }
                }
                if (Physics.Raycast(new Ray(p + Vector3.up * 1f, Vector3.down), out RaycastHit hit, 10f, 6144))
                {
                    source = "raycast:" + hit.collider.name + "@" + LayerMask.LayerToName(hit.collider.gameObject.layer);
                    return hit.point.y;
                }
            }
            catch (Exception e)
            {
                source = "error:" + e.Message;
                return float.NaN;
            }
            source = "none";
            return float.NaN;
        }
        private static Transform seatRoot;
        private static Vector3 seatRest;
        private static float seatApplied;

        public static bool IsMounted => mounted;

        // Settings of the style being ridden (or, when not mounted, of the style that would be).
        internal static StyleSettings S => Plugin.For(mounted ? activeStyle : Plugin.Style.Value);

        // Side view is on when the setting says so, or when the selected pose only exists as left/right art
        // (FishingIdle): with the keeper facing up or down the animator has no frame and the body renders nothing.
        public static bool PoseNeedsSideView => S.Pose.Value == RiderPose.FishingIdle;
        public static bool SideView => Plugin.SideViewOnly.Value || (mounted && PoseNeedsSideView);

        // The animator state the keeper is held in while riding (see the SetState prefix); Cart keeps the walk.
        public static AnimationState RiderPoseState =>
            (AnimationState)(int)S.Pose.Value;

        // Horizontal facing shared by rider and mount while SideViewOnly is on: +1 right, -1 left.
        private static float riderFacingX = 1f;

        // Called from the SetDirection prefix for the keeper's animation component (see Patches.cs).
        public static Vector2 RiderFacing(Vector2 requested)
        {
            if (requested.sqrMagnitude < 0.0001f)
            {
                return requested;
            }
            if (Mathf.Abs(requested.x) > 0.05f)
            {
                riderFacingX = requested.x > 0f ? 1f : -1f;
            }
            return new Vector2(riderFacingX, 0f);
        }

        // ------------------------------------------------------------------ per frame

        public static void Tick()
        {
            if (!IsInGame(out PlayerController pc, out GameScene scene))
            {
                if (mounted)
                {
                    ForceCleanup("left the game");
                }
                return;
            }

            if (UnityInput.Current.GetKeyDown(Plugin.MountKey.Value))
            {
                if (mounted)
                {
                    Dismount(pc, "dismounted");
                }
                else
                {
                    TryMount(pc, scene);
                }
            }
            if (!mounted)
            {
                // A pose's animator state switches its gear on every frame and needs a frame or two to be left after
                // a dismount; keep that gear hidden until it has (see PoseGearLayers).
                if (UnityEngine.Time.frameCount <= gearHideUntilFrame)
                {
                    HidePoseGear(pc, lastRiderPose);
                }
                return;
            }

            if (Plugin.TuningKeys.Value)
            {
                TuningKeys(pc);
            }
            if (activeStyle == MountStyle.Cart && partsReady && !CargoSheet.Running && UnityInput.Current.GetKeyDown(Plugin.CargoKey.Value))
            {
                Cargo.Toggle(pc);
            }

            string why = WhyMustDismount(pc);
            if (why != null)
            {
                Dismount(pc, why);
                return;
            }

            // Unity null: the scene that owned the view was unloaded (door to another scene) or the game removed it.
            if (donkey == null || !donkey)
            {
                donkey = null;
                if (!pc.IsControlsEnabled)
                {
                    // Still teleporting: the keeper's scene id and position are rewritten only once the new scene
                    // is up (PlayerController.Teleport), and control comes back after that. Dismounting now would
                    // set the cargo down at the old door; waiting puts it at his feet where he arrives.
                    return;
                }
                if (!Plugin.StayMountedThroughDoors.Value)
                {
                    Dismount(pc, "scene changed");
                    return;
                }
                if (!TrySpawn(pc, scene))
                {
                    Dismount(pc, "could not re-summon the donkey");
                    return;
                }
                Plugin.Log.LogInfo("Mount: donkey re-summoned after scene change.");
            }

            if (!partsReady && donkey.MainWgoPart != null)
            {
                PrepareParts(pc);
            }

            // Only the attack states touch SpeedMultiplier, and we dismount before they can run; still, keep it ours.
            if (pc.Ssm?.CurState is FreePlayerState && Mathf.Abs(pc.PhysicalBody.SpeedMultiplier - S.SpeedMultiplier.Value) > 0.001f)
            {
                pc.PhysicalBody.SpeedMultiplier = S.SpeedMultiplier.Value;
            }

            Follow(pc);
            RevealDonkeyIfDue();
            if (activeStyle == MountStyle.Cart && partsReady)
            {
                Lantern.Tick();
            }
            if (seatPending && partsReady)
            {
                seatPending = false;
                ApplyAutoSeat(pc);
            }
            lastRiderPose = S.Pose.Value;
            HidePoseGear(pc, lastRiderPose);

            // A pose whose animator state takes control (BySelf) freezes the keeper: the free state goes inactive
            // and no movement is applied. Surface it in the panel and once in the log instead of silently sticking.
            bool blocks = !pc.IsControlEnabledByType(TakenControlType.BySelf);
            if (blocks != PoseBlocksMovement)
            {
                PoseBlocksMovement = blocks;
                if (blocks && S.Pose.Value != RiderPose.Idle && S.Pose.Value != RiderPose.Walk)
                {
                    // Such a state ends by forcing Idle, which the SetState prefix would turn back into the pose:
                    // an enter/exit loop every frame (visible as flicker). Fall back to Idle for good.
                    Plugin.Log.LogWarning($"Mount: pose {S.Pose.Value} makes the game take control of the keeper; falling back to Idle.");
                    S.Pose.Value = RiderPose.Idle;
                    ApplyPoseNow();
                }
            }
        }

        // True while the selected pose's animator state holds player control (see above).
        public static bool PoseBlocksMovement { get; private set; }

        // Some animator states used as riding poses switch on gear the pose was made for: the fishing states turn on
        // the keeper's rod sprite layer ("tol_rod") and the game's fishing container (rod, line, bob), every frame.
        // A rider holds reins, not a rod, so the gear a pose brings is switched off again every frame while mounted
        // (this runs after the animation has written its values) and for a few frames after the dismount, until the
        // animator has left the pose. Which gear a pose brings is this table; a pose with no row brings nothing, a
        // new pose gets a row with the sprite layer names it turns on (see the sprite dump for the layer names).
        private static readonly Dictionary<RiderPose, string[]> PoseGearLayers = new Dictionary<RiderPose, string[]>
        {
            { RiderPose.FishingIdle, new[] { "tol_rod" } },
        };

        private static readonly Dictionary<string, SpriteRenderer> gearLayers = new Dictionary<string, SpriteRenderer>();
        private static RiderPose lastRiderPose = RiderPose.Walk;

        private static void HidePoseGear(PlayerController pc, RiderPose pose)
        {
            if (!PoseGearLayers.TryGetValue(pose, out string[] names))
            {
                return;
            }
            try
            {
                foreach (string name in names)
                {
                    if (!gearLayers.TryGetValue(name, out SpriteRenderer layer) || layer == null || !layer)
                    {
                        layer = null;
                        Animator animator = pc.View?.PlayerAnimation?.Animator;
                        if (animator != null)
                        {
                            foreach (SpriteRenderer sr in animator.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
                            {
                                if (sr.name == name)
                                {
                                    layer = sr;
                                    break;
                                }
                            }
                        }
                        gearLayers[name] = layer;
                    }
                    if (layer != null && layer && layer.gameObject.activeSelf)
                    {
                        layer.gameObject.SetActive(false);
                    }
                }
                if (pose == RiderPose.FishingIdle)
                {
                    // The fishing states also enable the game's fishing container, a separate object under the view.
                    FishingContainer fc = pc.View?.FishingContainer;
                    if (fc != null && fc.gameObject.activeSelf)
                    {
                        fc.gameObject.SetActive(false);
                    }
                }
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ mount / dismount

        private static void TryMount(PlayerController pc, GameScene scene)
        {
            string reason = WhyCannotMount(pc);
            if (reason != null)
            {
                Plugin.Log.LogInfo("Mount refused: " + reason);
                Say(reason);
                return;
            }
            activeStyle = Plugin.Style.Value;
            if (!TrySpawn(pc, scene))
            {
                Say("The donkey is nowhere to be found.");
                return;
            }
            mounted = true;
            riderFacingX = pc.PlayerData.Direction.x < -0.05f ? -1f : 1f;
            if (Mount.SideView)
            {
                // Turn the keeper to the side right away instead of waiting for the next step.
                pc.PhysicalBody.SetFacingDirection(new Vector2(riderFacingX, 0f));
            }
            pc.PhysicalBody.SpeedMultiplier = S.SpeedMultiplier.Value;
            // Ride with AutoSeat computes the seat once the donkey's sprite is loaded (PrepareParts).
            if (!S.AutoSeat.Value)
            {
                ApplySeat(pc, S.SeatOffset.Value);
            }
            Plugin.Log.LogInfo($"Mounted ({activeStyle}, speed x{S.SpeedMultiplier.Value}).");
        }

        private static string WhyCannotMount(PlayerController pc)
        {
            if (IsCoop())
            {
                return "Not in a co-op game.";
            }
            if (!pc.IsControlsEnabled)
            {
                return "Not right now.";
            }
            if (Plugin.DismountIndoors.Value && IsIndoors())
            {
                return "Not indoors.";
            }
            if (!(pc.Ssm?.CurState is FreePlayerState))
            {
                return "I'm busy.";
            }
            if (BuildController.Instance != null && BuildController.Instance.IsBuildModeActive)
            {
                return "Not while building.";
            }
            WGODef def = GameBalance.Me?.GetData<WGODef>(Plugin.MountWgoId.Value);
            if (def == null)
            {
                return "There is no such animal (" + Plugin.MountWgoId.Value + ").";
            }
            string sideEffect = WhyDefinitionTouchesTheSave(def);
            if (sideEffect != null)
            {
                return "Can't ride a " + Plugin.MountWgoId.Value + ": " + sideEffect;
            }
            return null;
        }

        // Constructing a WgoData (WgoData.SetDataFromDefinition) writes into the save for some definitions: town
        // quality is added to TownSystem.Quality, an NPC life-sim group gets the object's GUID appended to its saved
        // list, and start items are created in its inventory (which the cart would then hand out on dismount). The
        // donkey has none of these (Round 11), but MountWgoId is a free text setting, so a definition that would
        // leave a mark in the save is refused before anything is constructed.
        private static string WhyDefinitionTouchesTheSave(WGODef def)
        {
            if (def.townQuality != 0)
            {
                return "it would change the town quality in the save.";
            }
            if (!string.IsNullOrEmpty(def.npcLifeSimGroup))
            {
                return "it belongs to an NPC group the save keeps track of.";
            }
            if (def.startItems != null && def.startItems.Count > 0)
            {
                return "it comes with items, which the cart would create out of nothing.";
            }
            return null;
        }

        // Anything that takes the keeper out of free walking ends the ride: work, ladders, building, planting,
        // fishing, combat, sleep, death, cutscenes. UI (chests, inventory) and doors (teleport) do not.
        private static string WhyMustDismount(PlayerController pc)
        {
            SSMState cur = pc.Ssm?.CurState;
            if (!(cur is FreePlayerState))
            {
                return "state " + (cur?.GetType().Name ?? "none");
            }
            foreach (TakenControlType t in Enum.GetValues(typeof(TakenControlType)))
            {
                // ByUI: chests/inventory. ByTeleport: doors. BySelf: set by the animator itself while certain
                // states play (tool, work and some fishing poses carry CharacterSelfStateMachineBehaviour); with a
                // pose like that selected it is our own doing, so it must not end the ride.
                if (t == TakenControlType.ByUI || t == TakenControlType.ByTeleport || t == TakenControlType.BySelf)
                {
                    continue;
                }
                if (!pc.IsControlEnabledByType(t))
                {
                    return "control taken " + t;
                }
            }
            if (IsCoop())
            {
                return "co-op";
            }
            if (Plugin.DismountIndoors.Value && IsIndoors())
            {
                return "indoors";
            }
            return null;
        }

        // The game switches its time-of-day lighting preset per area; interiors, the church and dungeons use presets
        // flagged indoorPreset. Read after teleports settle (the switch is asynchronous, a frame or two).
        private static bool IsIndoors()
        {
            try
            {
                TimeOfDayPresets p = EnvironmentEngine.Instance?.TimesOfDay;
                return p != null && p.indoorPreset;
            }
            catch
            {
                return false;
            }
        }

        private static void Dismount(PlayerController pc, string reason)
        {
            mounted = false;
            gearHideUntilFrame = UnityEngine.Time.frameCount + 8;
            Cargo.Unload(reason); // cargo lives in a temp inventory; hand it back before the view goes
            DestroyDonkey();
            RestoreSeat();
            RestoreSpeed(pc);
            RestorePose(pc);
            Plugin.Log.LogInfo("Dismounted (" + reason + ").");
        }

        // Used when the player object may already be gone (menu, plugin unload, error).
        public static void ForceCleanup(string reason)
        {
            bool was = mounted;
            mounted = false;
            gearHideUntilFrame = UnityEngine.Time.frameCount + 8;
            try
            {
                Cargo.Unload(reason);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: unloading at cleanup failed: " + e.Message);
            }
            DestroyDonkey();
            RestoreSeat();
            try
            {
                if (MainGame.Instance != null && MainGame.PlayerController != null)
                {
                    RestoreSpeed(MainGame.PlayerController);
                    UnfreezeRider(MainGame.PlayerController);
                }
            }
            catch
            {
            }
            if (was)
            {
                Plugin.Log.LogInfo("Dismounted (" + reason + ").");
            }
        }

        // If the keeper is standing still at dismount, nothing would call SetState again; put the real state back.
        private static void RestorePose(PlayerController pc)
        {
            UnfreezeRider(pc);
            try
            {
                AnimationState real = pc.PlayerData.charState.Value;
                pc.View?.PlayerAnimation?.SetState(real == AnimationState.Walk ? AnimationState.Walk : AnimationState.Idle);
            }
            catch
            {
            }
        }

        // The attack states set the multiplier themselves on enter and reset it on exit; don't fight them.
        private static void RestoreSpeed(PlayerController pc)
        {
            try
            {
                if (pc == null || pc.PhysicalBody == null)
                {
                    return;
                }
                SSMState cur = pc.Ssm?.CurState;
                if (cur != null && cur.GetType().Name.StartsWith("Attack"))
                {
                    return;
                }
                pc.PhysicalBody.SpeedMultiplier = 1f;
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ the donkey view

        private static bool TrySpawn(PlayerController pc, GameScene scene)
        {
            try
            {
                string id = Plugin.MountWgoId.Value;
                WgoData data;
                SuppressScripts = true;
                try
                {
                    data = new WgoData(id, pc.PlayerData.position.Value, scene.Id) { isTempObject = true };
                }
                finally
                {
                    SuppressScripts = false;
                }
                donkey = Wgo.Spawn(data, scene.transform, registerInChunkManagerIfStatic: true, ignoreChunkRegistration: true, applyDefaultWgoPartState: true);
                donkey.UpdateChunkVisibility(isVisible: true);
                partsReady = false;
                cartParts.Clear();
                lastMirrored = (AnimationState)(-1);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Mount: spawning [{Plugin.MountWgoId.Value}] failed: {e}");
                DestroyDonkey();
                return false;
            }
        }

        // Once the prefab is loaded: no blocking, no interaction target, no navmesh cut; cart per style.
        private static void PrepareParts(PlayerController pc)
        {
            partsReady = true;
            try
            {
                donkey.SetInteractableCollidersState(isActive: false);
                donkey.SetLayerToAllColliders(26);
                foreach (Collider collider in donkey.GetComponentsInChildren<Collider>(includeInactive: true))
                {
                    int layer = collider.gameObject.layer;
                    if (layer == 8 || layer == 11 || layer == 6)
                    {
                        collider.gameObject.SetActive(value: false);
                    }
                    else
                    {
                        collider.enabled = false;
                    }
                }
                foreach (NavMeshCutBoxCustom cut in donkey.GetComponentsInChildren<NavMeshCutBoxCustom>(includeInactive: true))
                {
                    cut.gameObject.SetActive(value: false);
                }
                bool cart = activeStyle == MountStyle.Cart;
                foreach (Transform t in donkey.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (Array.IndexOf(CartPartNames, t.name) < 0)
                    {
                        continue;
                    }
                    if (cart && t.name == "donkey_cart_body_sway")
                    {
                        continue; // the loaded-cart body (corpse shroud) stays off, as in the prefab
                    }
                    t.gameObject.SetActive(cart);
                }
                cartParts.Clear();
                if (cart)
                {
                    Cargo.Attach(donkey); // the cart's inventory is the cargo; creates the cargo holder before the parts are collected
                    cartParts.Clear();    // (re-adding carried-over cargo may already have collected once)
                    CollectCartParts();
                    Lantern.Attach(donkey); // poses the pole's child nodes; the sway parents stay the animator's
                }
                donkey.MainWgoPart?.AnimationComponent?.SetWalkAnimationSpeedMultiplier(1.5f * pc.PhysicalBody.SpeedMultiplier);
                bodySprite = null;
                donkeyBodyRenderer = null;
                foreach (SpriteRenderer sr in donkey.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
                {
                    if (sr.name == "bdy")
                    {
                        bodySprite = sr.transform;
                        donkeyBodyRenderer = sr;
                        break;
                    }
                }
                if (bodySprite == null)
                {
                    Plugin.Log.LogWarning("Mount: body sprite 'bdy' not found on the mount prefab; using the fixed centering offset.");
                }
                keeperBodyRenderer = FindKeeperBody(pc);
                HookKeeperLateUpdate(pc);

                {
                    if (S.AutoSeat.Value)
                    {
                        seatPending = true; // computed after the first Follow, once the donkey sits at its offsets
                    }
                    if (S.NearLeg.Value)
                    {
                        RiderLeg.Attach(donkeyBodyRenderer, keeperBodyRenderer);
                        if (!RiderLeg.IsAttached)
                        {
                            Plugin.Log.LogWarning("Mount: rider leg not attached (donkey or keeper body sprite missing).");
                        }
                    }
                }
                HideDonkeyForAFrame(); // the facing is applied by Follow this frame; the animator shows it next frame
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Mount: preparing the donkey view failed: {e.Message}");
            }
        }

        // Top-level cart part transforms (the prefab names, active ones only) with their rest positions. Taken once,
        // right after spawn, before anything has moved them. The prefab reuses names down the tree (each Object3D's
        // mesh child is named like its parent, and the far wheel's mesh child is even called donkey_cart_wheel_f), so
        // a transform with a cart-part ancestor is skipped: moving it as well would shift the mesh twice.
        private static void CollectCartParts()
        {
            cartBodyPart = null;
            cartShiftTarget = 0f;
            cartShiftApplied = 0f;
            hasLastCartPoint = false;
            foreach (Transform t in donkey.GetComponentsInChildren<Transform>(includeInactive: false))
            {
                if (Array.IndexOf(CartPartNames, t.name) < 0 || t.parent == null || HasCartPartAncestor(t))
                {
                    continue;
                }
                var part = new CartPart { T = t, Base = t.localPosition, Sample = t.localPosition, Weight = t.name == "donkey_cart_bars" ? 0.5f : 1f };
                Renderer r = t.GetComponentInChildren<Renderer>(includeInactive: false);
                if (r != null)
                {
                    part.Sample = t.parent.InverseTransformPoint(r.bounds.center);
                }
                cartParts.Add(part);
                if (t.name == "donkey_cart_empty_sway" || (t.name == "donkey_cart_body_sway" && cartBodyPart == null))
                {
                    cartBodyPart = part;
                }
            }
            if (cartBodyPart == null && cartParts.Count > 0)
            {
                cartBodyPart = cartParts.Find(p => p.Weight >= 1f) ?? cartParts[0];
            }
            // The rock reference for the cargo: one of the nodes the walk animation rotates (all three share the pivot
            // and the angle, Round 8). The lantern's is never switched off by the game's drawer; the cart bodies are.
            swayRefPart = cartParts.Find(p => p.T.name == "donkey_cart_lantern_sway")
                          ?? cartParts.Find(p => p.T.name == "donkey_cart_empty_sway")
                          ?? cartParts.Find(p => p.T.name == "donkey_cart_body_sway");
            Plugin.Log.LogInfo($"Mount: {cartParts.Count} cart parts follow the ground as one (sampled under {cartBodyPart?.T.name}): " + string.Join(", ", cartParts.ConvertAll(p => p.T.name)));
        }

        private static bool HasCartPartAncestor(Transform t)
        {
            for (Transform p = t.parent; p != null && p != donkey.transform; p = p.parent)
            {
                if (Array.IndexOf(CartPartNames, p.name) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        // Per frame, after the root has been placed: the whole cart moves straight up or down as one by the difference
        // between the ground under the cart body and the ground under the donkey's body, within the cap; the bars take
        // half of it. One shift for all parts because the cart's wheels exist twice, as animated sprites at the root and
        // as meshes on the axle, and any difference between their shifts shows as a second pair of wheels. The ground
        // is sampled from a point at the root's height plus one, so a slope rising behind the donkey is still hit by
        // the fallback raycast. With the setting off every part sits at its prefab position.
        //
        // Both heights come from the same source (navigation mesh for both, or the ground raycast for both), as the
        // donkey/keeper delta does, so a height mismatch between the two sources can never move the cart on flat
        // ground. A difference steeper than any walkable slope (over CartSlopeLimit) is treated as a bad sample and
        // the previous target is kept. The applied shift then moves towards the target at CartShiftRate, so a
        // one-frame outlier cannot make the cart jump; a real slope is still followed within a fraction of a second.
        // Every rejected sample and every jump of the target is written to the log (at most twice a second) with
        // both samples and their sources, so a flicker can be traced to its cause.
        private const float CartSlopeLimit = 1.2f;  // world units of height difference between the two samples
        private const float CartShiftRate = 3f;     // world units per second
        private static float cartShiftTarget, cartShiftApplied;
        private static float nextCartAnomalyLog;
        private static int cartAnomalies;
        private static Vector3 lastCartPoint;
        private static bool hasLastCartPoint;

        // levelY is the keeper's own height, i.e. the ground level the cart belongs to: both samples look for ground
        // near that level, so a walkway or a cellar floor stacked above or below the road can never be picked.
        private static void FollowCartParts(Vector3 bodyGroundPoint, float levelY)
        {
            if (cartParts.Count == 0)
            {
                return;
            }
            bool on = S.CartFollowsGround.Value;
            float cap = Mathf.Max(0f, S.CartGroundMaxShift.Value);
            float target = 0f;
            string why = null;
            Vector3 refPoint = new Vector3(bodyGroundPoint.x, levelY, bodyGroundPoint.z);
            Vector3 cartPoint = refPoint;
            string src = "-";
            lastCartRefGround = float.NaN;
            // When the donkey turns, the cart swaps to its other side in one frame (about 1.7 units): the ground
            // there is a different place, so the new shift is applied at once instead of glided to. Walking moves
            // the sample by at most 0.12 per frame, so 0.5 separates the two cleanly.
            bool sideChange = false;
            if (on && cartBodyPart != null && cartBodyPart.T != null && cartBodyPart.T)
            {
                Vector3 w = cartBodyPart.T.parent.TransformPoint(cartBodyPart.Sample);
                cartPoint = new Vector3(w.x, levelY, w.z);
                Vector3 d = cartPoint - lastCartPoint;
                sideChange = !hasLastCartPoint || d.x * d.x + d.z * d.z > 0.5f * 0.5f;
                lastCartPoint = cartPoint;
                hasLastCartPoint = true;
                GroundHeightPair(refPoint, cartPoint, levelY, out lastCartRefGround, out float g, out src);
                cartBodyPart.LastGround = g;
                cartBodyPart.LastSource = src;
                if (float.IsNaN(lastCartRefGround) || float.IsNaN(g))
                {
                    target = cartShiftTarget; // nothing known this frame: keep going as before
                    why = "no ground sample";
                }
                else if (Mathf.Abs(g - lastCartRefGround) > CartSlopeLimit)
                {
                    target = cartShiftTarget;
                    why = "steeper than any slope, sample rejected";
                }
                else
                {
                    target = Mathf.Clamp(g - lastCartRefGround, -cap, cap);
                    if (!sideChange && Mathf.Abs(target - cartShiftTarget) > 0.15f)
                    {
                        why = "target jumped";
                    }
                }
            }
            if (why != null && Time.unscaledTime >= nextCartAnomalyLog)
            {
                nextCartAnomalyLog = Time.unscaledTime + 0.5f;
                cartAnomalies++;
                Plugin.Log.LogInfo($"[cart] {why}: ref={lastCartRefGround:F3} at {refPoint:F2}, cart={cartBodyPart?.LastGround:F3} at {cartPoint:F2} ({src}), target {cartShiftTarget:F3} -> {target:F3}, applied {cartShiftApplied:F3}, root={donkey.transform.position:F2}, frame {Time.frameCount}");
            }
            cartShiftTarget = target;
            cartShiftApplied = sideChange ? cartShiftTarget : Mathf.MoveTowards(cartShiftApplied, cartShiftTarget, CartShiftRate * Time.deltaTime);
            foreach (CartPart p in cartParts)
            {
                if (p.T == null || !p.T)
                {
                    continue;
                }
                float lift = cartShiftApplied * p.Weight;
                p.LastLift = lift;
                Vector3 liftVec = Mathf.Abs(lift) < 0.0005f ? Vector3.zero : p.T.parent.InverseTransformVector(Vector3.up * lift);
                if (p.T.name == Cargo.ContainerName)
                {
                    // The cargo rocks with the cart the way the game's loaded corpse does: that corpse hangs under
                    // donkey_cart_body_sway, which the walk animation rotates about the axle pivot (the sway node's
                    // own rest position) by up to 3 degrees. The holder is not reparented (the game's drawer switches
                    // those nodes on and off); it copies the rotation and turns about the same pivot every frame,
                    // after the animator. Off: the holder stays level.
                    if (S.CargoSwaysWithCart.Value && swayRefPart != null && swayRefPart.T != null && swayRefPart.T)
                    {
                        Quaternion r = swayRefPart.T.localRotation;
                        Vector3 pivot = swayRefPart.Base;
                        p.T.localRotation = r;
                        p.T.localPosition = pivot + r * (p.Base - pivot) + liftVec;
                        continue;
                    }
                    p.T.localRotation = Quaternion.identity;
                }
                p.T.localPosition = p.Base + liftVec;
            }
        }

        private static CartPart swayRefPart;

        // Ground heights at two points from one source: the navigation mesh when it knows both, otherwise the ground
        // raycast for both. NaN where neither knows. Only ground within CartLevelWindow of levelY counts.
        private const float CartLevelWindow = 1.0f;

        private static void GroundHeightPair(Vector3 a, Vector3 b, float levelY, out float ga, out float gb, out string source)
        {
            ga = NavmeshHeight(a, levelY);
            gb = NavmeshHeight(b, levelY);
            if (!float.IsNaN(ga) && !float.IsNaN(gb))
            {
                source = "navmesh";
                return;
            }
            // Raycast for both: the highest walkable-layer hit within the level window at each point, i.e. the
            // surface. The terrain is layered here and there: walkable mesh tiles (Ground_01, ...) with a flat box
            // ("Collider 0") about 1.5 below them, sometimes a bridge or a roof above. Two rules tried before failed
            // on this: "nearest to the keeper's level" chose the box under a rising slope, and "the collider both
            // points share" chose the box wherever the two points sat on different terrain tiles. The window (1.0
            // above and below the keeper, more than any walkable slope climbs over the cart's length) keeps the box
            // and far-away levels out.
            ga = HighestHitUnder(a, levelY, out string ca);
            gb = HighestHitUnder(b, levelY, out string cb);
            source = "raycast:" + ca + "/" + cb;
        }

        private static readonly RaycastHit[] rayHits = new RaycastHit[16];

        private static float HighestHitUnder(Vector3 p, float levelY, out string colliderName)
        {
            int n = Physics.RaycastNonAlloc(new Ray(new Vector3(p.x, levelY + CartLevelWindow, p.z), Vector3.down), rayHits, 2f * CartLevelWindow, 6144);
            float best = float.NaN;
            colliderName = "none";
            for (int i = 0; i < n; i++)
            {
                // The walkable top of a dropped big item (a log lying on the road) is 0.4 above the ground; the keeper
                // may step on it, the cart must not climb it.
                if (rayHits[i].collider.name == "BigDropTopSurface")
                {
                    continue;
                }
                if (float.IsNaN(best) || rayHits[i].point.y > best)
                {
                    best = rayHits[i].point.y;
                    colliderName = rayHits[i].collider.name;
                }
            }
            return best;
        }


        // Nearest walkable node by true 3D distance from a point at the keeper's level (not "as seen from above",
        // which cannot tell stacked levels apart), accepted when it lies under the point and within the level window.
        private static float NavmeshHeight(Vector3 p, float levelY)
        {
            try
            {
                Pathfinding.RecastGraph graph = MainGame.PlayerController?.SceneRecastGraph;
                if (graph == null)
                {
                    return float.NaN;
                }
                Pathfinding.NNInfo nn = graph.GetNearest(new Vector3(p.x, levelY, p.z), Pathfinding.NearestNodeConstraint.Walkable);
                Vector3 np = nn.position;
                float dx = np.x - p.x, dz = np.z - p.z;
                return dx * dx + dz * dz < 0.05f * 0.05f && Mathf.Abs(np.y - levelY) <= CartLevelWindow ? np.y : float.NaN;
            }
            catch
            {
                return float.NaN;
            }
        }


        private static string CartLiftSummary()
        {
            if (cartParts.Count == 0)
            {
                return "no cart parts";
            }
            var parts = new System.Collections.Generic.List<string>();
            parts.Add($"refGround={lastCartRefGround:F3} target={cartShiftTarget:F3} applied={cartShiftApplied:F3} anomaliesLogged={cartAnomalies}");
            foreach (CartPart p in cartParts)
            {
                if (p.T == null || !p.T)
                {
                    continue;
                }
                parts.Add($"{p.T.name}: lift={p.LastLift:F3} ground={p.LastGround:F3} ({p.LastSource}) lpos={p.T.localPosition:F3} base={p.Base:F3} lrot={p.T.localEulerAngles:F0} world={p.T.position:F2}");
            }
            return string.Join(" | ", parts);
        }

        // The keeper's body sprite layer ("bdy" under the Animator transform): its material carries the outfit
        // palette, its trimmed rect tells where the feet are.
        private static SpriteRenderer FindKeeperBody(PlayerController pc)
        {
            try
            {
                Animator animator = pc.View?.PlayerAnimation?.Animator;
                if (animator == null)
                {
                    return null;
                }
                foreach (SpriteRenderer sr in animator.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
                {
                    if (sr.name == "bdy")
                    {
                        return sr;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        // Seat height from the art itself: put the rider's hip (7 px above the keeper's feet) on the donkey's back
        // line (24 px above the donkey's ground) as seen on screen. Both points come from the sprites' trimmed-rect
        // offsets (2 px transparent border, pivot at the frame centre). The comparison is done along the camera's
        // screen-up axis, so the donkey's depth offset is accounted for. SeatAdjustPx and the tuning keys refine it.
        private static void ApplyAutoSeat(PlayerController pc)
        {
            try
            {
                float? seat = ComputeAutoSeat();
                if (!seat.HasValue)
                {
                    ApplySeat(pc, S.SeatOffset.Value);
                    return;
                }
                ApplySeat(pc, seat.Value);
                Plugin.Log.LogInfo($"Mount: auto seat {seat.Value:F3} (adjust {S.SeatAdjustPx.Value} px); follows the donkey's back each frame from here.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Mount: auto seat failed, using Ride.SeatOffset: {e.Message}");
                ApplySeat(pc, S.SeatOffset.Value);
            }
        }

        // One art pixel of vertical movement on screen equals this much world Y (sprites are stretched 1.667x
        // vertically to counter the 53° camera, so 1 px = 0.02 * 1.667 world units).
        private const float PxWorldY = 0.02f * 1.6667f;

        // Debug.TuningKeys: PageUp/PageDown seat, Home/End leg X, Insert/Delete leg Y, all in art pixels. They edit
        // the same config values the tuning window shows.
        private static void TuningKeys(PlayerController pc)
        {
            var input = UnityInput.Current;
            bool changed = false;
            if (input.GetKeyDown(KeyCode.PageUp)) { S.SeatAdjustPx.Value++; changed = true; }
            if (input.GetKeyDown(KeyCode.PageDown)) { S.SeatAdjustPx.Value--; changed = true; }
            if (input.GetKeyDown(KeyCode.End)) { S.LegAdjustXPx.Value++; changed = true; }
            if (input.GetKeyDown(KeyCode.Home)) { S.LegAdjustXPx.Value--; changed = true; }
            if (input.GetKeyDown(KeyCode.Insert)) { S.LegAdjustYPx.Value++; changed = true; }
            if (input.GetKeyDown(KeyCode.Delete)) { S.LegAdjustYPx.Value--; changed = true; }
            if (!changed)
            {
                return;
            }
            RefreshSeat();
            Plugin.Log.LogInfo($"[tune] seat adjust {S.SeatAdjustPx.Value} px, rider offset {S.RiderOffsetPx.Value} px, leg adjust x {S.LegAdjustXPx.Value} y {S.LegAdjustYPx.Value} px, leg height {S.LegHeightPx.Value} px");
        }

        // ------------------------------------------------------------------ live tuning hooks (window / keys)

        public static MountStyle CurrentStyle => activeStyle;

        // Style switch from the panel: the cart parts and offsets are decided at spawn, so mount again in place.
        public static void Remount()
        {
            if (!mounted)
            {
                return;
            }
            PlayerController pc = MainGame.PlayerController;
            if (pc == null || !pc.TryGetCurrentGameScene(out GameScene scene) || scene == null)
            {
                return;
            }
            Dismount(pc, "style change");
            TryMount(pc, scene);
        }

        public static void RefreshSpeed()
        {
            if (!mounted)
            {
                return;
            }
            PlayerController pc = MainGame.PlayerController;
            if (pc == null)
            {
                return;
            }
            pc.PhysicalBody.SpeedMultiplier = S.SpeedMultiplier.Value;
            try
            {
                donkey?.MainWgoPart?.AnimationComponent?.SetWalkAnimationSpeedMultiplier(1.5f * S.SpeedMultiplier.Value);
            }
            catch
            {
            }
        }

        public static void RefreshSeat()
        {
            if (!mounted)
            {
                return;
            }
            PlayerController pc = MainGame.PlayerController;
            if (pc == null)
            {
                return;
            }
            if (S.AutoSeat.Value)
            {
                ApplyAutoSeat(pc);
            }
            else
            {
                ApplySeat(pc, S.SeatOffset.Value);
            }
        }

        public static void RefreshLeg()
        {
            if (!mounted)
            {
                return;
            }
            RiderLeg.Detach();
            RiderLeg.ClearCache();
            if (S.NearLeg.Value && donkeyBodyRenderer != null && keeperBodyRenderer != null)
            {
                RiderLeg.Attach(donkeyBodyRenderer, keeperBodyRenderer);
            }
        }

        public static void ApplyPoseNow()
        {
            if (!mounted)
            {
                return;
            }
            try
            {
                PlayerController pc = MainGame.PlayerController;
                AnimationState real = pc.PlayerData.charState.Value == AnimationState.Walk ? AnimationState.Walk : AnimationState.Idle;
                pc.View?.PlayerAnimation?.SetState(real); // the SetState prefix maps it to the configured pose
            }
            catch
            {
            }
        }

        private static void Follow(PlayerController pc)
        {
            PlayerData pd = pc.PlayerData;
            // With SideViewOnly the keeper's sprite is already locked to left/right (SetDirection prefix); give the
            // donkey that same facing so the two never disagree. Otherwise mirror the keeper's real direction.
            if (Mathf.Abs(pd.Direction.x) > 0.05f)
            {
                riderFacingX = pd.Direction.x > 0f ? 1f : -1f; // the one facing source for torso shift and legs
            }
            Vector2 dir = Mount.SideView ? new Vector2(riderFacingX, 0f) : pd.Direction;
            if (dir.sqrMagnitude > 0.0001f)
            {
                donkey.Data.MovableDirection = dir; // applied to the donkey's animator synchronously
            }

            float depth = S.Depth.Value;
            float center = S.CenterOffset.Value;
            Vector3 away = AwayFromCamera();
            Vector3 right = Vector3.Cross(Vector3.up, away).normalized;

            // The donkey has only left/right sprites, drawn 0.48 to the facing side of its root, and the animator
            // mirrors them one evaluation after the direction changes. We run in LateUpdate, after that evaluation,
            // and measure where the body sprite actually is relative to the root this frame, so the compensation
            // always matches what is on screen, including the frames of a facing flip.
            float lateral = 0f;
            if (Mathf.Abs(center) > 0.0001f)
            {
                if (bodySprite != null && bodySprite)
                {
                    lateral = Vector3.Dot(bodySprite.position - donkey.transform.position, right);
                }
                else
                {
                    facingSign = dir.x > 0.0001f ? 1f : -1f;
                    lateral = center * facingSign;
                }
            }

            // PlayerData.position is the 50 Hz physics value; the body transform is the Rigidbody-interpolated,
            // per-frame position the keeper is actually drawn at. Snap to the same pixel grid PlayerView uses so
            // the two sprites never disagree by a sub-pixel amount.
            Rigidbody rb = pc.PhysicalBody.Rb;
            Vector3 basePos = rb != null ? rb.transform.position : pd.position.Value;

            // Rider fore/aft: RiderOffsetPx > 0 puts the keeper closer to the head, i.e. the donkey shifts towards its
            // rear by that many art pixels (the leg overlay, a child of the donkey, compensates in RiderLeg.Update).
            float sign = Mathf.Abs(lateral) > 0.001f ? Mathf.Sign(lateral) : (Mount.SideView ? riderFacingX : (dir.x >= 0f ? 1f : -1f));
            float fore = S.RiderOffsetPx.Value * 0.02f * sign;

            Vector3 target = basePos + away * depth - right * (lateral + fore);

            // Ground height at the donkey's own position, as the game's WgoMovementAdjustComponent does for walking
            // objects (our temp donkey never moves through that system). Without it the donkey and cart take the
            // keeper's height and dig into slopes. The rider follows the donkey's height through groundDelta.
            groundDelta = 0f;
            if (S.SnapToGround.Value)
            {
                // Difference between the ground under the donkey and under the keeper, both from the same source, so
                // flat ground is exactly 0 and no absolute mismatch between sources can sink or float the donkey.
                // Sample under the donkey's feet, i.e. its body sprite, which the prefab draws about half a unit to the
                // facing side of the root (and a little towards the camera). The root itself may stand on very
                // different ground on a slope, and lifting the body to that height leaves it floating above the terrain
                // under it, where the slope in front then occludes its legs.
                Vector3 bodyOffset = (bodySprite != null && bodySprite) ? bodySprite.position - donkey.transform.position : right * lateral;
                bodyOffset.y = 0f;
                float gd = GroundHeight(target + bodyOffset, out lastDonkeyGroundSource);
                lastCartGround = GroundHeight(target + bodyOffset - right * (sign * 1.2f), out lastCartGroundSource);
                float gk = GroundHeight(basePos, out lastKeeperGroundSource);
                if (!float.IsNaN(gd) && !float.IsNaN(gk))
                {
                    groundDelta = gd - gk;
                    target.y = basePos.y + groundDelta;
                }
                lastGroundSample = new Vector4(gd, gk, basePos.y, groundDelta);
            }
            donkey.Data.Position = VisualConsts.GetRoundedPosXYZ(target, PixelSize());
            if (cartParts.Count > 0)
            {
                Vector3 bodyGround = (bodySprite != null && bodySprite) ? bodySprite.position : donkey.transform.position + right * lateral;
                FollowCartParts(bodyGround, basePos.y);
            }

            // Legs and seat are updated from the keeper's OnLateUpdate (after the skin swap), see OnKeeperLateUpdate.
            AnimationState state = pd.charState.Value == AnimationState.Walk ? AnimationState.Walk : AnimationState.Idle;
            if (state != lastMirrored)
            {
                lastMirrored = state;
                try
                {
                    AnimationComponentBase anim = donkey.MainWgoPart?.AnimationComponent;
                    if (state == AnimationState.Walk && anim != null)
                    {
                        // The idle graze/ear/lips clips are one-shots the animator finishes before it will walk;
                        // the game's own reset trigger cuts them so the walk starts at once.
                        anim.SetTrigger(AnimationComponentBase.RESET_TO_IDLE_TRIGGER);
                    }
                    donkey.Data.SetStateToAnimator(state);
                    anim?.SetWalkAnimationSpeedMultiplier(1.5f * pc.PhysicalBody.SpeedMultiplier);
                }
                catch
                {
                    donkey.Data.SetStateToAnimator(state);
                }
            }
            FreezeRiderWhenIdle(pc, state);
        }

        // Ride.FreezePoseWhenIdle: the riding pose is a breathing loop (arms move). Standing still, pause the keeper's
        // animator once the pose transition has had time to finish; resume as soon as he moves. Restored on dismount.
        private static float idleSince = -1f;
        private static bool riderFrozen;

        private static void FreezeRiderWhenIdle(PlayerController pc, AnimationState state)
        {
            Animator a = null;
            try
            {
                a = pc.View?.PlayerAnimation?.Animator;
            }
            catch
            {
            }
            if (a == null)
            {
                return;
            }
            bool wantFreeze = S.FreezePoseWhenIdle.Value && state == AnimationState.Idle
                              && S.Pose.Value != RiderPose.Idle && S.Pose.Value != RiderPose.Walk;
            if (!wantFreeze)
            {
                idleSince = -1f;
                riderFrozen = false;
                // Playback speed of the pose: PoseSpeed while moving (optionally scaled by the riding speed so it keeps
                // pace with the donkey's walk animation), the clip's own speed otherwise. Game poses (Idle/Walk) untouched.
                float target = 1f;
                if (S.Pose.Value != RiderPose.Idle && S.Pose.Value != RiderPose.Walk)
                {
                    target = state == AnimationState.Walk
                        ? S.PoseSpeed.Value * (S.PoseSpeedFollowsDonkey.Value ? S.SpeedMultiplier.Value : 1f)
                        : S.PoseSpeed.Value;
                }
                if (Mathf.Abs(a.speed - target) > 0.001f)
                {
                    a.speed = target;
                }
                return;
            }
            if (idleSince < 0f)
            {
                idleSince = Time.unscaledTime;
            }
            if (!riderFrozen && Time.unscaledTime - idleSince > 0.35f)
            {
                riderFrozen = true;
                fidgetUntil = -1f;
                nextFidget = Time.unscaledTime + NextFidgetDelay();
            }
            if (riderFrozen)
            {
                // Not a dead stop: a crawl keeps the game's breathing layer barely alive, and now and then the pose
                // plays for about a second (a fidget), so the rider is never a still image.
                float now = Time.unscaledTime;
                float speed = Mathf.Max(0f, S.IdleCrawlSpeed.Value);
                if (fidgetUntil > now)
                {
                    speed = S.IdleFidgetSpeed.Value;
                }
                else if (S.IdleFidgetEverySeconds.Value > 0f && now >= nextFidget)
                {
                    fidgetUntil = now + UnityEngine.Random.Range(0.6f, 1.2f);
                    nextFidget = fidgetUntil + NextFidgetDelay();
                    speed = S.IdleFidgetSpeed.Value;
                }
                if (Mathf.Abs(a.speed - speed) > 0.001f)
                {
                    a.speed = speed;
                }
            }
        }

        private static float fidgetUntil = -1f, nextFidget;

        private static float NextFidgetDelay()
        {
            float every = S.IdleFidgetEverySeconds.Value;
            return every <= 0f ? float.MaxValue / 4f : every * UnityEngine.Random.Range(0.5f, 1.5f);
        }

        private static void UnfreezeRider(PlayerController pc)
        {
            idleSince = -1f;
            if (!riderFrozen)
            {
                return;
            }
            riderFrozen = false;
            try
            {
                Animator a = pc?.View?.PlayerAnimation?.Animator;
                if (a != null)
                {
                    a.speed = 1f;
                }
            }
            catch
            {
            }
        }

        private static void DestroyDonkey()
        {
            if (donkey != null)
            {
                try
                {
                    if (donkey)
                    {
                        UnityEngine.Object.Destroy(donkey.gameObject);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Mount: destroying the donkey view failed: {e.Message}");
                }
            }
            donkey = null;
            partsReady = false;
            cartParts.Clear();
            Cargo.Detach(); // keeps the item list; Unload (called before this on a real dismount) clears it
            Lantern.Detach();
            hiddenRenderers.Clear();
            seatPending = false;
            bodySprite = null;
            donkeyBodyRenderer = null;
            RiderLeg.Detach();
            UnhookKeeperLateUpdate();
        }

        // ------------------------------------------------------------------ rider sprite

        // The keeper's sprites hang under one transform (the one with the Animator, "main_character", at a fixed
        // local height). Shifting it is enough; nothing in the game writes that local position at runtime.
        // World-space shift currently applied to the torso by Ride.TorsoOffsetPx (zero when not riding).
        internal static Vector3 TorsoShiftWorld;

        // +1 when the keeper faces right (mirrored art), -1 when facing left.
        // +1 facing right, -1 facing left. Taken from the tracked facing: Unity reports the game's mirroring scales
        // back inconsistently, so inspecting transforms gave the same sign for both facings.
        private static float TorsoFacingSign()
        {
            return riderFacingX;
        }

        internal static bool FacingRight => riderFacingX > 0f;

        // The sprite root's rest position is remembered once; the seat is always "rest + offset", so repeated
        // updates (every frame while riding, to follow the donkey's back) never accumulate.
        private static void ApplySeat(PlayerController pc, float offset)
        {
            try
            {
                if (seatRoot == null || !seatRoot)
                {
                    Animator animator = pc.View?.PlayerAnimation?.Animator;
                    if (animator == null)
                    {
                        return;
                    }
                    seatRoot = animator.transform;
                    seatRest = seatRoot.localPosition;
                }
                // Torso fore/aft: shifts the sprite root sideways (the root sits above the keeper's mirroring, so
                // this is a world-x shift; +facing = towards the head). The legs subtract it (RiderLeg) and stay.
                TorsoShiftWorld = Vector3.right * (TorsoFacingSign() * S.TorsoOffsetPx.Value * 0.02f);
                seatRoot.localPosition = seatRest + Vector3.up * offset + TorsoShiftWorld;
                seatApplied = offset;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Mount: seat offset failed: {e.Message}");
            }
        }

        private static void RestoreSeat()
        {
            try
            {
                if (seatRoot != null && seatRoot)
                {
                    seatRoot.localPosition = seatRest;
                }
                TorsoShiftWorld = Vector3.zero;
            }
            catch
            {
            }
            seatRoot = null;
            seatApplied = 0f;
        }

        // Seat height for the donkey's current frame (its back bobs 1-2 px through the walk cycle): hip (7 px above
        // the keeper's feet) on the back line (24 px above the donkey's ground), compared along screen-up, plus the
        // pixel adjustment. Null when either sprite is unavailable.
        private static float? ComputeAutoSeat()
        {
            if (keeperBodyRenderer == null || keeperBodyRenderer.sprite == null || donkeyBodyRenderer == null || donkeyBodyRenderer.sprite == null)
            {
                return null;
            }
            const float px = 1f / 50f;
            Sprite k = keeperBodyRenderer.sprite;
            Sprite d = donkeyBodyRenderer.sprite;
            float keeperHipLocalY = (k.textureRectOffset.y + 2f + 7f - k.pivot.y) * px;
            float back = S.Bob.Value ? BackHeightPx(d) : 25f;
            float donkeyBackLocalY = (d.textureRectOffset.y + 2f + back - d.pivot.y) * px;
            // Measure with the seat at rest so the result is absolute, not relative to the current seat.
            Vector3 hipWorld = keeperBodyRenderer.transform.TransformPoint(new Vector3(0f, keeperHipLocalY, 0f)) - Vector3.up * seatApplied;
            Vector3 backWorld = donkeyBodyRenderer.transform.TransformPoint(new Vector3(0f, donkeyBackLocalY, 0f));
            Camera cam = Camera.main;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            if (Mathf.Abs(up.y) < 0.1f)
            {
                up = Vector3.up;
            }
            return Vector3.Dot(backWorld - hipWorld, up) / up.y + S.SeatAdjustPx.Value * PxWorldY;
        }

        // Height of the donkey's back above its ground line at the saddle, per body frame, measured from the atlas
        // (the frames' bottom edge never moves; the bob is painted inside them). Names arrive as "…(Clone)".
        internal static float BackHeightPx(Sprite donkeyBody)
        {
            string n = donkeyBody != null ? donkeyBody.name : null;
            if (string.IsNullOrEmpty(n))
            {
                return 25f;
            }
            int clone = n.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone > 0)
            {
                n = n.Substring(0, clone);
            }
            // Only the walk cycle moves the back. Graze, ear, lips and turn frames are numbered too but keep the
            // back where the static frame has it, so they must not bob the rider.
            if (n.IndexOf("_walk_", StringComparison.Ordinal) < 0)
            {
                return 25f;
            }
            if (n.EndsWith("_01") || n.EndsWith("_05")) return 24f;
            if (n.EndsWith("_03") || n.EndsWith("_07")) return 26f;
            return 25f; // _02, _04, _06, _08
        }

        // The keeper's clothed sprites are swapped in by the game's skin changer inside PlayerAnimation.LateUpdate,
        // which then raises OnLateUpdate. Legs and seat are updated there, so they always see the clothed frame the
        // player actually sees (a plugin LateUpdate can run before that swap and would read the naked base body).
        private static Action keeperLateUpdate;
        private static bool warnedNoSprite;
        private static AnimationComponent hookedAnimation;

        private static void HookKeeperLateUpdate(PlayerController pc)
        {
            UnhookKeeperLateUpdate();
            try
            {
                AnimationComponent anim = pc.View?.PlayerAnimation;
                if (anim == null)
                {
                    return;
                }
                keeperLateUpdate = () => OnKeeperLateUpdate(pc);
                anim.OnLateUpdate += keeperLateUpdate;
                hookedAnimation = anim;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Mount: could not hook the keeper's LateUpdate: {e.Message}");
            }
        }

        private static void UnhookKeeperLateUpdate()
        {
            try
            {
                if (hookedAnimation != null && keeperLateUpdate != null)
                {
                    hookedAnimation.OnLateUpdate -= keeperLateUpdate;
                }
            }
            catch
            {
            }
            hookedAnimation = null;
            keeperLateUpdate = null;
        }

        private static void OnKeeperLateUpdate(PlayerController pc)
        {
            if (!mounted || !partsReady || donkey == null || !donkey)
            {
                return;
            }
            try
            {
                if (keeperBodyRenderer != null && keeperBodyRenderer.sprite == null && !warnedNoSprite)
                {
                    warnedNoSprite = true;
                    Plugin.Log.LogWarning($"Mount: the keeper body has no sprite this frame (pose {S.Pose.Value}, facing {pc.PlayerData.Direction}); the pose has no frame for this facing.");
                }
                if (RiderLeg.IsAttached)
                {
                    RiderLeg.Update(donkeyBodyRenderer, keeperBodyRenderer, riderFacingX > 0f);
                }
                if (!seatPending)
                {
                    TickSeat(pc);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Mount: rider update failed: {e.Message}");
            }
        }

        // Called every frame while riding with AutoSeat: keeps the rider on the back through the walk cycle.
        private static void TickSeat(PlayerController pc)
        {
            if (seatRoot == null || !seatRoot)
            {
                return;
            }
            float? seat;
            if (S.AutoSeat.Value)
            {
                seat = ComputeAutoSeat();
            }
            else
            {
                // Manual height plus the walk-cycle bob (the back is 24-26 px above the ground depending on frame).
                float bob = 0f;
                if (S.Bob.Value && donkeyBodyRenderer != null && donkeyBodyRenderer.sprite != null)
                {
                    bob = (BackHeightPx(donkeyBodyRenderer.sprite) - 25f) * PxWorldY;
                }
                seat = S.SeatOffset.Value + bob + groundDelta;
            }
            if (!seat.HasValue)
            {
                return;
            }
            bool torsoShiftStale = (TorsoShiftWorld - Vector3.right * (TorsoFacingSign() * S.TorsoOffsetPx.Value * 0.02f)).sqrMagnitude > 0.000001f;
            if (Mathf.Abs(seat.Value - seatApplied) > 0.0005f || torsoShiftStale)
            {
                ApplySeat(pc, seat.Value);
            }
        }

        // The game's own donkey, if its view is loaded in this scene: the same measurements as for ours, so the two
        // can be compared on the same slope (root, body sprite, cart parts, and the ground under each).
        private static string RealDonkeySummary()
        {
            try
            {
                var list = MainGame.WorldData.GetWgoDataList("npc_donkey");
                if (list == null || list.Count == 0)
                {
                    return "no npc_donkey in world data";
                }
                var sb = new System.Text.StringBuilder();
                foreach (WgoData d in list)
                {
                    Wgo view = GameScene.GetWgoViewGlobal(d.UniqueId);
                    sb.Append($"data pos={d.Position:F3} world={d.WorldId} moving={d.MovementComponent?.IsMoving} ");
                    if (view == null || !view)
                    {
                        sb.Append("(no view loaded) ");
                        continue;
                    }
                    string src;
                    Vector3 root = view.transform.position;
                    sb.Append($"| view root={root:F3} groundAtRoot={GroundHeight(root, out src):F3} ({src}) ");
                    foreach (SpriteRenderer sr in view.GetComponentsInChildren<SpriteRenderer>(includeInactive: false))
                    {
                        if (sr.name == "bdy")
                        {
                            Vector3 bp = sr.transform.position;
                            sb.Append($"| body={bp:F3} groundAtBody={GroundHeight(bp, out src):F3} ");
                            break;
                        }
                    }
                    foreach (Transform t in view.GetComponentsInChildren<Transform>(includeInactive: false))
                    {
                        if (t.name.StartsWith("donkey_cart_") && t.parent != null && !t.parent.name.StartsWith("donkey_cart_"))
                        {
                            sb.Append($"| {t.name}={t.position:F2} ground={GroundHeight(t.position, out src):F2} ");
                        }
                    }
                }
                return sb.ToString();
            }
            catch (Exception e)
            {
                return "err " + e.Message;
            }
        }

        private static string CartPartsSummary()
        {
            try
            {
                if (donkey == null || !donkey)
                {
                    return "-";
                }
                var parts = new System.Collections.Generic.List<string>();
                foreach (Transform t in donkey.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (t.name.StartsWith("donkey_cart_") && t.parent != null && !t.parent.name.StartsWith("donkey_cart_"))
                    {
                        parts.Add(t.name + "@" + t.position.ToString("F2"));
                    }
                }
                return string.Join(" ", parts);
            }
            catch (Exception e)
            {
                return "err " + e.Message;
            }
        }

        // Debug: every transform involved in placing rider and donkey, for the current facing. Press in both facings.
        public static void LogGeometry()
        {
            try
            {
                PlayerController pc = MainGame.PlayerController;
                var sb = new System.Text.StringBuilder("[geometry] ");
                sb.Append($"keeperDir={pc.PlayerData.Direction} riderFacingX={riderFacingX} ");
                if (keeperBodyRenderer != null)
                {
                    Transform t = keeperBodyRenderer.transform;
                    sb.Append($"| keeperBody: sprite={keeperBodyRenderer.sprite?.name} flipX={keeperBodyRenderer.flipX} pos={t.position:F3} lossyScale={t.lossyScale:F2} localXaxis={t.TransformDirection(Vector3.right):F2} mirrored={RiderLeg.IsMirrored(keeperBodyRenderer)} ");
                    for (Transform p = t.parent; p != null && p.name != "PlayerPhysicalBody"; p = p.parent)
                    {
                        sb.Append($"| {p.name}: lpos={p.localPosition:F3} lrot={p.localEulerAngles:F0} lscale={p.localScale:F2} ");
                    }
                }
                if (donkey != null && donkey && donkeyBodyRenderer != null)
                {
                    Transform t = donkeyBodyRenderer.transform;
                    sb.Append($"| donkeyRoot pos={donkey.transform.position:F3} | donkeyBody: sprite={donkeyBodyRenderer.sprite?.name} flipX={donkeyBodyRenderer.flipX} pos={t.position:F3} lossyScale={t.lossyScale:F2} localXaxis={t.TransformDirection(Vector3.right):F2} mirrored={RiderLeg.IsMirrored(donkeyBodyRenderer)} ");
                    for (Transform p = t.parent; p != null && p != donkey.transform; p = p.parent)
                    {
                        sb.Append($"| {p.name}: lpos={p.localPosition:F3} lrot={p.localEulerAngles:F0} lscale={p.localScale:F2} ");
                    }
                }
                sb.Append($"| seatRoot lpos={(seatRoot ? seatRoot.localPosition.ToString("F3") : "-")} seatRest={seatRest:F3} seatApplied={seatApplied:F3} torsoShift={TorsoShiftWorld:F3} torsoSign={TorsoFacingSign()} ");
                sb.Append($"| playerBody pos={pc.PhysicalBody.Rb.transform.position:F3}");
                sb.Append($" | ground: donkey={lastGroundSample.x:F3} ({lastDonkeyGroundSource}) keeper={lastGroundSample.y:F3} ({lastKeeperGroundSource}) keeperY={lastGroundSample.z:F3} delta={lastGroundSample.w:F3} cartRear={lastCartGround:F3} ({lastCartGroundSource}) snap={S.SnapToGround.Value}");
                sb.Append(" | cart lift: " + CartLiftSummary());
                sb.Append(" | REAL DONKEY: " + RealDonkeySummary());
                Vector3 kp = pc.PhysicalBody.Rb.transform.position;
                string src;
                sb.Append($" | slope around keeper (ground at +-0.5): x+={GroundHeight(kp + Vector3.right * 0.5f, out src):F3} x-={GroundHeight(kp - Vector3.right * 0.5f, out src):F3} z+={GroundHeight(kp + Vector3.forward * 0.5f, out src):F3} z-={GroundHeight(kp - Vector3.forward * 0.5f, out src):F3} | cart parts: {CartPartsSummary()}");
                Animator ka = pc.View?.PlayerAnimation?.Animator;
                if (ka != null && ka.isActiveAndEnabled)
                {
                    AnimatorStateInfo st = ka.GetCurrentAnimatorStateInfo(0);
                    AnimatorClipInfo[] ci = ka.GetCurrentAnimatorClipInfo(0);
                    string clip = ci.Length > 0 && ci[0].clip != null ? ci[0].clip.name : "-";
                    sb.Append($" | keeperAnimator: speed={ka.speed} StateParam={ka.GetInteger("State")} clip={clip} nTime={st.normalizedTime:F2} loop={st.loop} charState={pc.PlayerData.charState.Value} frozen={riderFrozen}");
                }
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("geometry log failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static bool IsInGame(out PlayerController pc, out GameScene scene)
        {
            pc = null;
            scene = null;
            try
            {
                if (MainGame.Instance == null || MainGame.PlayerData == null || MainGame.Instance.GameSave == null)
                {
                    return false;
                }
                pc = MainGame.PlayerController;
                return pc != null && pc.TryGetCurrentGameScene(out scene) && scene != null;
            }
            catch
            {
                return false;
            }
        }

        // Same test the game's bug reporter uses for "is this a co-op game".
        private static bool IsCoop()
        {
            try
            {
                return LazyNetwork.IsInitialized && LazyNetwork.NetworkManager != null && LazyNetwork.NetworkManager.IsCoopGame;
            }
            catch
            {
                return false;
            }
        }

        // Same rounding granularity PlayerView picks (1 in Native render mode, 2 otherwise).
        private static int PixelSize()
        {
            try
            {
                return CameraSystem.Instance.MainCamera.GetRenderType() == MainCamera.RenderMode.Native ? 1 : 2;
            }
            catch
            {
                return 1;
            }
        }

        // Ground direction pointing away from the camera (+Z with the game's fixed camera), so a positive depth draws behind.
        private static Vector3 AwayFromCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return Vector3.forward;
            }
            Vector3 f = cam.transform.forward;
            f.y = 0f;
            return f.sqrMagnitude < 0.0001f ? Vector3.forward : f.normalized;
        }

        internal static void Say(string text)
        {
            if (!Plugin.ShowMessages.Value || string.IsNullOrEmpty(text))
            {
                return;
            }
            try
            {
                Bubble.Talk(new PhraseData(true, null, text, null, null, SpeechBubbleType.Think));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Mount: could not show \"{text}\": {e.Message}");
            }
        }
    }
}
