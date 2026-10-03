using System;
using System.Collections.Generic;
using System.Text;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;

namespace QaHunt
{
    /// <summary>
    /// Engine-exact (not regex) flag reachability: runs every host-startable node under every choice path and iterates to a fixpoint.
    /// Answers: which flags can a real player EVER set, which keys / endings are therefore impossible?
    /// </summary>
    public static class FlagReach
    {
        public static List<string> HostNodes(World w)
        {
            List<string> list = new List<string>();
            for (int c = 1; c <= 8; c++) { list.Add(ChapterFlow.IntroNode(c)); list.Add(ChapterFlow.OutroNode(c)); }
            list.Add(RouteFlow.HaremTable); list.Add(EndingNodes.Harem2); list.Add(EndingNodes.Harem3); list.Add(EndingNodes.Community); list.Add("Howl_E4_Retry");
            foreach (string id in CharIds.All)
            {
                for (int i = 1; i <= NightRomanceActions.MaxTalkIndex; i++) list.Add(NightRomanceActions.TalkNodeName(id, i));
                foreach (Preference pr in new Preference[] { Preference.Like, Preference.Neutral, Preference.Dislike }) list.Add(NightRomanceActions.GiftNodeName(id, pr));
                list.Add(NightRomanceActions.DateGenericNodeName(id));
                foreach (string spot in w.Cat.Spots.Keys) { list.Add(NightRomanceActions.DateNodeName(id, spot)); list.Add(NightRomanceActions.CharTitle(id) + "_Date_" + spot); }
                for (int st = 1; st <= 5; st++) list.Add(NightRomanceActions.EventNodeName(id, st));
                foreach (EndingKind k in new EndingKind[] { EndingKind.Normal, EndingKind.Good, EndingKind.True }) list.Add(EndingNodes.Personal(id, k));
                list.Add(ConsentRetry.ConsentNode(id)); list.Add(RouteFlow.EpilogueNode(id));
            }
            // host-auto-played common scenes (scene_rules.csv, SceneGate) - present only once the scheduler exists
            if (!string.IsNullOrEmpty(w.Cat.SceneRulesText))
                foreach (SceneRule r in SceneRuleTable.Parse(w.Cat.SceneRulesText).Rules) if (r.AutoPlay) list.Add(r.Node);
            List<string> ok = new List<string>();
            foreach (string n in list) if (w.Proj.Contains(n) && !ok.Contains(n)) ok.Add(n);
            return ok;
        }

        private static GameState Base(World w, BalanceTable b, Dictionary<string, int> flags, string node, string lockChar)
        {
            GameState s = Content.Fresh(w, b, 8, "neutral");
            foreach (string id in CharIds.All) { s.Chars[id].Aff = 100; s.Chars[id].Trust = 50; s.Chars[id].EventsDone = 5; }
            foreach (KeyValuePair<string, int> p in flags) s.Flags[p.Key] = p.Value;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(node, "^([A-Za-z]+)_E([1-5])$");
            if (m.Success) s.Chars[m.Groups[1].Value.ToLowerInvariant()].EventsDone = int.Parse(m.Groups[2].Value) - 1;
            if (lockChar != null) s.SetLock(new string[] { lockChar });
            return s;
        }

        /// <summary>Flags (with node) newly set by any path of `node` given the flag baseline.</summary>
        public static HashSet<string> NodeSets(World w, BalanceTable b, Dictionary<string, int> flags, string node, string lockChar)
        {
            HashSet<string> got = new HashSet<string>();
            GameState s = Base(w, b, flags, node, lockChar);
            Dictionary<string, DialogueValue> temps = new Dictionary<string, DialogueValue>();
            temps["gift_name"] = DialogueValue.FromString("x"); temps["spot_name"] = DialogueValue.FromString("x"); temps["spot_id"] = DialogueValue.FromString("beach");
            temps["event_step"] = DialogueValue.FromNumber(1); temps["gift_pref"] = DialogueValue.FromString("like");
            List<Runner.PathOutcome> outs = Runner.EnumeratePaths(w, b, s, new List<string>(), node, temps, 400);
            foreach (Runner.PathOutcome po in outs)
                foreach (KeyValuePair<string, int> p in po.End.Flags)
                {
                    int old;
                    bool had = s.Flags.TryGetValue(p.Key, out old);
                    if (p.Value != 0 && (!had || old == 0)) got.Add(p.Key);
                }
            return got;
        }

