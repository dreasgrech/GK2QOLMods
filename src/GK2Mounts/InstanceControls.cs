using System;
using System.Collections.Generic;
using BepInEx;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Mounts
{
    // The keeper's controls for instances in the world (docs/mounts-instances.md), on with General.InstanceControls:
    // - on foot with empty hands, the interact key next to a standing donkey rides it;
    // - riding a donkey without its cart, the interact key next to a free cart, or General.HitchKey, hitches it;
    // - riding a donkey with its cart, General.HitchKey leaves the cart standing.
    // The interact key only counts when the game has nothing of its own to do with it (no item or object in front of
    // the keeper): PlayerInputHandler_UpdateInput_Patch asks TryInteract first. The game's own interaction hint over the
    // donkey or cart in reach says what the key does (Hint, below).
    internal static class InstanceControls
    {
        private const float RideReach = 1.3f;   // keeper to the donkey's body
        private const float HitchReach = 1.3f;  // the ridden donkey's body to where the free cart's donkey would stand

        private static Mount.Instance target;
        private static bool warnedScene;
        private static string label;

        // Nothing in reach any more (the game left, every instance gone).
        internal static void Clear()
        {
            target = null;
            label = null;
            UpdateHint();
        }

        internal static void Tick(PlayerController pc)
        {
            try
            {
                Find(pc);
            }
            finally
            {
                UpdateHint();
            }
        }

        private static void Find(PlayerController pc)
        {
            target = null;
            label = null;
            if (!Plugin.InstanceControls.Value || !pc.IsControlsEnabled)
            {
                return;
            }
            try
            {
                // The game's own interaction target wins the key (TryInteract steps aside for it), so our label hides
                // while it has one: two labels on one key promised what the key would not do (an NPC's Speak next to a
                // donkey's Drive, 2026-09-30).
                bool gameTarget = GameHasTarget(pc);
                if (!Mount.IsMounted)
                {
                    if (!gameTarget && !pc.PlayerData.HasOverheadItem)
                    {
                        target = Nearest(pc, pc.PlayerData.position.Value, RideReach, i => i.HasDonkey && i.Rideable);
                        if (target != null)
                        {
                            label = HintText(target.HasCart ? "Drive" : "Ride");
                        }
                    }
                    return;
                }
                Mount.Instance r = Mount.Ridden;
                if (!gameTarget && r.HasDonkey && !r.HasCart && r.Ready)
                {
                    target = Nearest(pc, r.BodyPoint(), HitchReach, i => !i.HasDonkey && i.Parent == null);
                    if (target != null)
                    {
                        label = HintText("Hitch the cart");
                    }
                }
                KeyCode k = Plugin.HitchKey.Value;
                if (k != KeyCode.None && UnityInput.Current.GetKeyDown(k) && pc.IsControlsEnabled)
                {
                    HitchKey(pc, r);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Mount: instance controls failed: " + e.Message);
            }
        }

        // Something of the game's own is under interaction (a character, a work station, a pile): its prompt is shown and
        // the interact key is its.
        private static bool GameHasTarget(PlayerController pc)
        {
            PlayerInteractionComponent ic = pc.PlayerInteractionComponent;
            return ic != null && (ic.WgoUnderInteraction != null
                || ic.BigDropUnderInteraction != null && ic.BigDropUnderInteraction.InteractionHandler.HasInteraction());
        }

        private static void HitchKey(PlayerController pc, Mount.Instance r)
        {
            if (r.HasCart)
            {
                Mount.Instance cart;
                string why;
                using (Mount.Bind(r))
                {
                    cart = r.Unhitch(pc, out why);
                }
                if (cart == null)
                {
                    Mount.Say(why);
                }
                return;
            }
            if (target != null && !target.HasDonkey)
            {
                Hitch(pc, r, target);
            }
            else
            {
                Mount.Say("No cart here.");
            }
        }

        private static void Hitch(PlayerController pc, Mount.Instance r, Mount.Instance cart)
        {
            string why;
            using (Mount.Bind(r))
            {
                why = r.Hitch(cart, pc);
            }
            if (why != null)
            {
                Mount.Say(why);
            }
        }

        // The interact key, when the game has nothing of its own to do with it. True: used (the game's branch skipped).
        internal static bool TryInteract(PlayerController pc)
        {
            if (!Plugin.InstanceControls.Value || target == null || !Mount.IsRegistered(target))
            {
                return false;
            }
            if (GameHasTarget(pc))
            {
                return false;
            }
            Mount.Instance t = target;
            if (!Mount.IsMounted)
            {
                using (Mount.Bind(t))
                {
                    t.RideFromWorld(pc, showMessage: true);
                }
                return true;
            }
            if (!t.HasDonkey)
            {
                Hitch(pc, Mount.Ridden, t);
                return true;
            }
            return false;
        }

        // The standing instance nearest to 'from' within reach, in the keeper's scene, ready, matching 'which'.
        private static Mount.Instance Nearest(PlayerController pc, Vector3 from, float reach, Func<Mount.Instance, bool> which)
        {
            string scene = pc.PlayerData.currentGameSceneId;
            Mount.Instance best = null;
            float bestD = reach * reach;
            // Every instance, not only the keeper's scene's: the check below says once when one is in reach but filed
            // under another scene id. Cheap (a distance per instance), but at most once per frame.
            foreach (Mount.Instance i in Mount.All)
            {
                if (i.Ephemeral || i.IsRidden || !i.Ready || !which(i))
                {
                    continue;
                }
                Vector3 d = i.BodyPoint() - from;
                d.y = 0f;
                if (i.SceneId != scene)
                {
                    // Close by but "in another scene": if the two ids ever disagree for the same place, nothing would be
                    // in reach; said once so a log shows it.
                    if (d.sqrMagnitude <= reach * reach && !warnedScene)
                    {
                        warnedScene = true;
                        Plugin.Log.LogInfo($"Mount: {i.Describe()} is in reach but in scene '{i.SceneId}', the keeper in '{scene}'.");
                    }
                    continue;
                }
                if (d.sqrMagnitude <= bestD)
                {
                    bestD = d.sqrMagnitude;
                    best = i;
                }
            }
            return best;
        }

        // As the game's handlers build it (WGOInteractionHandlerBase.LocalizeHintWithActionIcon): the key's icon (the
        // keyboard key or the gamepad button in use, with a space) and the text.
        private static string HintText(string text)
        {
            try
            {
                return ControllerIconLibrary.GetIconId(GameKey.Interaction) + text;
            }
            catch
            {
                return "[E] " + text;
            }
        }

        // ------------------------------------------------------------------ the hint

        // The game's own interaction hint ("[E] Craft"), drawn by its bubble manager: the same widget, font, outline and
        // gamepad icon as every hint in the game. Wgo.GetWidgetData builds it as UIInteractionHintWidgetData from
        // InteractionInfo rows whose text is ControllerIconLibrary.GetIconId(key) + the hint; any IBubbleDrawable can
        // be shown (UIObjectBubbleManager only treats Unity objects specially), so the mod shows its own.
        private sealed class Hint : IBubbleDrawable
        {
            private readonly SGuid id = new SGuid(Guid.NewGuid());
            internal Mount.Instance Over;
            internal string Text;

            public SGuid BubbleDrawableUniqueId => id;

            public List<LazyWidgetDataBase> BubbleDrawableWidgets => new List<LazyWidgetDataBase>
            {
                new UIInteractionHintWidgetData(new UIInteractionHintRowWidgetData(new InteractionInfo(Text))),
            };

            public Vector3 BubbleDrawablePosition => Over != null ? Over.HintPoint() : Vector3.zero;
        }

        private static Hint hint;
        private static bool shown;

        // Shown, moved to another instance, re-texted or hidden to match what is in reach this frame.
        private static void UpdateHint()
        {
            try
            {
                UIObjectBubbleManager m = UIObjectBubbleManager.Instance;
                if (m == null)
                {
                    return;
                }
                bool want = target != null && label != null && Mount.IsRegistered(target);
                if (!want)
                {
                    if (shown && hint != null)
                    {
                        m.Hide(hint);
                    }
                    shown = false;
                    return;
                }
                if (hint != null && hint.Over != target)
                {
                    // Another donkey or cart: the old bubble fades out where it was, a new one (its own id) comes up
                    // here, as the game's hints do when the keeper turns to another object.
                    if (shown)
                    {
                        m.Hide(hint);
                    }
                    hint = null;
                    shown = false;
                }
                if (hint == null)
                {
                    hint = new Hint { Over = target };
                }
                // Asked again until the manager shows it (a request waits for its next LateUpdate), and after the game
                // cleared its bubbles (a scene load): then it is simply not displayed any more.
                if (!shown || hint.Text != label || !m.TryGetDisplayedBubble(hint.BubbleDrawableUniqueId, out _))
                {
                    hint.Text = label;
                    m.RequestDisplay(hint);
                    shown = true;
                }
            }
            catch (Exception e)
            {
                if (!warnedHint)
                {
                    warnedHint = true;
                    Plugin.Log.LogWarning("Mount: the interaction hint could not be shown: " + e.Message);
                }
            }
        }

        private static bool warnedHint;
    }
}
