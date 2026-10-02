using System;
using System.Collections.Generic;
using BepInEx;
using GK2Mounts.Api;
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
    internal static partial class Mount
    {
        internal sealed partial class Instance
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

            private bool mounted;
            private Wgo donkey;
            private bool partsReady;
            private MountStyle activeStyle;

            internal static int gearHideUntilFrame = -1; // frames after a dismount during which the rod stays hidden

            // The donkey is kept from rendering from the moment its view exists until one frame after PrepareParts: the
            // facing is written to the animator that frame, but the animator mirrors the sprites one evaluation later, so
            // the prefab's default facing would show for a frame.
            // Renderer.forceRenderingOff, not Renderer.enabled: the donkey's clips key the sprite renderers' enabled flag,
            // so the animator switched them straight back on (measured 2026-09-30 in the user's 120 fps video: with
            // enabled=false the 3D cart body stayed hidden but the donkey, wheel and lamp sprites showed for a game frame
            // on the wrong side). forceRenderingOff is not animatable and leaves enabled alone; the game hides ground
            // items in hidden chunks the same way (DropView.ApplyRendererVisibility).
            private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
            private int revealFrame = -1;

            // Adds to what OnVisualPartsLoaded already hid (those stay hidden until this reveal), so every renderer that
            // was on comes back once, one frame after the parts are prepared.
            private void HideDonkeyForAFrame()
            {
                HideRenderersNow();
                revealFrame = UnityEngine.Time.frameCount + 1;
            }

            private void HideRenderersNow()
            {
                foreach (Renderer r in donkey.GetComponentsInChildren<Renderer>(includeInactive: true))
                {
                    if (!r.forceRenderingOff)
                    {
                        r.forceRenderingOff = true;
                        hiddenRenderers.Add(r);
                    }
                }
            }

            // A renderer added to the donkey after the hide (the mod's hanging lamp): kept from rendering until the same
            // reveal. Nothing to do once the donkey is revealed.
            internal void HideWithDonkeyWhilePending(Renderer r)
            {
                if (r != null && hiddenRenderers.Count > 0 && !r.forceRenderingOff)
                {
                    r.forceRenderingOff = true;
                    hiddenRenderers.Add(r);
                }
            }

            private void RevealDonkeyIfDue()
            {
                if (hiddenRenderers.Count == 0 || UnityEngine.Time.frameCount <= revealFrame)
                {
                    return;
                }
                foreach (Renderer r in hiddenRenderers)
                {
                    if (r != null && r && !donkeyOnly.Contains(r)) // a free cart's donkey stays hidden
                    {
                        r.forceRenderingOff = false;
                    }
                }
                hiddenRenderers.Clear();
            }

            // The view of this instance (null until spawned, and while its scene is not loaded).
            internal Wgo Donkey => donkey;

            // General.MountWgoId as it was when this instance was made, without the spaces a hand edit or a paste may
            // leave around it.
            private string WgoId => wgoId ?? (wgoId = (Plugin.MountWgoId.Value ?? "").Trim());
            private string wgoId;

            // Cargo was added or removed: the prefab's own drawer may have swapped the empty and the loaded cart body, so
            // the list of lifted parts is rebuilt from the parts active now. Every part goes back to its prefab position
            // first, so the current lift is not baked into the new base positions.
            internal void OnCargoChanged()
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
                CargoRig.RefreshViews();
                cartParts.Clear();
                logGeometry = false; // measured and logged at mount already; a cargo change only re-collects
                try
                {
                    CollectCartParts();
                }
                finally
                {
                    logGeometry = true;
                }
            }

            private bool logGeometry = true;
            private float facingSign = 1f; // +1 when the donkey shows its right-facing sprite, -1 for left
            private AnimationState lastMirrored = (AnimationState)(-1);
            private Transform bodySprite; // the donkey's "bdy" sprite; its offset from the root is what centering cancels
            private SpriteRenderer donkeyBodyRenderer;
            private SpriteRenderer keeperBodyRenderer;
            private bool seatPending;
            private float groundDelta; // donkey ground height minus the keeper's, world units (slopes)
            private float lastGoodGroundDelta;
            private const float RideGroundStepLimit = 0.5f; // world units between the donkey's and the keeper's sample
            private float nextGroundLog;
            private int groundRejected;

            // A rejected ride sample, logged at most twice a second with how many since the last line.
            private void NoteRejectedGround(float gd, string donkeySrc, float gk, string keeperSrc, Vector3 at)
            {
                groundRejected++;
                if (Time.unscaledTime < nextGroundLog)
                {
                    return;
                }
                nextGroundLog = Time.unscaledTime + 0.5f;
                Plugin.Log.LogInfo($"[ground] ride sample rejected ({groundRejected} since the last line): under the donkey {gd:F3} ({donkeySrc}), under the keeper {gk:F3} ({keeperSrc}), keeper at {at:F2}; kept {lastGoodGroundDelta:F3}.");
                groundRejected = 0;
            }
            private string lastDonkeyGroundSource = "-", lastKeeperGroundSource = "-";
            private string lastCartGroundSource = "-";
            private float lastCartGround;
            private Vector4 lastGroundSample;

            // Cart parts, each lifted to the ground under itself (CartFollowsGround). The cart is not a sprite: wheels,
            // bars, cart body and lantern are separate Object3D meshes (plus two wheel sprites) that extend up to 1.2
            // units behind the donkey along the ground. A flat, upright donkey sprite touches the ground only along its
            // bottom row, so it never shows a slope; the cart's rigid extent does, and the terrain mesh depth-tests
            // over the parts that end up below it. Base = the prefab's local position, Sample = where the part's
            // renderer sits (parent-local), Weight = how much of the ground difference it takes (bars: half).
            private sealed class CartPart
            {
                public Transform T;
                // The cargo holder (Cargo.ContainerName), told once: a Transform's name is a new string on every read,
                // and the cart's parts are walked every frame.
                public bool IsContainer;
                public Vector3 Base;
                public Vector3 Sample;
                public float Weight;
                public float LastLift;
                // Shafts only (CartSlope = ShaftsTilt, and the harness for WholeCartPitch): the harness end in parent space, the signed length from there to the cart
                // end along the parent's x, the animator's rotation of this frame and what the mod wrote last frame.
                public bool IsShafts;
                public Vector3 Harness;
                public float ReachX;
                public Quaternion AnimRot = Quaternion.identity;
                public Quaternion WrittenRot = Quaternion.identity;
                public bool Written;
                public float LastTiltDeg;
                // WholeCartPitch: pitched with the cart body, and the axle in this part's parent space.
                public bool Pitched;
                public Vector3 Pivot;
                public float LastGround;
                public string LastSource = "-";
            }
            private readonly System.Collections.Generic.List<CartPart> cartParts = new System.Collections.Generic.List<CartPart>();
            private float lastCartRefGround = float.NaN;
            private CartPart cartBodyPart; // the part whose ground sets the whole cart's shift

            // Ground height at a point, the way the game's WgoMovementAdjustComponent finds it: the scene's navigation
            // mesh first (nearest walkable node as seen from above, accepted if it is under the point), the ground raycast
            // (walkable layers 11/12) only as a fallback. NaN when neither knows.
            // "raycast:<collider>@<layer>", made once per collider: built on every ground check it was most of the ground
            // checks' garbage, and only failure messages read it (measured 2026-10-01).
            private static readonly Dictionary<int, string> raycastSources = new Dictionary<int, string>();

            private static string RaycastSource(Collider c)
            {
                int id = c.GetInstanceID();
                if (!raycastSources.TryGetValue(id, out string s))
                {
                    if (raycastSources.Count > 512)
                    {
                        raycastSources.Clear();
                    }
                    s = "raycast:" + c.name + "@" + LayerMask.LayerToName(c.gameObject.layer);
                    raycastSources[id] = s;
                }
                return s;
            }

            internal static float GroundHeight(Vector3 p, out string source)
            {
                using var perf = Perf.Measure(Perf.S.Ground);
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
                        if (nn.node != null && dx * dx + dz * dz < 0.05f * 0.05f) // no node: position is meaningless (the game checks this too)
                        {
                            source = "navmesh";
                            return np.y;
                        }
                    }
                    if (Physics.Raycast(new Ray(p + Vector3.up * 1f, Vector3.down), out RaycastHit hit, 10f, 6144))
                    {
                        source = RaycastSource(hit.collider);
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
            private Transform seatRoot;
            private Vector3 seatRest;
            private float seatApplied;

            public bool IsMounted => mounted;

            // Settings of this instance's style: Cart with the cart, Ride without (instance overrides on top, see Tuned).
            internal StyleSettings S => Plugin.For(activeStyle);

            // Side view is on when the setting says so, or when the selected pose only exists as left/right art
            // (FishingIdle): with the keeper facing up or down the animator has no frame and the body renders nothing.
            // While mounted the keeper faces only left or right, like the donkey (it has no up/down art, and the riding
            // poses in use have only left/right frames). The option to let him turn freely is gone (2026-09-30): it only
            // ever looked wrong.
            public bool SideView => mounted;

            // The animator state the keeper is held in while riding (see the SetState prefix); Cart keeps the walk.
            public AnimationState RiderPoseState =>
                (AnimationState)(int)S.Pose.Value;

            // Horizontal facing shared by rider and mount while mounted: +1 right, -1 left.
            private float riderFacingX = 1f;

            // Called from the SetDirection prefix for the keeper's animation component (see Patches.cs).
            public Vector2 RiderFacing(Vector2 requested)
            {
                if (requested.sqrMagnitude < 0.0001f)
                {
                    return requested;
                }
                if (Mathf.Abs(requested.x) > 0.05f)
                {
                    float was = riderFacingX;
                    riderFacingX = requested.x > 0f ? 1f : -1f;
                    if (riderFacingX != was && donkey != null && donkey)
                    {
                        // The donkey turns in the same moment as the keeper, before this frame's animation runs. Turned
                        // only in Follow (LateUpdate, after the animators), it showed its old side for a frame or two
                        // with the keeper already turned: at every turn its head jumped across the rider, read as a
                        // second donkey flickering next to him (the user's videos, 2026-10-01 and 2026-10-02).
                        donkey.Data.MovableDirection = new Vector2(riderFacingX, 0f);
                    }
                }
                return new Vector2(riderFacingX, 0f);
            }

            // ------------------------------------------------------------------ per frame

            // The ridden instance's frame (Mount.Tick calls it with this instance bound).
            internal void Tick(PlayerController pc, GameScene scene)
            {
                if (!mounted)
                {
                    return;
                }

                if (Plugin.TuningKeys.Value)
                {
                    TuningKeys(pc);
                }
                string why = WhyMustDismount(pc);
                if (why != null)
                {
                    Dismount(pc, why);
                    return;
                }
                MountRules.Poll(force: false);
                string ruleWhy = MountRules.WhyMustDismountIfDue();
                if (ruleWhy != null)
                {
                    Say(ruleWhy); // another mod's reason: tell the player why the ride ended
                    Dismount(pc, ruleWhy);
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
                    if (!Plugin.StayMountedThroughDoors.Value || !MountRules.Allowed(MountFeature.StayThroughDoors))
                    {
                        Dismount(pc, "scene changed");
                        return;
                    }
                    if (!TrySpawn(pc, scene))
                    {
                        Dismount(pc, "could not re-summon the donkey");
                        return;
                    }
                    SceneId = scene.Id;
                    Plugin.Log.LogInfo("Mount: donkey re-summoned after scene change.");
                }

                if (!partsReady && donkey.MainWgoPart != null)
                {
                    PrepareParts(pc);
                }
                // Empty hands while riding (HeadReroute.HandsLocked). Checked once the parts are ready, because in Cart style
                // PrepareParts attaches the cart, which first takes what the keeper holds; what is still held after that
                // (Ride style, Cargo off, cart full, a zombie) is set down.
                if (partsReady && !CargoRig.Unloading && !CargoSheet.Running && pc.PlayerData.HasOverheadItem)
                {
                    int n = Cargo.SetDownHeld(pc.PlayerData);
                    if (n > 0)
                    {
                        Plugin.Log.LogInfo($"Mount: set down {n} held item(s); the keeper rides with empty hands.");
                    }
                }

                // Only the attack states touch SpeedMultiplier, and we dismount before they can run; still, keep it ours.
                if (pc.Ssm?.CurState is FreePlayerState && Mathf.Abs(pc.PhysicalBody.SpeedMultiplier - S.SpeedMultiplier.Value) > 0.001f)
                {
                    pc.PhysicalBody.SpeedMultiplier = S.SpeedMultiplier.Value;
                }

                Follow(pc);
                RevealDonkeyIfDue();
                if (partsReady)
                {
                    try
                    {
                        MountSounds.Tick(donkey, pc.PlayerData.charState.Value == AnimationState.Walk);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning("Sounds: update failed: " + e.Message);
                    }
                }
                if (activeStyle == MountStyle.Cart && partsReady)
                {
                    LanternRig.Tick();
                }
                if (partsReady)
                {
                    ColorsRig.Tick(); // after the lantern: the hanging lamp has copied the pole's look (its LUT too)
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
                        if (Overrides.Remove("Pose"))
                        {
                            // This instance's own setting: dropped; everyone's value is untouched.
                            OverrideOwners.Remove("Pose");
                            Plugin.Log.LogWarning($"Mount: that pose was {Describe()}'s own setting; it is removed.");
                        }
                        else if (S.Pose.HasOverride)
                        {
                            // Another mod set it (MountTuning): drop that override; writing the cfg would change nothing.
                            Plugin.Log.LogWarning($"Mount: that pose was set by {S.Pose.OverrideOwner}; its override is removed.");
                            S.Pose.ClearOverride();
                        }
                        else
                        {
                            S.Pose.Value = RiderPose.Idle;
                        }
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
            internal static RiderPose lastRiderPose = RiderPose.Walk;

            internal static void HidePoseGear(PlayerController pc, RiderPose pose)
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

            // Mounts.TryMount: null when mounted, else the reason (logged, and said when showMessage).
            internal string TryMountFromApi(MountStyle style, bool showMessage)
            {
                if (!IsInGame(out PlayerController pc, out GameScene scene))
                {
                    return "Not in a game.";
                }
                return TryMount(pc, scene, style, showMessage);
            }

            // A feature rule changed its answer (MountRules.Poll). Lantern, light, sounds, bell and doors are read where
            // they are used; cargo already on the cart has to be handed back when cargo is switched off.
            internal void OnFeatureChanged(MountFeature feature, bool allowed)
            {
                if (feature == MountFeature.Cargo && !allowed && mounted && CargoRig.Count > 0)
                {
                    CargoRig.Unload("cargo switched off", toHands: false); // still riding: the hands stay empty
                }
                if (feature == MountFeature.Lantern || feature == MountFeature.LanternLight)
                {
                    LanternRig.Refresh();
                }
            }

            // A tuning override changed (MountTuning). Values read every frame need nothing; these apply what the panel's
            // own rows apply after a change: speed, seat, legs, pose, lantern pose. Geometry measured at summon waits for
            // the next mount.
            // style null: a General (behaviour) value, which applies to either style.
            internal void OnTuningChanged(MountStyle? style)
            {
                if (!style.HasValue)
                {
                    Colors.MarkDirty(); // a Colors (or behaviour) override: rebuilt at the next frame
                }
                if (!mounted || (style.HasValue && activeStyle != style.Value))
                {
                    return;
                }
                try
                {
                    RefreshSpeed();
                    RefreshSeat();
                    RefreshLeg();
                    ApplyPoseNow();
                    if (activeStyle == MountStyle.Cart)
                    {
                        LanternRig.Refresh();
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("API: applying a tuning change failed: " + e.Message);
                }
            }

            // Mounts.Dismount: the normal dismount in a game, the cleanup otherwise.
            internal void DismountFromApi(string reason)
            {
                PlayerController pc = MainGame.Instance != null ? MainGame.PlayerController : null;
                if (pc != null)
                {
                    Dismount(pc, reason);
                }
                else
                {
                    ForceCleanup(reason);
                }
            }

            private string TryMount(PlayerController pc, GameScene scene, MountStyle style, bool showMessage)
            {
                // The core's own checks first (safety); other mods' rules can only add refusals on top.
                string reason = WhyCannotMount(pc) ?? MountRules.WhyCannotMount(style);
                if (reason != null)
                {
                    Plugin.Log.LogInfo("Mount refused: " + reason);
                    if (showMessage)
                    {
                        Say(reason);
                    }
                    return reason;
                }
                MountRules.Poll(force: true);
                activeStyle = style;
                Position = pc.PlayerData.position.Value;
                riderAttached = true; // sounds are bound from the mount on (MountSounds.OnMounted below)
                if (!TrySpawn(pc, scene))
                {
                    reason = "The donkey is nowhere to be found.";
                    if (showMessage)
                    {
                        Say(reason);
                    }
                    return reason;
                }
                mounted = true;
                lastGoodGroundDelta = 0f; // a new ride: no ride sample yet (Ride style ground, Mount.cs)
                Ridden = this;
                MountSounds.OnMounted();
                riderFacingX = pc.PlayerData.Direction.x < -0.05f ? -1f : 1f;
                if (SideView)
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
                Mounts.RaiseMounted(activeStyle);
                return null;
            }

            private string WhyCannotMount(PlayerController pc)
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
                WGODef def = GameBalance.Me?.GetData<WGODef>(WgoId);
                if (def == null)
                {
                    return "There is no such animal (" + WgoId + ").";
                }
                string sideEffect = WhyDefinitionTouchesTheSave(def);
                if (sideEffect != null)
                {
                    return "Can't ride a " + WgoId + ": " + sideEffect;
                }
                return null;
            }

            // Constructing a WgoData (WgoData.SetDataFromDefinition) writes into the save for some definitions: town
            // quality is added to TownSystem.Quality, an NPC life-sim group gets the object's GUID appended to its saved
            // list, and start items are created in its inventory (which the cart would then hand out on dismount). The
            // donkey has none of these (Round 11), but MountWgoId is a free text setting, so a definition that would
            // leave a mark in the save is refused before anything is constructed.
            internal static string WhyDefinitionTouchesTheSave(WGODef def)
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
            // Asked every frame while riding: Enum.GetValues makes a new array each call, and a foreach over it boxes each value.
            private static readonly TakenControlType[] ControlTypes = (TakenControlType[])Enum.GetValues(typeof(TakenControlType));

            private string WhyMustDismount(PlayerController pc)
            {
                SSMState cur = pc.Ssm?.CurState;
                if (!(cur is FreePlayerState))
                {
                    return "state " + (cur?.GetType().Name ?? "none");
                }
                foreach (TakenControlType t in ControlTypes)
                {
                    // ByUI: chests/inventory. ByTeleport: doors. BySelf: set by the animator itself while certain
                    // states play (tool, work and some fishing poses carry CharacterSelfStateMachineBehaviour); with a
                    // pose like that selected it is our own doing, so it must not end the ride. ByFlow: below.
                    if (t == TakenControlType.ByUI || t == TakenControlType.ByTeleport || t == TakenControlType.BySelf
                        || t == TakenControlType.ByFlow)
                    {
                        continue;
                    }
                    if (!pc.IsControlEnabledByType(t))
                    {
                        return "control taken " + t;
                    }
                }
                // A script holding control (an NPC's conversation, a quest moment, the town building window): the ride
                // goes on while it only talks, and ends as soon as it stages something with the keeper
                // (docs/mounts-talk-while-mounted.md).
                if (!pc.IsControlEnabledByType(TakenControlType.ByFlow))
                {
                    string staged = ScriptStaged;
                    if (staged != null)
                    {
                        return "script: " + staged;
                    }
                    if (pc.MovementComponent != null && pc.MovementComponent.IsMoving)
                    {
                        return "script: scripted walk";
                    }
                    if (CinematicShown())
                    {
                        return "script: cinematic";
                    }
                }
                else
                {
                    ScriptStaged = null;
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

            // The letterbox bars of a staged scene (Flow_Cinematic, Flow_SetControlActive with cinematic).
            private static bool CinematicShown()
            {
                try
                {
                    UICinematic c = LazyBearTechnology.LazyUI.Get<UICinematic>();
                    return c != null && c.gameObject.activeSelf;
                }
                catch
                {
                    return false;
                }
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

            private void Dismount(PlayerController pc, string reason)
            {
                if (!Ephemeral)
                {
                    Park(pc, reason);
                    return;
                }
                mounted = false;
                if (Ridden == this) Ridden = null;
                gearHideUntilFrame = UnityEngine.Time.frameCount + 8;
                CargoRig.Unload(reason); // cargo lives in a temp inventory; hand it back before the view goes
                DestroyDonkey();
                ColorsRig.Release();
                RestoreSeat();
                RestoreSpeed(pc);
                RestorePose(pc);
                Unregister(this);
                Plugin.Log.LogInfo("Dismounted (" + reason + ").");
                Mounts.RaiseDismounted(reason);
            }

            // Used when the player object may already be gone (menu, plugin unload, error).
            // An instance in the world is only taken down (its cargo stays where it is: nothing to hand it to, or the
            // game is closing); the old summon hands its cargo back first, as always.
            public void ForceCleanup(string reason)
            {
                bool was = mounted;
                mounted = false;
                if (Ridden == this) Ridden = null;
                gearHideUntilFrame = UnityEngine.Time.frameCount + 8;
                try
                {
                    if (Ephemeral)
                    {
                        CargoRig.Unload(reason);
                    }
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
                if (Ephemeral)
                {
                    ColorsRig.Release();
                    Unregister(this);
                }
                if (was)
                {
                    Plugin.Log.LogInfo("Dismounted (" + reason + ").");
                    Mounts.RaiseDismounted(reason);
                }
            }

            // If the keeper is standing still at dismount, nothing would call SetState again; put the real state back.
            private void RestorePose(PlayerController pc)
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
            private void RestoreSpeed(PlayerController pc)
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

            private bool TrySpawn(PlayerController pc, GameScene scene)
            {
                try
                {
                    string id = WgoId;
                    if (TakeParkedView(scene, id)) // a released view of the same kind (Tiers.cs, ViewPool): no Wgo.Spawn
                    {
                        switchedOff = false;
                        quietAnimators.Clear();
                        quietKeepState.Clear();
                        quietRenderers.Clear();
                        SceneId = scene.Id;
                        partsReady = false;
                        cartParts.Clear();
                        lastMirrored = (AnimationState)(-1);
                        return true;
                    }
                    WgoData data;
                    SuppressScripts = true;
                    try
                    {
                        // Not interactable: the game's interaction scan (PlayerInteractionComponent) skips such objects, so the
                        // donkey never offers the NPC's "[E] Speak", not even for the frame before PrepareParts.
                        data = new WgoData(id, mounted ? pc.PlayerData.position.Value : Position, scene.Id) { isTempObject = true, IsInteractable = false };
                        // The game's own scale (Wgo.Spawn puts it on the view's new root, never on the pooled parts); 1 but
                        // for a foal (Foals.cs).
                        data.Scale = Vector3.one * ViewScale;
                    }
                    finally
                    {
                        SuppressScripts = false;
                    }
                    donkey = Wgo.Spawn(data, scene.transform, registerInChunkManagerIfStatic: true, ignoreChunkRegistration: true, applyDefaultWgoPartState: true);
                    donkey.UpdateChunkVisibility(isVisible: true);
                    quietAnimators.Clear(); // the old view's (Tiers.cs): a view the game destroyed while idle left them
                    quietKeepState.Clear();
                    switchedOff = false;
                    quietRenderers.Clear();
                    SceneId = scene.Id;
                    partsReady = false;
                    cartParts.Clear();
                    lastMirrored = (AnimationState)(-1);
                    return true;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Mount: spawning [{WgoId}] failed: {e}");
                    DestroyDonkey();
                    return false;
                }
            }

            // Wgo.CompleteVisualPartsLoad just created our donkey's view. The part loader delivers the prefab some frames
            // after Wgo.Spawn and switches it on in its default state (facing left, cart shown), and a frame of that could
            // render before PrepareParts (found in game 2026-09-30: the cart on the wrong side for a frame). So it is hidden
            // here, in the same call, and stays hidden until PrepareParts (next LateUpdate) has set it up and the facing is
            // applied; HideDonkeyForAFrame then schedules the reveal. PrepareParts itself must stay in LateUpdate: it
            // runs once per summon (preparing here as well, tried the same day, prepared twice: with a pooled part this
            // call comes inside TrySpawn, which then resets partsReady, so LateUpdate prepared again on already moved
            // parts - scattered wheels, a second lantern attach; the log showed both sets, the first before "Mounted").
            internal void OnVisualPartsLoaded(Wgo wgo)
            {
                if (donkey == null || !ReferenceEquals(wgo, donkey) || partsReady)
                {
                    return;
                }
                hiddenRenderers.Clear();
                HideRenderersNow();
                revealFrame = int.MaxValue; // until PrepareParts
            }

            // Once the prefab is loaded: no blocking, no interaction target, no navmesh cut; cart per style.
            private void PrepareParts(PlayerController pc)
            {
                partsReady = true;
                try
                {
                    RememberColliders(); // put back with the rest before the parts return to the game's pool
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
                    RememberPartRest(); // the prefab's own state, put back before the parts return to the game's pool
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
                        CargoRig.Attach(donkey); // the cart's inventory is the cargo; creates the cargo holder before the parts are collected
                        cartParts.Clear();    // (re-adding carried-over cargo may already have collected once)
                        CollectCartParts();
                        LanternRig.Attach(donkey); // poses the pole's child nodes; the sway parents stay the animator's
                    }
                    donkey.MainWgoPart?.AnimationComponent?.SetWalkAnimationSpeedMultiplier(1.5f * (mounted ? pc.PhysicalBody.SpeedMultiplier : 1f));
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
                    if (!HasDonkey)
                    {
                        HideDonkeyOnly(); // a free cart: the donkey's own sprites, shadow and grass bend stay off
                    }
                    if (mounted)
                    {
                        AttachRider(pc);
                    }
                    ColorsRig.Attach(donkey); // the Colors tab: nothing is touched while every colour is at its default
                    HideDonkeyForAFrame(); // the facing is applied by Follow this frame; the animator shows it next frame
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Mount: preparing the donkey view failed: {e.Message}");
                }
            }

            // The rider's half of preparing: the keeper's body sprite, his late-update hook, the seat and the near leg.
            // At summon (from PrepareParts) and when the keeper gets onto an instance standing in the world.
            private void AttachRider(PlayerController pc)
            {
                riderAttached = true;
                keeperBodyRenderer = FindKeeperBody(pc);
                HookKeeperLateUpdate(pc);

                {
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
                }
            }

            // Top-level cart part transforms (the prefab names, active ones only) with their rest positions. Taken once,
            // right after spawn, before anything has moved them. The prefab reuses names down the tree (each Object3D's
            // mesh child is named like its parent, and the far wheel's mesh child is even called donkey_cart_wheel_f), so
            // a transform with a cart-part ancestor is skipped: moving it as well would shift the mesh twice.
            private void CollectCartParts()
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
                    var part = new CartPart { T = t, IsContainer = t.name == Cargo.ContainerName, Base = t.localPosition, Sample = t.localPosition, Weight = t.name == "donkey_cart_bars" ? 0.5f : 1f };
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
                CartPart shafts = cartParts.Find(p => p.T.name == "donkey_cart_bars");
                if (shafts != null)
                {
                    MeasureShafts(shafts, cartBodyPart);
                }
                // The rock reference for the cargo: one of the nodes the walk animation rotates (all three share the pivot
                // and the angle, Round 8). The lantern's is never switched off by the game's drawer; the cart bodies are.
                swayRefPart = cartParts.Find(p => p.T.name == "donkey_cart_lantern_sway")
                              ?? cartParts.Find(p => p.T.name == "donkey_cart_empty_sway")
                              ?? cartParts.Find(p => p.T.name == "donkey_cart_body_sway");
                ClassifyForPitch();
                if (logGeometry) Plugin.Log.LogInfo($"Mount: {cartParts.Count} cart parts follow the ground as one (sampled under {cartBodyPart?.T.name}): " + string.Join(", ", cartParts.ConvertAll(p => p.T.name + (p.Pitched ? "(pitched)" : ""))));
            }

            // The shafts are one rigid mesh from the donkey's harness to the front of the cart (0.76 long). Their extent
            // along the parent's x (the travel axis in Base space; facing only flips Base's scale, so parent-local values
            // hold for both facings) comes from the mesh bounds. The cart end is the end nearer the cart body, the
            // harness end the other one. (Not "nearer the donkey's root": the donkey is drawn 0.48 to its facing side of
            // the root, so the root sits near the cart end; measured x -0.66..0.10 with the cart body at +0.37.)
            private void MeasureShafts(CartPart part, CartPart body)
            {
                Transform t = part.T;
                part.AnimRot = t.localRotation;
                if (body == null || body.T == null || !Cargo.MeasureMeshes(t, t.parent, out Bounds b))
                {
                    Plugin.Log.LogWarning("Mount: the shafts or the cart body could not be measured; the shafts keep the half lift.");
                    return;
                }
                float bodyX = t.parent.InverseTransformPoint(body.T.parent.TransformPoint(body.Sample)).x;
                float minX = part.Base.x + b.min.x, maxX = part.Base.x + b.max.x;
                bool minIsCartEnd = Mathf.Abs(minX - bodyX) <= Mathf.Abs(maxX - bodyX);
                float harnessX = minIsCartEnd ? maxX : minX;
                float cartEndX = minIsCartEnd ? minX : maxX;
                part.Harness = new Vector3(harnessX, part.Base.y + b.center.y, part.Base.z + b.center.z);
                part.ReachX = cartEndX - harnessX;
                part.IsShafts = Mathf.Abs(part.ReachX) > 0.05f;
                if (logGeometry) Plugin.Log.LogInfo($"Mount: shafts x {minX:F2}..{maxX:F2} in {t.parent.name} space, cart body at x {bodyX:F2}: harness end x {harnessX:F2}, cart end x {cartEndX:F2}, reach {part.ReachX:F2}.");
            }

            // WholeCartPitch: everything that belongs to the cart body turns with it about the axle, the point the walk
            // animation rocks the cart about (the _sway nodes' rest position, Round 8): the sway nodes themselves (their
            // origin is that point), the shafts and the cargo holder (turned about the axle carried into their parent's
            // space). The wheels only follow the ground. A node under a stretched parent (the lamp rig's light_sway under
            // gfx, scale 1 x 1.667) would slide along a stretched arc if turned, so it is only moved by what its child
            // (the lamp, posed by Lantern.cs) must move to stay at the turned pole head, and the lamp keeps hanging.
            private void ClassifyForPitch()
            {
                Vector3 axleWorld = Vector3.zero;
                bool haveAxle = swayRefPart != null && swayRefPart.T != null && swayRefPart.T.parent != null;
                if (haveAxle)
                {
                    axleWorld = swayRefPart.T.parent.TransformPoint(swayRefPart.Base);
                }
                foreach (CartPart p in cartParts)
                {
                    bool sway = p.T.name.EndsWith("_sway", StringComparison.Ordinal);
                    p.Pitched = haveAxle && (sway || p.T.name == "donkey_cart_bars" || p.T.name == Cargo.ContainerName);
                    if (!p.Pitched)
                    {
                        continue;
                    }
                    p.Pivot = sway ? p.Base : p.T.parent.InverseTransformPoint(axleWorld);
                    Vector3 sc = p.T.parent.lossyScale;
                    if (sway)
                    {
                        Vector3 originW = p.T.parent.TransformPoint(p.Base);
                        if (logGeometry) Plugin.Log.LogInfo($"Mount: pitch node {p.T.name} under {p.T.parent.name}: origin {Vector3.Distance(originW, axleWorld):F3} from the axle, parent scale {sc:F3}.");
                    }
                    if (p.T.name != Cargo.ContainerName)
                    {
                        p.AnimRot = p.T.localRotation;
                    }
                }
            }

            // The pitch angle for this frame: the shafts' harness end (measured, MeasureShafts) must come down by the lift
            // relative to the lifted axle, i.e. stay at the donkey. 0 without measured shafts.
            private float lastPitchDeg;
            private bool lastPitchCapped;

            private float PitchDegrees(float liftLocalY)
            {
                CartPart shafts = cartParts.Find(p => p.IsShafts);
                lastPitchCapped = false;
                if (shafts == null || !shafts.Pitched)
                {
                    return 0f;
                }
                float lever = shafts.Harness.x - shafts.Pivot.x; // signed, axle to harness along the travel axis
                if (Mathf.Abs(lever) < 0.1f)
                {
                    return 0f;
                }
                float sin = -liftLocalY / lever;
                float cap = Mathf.Sin(Mathf.Clamp(S.CartPitchMaxDegrees.Value, 0f, 45f) * Mathf.Deg2Rad);
                if (Mathf.Abs(sin) > cap)
                {
                    lastPitchCapped = true;
                    sin = Mathf.Sign(sin) * cap;
                }
                return Mathf.Asin(sin) * Mathf.Rad2Deg;
            }

            // The back end of the cart on the ground and the way out behind it, for setting cargo down there
            // (CargoSetDownBehindCart). Measured from what is drawn this frame: "behind" is from the donkey's body to the
            // cart body, the back end is the farthest point of the cart body that way (renderer bounds), at the keeper's
            // height. Not the farthest of every cart part: the lantern hangs out past the tailboard (first version,
            // 2026-09-30: items landed well behind the cart). detail names the farthest part of all, for the log.
            // False when it has no cart or the cart is not drawn. A standing cart too (a donkey with its cart, a free
            // cart: its donkey's hidden body is where the donkey would stand), for the zones of the on-foot controls
            // (docs/mounts-load-on-foot.md).
            internal bool TryCartRear(out Vector3 rear, out Vector3 back, out string detail)
            {
                rear = back = Vector3.zero;
                detail = "";
                if (!HasCart || donkey == null || cartBodyPart == null || cartBodyPart.T == null)
                {
                    return false;
                }
                Vector3 front = bodySprite != null && bodySprite ? bodySprite.position : donkey.transform.position;
                front.y = 0f;
                bool any = false;
                Bounds body = default;
                foreach (Renderer r in cartBodyPart.T.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (!any) { body = r.bounds; any = true; } else body.Encapsulate(r.bounds);
                }
                if (!any)
                {
                    return false;
                }
                back = body.center;
                back.y = 0f;
                back -= front;
                if (back.sqrMagnitude < 0.01f)
                {
                    return false;
                }
                back.Normalize();
                float reach = Reach(body, front, back);
                float farthest = reach;
                string farthestName = cartBodyPart.T.name;
                foreach (CartPart p in cartParts)
                {
                    if (p.T == null) continue;
                    foreach (Renderer r in p.T.GetComponentsInChildren<Renderer>())
                    {
                        if (!r.enabled) continue;
                        float d = Reach(r.bounds, front, back);
                        if (d > farthest)
                        {
                            farthest = d;
                            farthestName = r.name;
                        }
                    }
                }
                detail = $"cart body ends {reach:0.00} behind the donkey's body; farthest drawn part {farthestName} at {farthest:0.00}";
                float y = MainGame.PlayerData != null ? MainGame.PlayerData.position.Value.y : donkey.transform.position.y;
                rear = front + back * reach;
                rear.y = y;
                return true;
            }

            // The cart body's drawn box in the cart's own frame, for the on-foot zones (InstanceControls.ZoneAt): from
            // the donkey's body (a free cart: where it would stand) along 'back', where the cart body starts and ends;
            // across it (Cross(back, up), positive to the cart's left as seen from behind), its two sides. Renderer
            // bounds of the cart body only, as TryCartRear (the lantern hangs out past it).
            internal bool TryCartBox(out Vector3 origin, out Vector3 back, out float front, out float end, out float left, out float right)
            {
                origin = back = Vector3.zero;
                front = end = left = right = 0f;
                if (!HasCart || donkey == null || cartBodyPart == null || cartBodyPart.T == null)
                {
                    return false;
                }
                origin = bodySprite != null && bodySprite ? bodySprite.position : donkey.transform.position;
                origin.y = 0f;
                bool any = false;
                Bounds body = default;
                foreach (Renderer r in cartBodyPart.T.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (!any) { body = r.bounds; any = true; } else body.Encapsulate(r.bounds);
                }
                if (!any)
                {
                    return false;
                }
                back = body.center;
                back.y = 0f;
                back -= origin;
                if (back.sqrMagnitude < 0.01f)
                {
                    return false;
                }
                back.Normalize();
                Vector3 side = Vector3.Cross(back, Vector3.up);
                front = float.PositiveInfinity; end = float.NegativeInfinity;
                left = float.NegativeInfinity; right = float.PositiveInfinity;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? body.min.x : body.max.x, 0f, (i & 2) == 0 ? body.min.z : body.max.z) - origin;
                    float a = Vector3.Dot(c, back), s = Vector3.Dot(c, side);
                    front = Mathf.Min(front, a); end = Mathf.Max(end, a);
                    left = Mathf.Max(left, s); right = Mathf.Min(right, s);
                }
                origin.y = MainGame.PlayerData != null ? MainGame.PlayerData.position.Value.y : donkey.transform.position.y;
                return true;
            }

            // How far a box reaches from 'from' along 'dir' (horizontal).
            private static float Reach(Bounds b, Vector3 from, Vector3 dir)
            {
                float reach = float.NegativeInfinity;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, 0f, (i & 2) == 0 ? b.min.z : b.max.z);
                    reach = Mathf.Max(reach, Vector3.Dot(c - from, dir));
                }
                return reach;
            }

            public string CartSlopeStatus()
            {
                if (!mounted || activeStyle != MountStyle.Cart || cartParts.Count == 0)
                {
                    return "";
                }
                string s = $"Now: lift {cartShiftApplied:F2}";
                switch (S.CartSlope.Value)
                {
                    case CartSlopeStyle.WholeCartPitch: s += $", pitch {lastPitchDeg:F1}°{(lastPitchCapped ? " (at the cap)" : "")}"; break;
                    case CartSlopeStyle.ShaftsTilt:
                        CartPart sh = cartParts.Find(p => p.IsShafts);
                        s += $", shafts tilt {(sh != null ? sh.LastTiltDeg : 0f):F1}°"; break;
                }
                return s + ".";
            }

            // CartSlope = ShaftsTilt: the harness end stays with the donkey and the cart end follows the cart's lift, a turn about
            // the harness end in the travel plane (parent z). The animator rocks the shafts too (Round 8, about -4..0
            // degrees), so the tilt goes on top of its rotation of this frame: a rotation that differs from what the mod
            // wrote last frame is the animator's; an unchanged one means the animator left it, and last frame's is kept.
            private void PoseShafts(CartPart p, float liftLocalY, bool tilt)
            {
                Quaternion cur = p.T.localRotation;
                if (!p.Written || Quaternion.Angle(cur, p.WrittenRot) > 0.01f)
                {
                    p.AnimRot = cur;
                }
                float deg = 0f;
                if (tilt)
                {
                    deg = Mathf.Asin(Mathf.Clamp(liftLocalY / p.ReachX, -0.9f, 0.9f)) * Mathf.Rad2Deg;
                }
                Quaternion r = Quaternion.AngleAxis(deg, Vector3.forward);
                p.T.localRotation = r * p.AnimRot;
                p.T.localPosition = p.Harness + r * (p.Base - p.Harness);
                p.WrittenRot = p.T.localRotation;
                p.Written = true;
                p.LastTiltDeg = deg;
            }

            private bool HasCartPartAncestor(Transform t)
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
            private float cartShiftTarget, cartShiftApplied;
            private float nextCartAnomalyLog;
            private int cartAnomalies;
            private Vector3 lastCartPoint;
            private bool hasLastCartPoint;

            // levelY is the keeper's own height, i.e. the ground level the cart belongs to: both samples look for ground
            // near that level, so a walkway or a cellar floor stacked above or below the road can never be picked.
            private void FollowCartParts(Vector3 bodyGroundPoint, float levelY)
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
                CartSlopeStyle style = on ? S.CartSlope.Value : CartSlopeStyle.HalfLift;
                // Pass 1: this frame's animator rotations (a rotation that differs from the mod's last write is the
                // animator's; an unchanged one means it left the node, and the last known one is kept).
                foreach (CartPart p in cartParts)
                {
                    if (p.T == null || !p.T || p.IsContainer)
                    {
                        continue;
                    }
                    Quaternion cur = p.T.localRotation;
                    if (!p.Written || Quaternion.Angle(cur, p.WrittenRot) > 0.01f)
                    {
                        p.AnimRot = cur;
                    }
                }
                float pitchDeg = 0f;
                if (style == CartSlopeStyle.WholeCartPitch && swayRefPart != null && swayRefPart.T != null && swayRefPart.T)
                {
                    float ly = Mathf.Abs(cartShiftApplied) < 0.0005f ? 0f : swayRefPart.T.parent.InverseTransformVector(Vector3.up * cartShiftApplied).y;
                    pitchDeg = PitchDegrees(ly);
                }
                lastPitchDeg = pitchDeg;
                Quaternion pitch = Quaternion.AngleAxis(pitchDeg, Vector3.forward);
                Quaternion swayAnim = swayRefPart != null ? swayRefPart.AnimRot : Quaternion.identity;
                foreach (CartPart p in cartParts)
                {
                    if (p.T == null || !p.T)
                    {
                        continue;
                    }
                    Vector3 fullLift = Mathf.Abs(cartShiftApplied) < 0.0005f ? Vector3.zero : p.T.parent.InverseTransformVector(Vector3.up * cartShiftApplied);
                    bool cargo = p.IsContainer;
                    if (cargo)
                    {
                        // The cargo rocks with the cart the way the game's loaded corpse does: that corpse hangs under
                        // donkey_cart_body_sway, which the walk animation rotates about the axle pivot by up to 3 degrees.
                        // The holder is not reparented (the game's drawer switches those nodes on and off); it copies the
                        // rotation (and the pitch) and turns about the axle every frame, after the animator.
                        bool pitchIt = style == CartSlopeStyle.WholeCartPitch && p.Pitched;
                        Quaternion r = S.CargoSwaysWithCart.Value && swayRefPart != null ? swayAnim : Quaternion.identity;
                        if (pitchIt)
                        {
                            r = pitch * r;
                        }
                        Vector3 pivot = pitchIt ? p.Pivot : (swayRefPart != null ? swayRefPart.Base : p.Base);
                        p.T.localRotation = r;
                        p.T.localPosition = pivot + r * (p.Base - pivot) + fullLift;
                        p.LastLift = cartShiftApplied;
                        continue;
                    }
                    if (style == CartSlopeStyle.WholeCartPitch && p.Pitched)
                    {
                        p.LastLift = cartShiftApplied;
                        p.LastTiltDeg = 0f;
                        p.T.localRotation = pitch * p.AnimRot;
                        p.T.localPosition = p.Pivot + fullLift + pitch * (p.Base - p.Pivot);
                        p.WrittenRot = p.T.localRotation;
                        p.Written = true;
                        continue;
                    }
                    if (style == CartSlopeStyle.ShaftsTilt && p.IsShafts)
                    {
                        p.LastLift = 0f;
                        float ly = fullLift.y;
                        PoseShafts(p, ly, tilt: true);
                        continue;
                    }
                    if (p.Written)
                    {
                        // Left a pitching or tilting style: hand the rotation back to the animator's value once.
                        p.T.localRotation = p.AnimRot;
                        p.Written = false;
                        p.LastTiltDeg = 0f;
                    }
                    float lift = cartShiftApplied * p.Weight;
                    p.LastLift = lift;
                    Vector3 liftVec = Mathf.Abs(lift) < 0.0005f ? Vector3.zero : p.T.parent.InverseTransformVector(Vector3.up * lift);
                    p.T.localPosition = p.Base + liftVec;
                }
            }

            private CartPart swayRefPart;

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
                ga = HighestHitUnder(a, levelY, out Collider ca);
                gb = HighestHitUnder(b, levelY, out Collider cb);
                source = RaycastPairSource(ca, cb);
            }

            // "raycast:<collider>/<collider>", made once per pair (the cart's ground is checked every frame).
            private static readonly Dictionary<long, string> raycastPairSources = new Dictionary<long, string>();

            private static string RaycastPairSource(Collider a, Collider b)
            {
                long key = ((long)(a != null ? a.GetInstanceID() : 0) << 32) ^ (uint)(b != null ? b.GetInstanceID() : 0);
                if (!raycastPairSources.TryGetValue(key, out string s))
                {
                    if (raycastPairSources.Count > 512)
                    {
                        raycastPairSources.Clear();
                    }
                    s = "raycast:" + (a != null ? a.name : "none") + "/" + (b != null ? b.name : "none");
                    raycastPairSources[key] = s;
                }
                return s;
            }

            // Whether a collider is the walkable top of a dropped big item, told once per collider (its name is a new
            // string on every read).
            private static readonly Dictionary<int, bool> bigDropTops = new Dictionary<int, bool>();

            private static bool IsBigDropTop(Collider c)
            {
                int id = c.GetInstanceID();
                if (!bigDropTops.TryGetValue(id, out bool top))
                {
                    if (bigDropTops.Count > 1024)
                    {
                        bigDropTops.Clear();
                    }
                    top = c.name == "BigDropTopSurface";
                    bigDropTops[id] = top;
                }
                return top;
            }

            private static readonly RaycastHit[] rayHits = new RaycastHit[16];

            private static float HighestHitUnder(Vector3 p, float levelY, out Collider bestCollider)
            {
                int n = Physics.RaycastNonAlloc(new Ray(new Vector3(p.x, levelY + CartLevelWindow, p.z), Vector3.down), rayHits, 2f * CartLevelWindow, 6144);
                float best = float.NaN;
                bestCollider = null;
                for (int i = 0; i < n; i++)
                {
                    // The walkable top of a dropped big item (a log lying on the road) is 0.4 above the ground; the keeper
                    // may step on it, the cart must not climb it.
                    if (IsBigDropTop(rayHits[i].collider))
                    {
                        continue;
                    }
                    if (float.IsNaN(best) || rayHits[i].point.y > best)
                    {
                        best = rayHits[i].point.y;
                        bestCollider = rayHits[i].collider;
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
                    if (nn.node == null)
                    {
                        return float.NaN;
                    }
                    Vector3 np = nn.position;
                    float dx = np.x - p.x, dz = np.z - p.z;
                    return dx * dx + dz * dz < 0.05f * 0.05f && Mathf.Abs(np.y - levelY) <= CartLevelWindow ? np.y : float.NaN;
                }
                catch
                {
                    return float.NaN;
                }
            }


            private string CartLiftSummary()
            {
                if (cartParts.Count == 0)
                {
                    return "no cart parts";
                }
                var parts = new System.Collections.Generic.List<string>();
                parts.Add($"style={S.CartSlope.Value} pitch={lastPitchDeg:F1}deg{(lastPitchCapped ? " (capped)" : "")} refGround={lastCartRefGround:F3} target={cartShiftTarget:F3} applied={cartShiftApplied:F3} anomaliesLogged={cartAnomalies}");
                foreach (CartPart p in cartParts)
                {
                    if (p.T == null || !p.T)
                    {
                        continue;
                    }
                    parts.Add($"{p.T.name}: lift={p.LastLift:F3}{(p.IsShafts ? $" tilt={p.LastTiltDeg:F1}deg harness={p.Harness:F2} reach={p.ReachX:F2}" : "")} ground={p.LastGround:F3} ({p.LastSource}) lpos={p.T.localPosition:F3} base={p.Base:F3} lrot={p.T.localEulerAngles:F0} world={p.T.position:F2}");
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
            private void ApplyAutoSeat(PlayerController pc)
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
            private void TuningKeys(PlayerController pc)
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

            public MountStyle CurrentStyle => activeStyle;



            public void RefreshSpeed()
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

            public void RefreshSeat()
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

            public void RefreshLeg()
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

            public void ApplyPoseNow()
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

            private void Follow(PlayerController pc)
            {
                PlayerData pd = pc.PlayerData;
                // The keeper's sprite is locked to left/right while mounted (SetDirection prefix); give the donkey that
                // same facing so the two never disagree.
                if (Mathf.Abs(pd.Direction.x) > 0.05f)
                {
                    riderFacingX = pd.Direction.x > 0f ? 1f : -1f; // the one facing source for torso shift and legs
                }
                Vector2 dir = SideView ? new Vector2(riderFacingX, 0f) : pd.Direction;
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
                float sign = Mathf.Abs(lateral) > 0.001f ? Mathf.Sign(lateral) : (SideView ? riderFacingX : (dir.x >= 0f ? 1f : -1f));
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
                    // Both points from one source, as the cart does (GroundHeightPair): the navmesh for both, else the
                    // raycast for both. Sampled one by one, one point could get the navmesh and the other the raycast,
                    // two different levels where they lie stacked: at the bridge's ends the navmesh under the deck (6.0)
                    // and the deck itself (6.3 to 6.5). The mount and the rider's seat then dropped for single frames
                    // in Ride style only, the one that snaps to the ground (the user's videos and [ground] lines,
                    // 2026-10-01). The step limit below stays as the backstop.
                    GroundHeightPair(target + bodyOffset, basePos, basePos.y, out float gd, out float gk, out string pairSource);
                    lastDonkeyGroundSource = lastKeeperGroundSource = pairSource;
                    lastCartGround = GroundHeight(target + bodyOffset - right * (sign * 1.2f), out lastCartGroundSource);
                    if (!float.IsNaN(gd) && !float.IsNaN(gk))
                    {
                        float d = gd - gk;
                        if (Mathf.Abs(d) > RideGroundStepLimit)
                        {
                            // Steeper than any walkable slope over the half unit between the two samples: a wrong level,
                            // not the ground (a narrow bridge: the donkey's feet off the deck's navmesh, the raycast then
                            // finding the bank under it; the mount dropped for single frames, the user's video
                            // 2026-10-01). The last good difference is kept.
                            d = lastGoodGroundDelta;
                            NoteRejectedGround(gd, lastDonkeyGroundSource, gk, lastKeeperGroundSource, basePos);
                        }
                        else
                        {
                            lastGoodGroundDelta = d;
                        }
                        groundDelta = d;
                        target.y = basePos.y + groundDelta;
                    }
                    lastGroundSample = new Vector4(gd, gk, basePos.y, groundDelta);
                }
                donkey.Data.Position = VisualConsts.GetRoundedPosXYZ(target, PixelSize());
                Position = donkey.Data.Position;
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
            private float idleSince = -1f;
            private bool riderFrozen;

            private void FreezeRiderWhenIdle(PlayerController pc, AnimationState state)
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

            private float fidgetUntil = -1f, nextFidget;

            private float NextFidgetDelay()
            {
                float every = S.IdleFidgetEverySeconds.Value;
                return every <= 0f ? float.MaxValue / 4f : every * UnityEngine.Random.Range(0.5f, 1.5f);
            }

            private void UnfreezeRider(PlayerController pc)
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

            private void DestroyDonkey()
            {
                // The game pools the donkey's parts (Wgo.OnDestroy hands them back): renderers, colours, the lantern,
                // the cargo holder and the cart parts go back to the prefab's state first (ReleaseView), or the next
                // donkey built from the pool, ours or the game's delivery donkey, would inherit them.
                ReleaseView();
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
                // The rider's side of the view (sounds, the near leg, the keeper's late-update hook): whenever a rider was on
                // this view. Not "mounted": a dismount clears that before it gets here, and the old code ran these always.
                if (riderAttached)
                {
                    MountSounds.OnDonkeyGone();
                    RiderLeg.Detach();
                    UnhookKeeperLateUpdate();
                }
                riderAttached = mounted; // still ridden (a restyle): the next PrepareParts attaches the rider again
                hiddenRenderers.Clear();
                seatPending = false;
                bodySprite = null;
                donkeyBodyRenderer = null;
            }

            // ------------------------------------------------------------------ rider sprite

            // The keeper's sprites hang under one transform (the one with the Animator, "main_character", at a fixed
            // local height). Shifting it is enough; nothing in the game writes that local position at runtime.
            // World-space shift currently applied to the torso by Ride.TorsoOffsetPx (zero when not riding).
            internal static Vector3 TorsoShiftWorld;

            // +1 when the keeper faces right (mirrored art), -1 when facing left.
            // +1 facing right, -1 facing left. Taken from the tracked facing: Unity reports the game's mirroring scales
            // back inconsistently, so inspecting transforms gave the same sign for both facings.
            private float TorsoFacingSign()
            {
                return riderFacingX;
            }

            internal bool FacingRight => riderFacingX > 0f;

            // The sprite root's rest position is remembered once; the seat is always "rest + offset", so repeated
            // updates (every frame while riding, to follow the donkey's back) never accumulate.
            private void ApplySeat(PlayerController pc, float offset)
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

            private void RestoreSeat()
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
            private float? ComputeAutoSeat()
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
            // Per frame while riding, two or three times: a sprite's name is a new string on every read, so the answer is
            // kept per sprite (the donkey's atlas has a few dozen frames).
            private static readonly Dictionary<int, float> backHeights = new Dictionary<int, float>();
            private static bool warnedBackHeights;

            internal static float BackHeightPx(Sprite donkeyBody)
            {
                if (donkeyBody == null)
                {
                    return 25f;
                }
                int id = donkeyBody.GetInstanceID();
                if (!backHeights.TryGetValue(id, out float h))
                {
                    if (backHeights.Count > 256)
                    {
                        // More frames than the atlas has: the sprites would be new objects, and this cache no help.
                        if (!warnedBackHeights)
                        {
                            warnedBackHeights = true;
                            Plugin.Log.LogInfo("[perf] the back-height cache filled (256 sprites): the donkey's body sprites are new objects, not reused.");
                        }
                        backHeights.Clear();
                    }
                    h = BackHeightPxByName(donkeyBody.name);
                    backHeights[id] = h;
                }
                return h;
            }

            private static float BackHeightPxByName(string n)
            {
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
            private Action keeperLateUpdate;
            private bool warnedNoSprite;
            private AnimationComponent hookedAnimation;

            private void HookKeeperLateUpdate(PlayerController pc)
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

            private void UnhookKeeperLateUpdate()
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

            private void OnKeeperLateUpdate(PlayerController pc)
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
            private void TickSeat(PlayerController pc)
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

            private string CartPartsSummary()
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
            public void LogGeometry()
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

            internal static bool IsInGame(out PlayerController pc, out GameScene scene)
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
}
