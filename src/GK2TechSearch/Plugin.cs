using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Shared;
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
        public const string Version = "1.0.1"; // one place: the plugin attribute and the log line

        // No pinned game build: at start GameDependencies compares every game member this DLL uses (the gk2deps.txt
        // manifest tools/DepManifest embedded after the build) with the installed game; docs/dependency-check.md.

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SearchUnlocks;
        internal static ConfigEntry<int> MaxResults;
        internal static ConfigEntry<KeyboardShortcut> FocusKey;
        internal static ConfigEntry<Vector2> BoxOffset;
        internal static ConfigEntry<Vector2> BoxSize;
        internal static ConfigEntry<float> ListWidth;

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
            // Defaults from the measured page (2026-10-04): 640 x 385 units, tab buttons end about 165 right of centre,
            // tab strip centred 168.5 above it. The box fills the free strip right of the tabs.
            BoxOffset = Config.Bind("Layout", "BoxOffset", new Vector2(-8f, -13f),
                "Where the box sits, from the top right corner of the Tech Tree page (x left is negative, y down is negative). Applies the next time the page opens.");
            BoxSize = Config.Bind("Layout", "BoxSize", new Vector2(130f, 22f),
                "Width and height of the box. Applies the next time the page opens.");
            ListWidth = Config.Bind("Layout", "ListWidth", 280f,
                "Width of the result list under the box (right edges aligned; never narrower than the box). Applies the next time the page opens.");
            GameDependencies.Bind(Config); // Safety.StopOnChangedGameCode, Safety.LastDependencyCheck
        }

        private string StartMod()
        {
            // Identical game members: patch. Changed ones: patch with a warning (or stop, Safety.StopOnChangedGameCode).
            // A member gone, or a check that could not run: stop; the game runs unmodified.
            GameDependencies.Result deps = GameDependencies.Check(typeof(Plugin).Assembly, typeof(WgoData).Assembly, typeof(LazyBearTechnology.LazyAudio).Assembly);
            if (!GameDependencies.Apply(deps, "GK2 Tech Search", Log, "Running anyway", out string status))
            {
                return status;
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
            return status;
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}
