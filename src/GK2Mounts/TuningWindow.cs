using System;
using BepInEx.Configuration;
using UnityEngine;

namespace GK2Mounts
{
    // In-game tuning panel (Unity IMGUI), shown in the top-right corner while mounted when Debug.TuningWindow is on.
    // Tabs: General (behaviour), Riding (everything that places and animates rider and donkey, for the style
    // currently mounted), Debug. Every control edits the live config value and the mount reacts immediately;
    // "Save to config file" writes the cfg. Right-click on a row resets that value to its default.
    internal static class TuningWindow
    {
        private const int WindowId = 0x6d6e7431; // 'mnt1'
        private static Rect rect = new Rect(0f, 0f, 470f, 10f);
        private static GUIStyle label, header, small;
        private static bool stylesReady;
        private static int tab;
        private static string donkeyEvent = "";
        private static string cargoItemId = "wood";
        private static string spawnItemId = "wood";
        private static readonly string[] TabNames = { "General", "Riding", "Debug" };

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
            // The tab contents scroll when they do not fit the screen (the Riding tab is long); the toolbar and the
            // Save/Reset row stay in view. 130 covers the title bar, the toolbar and the footer.
            maxContentHeight = Mathf.Max(150f, Screen.height / scale - 32f - 130f);
            rect.x = vw - rect.width - 16f;
            rect.y = 16f;
            rect = GUILayout.Window(WindowId, rect, DrawContents, "GK2 Mounts tuning", GUILayout.Width(470f));
            GUI.matrix = Matrix4x4.identity;
        }

