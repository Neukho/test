using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using MorpheHouse.Save;
using MorpheHouse.Tycoon;

namespace QaHunt
{
    public sealed class Policy
    {
        public string Name = "p";
        public Difficulty Diff = Difficulty.Normal;
        public string Gender = "neutral";
        public int Seed = 1;
        public List<string> Targets = new List<string>();
        /// <summary>none | single | group</summary>
        public string LockMode = "none";
        /// <summary>ch7first | ch7last | ch8first | ch8last | asap</summary>
        public string LockAt = "ch7last";
        public bool Smart = true;
        public bool Chaos = false;
        public bool WantTrue = true;
        public bool SaveLoadMid = true;
        public bool NeverUpgrade = false;
        public bool GroupFocus = false;     // FocusFrac with several Targets: events for every member first, focus share split evenly among them, random answers (docs/10 section 2)
        public bool NoFreeRest = false;     // fatigue probe: never use the free rest
        public bool SpamDates = false;      // fatigue probe: date every action
        public double RestAbove = 999;
        public bool PlayConsentSmart = true;
        /// <summary>share of free actions spent on gomseok (ledger_truth source) when going for a true ending with another character</summary>
        public double LedgerShare = 0.35;
        /// <summary>true: never spend night actions (press 'finish night' immediately) - tests that scheduled scenes still play</summary>
        public bool Idle = false;
        /// <summary>FocusFrac mode: keep spending the 'other' share on locked-out (frozen) characters after the lock, like tools/simulate_balance.py does</summary>
        public bool WasteAfterLock = false;
        /// <summary>>0: python-simulator style policy - this fraction of actions go to Targets[0], the rest uniformly to the other 5; no deliberate ledger pursuit.</summary>
        public double FocusFrac = 0;
        /// <summary>true: ignore Targets[0] completely (no events, no actions) until the route is locked, then go all-in (explore 'lock early, cram after' exploit)</summary>
        public bool LateOnly = false;
    }

    public sealed class RunResult
    {
        public Policy P;
        public string EndingId = "";
        public string EndingNode = "";
        public bool EndingNodeOk;
        public string[] Members = new string[0];
        public List<string> Violations = new List<string>();
        public List<string> Notes = new List<string>();
        public int Days;
        public int LimitedDays;          // nights where fatigue >= limit at BeginNight
        public float MaxFatigue;
        public float MinGold = float.MaxValue;
        public double[] GrossByChapter = new double[9];
        public int[] DaysByChapter = new int[9];
        public int[] LevelByChapter = new int[9];
        public int MaxFlags;
        public GameState Final;
        public HashSet<string> Flags = new HashSet<string>();
        public int LockDay = -1, LockChapter = -1;
        public int ActionsUsed;
        public int ActionsWasted;       // nights ended with ActionsLeft>0
        public Dictionary<string, int> EventDay = new Dictionary<string, int>();
        public int PathPlans;
        /// <summary>sum of aff/trust deltas applied by script nodes (dialogue commands), per char, split own-node vs other-char</summary>
        public Dictionary<string, double> ScriptAff = new Dictionary<string, double>();
        public Dictionary<string, double> ScriptTrust = new Dictionary<string, double>();
        public Dictionary<string, double> CrossAff = new Dictionary<string, double>(); // node of char X changed char Y != X
        public Dictionary<string, int> ActionsOn = new Dictionary<string, int>();
        /// <summary>every node the host started, in order: "chapter.dayInChapter:slot:node"</summary>
        public List<SceneLogEntry> SceneLog = new List<SceneLogEntry>();
        public HashSet<string> Visited = new HashSet<string>();
    }

    public sealed class SceneLogEntry
    {
        public int Day, Chapter, DayInChapter;
        public string Slot, Node;
    }

    public sealed class Sim
    {
        public World W;
        public Policy P;
        public Random Rng;
        public BalanceTable B;
        public GameState S;
        public DayCycle C;
        public RomanceService R;
        public NightRomanceActions N;
        public DialogueSession D;
        public RecSink Sink;
        public HashSet<string> Attempted = new HashSet<string>();
        public RunResult Res;
        private Dictionary<string, int> _flagSnap = new Dictionary<string, int>();
        private string _playing = null;
        private Dictionary<string, float[]> _frozenSnap = null;
        private HashSet<string> _eventsPlayed = new HashSet<string>();
        private DayPhase _lastPhase = DayPhase.Settlement;
        private int _lastDay = 0;
        private Dictionary<string, int> _lastGiftOk = new Dictionary<string, int>();
        private Dictionary<string, int> _lastDateOk = new Dictionary<string, int>();
        private SaveService _svc = new SaveService(new XmlSer(), new MemStorage());
        private HashSet<string> _want;
        private static Dictionary<string, HashSet<string>> _prereq;
        private HashSet<string> _offeredExtraToday = new HashSet<string>();
        private int _promptDay = -1;
        private string _slot = "";
        public bool UseSceneGate = true;

        public static Dictionary<string, HashSet<string>> Prereq(World w)
        {
            if (_prereq != null) return _prereq;
            _prereq = new Dictionary<string, HashSet<string>>();
            foreach (DialogueNode n in w.Proj.AllNodes) Collect(n.Body, new List<string>(), _prereq);
            return _prereq;
        }

