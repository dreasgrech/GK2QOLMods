using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Mounts.Api;
using HarmonyLib;
using UnityEngine;

namespace GK2Mounts
{
    // Everything that positions and animates rider and donkey, one complete set per style ([Ride] and [Cart]
    // sections in the cfg). The two looks differ in almost every value, so they are tuned independently; the
    // tuning panel always shows the set of the style currently mounted.
    internal sealed class StyleSettings
    {
        public readonly string Section;
        public readonly Tuned<float> SpeedMultiplier;
        public readonly Tuned<float> Depth;
        public readonly Tuned<float> CenterOffset;
        public readonly Tuned<int> RiderOffsetPx;
        public readonly Tuned<int> TorsoOffsetPx;
        public readonly Tuned<bool> AutoSeat;
        public readonly Tuned<int> SeatAdjustPx;
        public readonly Tuned<float> SeatOffset;
        public readonly Tuned<bool> Bob;
        public readonly Tuned<RiderPose> Pose;
        public readonly Tuned<bool> FreezePoseWhenIdle;
        public readonly Tuned<float> PoseSpeed;
        public readonly Tuned<bool> PoseSpeedFollowsDonkey;
        public readonly Tuned<bool> NearLeg;
        public readonly Tuned<LegSource> LegSource;
        public readonly Tuned<int> LegHeightPx;
        public readonly Tuned<int> LegAdjustXPx;
        public readonly Tuned<int> LegAdjustYPx;
        public readonly Tuned<bool> SnapToGround;
        public readonly Tuned<bool> CartFollowsGround;
        public readonly Tuned<float> CartGroundMaxShift;
        public readonly Tuned<CartSlopeStyle> CartSlope;
        public readonly Tuned<float> CartPitchMaxDegrees;
        public readonly Tuned<bool> CargoDrawCorpse, CargoSwaysWithCart, CargoSetDownBehindCart;
        public readonly Tuned<float> CargoBedDepth, CargoSideGap, CargoOverhang, CargoNestle, CargoSetDownGap;
        public readonly Tuned<float> LanternAlong, LanternUp, LanternAcross, LanternTilt, LanternLean;
        public readonly Tuned<bool> LanternFlip;
        public readonly Tuned<bool> LanternHangs;
        public readonly Tuned<float> LanternSwayDegrees, LanternSwaySeconds;
        public readonly Tuned<bool> LanternShow, LanternLightOn, LanternLightShadows, LanternSelfLightOn, LanternGlowOn, LanternGlassOn;
        public readonly Tuned<float> LanternScale, LanternLightIntensity, LanternLightRange, LanternLightDayDim, LanternSelfLightIntensity, LanternSelfLightRange, LanternGlowAlpha, LanternGlowScale;
        public readonly Tuned<string> LanternLightColor;
        public readonly Tuned<float> IdleFidgetEverySeconds, IdleFidgetSpeed, IdleCrawlSpeed;
        // Sounds tab (MountSounds.cs). Volumes multiply the game's own volume for that sound (1 = as the game plays it).
        public readonly Tuned<bool> SoundsOn, HoofOn, HoofSurfaceAware, GrazeOn, BrayWhileWalking, BrayInSfxChannel,
            CargoThumpOn, WheelOn, WheelPitchFollowsSpeed, BellOnSetOff, BellOnlyWithCargo;
        public readonly Tuned<float> SoundVolume, HoofVolume, HoofPitch, HoofPitchVariation, HoofPhase, GrazeVolume, LipsVolume,
            BrayOnMountChance, BrayIdleEverySeconds, BrayVolume, BrayPitch, CargoThumpVolume, CargoThumpPitch,
            WheelVolume, WheelPitch, WheelPhase, BellVolume, BellPitch, BellSetOffIdleSeconds;
        public readonly Tuned<int> HoofStepsPerCycle, WheelEveryCycles;
        public readonly Tuned<string> HoofSoundId, GrazeSoundId, BraySoundId, CargoThumpSoundId, WheelSoundId, BellSoundId;

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
            CartSlope = cfg.Bind(section, "CartSlope", Defaults.Get(section, "CartSlope", CartSlopeStyle.WholeCartPitch),
                "Cart style, with CartFollowsGround: how the cart meets a slope. WholeCartPitch: the wheels follow the ground and the cart (body, shafts, pole, cargo) pitches about the axle so the shafts stay at the donkey, as a real cart does. ShaftsTilt: the cart is lifted level and only the shafts turn about their harness end. HalfLift: the cart is lifted level and the shafts by half (a gap at both ends).");
            CartPitchMaxDegrees = cfg.Bind(section, "CartPitchMaxDegrees", Defaults.Get(section, "CartPitchMaxDegrees", 20f),
                new ConfigDescription("WholeCartPitch: the steepest the cart may pitch, in degrees. Steeper ground leaves the shafts short of the donkey instead of tipping the pole and cargo further.", new AcceptableValueRange<float>(0f, 45f)));
            CargoDrawCorpse = cfg.Bind(section, "CargoDrawCorpse", Defaults.Get(section, "CargoDrawCorpse", true),
                "Cart style only: the mod draws every corpse on the cart itself (same packer and corrections as any other item) and keeps the game's loaded-cart body off. Off: the first corpse is shown by the game's own loaded cart body with the shroud, which the packer cannot move; other items then ride on top of it.");
            CargoSwaysWithCart = cfg.Bind(section, "CargoSwaysWithCart", Defaults.Get(section, "CargoSwaysWithCart", true),
                "Cart style only: the cargo rocks with the cart while it rolls, as the game's own loaded corpse does (the walk animation rotates the cart body about the axle by up to 3 degrees; the cargo holder copies that rotation every frame). Off: the cargo stays level.");
            CargoSetDownBehindCart = cfg.Bind(section, "CargoSetDownBehindCart", Defaults.Get(section, "CargoSetDownBehindCart", true),
                "Cart style only: the interact key sets the top cart item down behind the cart (the game's own drop-spot search, run from the cart's back end outwards), out of the keeper's reach, so pressing twice sets both items down. Off: in front of the keeper, as the game drops a held item, where the next press picks it up again. When there is no free spot behind the cart the item goes in front.");
            CargoSetDownGap = cfg.Bind(section, "CargoSetDownGap", Defaults.Get(section, "CargoSetDownGap", 0.1f),
                new ConfigDescription("Cart style, with CargoSetDownBehindCart: the gap between the back end of the cart body and the near end of an item set down behind it, world units. The item's own measured length decides where its centre goes, so every size keeps this gap. A taken spot moves the next item 0.1 further out.", new AcceptableValueRange<float>(0f, 1.5f)));
            CargoBedDepth = cfg.Bind(section, "CargoBedDepth", Defaults.Get(section, "CargoBedDepth", -0.26f),
                "Cart style only: the cargo floor relative to the measured top of the empty cart body, world units (negative = items sink below the rails onto the bed floor). Every item's measured bottom is put on this floor.");
            CargoSideGap = cfg.Bind(section, "CargoSideGap", Defaults.Get(section, "CargoSideGap", 0.02f),
                "Cart style only: gap between two items lying next to each other (across the bed or end to end along it), world units.");
            CargoOverhang = cfg.Bind(section, "CargoOverhang", Defaults.Get(section, "CargoOverhang", 0.12f),
                "Cart style only: how far two items lying next to each other may together exceed the bed's measured width (across) or length (end to end) before the second one is stacked on the first instead, world units.");
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
            LanternHangs = cfg.Bind(section, "LanternHangs", Defaults.Get(section, "LanternHangs", true),
                "Cart style only: the iron lamp hangs straight down from the pole's hook (with its lit glass, glow and lights) while the pole tips with a slope, rocks while walking, or is tilted or leaned on the Lantern tab. Off: the lamp turns with the pole, as in the game.");
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

