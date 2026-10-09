using System;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GK2.Framework;

namespace GK2CorpsePickup
{
    // Optional GK2 Mod Framework integration (SuperMan4eg's, docs/gk2-mod-framework.md): with the framework
    // installed, the mod and its settings show in the game's Mods menu. The same DLL works without it.
    //
    // Only Register and CorpsePickupMod touch GK2.Framework types. Mono loads GK2.Framework.dll the first time a
    // method using its types is compiled, so as long as nothing calls Register while the framework is absent, the
    // DLL is never needed. tests/FrameworkScan checks that no other code references the framework.
    internal static class FrameworkLink
    {
        internal const string FrameworkGuid = "ru.superman4eg.gk2.framework";

        internal static void TryRegister(ConfigFile config)
        {
            if (!Chainloader.PluginInfos.TryGetValue(FrameworkGuid, out var info) || info.Instance == null)
            {
                return;
            }
            try
            {
                Register(config);
            }
            catch (Exception e)
            {
                // e.g. a framework version whose API changed: the mod runs on, only the Mods menu entry is missing
                Plugin.Log.LogWarning($"GK2 Mod Framework {info.Metadata.Version} is installed, but registering with it failed, so this mod is not in its Mods menu. The mod itself works as normal. {e.GetType().Name}: {e.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Register(ConfigFile config)
        {
            FrameworkApi.RegisterMod(new CorpsePickupMod(), config);
        }

        private sealed class CorpsePickupMod : Gk2ModBase
        {
            // Our own switches stay in charge (General.Enabled, the game-update guard): the framework adds no
            // Enabled entry of its own. requiresKnownBuild makes its list say "Unknown build" unless OnRegister
            // confirms the build, which it does only when our own guard passed (ConfirmBuild below).
            private readonly Gk2ModMetadata metadata = new Gk2ModMetadata(
                Plugin.Guid,
                "GK2 Corpse Pickup",
                "dreas",
                Plugin.Version,
                "The donkey leaves the bodies it brings at the crossroads by the nun; you carry or cart them to the morgue yourself.",
                supportsRuntimeToggle: false,
                requiresKnownBuild: true,
                frameworkManagesEnabledState: false);

            public override Gk2ModMetadata Metadata => metadata;

            // No Dependencies: the mod never needs the framework (soft BepInDependency); see Owned Count.

            private static void ConfirmBuild(Gk2ModContext context)
            {
                if (Plugin.Status != "Active")
                {
                    return;
                }
                try
                {
                    context.ConfirmCurrentBuildCompatibility("Its own check of the game code passed (the audited build).");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("GK2 Mod Framework didn't take the build confirmation (" + e.Message + "); its Mods menu may show this mod as not compatible. The mod itself works as normal.");
                }
            }

            public override void OnRegister(Gk2ModContext context)
            {
                ConfirmBuild(context);
                Gk2Settings s = context.Settings;
                s.AddReadOnly("General", "Status", "Status", "What the mod is doing right now.",
                    () => Plugin.Status == "Active" && !Plugin.Enabled.Value ? "Off (\"Leave bodies at the crossroads\" is unchecked)" : Plugin.Status,
                    order: -10);
                s.AddReadOnly("Debug", "SelfTest", "Self-test", "The last self-test run this session (Debug.SelfTestKey in the cfg starts one).",
                    () => SelfTest.Running ? "Running..." : SelfTest.Status, order: 90);
                // The same Section/Key/type as Plugin's binds, so BepInEx hands back those entries: the menu edits
                // the values the mod reads. Each is read at the moment a body is delivered, so a change applies to
                // the next delivery.
                Toggle(s, Plugin.Enabled, "Leave bodies at the crossroads", 0);
                s.AddDropdown(Plugin.DropPoint.Definition.Section, Plugin.DropPoint.Definition.Key, (string)Plugin.DropPoint.DefaultValue,
                    Plugin.DropPointChoices, "Where they are left", Plugin.DropPoint.Description.Description, order: 1);
                Toggle(s, Plugin.UnloadAtCrossroads, "Donkey unloads at the crossroads", 2);
                s.AddDropdown(Plugin.StandPoint.Definition.Section, Plugin.StandPoint.Definition.Key, (string)Plugin.StandPoint.DefaultValue,
                    Plugin.StandPointChoices, "Where the donkey walks to and unloads", Plugin.StandPoint.Description.Description, order: 3);
                Toggle(s, Plugin.BodiesNeverRot, "Bodies never rot", 4);
                s.AddText(Plugin.NotificationText.Definition.Section, Plugin.NotificationText.Definition.Key, (string)Plugin.NotificationText.DefaultValue,
                    "Notification text", Plugin.NotificationText.Description.Description, order: 5);
                // Not in the menu: Timing.* (set in the cfg), the Debug keys (testing aids) and Safety.DisableOnGameUpdate
                // (read once at startup; the Status row says when it applies).
            }

            private static void Toggle(Gk2Settings s, ConfigEntry<bool> entry, string name, int order)
            {
                s.AddToggle(entry.Definition.Section, entry.Definition.Key, (bool)entry.DefaultValue, name,
                    entry.Description.Description, order);
            }
        }
    }
}
