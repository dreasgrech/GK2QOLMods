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

        private static void DrawInstances()
        {
            Header("Donkeys and carts standing in the world");
            GUILayout.Label("Each is its own donkey (with or without its cart) or a cart on its own, with its own settings on top of everyone's. Walk up to a donkey with empty hands and press E to ride it; a dismount leaves it where it is, with its cart and cargo. Riding a donkey without its cart, ride up to a free cart's shafts and press E (or the hitch key) to take it; with the cart, the hitch key leaves it standing. Mods do the same through the API (Mounts.SpawnDonkey, SpawnCart, Hitch, Unhitch, MountHandle.Tuning). They are not saved: a cart's cargo is set down at it for each save, so nothing is lost on a reload.", small);
            Toggle("Controls in the game (the interact key rides and hitches, the label shows)", Plugin.InstanceControls, null);
            HitchKeyRow();

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
                string where = pd != null && pd.currentGameSceneId == i.SceneId
                    ? $"{Vector3.Distance(pd.position.Value, i.Position):F1} away"
                    : $"in {i.SceneId}";
                List<string> cargo = i.CargoRig.Ids();
                GUILayout.Label($"{i.Describe()}{(i.IsRidden ? " (ridden)" : "")}: {where}, facing {(i.FacingRight ? "right" : "left")}{(i.HasCart ? $", cargo [{string.Join(", ", cargo)}]" : "")}; {i.Overrides.Count} own setting(s); by {i.OwnerId}", editing == i ? instanceSet : label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(editing == i ? "Stop editing" : "Edit its settings", GUILayout.Width(120f)))
                {
                    editing = editing == i ? null : i;
                }
                GUI.enabled = i.HasDonkey && !i.IsRidden && !Mount.IsMounted && i.Ready;
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
                GUI.enabled = i.HasDonkey && !i.HasCart && cart != null;
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
