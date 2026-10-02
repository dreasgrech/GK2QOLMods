using System;
using System.Collections.Generic;
using BepInEx;
using LazyBearTechnology;
using UnityEngine;

using GK2Mounts.Api;

namespace GK2Mounts
{
    // The keeper's controls for instances in the world (docs/mounts-instances.md), on with General.InstanceControls:
    // - on foot at a standing cart, by area (the zones below, docs/mounts-load-on-foot.md):
    //     holding something the cart takes, anywhere in either area: the top such item onto the cart ("Load on cart"),
    //       or "Cart is full" (the key then sets nothing down: next to the cart the game's drop could put the item
    //       inside the cart's drawing, which has no collider);
    //     empty hands in the donkey's area: ride it ("Drive");
    //     empty hands in the cart's area, with cargo: its top item into the hands ("Take from cart"), not within
    //       half a second of a load, so a quick second press doesn't take back what was just loaded;
    // - on foot with empty hands, the interact key next to a standing donkey without a cart rides it;
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

        private enum FootAction { None, Ride, Load, Full, Take }
        private static FootAction action;
        private static bool hintAtCart;          // the label over the cart body instead of the donkey
        private static float lastLoad = -10f;
        private const float TakeAfterLoad = 0.5f;
        private const float CartLookRange = 4f;  // cart body middle to keeper: farther, no area of it can reach him

        // Nothing in reach any more (the game left, every instance gone).
        internal static void Clear()
        {
            target = null;
            label = null;
            DestroyZoneLines();
            UpdateHint();
        }

        internal static void Tick(PlayerController pc)
        {
            try
            {
                Find(pc);
                MeasureZones(pc);
            }
            finally
            {
                UpdateHint();
            }
        }

        // ------------------------------------------------------------------ zones of a standing cart

        // On foot next to a standing cart (docs/mounts-load-on-foot.md), measured in the cart's own frame from the cart
        // body's drawn box (Mount.Instance.TryCartBox): 'along' from the donkey's body towards the back end, 'across'
        // from the cart's line. Two areas, side by side, as wide as the cart body plus General.CartZoneSide each way:
        //   the cart:   from the cart body's front edge (+ CartZoneFrontShift) to its back end + CartZoneRear;
        //   the donkey: from DonkeyZoneAhead ahead of the donkey's body to where the cart's area starts (none for a
        //               free cart).
        // The first version (whichever of the donkey's body and the back end is nearer) split the cart in the middle:
        // 1.40 apart, standing at the cart's front edge still meant the donkey (the user's screenshot, 2026-10-02).
        internal enum Zone { None, Donkey, Cart }

        internal struct CartZones
        {
            public Vector3 Origin, Back, Side;
            public float DonkeyStart, Split, CartEnd, Left, Right;
            public bool HasDonkey;
        }

        internal static bool TryZones(Mount.Instance i, out CartZones z)
        {
            z = default;
            if (!i.TryCartBox(out Vector3 origin, out Vector3 back, out float front, out float end, out float left, out float right))
            {
                return false;
            }
            float side = Plugin.CartZoneSide.Value;
            z.Origin = origin;
            z.Back = back;
            z.Side = Vector3.Cross(back, Vector3.up);
            z.HasDonkey = i.HasDonkey;
            z.Split = front + Plugin.CartZoneFrontShift.Value;
            z.DonkeyStart = -Plugin.DonkeyZoneAhead.Value;
            z.CartEnd = end + Plugin.CartZoneRear.Value;
            z.Left = left + side;
            z.Right = right - side;
            return true;
        }

        internal static Zone ZoneAt(in CartZones z, Vector3 p, out float along, out float across)
        {
            Vector3 rel = p - z.Origin;
            rel.y = 0f;
            along = Vector3.Dot(rel, z.Back);
            across = Vector3.Dot(rel, z.Side);
            if (across > z.Left || across < z.Right || along > z.CartEnd)
            {
                return Zone.None;
            }
            if (along >= z.Split)
            {
                return Zone.Cart;
            }
            return z.HasDonkey && along >= z.DonkeyStart ? Zone.Donkey : Zone.None;
        }

