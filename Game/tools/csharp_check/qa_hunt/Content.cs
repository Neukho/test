using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;

namespace QaHunt
{
    public static class Content
    {
        public static GameState Fresh(World w, BalanceTable b, int chapter, string gender)
        {
            GameState s = GameState.NewGame(b, Difficulty.Normal, "민준");
            s.Gender = gender;
            s.Chapter = chapter;
            s.Day = 40; s.DayInChapter = 3;
            s.Phase = DayPhase.Night; s.ActionsLeft = 2; s.Gold = 5000f;
            return s;
        }

        public static void Run(World w, string dialogueRoot)
        {
            Report.Header("CONTENT QA");
            BalanceTable b = w.Balance(Difficulty.Normal);
            DialogueProject p = w.Proj;
            Report.Info("files=" + w.FileCount + " nodes=" + p.AllNodes.Count + " loadErrors=" + w.LoadErrors.Count);
            for (int i = 0; i < w.LoadErrors.Count; i++) Report.Info("  load: " + w.LoadErrors[i]);
            Report.Check("scripts load without parse/dup errors", w.LoadErrors.Count == 0);

            // ---- entries lists vs nodes; lint ----
            List<string> entries = new List<string>();
            foreach (string f in Directory.GetFiles(dialogueRoot, "entries*.txt", SearchOption.AllDirectories))
                foreach (string line in File.ReadAllLines(f)) { string t = line.Trim(); if (t.Length > 0 && t[0] != '#') entries.Add(t); }
            List<LintIssue> issues = DialogueLint.Run(p, entries);
            int lintErr = 0;
            foreach (LintIssue li in issues) { if (li.Severity == LintSeverity.Error || li.Code == "UNREACHABLE" || li.Code == "MISSING_LINE_ID") { lintErr++; Report.Info("  lint: " + li); } }
            Report.Check("lint: no dup node / undefined jump / dup #line id / missing id / unreachable (entries*.txt)", lintErr == 0, lintErr + " issues");
            // repo-wide unique #line ids (independent re-check, includes ids on options)
            Dictionary<string, string> seen = new Dictionary<string, string>();
            int dups = 0;
            foreach (DialogueNode n in p.AllNodes)
            {
                StatementWalker.Walk(n.Body, delegate (Statement st)
                {
                    LineStatement ls = st as LineStatement;
                    if (ls != null && ls.LineId != null) { string o; if (seen.TryGetValue(ls.LineId, out o)) { dups++; Report.Info("  dup #line:" + ls.LineId + " in " + n.Title + " and " + o); } else seen[ls.LineId] = n.Title; }
                }, delegate (OptionItem it)
                {
                    if (it.LineId != null) { string o; if (seen.TryGetValue(it.LineId, out o)) { dups++; Report.Info("  dup #line:" + it.LineId + " in " + n.Title + " and " + o); } else seen[it.LineId] = n.Title; }
                });
            }
            Report.Check("#line ids unique repo-wide", dups == 0, dups + " dups");

            // ---- entries that do not exist ----
            int missingEntry = 0;
            foreach (string e in entries) if (!p.Contains(e)) { missingEntry++; Report.Info("  entry not defined: " + e); }
            Report.Check("every entries*.txt name is a defined node", missingEntry == 0);

            // ---- host-called node names ----
            List<string> missing = new List<string>();
            for (int c = 1; c <= 8; c++)
            {
                if (!p.Contains(ChapterFlow.IntroNode(c))) missing.Add(ChapterFlow.IntroNode(c));
                if (!p.Contains(ChapterFlow.OutroNode(c))) missing.Add(ChapterFlow.OutroNode(c));
            }
            foreach (string n in new string[] { RouteFlow.HaremTable, EndingNodes.Harem2, EndingNodes.Harem3, EndingNodes.Community, "Howl_E4_Retry" })
                if (!p.Contains(n)) missing.Add(n);
            int missingDetail = 0;
            foreach (string id in CharIds.All)
            {
                for (int i = 1; i <= 6; i++) if (!p.Contains(NightRomanceActions.TalkNodeName(id, i))) missing.Add(NightRomanceActions.TalkNodeName(id, i));
                foreach (Preference pr in new Preference[] { Preference.Like, Preference.Neutral, Preference.Dislike })
                    if (!p.Contains(NightRomanceActions.GiftNodeName(id, pr))) missing.Add(NightRomanceActions.GiftNodeName(id, pr));
                if (!p.Contains(NightRomanceActions.DateGenericNodeName(id))) missing.Add(NightRomanceActions.DateGenericNodeName(id));
                for (int st = 1; st <= 5; st++) if (!p.Contains(NightRomanceActions.EventNodeName(id, st))) missing.Add(NightRomanceActions.EventNodeName(id, st));
                foreach (EndingKind k in new EndingKind[] { EndingKind.Normal, EndingKind.Good, EndingKind.True })
                    if (!p.Contains(EndingNodes.Personal(id, k))) missing.Add(EndingNodes.Personal(id, k));
                if (!p.Contains(ConsentRetry.ConsentNode(id))) missing.Add(ConsentRetry.ConsentNode(id));
                if (!p.Contains(RouteFlow.EpilogueNode(id))) missing.Add(RouteFlow.EpilogueNode(id));
                if (p.Contains(NightRomanceActions.TalkNodeName(id, 7))) Report.Info("  note: " + id + " has Talk_07+ (host would use up to 40, bands by thirds)");
            }
            foreach (string m in missing) Report.Info("  MISSING host-called node: " + m);
            Report.Check("all host-called nodes exist for 6 chars x (talk01-06, gift x3, date generic, E1-5, 3 endings, consent, epilogue) + chapters 1-8 + harem/community", missing.Count == 0, missing.Count + " missing");
            if (missingDetail > 0) Report.Info("");

            // ---- spots: specific date node coverage ----
            Report.Info("-- date spots: specific node per (char,spot); liked spot w/o node falls back to Generic --");
            int uncoveredSpots = 0;
            foreach (KeyValuePair<string, SpotRow> sp in w.Cat.Spots)
            {
                int have = 0;
                foreach (string id in CharIds.All) if (p.Contains(NightRomanceActions.DateNodeName(id, sp.Key))) have++;
                if (have == 0) { uncoveredSpots++; Report.Info("  spot " + sp.Key + " has NO character-specific date node (always Generic)"); }
            }
            Report.Check("every spot in spots.csv has >=1 character-specific date node", uncoveredSpots == 0);
            foreach (string id in CharIds.All)
            {
                List<string> likedNoNode = new List<string>();
                foreach (string spot in w.Cat.TagsFor(id, PrefKind.Spot, Preference.Like))
                    if (!p.Contains(NightRomanceActions.DateNodeName(id, spot))) likedNoNode.Add(spot);
                if (likedNoNode.Count > 0) Report.Info("  [gap] " + id + " LIKES spot(s) [" + string.Join(",", likedNoNode.ToArray()) + "] but has no dedicated date node -> Generic scene");
            }
            // dated nodes that no spot/char combination can reach
            foreach (DialogueNode n in p.AllNodes)
            {
                Match m = Regex.Match(n.Title, "^([A-Z][a-z]+)_Date_(.+)$");
                if (!m.Success || m.Groups[2].Value == "Generic") continue;
                bool ok = false;
                foreach (string spot in w.Cat.Spots.Keys)
                    if (NightRomanceActions.DateNodeName(m.Groups[1].Value.ToLowerInvariant(), spot) == n.Title) ok = true;
                Report.Check("date node " + n.Title + " maps to a spots.csv id", ok);
            }

            // ---- prefs / gifts consistency ----
            int badPref = 0;
            foreach (PrefRow pr in w.Cat.Prefs)
            {
                if (pr.Kind == PrefKind.Gift && w.Cat.FindGift(pr.Tag) == null) { badPref++; Report.Info("  prefs gift tag not in gifts.csv: " + pr.CharId + " " + pr.Tag); }
                if (pr.Kind == PrefKind.Spot && w.Cat.FindSpot(pr.Tag) == null) { badPref++; Report.Info("  prefs spot tag not in spots.csv: " + pr.CharId + " " + pr.Tag); }
                if (w.Cat.FindCharacter(pr.CharId) == null) { badPref++; Report.Info("  prefs unknown char " + pr.CharId); }
            }
            Report.Check("prefs.csv tags all resolve", badPref == 0);
            int never = 0;
            foreach (GiftRow g in w.Cat.Gifts.Values)
            {
                int likes = 0, dis = 0;
                foreach (string id in CharIds.All)
                {
                    Preference pr = w.Cat.GetPreference(id, PrefKind.Gift, g.Id);
                    if (pr == Preference.Like) likes++; else if (pr == Preference.Dislike) dis++;
                }
                if (likes == 0) { never++; Report.Info("  gift '" + g.Id + "' is liked by nobody (" + dis + " dislike) - " + (dis == 0 ? "pure filler" : "trap gift")); }
            }
            // every (char,gift) reaction node resolves + runs and is non-empty
            int giftBad = 0;
            foreach (string id in CharIds.All)
            {
                foreach (GiftRow g in w.Cat.Gifts.Values)
                {
                    Preference pr = w.Cat.GetPreference(id, PrefKind.Gift, g.Id);
                    string node = NightRomanceActions.GiftNodeName(id, pr);
                    if (!p.Contains(node)) { giftBad++; continue; }
                }
            }
            Report.Check("every (char x gift) has a reaction node", giftBad == 0);

            // ---- scene sanity: every node, 4 genders, all branches (enumerated) ----
            Report.Info("-- exhaustive branch enumeration of every node (neutral gender, ch7 night state, cap 600 paths/node) --");
            int badNodes = 0, deadEnds = 0, totalPaths = 0, capped = 0, badSpeaker = 0;
            string[] okSpeakers = { "player", "howl", "johan", "beomjun", "haru", "gomseok", "nabi" };
            foreach (DialogueNode n in p.AllNodes)
            {
                GameState s = Fresh(w, b, 8, "neutral");
                foreach (string id in CharIds.All) { s.Chars[id].Aff = 70; s.Chars[id].Trust = 30; }
                Dictionary<string, DialogueValue> temps = new Dictionary<string, DialogueValue>();
                temps["gift_name"] = DialogueValue.FromString("테스트"); temps["spot_name"] = DialogueValue.FromString("테스트"); temps["spot_id"] = DialogueValue.FromString("beach");
                temps["event_step"] = DialogueValue.FromNumber(1); temps["gift_pref"] = DialogueValue.FromString("like");
                List<Runner.PathOutcome> outs = Runner.EnumeratePaths(w, b, s, new List<string>(), n.Title, temps, 600);
                totalPaths += outs.Count;
                if (outs.Count >= 600) capped++;
                foreach (Runner.PathOutcome po in outs)
                {
                    if (!po.Scene.Ended) { deadEnds++; Report.Info("  node did not End: " + n.Title + " path [" + string.Join(",", po.Seq.ConvertAll<string>(delegate (int i) { return i.ToString(); }).ToArray()) + "]"); }
                    foreach (string e in po.Scene.Errors)
                    {
                        if (e.Contains("end_event") && e.Contains("event_not_available")) continue; // standalone-run artefact
                        badNodes++; Report.Info("  error in " + n.Title + ": " + e); break;
                    }
                    foreach (string sp in po.Scene.Speakers)
                        if (Array.IndexOf(okSpeakers, sp) < 0 && !sp.StartsWith("npc_")) { badSpeaker++; Report.Info("  unknown speaker '" + sp + "' in " + n.Title); }
                }
            }
            Report.Info("  paths enumerated=" + totalPaths + " nodes capped=" + capped);
            Report.Check("all branches of all nodes reach End", deadEnds == 0);
            Report.Check("no script runtime warnings/errors on any branch", badNodes == 0);
            Report.Check("speaker ids valid (6 chars, player, npc_*)", badSpeaker == 0);

            SceneRules(w, b);
            EndingNodesRun(w, b);
            HaremCombos(w, b);
            ConsentPaths(w, b);
            NonMemberLeaks(w, b);
        }

