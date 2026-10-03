using System;
using System.Collections.Generic;
using System.Text;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using MorpheHouse.Tycoon;

namespace QaHunt
{
    public sealed class SceneResult
    {
        public string Node;
        public List<string> Lines = new List<string>();
        public HashSet<string> Speakers = new HashSet<string>();
        public List<string> Cgs = new List<string>();
        public HashSet<string> Standing = new HashSet<string>();
        public List<string> Errors = new List<string>();
        public List<int> Choices = new List<int>();
        public bool Ended;
        public bool RouteChoice;
        public bool UsedFallback;
        public int Steps;
        public List<string> NodesEntered = new List<string>();
    }

    public static class Runner
    {
        public static GameState Clone(GameState s)
        {
            GameState c = new GameState();
            c.Difficulty = s.Difficulty; c.PlayerName = s.PlayerName; c.Gender = s.Gender; c.PlayerSpecies = s.PlayerSpecies;
            c.PlayerAppearance = s.PlayerAppearance; c.Chapter = s.Chapter; c.Day = s.Day; c.DayInChapter = s.DayInChapter;
            c.Phase = s.Phase; c.Finished = s.Finished; c.Gold = s.Gold; c.Reputation = s.Reputation; c.Fatigue = s.Fatigue;
            c.ShopLevel = s.ShopLevel; c.ActionsLeft = s.ActionsLeft; c.SatisfactionBonus = s.SatisfactionBonus;
            c.LockedRoute = s.LockedRoute;
            foreach (string g in s.LockedGroup) c.LockedGroup.Add(g);
            c.PlayTimeSec = s.PlayTimeSec;
            foreach (KeyValuePair<string, CharState> p in s.Chars)
            {
                CharState n = new CharState();
                n.Aff = p.Value.Aff; n.Trust = p.Value.Trust; n.EventsDone = p.Value.EventsDone;
                n.LastGiftDay = p.Value.LastGiftDay; n.LastDateDay = p.Value.LastDateDay; n.LastContactDay = p.Value.LastContactDay;
                c.Chars[p.Key] = n;
            }
            foreach (KeyValuePair<string, int> p in s.Flags) c.Flags[p.Key] = p.Value;
            foreach (string v in s.VisitedNodes) c.VisitedNodes.Add(v);
            return c;
        }

        public static DialogueSession NewSession(World w, GameState s, RomanceService r, RecSink sink)
        {
            return new DialogueSession(w.Proj, s, r, w.Book, "ko", sink);
        }

        public static RomanceService NewRomance(World w, GameState s, BalanceTable b, Func<double> rng)
        {
            return new RomanceService(s, b, w.Cat, rng);
        }

        /// <summary>Plays one node to its End the way DialogueDirector does (temps, fallback, errors).</summary>
        public static SceneResult Play(DialogueSession d, RecSink sink, string node, Func<DialogueStep, int> pick,
            Dictionary<string, DialogueValue> temps, string fbSpeaker, string fbText)
        {
            SceneResult res = new SceneResult();
            res.Node = node;
            d.Vars.ClearTemps();
            if (temps != null) foreach (KeyValuePair<string, DialogueValue> p in temps) d.SetTemp(p.Key, p.Value);
            int cgBefore = sink.Cgs.Count;
            d.Start(node, fbSpeaker, fbText);
            res.UsedFallback = d.UsingFallback;
            for (int i = 0; i < 5000; i++)
            {
                DialogueStep step = d.Next();
                res.Steps++;
                if (step.IsEnd) { res.Ended = true; break; }
                if (step.IsLine)
                {
                    res.Lines.Add((step.Speaker ?? "-") + ": " + step.Text);
                    if (step.Speaker != null) res.Speakers.Add(step.Speaker);
                }
                else if (step.IsCommand)
                {
                    res.Errors.Add("UNHANDLED COMMAND " + step.CommandName);
                }
                else if (step.IsChoices)
                {
                    int idx = pick(step);
                    if (idx < 0) { res.Errors.Add("no available option at line " + step.SourceLine); break; }
                    res.Choices.Add(idx);
                    d.Choose(idx);
                }
            }
            d.Finish();
            for (int i = 0; i < d.Errors.Count; i++) res.Errors.Add(d.Errors[i]);
            d.Errors.Clear();
            res.RouteChoice = d.ConsumeRouteChoice();
            for (int i = cgBefore; i < sink.Cgs.Count; i++) res.Cgs.Add(sink.Cgs[i]);
            foreach (string st in sink.Standing) res.Standing.Add(st);
            return res;
        }

