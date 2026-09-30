using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GK2.Framework;

namespace GK2Showcase
{
    // Optional GK2 Mod Framework integration (SuperMan4eg's, docs/gk2-mod-framework.md): with the framework
    // installed, the mod and its settings show in the game's Mods menu. The same DLL works without it.
    //
    // Only Register and ShowcaseMod touch GK2.Framework types. Mono loads GK2.Framework.dll the first time a method
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
            FrameworkApi.RegisterMod(new ShowcaseMod(), config);
        }

        private sealed class ShowcaseMod : Gk2ModBase
        {
            // The mod has no Enabled switch of its own (GK2 Mounts' General.Enabled is the kill switch), and the
            // framework adds none: frameworkManagesEnabledState false.
            private readonly Gk2ModMetadata metadata = new Gk2ModMetadata(
                Plugin.Guid,
                "GK2 Showcase",
                "dreas",
                Plugin.Version,
                "Ride the donkey (H) or drive its cart (G). Built on GK2 Mounts, which it needs.",
                supportsRuntimeToggle: false,
                requiresKnownBuild: false,
                frameworkManagesEnabledState: false);

            private readonly IReadOnlyList<Gk2ModDependency> dependencies = new[]
            {
                new Gk2ModDependency(FrameworkGuid, "0.1.0", "0.2.0")
            };

            public override Gk2ModMetadata Metadata => metadata;

            public override IReadOnlyList<Gk2ModDependency> Dependencies => dependencies;

            public override void OnRegister(Gk2ModContext context)
            {
                Gk2Settings s = context.Settings;
                Plugin p = Plugin.Instance;
                s.AddReadOnly("Keys", "Status", "Status", "What the mod is doing right now.", () => Plugin.Status, order: -10);
                // The same Section/Key/type as Plugin's binds, so BepInEx hands back those entries: the menu edits the
                // values the mod reads. Keys are read every frame; the behaviour switches reach GK2 Mounts through
                // their SettingChanged handlers at once.
                Key(s, p.rideKey, "Ride the donkey", 0);
                Key(s, p.cartKey, "Drive the cart", 1);
                Key(s, p.bellKey, "Ring the bell", 2);
                Key(s, p.sendAwayKey, "Send the donkey away", 3);
                Toggle(s, p.dismountIndoors, "Dismount indoors", 0);
                Toggle(s, p.stayThroughDoors, "Stay mounted through doors to other areas", 1);
                Toggle(s, p.showMessages, "Say why mounting is refused", 2);
            }

            private static void Key(Gk2Settings s, ConfigEntry<KeyboardShortcut> entry, string name, int order)
            {
                s.AddKeybind(entry.Definition.Section, entry.Definition.Key, (KeyboardShortcut)entry.DefaultValue, name,
                    entry.Description.Description, order);
            }

            private static void Toggle(Gk2Settings s, ConfigEntry<bool> entry, string name, int order)
            {
                s.AddToggle(entry.Definition.Section, entry.Definition.Key, (bool)entry.DefaultValue, name,
                    entry.Description.Description, order);
            }
        }
    }
}
