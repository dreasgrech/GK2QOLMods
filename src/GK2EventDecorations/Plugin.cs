using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Shared;
using HarmonyLib;

namespace GK2EventDecorations
{
    [BepInPlugin(Guid, "GK2 Event Decorations", Version)]
    // Soft: when GK2 Mod Framework is installed it loads first, so FrameworkLink can register; without it nothing changes.
    [BepInDependency(FrameworkLink.FrameworkGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.eventdecorations";
        public const string Version = "1.0.0";

        // No pinned game build: at start GameDependencies compares every game member this DLL uses (the gk2deps.txt
        // manifest tools/DepManifest embedded after the build) with the installed game; docs/dependency-check.md.

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SendGhostHome;

        // Every Harmony patch class, patched by name. Not PatchAll(assembly): that lists every type in this DLL, and
        // FrameworkLink's mod class can't be loaded without GK2.Framework.dll, so Harmony would log a warning.
        // tests/FrameworkScan fails if a [HarmonyPatch] class is missing here. Events a newer game adds are patched
        // by Events.PatchDiscovered.
        internal static readonly Type[] PatchClasses =
        {
            typeof(TemporaryEventDates_IsHalloweenEvent_Patch),
            typeof(TemporaryEventDates_IsHalloweenEventDate_Patch),
            typeof(HalloweenConditionalLoadContentPart_ShouldLoad_Patch),
            typeof(HalloweenHeadSkinOverride_Resolve_Patch),
            typeof(WorldData_AddMissingConditionalContent_Patch)
        };

        // What the mod is doing, for the Mods menu's status row.
        internal static string Status = "not started";

        private Harmony harmony;
        private bool listening;

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
                "Off: every event follows the game's own dates, as without the mod.");
            Events.Bind(Config); // one section per event: Mode, Start, End; Halloween and any event a newer game adds
            Halloween.Bind(Config); // Halloween's pieces, areas and kinds
            SendGhostHome = Config.Bind(Events.HalloweenName, "SendGhostHome", true,
                "While Halloween is off, a ghost left standing in the village is sent home. The game loses the last step of " +
                "the ghost's morning walk home when the save is made during it, and leaves the ghost in the village after the event.");
            GameDependencies.Bind(Config); // Safety.StopOnChangedGameCode, Safety.LastDependencyCheck
        }

        private string StartMod()
        {
            // Identical game members: patch. Changed ones: patch with a warning (or stop, Safety.StopOnChangedGameCode).
            // A member gone, or a check that could not run: stop; the game runs unmodified.
            GameDependencies.Result deps = GameDependencies.Check(typeof(Plugin).Assembly, typeof(WgoData).Assembly, typeof(LazyBearTechnology.LazyAudio).Assembly);
            if (!GameDependencies.Apply(deps, "GK2 Event Decorations", Log, "Running anyway", out string status))
            {
                return status;
            }

            harmony = new Harmony(Guid);
            try
            {
                foreach (Type patch in PatchClasses)
                {
                    harmony.PatchAll(patch);
                }
                Events.PatchDiscovered(harmony);
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                harmony = null;
                Log.LogError($"Patching failed; all patches removed, game runs unmodified. {e}");
                return "Disabled: patching failed (see BepInEx/LogOutput.log).";
            }
            MainGame.OnGameStarted = (Action)Delegate.Combine(MainGame.OnGameStarted, (Action)GhostHome.OnGameStarted);
            MainGame.OnGameStarted = (Action)Delegate.Combine(MainGame.OnGameStarted, (Action)Decorations.OnGameStarted);
            Config.SettingChanged += Decorations.OnSettingChanged; // any setting: the decorations follow at once
            listening = true;
            Log.LogInfo($"GK2 Event Decorations {Version} loaded. Events: {Events.Describe()}.");
            return status;
        }

        private void Update()
        {
            if (harmony != null)
            {
                GhostHome.Tick();
            }
        }

        private void OnDestroy()
        {
            if (listening)
            {
                MainGame.OnGameStarted = (Action)Delegate.Remove(MainGame.OnGameStarted, (Action)GhostHome.OnGameStarted);
                MainGame.OnGameStarted = (Action)Delegate.Remove(MainGame.OnGameStarted, (Action)Decorations.OnGameStarted);
                Config.SettingChanged -= Decorations.OnSettingChanged;
                listening = false;
            }
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}
