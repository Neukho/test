using System;
using System.Collections.Generic;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using MorpheHouse.Tycoon;

namespace QaHunt
{
    public static class EndingProps
    {
        // Independent oracle straight from docs/11 section 2 (numbers typed here, NOT read from the table).
        private sealed class Spec { public float A, T; public int E; public Spec(float a, float t, int e) { A = a; T = t; E = e; } }
        private static readonly Spec Normal = new Spec(60, 20, 3), Good = new Spec(85, 40, 5), True = new Spec(95, 46, 5), Duo = new Spec(72, 24, 3), Trio = new Spec(65, 14, 2);

        private static bool M(CharState c, Spec s) { return c.Aff >= s.A && c.Trust >= s.T && c.EventsDone >= s.E; }
        private static bool F(GameState g, string k) { int v; return g.Flags.TryGetValue(k, out v) && v != 0; }

        public static string Oracle(GameState g)
        {
            List<string> m = g.LockedIds();
            if (m.Count == 0) return "community";
            if (m.Count == 1)
            {
                CharState c = g.Chars[m[0]];
                if (M(c, True) && F(g, "ledger_truth") && F(g, m[0] + "_key")) return "true:" + m[0];
                if (M(c, Good)) return "good:" + m[0];
                if (M(c, Normal)) return "normal:" + m[0];
                return "community";
            }
            Spec sp = m.Count >= 3 ? Trio : Duo;
            foreach (string id in m) if (!M(g.Chars[id], sp) || !F(g, "harem_ok_" + id)) return "community";
            return m.Count >= 3 ? "harem_trio" : "harem_duo";
        }

        public static string Actual(GameState g, EndingTable t)
        {
            EndingResult r = EndingEvaluator.Evaluate(g, t);
            switch (r.Kind)
            {
                case EndingKind.True: return "true:" + r.PrimaryChar;
                case EndingKind.Good: return "good:" + r.PrimaryChar;
                case EndingKind.Normal: return "normal:" + r.PrimaryChar;
                case EndingKind.HaremDuo: return "harem_duo";
                case EndingKind.HaremTrio: return "harem_trio";
                default: return "community";
            }
        }

