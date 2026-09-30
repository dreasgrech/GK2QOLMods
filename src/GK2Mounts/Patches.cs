using System;
using HarmonyLib;

using GK2Mounts.Api;

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
            Require(AccessTools.PropertyGetter(typeof(PlayerData), nameof(PlayerData.HasFreeOverheadSlot)), "PlayerData.HasFreeOverheadSlot getter");
            Require(AccessTools.Method(typeof(PlayerData), nameof(PlayerData.TryGetOverheadItem)), "PlayerData.TryGetOverheadItem");
            Require(AccessTools.Method(typeof(PlayerData), nameof(PlayerData.AddOverheadItem)), "PlayerData.AddOverheadItem");
            Require(AccessTools.Method(typeof(PlayerData), nameof(PlayerData.RemoveOverheadItem), new[] { typeof(Item) }), "PlayerData.RemoveOverheadItem(Item)");
            Require(AccessTools.Method(typeof(LazyExpressionEvaluationScope), nameof(LazyExpressionEvaluationScope.GetOverheadItemForMutation)), "LazyExpressionEvaluationScope.GetOverheadItemForMutation");
            Require(AccessTools.Method(typeof(Inventory), nameof(Inventory.Create)), "Inventory.Create");
            Require(AccessTools.Method(typeof(PlayerData), "PlayOverheadItemTakeSound"), "PlayerData.PlayOverheadItemTakeSound");
            Require(AccessTools.Method(typeof(PlayerData), nameof(PlayerData.RemoveInteractingItem)), "PlayerData.RemoveInteractingItem");
            Require(AccessTools.PropertyGetter(typeof(PlayerData), nameof(PlayerData.HasInteractingItem)), "PlayerData.HasInteractingItem getter");
            Require(AccessTools.Method(typeof(GlobalEventsSystem), nameof(GlobalEventsSystem.FireTrigger)), "GlobalEventsSystem.FireTrigger");
            Require(AccessTools.Method(typeof(PlayerData), nameof(PlayerData.DropOverheadItem), new System.Type[0]), "PlayerData.DropOverheadItem()");
            Require(AccessTools.Method(typeof(PlayerInputHandler), nameof(PlayerInputHandler.UpdateInput)), "PlayerInputHandler.UpdateInput");
            Require(AccessTools.Field(typeof(PlayerInputHandler), "playerController"), "PlayerInputHandler.playerController");
            Require(AccessTools.Field(typeof(PlayerInputHandler), "curInteractionHandler"), "PlayerInputHandler.curInteractionHandler");
            Require(AccessTools.Method(typeof(PlayerInputHandler), "TryCallFireEvent"), "PlayerInputHandler.TryCallFireEvent");
            Require(AccessTools.Inner(typeof(PlayerInputHandler), "InteractionType"), "PlayerInputHandler.InteractionType");
            // Sounds (MountSounds.cs)
            Require(AccessTools.PropertyGetter(typeof(IOrderedSurface), nameof(IOrderedSurface.SurfaceType)), "IOrderedSurface.SurfaceType");
            Require(AccessTools.Method(typeof(AnimationComponentBase), nameof(AnimationComponentBase.OnAnimationStepCompleted)), "AnimationComponentBase.OnAnimationStepCompleted");
            Require(AccessTools.Method(typeof(AnimationComponentBase), nameof(AnimationComponentBase.PlaySound), new[] { typeof(string) }), "AnimationComponentBase.PlaySound(string)");
            Require(AccessTools.Method(typeof(LazyBearTechnology.LazyAudio), nameof(LazyBearTechnology.LazyAudio.PlayAtGameObject)), "LazyAudio.PlayAtGameObject");
            Require(AccessTools.Method(typeof(LazyBearTechnology.SoundHandler), nameof(LazyBearTechnology.SoundHandler.SetFadeInFadeOut)), "SoundHandler.SetFadeInFadeOut");
            Require(AccessTools.Method(typeof(SurfaceStepSoundSettings), nameof(SurfaceStepSoundSettings.GetStepSoundIdForSurfaceType)), "SurfaceStepSoundSettings.GetStepSoundIdForSurfaceType");
            // Colours (Colors.cs)
            Require(AccessTools.Method(typeof(Wgo), "OnDestroy"), "Wgo.OnDestroy");
            Require(AccessTools.Method(typeof(Object3DMesh), nameof(Object3DMesh.SetLutTexture)), "Object3DMesh.SetLutTexture");
            Require(AccessTools.Method(typeof(Object3DMesh), nameof(Object3DMesh.GetSubMesh)), "Object3DMesh.GetSubMesh");
            Require(AccessTools.PropertyGetter(typeof(Object3DMesh), nameof(Object3DMesh.SubMeshCount)), "Object3DMesh.SubMeshCount getter");
            Require(AccessTools.PropertyGetter(typeof(Object3DMesh), nameof(Object3DMesh.ObjRenderer)), "Object3DMesh.ObjRenderer getter");
            Require(AccessTools.PropertyGetter(typeof(Object3DSubMesh), nameof(Object3DSubMesh.LutTexture)), "Object3DSubMesh.LutTexture getter");
            Require(AccessTools.PropertyGetter(typeof(Object3DSubMesh), nameof(Object3DSubMesh.MainTextureName)), "Object3DSubMesh.MainTextureName getter");
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

    // The donkey's view is created by the part loader: inside the summon when the part comes from the pool, or frames
    // later. It is hidden in that very call and revealed after PrepareParts (see Mount.OnVisualPartsLoaded). Other
    // objects are untouched.
    [HarmonyPatch(typeof(Wgo), nameof(Wgo.CompleteVisualPartsLoad))]
    internal static class Wgo_CompleteVisualPartsLoad_Patch
    {
        private static void Postfix(Wgo __instance)
        {
            try
            {
                Mount.OnVisualPartsLoaded(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Mount: preparing the donkey on load failed; it is prepared a frame later: " + e.Message);
            }
        }
    }

    // Our donkey's view destroyed by the game rather than by the mod (its scene unloaded behind a door): the colours
    // come off first, while the parts still exist, so they go back to the game's pool with the game's own look.
    // (The mod's own dismount restores them before it destroys the view; this is only the other way out.)
    [HarmonyPatch(typeof(Wgo), "OnDestroy")]
    internal static class Wgo_OnDestroy_Patch
    {
        private static void Prefix(Wgo __instance)
        {
            try
            {
                Colors.OnWgoDestroyed(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Colors: restoring the colours of a destroyed donkey failed: " + e.Message);
            }
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
                    Cargo.SuppressAnnounce = true; // the cargo comes back in the postfix; not an unload for the API
                    Cargo.Unload("save");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: unloading before the save failed: " + e.Message);
            }
        }

        // The save is written inside Save (synchronous, Round 9). Still riding afterwards, the hands are the cart again.
        private static void Postfix()
        {
            try
            {
                if (Mount.IsMounted && Mount.CurrentStyle == MountStyle.Cart && Cargo.Attached)
                {
                    Cargo.LoadFromHead("after the save");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cargo: reloading the cart after the save failed: " + e.Message);
            }
        }

        // Runs after the postfix, and also when the save threw: report whatever did not come back (hands full, a
        // failed reload) as unloaded, and never leave the announcing held.
        private static Exception Finalizer(Exception __exception)
        {
            if (Cargo.SuppressAnnounce)
            {
                Cargo.SuppressAnnounce = false;
                try
                {
                    Cargo.Announce();
                }
                catch
                {
                }
            }
            return __exception;
        }
    }
}
