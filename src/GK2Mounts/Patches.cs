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
            Require(AccessTools.Method(typeof(GK2.FlowCanvasNodes.Flow_Body), "DoAction", new[] { typeof(FlowCanvas.Flow) }), "Flow_Body.DoAction(Flow)");
            Require(AccessTools.Field(typeof(GK2.FlowCanvasNodes.Flow_Body), "success"), "Flow_Body.success");
            Require(AccessTools.Method(typeof(SaveSystem), nameof(SaveSystem.Save)), "SaveSystem.Save");
            Require(AccessTools.Method(typeof(PlayerData), nameof(PlayerData.TryAddOverheadItemNoReplace)), "PlayerData.TryAddOverheadItemNoReplace");
            Require(AccessTools.Method(typeof(PlayerData), nameof(PlayerData.InsertOverheadItemTo), new[] { typeof(WgoData), typeof(Item) }), "PlayerData.InsertOverheadItemTo(WgoData, Item)");
            Require(AccessTools.Method(typeof(Inventory), nameof(Inventory.RemoveItemFromInventoryByUID)), "Inventory.RemoveItemFromInventoryByUID");
            Require(AccessTools.Method(typeof(SpecialPhysicsCastUtils), nameof(SpecialPhysicsCastUtils.GetPlayerDropPosition)), "SpecialPhysicsCastUtils.GetPlayerDropPosition");
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
            if (Mount.IsMounted && Mount.SideView && __instance is PlayerAnimation)
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

    // Debug aid for the panel's "Start a real delivery" button. The donkey script's "Body Roll" node only lets the
    // cart drive when the morgue has room and a random drop-chance roll passes; for the two frames after the button
    // is pressed it is made to take its Success exit without touching the drop-chance resource, so the game's own
    // cart can be watched on the same ground as ours. Every other Body Roll (the game's scheduled deliveries) runs
    // untouched.
    [HarmonyPatch(typeof(GK2.FlowCanvasNodes.Flow_Body), "DoAction")]
    internal static class Flow_Body_DoAction_Patch
    {
        private static readonly AccessTools.FieldRef<GK2.FlowCanvasNodes.Flow_Body, FlowCanvas.FlowOutput> Success =
            AccessTools.FieldRefAccess<GK2.FlowCanvasNodes.Flow_Body, FlowCanvas.FlowOutput>("success");

        private static bool Prefix(GK2.FlowCanvasNodes.Flow_Body __instance, FlowCanvas.Flow flow)
        {
            if (UnityEngine.Time.frameCount > ScriptDump.ForceBodyRollUntilFrame)
            {
                return true;
            }
            ScriptDump.ForceBodyRollUntilFrame = -1;
            Plugin.Log.LogInfo("[script] Body Roll forced to succeed for the manually started delivery.");
            Success(__instance).Call(flow);
            return false;
        }
    }

    // Cargo on the cart lives in the temporary donkey's inventory, which no save contains. Right before the game
    // serializes a save (every save path ends in this static method, after which PrepareToSave runs) the cargo is
    // handed back to the keeper (hands, then dropped at his feet), so the drops are part of the save being written.
    [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.Save))]
    internal static class SaveSystem_Save_Patch
    {
        private static void Prefix()
        {
            try
            {
                if (Mount.IsMounted && Cargo.Count > 0)
                {
                    Cargo.Unload("save");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: unloading before the save failed: " + e.Message);
            }
        }
    }
}
