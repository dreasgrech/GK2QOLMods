using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace GK2MountProbe
{
    // Hotkey experiments that answer what static analysis cannot:
    //  - SpeedTest: how PlayerPhysicalBody.SpeedMultiplier translates into measured speed and how it feels.
    //  - FollowTest: a temp WGO glued to the player (position, facing, walk/idle), with adjustable depth offset
    //    and main-part variation, to see how the mount would look and whether the sprites sort correctly.
    //  - Seat: a rider height offset applied in a PlayerView.LateUpdate postfix.
    // Also logs player state-machine and animation-state transitions.
    internal static class Experiments
    {
        private static readonly float[] DepthSteps = { -0.3f, -0.15f, 0f, 0.15f, 0.3f };

        private static bool speedActive;
        private static bool measureEnabled;
        private static float maxVelocity;
        private static Vector2 axisAccum;
        private static bool lastControls = true;
        private static bool lastKinematic;
        private static Vector3 lastMeasurePos;
        private static float lastMeasureTime;
        private static float distanceAccum;
        private static float movingTimeAccum;

        private static Wgo follower;
        private static bool followerCollidersDone;
        private static int depthIndex = 3;
        private static bool cartVisible;
        private static bool centering = true;
        private static float facingSign = 1f; // +1 facing right, -1 facing left; kept while facing up/down
        private static AnimationState lastMirroredState = (AnimationState)(-1);
        internal static float SeatOffset;

        private static string lastSsmState;
        private static AnimationState lastCharState = (AnimationState)(-1);

        public static void Tick(PlayerController pc, GameScene scene)
        {
            LogTransitions(pc);
            HandleKeys(pc, scene);
            TickSpeed(pc);
            TickFollower(pc);
        }

        public static void Reset(string reason)
        {
            try
            {
                if (speedActive && MainGame.PlayerController != null)
                {
                    MainGame.PlayerController.PhysicalBody.SpeedMultiplier = 1f;
                }
            }
            catch
            {
            }
            speedActive = false;
            TempSpawner.Destroy(ref follower);
            followerCollidersDone = false;
            SeatOffset = 0f;
            lastMirroredState = (AnimationState)(-1);
            Plugin.Log.LogInfo($"Experiments reset ({reason}).");
        }

        // ---------------------------------------------------------------- keys

        private static void HandleKeys(PlayerController pc, GameScene scene)
        {
            var input = UnityInput.Current;
            if (input.GetKeyDown(Plugin.SpeedKey.Value))
            {
                speedActive = !speedActive;
                measureEnabled = true;
                pc.PhysicalBody.SpeedMultiplier = speedActive ? Plugin.TestSpeedMultiplier.Value : 1f;
                distanceAccum = 0f;
                movingTimeAccum = 0f;
                lastMeasurePos = pc.PlayerData.position.Value;
                lastMeasureTime = Time.time;
                Plugin.Log.LogInfo($"[speed] {(speedActive ? "ON" : "OFF")} SpeedMultiplier={pc.PhysicalBody.SpeedMultiplier} (physicsConfig.speed={pc.PhysicalBody.PhysicsConfig.speed}). Walk in a straight line; measured speed is logged every second.");
            }
            if (input.GetKeyDown(Plugin.FollowKey.Value))
            {
                if (follower != null)
                {
                    TempSpawner.Destroy(ref follower);
                    followerCollidersDone = false;
                    SeatOffset = 0f;
                    Plugin.Log.LogInfo("[follow] OFF");
                }
                else
                {
                    try
                    {
                        string id = Plugin.FollowWgoId.Value;
                        if (GameBalance.Me.GetData<WGODef>(id) == null)
                        {
                            Plugin.Log.LogWarning($"[follow] no WGODef '{id}'");
                        }
                        else
                        {
                            follower = TempSpawner.Spawn(id, pc.PlayerData.position.Value, scene, disableColliders: false);
                            followerCollidersDone = false;
                            depthIndex = NearestDepthIndex(Plugin.FollowDepthOffset.Value);
                            cartVisible = Plugin.FollowWithCart.Value;
                            lastMirroredState = (AnimationState)(-1);
                            SeatOffset = Plugin.SeatHeight.Value;
                            Plugin.Log.LogInfo($"[follow] ON id={id} depth={DepthSteps[depthIndex]} seat={SeatOffset}");
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError($"[follow] spawn failed: {e}");
                    }
                }
            }
            if (input.GetKeyDown(Plugin.DepthKey.Value))
            {
                depthIndex = (depthIndex + 1) % DepthSteps.Length;
                Plugin.Log.LogInfo($"[follow] depth offset = {DepthSteps[depthIndex]} (positive = away from camera, behind the player)");
            }
            if (input.GetKeyDown(Plugin.CenterKey.Value))
            {
                centering = !centering;
                Plugin.Log.LogInfo($"[follow] centering {(centering ? "ON" : "OFF")} (offset {Plugin.FollowCenterOffset.Value} against facing)");
            }
            if (input.GetKeyDown(Plugin.CartKey.Value))
            {
                cartVisible = !cartVisible;
                int n = TempSpawner.SetCartVisible(follower, cartVisible);
                Plugin.Log.LogInfo($"[follow] cart {(cartVisible ? "shown" : "hidden")} ({n} parts)");
            }
            if (input.GetKeyDown(Plugin.SeatUpKey.Value))
            {
                SeatOffset += 0.05f;
                Plugin.Log.LogInfo($"[seat] rider offset = {SeatOffset:F2}");
            }
            if (input.GetKeyDown(Plugin.SeatDownKey.Value))
            {
                SeatOffset -= 0.05f;
                Plugin.Log.LogInfo($"[seat] rider offset = {SeatOffset:F2}");
            }
        }

        private static int NearestDepthIndex(float v)
        {
            int best = 0;
            for (int i = 1; i < DepthSteps.Length; i++)
            {
                if (Mathf.Abs(DepthSteps[i] - v) < Mathf.Abs(DepthSteps[best] - v))
                {
                    best = i;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- speed

        // Measures at both settings once the speed key has been pressed at least once, so 1.0x has a baseline.
        private static void TickSpeed(PlayerController pc)
        {
            if (!measureEnabled)
            {
                return;
            }
            // Combat states overwrite the multiplier on enter/exit; re-apply while in the free state.
            if (speedActive && pc.Ssm?.CurState is FreePlayerState && Mathf.Abs(pc.PhysicalBody.SpeedMultiplier - Plugin.TestSpeedMultiplier.Value) > 0.001f)
            {
                pc.PhysicalBody.SpeedMultiplier = Plugin.TestSpeedMultiplier.Value;
            }
            Vector3 pos = pc.PlayerData.position.Value;
            bool moving = pc.PlayerData.charState.Value == AnimationState.Walk;
            if (moving)
            {
                Vector3 d = pos - lastMeasurePos;
                d.y = 0f;
                distanceAccum += d.magnitude;
                axisAccum += new Vector2(Mathf.Abs(d.x), Mathf.Abs(d.z));
                movingTimeAccum += Time.deltaTime;
                Rigidbody rb = pc.PhysicalBody.Rb;
                if (rb != null)
                {
                    Vector3 v = rb.linearVelocity;
                    v.y = 0f;
                    maxVelocity = Mathf.Max(maxVelocity, v.magnitude);
                }
            }
            lastMeasurePos = pos;
            if (Time.time - lastMeasureTime >= 1f)
            {
                if (movingTimeAccum > 0.2f)
                {
                    float axisSum = axisAccum.x + axisAccum.y;
                    string axis = axisSum > 0.001f ? $"x {axisAccum.x / axisSum:P0} / z {axisAccum.y / axisSum:P0}" : "-";
                    Plugin.Log.LogInfo($"[speed] mult={pc.PhysicalBody.SpeedMultiplier:F2} measured={distanceAccum / movingTimeAccum:F2} u/s over {movingTimeAccum:F2}s  maxRbVelocity={maxVelocity:F2}  axes={axis}  state={pc.Ssm?.CurState?.GetType().Name}");
                }
                distanceAccum = 0f;
                movingTimeAccum = 0f;
                maxVelocity = 0f;
                axisAccum = Vector2.zero;
                lastMeasureTime = Time.time;
            }
        }

        // ---------------------------------------------------------------- follower

        private static void TickFollower(PlayerController pc)
        {
            if (follower == null)
            {
                return;
            }
            // Unity's null: the scene was unloaded or the view destroyed by the game.
            if (!follower)
            {
                follower = null;
                followerCollidersDone = false;
                SeatOffset = 0f;
                Plugin.Log.LogInfo("[follow] follower view is gone (scene change?), experiment stopped.");
                return;
            }
            if (!followerCollidersDone && follower.MainWgoPart != null)
            {
                TempSpawner.DisableColliders(follower);
                int cartParts = TempSpawner.SetCartVisible(follower, cartVisible);
                followerCollidersDone = true;
                Plugin.Log.LogInfo($"[follow] parts loaded: mainPart={follower.MainWgoPart.name} animComp={(follower.MainWgoPart.AnimationComponent ? follower.MainWgoPart.AnimationComponent.GetType().Name : "null")}. Colliders disabled, cart {(cartVisible ? "shown" : "hidden")} ({cartParts} parts).");
            }
            PlayerData pd = pc.PlayerData;
            Vector2 dir = pd.Direction;
            if (Mathf.Abs(dir.x) > 0.3f)
            {
                facingSign = dir.x > 0f ? 1f : -1f;
            }
            Vector3 away = TempSpawner.AwayFromCamera();
            Vector3 right = Vector3.Cross(Vector3.up, away).normalized;
            Vector3 target = pd.position.Value + away * DepthSteps[depthIndex];
            if (centering)
            {
                // The body sprite is drawn 0.48 to the facing side of the data position; pull the data position back.
                target -= right * (Plugin.FollowCenterOffset.Value * facingSign);
            }
            follower.Data.Position = target;
            if (dir.sqrMagnitude > 0.0001f)
            {
                follower.Data.MovableDirection = dir;
            }
            AnimationState state = pd.charState.Value == AnimationState.Walk ? AnimationState.Walk : AnimationState.Idle;
            if (state != lastMirroredState)
            {
                lastMirroredState = state;
                follower.Data.SetStateToAnimator(state);
                try
                {
                    follower.MainWgoPart?.AnimationComponent?.SetWalkAnimationSpeedMultiplier(1.5f * pc.PhysicalBody.SpeedMultiplier);
                }
                catch
                {
                }
            }
        }

        // ---------------------------------------------------------------- transitions

        private static void LogTransitions(PlayerController pc)
        {
            if (!Plugin.LogStateChanges.Value)
            {
                return;
            }
            string s = pc.Ssm?.CurState?.GetType().Name ?? "null";
            if (s != lastSsmState)
            {
                Plugin.Log.LogInfo($"[state] SSM {lastSsmState ?? "-"} -> {s}  controls={pc.IsControlsEnabled} kinematic={pc.PhysicalBody.Rb?.isKinematic}");
                lastSsmState = s;
            }
            AnimationState cs = pc.PlayerData.charState.Value;
            if (cs != lastCharState)
            {
                Plugin.Log.LogInfo($"[state] charState {lastCharState} -> {cs}");
                lastCharState = cs;
            }
            // Opening UI, working, sleeping etc. don't change the SSM state type; they flip control flags instead.
            bool controls = pc.IsControlsEnabled;
            bool kinematic = pc.PhysicalBody.Rb != null && pc.PhysicalBody.Rb.isKinematic;
            if (controls != lastControls || kinematic != lastKinematic)
            {
                List<string> off = new List<string>();
                foreach (TakenControlType t in Enum.GetValues(typeof(TakenControlType)))
                {
                    if (!pc.IsControlEnabledByType(t))
                    {
                        off.Add(t.ToString());
                    }
                }
                Plugin.Log.LogInfo($"[state] controls={controls} kinematic={kinematic} takenBy=[{string.Join(", ", off)}] ssm={s} follower={(follower != null ? "active" : "none")}");
                lastControls = controls;
                lastKinematic = kinematic;
            }
        }
    }

    // Rider height: nudges the player's view up after the game has snapped it to the body position.
    [HarmonyPatch(typeof(PlayerView), "LateUpdate")]
    internal static class PlayerView_LateUpdate_Patch
    {
        private static void Postfix(PlayerView __instance)
        {
            if (Experiments.SeatOffset != 0f)
            {
                __instance.transform.position += Vector3.up * Experiments.SeatOffset;
            }
        }
    }
}
