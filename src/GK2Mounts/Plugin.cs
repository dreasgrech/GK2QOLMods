using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GK2Mounts
{
    public enum MountStyle
    {
        Ride,
        Cart,
    }

    public enum LegSource
    {
        Body,
        Drawn,
    }

    // Animator states the keeper can be held in while riding (values = the game's AnimationState enum).
    // Only states that loop on their own and carry no control-taking behaviour. Tool, work, planting, cast and pull
    // states either end by forcing Idle (which would re-trigger the pose every frame) or take player control.
    public enum RiderPose
    {
        Walk = 1,
        Idle = 0,
        FishingIdle = 203,
        FocusedWalk = 41,
    }

    // Everything that positions and animates rider and donkey, one complete set per style ([Ride] and [Cart]
    // sections in the cfg). The two looks differ in almost every value, so they are tuned independently; the
    // tuning panel always shows the set of the style currently mounted.
    internal sealed class StyleSettings
    {
        public readonly string Section;
        public readonly ConfigEntry<float> SpeedMultiplier;
        public readonly ConfigEntry<float> Depth;
        public readonly ConfigEntry<float> CenterOffset;
        public readonly ConfigEntry<int> RiderOffsetPx;
        public readonly ConfigEntry<int> TorsoOffsetPx;
        public readonly ConfigEntry<bool> AutoSeat;
        public readonly ConfigEntry<int> SeatAdjustPx;
        public readonly ConfigEntry<float> SeatOffset;
        public readonly ConfigEntry<bool> Bob;
        public readonly ConfigEntry<RiderPose> Pose;
        public readonly ConfigEntry<bool> FreezePoseWhenIdle;
        public readonly ConfigEntry<bool> NearLeg;
        public readonly ConfigEntry<LegSource> LegSource;
        public readonly ConfigEntry<int> LegHeightPx;
        public readonly ConfigEntry<int> LegAdjustXPx;
        public readonly ConfigEntry<int> LegAdjustYPx;

        public StyleSettings(ConfigFile cfg, string section, float speed, float depth, float center, int riderPx, int torsoPx,
            bool autoSeat, int seatAdjustPx, float seatOffset, bool bob, RiderPose pose, bool freeze,
            bool legs, LegSource legSource, int legHeight, int legX, int legY)
        {
            Section = section;
            string who = section == "Cart" ? "in cart style" : "while riding";
            SpeedMultiplier = cfg.Bind(section, "SpeedMultiplier", speed,
                new ConfigDescription($"Walking speed {who}, as a multiple of normal (measured: exactly linear).", new AcceptableValueRange<float>(0.5f, 4f)));
            Depth = cfg.Bind(section, "Depth", depth,
                "Donkey offset towards the camera (negative, drawn in front of the keeper) or away from it (positive, behind), in world units.");
            CenterOffset = cfg.Bind(section, "CenterOffset", center,
                "Above 0: keep the donkey centred under the rider (its sprite's built-in sideways offset is measured and cancelled every frame). 0: leave the donkey where the game draws it, half a body to the side.");
            RiderOffsetPx = cfg.Bind(section, "RiderOffsetPx", riderPx,
                "Where the keeper stands or sits along the donkey, in art pixels: positive = closer to the head, negative = closer to the rear.");
            TorsoOffsetPx = cfg.Bind(section, "TorsoOffsetPx", torsoPx,
                "Shifts only the keeper's upper body along the donkey, in art pixels (positive = towards the head); the legs stay where they are.");
            AutoSeat = cfg.Bind(section, "AutoSeat", autoSeat,
                "Compute the torso height from the sprites (rider's hip on the donkey's back line). Off: use SeatOffset.");
            SeatAdjustPx = cfg.Bind(section, "SeatAdjustPx", seatAdjustPx,
                "With AutoSeat: fine adjustment of the automatic height, in art pixels (positive = higher).");
            SeatOffset = cfg.Bind(section, "SeatOffset", seatOffset,
                "Without AutoSeat: torso height in world units (0 = standing on the ground).");
            Bob = cfg.Bind(section, "Bob", bob,
                "Move the rider up and down with the donkey's back through its walk cycle (1 px).");
            Pose = cfg.Bind(section, "Pose", pose,
                "Keeper's body pose. FishingIdle: hands forward as if holding reins, legs still (left/right frames only). Idle: standing still. FocusedWalk: the combat-focus walk. Walk: unchanged (arms swing).");
            FreezePoseWhenIdle = cfg.Bind(section, "FreezePoseWhenIdle", freeze,
                "Pause the keeper's animation while standing still, so the arms stop moving; resumes when moving. Not used for the Idle and Walk poses.");
            NearLeg = cfg.Bind(section, "NearLeg", legs,
                "Draw the rider's legs in front of the donkey while his real (baked-in) legs are hidden inside it.");
            LegSource = cfg.Bind(section, "LegSource", legSource,
                "Body: the keeper's own trousers and boots, re-sliced from his current body frame (outfit-exact, follows the pose). Drawn: a hand-drawn bent leg in outfit colours.");
            LegHeightPx = cfg.Bind(section, "LegHeightPx", legHeight,
                "With LegSource=Body: how many bottom rows of the body frame count as legs (feet to belt), in art pixels.");
            LegAdjustXPx = cfg.Bind(section, "LegAdjustXPx", legX,
                "Fine adjustment of the legs along the donkey, relative to the torso, in art pixels (positive = towards the rear).");
            LegAdjustYPx = cfg.Bind(section, "LegAdjustYPx", legY,
                "Fine adjustment of the legs' height, relative to the donkey's back line, in art pixels (positive = higher).");
        }

        public void ResetAll()
        {
            Reset(SpeedMultiplier); Reset(Depth); Reset(CenterOffset); Reset(RiderOffsetPx); Reset(TorsoOffsetPx);
            Reset(AutoSeat); Reset(SeatAdjustPx); Reset(SeatOffset); Reset(Bob); Reset(Pose); Reset(FreezePoseWhenIdle);
            Reset(NearLeg); Reset(LegSource); Reset(LegHeightPx); Reset(LegAdjustXPx); Reset(LegAdjustYPx);
        }

        private static void Reset<T>(ConfigEntry<T> e)
        {
            e.Value = (T)e.DefaultValue;
        }
    }

    // A rideable mount for Graveyard Keeper 2. Press the mount key and a donkey appears under the keeper; you walk
    // faster while riding. The donkey is a temporary view only (the game's build-preview mechanism): it is never
    // added to the world data, so nothing about it is saved. See README.md and FEASIBILITY.md.
    [BepInPlugin(Guid, "GK2 Mounts", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "anvil.gk2.mounts";
        public const string Version = "0.1.0";

        // Module version id of the Assembly-CSharp.dll this plugin was audited against.
        private static readonly System.Guid AuditedGameMvid = new System.Guid("97f92f0d-a093-49fe-ba4d-74aa62fae81e");

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        // [General]: behaviour
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> MountKey;
        internal static ConfigEntry<MountStyle> Style;
        internal static ConfigEntry<string> MountWgoId;
        internal static ConfigEntry<bool> StayMountedThroughDoors;
        internal static ConfigEntry<bool> ShowMessages;
        internal static ConfigEntry<bool> SideViewOnly;

        // [Ride] / [Cart]: position, pose, animation, legs, speed
        internal static StyleSettings Ride;
        internal static StyleSettings Cart;
        internal static StyleSettings For(MountStyle style) => style == MountStyle.Cart ? Cart : Ride;

        // [Debug]
        internal static ConfigEntry<bool> TuningKeys;
        internal static ConfigEntry<bool> TuningWindowEnabled;
        internal static ConfigEntry<float> TuningWindowScale;

        // [Safety]
        internal static ConfigEntry<bool> DisableOnGameUpdate;

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true,
                "Master switch. Press the mount key to summon a donkey under the keeper and ride it; press again to dismount.");
            MountKey = Config.Bind("General", "MountKey", KeyCode.H,
                "Key that mounts/dismounts. Pick one the game does not use.");
            Style = Config.Bind("General", "Style", MountStyle.Ride,
                "Ride: sit on the donkey's back. Cart: the donkey pulls its cart and the keeper stands in it. Each style has its own [Ride] / [Cart] settings section.");
            MountWgoId = Config.Bind("General", "MountWgoId", "npc_donkey",
                "World object id used for the mount's looks. Only npc_donkey has been tested.");
            StayMountedThroughDoors = Config.Bind("General", "StayMountedThroughDoors", true,
                "Keep riding after a scene change (the donkey is re-summoned in the new scene). Off: dismount at every door.");
            ShowMessages = Config.Bind("General", "ShowMessages", true,
                "Show a thought bubble when the keeper cannot mount and why.");
            SideViewOnly = Config.Bind("General", "SideViewOnly", false,
                "The donkey only has left/right art. On: while mounted the keeper also only faces left or right (walking up/down keeps the last horizontal facing). Off: the keeper turns as usual and the donkey shows its side. Forced on for poses that only have left/right frames.");

            Ride = new StyleSettings(Config, "Ride",
                speed: 1.75f, depth: 0.32f, center: 0.48f, riderPx: -4, torsoPx: -3,
                autoSeat: false, seatAdjustPx: -6, seatOffset: 0.32f, bob: true, pose: RiderPose.FishingIdle, freeze: true,
                legs: false, legSource: LegSource.Body, legHeight: 15, legX: 3, legY: 1);
            Cart = new StyleSettings(Config, "Cart",
                speed: 1.5f, depth: 0f, center: 0f, riderPx: 0, torsoPx: 0,
                autoSeat: false, seatAdjustPx: 0, seatOffset: 0f, bob: false, pose: RiderPose.Walk, freeze: false,
                legs: false, legSource: LegSource.Body, legHeight: 10, legX: 0, legY: 0);

            TuningWindowEnabled = Config.Bind("Debug", "TuningWindow", true,
                "Show a tuning panel for all mount settings in the top-right corner while mounted. Values apply live; Save writes them to this file. Turn off for normal play.");
            TuningWindowScale = Config.Bind("Debug", "TuningWindowScale", 1.5f,
                "Size of the tuning panel (1 = Unity default, 1.5 suits 1440p).");
            TuningKeys = Config.Bind("Debug", "TuningKeys", true,
                "While mounted: PageUp/PageDown move the torso, Home/End the legs sideways, Insert/Delete the legs up/down, one art pixel per press, editing the current style's values. Development aid, turn off for normal play.");

            DisableOnGameUpdate = Config.Bind("Safety", "DisableOnGameUpdate", true,
                "Do not patch anything if the game's code has changed since this mod was audited. Rebuild/re-audit after game updates.");

            System.Guid gameMvid = typeof(WgoData).Assembly.ManifestModule.ModuleVersionId;
            if (gameMvid != AuditedGameMvid)
            {
                if (DisableOnGameUpdate.Value)
                {
                    Log.LogError($"Game code changed since this mod was audited (MVID {gameMvid}, expected {AuditedGameMvid}). Mod is DISABLED; the game runs unmodified. Set Safety.DisableOnGameUpdate=false to force-enable at your own risk.");
                    return;
                }
                Log.LogWarning($"Game code changed since this mod was audited (MVID {gameMvid}). Running anyway because DisableOnGameUpdate=false.");
            }

            harmony = new Harmony(Guid);
            try
            {
                Access.Verify();
                harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                harmony = null;
                Log.LogError($"Patching failed; all patches removed, game runs unmodified. {e}");
                return;
            }
            Log.LogInfo($"GK2 Mounts {Version} loaded. Mount key: {MountKey.Value}, style {Style.Value}, speed x{For(Style.Value).SpeedMultiplier.Value}.");
        }

        // LateUpdate: runs after the game moved the player for this frame, so the donkey follows without lag.
        private void LateUpdate()
        {
            if (harmony == null || !Enabled.Value)
            {
                return;
            }
            try
            {
                Mount.Tick();
            }
            catch (Exception e)
            {
                Log.LogError($"Mount update failed; dismounting. {e}");
                Mount.ForceCleanup("error");
            }
        }

        private void OnGUI()
        {
            if (harmony == null || !Enabled.Value)
            {
                return;
            }
            try
            {
                TuningWindow.Draw();
            }
            catch (Exception e)
            {
                Log.LogWarning("Tuning window failed: " + e.Message);
            }
        }

        private void OnDestroy()
        {
            try
            {
                Mount.ForceCleanup("plugin unloaded");
            }
            catch
            {
            }
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}