        private static void AddAll(Dictionary<string, HashSet<string>> map, string flag, List<string> conds)
        {
            HashSet<string> set;
            if (!map.TryGetValue(flag, out set)) { set = new HashSet<string>(); map[flag] = set; }
            foreach (string c in conds)
            {
                string pos = Regex.Replace(c, "not\\s+flag\\(\"\\w+\"\\)", "");
                pos = Regex.Replace(pos, "not\\s+\\$\\w+", "");
                foreach (Match m in Regex.Matches(pos, "flag\\(\"(\\w+)\"\\)|\\$(\\w+)"))
                {
                    string f = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (f.StartsWith("aff_") || f.StartsWith("trust_") || f == "gold" || f == "day" || f == "chapter" || f == "reputation" || f == "fatigue") continue;
                    set.Add(f);
                }
            }
        }

        private static void Collect(List<Statement> body, List<string> conds, Dictionary<string, HashSet<string>> map)
        {
            foreach (Statement st in body)
            {
                CommandStatement cs = st as CommandStatement;
                if (cs != null && cs.Name == "flag" && cs.Args.Count >= 1) { AddAll(map, cs.Args[0].Source.TrimStart('$'), conds); continue; }
                SetStatement ss = st as SetStatement;
                if (ss != null) { AddAll(map, ss.Variable.TrimStart('$'), conds); continue; }
                IfStatement ifs = st as IfStatement;
                if (ifs != null)
                {
                    foreach (IfBranch br in ifs.Branches)
                    {
                        List<string> c2 = new List<string>(conds);
                        if (br.Condition != null) c2.Add(br.Condition.Source);
                        Collect(br.Body, c2, map);
                    }
                    continue;
                }
                OptionsStatement os = st as OptionsStatement;
                if (os != null)
                {
                    foreach (OptionItem it in os.Items)
                    {
                        List<string> c2 = new List<string>(conds);
                        if (it.Condition != null) c2.Add(it.Condition.Source);
                        Collect(it.Body, c2, map);
                    }
                }
            }
        }

        private HashSet<string> ComputeWant()
        {
            HashSet<string> want = new HashSet<string>();
            Dictionary<string, HashSet<string>> pre = Prereq(W);
            List<string> q = new List<string>();
            foreach (string t in P.Targets)
            {
                if (P.WantTrue && P.LockMode == "single") { q.Add(t + "_key"); q.Add("ledger_truth"); }
                if (P.LockMode == "group") q.Add("harem_ok_" + t);
            }
            while (q.Count > 0)
            {
                string f = q[q.Count - 1]; q.RemoveAt(q.Count - 1);
                if (!want.Add(f)) continue;
                HashSet<string> ps;
                if (pre.TryGetValue(f, out ps)) foreach (string x in ps) q.Add(x);
            }
            return want;
        }

        public Sim(World w, Policy p)
        {
            W = w; P = p; Rng = new Random(p.Seed);
            Res = new RunResult(); Res.P = p;
            B = w.Balance(p.Diff);
            ProtagonistProfile prof = new ProtagonistProfile();
            prof.Name = "민준"; prof.Gender = p.Gender; prof.Species = "human"; prof.Appearance = 1;
            S = GameState.NewGame(B, p.Diff, prof);
            _want = ComputeWant();
            Build();
        }

        private void Build()
        {
            C = new DayCycle(S, B, new SystemRandomAdapter(P.Seed * 7919 + 13));
            R = new RomanceService(S, B, W.Cat, new Random(P.Seed * 31 + 7).NextDouble);
            Sink = new RecSink();
            D = Runner.NewSession(W, S, R, Sink);
            N = new NightRomanceActions(S, B, R, W.Cat, delegate (string n) { return D.HasNode(n); }, HasVisited);
            Attempted.Clear();
        }

        private bool HasVisited(string n) { return D.HasVisited(n) || (n != null && Attempted.Contains(n)); }

        private void Viol(string v)
        {
            if (Res.Violations.Count < 40) Res.Violations.Add("d" + S.Day + " ch" + S.Chapter + " " + S.Phase + ": " + v);
        }

        // ------------------------------------------------------------ invariants
        private void SnapFlags()
        {
            _flagSnap.Clear();
            foreach (KeyValuePair<string, int> p in S.Flags) _flagSnap[p.Key] = p.Value;
        }

        /// <summary>Flags may only change inside a dialogue node (host never writes them, except sys_*).</summary>
        private void CheckFlagsOutsideDialogue(string where)
        {
            foreach (KeyValuePair<string, int> p in S.Flags)
            {
                int old;
                bool had = _flagSnap.TryGetValue(p.Key, out old);
                if ((!had && p.Value != 0) || (had && old != p.Value))
                {
                    if (p.Key.StartsWith("sys_")) continue;
                    Viol("flag '" + p.Key + "' changed outside any dialogue node (" + where + ")");
                }
            }
            SnapFlags();
        }

