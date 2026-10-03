using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using MorpheHouse.Save;
using MorpheHouse.Tycoon;

namespace QaHunt
{
    /// <summary>All content loaded once (balance csv, real .yarn scripts, address book).</summary>
    public sealed class World
    {
        public string BalDir;
        public string YarnDir;
        public RomanceCatalog Cat;
        public EndingTable ET;
        public DialogueProject Proj;
        public AddressBook Book;
        public List<string> LoadErrors = new List<string>();
        public int FileCount;

        public static World Load(string balDir, string yarnDir, string addrCsv)
        {
            World w = new World();
            w.BalDir = balDir;
            w.YarnDir = yarnDir;
            FileTextSource src = new FileTextSource(balDir);
            w.Cat = RomanceCatalog.Load(src);
            w.ET = EndingTable.Load(src);
            string[] files = Directory.GetFiles(yarnDir, "*.yarn", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            List<KeyValuePair<string, string>> srcs = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < files.Length; i++)
            {
                string rel = files[i].Substring(yarnDir.Length).TrimStart('/', '\\');
                srcs.Add(new KeyValuePair<string, string>(DialogueProjectLoader.FlattenName(rel), File.ReadAllText(files[i])));
            }
            DialogueLoadResult res = DialogueProjectLoader.Build(srcs);
            w.Proj = res.Project;
            w.LoadErrors.AddRange(res.Errors);
            w.FileCount = res.FileCount;
            w.Book = AddressBook.FromCsv(File.ReadAllText(addrCsv));
            return w;
        }

        public BalanceTable Balance(Difficulty d)
        {
            return BalanceTable.Load(new FileTextSource(BalDir), d);
        }
    }

    /// <summary>Records what a scene asked the VN stage to show.</summary>
    public sealed class RecSink : IVnCommandSink
    {
        public List<string> Cgs = new List<string>();
        public HashSet<string> Standing = new HashSet<string>();
        public int Bg;
        public void ShowStanding(string c, string e, string o) { Standing.Add(c); }
        public void HideStanding(string c) { }
        public void ShowCg(string c, int n) { Cgs.Add((c ?? "").ToUpperInvariant() + "_E" + n); }
        public void ShowCommonCg(string k) { Cgs.Add(k); }
        public void HideCg() { }
        public void SetBackground(string p, string t) { Bg++; }
    }

    public sealed class XmlSer : ISerializer
    {
        private readonly XmlSerializer _x = new XmlSerializer(typeof(SaveData));
        public string ToJson(SaveData d) { using (StringWriter w = new StringWriter()) { _x.Serialize(w, d); return w.ToString(); } }
        public SaveData FromJson(string s) { using (StringReader r = new StringReader(s)) { return (SaveData)_x.Deserialize(r); } }
    }

    public sealed class MemStorage : ISaveStorage
    {
        public Dictionary<string, string> Files = new Dictionary<string, string>();
        public Dictionary<string, string> Baks = new Dictionary<string, string>();
        public void Write(string name, string text) { string o; if (Files.TryGetValue(name, out o)) Baks[name] = o; Files[name] = text; }
        public string Read(string name) { string o; return Files.TryGetValue(name, out o) ? o : null; }
        public string ReadBackup(string name) { string o; return Baks.TryGetValue(name, out o) ? o : null; }
        public bool Exists(string name) { return Files.ContainsKey(name) || Baks.ContainsKey(name); }
    }

    public sealed class Report
    {
        public static int Checks;
        public static int Fails;
        public static List<string> Findings = new List<string>();
        public static void Check(string name, bool ok, string detail = null)
        {
            Checks++;
            if (!ok)
            {
                Fails++;
                string line = "  [FAIL] " + name + (detail != null ? " :: " + detail : "");
                Console.WriteLine(line);
                Findings.Add(line);
            }
        }
        public static void Info(string s) { Console.WriteLine(s); }
        public static void Header(string s) { Console.WriteLine(); Console.WriteLine("=== " + s + " ==="); }
    }
}
