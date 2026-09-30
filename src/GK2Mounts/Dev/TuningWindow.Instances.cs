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

        // Over every donkey and cart in the keeper's scene while the Instances tab is open or one is edited: its number,
        // so the list and the world can be matched.
        private static GUIStyle markerStyle, markerEditStyle;

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
                string text = $"#{i.Id}{(ed ? " editing" : "")}{(i.IsStepping ? " ~" : "")}";
                GUIStyle st = ed ? markerEditStyle : markerStyle;
                Vector2 size = st.CalcSize(new GUIContent(text));
                float x = sp.x / scale - size.x / 2f;
                float y = (Screen.height - sp.y) / scale - size.y;
                GUI.Label(new Rect(x, y, size.x, size.y), text, st);
            }
        }

        private static void DrawInstances()
        {
            Header("Donkeys and carts standing in the world");
            GUILayout.Label("Each is its own donkey (with or without its cart) or a cart on its own, with its own settings on top of everyone's. Walk up to a donkey with empty hands and press E to ride it; a dismount leaves it where it is, with its cart and cargo. Riding a donkey without its cart, ride up to a free cart's shafts and press E (or the hitch key) to take it; with the cart, the hitch key leaves it standing. Mods do the same through the API (Mounts.SpawnDonkey, SpawnCart, Hitch, Unhitch, MountHandle.Tuning). They are not saved: a cart's cargo is set down at it for each save, so nothing is lost on a reload.", small);
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
            familyFoals = SessionIntRow("Foals in a family", familyFoals, 1, Mount.MaxChildren);
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

            List<Mount.Instance> list = new List<Mount.Instance>();
            foreach (Mount.Instance i in Mount.All)
            {
                if (!i.Ephemeral) list.Add(i);
            }
            Header($"In the world ({list.Count})");
            if (list.Count == 0)
            {
                GUILayout.Label("None yet.", small);
            }
            PlayerData pd = MainGame.Instance != null ? MainGame.PlayerData : null;
            foreach (Mount.Instance i in list)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                // Ridden: under the keeper (a distance to its root would jitter with the ride). Standing: to its body.
                string where = i.IsRidden ? "ridden by you"
                    : pd != null && pd.currentGameSceneId == i.SceneId
                    ? $"{Vector3.Distance(pd.position.Value, i.BodyPoint()):F1} away"
                    : $"in {i.SceneId}";
                List<string> cargo = i.CargoRig.Ids();
                string family = i.Parent != null ? $" foal of #{i.Parent.Id}, size {i.ViewScale:F2};"
                    : i.Children().Count > 0 ? $" parent of {string.Join(", ", i.Children().ConvertAll(c => "#" + c.Id))};" : "";
                GUILayout.Label($"{i.Describe()}:{family} {where}, facing {(i.FacingRight ? "right" : "left")}{(i.HasCart ? $", cargo [{string.Join(", ", cargo)}]" : "")}; {i.Overrides.Count} own setting(s); by {i.OwnerId}", editing == i ? instanceSet : label);
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
                if (GUILayout.Button(cart != null ? $"Hitch #{cart.Id}" : "Hitch", GUILayout.Width(90f)))
                {
                    pending = () =>
                    {
                        MountResult res = Mounts.Hitch(i.Handle, cart.Handle);
                        spawnNote = res.Ok ? $"{i.Describe()} took cart #{cart.Id}." : "Hitch refused: " + res.Reason;
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
                if (i.HasDonkey && i.Parent == null && i.Rideable)
                {
                    GUI.enabled = i.Children().Count < Mount.MaxChildren;
                    if (GUILayout.Button($"Add a foal (size {foalScale:F2})"))
                    {
                        pending = () =>
                        {
                            MountHandle f = Mounts.SpawnFoal(PanelOwner, i.Handle, foalScale);
                            spawnNote = f != null ? $"{f.I.Describe()} is a foal of {i.Describe()}." : "Could not add a foal (see the log).";
                        };
                    }
                    GUI.enabled = true;
                }
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
                GUILayout.EndVertical();
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
            MountHandle h = donkey ? Mounts.SpawnDonkey(PanelOwner, at, cart, right) : Mounts.SpawnCart(PanelOwner, at, right);
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
            MountHandle parent = Mounts.SpawnDonkey(PanelOwner, InFront(pd, 2.6f), false, pd.Direction.x >= 0f);
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