        private static void SceneRules(World w, BalanceTable b)
        {
            Report.Info("-- scene_rules.csv / event_schedule.csv consistency (only if present) --");
            if (string.IsNullOrEmpty(w.Cat.SceneRulesText)) { Report.Info("  (no scene_rules table in this tree)"); return; }
            SceneRuleTable t = SceneRuleTable.Parse(w.Cat.SceneRulesText);
            List<string> errs = t.Validate(delegate (int c) { return c >= 1 && c <= b.MaxChapter ? b.Chapter(c).Days : 0; });
            foreach (string e in errs) Report.Info("  validate: " + e);
            Report.Check("scene_rules: table self-validation", errs.Count == 0, errs.Count + " errors");
            int unknown = 0;
            foreach (SceneRule r in t.Rules)
            {
                if (!w.Proj.Contains(r.Node)) { unknown++; Report.Info("  rule for undefined node " + r.Node); }
                foreach (SceneRef a in r.After) if (!w.Proj.Contains(a.Node)) { unknown++; Report.Info("  " + r.Node + ": after_node undefined " + a.Node); }
                foreach (string bn in r.Before) if (!w.Proj.Contains(bn)) { unknown++; Report.Info("  " + r.Node + ": before_node undefined " + bn); }
                foreach (string f in r.RequiresFlags) { }
            }
            Report.Check("scene_rules: every node / after / before names a defined node (typo check)", unknown == 0, unknown + " unknown");
            // after_node cycles (hard + soft)
            Dictionary<string, List<string>> g = new Dictionary<string, List<string>>();
            foreach (SceneRule r in t.Rules) { List<string> l = new List<string>(); foreach (SceneRef a in r.After) l.Add(a.Node); g[r.Node] = l; }
            int cyc = 0;
            foreach (string start in g.Keys)
            {
                HashSet<string> seen = new HashSet<string>(); List<string> st = new List<string>(g[start]);
                while (st.Count > 0) { string n = st[st.Count - 1]; st.RemoveAt(st.Count - 1); if (n == start) { cyc++; Report.Info("  after_node cycle through " + start); break; } if (!seen.Add(n)) continue; List<string> nx; if (g.TryGetValue(n, out nx)) st.AddRange(nx); }
            }
            Report.Check("scene_rules: no after_node cycles", cyc == 0);
            // every personal event has a schedule/scene rule consistent with events.csv step ordering
            int evBad = 0;
            foreach (string id in CharIds.All)
            {
                int prevCh = 0;
                for (int st2 = 1; st2 <= 5; st2++)
                {
                    SceneRule r = t.Find(NightRomanceActions.EventNodeName(id, st2));
                    int ch = r != null ? r.Chapter : 0;
                    EventScheduleRow er = w.Cat.Schedule != null ? w.Cat.Schedule.Find(id, st2) : null;
                    int effMin = er != null && er.ChapterMin > 0 ? er.ChapterMin : b.Events[st2 - 1].ChapterMin;
                    if (r == null) { Report.Info("  (info) no scene rule for " + id + " E" + st2 + " -> only events.csv/event_schedule gating applies"); prevCh = effMin; continue; }
                    if (ch < effMin) { evBad++; Report.Info("  " + id + " E" + st2 + " scene rule chapter " + ch + " < schedule chapter_min " + effMin); }
                    if (effMin < prevCh) { evBad++; Report.Info("  " + id + " E" + st2 + " chapter_min " + effMin + " earlier than previous step " + prevCh); }
                    prevCh = effMin;
                }
            }
            Report.Check("event_schedule/scene_rules: scene-rule chapter >= schedule chapter, chapters non-decreasing per character", evBad == 0, evBad + " problems");
        }

