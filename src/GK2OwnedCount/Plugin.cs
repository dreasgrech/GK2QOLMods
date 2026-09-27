using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace GK2OwnedCount
{
    [BepInPlugin(Guid, "GK2 Owned Count", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "anvil.gk2.ownedcount";
        public const string Version = "1.0.1";

        // Module version id of the Assembly-CSharp.dll this plugin was audited against.
        private static readonly System.Guid AuditedGameMvid = new System.Guid("97f92f0d-a093-49fe-ba4d-74aa62fae81e");

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> OnlyOnOwnTooltip;
        internal static ConfigEntry<bool> ShowBreakdown;
        internal static ConfigEntry<bool> ShowPerArea;
        internal static ConfigEntry<bool> IncludeOtherGrades;
        internal static ConfigEntry<bool> IncludeFuelContainers;
        internal static ConfigEntry<bool> IncludeConveyorStorage;
        internal static ConfigEntry<bool> DisableOnGameUpdate;
        internal static ConfigEntry<bool> DumpItemDefs;

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Item tooltips get a line with how many of that item you own: on you (inventory, bags, tool belt) plus every storage chest in every area. Display only.");
            OnlyOnOwnTooltip = Config.Bind("General", "OnlyOnOwnTooltip", true,
                "Show the line only on an item's own tooltip (inventory, chest, vendor, craft result). Off: also on items embedded in other tooltips (craft ingredient rows, alchemy, dialogue).");
            ShowBreakdown = Config.Bind("General", "ShowBreakdown", true,
                "Add \"on you N · in storage N (chests)\" after the total.");
            ShowPerArea = Config.Bind("General", "ShowPerArea", false,
                "Add a second line with the count per area, e.g. \"yard 80 · cellar 36\".");
            IncludeOtherGrades = Config.Bind("General", "IncludeOtherGrades", true,
                "For graded items (the ones with a star, e.g. Bones of different skull grades) also show the total across all grades.");
            IncludeFuelContainers = Config.Bind("Storage", "IncludeFuelContainers", false,
                "Count items inside fuel containers (e.g. furnace wood piles) as storage. Same meaning as in Global Storage.");
            IncludeConveyorStorage = Config.Bind("Storage", "IncludeConveyorStorage", false,
                "Count items inside conveyor chests as storage. Same meaning as in Global Storage.");
            DisableOnGameUpdate = Config.Bind("Safety", "DisableOnGameUpdate", true,
                "Do not patch anything if the game's code has changed since this mod was audited. Rebuild/re-audit after game updates.");
            DumpItemDefs = Config.Bind("Debug", "DumpItemDefs", false,
                "Once the game data is loaded, write every item definition (id, name, type, stack size, grade, star) to BepInEx\\GK2-itemdefs.csv. Off by default.");

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
            Log.LogInfo($"GK2 Owned Count {Version} loaded.");
        }

        // Only used by the debug dump; nothing runs per frame otherwise.
        private void Update()
        {
            if (harmony != null && DumpItemDefs.Value && !ItemDefDump.Done)
            {
                ItemDefDump.TryWrite();
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}
