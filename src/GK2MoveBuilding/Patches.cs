using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GK2MoveBuilding
{
    // The desk window builds its tab lists in this constructor and appends the game's own Remove row last.
    // We append a second Remove-mode row after it; picking it arms the move (see BuildManager_EnableBuildMode_Patch).
    [HarmonyPatch(typeof(UIBuildingWindowData), MethodType.Constructor, new Type[]
    {
        typeof(Wgo), typeof(PlayerData), typeof(List<BuildData>),
        typeof(Action<BuildData, List<NeedItemData>>), typeof(Func<BuildData, List<NeedItemData>, bool>), typeof(List<Inventory>)
    })]
    internal static class UIBuildingWindowData_Ctor_Patch
    {
        private static void Postfix(UIBuildingWindowData __instance)
        {
            if (!Plugin.Enabled.Value || !Plugin.ShowMoveRow.Value)
            {
                return;
            }
            try
            {
                Dictionary<string, List<BuildData>> tabs = __instance.TabSortedBuilds;
                if (tabs == null)
                {
                    return;
                }
                BuildData entry = BuildData.GetDataForRemove();
                Mover.MoveEntry = entry;
                foreach (List<BuildData> list in tabs.Values)
                {
                    if (list != null && list.Exists(b => b != null && b.BuildingMode == BuildingDef.BuildingMode.Remove))
                    {
                        list.Add(entry);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Move row: {e.Message}");
            }
        }
    }

    // Row data for our entry gets the Move label instead of the game's "remove" text.
    [HarmonyPatch(typeof(UIBuildingWidgetData), MethodType.Constructor, new Type[]
    {
        typeof(BuildData), typeof(MultiInventory), typeof(Action<BuildData, List<NeedItemData>>),
        typeof(Func<BuildData, List<NeedItemData>, bool>), typeof(Action), typeof(Action), typeof(WorldZoneData)
    })]
    internal static class UIBuildingWidgetData_Ctor_Patch
    {
        private static void Postfix(UIBuildingWidgetData __instance, BuildData buildData)
        {
            if (Mover.MoveEntry == null || !ReferenceEquals(buildData, Mover.MoveEntry))
            {
                return;
            }
            try
            {
                Access.SetWidgetName(__instance, Plugin.MoveLabel.Value);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Move row label: {e.Message}");
            }
        }
    }

    // Every build-mode start goes through here (the window's row press and our own preview).
    // Armed only while the player's pick was our row; the game then runs its normal Remove mode.
    [HarmonyPatch(typeof(BuildManager), nameof(BuildManager.EnableBuildMode))]
    internal static class BuildManager_EnableBuildMode_Patch
    {
        private static void Prefix(BuildData selectedBuildData)
        {
            Mover.Armed = Plugin.Enabled.Value && Mover.MoveEntry != null && ReferenceEquals(selectedBuildData, Mover.MoveEntry);
        }
    }

    // The remove pointer's click. When moving, the click picks the building up instead; the original never runs,
    // so nothing is demolished and no demolition expressions are evaluated.
    [HarmonyPatch(typeof(RemovePointer), nameof(RemovePointer.TryDoBuildAction))]
    internal static class RemovePointer_TryDoBuildAction_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Plugin.Enabled.Value)
            {
                return true;
            }
            if (Mover.State != MoveState.Idle)
            {
                __result = false; // a move is already underway
                return false;
            }
            if (!Mover.Armed && !Mover.ModifierHeld())
            {
                return true;
            }
            __result = false;
            try
            {
                Wgo wgo = Access.CurrentRemovingSelection() as Wgo;
                if (wgo != null && !Mover.TryPickUp(wgo, out string reason))
                {
                    Plugin.Log.LogInfo($"Move: {wgo.Data?.id} refused: {reason}");
                    Mover.Say(reason);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Move: pick-up failed; nothing changed. {e}");
                Mover.Cancel("pick-up failed");
            }
            return false;
        }
    }

    // The build key while our preview is up. The original would pay, spawn a new object and run its build
    // expressions and the BuildBuilding trigger; we relocate the existing object instead.
    [HarmonyPatch(typeof(BuildPointer), nameof(BuildPointer.TryBuildActionInput))]
    internal static class BuildPointer_TryBuildActionInput_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            switch (Mover.State)
            {
                case MoveState.Placing:
                    __result = false; // the controller does nothing more; Mover.Tick closes the preview
                    try
                    {
                        Mover.TryCommit();
                    }
                    catch (Exception e)
                    {
                        // Relocate undoes its own data changes; this catches anything around it.
                        Plugin.Log.LogError($"Move: placing failed; the move is called off. {e}");
                        Mover.Cancel("placing failed");
                    }
                    return false;
                case MoveState.Done:
                    __result = false; // no second build with the now free preview
                    return false;
                default:
                    return true;
            }
        }
    }

    // Placement validity per cell. While moving, the picked building's own colliders don't count as blocking,
    // so it can be put down overlapping its old spot.
    [HarmonyPatch(typeof(BuildSelectionCell), nameof(BuildSelectionCell.OverlapBoxNonAlloc))]
    internal static class BuildSelectionCell_OverlapBoxNonAlloc_Patch
    {
        private static void Postfix(Collider[] overlapColliders, ref int __result)
        {
            if (Mover.State != MoveState.Placing || overlapColliders == null || __result <= 0)
            {
                return;
            }
            Wgo source = Mover.Source;
            if (source == null)
            {
                return;
            }
            Transform root = source.transform;
            int n = Math.Min(__result, overlapColliders.Length);
            int kept = 0;
            for (int i = 0; i < n; i++)
            {
                Collider c = overlapColliders[i];
                if (c != null && c.transform.IsChildOf(root))
                {
                    continue;
                }
                overlapColliders[kept++] = c;
            }
            for (int i = kept; i < n; i++)
            {
                overlapColliders[i] = null;
            }
            __result = kept;
        }
    }

    // The remove pointer's hover/selection tint pass; our movable tints go on top of it.
    [HarmonyPatch(typeof(RemovePointer), "UpdateSelectionRect")]
    internal static class RemovePointer_UpdateSelectionRect_Patch
    {
        private static void Postfix()
        {
            Highlight.ApplyTints();
        }
    }

    // The remove pointer goes away (Back, or our switch to the preview): drop our tints.
    [HarmonyPatch(typeof(RemovePointer), nameof(RemovePointer.OnPointerDisable))]
    internal static class RemovePointer_OnPointerDisable_Patch
    {
        private static void Postfix()
        {
            Highlight.Clear();
        }
    }

    // The zone grid is (re)drawn on entering build mode and after each action; in armed move mode the cells
    // under movable buildings are shown as plain cells.
    [HarmonyPatch(typeof(BuildGrid3D), nameof(BuildGrid3D.Draw))]
    internal static class BuildGrid3D_Draw_Patch
    {
        private static void Prefix(BuildCellData[,] gridData, BuildCellData.BuildMode buildMode)
        {
            if (buildMode == BuildCellData.BuildMode.Remove)
            {
                Highlight.StripMovableCells(gridData);
            }
        }
    }

    // The preview pointer is set up here. While moving, its cost check always passes (the building is already paid
    // for; limited buildings would otherwise count themselves). A pointer subclass means a special placement flow.
    [HarmonyPatch(typeof(WgoBuildPointer), nameof(WgoBuildPointer.SetTarget))]
    internal static class WgoBuildPointer_SetTarget_Patch
    {
        private static void Postfix(WgoBuildPointer __instance)
        {
            if (Mover.State != MoveState.Placing)
            {
                return;
            }
            if (__instance.GetType() != typeof(WgoBuildPointer))
            {
                Mover.RefusePointer(__instance);
                return;
            }
            Access.CanTakeResources(__instance) = () => true;
        }
    }
}