        // Measuring, the panel only: the nearest standing cart within 3, its numbers and the zone the keeper is in, live
        // on the Instances tab, and the two areas drawn on the ground (ShowZones). Each cart's box is logged once.
        internal static string ZoneReadout = "No standing cart within 3.";
        internal static bool ShowZones = true;
        private static readonly HashSet<Mount.Instance> zonesLogged = new HashSet<Mount.Instance>();
        private const float ZoneLookRange = 3f;
        private static float nextZoneText;

        private static void MeasureZones(PlayerController pc)
        {
            if (!TuningWindow.Showing())
            {
                HideZoneLines();
                return;
            }
            try
            {
                Vector3 k = pc.PlayerData.position.Value;
                Mount.Instance best = null;
                float bestD = ZoneLookRange * ZoneLookRange;
                foreach (Mount.Instance i in Mount.All)
                {
                    if (!i.HasCart || i.IsRidden || !i.Ready || i.SceneId != pc.PlayerData.currentGameSceneId)
                    {
                        continue;
                    }
                    Vector3 d = i.CartPoint() - k;
                    d.y = 0f;
                    if (d.sqrMagnitude < bestD)
                    {
                        bestD = d.sqrMagnitude;
                        best = i;
                    }
                }
                if (best == null || !TryZones(best, out CartZones z))
                {
                    ZoneReadout = best == null ? $"No standing cart within {ZoneLookRange:0}." : $"{best.Describe()}: its cart is not drawn, nothing to measure.";
                    HideZoneLines();
                    return;
                }
                Zone zone = ZoneAt(z, k, out float along, out float across);
                if (ShowZones)
                {
                    DrawZoneLines(z, zone);
                }
                else
                {
                    HideZoneLines();
                }
                float front = z.Split - Plugin.CartZoneFrontShift.Value;
                float end = z.CartEnd - Plugin.CartZoneRear.Value;
                float left = z.Left - Plugin.CartZoneSide.Value, right = z.Right + Plugin.CartZoneSide.Value;
                if (Time.unscaledTime >= nextZoneText)
                {
                    nextZoneText = Time.unscaledTime + 0.1f;
                    ZoneReadout = $"{best.Describe()}: the cart body runs from {front:0.00} to {end:0.00} behind the donkey's body{(best.HasDonkey ? "" : " (a free cart: where its donkey would stand)")}, sides at {left:0.00} and {right:0.00}.\n"
                        + $"Keeper: {along:0.00} along the cart from the donkey's body, {across:0.00} to the side.\n"
                        + $"Zone: {(zone == Zone.Cart ? "the cart (orange)" : zone == Zone.Donkey ? "the donkey (blue)" : "none")}.";
                }
                if (zonesLogged.Add(best))
                {
                    Plugin.Log.LogInfo($"[zones] {best.Describe()}: cart body {front:0.00} to {end:0.00} behind the donkey's body, sides {left:0.00} / {right:0.00}.");
                }
            }
            catch (Exception e)
            {
                ZoneReadout = "Measuring failed: " + e.Message;
                HideZoneLines();
            }
        }

        // Two outlines on the ground, a little above the keeper's height: the donkey's area blue, the cart's orange; the
        // one the keeper stands in drawn brighter and thicker. The mod's own objects, removed in Clear.
        private static LineRenderer donkeyLine, cartLine;
        private static Material lineMaterial;
        private static readonly Color DonkeyColor = new Color(0.35f, 0.7f, 1f), CartColor = new Color(1f, 0.6f, 0.15f);

        private static void DrawZoneLines(in CartZones z, Zone now)
        {
            float y = z.Origin.y + 0.05f;
            cartLine = Outline(cartLine, "GK2Mounts zone: cart");
            SetBox(cartLine, z, z.Split, z.CartEnd, y, CartColor, now == Zone.Cart);
            if (z.HasDonkey)
            {
                donkeyLine = Outline(donkeyLine, "GK2Mounts zone: donkey");
                SetBox(donkeyLine, z, z.DonkeyStart, z.Split, y, DonkeyColor, now == Zone.Donkey);
            }
            else if (donkeyLine != null && donkeyLine)
            {
                donkeyLine.enabled = false;
            }
        }