        private static Vector2 scroll;
        private static float maxContentHeight = 800f;
        private static readonly float[] contentHeight = new float[3]; // measured per tab, last repaint

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
            int newTab = GUILayout.Toolbar(tab, TabNames);
            if (newTab != tab)
            {
                tab = newTab;
                scroll = Vector2.zero;
            }
            GUILayout.Space(6f);
            // An IMGUI scroll view needs an explicit height, or it collapses; the content's height is measured on
            // each repaint (last frame's value is used) and capped to what fits on the screen.
            float h = contentHeight[tab] > 0f ? Mathf.Min(contentHeight[tab], maxContentHeight) : maxContentHeight;
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUILayout.Height(h));
            GUILayout.BeginVertical();
            switch (tab)
            {
                case 0: DrawGeneral(); break;
                case 1: DrawRiding(); break;
                default: DrawDebug(); break;
            }
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint)
            {
                contentHeight[tab] = GUILayoutUtility.GetLastRect().height + 6f;
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
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
            if (GUILayout.Button("Reset this style to defaults"))
            {
                Mount.S.ResetAll();
                Mount.RefreshLeg();
                Mount.RefreshSeat();
                Mount.RefreshSpeed();
                Mount.ApplyPoseNow();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Changes apply immediately. Right-click any row to put that value back to its default. Saving writes to anvil.gk2.mounts.cfg.", small);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        // ------------------------------------------------------------------ tabs

        private static void DrawGeneral()
        {
            GUILayout.Label("Controls", header);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mount / dismount key (change General.MountKey in the cfg)", label, GUILayout.Width(300f));
            GUILayout.Label(Plugin.MountKey.Value.ToString(), label);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Cart cargo key: load what you carry / take the last item (General.CargoKey)", label, GUILayout.Width(300f));
            GUILayout.Label(Plugin.CargoKey.Value.ToString(), label);
            GUILayout.EndHorizontal();

            GUILayout.Label("Behaviour", header);
            Toggle("Stay mounted when going through doors (scene changes)", Plugin.StayMountedThroughDoors, null);
            Toggle("Dismount when entering a building, church or dungeon (no mounting indoors)", Plugin.DismountIndoors, null);
            Toggle("Show a thought bubble when mounting is refused", Plugin.ShowMessages, null);
            Toggle("Keeper only faces left/right while riding", Plugin.SideViewOnly, null);
            if (!Plugin.SideViewOnly.Value && Mount.PoseNeedsSideView)
            {
                GUILayout.Label("Forced on: the " + Mount.S.Pose.Value + " pose only has left/right frames; facing up or down would show no body.", small);
            }
            GUILayout.Label("You always dismount for work, ladders, building, fishing, fighting, sleep and co-op; chests and inventory keep you mounted.", small);
        }

        private static void DrawRiding()
        {
            StyleSettings s = Mount.S;
            bool ride = Mount.CurrentStyle == MountStyle.Ride;

            GUILayout.Label("Style being tuned: " + (ride ? "riding on the donkey's back" : "the donkey pulling its cart") + "  [" + s.Section + "]", header);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Switch style (the donkey is summoned again)", label, GUILayout.Width(300f));
            if (GUILayout.Button(ride ? "Go to cart" : "Go to riding", GUILayout.Width(150f)))
            {
                Plugin.Style.Value = ride ? MountStyle.Cart : MountStyle.Ride;
                Mount.Remount();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Each style keeps its own values below; they are saved under [" + s.Section + "] in the cfg.", small);

            GUILayout.Label("Speed", header);
            FloatSlider("Speed (x normal walking)", s.SpeedMultiplier, 0.5f, 4f, 2, Mount.RefreshSpeed);

            GUILayout.Label("Donkey position", header);
            FloatSlider("Donkey in front of (-) / behind (+) the rider", s.Depth, -0.5f, 0.5f, 2, null);
            CenteringToggle(s.CenterOffset);
            Toggle("Donkey follows the ground height at its own position (slopes)", s.SnapToGround, null);
            Toggle("Cart follows the ground under the cart (slopes; cart only)", s.CartFollowsGround, null);
            if (s.CartFollowsGround.Value)
            {
                FloatSlider("Cart: largest height shift allowed", s.CartGroundMaxShift, 0f, 1.5f, 2, null);
                GUILayout.Label("Wheels, cart body and lantern move up or down together by the ground difference between cart and donkey; the bars between them take half of it.", small);
            }
            if (Mount.CurrentStyle == MountStyle.Cart)
            {
                GUILayout.Label("Cart cargo (proof of concept)", header);
                Toggle("Place items by their measured size (bottom on the bed, footprint on the slot; stack when too wide)", s.CargoAutoFit, Cargo.RefreshViews);
                Toggle("Mod draws corpses too (sliders move them; off = the game's own shroud for the first corpse)", s.CargoDrawCorpse, Cargo.RefreshViews);
                GUILayout.Label("With the corpse toggle off, the first corpse is the game's loaded-cart mesh, which the sliders cannot move; other items then ride on top of the shroud.", small);
                GUILayout.Label("First item on the cart", small);
                FloatSlider("Slot 1: along the cart (tail - / head +)", s.Cargo1Along, -0.6f, 0.6f, 2, Cargo.RefreshViews);
                FloatSlider("Slot 1: height above the bed", s.Cargo1Up, -0.5f, 0.8f, 2, Cargo.RefreshViews);
                FloatSlider("Slot 1: across the cart (towards camera - / away +)", s.Cargo1Across, -0.5f, 0.5f, 2, Cargo.RefreshViews);
                GUILayout.Label("Second item on the cart", small);
                FloatSlider("Slot 2: along the cart (tail - / head +)", s.Cargo2Along, -0.6f, 0.6f, 2, Cargo.RefreshViews);
                FloatSlider("Slot 2: height above the bed", s.Cargo2Up, -0.5f, 0.8f, 2, Cargo.RefreshViews);
                FloatSlider("Slot 2: across the cart (towards camera - / away +)", s.Cargo2Across, -0.5f, 0.5f, 2, Cargo.RefreshViews);
                GUILayout.Label($"Press {Plugin.CargoKey.Value} with a log (or stone, ore, crate) on your head to put it on the cart, and with empty hands to take it back. A corpse is drawn by the game itself; other items by a copy of the keeper's overhead mesh. Right-click a row to reset it; 'Export current values as mod defaults' (Debug tab) keeps them. {Cargo.Summary()}", small);
            }
            IntSlider("Move whole rider along the donkey: tail (-) / head (+)", s.RiderOffsetPx, -16, 16, null);

            GUILayout.Label("Rider torso", header);
            IntSlider("Move torso only along the donkey: tail (-) / head (+)", s.TorsoOffsetPx, -16, 16, Mount.RefreshSeat);
            Toggle("Torso height automatic (hip on the donkey's back)", s.AutoSeat, Mount.RefreshSeat);
            if (s.AutoSeat.Value)
            {
                IntSlider("Move torso down (-) / up (+), pixels", s.SeatAdjustPx, -16, 16, Mount.RefreshSeat);
            }
            else
            {
                FloatSlider("Torso height (0 = standing on the ground)", s.SeatOffset, -1f, 1f, 2, Mount.RefreshSeat);
            }
            Toggle("Torso bobs with the donkey's walk", s.Bob, Mount.RefreshSeat);

            GUILayout.Label("Rider pose and animation", header);
            EnumCycler("Body pose", s.Pose, Mount.ApplyPoseNow);
            if (Mount.PoseBlocksMovement)
            {
                GUILayout.Label("This pose makes the game take control: the keeper cannot move while it plays. Choose another.", small);
            }
            Toggle("Freeze the pose while standing still (arms stop moving)", s.FreezePoseWhenIdle, null);
            FloatSlider("Pose animation speed while moving (x clip speed)", s.PoseSpeed, 0.1f, 4f, 2, null);
            Toggle("Scale pose animation speed with the riding speed", s.PoseSpeedFollowsDonkey, null);

            GUILayout.Label("Rider legs (drawn on the donkey's side)", header);
            Toggle("Show legs on the donkey's side", s.NearLeg, Mount.RefreshLeg);
            if (s.NearLeg.Value)
            {
                EnumCycler("Legs taken from", s.LegSource, Mount.RefreshLeg);
                if (s.LegSource.Value == LegSource.Body)
                {
                    IntSlider("How much body counts as legs (px up from feet)", s.LegHeightPx, 4, 20, Mount.RefreshLeg);
                }
                IntSlider("Move legs along the donkey: head (-) / tail (+)", s.LegAdjustXPx, -12, 12, null);
                IntSlider("Move legs down (-) / up (+), pixels", s.LegAdjustYPx, -12, 12, null);
            }
            else
            {
                GUILayout.Label("Off: the keeper's own legs show, hidden wherever the donkey is drawn in front of them.", small);
            }
        }

        private static void DrawDebug()
        {
            GUILayout.Label("This panel", header);
            FloatSlider("Panel size", Plugin.TuningWindowScale, 0.75f, 3f, 2, null);
            Toggle("Tuning keys (PageUp/Down torso, Home/End legs sideways, Insert/Delete legs up/down)", Plugin.TuningKeys, null);
            GUILayout.Label("Debug.TuningWindow=false in the cfg hides this panel for normal play.", small);

            GUILayout.Label("Diagnostics", header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Write values to log"))
            {
                StyleSettings s = Mount.S;
                Plugin.Log.LogInfo($"[tune] [{s.Section}] speed {s.SpeedMultiplier.Value}, depth {s.Depth.Value}, centering {s.CenterOffset.Value}, rider {s.RiderOffsetPx.Value} px, torso {s.TorsoOffsetPx.Value} px, autoSeat {s.AutoSeat.Value}, seat adjust {s.SeatAdjustPx.Value} px, seat offset {s.SeatOffset.Value}, bob {s.Bob.Value}, freeze {s.FreezePoseWhenIdle.Value}, pose {s.Pose.Value}, legs {s.NearLeg.Value}/{s.LegSource.Value} h {s.LegHeightPx.Value} x {s.LegAdjustXPx.Value} y {s.LegAdjustYPx.Value} | general: sideView {Plugin.SideViewOnly.Value}, doors {Plugin.StayMountedThroughDoors.Value}");
            }
            if (GUILayout.Button("Write geometry to log"))
            {
                Mount.LogGeometry();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Both write one line to BepInEx/LogOutput.log. Geometry: press once per facing (and once while walking) when reporting a placement problem.", small);

            GUILayout.Label("Game donkey (for comparison)", header);
            if (GUILayout.Button("Start a real delivery now (the game's own cart brings a body)"))
            {
                Plugin.Log.LogInfo("[script] " + ScriptDump.StartDelivery());
            }
            GUILayout.Label("Fires the donkey script's 'full_cycle' event with its body roll forced to pass: the cart appears at the road point donkey_01 and drives to the morgue point donkey_05. Nothing happens while global_lock_on_drop_bodies is set or while the quest 33_base_complain_go is in progress (then it walks to the morgue instead); the log line says which.", small);
            if (GUILayout.Button("Dump the game donkey's script (npc_donkey) to a file"))
            {
                Plugin.Log.LogInfo("[script] " + ScriptDump.DumpWgoScript("npc_donkey"));
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Send event to the real donkey's script", label, GUILayout.Width(300f));
            donkeyEvent = GUILayout.TextField(donkeyEvent, GUILayout.Width(110f));
            if (GUILayout.Button("Send", GUILayout.Width(50f)))
            {
                Plugin.Log.LogInfo("[script] " + ScriptDump.FireEventOnWgo("npc_donkey", donkeyEvent));
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Other events from the dumped graph: move_to_morgue, full_cycle_back, move_from_morgue, stay_morgue, Disable, SetInteractive, SetNonInteractive.", small);

            GUILayout.Label("Cart cargo (investigation)", header);
            if (GUILayout.Button("Dump carry facts to a file (drawer rules, inventory filters, big items)"))
            {
                Plugin.Log.LogInfo("[cargo] " + CarryProbe.DumpFacts());
            }
            GUILayout.Label("Writes BepInEx/GK2-carry-facts.txt: the donkey prefab's ConditionalDrawer rules (what switches the loaded-cart mesh), the npc_donkey definition's inventory size and item filters, every item with itemSize Big, and the mesh elements the keeper's overhead view can show. Mount first for the drawer rules.", small);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Test item id", label, GUILayout.Width(100f));
            cargoItemId = GUILayout.TextField(cargoItemId, GUILayout.Width(110f));
            if (GUILayout.Button("Put in cart inventory", GUILayout.Width(170f)))
            {
                Plugin.Log.LogInfo("[cargo] " + CarryProbe.TestLoad(cargoItemId));
            }
            if (GUILayout.Button("Take first out", GUILayout.Width(120f)))
            {
                Plugin.Log.LogInfo("[cargo] " + CarryProbe.TestUnload());
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Adds a plain item to our donkey's inventory exactly as the delivery script does, and logs whether the game's drawer switched the cart meshes. Test items vanish; the donkey is a temp object, nothing is saved.", small);

            GUILayout.Label("Spawn a carryable item in front of the keeper (test)", header);
            GUILayout.BeginHorizontal();
            System.Collections.Generic.List<string> big = CarryProbe.BigItemIds();
            int at = big.IndexOf(spawnItemId);
            if (GUILayout.Button("<", GUILayout.Width(28f)) && big.Count > 0)
            {
                spawnItemId = big[(at <= 0 ? big.Count : at) - 1];
            }
            spawnItemId = GUILayout.TextField(spawnItemId, GUILayout.Width(200f));
            if (GUILayout.Button(">", GUILayout.Width(28f)) && big.Count > 0)
            {
                spawnItemId = big[(at + 1) % big.Count];
            }
            if (GUILayout.Button("Drop it here", GUILayout.Width(120f)))
            {
                Plugin.Log.LogInfo("[cargo] " + CarryProbe.SpawnDrop(spawnItemId));
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Uses the game's own drop routine at the keeper's drop position, so the item is a normal world drop (it is saved like any other; pick it up with the interaction key). < and > cycle through the {big.Count} big items the game defines (zombies excluded, they are linked world objects). A corpse is generated from the delivery script's body definition.", small);

            GUILayout.Label("Mod defaults", header);
            if (GUILayout.Button("Export current values as mod defaults"))
            {
                try
                {
                    string path = Plugin.DefaultsExportPath.Value;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "GK2Mounts.defaults.cfg");
                    }
                    Plugin.Log.LogInfo("Mod defaults exported to " + Defaults.Export(path) + " (rebuild the mod to embed them).");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Defaults export failed: " + e.Message);
                }
            }
            GUILayout.Label("Writes both styles' values (and the General look settings) to defaults.cfg, the file the build embeds as the mod's defaults: no code edits needed. Path: Debug.DefaultsExportPath (empty = BepInEx/config).", small);
        }

        // ------------------------------------------------------------------ controls

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
    }
}
