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
        public readonly ConfigEntry<float> PoseSpeed;
        public readonly ConfigEntry<bool> PoseSpeedFollowsDonkey;
        public readonly ConfigEntry<bool> NearLeg;
        public readonly ConfigEntry<LegSource> LegSource;
        public readonly ConfigEntry<int> LegHeightPx;
        public readonly ConfigEntry<int> LegAdjustXPx;
        public readonly ConfigEntry<int> LegAdjustYPx;
        public readonly ConfigEntry<bool> SnapToGround;
        public readonly ConfigEntry<bool> CartFollowsGround;
        public readonly ConfigEntry<float> CartGroundMaxShift;
        public readonly ConfigEntry<bool> CargoDrawCorpse;
        public readonly ConfigEntry<float> CargoBedDepth, CargoSideGap, CargoOverhang, CargoNestle;
        public readonly ConfigEntry<float> LanternAlong, LanternUp, LanternAcross, LanternTilt, LanternLean;
        public readonly ConfigEntry<bool> LanternFlip;
        public readonly ConfigEntry<float> LanternSwayDegrees, LanternSwaySeconds;
        public readonly ConfigEntry<bool> LanternShow, LanternLightOn, LanternLightShadows, LanternSelfLightOn, LanternGlowOn, LanternGlassOn;
        public readonly ConfigEntry<float> LanternScale, LanternLightIntensity, LanternLightRange, LanternLightDayDim, LanternSelfLightIntensity, LanternSelfLightRange, LanternGlowAlpha, LanternGlowScale;
        public readonly ConfigEntry<string> LanternLightColor;
        public readonly ConfigEntry<float> IdleFidgetEverySeconds, IdleFidgetSpeed, IdleCrawlSpeed;

        public StyleSettings(ConfigFile cfg, string section, float speed, float depth, float center, int riderPx, int torsoPx,
            bool autoSeat, int seatAdjustPx, float seatOffset, bool bob, RiderPose pose, bool freeze,
            bool legs, LegSource legSource, int legHeight, int legX, int legY, float poseSpeed = 1f, bool poseSpeedFollows = true)
        {
            Section = section;
            string who = section == "Cart" ? "in cart style" : "while riding";
            SpeedMultiplier = cfg.Bind(section, "SpeedMultiplier", Defaults.Get(section, "SpeedMultiplier", speed),
                new ConfigDescription($"Walking speed {who}, as a multiple of normal (measured: exactly linear).", new AcceptableValueRange<float>(0.5f, 4f)));
            Depth = cfg.Bind(section, "Depth", Defaults.Get(section, "Depth", depth),
                "Donkey offset towards the camera (negative, drawn in front of the keeper) or away from it (positive, behind), in world units.");
            CenterOffset = cfg.Bind(section, "CenterOffset", Defaults.Get(section, "CenterOffset", center),
                "Above 0: keep the donkey centred under the rider (its sprite's built-in sideways offset is measured and cancelled every frame). 0: leave the donkey where the game draws it, half a body to the side.");
            RiderOffsetPx = cfg.Bind(section, "RiderOffsetPx", Defaults.Get(section, "RiderOffsetPx", riderPx),
                "Where the keeper stands or sits along the donkey, in art pixels: positive = closer to the head, negative = closer to the rear.");
            TorsoOffsetPx = cfg.Bind(section, "TorsoOffsetPx", Defaults.Get(section, "TorsoOffsetPx", torsoPx),
                "Shifts only the keeper's upper body along the donkey, in art pixels (positive = towards the head); the legs stay where they are.");
            AutoSeat = cfg.Bind(section, "AutoSeat", Defaults.Get(section, "AutoSeat", autoSeat),
                "Compute the torso height from the sprites (rider's hip on the donkey's back line). Off: use SeatOffset.");
            SeatAdjustPx = cfg.Bind(section, "SeatAdjustPx", Defaults.Get(section, "SeatAdjustPx", seatAdjustPx),
                "With AutoSeat: fine adjustment of the automatic height, in art pixels (positive = higher).");
            SeatOffset = cfg.Bind(section, "SeatOffset", Defaults.Get(section, "SeatOffset", seatOffset),
                "Without AutoSeat: torso height in world units (0 = standing on the ground).");
            Bob = cfg.Bind(section, "Bob", Defaults.Get(section, "Bob", bob),
                "Move the rider up and down with the donkey's back through its walk cycle (1 px).");
            Pose = cfg.Bind(section, "Pose", Defaults.Get(section, "Pose", pose),
                "Keeper's body pose. FishingIdle: hands forward as if holding reins, legs still (left/right frames only). Idle: standing still. FocusedWalk: the combat-focus walk. Walk: unchanged (arms swing).");
            FreezePoseWhenIdle = cfg.Bind(section, "FreezePoseWhenIdle", Defaults.Get(section, "FreezePoseWhenIdle", freeze),
                "Pause the keeper's animation while standing still, so the arms stop moving; resumes when moving. Not used for the Idle and Walk poses.");
            PoseSpeed = cfg.Bind(section, "PoseSpeed", Defaults.Get(section, "PoseSpeed", poseSpeed),
                new ConfigDescription("Playback speed of the keeper's pose animation while the donkey moves (1 = the clip's own speed).", new AcceptableValueRange<float>(0.1f, 4f)));
            PoseSpeedFollowsDonkey = cfg.Bind(section, "PoseSpeedFollowsDonkey", Defaults.Get(section, "PoseSpeedFollowsDonkey", poseSpeedFollows),
                "Also multiply the pose animation speed by the riding speed multiplier, so it keeps pace with the donkey's walk animation.");
            NearLeg = cfg.Bind(section, "NearLeg", Defaults.Get(section, "NearLeg", legs),
                "Draw the rider's legs in front of the donkey while his real (baked-in) legs are hidden inside it.");
            LegSource = cfg.Bind(section, "LegSource", Defaults.Get(section, "LegSource", legSource),
                "Body: the keeper's own trousers and boots, re-sliced from his current body frame (outfit-exact, follows the pose). Drawn: a hand-drawn bent leg in outfit colours.");
            LegHeightPx = cfg.Bind(section, "LegHeightPx", Defaults.Get(section, "LegHeightPx", legHeight),
                "With LegSource=Body: how many bottom rows of the body frame count as legs (feet to belt), in art pixels.");
            LegAdjustXPx = cfg.Bind(section, "LegAdjustXPx", Defaults.Get(section, "LegAdjustXPx", legX),
                "Fine adjustment of the legs along the donkey, relative to the torso, in art pixels (positive = towards the rear).");
            LegAdjustYPx = cfg.Bind(section, "LegAdjustYPx", Defaults.Get(section, "LegAdjustYPx", legY),
                "Fine adjustment of the legs' height, relative to the donkey's back line, in art pixels (positive = higher).");
            SnapToGround = cfg.Bind(section, "SnapToGround", Defaults.Get(section, "SnapToGround", true),
                "Place the donkey at the ground height under its own position (the game does this for every walking object). Off: it takes the keeper's height, which digs it into slopes when it stands beside or behind him.");
            CartFollowsGround = cfg.Bind(section, "CartFollowsGround", Defaults.Get(section, "CartFollowsGround", true),
                "Cart style only: raise or lower the whole cart (wheels, cart body, lantern) as one, every frame, by the ground difference between the cart and the donkey, so the rigid 3D cart does not sink into a slope behind the donkey. The bars between donkey and cart take half of that shift. Nothing rotates.");
            CartGroundMaxShift = cfg.Bind(section, "CartGroundMaxShift", Defaults.Get(section, "CartGroundMaxShift", 0.8f),
                "Largest shift the cart may get from CartFollowsGround, in world units, either way.");
            CargoDrawCorpse = cfg.Bind(section, "CargoDrawCorpse", Defaults.Get(section, "CargoDrawCorpse", true),
                "Cart style only: the mod draws every corpse on the cart itself (same packer and corrections as any other item) and keeps the game's loaded-cart body off. Off: the first corpse is shown by the game's own loaded cart body with the shroud, which the packer cannot move; other items then ride on top of it.");
            CargoBedDepth = cfg.Bind(section, "CargoBedDepth", Defaults.Get(section, "CargoBedDepth", -0.26f),
                "Cart style only: the cargo floor relative to the measured top of the empty cart body, world units (negative = items sink below the rails onto the bed floor). Every item's measured bottom is put on this floor.");
            CargoSideGap = cfg.Bind(section, "CargoSideGap", Defaults.Get(section, "CargoSideGap", 0.02f),
                "Cart style only: gap between two items lying side by side across the bed, world units.");
            CargoOverhang = cfg.Bind(section, "CargoOverhang", Defaults.Get(section, "CargoOverhang", 0.12f),
                "Cart style only: how far two side-by-side items may together exceed the bed's measured width before the second one is stacked on the first instead, world units.");
            CargoNestle = cfg.Bind(section, "CargoNestle", Defaults.Get(section, "CargoNestle", 0.03f),
                "Cart style only: how far an item stacked on another sinks into it, world units (a log on a shroud does not float on the highest point).");
            LanternAlong = cfg.Bind(section, "LanternAlong", Defaults.Get(section, "LanternAlong", 0f),
                "Cart style only: move the lantern pole (with its light) along the cart, world units; positive = towards the back of the cart, away from the donkey. The prefab has it at the front rail, where the rider's head hides it.");
            LanternUp = cfg.Bind(section, "LanternUp", Defaults.Get(section, "LanternUp", 0f),
                "Cart style only: move the lantern pole up (positive) or down, world units.");
            LanternAcross = cfg.Bind(section, "LanternAcross", Defaults.Get(section, "LanternAcross", 0f),
                "Cart style only: move the lantern pole across the cart, world units; positive = towards the camera (in front of the rider), negative = away (behind him).");
            LanternTilt = cfg.Bind(section, "LanternTilt", Defaults.Get(section, "LanternTilt", 0f),
                new ConfigDescription("Cart style only: tilt the pole about its foot in the screen plane, degrees; positive = its top leans towards the back of the cart. The lamp and its light follow the top; the cart's walking sway is kept.", new AcceptableValueRange<float>(-60f, 60f)));
            LanternLean = cfg.Bind(section, "LanternLean", Defaults.Get(section, "LanternLean", 0f),
                new ConfigDescription("Cart style only: lean the pole about its foot towards the camera (positive) or away from it, degrees. The lamp follows the top; its sprites stay upright.", new AcceptableValueRange<float>(-60f, 60f)));
            LanternFlip = cfg.Bind(section, "LanternFlip", Defaults.Get(section, "LanternFlip", false),
                "Cart style only: mirror the pole in the plane through its foot, so the slanted pole leans out over the back of the cart instead of forward over the donkey; the foot stays where it is. The lamp, its glow and its lights are mirrored with it.");
            LanternSwayDegrees = cfg.Bind(section, "LanternSwayDegrees", Defaults.Get(section, "LanternSwayDegrees", 1.5f),
                new ConfigDescription("Cart style only: a slow pendulum sway of the lantern pole about its foot, in degrees either way (0 = off). Scaled by the game's wind: about half this in still air, up to one and a half times in a gale. The lamp and its light follow, so the light pool shifts with it. Adds to the cart's own rocking while it rolls.", new AcceptableValueRange<float>(0f, 10f)));
            LanternSwaySeconds = cfg.Bind(section, "LanternSwaySeconds", Defaults.Get(section, "LanternSwaySeconds", 2.6f),
                new ConfigDescription("Cart style only: period of one full sway, seconds. The amplitude drifts slowly so it is not a metronome.", new AcceptableValueRange<float>(0.5f, 10f)));
            LanternShow = cfg.Bind(section, "LanternShow", Defaults.Get(section, "LanternShow", true),
                "Cart style only: show the lantern pole and its lamp at all. Off hides pole, lamp, glow and lights.");
            LanternScale = cfg.Bind(section, "LanternScale", Defaults.Get(section, "LanternScale", 1f),
                new ConfigDescription("Cart style only: size of the pole, scaled about its foot (1 = the prefab's). The lamp moves to the scaled top; its pixel art is not scaled.", new AcceptableValueRange<float>(0.5f, 1.6f)));
            LanternLightOn = cfg.Bind(section, "LanternLightOn", Defaults.Get(section, "LanternLightOn", true),
                "Cart style only: the lamp's main point light (the game's: range 4.5, intensity 3, orange, soft shadows, lit day and night).");
            LanternLightIntensity = cfg.Bind(section, "LanternLightIntensity", Defaults.Get(section, "LanternLightIntensity", 1f),
                new ConfigDescription("Cart style only: multiplier on the main light's intensity (1 = the game's 3).", new AcceptableValueRange<float>(0f, 4f)));
            LanternLightRange = cfg.Bind(section, "LanternLightRange", Defaults.Get(section, "LanternLightRange", 1f),
                new ConfigDescription("Cart style only: multiplier on the main light's range (1 = the game's 4.5 world units).", new AcceptableValueRange<float>(0.2f, 3f)));
            LanternLightColor = cfg.Bind(section, "LanternLightColor", Defaults.Get(section, "LanternLightColor", "FF9600"),
                "Cart style only: colour of both lights as hex RRGGBB (the game's is FF9600, orange). Empty = the game's colour.");
            LanternLightShadows = cfg.Bind(section, "LanternLightShadows", Defaults.Get(section, "LanternLightShadows", true),
                "Cart style only: the main light casts soft shadows (as the game's does). Off saves a little rendering.");
            LanternLightDayDim = cfg.Bind(section, "LanternLightDayDim", Defaults.Get(section, "LanternLightDayDim", 1f),
                new ConfigDescription("Cart style only: how bright the lights are in full daylight, as a fraction of their night value (1 = always full, as the game's cart; 0 = off by day, like the keeper's own lantern). Follows the game's sun value through dusk and dawn.", new AcceptableValueRange<float>(0f, 1f)));
            LanternSelfLightOn = cfg.Bind(section, "LanternSelfLightOn", Defaults.Get(section, "LanternSelfLightOn", true),
                "Cart style only: the lamp's second light, which lights only characters and sprites nearby (the game's: range 2, intensity 10).");
            LanternSelfLightIntensity = cfg.Bind(section, "LanternSelfLightIntensity", Defaults.Get(section, "LanternSelfLightIntensity", 1f),
                new ConfigDescription("Cart style only: multiplier on the character light's intensity (1 = the game's 10).", new AcceptableValueRange<float>(0f, 4f)));
            LanternSelfLightRange = cfg.Bind(section, "LanternSelfLightRange", Defaults.Get(section, "LanternSelfLightRange", 1f),
                new ConfigDescription("Cart style only: multiplier on the character light's range (1 = the game's 2 world units).", new AcceptableValueRange<float>(0.2f, 3f)));
            LanternGlowOn = cfg.Bind(section, "LanternGlowOn", Defaults.Get(section, "LanternGlowOn", true),
                "Cart style only: the soft additive glow sprite around the lamp.");
            LanternGlowAlpha = cfg.Bind(section, "LanternGlowAlpha", Defaults.Get(section, "LanternGlowAlpha", 1f),
                new ConfigDescription("Cart style only: multiplier on the glow's opacity (1 = the game's).", new AcceptableValueRange<float>(0f, 4f)));
            LanternGlowScale = cfg.Bind(section, "LanternGlowScale", Defaults.Get(section, "LanternGlowScale", 1f),
                new ConfigDescription("Cart style only: multiplier on the glow's size (1 = the game's).", new AcceptableValueRange<float>(0.3f, 3f)));
            LanternGlassOn = cfg.Bind(section, "LanternGlassOn", Defaults.Get(section, "LanternGlassOn", true),
                "Cart style only: the lit lantern-glass sprite on the lamp (unlit shader, always bright).");
            IdleFidgetEverySeconds = cfg.Bind(section, "IdleFidgetEverySeconds", Defaults.Get(section, "IdleFidgetEverySeconds", 7f),
                new ConfigDescription("With FreezePoseWhenIdle: instead of a dead stop, the frozen pose plays for about a second every this many seconds (randomised by half either way), so the rider shifts now and then. 0 = a hard freeze as before.", new AcceptableValueRange<float>(0f, 60f)));
            IdleFidgetSpeed = cfg.Bind(section, "IdleFidgetSpeed", Defaults.Get(section, "IdleFidgetSpeed", 0.45f),
                new ConfigDescription("Playback speed of the pose during a fidget (1 = the clip's own speed).", new AcceptableValueRange<float>(0.05f, 2f)));
            IdleCrawlSpeed = cfg.Bind(section, "IdleCrawlSpeed", Defaults.Get(section, "IdleCrawlSpeed", 0.05f),
                new ConfigDescription("With FreezePoseWhenIdle: playback speed between fidgets, so the keeper's breathing layer keeps a barely visible life instead of stopping. 0 = fully still between fidgets.", new AcceptableValueRange<float>(0f, 0.5f)));
        }

        public void ResetAll()
        {
            Reset(SpeedMultiplier); Reset(Depth); Reset(CenterOffset); Reset(RiderOffsetPx); Reset(TorsoOffsetPx);
            Reset(AutoSeat); Reset(SeatAdjustPx); Reset(SeatOffset); Reset(Bob); Reset(Pose); Reset(FreezePoseWhenIdle);
            Reset(PoseSpeed); Reset(PoseSpeedFollowsDonkey); Reset(NearLeg); Reset(LegSource); Reset(LegHeightPx); Reset(LegAdjustXPx); Reset(LegAdjustYPx); Reset(SnapToGround); Reset(CartFollowsGround); Reset(CartGroundMaxShift); Reset(CargoDrawCorpse); Reset(CargoBedDepth); Reset(CargoSideGap); Reset(CargoOverhang); Reset(CargoNestle);
            Reset(LanternAlong); Reset(LanternUp); Reset(LanternAcross); Reset(LanternTilt); Reset(LanternLean); Reset(LanternFlip);
            Reset(LanternSwayDegrees); Reset(LanternSwaySeconds); Reset(IdleFidgetEverySeconds); Reset(IdleFidgetSpeed); Reset(IdleCrawlSpeed);
            Reset(LanternShow); Reset(LanternScale); Reset(LanternLightOn); Reset(LanternLightIntensity); Reset(LanternLightRange); Reset(LanternLightColor); Reset(LanternLightShadows); Reset(LanternLightDayDim);
            Reset(LanternSelfLightOn); Reset(LanternSelfLightIntensity); Reset(LanternSelfLightRange); Reset(LanternGlowOn); Reset(LanternGlowAlpha); Reset(LanternGlowScale); Reset(LanternGlassOn);
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
        internal static ConfigEntry<KeyCode> CargoKey;
        internal static ConfigEntry<MountStyle> Style;
        internal static ConfigEntry<string> MountWgoId;
        internal static ConfigEntry<bool> StayMountedThroughDoors;
        internal static ConfigEntry<bool> DismountIndoors;
        internal static ConfigEntry<bool> ShowMessages;
        internal static ConfigEntry<bool> SideViewOnly;

        // [Ride] / [Cart]: position, pose, animation, legs, speed
        internal static StyleSettings Ride;
        internal static StyleSettings Cart;
        internal static StyleSettings For(MountStyle style) => style == MountStyle.Cart ? Cart : Ride;

        // [Debug]
        internal static ConfigEntry<bool> TuningKeys;
        internal static ConfigEntry<string> DefaultsExportPath;
        internal static ConfigEntry<bool> TuningWindowEnabled;
        internal static ConfigEntry<float> TuningWindowScale;
        internal static ConfigEntry<float> TuningWindowFontScale;
        internal static ConfigEntry<float> TuningWindowWidth;

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
            CargoKey = Config.Bind("General", "CargoKey", KeyCode.L,
                "Cart style: sets the last item on the cart down on the ground in front of the keeper. Loading needs no key: while riding the cart, anything picked up (from the ground, a pile, a table) goes straight onto the cart, and piles, tables and graves take their items from the cart. The cart holds two items, like the game's own delivery cart. Pick a key the game does not use.");
            Style = Config.Bind("General", "Style", MountStyle.Ride,
                "Ride: sit on the donkey's back. Cart: the donkey pulls its cart and the keeper stands in it. Each style has its own [Ride] / [Cart] settings section.");
            MountWgoId = Config.Bind("General", "MountWgoId", "npc_donkey",
                "World object id used for the mount's looks. Only npc_donkey has been tested.");
            StayMountedThroughDoors = Config.Bind("General", "StayMountedThroughDoors", Defaults.Get("General", "StayMountedThroughDoors", true),
                "Keep riding after a scene change (the donkey is re-summoned in the new scene). Off: dismount at every door.");
            DismountIndoors = Config.Bind("General", "DismountIndoors", Defaults.Get("General", "DismountIndoors", true),
                "Dismount automatically when entering a building, church or dungeon (the game's indoor lighting presets), and refuse to mount while indoors. The cart cannot avoid furniture and door frames inside.");
            ShowMessages = Config.Bind("General", "ShowMessages", Defaults.Get("General", "ShowMessages", true),
                "Show a thought bubble when the keeper cannot mount and why.");
            SideViewOnly = Config.Bind("General", "SideViewOnly", Defaults.Get("General", "SideViewOnly", false),
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
                "Overall size of the tuning panel (1 = Unity default, 1.5 suits 1440p). Text and width scale with it.");
            TuningWindowFontScale = Config.Bind("Debug", "TuningWindowFontScale", 1f,
                "Text size inside the tuning panel, relative (1 = normal). Lower it so long rows fit.");
            TuningWindowWidth = Config.Bind("Debug", "TuningWindowWidth", 470f,
                "Width of the tuning panel in panel units before the overall size is applied (470 = original).");
            TuningKeys = Config.Bind("Debug", "TuningKeys", true,
                "While mounted: PageUp/PageDown move the torso, Home/End the legs sideways, Insert/Delete the legs up/down, one art pixel per press, editing the current style's values. Development aid, turn off for normal play.");
            DefaultsExportPath = Config.Bind("Debug", "DefaultsExportPath", "",
                "Where the panel's \"Export current values as mod defaults\" button writes defaults.cfg. Point it at src/GK2Mounts/defaults.cfg in the repo; the next build embeds it. Empty: BepInEx/config/GK2Mounts.defaults.cfg.");

            DisableOnGameUpdate = Config.Bind("Safety", "DisableOnGameUpdate", true,
                "Do not patch anything if the game's code has changed since this mod was audited. Rebuild/re-audit after game updates.");

            CargoLayout.Load(); // cargo-layout.cfg: embedded copy, or BepInEx/config/GK2Mounts.cargo-layout.cfg when present
            Log.LogInfo("Cargo layout: " + CargoLayout.Summary());

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