        public static void Run(World w)
        {
            Report.Header("ENDING LOGIC: truth tables vs independent oracle");
            BalanceTable b = w.Balance(Difficulty.Normal);
            // CSV values equal the documented spec?
            EndingTable t = w.ET;
            Report.Check("endings.csv matches documented thresholds",
                t.NormalRow.AffMin == 60 && t.NormalRow.TrustMin == 20 && t.NormalRow.RequiredEvents == 3 &&
                t.GoodRow.AffMin == 85 && t.GoodRow.TrustMin == 40 && t.GoodRow.RequiredEvents == 5 &&
                t.TrueRow.AffMin == 95 && t.TrueRow.TrustMin == 46 && t.TrueRow.RequiredEvents == 5 &&
                t.HaremDuoRow.AffMin == 72 && t.HaremDuoRow.TrustMin == 24 && t.HaremDuoRow.RequiredEvents == 3 &&
                t.HaremTrioRow.AffMin == 65 && t.HaremTrioRow.TrustMin == 14 && t.HaremTrioRow.RequiredEvents == 2);

            float[] affs = { 0, 59.99f, 60, 64.99f, 65, 69.99f, 70, 71.99f, 72, 84.99f, 85, 94.99f, 95, 100 };
            float[] trs = { 0, 13.99f, 14, 19.99f, 20, 23.99f, 24, 39.99f, 40, 45.99f, 46, 50, 80 };
            int[] evs = { 0, 1, 2, 3, 4, 5, 6, 9 };
            int mism = 0, total = 0; string firstMis = null;
            // single locks: full grid x flag subsets
            foreach (string id in CharIds.All)
                foreach (float a in affs) foreach (float tr in trs) foreach (int e in evs)
                    for (int fl = 0; fl < 8; fl++)
                    {
                        GameState g = GameState.NewGame(b, Difficulty.Normal, "x");
                        g.SetLock(new string[] { id });
                        g.Chars[id].Aff = a; g.Chars[id].Trust = tr; g.Chars[id].EventsDone = e;
                        if ((fl & 1) != 0) g.Flags["ledger_truth"] = 1;
                        if ((fl & 2) != 0) g.Flags[id + "_key"] = 1;
                        if ((fl & 4) != 0) g.Flags["harem_ok_" + id] = 1; // must be ignored for single
                        total++;
                        string o = Oracle(g), x = Actual(g, t);
                        if (o != x) { mism++; if (firstMis == null) firstMis = id + " a=" + a + " t=" + tr + " e=" + e + " fl=" + fl + " oracle=" + o + " actual=" + x; }
                    }
            Report.Check("single-lock grid (" + total + " cases) equals oracle", mism == 0, mism + " mismatches e.g. " + firstMis);

            // single with legacy state (LockedRoute only, LockedGroup empty)
            {
                GameState g = GameState.NewGame(b, Difficulty.Normal, "x"); g.LockedRoute = "nabi";
                g.Chars["nabi"].Aff = 96; g.Chars["nabi"].Trust = 47; g.Chars["nabi"].EventsDone = 5; g.Flags["ledger_truth"] = 1; g.Flags["nabi_key"] = 1;
                Report.Check("legacy LockedRoute-only true ending", Actual(g, t) == "true:nabi");
            }

            // group property tests
            Random r = new Random(11); int gm = 0; string gfirst = null; int gcount = 40000;
            for (int i = 0; i < gcount; i++)
            {
                GameState g = GameState.NewGame(b, Difficulty.Normal, "x");
                int k = 2 + r.Next(2);
                List<string> pool = new List<string>(CharIds.All); List<string> mem = new List<string>();
                for (int j = 0; j < k; j++) { int q = r.Next(pool.Count); mem.Add(pool[q]); pool.RemoveAt(q); }
                g.SetLock(mem);
                foreach (string id in mem)
                {
                    g.Chars[id].Aff = affs[r.Next(affs.Length)]; g.Chars[id].Trust = trs[r.Next(trs.Length)]; g.Chars[id].EventsDone = evs[r.Next(evs.Length)];
                    // bias toward all-pass so both outcomes appear
                    if (r.Next(2) == 0) { g.Chars[id].Aff = k == 3 ? 65 : 72; g.Chars[id].Trust = k == 3 ? 14 : 24; g.Chars[id].EventsDone = k == 3 ? 2 : 3; }
                    if (r.Next(5) != 0) g.Flags["harem_ok_" + id] = 1;
                }
                if (r.Next(2) == 0) g.Flags["ledger_truth"] = 1;
                string o = Oracle(g), x = Actual(g, t);
                if (o != x) { gm++; if (gfirst == null) gfirst = string.Join("+", mem.ToArray()) + " oracle=" + o + " actual=" + x; }
            }
            Report.Check("group random grid (" + gcount + " cases, exactly-at-threshold values) equals oracle", gm == 0, gm + " mismatches e.g. " + gfirst);

            // unusual states
            {
                GameState g = GameState.NewGame(b, Difficulty.Normal, "x");
                g.SetLock(new string[] { "howl", "howl" });
                g.Chars["howl"].Aff = 80; g.Chars["howl"].Trust = 40; g.Chars["howl"].EventsDone = 5; g.Flags["harem_ok_howl"] = 1;
                string x = Actual(g, t);
                Report.Check("[PROBE] duplicate member group [howl,howl] must not yield a harem ending (SaveValidator forbids it, evaluator should too)", x == "community", "got " + x);
                g = GameState.NewGame(b, Difficulty.Normal, "x"); g.SetLock(new string[] { "howl", "johan", "nabi", "haru" });
                foreach (string id in g.LockedIds()) { g.Chars[id].Aff = 99; g.Chars[id].Trust = 40; g.Chars[id].EventsDone = 5; g.Flags["harem_ok_" + id] = 1; }
                x = Actual(g, t);
                Report.Check("[PROBE] 4-member group (invalid) must not yield harem_trio", x == "community", "got " + x);
                g = GameState.NewGame(b, Difficulty.Normal, "x"); g.SetLock(new string[] { "zzz" });
                x = Actual(g, t);
                Report.Check("unknown locked id resolves to community (no exception)", x == "community");
                g = GameState.NewGame(b, Difficulty.Normal, "x"); g.SetLock(new string[] { "howl", "zzz" });
                g.Chars["howl"].Aff = 99; g.Chars["howl"].Trust = 40; g.Chars["howl"].EventsDone = 5; g.Flags["harem_ok_howl"] = 1; g.Flags["harem_ok_zzz"] = 1;
                x = Actual(g, t);
                Report.Check("duo with unknown member resolves to community", x == "community");
                g = GameState.NewGame(b, Difficulty.Normal, "x"); g.SetLock(new string[] { "howl" });
                g.Chars["howl"].Aff = 95; g.Chars["howl"].Trust = 46; g.Chars["howl"].EventsDone = 5; g.Flags["ledger_truth"] = -1; g.Flags["howl_key"] = 2;
                Report.Check("negative/!=1 flag values count as set (document: flags are 'non-zero')", Actual(g, t) == "true:howl");
            }

            {
                GameState g = GameState.NewGame(b, Difficulty.Normal, "x"); g.SetLock(new string[] { "howl", "johan" });
                g.SetLock(g.LockedGroup); // aliasing: re-applying the state's own list
                Report.Check("[PROBE] GameState.SetLock(state.LockedGroup) keeps the lock (aliasing)", g.HasRouteLock && g.LockedCount == 2, "lock wiped: LockedGroup.Clear() empties the very list being copied (latent: no caller does this today)");
            }
            // EndingPlan / EndingFlow over the whole outcome space
            int planBad = 0; int cases = 0;
            Random r2 = new Random(5);
            for (int i = 0; i < 4000; i++)
            {
                GameState g = SaveProps.RandomState(w, b, r2);
                // push many states to endings
                if (r2.Next(2) == 0) foreach (string id in g.LockedIds()) { g.Chars[id].Aff = 95; g.Chars[id].Trust = 46; g.Chars[id].EventsDone = 5; g.Flags["harem_ok_" + id] = 1; g.Flags[id + "_key"] = 1; g.Flags["ledger_truth"] = 1; }
                EndingPlan plan = EndingPlan.Create(g, t, w.Cat, delegate (string n) { return w.Proj.Contains(n); });
                cases++;
                if (plan.NodeName == null) { planBad++; Report.Info("  plan has no node: " + plan.IntendedNode); continue; }
                if (plan.FallbackText.Length == 0 || plan.TitleKo.Length == 0) planBad++;
                List<SceneItem> sc = EndingFlow.Scenes(plan, delegate (string n) { return w.Proj.Contains(n); });
                int expect = 1 + (plan.Result.IsHarem ? plan.Result.Members.Count : 0);
                if (sc.Count != expect) planBad++;
                foreach (SceneItem si in sc) if (si.Node == null) { planBad++; Report.Info("  scene without node " + si.IntendedNode); }
                if (plan.Result.Kind == EndingKind.True && !plan.CgIds.Contains(EndingNodes.TrueCg(plan.Result.PrimaryChar))) planBad++;
                EndSummary.Build(plan, g, 3, 10, 2, 21);
            }
            {
                HashSet<string> slotKeys = new HashSet<string>();
                foreach (EndingSlot sl in EndingGallery.Slots(null, null)) slotKeys.Add(sl.Key);
                int miss = 0;
                Random r3 = new Random(9);
                for (int i = 0; i < 3000; i++)
                {
                    GameState g = SaveProps.RandomState(w, b, r3);
                    foreach (string id in g.LockedIds()) if (r3.Next(2) == 0) { g.Chars[id].Aff = 95; g.Chars[id].Trust = 46; g.Chars[id].EventsDone = 5; g.Flags["harem_ok_" + id] = 1; g.Flags[id + "_key"] = 1; g.Flags["ledger_truth"] = 1; }
                    string key = ProfileService.EndingKey(EndingEvaluator.Evaluate(g, t));
                    if (!slotKeys.Contains(key)) { miss++; Report.Info("  ending key not in gallery: " + key); }
                }
                Report.Check("every evaluator outcome maps to one of the 21 gallery slots (profile/gallery consistency)", miss == 0 && slotKeys.Count == 21, miss + " missing, slots=" + slotKeys.Count);
            }
            Report.Check("EndingPlan/EndingFlow/EndSummary over " + cases + " random states: node always exists, scene count right", planBad == 0, planBad + " problems");

            // reachable-set accounting: every one of 21 endings can be produced by the evaluator
            HashSet<string> kinds = new HashSet<string>();
            foreach (string id in CharIds.All)
                foreach (EndingKind k in new EndingKind[] { EndingKind.Normal, EndingKind.Good, EndingKind.True })
                {
                    GameState g = GameState.NewGame(b, Difficulty.Normal, "x"); g.SetLock(new string[] { id });
                    Spec s = k == EndingKind.True ? True : (k == EndingKind.Good ? Good : Normal);
                    g.Chars[id].Aff = s.A; g.Chars[id].Trust = s.T; g.Chars[id].EventsDone = s.E;
                    if (k == EndingKind.True) { g.Flags["ledger_truth"] = 1; g.Flags[id + "_key"] = 1; }
                    kinds.Add(EndingEvaluator.Evaluate(g, t).Id + ":" + id);
                }
            Report.Check("evaluator can emit all 18 personal endings at exact thresholds", kinds.Count == 18, kinds.Count + "");

            RouteLockEdges(w, b);
        }

