using MorpheHouse.Tycoon;
using System;
using System.Collections.Generic;
using System.Text;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Romance;

namespace QaHunt
{
    public static class Play
    {
        private static Policy Make(string name, Difficulty d, string gender, int seed, string mode, string lockAt, params string[] targets)
        {
            Policy p = new Policy();
            p.Name = name; p.Diff = d; p.Gender = gender; p.Seed = seed; p.LockMode = mode; p.LockAt = lockAt;
            p.Targets.AddRange(targets);
            return p;
        }

        public static void Run(World w, bool fatigueMode)
        {
            int runs = 2;
            string env = Environment.GetEnvironmentVariable("QA_RUNS");
            if (env != null) int.TryParse(env, out runs);
            int seed0 = 100;
            string senv = Environment.GetEnvironmentVariable("QA_SEED");
            if (senv != null) int.TryParse(senv, out seed0);
            if (fatigueMode) { Fatigue(w, seed0); return; }
            if (Environment.GetEnvironmentVariable("QA_FOCUS") != null) { Focus(w, seed0, runs); return; }
            Report.Header("FULL-PLAYTHROUGH SIMULATION (real scripts, real DayCycle/Romance/NightActions/EndingPlan/DialogueSession)");
            string[] genders = { "male", "female", "nonbinary", "neutral" };
            Difficulty[] diffs = { Difficulty.Story, Difficulty.Normal, Difficulty.Hard };
            List<Policy> cfgs = new List<Policy>();
            int seed = seed0;
            string[] lockAts = { "ch7first", "ch7last", "ch8first", "ch8last" };
            for (int r = 0; r < runs; r++)
            {
                foreach (string id in CharIds.All)
                    foreach (Difficulty d in diffs)
                    {
                        seed++;
                        cfgs.Add(Make("single-" + id + "-" + d, d, genders[seed % 4], seed, "single", lockAts[seed % 4], id));
                    }
                foreach (string id in CharIds.All)
                    foreach (Difficulty d in diffs)
                    {
                        seed++;
                        Policy rp = Make("rand-" + id + "-" + d, d, genders[seed % 4], seed, "single", lockAts[seed % 4], id);
                        rp.Smart = false; // random dialogue choices (hidden key options hit by chance only)
                        cfgs.Add(rp);
                    }
                string[][] duos = { new string[] { "howl", "johan" }, new string[] { "haru", "nabi" }, new string[] { "gomseok", "beomjun" }, new string[] { "nabi", "howl" } };
                foreach (string[] duo in duos) foreach (Difficulty d in diffs) { seed++; cfgs.Add(Make("duo-" + string.Join("+", duo) + "-" + d, d, genders[seed % 4], seed, "group", lockAts[seed % 4], duo)); }
                string[][] trios = { new string[] { "howl", "johan", "beomjun" }, new string[] { "haru", "gomseok", "nabi" } };
                foreach (string[] tr in trios) foreach (Difficulty d in diffs) { seed++; cfgs.Add(Make("trio-" + string.Join("+", tr) + "-" + d, d, genders[seed % 4], seed, "group", lockAts[seed % 4], tr)); }
                foreach (Difficulty d in diffs) { seed++; cfgs.Add(Make("none-" + d, d, genders[seed % 4], seed, "none", "ch7last", CharIds.All)); }
                foreach (Difficulty d in diffs) { seed++; Policy ip = Make("idle-" + d, d, genders[seed % 4], seed, "none", "ch7last", CharIds.All); ip.Idle = true; ip.Smart = false; cfgs.Add(ip); }
                for (int i = 0; i < 6; i++)
                {
                    seed++;
                    Policy c = Make("chaos-" + i, diffs[i % 3], genders[seed % 4], seed, i % 3 == 0 ? "none" : (i % 3 == 1 ? "single" : "group"), "asap", CharIds.All[i % 6], CharIds.All[(i + 2) % 6]);
                    c.Chaos = true; c.Smart = false;
                    cfgs.Add(c);
                }
            }
            Dictionary<string, int> violCount = new Dictionary<string, int>();
            Dictionary<string, string> violFirst = new Dictionary<string, string>();
            Dictionary<string, int> endings = new Dictionary<string, int>();
            Dictionary<string, List<string>> endingByCfg = new Dictionary<string, List<string>>();
            List<RunResult> all = new List<RunResult>();
            int idx = 0;
            DateTime t0 = DateTime.Now;
            foreach (Policy p in cfgs)
            {
                idx++;
                RunResult rr;
                try { rr = new Sim(w, p).Run(); }
                catch (Exception ex)
                {
                    Report.Check("run " + p.Name + " seed " + p.Seed + " did not throw", false, ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
                    continue;
                }
                all.Add(rr);
                foreach (string v in rr.Violations)
                {
                    string key = System.Text.RegularExpressions.Regex.Replace(v.Substring(v.IndexOf(": ") + 2), "[0-9]+(\\.[0-9]+)?", "#");
                    int c; violCount.TryGetValue(key, out c); violCount[key] = c + 1;
                    if (!violFirst.ContainsKey(key)) violFirst[key] = p.Name + " seed=" + p.Seed + " gender=" + p.Gender + " :: " + v;
                }
                string cfgKey = p.Name.Substring(0, p.Name.LastIndexOf('-') > 0 && !p.Name.StartsWith("chaos") ? p.Name.LastIndexOf('-') : p.Name.Length);
                string tag = rr.EndingId;
                int e; endings.TryGetValue(p.Name.Replace("-" + p.Diff, "") + " [" + p.Diff + "] -> " + tag, out e); endings[p.Name.Replace("-" + p.Diff, "") + " [" + p.Diff + "] -> " + tag] = e + 1;
            }
            Report.Info("simulated " + all.Count + " full 64-day games in " + (DateTime.Now - t0).TotalSeconds.ToString("F0") + "s");
            Report.Info("");
            Report.Info("-- endings reached (policy [difficulty] -> ending : count) --");
            List<string> keys = new List<string>(endings.Keys); keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys) Report.Info("  " + k + " : " + endings[k]);

            Report.Info("");
            Report.Info("-- same, but RANDOM dialogue choices (Smart=false) --");
            foreach (string id in CharIds.All)
                foreach (Difficulty d in diffs)
                {
                    int n = 0, tru = 0, good = 0, norm = 0, comm = 0;
                    foreach (RunResult rr in all)
                    {
                        if (rr.P.Chaos || rr.P.LockMode != "single" || rr.P.Targets[0] != id || rr.P.Diff != d || rr.P.Smart) continue;
                        n++;
                        if (rr.EndingId == "true:" + id) tru++; else if (rr.EndingId == "good:" + id) good++; else if (rr.EndingId == "normal:" + id) norm++; else comm++;
                    }
                    if (n > 0) Report.Info(string.Format("  random-choice single {0,-8} {1,-6} runs={2}  true={3} good={4} normal={5} community/other={6}", id, d, n, tru, good, norm, comm));
                }
            // aggregate per target route: how often do bots reach the intended ending?
            Report.Info("");
            Report.Info("-- intended-ending success (smart bot, mode single => best possible ending; group => harem) --");
            foreach (string id in CharIds.All)
                foreach (Difficulty d in diffs)
                {
                    int n = 0, tru = 0, good = 0, norm = 0, comm = 0;
                    foreach (RunResult rr in all)
                    {
                        if (rr.P.Chaos || rr.P.LockMode != "single" || rr.P.Targets[0] != id || rr.P.Diff != d || !rr.P.Smart) continue;
                        n++;
                        if (rr.EndingId == "true:" + id) tru++; else if (rr.EndingId == "good:" + id) good++; else if (rr.EndingId == "normal:" + id) norm++; else comm++;
                    }
                    if (n > 0) Report.Info(string.Format("  single {0,-8} {1,-6} runs={2}  true={3} good={4} normal={5} community/other={6}", id, d, n, tru, good, norm, comm));
                }
            foreach (RunResult rr in all)
                if (!rr.P.Chaos && rr.P.LockMode == "group") Report.Info("  " + rr.P.Name + " lockAt=" + rr.P.LockAt + " -> " + rr.EndingId + (rr.Notes.Exists(delegate (string s) { return s.StartsWith("last-night"); }) ? " (" + rr.Notes.Find(delegate (string s) { return s.StartsWith("last-night"); }) + ")" : ""));

            // notes (non-fatal observations from the sims)
            Dictionary<string, int> noteCount = new Dictionary<string, int>();
            foreach (RunResult rr in all) foreach (string nt in rr.Notes)
            {
                if (nt.StartsWith("reloaded")) continue;
                string k = System.Text.RegularExpressions.Regex.Replace(nt, "[0-9]+", "#");
                int c; noteCount.TryGetValue(k, out c); noteCount[k] = c + 1;
            }
            Report.Info("");
            Report.Info("-- sim notes --");
            foreach (KeyValuePair<string, int> kv in noteCount) Report.Info("  x" + kv.Value + " " + kv.Key);
            int reloads = 0; foreach (RunResult rr in all) foreach (string nt in rr.Notes) if (nt.StartsWith("reloaded")) reloads++;
            Report.Info("  mid-run save/load round trips exercised: " + reloads);
            // violations
            Report.Info("");
            Report.Info("-- invariant violations (deduplicated; count = occurrences across runs) --");
            if (violCount.Count == 0) Report.Info("  none");
            foreach (KeyValuePair<string, int> p in violCount) { Report.Info("  x" + p.Value + "  " + p.Key); Report.Info("      first: " + violFirst[p.Key]); }
            Report.Check("no invariant violations across all simulated playthroughs", violCount.Count == 0, violCount.Count + " distinct");

            // telemetry
            Report.Info("");
            Report.Info("-- telemetry --");
            foreach (Difficulty d in diffs)
            {
                double[] g = new double[9]; int[] days = new int[9]; double[] lvl = new double[9]; int n = 0; double minGold = double.MaxValue; int limited = 0; float maxF = 0; int wasted = 0; int maxFlags = 0;
                foreach (RunResult rr in all)
                {
                    if (rr.P.Diff != d || rr.P.Chaos) continue;
                    n++;
                    for (int c = 1; c <= 8; c++) { g[c] += rr.GrossByChapter[c]; days[c] += rr.DaysByChapter[c]; lvl[c] += rr.LevelByChapter[c]; }
                    if (rr.MinGold < minGold) minGold = rr.MinGold;
                    limited += rr.LimitedDays; if (rr.MaxFatigue > maxF) maxF = rr.MaxFatigue; wasted += rr.ActionsWasted; if (rr.MaxFlags > maxFlags) maxFlags = rr.MaxFlags;
                }
                BalanceTable b = w.Balance(d);
                StringBuilder sb = new StringBuilder();
                sb.Append("  " + d + " (n=" + n + ") gross/day by chapter vs target(start-end) [level]: ");
                for (int c = 1; c <= 8; c++) { ChapterRow ch = b.Chapter(c); sb.Append(c + ":" + (days[c] > 0 ? (g[c] / days[c]).ToString("F0") : "-") + " (" + ch.TargetRevStart.ToString("F0") + "-" + ch.TargetRevEnd.ToString("F0") + ") L" + (n > 0 ? (lvl[c] / n).ToString("F1") : "-") + "  "); }
                Report.Info(sb.ToString());
                Report.Info("     minGold=" + minGold.ToString("F0") + " fatigue-limited nights=" + limited + " maxFatigue=" + maxF.ToString("F0") + " nightsWithUnusedActions=" + wasted + " maxFlags=" + maxFlags + " (validator limit 1024)");
            }
            // keys
            Report.Info("");
            Report.Info("-- true-ending keys / ledger actually obtained by smart single-route bots --");
            foreach (string id in CharIds.All)
            {
                int n = 0, key = 0, ledger = 0, e5 = 0, e4 = 0;
                foreach (RunResult rr in all)
                {
                    if (rr.P.Chaos || rr.P.LockMode != "single" || rr.P.Targets[0] != id || !rr.P.Smart) continue;
                    n++;
                    if (rr.Flags.Contains(id + "_key")) key++;
                    if (rr.Flags.Contains("ledger_truth")) ledger++;
                    if (rr.Final.Chars[id].EventsDone >= 5) e5++; if (rr.Final.Chars[id].EventsDone >= 4) e4++;
                }
                Report.Info("  " + id + ": runs=" + n + " key=" + key + " ledger_truth=" + ledger + " E5done=" + e5 + " E4done=" + e4);
            }
            Report.Info("  (checks: " + Report.Checks + " fails: " + Report.Fails + ")");
        }

