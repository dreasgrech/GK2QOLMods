using System;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GK2.Framework;

namespace GK2EventDecorations
{
    // Optional GK2 Mod Framework integration (SuperMan4eg's, docs/gk2-mod-framework.md): with the framework
    // installed, the mod and its settings show in the game's Mods menu. The same DLL works without it.
    //
    // Only Register and EventDecorationsMod touch GK2.Framework types. Mono loads GK2.Framework.dll the first time a
    // method using its types is compiled, so as long as nothing calls Register while the framework is absent, the DLL
    // is never needed. tests/FrameworkScan checks that no other code references the framework.
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
            FrameworkApi.RegisterMod(new EventDecorationsMod(), config);
        }

        private sealed class EventDecorationsMod : Gk2ModBase
        {
            // Our own switches stay in charge (General.Enabled, the dependency check): the framework adds no Enabled
            // entry of its own; the build is confirmed only when our own check found every game member identical
            // (the same reasoning as in GK2 Owned Count's FrameworkLink).
            private readonly Gk2ModMetadata metadata = new Gk2ModMetadata(
                Plugin.Guid,
                "GK2 Event Decorations",
                "dreas",
                Plugin.Version,
                "Choose when the game's temporary events (Halloween) show: by the game's own dates, every day, or never.",
                supportsRuntimeToggle: false,
                requiresKnownBuild: true,
                frameworkManagesEnabledState: false);

            public override Gk2ModMetadata Metadata => metadata;

            private static void ConfirmBuild(Gk2ModContext context)
            {
                if (Plugin.Status != "Active")
                {
                    return;
                }
                try
                {
                    context.ConfirmCurrentBuildCompatibility("Its own check passed: every game member it uses is identical to the audited build.");
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
                    () => Plugin.Status == "Active" && !Plugin.Enabled.Value ? "Off (\"Use these settings\" is unchecked)" : Plugin.Status,
                    order: -10);
                // The same Section/Key/type as Plugin's binds, so BepInEx hands back those entries: the menu edits the
                // values the mod reads.
                s.AddToggle(Plugin.Enabled.Definition.Section, Plugin.Enabled.Definition.Key, (bool)Plugin.Enabled.DefaultValue,
                    "Use these settings", Plugin.Enabled.Description.Description, 0);
                for (int i = 0; i < Events.All.Count; i++)
                {
                    ConfigEntry<EventMode> mode = Events.All[i].Mode;
                    s.AddEnum(mode.Definition.Section, mode.Definition.Key, (EventMode)mode.DefaultValue,
                        Events.All[i].Name, mode.Description.Description, 0);
                }
                s.AddToggle(Plugin.SendGhostHome.Definition.Section, Plugin.SendGhostHome.Definition.Key, (bool)Plugin.SendGhostHome.DefaultValue,
                    "Send the ghost home when the event is off", Plugin.SendGhostHome.Description.Description, 1);
                // Not in the menu: Safety.StopOnChangedGameCode (read once at startup; the Status row says when it
                // applies). It stays in the cfg.
            }
        }
    }
}
