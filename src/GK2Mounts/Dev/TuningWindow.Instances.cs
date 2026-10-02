using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

using GK2Mounts.Api;

namespace GK2Mounts
{
    // The Instances tab (donkeys and carts standing in the world, docs/mounts-instances.md) and instance editing: with
    // an instance picked ("Edit its settings"), every row of every tab reads and writes that instance's own value (its
    // override, MountHandle.Tuning) instead of the cfg; right-click takes the instance's value away again.
    internal static partial class TuningWindow
    {
        internal const string PanelOwner = "dreas.gk2.mounts.panel";

        private static Mount.Instance editing;

        // The instance edited on the panel: always active (Tiers.cs), so what you tune is what you see.
        internal static Mount.Instance Editing => editing;

        private static bool EditingNow => editing != null;

        // The style the tabs show: the edited instance's, else the ride's.
        private static MountStyle PanelStyle => EditingNow ? editing.CurrentStyle : Mount.CurrentStyle;

        // Only tuning values can be an instance's own (the panel's own size, the safety switch and the like cannot).
        private static bool EditsInstance(ConfigEntryBase e) => EditingNow && e != null && Tuned.ByEntry.ContainsKey(e);

        // A click that changes the list of instances or a row's contents runs after the tab is laid out, so this
        // event draws the same controls its layout pass measured (IMGUI throws otherwise).
        private static Action pending;

        // The value a row shows: the edited instance's own, else the value in effect for everyone; the cfg's when not
        // editing (as the panel always did).
        private static T Val<T>(ConfigEntry<T> e)
        {
            if (!EditsInstance(e))
            {
                return e.Value;
            }
            if (editing.Overrides.TryGetValue(e.Definition.Key, out object o) && o is T t)
            {
                return t;
            }
            return Tuned.ByEntry.TryGetValue(e, out ITuned tu) && tu.Current is T g ? g : e.Value;
        }

        // A row's change: into the edited instance (clamped like any override), else into the cfg.
        private static void Put<T>(ConfigEntry<T> e, T v)
        {
            if (!EditsInstance(e))
            {
                e.Value = v;
                return;
            }
            object c = Tuned.ByEntry.TryGetValue(e, out ITuned tu) ? tu.Clamp(v) : v;
            editing.Overrides[e.Definition.Key] = c;
            editing.OverrideOwners[e.Definition.Key] = PanelOwner;
            InstanceChanged(editing);
        }

        // True when the edited instance had its own value for this key (it now follows everyone's again).
        private static bool Unput(ConfigEntryBase e)
        {
            if (!EditsInstance(e) || !editing.Overrides.Remove(e.Definition.Key))
            {
                return false;
            }
            editing.OverrideOwners.Remove(e.Definition.Key);
            InstanceChanged(editing);
            return true;
        }

        private static void InstanceChanged(Mount.Instance i)
        {
            i.ColorsRig.MarkDirty();
            using (Mount.Bind(i))
            {
                i.OnTuningChanged(i.CurrentStyle);
                i.LanternRig.Refresh();
                i.CargoRig.RefreshViews();
            }
        }

        // Under the tab bar of every tab: who the rows change. Either one donkey's own values (editing), or everyone's (the
        // cfg), which every donkey uses where it has no value of its own.
        private static void DrawScope()
        {
            if (EditingNow)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Editing {editing.Describe()}'s own settings (marked in the world). Every row changes only this one: magenta = its own value, right-click = back to everyone's. The style tabs show its style ({editing.CurrentStyle}).", instanceSet);
                if (GUILayout.Button("Stop", Wd(60f)))
                {
                    editing = null;
                }
                GUILayout.EndHorizontal();
                return;
            }
            Mount.Instance r = Mount.Ridden;
            int standing = 0;
            foreach (Mount.Instance i in Mount.All)
            {
                if (!i.Ephemeral && !i.IsRidden) standing++;
            }
            string ride = r != null
                ? $"the donkey you ride ({(r.Ephemeral ? "the summoned donkey" : r.Describe())}, {r.CurrentStyle} style)"
                : "nothing is ridden right now";
            GUILayout.Label($"Editing everyone's settings (your cfg). They apply to {ride}, to {standing} donkey(s) and cart(s) standing in the world, and to every later ride; a donkey keeps its own value where it has one. Style rows ([Ride] / [Cart]) apply to donkeys in that style; the style tabs show the {PanelStyle} set. One donkey only: Instances tab, Edit its settings.", small);
        }