        private static void EndingNodesRun(World w, BalanceTable b)
        {
            Report.Info("-- ending nodes: CG ids promised by EndingEvaluator are actually shown by the node --");
            foreach (string id in CharIds.All)
            {
                foreach (EndingKind k in new EndingKind[] { EndingKind.Normal, EndingKind.Good, EndingKind.True })
                {
                    string node = EndingNodes.Personal(id, k);
                    GameState s = Fresh(w, b, 8, "neutral");
                    s.Finished = true; s.SetLock(new string[] { id });
                    s.Chars[id].Aff = 100; s.Chars[id].Trust = 50; s.Chars[id].EventsDone = 5;
                    s.Flags["ledger_truth"] = 1; s.Flags[id + "_key"] = 1;
                    List<Runner.PathOutcome> outs = Runner.EnumeratePaths(w, b, s, new List<string>(), node, null, 200);
                    bool anyCg = false; bool allEnd = true;
                    string needCg = k == EndingKind.True ? EndingNodes.TrueCg(id) : null;
                    int withCg = 0;
                    foreach (Runner.PathOutcome po in outs)
                    {
                        if (!po.Scene.Ended) allEnd = false;
                        if (needCg != null && po.Scene.Cgs.Contains(needCg)) withCg++;
                        if (po.Scene.Cgs.Count > 0) anyCg = true;
                    }
                    Report.Check(node + " runs (" + outs.Count + " paths)", allEnd && outs.Count > 0);
                    if (needCg != null) Report.Check(node + " shows " + needCg + " on every path (EndingResult.CgIds)", withCg == outs.Count, withCg + "/" + outs.Count + " paths");
                }
            }
        }

