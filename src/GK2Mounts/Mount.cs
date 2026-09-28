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
        };

        private static bool mounted;
        private static Wgo donkey;
        private static bool partsReady;
        private static MountStyle activeStyle;
        private static float facingSign = 1f; // +1 when the donkey shows its right-facing sprite, -1 for left
        private static AnimationState lastMirrored = (AnimationState)(-1);
        private static Transform bodySprite; // the donkey's "bdy" sprite; its offset from the root is what centering cancels
        private static SpriteRenderer donkeyBodyRenderer;
        private static SpriteRenderer keeperBodyRenderer;
        private static bool seatPending;
        private static Transform seatRoot;
        private static Vector3 seatRest;
        private static float seatApplied;

        public static bool IsMounted => mounted;

        // Side view is on when the setting says so, or when the selected pose only exists as left/right art
        // (FishingIdle): with the keeper facing up or down the animator has no frame and the body renders nothing.
        public static bool PoseNeedsSideView => Plugin.Pose.Value == RiderPose.FishingIdle;
        public static bool SideView => Plugin.SideViewOnly.Value || (mounted && PoseNeedsSideView);

        // The animator state the keeper is held in while riding (see the SetState prefix); Cart keeps the walk.
        public static AnimationState RiderPoseState =>
            activeStyle == MountStyle.Cart ? AnimationState.Walk : (AnimationState)(int)Plugin.Pose.Value;

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

            // Unity null: the scene that owned the view was unloaded (door to another scene) or the game removed it.
            if (donkey == null || !donkey)
            {
                donkey = null;
                if (!Plugin.StayMountedThroughDoors.Value)
                {
                    Dismount(pc, "scene changed");
                    return;
                }
                if (!pc.IsControlsEnabled)
                {
                    return; // still teleporting; try again when control is back
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
            if (pc.Ssm?.CurState is FreePlayerState && Mathf.Abs(pc.PhysicalBody.SpeedMultiplier - Plugin.SpeedMultiplier.Value) > 0.001f)
            {
                pc.PhysicalBody.SpeedMultiplier = Plugin.SpeedMultiplier.Value;
            }

            Follow(pc);
            if (seatPending && partsReady)
            {
                seatPending = false;
                ApplyAutoSeat(pc);
            }
            HideFishingGear(pc);

            // A pose whose animator state takes control (BySelf) freezes the keeper: the free state goes inactive
            // and no movement is applied. Surface it in the panel and once in the log instead of silently sticking.
            bool blocks = !pc.IsControlEnabledByType(TakenControlType.BySelf);
            if (blocks != PoseBlocksMovement)
            {
                PoseBlocksMovement = blocks;
                if (blocks && Plugin.Pose.Value != RiderPose.Idle && Plugin.Pose.Value != RiderPose.Walk)
                {
                    // Such a state ends by forcing Idle, which the SetState prefix would turn back into the pose:
                    // an enter/exit loop every frame (visible as flicker). Fall back to Idle for good.
                    Plugin.Log.LogWarning($"Mount: pose {Plugin.Pose.Value} makes the game take control of the keeper; falling back to Idle.");
                    Plugin.Pose.Value = RiderPose.Idle;
                    ApplyPoseNow();
                }
            }
        }

        // True while the selected pose's animator state holds player control (see above).
        public static bool PoseBlocksMovement { get; private set; }

        // The fishing animator states switch on the keeper's rod sprite layer ("tol_rod") and the game's fishing
        // container (rod, line, bob). A rider holds reins, not a rod: keep both off while mounted. Runs in LateUpdate,
        // after the animation has written its values for the frame; both objects are off by default when on foot.
        private static SpriteRenderer rodLayer;
        private static void HideFishingGear(PlayerController pc)
        {
            try
            {
                if (rodLayer == null || !rodLayer)
                {
                    Animator animator = pc.View?.PlayerAnimation?.Animator;
                    if (animator != null)
                    {
                        foreach (SpriteRenderer sr in animator.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
                        {
                            if (sr.name == "tol_rod")
                            {
                                rodLayer = sr;
                                break;
                            }
                        }
                    }
                }
                if (rodLayer != null && rodLayer && rodLayer.gameObject.activeSelf)
                {
                    rodLayer.gameObject.SetActive(false);
                }
                FishingContainer fc = pc.View?.FishingContainer;
                if (fc != null && fc.gameObject.activeSelf)
                {
                    fc.gameObject.SetActive(false);
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
            pc.PhysicalBody.SpeedMultiplier = Plugin.SpeedMultiplier.Value;
            // Ride with AutoSeat computes the seat once the donkey's sprite is loaded (PrepareParts).
            if (activeStyle == MountStyle.Cart)
            {
                ApplySeat(pc, Plugin.CartSeatOffset.Value);
            }
            else if (!Plugin.AutoSeat.Value)
            {
                ApplySeat(pc, Plugin.RideSeatOffset.Value);
            }
            Plugin.Log.LogInfo($"Mounted ({activeStyle}, speed x{Plugin.SpeedMultiplier.Value}).");
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
            if (!(pc.Ssm?.CurState is FreePlayerState))
            {
                return "I'm busy.";
            }
            if (BuildController.Instance != null && BuildController.Instance.IsBuildModeActive)
            {
                return "Not while building.";
            }
            if (GameBalance.Me?.GetData<WGODef>(Plugin.MountWgoId.Value) == null)
            {
                return "There is no such animal (" + Plugin.MountWgoId.Value + ").";
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
            return null;
        }

        private static void Dismount(PlayerController pc, string reason)
        {
            mounted = false;
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
            DestroyDonkey();
            RestoreSeat();
            try
            {
                if (MainGame.Instance != null && MainGame.PlayerController != null)
                {
                    RestoreSpeed(MainGame.PlayerController);
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

                if (!cart)
                {
                    if (Plugin.AutoSeat.Value)
                    {
                        seatPending = true; // computed after the first Follow, once the donkey sits at its offsets
                    }
                    if (Plugin.NearLeg.Value)
                    {
                        RiderLeg.Attach(donkeyBodyRenderer, keeperBodyRenderer);
                        if (!RiderLeg.IsAttached)
                        {
                            Plugin.Log.LogWarning("Mount: rider leg not attached (donkey or keeper body sprite missing).");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Mount: preparing the donkey view failed: {e.Message}");
            }
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
                    ApplySeat(pc, Plugin.RideSeatOffset.Value);
                    return;
                }
                ApplySeat(pc, seat.Value);
                Plugin.Log.LogInfo($"Mount: auto seat {seat.Value:F3} (adjust {Plugin.SeatAdjustPx.Value} px); follows the donkey's back each frame from here.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Mount: auto seat failed, using Ride.SeatOffset: {e.Message}");
                ApplySeat(pc, Plugin.RideSeatOffset.Value);
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
            if (input.GetKeyDown(KeyCode.PageUp)) { Plugin.SeatAdjustPx.Value++; changed = true; }
            if (input.GetKeyDown(KeyCode.PageDown)) { Plugin.SeatAdjustPx.Value--; changed = true; }
            if (input.GetKeyDown(KeyCode.End)) { Plugin.LegAdjustXPx.Value++; changed = true; }
            if (input.GetKeyDown(KeyCode.Home)) { Plugin.LegAdjustXPx.Value--; changed = true; }
            if (input.GetKeyDown(KeyCode.Insert)) { Plugin.LegAdjustYPx.Value++; changed = true; }
            if (input.GetKeyDown(KeyCode.Delete)) { Plugin.LegAdjustYPx.Value--; changed = true; }
            if (!changed)
            {
                return;
            }
            RefreshSeat();
            Plugin.Log.LogInfo($"[tune] seat adjust {Plugin.SeatAdjustPx.Value} px, rider offset {Plugin.RiderOffsetPx.Value} px, leg adjust x {Plugin.LegAdjustXPx.Value} y {Plugin.LegAdjustYPx.Value} px, leg height {Plugin.LegHeightPx.Value} px");
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

        public static void RefreshSeat()
        {
            if (!mounted || activeStyle != MountStyle.Ride)
            {
                return;
            }
            PlayerController pc = MainGame.PlayerController;
            if (pc == null)
            {
                return;
            }
            if (Plugin.AutoSeat.Value)
            {
                ApplyAutoSeat(pc);
            }
            else
            {
                ApplySeat(pc, Plugin.RideSeatOffset.Value);
            }
        }

        public static void RefreshLeg()
        {
            if (!mounted || activeStyle != MountStyle.Ride)
            {
                return;
            }
            RiderLeg.Detach();
            RiderLeg.ClearCache();
            if (Plugin.NearLeg.Value && donkeyBodyRenderer != null && keeperBodyRenderer != null)
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

            bool cart = activeStyle == MountStyle.Cart;
            float depth = cart ? Plugin.CartDepth.Value : Plugin.RideDepth.Value;
            float center = cart ? Plugin.CartCenterOffset.Value : Plugin.RideCenterOffset.Value;
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
            float fore = (activeStyle == MountStyle.Ride ? Plugin.RiderOffsetPx.Value : 0) * 0.02f * sign;

            Vector3 target = basePos + away * depth - right * (lateral + fore);
            donkey.Data.Position = VisualConsts.GetRoundedPosXYZ(target, PixelSize());

            // Legs and seat are updated from the keeper's OnLateUpdate (after the skin swap), see OnKeeperLateUpdate.
            AnimationState state = pd.charState.Value == AnimationState.Walk ? AnimationState.Walk : AnimationState.Idle;
            if (state != lastMirrored)
            {
                lastMirrored = state;
                donkey.Data.SetStateToAnimator(state);
                try
                {
                    donkey.MainWgoPart?.AnimationComponent?.SetWalkAnimationSpeedMultiplier(1.5f * pc.PhysicalBody.SpeedMultiplier);
                }
                catch
                {
                }
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
                TorsoShiftWorld = Vector3.right * (TorsoFacingSign() * Plugin.TorsoOffsetPx.Value * 0.02f);
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
            float back = Plugin.Bob.Value ? BackHeightPx(d) : 25f;
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
            return Vector3.Dot(backWorld - hipWorld, up) / up.y + Plugin.SeatAdjustPx.Value * PxWorldY;
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
            if (n.EndsWith("_01") || n.EndsWith("_05")) return 24f;
            if (n.EndsWith("_03") || n.EndsWith("_07")) return 26f;
            return 25f; // static, _02, _04, _06, _08
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
                    Plugin.Log.LogWarning($"Mount: the keeper body has no sprite this frame (pose {Plugin.Pose.Value}, facing {pc.PlayerData.Direction}); the pose has no frame for this facing.");
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
            if (activeStyle != MountStyle.Ride || seatRoot == null || !seatRoot)
            {
                return;
            }
            float? seat;
            if (Plugin.AutoSeat.Value)
            {
                seat = ComputeAutoSeat();
            }
            else
            {
                // Manual height plus the walk-cycle bob (the back is 24-26 px above the ground depending on frame).
                float bob = 0f;
                if (Plugin.Bob.Value && donkeyBodyRenderer != null && donkeyBodyRenderer.sprite != null)
                {
                    bob = (BackHeightPx(donkeyBodyRenderer.sprite) - 25f) * PxWorldY;
                }
                seat = Plugin.RideSeatOffset.Value + bob;
            }
            if (!seat.HasValue)
            {
                return;
            }
            bool torsoShiftStale = (TorsoShiftWorld - Vector3.right * (TorsoFacingSign() * Plugin.TorsoOffsetPx.Value * 0.02f)).sqrMagnitude > 0.000001f;
            if (Mathf.Abs(seat.Value - seatApplied) > 0.0005f || torsoShiftStale)
            {
                ApplySeat(pc, seat.Value);
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

        private static void Say(string text)
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