        public static Dictionary<string, string> Fixpoint(World w, BalanceTable b, List<string> host, out Dictionary<string, int> flags)
        {
            flags = new Dictionary<string, int>();
            Dictionary<string, string> setter = new Dictionary<string, string>();
            bool changed = true; int iter = 0;
            while (changed && iter++ < 12)
            {
                changed = false;
                foreach (string n in host)
                {
                    string[] locks = { null, "howl" };
                    foreach (string lk in locks)
                        foreach (bool useAcc in new bool[] { true, false })
                        {
                            Dictionary<string, int> bf = useAcc ? flags : new Dictionary<string, int>();
                            foreach (string f in NodeSets(w, b, bf, n, lk))
                                if (!flags.ContainsKey(f)) { flags[f] = 1; setter[f] = n; changed = true; }
                        }
                }
            }
            return setter;
        }

        public static void Run(World w)
        {
            Report.Header("FLAG REACHABILITY (engine-exact fixpoint over host-startable nodes)");
            BalanceTable b = w.Balance(Difficulty.Normal);
            List<string> host = HostNodes(w);
            Dictionary<string, int> flags;
            Dictionary<string, string> setter = Fixpoint(w, b, host, out flags);
            Report.Info("host-startable nodes: " + host.Count + "  flags a real player can ever set: " + flags.Count);
            string[] keys = { "ledger_truth", "drawer_key_clue", "howl_key_photo", "howl_key", "johan_key", "beomjun_key", "haru_key", "gomseok_key", "nabi_key",
                "harem_ok_howl", "harem_ok_johan", "harem_ok_beomjun", "harem_ok_haru", "harem_ok_gomseok", "harem_ok_nabi" };
            foreach (string k in keys)
                Report.Check("flag '" + k + "' is settable by some host-startable path", flags.ContainsKey(k), flags.ContainsKey(k) ? null : "NEVER settable");
            foreach (string k in keys) if (flags.ContainsKey(k)) Report.Info("  " + k + " set first by " + setter[k]);

            // flags that are read by some script condition but unreachable
            HashSet<string> readFlags = new HashSet<string>();
            System.Text.RegularExpressions.Regex rx = new System.Text.RegularExpressions.Regex("flag\\(\"(\\w+)\"\\)|\\$(\\w+)");
            foreach (DialogueNode n in w.Proj.AllNodes)
                StatementWalker.Walk(n.Body, delegate (Statement st)
                {
                    IfStatement ifs = st as IfStatement;
                    if (ifs != null) foreach (IfBranch br in ifs.Branches) if (br.Condition != null) foreach (System.Text.RegularExpressions.Match m in rx.Matches(br.Condition.Source)) readFlags.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                }, delegate (OptionItem it)
                {
                    if (it.Condition != null) foreach (System.Text.RegularExpressions.Match m in rx.Matches(it.Condition.Source)) readFlags.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                });
            List<string> dead = new List<string>();
            foreach (string f in readFlags)
            {
                if (f.StartsWith("aff_") || f.StartsWith("trust_") || f == "gold" || f == "reputation" || f == "fatigue" || f == "day" || f == "chapter" || f == "gift_name" || f == "spot_name" || f == "event_step" || f == "spot_id" || f == "gift_pref") continue;
                if (!flags.ContainsKey(f)) dead.Add(f);
            }
            dead.Sort();
            Report.Info("  flags read in conditions but never settable: " + (dead.Count == 0 ? "(none)" : string.Join(", ", dead.ToArray())));
            Report.Check("no script condition depends on a flag that no host-startable path sets", dead.Count == 0, string.Join(", ", dead.ToArray()));

            // counterfactual: if the host also started the common-event nodes
            List<string> ce = new List<string>(host);
            foreach (DialogueNode n in w.Proj.AllNodes)
                if (n.Title.StartsWith("CE") || n.Title.StartsWith("Ch5_Show") || n.Title == "Ch6_LedgerTruth") if (!ce.Contains(n.Title)) ce.Add(n.Title);
            Dictionary<string, int> flags2; Fixpoint(w, b, ce, out flags2);
            Report.Info("  counterfactual (host also plays CE01-08, Ch5_Show*, Ch6_LedgerTruth): flags=" + flags2.Count + "; unreachable keys now: " +
                (flags2.ContainsKey("howl_key") ? "none" : "howl_key still unreachable"));
            // what do the dead CE nodes provide that live nodes consume?
            Report.Info("  host never starts these nodes (found by reading Scripts/** for any Play/Start/node-name builder):");
            foreach (DialogueNode n in w.Proj.AllNodes)
                if (!host.Contains(n.Title) && IsEntryLike(n.Title)) Report.Info("    " + n.Title + (IsJumped(w, n.Title) ? " (jumped to)" : " (NOT reachable)"));

            // Immediate prerequisites of each key (necessity by removal, 1 level, then recurse)
            Report.Info("-- necessary prerequisite flags for each true-ending key (single-node necessity, recursive) --");
            foreach (string id in CharIds.All)
            {
                string key = id + "_key";
                if (!flags.ContainsKey(key)) { Report.Info("  " + key + ": UNREACHABLE"); continue; }
                HashSet<string> visited = new HashSet<string>(); List<string> chain = new List<string>();
                Needs(w, b, host, flags, setter, key, visited, chain, 0);
                Report.Info("  " + key + " <= " + string.Join(" ; ", chain.ToArray()));
            }
        }

