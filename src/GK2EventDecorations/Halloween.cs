using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2EventDecorations
{
    // A part of Halloween: shown with the event, or never. There is no "always" for a part (taken out 2026-11-18): a
    // part set to it showed while Halloween itself was off, and "Halloween: By date" beside decorations up all year
    // read as a bug, twice, to the mod's own user. Halloween's mode says when; a part only says whether it takes part.
    // Decorations all year without the ghost: Halloween on Always, the ghost on Never.
    internal enum PieceMode
    {
        Event, // whatever the event's Mode says today
        Off
    }

    // Halloween's own settings: the three pieces the game ties to the event, and the decorations' areas and kinds.
    // The pieces are separate game paths (docs/seasonal-events.md, "What Halloween changes"): the decorations ask
    // HalloweenConditionalLoadContentPart.ShouldLoad, the zombie head HalloweenHeadSkinOverride.Resolve, and the ghost's
    // scripts the gate TemporaryEventDates.IsHalloweenEvent itself (its flow node and expression function).
    internal static class Halloween
    {
        // One cfg section, so one tab of the options window per event: the mode and dates, the pieces, then the areas and
        // the decorations under their headings (OptionsPage).
        private const string Section = Events.HalloweenName;

        internal static ConfigEntry<PieceMode> Decorations;
        internal static ConfigEntry<PieceMode> Ghost;
        internal static ConfigEntry<PieceMode> ZombieHead;

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
            new Area("AstrologerInterior", "Astrologer", "Inside the astrologer's tower."), // the game's own name for the place: Astrologer's Tower (wz_astrologer_tower)
            new Area("WarehouseInterior", "Warehouse", "Inside the warehouse."),
            new Area("Dungeons", "Dungeons", "The dungeons.")
        };

        // Kinds by the item's object name (prefix, before " (n)"); an item no kind matches is always shown. In game 1.012
        // every item of every group matches one (docs/seasonal-events.md, "The decorations' layout").
        internal static readonly List<Kind> Kinds = new List<Kind>
        {
            new Kind("Pumpkins", "Pumpkins", "Carved pumpkins and jack-o'-lanterns.", "common_hw_pumpkin"),
            new Kind("Garlands", "Garlands", "The flag garlands strung between houses.", "common_hw_flags"),
            new Kind("Scarecrows", "Scarecrows", "The two scarecrows (village, port).", "common_hw_scarecrow"),
            new Kind("Pies", "Pies", "Pies set out on the tables of the tavern and the restaurant.", "common_hw_pie"),
            new Kind("Plants", "Plants", "The Halloween plants in the village.", "common_hw_plants"),
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
            Decorations = config.Bind(Section, "Decorations", PieceMode.Event,
                "The decorations (pumpkins, garlands, scarecrows, candles, bats) in the village, the port, the dungeons, your " +
                "grounds and most interiors. With Halloween (Event): while Halloween is on. Never (Off): not at all.");
            Ghost = config.Bind(Section, "Ghost", PieceMode.Event,
                "The ghost that walks through the village at night (and any other script the game runs for Halloween). " +
                "With Halloween (Event): while Halloween is on. Never (Off): not at all. Takes effect at its next step or evening.");
            ZombieHead = config.Bind(Section, "ZombieHead", PieceMode.Event,
                "Zombies with one particular head get the game's Halloween zombie head instead. With Halloween (Event): while " +
                "Halloween is on. Never (Off): not at all. Pictures already drawn change when the game redraws them (e.g. the " +
                "next load).");
            for (int i = 0; i < Areas.Count; i++)
            {
                Area a = Areas[i];
                a.Entry = config.Bind(Section, a.Key, true, "Show the Halloween decorations here: " + a.Text);
            }
            for (int i = 0; i < Kinds.Count; i++)
            {
                Kind k = Kinds[i];
                k.Entry = config.Bind(Section, k.Key, true, "Show these Halloween decorations: " + k.Text);
            }
            Glow = config.Bind(Section, "CandleGlow", true,
                "The candles and the glow of lanterns and pumpkins. Off: the decorations stay, unlit.");
            BlockWay = config.Bind(Section, "BlockTheWay", true,
                "Decorations block the way like the game's other objects. Off: you walk through them at once; villagers' paths are " +
                "worked out when a save loads, so they follow at the next load.");
        }

        // A piece's answer: never when it is set to Off; otherwise the event's answer today (its mode and dates).
        internal static bool PieceOn(ConfigEntry<PieceMode> piece)
        {
            return piece.Value != PieceMode.Off && Events.Halloween.IsOn();
        }

        // Halloween's mode is Off (with the mod's settings in use): nothing of the event, whatever the pieces say.
        internal static bool EventOff() => Plugin.Enabled.Value && Events.Halloween.Mode.Value == EventMode.Off;

        // The decorations can show at some time with these settings (not by today's date): the event isn't Off and the
        // decorations piece isn't Off. False: their areas and kinds change nothing (greyed in the options window).
        internal static bool DecorationsCanShow() => !EventOff() && Decorations.Value != PieceMode.Off;

        internal static bool DecorationsOn() => Plugin.Enabled.Value ? PieceOn(Decorations) : Events.GameDates(Events.Halloween);

        // The pieces that don't follow the event, for the log line at start: "decorations Off, ghost Off"; "" when all do.
        internal static string Overrides()
        {
            string s = "";
            if (Decorations.Value != PieceMode.Event) s += "decorations " + Decorations.Value;
            if (Ghost.Value != PieceMode.Event) s += (s.Length > 0 ? ", " : "") + "ghost " + Ghost.Value;
            if (ZombieHead.Value != PieceMode.Event) s += (s.Length > 0 ? ", " : "") + "zombie head " + ZombieHead.Value;
            return s;
        }

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
        // Untouched: the game's own ShouldLoad runs, which is only `return TemporaryEventDates.IsHalloweenEvent()` (1.012).
        // That gate answers with the Ghost setting (the patches above), so it runs bypassed: the game's real date check,
        // or the ghost set to Never would take the decorations down in season too (the leak found 2026-11-15, then
        // with a ghost on "Always" putting them up out of season).
        private static bool Prefix(ref bool __result, out bool __state)
        {
            __state = false;
            if (Halloween.Untouched(Halloween.Decorations))
            {
                Events.Bypass++;
                __state = true;
                return true;
            }
            __result = Halloween.DecorationsOn();
            return false;
        }

        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                Events.Bypass--;
            }
        }
    }

    // The zombie head: every head picture and every lying or carried zombie asks this (SkinPresetGK2, SkinChangerGK2,
    // UIWorkerIcon, DropViewAtomMesh). On: the table's swap whatever the date; off: every head its own.
    [HarmonyPatch(typeof(HalloweenHeadSkinOverride), nameof(HalloweenHeadSkinOverride.Resolve), new Type[] { typeof(int) })]
    internal static class HalloweenHeadSkinOverride_Resolve_Patch
    {
        private static bool tableMissing;

        // Untouched: the game's own Resolve runs; it asks the gate first, so bypassed, as ShouldLoad's above.
        private static bool Prefix(int headId, ref int __result, out bool __state)
        {
            __state = false;
            if (Halloween.Untouched(Halloween.ZombieHead))
            {
                Events.Bypass++;
                __state = true;
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

        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                Events.Bypass--;
            }
        }
    }
}
