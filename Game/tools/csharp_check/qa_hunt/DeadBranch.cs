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
    /// "Flags checked in conditions are settable BEFORE they are read": assigns every node the earliest/latest chapter in which the host can start it
    /// (chapter nodes, scene_rules, event schedule, jump closure) and reports conditions whose flag can only be set AFTER the reader can no longer run.
    /// </summary>
    public static class DeadBranch
    {
        private sealed class Win { public int Min = 99, Max = 0; }

        public static void Run(World w)
        {
            Report.Header("DEAD-BRANCH SCAN: conditions that read a flag before anyone can have set it");
            BalanceTable b = w.Balance(Difficulty.Normal);
            Dictionary<string, Win> win = new Dictionary<string, Win>();
            SceneRuleTable rules = string.IsNullOrEmpty(w.Cat.SceneRulesText) ? new SceneRuleTable() : SceneRuleTable.Parse(w.Cat.SceneRulesText);
            Action<string, int, int> put = delegate (string n, int lo, int hi)
            {
                if (!w.Proj.Contains(n)) return;
                Win x; if (!win.TryGetValue(n, out x)) { x = new Win(); win[n] = x; }
                x.Min = Math.Min(x.Min, lo); x.Max = Math.Max(x.Max, hi);
            };
            for (int c = 1; c <= 8; c++) { put(ChapterFlow.IntroNode(c), c, c); put(ChapterFlow.OutroNode(c), c + 1, Math.Min(8, c + 1)); }
            put("Ch8_Outro", 8, 8);
            foreach (SceneRule r in rules.Rules) if (r.AutoPlay) put(r.Node, r.Chapter, Math.Min(8, r.Chapter + 1));
            foreach (string id in CharIds.All)
            {
                for (int i = 1; i <= NightRomanceActions.MaxTalkIndex; i++) put(NightRomanceActions.TalkNodeName(id, i), 1, 8);
                foreach (Preference pr in new Preference[] { Preference.Like, Preference.Neutral, Preference.Dislike }) put(NightRomanceActions.GiftNodeName(id, pr), 1, 8);
                put(NightRomanceActions.DateGenericNodeName(id), 1, 8);
                foreach (string spot in w.Cat.Spots.Keys) { put(NightRomanceActions.DateNodeName(id, spot), w.Cat.Spots[spot].UnlockChapter, 8); }
                RomanceService rs = new RomanceService(GameState.NewGame(b, Difficulty.Normal, "x"), b, w.Cat, new Random(1).NextDouble);
                for (int st = 1; st <= 5; st++) put(NightRomanceActions.EventNodeName(id, st), Math.Max(1, rs.EventChapterMin(id, st)), 8);
                foreach (EndingKind k in new EndingKind[] { EndingKind.Normal, EndingKind.Good, EndingKind.True }) put(EndingNodes.Personal(id, k), 8, 8);
                put(ConsentRetry.ConsentNode(id), 7, 8); put(RouteFlow.EpilogueNode(id), 8, 8);
            }
            put(RouteFlow.HaremTable, 7, 8); put(EndingNodes.Harem2, 8, 8); put(EndingNodes.Harem3, 8, 8); put(EndingNodes.Community, 8, 8); put("Howl_E4_Retry", 7, 8);
            // propagate through jumps
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (DialogueNode n in w.Proj.AllNodes)
                {
                    Win mine; if (!win.TryGetValue(n.Title, out mine)) continue;
                    StatementWalker.Walk(n.Body, delegate (Statement st)
                    {
                        JumpStatement j = st as JumpStatement;
                        if (j == null || !w.Proj.Contains(j.Target)) return;
                        Win t; if (!win.TryGetValue(j.Target, out t)) { t = new Win(); win[j.Target] = t; }
                        if (mine.Min < t.Min) { t.Min = mine.Min; changed = true; }
                        if (mine.Max > t.Max) { t.Max = mine.Max; changed = true; }
                    }, null);
                }
            }
            // setters per flag
            Dictionary<string, int> earliestSet = new Dictionary<string, int>();
            foreach (DialogueNode n in w.Proj.AllNodes)
            {
                Win m; if (!win.TryGetValue(n.Title, out m)) continue;
                StatementWalker.Walk(n.Body, delegate (Statement st)
                {
                    string f = null;
                    CommandStatement cs = st as CommandStatement;
                    if (cs != null && cs.Name == "flag" && cs.Args.Count > 0) f = cs.Args[0].Source.TrimStart('$');
                    SetStatement ss = st as SetStatement;
                    if (ss != null) f = ss.Variable.TrimStart('$');
                    if (f == null) return;
                    int e; if (!earliestSet.TryGetValue(f, out e) || m.Min < e) earliestSet[f] = m.Min;
                }, null);
            }
            Regex rx = new Regex("flag\\(\"(\\w+)\"\\)|\\$(\\w+)");
            int dead = 0; HashSet<string> seen = new HashSet<string>();
            foreach (DialogueNode n in w.Proj.AllNodes)
            {
                Win m; if (!win.TryGetValue(n.Title, out m)) continue;
                Action<string, int> check = delegate (string cond, int line)
                {
                    string pos = Regex.Replace(cond, "not\\s+flag\\(\"\\w+\"\\)", "");
                    foreach (Match mm in rx.Matches(pos))
                    {
                        string f = mm.Groups[1].Success ? mm.Groups[1].Value : mm.Groups[2].Value;
                        if (f.StartsWith("aff_") || f.StartsWith("trust_") || f == "gold" || f == "reputation" || f == "fatigue" || f == "day" || f == "chapter" || f.StartsWith("sys_")) continue;
                        if (f == "gift_name" || f == "spot_name" || f == "spot_id" || f == "event_step" || f == "gift_pref") continue;
                        int e;
                        if (!earliestSet.TryGetValue(f, out e)) continue; // never-set flags are reported by the flags mode
                        if (e > m.Max)
                        {
                            string key = n.Title + "|" + f;
                            if (seen.Add(key)) { dead++; Report.Info("  DEAD READ: " + n.Title + ":" + line + " reads '" + f + "' (node runs ch" + m.Min + "-" + m.Max + ") but it is first settable in ch" + e + "  cond: " + cond.Trim()); }
                        }
                    }
                };
                StatementWalker.Walk(n.Body, delegate (Statement st)
                {
                    IfStatement ifs = st as IfStatement;
                    if (ifs != null) foreach (IfBranch br in ifs.Branches) if (br.Condition != null) check(br.Condition.Source, br.LineNumber);
                }, delegate (OptionItem it) { if (it.Condition != null) check(it.Condition.Source, it.LineNumber); });
            }
            Report.Check("no condition reads a flag that can only be set in a later chapter than the reader can run (dead branch)", dead == 0, dead + " dead reads");
        }
    }
}