        private void Invariants(string tag)
        {
            ChapterRow ch = B.Chapter(S.Chapter);
            if (float.IsNaN(S.Gold) || float.IsInfinity(S.Gold)) Viol("gold not finite " + tag);
            if (S.Gold < Res.MinGold) Res.MinGold = S.Gold;
            if (S.Fatigue < 0f || S.Fatigue > 100f) Viol("fatigue out of range " + S.Fatigue + " " + tag);
            if (S.Reputation < 0f || S.Reputation > 100f) Viol("reputation out of range " + S.Reputation);
            if (S.ActionsLeft < 0 || S.ActionsLeft > (int)B.P("actions_per_night")) Viol("ActionsLeft out of range " + S.ActionsLeft);
            if (S.ShopLevel < 1 || S.ShopLevel > ch.LevelCap) Viol("shop level " + S.ShopLevel + " > cap " + ch.LevelCap);
            if (S.DayInChapter < 1 || S.DayInChapter > ch.Days) Viol("day-in-chapter " + S.DayInChapter + " out of 1.." + ch.Days);
            foreach (string id in CharIds.All)
            {
                CharState c = S.Chars[id];
                if (c.Aff < 0f || c.Aff > ch.AffCap + 0.001f) Viol(id + " aff " + c.Aff + " outside 0.." + ch.AffCap + " " + tag);
                if (c.Trust < 0f || c.Trust > ch.TrustCap + 0.001f) Viol(id + " trust " + c.Trust + " outside 0.." + ch.TrustCap + " " + tag);
                if (c.EventsDone < 0 || c.EventsDone > 5) Viol(id + " eventsDone " + c.EventsDone);
                if (float.IsNaN(c.Aff) || float.IsNaN(c.Trust)) Viol(id + " NaN");
            }
            if (S.Flags.Count > Res.MaxFlags) Res.MaxFlags = S.Flags.Count;
            if (S.HasRouteLock)
            {
                List<string> ids = S.LockedIds();
                if (ids.Count < 1 || ids.Count > 3) Viol("lock size " + ids.Count);
                if (S.LockedRoute != ids[0]) Viol("LockedRoute != first member");
                if (S.LockedGroup.Count > 0 && S.LockedGroup[0] != S.LockedRoute) Viol("group[0] != LockedRoute");
            }
            if (_frozenSnap != null)
            {
                foreach (KeyValuePair<string, float[]> p in _frozenSnap)
                {
                    CharState c = S.Chars[p.Key];
                    if (c.Aff != p.Value[0] || c.Trust != p.Value[1] || c.EventsDone != (int)p.Value[2]) Viol("frozen non-member " + p.Key + " changed: aff " + p.Value[0] + "->" + c.Aff + " trust " + p.Value[1] + "->" + c.Trust + " ev " + p.Value[2] + "->" + c.EventsDone);
                }
            }
        }

        private void PhaseOrder(DayPhase expectedPrev, DayPhase now)
        {
            // legal transitions: Morning->Business->Night->Settlement->Morning
            if (_lastPhase == DayPhase.Settlement && now != DayPhase.Morning) Viol("phase " + _lastPhase + " -> " + now);
            _lastPhase = now;
        }

        // ------------------------------------------------------------ dialogue
        private Func<DialogueStep, int> Picker(string node, Dictionary<string, DialogueValue> temps)
        {
            // group-focus reference player: random answers everywhere except the consent scene, where a player who wants the group says yes
            bool consentScene = P.GroupFocus && node != null && node.EndsWith("_Harem_Consent");
            if (!P.Smart && !consentScene) return Runner.PickRandom(Rng);
            List<int> plan = Plan(node, temps);
            if (plan == null) return Runner.PickRandom(Rng);
            return Runner.PickSeq(plan);
        }

        private List<int> Plan(string node, Dictionary<string, DialogueValue> temps)
        {
            if (!D.HasNode(node)) return null;
            Res.PathPlans++;
            List<Runner.PathOutcome> outs = Runner.EnumeratePaths(W, B, S, D.ExportVisited(), node, temps, 300);
            if (outs.Count == 0) return null;
            double best = double.MinValue; List<int> bestSeq = null;
            foreach (Runner.PathOutcome po in outs)
            {
                double sc = 0;
                foreach (string f in _want)
                {
                    int ov, nv; S.Flags.TryGetValue(f, out ov); po.End.Flags.TryGetValue(f, out nv);
                    if (nv != 0 && ov == 0) sc += f.EndsWith("_key") ? 5000 : (f.StartsWith("harem_ok_") ? 3000 : 500);
                }
                foreach (string t in P.Targets)
                {
                    sc += (po.End.Chars[t].Aff - S.Chars[t].Aff) + 1.5 * (po.End.Chars[t].Trust - S.Chars[t].Trust);
                }
                if (sc > best) { best = sc; bestSeq = po.Seq; }
            }
            return bestSeq;
        }

