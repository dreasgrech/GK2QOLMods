using System;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GK2.Framework;

namespace GK2TechTreeSearch
{
    // Optional GK2 Mod Framework integration (SuperMan4eg's, docs/gk2-mod-framework.md): with the framework
    // installed, the mod and its settings show in the game's Mods menu. The same DLL works without it.
    //
    // Only Register and TechTreeSearchMod touch GK2.Framework types. Mono loads GK2.Framework.dll the first time a method
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
            FrameworkApi.RegisterMod(new TechTreeSearchMod(), config);
        }

        private sealed class TechTreeSearchMod : Gk2ModBase
        {
            // Our own switches stay in charge (General.Enabled, the dependency check): the framework adds no
            // Enabled entry of its own. requiresKnownBuild makes its list say "Unknown build" unless OnRegister
            // confirms the build, which it does only when our own check found every game member identical (Status
            // "Active", ConfirmBuild below; on changed game code the Status row names the members).
            private readonly Gk2ModMetadata metadata = new Gk2ModMetadata(
                Plugin.Guid,
                "GK2 Tech Tree Search",
                "dreas",
                Plugin.Version,
                "A search box on the Tech Tree page: type part of a tech's name, or of something it unlocks, and jump to it. Display only.",
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
                    () => Plugin.Status == "Active" && !Plugin.Enabled.Value ? "Off (\"Search box on the Tech Tree\" is unchecked)" : Plugin.Status,
                    order: -10);
                // The same Section/Key/type as Plugin's binds, so BepInEx hands back those entries: the menu edits
                // the values the mod reads. Each is read when the page opens or on every search, so a change applies
                // the next time the Tech Tree page is opened.
                Toggle(s, Plugin.Enabled, "Search box on the Tech Tree", 0);
                Toggle(s, Plugin.SearchUnlocks, "Also search what techs unlock", 1);
                // The menu's key capture saves combinations as main key + modifiers, which KeyboardShortcut.IsDown
                // reads the same way. It can't set None (Esc cancels): set FocusKey = None in the cfg for no key.
                s.AddKeybind(Plugin.FocusKey.Definition.Section, Plugin.FocusKey.Definition.Key, (KeyboardShortcut)Plugin.FocusKey.DefaultValue,
                    "Key to start a search",
                    "Puts the cursor in the search box while the Tech Tree page is open. Clicking the box works too. To switch the key off, set FocusKey = None in the cfg.",
                    order: 2);
                // Not in the menu: General.MaxResults and Layout.* (the framework's controls don't fit them well;
                // set them in the cfg), and Safety.StopOnChangedGameCode (read once at startup; the Status row says when
                // it applies).
            }

            private static void Toggle(Gk2Settings s, ConfigEntry<bool> entry, string name, int order)
            {
                s.AddToggle(entry.Definition.Section, entry.Definition.Key, (bool)entry.DefaultValue, name,
                    entry.Description.Description, order);
            }
        }
    }
}
