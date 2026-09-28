using System;
using BepInEx.Configuration;
using UnityEngine;

namespace GK2Mounts
{
    // In-game tuning panel (Unity IMGUI), shown in the top-right corner while mounted when Debug.TuningWindow is on.
    // Every control edits the live config value, and the mount reacts immediately; "Save" writes the cfg file.
    internal static class TuningWindow
    {
        private const int WindowId = 0x6d6e7431; // 'mnt1'
        private static Rect rect = new Rect(0f, 0f, 470f, 10f);
        private static GUIStyle label, header, small;
        private static bool stylesReady;

        public static void Draw()
        {
            if (!Mount.IsMounted || !Plugin.TuningWindowEnabled.Value)
            {
                return;
            }
            float scale = Mathf.Clamp(Plugin.TuningWindowScale.Value, 0.75f, 3f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            EnsureStyles();
            float vw = Screen.width / scale;
            rect.x = vw - rect.width - 16f;
            rect.y = 16f;
            rect = GUILayout.Window(WindowId, rect, DrawContents, "GK2 Mounts tuning", GUILayout.Width(470f));
            GUI.matrix = Matrix4x4.identity;
        }

        private static void EnsureStyles()
        {
            if (stylesReady)
            {
                return;
            }
            label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            small.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            header = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            stylesReady = true;
        }

        private static void DrawContents(int id)
        {
            bool ride = Mount.CurrentStyle == MountStyle.Ride;

            GUILayout.Label("Mount", header);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Style (click to switch; the donkey is summoned again)", label, GUILayout.Width(300f));
            if (GUILayout.Button(ride ? "Riding on its back" : "Pulling the cart", GUILayout.Width(150f)))
            {
                Plugin.Style.Value = ride ? MountStyle.Cart : MountStyle.Ride;
                Mount.Remount();
            }
            GUILayout.EndHorizontal();
            FloatSlider("Riding speed (× normal walking)", Plugin.SpeedMultiplier, 0.5f, 4f, 2, null);

            GUILayout.Label("Donkey position", header);
            FloatSlider("Donkey in front of (−) / behind (+) the rider", ride ? Plugin.RideDepth : Plugin.CartDepth, -0.5f, 0.5f, 2, null);
            CenteringToggle(ride ? Plugin.RideCenterOffset : Plugin.CartCenterOffset);
            if (ride)
            {
                IntSlider("Move whole rider along the donkey: tail (−) / head (+)", Plugin.RiderOffsetPx, -16, 16, null);
            }

            if (ride)
            {
                GUILayout.Label("Rider torso", header);
                IntSlider("Move torso only along the donkey: tail (−) / head (+)", Plugin.TorsoOffsetPx, -16, 16, Mount.RefreshSeat);
                Toggle("Torso height automatic (hip on the donkey's back)", Plugin.AutoSeat, Mount.RefreshSeat);
                if (Plugin.AutoSeat.Value)
                {
                    IntSlider("Move torso down (−) / up (+), pixels", Plugin.SeatAdjustPx, -16, 16, Mount.RefreshSeat);
                }
                else
                {
                    FloatSlider("Torso height (0 = standing on the ground)", Plugin.RideSeatOffset, -1f, 1f, 2, Mount.RefreshSeat);
                }
                Toggle("Torso bobs with the donkey's walk", Plugin.Bob, Mount.RefreshSeat);

                GUILayout.Label("Rider pose and facing", header);
                EnumCycler("Body pose while riding", Plugin.Pose, Mount.ApplyPoseNow);
                if (Mount.PoseBlocksMovement)
                {
                    GUILayout.Label("This pose makes the game take control: the keeper cannot move while it plays. Choose another.", small);
                }
                Toggle("Keeper only faces left/right while riding", Plugin.SideViewOnly, null);
                if (!Plugin.SideViewOnly.Value && Mount.PoseNeedsSideView)
                {
                    GUILayout.Label("Forced on: the " + Plugin.Pose.Value + " pose only has left/right frames; facing up or down would show no body.", small);
                }

                GUILayout.Label("Rider legs (drawn on the donkey's side)", header);
                Toggle("Show legs on the donkey's side", Plugin.NearLeg, Mount.RefreshLeg);
                if (Plugin.NearLeg.Value)
                {
                    EnumCycler("Legs taken from", Plugin.LegSourceSetting, Mount.RefreshLeg);
                    if (Plugin.LegSourceSetting.Value == LegSource.Body)
                    {
                        IntSlider("How much body counts as legs (px up from feet)", Plugin.LegHeightPx, 4, 20, Mount.RefreshLeg);
                    }
                    IntSlider("Move legs along the donkey: head (−) / tail (+)", Plugin.LegAdjustXPx, -12, 12, null);
                    IntSlider("Move legs down (−) / up (+), pixels", Plugin.LegAdjustYPx, -12, 12, null);
                }
            }
            else
            {
                GUILayout.Label("Rider pose and facing", header);
                Toggle("Keeper only faces left/right while riding", Plugin.SideViewOnly, null);
            }

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save to config file"))
            {
                try
                {
                    Plugin.Instance.Config.Save();
                    Plugin.Log.LogInfo("Mount tuning saved to config.");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Config save failed: " + e.Message);
                }
            }
            if (GUILayout.Button("Reset all to defaults"))
            {
                ResetDefaults();
            }
            if (GUILayout.Button("Write values to log"))
            {
                Plugin.Log.LogInfo($"[tune] style {Plugin.Style.Value}, speed {Plugin.SpeedMultiplier.Value}, depth {Plugin.RideDepth.Value}, centering {Plugin.RideCenterOffset.Value}, rider {Plugin.RiderOffsetPx.Value} px, torso {Plugin.TorsoOffsetPx.Value} px, autoSeat {Plugin.AutoSeat.Value}, seat adjust {Plugin.SeatAdjustPx.Value} px, seat offset {Plugin.RideSeatOffset.Value}, bob {Plugin.Bob.Value}, pose {Plugin.Pose.Value}, legs {Plugin.NearLeg.Value}/{Plugin.LegSourceSetting.Value} h {Plugin.LegHeightPx.Value} x {Plugin.LegAdjustXPx.Value} y {Plugin.LegAdjustYPx.Value}, sideView {Plugin.SideViewOnly.Value}");
            }
            if (GUILayout.Button("Write geometry to log"))
            {
                Mount.LogGeometry();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Changes apply immediately. Right-click any row to put that value back to its default. Saving writes to anvil.gk2.mounts.cfg. Debug.TuningWindow=false hides this panel.", small);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        // CenterOffset is a switch in practice: any value above 0 means "cancel the sprite's built-in sideways offset
        // so the donkey sits under the rider" (the exact offset is measured from the sprite each frame); 0 leaves
        // the donkey where the game draws it. Shown as a toggle; the stored value stays a float for the cfg.
        private static void CenteringToggle(ConfigEntry<float> entry)
        {
            bool on = entry.Value > 0.0001f;
            bool clicked = OnOffRow("Keep the donkey centred under the rider", on);
            if (ResetOnRightClick(entry, null)) { return; }
            if (clicked)
            {
                entry.Value = on ? 0f : 0.48f;
            }
        }

        private static void FloatSlider(string name, ConfigEntry<float> entry, float min, float max, int decimals, Action onChange)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(300f));
            float v = GUILayout.HorizontalSlider(entry.Value, min, max, GUILayout.Width(90f));
            v = (float)Math.Round(v, decimals);
            GUILayout.Label(v.ToString("F" + decimals), label, GUILayout.Width(45f));
            GUILayout.EndHorizontal();
            if (ResetOnRightClick(entry, onChange)) { return; }
            if (Mathf.Abs(v - entry.Value) > 0.00001f)
            {
                entry.Value = v;
                onChange?.Invoke();
            }
        }

        private static void IntSlider(string name, ConfigEntry<int> entry, int min, int max, Action onChange)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(300f));
            // The slider is laid out between the buttons and reads the value before "+" runs; a button press must
            // therefore win over the slider's (stale) value in the same pass.
            bool pressed = false;
            if (GUILayout.Button("-", GUILayout.Width(22f)) && entry.Value > min) { entry.Value--; pressed = true; onChange?.Invoke(); }
            int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(entry.Value, min, max, GUILayout.Width(50f)));
            if (GUILayout.Button("+", GUILayout.Width(22f)) && entry.Value < max) { entry.Value++; pressed = true; onChange?.Invoke(); }
            GUILayout.Label(entry.Value.ToString(), label, GUILayout.Width(30f));
            GUILayout.EndHorizontal();
            if (ResetOnRightClick(entry, onChange)) { return; }
            if (!pressed && v != entry.Value)
            {
                entry.Value = v;
                onChange?.Invoke();
            }
        }

        // Booleans as a labelled On/Off button in the value column (a label-styled GUILayout.Toggle draws no box).
        private static bool OnOffRow(string name, bool value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(300f));
            bool clicked = GUILayout.Button(value ? "On" : "Off", GUILayout.Width(70f));
            GUILayout.Label(value ? "" : "", small, GUILayout.Width(10f));
            GUILayout.EndHorizontal();
            return clicked;
        }

        private static void Toggle(string name, ConfigEntry<bool> entry, Action onChange)
        {
            bool clicked = OnOffRow(name, entry.Value);
            if (ResetOnRightClick(entry, onChange)) { return; }
            if (clicked)
            {
                entry.Value = !entry.Value;
                onChange?.Invoke();
            }
        }

        private static void EnumCycler<T>(string name, ConfigEntry<T> entry, Action onChange) where T : struct, Enum
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            int idx = Array.IndexOf(values, entry.Value);
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(300f));
            bool changed = false;
            if (GUILayout.Button("<", GUILayout.Width(22f))) { idx = (idx - 1 + values.Length) % values.Length; changed = true; }
            GUILayout.Label(entry.Value.ToString(), label, GUILayout.Width(90f));
            if (GUILayout.Button(">", GUILayout.Width(22f))) { idx = (idx + 1) % values.Length; changed = true; }
            GUILayout.EndHorizontal();
            if (ResetOnRightClick(entry, onChange)) { return; }
            if (changed)
            {
                entry.Value = values[idx];
                onChange?.Invoke();
            }
        }

        // Right-clicking a control row puts that setting back to its default. Called right after the row is laid out,
        // so GetLastRect is the row's rectangle.
        private static bool ResetOnRightClick<T>(ConfigEntry<T> entry, Action onChange)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.MouseDown || e.button != 1)
            {
                return false;
            }
            if (!GUILayoutUtility.GetLastRect().Contains(e.mousePosition))
            {
                return false;
            }
            e.Use();
            T def = (T)entry.DefaultValue;
            if (!Equals(entry.Value, def))
            {
                entry.Value = def;
                onChange?.Invoke();
            }
            return true;
        }

        private static void ResetDefaults()
        {
            Reset(Plugin.SpeedMultiplier); Reset(Plugin.RideDepth); Reset(Plugin.RideCenterOffset); Reset(Plugin.CartDepth); Reset(Plugin.CartCenterOffset);
            Reset(Plugin.RiderOffsetPx); Reset(Plugin.TorsoOffsetPx); Reset(Plugin.AutoSeat); Reset(Plugin.Bob); Reset(Plugin.SeatAdjustPx); Reset(Plugin.RideSeatOffset); Reset(Plugin.Pose);
            Reset(Plugin.NearLeg); Reset(Plugin.LegSourceSetting); Reset(Plugin.LegHeightPx); Reset(Plugin.LegAdjustXPx); Reset(Plugin.LegAdjustYPx); Reset(Plugin.SideViewOnly);
            Mount.RefreshLeg();
            Mount.RefreshSeat();
            Mount.ApplyPoseNow();
        }

        private static void Reset<T>(ConfigEntry<T> e)
        {
            e.Value = (T)e.DefaultValue;
        }
    }
}
