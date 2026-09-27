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
        private static Rect rect = new Rect(0f, 0f, 360f, 10f);
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
            rect = GUILayout.Window(WindowId, rect, DrawContents, "GK2 Mounts tuning", GUILayout.Width(360f));
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
            GUILayout.Label(ride ? "Style: Ride" : "Style: Cart (change General.Style, re-mount)", small);

            GUILayout.Label("Movement", header);
            FloatSlider("Speed x", Plugin.SpeedMultiplier, 0.5f, 4f, 2, null);

            GUILayout.Label("Donkey placement", header);
            FloatSlider("Depth (- towards camera)", ride ? Plugin.RideDepth : Plugin.CartDepth, -0.5f, 0.5f, 2, null);
            FloatSlider("Centering", ride ? Plugin.RideCenterOffset : Plugin.CartCenterOffset, 0f, 1f, 2, null);

            if (ride)
            {
                GUILayout.Label("Rider", header);
                IntSlider("Fore/aft (+ towards head) px", Plugin.RiderOffsetPx, -16, 16, null);
                Toggle("Auto seat (hip on back line)", Plugin.AutoSeat, Mount.RefreshSeat);
                if (Plugin.AutoSeat.Value)
                {
                    IntSlider("Seat adjust px", Plugin.SeatAdjustPx, -16, 16, Mount.RefreshSeat);
                }
                else
                {
                    FloatSlider("Seat offset", Plugin.RideSeatOffset, -1f, 1f, 2, Mount.RefreshSeat);
                }
                EnumCycler("Pose", Plugin.Pose, Mount.ApplyPoseNow);
                if (Mount.PoseBlocksMovement)
                {
                    GUILayout.Label("This pose makes the game take control: the keeper cannot move while it plays. Choose another.", small);
                }

                GUILayout.Label("Legs", header);
                Toggle("Draw legs", Plugin.NearLeg, Mount.RefreshLeg);
                if (Plugin.NearLeg.Value)
                {
                    EnumCycler("Source", Plugin.LegSourceSetting, Mount.RefreshLeg);
                    if (Plugin.LegSourceSetting.Value == LegSource.Body)
                    {
                        IntSlider("Leg height (feet to belt) px", Plugin.LegHeightPx, 4, 20, Mount.RefreshLeg);
                    }
                    IntSlider("Leg X (+ rear) px", Plugin.LegAdjustXPx, -12, 12, null);
                    IntSlider("Leg Y (+ up) px", Plugin.LegAdjustYPx, -12, 12, null);
                }
            }

            GUILayout.Label("View", header);
            Toggle("Side view only (keeper faces left/right)", Plugin.SideViewOnly, null);

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save to cfg"))
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
            if (GUILayout.Button("Reset to defaults"))
            {
                ResetDefaults();
            }
            if (GUILayout.Button("Log values"))
            {
                Plugin.Log.LogInfo($"[tune] speed {Plugin.SpeedMultiplier.Value}, depth {Plugin.RideDepth.Value}, centering {Plugin.RideCenterOffset.Value}, rider offset {Plugin.RiderOffsetPx.Value} px, seat adjust {Plugin.SeatAdjustPx.Value} px, pose {Plugin.Pose.Value}, legs {Plugin.NearLeg.Value}/{Plugin.LegSourceSetting.Value} h {Plugin.LegHeightPx.Value} x {Plugin.LegAdjustXPx.Value} y {Plugin.LegAdjustYPx.Value}");
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Values apply live. Right-click a row to reset it to its default. Debug.TuningWindow=false hides this panel.", small);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        private static void FloatSlider(string name, ConfigEntry<float> entry, float min, float max, int decimals, Action onChange)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(210f));
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
            GUILayout.Label(name, label, GUILayout.Width(210f));
            if (GUILayout.Button("-", GUILayout.Width(22f)) && entry.Value > min) { entry.Value--; onChange?.Invoke(); }
            int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(entry.Value, min, max, GUILayout.Width(50f)));
            if (GUILayout.Button("+", GUILayout.Width(22f)) && entry.Value < max) { entry.Value++; onChange?.Invoke(); }
            GUILayout.Label(entry.Value.ToString(), label, GUILayout.Width(30f));
            GUILayout.EndHorizontal();
            if (ResetOnRightClick(entry, onChange)) { return; }
            if (v != entry.Value)
            {
                entry.Value = v;
                onChange?.Invoke();
            }
        }

        private static void Toggle(string name, ConfigEntry<bool> entry, Action onChange)
        {
            bool v = GUILayout.Toggle(entry.Value, " " + name, label);
            if (ResetOnRightClick(entry, onChange)) { return; }
            if (v != entry.Value)
            {
                entry.Value = v;
                onChange?.Invoke();
            }
        }

        private static void EnumCycler<T>(string name, ConfigEntry<T> entry, Action onChange) where T : struct, Enum
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            int idx = Array.IndexOf(values, entry.Value);
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(210f));
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
            Reset(Plugin.RiderOffsetPx); Reset(Plugin.AutoSeat); Reset(Plugin.SeatAdjustPx); Reset(Plugin.RideSeatOffset); Reset(Plugin.Pose);
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
