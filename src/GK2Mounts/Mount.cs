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
        private static Transform seatRoot;
        private static float seatApplied;

        public static bool IsMounted => mounted;

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
            pc.PhysicalBody.SpeedMultiplier = Plugin.SpeedMultiplier.Value;
            ApplySeat(pc, activeStyle == MountStyle.Cart ? Plugin.CartSeatOffset.Value : Plugin.RideSeatOffset.Value);
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
                if (t == TakenControlType.ByUI || t == TakenControlType.ByTeleport)
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
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Mount: preparing the donkey view failed: {e.Message}");
            }
        }

        private static void Follow(PlayerController pc)
        {
            PlayerData pd = pc.PlayerData;
            Vector2 dir = pd.Direction;
            if (dir.sqrMagnitude > 0.0001f)
            {
                donkey.Data.MovableDirection = dir; // applied to the donkey's animator synchronously
            }

            // The donkey has only left/right sprites. Its stepped-rotation preset shows the right-facing one only
            // when the movement has a rightward component; straight up and straight down both resolve to left.
            // Read the facing back from its animator so the centering always cancels the sprite's real offset.
            facingSign = dir.x > 0.0001f ? 1f : -1f;
            try
            {
                AnimationComponentBase anim = donkey.MainWgoPart?.AnimationComponent;
                if (anim != null && anim.Animator != null && anim.Animator.isActiveAndEnabled)
                {
                    Vector2 shown = anim.GetDirection();
                    if (Mathf.Abs(shown.x) > 0.01f)
                    {
                        facingSign = shown.x > 0f ? 1f : -1f;
                    }
                }
            }
            catch
            {
            }

            bool cart = activeStyle == MountStyle.Cart;
            float depth = cart ? Plugin.CartDepth.Value : Plugin.RideDepth.Value;
            float center = cart ? Plugin.CartCenterOffset.Value : Plugin.RideCenterOffset.Value;

            // PlayerData.position is the 50 Hz physics value; the body transform is the Rigidbody-interpolated,
            // per-frame position the keeper is actually drawn at. Snap to the same pixel grid PlayerView uses so
            // the two sprites never disagree by a sub-pixel amount.
            Rigidbody rb = pc.PhysicalBody.Rb;
            Vector3 basePos = rb != null ? rb.transform.position : pd.position.Value;
            Vector3 away = AwayFromCamera();
            Vector3 right = Vector3.Cross(Vector3.up, away).normalized;
            Vector3 target = basePos + away * depth - right * (center * facingSign);
            donkey.Data.Position = VisualConsts.GetRoundedPosXYZ(target, PixelSize());
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
        }

        // ------------------------------------------------------------------ rider sprite

        // The keeper's sprites hang under one transform (the one with the Animator, "main_character", at a fixed
        // local height). Shifting it is enough; nothing in the game writes that local position at runtime.
        private static void ApplySeat(PlayerController pc, float offset)
        {
            RestoreSeat();
            if (Mathf.Abs(offset) < 0.0001f)
            {
                return;
            }
            try
            {
                Animator animator = pc.View?.PlayerAnimation?.Animator;
                if (animator == null)
                {
                    return;
                }
                seatRoot = animator.transform;
                seatRoot.localPosition += Vector3.up * offset;
                seatApplied = offset;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Mount: seat offset failed: {e.Message}");
                seatRoot = null;
                seatApplied = 0f;
            }
        }

        private static void RestoreSeat()
        {
            try
            {
                if (seatRoot != null && seatRoot && Mathf.Abs(seatApplied) > 0.0001f)
                {
                    seatRoot.localPosition -= Vector3.up * seatApplied;
                }
            }
            catch
            {
            }
            seatRoot = null;
            seatApplied = 0f;
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