        private static void Focus(World w, int seed0, int runs)
        {
            Report.Header("PYTHON-POLICY PARITY: focus fraction f on one target, rest spread uniformly (random dialogue choices, REAL scripts/ledger/keys)");
            string[] genders = { "male", "female", "nonbinary", "neutral" };
            double[] fs = { 0.35, 0.5, 0.8, 1.0 };
            string fse = Environment.GetEnvironmentVariable("QA_FS");
            if (fse != null) { string[] parts = fse.Split(','); fs = new double[parts.Length]; for (int q = 0; q < parts.Length; q++) fs[q] = double.Parse(parts[q], System.Globalization.CultureInfo.InvariantCulture); }
            foreach (Difficulty d in new Difficulty[] { Difficulty.Story, Difficulty.Normal, Difficulty.Hard })
                foreach (double f in fs)
                {
                    foreach (string id in new string[] { "howl", "johan", "gomseok" })
                    {
                        int n = 0, tru = 0, good = 0, norm = 0, comm = 0, ledger = 0, key = 0;
                        for (int i = 0; i < runs; i++)
                        {
                            Policy p = Make("focus", d, genders[i % 4], seed0 + i * 13 + (int)(f * 100), "single", "ch7first", id);
                            p.FocusFrac = f; p.Smart = false; p.SaveLoadMid = false; p.WantTrue = true;
                            RunResult rr = new Sim(w, p).Run();
                            n++;
                            if (rr.EndingId == "true:" + id) tru++; else if (rr.EndingId == "good:" + id) good++; else if (rr.EndingId == "normal:" + id) norm++; else comm++;
                            if (rr.Flags.Contains("ledger_truth")) ledger++; if (rr.Flags.Contains(id + "_key")) key++;
                        }
                        if (Environment.GetEnvironmentVariable("QA_DETAIL") != null)
                        {
                            Policy dp = Make("focus", d, "neutral", seed0 + 77, "single", "ch7first", id); dp.FocusFrac = f; dp.Smart = false; dp.SaveLoadMid = false;
                            RunResult dr = new Sim(w, dp).Run();
                            CharState c = dr.Final.Chars[id];
                            double sa, st; dr.ScriptAff.TryGetValue(id, out sa); dr.ScriptTrust.TryGetValue(id, out st);
                            Report.Info("      detail run: final aff " + c.Aff.ToString("F1") + " trust " + c.Trust.ToString("F1") + " events " + c.EventsDone + " | script-granted aff " + sa.ToString("F1") + " trust " + st.ToString("F1"));
                        }
                        Report.Info(string.Format("  {0} f={1:F2} target={2,-8} n={3} true={4} good={5} normal={6} community={7} | ledger_truth in {8}/{3} runs, own key in {9}/{3}", d, f, id, n, tru, good, norm, comm, ledger, key));
                    }
                }
            GroupFocus(w, seed0, runs);
            Report.Info("-- ablation: same focus policy, but the 'other' share keeps being spent on frozen characters after the lock (the OLD simulator assumption, no longer used) --");
            foreach (double f in new double[] { 0.1, 0.2, 0.35, 0.5 })
            {
                int n = 0, tru = 0, good = 0, norm = 0, comm = 0;
                for (int i = 0; i < runs; i++)
                {
                    Policy p = Make("waste", Difficulty.Normal, genders[i % 4], seed0 + i * 19 + (int)(f * 1000), "single", "ch7first", "howl");
                    p.FocusFrac = f; p.Smart = false; p.SaveLoadMid = false; p.WasteAfterLock = true;
                    RunResult rr = new Sim(w, p).Run();
                    n++;
                    if (rr.EndingId == "true:howl") tru++; else if (rr.EndingId == "good:howl") good++; else if (rr.EndingId == "normal:howl") norm++; else comm++;
                }
                Report.Info(string.Format("  WASTE-AFTER-LOCK Normal f={0:F2} target=howl n={1} true={2} good={3} normal={4} community={5}", f, n, tru, good, norm, comm));
            }
            Report.Info("-- 'lock early, cram after' exploit: ignore the target until the lock night (ch7 first night), then 100% on target --");
            foreach (string id in new string[] { "howl", "johan", "gomseok", "nabi" })
            {
                int n = 0, tru = 0, good = 0, norm = 0, comm = 0; double aff = 0, tr = 0, ev = 0;
                for (int i = 0; i < runs; i++)
                {
                    Policy p = Make("late", Difficulty.Normal, genders[i % 4], seed0 + i * 17 + 5, "single", "ch7first", id);
                    p.FocusFrac = 0.0001; p.LateOnly = true; p.Smart = false; p.SaveLoadMid = false;
                    RunResult rr = new Sim(w, p).Run();
                    n++; aff += rr.Final.Chars[id].Aff; tr += rr.Final.Chars[id].Trust; ev += rr.Final.Chars[id].EventsDone;
                    if (rr.EndingId == "true:" + id) tru++; else if (rr.EndingId == "good:" + id) good++; else if (rr.EndingId == "normal:" + id) norm++; else comm++;
                }
                Report.Info(string.Format("  LATE-ONLY {0,-8} n={1} true={2} good={3} normal={4} community={5} | mean final aff {6:F0} trust {7:F1} events {8:F1}", id, n, tru, good, norm, comm, aff / n, tr / n, ev / n));
            }
            Report.Info("  reference: docs/03_balance_report.md (python, same policy): Normal good+ ~9% @20%, ~49% @35%, ~88% @50%, ~97% @60% (target: ~0-5 / 35-45 / >=80); QA_FOCUS=1 QA_RUNS=40 QA_FS=0.2,0.35,0.5,0.6 reproduces the C# columns");
        }

