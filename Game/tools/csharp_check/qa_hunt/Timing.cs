using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;

namespace QaHunt
{
    /// <summary>
    /// Can the host enforce the narrative timing the scripts assume?  For each character an 'all-in from day 1' bot records the
    /// chapter in which each personal event actually becomes playable, and compares it with the chapter written in the E-node header.
    /// </summary>
    public static class Timing
    {
        public static int ChapterOfDay(BalanceTable b, int day)
        {
            int d = day;
            for (int c = 1; c <= b.MaxChapter; c++) { int n = b.Chapter(c).Days; if (d <= n) return c; d -= n; }
            return b.MaxChapter;
        }

        public static void Run(World w)
        {
            Report.Header("STORY-TIMING ENFORCEMENT (docs/12 section 5-1 and E-node narrative chapters)");
            BalanceTable b = w.Balance(Difficulty.Normal);
            // narrative chapter from each E node's first comment line "(N장"
            Dictionary<string, int> narrative = new Dictionary<string, int>();
            foreach (string f in System.IO.Directory.GetFiles(w.YarnDir, "*_E?.yarn", System.IO.SearchOption.AllDirectories))
            {
                string first = System.IO.File.ReadAllLines(f)[0];
                Match m = Regex.Match(first, "(\\p{L}+)_E([1-5])");
                Match ch = Regex.Match(first, "\\((\\d)장");
                Match who = Regex.Match(System.IO.Path.GetFileName(f), "^([a-z]+)_E([1-5])");
                if (who.Success && ch.Success) narrative[who.Groups[1].Value + "_E" + who.Groups[2].Value] = int.Parse(ch.Groups[1].Value);
            }
            int early = 0;
            StringBuilder table = new StringBuilder();
            foreach (string id in CharIds.All)
            {
                Policy p = new Policy(); p.Name = "timing-" + id; p.Seed = 77; p.Diff = Difficulty.Normal; p.LockMode = "none"; p.Targets.Add(id); p.Smart = false; p.SaveLoadMid = false; p.FocusFrac = 1.0; p.WantTrue = false;
                // all actions on this single character, events first
                Sim sim = new Sim(w, p);
                RunResult rr = sim.Run();
                CharacterRow cr = w.Cat.FindCharacter(id);
                Report.Info("   [" + id + " final aff " + rr.Final.Chars[id].Aff.ToString("F0") + " trust " + rr.Final.Chars[id].Trust.ToString("F1") + " events " + rr.Final.Chars[id].EventsDone + " notes=" + string.Join(";", rr.Notes.ToArray()) + "]");
                StringBuilder line = new StringBuilder();
                line.Append("  " + id + " (first_chapter=" + cr.FirstChapter + ", staff_join=" + cr.StaffJoinChapter + "): ");
                for (int st = 1; st <= 5; st++)
                {
                    int day;
                    string key = id + "_E" + st;
                    if (!rr.EventDay.TryGetValue(key, out day)) { line.Append("E" + st + ":never "); continue; }
                    int chap = ChapterOfDay(b, day);
                    int nar; narrative.TryGetValue(key, out nar);
                    bool tooEarly = nar > 0 && chap < nar;
                    if (tooEarly) early++;
                    line.Append("E" + st + ": day" + day + "=ch" + chap + " (script says ch" + nar + ")" + (tooEarly ? " **TOO EARLY** " : " "));
                }
                Report.Info(line.ToString());
            }
            Report.Check("every personal event is first playable no earlier than the chapter its script is written for", early == 0, early + " events can be triggered before their story chapter by an all-in player");
            // QA-03: characters are not selectable before their first_chapter (characters.csv) - talk / gift / date / event / pick lists
            Report.Info("-- character availability by chapter (characters.csv first_chapter) --");
            int leaks = 0, wrongRefusals = 0, listLeaks = 0;
            foreach (string id in CharIds.All)
            {
                int first = w.Cat.FindCharacter(id).FirstChapter;
                for (int chap = 1; chap <= 8; chap++)
                {
                    GameState g = Content.Fresh(w, b, chap, "neutral"); g.Day = 1 + chap * 8; g.DayInChapter = 1; g.Phase = DayPhase.Night; g.ActionsLeft = 2; g.Gold = 99999;
                    RomanceService r2 = new RomanceService(g, b, w.Cat, new Random(1).NextDouble);
                    RecSink sk = new RecSink();
                    DialogueSession ds = Runner.NewSession(w, g, r2, sk);
                    NightRomanceActions na = new NightRomanceActions(g, b, r2, w.Cat, ds.HasNode, ds.HasVisited);
                    bool met = chap >= first;
                    NightActionOutcome t = na.Talk(id);
                    g.ActionsLeft = 2; NightActionOutcome gf = na.Gift(id, "small_plant");
                    g.ActionsLeft = 2; NightActionOutcome dt = na.Date(id, "beach");
                    g.ActionsLeft = 2; NightActionOutcome ev = na.Event(id);
                    if (!met)
                    {
                        if (t.Success || gf.Success || dt.Success || ev.Success) { leaks++; Report.Info("  LEAK ch" + chap + " " + id); }
                        if (t.Result.Reason != RomanceRefusal.NotMet || gf.Result.Reason != RomanceRefusal.NotMet || dt.Result.Reason != RomanceRefusal.NotMet || ev.Result.Reason != RomanceRefusal.NotMet) wrongRefusals++;
                        if (t.RefusalMessage.Length == 0 || na.CharUnavailableText(id).Length == 0 || na.GiftBlockReason(id) != RomanceRefusal.NotMet || na.DateBlockReason(id) != RomanceRefusal.NotMet) wrongRefusals++;
                        if (na.AvailableChars().Contains(id)) listLeaks++;
                    }
                    else
                    {
                        if (!t.Success) wrongRefusals++;
                        if (!na.AvailableChars().Contains(id)) listLeaks++;
                        if (na.CharUnavailableText(id).Length != 0) wrongRefusals++;
                    }
                }
                Report.Info("  " + id + ": first_chapter=" + w.Cat.FindCharacter(id).FirstChapter + " staff_join=" + w.Cat.FindCharacter(id).StaffJoinChapter + " (BalanceTable agrees: " + b.FirstChapterOf(id) + "/" + b.StaffJoinChapterOf(id) + ")");
            }
            Report.Check("QA-03: no character can be talked to / gifted / dated / event-started before first_chapter", leaks == 0, leaks + " leaks");
            Report.Check("QA-03: pre-join refusals use reason not_met + a Korean message; pick list (AvailableChars) honours first_chapter; met characters unaffected", wrongRefusals == 0 && listLeaks == 0, wrongRefusals + " wrong refusals, " + listLeaks + " list errors");
            Report.Check("QA-03: refused pre-join action leaves state untouched", PreJoinRefusalIsClean(w, b));
            // QA-14: staff ids / join chapters come from characters.csv (no duplicate table in code)
            Report.Check("QA-14: BalanceTable.Characters mirrors RomanceCatalog characters (single source characters.csv)", CatalogMatchesBalance(w, b));
        }

        private static bool PreJoinRefusalIsClean(World w, BalanceTable b)
        {
            GameState g = Content.Fresh(w, b, 1, "neutral"); g.Day = 3; g.DayInChapter = 3; g.Phase = DayPhase.Night; g.ActionsLeft = 2; g.Gold = 5000;
            RomanceService r = new RomanceService(g, b, w.Cat, new Random(1).NextDouble);
            float gold = g.Gold, fat = g.Fatigue; int lc = g.Chars["haru"].LastContactDay;
            r.Talk("haru"); r.Gift("haru", "small_plant"); r.Date("haru", "beach"); r.CompleteEventResult("haru", 1);
            return g.ActionsLeft == 2 && g.Gold == gold && g.Fatigue == fat && g.Chars["haru"].Aff == 0f && g.Chars["haru"].LastContactDay == lc;
        }

        private static bool CatalogMatchesBalance(World w, BalanceTable b)
        {
            foreach (CharacterRow c in w.Cat.Characters)
                if (b.FirstChapterOf(c.Id) != c.FirstChapter || b.StaffJoinChapterOf(c.Id) != c.StaffJoinChapter) return false;
            return b.Characters.Count == w.Cat.Characters.Count;
        }
    }
}
