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
    /// <summary>How much aff/trust do scripts themselves hand out (bypassing the actions.csv model)? per node kind, min/avg/max over all choice paths.</summary>
    public static class NodeGain
    {
        public static void Run(World w)
        {
            Report.Header("SCRIPT-GRANTED AFFECTION (dialogue <<aff>>/<<trust>> on top of actions.csv)");
            BalanceTable b = w.Balance(Difficulty.Normal);
            foreach (string kind in new string[] { "Talk", "Gift_Like", "Gift_Neutral", "Gift_Dislike", "Date", "E" })
            {
                double sumA = 0, sumT = 0, maxA = -999, maxT = -999, minA = 999, minT = 999; int n = 0;
                string maxNode = ""; 
                foreach (string id in CharIds.All)
                {
                    foreach (DialogueNode node in w.Proj.AllNodes)
                    {
                        string t = node.Title;
                        string T = NightRomanceActions.CharTitle(id) + "_";
                        bool match = false;
                        if (kind == "Talk") match = t.StartsWith(T + "Talk_");
                        else if (kind == "Date") match = t.StartsWith(T + "Date_");
                        else if (kind == "E") match = System.Text.RegularExpressions.Regex.IsMatch(t, "^" + T + "E[1-5]$");
                        else match = t == T + kind;
                        if (!match) continue;
                        GameState s = Content.Fresh(w, b, 5, "neutral");
                        s.Chars[id].Aff = 30; s.Chars[id].Trust = 10;
                        Dictionary<string, DialogueValue> temps = new Dictionary<string, DialogueValue>();
                        temps["gift_name"] = DialogueValue.FromString("x"); temps["spot_name"] = DialogueValue.FromString("x"); temps["spot_id"] = DialogueValue.FromString("beach");
                        List<Runner.PathOutcome> outs = Runner.EnumeratePaths(w, b, s, new List<string>(), t, temps, 300);
                        foreach (Runner.PathOutcome po in outs)
                        {
                            double da = po.End.Chars[id].Aff - 30, dt = po.End.Chars[id].Trust - 10;
                            sumA += da; sumT += dt; n++;
                            if (da > maxA) { maxA = da; maxNode = t; }
                            if (dt > maxT) maxT = dt;
                            if (da < minA) minA = da; if (dt < minT) minT = dt;
                        }
                    }
                }
                Report.Info(string.Format("  {0,-13} paths={1,4}  aff: avg {2:F2} min {3:F1} max {4:F1} ({5})   trust: avg {6:F2} min {7:F1} max {8:F1}", kind, n, sumA / Math.Max(1, n), minA, maxA, maxNode, sumT / Math.Max(1, n), minT, maxT));
            }
            Report.Info("-- per-node outliers: max script-granted gain over all choice paths (aff>10 or trust>4, or aff<-6) --");
            foreach (DialogueNode node in w.Proj.AllNodes)
            {
                string t = node.Title;
                string owner = null;
                foreach (string id in CharIds.All) if (t.StartsWith(NightRomanceActions.CharTitle(id) + "_")) owner = id;
                if (owner == null) continue;
                GameState s0 = Content.Fresh(w, b, 5, "neutral");
                s0.Chars[owner].Aff = 30; s0.Chars[owner].Trust = 10;
                Dictionary<string, DialogueValue> tp = new Dictionary<string, DialogueValue>();
                tp["gift_name"] = DialogueValue.FromString("x"); tp["spot_name"] = DialogueValue.FromString("x"); tp["spot_id"] = DialogueValue.FromString("beach");
                List<Runner.PathOutcome> os2 = Runner.EnumeratePaths(w, b, s0, new List<string>(), t, tp, 300);
                double ma = -999, mt = -999, na = 999;
                foreach (Runner.PathOutcome po in os2) { double da = po.End.Chars[owner].Aff - 30, dt = po.End.Chars[owner].Trust - 10; if (da > ma) ma = da; if (dt > mt) mt = dt; if (da < na) na = da; }
                if (ma > 10 || mt > 4 || na < -6) Report.Info("  " + t + ": max aff +" + ma.ToString("F1") + " max trust +" + mt.ToString("F1") + " min aff " + na.ToString("F1") + " (" + os2.Count + " paths)");
            }
            TalkModel(w, b);
            ActionRow talk = b.Actions["talk"], cs = b.Actions["choice_success"], gl = b.Actions["gift_like"], dt2 = b.Actions["date"], ev = b.Actions["event"];
            Report.Info(string.Format("  model (actions.csv, before soft_diminish): talk +{0}/+{1}, choice_success +{2}/+{3}, gift_like +{4}, date +{5}/+{6}, event +{7}/+{8}", talk.AffGain, talk.TrustGain, cs.AffGain, cs.TrustGain, gl.AffGain, dt2.AffGain, dt2.TrustGain, ev.AffGain, ev.TrustGain));
        }

        /// <summary>Monte-Carlo: play a node n times with uniformly random options at raw aff 0 (no soft_diminish) and report mean script-granted gain.</summary>
        private static void Sample(World w, BalanceTable b, string id, string node, int n, Random rng, float startAff, float startTrust, int eventsDone, out double aff, out double trust, out double points)
        {
            aff = trust = points = 0;
            for (int i = 0; i < n; i++)
            {
                GameState s = Content.Fresh(w, b, 8, "neutral");
                s.Chars[id].Aff = startAff; s.Chars[id].Trust = startTrust; s.Chars[id].EventsDone = eventsDone; // events: the night menu completes the step before the node plays (end_event is then a no-op) // away from the 0 floor so negative options are not clamped away
                RomanceService r = Runner.NewRomance(w, s, b, delegate () { return 0.5; });
                RecSink sink = new RecSink();
                DialogueSession d = Runner.NewSession(w, s, r, sink);
                int picks = 0;
                Func<DialogueStep, int> inner = Runner.PickRandom(rng);
                Func<DialogueStep, int> pick = delegate (DialogueStep st) { picks++; return inner(st); };
                Runner.Play(d, sink, node, pick, new Dictionary<string, DialogueValue>(), null, null);
                aff += s.Chars[id].Aff - startAff; trust += s.Chars[id].Trust - startTrust; points += picks;
            }
            aff /= n; trust /= n; points /= n;
        }

        /// <summary>
        /// QA-05 model constants measured from the real nodes (raw aff 0, uniformly random options): choice points per Talk node and the mean script-granted aff/trust.
        /// docs/06 section 2: a Talk action = talk.aff_gain (service, base) + the node's own choice rewards (script). The same numbers (params.csv talk_* / event_script_*)
        /// are used by tools/simulate_balance.py, which models the random-option reference player of this harness.
        /// </summary>
        private static void TalkModel(World w, BalanceTable b)
        {
            Random rng = new Random(4242);
            // soft_diminish off so the numbers are raw script values
            float soft = b.Params["soft_diminish"]; b.Params["soft_diminish"] = 0f;
            try { TalkModelCore(w, b, rng); } finally { b.Params["soft_diminish"] = soft; }
        }

        private static void TalkModelCore(World w, BalanceTable b, Random rng)
        {
            double ta = 0, tt = 0, tp = 0, tn = 0;
            foreach (string id in CharIds.All)
                foreach (DialogueNode node in w.Proj.AllNodes)
                {
                    if (!node.Title.StartsWith(NightRomanceActions.CharTitle(id) + "_Talk_")) continue;
                    double a, t, p; Sample(w, b, id, node.Title, 60, rng, 50f, 20f, 0, out a, out t, out p);
                    ta += a; tt += t; tp += p; tn++;
                }
            Report.Info(string.Format("  TALK-MODEL ({0} Talk nodes, random options, raw script values): choice points per node {1:F2}; script aff {2:F2} trust {3:F2} per talk  (right option +5/+1.5 = 36%, fail -1/-1 = 31%, neutral 0 = 33% of the 242 options)", tn, tp / tn, ta / tn, tt / tn));
            // QA-05 regression: the service grants ONLY the base (no random +5/-1 roll on top of the node's own choice rewards)
            {
                int bad = 0; double sum = 0; int n = 600; Random r2 = new Random(99);
                for (int i = 0; i < n; i++)
                {
                    GameState s = Content.Fresh(w, b, 8, "neutral"); s.Day = 40; s.Phase = MorpheHouse.Core.DayPhase.Night; s.ActionsLeft = 2;
                    RomanceService svc = new RomanceService(s, b, w.Cat, r2.NextDouble);
                    ActionResult res = svc.Talk("howl");
                    sum += res.AffDelta;
                    float m = b.P("romance_gain_mult") * b.GainMultOf("howl"); // difficulty x character multiplier (1.0 x 1.0 for the base rows when mult = 1)
                    if (!res.Success || res.ChoiceOffered || res.ChoiceSucceeded || Math.Abs(res.AffDelta - b.Actions["talk"].AffGain * m) > 1e-4 || Math.Abs(res.TrustDelta - b.Actions["talk"].TrustGain * m) > 1e-4) bad++;
                }
                Report.Check("QA-05: RomanceService.Talk = base talk row only (x gain multipliers), no random choice roll (dialogue nodes own choice rewards)", bad == 0, bad + "/" + n + " talks deviated from the base +" + b.Actions["talk"].AffGain + " (mean " + (sum / n).ToString("F3") + ")");
            }
            ActionRow tk = b.Actions["talk"];
            Report.Info(string.Format("  TALK-MODEL => one Talk action = base {0:F1}/{1:F1} (RomanceService, diminished) + script {2:F2}/{3:F2} (AdjustAffection, diminished aff) = {4:F2} aff before diminish (old double-counted model: {5:F2})", tk.AffGain, tk.TrustGain, ta / tn, tt / tn, tk.AffGain + ta / tn, 2.6));
            for (int step = 1; step <= 5; step++)
            {
                double ea = 0, et = 0, en = 0;
                foreach (string id in CharIds.All)
                {
                    string node = NightRomanceActions.EventNodeName(id, step);
                    if (!w.Proj.Contains(node)) continue;
                    double a, t, p; Sample(w, b, id, node, 60, rng, b.Events[step - 1].AffMin, b.Events[step - 1].TrustMin, step, out a, out t, out p);
                    ea += a; et += t; en++;
                }
                Report.Info(string.Format("  EVENT-MODEL (at the step\'s own aff_min/trust_min) E{0}: script raw aff {1:F2} trust {2:F2} (mean over {3} characters, random options) + events.csv {4}/{5}", step, ea / Math.Max(1, en), et / Math.Max(1, en), en, b.Events[step - 1].AffGain, b.Events[step - 1].TrustGain));
            }
        }
    }
}
