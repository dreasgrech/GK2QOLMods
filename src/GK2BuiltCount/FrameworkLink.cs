using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GK2.Framework;

namespace GK2BuiltCount
{
    // Optional GK2 Mod Framework integration (SuperMan4eg's, docs/gk2-mod-framework.md): with the framework
    // installed, the mod and its settings show in the game's Mods menu. The same DLL works without it.
    //
    // Only Register and BuiltCountMod touch GK2.Framework types. Mono loads GK2.Framework.dll the first time a method
    // using its types is compiled, so as long as nothing calls Register while the framework is absent, the DLL is
    // never needed. tests/FrameworkScan checks that no other code references the framework.
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
            FrameworkApi.RegisterMod(new BuiltCountMod(), config);
        }

        private sealed class BuiltCountMod : Gk2ModBase
        {
            // Our own switches stay in charge (General.Enabled, the dependency check): the framework adds no
            // Enabled entry of its own. requiresKnownBuild makes its list say "Unknown build" unless OnRegister
            // confirms the build, which it does only when our own check found every game member identical (Status
            // "Active", ConfirmBuild below); on changed game code the mod runs but the list says "Unknown build" and
            // the Status row names the members. Without it the list said "Compatible" while the mod had switched
            // itself off (game 1.008, 2026-10-02).
            private readonly Gk2ModMetadata metadata = new Gk2ModMetadata(
                Plugin.Guid,
                "GK2 Built Count",
                "dreas",
                Plugin.Version,
                "Build menus show how many of each building you already have in that area, e.g. \"Sawhorse (2)\". Display only.",
                supportsRuntimeToggle: false,
                requiresKnownBuild: true,
                frameworkManagesEnabledState: false);

            public override Gk2ModMetadata Metadata => metadata;

            // No Dependencies: the menu printed the framework there with no "(optional)", which read as if the mod
            // needed it. The mod never does (soft BepInDependency), and a framework whose API changed fails
            // Register inside TryRegister's catch, so the mod runs on without its menu entry.

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
                    () => Plugin.Status == "Active" && !Plugin.Enabled.Value ? "Off (\"Show built counts\" is unchecked)" : Plugin.Status,
                    order: -10);
                // The same Section/Key/type as Plugin's bind, so BepInEx hands back that entry: the menu edits the
                // value the mod reads. It's read on every row redraw, so a change shows the next time a build menu
                // tab is drawn.
                ConfigEntry<bool> enabled = Plugin.Enabled;
                s.AddToggle(enabled.Definition.Section, enabled.Definition.Key, (bool)enabled.DefaultValue,
                    "Show built counts", enabled.Description.Description, order: 0);
                // Not in the menu: Safety.StopOnChangedGameCode (read once at startup; the Status row says when it
                // applies) and the Safety.LastDependencyCheck cache. They stay in the cfg.
            }
        }
    }
}