            bool cart = section == "Cart";
            SoundsOn = B(cfg, section, "SoundsOn", true, "Master switch for all mount sounds added by the mod " + who + ".");
            SoundVolume = F(cfg, section, "SoundVolume", 1f, 0f, 3f, "Master volume multiplier for all mount sounds " + who + ".");
            HoofOn = B(cfg, section, "HoofOn", true, "Hoofsteps: the game's donkey walks silently; this plays a step sound on each footfall of its walk cycle.");
            HoofSoundId = S(cfg, section, "HoofSoundId", "", "Sound id for the hoofsteps. Empty = by the ground under the donkey (step_grass/step_dirt/step_wood/step_stone, as the game's footsteps).");
            HoofSurfaceAware = B(cfg, section, "HoofSurfaceAware", true, "With an empty HoofSoundId: pick the step sound by the surface under the donkey. Off: always step_dirt.");
            HoofVolume = F(cfg, section, "HoofVolume", 1.3f, 0f, 3f, "Hoofstep volume (x the game's footstep volume).");
            HoofPitch = F(cfg, section, "HoofPitch", 0.8f, 0.3f, 2f, "Hoofstep pitch (below 1 = heavier than the keeper's steps).");
            HoofPitchVariation = F(cfg, section, "HoofPitchVariation", 0.06f, 0f, 0.5f, "Random pitch change per hoofstep, either way, so steps do not sound identical.");
            HoofStepsPerCycle = I(cfg, section, "HoofStepsPerCycle", 4, 1, 8, "Footfalls per walk cycle of the donkey's animation (a donkey has four hooves).");
            HoofPhase = F(cfg, section, "HoofPhase", 0f, 0f, 1f, "Shifts the footfalls within the walk cycle (fraction of one step), to line them up with the art.");
            GrazeOn = B(cfg, section, "GrazeOn", true, "Play a chewing sound when the donkey grazes while standing (the game has the sound, made for the graze animation, but never plays it).");
            GrazeSoundId = S(cfg, section, "GrazeSoundId", "donkey_eating_grass", "Sound id for grazing.");
            GrazeVolume = F(cfg, section, "GrazeVolume", 1f, 0f, 3f, "Graze sound volume.");
            LipsVolume = F(cfg, section, "LipsVolume", 1f, 0f, 3f, "Volume of the lip-smacking the game's own donkey animation plays (0 = silent).");
            BraySoundId = S(cfg, section, "BraySoundId", "voice_donkey", "Sound id for brays (the donkey's dialogue voice: short ee-aw sounds).");
            BrayOnMountChance = F(cfg, section, "BrayOnMountChance", 0.5f, 0f, 1f, "Chance the donkey brays when you mount (0 = never, 1 = always).");
            BrayIdleEverySeconds = F(cfg, section, "BrayIdleEverySeconds", 40f, 0f, 300f, "On average one bray this often while standing (randomised by half either way). 0 = no idle brays.");
            BrayWhileWalking = B(cfg, section, "BrayWhileWalking", false, "Idle brays can also happen while moving.");
            BrayVolume = F(cfg, section, "BrayVolume", 1f, 0f, 3f, "Bray volume.");
            BrayPitch = F(cfg, section, "BrayPitch", 1f, 0.3f, 2f, "Bray pitch.");
            BrayInSfxChannel = B(cfg, section, "BrayInSfxChannel", true, "Route brays through the game's SFX volume. Off: the dialogue-voice channel the game uses for this sound (speech volume).");
            CargoThumpOn = B(cfg, section, "CargoThumpOn", true, "Cart: a thump when an item lands in the cart (on top of the game's pickup sound).");
            CargoThumpSoundId = S(cfg, section, "CargoThumpSoundId", "oh_wood_container_drop", "Sound id for an item landing in the cart.");
            CargoThumpVolume = F(cfg, section, "CargoThumpVolume", 1f, 0f, 3f, "Cargo thump volume.");
            CargoThumpPitch = F(cfg, section, "CargoThumpPitch", 1f, 0.3f, 2f, "Cargo thump pitch.");
            WheelOn = B(cfg, section, "WheelOn", cart, "Cart: a wooden wheel rumble once per walk cycle while the cart moves (the game has no cart sound; this borrows a machine wheel).");
            WheelSoundId = S(cfg, section, "WheelSoundId", "conveyor_furnace_wheel", "Sound id for the wheels (conveyor_assembly_wheel is the chain-and-rev alternative).");
            WheelVolume = F(cfg, section, "WheelVolume", 0.6f, 0f, 3f, "Wheel volume.");
            WheelPitch = F(cfg, section, "WheelPitch", 1f, 0.3f, 2f, "Wheel pitch.");
            WheelPitchFollowsSpeed = B(cfg, section, "WheelPitchFollowsSpeed", false, "Raise the wheel pitch with the walk animation speed (faster = higher).");
            WheelEveryCycles = I(cfg, section, "WheelEveryCycles", 1, 1, 4, "Play the wheel sound every this many walk cycles.");
            WheelPhase = F(cfg, section, "WheelPhase", 0f, 0f, 1f, "Shifts the wheel sound within its cycle.");
            BellSoundId = S(cfg, section, "BellSoundId", "donkey_bell", "Sound id for the bell (the donkey delivery's bell).");
            BellOnSetOff = B(cfg, section, "BellOnSetOff", cart, "Ring the bell when the donkey sets off after standing still a while.");
            BellOnlyWithCargo = B(cfg, section, "BellOnlyWithCargo", true, "Only ring on setting off when there is cargo on the cart.");
            BellSetOffIdleSeconds = F(cfg, section, "BellSetOffIdleSeconds", 4f, 0f, 60f, "How long the donkey must have stood still before setting off rings the bell.");
            BellVolume = F(cfg, section, "BellVolume", 0.7f, 0f, 3f, "Bell volume.");
            BellPitch = F(cfg, section, "BellPitch", 1f, 0.3f, 2f, "Bell pitch.");
        }

