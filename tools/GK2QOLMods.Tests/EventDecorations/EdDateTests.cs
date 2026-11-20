extern alias EDasm;
using System;
using System.Collections;
using System.Collections.Generic;
using GK2ModTestBench;
using ED = EDasm::GK2EventDecorations;

namespace GK2QOLMods.Tests
{
    // The player's own dates for Halloween (Start and End, day-month): when they count, what counts as a date, the
    // first and the last day, a window past the new year. Every window is made from today's date, so the tests say
    // the same on any day; and where the force works, the game's own dates are made to say the opposite of what the
    // player's dates should give, so an answer can only come from the player's.
    internal static class EdDateTests
    {
        private static ED.GameEvent H => ED.Events.Halloween;

        private static string Dm(DateTime d) => $"{d.Day:D2}-{d.Month:D2}";

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
            H.Mode.Value = ED.EventMode.Auto; // the player's dates count By date
            DateTime now = DateTime.Now;
            try
            {
                Dates(Dm(now.AddDays(-1)), Dm(now.AddDays(1)));
                Test.Check(OnAgainstTheGame(true), $"{H.Start.Value} to {H.End.Value}, today inside: Halloween is on (the game's dates {(EdRun.SeasonNow ? "agree" : "say no")})");
                Test.Check(H.CustomWindow() == $"{Dm(now.AddDays(-1))} to {Dm(now.AddDays(1))}" && !H.UsesGameDates(), $"the mod reads them as \"{H.CustomWindow()}\" and no longer asks the game's dates");
                Dates(Dm(now.AddDays(10)), Dm(now.AddDays(20)));
                Test.Check(!OnAgainstTheGame(false), $"{H.Start.Value} to {H.End.Value}, today before it: Halloween is off (the game's dates {(EdRun.SeasonNow ? "say yes" : "agree")})");
                Dates(Dm(now.AddDays(-20)), Dm(now.AddDays(-10)));
                Test.Check(!OnAgainstTheGame(false), $"{H.Start.Value} to {H.End.Value}, today after it: off");
                yield return null;

                // only By date: Always and Never don't look at them
                Dates(Dm(now.AddDays(10)), Dm(now.AddDays(20)));
                H.Mode.Value = ED.EventMode.On;
                Test.Check(OnAgainstTheGame(true) && !H.DatesDontCount(), "Halloween on Always with dates that leave today out: on, and nothing said about the dates");
                Dates(Dm(now.AddDays(-1)), Dm(now.AddDays(1)));
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
            H.Mode.Value = ED.EventMode.Auto; // the player's dates count By date
            DateTime now = DateTime.Now;
            try
            {
                Dates(Dm(now), Dm(now.AddDays(3)));
                Test.Check(OnAgainstTheGame(true), $"{H.Start.Value} to {H.End.Value}: today is the first day, and it counts");
                Dates(Dm(now.AddDays(-3)), Dm(now));
                Test.Check(OnAgainstTheGame(true), $"{H.Start.Value} to {H.End.Value}: today is the last day, and it counts");
                Dates(Dm(now), Dm(now));
                Test.Check(OnAgainstTheGame(true), $"{H.Start.Value} to {H.End.Value}: a window of today alone is on");
                Dates(Dm(now.AddDays(1)), Dm(now.AddDays(1)));
                Test.Check(!OnAgainstTheGame(false), $"{H.Start.Value} to {H.End.Value}: a window of tomorrow alone is off");
                Dates(Dm(now.AddDays(-1)), Dm(now.AddDays(-1)));
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
            H.Mode.Value = ED.EventMode.Auto; // the player's dates count By date
            DateTime now = DateTime.Now;
            try
            {
                Dates(Dm(now.AddDays(-10)), Dm(now.AddDays(-20)));
                Test.Check(OnAgainstTheGame(true), $"{H.Start.Value} round the year to {H.End.Value}, today inside: on");
                Dates(Dm(now.AddDays(10)), Dm(now.AddDays(-10)));
                Test.Check(!OnAgainstTheGame(false), $"{H.Start.Value} round the year to {H.End.Value}, today left out: off");
                // the README's own example, whatever today is: read as a window, and on exactly from 20 December to 6 January
                Dates("20-12", "06-01");
                int today = now.Month * 100 + now.Day;
                bool inside = today >= 1220 || today <= 106;
                Test.Check(H.CustomWindow() == "20-12 to 06-01" && OnAgainstTheGame(inside) == inside, $"20-12 to 06-01 is read as a window past the new year; today ({Dm(now)}) is {(inside ? "inside" : "outside")} it, and Halloween is {(H.IsOn() ? "on" : "off")}");
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
            H.Mode.Value = ED.EventMode.Auto; // the player's dates count By date
            DateTime now = DateTime.Now, a = now.AddDays(-1), b = now.AddDays(1);
            try
            {
                // what is a date: day and month with -, / or a dot between, blanks around, a leading zero or none
                var good = new List<string[]>
                {
                    new[] { $"{a.Day}/{a.Month}", $"{b.Day}/{b.Month}" },
                    new[] { $"{a.Day:D2}.{a.Month:D2}", $"{b.Day:D2}.{b.Month:D2}" },
                    new[] { $"  {Dm(a)} ", $" {Dm(b)}  " },
                    new[] { $"{a.Day}-{a.Month}", $"{b.Day}-{b.Month}" }
                };
                var notRead = new List<string>();
                foreach (string[] g in good)
                {
                    Dates(g[0], g[1]);
                    if (H.CustomWindow() != $"{Dm(a)} to {Dm(b)}" || !OnAgainstTheGame(true)) notRead.Add($"\"{g[0]}\" to \"{g[1]}\" (read as {H.CustomWindow() ?? "no window"})");
                }
                Test.Check(notRead.Count == 0, $"{good.Count - notRead.Count} of {good.Count} ways to write the two days around today are read as {Dm(a)} to {Dm(b)}{EdWorld.Some(notRead)}");
                Dates("29-02", "01-03");
                Test.Check(H.CustomWindow() == "29-02 to 01-03", $"29 February is a day (\"{H.CustomWindow()}\")");
                // the day comes first: the game's own Halloween as a player writes it since 1.0.1 (the same two days
                // written 1.0.0's way, 10-24 and 11-07, are among the things that aren't a date, below)
                Dates("24-10", "07-11");
                int today = now.Month * 100 + now.Day;
                bool inside = today >= 1024 && today <= 1107;
                Test.Check(H.CustomWindow() == "24-10 to 07-11" && OnAgainstTheGame(inside) == inside,
                    $"24-10 to 07-11 is 24 October to 7 November (read as \"{H.CustomWindow()}\"); today ({Dm(now)}) is {(inside ? "inside" : "outside")} it, and Halloween is {(H.IsOn() ? "on" : "off")}");
                yield return null;

                // what isn't: said once in the log for each, the game's own dates used, and the window's line says why
                string[] bad = { "45-13", "30-02", "10-00", "00-10", "abc", "24-10-2026", "10", "10-", "-24", "31-4", "10-24", "11-30" }; // the last two: 1.0.0's month-day, no day-month date
                int mark = EdRun.Mark;
                var taken = new List<string>();
                var silent = new List<string>();
                Dates("", Dm(b));
                using (EdRun.Expect("dates are day-month"))
                {
                    foreach (string s in bad)
                    {
                        int before = EdRun.Count("dates are day-month", mark);
                        Dates(s, Dm(b));
                        EdRun.Season(true);
                        bool said1 = EdRun.SeasonNow, on1 = H.IsOn();
                        EdRun.Season(false);
                        bool said2 = EdRun.SeasonNow, on2 = H.IsOn();
                        bool gameDecides = H.CustomWindow() == null && H.UsesGameDates() && on1 == said1 && on2 == said2;
                        if (!gameDecides || !H.DatesDontCount()) taken.Add($"\"{s}\"");
                        if (EdRun.Count("dates are day-month", mark) != before + 1) silent.Add($"\"{s}\" ({EdRun.Count("dates are day-month", mark) - before} line(s))");
                    }
                    // as the last day too
                    Dates(Dm(a), "45-13");
                    if (H.CustomWindow() != null || !H.DatesDontCount()) taken.Add("a last day \"45-13\"");
                }
                Test.Check(taken.Count == 0, $"{bad.Length + 1 - taken.Count} of {bad.Length + 1} things that aren't a date leave the game's own dates in charge, with the window's line saying so{EdWorld.Some(taken)}");
                Test.Check(silent.Count == 0, $"each is said in the log once{EdWorld.Some(silent)}");
                yield return null;

                // one of the two alone (the other still to be typed): the game's dates, no warning, the line says why
                int warned = EdRun.Count("dates are day-month");
                Dates(Dm(a), "");
                bool firstOnly = H.CustomWindow() == null && H.UsesGameDates() && H.DatesDontCount();
                Dates("", Dm(b));
                bool lastOnly = H.CustomWindow() == null && H.UsesGameDates() && H.DatesDontCount();
                Test.Check(firstOnly && lastOnly && EdRun.Count("dates are day-month") == warned, $"the first day alone ({firstOnly}) or the last alone ({lastOnly}): the game's dates, said in the window's line, no warning in the log");
                Dates("", "");
                Test.Check(!H.DatesDontCount(), "no dates at all: nothing to say about them");
            }
            finally
            {
                EdRun.Arrange();
            }
        }

        // ------------------------------------------------------------------ a settings file of 1.0.0

        // The mod's own start steps for the dates, on a settings file of the test's own (never the player's): is the
        // file 1.0.0's (asked before anything is bound), the binding, the dates turned round if it was, one save.
        private static (string Start, string End, bool WasOld, string File) Start101(string path)
        {
            bool old = ED.OldDates.FileIsMonthDay(path);
            var cfg = new BepInEx.Configuration.ConfigFile(path, false);
            cfg.SaveOnConfigSet = false; // as the settings page holds it until its one save
            var e = new ED.GameEvent("Halloween");
            e.Bind(cfg, "the mode", "");
            ED.OldDates.Bind(cfg);
            if (old) ED.OldDates.Turn(e, null);
            cfg.Save();
            return (e.Start.Value, e.End.Value, old, System.IO.File.ReadAllText(path));
        }

        // A settings file as 1.0.0 wrote it: no DateForm line.
        private static string File100(string start, string end) =>
            "[General]\r\n\r\nEnabled = true\r\n\r\n[Halloween]\r\n\r\nMode = Auto\r\n\r\nStart = " + start + "\r\n\r\nEnd = " + end + "\r\n";

        internal static IEnumerator Upgrade()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gk2-event-decorations-test-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                // what 1.0.0 held (Start, End) and what 1.0.1 holds after its first start on that file
                var cases = new (string Name, string Start, string End, string WantStart, string WantEnd)[]
                {
                    ("both dates", "11-01", "11-20", "01-11", "20-11"),
                    ("the game's own Halloween typed in", "10-24", "11-07", "24-10", "07-11"),
                    ("a window past the new year", "12-20", "01-06", "20-12", "06-01"),
                    ("written with slashes and no zeros", "10/1", "11/30", "01-10", "30-11"),
                    ("the first date only", "10-24", "", "24-10", ""),
                    ("the last date only", "", "11-07", "", "07-11"),
                    ("no dates", "", "", "", ""),
                    ("something 1.0.0 took for no date beside a date", "11-1A", "11-20", "11-1A", "20-11"),
                    ("a date 1.0.0 took for none, being day-month already", "24-10", "11-07", "24-10", "07-11")
                };
                var wrong = new List<string>();
                int n = 0;
                foreach (var c in cases)
                {
                    string path = System.IO.Path.Combine(dir, "case" + n++ + ".cfg");
                    System.IO.File.WriteAllText(path, File100(c.Start, c.End));
                    var first = Start101(path);
                    bool line = first.File.Contains("DateForm = day-month");
                    if (!first.WasOld || first.Start != c.WantStart || first.End != c.WantEnd || !line
                        || !first.File.Contains("Start = " + c.WantStart) || !first.File.Contains("End = " + c.WantEnd))
                    {
                        wrong.Add($"{c.Name} (\"{c.Start}\", \"{c.End}\"): taken as 1.0.0's {first.WasOld}, now \"{first.Start}\", \"{first.End}\" (expected \"{c.WantStart}\", \"{c.WantEnd}\"), the DateForm line written {line}");
                        continue;
                    }
                    // the next start, and the one after: the file has the line now, nothing is turned again
                    var second = Start101(path);
                    var third = Start101(path);
                    if (second.WasOld || third.WasOld || second.Start != c.WantStart || second.End != c.WantEnd || third.Start != c.WantStart || third.End != c.WantEnd)
                    {
                        wrong.Add($"{c.Name}: at the next starts taken as 1.0.0's again ({second.WasOld}, {third.WasOld}) or changed: \"{second.Start}\", \"{second.End}\" then \"{third.Start}\", \"{third.End}\"");
                    }
                }
                Test.Check(wrong.Count == 0, $"{cases.Length - wrong.Count} of {cases.Length} settings files of 1.0.0 keep their days at the first start of 1.0.1 (month-day turned to day-month, what was no date left alone, the DateForm line written) and are not touched at the starts after{EdWorld.Some(wrong)}");
                yield return null;

                // a first start (no file): nothing to turn, the line written
                string fresh = System.IO.Path.Combine(dir, "fresh.cfg");
                var f = Start101(fresh);
                Test.Check(!f.WasOld && f.Start == "" && f.End == "" && f.File.Contains("DateForm = day-month"), $"a first start with no settings file: nothing taken as 1.0.0's ({f.WasOld}), the dates empty, the DateForm line written");
                // a file of 1.0.1 whose dates would also read as month-day: left as they are, whatever the line's value
                string later = System.IO.Path.Combine(dir, "later.cfg");
                System.IO.File.WriteAllText(later, "[General]\r\n\r\nDateForm = anything\r\n\r\n[Halloween]\r\n\r\nStart = 01-11\r\n\r\nEnd = 05-12\r\n");
                var l = Start101(later);
                Test.Check(!l.WasOld && l.Start == "01-11" && l.End == "05-12", $"a file that has the DateForm line (whatever it says) is not 1.0.0's: its dates stay \"{l.Start}\" and \"{l.End}\"");
                Test.Check(ED.OldDates.Turned("02-29") == "29-02" && ED.OldDates.Turned("13-01") == null && ED.OldDates.Turned("") == null,
                    "one date turned: 02-29 becomes 29-02; 13-01 was no date in 1.0.0 and is not turned");
            }
            finally
            {
                try
                {
                    System.IO.Directory.Delete(dir, true);
                }
                catch (Exception e)
                {
                    Test.Info($"the test's own folder {dir} could not be removed ({e.GetType().Name}); it holds only its scratch settings files");
                }
            }
        }
    }
}