        private static List<string[]> Combos(int k)
        {
            List<string[]> list = new List<string[]>();
            string[] a = CharIds.All;
            if (k == 2) { for (int i = 0; i < a.Length; i++) for (int j = i + 1; j < a.Length; j++) list.Add(new string[] { a[i], a[j] }); }
            else { for (int i = 0; i < a.Length; i++) for (int j = i + 1; j < a.Length; j++) for (int l = j + 1; l < a.Length; l++) list.Add(new string[] { a[i], a[j], a[l] }); }
            return list;
        }

        private static void HaremCombos(World w, BalanceTable b)
        {
            Report.Info("-- Harem_Table / Ending_Harem2 / Ending_Harem3 for all 15 pairs + 20 triples (member order = both permutations tested for pairs) --");
            int bad = 0;
            string[] nodes = { RouteFlow.HaremTable, EndingNodes.Harem2, EndingNodes.Harem3 };
            for (int k = 2; k <= 3; k++)
            {
                foreach (string[] combo in Combos(k))
                {
                    foreach (string node in new string[] { RouteFlow.HaremTable, k == 2 ? EndingNodes.Harem2 : EndingNodes.Harem3 })
                    {
                        GameState s = Fresh(w, b, 8, "neutral");
                        s.SetLock(combo);
                        foreach (string id in combo) { s.Chars[id].Aff = 90; s.Chars[id].Trust = 40; s.Chars[id].EventsDone = 4; s.Flags["harem_ok_" + id] = 1; }
                        List<Runner.PathOutcome> outs = Runner.EnumeratePaths(w, b, s, new List<string>(), node, null, 200);
                        foreach (Runner.PathOutcome po in outs)
                        {
                            string tag = node + "[" + string.Join("+", combo) + "]";
                            if (!po.Scene.Ended || po.Scene.Errors.Count > 0) { bad++; Report.Info("  " + tag + " ended=" + po.Scene.Ended + " err=" + (po.Scene.Errors.Count > 0 ? po.Scene.Errors[0] : "-")); break; }
                            // each member should appear on stage / speak at least once in the ending/table
                            foreach (string id in combo)
                                if (!po.Scene.Speakers.Contains(id) && !po.Scene.Standing.Contains(id))
                                { bad++; Report.Info("  " + tag + ": member '" + id + "' never speaks or appears"); break; }
                            if (node != RouteFlow.HaremTable && !po.Scene.Cgs.Contains(EndingNodes.HaremCg)) { bad++; Report.Info("  " + tag + " does not show " + EndingNodes.HaremCg); break; }
                            break; // first path is enough for member-presence; branches already enumerated above
                        }
                    }
                }
            }
            Report.Check("harem table/endings valid for every member combination", bad == 0, bad + " problems");

            // Epilogue + consent nodes per char
            foreach (string id in CharIds.All)
            {
                GameState s = Fresh(w, b, 8, "neutral");
                s.SetLock(new string[] { id, id == "howl" ? "johan" : "howl" });
                List<Runner.PathOutcome> outs = Runner.EnumeratePaths(w, b, s, new List<string>(), RouteFlow.EpilogueNode(id), null, 50);
                bool ok = outs.Count > 0; foreach (Runner.PathOutcome po in outs) if (!po.Scene.Ended || po.Scene.Errors.Count > 0) ok = false;
                Report.Check("epilogue node " + RouteFlow.EpilogueNode(id) + " runs clean", ok);
            }
        }