        private static LineRenderer Outline(LineRenderer lr, string name)
        {
            if (lr != null && lr)
            {
                lr.enabled = true;
                return lr;
            }
            if (lineMaterial == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null)
                {
                    sh = Shader.Find("Hidden/Internal-Colored");
                }
                lineMaterial = new Material(sh);
            }
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = lineMaterial;
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = 32000;
            return lr;
        }

        private static void SetBox(LineRenderer lr, in CartZones z, float a0, float a1, float y, Color c, bool active)
        {
            Vector3 o = new Vector3(z.Origin.x, y, z.Origin.z);
            lr.SetPosition(0, o + z.Back * a0 + z.Side * z.Left);
            lr.SetPosition(1, o + z.Back * a1 + z.Side * z.Left);
            lr.SetPosition(2, o + z.Back * a1 + z.Side * z.Right);
            lr.SetPosition(3, o + z.Back * a0 + z.Side * z.Right);
            Color col = active ? c : new Color(c.r, c.g, c.b, 0.45f);
            lr.startColor = lr.endColor = col;
            lr.startWidth = lr.endWidth = active ? 0.06f : 0.03f;
        }

        private static void HideZoneLines()
        {
            if (donkeyLine != null && donkeyLine) donkeyLine.enabled = false;
            if (cartLine != null && cartLine) cartLine.enabled = false;
        }

        private static void DestroyZoneLines()
        {
            if (donkeyLine != null && donkeyLine) UnityEngine.Object.Destroy(donkeyLine.gameObject);
            if (cartLine != null && cartLine) UnityEngine.Object.Destroy(cartLine.gameObject);
            donkeyLine = cartLine = null;
        }

        private static void Find(PlayerController pc)
        {
            target = null;
            label = null;
            action = FootAction.None;
            hintAtCart = false;
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
                    if (!gameTarget)
                    {
                        FindOnFoot(pc);
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

        private static void FindOnFoot(PlayerController pc)
        {
            PlayerData pd = pc.PlayerData;
            Vector3 k = pd.position.Value;
            Mount.Instance cart = CartAround(pc, k, out Zone zone);
            if (pd.HasOverheadItem)
            {
                // Only what the cart takes (not a zombie): anything else keeps the game's own key (set it down).
                if (cart != null && MountRules.Allowed(MountFeature.Cargo) && pd.TryGetOverheadItem(Cargo.Rig.AllowedItem, out _))
                {
                    bool fits = cart.CargoRig.HeldItemFits(pd);
                    Set(cart, fits ? FootAction.Load : FootAction.Full, fits ? "Load on cart" : "Cart is full", atCart: true);
                }
                return;
            }
            if (cart != null)
            {
                if (zone == Zone.Donkey && cart.Rideable)
                {
                    Set(cart, FootAction.Ride, "Drive", atCart: false);
                    return;
                }
                if (zone == Zone.Cart && cart.CargoRig.Count > 0 && MountRules.Allowed(MountFeature.Cargo)
                    && Time.unscaledTime - lastLoad >= TakeAfterLoad)
                {
                    Set(cart, FootAction.Take, "Take from cart", atCart: true);
                    return;
                }
            }
            Mount.Instance d = Nearest(pc, k, RideReach, i => i.HasDonkey && !i.HasCart && i.Rideable);
            if (d != null)
            {
                Set(d, FootAction.Ride, "Ride", atCart: false);
            }
        }

        private static void Set(Mount.Instance i, FootAction a, string text, bool atCart)
        {
            target = i;
            action = a;
            label = HintText(text);
            hintAtCart = atCart;
        }

        // The standing cart (a donkey with its cart, or a free cart) whose donkey's or cart's area the keeper stands in;
        // the nearest by its cart body when areas overlap. Null when none.
        private static Mount.Instance CartAround(PlayerController pc, Vector3 k, out Zone zone)
        {
            zone = Zone.None;
            string scene = pc.PlayerData.currentGameSceneId;
            Mount.Instance best = null;
            float bestD = CartLookRange * CartLookRange;
            List<Mount.Instance> all = Mount.All;
            for (int n = 0; n < all.Count; n++)
            {
                Mount.Instance i = all[n];
                if (!i.HasCart || i.Ephemeral || i.IsRidden || !i.Ready || i.SceneId != scene)
                {
                    continue;
                }
                Vector3 d = i.CartPoint() - k;
                d.y = 0f;
                if (d.sqrMagnitude >= bestD || !ZonesOf(i, out CartZones z))
                {
                    continue;
                }
                Zone at = ZoneAt(z, k, out _, out _);
                if (at != Zone.None)
                {
                    bestD = d.sqrMagnitude;
                    best = i;
                    zone = at;
                }
            }
            return best;
        }

        // A standing cart's areas, measured again at most twice a second or when it moved (measuring walks its
        // renderers). A changed setting shows within half a second.
        private struct CachedZones
        {
            public CartZones Z;
            public float Until;
            public Vector3 At;
        }

        private static readonly Dictionary<Mount.Instance, CachedZones> zoneCache = new Dictionary<Mount.Instance, CachedZones>();

        private static bool ZonesOf(Mount.Instance i, out CartZones z)
        {
            if (zoneCache.TryGetValue(i, out CachedZones c) && Time.unscaledTime < c.Until && (c.At - i.Position).sqrMagnitude < 0.0001f)
            {
                z = c.Z;
                return true;
            }
            if (!TryZones(i, out z))
            {
                zoneCache.Remove(i);
                return false;
            }
            if (zoneCache.Count > 32)
            {
                zoneCache.Clear();
            }
            zoneCache[i] = new CachedZones { Z = z, Until = Time.unscaledTime + 0.5f, At = i.Position };
            return true;
        }

        // Something of the game's own takes the interact key (a character, a work station, a pile, an item in front):
        // its prompt is shown and the key is its. As the game's own key handling decides (PlayerInputHandler.
        // UpdateInput): a big drop always; a world object only with an interaction or a script event. Any object in
        // front counted before, so with an item in hand next to a fence post the game's drop happened instead.
        private static bool GameHasTarget(PlayerController pc)
        {
            PlayerInteractionComponent ic = pc.PlayerInteractionComponent;
            if (ic == null)
            {
                return false;
            }
            if (ic.BigDropUnderInteraction != null && ic.BigDropUnderInteraction.InteractionHandler.HasInteraction())
            {
                return true;
            }
            Wgo w = ic.WgoUnderInteraction;
            if (w == null)
            {
                return false;
            }
            if (w.Data != null && w.Data.Events.Count > 0)
            {
                return true;
            }
            IWGOInteractionHandler h = w.InteractionHandler;
            return h == null || h.HasInteraction(); // no handler to ask: the game's own, to be safe
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
                switch (action)
                {
                    case FootAction.Load:
                        if (t.Handle.LoadFromHead().Ok)
                        {
                            lastLoad = Time.unscaledTime;
                        }
                        return true;
                    case FootAction.Full:
                        Mount.Say("The cart is full.");
                        return true;
                    case FootAction.Take:
                        t.Handle.TakeToHead();
                        return true;
                    case FootAction.Ride:
                        using (Mount.Bind(t))
                        {
                            t.RideFromWorld(pc, showMessage: true);
                        }
                        return true;
                }
                return false;
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
        // Asked every frame while something is in reach. The game's GetIconId builds a new string on every call, so the
        // icon is asked twice a second (a rebound key or another input device shows within half a second) and the
        // joined text is kept until the icon or the text changes.
        private static string hintIcon, hintText, hintJoined;
        private static float nextIconCheck;

        private static string HintText(string text)
        {
            try
            {
                string icon = hintIcon;
                if (icon == null || Time.unscaledTime >= nextIconCheck)
                {
                    nextIconCheck = Time.unscaledTime + 0.5f;
                    icon = ControllerIconLibrary.GetIconId(GameKey.Interaction);
                }
                if (hintJoined == null || !ReferenceEquals(text, hintText) || icon != hintIcon)
                {
                    hintIcon = icon;
                    hintText = text;
                    hintJoined = icon + text;
                }
                return hintJoined;
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
            internal bool AtCart; // over the cart body (load, take) instead of the donkey's bubble point

            public SGuid BubbleDrawableUniqueId => id;

            public List<LazyWidgetDataBase> BubbleDrawableWidgets => new List<LazyWidgetDataBase>
            {
                new UIInteractionHintWidgetData(new UIInteractionHintRowWidgetData(new InteractionInfo(Text))),
            };

            public Vector3 BubbleDrawablePosition => Over == null ? Vector3.zero : AtCart ? Over.CartPoint() + Vector3.up * 1.3f : Over.HintPoint();
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
                hint.AtCart = hintAtCart;
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
