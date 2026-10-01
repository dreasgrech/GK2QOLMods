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
                if (GUILayout.Button("Stop", GUILayout.Width(60f)))
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
            if (tab != 5 && !EditingNow)
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

        private static void DrawInstances()
        {
            Header("Donkeys and carts standing in the world");
            GUILayout.Label("Each is its own donkey (with or without its cart) or a cart on its own, with its own settings on top of everyone's. Walk up to a donkey with empty hands and press E to ride it; a dismount leaves it where it is, with its cart and cargo. Riding a donkey without its cart, ride up to a free cart's shafts and press E (or the hitch key) to take it; with the cart, the hitch key leaves it standing. Mods do the same through the API (Mounts.SpawnDonkey, SpawnCart, Hitch, Unhitch, MountHandle.Tuning). The ones marked kept with saves (the panel's spawns are) come back when that save is loaded, with their cargo and own settings; the game's save itself is never changed (a side file next to it). The others are gone after a reload, with their cargo on the ground where they stood.", small);
            GUILayout.Label("Saves: " + Mount.PersistenceNote, small);
            Toggle("Controls in the game (the interact key rides and hitches, the label shows)", Plugin.InstanceControls, null);
            HitchKeyRow();

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

            // Removed instances out of the panel's own view state (it held on to them: a small leak, 2026-10-01).
            expanded.RemoveWhere(x => !Mount.IsRegistered(x));
            // Grouped by the mod that owns them (each numbers its own: #1 of one mod and #1 of another are two donkeys),
            // with that mod's dormant records (kept with the save, its mod not installed) at the end of its group.
            List<Mount.Instance> list = new List<Mount.Instance>();
            var owners = new List<string>();
            foreach (Mount.Instance i in Mount.All)
            {
                if (i.Ephemeral) continue;
                list.Add(i);
                if (!owners.Contains(i.OwnerId)) owners.Add(i.OwnerId);
            }
            foreach (Mount.DormantRecord d in Mount.Dormant)
            {
                if (!owners.Contains(d.Owner)) owners.Add(d.Owner);
            }
            Header($"In the world ({list.Count}{(Mount.Dormant.Count > 0 ? $", {Mount.Dormant.Count} dormant" : "")}), by mod");
            if (owners.Count == 0)
            {
                GUILayout.Label("None yet.", small);
            }
            PlayerData pd = MainGame.Instance != null ? MainGame.PlayerData : null;
            foreach (string owner in owners)
            {
                List<Mount.Instance> mine = list.FindAll(x => x.OwnerId == owner);
                List<Mount.DormantRecord> sleeping = new List<Mount.DormantRecord>();
                foreach (Mount.DormantRecord d in Mount.Dormant)
                {
                    if (d.Owner == owner) sleeping.Add(d);
                }
                int keptCount = mine.FindAll(x => x.Persist).Count;
                // A click on the header folds the group. Many rows of IMGUI controls are expensive (63 donkeys
                // with all their buttons took 27 ms a frame, measured 2026-10-01), so a big group starts folded.
                bool open = groupOpen.TryGetValue(owner, out bool o) ? o : mine.Count + sleeping.Count <= 6;
                string ownerKey = owner;
                if (GUILayout.Button($"{(open ? "[-]" : "[+]")} {Mount.OwnerName(owner)} ({owner}){(Mount.OwnerPresent(owner) ? "" : ", NOT INSTALLED")}: {mine.Count} in the world, {keptCount} kept with saves{(sleeping.Count > 0 ? $", {sleeping.Count} dormant" : "")}",
                    OwnerStyle(ownerHeaderStyles, label, owner)))
                {
                    bool now = open;
                    pending = () => groupOpen[ownerKey] = !now;
                }
                foreach (Mount.DormantRecord d in open ? sleeping : new List<Mount.DormantRecord>())
                {
                    // The text above its button, not beside it: a long wrapped label beside a button pushes the
                    // button off the panel's right edge (seen 2026-10-01).
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label($"#{d.Id} {d.Kind}{(d.Key != null ? $" '{d.Key}'" : "")}: dormant ({d.Why}), in {d.Scene}{(d.Cargo > 0 ? $", {d.Cargo} cargo item(s) on the ground there" : "")}. Back with its mod; Forget drops it at the next save.", small);
                    Mount.DormantRecord forget = d;
                    if (GUILayout.Button("Forget"))
                    {
                        pending = () => Mount.ForgetDormant(forget);
                    }
                    GUILayout.EndVertical();
                }
                if (!open)
                {
                    continue;
                }
                foreach (Mount.Instance i in mine)
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
            GUILayout.Label(name, label, GUILayout.Width(LabelW));
            if (GUILayout.Button("-", GUILayout.Width(22f)) && value > min) value--;
            GUILayout.Label(value.ToString(), label, GUILayout.Width(30f));
            if (GUILayout.Button("+", GUILayout.Width(22f)) && value < max) value++;
            GUILayout.EndHorizontal();
            return value;
        }

        private static float SessionFloatRow(string name, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(LabelW));
            value = (float)Math.Round(GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(90f)), 2);
            GUILayout.Label(value.ToString("F2"), label, GUILayout.Width(45f));
            GUILayout.EndHorizontal();
            return value;
        }

        private static Vector3 InFront(PlayerData pd, float distance)
        {
            Vector2 d = pd.Direction.sqrMagnitude > 0.01f ? pd.Direction.normalized : Vector2.right;
            return pd.position.Value + new Vector3(d.x, 0f, d.y) * distance;
        }

        // The free cart nearest to a donkey in its scene (for the panel's hitch button: any distance).
        // The panel's own view state (this session): which mods' groups are open, which rows show everything.
        private static readonly Dictionary<string, bool> groupOpen = new Dictionary<string, bool>();
        private static readonly HashSet<Mount.Instance> expanded = new HashSet<Mount.Instance>();

        // One line per donkey: what it is, where, and whether it is kept; Details shows everything else.
        private static void DrawInstanceCompact(Mount.Instance i, PlayerData pd)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            string where = i.IsRidden ? "ridden by you"
                : pd != null && pd.currentGameSceneId == i.SceneId
                ? $"{Vector3.Distance(pd.position.Value, i.BodyPoint()):F1} away"
                : $"in {i.SceneId}";
            string family = i.Parent != null ? $", foal of {Ref(i, i.Parent)}" : "";
            string level = i.tier == Mount.Tier.Idle ? ", idle (off screen)" : i.tier == Mount.Tier.Asleep ? ", asleep (far)" : "";
            GUILayout.Label($"#{i.Id} {i.KindName}{(i.Key != null ? $" '{i.Key}'" : "")}{family}, {where}{level}{(i.IsStepping ? ", stepping" : "")}{(i.Persist ? "" : ", not kept")}", label);
            if (GUILayout.Button("Details", GUILayout.Width(80f)))
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
            if (GUILayout.Button(editing == i ? "Stop editing" : "Edit its settings", GUILayout.Width(120f)))
            {
                editing = editing == i ? null : i;
            }
            GUI.enabled = i.HasDonkey && i.Rideable && !i.IsRidden && !Mount.IsMounted && i.Ready;
            if (GUILayout.Button("Ride", GUILayout.Width(60f)))
            {
                pending = () => i.Handle.Ride();
            }
            GUI.enabled = !i.IsRidden;
            if (GUILayout.Button("Move here", GUILayout.Width(90f)) && pd != null)
            {
                pending = () => i.Handle.MoveTo(InFront(pd, 1.8f), pd.Direction.x >= 0f);
            }
            GUI.enabled = i.HasDonkey && i.HasCart && i.Ready;
            if (GUILayout.Button("Unhitch", GUILayout.Width(80f)))
            {
                pending = () => Mounts.Unhitch(i.Handle);
            }
            Mount.Instance cart = NearestFreeCart(i);
            GUI.enabled = i.HasDonkey && i.Rideable && !i.HasCart && cart != null;
            if (GUILayout.Button(cart != null ? $"Hitch {Ref(i, cart)}" : "Hitch", GUILayout.Width(110f)))
            {
                pending = () =>
                {
                    MountResult res = Mounts.Hitch(i.Handle, cart.Handle);
                    spawnNote = res.Ok ? $"{i.Describe()} took cart {cart.FullId}." : "Hitch refused: " + res.Reason;
                };
            }
            GUI.enabled = true;
            if (GUILayout.Button("Remove", GUILayout.Width(80f)))
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
            if (GUILayout.Button("Fewer details", GUILayout.Width(120f)))
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
            GUILayout.Label("Hitch key (General.HitchKey)", label, GUILayout.Width(LabelW));
            if (GUILayout.Button(Plugin.HitchKey.Value.ToString(), GUILayout.Width(90f)))
            {
                Plugin.HitchKey.Value = Plugin.HitchKey.Value == KeyCode.None ? KeyCode.J : KeyCode.None;
            }
            GUILayout.EndHorizontal();
        }
    }
}