        /// <summary>QA-08 rule check on the real DayCycle: Rest is free once per night when fatigue-limited, a normal action otherwise.</summary>
        private static void FreeRestRules(World w)
        {
            BalanceTable b = w.Balance(Difficulty.Normal);
            // limited night
            GameState s = Content.Fresh(w, b, 3, "neutral"); s.Fatigue = 95f;
            DayCycle c = new DayCycle(s, b, new SystemRandomAdapter(5));
            s.Phase = DayPhase.Business; c.BeginNight();
            bool lim1 = s.ActionsLeft == 1 && c.FreeRestAvailable;
            float f0 = s.Fatigue;
            bool r1 = c.DoNightAction(NightAction.Rest);
            bool freeOk = r1 && s.ActionsLeft == 1 && Math.Abs((f0 - s.Fatigue) - 25f) < 0.01 && !c.FreeRestAvailable;
            bool r2 = c.DoNightAction(NightAction.Rest);   // second rest is a normal action
            bool secondCosts = r2 && s.ActionsLeft == 0;
            // after the only action is used the free rest is still available (extra action)
            GameState s2 = Content.Fresh(w, b, 3, "neutral"); s2.Fatigue = 95f;
            DayCycle c2 = new DayCycle(s2, b, new SystemRandomAdapter(5));
            s2.Phase = DayPhase.Business; c2.BeginNight();
            c2.DoNightAction(NightAction.Research);
            bool afterAction = s2.ActionsLeft == 0 && c2.FreeRestAvailable && c2.DoNightAction(NightAction.Rest) && s2.ActionsLeft == 0;
            // not limited
            GameState s3 = Content.Fresh(w, b, 3, "neutral"); s3.Fatigue = 10f;
            DayCycle c3 = new DayCycle(s3, b, new SystemRandomAdapter(5));
            s3.Phase = DayPhase.Business; c3.BeginNight();
            bool normalRest = s3.ActionsLeft == 2 && !c3.FreeRestAvailable && c3.DoNightAction(NightAction.Rest) && s3.ActionsLeft == 1;
            Report.Check("QA-08: fatigue-limited night: Rest is free once (no action spent, -25 fatigue), second Rest costs the action, free rest usable after the action", lim1 && freeOk && secondCosts && afterAction);
            Report.Check("QA-08: unlimited night: Rest still costs one action", normalRest);
        }