        public bool PlayNode(string node, string fbSpeaker, string fbText, Dictionary<string, DialogueValue> temps)
        {
            if (!string.IsNullOrEmpty(node)) Attempted.Add(node);
            if (!string.IsNullOrEmpty(node) && D.HasNode(node))
            {
                SceneLogEntry le = new SceneLogEntry(); le.Day = S.Day; le.Chapter = S.Chapter; le.DayInChapter = S.DayInChapter; le.Slot = _slot; le.Node = node;
                Res.SceneLog.Add(le);
            }
            SnapFlags();
            Func<DialogueStep, int> pick = Picker(node, temps);
            _playing = node;
            Dictionary<string, float[]> pre = new Dictionary<string, float[]>();
            foreach (string id in CharIds.All) pre[id] = new float[] { S.Chars[id].Aff, S.Chars[id].Trust };
            SceneResult sr = Runner.Play(D, Sink, node, pick, temps, fbSpeaker, fbText);
            _playing = null;
            foreach (string id in CharIds.All)
            {
                double da = S.Chars[id].Aff - pre[id][0], dt = S.Chars[id].Trust - pre[id][1];
                if (da == 0 && dt == 0) continue;
                double x;
                Res.ScriptAff.TryGetValue(id, out x); Res.ScriptAff[id] = x + da;
                Res.ScriptTrust.TryGetValue(id, out x); Res.ScriptTrust[id] = x + dt;
                if (node != null && !node.StartsWith(NightRomanceActions.CharTitle(id) + "_") && node.IndexOf('_') > 0 && !node.StartsWith("Ch") && !node.StartsWith("CE") && !node.StartsWith("Ending") && !node.StartsWith("Harem"))
                { Res.CrossAff.TryGetValue(id, out x); Res.CrossAff[id] = x + da; }
            }
            // attribute flag changes
            foreach (KeyValuePair<string, int> p in S.Flags)
            {
                int old; bool had = _flagSnap.TryGetValue(p.Key, out old);
                if (had && old != 0 && p.Value == 0) Res.Notes.Add("flag cleared by script: " + p.Key + " in " + node);
                if (p.Key.StartsWith("harem_ok_") && p.Value != 0 && (!had || old == 0))
                {
                    string who = p.Key.Substring(9);
                    if (node != ConsentRetry.ConsentNode(who)) Viol("harem_ok_" + who + " set by node " + node + " (only the consent node may set it)");
                }
            }
            SnapFlags();
            if (!sr.Ended) Viol("scene did not end: " + node);
            foreach (string e in sr.Errors) Viol("script error in " + node + ": " + e);
            return sr.RouteChoice;
        }

        // ------------------------------------------------------------ night helpers
        private List<string> ActiveTargets()
        {
            List<string> list = new List<string>();
            foreach (string t in P.Targets) if (!R.IsFrozen(t) && (P.Chaos || R.IsAvailable(t))) list.Add(t);
            if (list.Count == 0 && !P.Chaos)
            {
                // nobody on the wish list has appeared in the story yet (e.g. Haru before ch4): a player spends the night on whoever is there
                foreach (string id in CharIds.All) if (!R.IsFrozen(id) && R.IsAvailable(id)) list.Add(id);
            }
            return list;
        }

        private string LikedGift(string id)
        {
            List<string> l = W.Cat.TagsFor(id, PrefKind.Gift, Preference.Like);
            return l.Count > 0 ? l[Rng.Next(l.Count)] : "small_plant";
        }

        private string LikedSpot(string id)
        {
            List<string> l = W.Cat.TagsFor(id, PrefKind.Spot, Preference.Like);
            List<string> ok = new List<string>();
            foreach (string s in l) if (W.Cat.FindSpot(s).UnlockChapter <= S.Chapter) ok.Add(s);
            if (ok.Count > 0) return ok[Rng.Next(ok.Count)];
            List<SpotChoice> sc = N.SpotChoices();
            foreach (SpotChoice c in sc) if (!c.Locked) return c.Id;
            return "beach";
        }

        private void Outcome(NightActionOutcome o, string kind)
        {
            if (!o.Success) return;
            Res.ActionsUsed++;
            PlayNode(o.NodeName, o.FallbackSpeaker, o.FallbackText, o.Temps);
            if (kind == "event")
            {
                string key = o.CharId + ":" + S.Chars[o.CharId].EventsDone;
                if (!_eventsPlayed.Add(key)) Viol("event " + key + " played twice");
                Res.EventDay[o.CharId + "_E" + S.Chars[o.CharId].EventsDone] = S.Day;
            }
        }

        private void DoGift(string id, string gift)
        {
            int before = S.ActionsLeft; float gold = S.Gold; int lastBefore;
            _lastGiftOk.TryGetValue(id, out lastBefore);
            NightActionOutcome o = N.Gift(id, gift);
            if (!o.Success)
            {
                if (S.ActionsLeft != before || S.Gold != gold) Viol("refused gift changed state");
                return;
            }
            if (!R.IsFrozen(id) && _lastGiftOk.ContainsKey(id) && S.Day - lastBefore < (int)B.Actions["gift_like"].CooldownDays) Viol("gift cooldown violated for " + id);
            if (!R.IsFrozen(id)) _lastGiftOk[id] = S.Day;
            Outcome(o, "gift");
        }

        private void DoDate(string id, string spot)
        {
            int before = S.ActionsLeft; float gold = S.Gold; int lastBefore;
            _lastDateOk.TryGetValue(id, out lastBefore);
            NightActionOutcome o = N.Date(id, spot);
            if (!o.Success)
            {
                if (S.ActionsLeft != before || S.Gold != gold) Viol("refused date changed state");
                return;
            }
            if (!R.IsFrozen(id) && _lastDateOk.ContainsKey(id) && S.Day - lastBefore < (int)B.Actions["date"].CooldownDays) Viol("date cooldown violated for " + id);
            if (!R.IsFrozen(id)) _lastDateOk[id] = S.Day;
            Outcome(o, "date");
        }

