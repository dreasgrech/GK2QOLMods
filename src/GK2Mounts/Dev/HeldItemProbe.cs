using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace GK2Mounts
{
    // Investigation (Debug tab): why an item in the keeper's hands is not drawn while he rides. The game draws a held
    // item with the drop view (DropViewAtomMesh) of the keeper's animation component, and turns the animator's
    // Overhead layer (3) to weight 1 (PlayerController.SetOverheadItems). This writes the real state of both, once
    // on foot (reference) and once mounted, to BepInEx/LogOutput.log. Nothing is changed.
    internal static class HeldItemProbe
    {
        private static readonly AccessTools.FieldRef<AnimationComponentBase, DropViewAtomMesh> DropViewRef =
            AccessTools.FieldRefAccess<AnimationComponentBase, DropViewAtomMesh>("dropView");

        public static void Write()
        {
            var sb = new StringBuilder();
            try
            {
                PlayerController pc = MainGame.PlayerController;
                PlayerData pd = MainGame.PlayerData;
                if (pc == null || pd == null)
                {
                    Plugin.Log.LogInfo("[held] not in a game.");
                    return;
                }
                var ids = new List<string>();
                foreach (Item it in pd.OverheadItems)
                {
                    ids.Add(it == null ? "null" : $"{it.id}x{it.Count}{(it.IsEmpty ? "(empty)" : "")}");
                }
                sb.Append($"[held] mounted {Mount.IsMounted} ({(Mount.IsMounted ? Mount.CurrentStyle.ToString() : "-")}); held items [{string.Join(", ", ids)}]; keeper at {pc.transform.position}.");

                AnimationComponentBase pa = pc.View?.PlayerAnimation;
                if (pa == null)
                {
                    sb.Append(" No player animation component.");
                    return;
                }
                Animator an = pa.Animator;
                if (an != null)
                {
                    sb.Append($" Animator enabled {an.enabled}, speed {an.speed:F2}, layers {an.layerCount}:");
                    for (int i = 0; i < an.layerCount; i++)
                    {
                        AnimatorClipInfo[] clips = an.GetCurrentAnimatorClipInfo(i);
                        var names = new List<string>();
                        foreach (AnimatorClipInfo c in clips)
                        {
                            names.Add(c.clip != null ? c.clip.name + "@" + c.weight.ToString("F2") : "?");
                        }
                        AnimatorStateInfo st = an.GetCurrentAnimatorStateInfo(i);
                        sb.Append($" [{i} {an.GetLayerName(i)} w{an.GetLayerWeight(i):F2} state {st.shortNameHash} t{st.normalizedTime:F2} clips {string.Join("/", names)}]");
                    }
                }
                DropViewAtomMesh dv = DropViewRef(pa);
                if (dv == null)
                {
                    sb.Append(" Drop view: none.");
                    return;
                }
                Transform t = dv.transform;
                string inactive = "none";
                for (Transform p = t; p != null; p = p.parent)
                {
                    if (!p.gameObject.activeSelf)
                    {
                        inactive = p.name;
                        break;
                    }
                }
                sb.Append($" Drop view '{dv.name}': activeSelf {dv.gameObject.activeSelf}, inHierarchy {dv.gameObject.activeInHierarchy}, first inactive in chain {inactive}, isOverhead {dv.isOverhead}, world {t.position}, local {t.localPosition}, lossyScale {t.lossyScale}, path {Path(t)}.");
                Renderer[] rs = dv.GetComponentsInChildren<Renderer>(includeInactive: true);
                int on = 0;
                foreach (Renderer r in rs)
                {
                    if (r.enabled && r.gameObject.activeInHierarchy)
                    {
                        on++;
                        sb.Append($" R '{r.name}' visible {r.isVisible}, bounds {r.bounds.center} size {r.bounds.size}, order {r.sortingOrder}, layer {LayerMask.LayerToName(r.gameObject.layer)}, mat {(r.sharedMaterial != null ? r.sharedMaterial.name : "-")};");
                    }
                }
                sb.Append($" Renderers {rs.Length}, {on} on.");
            }
            catch (Exception e)
            {
                sb.Append(" Probe failed: " + e.Message);
            }
            finally
            {
                Plugin.Log.LogInfo(sb.ToString());
            }
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (Transform p = t; p != null && parts.Count < 8; p = p.parent)
            {
                parts.Add(p.name);
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
