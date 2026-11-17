using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GK2.Framework;
using HarmonyLib;

namespace GK2TechTreeSearch
{
    // Optional GK2 Mod Framework integration (SuperMan4eg's, docs/gk2-mod-framework.md). The options themselves are the
    // mod's own window (the settings page in Plugin.BindConfig, the window code in lib\GK2DreasUI), the same for every
    // player and in the game's language: the framework isn't on the Steam Workshop. With the framework installed the mod
    // is in its Mods menu too, and its Settings button opens that window: one set of rows, nothing kept twice (the user,
    // 2026-11-17; GK2 Event Decorations and GK2 Donkey API do the same. Up to 1.0.2 the menu had rows of its own: the
    // status, the two switches and the key).
    // The framework has no public way to say what Settings does: it opens its own settings page
    // (ModsMenuWindow.OpenSettingsPage(RegisteredMod), private), and only for a mod with a setting. So the entry has one
    // setting, a button opening the window, and a Harmony prefix on that method opens the window straight away for this
    // mod. The prefix is found by its exact signature and is an extra: if a framework version changes the method, it isn't
    // applied (said once), and Settings shows the one button.
    //
    // Only Register and TechTreeSearchMod touch GK2.Framework types. Mono loads GK2.Framework.dll the first time a method
    // using its types is compiled, so as long as nothing calls Register while the framework is absent, the DLL is
    // never needed. tests/FrameworkScan checks that no other code references the framework.
    internal static class FrameworkLink
    {
        internal const string FrameworkGuid = "ru.superman4eg.gk2.framework";

        internal static void TryRegister(ConfigFile config, SettingsPage page)
        {
            if (!Chainloader.PluginInfos.TryGetValue(FrameworkGuid, out var info) || info.Instance == null)
            {
                return;
            }
            try
            {
                Register(config, page);
            }
            catch (Exception e)
            {
                // e.g. a framework version whose API changed: the mod runs on, its options open from the Settings window
                Plugin.Log.LogWarning($"GK2 Mod Framework {info.Metadata.Version} is installed, but registering with it failed, so this mod is not in its Mods menu; its options open from the game's Settings window. {e.GetType().Name}: {e.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Register(ConfigFile config, SettingsPage page)
        {
            TechTreeSearchMod.Page = page;
            FrameworkApi.RegisterMod(new TechTreeSearchMod(), config);
            TechTreeSearchMod.PatchSettingsButton();
        }

        private sealed class TechTreeSearchMod : Gk2ModBase
        {
            internal static SettingsPage Page;

            // Our own switches stay in charge (General.Enabled, the dependency check): the framework adds no
            // Enabled entry of its own. requiresKnownBuild makes its list say "Unknown build" unless OnRegister
            // confirms the build, which it does only when our own check found every game member identical (Status
            // "Active"; on changed game code the options window's Status row names the members).
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

            public override void OnRegister(Gk2ModContext context)
            {
                if (Plugin.Status == "Active")
                {
                    try
                    {
                        context.ConfirmCurrentBuildCompatibility("Its own check passed: every game member it uses is identical to the audited build.");
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning("GK2 Mod Framework didn't take the build confirmation (" + e.Message + "); its Mods menu may show this mod as not compatible. The mod itself works as normal.");
                    }
                }
                // One setting: a button opening the options window. The framework's Settings button is live only for a mod
                // with a setting; the prefix below opens the window from it straight away, and without the prefix the
                // framework's page shows this button. No cfg entry (a button binds nothing).
                context.Settings.AddButton("Options", "OpenOptions", "GK2 Tech Tree Search options",
                    "Opens the options window: every setting of GK2 Tech Tree Search.", () => "Open", OpenWindow, 1);
            }

            private static void OpenWindow()
            {
                try
                {
                    OptionsWindow.Open(Page); // over the Mods menu, which waits underneath and is shown again on Escape
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Mods menu: opening the options of GK2 Tech Tree Search failed: {e.Message}");
                }
            }

            private static bool patchTried, prefixFailed;

            // The framework's Settings button for this mod opens the options window, not the framework's page. Tried once.
            internal static void PatchSettingsButton()
            {
                if (patchTried) return;
                patchTried = true;
                try
                {
                    // System.Type's own reflection, by the exact signature (one method, never an overload picked by name): a
                    // lookup meant to survive the method's absence (the dependency manifest follows lookups into the game,
                    // not into the framework)
                    Type window = Type.GetType("GK2.Framework.ModsMenuWindow, GK2.Framework");
                    Type registered = Type.GetType("GK2.Framework.RegisteredMod, GK2.Framework");
                    MethodInfo open = window != null && registered != null
                        ? window.GetMethod("OpenSettingsPage", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { registered }, null)
                        : null;
                    if (open == null)
                    {
                        Plugin.Log.LogInfo("Settings: GK2 Mod Framework's Mods menu has no OpenSettingsPage(RegisteredMod) (another version): its Settings button shows one button, which opens the options window.");
                        return;
                    }
                    new Harmony(Plugin.Guid + ".modsmenu").Patch(open, prefix: new HarmonyMethod(typeof(TechTreeSearchMod), nameof(OpenSettingsPrefix)));
                    Plugin.Log.LogInfo("Settings: GK2 Mod Framework's Settings button opens the options window.");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogInfo($"Settings: GK2 Mod Framework's Settings button could not be pointed at the options window ({e.GetType().Name}: {e.Message}); it shows one button, which opens it.");
                }
            }

            // false: the window is open instead of the framework's page; true: the framework goes on (another mod's).
            private static bool OpenSettingsPrefix(RegisteredMod mod)
            {
                if (prefixFailed || mod == null || Page == null) return true;
                try
                {
                    if (string.Equals(mod.Metadata.Id, Plugin.Guid, StringComparison.OrdinalIgnoreCase))
                    {
                        OptionsWindow.Open(Page);
                        return false;
                    }
                }
                catch (Exception e)
                {
                    prefixFailed = true; // the framework's own page from now on (the button on it opens the window)
                    Plugin.Log.LogWarning($"Mods menu: the Settings button could not open the options window ({e.Message}); it opens the framework's page from now on.");
                }
                return true;
            }
        }
    }
}