        private static ConfigEntry<bool> B(ConfigFile cfg, string section, string key, bool def, string desc)
        {
            return cfg.Bind(section, key, Defaults.Get(section, key, def), desc);
        }

        private static ConfigEntry<string> S(ConfigFile cfg, string section, string key, string def, string desc)
        {
            return cfg.Bind(section, key, Defaults.Get(section, key, def), desc);
        }

        private static ConfigEntry<float> F(ConfigFile cfg, string section, string key, float def, float min, float max, string desc)
        {
            return cfg.Bind(section, key, Defaults.Get(section, key, def), new ConfigDescription(desc, new AcceptableValueRange<float>(min, max)));
        }

        private static ConfigEntry<int> I(ConfigFile cfg, string section, string key, int def, int min, int max, string desc)
        {
            return cfg.Bind(section, key, Defaults.Get(section, key, def), new ConfigDescription(desc, new AcceptableValueRange<int>(min, max)));
        }

        // Every sound entry, for the Sounds tab's reset and the defaults export.
        public ConfigEntryBase[] SoundEntries => new ConfigEntryBase[]
        {
            SoundsOn, SoundVolume, HoofOn, HoofSoundId, HoofSurfaceAware, HoofVolume, HoofPitch, HoofPitchVariation, HoofStepsPerCycle, HoofPhase,
            GrazeOn, GrazeSoundId, GrazeVolume, LipsVolume,
            BraySoundId, BrayOnMountChance, BrayIdleEverySeconds, BrayWhileWalking, BrayVolume, BrayPitch, BrayInSfxChannel,
            CargoThumpOn, CargoThumpSoundId, CargoThumpVolume, CargoThumpPitch,
            WheelOn, WheelSoundId, WheelVolume, WheelPitch, WheelPitchFollowsSpeed, WheelEveryCycles, WheelPhase,
            BellSoundId, BellOnSetOff, BellOnlyWithCargo, BellSetOffIdleSeconds, BellVolume, BellPitch,
        };