        private void ChooseAction()
        {
            // 1. any available event for targets (and gomseok for ledger when needed)
            List<string> evChars = new List<string>(ActiveTargets());
            if (P.LockMode == "single" && P.WantTrue && !evChars.Contains("gomseok") && !R.IsFrozen("gomseok")) evChars.Add("gomseok");
            if (P.Chaos) { evChars.Clear(); foreach (string id in CharIds.All) evChars.Add(id); }
            if (P.FocusFrac > 0) { evChars.Clear(); if (P.GroupFocus) evChars.AddRange(ActiveTargets()); else evChars.Add(P.Targets[0]); }
            if (P.LateOnly && !S.HasRouteLock) evChars.Clear();
            // prefer lowest EventsDone
            evChars.Sort(delegate (string a, string b2) { return S.Chars[a].EventsDone.CompareTo(S.Chars[b2].EventsDone); });
            foreach (string id in evChars)
            {
                if (N.HasEvent(id)) { Outcome(N.Event(id), "event"); return; }
            }
            if (S.Fatigue >= P.RestAbove) { C.DoNightAction(NightAction.Rest); return; }
            List<string> tgt = ActiveTargets();
            if (P.Chaos || tgt.Count == 0) { tgt = new List<string>(CharIds.All); }
            string who = tgt[Rng.Next(tgt.Count)];
            if (P.FocusFrac > 0)
            {
                List<string> gt = ActiveTargets();
                if (P.GroupFocus && Rng.NextDouble() < P.FocusFrac && gt.Count > 0) who = gt[Rng.Next(gt.Count)];
                else if (!P.GroupFocus && Rng.NextDouble() < P.FocusFrac && !(P.LateOnly && !S.HasRouteLock) && R.IsAvailable(P.Targets[0])) who = P.Targets[0];
                else
                {
                    List<string> others = new List<string>();
                    foreach (string id in CharIds.All) if ((P.GroupFocus ? !P.Targets.Contains(id) : id != P.Targets[0]) && R.IsAvailable(id) && (P.WasteAfterLock || !R.IsFrozen(id))) others.Add(id);
                    if (others.Count == 0) others.Add(P.GroupFocus && gt.Count > 0 ? gt[Rng.Next(gt.Count)] : P.Targets[0]);
                    who = others[Rng.Next(others.Count)];
                    if (N.HasEvent(who)) { Outcome(N.Event(who), "event"); return; }
                }
            }
            bool needLedger = P.LockMode == "single" && P.WantTrue && P.Targets[0] != "gomseok" && !R.IsFrozen("gomseok") && !S.Flags.ContainsKey("ledger_truth") && S.Chars["gomseok"].EventsDone < 4;
            if (!P.Chaos && P.FocusFrac <= 0 && needLedger && Rng.NextDouble() < P.LedgerShare) who = "gomseok";
            if (P.SpamDates)
            {
                // fatigue probe: spend every action on whichever char has date ready, else talk
                foreach (string id in CharIds.All) if (R.DateReady(id) && S.Gold >= R.DateCost()) { DoDate(id, LikedSpot(id)); return; }
                Outcome(N.Talk(who), "talk"); return;
            }
            if (P.Chaos)
            {
                int k = Rng.Next(8);
                if (k == 0) { Outcome(N.Talk(who), "talk"); }
                else if (k == 1) { DoGift(who, LikedGift(who)); }
                else if (k == 2) { DoGift(who, "no_such_gift"); }
                else if (k == 3) { DoDate(who, LikedSpot(who)); }
                else if (k == 4) { DoDate(who, "no_such_spot"); }
                else if (k == 5) { DoDate(who, "rooftop"); }  // locked early
                else if (k == 6) { C.DoNightAction(Rng.Next(2) == 0 ? NightAction.Rest : NightAction.Research); }
                else { NightActionOutcome o = N.Event(who); if (o.Success) Outcome(o, "event"); }
                return;
            }
            // focused: date > gift > talk by readiness
            if (R.DateReady(who) && S.Gold >= R.DateCost() && Rng.Next(10) < 8) { DoDate(who, LikedSpot(who)); return; }
            if (R.GiftReady(who) && S.Gold >= R.GiftCost()) { DoGift(who, LikedGift(who)); return; }
            // try another target with something ready
            foreach (string id in tgt)
            {
                if (R.DateReady(id) && S.Gold >= R.DateCost()) { DoDate(id, LikedSpot(id)); return; }
                if (R.GiftReady(id) && S.Gold >= R.GiftCost()) { DoGift(id, LikedGift(id)); return; }
            }
            Outcome(N.Talk(who), "talk");
        }

        // ------------------------------------------------------------ route lock
        private bool LockWindowOpen()
        {
            if (P.LockMode == "none") return false;
            if (S.Chapter < RomanceRules.LockMinChapter) return false;
            switch (P.LockAt)
            {
                case "asap": return true;
                case "ch7first": return true;
                case "ch7last": return S.Chapter > 7 || S.DayInChapter >= B.Chapter(7).Days;
                case "ch8first": return S.Chapter >= 8;
                case "ch8last": return S.Chapter >= 8 && S.DayInChapter >= B.Chapter(8).Days - 1;
            }
            return true;
        }

