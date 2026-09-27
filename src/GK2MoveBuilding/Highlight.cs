using System;
using System.Collections.Generic;
using LazyBearTechnology;
using UnityEngine;

namespace GK2MoveBuilding
{
    // In armed move mode: movable buildings lose the red grid patch and get a colour tint (stronger under the
    // cursor); everything else keeps the game's own remove-mode look. Tints use the game's Wgo.SetSelectionTint.
    internal static class Highlight
    {
        private const float BaseAmount = 0.2f;
        private const float HoverAmount = 0.55f;

        // The mask BuildCellData.UpdateData uses to find what stands on a cell.
        private const int CellMask = 269091072;

        private static readonly HashSet<Wgo> tinted = new HashSet<Wgo>();
        private static readonly Dictionary<Wgo, bool> movable = new Dictionary<Wgo, bool>();
        private static readonly Collider[] hits = new Collider[30];
        private static bool errorLogged;

        public static bool Active =>
            Plugin.Enabled.Value && Plugin.HighlightMovable.Value && Mover.Armed && Mover.State == MoveState.Idle;

        private static bool IsMovable(Wgo wgo)
        {
            if (wgo == null)
            {
                return false;
            }
            if (!movable.TryGetValue(wgo, out bool ok))
            {
                ok = Mover.CanMove(wgo, out _, out _, out _);
                movable[wgo] = ok;
            }
            return ok;
        }

        // Runs after the game's own hover tint pass (RemovePointer.UpdateSelectionRect).
        public static void ApplyTints()
        {
            try
            {
                if (!Active)
                {
                    Clear();
                    return;
                }
                movable.Clear();
                List<Wgo> wgos = LazySingleton<BuildManager>.Instance?.WorldZone?.Wgos;
                if (wgos == null)
                {
                    return;
                }
                Color color = Plugin.MovableColor.Value;
                Wgo hovered = Access.CurrentRemovingSelection() as Wgo;
                foreach (Wgo wgo in wgos)
                {
                    if (wgo == null || !IsMovable(wgo))
                    {
                        continue;
                    }
                    wgo.SetSelectionTint(color, wgo == hovered ? HoverAmount : BaseAmount);
                    tinted.Add(wgo);
                }
            }
            catch (Exception e)
            {
                LogOnce("tint", e);
            }
        }

        public static void Clear()
        {
            foreach (Wgo wgo in tinted)
            {
                if (wgo != null)
                {
                    wgo.SetSelectionTint(Color.white, 0f);
                }
            }
            tinted.Clear();
            movable.Clear();
        }

        // Runs before BuildGrid3D.Draw in remove mode: cells occupied only by movable buildings are drawn as plain
        // cells instead of red ones. The grid is display only in remove mode; the pointer uses physics.
        public static void StripMovableCells(BuildCellData[,] grid)
        {
            try
            {
                if (!Active || grid == null)
                {
                    return;
                }
                movable.Clear();
                int removableBit = (int)BuildCellData.CellState.RemovableWgo;
                int emptyBit = (int)BuildCellData.CellState.Empty;
                int n0 = grid.GetLength(0);
                int n1 = grid.GetLength(1);
                for (int i = 0; i < n0; i++)
                {
                    for (int j = 0; j < n1; j++)
                    {
                        ref BuildCellData cell = ref grid[i, j];
                        int state = Access.CellState(ref cell);
                        if ((state & removableBit) == 0)
                        {
                            continue;
                        }
                        int n = Physics.OverlapBoxNonAlloc(cell.Coords, BuildConsts.CASTING_BOX_HALF_EXTENTS, hits, Quaternion.identity, CellMask);
                        bool any = false;
                        bool all = true;
                        for (int k = 0; k < n; k++)
                        {
                            Collider c = hits[k];
                            if (c == null)
                            {
                                continue;
                            }
                            Wgo wgo = c.GetComponentInParent<Wgo>();
                            if (wgo == null || wgo.Data == null || wgo.Data.isTempObject || wgo.GetComponentInParent<BuildPointerObject>() != null)
                            {
                                continue;
                            }
                            any = true;
                            if (!IsMovable(wgo))
                            {
                                all = false;
                                break;
                            }
                        }
                        if (any && all)
                        {
                            Access.CellState(ref cell) = state & emptyBit;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogOnce("grid", e);
            }
        }

        private static void LogOnce(string what, Exception e)
        {
            if (!errorLogged)
            {
                errorLogged = true;
                Plugin.Log.LogWarning($"Move highlight ({what}) failed; the game's own colours are shown. {e}");
            }
        }
    }
}
