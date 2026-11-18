extern alias EDasm;
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using GK2ModTestBench;
using UnityEngine;
using ED = EDasm::GK2EventDecorations;

namespace GK2QOLMods.Tests
{
    // The decorations in the world: put up and taken down by a setting, at once, as the options window does it; what
    // each area, kind, glow and walk-through switch does to them, read from the game's own objects; and the two
    // moments of a load the mod steps in at. What is expected comes from EdWorld's own names and counts.
    internal static class EdWorldTests
    {
        private static ED.PieceMode Deco
        {
            set => ED.Halloween.Decorations.Value = value;
        }

        // Up, by the setting, with every switch on: where the switch tests start. Null (and a FAIL) when they aren't.
        private static EdWorld.Survey PutUp()
        {
            EdRun.Arrange();
            Deco = ED.PieceMode.Event;
            EdWorld.Survey s = EdWorld.Look();
            if (s == null || s.Root == null)
            {
                Test.Fail(s == null ? "the decorations didn't go up with Halloween on Always and the decorations with it" : $"the decorations have no {EdWorld.StaticRoot} any more: the game laid them out anew");
                return null;
            }
            return s;
        }

        private static int Shown(List<Transform> list)
        {
            int n = 0;
            foreach (Transform t in list) if (t.gameObject.activeSelf) n++;
            return n;
        }

        // ------------------------------------------------------------------ up and down