        private void MaybeLock()
        {
            if (S.HasRouteLock || !LockWindowOpen()) return;
            List<string> want = P.LockMode == "single" ? new List<string>(new string[] { P.Targets[0] }) : new List<string>(P.Targets);
            // wait for events 4 (or 3 for single) + ledger (if single true) unless this is the last chance
            bool lastChance = S.Chapter == 8 && S.DayInChapter >= B.Chapter(8).Days;
            if (P.LockAt != "asap" && !lastChance)
            {
                if (P.LockMode == "single" && P.WantTrue && P.Targets[0] != "gomseok" && !S.Flags.ContainsKey("ledger_truth") && S.Chapter < 8) return;
            }
            string msg;
            string reason = R.CheckGroupLock(want);
            if (reason != null) { if (lastChance) Res.Notes.Add("last-night lock impossible: " + reason); return; }
            // snapshot frozen
            Dictionary<string, float[]> snap = new Dictionary<string, float[]>();
            foreach (string id in CharIds.All) if (!want.Contains(id)) snap[id] = new float[] { S.Chars[id].Aff, S.Chars[id].Trust, S.Chars[id].EventsDone };
            if (!N.TryLockGroup(want, out msg)) { Viol("CheckGroupLock null but TryLockGroup failed: " + msg); return; }
            _frozenSnap = snap;
            Res.LockDay = S.Day; Res.LockChapter = S.Chapter;
            // post-lock scenes exactly like RouteSelectScreen.OnCommit
            List<SceneItem> scenes = RouteFlow.PostLockScenes(want, D.HasNode);
            foreach (SceneItem si in scenes) PlayNode(si.Node, null, si.FallbackText, null);
        }

        private void PlayExtras()
        {
            int guard = 0;
            while (guard++ < 12)
            {
                List<NightExtraEntry> extras = NightExtras.Collect(S, null, D.HasNode, HasVisited, N.CharName);
                NightExtraEntry pick = null;
                foreach (NightExtraEntry e in extras) if (!_offeredExtraToday.Contains(e.Id + ":" + S.Day)) { pick = e; break; }
                if (pick == null) return;
                _offeredExtraToday.Add(pick.Id + ":" + S.Day);
                int actionsBefore = S.ActionsLeft;
                PlayNode(pick.NodeName, null, null, null);
                if (S.ActionsLeft != actionsBefore) Viol("extra scene consumed an action");
                if (!P.PlayConsentSmart && Rng.Next(2) == 0) return;
            }
        }

        private void CommonScenes(SceneSlot slot)
        {
            if (!UseSceneGate) return;
            int guard = 0;
            string prev = _slot;
            while (guard++ < 12)
            {
                ChapterStep cs = N.NextCommonScene(slot);
                if (cs.Kind == ChapterStepKind.None) break;
                if (cs.Kind != ChapterStepKind.CommonScene) { Viol("NextCommonScene returned kind " + cs.Kind); break; }
                _slot = "common-" + slot;
                int before = S.ActionsLeft;
                bool rc = PlayNode(cs.Node, null, null, null);
                if (S.ActionsLeft != before) Viol("common scene consumed an action");
                if (HasVisited(cs.Node) == false) { Viol("common scene " + cs.Node + " not marked visited after playing (would loop)"); break; }
                if (rc && RouteFlow.AcceptDialogueRequest(S)) MaybeLock();
            }
            if (guard >= 12) Viol("common-scene loop did not settle in slot " + slot);
            _slot = prev;
        }

        // ------------------------------------------------------------ day loop
        private void Morning()
        {
            int guard = 0;
            _slot = "morning";
            while (guard++ < 6)
            {
                ChapterStep step = ChapterFlow.NextMorningStep(S.Chapter, S.DayInChapter, C.ChapterIntroPending, D.HasNode, HasVisited);
                if (step.Kind == ChapterStepKind.None) break;
                if (step.Kind == ChapterStepKind.Outro) { PlayNode(step.Node, null, null, null); continue; }
                if (step.Kind == ChapterStepKind.Intro)
                {
                    bool rc = PlayNode(step.Node, null, null, null);
                    C.AckChapterIntro();
                    if (rc && RouteFlow.AcceptDialogueRequest(S)) MaybeLock();
                    continue;
                }
                if (step.Kind == ChapterStepKind.InfoCard) { C.AckChapterIntro(); continue; }
            }
            if (guard >= 6) Viol("chapter flow loop did not settle");
            CommonScenes(SceneSlot.Morning);
            _slot = "morning";
            if (!P.NeverUpgrade) { int g2 = 0; float cost; while (C.CanUpgradeShop(out cost) && g2++ < 10) C.TryUpgradeShop(); }
        }