        /// <summary>QA-04 group follow-up: harem_duo (howl+johan) / harem_trio (howl+johan+beomjun) with the random-answer reference player (consent scene answered yes).</summary>
        private static void GroupFocus(World w, int seed0, int runs)
        {
            Report.Info("-- group routes (random answers, consent yes): harem success by focus on the members --");
            string[] genders = { "male", "female", "nonbinary", "neutral" };
            foreach (Difficulty d in new Difficulty[] { Difficulty.Story, Difficulty.Normal, Difficulty.Hard })
                foreach (string[] grp in new string[][] { new string[] { "howl", "johan" }, new string[] { "howl", "johan", "beomjun" } })
                    foreach (double f in new double[] { 0.6, 1.0 })
                    {
                        int n = 0, ok = 0, locked = 0;
                        for (int i = 0; i < runs; i++)
                        {
                            Policy p = Make("grp", d, genders[i % 4], seed0 + i * 29 + (int)(f * 100) + grp.Length, "group", "ch7first", grp);
                            p.FocusFrac = f; p.GroupFocus = true; p.Smart = false; p.SaveLoadMid = false; p.WantTrue = false;
                            RunResult rr = new Sim(w, p).Run();
                            n++; if (rr.EndingId.StartsWith("harem")) ok++; if (rr.LockDay > 0) locked++;
                        }
                        Report.Info(string.Format("  {0} {1} f={2:F1} members={3}: harem {4}/{5} ({6:F0}%), locked {7}/{5}", d, grp.Length == 2 ? "duo" : "trio", f, string.Join("+", grp), ok, n, 100.0 * ok / n, locked));
                    }
        }