        internal static IEnumerator UpAndDown()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            yield return Test.Frames(2);
            if (EdWorld.Owner() == null)
            {
                Test.Fail("no scene of the game has the Halloween decorations (" + EdWorld.ContentName + ")");
                yield break;
            }
            try
            {
                Test.Check(!EdWorld.Up && EdWorld.Copies() == 0, $"Halloween on Always, the decorations on Never: they are down (up: {EdWorld.Up}, {EdWorld.Copies()} set(s) in the world)");
                string saveBefore = EdWorld.SaveCounts();
                int mark = EdRun.Mark;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Deco = ED.PieceMode.Event;
                double upMs = watch.Elapsed.TotalMilliseconds;
                Test.Check(EdWorld.Up, "the decorations set to With Halloween: up in the same frame, without a load");
                yield return Test.Frames(2);
                GameObject content = EdWorld.Content();
                Test.Check(content != null && content.activeInHierarchy && EdWorld.Copies() == 1 && EdRun.Count("Halloween decorations put up.", mark) == 1,
                    $"one set of them stands in the world, and the mod says so once ({EdWorld.Copies()} set(s), {EdRun.Count("Halloween decorations put up.", mark)} line(s))");
                Test.Check(EdWorld.SaveCounts() == saveBefore, $"the save holds what it held before ({EdWorld.SaveCounts()}): the decorations are scenery, nothing of them is saved");

                mark = EdRun.Mark;
                watch.Restart();
                Deco = ED.PieceMode.Off;
                double downMs = watch.Elapsed.TotalMilliseconds;
                Test.Check(!EdWorld.Up, "set to Never: down in the same frame");
                yield return Test.Frames(2);
                Test.Check(EdWorld.Copies() == 0 && EdRun.Count("Halloween decorations taken down.", mark) == 1 && EdWorld.SaveCounts() == saveBefore,
                    $"nothing of them is left in the world, the mod says so once, the save as before ({EdWorld.Copies()} set(s), {EdRun.Count("Halloween decorations taken down.", mark)} line(s))");
                Test.Info($"putting them up took {upMs:0.0} ms, taking them down {downMs:0.0} ms (a frame at 60 fps is 16.7 ms; the player does it from the options window, the game paused)");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // The mod with every switch on changes nothing of what the game made: a set of decorations loaded by the
        // game's own call, before the mod has touched it, against the same set after the mod went over it.
        internal static IEnumerator AsTheGameMakesThem()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            yield return Test.Frames(2);
            GameSceneConfig owner = EdWorld.Owner();
            if (owner == null || EdWorld.Up)
            {
                Test.Fail(owner == null ? "no scene of the game has the Halloween decorations" : "the decorations are up though Decorations is on Never");
                yield break;
            }
            EdRun.Detach(); // the mod doesn't hear the setting: the load below is the game's own, untouched
            try
            {
                Deco = ED.PieceMode.Event; // the part's own check at a load asks the mod, which now says yes
                bool loaded = owner.TryLoadSceneDataContentByName(EdWorld.ContentName, out SceneWgoContentData _);
                EdWorld.Survey fresh = EdWorld.Look();
                Test.Check(loaded && fresh != null && fresh.Root != null, "loaded by the game's own call, the mod not told");
                if (fresh == null || fresh.Root == null) yield break;
                List<string> offFresh = EdWorld.Off(fresh);
                Test.Info($"as the game makes them: {fresh.GroupList.Count} groups, {fresh.ItemList.Count} items, {fresh.GlowList.Count} glows, {fresh.BlockerList.Count} blocking colliders, {fresh.OtherColliders.Count} other collider(s); switched off by the game itself: {offFresh.Count}{EdWorld.Some(offFresh)}");
                EdRun.Attach();
                int mark = EdRun.Mark;
                ED.Decorations.OnSettingChanged(null, EventArgs.Empty); // now the mod goes over them, every switch on
                List<string> offAfter = EdWorld.Off(EdWorld.Look());
                Test.Check(string.Join("|", offAfter) == string.Join("|", offFresh),
                    $"with every switch on, the mod leaves them exactly as the game made them ({offFresh.Count} switched off before, {offAfter.Count} after{EdWorld.Some(offAfter)})");
                Test.Check(EdWorld.Copies() == 1 && EdRun.Count("put up", mark) == 0, $"and puts up no second set ({EdWorld.Copies()} in the world)");
                yield return null;
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // ------------------------------------------------------------------ the layout the switches are written for

        internal static IEnumerator Layout()
        {
            if (!EdRun.Ready()) yield break;
            EdWorld.Survey s = PutUp();
            if (s == null) yield break;
            try
            {
                Test.Check(s.GroupList.Count == EdWorld.Groups, $"{s.GroupList.Count} groups under {EdWorld.StaticRoot} ({EdWorld.Groups} audited: the {EdWorld.AreaKeys.Length} areas and the bats)");
                var strangers = new List<string>();
                var areaWrong = new List<string>();
                foreach (Transform g in s.GroupList)
                {
                    if (g.name == EdWorld.FxGroup) continue;
                    int a = EdWorld.AreaOf(g);
                    if (a < 0) strangers.Add(g.name);
                    else if (g.childCount != EdWorld.AreaItems[a]) areaWrong.Add($"{g.name} has {g.childCount} items, {EdWorld.AreaItems[a]} audited");
                }
                for (int k = 0; k < EdWorld.AreaKeys.Length; k++)
                {
                    if (s.Group("Halloween" + EdWorld.AreaKeys[k]) == null) strangers.Add("no group Halloween" + EdWorld.AreaKeys[k]);
                }
                Test.Check(strangers.Count == 0, $"every group is one of the areas' or the bats', and every area has its group{EdWorld.Some(strangers)} (a group without an area would have no switch)");
                Test.Check(areaWrong.Count == 0, $"each area has the items the audit counted{EdWorld.Some(areaWrong)}");
                yield return null;

                var kinds = new int[EdWorld.KindKeys.Length];
                var kindless = new List<string>();
                foreach (Transform i in s.ItemList)
                {
                    int k = EdWorld.KindOf(i);
                    if (k < 0) kindless.Add(i.parent.name + "/" + i.name);
                    else kinds[k]++;
                }
                var kindWrong = new List<string>();
                for (int k = 0; k < kinds.Length; k++)
                {
                    if (kinds[k] != EdWorld.KindItems[k]) kindWrong.Add($"{EdWorld.KindKeys[k]}: {kinds[k]} items, {EdWorld.KindItems[k]} audited");
                }
                Test.Check(s.ItemList.Count == EdWorld.Items && kindless.Count == 0, $"{s.ItemList.Count} items ({EdWorld.Items} audited), {kindless.Count} of no kind{EdWorld.Some(kindless)} (an item of no kind would have no switch)");
                Test.Check(kindWrong.Count == 0, $"each kind has the items the audit counted{EdWorld.Some(kindWrong)}");
                Transform fx = s.Fx;
                Test.Check(fx != null && fx.childCount == EdWorld.FxItems, $"the bats' group has {(fx != null ? fx.childCount : 0)} things ({EdWorld.FxItems} audited: bats and glowing eyes)");
                Test.Check(s.GlowList.Count == EdWorld.Glows, $"{s.GlowList.Count} glows ({EdWorld.Glows} audited)");
                Test.Check(s.BlockerList.Count == EdWorld.Blockers && s.OtherColliders.Count == 3, $"{s.BlockerList.Count} blocking colliders ({EdWorld.Blockers} audited) and {s.OtherColliders.Count} other(s) (3 audited: the bats' zones, which must keep working)");
                yield return null;

                // the mod's own count of what shows, said in the log whenever it changes
                string said = EdRun.Last("Halloween decorations: ");
                string want = EdWorld.LineFor(s.GroupList.Count, s.ItemList.Count, s.GlowList.Count, s.BlockerList.Count, s);
                if (said == null) Test.Info("the mod didn't say what shows in this run (it says it when the count changes, and it is what it was before the run)");
                else Test.Check(said == want, $"the mod counts the same: \"{said}\"{(said == want ? "" : $" (these tests count: \"{want}\")")}");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // ------------------------------------------------------------------ the switches

        internal static IEnumerator Areas()
        {
            if (!EdRun.Ready()) yield break;
            EdWorld.Survey s = PutUp();
            if (s == null) yield break;
            try
            {
                for (int k = 0; k < EdWorld.AreaKeys.Length; k++)
                {
                    string key = EdWorld.AreaKeys[k];
                    ConfigEntry<bool> e = EdRun.Switch(key);
                    Transform group = s.Group("Halloween" + key);
                    if (e == null || group == null)
                    {
                        Test.Fail($"{key}: {(e == null ? "no setting" : "no group in the world")}");
                        continue;
                    }
                    int mark = EdRun.Mark;
                    e.Value = false;
                    bool off = !group.gameObject.activeSelf;
                    int othersShown = Shown(s.GroupList), itemsShown = Shown(s.ItemList);
                    string said = EdRun.Last("Halloween decorations: ", mark);
                    string want = EdWorld.LineFor(s.GroupList.Count - 1, s.ItemList.Count - group.childCount, s.GlowList.Count, s.BlockerList.Count, s);
                    e.Value = true;
                    bool on = group.gameObject.activeSelf && Shown(s.GroupList) == s.GroupList.Count;
                    Test.Check(off && othersShown == s.GroupList.Count - 1 && itemsShown == s.ItemList.Count && said == want && on,
                        $"{key}: off hides its group of {group.childCount} and no other ({othersShown} of {s.GroupList.Count} groups still shown), the mod says {(said == want ? "so" : $"\"{said}\"")}; on shows it again ({on})");
                    if (k % 4 == 3) yield return null;
                }
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        internal static IEnumerator Kinds()
        {
            if (!EdRun.Ready()) yield break;
            EdWorld.Survey s = PutUp();
            if (s == null) yield break;
            try
            {
                for (int k = 0; k < EdWorld.KindKeys.Length; k++)
                {
                    string key = EdWorld.KindKeys[k];
                    ConfigEntry<bool> e = EdRun.Switch(key);
                    if (e == null)
                    {
                        Test.Fail($"{key}: no setting");
                        continue;
                    }
                    int mark = EdRun.Mark;
                    e.Value = false;
                    int mine = 0, mineHidden = 0, othersHidden = 0;
                    foreach (Transform i in s.ItemList)
                    {
                        bool hidden = !i.gameObject.activeSelf;
                        if (EdWorld.KindOf(i) == k)
                        {
                            mine++;
                            if (hidden) mineHidden++;
                        }
                        else if (hidden) othersHidden++;
                    }
                    int groupsShown = Shown(s.GroupList);
                    string said = EdRun.Last("Halloween decorations: ", mark);
                    string want = EdWorld.LineFor(s.GroupList.Count, s.ItemList.Count - mine, s.GlowList.Count, s.BlockerList.Count, s);
                    e.Value = true;
                    bool on = Shown(s.ItemList) == s.ItemList.Count;
                    Test.Check(mine > 0 && mineHidden == mine && othersHidden == 0 && groupsShown == s.GroupList.Count && said == want && on,
                        $"{key}: off hides its {mineHidden} of {mine} item(s) in every area and {othersHidden} other(s), the mod says {(said == want ? "so" : $"\"{said}\"")}; on shows them again ({on})");
                    if (k % 3 == 2) yield return null;
                }

                // the bats and the glowing eyes: their own group
                ConfigEntry<bool> bats = EdRun.Switch(EdWorld.BatsKey);
                Transform fx = s.Fx;
                if (bats == null || fx == null)
                {
                    Test.Fail($"{EdWorld.BatsKey}: {(bats == null ? "no setting" : "no " + EdWorld.FxGroup + " group in the world")}");
                    yield break;
                }
                int m = EdRun.Mark;
                bats.Value = false;
                bool fxOff = !fx.gameObject.activeSelf && Shown(s.GroupList) == s.GroupList.Count - 1 && Shown(s.ItemList) == s.ItemList.Count;
                string batsSaid = EdRun.Last("Halloween decorations: ", m);
                string batsWant = EdWorld.LineFor(s.GroupList.Count - 1, s.ItemList.Count, s.GlowList.Count, s.BlockerList.Count, s);
                bats.Value = true;
                Test.Check(fxOff && batsSaid == batsWant && fx.gameObject.activeSelf,
                    $"{EdWorld.BatsKey}: off hides the bats' group and nothing else, the mod says {(batsSaid == batsWant ? "so" : $"\"{batsSaid}\"")}; on shows it again ({fx.gameObject.activeSelf})");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        internal static IEnumerator AreaAndKind()
        {
            if (!EdRun.Ready()) yield break;
            EdWorld.Survey s = PutUp();
            if (s == null) yield break;
            try
            {
                ConfigEntry<bool> village = EdRun.Switch("Village"), pumpkins = EdRun.Switch("Pumpkins"), tavern = EdRun.Switch("TavernInterior"), pies = EdRun.Switch("Pies");
                Transform villageGroup = s.Group("HalloweenVillage"), tavernGroup = s.Group("HalloweenTavernInterior");
                if (village == null || pumpkins == null || tavern == null || pies == null || villageGroup == null || tavernGroup == null)
                {
                    Test.Fail("the village, the tavern, the pumpkins or the pies have no setting or no group");
                    yield break;
                }
                int mark = EdRun.Mark;
                village.Value = false;
                pumpkins.Value = false;
                int expected = 0, visible = 0, wrong = 0;
                foreach (Transform i in s.ItemList)
                {
                    bool should = i.parent != villageGroup && EdWorld.KindOf(i) != 0; // 0: the pumpkins
                    if (should) expected++;
                    if (i.gameObject.activeInHierarchy) visible++;
                    if (i.gameObject.activeInHierarchy != should) wrong++;
                }
                string said = EdRun.Last("Halloween decorations: ", mark);
                string want = EdWorld.LineFor(s.GroupList.Count - 1, expected, s.GlowList.Count, s.BlockerList.Count, s);
                Test.Check(wrong == 0 && said == want, $"the village and the pumpkins off: {visible} items show, the {expected} that are neither ({wrong} shown or hidden wrongly); the mod says {(said == want ? "so" : $"\"{said}\"")}");
                yield return null;

                // the one put back brings back only what the other doesn't hide
                village.Value = true;
                int pumpkinsInVillage = 0, pumpkinsShown = 0, villageShown = 0;
                foreach (Transform i in s.ItemList)
                {
                    bool pumpkin = EdWorld.KindOf(i) == 0;
                    if (pumpkin && i.parent == villageGroup) pumpkinsInVillage++;
                    if (pumpkin && i.gameObject.activeInHierarchy) pumpkinsShown++;
                    if (!pumpkin && i.parent == villageGroup && i.gameObject.activeInHierarchy) villageShown++;
                }
                Test.Check(villageShown == villageGroup.childCount - pumpkinsInVillage && pumpkinsShown == 0,
                    $"the village on again, the pumpkins still off: its {villageShown} other items are back, its {pumpkinsInVillage} pumpkins not ({pumpkinsShown} pumpkins show anywhere)");
                pumpkins.Value = true;
                tavern.Value = false;
                pies.Value = false;
                int piesShown = 0, tavernShown = 0;
                foreach (Transform i in s.ItemList)
                {
                    if (EdWorld.KindOf(i) == 3 && i.gameObject.activeInHierarchy) piesShown++; // 3: the pies
                    if (i.parent == tavernGroup && i.gameObject.activeInHierarchy) tavernShown++;
                }
                Test.Check(piesShown == 0 && tavernShown == 0 && Shown(s.ItemList) == s.ItemList.Count - EdWorld.KindItems[3],
                    $"the tavern and the pies off: no pie shows anywhere ({piesShown}), nothing in the tavern ({tavernShown})");
                tavern.Value = true;
                pies.Value = true;
                Test.Check(EdWorld.Off(s).Count == 0, $"everything on again: nothing is left switched off{EdWorld.Some(EdWorld.Off(s))}");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        internal static IEnumerator Glow()
        {
            if (!EdRun.Ready()) yield break;
            EdWorld.Survey s = PutUp();
            if (s == null) yield break;
            try
            {
                int mark = EdRun.Mark;
                ED.Halloween.Glow.Value = false;
                int glowing = Shown(s.GlowList);
                string said = EdRun.Last("Halloween decorations: ", mark);
                string want = EdWorld.LineFor(s.GroupList.Count, s.ItemList.Count, 0, s.BlockerList.Count, s);
                Test.Check(s.GlowList.Count > 0 && glowing == 0 && Shown(s.ItemList) == s.ItemList.Count && Shown(s.GroupList) == s.GroupList.Count && said == want,
                    $"glow off: {s.GlowList.Count - glowing} of {s.GlowList.Count} glows are out, every decoration still stands ({Shown(s.ItemList)} of {s.ItemList.Count}); the mod says {(said == want ? "so" : $"\"{said}\"")}");
                int blockersOn = 0;
                foreach (Collider c in s.BlockerList) if (c.enabled) blockersOn++;
                Test.Check(blockersOn == s.BlockerList.Count, $"and they still block the way ({blockersOn} of {s.BlockerList.Count} colliders on)");
                yield return null;
                ED.Halloween.Glow.Value = true;
                Test.Check(Shown(s.GlowList) == s.GlowList.Count, $"glow on again: all {Shown(s.GlowList)} are lit");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        internal static IEnumerator BlockTheWay()
        {
            if (!EdRun.Ready()) yield break;
            EdWorld.Survey s = PutUp();
            if (s == null) yield break;
            try
            {
                int mark = EdRun.Mark;
                ED.Halloween.BlockWay.Value = false;
                int blocking = 0, zones = 0;
                foreach (Collider c in s.BlockerList) if (c.enabled) blocking++;
                foreach (Collider c in s.OtherColliders) if (c.enabled) zones++;
                string said = EdRun.Last("Halloween decorations: ", mark);
                string want = EdWorld.LineFor(s.GroupList.Count, s.ItemList.Count, s.GlowList.Count, 0, s);
                Test.Check(s.BlockerList.Count > 0 && blocking == 0 && said == want, $"\"block the way\" off: {s.BlockerList.Count - blocking} of {s.BlockerList.Count} blocking colliders are off, at once; the mod says {(said == want ? "so" : $"\"{said}\"")}");
                Test.Check(zones == s.OtherColliders.Count && Shown(s.ItemList) == s.ItemList.Count && Shown(s.GlowList) == s.GlowList.Count,
                    $"the bats' {s.OtherColliders.Count} zone(s) still work ({zones} on), every decoration still stands and glows");
                yield return null;
                ED.Halloween.BlockWay.Value = true;
                blocking = 0;
                foreach (Collider c in s.BlockerList) if (c.enabled) blocking++;
                Test.Check(blocking == s.BlockerList.Count, $"on again: all {blocking} block the way");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // "Mod enabled" off: the game decides by its own dates, and what it puts up is as it made it, whatever the
        // areas and kinds are set to; on again, they count again.
        internal static IEnumerator ModOff()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            try
            {
                ED.Events.Halloween.Mode.Value = ED.EventMode.Auto; // By date: the game's dates decide, as they do with the mod off
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                if (!EdRun.InSeason())
                {
                    // today's real season, out of it: the mod off leaves them down as the game does
                    ED.Events.Halloween.Mode.Value = ED.EventMode.On;
                    yield return null;
                    bool wasUp = EdWorld.Up;
                    ED.Plugin.Enabled.Value = false;
                    Test.Check(wasUp && !EdWorld.Up, "out of season (the game's dates can't be made to say Halloween here): with Halloween on Always the decorations are up, and with the mod switched off they are taken down, as the game has it");
                    Test.Info("the other half (in season with the mod off: up, and as the game made them) needs the forced season");
                    yield break;
                }
                // the game's dates say Halloween: up by the game's own rule
                yield return null;
                EdWorld.Survey s = EdWorld.Look();
                if (s == null || s.Root == null)
                {
                    Test.Fail("with the game's dates saying Halloween and everything following them, the decorations aren't up");
                    yield break;
                }
                EdRun.Switch("Village").Value = false;
                EdRun.Switch("Pumpkins").Value = false;
                EdRun.Switch(EdWorld.BatsKey).Value = false;
                ED.Halloween.Glow.Value = false;
                ED.Halloween.BlockWay.Value = false;
                int offWithMod = EdWorld.Off(s).Count;
                ED.Plugin.Enabled.Value = false; // (no frame passes like this: the ghost's scripts hear the game's dates now)
                EdWorld.Survey after = EdWorld.Look();
                Test.Check(after != null && after.Root != null, "the mod switched off while the game's dates say Halloween: the decorations stay up, as the game has them");
                if (after == null || after.Root == null) yield break;
                List<string> off = EdWorld.Off(after);
                Test.Check(offWithMod > 0 && off.Count == 0, $"and are as the game made them: the village, the pumpkins, the bats, the glow and the blocking were switched off ({offWithMod} things), now nothing is{EdWorld.Some(off)}");
                Test.Check(ED.Plugin.StatusShown().StartsWith("Off", StringComparison.Ordinal), $"the status line says the mod is switched off (\"{ED.Plugin.StatusShown()}\")");
                ED.Plugin.Enabled.Value = true;
                Test.Check(EdWorld.Off(EdWorld.Look()).Count == offWithMod, $"switched on again: the same {offWithMod} things are off again ({EdWorld.Off(EdWorld.Look()).Count})");

                // and the mod off out of season: down
                ED.Plugin.Enabled.Value = false;
                EdRun.Season(false);
                Test.Check(!EdWorld.Up, "the mod off and the game's dates saying it isn't Halloween: down");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // ------------------------------------------------------------------ what decides whether they are up

        internal static IEnumerator WhatDecides()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            DateTime now = DateTime.Now;
            string Md(DateTime d) => $"{d.Month:D2}-{d.Day:D2}";
            try
            {
                // Halloween says when, the part says whether: up needs both
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                bool up1 = EdWorld.Up;
                ED.Events.Halloween.Mode.Value = ED.EventMode.Off;
                bool down1 = !EdWorld.Up;
                ED.Events.Halloween.Mode.Value = ED.EventMode.On;
                bool up2 = EdWorld.Up;
                ED.Halloween.Decorations.Value = ED.PieceMode.Off;
                bool down2 = !EdWorld.Up;
                Test.Check(up1 && down1 && up2 && down2, $"Halloween on Always with the decorations: up ({up1}); Halloween set to Never: down ({down1}); Always again: up ({up2}); the decorations set to Never: down ({down2})");
                yield return Test.Frames(2);

                // following Halloween: Always, the player's own dates, the game's dates
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                EdRun.Season(false);
                ED.Events.Halloween.Mode.Value = ED.EventMode.On;
                bool always = EdWorld.Up;
                ED.Events.Halloween.Mode.Value = ED.EventMode.Auto;
                Test.Check(always && EdWorld.Up == EdRun.SeasonNow, $"Decorations with Halloween: Halloween on Always puts them up ({always}); By date they follow the game's dates ({(EdRun.SeasonNow ? "Halloween: up" : "not Halloween: down")}, up {EdWorld.Up})");
                yield return Test.Frames(2);
                ED.Events.Halloween.Start.Value = Md(now.AddDays(-1));
                ED.Events.Halloween.End.Value = Md(now.AddDays(1));
                bool inWindow = EdWorld.Up;
                ED.Events.Halloween.End.Value = Md(now.AddDays(20));     // the last day first: today stays inside on the way
                ED.Events.Halloween.Start.Value = Md(now.AddDays(10));
                bool movedAway = !EdWorld.Up;
                EdRun.Season(true);
                bool outOfWindow = !EdWorld.Up;
                Test.Check(inWindow && movedAway && outOfWindow, $"the player's own dates around today: up ({inWindow}); moved away from today: down ({movedAway}), and still down with the game's dates saying {(EdRun.SeasonNow ? "Halloween" : "what they say today")} ({outOfWindow})");
                yield return Test.Frames(2);
                ED.Events.Halloween.Start.Value = "";
                ED.Events.Halloween.End.Value = "";
                EdRun.Season(true);
                bool s1 = EdRun.SeasonNow, u1 = EdWorld.Up;
                yield return Test.Frames(2);
                EdRun.Season(false);
                bool s2 = EdRun.SeasonNow, u2 = EdWorld.Up;
                Test.Check(u1 == s1 && u2 == s2, $"no dates of the player's: up when the game's dates say Halloween (they say {s1}: up {u1}; they say {s2}: up {u2})");
                yield return Test.Frames(2);
                Test.Check(EdWorld.Copies() == (EdWorld.Up ? 1 : 0), $"after all that, {EdWorld.Copies()} set(s) in the world");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // ------------------------------------------------------------------ the two moments of a load

        // While a save loads the game asks each scene for its conditional content (the decorations are RuinedTemple's
        // only one), and the mod's patch after that step switches the areas before the scene is built. Here the game's
        // own step is called again in the running game, and the mod's part after it.
        internal static IEnumerator AtALoad()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            yield return Test.Frames(2);
            GameSceneConfig owner = EdWorld.Owner();
            if (owner == null || EdWorld.Up)
            {
                Test.Fail(owner == null ? "no scene of the game has the Halloween decorations" : "the decorations are up though Decorations is on Never");
                yield break;
            }
            try
            {
                bool loaded = owner.TryLoadConditionalSceneDataContent(out List<SceneWgoContentData> none);
                Test.Check(!loaded && !EdWorld.Up && (none == null || none.Count == 0), $"the settings say down: the game's own step at a load loads nothing ({(none != null ? none.Count : 0)} part(s))");
                yield return null;

                EdRun.Detach(); // a load is not a changed setting: the mod's part is its patch after the game's step
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                EdRun.Switch("Village").Value = false;
                EdRun.Switch("Pies").Value = false;
                ED.Halloween.Glow.Value = false;
                loaded = owner.TryLoadConditionalSceneDataContent(out List<SceneWgoContentData> parts);
                EdWorld.Survey fresh = EdWorld.Look();
                Test.Check(loaded && parts != null && parts.Count == 1 && fresh != null && fresh.Root != null, $"the settings say up: the game's own step loads them ({(parts != null ? parts.Count : 0)} part)");
                if (fresh == null || fresh.Root == null) yield break;
                int offBefore = EdWorld.Off(fresh).Count;
                ED.Decorations.AfterGameAdded(MainGame.Instance.gameSceneConfigs); // what the mod's patch on WorldData.AddMissingConditionalContent calls
                Transform village = fresh.Group("HalloweenVillage");
                int piesShown = 0;
                foreach (Transform i in fresh.ItemList) if (EdWorld.KindOf(i) == 3 && i.gameObject.activeSelf) piesShown++;
                Test.Check(village != null && !village.gameObject.activeSelf && piesShown == 0 && Shown(fresh.GlowList) == 0 && Shown(fresh.GroupList) == fresh.GroupList.Count - 1,
                    $"and the mod's part of the load switches them before the world is built: the village hidden ({village != null && !village.gameObject.activeSelf}), no pie ({piesShown}), no glow ({Shown(fresh.GlowList)}); {offBefore} thing(s) were off before it");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // When the game has started (MainGame.OnGameStarted) the mod looks once more: decorations that are up or down
        // against the settings are a warning in the log, and put right. Made here behind the mod's back.
        internal static IEnumerator AfterALoad()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            yield return Test.Frames(2);
            GameSceneConfig owner = EdWorld.Owner();
            if (owner == null)
            {
                Test.Fail("no scene of the game has the Halloween decorations");
                yield break;
            }
            try
            {
                // as they should be: nothing said
                int mark = EdRun.Mark;
                ED.Decorations.OnGameStarted();
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                ED.Decorations.OnGameStarted();
                Test.Check(EdWorld.Up && EdRun.Count("After loading", mark) == 0, $"down with the settings saying down, then up with them saying up: nothing to put right, nothing said ({EdRun.Count("After loading", mark)} line(s))");
                yield return Test.Frames(2);

                // down though the settings say up
                bool unloaded = owner.TryUnloadSceneDataContent(EdWorld.ContentName);
                yield return Test.Frames(2);
                mark = EdRun.Mark;
                using (EdRun.Expect("After loading, the Halloween decorations were down though the settings say up"))
                {
                    ED.Decorations.OnGameStarted();
                }
                Test.Check(unloaded && EdWorld.Up && EdRun.Count("were down though the settings say up", mark) == 1,
                    $"taken down behind the mod's back: at the game's start it says so once ({EdRun.Count("were down though the settings say up", mark)}) and puts them up ({EdWorld.Up})");
                yield return Test.Frames(2);

                // up though the settings say down
                EdRun.Detach();
                ED.Halloween.Decorations.Value = ED.PieceMode.Off;
                EdRun.Attach();
                mark = EdRun.Mark;
                bool stillUp = EdWorld.Up;
                using (EdRun.Expect("After loading, the Halloween decorations were up though the settings say down"))
                {
                    ED.Decorations.OnGameStarted();
                }
                Test.Check(stillUp && !EdWorld.Up && EdRun.Count("were up though the settings say down", mark) == 1,
                    $"up though the settings say down: said once ({EdRun.Count("were up though the settings say down", mark)}) and taken down ({!EdWorld.Up})");
                yield return Test.Frames(2);
                Test.Check(EdWorld.Copies() == 0, $"{EdWorld.Copies()} set(s) left in the world");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // ------------------------------------------------------------------ over time

        // Every other test looks in the frame of the change, or two frames on: the whole suite is over in a few seconds.
        // That is right for what the mod does (a setting acts within its frame), but it can't see something of the
        // game's undoing it a moment later: a script on a decoration switching its light back on, the game's streaming
        // putting a hidden group back, a set of decorations coming back after it was taken down. So here the game is
        // left to run for seconds at a time, and what was switched is looked at again.
        internal static IEnumerator Stays()
        {
            if (!EdRun.Ready()) yield break;
            EdWorld.Survey s = PutUp();
            if (s == null) yield break;
            try
            {
                EdRun.Switch("Village").Value = false;
                EdRun.Switch("Pumpkins").Value = false;
                EdRun.Switch(EdWorld.BatsKey).Value = false;
                ED.Halloween.Glow.Value = false;
                ED.Halloween.BlockWay.Value = false;
                string off = string.Join("|", EdWorld.Off(s));
                int n = EdWorld.Off(s).Count, frame = Time.frameCount, unexpected = EdRun.Unexpected.Count;
                yield return Test.Seconds(3f);
                EdWorld.Survey later = EdWorld.Look();
                string offLater = later != null ? string.Join("|", EdWorld.Off(later)) : "(down)";
                int zones = 0;
                if (later != null) foreach (Collider c in later.OtherColliders) if (c.enabled) zones++;
                Test.Check(later != null && later.Root == s.Root && offLater == off && EdWorld.Copies() == 1,
                    $"the village, the pumpkins, the bats, the glow and the blocking switched off ({n} things), the game left to run for 3 s ({Time.frameCount - frame} frames): the same set of decorations stands and exactly the same things are off{(offLater == off ? "" : ": now " + offLater)}");
                Test.Check(later != null && zones == later.OtherColliders.Count, $"the bats' zones are still working after it ({zones} of {(later != null ? later.OtherColliders.Count : 0)})");

                EdRun.Switch("Village").Value = true;
                EdRun.Switch("Pumpkins").Value = true;
                EdRun.Switch(EdWorld.BatsKey).Value = true;
                ED.Halloween.Glow.Value = true;
                ED.Halloween.BlockWay.Value = true;
                frame = Time.frameCount;
                yield return Test.Seconds(2f);
                later = EdWorld.Look();
                List<string> offNow = EdWorld.Off(later);
                Test.Check(later != null && offNow.Count == 0 && EdWorld.Copies() == 1, $"everything on again and left for 2 s ({Time.frameCount - frame} frames): nothing is off{EdWorld.Some(offNow)}");

                ED.Halloween.Decorations.Value = ED.PieceMode.Off;
                frame = Time.frameCount;
                yield return Test.Seconds(2f);
                Test.Check(!EdWorld.Up && EdWorld.Copies() == 0 && EdRun.Unexpected.Count == unexpected,
                    $"taken down and left for 2 s ({Time.frameCount - frame} frames): they stay down, nothing of them in the world ({EdWorld.Copies()}), no warning of the mod's meanwhile ({EdRun.Unexpected.Count - unexpected})");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // ------------------------------------------------------------------ many times over

        internal static IEnumerator ManyTimes()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            yield return Test.Frames(2);
            try
            {
                const int times = 10;
                int mark = EdRun.Mark, wrong = 0, unexpectedBefore = EdRun.Unexpected.Count;
                string saveBefore = EdWorld.SaveCounts();
                double up = 0, down = 0, worstUp = 0;
                var watch = new System.Diagnostics.Stopwatch();
                for (int k = 0; k < times && !Test.Aborted; k++)
                {
                    watch.Restart();
                    ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                    double ms = watch.Elapsed.TotalMilliseconds;
                    up += ms;
                    if (ms > worstUp) worstUp = ms;
                    if (!EdWorld.Up) wrong++;
                    yield return null;
                    watch.Restart();
                    ED.Halloween.Decorations.Value = ED.PieceMode.Off;
                    down += watch.Elapsed.TotalMilliseconds;
                    if (EdWorld.Up) wrong++;
                    yield return null;
                }
                yield return Test.Frames(2);
                Test.Check(wrong == 0 && EdRun.Count("put up", mark) == times && EdRun.Count("taken down", mark) == times,
                    $"{times} times up and down, a frame apart: each time as set ({wrong} wrong), said {EdRun.Count("put up", mark)} and {EdRun.Count("taken down", mark)} times");
                Test.Check(EdWorld.Copies() == 0 && EdWorld.SaveCounts() == saveBefore && EdRun.Unexpected.Count == unexpectedBefore,
                    $"nothing left behind: {EdWorld.Copies()} set(s) in the world, the save as before, {EdRun.Unexpected.Count - unexpectedBefore} warning(s)");
                Test.Info($"a put-up took {up / times:0.0} ms on average (the worst {worstUp:0.0}), a take-down {down / times:0.0} ms");

                // a switch flipped while they are up: what one click in the options window costs
                ED.Halloween.Decorations.Value = ED.PieceMode.Event;
                yield return null;
                ConfigEntry<bool> village = EdRun.Switch("Village");
                watch.Restart();
                for (int k = 0; k < 20; k++) village.Value = !village.Value;
                Test.Info($"an area switched while they are up: {watch.Elapsed.TotalMilliseconds / 20:0.00} ms a switch");
            }
            finally
            {
                EdRun.Arrange();
            }
        }
    }
}
