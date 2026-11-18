extern alias EDasm;
using System;
using System.Collections;
using System.Collections.Generic;
using GK2ModTestBench;
using ED = EDasm::GK2EventDecorations;

namespace GK2QOLMods.Tests
{
    // The player's own dates for Halloween (Start and End, month-day): when they count, what counts as a date, the
    // first and the last day, a window past the new year. Every window is made from today's date, so the tests say
    // the same on any day; and where the force works, the game's own dates are made to say the opposite of what the
    // player's dates should give, so an answer can only come from the player's.
    internal static class EdDateTests
    {
        private static ED.GameEvent H => ED.Events.Halloween;

        private static string Md(DateTime d) => $"{d.Month:D2}-{d.Day:D2}";

        private static void Dates(string start, string end)
        {
            H.Start.Value = start;
            H.End.Value = end;
        }

        // Is Halloween on by the mod's settings, with the game's dates saying the opposite of `expected` where they can
        // be made to.
        private static bool OnAgainstTheGame(bool expected)
        {
            EdRun.Season(!expected);
            return H.IsOn();
        }

        internal static IEnumerator Count()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            DateTime now = DateTime.Now;
            try
            {
                Dates(Md(now.AddDays(-1)), Md(now.AddDays(1)));
                Test.Check(OnAgainstTheGame(true), $"{H.Start.Value} to {H.End.Value}, today inside: Halloween is on (the game's dates {(EdRun.SeasonNow ? "agree" : "say no")})");
                Test.Check(H.CustomWindow() == $"{Md(now.AddDays(-1))} to {Md(now.AddDays(1))}" && !H.UsesGameDates(), $"the mod reads them as \"{H.CustomWindow()}\" and no longer asks the game's dates");
                Dates(Md(now.AddDays(10)), Md(now.AddDays(20)));
                Test.Check(!OnAgainstTheGame(false), $"{H.Start.Value} to {H.End.Value}, today before it: Halloween is off (the game's dates {(EdRun.SeasonNow ? "say yes" : "agree")})");
                Dates(Md(now.AddDays(-20)), Md(now.AddDays(-10)));
                Test.Check(!OnAgainstTheGame(false), $"{H.Start.Value} to {H.End.Value}, today after it: off");
                yield return null;

                // only By date: Always and Never don't look at them
                Dates(Md(now.AddDays(10)), Md(now.AddDays(20)));
                H.Mode.Value = ED.EventMode.On;
                Test.Check(OnAgainstTheGame(true) && !H.DatesDontCount(), "Halloween on Always with dates that leave today out: on, and nothing said about the dates");
                Dates(Md(now.AddDays(-1)), Md(now.AddDays(1)));
                H.Mode.Value = ED.EventMode.Off;
                Test.Check(!OnAgainstTheGame(false), "Halloween on Never with dates that take today in: off");
                H.Mode.Value = ED.EventMode.Auto;
                Dates("", "");
                EdRun.Season(true);
                bool said1 = EdRun.SeasonNow, on1 = H.IsOn();
                EdRun.Season(false);
                bool said2 = EdRun.SeasonNow, on2 = H.IsOn();
                Test.Check(H.UsesGameDates() && H.CustomWindow() == null && on1 == said1 && on2 == said2,
                    $"no dates of the player's: the game's own decide (they say {said1}: Halloween {on1}; they say {said2}: Halloween {on2})");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        internal static IEnumerator Ends()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            DateTime now = DateTime.Now;
            try
            {
                Dates(Md(now), Md(now.AddDays(3)));
                Test.Check(OnAgainstTheGame(true), $"{H.Start.Value} to {H.End.Value}: today is the first day, and it counts");
                Dates(Md(now.AddDays(-3)), Md(now));
                Test.Check(OnAgainstTheGame(true), $"{H.Start.Value} to {H.End.Value}: today is the last day, and it counts");
                Dates(Md(now), Md(now));
                Test.Check(OnAgainstTheGame(true), $"{H.Start.Value} to {H.End.Value}: a window of today alone is on");
                Dates(Md(now.AddDays(1)), Md(now.AddDays(1)));
                Test.Check(!OnAgainstTheGame(false), $"{H.Start.Value} to {H.End.Value}: a window of tomorrow alone is off");
                Dates(Md(now.AddDays(-1)), Md(now.AddDays(-1)));
                Test.Check(!OnAgainstTheGame(false), $"{H.Start.Value} to {H.End.Value}: a window of yesterday alone is off");
                yield return null;
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // A window whose first day comes later in the year than its last runs past the new year. From ten days ago
        // round to twenty days ago is the whole year but nine days, today inside; from ten days ahead round to ten
        // days ago leaves out the twenty days around today.
        internal static IEnumerator NewYear()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            DateTime now = DateTime.Now;
            try
            {
                Dates(Md(now.AddDays(-10)), Md(now.AddDays(-20)));
                Test.Check(OnAgainstTheGame(true), $"{H.Start.Value} round the year to {H.End.Value}, today inside: on");
                Dates(Md(now.AddDays(10)), Md(now.AddDays(-10)));
                Test.Check(!OnAgainstTheGame(false), $"{H.Start.Value} round the year to {H.End.Value}, today left out: off");
                // the README's own example, whatever today is: read as a window, and on exactly from 20 December to 6 January
                Dates("12-20", "01-06");
                int today = now.Month * 100 + now.Day;
                bool inside = today >= 1220 || today <= 106;
                Test.Check(H.CustomWindow() == "12-20 to 01-06" && OnAgainstTheGame(inside) == inside, $"12-20 to 01-06 is read as a window past the new year; today ({Md(now)}) is {(inside ? "inside" : "outside")} it, and Halloween is {(H.IsOn() ? "on" : "off")}");
                yield return null;
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        internal static IEnumerator Forms()
        {
            if (!EdRun.Ready()) yield break;
            EdRun.Arrange();
            DateTime now = DateTime.Now, a = now.AddDays(-1), b = now.AddDays(1);
            try
            {
                // what is a date: month and day with -, / or a dot between, blanks around, a leading zero or none
                var good = new List<string[]>
                {
                    new[] { $"{a.Month}/{a.Day}", $"{b.Month}/{b.Day}" },
                    new[] { $"{a.Month:D2}.{a.Day:D2}", $"{b.Month:D2}.{b.Day:D2}" },
                    new[] { $"  {Md(a)} ", $" {Md(b)}  " },
                    new[] { $"{a.Month}-{a.Day}", $"{b.Month}-{b.Day}" }
                };
                var notRead = new List<string>();
                foreach (string[] g in good)
                {
                    Dates(g[0], g[1]);
                    if (H.CustomWindow() != $"{Md(a)} to {Md(b)}" || !OnAgainstTheGame(true)) notRead.Add($"\"{g[0]}\" to \"{g[1]}\" (read as {H.CustomWindow() ?? "no window"})");
                }
                Test.Check(notRead.Count == 0, $"{good.Count - notRead.Count} of {good.Count} ways to write the two days around today are read as {Md(a)} to {Md(b)}{EdWorld.Some(notRead)}");
                Dates("02-29", "03-01");
                Test.Check(H.CustomWindow() == "02-29 to 03-01", $"29 February is a day (\"{H.CustomWindow()}\")");
                yield return null;

                // what isn't: said once in the log for each, the game's own dates used, and the window's line says why
                string[] bad = { "13-45", "02-30", "00-10", "10-00", "abc", "10-24-2026", "10", "10-", "-24", "4-31" };
                int mark = EdRun.Mark;
                var taken = new List<string>();
                var silent = new List<string>();
                Dates("", Md(b));
                using (EdRun.Expect("dates are month-day"))
                {
                    foreach (string s in bad)
                    {
                        int before = EdRun.Count("dates are month-day", mark);
                        Dates(s, Md(b));
                        EdRun.Season(true);
                        bool said1 = EdRun.SeasonNow, on1 = H.IsOn();
                        EdRun.Season(false);
                        bool said2 = EdRun.SeasonNow, on2 = H.IsOn();
                        bool gameDecides = H.CustomWindow() == null && H.UsesGameDates() && on1 == said1 && on2 == said2;
                        if (!gameDecides || !H.DatesDontCount()) taken.Add($"\"{s}\"");
                        if (EdRun.Count("dates are month-day", mark) != before + 1) silent.Add($"\"{s}\" ({EdRun.Count("dates are month-day", mark) - before} line(s))");
                    }
                    // as the last day too
                    Dates(Md(a), "13-45");
                    if (H.CustomWindow() != null || !H.DatesDontCount()) taken.Add("a last day \"13-45\"");
                }
                Test.Check(taken.Count == 0, $"{bad.Length + 1 - taken.Count} of {bad.Length + 1} things that aren't a date leave the game's own dates in charge, with the window's line saying so{EdWorld.Some(taken)}");
                Test.Check(silent.Count == 0, $"each is said in the log once{EdWorld.Some(silent)}");
                yield return null;

                // one of the two alone (the other still to be typed): the game's dates, no warning, the line says why
                int warned = EdRun.Count("dates are month-day");
                Dates(Md(a), "");
                bool firstOnly = H.CustomWindow() == null && H.UsesGameDates() && H.DatesDontCount();
                Dates("", Md(b));
                bool lastOnly = H.CustomWindow() == null && H.UsesGameDates() && H.DatesDontCount();
                Test.Check(firstOnly && lastOnly && EdRun.Count("dates are month-day") == warned, $"the first day alone ({firstOnly}) or the last alone ({lastOnly}): the game's dates, said in the window's line, no warning in the log");
                Dates("", "");
                Test.Check(!H.DatesDontCount(), "no dates at all: nothing to say about them");
            }
            finally
            {
                EdRun.Arrange();
            }
        }
    }
}
