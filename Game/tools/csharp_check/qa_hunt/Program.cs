using System;
using System.Collections.Generic;
using MorpheHouse.Data;

namespace QaHunt
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            string bal = args.Length > 0 ? args[0] : "balance";
            string yarn = args.Length > 1 ? args[1] : "UnityProject/Assets/_Project/Dialogue";
            string addr = args.Length > 2 ? args[2] : "UnityProject/Assets/_Project/Resources/Dialogue/address_terms.csv";
            List<string> modes = new List<string>();
            for (int i = 3; i < args.Length; i++) modes.Add(args[i]);
            if (modes.Count == 0 || modes.Contains("all")) { modes.Clear(); modes.AddRange(new string[] { "content", "save", "ending", "flags", "gain", "timing", "fuzz", "dead", "play", "fatigue" }); }
            World w = World.Load(bal, yarn, addr);
            foreach (string m in modes)
            {
                if (m == "content") Content.Run(w, yarn);
                else if (m == "save") SaveProps.Run(w);
                else if (m == "ending") EndingProps.Run(w);
                else if (m == "flags") FlagReach.Run(w);
                else if (m == "play") Play.Run(w, false);
                else if (m == "fatigue") Play.Run(w, true);
                else if (m == "gain") NodeGain.Run(w);
                else if (m == "timing") Timing.Run(w);
                else if (m == "fuzz") Fuzz.Run(w);
                else if (m == "dead") DeadBranch.Run(w);
                else Console.WriteLine("unknown mode " + m);
            }
            Console.WriteLine();
            Console.WriteLine("checks=" + Report.Checks + " failures=" + Report.Fails);
            return Report.Fails == 0 ? 0 : 1;
        }
    }
}
