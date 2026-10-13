using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GK2.Framework;

namespace GK2OwnedCount
{
    // Optional GK2 Mod Framework integration (SuperMan4eg's, docs/gk2-mod-framework.md): with the framework
    // installed, the mod and its settings show in the game's Mods menu. The same DLL works without it.
    //
    // Only Register and OwnedCountMod touch GK2.Framework types. Mono loads GK2.Framework.dll the first time a method
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
            FrameworkApi.RegisterMod(new OwnedCountMod(), config);
        }

        private sealed class OwnedCountMod : Gk2ModBase
        {
            // Our own switches stay in charge (General.Enabled, the dependency check): the framework adds no
            // Enabled entry of its own. requiresKnownBuild makes its list say "Unknown build" unless OnRegister
            // confirms the build, which it does only when our own check found every game member identical (Status
            // "Active", ConfirmBuild below; on changed game code the Status row names the members). Without it the
            // list said "Compatible" while the mod had switched itself off (game 1.008, 2026-10-02).
            private readonly Gk2ModMetadata metadata = new Gk2ModMetadata(
                Plugin.Guid,
                "GK2 Owned Count",
                "dreas",
                Plugin.Version,
                "Item tooltips show how many of that item you own: on you plus every storage chest in every area. Display only.",
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
                    () => Plugin.Status == "Active" && !Plugin.Enabled.Value ? "Off (\"Show the owned count\" is unchecked)" : Plugin.Status,
                    order: -10);
                // The same Section/Key/type as Plugin's binds, so BepInEx hands back those entries: the menu edits
                // the values the mod reads. All of them apply at once (read on every tooltip).
                Toggle(s, Plugin.Enabled, "Show the owned count", 0);
                Toggle(s, Plugin.OnlyOnOwnTooltip, "Only on the item's own tooltip", 1);
                Toggle(s, Plugin.ShowBreakdown, "Show on you / in storage", 2);
                Toggle(s, Plugin.ShowPerArea, "Show the count per area", 3);
                Toggle(s, Plugin.IncludeOtherGrades, "Also total all grades", 4);
                Toggle(s, Plugin.IncludeFuelContainers, "Count fuel containers", 0);
                Toggle(s, Plugin.IncludeConveyorStorage, "Count conveyor chests", 1);
                // Not in the menu: Safety.StopOnChangedGameCode (read once at startup; the Status row says when it
                // applies) and Debug.DumpItemDefs. Both stay in the cfg.
            }

            private static void Toggle(Gk2Settings s, ConfigEntry<bool> entry, string name, int order)
            {
                s.AddToggle(entry.Definition.Section, entry.Definition.Key, (bool)entry.DefaultValue, name,
                    entry.Description.Description, order);
            }
        }
    }
}
