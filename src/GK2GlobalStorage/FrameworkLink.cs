using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GK2.Framework;

namespace GK2GlobalStorage
{
    // Optional GK2 Mod Framework integration (SuperMan4eg's, docs/gk2-mod-framework.md): with the framework
    // installed, the mod and its settings show in the game's Mods menu. The same DLL works without it.
    //
    // Only Register and GlobalStorageMod touch GK2.Framework types. Mono loads GK2.Framework.dll the first time a
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
            FrameworkApi.RegisterMod(new GlobalStorageMod(), config);
        }

        private sealed class GlobalStorageMod : Gk2ModBase
        {
            // Our own switches stay in charge (General.Enabled, the game-update guard): the framework adds no
            // Enabled entry of its own. requiresKnownBuild makes its list say "Unknown build" unless OnRegister
            // confirms the build, which it does only when our own guard passed (ConfirmBuild below). Without it the
            // list said "Compatible" while the guard had switched the mod off (game 1.008, 2026-10-02).
            private readonly Gk2ModMetadata metadata = new Gk2ModMetadata(
                Plugin.Guid,
                "GK2 Global Storage",
                "dreas",
                Plugin.Version,
                "Craft and build with materials from storage chests in every area, not just the one you're standing in.",
                supportsRuntimeToggle: false,
                requiresKnownBuild: true,
                frameworkManagesEnabledState: false);

            private readonly IReadOnlyList<Gk2ModDependency> dependencies = new[]
            {
                new Gk2ModDependency(FrameworkGuid, "0.1.0", "0.2.0")
            };

            public override Gk2ModMetadata Metadata => metadata;

            public override IReadOnlyList<Gk2ModDependency> Dependencies => dependencies;

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
                    () => Plugin.Status == "Active" && !Plugin.Enabled.Value ? "Off (\"Craft from every area\" is unchecked)" : Plugin.Status,
                    order: -10);
                // The same Section/Key/type as Plugin's binds, so BepInEx hands back those entries: the menu edits
                // the values the mod reads. The patches read them each time the game gathers materials, so a change
                // applies the next time a bench, build desk or picker asks.
                Toggle(s, Plugin.Enabled, "Craft from every area", 0);
                Toggle(s, Plugin.IncludeBuildAndAlchemy, "Also build, alchemy, gardens and more", 1);
                Toggle(s, Plugin.IncludeFuelContainers, "Use fuel containers in other areas", 0);
                Toggle(s, Plugin.IncludeConveyorStorage, "Use conveyor chests in other areas", 1);
                Toggle(s, Plugin.IncludeZombieOrderDelivery, "Zombie orders from every area", 2);
                // Not in the menu: Safety.DisableOnGameUpdate (read once at startup; the Status row says when it
                // applies) and Debug.VerboseLogging. Both stay in the cfg.
            }

            private static void Toggle(Gk2Settings s, ConfigEntry<bool> entry, string name, int order)
            {
                s.AddToggle(entry.Definition.Section, entry.Definition.Key, (bool)entry.DefaultValue, name,
                    entry.Description.Description, order);
            }
        }
    }
}