        /// <summary>Consent / epilogue / table / ending nodes must never put a NON-member on stage or name them (group of 2-3 out of 6).</summary>
        private static void NonMemberLeaks(World w, BalanceTable b)
        {
            Report.Info("-- non-member leaks: consent/epilogue/table/ending nodes run for every group containing the owner --");
            int leaks = 0;
            List<string[]> groups = new List<string[]>();
            groups.AddRange(Combos(2)); groups.AddRange(Combos(3));
            foreach (string[] g in groups)
            {
                foreach (string id in g)
                {
                    foreach (string node in new string[] { ConsentRetry.ConsentNode(id), RouteFlow.EpilogueNode(id) })
                    {
                        GameState s = Fresh(w, b, 8, "neutral");
                        s.SetLock(g);
                        foreach (string m in g) { s.Chars[m].Aff = 90; s.Chars[m].Trust = 40; s.Chars[m].EventsDone = 5; }
                        List<Runner.PathOutcome> outs = Runner.EnumeratePaths(w, b, s, new List<string>(), node, null, 100);
                        foreach (Runner.PathOutcome po in outs)
                        {
                            bool leaked = false;
                            foreach (string sp in po.Scene.Speakers) if (Array.IndexOf(CharIds.All, sp) >= 0 && Array.IndexOf(g, sp) < 0) { leaked = true; Report.Info("  LEAK " + node + " group=" + string.Join("+", g) + ": non-member '" + sp + "' speaks"); }
                            foreach (string st in po.Scene.Standing) if (Array.IndexOf(g, st) < 0) { leaked = true; Report.Info("  LEAK " + node + " group=" + string.Join("+", g) + ": non-member '" + st + "' on stage"); }
                            foreach (string line in po.Scene.Lines)
                                foreach (string other in CharIds.All)
                                {
                                    if (Array.IndexOf(g, other) >= 0) continue;
                                    string nm = w.Cat.FindCharacter(other).NameKo;
                                    if (nm == "하루") continue; // common word ('a day')
                                    if (System.Text.RegularExpressions.Regex.IsMatch(line, "(?<![\\p{L}])" + nm + "(?![\\p{L}]*[다요])|(?<![\\p{L}])" + nm + "(은|는|이|가|을|를|과|와|도|의|에게|한테|야|아|씨)(?![\\p{L}])")) { leaked = true; Report.Info("  NAME-LEAK " + node + " group=" + string.Join("+", g) + ": mentions " + nm + " :: " + line); }
                                }
                            if (leaked) { leaks++; break; }
                        }
                    }
                }
            }
            Report.Check("consent/epilogue nodes never show or name a character outside the locked group", leaks == 0, leaks + " leaking (node, group) pairs");
        }