        public void ResetSounds()
        {
            foreach (ConfigEntryBase e in SoundEntries)
            {
                Defaults.ResetToDefault(e);
            }
        }

        // Every setting of this style, in the order defaults.cfg lists them.
        public ConfigEntryBase[] AllEntries
        {
            get
            {
                var list = new System.Collections.Generic.List<ConfigEntryBase>
                {
                    SpeedMultiplier, Depth, CenterOffset, RiderOffsetPx, TorsoOffsetPx,
                    AutoSeat, SeatAdjustPx, SeatOffset, Bob, Pose, FreezePoseWhenIdle, PoseSpeed, PoseSpeedFollowsDonkey,
                    NearLeg, LegSource, LegHeightPx, LegAdjustXPx, LegAdjustYPx, SnapToGround, CartFollowsGround, CartGroundMaxShift, CartSlope, CartPitchMaxDegrees,
                    CargoDrawCorpse, CargoSwaysWithCart, CargoSetDownBehindCart, CargoSetDownGap, CargoBedDepth, CargoSideGap, CargoOverhang, CargoNestle,
                    LanternAlong, LanternUp, LanternAcross, LanternTilt, LanternLean, LanternFlip, LanternHangs,
                    LanternSwayDegrees, LanternSwaySeconds, IdleFidgetEverySeconds, IdleFidgetSpeed, IdleCrawlSpeed,
                    LanternShow, LanternScale, LanternLightOn, LanternLightIntensity, LanternLightRange, LanternLightColor, LanternLightShadows, LanternLightDayDim,
                    LanternSelfLightOn, LanternSelfLightIntensity, LanternSelfLightRange, LanternGlowOn, LanternGlowAlpha, LanternGlowScale, LanternGlassOn,
                };
                list.AddRange(SoundEntries);
                return list.ToArray();
            }
        }

