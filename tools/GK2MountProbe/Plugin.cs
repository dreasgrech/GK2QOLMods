using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GK2MountProbe
{
    // Development probe for the GK2 Mounts mod. Not a gameplay mod: it writes one report file with everything
    // we need to design the mount (player and donkey prefab hierarchies, animators, physics, camera, WGO
    // definitions) and offers a few hotkey experiments (speed multiplier, a donkey that follows the player,
    // a rider seat offset). Everything it spawns is a temp object (like the build preview) and is never saved.
    [BepInPlugin(Guid, "GK2 Mount Probe", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dreas.gk2.mountprobe";
        public const string Version = "0.1.0";

        private static readonly System.Guid AuditedGameMvid = new System.Guid("3ffadd70-169d-402a-bcdf-59c322dfc26d");
        // And of LazyBearTechnology.dll (the engine library the plugin also uses), same build (Steam 25601286).
        private static readonly System.Guid AuditedLazyBearMvid = new System.Guid("8a7a5e04-396d-4525-a779-3f0de1341398");

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        internal static ConfigEntry<bool> AutoReport;
        internal static ConfigEntry<float> ReportDelaySeconds;
        internal static ConfigEntry<string> SampleIds;
        internal static ConfigEntry<float> SampleLifetimeSeconds;
        internal static ConfigEntry<float> SampleSpacing;
        internal static ConfigEntry<KeyCode> ReportKey;
        internal static ConfigEntry<KeyCode> SpeedKey;
        internal static ConfigEntry<KeyCode> FollowKey;
        internal static ConfigEntry<KeyCode> DepthKey;
        internal static ConfigEntry<KeyCode> SeatUpKey;
        internal static ConfigEntry<KeyCode> SeatDownKey;
        internal static ConfigEntry<KeyCode> CartKey;
        internal static ConfigEntry<bool> FollowWithCart;
        internal static ConfigEntry<float> FollowCenterOffset;
        internal static ConfigEntry<KeyCode> CenterKey;
        internal static ConfigEntry<float> TestSpeedMultiplier;
        internal static ConfigEntry<string> FollowWgoId;
        internal static ConfigEntry<float> FollowDepthOffset;
        internal static ConfigEntry<float> SeatHeight;
        internal static ConfigEntry<bool> LogStateChanges;
        internal static ConfigEntry<bool> DumpSprites;
        internal static ConfigEntry<bool> DisableOnGameUpdate;

        private Harmony harmony;
        private bool wasInGame;
        private float inGameSince;
        private bool reportedThisSession;
        private Coroutine reportCoroutine;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            AutoReport = Config.Bind("General", "AutoReport", true,
                "Write the report automatically a few seconds after a save is loaded (once per load).");
            ReportDelaySeconds = Config.Bind("General", "ReportDelaySeconds", 6f,
                "Seconds to wait after the save is loaded before writing the report.");
            SampleIds = Config.Bind("General", "SampleIds", "npc_donkey,cow,npc_donkey_test",
                "Comma-separated WGO ids to spawn as temporary samples next to the player for the report (and screenshots). They are destroyed after SampleLifetimeSeconds and never saved.");
            SampleLifetimeSeconds = Config.Bind("General", "SampleLifetimeSeconds", 25f,
                "How long the spawned samples stay visible for screenshots.");
            SampleSpacing = Config.Bind("General", "SampleSpacing", 2.5f,
                "World-unit spacing between spawned samples, to the right of the player.");
            LogStateChanges = Config.Bind("General", "LogStateChanges", true,
                "Log player state-machine and animation-state transitions to the BepInEx log.");
            DumpSprites = Config.Bind("General", "DumpSprites", true,
                "Dump the character sprite atlases as PNG plus a CSV index of all sprites (name, rect, pivot) and the skin palettes to BepInEx/GK2-sprites.");

            ReportKey = Config.Bind("Keys", "Report", KeyCode.F8, "Write the report again (spawns samples again).");
            SpeedKey = Config.Bind("Keys", "SpeedTest", KeyCode.F9, "Toggle the speed multiplier experiment.");
            FollowKey = Config.Bind("Keys", "FollowTest", KeyCode.F10, "Toggle a temporary FollowWgoId that follows the player and mirrors walk/idle and facing.");
            DepthKey = Config.Bind("Keys", "CycleDepth", KeyCode.F11, "Cycle the follower's depth offset (towards/away from camera).");
            CartKey = Config.Bind("Keys", "ToggleCart", KeyCode.F12, "Show/hide the follower's cart, wheels and lantern.");
            SeatUpKey = Config.Bind("Keys", "SeatUp", KeyCode.PageUp, "Raise the rider (player view) by 0.05.");
            SeatDownKey = Config.Bind("Keys", "SeatDown", KeyCode.PageDown, "Lower the rider (player view) by 0.05.");

            TestSpeedMultiplier = Config.Bind("Experiments", "SpeedMultiplier", 1.75f, "Multiplier applied by the speed experiment.");
            FollowWgoId = Config.Bind("Experiments", "FollowWgoId", "npc_donkey", "WGO id spawned by the follow experiment.");
            FollowDepthOffset = Config.Bind("Experiments", "FollowDepthOffset", -0.15f, "Initial depth offset of the follower, in world units away from the camera (negative = towards the camera, in front of the rider's legs).");
            FollowWithCart = Config.Bind("Experiments", "FollowWithCart", false, "Spawn the follower with its cart, wheels and lantern visible.");
            FollowCenterOffset = Config.Bind("Experiments", "FollowCenterOffset", 0.48f,
                "The donkey body sprite sits 0.48 units to the side of its data position (prefab offset, flips with facing). The follower is shifted by this much against the facing so the body is centred under the player. 0 disables.");
            CenterKey = Config.Bind("Keys", "ToggleCentering", KeyCode.F7, "Toggle the facing-aware centering of the follower.");
            SeatHeight = Config.Bind("Experiments", "SeatHeight", 0.5f, "Initial rider height offset while the follower is active.");

            DisableOnGameUpdate = Config.Bind("Safety", "DisableOnGameUpdate", true,
                "Do nothing if the game's code has changed since this probe was written.");

            System.Guid gameMvid = typeof(WgoData).Assembly.ManifestModule.ModuleVersionId;
            System.Guid lazyBearMvid = typeof(LazyBearTechnology.LazyAudio).Assembly.ManifestModule.ModuleVersionId;
            if (gameMvid != AuditedGameMvid || lazyBearMvid != AuditedLazyBearMvid)
            {
                if (DisableOnGameUpdate.Value)
                {
                    Log.LogError($"Game code changed since this probe was written (Assembly-CSharp {gameMvid}, expected {AuditedGameMvid}; LazyBearTechnology {lazyBearMvid}, expected {AuditedLazyBearMvid}). Probe is DISABLED.");
                    return;
                }
                Log.LogWarning($"Game code changed since this probe was written (Assembly-CSharp {gameMvid}, LazyBearTechnology {lazyBearMvid}). Running anyway.");
            }

            harmony = new Harmony(Guid);
            try
            {
                harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                harmony = null;
                Log.LogError($"Patching failed; probe disabled. {e}");
                return;
            }
            Log.LogInfo($"GK2 Mount Probe {Version} loaded. Report: {Report.OutputPath}. Keys: {ReportKey.Value}=report, {SpeedKey.Value}=speed x{TestSpeedMultiplier.Value}, {FollowKey.Value}=follow {FollowWgoId.Value}, {DepthKey.Value}=depth, {CartKey.Value}=cart, {CenterKey.Value}=centering, {SeatUpKey.Value}/{SeatDownKey.Value}=seat.");
        }

        internal static bool IsInGame(out PlayerController pc, out GameScene scene)
        {
            pc = null;
            scene = null;
            try
            {
                if (MainGame.Instance == null || MainGame.PlayerData == null)
                {
                    return false;
                }
                pc = MainGame.PlayerController;
                if (pc == null || !pc.TryGetCurrentGameScene(out scene) || scene == null)
                {
                    return false;
                }
                return MainGame.Instance.GameSave != null;
            }
            catch
            {
                return false;
            }
        }

        private void Update()
        {
            if (harmony == null)
            {
                return;
            }
            try
            {
                bool inGame = IsInGame(out PlayerController pc, out GameScene scene);
                if (inGame && !wasInGame)
                {
                    inGameSince = Time.unscaledTime;
                    reportedThisSession = false;
                }
                if (!inGame && wasInGame)
                {
                    Experiments.Reset("left game");
                }
                wasInGame = inGame;
                if (!inGame)
                {
                    return;
                }

                if (AutoReport.Value && !reportedThisSession && Time.unscaledTime - inGameSince >= ReportDelaySeconds.Value)
                {
                    reportedThisSession = true;
                    StartReport();
                }
                if (UnityInput.Current.GetKeyDown(ReportKey.Value))
                {
                    StartReport();
                }

                Experiments.Tick(pc, scene);
            }
            catch (Exception e)
            {
                Log.LogError($"Probe update failed: {e}");
            }
        }

        private void StartReport()
        {
            if (reportCoroutine != null)
            {
                Log.LogWarning("Report already running.");
                return;
            }
            reportCoroutine = StartCoroutine(ReportCoroutine());
        }

        private IEnumerator ReportCoroutine()
        {
            IEnumerator e = Report.WriteCoroutine();
            while (true)
            {
                bool moved;
                try
                {
                    moved = e.MoveNext();
                }
                catch (Exception ex)
                {
                    Log.LogError($"Report failed: {ex}");
                    break;
                }
                if (!moved)
                {
                    break;
                }
                yield return e.Current;
            }
            reportCoroutine = null;
        }

        private void OnDestroy()
        {
            try
            {
                Experiments.Reset("plugin destroyed");
                Report.DestroySamples();
            }
            catch
            {
            }
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}