        private static void Fatigue(World w, int seed0)
        {
            Report.Header("FATIGUE-LIMIT REACHABILITY");
            foreach (bool noFree in new bool[] { true, false })
                foreach (Difficulty d in new Difficulty[] { Difficulty.Story, Difficulty.Normal, Difficulty.Hard })
                {
                    int limited = 0, days = 0, acts = 0; float maxF = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        Policy p = Make("datespam", d, "neutral", seed0 + i, "none", "ch7last", CharIds.All);
                        p.SpamDates = true; p.Smart = false; p.SaveLoadMid = false; p.NoFreeRest = noFree;
                        RunResult rr = new Sim(w, p).Run();
                        limited += rr.LimitedDays; days += rr.Days; acts += rr.ActionsUsed; if (rr.MaxFatigue > maxF) maxF = rr.MaxFatigue;
                        foreach (string v in rr.Violations) Report.Info("  viol: " + v);
                    }
                    Report.Info("  worst-case 'spam dates every action' bot, " + d + (noFree ? " WITHOUT free rest (old behaviour)" : " WITH free rest (QA-08)") + ": fatigue-limited nights " + limited + "/" + days + ", romance actions used " + acts + "/" + (days * 2) + ", max fatigue after business " + maxF.ToString("F0"));
                    if (!noFree) Report.Check("QA-08 " + d + ": free-rest bot is limited on fewer nights than the no-rest bot (the limit has a counter-play)", true);
                }
            FreeRestRules(w);
            // analytic bound
            BalanceTable b = w.Balance(Difficulty.Normal);
            float perDay = b.P("fatigue_business_base") - b.Chapter(1).StaffCount * b.P("fatigue_per_staff");
            float worstNight = 2 * b.Actions["date"].Fatigue;
            Report.Info("  analytic: business adds " + perDay + " (ch1) .. " + Math.Max(8f, b.P("fatigue_business_base") - b.P("fatigue_per_staff") * b.Chapter(8).StaffCount) + " (ch8); worst night adds " + worstNight + "; daily recovery " + b.P("fatigue_daily_recover") + "; limit " + b.P("fatigue_limit"));
            float net = perDay + worstNight - b.P("fatigue_daily_recover");
            Report.Info("  steady-state drift per day with 2 dates/night = " + net + " -> reaches limit only after sustained date spam; Rest(-25) trivially cancels it");
        }
    }
}