        // Over every donkey and cart in the keeper's scene while the Instances tab is open or one is edited: its mod and
        // number, in its mod's colour (the same colour as its group in the list), so the list and the world match.
        private static GUIStyle markerStyle, markerEditStyle;
        private static readonly Dictionary<string, GUIStyle> ownerMarkerStyles = new Dictionary<string, GUIStyle>();
        private static readonly Dictionary<string, GUIStyle> ownerHeaderStyles = new Dictionary<string, GUIStyle>();

        // A steady colour per mod: its GUID hashed to a hue (the same every session).
        private static Color OwnerColor(string owner)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in owner ?? "")
                {
                    h = h * 31 + c;
                }
                float hue = (h & 0x7fffffff) % 360 / 360f;
                return Color.HSVToRGB(hue, 0.45f, 1f);
            }
        }

        // The mod's name, short enough for a marker (cached: asked twice a frame for every marker).
        private static readonly Dictionary<string, string> ownerShort = new Dictionary<string, string>();

        private static string OwnerShort(string owner)
        {
            if (owner != null && ownerShort.TryGetValue(owner, out string cached))
            {
                return cached;
            }
            string n = OwnerShortUncached(owner);
            if (owner != null)
            {
                ownerShort[owner] = n;
            }
            return n;
        }

        private static string OwnerShortUncached(string owner)
        {
            string n = Mount.OwnerName(owner);
            if (n.StartsWith("GK2 ", StringComparison.Ordinal) && n.Length > 4)
            {
                n = n.Substring(4);
            }
            return n.Length > 14 ? n.Substring(0, 13) + "…" : n;
        }

        private static GUIStyle OwnerStyle(Dictionary<string, GUIStyle> cache, GUIStyle basis, string owner)
        {
            if (!cache.TryGetValue(owner, out GUIStyle st))
            {
                st = new GUIStyle(basis);
                st.normal.textColor = OwnerColor(owner);
                cache[owner] = st;
            }
            return st;
        }

        // Another instance as this row names it: #id within the same mod, owner#id across mods.
        private static string Ref(Mount.Instance from, Mount.Instance to)
        {
            return to == null ? "?" : to.OwnerId == from.OwnerId ? "#" + to.Id : $"{OwnerShort(to.OwnerId)} #{to.Id}";
        }

        // A marker's text and size, made once per look (owner, number, editing, stepping): building and measuring 63
        // strings twice a frame was most of the markers' 0.56 ms (measured 2026-10-01).
        private static readonly Dictionary<Mount.Instance, (bool Ed, bool Step, string Text)> markerText = new Dictionary<Mount.Instance, (bool, bool, string)>();
        private static readonly Dictionary<string, Vector2> markerSize = new Dictionary<string, Vector2>();

        private static string MarkerText(Mount.Instance i, bool ed)
        {
            bool step = i.IsStepping;
            if (markerText.TryGetValue(i, out var c) && c.Ed == ed && c.Step == step)
            {
                return c.Text;
            }
            if (markerText.Count > Mount.All.Count + 64)
            {
                // Removed instances (it held on to them): keep only the registered ones.
                var gone = new List<Mount.Instance>();
                foreach (Mount.Instance k in markerText.Keys)
                {
                    if (!Mount.IsRegistered(k)) gone.Add(k);
                }
                foreach (Mount.Instance k in gone)
                {
                    markerText.Remove(k);
                }
            }
            string text = $"{OwnerShort(i.OwnerId)} #{i.Id}{(ed ? " editing" : "")}{(step ? " ~" : "")}";
            markerText[i] = (ed, step, text);
            return text;
        }

        private static Vector2 MarkerSize(GUIStyle st, string text)
        {
            string k = (st == markerEditStyle ? "e" : "n") + st.fontSize + ":" + text; // the panel's text size changes it
            if (!markerSize.TryGetValue(k, out Vector2 size))
            {
                if (markerSize.Count > 1024)
                {
                    markerSize.Clear();
                }
                size = st.CalcSize(new GUIContent(text));
                markerSize[k] = size;
            }
            return size;
        }

        private static void DrawWorldMarkers(float scale)
        {
            // Drawn on the repaint only: plain labels take no input, and every other event (the layout pass, mouse moves)
            // drew all 63 again for nothing (0.33 ms and 12-22 KB a frame, measured 2026-10-01).
            if (tab != 5 && !EditingNow || Event.current.type != EventType.Repaint)
            {
                return;
            }
            PlayerData pd = MainGame.Instance != null ? MainGame.PlayerData : null;
            if (CameraSystem.Instance == null || pd == null)
            {
                return;
            }
            if (markerStyle == null)
            {
                markerStyle = new GUIStyle(GUI.skin.box) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                markerStyle.normal.textColor = Color.white;
                markerEditStyle = new GUIStyle(markerStyle);
                markerEditStyle.normal.textColor = new Color(1f, 0.45f, 1f);
            }
            foreach (Mount.Instance i in Mount.All)
            {
                if (i.Ephemeral || i.SceneId != pd.currentGameSceneId || !i.Ready)
                {
                    continue;
                }
                // The game's own projection (as its bubbles use): its world camera renders into a smaller pixel-art
                // texture that is scaled to the screen, so Camera.main's screen points are in that texture's pixels
                // (found 2026-09-30: the labels sat down and left of the donkeys).
                Vector3 sp = CameraSystem.WorldToScreenPoint(i.HintPoint() + Vector3.up * 0.35f);
                if (sp.z <= 0f)
                {
                    continue;
                }
                bool ed = editing == i;
                string text = MarkerText(i, ed);
                GUIStyle st = ed ? markerEditStyle : OwnerStyle(ownerMarkerStyles, markerStyle, i.OwnerId);
                Vector2 size = MarkerSize(st, text);
                float x = sp.x / scale - size.x / 2f;
                float y = (Screen.height - sp.y) / scale - size.y;
                GUI.Label(new Rect(x, y, size.x, size.y), text, st);
            }
        }

        // For testing several held items without the perk (perk_extra_overhead_1): the game's own resource behind it,
        // extra_overhead (PlayerData.OverheadStackLimit = 1 + extra_overhead), as its script SetOverheadStackLimit sets
        // it. It is part of the save, so it is set back before sleeping.
        private static void HeadLimitRow()
        {
            PlayerData pd = MainGame.PlayerData;
            if (pd == null)
            {
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Head limit {pd.OverheadStackLimit} (extra_overhead {pd.ExtraOverhead}), holding {pd.OverheadCount}. Testing only: it is saved with the game, set it back before sleeping.", small);
            if (GUILayout.Button("+1", Wd(40)))
            {
                pending = () => pd.SetRes("extra_overhead", pd.ExtraOverhead + 1);
            }
            if (GUILayout.Button("Back to 1", Wd(90)))
            {
                pending = () => pd.SetRes("extra_overhead", 0);
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawInstances()
        {
            Header("Donkeys and carts standing in the world");
            GUILayout.Label("Each is its own donkey (with or without its cart) or a cart on its own, with its own settings on top of everyone's. Walk up to a donkey with empty hands and press E to ride it; a dismount leaves it where it is, with its cart and cargo. Riding a donkey without its cart, ride up to a free cart's shafts and press E (or the hitch key) to take it; with the cart, the hitch key leaves it standing. Mods do the same through the API (Mounts.SpawnDonkey, SpawnCart, Hitch, Unhitch, MountHandle.Tuning). The ones marked kept with saves (the panel's spawns are) come back when that save is loaded, with their cargo and own settings; the game's save itself is never changed (a side file next to it). The others are gone after a reload, with their cargo on the ground where they stood.", small);
            GUILayout.Label("Saves: " + Mount.PersistenceNote, small);
            Toggle("Controls in the game (the interact key rides and hitches, the label shows)", Plugin.InstanceControls, null);
            HitchKeyRow();

            Header("Standing cart zones (measuring)");
            GUILayout.Label("For loading and unloading a standing cart on foot (docs/mounts-load-on-foot.md): the nearest standing cart within 3, drawn on the ground while the panel is open. Orange: the cart's area (Take from cart, Load on cart). Blue: the donkey's area (Ride/Drive). The one the keeper stands in is drawn thicker. Both are as wide as the cart body plus the side margin.", small);
            InstanceControls.ShowZones = GUILayout.Toggle(InstanceControls.ShowZones, " Draw the areas on the ground");
            FloatSlider("Beside the cart body (either side)", Plugin.CartZoneSide, 0f, 2f, 2, null);
            FloatSlider("Behind the cart body's back end", Plugin.CartZoneRear, 0f, 2f, 2, null);
            FloatSlider("Donkey/cart border, from the cart body's front edge", Plugin.CartZoneFrontShift, -1f, 1f, 2, null);
            FloatSlider("Ahead of the donkey's body", Plugin.DonkeyZoneAhead, 0.2f, 2.5f, 2, null);
            GUILayout.Label(InstanceControls.ZoneReadout, small);
            HeadLimitRow();

            Header("Idle shuffle (standing donkeys without a cart)");
            GUILayout.Label($"A few slow steps now and then inside a small box around where it was left, never straight up or down, never with a cart, and not while the keeper is close. Off in GK2 Mounts; mods switch it on (Mounts.Behaviour IdleShuffle, per donkey through MountHandle.Tuning). {(EditingNow ? $"These rows are {editing.Describe()}'s own values." : "These rows are everyone's values: every standing donkey without its own value uses them.")} Each donkey's status is in the list below.", small);
            Toggle("Idle shuffle", IdleShuffle.On, null);
            FloatSlider("Area sideways (either way)", IdleShuffle.AreaWidth, 0.1f, 4f, 2, null);
            FloatSlider("Area up/down (either way)", IdleShuffle.AreaDepth, 0f, 2f, 2, null);
            FloatSlider("Shortest step", IdleShuffle.StepMin, 0.05f, 2f, 2, null);
            FloatSlider("Longest step", IdleShuffle.StepMax, 0.1f, 3f, 2, null);
            FloatSlider("Speed (units/s)", IdleShuffle.Speed, 0.1f, 2f, 2, null);
            FloatSlider("Walk animation rate", IdleShuffle.AnimSpeed, 0.2f, 3f, 2, null);
            FloatSlider("Shortest pause (s)", IdleShuffle.PauseMin, 0.5f, 120f, 1, null);
            FloatSlider("Longest pause (s)", IdleShuffle.PauseMax, 0.5f, 300f, 1, null);
            FloatSlider("Steepest step (degrees)", IdleShuffle.MaxAngle, 0f, 60f, 0, null);
            FloatSlider("Turn chance", IdleShuffle.TurnChance, 0f, 1f, 2, null);
            FloatSlider("Hold still with the keeper within", IdleShuffle.KeeperDistance, 0f, 10f, 1, null);
            FloatSlider("Foals: trot speed after the parent", IdleShuffle.FollowSpeed, 0.3f, 8f, 1, null);
            FloatSlider("Foals: follow only within", IdleShuffle.FollowMaxDistance, 1f, 30f, 1, null);
            FloatSlider("Foals: settle this close to the parent", IdleShuffle.FollowGap, 0.3f, 4f, 2, null);
            Toggle("Foals: follow however far (stop only when blocked)", IdleShuffle.FollowAlways, null);
            Toggle("Foals: find a way round fences and buildings", IdleShuffle.FollowRoutes, null);
            FloatSlider("Foals: a way round may reach this far", IdleShuffle.RouteRange, 2f, 8f, 1, null);

            Header("Spawn one in front of the keeper");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Donkey"))
            {
                pending = () => SpawnHere(true, false);
            }
            if (GUILayout.Button("Donkey with its cart"))
            {
                pending = () => SpawnHere(true, true);
            }
            if (GUILayout.Button("Cart on its own"))
            {
                pending = () => SpawnHere(false, true);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"A donkey family: a donkey and {familyFoals} foal{(familyFoals == 1 ? "" : "s")}"))
            {
                pending = SpawnFamily;
            }
            GUILayout.EndHorizontal();
            familyFoals = SessionIntRow("Foals in a family", familyFoals, 1, 20);
            foalScale = SessionFloatRow("Foal size (1 = a full-size donkey)", foalScale, 0.3f, 0.9f);
            GUILayout.Label(spawnNote, small);

            Mount.Instance r = Mount.Ridden;
            if (r != null)
            {
                Header("The ride");
                GUILayout.Label($"Riding {(r.Ephemeral ? "the summoned donkey (it goes at dismount)" : r.Describe())}, {r.CurrentStyle} style.", label);
                if (r.HasCart && GUILayout.Button("Leave the cart standing here (unhitch)"))
                {
                    pending = () => Mounts.UnhitchRidden();
                }
            }

            if (Event.current.type == EventType.Layout)
            {
                RefreshGroups();
            }
            Header(groupsTitle);
            if (groups.Count == 0)
            {
                GUILayout.Label("None yet.", small);
            }
            PlayerData pd = MainGame.Instance != null ? MainGame.PlayerData : null;
            foreach (OwnerGroup g in groups)
            {
                // A click on the header folds the group. Many rows of IMGUI controls are expensive (63 donkeys
                // with all their buttons took 27 ms a frame, measured 2026-10-01), so a big group starts folded.
                bool open = groupOpen.TryGetValue(g.Owner, out bool o) ? o : g.Mine.Count + g.Sleeping.Count <= 6;
                if (GUILayout.Button(open ? g.HeaderOpen : g.HeaderClosed, OwnerStyle(ownerHeaderStyles, label, g.Owner)))
                {
                    string ownerKey = g.Owner;
                    bool now = open;
                    pending = () => groupOpen[ownerKey] = !now;
                }
                if (!open)
                {
                    continue;
                }
                foreach (Mount.DormantRecord d in g.Sleeping)
                {
                    // The text above its button, not beside it: a long wrapped label beside a button pushes the
                    // button off the panel's right edge (seen 2026-10-01).
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label($"#{d.Id} {d.Kind}{(d.Key != null ? $" '{d.Key}'" : "")}: dormant ({d.Why}), in {d.Scene}{(d.Cargo > 0 ? $", {d.Cargo} cargo item(s) on the ground there" : "")}. Back with its mod; Forget drops it at the next save.", small);
                    if (GUILayout.Button("Forget"))
                    {
                        Mount.DormantRecord forget = d;
                        pending = () => Mount.ForgetDormant(forget);
                    }
                    GUILayout.EndVertical();
                }
                foreach (Mount.Instance i in g.Mine)
                {
                    if (editing == i || expanded.Contains(i))
                    {
                        DrawInstanceFull(i, pd);
                    }
                    else
                    {
                        DrawInstanceCompact(i, pd);
                    }
                }
            }
            if (pending != null)
            {
                Action a = pending;
                pending = null;
                try
                {
                    a();
                }
                catch (Exception e)
                {
                    spawnNote = "Failed: " + e.Message;
                    Plugin.Log.LogWarning("Instances tab: " + e);
                }
            }
        }

        private static string spawnNote = "Spawned where the keeper looks, facing his way. Removing one takes its cargo with it.";

        private static void SpawnHere(bool donkey, bool cart)
        {
            PlayerData pd = MainGame.Instance != null ? MainGame.PlayerData : null;
            if (pd == null)
            {
                spawnNote = "Not in a game.";
                return;
            }
            Vector3 at = InFront(pd, cart ? 2.2f : 1.6f);
            bool right = pd.Direction.x >= 0f;
            MountHandle h = Mounts.Spawn(PanelOwner, new MountSpawn { Position = at, FacingRight = right, Donkey = donkey, WithCart = cart, Persist = true });
            spawnNote = h != null ? $"Spawned {h.I.Describe()} at {h.Position:F1}." : "Could not spawn (see the log).";
        }

        // The panel's own spawn choices (this session only, not settings).
        private static int familyFoals = 2;
        private static float foalScale = 0.6f;

        // A donkey with foals in front of the keeper; the donkey's own idle shuffle is switched on too, so the family
        // moves even when no mod switches the shuffle on for everyone.
        private static void SpawnFamily()
        {
            PlayerData pd = MainGame.Instance != null ? MainGame.PlayerData : null;
            if (pd == null)
            {
                spawnNote = "Not in a game.";
                return;
            }
            MountHandle parent = Mounts.Spawn(PanelOwner, new MountSpawn { Position = InFront(pd, 2.6f), FacingRight = pd.Direction.x >= 0f, Persist = true });
            if (parent == null)
            {
                spawnNote = "Could not spawn the donkey (see the log).";
                return;
            }
            parent.Tuning.Set(PanelOwner, "IdleShuffle", true);
            int n = 0;
            for (int k = 0; k < familyFoals; k++)
            {
                if (Mounts.SpawnFoal(PanelOwner, parent, foalScale) != null)
                {
                    n++;
                }
            }
            spawnNote = $"Spawned {parent.I.Describe()} with {n} foal(s) of size {foalScale:F2}.";
        }

        private static int SessionIntRow(string name, int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, Wd(LabelW));
            if (GUILayout.Button("-", Wd(22f)) && value > min) value--;
            GUILayout.Label(value.ToString(), label, Wd(30f));
            if (GUILayout.Button("+", Wd(22f)) && value < max) value++;
            GUILayout.EndHorizontal();
            return value;
        }

        private static float SessionFloatRow(string name, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, Wd(LabelW));
            value = (float)Math.Round(GUILayout.HorizontalSlider(value, min, max, Wd(90f)), 2);
            GUILayout.Label(value.ToString("F2"), label, Wd(45f));
            GUILayout.EndHorizontal();
            return value;
        }

        private static Vector3 InFront(PlayerData pd, float distance)
        {
            Vector2 d = pd.Direction.sqrMagnitude > 0.01f ? pd.Direction.normalized : Vector2.right;
            return pd.position.Value + new Vector3(d.x, 0f, d.y) * distance;
        }

        // The list's groups by the mod that owns them (each numbers its own: #1 of one mod and #1 of another are two
        // donkeys), with that mod's dormant records (kept with the save, its mod not installed) at the end of its group.
        // Made in a Layout event, so the events after it draw the same rows; again at most four times a second, or at
        // once when an instance or a dormant record came or went. Rebuilding it and its texts on every event was most
        // of this tab's garbage.
        private sealed class OwnerGroup
        {
            internal string Owner;
            internal readonly List<Mount.Instance> Mine = new List<Mount.Instance>();
            internal readonly List<Mount.DormantRecord> Sleeping = new List<Mount.DormantRecord>();
            internal string HeaderOpen, HeaderClosed;
        }

        private static readonly List<OwnerGroup> groups = new List<OwnerGroup>();
        private static readonly List<OwnerGroup> spareGroups = new List<OwnerGroup>();
        private static string groupsTitle = "In the world";
        private static float nextGroups;
        private static int groupsVersion = -1, groupsDormant = -1;

        private static void RefreshGroups()
        {
            if (Time.unscaledTime < nextGroups && groupsVersion == Mount.structureVersion && groupsDormant == Mount.Dormant.Count)
            {
                return;
            }
            nextGroups = Time.unscaledTime + 0.25f;
            groupsVersion = Mount.structureVersion;
            groupsDormant = Mount.Dormant.Count;
            // Removed instances out of the panel's own view state (it held on to them: a small leak, 2026-10-01).
            expanded.RemoveWhere(x => !Mount.IsRegistered(x));
            foreach (OwnerGroup g in groups)
            {
                g.Mine.Clear();
                g.Sleeping.Clear();
                spareGroups.Add(g);
            }
            groups.Clear();
            int total = 0;
            foreach (Mount.Instance i in Mount.All)
            {
                if (i.Ephemeral) continue;
                GroupFor(i.OwnerId).Mine.Add(i);
                total++;
            }
            foreach (Mount.DormantRecord d in Mount.Dormant)
            {
                GroupFor(d.Owner).Sleeping.Add(d);
            }
            foreach (OwnerGroup g in groups)
            {
                int kept = 0;
                foreach (Mount.Instance i in g.Mine)
                {
                    if (i.Persist) kept++;
                }
                string text = $"{Mount.OwnerName(g.Owner)} ({g.Owner}){(Mount.OwnerPresent(g.Owner) ? "" : ", NOT INSTALLED")}: {g.Mine.Count} in the world, {kept} kept with saves{(g.Sleeping.Count > 0 ? $", {g.Sleeping.Count} dormant" : "")}";
                g.HeaderOpen = "[-] " + text;
                g.HeaderClosed = "[+] " + text;
            }
            groupsTitle = $"In the world ({total}{(Mount.Dormant.Count > 0 ? $", {Mount.Dormant.Count} dormant" : "")}), by mod";
            if (rowText.Count > total + 64)
            {
                rowText.Clear();
            }
        }

        private static OwnerGroup GroupFor(string owner)
        {
            foreach (OwnerGroup g in groups)
            {
                if (g.Owner == owner) return g;
            }
            OwnerGroup n;
            if (spareGroups.Count > 0)
            {
                n = spareGroups[spareGroups.Count - 1];
                spareGroups.RemoveAt(spareGroups.Count - 1);
            }
            else
            {
                n = new OwnerGroup();
            }
            n.Owner = owner;
            groups.Add(n);
            return n;
        }

        // A compact row's text, made in a Layout event at most four times a second (the distance moves; the rest
        // rarely changes): 63 rows formatted on every event were the rest of this tab's garbage.
        private static readonly Dictionary<Mount.Instance, (float Until, string Text)> rowText = new Dictionary<Mount.Instance, (float, string)>();

        private static string CompactText(Mount.Instance i, PlayerData pd)
        {
            bool have = rowText.TryGetValue(i, out var c);
            if (have && (Event.current.type != EventType.Layout || Time.unscaledTime < c.Until))
            {
                return c.Text;
            }
            string where = i.IsRidden ? "ridden by you"
                : pd != null && pd.currentGameSceneId == i.SceneId
                ? $"{Vector3.Distance(pd.position.Value, i.BodyPoint()):F1} away"
                : $"in {i.SceneId}";
            string family = i.Parent != null ? $", foal of {Ref(i, i.Parent)}" : "";
            string level = i.tier == Mount.Tier.Idle ? ", idle (off screen)" : i.tier == Mount.Tier.Asleep ? ", asleep (far)" : "";
            string text = $"#{i.Id} {i.KindName}{(i.Key != null ? $" '{i.Key}'" : "")}{family}, {where}{level}{(i.IsStepping ? ", stepping" : "")}{(i.Persist ? "" : ", not kept")}";
            rowText[i] = (Time.unscaledTime + 0.25f, text);
            return text;
        }

        // The free cart nearest to a donkey in its scene (for the panel's hitch button: any distance).
        // The panel's own view state (this session): which mods' groups are open, which rows show everything.
        private static readonly Dictionary<string, bool> groupOpen = new Dictionary<string, bool>();
        private static readonly HashSet<Mount.Instance> expanded = new HashSet<Mount.Instance>();

        // One line per donkey: what it is, where, and whether it is kept; Details shows everything else.
        private static void DrawInstanceCompact(Mount.Instance i, PlayerData pd)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(CompactText(i, pd), label);
            if (GUILayout.Button("Details", Wd(80f)))
            {
                pending = () => expanded.Add(i);
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawInstanceFull(Mount.Instance i, PlayerData pd)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            // Ridden: under the keeper (a distance to its root would jitter with the ride). Standing: to its body.
            string where = i.IsRidden ? "ridden by you"
                : pd != null && pd.currentGameSceneId == i.SceneId
                ? $"{Vector3.Distance(pd.position.Value, i.BodyPoint()):F1} away"
                : $"in {i.SceneId}";
            List<string> cargo = i.CargoRig.Ids();
            string family = i.Parent != null ? $" foal of {Ref(i, i.Parent)}, size {i.ViewScale:F2};"
                : i.Children().Count > 0 ? $" parent of {string.Join(", ", i.Children().ConvertAll(c => Ref(i, c)))};" : "";
            GUILayout.Label($"#{i.Id} {i.KindName}{(i.Key != null ? $" '{i.Key}'" : "")}:{family} {where}, facing {(i.FacingRight ? "right" : "left")}{(i.HasCart ? $", cargo [{string.Join(", ", cargo)}]" : "")}; {i.Overrides.Count} own setting(s); {(i.Persist ? "kept with saves" : "not kept with saves")}", editing == i ? instanceSet : label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(editing == i ? "Stop editing" : "Edit its settings", Wd(120f)))
            {
                editing = editing == i ? null : i;
            }
            GUI.enabled = i.HasDonkey && i.Rideable && !i.IsRidden && !Mount.IsMounted && i.Ready;
            if (GUILayout.Button("Ride", Wd(60f)))
            {
                pending = () => i.Handle.Ride();
            }
            GUI.enabled = !i.IsRidden;
            if (GUILayout.Button("Move here", Wd(90f)) && pd != null)
            {
                pending = () => i.Handle.MoveTo(InFront(pd, 1.8f), pd.Direction.x >= 0f);
            }
            GUI.enabled = i.HasDonkey && i.HasCart && i.Ready;
            if (GUILayout.Button("Unhitch", Wd(80f)))
            {
                pending = () => Mounts.Unhitch(i.Handle);
            }
            Mount.Instance cart = NearestFreeCart(i);
            GUI.enabled = i.HasDonkey && i.Rideable && !i.HasCart && cart != null;
            if (GUILayout.Button(cart != null ? $"Hitch {Ref(i, cart)}" : "Hitch", Wd(110f)))
            {
                pending = () =>
                {
                    MountResult res = Mounts.Hitch(i.Handle, cart.Handle);
                    spawnNote = res.Ok ? $"{i.Describe()} took cart {cart.FullId}." : "Hitch refused: " + res.Reason;
                };
            }
            GUI.enabled = true;
            if (GUILayout.Button("Remove", Wd(80f)))
            {
                pending = () =>
                {
                    if (editing == i) editing = null;
                    i.Handle.Remove();
                };
            }
            GUILayout.EndHorizontal();
            // A second row: the first one is full (Keep in it pushed Remove off the edge, seen 2026-10-01).
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Fewer details", Wd(120f)))
            {
                pending = () => expanded.Remove(i);
            }
            if (GUILayout.Button(i.Persist ? "Don't keep with saves" : "Keep with saves"))
            {
                pending = () => i.Handle.SetPersist(PanelOwner, !i.Persist);
            }
            if (i.HasDonkey && i.Parent == null && i.Rideable)
            {
                if (GUILayout.Button($"Add a foal (size {foalScale:F2})"))
                {
                    pending = () =>
                    {
                        MountHandle f = Mounts.SpawnFoal(PanelOwner, i.Handle, foalScale);
                        spawnNote = f != null ? $"{f.I.Describe()} is a foal of {i.Describe()}." : "Could not add a foal (see the log).";
                    };
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(i.LevelText(), small);
            if (i.HasDonkey)
            {
                GUILayout.Label("Idle shuffle: " + i.ShuffleStatusText(), small);
            }
            if (i.Overrides.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in i.Overrides)
                {
                    parts.Add($"{kv.Key} = {kv.Value}");
                }
                GUILayout.Label("Own settings: " + string.Join("; ", parts), small);
                if (GUILayout.Button("Back to everyone's settings (clear its own)"))
                {
                    pending = () => i.Handle.Tuning.Reset();
                }
            }
            if (i.Data.Count > 0)
            {
                var parts = new List<string>();
                foreach (var mod in i.Data)
                {
                    foreach (var kv in mod.Value)
                    {
                        parts.Add($"{OwnerShort(mod.Key)}.{kv.Key} = {kv.Value}");
                    }
                }
                GUILayout.Label("Mods' data: " + string.Join("; ", parts), small);
            }
            GUILayout.EndVertical();
        }

        private static Mount.Instance NearestFreeCart(Mount.Instance donkey)
        {
            Mount.Instance best = null;
            float bestD = float.MaxValue;
            foreach (Mount.Instance c in Mount.All)
            {
                if (c.Ephemeral || c.HasDonkey || c.IsRidden || c.SceneId != donkey.SceneId)
                {
                    continue;
                }
                float d = (c.Position - donkey.Position).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }
            return best;
        }

        private static void HitchKeyRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Hitch key (General.HitchKey)", label, Wd(LabelW));
            if (GUILayout.Button(Plugin.HitchKey.Value.ToString(), Wd(90f)))
            {
                Plugin.HitchKey.Value = Plugin.HitchKey.Value == KeyCode.None ? KeyCode.J : KeyCode.None;
            }
            GUILayout.EndHorizontal();
        }
    }
}
