using UnityEngine;

namespace GK2Mounts
{
    // Read-only views of an instance's state for the self-tests (Dev/SelfTest.cs): what decides what is seen (the
    // animator's facing, which cart parts are on, whose materials the sprites wear) rather than what a screenshot shows.
    internal static partial class Mount
    {
        // Each catch-up (a foal placed straight next to its parent): a test of following counts the jumps.
        internal static int CatchUpCount;

        internal sealed partial class Instance
        {
            internal bool TestSwitchedOff => switchedOff;

            internal float TestFacing => riderFacingX;

            internal bool TestViewActive => donkey != null && donkey && donkey.gameObject.activeInHierarchy;

            // The view's animator Direction parameter (0 = right, -180 = left), or null without one.
            internal float? TestAnimatorDirection()
            {
                if (donkey == null || !donkey)
                {
                    return null;
                }
                foreach (Animator a in donkey.GetComponentsInChildren<Animator>())
                {
                    if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null)
                    {
                        continue;
                    }
                    foreach (AnimatorControllerParameter p in a.parameters)
                    {
                        if (p.name == "Direction" && p.type == AnimatorControllerParameterType.Float)
                        {
                            return a.GetFloat(p.nameHash);
                        }
                    }
                }
                return null;
            }

            // How many of the cart's parts are switched on (a donkey without a cart: none).
            internal int TestCartPartsOn()
            {
                if (donkey == null || !donkey)
                {
                    return 0;
                }
                int n = 0;
                foreach (Transform t in donkey.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (System.Array.IndexOf(CartPartNames, t.name) >= 0 && t.gameObject.activeSelf)
                    {
                        n++;
                    }
                }
                return n;
            }

            // Renderers of the view left out of drawing by the levels (idle) or the experiment: none while it is active.
            internal int TestQuietRenderers => quietRenderers.Count + quietAnimators.Count;

            // The size its view's root was made at (WgoData.Scale on the root).
            internal float TestViewScale => donkey != null && donkey ? donkey.transform.localScale.x : float.NaN;

            // Every coloured sprite wears this instance's own material (null: colours not attached or off).
            internal bool? TestColoursOurs() => ColorsRig.TestSpritesOurs();
        }
    }

    internal static partial class Colors
    {
        internal sealed partial class Rig
        {
            internal bool? TestSpritesOurs()
            {
                if (attachedTo == null || sprites.Count == 0)
                {
                    return null;
                }
                bool any = false;
                foreach (SpriteTarget t in sprites)
                {
                    if (t.Mine == null)
                    {
                        continue;
                    }
                    any = true;
                    if (t.R == null || !t.R || t.R.sharedMaterial != t.Mine)
                    {
                        return false;
                    }
                }
                return any ? true : (bool?)null;
            }
        }
    }
}