        private void Reload()
        {
            // Mirrors AppContext.SaveGame + LoadLatest.
            D.Runner.ExportVisited();
            S.VisitedNodes.Clear(); S.VisitedNodes.AddRange(D.ExportVisited());
            SaveData sd = SaveMapper.ToSave(S, 1, "qa");
            _svc.Save(1, sd);
            LoadResult lr = _svc.Load(1);
            if (lr.Status != LoadStatus.Ok) { Viol("save/load failed mid-run: " + lr.Status + " " + lr.Detail); return; }
            GameState t = SaveMapper.FromSave(lr.Data);
            string diff = SaveProps.Diff(S, t);
            if (diff != null && diff != "Phase") Viol("mid-run save/load mismatch: " + diff);
            // pre-load facts that legitimately differ: nothing besides Phase (already Morning)
            List<string> visited = D.ExportVisited();
            GameState old = S;
            S = t;
            Build();
            D.ImportVisited(t.VisitedNodes);
            C.Resume();
            // frozen snapshot stays valid; flag snapshot reset
            SnapFlags();
            if (S.HasRouteLock != old.HasRouteLock) Viol("lock lost across save/load");
            List<string> a = old.LockedIds(), b2 = S.LockedIds();
            if (!a.SequenceEqualStr(b2)) Viol("lock members changed across save/load");
            Res.Notes.Add("reloaded at day " + S.Day);
        }

        public RunResult Run()
        {
            C.Begin();
            SnapFlags();
            int guard = 0;
            while (!S.Finished && guard++ < 400)
            {
                int dayStart = S.Day;
                PhaseOrder(DayPhase.Settlement, DayPhase.Morning);
                Invariants("morning");
                // adversarial: wrong-phase calls must be refused without side effects
                if (guard % 7 == 3) WrongPhaseProbe();
                Morning();
                Invariants("after morning scenes");
                if (P.SaveLoadMid && Rng.Next(100) < 9) Reload();
                Res.DaysByChapter[S.Chapter]++;
                Res.LevelByChapter[S.Chapter] = S.ShopLevel;
                DayResult dr = C.RunBusiness();
                PhaseOrder(DayPhase.Morning, DayPhase.Business);
                Res.GrossByChapter[S.Chapter] += dr.Gross;
                if (S.Fatigue > Res.MaxFatigue) Res.MaxFatigue = S.Fatigue;
                C.BeginNight();
                PhaseOrder(DayPhase.Business, DayPhase.Night);
                if (S.Fatigue >= B.P("fatigue_limit")) { Res.LimitedDays++; if (S.ActionsLeft != 1) Viol("fatigue limit did not reduce actions"); }
                Invariants("night start");
                SnapFlags();
                // night
                CommonScenes(SceneSlot.Night);
                MaybeLock();
                PlayExtras();
                // QA-08: fatigue-limited night -> take the free (action-neutral) rest first, like a player who reads the fatigue bar
                if (C.FreeRestAvailable && !P.Idle && !P.NoFreeRest) { int a0 = S.ActionsLeft; C.DoNightAction(NightAction.Rest); if (S.ActionsLeft != a0) Viol("free rest consumed an action"); }
                int guard2 = 0;
                while (S.ActionsLeft > 0 && guard2++ < 6 && !P.Idle)
                {
                    int left = S.ActionsLeft;
                    SnapFlags();
                    ChooseAction();
                    if (S.ActionsLeft >= left && !(P.Chaos)) { Res.Notes.Add("action did not consume: loop break"); break; }
                    if (S.ActionsLeft > left) Viol("ActionsLeft increased during night");
                    Invariants("after action");
                    if (Rng.Next(6) == 0) PlayExtras();
                }
                if (S.ActionsLeft > 0 && !P.Idle) Res.ActionsWasted++;
                CommonScenes(SceneSlot.NightEnd);
                if (P.Chaos && Rng.Next(4) == 0) MaybeLock();
                C.Settle();
                PhaseOrder(DayPhase.Night, DayPhase.Settlement);
                Invariants("settled");
                C.NextDay();
                if (!S.Finished && S.Day != dayStart + 1) Viol("day did not advance by 1");
                Res.Days++;
            }
            if (!S.Finished) Viol("game did not finish in 400 iterations");
            if (Res.Days != 64) Viol("game lasted " + Res.Days + " days (expected 64)");
            Ending();
            VerifyScenes();
            Res.Final = S;
            foreach (KeyValuePair<string, int> p in S.Flags) if (p.Value != 0) Res.Flags.Add(p.Key);
            foreach (string vn in D.ExportVisited()) Res.Visited.Add(vn);
            return Res;
        }

        private int FirstIdx(string node)
        {
            for (int i = 0; i < Res.SceneLog.Count; i++) if (Res.SceneLog[i].Node == node) return i;
            return -1;
        }

