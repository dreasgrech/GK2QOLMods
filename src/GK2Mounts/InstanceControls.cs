using System;
using BepInEx;
using UnityEngine;

namespace GK2Mounts
{
    // The keeper's controls for instances in the world (docs/mounts-instances.md), on with General.InstanceControls:
    // - on foot with empty hands, the interact key next to a standing donkey rides it;
    // - riding a donkey without its cart, the interact key next to a free cart, or General.HitchKey, hitches it;
    // - riding a donkey with its cart, General.HitchKey leaves the cart standing.
    // The interact key only counts when the game has nothing of its own to do with it (no item or object in front of
    // the keeper): PlayerInputHandler_UpdateInput_Patch asks TryInteract first. A label over the donkey or cart in reach
    // says what the key does (the game's own hints need an interactable world object, which ours are not).
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
        }

        internal static void Tick(PlayerController pc)
        {
            target = null;
            label = null;
            if (!Plugin.InstanceControls.Value || !pc.IsControlsEnabled)
            {
                return;
            }
            try
            {
                if (!Mount.IsMounted)
                {
                    if (!pc.PlayerData.HasOverheadItem)
                    {
                        target = Nearest(pc, pc.PlayerData.position.Value, RideReach, i => i.HasDonkey);
                        if (target != null)
                        {
                            label = Key(GameKeyName()) + (target.HasCart ? " Drive" : " Ride");
                        }
                    }
                    return;
                }
                Mount.Instance r = Mount.Ridden;
                if (r.HasDonkey && !r.HasCart && r.Ready)
                {
                    target = Nearest(pc, r.BodyPoint(), HitchReach, i => !i.HasDonkey);
                    if (target != null)
                    {
                        label = Key(GameKeyName()) + " Hitch the cart";
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
            PlayerInteractionComponent ic = pc.PlayerInteractionComponent;
            if ((ic.BigDropUnderInteraction != null && ic.BigDropUnderInteraction.InteractionHandler.HasInteraction()) || ic.WgoUnderInteraction != null)
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

        // The game's interact key; its binding has no name to read here (LazyInput offers none), so the default.
        private static string GameKeyName() => "E";

        private static string Key(string k) => "[" + k + "]";

        // ------------------------------------------------------------------ the label

        private static GUIStyle style, shadow;

        internal static void OnGUI()
        {
            if (label == null || target == null || Event.current.type != EventType.Repaint || !Mount.IsRegistered(target))
            {
                return;
            }
            Camera cam = CameraSystem.Instance != null ? CameraSystem.Instance.WorldCamera : Camera.main;
            if (cam == null)
            {
                return;
            }
            Vector3 sp = cam.WorldToScreenPoint(target.BodyPoint() + Vector3.up * 1.7f);
            if (sp.z <= 0f)
            {
                return;
            }
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(Screen.height / 45f) };
                style.normal.textColor = new Color(1f, 0.92f, 0.45f);
                shadow = new GUIStyle(style);
                shadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            }
            float y = Screen.height - sp.y;
            var r = new Rect(sp.x - 200f, y - 20f, 400f, 40f);
            for (int dx = -2; dx <= 2; dx += 2)
            {
                for (int dy = -2; dy <= 2; dy += 2)
                {
                    if (dx != 0 || dy != 0) GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), label, shadow);
                }
            }
            GUI.Label(r, label, style);
        }
    }
}