        private static void RouteLockEdges(World w, BalanceTable b)
        {
            Report.Info("-- route lock boundary: chapter 6 last night vs chapter 7 first night via real DayCycle --");
            GameState s = GameState.NewGame(b, Difficulty.Normal, "x");
            foreach (string id in CharIds.All) { s.Chars[id].Aff = 80; s.Chars[id].Trust = 30; }
            DayCycle dc = new DayCycle(s, b, new SystemRandomAdapter(1));
            RomanceService rs = new RomanceService(s, b, w.Cat, new Random(1).NextDouble);
            NightRomanceActions na = new NightRomanceActions(s, b, rs, w.Cat, delegate (string n) { return w.Proj.Contains(n); }, delegate (string n) { return false; });
            dc.Begin();
            int guard = 0; bool lockedEarly = false; int lockDay = -1;
            while (!(s.Chapter == 7 && s.DayInChapter == 1) && guard++ < 200)
            {
                dc.RunBusiness(); dc.BeginNight();
                string msg;
                if (s.Chapter < 7) { if (na.TryLockGroup(new string[] { "howl", "johan" }, out msg)) lockedEarly = true; }
                dc.Settle(); dc.NextDay();
                foreach (string id in CharIds.All) { s.Chars[id].LastContactDay = s.Day; } // avoid decay masking
            }
            Report.Check("cannot lock in chapters 1-6 even with 80/30 everyone", !lockedEarly);
            dc.RunBusiness(); dc.BeginNight();
            string m2;
            bool single = na.TryLockRoute("howl", out m2);
            Report.Check("lock succeeds on first night of chapter 7 (day " + s.Day + ")", single);
            Report.Check("second lock attempt refused 'already locked'", !na.TryLockGroup(new string[] { "johan" }, out m2) && m2.Contains("이미"));
            lockDay = s.Day;
            Report.Check("lock at ch7 d1 is day 49 (6+8+8+8+8+10=48 days before)", lockDay == 49, "day=" + lockDay);

            // freeze invariant: snapshot non-members, play on through the rest of the game and compare
            Dictionary<string, float[]> snap = new Dictionary<string, float[]>();
            foreach (string id in CharIds.All) snap[id] = new float[] { s.Chars[id].Aff, s.Chars[id].Trust, s.Chars[id].EventsDone };
            dc.Settle(); dc.NextDay();
            Random rng = new Random(3);
            int ok = 0;
            while (!s.Finished && guard++ < 1000)
            {
                dc.RunBusiness(); dc.BeginNight();
                while (s.ActionsLeft > 0)
                {
                    string id = CharIds.All[rng.Next(6)];
                    int k = rng.Next(3);
                    if (k == 0) rs.Talk(id); else if (k == 1) rs.Gift(id, "small_plant"); else rs.Date(id, "beach");
                    rs.AdjustAffection(id, 5, 2, "t");
                }
                dc.Settle(); dc.NextDay(); ok++;
            }
            bool frozenOk = true; string why = "";
            foreach (string id in CharIds.All)
            {
                if (id == "howl") continue;
                CharState c = s.Chars[id];
                if (c.Aff != snap[id][0] || c.Trust != snap[id][1] || c.EventsDone != (int)snap[id][2]) { frozenOk = false; why += id + " "; }
            }
            Report.Check("non-members frozen (aff/trust/events unchanged) through chapters 7-8 under random actions + dialogue adjustments", frozenOk, why);
        }
    }
}