        // Back to the mod defaults (Defaults.DefaultFor: the defaults file as last written, not only as built).
        public void ResetAll()
        {
            foreach (ConfigEntryBase e in AllEntries)
            {
                Defaults.ResetToDefault(e);
            }
        }
    }

    // A rideable mount for Graveyard Keeper 2. Press the mount key and a donkey appears under the keeper; you walk
    // faster while riding. The donkey is a temporary view only (the game's build-preview mechanism): it is never
    // added to the world data, so nothing about it is saved. See README.md and FEASIBILITY.md.
    [BepInPlugin(Guid, "GK2 Mounts", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.mounts";
        public const string Version = "0.1.0";

        // Module version id of the Assembly-CSharp.dll this plugin was audited against (Steam build 25601286).
        private static readonly System.Guid AuditedGameMvid = new System.Guid("3ffadd70-169d-402a-bcdf-59c322dfc26d");
        // And of LazyBearTechnology.dll (the engine library the plugin also uses), same build (Steam 25601286).
        private static readonly System.Guid AuditedLazyBearMvid = new System.Guid("8a7a5e04-396d-4525-a779-3f0de1341398");

        internal static ManualLogSource Log;

        // Set when the application starts closing (Application.quitting runs before the plugin's OnDestroy).
        internal static bool Quitting;
        internal static Plugin Instance;

        // [General]: behaviour
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> MountWgoId;
        // Ride behaviour: the core's defaults, overridable per session through Mounts.Behaviour (the showcase mod sets
        // them from its own settings). No keys here: the core mounts only when a mod asks (Mounts.TryMount).
        internal static Tuned<bool> StayMountedThroughDoors;
        internal static Tuned<bool> DismountIndoors;
        internal static Tuned<bool> ShowMessages;
        internal static ConfigEntry<bool> InstanceControls;
        internal static ConfigEntry<KeyCode> HitchKey;

        // [Ride] / [Cart]: position, pose, animation, legs, speed
        internal static StyleSettings Ride;
        internal static StyleSettings Cart;
        internal static StyleSettings For(MountStyle style) => style == MountStyle.Cart ? Cart : Ride;

        // [Debug]
        internal static ConfigEntry<bool> TuningKeys;
        internal static ConfigEntry<bool> TuningWindowEnabled;
        internal static ConfigEntry<KeyCode> TuningWindowKey;
        internal static bool TuningWindowHidden; // this session only, toggled by TuningWindowKey (e.g. for screenshots)
        internal static ConfigEntry<float> TuningWindowScale;
        internal static ConfigEntry<float> TuningWindowFontScale;
        internal static ConfigEntry<float> TuningWindowWidth;

        // [Safety]
        internal static ConfigEntry<bool> DisableOnGameUpdate;

        private Harmony harmony;

        // True once every patch is in: the API (Mounts.IsAvailable) refuses everything before that and after a
        // game update disabled the mod.
        internal static bool Patched;

        private void Awake()
        {
            Log = Logger;
            Log.LogEvent += (_, __) => Perf.NoteLogLine(); // the panel's log lines per second
            Instance = this;
            Application.quitting += () => Quitting = true;
            Enabled = Config.Bind("General", "Enabled", true,
                "Master switch for the mount. GK2 Mounts does nothing on its own: a mod built on it (such as the showcase mod) summons and dismounts the donkey. Off: no mod can mount (the kill switch if one misbehaves).");
            MountWgoId = Config.Bind("General", "MountWgoId", "npc_donkey",
                "World object id used for the mount's looks. Only npc_donkey has been tested.");
            StayMountedThroughDoors = Config.Bind("General", "StayMountedThroughDoors", Defaults.Get("General", "StayMountedThroughDoors", true),
                "Keep riding after a scene change (the donkey is re-summoned in the new scene). Off: dismount at every door.");
            DismountIndoors = Config.Bind("General", "DismountIndoors", Defaults.Get("General", "DismountIndoors", true),
                "Dismount automatically when entering a building, church or dungeon (the game's indoor lighting presets), and refuse to mount while indoors. The cart cannot avoid furniture and door frames inside.");
            ShowMessages = Config.Bind("General", "ShowMessages", Defaults.Get("General", "ShowMessages", true),
                "Show a thought bubble when the keeper cannot mount and why.");
            InstanceControls = Config.Bind("General", "InstanceControls", Defaults.Get("General", "InstanceControls", true),
                "Donkeys and carts a mod left standing in the world (Mounts.SpawnDonkey / SpawnCart): the interact key next to a standing donkey rides it (empty hands), and next to a free cart while riding a donkey without its cart hitches it; a label over it says so. Off: only mods can do that (MountHandle.Ride, Mounts.Hitch).");
            HitchKey = Config.Bind("General", "HitchKey", Defaults.Get("General", "HitchKey", KeyCode.J),
                "With InstanceControls, while riding: leave the cart standing where it is (a free cart with its cargo and settings), or take the free cart in reach. None: the key is off (the interact key still hitches).");


            Ride = new StyleSettings(Config, "Ride",
                speed: 1.75f, depth: 0.32f, center: 0.48f, riderPx: -4, torsoPx: -3,
                autoSeat: false, seatAdjustPx: -6, seatOffset: 0.32f, bob: true, pose: RiderPose.FishingIdle, freeze: true,
                legs: false, legSource: LegSource.Body, legHeight: 15, legX: 3, legY: 1);
            Cart = new StyleSettings(Config, "Cart",
                speed: 1.5f, depth: 0f, center: 0f, riderPx: 0, torsoPx: 0,
                autoSeat: false, seatAdjustPx: 0, seatOffset: 0f, bob: false, pose: RiderPose.Walk, freeze: false,
                legs: false, legSource: LegSource.Body, legHeight: 10, legX: 0, legY: 0);
            // [Colors] and [Colors.<region>]: the same donkey in both styles, so one set (Colors.cs).
            Colors.Bind(Config);
            // General.IdleShuffle and [Shuffle]: standing donkeys without a cart take a few slow steps (IdleShuffle.cs).
            IdleShuffle.Bind(Config);

            TuningWindowEnabled = Config.Bind("Debug", "TuningWindow", false,
                "Dev tool for mod authors: a tuning panel for every mount value in the top-right corner while mounted, plus the investigation tools on its Debug tab (and F5). Values apply live and are saved to this file as you change them.");
            TuningWindowKey = Config.Bind("Debug", "TuningWindowKey", KeyCode.F11,
                "Hides or shows the tuning panel, e.g. for screenshots. Only for this session: the panel is back after a restart. The game doesn't use F11 on its own (it can't be bound in its controls; Shift+F11 is its Workshop creator). None = no key.");
            TuningWindowScale = Config.Bind("Debug", "TuningWindowScale", 1.5f,
                "Overall size of the tuning panel (1 = Unity default, 1.5 suits 1440p). Text and width scale with it.");
            TuningWindowFontScale = Config.Bind("Debug", "TuningWindowFontScale", 1f,
                "Text size inside the tuning panel, relative (1 = normal). Lower it so long rows fit.");
            TuningWindowWidth = Config.Bind("Debug", "TuningWindowWidth", 470f,
                "Width of the tuning panel in panel units before the overall size is applied (470 = original).");
            TuningKeys = Config.Bind("Debug", "TuningKeys", false,
                "While mounted: PageUp/PageDown move the torso, Home/End the legs sideways, Insert/Delete the legs up/down, one art pixel per press, editing the current style's values. Development aid, turn off for normal play.");

            DisableOnGameUpdate = Config.Bind("Safety", "DisableOnGameUpdate", true,
                "Do not patch anything if the game's code has changed since this mod was audited. Rebuild/re-audit after game updates.");

            CargoLayout.Load(); // cargo-layout.cfg: the repo file on the machine with the sources, else the embedded copy
            Log.LogInfo("Cargo layout: " + CargoLayout.Summary());

            System.Guid gameMvid = typeof(WgoData).Assembly.ManifestModule.ModuleVersionId;
            System.Guid lazyBearMvid = typeof(LazyBearTechnology.LazyAudio).Assembly.ManifestModule.ModuleVersionId;
            if (gameMvid != AuditedGameMvid || lazyBearMvid != AuditedLazyBearMvid)
            {
                if (DisableOnGameUpdate.Value)
                {
                    Log.LogError($"Game code changed since this mod was audited (Assembly-CSharp {gameMvid}, expected {AuditedGameMvid}; LazyBearTechnology {lazyBearMvid}, expected {AuditedLazyBearMvid}). Mod is DISABLED; the game runs unmodified. Set Safety.DisableOnGameUpdate=false to force-enable at your own risk.");
                    return;
                }
                Log.LogWarning($"Game code changed since this mod was audited (Assembly-CSharp {gameMvid}, LazyBearTechnology {lazyBearMvid}). Running anyway because DisableOnGameUpdate=false.");
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
            Patched = true;
            // With the dev panel on (Debug.TuningWindow), the [perf] line is written every 10 s from the start: a
            // measurement no longer depends on remembering the panel's switch (it can still be switched off there).
            Perf.LogEvery10s = TuningWindowEnabled.Value;
            MainGame.OnGameStarted = (Action)Delegate.Combine(MainGame.OnGameStarted, new Action(Mount.OnGameStarted));
            Log.LogInfo($"GK2 Mounts {Version} loaded (API {Mounts.ApiVersion}); nothing happens until a mod uses it.");
        }

        // LateUpdate: runs after the game moved the player for this frame, so the donkey follows without lag.
        private void LateUpdate()
        {
            if (harmony == null)
            {
                return;
            }
            if (!Enabled.Value)
            {
                // Switched off mid-ride: hand the cargo back and restore the keeper instead of leaving a frozen
                // donkey, a raised speed and the cart reroute behind (the patches stay installed until restart).
                if (Mount.IsMounted || Mount.All.Count > 0)
                {
                    Mount.ForceCleanup("disabled in the config");
                }
                return;
            }
            // Investigation: F5 writes the keeper's held-item view to the log (works on foot too, unlike the panel).
            if (TuningWindowEnabled.Value && UnityInput.Current.GetKeyDown(KeyCode.F5))
            {
                HeldItemProbe.Write();
            }
            // Plain key only: Shift+F11 opens the game's Workshop creator (when enabled), so leave that combination to it.
            if (TuningWindowKey.Value != KeyCode.None && UnityInput.Current.GetKeyDown(TuningWindowKey.Value)
                && !UnityInput.Current.GetKey(KeyCode.LeftShift) && !UnityInput.Current.GetKey(KeyCode.RightShift))
            {
                TuningWindowHidden = !TuningWindowHidden;
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
                using (Perf.Measure(Perf.S.Panel))
                {
                    TuningWindow.Draw();
                }
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
            Patched = false;
            MainGame.OnGameStarted = (Action)Delegate.Remove(MainGame.OnGameStarted, new Action(Mount.OnGameStarted));
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}
