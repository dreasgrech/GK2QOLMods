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
        }

        private static void Require(object member, string name)
        {
            if (member == null)
            {
                throw new MissingMemberException("Game member not found: " + name);
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
