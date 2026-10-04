using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GK2TechSearch
{
    [BepInPlugin(Guid, "GK2 Tech Search", Version)]
    // Soft: when GK2 Mod Framework is installed it loads first, so FrameworkLink can register; without it nothing changes.
    [BepInDependency(FrameworkLink.FrameworkGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.techsearch";
        public const string Version = "1.0.0"; // one place: the plugin attribute and the log line

        // Module version id of the Assembly-CSharp.dll this plugin was audited against (Steam build 25676698).
        private static readonly System.Guid AuditedGameMvid = new System.Guid("f7d46132-dd54-4296-8f19-917330ceadd9");
        // And of LazyBearTechnology.dll (the engine library the plugin also uses), same build (Steam 25676698).
        private static readonly System.Guid AuditedLazyBearMvid = new System.Guid("f67c6993-6db1-4f08-8352-cf7315263736");

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SearchUnlocks;
        internal static ConfigEntry<int> MaxResults;
        internal static ConfigEntry<KeyboardShortcut> FocusKey;
        internal static ConfigEntry<Vector2> BoxOffset;
        internal static ConfigEntry<Vector2> BoxSize;
        internal static ConfigEntry<bool> DisableOnGameUpdate;

        // Every Harmony patch class, patched by name. Not PatchAll(assembly): that lists every type in this DLL, and
        // FrameworkLink's mod class can't be loaded without GK2.Framework.dll, so Harmony would log a warning.
        // tests/FrameworkScan fails if a [HarmonyPatch] class is missing here.
        internal static readonly Type[] PatchClasses =
        {
            typeof(TechTreePageWidget_Init_Patch),
            typeof(TechTreePageWidget_Redraw_Patch),
            typeof(TechTreePageWidget_Hide_Patch),
        };

        // What the mod is doing, for the Mods menu's status row.
        internal static string Status = "not started";

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            BindConfig();
            Status = StartMod(); // not "Start": Unity would call that itself on the first frame and patch twice
            FrameworkLink.TryRegister(Config);
        }

        private void BindConfig()
        {
            Enabled = Config.Bind("General", "Enabled", true,
                "A search box on the Tech Tree page: type part of a tech's name (or of something it unlocks) and jump to it. Hidden techs and locked tabs are never shown. Display only.");
            SearchUnlocks = Config.Bind("General", "SearchUnlocks", true,
                "Also match what a tech unlocks (the items, buildings and perks shown on its card), e.g. \"beer\" finds Brewing.");
            MaxResults = Config.Bind("General", "MaxResults", 8,
                new ConfigDescription("How many results the list under the box shows at most.", new AcceptableValueRange<int>(1, 20)));
            FocusKey = Config.Bind("General", "FocusKey", new KeyboardShortcut(KeyCode.F, KeyCode.LeftControl),
                "Puts the cursor in the search box while the Tech Tree page is open. Clicking the box works too. None: no key.");
            BoxOffset = Config.Bind("Layout", "BoxOffset", new Vector2(-24f, -20f),
                "Where the box sits, from the top right corner of the Tech Tree page (x left is negative, y down is negative). Applies the next time the page opens.");
            BoxSize = Config.Bind("Layout", "BoxSize", new Vector2(240f, 30f),
                "Width and height of the box. Applies the next time the page opens.");
            DisableOnGameUpdate = Config.Bind("Safety", "DisableOnGameUpdate", true,
                "Do not patch anything if the game's code has changed since this mod was audited. Rebuild/re-audit after game updates.");
        }

        private string StartMod()
        {
            System.Guid gameMvid = typeof(WgoData).Assembly.ManifestModule.ModuleVersionId;
            System.Guid lazyBearMvid = typeof(LazyBearTechnology.LazyAudio).Assembly.ManifestModule.ModuleVersionId;
            bool forced = false;
            if (gameMvid != AuditedGameMvid || lazyBearMvid != AuditedLazyBearMvid)
            {
                if (DisableOnGameUpdate.Value)
                {
                    Log.LogError($"Game code changed since this mod was audited (Assembly-CSharp {gameMvid}, expected {AuditedGameMvid}; LazyBearTechnology {lazyBearMvid}, expected {AuditedLazyBearMvid}). Mod is DISABLED; the game runs unmodified. Set Safety.DisableOnGameUpdate=false to force-enable at your own risk.");
                    return "Disabled: the game was updated since this version was checked. Update the mod, or set Safety.DisableOnGameUpdate=false in its cfg at your own risk.";
                }
                Log.LogWarning($"Game code changed since this mod was audited (Assembly-CSharp {gameMvid}, LazyBearTechnology {lazyBearMvid}). Running anyway because DisableOnGameUpdate=false.");
                forced = true;
            }

            harmony = new Harmony(Guid);
            try
            {
                Access.Verify();
                foreach (Type patch in PatchClasses)
                {
                    harmony.PatchAll(patch);
                }
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                harmony = null;
                Log.LogError($"Patching failed; all patches removed, game runs unmodified. {e}");
                return "Disabled: patching failed (see BepInEx/LogOutput.log).";
            }
            Log.LogInfo($"GK2 Tech Search {Version} loaded.");
            return forced ? "Active on an unchecked game version (Safety.DisableOnGameUpdate=false)" : "Active";
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}
