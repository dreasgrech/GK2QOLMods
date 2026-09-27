using System;
using HarmonyLib;

namespace GK2Mounts
{
    // Members the mod relies on, checked once at startup so a game update fails loudly instead of at ride time.
    internal static class Access
    {
        public static void Verify()
        {
            Require(AccessTools.Method(typeof(WgoDataScriptsManager), nameof(WgoDataScriptsManager.CreateScript), new[] { typeof(WgoData), typeof(string) }), "WgoDataScriptsManager.CreateScript");
            Require(AccessTools.Method(typeof(Wgo), nameof(Wgo.Spawn)), "Wgo.Spawn");
            Require(AccessTools.Method(typeof(Wgo), nameof(Wgo.UpdateChunkVisibility)), "Wgo.UpdateChunkVisibility");
            Require(AccessTools.Method(typeof(Wgo), nameof(Wgo.SetInteractableCollidersState)), "Wgo.SetInteractableCollidersState");
            Require(AccessTools.Method(typeof(Wgo), nameof(Wgo.SetLayerToAllColliders)), "Wgo.SetLayerToAllColliders");
            Require(AccessTools.Property(typeof(PlayerPhysicalBody), nameof(PlayerPhysicalBody.SpeedMultiplier)), "PlayerPhysicalBody.SpeedMultiplier");
            Require(AccessTools.Property(typeof(PlayerController), nameof(PlayerController.Ssm)), "PlayerController.Ssm");
            Require(AccessTools.Method(typeof(WgoData), nameof(WgoData.SetStateToAnimator)), "WgoData.SetStateToAnimator");
            Require(AccessTools.Method(typeof(AnimationComponentBase), nameof(AnimationComponentBase.SetWalkAnimationSpeedMultiplier)), "AnimationComponentBase.SetWalkAnimationSpeedMultiplier");
            Require(AccessTools.Method(typeof(AnimationComponentBase), nameof(AnimationComponentBase.SetDirection), new[] { typeof(UnityEngine.Vector2) }), "AnimationComponentBase.SetDirection(Vector2)");
            Require(AccessTools.Method(typeof(AnimationComponentBase), nameof(AnimationComponentBase.SetState), new[] { typeof(AnimationState) }), "AnimationComponentBase.SetState");
            Require(typeof(PlayerAnimation).IsSubclassOf(typeof(AnimationComponentBase)) ? typeof(PlayerAnimation) : null, "PlayerAnimation : AnimationComponentBase");
        }

        private static void Require(object member, string name)
        {
            if (member == null)
            {
                throw new MissingMemberException("Game member not found: " + name);
            }
        }
    }

    // The donkey has only left- and right-facing art (its own clips are static/walk/graze/ear/lips left|right and its
    // rotation preset knows only Left and Right). While mounted the keeper is kept in a side view as well, so rider
    // and mount always face the same way: any facing with a horizontal component becomes pure left or right, a
    // purely vertical one keeps the last horizontal facing. Every path that sets the keeper's sprite facing (walking,
    // click-to-move, workbench facing, aiming) ends in this method; NPCs and the donkey itself are untouched.
    [HarmonyPatch(typeof(AnimationComponentBase), nameof(AnimationComponentBase.SetDirection), typeof(UnityEngine.Vector2))]
    internal static class AnimationComponentBase_SetDirection_Patch
    {
        private static void Prefix(AnimationComponentBase __instance, ref UnityEngine.Vector2 direction)
        {
            if (Mount.IsMounted && Plugin.SideViewOnly.Value && __instance is PlayerAnimation)
            {
                direction = Mount.RiderFacing(direction);
            }
        }
    }

    // While mounted the keeper's body should not play the walk cycle (arms swinging, legs stepping under the
    // donkey). The game drives the keeper's animator with SetState(Walk|Idle) every physics step; this prefix
    // replaces those two with the configured riding pose (any other state, e.g. death, is left alone and dismounts
    // anyway). PlayerData.charState is unaffected, so the donkey still knows when the keeper walks.
    [HarmonyPatch(typeof(AnimationComponentBase), nameof(AnimationComponentBase.SetState))]
    internal static class AnimationComponentBase_SetState_Patch
    {
        private static void Prefix(AnimationComponentBase __instance, ref AnimationState animationState)
        {
            if (!Mount.IsMounted || !(__instance is PlayerAnimation))
            {
                return;
            }
            if (animationState == AnimationState.Walk || animationState == AnimationState.Idle)
            {
                AnimationState pose = Mount.RiderPoseState;
                if (pose != AnimationState.Walk)
                {
                    animationState = pose;
                }
            }
        }
    }

    // WgoData.PrepareForGame (called from the WgoData constructor) starts the definition's attached flow script.
    // The donkey's script teleports it to its delivery point and walks its route, and can touch saved state through
    // flow nodes, so a purely visual mount must never run it. While Mount is constructing its WgoData this flag is
    // set and the script is skipped. Every other WgoData in the game is unaffected.
    [HarmonyPatch(typeof(WgoDataScriptsManager), nameof(WgoDataScriptsManager.CreateScript))]
    internal static class WgoDataScriptsManager_CreateScript_Patch
    {
        private static bool Prefix(WgoData wgoData, string scriptName)
        {
            if (!Mount.SuppressScripts)
            {
                return true;
            }
            Plugin.Log.LogInfo($"Mount: skipped attached script [{scriptName}] for the temporary [{wgoData?.id}].");
            return false;
        }
    }
}
