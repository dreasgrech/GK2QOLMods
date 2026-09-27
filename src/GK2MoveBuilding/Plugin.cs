using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GK2MoveBuilding
{
    [BepInPlugin(Guid, "GK2 Move Building", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "anvil.gk2.movebuilding";
        public const string Version = "1.1.3";

        // Module version id of the Assembly-CSharp.dll this plugin was audited against.
        private static readonly System.Guid AuditedGameMvid = new System.Guid("97f92f0d-a093-49fe-ba4d-74aa62fae81e");

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowMoveRow;
        internal static ConfigEntry<string> MoveLabel;
        internal static ConfigEntry<KeyboardShortcut> MoveModifier;
        internal static ConfigEntry<bool> StayInMoveMode;
        internal static ConfigEntry<bool> HighlightMovable;
        internal static ConfigEntry<Color> MovableColor;
        internal static ConfigEntry<string> FaultInjection;
        internal static ConfigEntry<bool> DisableOnGameUpdate;

        private Harmony harmony;
        private bool updateErrorLogged;
        private static Plugin instance;

        // Re-reads the config file (one small file read per move). Lets every setting change without a restart.
        internal static void ReloadConfig()
        {
            try
            {
                instance?.Config.Reload();
            }
            catch (Exception e)
            {
                Log.LogWarning($"Config reload failed: {e.Message}");
            }
        }

        private void Awake()
        {
            Log = Logger;
            instance = this;
            Enabled = Config.Bind("General", "Enabled", true,
                "Lets a built building be picked up and put down elsewhere in the same area. The building keeps its contents, crafts and identity.");
            ShowMoveRow = Config.Bind("General", "ShowMoveRow", true,
                "Adds a \"Move\" row next to \"Remove\" in the build desk menu. Pick it, then click a building.");
            MoveLabel = Config.Bind("General", "MoveLabel", "Move",
                "Text of the Move row.");
            MoveModifier = Config.Bind("General", "MoveModifier", new KeyboardShortcut(KeyCode.LeftControl),
                "Hold this key in the game's own Remove mode and click a building to move it instead of demolishing it. Set to None to disable.");
            StayInMoveMode = Config.Bind("General", "StayInMoveMode", true,
                "After a move (or after cancelling the preview with Back) return to move mode instead of the desk menu. Back in move mode still opens the menu.");
            HighlightMovable = Config.Bind("General", "HighlightMovable", true,
                "In move mode, buildings that can be moved lose the red grid patch and get a colour tint; the one under the cursor is tinted stronger. Buildings that can't be moved keep the game's red.");
            MovableColor = Config.Bind("General", "MovableColor", new Color(0.30f, 0.75f, 1f, 1f),
                "Tint colour for movable buildings in move mode.");
            FaultInjection = Config.Bind("Debug", "FaultInjection", "",
                "Testing only. Throws inside the move at the named step so the restore path can be watched in the log; the move is then refused and the building must be exactly as before. " +
                "Values: AfterLinksCut, AfterZoneRemove, AfterPosition, AfterZoneAdd, WrongZone. Leave empty for normal use.");
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
                // Every private member the mod touches is resolved here; a missing one disables the whole mod.
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
            Log.LogInfo($"GK2 Move Building {Version} loaded.");
        }

        private void Update()
        {
            if (harmony == null || !Mover.HasWork)
            {
                return;
            }
            try
            {
                Mover.Tick();
            }
            catch (Exception e)
            {
                if (!updateErrorLogged)
                {
                    updateErrorLogged = true;
                    Log.LogError($"Move step failed; the move is cancelled. {e}");
                }
                Mover.Cancel("an error occurred");
            }
        }

        private void OnDestroy()
        {
            Mover.Cancel("the mod is unloading");
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}