        private static void Needs(World w, BalanceTable b, List<string> host, Dictionary<string, int> flags, Dictionary<string, string> setter, string target, HashSet<string> visited, List<string> chain, int depth)
        {
            if (!visited.Add(target) || depth > 4) return;
            // all host nodes that can set `target` given all flags
            List<string> setters = new List<string>();
            Dictionary<string, int> noT = new Dictionary<string, int>(flags); noT.Remove(target);
            foreach (string n in host) if (NodeSets(w, b, noT, n, null).Contains(target) || NodeSets(w, b, noT, n, "howl").Contains(target)) setters.Add(n);
            if (setters.Count == 0) { chain.Add(target + "(no setter)"); return; }
            List<string> needed = new List<string>();
            // flag f is necessary if removing it makes target unsettable in ALL setter nodes
            foreach (string f in new List<string>(flags.Keys))
            {
                if (f == target) continue;
                Dictionary<string, int> minus = new Dictionary<string, int>(noT); minus.Remove(f);
                bool still = false;
                foreach (string n in setters) { if (NodeSets(w, b, minus, n, null).Contains(target) || NodeSets(w, b, minus, n, "howl").Contains(target)) { still = true; break; } }
                if (!still) needed.Add(f);
            }
            chain.Add(target + "[set in " + string.Join("/", setters.ToArray()) + "] needs {" + string.Join(",", needed.ToArray()) + "}");
            foreach (string f in needed) Needs(w, b, host, flags, setter, f, visited, chain, depth + 1);
        }

        private static bool IsEntryLike(string t)
        {
            return t.StartsWith("CE") || t.StartsWith("Ch") || t.StartsWith("Ending") || t.StartsWith("Harem");
        }

        private static bool IsJumped(World w, string title)
        {
            foreach (DialogueNode n in w.Proj.AllNodes)
            {
                bool found = false;
                StatementWalker.Walk(n.Body, delegate (Statement s) { JumpStatement j = s as JumpStatement; if (j != null && j.Target == title) found = true; }, null);
                if (found) return true;
            }
            return false;
        }
    }
}
