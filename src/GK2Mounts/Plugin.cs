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
    public enum RiderPose
    {
        Walk = 1,
        Idle = 0,
        WorkHands = 29,
        FishingIdle = 203,
        FishingCast = 202,
        FishingPull = 204,
        FocusedWalk = 41,
        ToolHammer = 23,
        ToolShovel = 21,
        ToolAxe = 20,
        Planting = 100,
        FlagCapture = 35,
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
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> MountKey;
        internal static ConfigEntry<MountStyle> Style;
        internal static ConfigEntry<float> SpeedMultiplier;
        internal static ConfigEntry<string> MountWgoId;
        internal static ConfigEntry<bool> StayMountedThroughDoors;
        internal static ConfigEntry<bool> ShowMessages;
        internal static ConfigEntry<bool> SideViewOnly;
        internal static ConfigEntry<RiderPose> Pose;
        internal static ConfigEntry<float> RideSeatOffset;
        internal static ConfigEntry<float> RideDepth;
        internal static ConfigEntry<float> RideCenterOffset;
        internal static ConfigEntry<bool> AutoSeat;
        internal static ConfigEntry<int> SeatAdjustPx;
        internal static ConfigEntry<bool> NearLeg;
        internal static ConfigEntry<LegSource> LegSourceSetting;
        internal static ConfigEntry<int> LegHeightPx;
        internal static ConfigEntry<int> LegAdjustXPx;
        internal static ConfigEntry<int> LegAdjustYPx;
        internal static ConfigEntry<bool> TuningKeys;
        internal static ConfigEntry<float> CartSeatOffset;
        internal static ConfigEntry<float> CartDepth;
        internal static ConfigEntry<float> CartCenterOffset;
        internal static ConfigEntry<bool> DisableOnGameUpdate;

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Master switch. Press the mount key to summon a donkey under the keeper and ride it; press again to dismount.");
            MountKey = Config.Bind("General", "MountKey", KeyCode.H,
                "Key that mounts/dismounts. Pick one the game does not use.");
            Style = Config.Bind("General", "Style", MountStyle.Ride,
                "Ride: sit on the donkey's back. Cart: the donkey pulls its cart and the keeper stands in it. Takes effect on the next mount.");
            SpeedMultiplier = Config.Bind("General", "SpeedMultiplier", 1.75f,
                new ConfigDescription("Walking speed while mounted, as a multiple of normal (measured: exactly linear).", new AcceptableValueRange<float>(0.5f, 4f)));
            MountWgoId = Config.Bind("General", "MountWgoId", "npc_donkey",
                "World object id used for the mount's looks. Only npc_donkey has been tested.");
            StayMountedThroughDoors = Config.Bind("General", "StayMountedThroughDoors", true,
                "Keep riding after a scene change (the donkey is re-summoned in the new scene). Off: dismount at every door.");
            Pose = Config.Bind("Ride", "Pose", RiderPose.FishingIdle,
                "Keeper's body pose while riding. Idle: standing still (no walk cycle). FishingIdle / WorkHands / etc.: the game's animator state of that name, for poses with the hands forward. Walk: unchanged (arms swing while riding).");
            SideViewOnly = Config.Bind("General", "SideViewOnly", true,
                "The donkey only has left/right art. On: while mounted the keeper also only faces left or right (walking up/down keeps the last horizontal facing), so rider and mount match. Off: the keeper turns as usual and the donkey shows its side.");
            ShowMessages = Config.Bind("General", "ShowMessages", true,
                "Show a thought bubble when the keeper cannot mount and why.");

            RideSeatOffset = Config.Bind("Ride", "SeatOffset", -0.15f,
                "Vertical offset of the keeper's sprite while riding. Negative lowers the keeper so the legs hide behind the donkey's body.");
            RideDepth = Config.Bind("Ride", "Depth", -0.15f,
                "Donkey offset towards the camera (negative) or away from it (positive), in world units. Negative draws it in front of the keeper's legs.");
            RideCenterOffset = Config.Bind("Ride", "CenterOffset", 0.48f,
                "The donkey sprite is drawn this far to its facing side of its position (prefab offset). The mount is shifted back by this amount so the body sits under the keeper.");

            AutoSeat = Config.Bind("Ride", "AutoSeat", true,
                "Compute the seat height from the sprites themselves (rider's hip on the donkey's back line). Off: use Ride.SeatOffset.");
            SeatAdjustPx = Config.Bind("Ride", "SeatAdjustPx", -3,
                "Fine adjustment of the automatic seat, in art pixels (positive = higher).");
            NearLeg = Config.Bind("Ride", "NearLeg", true,
                "Draw the rider's near leg hanging down the donkey's flank, in the keeper's outfit colours.");
            LegSourceSetting = Config.Bind("Ride", "LegSource", LegSource.Body,
                "Body: the keeper's own trousers and boots, re-sliced from his current body frame (follows the pose, outfit-exact). Drawn: a hand-drawn bent leg in outfit colours.");
            LegHeightPx = Config.Bind("Ride", "LegHeightPx", 10,
                "With LegSource=Body: how many bottom rows of the body frame count as legs (feet to belt), in art pixels.");
            LegAdjustXPx = Config.Bind("Ride", "LegAdjustXPx", 0,
                "Fine adjustment of the leg's hip position along the donkey, in art pixels (positive = towards the rear).");
            LegAdjustYPx = Config.Bind("Ride", "LegAdjustYPx", 0,
                "Fine adjustment of the leg's hip height, in art pixels (positive = higher).");

            CartSeatOffset = Config.Bind("Cart", "SeatOffset", 0f, "Vertical offset of the keeper's sprite in cart style.");
            CartDepth = Config.Bind("Cart", "Depth", 0f, "Donkey depth offset in cart style (see Ride.Depth).");
            CartCenterOffset = Config.Bind("Cart", "CenterOffset", 0f,
                "Centering in cart style. 0 leaves the donkey ahead of the keeper, who then stands in the cart.");

            TuningKeys = Config.Bind("Debug", "TuningKeys", true,
                "While mounted: PageUp/PageDown move the seat, Home/End the leg sideways, Insert/Delete the leg up/down, one art pixel per press; values are logged so they can be copied into the Ride settings. Development aid, turn off for normal play.");

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
            Log.LogInfo($"GK2 Mounts {Version} loaded. Mount key: {MountKey.Value}, style {Style.Value}, speed x{SpeedMultiplier.Value}.");
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
