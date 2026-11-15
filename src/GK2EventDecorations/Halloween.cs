using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2EventDecorations
{
    internal enum PieceMode
    {
        Event, // whatever the event's Mode says today
        On,
        Off
    }

    // Halloween's own settings: the three pieces the game ties to the event, and the decorations' areas and kinds.
    // The pieces are separate game paths (docs/seasonal-events.md, "What Halloween changes"): the decorations ask
    // HalloweenConditionalLoadContentPart.ShouldLoad, the zombie head HalloweenHeadSkinOverride.Resolve, and the ghost's
    // scripts the gate TemporaryEventDates.IsHalloweenEvent itself (its flow node and expression function).
    internal static class Halloween
    {
        internal const string AreasSection = "Halloween Areas";
        internal const string KindsSection = "Halloween Decorations";

        internal static ConfigEntry<PieceMode> Decorations;
        internal static ConfigEntry<PieceMode> Ghost;
        internal static ConfigEntry<PieceMode> ZombieHead;
        internal static ConfigEntry<bool> LiveSwitch;

        // The decorations' groups in the game's content (HalloweenDecorationsStatic/Halloween<Key>), read from game
        // 1.012's bundle with tools/game-bundles.py tree. A group the game adds later is shown, untouched.
        internal static readonly List<Area> Areas = new List<Area>
        {
            new Area("Village", "Village", "The village streets and houses outside."),
            new Area("Port", "Port", "The port."),
            new Area("PlayerZone", "Your grounds", "Outside around your graveyard and house."),
            new Area("PlayerInterior", "Your house", "Inside your house."),
            new Area("TavernInterior", "Tavern", "Inside the tavern."),
            new Area("RestaurantInterior", "Restaurant", "Inside the restaurant."),
            new Area("Barracks", "Barracks", "Outside at the barracks."),
            new Area("BarracksInterior", "Barracks inside", "Inside the barracks."),
            new Area("AstrologerInterior", "Astrologer", "Inside the astrologer's house."),
            new Area("WarehouseInterior", "Warehouse", "Inside the warehouse."),
            new Area("Dungeons", "Dungeons", "The dungeons.")
        };

        // Kinds by the item's object name (prefix, before " (n)"); an item no kind matches is always shown.
        internal static readonly List<Kind> Kinds = new List<Kind>
        {
            new Kind("Pumpkins", "Pumpkins", "Carved pumpkins and jack-o'-lanterns.", "common_hw_pumpkin"),
            new Kind("Garlands", "Garlands", "The flag garlands strung between houses.", "common_hw_flags"),
            new Kind("Scarecrows", "Scarecrows", "Scarecrows.", "common_hw_scarecrow"),
            new Kind("Pies", "Pies", "Pies set out on tables.", "common_hw_pie"),
            new Kind("Plants", "Plants", "Halloween plants.", "common_hw_plants"),
            new Kind("OtherProps", "Other props", "The other Halloween props.", "common_hw_0"),
            new Kind("Bats", "Bats and glowing eyes", "Bats flying up as you pass, circling bats and glowing eyes in the dark.", null)
        };

        internal static ConfigEntry<bool> Glow;
        internal static ConfigEntry<bool> BlockWay;

        internal sealed class Area
        {
            internal readonly string Key, Label, Text;
            internal ConfigEntry<bool> Entry;
            internal Area(string key, string label, string text) { Key = key; Label = label; Text = text; }
        }

        internal sealed class Kind
        {
            internal readonly string Key, Label, Text, Prefix;
            internal ConfigEntry<bool> Entry;
            internal Kind(string key, string label, string text, string prefix) { Key = key; Label = label; Text = text; Prefix = prefix; }
        }

        internal static void Bind(ConfigFile config)
        {
            Decorations = config.Bind(Events.HalloweenName, "Decorations", PieceMode.Event,
                "The decorations (pumpkins, garlands, scarecrows, candles, bats) in the village, the port, the dungeons, your " +
                "grounds and most interiors. Event: when Halloween is on (Mode). On / Off: always / never.");
            Ghost = config.Bind(Events.HalloweenName, "Ghost", PieceMode.Event,
                "The ghost that walks through the village at night (and any other script the game runs for Halloween). " +
                "Event: when Halloween is on (Mode). On / Off: always / never. Takes effect at its next step or evening.");
            ZombieHead = config.Bind(Events.HalloweenName, "ZombieHead", PieceMode.Event,
                "Zombies with one particular head get the game's Halloween zombie head instead. Event: when Halloween is on " +
                "(Mode). On / Off: always / never. Pictures already drawn change when the game redraws them (e.g. the next load).");
            LiveSwitch = config.Bind(Events.HalloweenName, "SwitchWithoutReload", true,
                "Put up or take down the decorations at once when a setting turns them on or off in game. Off: only when a save loads.");
            for (int i = 0; i < Areas.Count; i++)
            {
                Area a = Areas[i];
                a.Entry = config.Bind(AreasSection, a.Key, true, "Show the Halloween decorations here: " + a.Text);
            }
            for (int i = 0; i < Kinds.Count; i++)
            {
                Kind k = Kinds[i];
                k.Entry = config.Bind(KindsSection, k.Key, true, "Show these Halloween decorations: " + k.Text);
            }
            Glow = config.Bind(KindsSection, "CandleGlow", true,
                "The candles and the glow of lanterns and pumpkins. Off: the decorations stay, unlit.");
            BlockWay = config.Bind(KindsSection, "BlockTheWay", true,
                "Decorations block the way like the game's other objects. Off: you walk through them. (Villagers' paths are " +
                "worked out when a save loads: a change in game reaches them at the next load.)");
        }

        internal static bool PieceOn(ConfigEntry<PieceMode> piece)
        {
            switch (piece.Value)
            {
                case PieceMode.On:
                    return true;
                case PieceMode.Off:
                    return false;
                default:
                    return Events.Halloween.IsOn();
            }
        }

        internal static bool DecorationsOn() => Plugin.Enabled.Value ? PieceOn(Decorations) : Events.GameDates(Events.Halloween);

        // A piece left as the game has it (the mod off, or Mode Auto with the game's dates and the piece on Event): its
        // patch lets the game's own code run, as without the mod.
        internal static bool Untouched(ConfigEntry<PieceMode> piece) =>
            !Plugin.Enabled.Value || (piece.Value == PieceMode.Event && Events.Halloween.UsesGameDates());

        // The gate's answer: what the game's Halloween scripts (the ghost) see.
        internal static bool ScriptsOn() => PieceOn(Ghost);

        internal static Area AreaByKey(string key)
        {
            for (int i = 0; i < Areas.Count; i++)
            {
                if (Areas[i].Key == key)
                {
                    return Areas[i];
                }
            }
            return null;
        }

        internal static Kind KindByKey(string key)
        {
            for (int i = 0; i < Kinds.Count; i++)
            {
                if (Kinds[i].Key == key)
                {
                    return Kinds[i];
                }
            }
            return null;
        }

        internal static Kind KindOf(string objectName)
        {
            for (int i = 0; i < Kinds.Count; i++)
            {
                string p = Kinds[i].Prefix;
                if (p != null && objectName.StartsWith(p, StringComparison.Ordinal))
                {
                    return Kinds[i];
                }
            }
            return null;
        }
    }

    // Both overloads of the gate: everything in the game calls the one without a date, which calls the other with
    // DateTime.Now; the second patch keeps the answer if Mono inlines the first into a caller. With the decorations and
    // the head answered by their own patches below, what is left on the gate is the event's scripts: the ghost.
    [HarmonyPatch(typeof(TemporaryEventDates), nameof(TemporaryEventDates.IsHalloweenEvent), new Type[] { })]
    internal static class TemporaryEventDates_IsHalloweenEvent_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (Events.Bypass > 0 || Halloween.Untouched(Halloween.Ghost))
            {
                return true;
            }
            __result = Halloween.ScriptsOn();
            return false;
        }
    }

    [HarmonyPatch(typeof(TemporaryEventDates), nameof(TemporaryEventDates.IsHalloweenEvent), new Type[] { typeof(DateTime) })]
    internal static class TemporaryEventDates_IsHalloweenEventDate_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (Events.Bypass > 0 || Halloween.Untouched(Halloween.Ghost))
            {
                return true;
            }
            __result = Halloween.ScriptsOn();
            return false;
        }
    }

    // While a save loads: the decorations were just instantiated (or not); switch areas, kinds, glow and blockers before
    // the scene's navigation is scanned.
    [HarmonyPatch(typeof(WorldData), nameof(WorldData.AddMissingConditionalContent))]
    internal static class WorldData_AddMissingConditionalContent_Patch
    {
        private static void Postfix(List<GameSceneConfig> configs)
        {
            Decorations.AfterGameAdded(configs);
        }
    }

    // The decorations: asked by GameSceneConfig.CanLoadContent while a save loads, and again by the content part itself
    // when it is instantiated (also on our live load).
    [HarmonyPatch(typeof(HalloweenConditionalLoadContentPart), nameof(HalloweenConditionalLoadContentPart.ShouldLoad))]
    internal static class HalloweenConditionalLoadContentPart_ShouldLoad_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (Halloween.Untouched(Halloween.Decorations))
            {
                return true;
            }
            __result = Halloween.DecorationsOn();
            return false;
        }
    }

    // The zombie head: every head picture and every lying or carried zombie asks this (SkinPresetGK2, SkinChangerGK2,
    // UIWorkerIcon, DropViewAtomMesh). On: the table's swap whatever the date; off: every head its own.
    [HarmonyPatch(typeof(HalloweenHeadSkinOverride), nameof(HalloweenHeadSkinOverride.Resolve), new Type[] { typeof(int) })]
    internal static class HalloweenHeadSkinOverride_Resolve_Patch
    {
        private static bool tableMissing;

        private static bool Prefix(int headId, ref int __result)
        {
            if (Halloween.Untouched(Halloween.ZombieHead))
            {
                return true;
            }
            if (!Halloween.PieceOn(Halloween.ZombieHead) || tableMissing)
            {
                __result = headId;
                return false;
            }
            try
            {
                HalloweenHeadSkinOverride table = LazySingletonSO<HalloweenHeadSkinOverride>.Instance;
                __result = table != null ? table.Replace(headId) : headId;
            }
            catch (Exception e)
            {
                tableMissing = true;
                Plugin.Log.LogWarning($"The game's Halloween head table could not be read; zombie heads stay as they are. {e.GetType().Name}: {e.Message}");
                __result = headId;
            }
            return false;
        }
    }
}
