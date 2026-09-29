using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace GK2Mounts
{
    // In-game tuning panel (Unity IMGUI), shown in the top-right corner while mounted when Debug.TuningWindow is on.
    // Tabs: General (behaviour), Riding (everything that places and animates rider and donkey, for the style
    // currently mounted), Lantern, Sounds, Debug, Defaults. Every control edits the live config value and the mount
    // reacts immediately; BepInEx saves the cfg on every change. A row whose value differs from its mod default is
    // yellow, and right-click puts it back to that default (see Defaults.cs for yours against the mod defaults).
    internal static class TuningWindow
    {
        private const int WindowId = 0x6d6e7431; // 'mnt1'
        private static Rect rect = new Rect(0f, 0f, 470f, 10f);
        private static GUIStyle label, header, small, changed, wrapToggle;

        // Yellow when the setting differs from its mod default.
        private static GUIStyle LabelFor(ConfigEntryBase e)
        {
            return e != null && Defaults.IsTracked(e) && Defaults.Differs(e) ? changed : label;
        }
        private static bool stylesReady;
        private static int tab;
        private static string donkeyEvent = "";
        private static string cargoItemId = "wood";
        private static string spawnItemId = "wood";
        private static bool pairOffsetsOpen;
        private static readonly string[] TabNames = { "General", "Riding", "Lantern", "Sounds", "Debug", "Defaults" };

        public static void Draw()
        {
            CargoLayout.SaveIfDue(now: !Mount.IsMounted);
            if (!Mount.IsMounted || !Plugin.TuningWindowEnabled.Value || CargoSheet.Running)
            {
                return; // hidden while the cargo sheet photographs the cart
            }
            float scale = Mathf.Clamp(Plugin.TuningWindowScale.Value, 0.75f, 3f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            EnsureStyles();
            float vw = Screen.width / scale;
            // The tab contents scroll when they do not fit the screen (the Riding tab is long); the toolbar and the
            // Save/Reset row stay in view. The chrome (title bar, toolbar, footer) is measured from the last frame:
            // window height minus the scroll view's height, so wrapped footer text at any font or width is counted.
            maxContentHeight = Mathf.Max(150f, Screen.height / scale - 32f - chromeHeight);
            rect.x = vw - W - 16f;
            rect.y = 16f;
            rect.width = W;
            rect.height = 0f; // a layout window grows to its content but never shrinks unless the height is reset
            rect = GUILayout.Window(WindowId, rect, DrawContents, "GK2 Mounts tuning", GUILayout.Width(W));
            if (lastScrollHeight > 0f && rect.height > lastScrollHeight)
            {
                chromeHeight = Mathf.Clamp(rect.height - lastScrollHeight, 60f, 600f);
            }
            GUI.matrix = Matrix4x4.identity;
        }

        private static Vector2 scroll;
        private static float maxContentHeight = 800f;
        private static float chromeHeight = 130f;   // measured; 130 is the first-frame guess
        private static float lastScrollHeight;
        private static readonly float[] contentHeight = new float[6]; // measured per tab, last repaint

        // Panel width in panel units (before the overall scale) and the widths derived from it, so rows shrink and
        // grow with the panel instead of overflowing it.
        private static float W => Mathf.Clamp(Plugin.TuningWindowWidth.Value, 300f, 900f);
        private static float LabelW => W * 0.62f;
        private static float SliderW => W * 0.30f;

        // The width inside the scroll view: the panel minus the window's padding and the vertical scrollbar. The tab
        // contents are held to it. An IMGUI scroll view gives its content unlimited width, and a wrapping label wraps
        // at the width of its column, which is set by the widest control in it; without this cap one wide row
        // widens the column and every sentence then runs past the panel's edge.
        private static float ContentW
        {
            get
            {
                GUISkin k = GUI.skin;
                float bar = k.verticalScrollbar.fixedWidth > 0f ? k.verticalScrollbar.fixedWidth : 15f;
                return Mathf.Max(200f, W - k.window.padding.horizontal - bar - k.verticalScrollbar.margin.horizontal - k.scrollView.padding.horizontal - 4f);
            }
        }

        private static float appliedFontScale = -1f;

        private static void EnsureStyles()
        {
            float fontScale = Mathf.Clamp(Plugin.TuningWindowFontScale.Value, 0.5f, 2f);
            if (stylesReady && Mathf.Approximately(fontScale, appliedFontScale))
            {
                return;
            }
            appliedFontScale = fontScale;
            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13f * fontScale) };
            small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(11f * fontScale), wordWrap = true };
            small.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            header = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13f * fontScale), fontStyle = FontStyle.Bold };
            wrapToggle = new GUIStyle(GUI.skin.toggle) { wordWrap = true, fontSize = Mathf.RoundToInt(13f * fontScale) };
            changed = new GUIStyle(label);
            changed.normal.textColor = new Color(1f, 0.85f, 0.3f);
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.35f));
            tex.Apply();
            rule = new GUIStyle();
            rule.normal.background = tex;
            rule.fixedHeight = 1f;
            rule.margin = new RectOffset(0, 0, 10, 4);
            stylesReady = true;
        }

        private static GUIStyle rule;

        // A section title with a rule line above it, so the rows below visibly belong to it.
        private static void Header(string text)
        {
            GUILayout.Box(GUIContent.none, rule, GUILayout.ExpandWidth(true));
            GUILayout.Label(text, header);
        }

        // The settings that differ from their mod defaults, taken once per layout pass. IMGUI lays out every event
        // first and then runs it again; the button and the list below must have the same controls in both passes,
        // so a value changed by this event (a slider moved off its default) shows up from the next event on.
        private static List<(string Group, ConfigEntryBase Entry)> diffSnapshot = new List<(string, ConfigEntryBase)>();

        private static string DiffByGroup()
        {
            int g = 0, r = 0, c = 0;
            foreach (var d in diffSnapshot)
            {
                if (d.Group == "Ride") r++;
                else if (d.Group == "Cart") c++;
                else g++;
            }
            var parts = new List<string>();
            if (r > 0) parts.Add("Ride " + r);
            if (c > 0) parts.Add("Cart " + c);
            if (g > 0) parts.Add("General " + g);
            return string.Join(", ", parts);
        }

        private static void DrawContents(int id)
        {
            if (Event.current.type == EventType.Layout)
            {
                diffSnapshot = Defaults.Differing();
            }
            int newTab = GUILayout.Toolbar(tab, TabNames);
            if (newTab != tab)
            {
                tab = newTab;
                scroll = Vector2.zero;
            }
            GUILayout.Space(6f);
            // An IMGUI scroll view needs an explicit height, or it collapses; the content's height is measured on
            // each repaint (last frame's value is used) and capped to what fits on the screen.
            // The vertical scrollbar is always shown so its width never changes how text wraps (an appearing and
            // disappearing bar changes the content height, which changes whether the bar is needed: a flicker loop).
            // Small height jitter is ignored for the same reason.
            float h = contentHeight[tab] > 0f ? Mathf.Min(contentHeight[tab], maxContentHeight) : maxContentHeight;
            lastScrollHeight = h;
            // No horizontal scrollbar: the content is held to the panel's width (ContentW).
            scroll = GUILayout.BeginScrollView(scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(h));
            GUILayout.BeginVertical(GUILayout.Width(ContentW));
            switch (tab)
            {
                case 0: DrawGeneral(); break;
                case 1: DrawRiding(); break;
                case 2: DrawLantern(); break;
                case 3: DrawSounds(); break;
                case 4: DrawDebug(); break;
                default: DrawDefaults(); break;
            }
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint)
            {
                float measured = GUILayoutUtility.GetLastRect().height + 6f;
                if (Mathf.Abs(measured - contentHeight[tab]) > 3f)
                {
                    contentHeight[tab] = measured;
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            int differing = diffSnapshot.Count;
            GUILayout.Label(differing == 0
                ? "Your changes are saved as you make them. All settings match the mod defaults."
                : $"Your changes are saved as you make them. {differing} setting{(differing == 1 ? "" : "s")} differ from the mod defaults ({DiffByGroup()}; yellow, listed on the Defaults tab).", small);
            DrawDefaultsButtons(differing);
            GUILayout.Label("Right-click a row to put it back to its mod default.", small);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        // ------------------------------------------------------------------ tabs

        private static void DrawGeneral()
        {
            Header("Controls");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mount / dismount key (change General.MountKey in the cfg)", label, GUILayout.Width(LabelW));
            GUILayout.Label(Plugin.MountKey.Value.ToString(), label);
            GUILayout.EndHorizontal();

            Header("Behaviour");
            Toggle("Stay mounted when going through doors (scene changes)", Plugin.StayMountedThroughDoors, null);
            Toggle("Dismount when entering a building, church or dungeon (no mounting indoors)", Plugin.DismountIndoors, null);
            Toggle("Show a thought bubble when mounting is refused", Plugin.ShowMessages, null);
            Toggle("Keeper only faces left/right while riding", Plugin.SideViewOnly, null);
            if (!Plugin.SideViewOnly.Value && Mount.PoseNeedsSideView)
            {
                GUILayout.Label("Forced on: the " + Mount.S.Pose.Value + " pose only has left/right frames; facing up or down would show no body.", small);
            }
            GUILayout.Label("You always dismount for work, ladders, building, fishing, fighting, sleep and co-op; chests and inventory keep you mounted.", small);

            Header("Setup");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mount: world-object id to summon (General.MountWgoId)", label, GUILayout.Width(LabelW));
            string wgo = GUILayout.TextField(Plugin.MountWgoId.Value ?? "", GUILayout.Width(150f));
            GUILayout.EndHorizontal();
            if (ResetOnRightClick(Plugin.MountWgoId, null)) { }
            else if (wgo != Plugin.MountWgoId.Value)
            {
                Plugin.MountWgoId.Value = wgo;
            }
            GUILayout.Label("Used at the next mount. The game defines npc_donkey (and npc_donkey_test, the same art); anything without a cart gives Ride style only.", small);
            Toggle("Safety: do nothing if the game's code changed since the audit (Safety.DisableOnGameUpdate)", Plugin.DisableOnGameUpdate, null);
            GUILayout.Label($"Checked once at game start; a change here counts from the next start. General.Enabled is {Plugin.Enabled.Value} (set in the cfg; turning the mod off while mounted would strand the donkey, so it is not a switch here).", small);
        }

        private static void DrawRiding()
        {
            StyleSettings s = Mount.S;
            bool ride = Mount.CurrentStyle == MountStyle.Ride;

            Header("Style being tuned: " + (ride ? "riding on the donkey's back" : "the donkey pulling its cart") + "  [" + s.Section + "]");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Switch style (the donkey is summoned again)", label, GUILayout.Width(LabelW));
            if (GUILayout.Button(ride ? "Go to cart" : "Go to riding", GUILayout.Width(SliderW)))
            {
                Plugin.Style.Value = ride ? MountStyle.Cart : MountStyle.Ride;
                Mount.Remount();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Each style keeps its own values below; they are saved under [" + s.Section + "] in the cfg.", small);

            Header("Speed");
            FloatSlider("Speed (x normal walking)", s.SpeedMultiplier, 0.5f, 4f, 2, Mount.RefreshSpeed);

            Header("Donkey position");
            FloatSlider("Donkey in front of (-) / behind (+) the rider", s.Depth, -0.5f, 0.5f, 2, null);
            CenteringToggle(s.CenterOffset);
            Toggle("Donkey follows the ground height at its own position (slopes)", s.SnapToGround, null);

            Header("Rider position");
            IntSlider("Move whole rider along the donkey: tail (-) / head (+)", s.RiderOffsetPx, -16, 16, null);

            Header("Rider torso");
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

            Header("Rider pose and animation");
            EnumCycler("Body pose", s.Pose, Mount.ApplyPoseNow);
            if (Mount.PoseBlocksMovement)
            {
                GUILayout.Label("This pose makes the game take control: the keeper cannot move while it plays. Choose another.", small);
            }
            Toggle("Freeze the pose while standing still (arms stop moving)", s.FreezePoseWhenIdle, null);
            if (s.FreezePoseWhenIdle.Value)
            {
                FloatSlider("Fidget: play the pose briefly every N seconds (0 = hard freeze)", s.IdleFidgetEverySeconds, 0f, 30f, 1, null);
                FloatSlider("Fidget playback speed (x clip speed)", s.IdleFidgetSpeed, 0.05f, 2f, 2, null);
                FloatSlider("Crawl speed between fidgets (0 = fully still)", s.IdleCrawlSpeed, 0f, 0.5f, 2, null);
                GUILayout.Label("The freeze stops the whole animator, the game's breathing layer included. The crawl keeps a trace of it; a fidget plays the pose for about a second at the fidget speed.", small);
            }
            FloatSlider("Pose animation speed while moving (x clip speed)", s.PoseSpeed, 0.1f, 4f, 2, null);
            Toggle("Scale pose animation speed with the riding speed", s.PoseSpeedFollowsDonkey, null);

            Header("Rider legs (drawn on the donkey's side)");
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

            // Everything about the cart itself sits together at the end; the sections only show in Cart style.
            if (Mount.CurrentStyle == MountStyle.Cart)
            {
                Header("Cart on slopes");
                Toggle("Cart follows the ground under the cart", s.CartFollowsGround, null);
                if (s.CartFollowsGround.Value)
                {
                    FloatSlider("Cart: largest height shift allowed", s.CartGroundMaxShift, 0f, 1.5f, 2, null);
                    GUILayout.Label("Wheels, cart body and lantern move up or down together by the ground difference between cart and donkey; the bars between them take half of it.", small);
                }

                Header("Cart cargo: the packer");
                GUILayout.Label("Every item's box is measured; the larger footprint goes to the bottom or far side; two items lie side by side when they fit across the bed, else the second stacks. These four values rule every combination.", small);
                FloatSlider("Cargo floor below the rail top (bed depth)", s.CargoBedDepth, -0.6f, 0.3f, 2, Cargo.RefreshViews);
                FloatSlider("Gap between side-by-side items", s.CargoSideGap, 0f, 0.3f, 2, Cargo.RefreshViews);
                FloatSlider("Overhang allowed before stacking", s.CargoOverhang, 0f, 0.5f, 2, Cargo.RefreshViews);
                FloatSlider("Nestle: how far a stacked item sinks into the one below", s.CargoNestle, 0f, 0.3f, 2, Cargo.RefreshViews);
                Toggle("Mod draws corpses too (off = the game's own shroud for the first corpse)", s.CargoDrawCorpse, Cargo.RefreshViews);
                Toggle("Cargo rocks with the cart while it rolls (as the game's loaded corpse does)", s.CargoSwaysWithCart, null);
                GUILayout.Label($"While riding, anything you pick up goes straight onto the cart and piles, tables and graves take from it. The interact key with nothing in front of you sets the top item down. {Cargo.Summary()}", small);
                DrawCargoCorrections();
                GUILayout.Label("The lantern pole and its lamp have their own tab.", small);
            }
        }

        // Everything about the cart's lantern: pole placement, sway, and the lamp's lights and sprites. All of it is
        // per style ([Cart]); the tab shows a note in Ride style, where there is no cart.
        private static void DrawLantern()
        {
            if (Mount.CurrentStyle != MountStyle.Cart)
            {
                GUILayout.Label("The lantern belongs to the cart. Set Style = Cart on the General tab and mount again to see and tune it here.", small);
                return;
            }
            StyleSettings s = Mount.S;
            Header("Show");
            Toggle("Show the lantern pole and lamp at all", s.LanternShow, Lantern.Refresh);
            if (!s.LanternShow.Value)
            {
                GUILayout.Label("Hidden: pole, lamp, glow and both lights. Everything below applies once it is shown again.", small);
                return;
            }

            Header("Pole placement");
            GUILayout.Label("The prefab puts the pole at the front rail, where the rider's head hides it. Offsets in world units, angles in degrees; the pole turns and scales about its own foot, the lamp and its lights follow the top, and the cart's walking sway is kept.", small);
            FloatSlider("Along the cart: towards the donkey (-) / the back (+)", s.LanternAlong, -0.6f, 0.9f, 2, Lantern.Refresh);
            FloatSlider("Up (+) / down (-)", s.LanternUp, -0.5f, 0.8f, 2, Lantern.Refresh);
            FloatSlider("Across: away from the camera (-) / towards it (+)", s.LanternAcross, -0.6f, 0.6f, 2, Lantern.Refresh);
            FloatSlider("Tilt in the screen plane: top towards the donkey (-) / the back (+)", s.LanternTilt, -60f, 60f, 0, Lantern.Refresh);
            FloatSlider("Lean: top away from the camera (-) / towards it (+)", s.LanternLean, -60f, 60f, 0, Lantern.Refresh);
            FloatSlider("Size of the pole (1 = the game's)", s.LanternScale, 0.5f, 1.6f, 2, Lantern.Refresh);
            Toggle("Mirror the pole (foot stays; it leans out over the back instead of over the donkey)", s.LanternFlip, Lantern.Refresh);

            Header("Sway");
            FloatSlider("Pendulum about the foot, degrees either way (0 = off; scaled by the wind)", s.LanternSwayDegrees, 0f, 10f, 1, Lantern.Refresh);
            FloatSlider("Sway period, seconds", s.LanternSwaySeconds, 0.5f, 10f, 1, Lantern.Refresh);
            GUILayout.Label("The game never sways the pole except by rocking the whole cart while it rolls; this adds a slow swing whose amplitude drifts and follows the weather's wind value.", small);

            Header("Main light (lights the ground and the cart)");
            Toggle("On", s.LanternLightOn, Lantern.Refresh);
            FloatSlider("Brightness (x the game's)", s.LanternLightIntensity, 0f, 4f, 2, Lantern.Refresh);
            FloatSlider("Reach (x the game's)", s.LanternLightRange, 0.2f, 3f, 2, Lantern.Refresh);
            Toggle("Casts soft shadows (as the game's does)", s.LanternLightShadows, Lantern.Refresh);
            FloatSlider("Brightness in full daylight (1 = always lit, as the game's cart; 0 = off by day)", s.LanternLightDayDim, 0f, 1f, 2, Lantern.Refresh);
            ColorRow("Colour of both lights (hex; the game's is FF9600)", s.LanternLightColor, Lantern.Refresh);

            Header("Character light (lights only people and sprites nearby)");
            Toggle("On", s.LanternSelfLightOn, Lantern.Refresh);
            FloatSlider("Brightness (x the game's)", s.LanternSelfLightIntensity, 0f, 4f, 2, Lantern.Refresh);
            FloatSlider("Reach (x the game's)", s.LanternSelfLightRange, 0.2f, 3f, 2, Lantern.Refresh);

            Header("Lamp sprites");
            Toggle("Glow around the lamp", s.LanternGlowOn, Lantern.Refresh);
            FloatSlider("Glow opacity (x the game's)", s.LanternGlowAlpha, 0f, 4f, 2, Lantern.Refresh);
            FloatSlider("Glow size (x the game's)", s.LanternGlowScale, 0.3f, 3f, 2, Lantern.Refresh);
            Toggle("Lit lantern glass", s.LanternGlassOn, Lantern.Refresh);

            GUILayout.Label(Lantern.LightSummary(), small);
            GUILayout.Label(Lantern.Summary(), small);
        }

        private static bool waitingForBellKey;

        private static void DrawSounds()
        {
            StyleSettings s = Mount.S;
            bool cart = Mount.CurrentStyle == MountStyle.Cart;
            Header("Sounds for this style  [" + s.Section + "]");
            GUILayout.Label("All sounds are the game's own, played at the donkey through the game's sound player, so the in-game SFX volume applies. Volumes and pitches multiply the game's values for that sound (1 = as the game plays it). Sound ids can be any id from the game's list (PROBE-FINDINGS Round 12); an unknown id plays nothing and is marked here. The play buttons preview a sound with the settings below.", small);
            Toggle("All mount sounds on", s.SoundsOn, null);
            FloatSlider("Master volume", s.SoundVolume, 0f, 3f, 2, null);
            if (!s.SoundsOn.Value)
            {
                GUILayout.Label("Off: the game's own behaviour (a silent donkey, the keeper's footsteps, the game's lip sound).", small);
                return;
            }

            Header("Hoofsteps");
            Toggle("Hoofsteps while the donkey walks", s.HoofOn, null);
            SoundIdRow("Sound id (empty = by the ground: grass, dirt, wood, stone)", s.HoofSoundId, "hoof", allowEmpty: true);
            Toggle("Pick the step by the ground under the donkey (empty id only; off = dirt)", s.HoofSurfaceAware, null);
            FloatSlider("Volume", s.HoofVolume, 0f, 3f, 2, null);
            FloatSlider("Pitch (below 1 = heavier)", s.HoofPitch, 0.3f, 2f, 2, null);
            FloatSlider("Random pitch change per step, either way", s.HoofPitchVariation, 0f, 0.5f, 2, null);
            IntSlider("Footfalls per walk cycle", s.HoofStepsPerCycle, 1, 8, null);
            FloatSlider("Timing within the cycle (fraction of a step)", s.HoofPhase, 0f, 1f, 2, null);

            Header("Grazing and lips (standing still)");
            Toggle("Chewing sound when the donkey grazes", s.GrazeOn, null);
            SoundIdRow("Graze sound id", s.GrazeSoundId, "graze", allowEmpty: false);
            FloatSlider("Graze volume", s.GrazeVolume, 0f, 3f, 2, null);
            FloatSlider("Lip-smacking volume (the game's own lips sound; 0 = silent)", s.LipsVolume, 0f, 3f, 2, null);
            if (GUILayout.Button("Play the lips sound", GUILayout.Width(160f))) MountSounds.Preview("lips");

            Header("Braying");
            SoundIdRow("Bray sound id (voice_donkey: four short ee-aw sounds)", s.BraySoundId, "bray", allowEmpty: false);
            FloatSlider("Chance of a bray when you mount", s.BrayOnMountChance, 0f, 1f, 2, null);
            FloatSlider("One idle bray on average every ... seconds (0 = never)", s.BrayIdleEverySeconds, 0f, 300f, 0, null);
            Toggle("Idle brays also while moving", s.BrayWhileWalking, null);
            FloatSlider("Volume", s.BrayVolume, 0f, 3f, 2, null);
            FloatSlider("Pitch", s.BrayPitch, 0.3f, 2f, 2, null);
            Toggle("Follows the SFX volume (off: the speech volume, as in the donkey's dialogue)", s.BrayInSfxChannel, null);

            Header("Cart");
            if (!cart)
            {
                GUILayout.Label("Wheels and the cargo thump belong to the cart. Mount in Cart style to tune them.", small);
            }
            else
            {
                Toggle("Wheel rumble while the cart moves", s.WheelOn, null);
                SoundIdRow("Wheel sound id (conveyor_assembly_wheel = chain and rev)", s.WheelSoundId, "wheel", allowEmpty: false);
                FloatSlider("Wheel volume", s.WheelVolume, 0f, 3f, 2, null);
                FloatSlider("Wheel pitch", s.WheelPitch, 0.3f, 2f, 2, null);
                Toggle("Wheel pitch rises with the walking speed", s.WheelPitchFollowsSpeed, null);
                IntSlider("Once every ... walk cycles", s.WheelEveryCycles, 1, 4, null);
                FloatSlider("Timing within the cycle", s.WheelPhase, 0f, 1f, 2, null);
                Toggle("Thump when an item lands in the cart", s.CargoThumpOn, null);
                SoundIdRow("Thump sound id", s.CargoThumpSoundId, "thump", allowEmpty: false);
                FloatSlider("Thump volume", s.CargoThumpVolume, 0f, 3f, 2, null);
                FloatSlider("Thump pitch", s.CargoThumpPitch, 0.3f, 2f, 2, null);
            }

            Header("Bell");
            SoundIdRow("Bell sound id", s.BellSoundId, "bell", allowEmpty: false);
            Toggle("Ring when the donkey sets off after standing", s.BellOnSetOff, null);
            Toggle("... only with cargo on the cart", s.BellOnlyWithCargo, null);
            FloatSlider("... after standing at least (seconds)", s.BellSetOffIdleSeconds, 0f, 60f, 0, null);
            FloatSlider("Volume", s.BellVolume, 0f, 3f, 2, null);
            FloatSlider("Pitch", s.BellPitch, 0.3f, 2f, 2, null);
            BellKeyRow();

            Header("Status");
            GUILayout.Label("Last sound: " + (string.IsNullOrEmpty(MountSounds.LastEvent) ? "none yet" : MountSounds.LastEvent), small);
            if (GUILayout.Button("Reset the sounds of this style to defaults"))
            {
                s.ResetSounds();
            }
        }

        // A sound id as a text field with a play button; an id the game does not have is flagged.
        private static void SoundIdRow(string name, ConfigEntry<string> entry, string preview, bool allowEmpty)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, LabelFor(entry), GUILayout.Width(LabelW));
            if (GUILayout.Button("Play", GUILayout.Width(50f)))
            {
                MountSounds.Preview(preview);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            string v = GUILayout.TextField(entry.Value ?? "", GUILayout.Width(LabelW));
            string id = (v ?? "").Trim();
            bool ok = (allowEmpty && id.Length == 0) || MountSounds.KnownId(id);
            GUILayout.Label(ok ? "" : "unknown id", small);
            GUILayout.EndHorizontal();
            if (ResetOnRightClick(entry, null)) { return; }
            if (v != entry.Value)
            {
                entry.Value = v;
            }
        }

        // General.BellKey: click, then press a key (Escape clears it).
        private static void BellKeyRow()
        {
            if (waitingForBellKey && Event.current.type == EventType.KeyDown && Event.current.keyCode != KeyCode.None)
            {
                Plugin.BellKey.Value = Event.current.keyCode == KeyCode.Escape ? KeyCode.None : Event.current.keyCode;
                waitingForBellKey = false;
                Event.current.Use();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Bell key (General.BellKey, both styles)", label, GUILayout.Width(LabelW));
            string shown = waitingForBellKey ? "press a key" : (Plugin.BellKey.Value == KeyCode.None ? "none" : Plugin.BellKey.Value.ToString());
            if (GUILayout.Button(shown, GUILayout.Width(100f)))
            {
                waitingForBellKey = !waitingForBellKey;
            }
            GUILayout.EndHorizontal();
            if (ResetOnRightClick(Plugin.BellKey, null)) { waitingForBellKey = false; }
            GUILayout.Label("Click, then press the key; Escape clears it. Pick a key the game does not use.", small);
        }

        // A hex colour as three 0-1 sliders; the entry keeps RRGGBB so the config stays hand-editable.
        private static void ColorRow(string name, ConfigEntry<string> entry, Action onChange)
        {
            Color c = Lantern.ParseColor(entry.Value, new Color(1f, 0.59f, 0f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, LabelFor(entry), GUILayout.Width(LabelW));
            GUILayout.Label(entry.Value, label, GUILayout.Width(70f));
            GUILayout.EndHorizontal();
            if (ResetOnRightClick(entry, onChange)) { return; }
            Color n = c;
            n.r = Channel("Red", c.r);
            n.g = Channel("Green", c.g);
            n.b = Channel("Blue", c.b);
            if (n != c)
            {
                entry.Value = Lantern.ToHex(n);
                onChange?.Invoke();
            }
        }

        private static float Channel(string name, float v)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("    " + name, label, GUILayout.Width(LabelW));
            float r = GUILayout.HorizontalSlider(v, 0f, 1f, GUILayout.Width(90f));
            GUILayout.Label(Mathf.RoundToInt(r * 255f).ToString(), label, GUILayout.Width(45f));
            GUILayout.EndHorizontal();
            return Mathf.Abs(r - v) > 0.002f ? r : v;
        }

        private static void DrawDebug()
        {
            Header("This panel");
            // Stepped, not sliders: dragging a slider that rescales the panel moves the slider under the mouse.
            Stepper("Panel size, everything (Debug.TuningWindowScale)", Plugin.TuningWindowScale, 0.1f, 0.75f, 3f, "0.00");
            Stepper("Text size inside the panel (Debug.TuningWindowFontScale)", Plugin.TuningWindowFontScale, 0.05f, 0.5f, 2f, "0.00");
            Stepper("Panel width (Debug.TuningWindowWidth)", Plugin.TuningWindowWidth, 20f, 300f, 900f, "0");
            Toggle("Tuning keys (PageUp/Down torso, Home/End legs sideways, Insert/Delete legs up/down)", Plugin.TuningKeys, null);
            GUILayout.Label("Debug.TuningWindow=false in the cfg hides this panel for normal play.", small);

            Header("Diagnostics");
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

            Header("Lantern pole (investigation)");
            if (GUILayout.Button("Sample the lantern for 6 s, then write GK2-lantern-facts.txt") && !LanternProbe.Running)
            {
                Plugin.Log.LogInfo("[lantern] " + LanternProbe.Start(6f));
            }
            GUILayout.Label(LanternProbe.Running ? "Sampling: ride around and turn once, so both facings are seen." : LanternProbe.Status, small);
            GUILayout.Label("Reads the pole and light nodes at the end of every frame (after the donkey's animator) while you ride, and writes BepInEx/GK2-lantern-facts.txt: whether the animator swings them and about which axis, the pole's foot and head relative to its pivot, the lights, how the root mirrors when turning, and the pole's overlap with the keeper's body on screen. Nothing is moved.", small);

            Header("Game donkey (for comparison)");
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
            GUILayout.Label("Send event to the real donkey's script", label, GUILayout.Width(LabelW));
            donkeyEvent = GUILayout.TextField(donkeyEvent, GUILayout.Width(110f));
            if (GUILayout.Button("Send", GUILayout.Width(50f)))
            {
                Plugin.Log.LogInfo("[script] " + ScriptDump.FireEventOnWgo("npc_donkey", donkeyEvent));
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Other events from the dumped graph: move_to_morgue, full_cycle_back, move_from_morgue, stay_morgue, Disable, SetInteractive, SetNonInteractive.", small);

            Header("Cart cargo (investigation)");
            if (GUILayout.Button("Dump carry facts to a file (drawer rules, inventory filters, big items)"))
            {
                Plugin.Log.LogInfo("[cargo] " + CarryProbe.DumpFacts());
            }
            GUILayout.Label("Writes BepInEx/GK2-carry-facts.txt: the donkey prefab's ConditionalDrawer rules (what switches the loaded-cart mesh), the npc_donkey definition's inventory size and item filters, every item with itemSize Big, and the mesh elements the keeper's overhead view can show. Mount first for the drawer rules.", small);
            if (GUILayout.Button("Dump head-item facts (patch checks, quest listeners, script expressions, item mechanics)"))
            {
                Plugin.Log.LogInfo("[head] " + HeadProbe.Dump());
            }
            GUILayout.Label("Writes BepInEx/GK2-head-facts.txt for routing pickups straight onto the cart: whether patches on the game's small head methods take effect when the game calls them, which quests listen to the head triggers, every expression in the game data and every world-object script that mentions the head, and a live test of the item mechanics on the cart's inventory. Mount in Cart style with empty hands and one free cart slot for the full set. " + HeadProbe.Status, small);
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

            Header("Cargo sheet: every combination on the cart, as pictures");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Photograph all cargo combinations (HTML sheet)", GUILayout.ExpandWidth(true)))
            {
                Plugin.Log.LogInfo("[sheet] " + CargoSheet.Start());
            }
            GUILayout.Label(CargoSheet.Status, small);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Photograph every item alone, grouped by shape bucket (shapes.html)", GUILayout.ExpandWidth(true)))
            {
                Plugin.Log.LogInfo("[sheet] " + CargoSheet.StartShapes());
            }
            GUILayout.Label("Mount in Cart style, stand still with the cart in view, press. Each distinct item shape alone and every ordered pair is put on the cart in turn, photographed and cropped; BepInEx/GK2-cargo-sheet/index.html shows the grid (rows: first item, columns: second). The panel hides during the run; your real cargo is put back afterwards.", small);

            Header("Spawn a carryable item in front of the keeper (test)");
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

        }

        // Your settings (saved as you change them) against the mod defaults (defaults.cfg, shipped in the DLL).
        private static string exportStatus = "";
        private static float confirmMakeDefaultsUntil = -1f;

        private static void DrawDefaults()
        {
            Header("Your settings and the mod defaults");
            GUILayout.Label("Your settings: everything you set on this panel, saved to dreas.gk2.mounts.cfg the moment you change it. They are what you play with.", small);
            GUILayout.Label("Mod defaults: what a fresh install starts with and what right-click and the reset buttons go back to. They come from defaults.cfg, which is built into the DLL.", small);
            GUILayout.Label("A setting shown in yellow differs from its mod default.", small);
            GUILayout.Label("Mod defaults read from: " + Defaults.SourceShort, small);

            List<(string Group, ConfigEntryBase Entry)> diff = diffSnapshot;
            Header(diff.Count == 0 ? "All settings match the mod defaults" : $"{diff.Count} setting{(diff.Count == 1 ? "" : "s")} differ from the mod defaults");
            foreach ((string group, ConfigEntryBase e) in diff)
            {
                GUILayout.Label($"[{group}] {e.Definition.Key}: yours {Defaults.Show(e.BoxedValue)}, mod default {Defaults.Show(Defaults.DefaultFor(e))}", small);
            }
            DrawDefaultsButtons(diff.Count);

            Header("Cargo layout (item and pair corrections)");
            if (CargoLayout.SavesToRepo)
            {
                GUILayout.Label("The corrections edited on the Riding tab are saved to the repo's cargo-layout.cfg as you make them. They have no separate 'yours': the file is the mod default, and a rebuild ships it.", small);
            }
            else
            {
                GUILayout.Label("The corrections edited on the Riding tab last for this session only: the mod's source folder is not on this machine, so there is nowhere to save them.", small);
            }
            if (GUILayout.Button("Re-read the layout file"))
            {
                CargoLayout.Load();
                Cargo.RefreshViews();
                exportStatus = "Cargo layout reloaded: " + CargoLayout.Summary();
            }
            GUILayout.Label("Loaded now: " + CargoLayout.Summary(), small);
            if (!string.IsNullOrEmpty(CargoLayout.LastSaveNote))
            {
                GUILayout.Label(CargoLayout.LastSaveNote, small);
            }

            if (!string.IsNullOrEmpty(exportStatus))
            {
                Header("Last result");
                GUILayout.Label(exportStatus, small);
            }
        }

        // "Make these the mod defaults" (only where the sources are) and "Reset this style"; used by the footer and
        // the Defaults tab.
        private static void DrawDefaultsButtons(int differing)
        {
            GUILayout.BeginHorizontal();
            if (Defaults.CanWriteDefaults && differing > 0)
            {
                bool confirming = Time.unscaledTime < confirmMakeDefaultsUntil;
                if (GUILayout.Button(confirming ? $"Click again: make all {differing} the mod defaults" : "Make these the mod defaults"))
                {
                    if (!confirming)
                    {
                        confirmMakeDefaultsUntil = Time.unscaledTime + 4f;
                    }
                    else
                    {
                        confirmMakeDefaultsUntil = -1f;
                        try
                        {
                            string path = Defaults.MakeCurrentTheDefaults();
                            exportStatus = $"{differing} setting{(differing == 1 ? "" : "s")} are now the mod defaults, written to the repo's defaults.cfg. Rebuild the mod to ship them in the DLL.";
                            Plugin.Log.LogInfo(exportStatus);
                        }
                        catch (Exception e)
                        {
                            exportStatus = "Making the mod defaults failed: " + e.Message;
                            Plugin.Log.LogWarning(exportStatus);
                        }
                    }
                }
            }
            if (GUILayout.Button("Reset this style to the mod defaults"))
            {
                Mount.S.ResetAll();
                Mount.RefreshLeg();
                Mount.RefreshSeat();
                Mount.RefreshSpeed();
                Mount.ApplyPoseNow();
            }
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ controls

        // CenterOffset is a switch in practice: any value above 0 means "cancel the sprite's built-in sideways offset
        // so the donkey sits under the rider" (the exact offset is measured from the sprite each frame); 0 leaves
        // the donkey where the game draws it. Shown as a toggle; the stored value stays a float for the cfg.
        private static void CenteringToggle(ConfigEntry<float> entry)
        {
            bool on = entry.Value > 0.0001f;
            bool clicked = OnOffRow("Keep the donkey centred under the rider", on, LabelFor(entry));
            if (ResetOnRightClick(entry, null)) { return; }
            if (clicked)
            {
                entry.Value = on ? 0f : 0.48f;
            }
        }

        // Layout-file corrections for what is on the cart right now: one group per item (its size class, in the role
        // it currently has: base, or top when stacked) and one for the pair. The packer re-runs on every change, and
        // the layout file is saved shortly after (CargoLayout.MarkChanged / SaveIfDue).
        private static void DrawCargoCorrections()
        {
            Header("Cart cargo: corrections (cargo-layout.cfg)");
            GUILayout.Label("Each item has one correction per role, shared by every item of its size: 'bottom / beside' when it stands on the bed, 'on top' when it is stacked. A stacked item rides on the one below, so moving the lower item moves both; side by side, each moves alone. " + CargoLayout.Summary(), small);
            if (Cargo.Placed.Count == 0)
            {
                GUILayout.Label("Load items onto the cart to edit the corrections for their size classes.", small);
            }
            // Work on a snapshot: a slider change re-runs the packer, which rebuilds Cargo.Placed; re-pack once after.
            var placed = new System.Collections.Generic.List<Cargo.PlacedItem>(Cargo.Placed);
            bool refresh = false;
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (Cargo.PlacedItem p in placed)
            {
                CargoLayout.ItemEntry e = CargoLayout.EditItem(p.Id);
                string role = p.OnTop ? "on top" : "bottom / beside";
                if (!seen.Add(e.Id + role))
                {
                    continue;
                }
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"{p.Id} ({role}; class entry '{e.Id}', box {p.Box.x:0.00} × {p.Box.y:0.00} × {p.Box.z:0.00})", label);
                if (p.OnTop)
                {
                    refresh |= RawSlider("  along (tail - / head +)", ref e.Top.x, -0.5f, 0.5f);
                    refresh |= RawSlider("  up", ref e.Top.y, -0.4f, 0.4f);
                    refresh |= RawSlider("  across (camera - / away +)", ref e.Top.z, -0.5f, 0.5f);
                }
                else
                {
                    refresh |= RawSlider("  along (tail - / head +)", ref e.Base.x, -0.5f, 0.5f);
                    refresh |= RawSlider("  up", ref e.Base.y, -0.4f, 0.4f);
                    refresh |= RawSlider("  across (camera - / away +)", ref e.Base.z, -0.5f, 0.5f);
                }
                GUILayout.EndVertical();
            }
            if (placed.Count >= 2)
            {
                string a = placed[0].Id, b = placed[1].Id;
                CargoLayout.PairEntry pair = CargoLayout.EditPair(a, b);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"Pair {pair.A} + {pair.B} (either order): how the two are arranged", label);
                GUILayout.BeginHorizontal();
                GUILayout.Label("  mode", label, GUILayout.Width(120f));
                foreach (string mode in new[] { "auto", "side", "along", "stack" })
                {
                    bool on = pair.Mode == mode;
                    if (GUILayout.Toggle(on, mode, GUI.skin.button, GUILayout.Width(70f)) && !on)
                    {
                        pair.Mode = mode;
                        refresh = true;
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("  auto: across the bed when both widths fit, else end to end when both lengths fit, else stacked. side = across, along = end to end, stack = on top, whatever the sizes.", small);
                bool hasPairOffsets = pair.Second != Vector3.zero;
                bool wantPairOffsets = GUILayout.Toggle(hasPairOffsets || pairOffsetsOpen, "  This pair needs its own correction (an exception: extra offsets for the second item, only with this partner)", wrapToggle, GUILayout.Width(ContentW - 16f));
                pairOffsetsOpen = wantPairOffsets;
                if (wantPairOffsets)
                {
                    GUILayout.Label($"  Normally the class sliders above are enough: '{placed[1].Id} on top' applies on every partner. Use these only when it is right elsewhere and wrong on {placed[0].Id}.", small);
                    refresh |= RawSlider("  extra along (tail - / head +)", ref pair.Second.x, -0.5f, 0.5f);
                    refresh |= RawSlider("  extra up", ref pair.Second.y, -0.4f, 0.4f);
                    refresh |= RawSlider("  extra across (camera - / away +)", ref pair.Second.z, -0.5f, 0.5f);
                }
                GUILayout.EndVertical();
            }
            if (refresh)
            {
                Cargo.RefreshViews();
                CargoLayout.MarkChanged();
            }
            GUILayout.Label("Right-click a slider to zero it. " + (CargoLayout.SavesToRepo ? "Saved to the repo's cargo-layout.cfg as you edit." : "Not saved on this machine (no source folder)."), small);
        }

        // A - value + row for a float setting, stepping by 'step' within [min, max]; right-click the value resets it.
        private static void Stepper(string name, ConfigEntry<float> entry, float step, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, LabelFor(entry), GUILayout.Width(LabelW));
            if (GUILayout.Button("-", GUILayout.Width(30f)))
            {
                entry.Value = Mathf.Max(min, (float)Math.Round(entry.Value - step, 2));
            }
            GUILayout.Label(entry.Value.ToString(format), label, GUILayout.Width(50f));
            Rect r = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && r.Contains(Event.current.mousePosition))
            {
                Defaults.ResetToDefault(entry);
                Event.current.Use();
            }
            if (GUILayout.Button("+", GUILayout.Width(30f)))
            {
                entry.Value = Mathf.Min(max, (float)Math.Round(entry.Value + step, 2));
            }
            GUILayout.EndHorizontal();
        }

        // A slider on a plain float (layout-file values). Right-click resets to 0. Returns true when it moved.
        private static bool RawSlider(string name, ref float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            // Inside a box (its padding) with a slider and a value: the label gets the rest of the content width.
            GUILayout.Label(name, label, GUILayout.Width(Mathf.Max(80f, ContentW - SliderW - 50f - GUI.skin.box.padding.horizontal - 12f)));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(SliderW));
            Rect r = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && r.Contains(Event.current.mousePosition))
            {
                v = 0f;
                Event.current.Use();
            }
            GUILayout.Label(v.ToString("0.00"), label, GUILayout.Width(50f));
            GUILayout.EndHorizontal();
            v = (float)Math.Round(v, 2);
            if (Mathf.Abs(v - value) > 0.0001f)
            {
                value = v;
                return true;
            }
            return false;
        }

        private static void FloatSlider(string name, ConfigEntry<float> entry, float min, float max, int decimals, Action onChange)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, LabelFor(entry), GUILayout.Width(LabelW));
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
            GUILayout.Label(name, LabelFor(entry), GUILayout.Width(LabelW));
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
        private static bool OnOffRow(string name, bool value, GUIStyle style = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, style ?? label, GUILayout.Width(LabelW));
            bool clicked = GUILayout.Button(value ? "On" : "Off", GUILayout.Width(70f));
            GUILayout.EndHorizontal();
            return clicked;
        }

        private static void Toggle(string name, ConfigEntry<bool> entry, Action onChange)
        {
            bool clicked = OnOffRow(name, entry.Value, LabelFor(entry));
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
            GUILayout.Label(name, LabelFor(entry), GUILayout.Width(LabelW));
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
            T def = (T)Defaults.DefaultFor(entry);
            if (!Equals(entry.Value, def))
            {
                entry.Value = def;
                onChange?.Invoke();
            }
            return true;
        }
    }
}
