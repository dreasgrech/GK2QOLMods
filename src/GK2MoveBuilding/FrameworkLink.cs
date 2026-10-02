using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GK2.Framework;

namespace GK2MoveBuilding
{
    // Optional GK2 Mod Framework integration (SuperMan4eg's, docs/gk2-mod-framework.md): with the framework
    // installed, the mod and its settings show in the game's Mods menu. The same DLL works without it.
    //
    // Only Register and MoveBuildingMod touch GK2.Framework types. Mono loads GK2.Framework.dll the first time a
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
            FrameworkApi.RegisterMod(new MoveBuildingMod(), config);
        }

        private sealed class MoveBuildingMod : Gk2ModBase
        {
            // Our own switches stay in charge (General.Enabled, the game-update guard): the framework adds no
            // Enabled entry of its own. requiresKnownBuild makes its list say "Unknown build" unless OnRegister
            // confirms the build, which it does only when our own guard passed (ConfirmBuild below). Without it the
            // list said "Compatible" while the guard had switched the mod off (game 1.008, 2026-10-02).
            private readonly Gk2ModMetadata metadata = new Gk2ModMetadata(
                Plugin.Guid,
                "GK2 Move Building",
                "dreas",
                Plugin.Version,
                "Pick up a built building and put it down somewhere else in the same area, contents and all.",
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
                    () => Plugin.Status == "Active" && !Plugin.Enabled.Value ? "Off (\"Allow moving buildings\" is unchecked)" : Plugin.Status,
                    order: -10);
                // The same Section/Key/type as Plugin's binds, so BepInEx hands back those entries: the menu edits
                // the values the mod reads. Each is read where it's used (the desk menu, a click, every drawn frame),
                // so a change applies the next time that happens. Each move also re-reads the cfg file, which the
                // framework has already written (BepInEx saves on every change), so the two never disagree.
                Toggle(s, Plugin.Enabled, "Allow moving buildings", 0);
                Toggle(s, Plugin.ShowMoveRow, "Move row in the build desk menu", 1);
                s.AddText(Plugin.MoveLabel.Definition.Section, Plugin.MoveLabel.Definition.Key, (string)Plugin.MoveLabel.DefaultValue,
                    "Move row text", "Text of the Move row. Applies the next time the build desk menu opens.", order: 2);
                // The menu's key capture can't set None (Esc cancels) and saves combinations as main key + modifiers;
                // the mod checks the main key only. Hence its own description here.
                s.AddKeybind(Plugin.MoveModifier.Definition.Section, Plugin.MoveModifier.Definition.Key, (KeyboardShortcut)Plugin.MoveModifier.DefaultValue,
                    "Hold to move in Remove mode",
                    "Hold this key in the game's own Remove mode and click a building to move it instead of demolishing it. Press one key; only that key counts. To switch this off, set MoveModifier = None in the cfg.",
                    order: 3);
                Toggle(s, Plugin.StayInMoveMode, "Stay in move mode after a move", 4);
                Toggle(s, Plugin.HighlightMovable, "Tint movable buildings", 5);
                // Not in the menu: General.MovableColor (the framework has no colour control; set it in the cfg),
                // Safety.DisableOnGameUpdate (read once at startup; the Status row says when it applies) and
                // Debug.FaultInjection (testing only).
            }

            private static void Toggle(Gk2Settings s, ConfigEntry<bool> entry, string name, int order)
            {
                s.AddToggle(entry.Definition.Section, entry.Definition.Key, (bool)entry.DefaultValue, name,
                    entry.Description.Description, order);
            }
        }
    }
}