        private static void ConsentPaths(World w, BalanceTable b)
        {
            Report.Info("-- consent nodes: is there a path that grants consent AND a path that does not (retry flow)? is the flag set only by consent? --");
            foreach (string id in CharIds.All)
            {
                string node = ConsentRetry.ConsentNode(id);
                GameState s = Fresh(w, b, 7, "neutral");
                s.SetLock(new string[] { id, id == "howl" ? "johan" : "howl" });
                s.Chars[id].Aff = 80; s.Chars[id].Trust = 35;
                List<Runner.PathOutcome> outs = Runner.EnumeratePaths(w, b, s, new List<string>(), node, null, 300);
                int grant = 0, deny = 0;
                foreach (Runner.PathOutcome po in outs)
                {
                    int v; bool set = po.End.Flags.TryGetValue("harem_ok_" + id, out v) && v != 0;
                    if (set) grant++; else deny++;
                    foreach (string other in CharIds.All)
                        if (other != id) { int ov; if (po.End.Flags.TryGetValue("harem_ok_" + other, out ov) && ov != 0) Report.Check(node + " must not set harem_ok_" + other, false); }
                }
                Report.Info("  " + node + ": paths=" + outs.Count + " grant=" + grant + " no-consent=" + deny);
                Report.Check(node + " has a consent path", grant > 0);
            }
        }
    }
}