        public static int PickFirst(DialogueStep s)
        {
            for (int k = 0; k < s.Options.Count; k++) if (s.Options[k].Available) return s.Options[k].Index;
            return -1;
        }

        public static int PickLast(DialogueStep s)
        {
            for (int k = s.Options.Count - 1; k >= 0; k--) if (s.Options[k].Available) return s.Options[k].Index;
            return -1;
        }

        public static Func<DialogueStep, int> PickRandom(Random rng)
        {
            return delegate (DialogueStep s)
            {
                List<int> av = new List<int>();
                for (int k = 0; k < s.Options.Count; k++) if (s.Options[k].Available) av.Add(s.Options[k].Index);
                return av.Count == 0 ? -1 : av[rng.Next(av.Count)];
            };
        }

        public static Func<DialogueStep, int> PickSeq(List<int> seq)
        {
            int pos = 0;
            return delegate (DialogueStep s)
            {
                if (pos < seq.Count) return seq[pos++];
                return PickFirst(s);
            };
        }

        /// <summary>
        /// Enumerates every choice path of a node on a CLONE of the state (so nothing leaks). Returns per-path final state.
        /// </summary>
        public sealed class PathOutcome
        {
            public List<int> Seq = new List<int>();
            public GameState End;
            public SceneResult Scene;
        }

        public static List<PathOutcome> EnumeratePaths(World w, BalanceTable b, GameState baseState, List<string> visited,
            string node, Dictionary<string, DialogueValue> temps, int cap)
        {
            List<PathOutcome> outs = new List<PathOutcome>();
            Enum(w, b, baseState, visited, node, temps, new List<int>(), outs, cap);
            return outs;
        }

        private static void Enum(World w, BalanceTable b, GameState baseState, List<string> visited, string node,
            Dictionary<string, DialogueValue> temps, List<int> prefix, List<PathOutcome> outs, int cap)
        {
            if (outs.Count >= cap) return;
            GameState c = Clone(baseState);
            RomanceService r = NewRomance(w, c, b, delegate () { return 0.5; });
            RecSink sink = new RecSink();
            DialogueSession d = NewSession(w, c, r, sink);
            d.ImportVisited(visited);
            List<int> branch = null;
            List<int> used = new List<int>();
            int depth = 0;
            Func<DialogueStep, int> pick = delegate (DialogueStep s)
            {
                if (depth < prefix.Count) { int v = prefix[depth++]; used.Add(v); return v; }
                // new branching point
                List<int> av = new List<int>();
                for (int k = 0; k < s.Options.Count; k++) if (s.Options[k].Available) av.Add(s.Options[k].Index);
                branch = av;
                return -1; // abort this run; recurse below
            };
            SceneResult sr = Play(d, sink, node, pick, temps, null, null);
            if (branch != null)
            {
                for (int i = 0; i < branch.Count && outs.Count < cap; i++)
                {
                    List<int> next = new List<int>(prefix);
                    next.Add(branch[i]);
                    Enum(w, b, baseState, visited, node, temps, next, outs, cap);
                }
                return;
            }
            PathOutcome po = new PathOutcome();
            po.Seq = new List<int>(prefix);
            po.End = c;
            po.Scene = sr;
            outs.Add(po);
        }
    }
}