        /// <summary>Independent check of scene_rules.csv + docs/12 section 5-1 against what the sim actually saw.</summary>
        private void VerifyScenes()
        {
            if (!UseSceneGate || N.Gate == null) return;
            foreach (SceneRule r in N.Gate.Table.Rules)
            {
                bool exists = D.HasNode(r.Node);
                int i = FirstIdx(r.Node);
                if (!exists) continue;
                if (i < 0)
                {
                    if (r.AutoPlay) Viol("auto_play scene never played: " + r.Node);
                    continue;
                }
                SceneLogEntry le = Res.SceneLog[i];
                if (r.Chapter > 0 && r.Kind != "chapter")
                {
                    if (le.Chapter < r.Chapter) Viol(r.Node + " played in chapter " + le.Chapter + " < rule chapter " + r.Chapter);
                    else if (le.Chapter == r.Chapter && le.DayInChapter < r.EffectiveMinDay) Viol(r.Node + " played on day-in-chapter " + le.DayInChapter + " < min " + r.EffectiveMinDay);
                    if (r.AutoPlay && (le.Chapter > r.Chapter || le.DayInChapter > r.TriggerDayMax + 1)) Res.Notes.Add("late auto scene " + r.Node + " (window " + r.TriggerDayMin + "-" + r.TriggerDayMax + ", played ch" + le.Chapter + " d" + le.DayInChapter + ")");
                }
                foreach (SceneRef a in r.After)
                {
                    int ai = FirstIdx(a.Node);
                    if (ai >= 0 && ai > i) { if (a.Soft) Res.Notes.Add("soft after_node order broken: " + r.Node + " played before ~" + a.Node + " (allowed once the auto window deadline passes)"); else Viol(r.Node + " played BEFORE its after_node " + a.Node); }
                    if (ai < 0 && !a.Soft && D.HasNode(a.Node)) Viol(r.Node + " played without hard after_node " + a.Node);
                }
                foreach (string bn in r.Before)
                {
                    int bi = FirstIdx(bn);
                    if (bi >= 0 && bi < i) Viol(r.Node + " played AFTER before_node " + bn);
                }
            }
            // docs/12 5-1 explicit orders
            string[][] chains = {
                new string[] { "Ch5_Show", "Johan_E3", "Nabi_E3", "Ch5_Show_After", "Howl_E3" },
                new string[] { "Johan_E1", "CE02_Tasting" },
                new string[] { "CE06_OldPhoto", "Gomseok_E4" },
                new string[] { "Ch6_LedgerTruth", "Ch6_Outro" },
            };
            foreach (string[] chain in chains)
            {
                int last = -1; string lastN = null;
                foreach (string n in chain)
                {
                    int ix = FirstIdx(n);
                    if (ix < 0) continue;
                    if (ix < last) Res.Notes.Add("docs/12 5-1 order broken: " + n + " played before " + lastN);
                    last = ix; lastN = n;
                }
            }
        }

        private void WrongPhaseProbe()
        {
            // At Morning: every night action must be refused and nothing may change.
            string snap = Snapshot();
            ActionResult[] rs = { R.Talk("howl"), R.Gift("howl", "small_plant"), R.Date("howl", "beach"), R.CompleteEventResult("howl", 1) };
            foreach (ActionResult r in rs) if (r.Success || r.Reason != RomanceRefusal.WrongPhase) Viol("night action in " + S.Phase + " was not refused with wrong_phase: " + r.Kind + " " + r.Reason);
            if (C.DoNightAction(NightAction.Rest)) Viol("DoNightAction succeeded in " + S.Phase);
            try { C.BeginNight(); Viol("BeginNight allowed in Morning"); } catch (InvalidOperationException) { }
            try { C.Settle(); Viol("Settle allowed in Morning"); } catch (InvalidOperationException) { }
            try { C.NextDay(); Viol("NextDay allowed in Morning"); } catch (InvalidOperationException) { }
            if (Snapshot() != snap) Viol("wrong-phase probe mutated state");
        }

        private string Snapshot()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(S.Gold).Append('|').Append(S.Fatigue).Append('|').Append(S.ActionsLeft).Append('|').Append(S.Phase);
            foreach (string id in CharIds.All) { CharState c = S.Chars[id]; sb.Append('|').Append(c.Aff).Append(',').Append(c.Trust).Append(',').Append(c.EventsDone).Append(',').Append(c.LastGiftDay).Append(',').Append(c.LastDateDay); }
            return sb.ToString();
        }

        // ------------------------------------------------------------ ending
        private void Ending()
        {
            EndingPlan plan = EndingPlan.Create(S, W.ET, W.Cat, D.HasNode);
            Res.EndingId = plan.Result.Id + (plan.Result.PrimaryChar.Length > 0 && !plan.Result.IsHarem && !plan.Result.IsCommunity ? ":" + plan.Result.PrimaryChar : "");
            Res.EndingNode = plan.IntendedNode;
            Res.Members = plan.Result.Members.ToArray();
            Res.EndingNodeOk = plan.NodeName != null;
            if (plan.NodeName == null) Viol("ending node missing: " + plan.IntendedNode);
            List<SceneItem> scenes = new List<SceneItem>();
            string outro = ChapterFlow.FinalOutro(B.MaxChapter, D.HasNode, HasVisited);
            if (outro != null) scenes.Add(SceneItem.Of(outro, true, string.Empty));
            scenes.AddRange(EndingFlow.Scenes(plan, D.HasNode));
            HashSet<string> shown = new HashSet<string>();
            foreach (SceneItem si in scenes)
            {
                if (si.Node == null) { Viol("ending scene fallback used: " + si.IntendedNode); continue; }
                int before = Sink.Cgs.Count;
                PlayNode(si.Node, null, null, null);
                for (int i = before; i < Sink.Cgs.Count; i++) shown.Add(Sink.Cgs[i]);
            }
            foreach (string cg in plan.CgIds) if (!shown.Contains(cg)) Viol("ending " + plan.Result.Id + " promised CG " + cg + " but scenes never showed it");
            // mid-ending: every member of a harem ending speaks/appears
            if (plan.Result.IsHarem)
                foreach (string m in plan.Result.Members)
                    if (!Sink.Standing.Contains(m)) Res.Notes.Add("harem member never shown on stage in whole run: " + m);
        }
    }
}
