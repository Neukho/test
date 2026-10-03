using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Save;

// 보안 자가 테스트(퍼징 하네스). 빌드/실행: sh tools/security_selftest/run.sh
//
// 범위·정직성 메모
//  - 고정 시드(Seed)로 결정적이다. 같은 시드 → 같은 입력 → 같은 결과(스레드 타임아웃 경계만 환경 영향).
//  - Unity 의 JsonUtility 는 Unity 밖에서 쓸 수 없으므로 두 가지 대역(JsonSer: MiniJson 기반, XmlSer: XmlSerializer)을 쓴다.
//    따라서 "파서 단계" 결과는 대역에 대한 것이고, SaveService/SaveValidator/SaveIntegrity/FileSaveStorage/YarnParser/
//    DialogueRunner/MiniJson/ProfileService/SettingsService 는 실제 소스를 그대로 컴파일해 시험한 것이다.
//  - 종료 코드: 0 = 모든 단언 통과(관찰/발견 사항은 별도 보고), 1 = 단언 실패, 2 = 하네스 자체 오류.
public static class Program
{
    private const int Seed = 20260101;
    private const int Iterations = 3000;     // 캠페인당 반복 수(모두 >= 2000)
    private const int TimeLimitMs = 3000;    // 케이스당 시간 제한(= hang 판정)

    private static readonly List<string> Failures = new List<string>();      // 고유 메시지(처음 나온 순서)
    private static readonly Dictionary<string, int> FailureCounts = new Dictionary<string, int>();
    private static int _totalFailures;
    private static readonly List<string> Observations = new List<string>();
    private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> Fails = new Dictionary<string, int>();
    private static string _dialogueDir = "UnityProject/Assets/_Project/Dialogue";
    private static string _exe = "";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length >= 3 && args[0] == "probe") return Probe(args[1], int.Parse(args[2]));
        if (args.Length > 0) _dialogueDir = args[0];
        _exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
        Stopwatch total = Stopwatch.StartNew();
        Console.WriteLine("security_selftest seed={0} iterations/campaign={1} time_limit_ms={2}", Seed, Iterations, TimeLimitMs);
        try
        {
            CampaignYarnMutation();
            CampaignYarnTargeted();
            CampaignSaveMutation();
            CampaignSaveSigning();
            CampaignSaveForged();
            DirectValidatorNullChecks();
            CampaignSaveRoundtripAndRefuse();
            CampaignStoragePaths();
            CampaignJsonProfileSettings();
            CampaignNameValidator();
            CampaignOversizedFiles();
            CampaignSubprocessProbes();
            CampaignRegressions();
            CampaignReviewRegressions();
        }
        catch (Exception ex)
        {
            Console.WriteLine("HARNESS ERROR: " + ex);
            return 2;
        }
        total.Stop();

        Console.WriteLine();
        Console.WriteLine("==== 요약 ====");
        int sum = 0;
        foreach (KeyValuePair<string, int> kv in Counts)
        {
            int f;
            Fails.TryGetValue(kv.Key, out f);
            Console.WriteLine("{0,-34} iterations={1,6} assertion_failures={2}", kv.Key, kv.Value, f);
            sum += kv.Value;
        }
        Console.WriteLine("total_iterations_or_cases={0} elapsed_s={1:F1}", sum, total.Elapsed.TotalSeconds);
        Console.WriteLine();
        Console.WriteLine("==== 단언 실패 {0} 건 (고유 {1} 종) ====", _totalFailures, Failures.Count);
        foreach (string s in Failures) Console.WriteLine("  FAIL x" + FailureCounts[s] + " " + s);
        Console.WriteLine();
        Console.WriteLine("==== 관찰/발견 {0} 건(단언이 아닌 보고용) ====", Observations.Count);
        foreach (string s in Observations) Console.WriteLine("  OBS  " + s);
        Console.WriteLine(_totalFailures == 0 ? "SELFTEST: assertions all passed" : "SELFTEST: " + _totalFailures + " assertion failure(s) in " + Failures.Count + " distinct finding(s) (each is a REAL finding in the module under test, see portfolio/SELFTEST_RESULTS.md)");
        return _totalFailures == 0 ? 0 : 1;
    }

    // ======================================================================
    // 공통 도구
    // ======================================================================

    private static void Count(string campaign)
    {
        int n;
        Counts.TryGetValue(campaign, out n);
        Counts[campaign] = n + 1;
    }

    private static void Fail(string campaign, string msg)
    {
        int n;
        Fails.TryGetValue(campaign, out n);
        Fails[campaign] = n + 1;
        _totalFailures++;
        string line = "[" + campaign + "] " + Trunc(msg, 300);
        int c;
        if (FailureCounts.TryGetValue(line, out c)) { FailureCounts[line] = c + 1; return; }
        FailureCounts[line] = 1;
        if (Failures.Count < 200) Failures.Add(line);
    }

    private static void Obs(string msg)
    {
        Observations.Add(Trunc(msg, 400));
    }

    private static string Trunc(string s, int n)
    {
        if (s == null) return "(null)";
        string t = s.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\0", "\\0");
        return t.Length <= n ? t : t.Substring(0, n) + "...(+" + (t.Length - n) + ")";
    }

    /// <summary>작업 스레드에서 실행하고 제한 시간을 넘기면 중단한다(hang 판정). 스택은 크게 잡아 완만한 재귀는 견디게 한다.</summary>
    private static bool Guarded(Action a, int ms, out Exception thrown, out long elapsedMs)
    {
        Exception captured = null;
        Thread t = new Thread(delegate ()
        {
            try { a(); }
            catch (ThreadAbortException) { }
            catch (Exception ex) { captured = ex; }
        }, 64 * 1024 * 1024);
        Stopwatch sw = Stopwatch.StartNew();
        t.Start();
        bool done = t.Join(ms);
        sw.Stop();
        elapsedMs = sw.ElapsedMilliseconds;
        if (!done)
        {
            try { t.Abort(); } catch (Exception) { }
            t.Join(2000);
            thrown = null;
            return false;
        }
        thrown = captured;
        return true;
    }

    private const string YarnTokens = "<<|>>|{|}|->|---|===|\"|\\|#|//|$|\t|\n|\r\n|title: |<<if |<<endif>>|<<else>>|<<elseif |<<jump |<<set $x to |<<stop>>|line:|(|)|!|&&|99999999999999999999999999999999|#line:|    |        |<<set $a += 1>>|\\{|\\}|: ";

    private static string MutateText(string s, Random r, string tokens)
    {
        string[] toks = tokens.Split('|');
        int ops = 1 + r.Next(4);
        for (int k = 0; k < ops; k++)
        {
            int kind = r.Next(9);
            int n = s.Length;
            if (n == 0) { s = toks[r.Next(toks.Length)]; continue; }
            int p = r.Next(n);
            switch (kind)
            {
                case 0: // 구간 삭제
                    { int len = Math.Min(n - p, 1 + r.Next(Math.Max(1, n / 8))); s = s.Remove(p, len); break; }
                case 1: // 구간 복제
                    { int len = Math.Min(n - p, 1 + r.Next(200)); s = s.Insert(p, s.Substring(p, len)); break; }
                case 2: // 토큰 삽입
                    s = s.Insert(p, toks[r.Next(toks.Length)]); break;
                case 3: // 잘라내기(truncate)
                    s = s.Substring(0, p); break;
                case 4: // 임의 문자 치환
                    { char[] c = s.ToCharArray(); c[p] = (char)r.Next(0, 0x2FF); s = new string(c); break; }
                case 5: // 짝 없는 서로게이트/널/제어 문자
                    s = s.Insert(p, new string[] { "\uD800", "\uDFFF", "\0", "\u202E", "\uFEFF", "\u0001", "\uFFFF" }[r.Next(7)]); break;
                case 6: // 아주 긴 반복
                    s = s.Insert(p, new string(toks[r.Next(toks.Length)].Length > 0 ? toks[r.Next(toks.Length)][0] : 'a', 1 + r.Next(3000))); break;
                case 7: // 줄 섞기(두 줄 교환)
                    {
                        string[] lines = s.Split('\n');
                        if (lines.Length > 2)
                        {
                            int a = r.Next(lines.Length), b = r.Next(lines.Length);
                            string tmp = lines[a]; lines[a] = lines[b]; lines[b] = tmp;
                            s = string.Join("\n", lines);
                        }
                        break;
                    }
                default: // 깊은 중첩 삽입
                    s = s.Insert(p, new string(r.Next(2) == 0 ? '{' : '(', 1 + r.Next(40))); break;
            }
        }
        return s;
    }

    /// <summary>바이트 수준 변형 후 UTF-8 로 되돌린다(유효하지 않은 UTF-8 은 U+FFFD 로 치환되어 들어온다).</summary>
    private static string MutateBytes(string s, Random r)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        if (b.Length == 0) return s;
        int flips = 1 + r.Next(6);
        for (int i = 0; i < flips; i++)
        {
            int p = r.Next(b.Length);
            switch (r.Next(3))
            {
                case 0: b[p] = (byte)r.Next(256); break;
                case 1: b[p] = (byte)(0x80 | r.Next(0x40)); break; // 연속 바이트만 단독으로
                default: b[p] ^= (byte)(1 << r.Next(8)); break;
            }
        }
        return Encoding.UTF8.GetString(b);
    }

    // ======================================================================
    // 1) YarnParser / DialogueRunner : 변이 퍼징
    // ======================================================================

    private static List<string> LoadCorpus()
    {
        List<string> corpus = new List<string>();
        foreach (string f in Directory.GetFiles(_dialogueDir, "*.yarn", SearchOption.AllDirectories))
            corpus.Add(File.ReadAllText(f, Encoding.UTF8));
        if (corpus.Count == 0) throw new InvalidOperationException("corpus empty: " + _dialogueDir);
        // 작은 합성 시드도 추가
        corpus.Add("title: a\n---\nhowl: 안녕 {$x}\n<<set $x to 1 + 2>>\n-> 선택 <<if $x > 1>>\n    howl: ok\n<<if $x == 3>>\n  A\n<<else>>\n  B\n<<endif>>\n<<jump b>>\n===\ntitle: b\n---\n끝 #line:b_001\n<<stop>>\n===\n");
        return corpus;
    }

    private static void CampaignYarnMutation()
    {
        const string C = "yarn.mutation";
        List<string> corpus = LoadCorpus();
        Console.WriteLine("[{0}] corpus files={1}", C, corpus.Count);
        Random r = new Random(Seed + 1);
        int parsedOk = 0, parseErr = 0, runOk = 0, runErr = 0, hangs = 0;
        long maxMs = 0;
        for (int it = 0; it < Iterations; it++)
        {
            Count(C);
            string baseText = corpus[r.Next(corpus.Count)];
            string text;
            int mode = r.Next(10);
            if (mode == 0) text = RandomGarbage(r, 1 + r.Next(2000));
            else if (mode == 1) text = MutateBytes(baseText, r);
            else text = MutateText(baseText, r, YarnTokens);

            DialogueProject project = null;
            Exception ex;
            long ms;
            bool done = Guarded(delegate () { project = DialogueProject.Parse(text, "fuzz"); }, TimeLimitMs, out ex, out ms);
            if (ms > maxMs) maxMs = ms;
            if (!done) { hangs++; Fail(C, "HANG(parse) it=" + it + " len=" + text.Length + " input=" + Trunc(text, 120)); continue; }
            if (ex != null)
            {
                if (ex is DialogueParseException) { parseErr++; continue; }
                Fail(C, "unexpected " + ex.GetType().Name + " from Parse it=" + it + ": " + Trunc(ex.Message, 120) + " input=" + Trunc(text, 120));
                continue;
            }
            parsedOk++;
            // 파싱이 성공한 입력은 실행도 한다(최대 2 노드, 500 스텝). 허용 예외: DialogueRuntimeException / InvalidOperationException.
            List<DialogueNode> nodes = project.AllNodes;
            for (int k = 0; k < 2 && nodes.Count > 0; k++)
            {
                string title = nodes[r.Next(nodes.Count)].Title;
                int seedRun = r.Next();
                string phase = "run";
                Exception rex;
                bool rdone = Guarded(delegate ()
                {
                    Random rr = new Random(seedRun);
                    DialogueRunner runner = new DialogueRunner(project, new InMemoryVariableStore(), new NullDialogueHost());
                    runner.Start(title);
                    for (int step = 0; step < 500; step++)
                    {
                        DialogueStep s = runner.Next();
                        if (s.IsEnd) break;
                        if (s.IsChoices)
                        {
                            List<int> ok = new List<int>();
                            for (int q = 0; q < s.Options.Count; q++) if (s.Options[q].Available) ok.Add(q);
                            runner.Choose(ok[rr.Next(ok.Count)]);
                        }
                    }
                }, TimeLimitMs, out rex, out ms);
                if (ms > maxMs) maxMs = ms;
                if (!rdone) { hangs++; Fail(C, "HANG(" + phase + ") node=" + title + " it=" + it + " input=" + Trunc(text, 120)); break; }
                if (rex == null) { runOk++; continue; }
                if (rex is DialogueRuntimeException || rex is InvalidOperationException) { runErr++; continue; }
                Fail(C, "unexpected " + rex.GetType().Name + " from Runner it=" + it + " node=" + title + ": " + Trunc(rex.Message, 120));
            }
        }
        Console.WriteLine("[{0}] parse ok={1} DialogueParseException={2} | run ok={3} documented-runtime-errors={4} | hangs={5} max_case_ms={6}",
            C, parsedOk, parseErr, runOk, runErr, hangs, maxMs);
    }

    private static string RandomGarbage(Random r, int n)
    {
        StringBuilder sb = new StringBuilder();
        string[] toks = YarnTokens.Split('|');
        for (int i = 0; i < n; i++)
        {
            int k = r.Next(4);
            if (k == 0) sb.Append(toks[r.Next(toks.Length)]);
            else if (k == 1) sb.Append((char)r.Next(32, 127));
            else if (k == 2) sb.Append((char)r.Next(0, 0xFFFF));
            else sb.Append("가나다라"[r.Next(4)]);
        }
        return sb.ToString();
    }

    // ======================================================================
    // 2) YarnParser : 표적 케이스(긴 문자열, 대규모 스크립트, 거대한 수, NaN/Infinity)
    // ======================================================================

    private static void TargetedParse(string name, string text, int limitMs, bool runAfter)
    {
        const string C = "yarn.targeted";
        Count(C);
        DialogueProject p = null;
        Exception ex;
        long ms;
        bool done = Guarded(delegate () { p = DialogueProject.Parse(text, name); }, limitMs, out ex, out ms);
        string result;
        if (!done) { Fail(C, "HANG(parse) " + name + " > " + limitMs + "ms"); Console.WriteLine("    {0,-46} HANG > {1} ms", name, limitMs); return; }
        if (ex != null && !(ex is DialogueParseException)) { Fail(C, "unexpected " + ex.GetType().Name + " in " + name + ": " + Trunc(ex.Message, 150)); Console.WriteLine("    {0,-46} UNEXPECTED {1}", name, ex.GetType().Name); return; }
        result = ex == null ? "parsed" : "DialogueParseException";
        long runMs = 0;
        if (runAfter && p != null && p.AllNodes.Count > 0)
        {
            Exception rex;
            string title = p.AllNodes[0].Title;
            bool rdone = Guarded(delegate ()
            {
                DialogueRunner runner = new DialogueRunner(p, new InMemoryVariableStore(), new NullDialogueHost());
                runner.Start(title);
                for (int i = 0; i < 200000; i++)
                {
                    DialogueStep st = runner.Next();
                    if (st.IsEnd) break;
                    if (st.IsChoices) { for (int q = 0; q < st.Options.Count; q++) if (st.Options[q].Available) { runner.Choose(q); break; } }
                }
            }, limitMs, out rex, out runMs);
            if (!rdone) { Fail(C, "HANG(run) " + name); result += " + run HANG"; }
            else if (rex != null && !(rex is DialogueRuntimeException) && !(rex is InvalidOperationException)) { Fail(C, "unexpected " + rex.GetType().Name + " running " + name + ": " + Trunc(rex.Message, 150)); result += " + run UNEXPECTED " + rex.GetType().Name; }
            else result += rex == null ? " + run ok" : " + run " + rex.GetType().Name;
        }
        if (done && ms > 1000) Obs("느린 파싱: " + name + " parse=" + ms + "ms (제한 " + limitMs + "ms 이내)");
        Console.WriteLine("    {0,-46} {1} parse={2}ms run={3}ms", name, result, ms, runMs);
    }

    private static string Rep(string s, int n)
    {
        StringBuilder sb = new StringBuilder(s.Length * n);
        for (int i = 0; i < n; i++) sb.Append(s);
        return sb.ToString();
    }

    private static string Node(string title, string body)
    {
        return "title: " + title + "\n---\n" + body + "\n===\n";
    }

    private static void CampaignYarnTargeted()
    {
        Console.WriteLine("[yarn.targeted]");
        // 대규모 스크립트
        TargetedParse("10k-line single node", Node("big", Rep("howl: 안녕 {$x} 반가워\n", 10000)), 5000, true);
        TargetedParse("10k nodes x 3 lines", BuildManyNodes(10000), 5000, false);
        TargetedParse("100k-line single node", Node("big", Rep("howl: 안녕\n", 100000)), 8000, true);
        TargetedParse("10k-line script w/ choices+if", Node("big", Rep("-> 선택 {$x}\n    howl: 응\n<<if $x == 1>>\nA\n<<endif>>\n", 2000)), 5000, true);
        // 긴 문자열
        TargetedParse("1MB dialogue line", Node("a", "howl: " + new string('가', 1000000)), 5000, true);
        TargetedParse("1MB node title", "title: " + new string('t', 1000000) + "\n---\nx\n===\n", 5000, false);
        TargetedParse("1MB comment line", Node("a", "x\n// " + new string('c', 1000000) + "\ny"), 5000, true);
        TargetedParse("1MB header value", "title: a\ntags: " + Rep("tag ", 250000) + "\n---\nx\n===\n", 5000, false);
        TargetedParse("100k-char expression {1+1+1...}", Node("a", "n: {" + Rep("1+", 50000) + "1}"), 5000, true);
        TargetedParse("50k interpolations on one line", Node("a", "n: " + Rep("{1}", 50000)), 5000, true);
        TargetedParse("100k unmatched '{'", Node("a", "n: " + new string('{', 100000)), 5000, false);
        TargetedParse("100k unmatched '<<'", Node("a", new string('<', 100000)), 5000, false);
        TargetedParse("unterminated quote 1MB", Node("a", "<<cmd \"" + new string('q', 1000000) + ">>"), 5000, false);
        // 꼬리표(#tag) 반복: 이차(O(n^2)) 가능성 점검
        foreach (int n in new int[] { 1000, 5000, 20000 })
            TargetedParse("line with " + n + " ' #t' tags", Node("a", "howl: hi" + Rep(" #t", n)), 8000, true);
        foreach (int n in new int[] { 1000, 5000, 20000 })
            TargetedParse("option with " + n + " ' #t' tags", Node("a", "-> hi" + Rep(" #t", n) + "\n    x"), 8000, true);
        // 거대한 수·NaN/Infinity
        string huge = new string('9', 400);
        TargetedParse("400-digit number literal", Node("a", "n: {" + huge + "}"), 5000, true);
        TargetedParse("400-digit number in <<set>>", Node("a", "<<set $x to " + huge + ">>\nn: {$x}"), 5000, true);
        TargetedParse("Infinity - Infinity -> NaN", Node("a", "<<set $x to " + huge + " * " + huge + " - " + huge + " * " + huge + ">>\nn: {$x}\n<<if $x == $x>>\nsame\n<<endif>>"), 5000, true);
        TargetedParse("1/0 and 0%0", Node("a", "n: {1/0}"), 5000, true);
        TargetedParse("0%0", Node("a", "n: {0%0}"), 5000, true);
        TargetedParse("NaN / Infinity words as identifiers", Node("a", "n: {NaN} {Infinity}"), 5000, true);
        TargetedParse("int overflow via GameFlags-like set", Node("a", "<<set $x to 2147483648>>\nn: {$x}"), 5000, true);
        // 구조 이상
        TargetedParse("empty input", "", 1000, false);
        TargetedParse("only BOM", "\uFEFF", 1000, false);
        TargetedParse("only NULs", new string('\0', 10000), 1000, false);
        TargetedParse("lone surrogates", Node("a", "n: \uD800\uDC00\uD800 x \uDFFF"), 1000, true);
        TargetedParse("bidi override in speaker text", Node("a", "n: \u202Eevil\u202C"), 1000, true);
        TargetedParse("CRLF + mixed tabs", "title: a\r\n---\r\n\tn: x\r\n    <<if true>>\r\n\t\ty: z\r\n<<endif>>\r\n===\r\n", 1000, true);
        // 무한 점프 루프(실행기): 하네스는 hang 을 '발견'으로 기록한다(단언 실패로 취급하지 않음; 아래 서브프로세스 프로브 참조).
    }

    private static string BuildManyNodes(int n)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append("title: n").Append(i).Append("\n---\na: 1\nb: 2\nc: 3\n===\n");
        return sb.ToString();
    }

    // ======================================================================
    // 3) SaveService / SaveValidator / SaveIntegrity
    // ======================================================================

    private abstract class Ser : ISerializer
    {
        public abstract string Name { get; }
        public abstract string ToJson(SaveData d);
        public abstract SaveData FromJson(string s);
        public abstract bool CanRepresentNonFinite { get; }
    }

    private sealed class XmlSer : Ser
    {
        private readonly XmlSerializer _x = new XmlSerializer(typeof(SaveData));
        public override string Name { get { return "xml"; } }
        public override bool CanRepresentNonFinite { get { return true; } }
        public override string ToJson(SaveData d) { using (StringWriter w = new StringWriter()) { _x.Serialize(w, d); return w.ToString(); } }
        public override SaveData FromJson(string s) { using (StringReader r = new StringReader(s)) { return (SaveData)_x.Deserialize(r); } }
    }

    /// <summary>MiniJson 기반 JSON 대역(JsonUtility 의 관대한 동작을 흉내: 알 수 없는 필드 무시, 형식이 다른 필드는 기본값 유지).</summary>
    private sealed class JsonSer : Ser
    {
        public override string Name { get { return "json"; } }
        public override bool CanRepresentNonFinite { get { return false; } }

        private static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        public override string ToJson(SaveData d)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"Version\":").Append(d.Version).Append(",\"Slot\":").Append(d.Slot);
            sb.Append(",\"SavedAtUtc\":").Append(MiniJson.Quote(d.SavedAtUtc)).Append(",\"AppVersion\":").Append(MiniJson.Quote(d.AppVersion));
            sb.Append(",\"PlayerName\":").Append(MiniJson.Quote(d.PlayerName)).Append(",\"Gender\":").Append(MiniJson.Quote(d.Gender));
            sb.Append(",\"PlayerSpecies\":").Append(MiniJson.Quote(d.PlayerSpecies)).Append(",\"PlayerAppearance\":").Append(d.PlayerAppearance);
            sb.Append(",\"Difficulty\":").Append(d.Difficulty).Append(",\"Chapter\":").Append(d.Chapter).Append(",\"Day\":").Append(d.Day);
            sb.Append(",\"DayInChapter\":").Append(d.DayInChapter);
            sb.Append(",\"Gold\":").Append(F(d.Gold)).Append(",\"Reputation\":").Append(F(d.Reputation)).Append(",\"Fatigue\":").Append(F(d.Fatigue));
            sb.Append(",\"ShopLevel\":").Append(d.ShopLevel).Append(",\"SatisfactionBonus\":").Append(F(d.SatisfactionBonus));
            sb.Append(",\"LockedRoute\":").Append(MiniJson.Quote(d.LockedRoute));
            sb.Append(",\"LockedGroup\":[");
            if (d.LockedGroup != null) for (int i = 0; i < d.LockedGroup.Count; i++) { if (i > 0) sb.Append(','); sb.Append(MiniJson.Quote(d.LockedGroup[i])); }
            sb.Append("],\"PlayTimeSec\":").Append(F(d.PlayTimeSec));
            sb.Append(",\"Chars\":[");
            if (d.Chars != null) for (int i = 0; i < d.Chars.Count; i++)
                {
                    CharSave c = d.Chars[i];
                    if (i > 0) sb.Append(',');
                    if (c == null) { sb.Append("null"); continue; }
                    sb.Append("{\"Id\":").Append(MiniJson.Quote(c.Id)).Append(",\"Aff\":").Append(F(c.Aff)).Append(",\"Trust\":").Append(F(c.Trust));
                    sb.Append(",\"EventsDone\":").Append(c.EventsDone).Append(",\"LastGiftDay\":").Append(c.LastGiftDay);
                    sb.Append(",\"LastDateDay\":").Append(c.LastDateDay).Append(",\"LastContactDay\":").Append(c.LastContactDay).Append('}');
                }
            sb.Append("],\"Flags\":[");
            if (d.Flags != null) for (int i = 0; i < d.Flags.Count; i++)
                {
                    FlagSave f = d.Flags[i];
                    if (i > 0) sb.Append(',');
                    if (f == null) { sb.Append("null"); continue; }
                    sb.Append("{\"Key\":").Append(MiniJson.Quote(f.Key)).Append(",\"Value\":").Append(f.Value).Append('}');
                }
            sb.Append("],\"VisitedNodes\":[");
            if (d.VisitedNodes != null) for (int i = 0; i < d.VisitedNodes.Count; i++) { if (i > 0) sb.Append(','); sb.Append(MiniJson.Quote(d.VisitedNodes[i])); }
            sb.Append("],\"Hash\":").Append(MiniJson.Quote(d.Hash)).Append('}');
            return sb.ToString();
        }

        private static int I(object o, int def) { return o is double ? unchecked((int)(double)o) : def; }
        private static float Fl(object o, float def) { return o is double ? (float)(double)o : def; }
        private static string S(object o, string def) { return o is string ? (string)o : def; }
        private static object G(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v : null; }

        public override SaveData FromJson(string s)
        {
            object root = MiniJson.Parse(s);
            Dictionary<string, object> d = root as Dictionary<string, object>;
            if (d == null) throw new FormatException("root is not object");
            SaveData x = new SaveData();
            x.Version = I(G(d, "Version"), x.Version); x.Slot = I(G(d, "Slot"), x.Slot);
            x.SavedAtUtc = S(G(d, "SavedAtUtc"), x.SavedAtUtc); x.AppVersion = S(G(d, "AppVersion"), x.AppVersion);
            x.PlayerName = S(G(d, "PlayerName"), x.PlayerName); x.Gender = S(G(d, "Gender"), x.Gender);
            x.PlayerSpecies = S(G(d, "PlayerSpecies"), x.PlayerSpecies); x.PlayerAppearance = I(G(d, "PlayerAppearance"), x.PlayerAppearance);
            x.Difficulty = I(G(d, "Difficulty"), x.Difficulty); x.Chapter = I(G(d, "Chapter"), x.Chapter); x.Day = I(G(d, "Day"), x.Day);
            x.DayInChapter = I(G(d, "DayInChapter"), x.DayInChapter);
            x.Gold = Fl(G(d, "Gold"), x.Gold); x.Reputation = Fl(G(d, "Reputation"), x.Reputation); x.Fatigue = Fl(G(d, "Fatigue"), x.Fatigue);
            x.ShopLevel = I(G(d, "ShopLevel"), x.ShopLevel); x.SatisfactionBonus = Fl(G(d, "SatisfactionBonus"), x.SatisfactionBonus);
            x.LockedRoute = S(G(d, "LockedRoute"), x.LockedRoute);
            x.PlayTimeSec = G(d, "PlayTimeSec") is double ? (double)G(d, "PlayTimeSec") : 0.0;
            List<object> lg = G(d, "LockedGroup") as List<object>;
            if (lg != null) { x.LockedGroup = new List<string>(); foreach (object o in lg) x.LockedGroup.Add(o as string); }
            List<object> ch = G(d, "Chars") as List<object>;
            if (ch != null)
            {
                x.Chars = new List<CharSave>();
                foreach (object o in ch)
                {
                    Dictionary<string, object> c = o as Dictionary<string, object>;
                    if (c == null) { x.Chars.Add(null); continue; }
                    CharSave cs = new CharSave();
                    cs.Id = S(G(c, "Id"), cs.Id); cs.Aff = Fl(G(c, "Aff"), 0f); cs.Trust = Fl(G(c, "Trust"), 0f);
                    cs.EventsDone = I(G(c, "EventsDone"), 0); cs.LastGiftDay = I(G(c, "LastGiftDay"), cs.LastGiftDay);
                    cs.LastDateDay = I(G(c, "LastDateDay"), cs.LastDateDay); cs.LastContactDay = I(G(c, "LastContactDay"), 0);
                    x.Chars.Add(cs);
                }
            }
            List<object> fl = G(d, "Flags") as List<object>;
            if (fl != null)
            {
                x.Flags = new List<FlagSave>();
                foreach (object o in fl)
                {
                    Dictionary<string, object> f = o as Dictionary<string, object>;
                    if (f == null) { x.Flags.Add(null); continue; }
                    FlagSave fs = new FlagSave();
                    fs.Key = S(G(f, "Key"), fs.Key); fs.Value = I(G(f, "Value"), 0);
                    x.Flags.Add(fs);
                }
            }
            List<object> vn = G(d, "VisitedNodes") as List<object>;
            if (vn != null) { x.VisitedNodes = new List<string>(); foreach (object o in vn) x.VisitedNodes.Add(o as string); }
            x.Hash = S(G(d, "Hash"), x.Hash);
            return x;
        }
    }

    private sealed class MemStorage : ISaveStorage
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public void Write(string name, string text)
        {
            string cur;
            if (Files.TryGetValue(name, out cur)) Files[name + ".bak"] = cur;
            Files[name] = text;
        }
        public string Read(string name) { string v; return Files.TryGetValue(name, out v) ? v : null; }
        public string ReadBackup(string name) { string v; return Files.TryGetValue(name + ".bak", out v) ? v : null; }
        public bool Exists(string name) { return Files.ContainsKey(name) || Files.ContainsKey(name + ".bak"); }
    }

    private static SaveData ValidSave(Random r)
    {
        SaveData d = new SaveData();
        string[] genders = ProfileValidator.GenderIds;
        string[] species = ProfileValidator.SpeciesIds;
        d.PlayerName = r == null ? "라이언" : RandomName(r, 1 + r.Next(12));
        d.Gender = genders[r == null ? 0 : r.Next(genders.Length)];
        d.PlayerSpecies = species[r == null ? 1 : r.Next(species.Length)];
        d.PlayerAppearance = r == null ? 2 : 1 + r.Next(6);
        d.Difficulty = r == null ? 1 : r.Next(3);
        d.Chapter = r == null ? 3 : 1 + r.Next(8);
        d.Day = r == null ? 20 : 1 + r.Next(1000);
        d.DayInChapter = r == null ? 4 : 1 + r.Next(60);
        d.Gold = r == null ? 1234.5f : (float)(r.NextDouble() * 1000000.0);
        d.Reputation = r == null ? 33f : (float)(r.NextDouble() * 100.0);
        d.Fatigue = r == null ? 10f : (float)(r.NextDouble() * 100.0);
        d.ShopLevel = r == null ? 2 : 1 + r.Next(10);
        d.SatisfactionBonus = 0.5f;
        d.AppVersion = "0.1.0";
        d.SavedAtUtc = "2026-01-01T00:00:00.0000000Z";
        d.LockedRoute = "howl";
        d.LockedGroup.Add("howl");
        d.PlayTimeSec = 1234.5;
        string[] ids = { "howl", "johan", "beomjun", "haru", "gomseok", "nabi" };
        for (int i = 0; i < ids.Length; i++)
        {
            CharSave c = new CharSave();
            c.Id = ids[i];
            c.Aff = r == null ? 40f : (float)(r.NextDouble() * 100.0);
            c.Trust = r == null ? 10f : (float)(r.NextDouble() * 50.0);
            c.EventsDone = r == null ? 1 : r.Next(6);
            d.Chars.Add(c);
        }
        for (int i = 0; i < 5; i++) { FlagSave f = new FlagSave(); f.Key = "flag_" + i; f.Value = i; d.Flags.Add(f); }
        d.VisitedNodes.Add("ch01_open"); d.VisitedNodes.Add("howl_E1");
        return d;
    }

    private static string RandomName(Random r, int n)
    {
        string alphabet = "가나다라마바사아자차ABCDEFghij0123 _-.가";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(alphabet[r.Next(alphabet.Length)]);
        string t = sb.ToString().Trim();
        return t.Length == 0 ? "a" : t;
    }

    private static string SignedText(Ser ser, SaveData d)
    {
        // SaveService.Save 와 동일한 서명 절차(검증은 건너뜀). 키가 소스에 있으므로 공격자도 할 수 있는 일이다.
        d.Hash = SaveIntegrity.Sign(d) ?? string.Empty; // 현재 서명 방식(v2 정규 페이로드); 알 수 없는 버전이면 빈 해시(= Tampered 가 되어야 함)
        return ser.ToJson(d);
    }

    private static string Canon(Ser ser, SaveData d)
    {
        string h = d.Hash;
        d.Hash = string.Empty;
        string s = ser.ToJson(d);
        d.Hash = h;
        return s;
    }

    private static bool LoadAllowed(LoadStatus s)
    {
        return s == LoadStatus.Ok || s == LoadStatus.NotFound || s == LoadStatus.RestoredFromBackup || s == LoadStatus.Tampered || s == LoadStatus.Corrupt;
    }

    // 서명 설계(portfolio/SECURITY_DESIGN.md 4.1): 직렬화기 독립 정규 페이로드, 구형 v1 호환, 변조된 주 파일은 .bak 으로 조용히 대체되지 않음.
    private static void CampaignSaveSigning()
    {
        string C = "save.signing";
        SaveData orig = ValidSave(null);
        string payJson = null, payXml = null;
        foreach (Ser ser in new Ser[] { new JsonSer(), new XmlSer() })
        {
            Count(C);
            MemStorage ms = new MemStorage();
            SaveService svc = new SaveService(ser, ms);
            svc.Save(1, ValidSave(null));
            string good = ms.Read("slot1.json");
            LoadResult ok = svc.Load(1);
            if (ok.Status != LoadStatus.Ok || ok.Data.Version != SaveData.CurrentVersion) Fail(C, ser.Name + ": fresh save did not load Ok at current version: " + ok.Status);
            string pay = SaveIntegrity.CanonicalPayload(ser.FromJson(good));
            if (ser.Name == "json") payJson = pay; else payXml = pay;

            // 구형 v1(텍스트 해시) 저장: Ok + v2 로 마이그레이션, 값 편집은 Tampered
            Count(C);
            SaveData v1 = ValidSave(null);
            v1.Version = 1; v1.Hash = string.Empty;
            string basis = ser.ToJson(v1);
            v1.Hash = SaveIntegrity.Compute(basis);
            MemStorage ms1 = new MemStorage(); ms1.Files["slot1.json"] = ser.ToJson(v1);
            LoadResult l1 = new SaveService(ser, ms1).Load(1);
            if (l1.Status != LoadStatus.Ok || l1.Data.Version != SaveData.CurrentVersion || l1.FileVersion != 1) Fail(C, ser.Name + ": legacy v1 save not loaded/migrated: " + l1.Status + " " + l1.Detail);
            SaveData v1t = ser.FromJson(ms1.Files["slot1.json"]); v1t.Gold += 1f;
            ms1.Files["slot1.json"] = ser.ToJson(v1t);
            if (new SaveService(ser, ms1).Load(1).Status == LoadStatus.Ok) Fail(C, ser.Name + ": edited legacy v1 save accepted");

            // 변조된 주 파일 + 정상 .bak: 복구하되 호출자가 알 수 있어야 함
            Count(C);
            MemStorage ms2 = new MemStorage(); SaveService s2 = new SaveService(ser, ms2);
            s2.Save(1, ValidSave(null)); s2.Save(1, ValidSave(null));
            SaveData t = ser.FromJson(ms2.Read("slot1.json")); t.Gold += 5f; ms2.Files["slot1.json"] = ser.ToJson(t);
            LoadResult rb = s2.Load(1);
            if (rb.Status != LoadStatus.RestoredFromBackup || rb.Detail != "primary: Tampered" || !rb.PrimaryRejected) Fail(C, ser.Name + ": tampered primary not reported: " + rb.Status + " '" + rb.Detail + "' rejected=" + rb.PrimaryRejected);

            // 모든 SaveData 필드는 서명 목록에 있거나 명시적으로 제외되어야 함(필드 추가 시 결정을 강제)
            Count(C);
            string[] names = SaveIntegrity.SignedFieldNames(SaveData.CurrentVersion);
            foreach (System.Reflection.FieldInfo fi in typeof(SaveData).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                if (Array.IndexOf(names, fi.Name) < 0 && Array.IndexOf(SaveIntegrity.ExcludedFields, fi.Name) < 0) Fail(C, "SaveData." + fi.Name + " is neither signed nor excluded");
        }
        Count(C);
        if (payJson != payXml) Fail(C, "canonical payload differs between serializers");
    }

    private static void CampaignSaveMutation()
    {
        foreach (Ser ser in new Ser[] { new JsonSer(), new XmlSer() })
        {
            string C = "save.mutation." + ser.Name;
            Random r = new Random(Seed + 2);
            SaveData orig = ValidSave(null);
            MemStorage seedStore = new MemStorage();
            new SaveService(ser, seedStore).Save(1, orig);
            string good = seedStore.Read("slot1.json");
            string goodCanon = Canon(ser, ser.FromJson(good));
            Dictionary<string, int> hist = new Dictionary<string, int>();
            int ex = 0, hangs = 0;
            for (int it = 0; it < Iterations; it++)
            {
                Count(C);
                string text;
                int m = r.Next(14);
                if (m == 0) text = MutateBytes(good, r);
                else if (m == 1) text = good.Substring(0, r.Next(good.Length));                       // 잘림
                else if (m == 2) text = RandomGarbage(r, 1 + r.Next(300));                              // 무작위
                else if (m == 3) text = good.Replace("\"PlayerName\":\"라이언\"", "\"PlayerName\":\"" + new string('가', 100000) + "\"").Replace("<PlayerName>라이언</PlayerName>", "<PlayerName>" + new string('가', 100000) + "</PlayerName>"); // 초장문
                else if (m == 4) text = DeepNest(ser, r.Next(2) == 0 ? 5000 : 50000);                // 깊은 중첩
                else if (m == 5) text = NumberSwap(good, r);                                            // 숫자를 극단값으로
                else if (m == 6) text = "";                                                              // 빈 파일
                else if (m == 7) text = new string('\0', 1 + r.Next(5000));                            // 널 바이트
                else if (m == 8) text = good + good;                                                    // 후행 쓰레기
                else text = MutateText(good, r, ser.Name == "json" ? "{|}|[|]|\"|,|:|null|true|NaN|Infinity|-Infinity|1e999|-1e999|99999999999999999999|\\u00zz|\\|\\\"|0.0|-0" : "<|>|</|/>|<!--|-->|<![CDATA[|]]>|&amp;|&#0;|&#xD800;|<!DOCTYPE x [<!ENTITY a \"b\">]>|NaN|INF|-INF|1e999");

                MemStorage st = new MemStorage();
                bool withBackup = r.Next(2) == 0;
                st.Files["slot1.json"] = text;
                if (withBackup) st.Files["slot1.json.bak"] = good;
                SaveService svc = new SaveService(ser, st);
                LoadResult res = null;
                Exception e;
                long ms;
                bool done = Guarded(delegate () { res = svc.Load(1); }, TimeLimitMs, out e, out ms);
                if (!done) { hangs++; Fail(C, "HANG it=" + it + " m=" + m + " len=" + text.Length); continue; }
                if (e != null) { ex++; Fail(C, "Load threw " + e.GetType().Name + " it=" + it + " m=" + m + ": " + Trunc(e.Message, 100) + " input=" + Trunc(text, 100)); continue; }
                string key = res.Status.ToString();
                int c0; hist.TryGetValue(key, out c0); hist[key] = c0 + 1;
                if (!LoadAllowed(res.Status)) { Fail(C, "bad status " + res.Status); continue; }
                if (res.Status == LoadStatus.Ok || res.Status == LoadStatus.RestoredFromBackup)
                {
                    if (SaveValidator.Validate(res.Data) != null) Fail(C, "accepted data fails validator it=" + it);
                    else if (Canon(ser, res.Data) != goodCanon) Fail(C, "ACCEPTED A MODIFIED SAVE (integrity bypass) it=" + it + " m=" + m + " input=" + Trunc(text, 160));
                    Exception fe;
                    long fms;
                    Guarded(delegate () { SaveMapper.FromSave(res.Data); }, TimeLimitMs, out fe, out fms);
                    if (fe != null) Fail(C, "FromSave threw " + fe.GetType().Name + " on accepted data");
                }
                else if (res.Data != null) Fail(C, "non-Ok status returned data");
            }
            StringBuilder h = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in hist) h.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            Console.WriteLine("[{0}] statuses: {1}| unexpected-exceptions={2} hangs={3}", C, h.ToString(), ex, hangs);
        }
    }

    private static string DeepNest(Ser ser, int depth)
    {
        if (ser.Name == "json") return Rep("[", depth) + Rep("]", depth);
        return "<?xml version=\"1.0\"?>" + Rep("<a>", depth) + Rep("</a>", depth);
    }

    private static string NumberSwap(string s, Random r)
    {
        string[] bad = { "1e999", "-1e999", "NaN", "Infinity", "-Infinity", "99999999999999999999999", "-99999999999", "2147483648", "-2147483649", "0.0000000001", "1e-999", "INF", "-INF", "340282350000000000000000000000000000000" };
        // 숫자 하나를 찾아 극단값으로 교체
        List<int> starts = new List<int>();
        for (int i = 0; i < s.Length; i++) if (char.IsDigit(s[i]) && (i == 0 || !char.IsLetterOrDigit(s[i - 1]))) starts.Add(i);
        if (starts.Count == 0) return s;
        int p = starts[r.Next(starts.Count)];
        int e = p;
        while (e < s.Length && (char.IsDigit(s[e]) || s[e] == '.')) e++;
        return s.Substring(0, p) + bad[r.Next(bad.Length)] + s.Substring(e);
    }

    // ----------------------------------------------------------------------

    private sealed class Attack
    {
        public string Name;
        public Action<SaveData> Apply;
        public bool NeedsNonFinite;
        public bool NullValue;   // 텍스트 직렬화(JSON/XML)로는 null 과 빈 값을 구분할 수 없어 파일로 만들 수 없음 -> 직접 호출로 시험
    }

    private static List<Attack> ContractAttacks()
    {
        List<Attack> a = new List<Attack>();
        Action<string, Action<SaveData>, bool> add = delegate (string n, Action<SaveData> f, bool nf) { Attack x = new Attack(); x.Name = n; x.Apply = f; x.NeedsNonFinite = nf; x.NullValue = n.IndexOf("null", StringComparison.Ordinal) >= 0; a.Add(x); };
        add("Version=0", d => d.Version = 0, false);
        add("Version=999", d => d.Version = 999, false);
        add("Version=-1", d => d.Version = -1, false);
        add("Difficulty=3", d => d.Difficulty = 3, false);
        add("Difficulty=-1", d => d.Difficulty = -1, false);
        add("Chapter=0", d => d.Chapter = 0, false);
        add("Chapter=9", d => d.Chapter = 9, false);
        add("Day=0", d => d.Day = 0, false);
        add("Day=1001", d => d.Day = 1001, false);
        add("Day=int.Max", d => d.Day = int.MaxValue, false);
        add("DayInChapter=61", d => d.DayInChapter = 61, false);
        add("DayInChapter=0", d => d.DayInChapter = 0, false);
        add("Gold=1e12", d => d.Gold = 1e12f, false);
        add("Gold=-1e12", d => d.Gold = -1e12f, false);
        add("Gold=NaN", d => d.Gold = float.NaN, true);
        add("Gold=+Inf", d => d.Gold = float.PositiveInfinity, true);
        add("Gold=-Inf", d => d.Gold = float.NegativeInfinity, true);
        add("Reputation=101", d => d.Reputation = 101f, false);
        add("Reputation=-1", d => d.Reputation = -1f, false);
        add("Reputation=NaN", d => d.Reputation = float.NaN, true);
        add("Fatigue=1000", d => d.Fatigue = 1000f, false);
        add("Fatigue=Inf", d => d.Fatigue = float.PositiveInfinity, true);
        add("ShopLevel=0", d => d.ShopLevel = 0, false);
        add("ShopLevel=11", d => d.ShopLevel = 11, false);
        add("PlayerName=null", d => d.PlayerName = null, false);
        add("PlayerName=33 chars", d => d.PlayerName = new string('a', 33), false);
        add("PlayerName=1MB", d => d.PlayerName = new string('a', 1000000), false);
        add("Gender=bogus", d => d.Gender = "attack", false);
        add("Gender=null", d => d.Gender = null, false);
        add("Species=bogus", d => d.PlayerSpecies = "dragon", false);
        add("Appearance=0", d => d.PlayerAppearance = 0, false);
        add("Appearance=7", d => d.PlayerAppearance = 7, false);
        add("Chars=null", d => d.Chars = null, false);
        add("Flags=null", d => d.Flags = null, false);
        add("Chars count 65", d => { d.Chars = new List<CharSave>(); for (int i = 0; i < 65; i++) { CharSave c = new CharSave(); c.Id = "c" + i; d.Chars.Add(c); } }, false);
        add("Flags count 1025", d => { d.Flags = new List<FlagSave>(); for (int i = 0; i < 1025; i++) { FlagSave f = new FlagSave(); f.Key = "k" + i; d.Flags.Add(f); } }, false);
        add("Flag key empty", d => d.Flags[0].Key = "", false);
        add("Flag key 65 chars", d => d.Flags[0].Key = new string('k', 65), false);
        add("Flag entry null", d => d.Flags[0] = null, false);
        add("Char entry null", d => d.Chars[0] = null, false);
        add("Char id empty", d => d.Chars[0].Id = "", false);
        add("Char id 33", d => d.Chars[0].Id = new string('x', 33), false);
        add("Aff=101", d => d.Chars[0].Aff = 101f, false);
        add("Aff=-1", d => d.Chars[0].Aff = -1f, false);
        add("Aff=NaN", d => d.Chars[0].Aff = float.NaN, true);
        add("Trust=51", d => d.Chars[0].Trust = 51f, false);
        add("Trust=Inf", d => d.Chars[0].Trust = float.PositiveInfinity, true);
        add("EventsDone=6", d => d.Chars[0].EventsDone = 6, false);
        add("EventsDone=-1", d => d.Chars[0].EventsDone = -1, false);
        add("LockedRoute 33 chars", d => d.LockedRoute = new string('r', 33), false);
        add("LockedGroup 4 members", d => { d.LockedGroup = new List<string> { "howl", "johan", "haru", "nabi" }; }, false);
        add("LockedGroup duplicate", d => { d.LockedGroup = new List<string> { "howl", "howl" }; }, false);
        add("LockedGroup empty member", d => { d.LockedGroup = new List<string> { "howl", "" }; }, false);
        add("LockedGroup primary mismatch", d => { d.LockedGroup = new List<string> { "johan" }; }, false);
        add("VisitedNodes 4097", d => { d.VisitedNodes = new List<string>(); for (int i = 0; i < 4097; i++) d.VisitedNodes.Add("n" + i); }, false);
        add("VisitedNodes node 129 chars", d => d.VisitedNodes[0] = new string('v', 129), false);
        add("VisitedNodes node null", d => d.VisitedNodes[0] = null, false);
        return a;
    }

    private static List<Attack> GapProbes()
    {
        List<Attack> a = new List<Attack>();
        Action<string, Action<SaveData>, bool> add = delegate (string n, Action<SaveData> f, bool nf) { Attack x = new Attack(); x.Name = n; x.Apply = f; x.NeedsNonFinite = nf; a.Add(x); };
        add("PlayerName empty string", d => d.PlayerName = "", false);
        add("SatisfactionBonus=1e30", d => d.SatisfactionBonus = 1e30f, false);
        add("SatisfactionBonus=NaN", d => d.SatisfactionBonus = float.NaN, true);
        add("SatisfactionBonus=+Inf", d => d.SatisfactionBonus = float.PositiveInfinity, true);
        add("PlayTimeSec=NaN", d => d.PlayTimeSec = double.NaN, true);
        add("PlayTimeSec=-1e300", d => d.PlayTimeSec = -1e300, false);
        add("Slot=999 (file/slot mismatch)", d => d.Slot = 999, false);
        add("Chars[0].LastGiftDay=int.Max", d => d.Chars[0].LastGiftDay = int.MaxValue, false);
        add("Chars[0].LastContactDay=int.Min", d => d.Chars[0].LastContactDay = int.MinValue, false);
        add("Flag value=int.Min", d => d.Flags[0].Value = int.MinValue, false);
        add("Duplicate flag keys", d => { d.Flags[1].Key = d.Flags[0].Key; }, false);
        add("Duplicate/unknown char ids", d => { d.Chars[1].Id = d.Chars[0].Id; }, false);
        add("Unknown char id '../../x'", d => d.Chars[0].Id = "../../x", false);
        add("PlayerName with <b>{x}</b>", d => d.PlayerName = "<b>{x}</b>", false);
        add("PlayerName with control chars", d => d.PlayerName = "a\u0001\u0007b", false);
        add("PlayerName with bidi override", d => d.PlayerName = "\u202Eabc", false);
        add("PlayerName 33 combining marks", d => d.PlayerName = "a" + Rep("\u0301", 32), false);
        add("PlayerName with U+2028", d => d.PlayerName = "a\u2028b", false);
        add("PlayerName with U+061C", d => d.PlayerName = "a\u061Cb", false);
        add("PlayerName with U+2060", d => d.PlayerName = "a\u2060b", false);
        add("PlayerName with U+00AD", d => d.PlayerName = "a\u00ADb", false);
        add("PlayerName with U+206A", d => d.PlayerName = "a\u206Ab", false);
        add("PlayerName with U+E000", d => d.PlayerName = "a\uE000b", false);
        add("PlayerName with <>{}\\", d => d.PlayerName = "a<b>{c}\\", false);
        add("PlayerName lone surrogate", d => d.PlayerName = "a\uD800", false);
        add("Flag key with control char", d => d.Flags[0].Key = "k\u0001", false);
        add("Flag value=1e6+1", d => d.Flags[0].Value = 1000001, false);
        add("Chars[0].LastGiftDay=5001", d => d.Chars[0].LastGiftDay = 5001, false);
        add("Slot=-1", d => d.Slot = -1, false);
        add("Slot=4", d => d.Slot = 4, false);
        add("VisitedNodes node with backslash", d => d.VisitedNodes[0] = "a\\b", false);
        add("LockedGroup unknown member", d => { d.LockedGroup = new List<string> { "howl", "zzz" }; }, false);
        add("VisitedNodes node contains ../", d => d.VisitedNodes[0] = "../../etc/passwd", false);
        add("LockedRoute not a known char", d => { d.LockedRoute = "zzz"; d.LockedGroup = new List<string> { "zzz" }; }, false);
        add("SavedAtUtc 1MB", d => d.SavedAtUtc = new string('x', 1000000), false);
        add("AppVersion 1MB", d => d.AppVersion = new string('x', 1000000), false);
        return a;
    }

    private static void CampaignSaveForged()
    {
        List<Attack> contract = ContractAttacks();
        foreach (Ser ser in new Ser[] { new JsonSer(), new XmlSer() })
        {
            string C = "save.forged_signed." + ser.Name;
            Random r = new Random(Seed + 3);
            int rejected = 0, skipped = 0;
            for (int it = 0; it < Iterations; it++)
            {
                Attack at = contract[r.Next(contract.Count)];
                if ((at.NeedsNonFinite && !ser.CanRepresentNonFinite) || at.NullValue) { skipped++; continue; }
                Count(C);
                SaveData d = ValidSave(r);
                try { at.Apply(d); } catch (Exception) { continue; }
                string text;
                try { text = SignedText(ser, d); }
                catch (Exception) { skipped++; continue; } // 직렬화기가 표현하지 못하는 값은 건너뜀(파일로 존재할 수 없음)
                MemStorage st = new MemStorage();
                st.Files["slot1.json"] = text;
                SaveService svc = new SaveService(ser, st);
                LoadResult res = null;
                Exception e;
                long ms;
                bool done = Guarded(delegate () { res = svc.Load(1); }, TimeLimitMs, out e, out ms);
                if (!done) { Fail(C, "HANG attack=" + at.Name); continue; }
                if (e != null) { Fail(C, "Load threw " + e.GetType().Name + " attack=" + at.Name + ": " + Trunc(e.Message, 100)); continue; }
                if (res.Status == LoadStatus.Ok || res.Status == LoadStatus.RestoredFromBackup)
                    Fail(C, "VALIDATOR ACCEPTED out-of-contract value, attack=" + at.Name);
                else rejected++;
            }
            Console.WriteLine("[{0}] signed-forged out-of-range files rejected={1} skipped(non-representable)={2}", C, rejected, skipped);
        }

        // 계약에 없는(=검증하지 않는) 필드: 수용되면 '관찰'로 기록한다.
        Console.WriteLine("[save.validator_gaps] formerly-unvalidated fields: forged + correctly signed file must now be rejected (F-7):");
        foreach (Ser ser in new Ser[] { new JsonSer(), new XmlSer() })
        {
            foreach (Attack at in GapProbes())
            {
                if (at.NeedsNonFinite && !ser.CanRepresentNonFinite) continue;
                Count("save.validator_gaps");
                SaveData d = ValidSave(null);
                at.Apply(d);
                string text;
                try { text = SignedText(ser, d); } catch (Exception ex) { Console.WriteLine("    {0,-5} {1,-36} (not representable: {2})", ser.Name, at.Name, ex.GetType().Name); continue; }
                MemStorage st = new MemStorage();
                st.Files["slot1.json"] = text;
                LoadResult res = null;
                Exception e;
                long ms;
                bool done = Guarded(delegate () { res = new SaveService(ser, st).Load(1); }, TimeLimitMs, out e, out ms);
                string outcome = !done ? "HANG" : e != null ? "THREW " + e.GetType().Name : res.Status.ToString();
                if (done && e == null && res.Status == LoadStatus.Ok)
                {
                    string extra = "";
                    Exception me;
                    long mms;
                    Guarded(delegate () { SaveMapper.FromSave(res.Data); }, TimeLimitMs, out me, out mms);
                    if (me != null) extra = " and FromSave THREW " + me.GetType().Name;
                    outcome = "ACCEPTED" + extra;
                    Fail("save.validator_gaps", "VALIDATOR ACCEPTED forged signed save [" + ser.Name + "]: " + at.Name + extra);
                }
                else if (!done || e != null) Fail("save.validator_gaps", at.Name + " -> " + outcome);
                Console.WriteLine("    {0,-5} {1,-36} {2}", ser.Name, at.Name, outcome);
            }
        }
    }

    /// <summary>null 이 들어간 객체는 텍스트 파일로 만들 수 없으므로 SaveValidator 를 직접 호출한다(예외 없이 사유를 돌려줘야 함).</summary>
    private static void DirectValidatorNullChecks()
    {
        const string C = "validator.direct_nulls";
        List<KeyValuePair<string, Action<SaveData>>> cases = new List<KeyValuePair<string, Action<SaveData>>>();
        cases.Add(new KeyValuePair<string, Action<SaveData>>("PlayerName=null", d => d.PlayerName = null));
        cases.Add(new KeyValuePair<string, Action<SaveData>>("Chars=null", d => d.Chars = null));
        cases.Add(new KeyValuePair<string, Action<SaveData>>("Flags=null", d => d.Flags = null));
        cases.Add(new KeyValuePair<string, Action<SaveData>>("Flags[0]=null", d => d.Flags[0] = null));
        cases.Add(new KeyValuePair<string, Action<SaveData>>("Chars[0]=null", d => d.Chars[0] = null));
        cases.Add(new KeyValuePair<string, Action<SaveData>>("VisitedNodes[0]=null", d => d.VisitedNodes[0] = null));
        cases.Add(new KeyValuePair<string, Action<SaveData>>("LockedGroup[0]=null", d => d.LockedGroup[0] = null));
        cases.Add(new KeyValuePair<string, Action<SaveData>>("Gender=null", d => d.Gender = null));
        foreach (KeyValuePair<string, Action<SaveData>> kv in cases)
        {
            Count(C);
            SaveData d = ValidSave(null);
            kv.Value(d);
            try
            {
                string r = SaveValidator.Validate(d);
                if (r == null) Fail(C, "Validate accepted " + kv.Key);
            }
            catch (Exception ex) { Fail(C, "Validate threw " + ex.GetType().Name + " for " + kv.Key); }
        }
        Count(C);
        if (SaveValidator.Validate(null) == null) Fail(C, "Validate(null) accepted");
        Console.WriteLine("[{0}] {1} direct null-field cases: validator returns a reason without throwing", C, cases.Count + 1);
    }

    private static void CampaignSaveRoundtripAndRefuse()
    {
        foreach (Ser ser in new Ser[] { new JsonSer(), new XmlSer() })
        {
            string C = "save.roundtrip_refuse." + ser.Name;
            Random r = new Random(Seed + 4);
            List<Attack> contract = ContractAttacks();
            int saved = 0, refused = 0;
            for (int it = 0; it < Iterations; it++)
            {
                Count(C);
                SaveData d = ValidSave(r);
                bool invalid = r.Next(2) == 0;
                Attack at = null;
                if (invalid)
                {
                    at = contract[r.Next(contract.Count)];
                    if (at.NeedsNonFinite && !ser.CanRepresentNonFinite) { /* 검증은 직렬화 이전이므로 NaN 도 시험 가능 */ }
                    try { at.Apply(d); } catch (Exception) { continue; }
                }
                MemStorage st = new MemStorage();
                SaveService svc = new SaveService(ser, st);
                int slot = r.Next(0, 4);
                string expectProblem = SaveValidator.Validate(d);
                Exception e;
                long ms;
                bool done = Guarded(delegate () { svc.Save(slot, d); }, TimeLimitMs, out e, out ms);
                if (!done) { Fail(C, "HANG Save"); continue; }
                if (expectProblem != null)
                {
                    if (e is InvalidOperationException && st.Files.Count == 0) refused++;
                    else Fail(C, "invalid data not refused cleanly attack=" + (at == null ? "-" : at.Name) + " ex=" + (e == null ? "none" : e.GetType().Name) + " files=" + st.Files.Count);
                    continue;
                }
                if (e != null) { Fail(C, "valid data failed to save: " + e.GetType().Name + " " + Trunc(e.Message, 80)); continue; }
                saved++;
                LoadResult res = svc.Load(slot);
                if (res.Status != LoadStatus.Ok) Fail(C, "roundtrip of valid data gave " + res.Status + "/" + res.Detail);
                else if (Canon(ser, res.Data) != Canon(ser, d)) Fail(C, "roundtrip changed data");
            }
            // 슬롯 번호 경계
            foreach (int bad in new int[] { -1, 4, int.MinValue, int.MaxValue, 1000 })
            {
                Count(C);
                try { SaveService.SlotName(bad); Fail(C, "SlotName(" + bad + ") accepted"); }
                catch (ArgumentOutOfRangeException) { }
            }
            Console.WriteLine("[{0}] saved+reloaded={1} invalid-refused={2} slot-bounds ok", C, saved, refused);
        }
    }

    // ======================================================================
    // 4) FileSaveStorage : 경로 이탈 금지
    // ======================================================================

    private static HashSet<string> Snapshot(string root, string savesDir)
    {
        HashSet<string> set = new HashSet<string>();
        foreach (string p in Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            string full = Path.GetFullPath(p);
            if (full.StartsWith(savesDir + Path.DirectorySeparatorChar) || full == savesDir) continue;
            set.Add(full);
        }
        return set;
    }

    private static void CampaignStoragePaths()
    {
        const string C = "storage.path_traversal";
        string root = Path.Combine(Path.GetTempPath(), "morphe_sectest_" + Guid.NewGuid().ToString("N"));
        string outer = Path.Combine(root, "outer");
        string saves = Path.GetFullPath(Path.Combine(outer, "saves"));
        Directory.CreateDirectory(outer);
        File.WriteAllText(Path.Combine(root, "secret.txt"), "SECRET");
        File.WriteAllText(Path.Combine(outer, "sibling.txt"), "SIBLING");
        FileSaveStorage storage = new FileSaveStorage(saves);
        HashSet<string> baseline = Snapshot(root, saves);
        string secretAbs = Path.Combine(root, "secret.txt");

        List<string> names = new List<string>
        {
            "../x.json", "../../x.json", "../../../../../../tmp/x.json", "/tmp/abs_x.json", secretAbs, "..\\x.json", "C:\\Windows\\x.json",
            "..", ".", "", "/", "./", "../", "a/", "a/..", "slot0.json/../../x", "....//....//x", "%2e%2e/x", "..;/x", "\0evil", "x\0y", "~/x",
            new string('a', 300), new string('가', 100) + "/../../x", "slot1.json\n../x", "CON", "NUL", "slot1.json.", "slot1.json ", "..\\..\\x", "x:y"
        };
        string[] segs = { "..", "/", "\\", "a", "slot0.json", ".", "", "\0", "~", "%2e%2e", "....", " ", "가", ":", "*", "?" };
        Random r = new Random(Seed + 5);
        for (int i = 0; i < Iterations; i++)
        {
            StringBuilder sb = new StringBuilder();
            int n = 1 + r.Next(6);
            for (int k = 0; k < n; k++) sb.Append(segs[r.Next(segs.Length)]);
            names.Add(sb.ToString());
        }

        int escapes = 0, allowedExc = 0, otherExc = 0, readLeaks = 0;
        Dictionary<string, int> excTypes = new Dictionary<string, int>();
        List<string> escapeExamples = new List<string>();
        foreach (string name in names)
        {
            Count(C);
            // Write
            Exception e = null;
            try { storage.Write(name, "{\"probe\":1}"); }
            catch (Exception ex) { e = ex; }
            if (e != null)
            {
                string t = e.GetType().Name;
                int c0; excTypes.TryGetValue(t, out c0); excTypes[t] = c0 + 1;
                if (e is ArgumentException || e is IOException || e is UnauthorizedAccessException || e is NotSupportedException) allowedExc++;
                else { otherExc++; Fail(C, "Write(" + Trunc(name, 40) + ") threw unexpected " + t); }
            }
            HashSet<string> now = Snapshot(root, saves);
            bool escaped = !now.SetEquals(baseline);
            if (escaped)
            {
                escapes++;
                foreach (string p in now) if (!baseline.Contains(p) && escapeExamples.Count < 6) escapeExamples.Add("Write(\"" + Trunc(name, 30) + "\") created " + p.Substring(root.Length));
                // 정리해서 다음 반복이 독립적이게 함
                foreach (string p in now) if (!baseline.Contains(p)) { try { if (File.Exists(p)) File.Delete(p); else if (Directory.Exists(p)) Directory.Delete(p, true); } catch (Exception) { } }
            }
            if (File.ReadAllText(secretAbs) != "SECRET" || File.ReadAllText(Path.Combine(outer, "sibling.txt")) != "SIBLING")
            {
                Fail(C, "EXISTING FILE OUTSIDE SAVES DIR WAS MODIFIED by Write(" + Trunc(name, 40) + ")");
                File.WriteAllText(secretAbs, "SECRET"); File.WriteAllText(Path.Combine(outer, "sibling.txt"), "SIBLING");
            }
            // Read / ReadBackup / Exists 는 바깥 파일 내용을 돌려주면 안 된다.
            try
            {
                string rd = storage.Read(name), bk = storage.ReadBackup(name);
                if ((rd != null && rd.Contains("SECRET")) || (bk != null && bk.Contains("SECRET")) || (rd != null && rd.Contains("SIBLING")) || (bk != null && bk.Contains("SIBLING")))
                { readLeaks++; Fail(C, "Read leaked outside file for name " + Trunc(name, 40)); }
                storage.Exists(name);
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)) Fail(C, "Read threw unexpected " + ex.GetType().Name + " for " + Trunc(name, 40));
            }
        }
        StringBuilder h = new StringBuilder();
        foreach (KeyValuePair<string, int> kv in excTypes) h.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
        Console.WriteLine("[{0}] names tried={1} (Write+Read+ReadBackup+Exists each) | documented-class exceptions={2} [{3}] other-exceptions={4} | read-leaks={5} | writes/creations OUTSIDE saves dir={6}",
            C, names.Count, allowedExc, h.ToString().Trim(), otherExc, readLeaks, escapes);
        foreach (string ex in escapeExamples) Console.WriteLine("    ESCAPE: " + ex);
        if (escapes > 0)
        {
            Obs("FileSaveStorage 경로 이탈(방어 심층 결함): 이름이 비거나 '/' 로 끝나면 Path.GetFileName 이 빈 문자열을 돌려줘 `<저장폴더>.tmp` 가 저장 폴더 바깥(형제 경로)에 생성됨. 예: " + string.Join("; ", escapeExamples.ToArray()) + ". SaveService.SlotName 은 정수 슬롯에서만 이름을 만들므로 현재 앱 경로에서는 도달 불가.");
            // 단언 실패로는 올리지 않는다: 요청된 불변식("never writes outside its directory")을 위반한 '실제 발견'이므로 아래에서 명시적으로 실패 처리한다.
            Fail(C, "FileSaveStorage wrote outside its directory " + escapes + " time(s); e.g. " + string.Join("; ", escapeExamples.ToArray()));
        }
        try { Directory.Delete(root, true); } catch (Exception) { }
    }

    // ======================================================================
    // 5) MiniJson / ProfileService / SettingsService
    // ======================================================================

    private static void CampaignJsonProfileSettings()
    {
        // MiniJson: 허용 예외는 FormatException 뿐
        {
            const string C = "minijson.parse";
            Random r = new Random(Seed + 6);
            string good = "{\"version\":1,\"endings\":{\"good:howl\":2,\"normal:johan\":1},\"cgs\":[\"A\",\"B\"],\"unlocks\":[\"x\"],\"n\":-1.5e3,\"t\":true,\"z\":null}";
            int ok = 0, fe = 0;
            for (int it = 0; it < Iterations; it++)
            {
                Count(C);
                string text;
                int m = r.Next(8);
                if (m == 0) text = MutateBytes(good, r);
                else if (m == 1) text = good.Substring(0, r.Next(good.Length));
                else if (m == 2) text = Rep("[", 1 + r.Next(200000));
                else if (m == 3) text = Rep("{\"a\":", 1 + r.Next(100000)) + "1";
                else if (m == 4) text = "[" + new string('9', 1 + r.Next(2000)) + "]";
                else if (m == 5) text = "\"" + new string('a', 1 + r.Next(2000000)) + "\"";
                else if (m == 6) text = RandomGarbage(r, 1 + r.Next(200));
                else text = MutateText(good, r, "{|}|[|]|\"|,|:|null|true|NaN|Infinity|1e999|-|\\u|\\uD800|\\u+123|\\ud83d\\ude00");
                Exception e;
                long ms;
                bool done = Guarded(delegate () { MiniJson.Parse(text); }, TimeLimitMs, out e, out ms);
                if (!done) { Fail(C, "HANG len=" + text.Length); continue; }
                if (e == null) ok++;
                else if (e is FormatException) fe++;
                else Fail(C, "unexpected " + e.GetType().Name + " for " + Trunc(text, 80) + ": " + Trunc(e.Message, 80));
            }
            Console.WriteLine("[{0}] parsed={1} FormatException={2}", C, ok, fe);
        }
        // ProfileService.Load: 예외 금지, 한도(512) 준수
        {
            const string C = "profile.load";
            Random r = new Random(Seed + 7);
            StringBuilder big = new StringBuilder("{\"endings\":{");
            for (int i = 0; i < 2000; i++) { if (i > 0) big.Append(','); big.Append("\"k").Append(i).Append("\":").Append(i % 3 == 0 ? "1e999" : i % 3 == 1 ? "-5" : "99999999999"); }
            big.Append("},\"cgs\":["); for (int i = 0; i < 2000; i++) { if (i > 0) big.Append(','); big.Append("\"cg").Append(i).Append('"'); } big.Append("],\"unlocks\":[1,null,\"u\",[\"x\"]]}");
            string[] seeds = { "{\"version\":1,\"endings\":{\"good:howl\":2},\"cgs\":[\"A\"],\"unlocks\":[\"x\"]}", big.ToString() };
            int loaded = 0, corrupt = 0;
            for (int it = 0; it < Iterations; it++)
            {
                Count(C);
                string baseText = seeds[r.Next(seeds.Length)];
                string text = r.Next(5) == 0 ? RandomGarbage(r, 1 + r.Next(300)) : MutateText(baseText, r, "{|}|[|]|\"|,|:|null|true|1e999|-1|99999999999|NaN|\\u0000");
                MemoryProfileStorage st = new MemoryProfileStorage();
                st.Text = text;
                ProfileService ps = new ProfileService(st);
                ProfileLoadStatus status = ProfileLoadStatus.Missing;
                Exception e;
                long ms;
                bool done = Guarded(delegate () { status = ps.Load(); }, TimeLimitMs, out e, out ms);
                if (!done) { Fail(C, "HANG"); continue; }
                if (e != null) { Fail(C, "Load threw " + e.GetType().Name + ": " + Trunc(e.Message, 100) + " input=" + Trunc(text, 100)); continue; }
                if (status == ProfileLoadStatus.Loaded) loaded++; else if (status == ProfileLoadStatus.Corrupt) corrupt++;
                if (ps.ReachedCount > 512 || ps.SeenCgs.Count > 512 || ps.Unlocks.Count > 512) Fail(C, "entry cap exceeded");
                // 다시 저장 → 다시 읽기가 안정적이어야 한다(왕복 불변)
                string j1 = ps.ToJson();
                MemoryProfileStorage st2 = new MemoryProfileStorage();
                st2.Text = j1;
                ProfileService ps2 = new ProfileService(st2);
                if (ps2.Load() != ProfileLoadStatus.Loaded || ps2.ToJson() != j1) Fail(C, "profile save/load not stable");
            }
            Console.WriteLine("[{0}] Loaded={1} Corrupt(empty profile fallback)={2}", C, loaded, corrupt);
        }
        // SettingsService.Load: 예외 금지, 값 범위 준수
        {
            const string C = "settings.load";
            Random r = new Random(Seed + 8);
            string good = "{\n  \"textSpeed\": 40,\n  \"masterVolume\": 0.8,\n  \"locale\": \"ko\"\n}";
            for (int it = 0; it < Iterations; it++)
            {
                Count(C);
                string text = r.Next(6) == 0 ? RandomGarbage(r, 1 + r.Next(200)) : MutateText(good, r, "\"|:|,|NaN|Infinity|-Infinity|1e999|-1e999|99999999999999999999|0x10|\\|{|}|null");
                MemorySettingsStorage st = new MemorySettingsStorage();
                st.Text = text;
                SettingsService ss = new SettingsService(st);
                Exception e;
                long ms;
                bool done = Guarded(delegate () { ss.Load(); }, TimeLimitMs, out e, out ms);
                if (!done) { Fail(C, "HANG"); continue; }
                if (e != null) { Fail(C, "Load threw " + e.GetType().Name + ": " + Trunc(e.Message, 100) + " input=" + Trunc(text, 100)); continue; }
                float ts = ss.TextSpeed, mv = ss.MasterVolume;
                if (float.IsNaN(ts) || ts < SettingsService.MinTextSpeed || ts > SettingsService.MaxTextSpeed) Fail(C, "textSpeed out of range: " + ts + " input=" + Trunc(text, 100));
                if (float.IsNaN(mv) || mv < 0f || mv > 1f) Fail(C, "masterVolume out of range: " + mv + " input=" + Trunc(text, 100));
                if (ss.Locale != "ko" && ss.Locale != "en") Fail(C, "locale not in {ko,en}: " + ss.Locale);
            }
            Console.WriteLine("[{0}] done (no exceptions / values clamped)", C);
        }
    }

    // ======================================================================
    // 6) 이름 입력(ProfileValidator) ↔ 세이브 검증 정합성
    // ======================================================================

    private static void CampaignNameValidator()
    {
        const string C = "profile.name_vs_save";
        Random r = new Random(Seed + 9);
        string pool = "가나다ABC12 \t　<>{}\\\u0000\u0001\u202E\u200B\u0301\uD800\uDC00\uD83D\uDE00\uFEFF";
        int accepted = 0, saveBreaking = 0;
        List<string> examples = new List<string>();
        for (int it = 0; it < Iterations; it++)
        {
            Count(C);
            StringBuilder sb = new StringBuilder();
            int n = r.Next(1, 20);
            if (r.Next(10) == 0) n = r.Next(20, 400);
            for (int i = 0; i < n; i++) sb.Append(pool[r.Next(pool.Length)]);
            // 일부는 조합 문자 폭탄
            if (r.Next(8) == 0) sb.Append(Rep("\u0301", r.Next(1, 200)));
            string name = sb.ToString();
            string err;
            Exception e;
            long ms;
            err = null;
            bool done = Guarded(delegate () { err = ProfileValidator.CheckName(name); }, TimeLimitMs, out e, out ms);
            if (!done) { Fail(C, "HANG"); continue; }
            if (e != null) { Fail(C, "CheckName threw " + e.GetType().Name + " for " + Trunc(name, 40)); continue; }
            if (err.Length != 0) continue;
            accepted++;
            string norm = ProfileValidator.NormalizeName(name);
            for (int i = 0; i < norm.Length; i++)
                if (char.IsControl(norm[i]) || "<>{}\\".IndexOf(norm[i]) >= 0) Fail(C, "accepted name contains forbidden char");
            SaveData d = ValidSave(null);
            d.PlayerName = norm;
            if (SaveValidator.Validate(d) != null)
            {
                saveBreaking++;
                if (examples.Count < 3) examples.Add("name utf16Length=" + norm.Length + " textElements=" + new StringInfo(norm).LengthInTextElements + " -> SaveValidator:" + SaveValidator.Validate(d));
            }
        }
        Console.WriteLine("[{0}] names accepted by ProfileValidator={1}; of those, rejected later by SaveValidator (player could not save)={2}", C, accepted, saveBreaking);
        if (saveBreaking > 0)
        {
            Obs("이름 검증 불일치: ProfileValidator.CheckName 은 텍스트 요소(StringInfo) 12개 이하만 보므로 조합 문자가 많은 이름(UTF-16 33자 이상)을 통과시키지만 SaveValidator 는 UTF-16 길이 32 초과를 거부함 -> 저장 시 InvalidOperationException. 예: " + string.Join("; ", examples.ToArray()));
            Fail(C, "inconsistent name limits: " + saveBreaking + " ProfileValidator-valid names fail SaveValidator (" + string.Join("; ", examples.ToArray()) + ")");
        }
        // 또한 알려진 비제어 위험 문자 수용 여부(보고용)
        int[] suspicious = { 0x202E, 0x200B, 0xFEFF, 0xD800 };
        foreach (int cp in suspicious)
        {
            string p = "a" + (char)cp + "b";
            string er = ProfileValidator.CheckName(p);
            if (er.Length == 0) Obs("이름 입력이 수용하는 비가시/방향 제어 문자: U+" + cp.ToString("X4") + " (표시 속임/스푸핑 소지, 로컬 단일 사용자 게임이라 위험 낮음)");
        }
    }

    // ======================================================================
    // 7) 용량 제한 없는 파일 읽기(관찰)
    // ======================================================================

    private static void CampaignOversizedFiles()
    {
        const string C = "oversized_files";
        string dir = Path.Combine(Path.GetTempPath(), "morphe_sectest_big_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (int mb in new int[] { 8, 64 })
            {
                Count(C);
                string path = Path.Combine(dir, "profile" + mb + ".json");
                using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false)))
                {
                    w.Write("{\"endings\":{},\"cgs\":[");
                    string chunk = "\"" + new string('Z', 60) + "\",";
                    long target = (long)mb * 1024 * 1024;
                    long written = 0;
                    while (written < target) { w.Write(chunk); written += chunk.Length; }
                    w.Write("\"end\"],\"unlocks\":[]}");
                }
                ProfileService ps = new ProfileService(new FileProfileStorage(path));
                Exception e;
                long ms;
                ProfileLoadStatus status = ProfileLoadStatus.Missing;
                bool done = Guarded(delegate () { status = ps.Load(); }, 60000, out e, out ms);
                Console.WriteLine("[{0}] profile.json {1}MB: status={2} ex={3} time={4}ms cgs_kept={5} (no size cap before ReadAllText)", C, mb, done ? status.ToString() : "HANG", e == null ? "none" : e.GetType().Name, ms, ps.SeenCgs.Count);
                if (!done) Fail(C, "profile load HANG at " + mb + "MB");
                else if (e != null) Fail(C, "profile load threw " + e.GetType().Name + " at " + mb + "MB");
            }
            Obs("FileProfileStorage/FileSettingsStorage/FileSaveStorage 는 File.ReadAllText 전에 파일 크기 상한이 없음(64MB 프로필도 전부 메모리에 읽음). 로컬 단일 사용자 위협 모델에서는 낮은 위험이나, 외부 입력(클라우드 동기화·공유 폴더)이 생기면 상한(예: 1MB)을 추가해야 함.");
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    // ======================================================================
    // 8) 서브프로세스 프로브(스택 오버플로·무한 루프는 프로세스를 죽이므로 격리해서 시험)
    // ======================================================================

    private static string ProbeText(string kind, int n)
    {
        switch (kind)
        {
            case "paren_expr": return Node("a", "n: {" + Rep("(", n) + "1" + Rep(")", n) + "}");
            case "unary_expr": return Node("a", "n: {" + Rep("!", n) + "1}");
            case "neg_expr": return Node("a", "n: {" + Rep("-", n) + "1}");
            case "call_nest": return Node("a", "n: {" + Rep("max(1,", n) + "1" + Rep(")", n) + "}");
            case "nested_options":
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < n; i++) sb.Append(new string(' ', i)).Append("-> o\n");
                    sb.Append(new string(' ', n)).Append("x");
                    return Node("a", sb.ToString());
                }
            case "nested_if":
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < n; i++) sb.Append("<<if true>>\n");
                    sb.Append("x\n");
                    for (int i = 0; i < n; i++) sb.Append("<<endif>>\n");
                    return Node("a", sb.ToString());
                }
            case "jump_loop": return Node("a", "<<jump a>>");
            case "set_loop": return Node("a", "<<set $x to 1>>\n<<jump b>>") + Node("b", "<<jump a>>");
            default: throw new ArgumentException(kind);
        }
    }

    private static int Probe(string kind, int n)
    {
        try
        {
            string text = ProbeText(kind, n);
            DialogueProject p = DialogueProject.Parse(text, "probe");
            if (kind.EndsWith("_loop"))
            {
                DialogueRunner r = new DialogueRunner(p, new InMemoryVariableStore(), new NullDialogueHost());
                r.Start("a");
                r.Next();
            }
            else
            {
                DialogueRunner r = new DialogueRunner(p, new InMemoryVariableStore(), new NullDialogueHost());
                r.Start("a");
                for (int i = 0; i < 100000; i++) { DialogueStep s = r.Next(); if (s.IsEnd) break; if (s.IsChoices) r.Choose(0); }
            }
            Console.WriteLine("completed");
            return 0;
        }
        catch (DialogueParseException) { Console.WriteLine("DialogueParseException"); return 0; }
        catch (DialogueRuntimeException) { Console.WriteLine("DialogueRuntimeException"); return 0; }
        catch (Exception ex) { Console.WriteLine("UNEXPECTED " + ex.GetType().Name); return 4; }
    }

    private static string RunProbe(string kind, int n, int timeoutMs)
    {
        ProcessStartInfo psi = new ProcessStartInfo("mono", "\"" + _exe + "\" probe " + kind + " " + n);
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        Process pr = Process.Start(psi);
        StringBuilder err = new StringBuilder();
        StringBuilder outb = new StringBuilder();
        pr.ErrorDataReceived += delegate (object s, DataReceivedEventArgs a) { if (a.Data != null && err.Length < 300) err.Append(a.Data).Append(' '); };
        pr.OutputDataReceived += delegate (object s, DataReceivedEventArgs a) { if (a.Data != null) outb.Append(a.Data).Append(' '); };
        pr.BeginErrorReadLine();
        pr.BeginOutputReadLine();
        if (!pr.WaitForExit(timeoutMs))
        {
            try { pr.Kill(); } catch (Exception) { }
            pr.WaitForExit(2000);
            return "HANG(killed after " + timeoutMs + "ms)";
        }
        pr.WaitForExit();
        string stdout = outb.ToString();
        if (pr.ExitCode == 0) return stdout.Trim();
        if (pr.ExitCode == 4) return stdout.Trim();
        return "CRASH(exit=" + pr.ExitCode + ") " + Trunc(err.ToString(), 90);
    }

    // ======================================================================
    // 9) 회귀 시험: 발견 사항 수정(F-1~F-6, F-8)이 유지되는지 직접 단언
    // ======================================================================

    private static void Expect(string C, bool ok, string what)
    {
        Count(C);
        if (!ok) Fail(C, "REGRESSION " + what);
    }

    private static T Catch<T>(Action a) where T : Exception
    {
        try { a(); } catch (T ex) { return ex; } catch (Exception) { return null; }
        return null;
    }

    private static void CampaignRegressions()
    {
        const string C = "regression.fixes";
        Exception e;
        long ms;
        string root = Path.Combine(Path.GetTempPath(), "morphe_sectest_reg_" + Guid.NewGuid().ToString("N"));
        string saves = Path.Combine(root, "saves");
        Directory.CreateDirectory(root);
        try
        {
            // F-1: 이름 거부(ArgumentException), 바깥에 아무것도 생기지 않음
            FileSaveStorage st = new FileSaveStorage(saves);
            foreach (string bad in new string[] { "", "/", "./", "../", "a/", "..", ".", "../x", "a/b", null })
            {
                Expect(C, Catch<ArgumentException>(delegate () { st.Write(bad, "x"); }) != null, "F-1 Write(" + (bad ?? "null") + ") must throw ArgumentException");
            }
            Expect(C, Directory.GetFileSystemEntries(root).Length == 1, "F-1 nothing created outside saves dir");
            st.Write("slot1.json", "{}");
            Expect(C, st.Read("slot1.json") == "{}", "F-1 valid name still works");

            // F-8: 저장 파일 1MB 초과 -> SaveService.Load 는 예외 없이 Corrupt
            File.WriteAllText(Path.Combine(saves, "slot2.json"), new string('a', 1024 * 1024 + 10));
            LoadResult big = null;
            bool done = Guarded(delegate () { big = new SaveService(new JsonSer(), st).Load(2); }, TimeLimitMs, out e, out ms);
            Expect(C, done && e == null && big != null && big.Status == LoadStatus.Corrupt, "F-8 oversized save -> Corrupt without exception");
            // F-8: 프로필/설정
            string pp = Path.Combine(root, "profile.json");
            File.WriteAllText(pp, new string('a', 1024 * 1024 + 10));
            ProfileService ps = new ProfileService(new FileProfileStorage(pp));
            Expect(C, ps.Load() == ProfileLoadStatus.Corrupt, "F-8 oversized profile -> Corrupt");
            string sp = Path.Combine(root, "settings.json");
            File.WriteAllText(sp, new string(' ', 1024 * 1024 + 10) + "\"textSpeed\": 99");
            SettingsService ss = new SettingsService(new FileSettingsStorage(sp));
            Expect(C, !ss.Load() && ss.TextSpeed == SettingsService.DefaultTextSpeed, "F-8 oversized settings ignored (defaults kept)");
        }
        finally { try { Directory.Delete(root, true); } catch (Exception) { } }

        // F-2: 거대 숫자 -> DialogueParseException
        Expect(C, Catch<DialogueParseException>(delegate () { DialogueExpression.Parse(new string('9', 400)); }) != null, "F-2 400-digit literal -> DialogueParseException");
        // F-3: 중첩 상한(표현식·블록)
        Expect(C, Catch<DialogueParseException>(delegate () { DialogueExpression.Parse(Rep("(", 5000) + "1" + Rep(")", 5000)); }) != null, "F-3 5000 nested parens -> DialogueParseException");
        Expect(C, Catch<DialogueParseException>(delegate () { DialogueExpression.Parse(Rep("!", 100000) + "1"); }) != null, "F-3 100000 unary -> DialogueParseException");
        Expect(C, Catch<DialogueParseException>(delegate () { DialogueExpression.Parse(Rep("1+", 5000) + "1"); }) != null, "F-3 5000-term expression -> DialogueParseException");
        Expect(C, Catch<DialogueParseException>(delegate () { DialogueProject.Parse(ProbeText("nested_if", 100000), "t"); }) != null, "F-3 100000 nested <<if>> -> DialogueParseException");
        DialogueParseException pe = Catch<DialogueParseException>(delegate () { DialogueProject.Parse(ProbeText("nested_if", 200), "t"); });
        Expect(C, pe != null && pe.LineNumber > 0, "F-3 nesting error carries a line number");
        Expect(C, Catch<DialogueParseException>(delegate () { DialogueProject.Parse(ProbeText("nested_options", 200), "t"); }) != null, "F-3 200 nested options -> DialogueParseException");
        Exception ok1 = null;
        try { DialogueProject.Parse(ProbeText("nested_if", 20), "t"); DialogueExpression.Parse("((((1+2))))*max(1,2)"); } catch (Exception ex) { ok1 = ex; }
        Expect(C, ok1 == null, "F-3 modest nesting still parses");
        // F-4: 실행기 내부 스텝 상한
        DialogueProject loop = DialogueProject.Parse(ProbeText("jump_loop", 0), "t");
        DialogueRuntimeException re = null;
        bool d2 = Guarded(delegate () { DialogueRunner r = new DialogueRunner(loop, new InMemoryVariableStore(), new NullDialogueHost()); r.Start("a"); try { r.Next(); } catch (DialogueRuntimeException x) { re = x; } }, TimeLimitMs, out e, out ms);
        Expect(C, d2 && re != null, "F-4 self-jump -> DialogueRuntimeException (no hang)");
        DialogueProject loop2 = DialogueProject.Parse(ProbeText("set_loop", 0), "t");
        re = null;
        d2 = Guarded(delegate () { DialogueRunner r = new DialogueRunner(loop2, new InMemoryVariableStore(), new NullDialogueHost()); r.Start("a"); try { r.Next(); } catch (DialogueRuntimeException x) { re = x; } }, TimeLimitMs, out e, out ms);
        Expect(C, d2 && re != null, "F-4 a<->b jump cycle -> DialogueRuntimeException (no hang)");
        // F-4: 린트가 무음 점프 순환을 보고
        Expect(C, DialogueLint.Count(DialogueLint.Run(loop2, null), "SILENT_JUMP_CYCLE") >= 1, "F-4 lint reports silent jump cycle (a<->b)");
        Expect(C, DialogueLint.Count(DialogueLint.Run(loop, null), "SILENT_JUMP_CYCLE") >= 1, "F-4 lint reports silent self-jump");
        DialogueProject audible = DialogueProject.Parse(Node("a", "x: hi\n<<jump a>>"), "t");
        Expect(C, DialogueLint.Count(DialogueLint.Run(audible, null), "SILENT_JUMP_CYCLE") == 0, "F-4 lint: cycle with a line is not reported");
        // F-5: 태그 상한
        Expect(C, Catch<DialogueParseException>(delegate () { DialogueProject.Parse(Node("a", "howl: hi" + Rep(" #t", 33)), "t"); }) != null, "F-5 33 tags on a line -> DialogueParseException");
        Expect(C, Catch<DialogueParseException>(delegate () { DialogueProject.Parse(Node("a", "-> hi" + Rep(" #t", 33) + "\n    x"), "t"); }) != null, "F-5 33 tags on an option -> DialogueParseException");
        Exception ok2 = null;
        try { DialogueProject.Parse(Node("a", "howl: hi" + Rep(" #t", 32)), "t"); } catch (Exception ex) { ok2 = ex; }
        Expect(C, ok2 == null, "F-5 32 tags still parse");
        // F-6: 이름 검증 일관성
        foreach (string bad in new string[] { "a" + Rep("\u0301", 40), "\u202Eabc", "a\u200Bb", "\uFEFFabc", "a\uD800", "\uDC00a", "a\u2066b", "a\u200Fb" })
            Expect(C, ProfileValidator.CheckName(bad).Length != 0, "F-6 name rejected: U+" + ((int)bad[bad.Length > 1 ? 1 : 0]).ToString("X4"));
        foreach (string good in new string[] { "라이언", "Ryan", "하늘 이", "a\uD83D\uDE00" })
            Expect(C, ProfileValidator.CheckName(good).Length == 0, "F-6 legitimate name accepted: " + good);
        Console.WriteLine("[{0}] direct regression assertions for F-1..F-6, F-8 executed", C);
    }

    // ======================================================================
    // 10) 회귀 시험: docs/19_Security_Review.md 의 수정(N-1~N-5, N-7, N-8)
    // ======================================================================

    private static string Slurp(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
    }

    private static void CampaignReviewRegressions()
    {
        const string C = "regression.review";
        string S = "UnityProject/Assets/_Project/Scripts/";
        string root = Path.Combine(Path.GetTempPath(), "morphe_sectest_rev_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // N-5: 손상된 primary 는 .corrupt 로 보존, 직전 정상 .bak 은 덮어쓰지 않음, 정상 교체 시 .bak 갱신
            string dir = Path.Combine(root, "saves");
            FileSaveStorage st = new FileSaveStorage(dir);
            SaveService svc = new SaveService(new JsonSer(), st);
            SaveData a = ValidSave(null); a.Gold = 111f;
            svc.Save(1, a);
            string firstText = File.ReadAllText(Path.Combine(dir, "slot1.json"));
            SaveData b = ValidSave(null); b.Gold = 222f;
            svc.Save(1, b);
            Expect(C, File.Exists(Path.Combine(dir, "slot1.json.bak")) && File.ReadAllText(Path.Combine(dir, "slot1.json.bak")) == firstText, "N-5 normal overwrite keeps previous good save as .bak");
            string goodBak = File.ReadAllText(Path.Combine(dir, "slot1.json.bak"));
            File.WriteAllText(Path.Combine(dir, "slot1.json"), "{garbage");
            SaveData c = ValidSave(null); c.Gold = 333f;
            svc.Save(1, c);
            Expect(C, File.Exists(Path.Combine(dir, "slot1.json.corrupt")) && File.ReadAllText(Path.Combine(dir, "slot1.json.corrupt")) == "{garbage", "N-5 corrupt primary kept as .corrupt");
            Expect(C, File.ReadAllText(Path.Combine(dir, "slot1.json.bak")) == goodBak, "N-5 good .bak not overwritten by unverified primary");
            LoadResult lr = svc.Load(1);
            Expect(C, lr.Status == LoadStatus.Ok && Math.Abs(lr.Data.Gold - 333f) < 0.01f, "N-5 save after corruption loads Ok with new data");

            // N-4: 프로필·설정
            string pp = Path.Combine(root, "profile.json");
            File.WriteAllText(pp, "not json at all");
            ProfileService ps = new ProfileService(new FileProfileStorage(pp));
            Expect(C, ps.Load() == ProfileLoadStatus.Corrupt && File.Exists(pp + ".corrupt") && Slurp(pp + ".corrupt") == "not json at all", "N-4 corrupt profile preserved as .corrupt");
            Expect(C, ps.Save() && File.Exists(pp), "N-4 profile saved after corrupt start");
            Expect(C, ps.Save() && File.Exists(pp + ".bak"), "N-4 second profile save replaces atomically and keeps .bak");
            ProfileService ps2 = new ProfileService(new FileProfileStorage(pp));
            Expect(C, ps2.Load() == ProfileLoadStatus.Loaded, "N-4 profile reloads after replace");
            string sp = Path.Combine(root, "settings.json");
            File.WriteAllText(sp, "garbage!");
            SettingsService ss = new SettingsService(new FileSettingsStorage(sp));
            Expect(C, !ss.Load() && File.Exists(sp + ".corrupt"), "N-4 corrupt settings preserved as .corrupt");
            Expect(C, ss.Save() && ss.Save() && File.Exists(sp + ".bak"), "N-4 settings save keeps .bak");
        }
        finally { try { Directory.Delete(root, true); } catch (Exception) { } }

        // N-2/N-3: 디스패처 숫자 인자·플래그 상한
        {
            string bal = "balance";
            if (Directory.Exists(bal))
            {
                FileTextSource src = new FileTextSource(bal);
                BalanceTable bt = BalanceTable.Load(src, MorpheHouse.Core.Difficulty.Normal);
                MorpheHouse.Romance.RomanceCatalog cat = MorpheHouse.Romance.RomanceCatalog.Load(src);
                GameState gs = GameState.NewGame(bt, MorpheHouse.Core.Difficulty.Normal, "민준");
                gs.Chapter = 3; gs.Day = 10; gs.Phase = MorpheHouse.Core.DayPhase.Night; gs.ActionsLeft = 3;
                MorpheHouse.Romance.RomanceService rs = new MorpheHouse.Romance.RomanceService(gs, bt, cat, new Random(1).NextDouble);
                DialogueCommandDispatcher d = new DialogueCommandDispatcher(gs, rs, new NullVnSink());
                float before = gs.Chars["howl"].Aff;
                d.Handle("aff", new string[] { "howl", "NaN" });
                d.Handle("aff", new string[] { "howl", "Infinity" });
                d.Handle("trust", new string[] { "howl", "-Infinity" });
                Expect(C, !float.IsNaN(gs.Chars["howl"].Aff) && Math.Abs(gs.Chars["howl"].Aff - before) < 0.0001f && !float.IsInfinity(gs.Chars["howl"].Trust), "N-2 aff/trust NaN/Infinity rejected (state unchanged)");
                d.Handle("flag", new string[] { "big", "1e30" });
                d.Handle("flag", new string[] { "big2", "NaN" });
                Expect(C, !gs.Flags.ContainsKey("big") && !gs.Flags.ContainsKey("big2"), "N-2 flag 1e30 / NaN rejected");
                d.Handle("flag", new string[] { "ok_flag", "5" });
                Expect(C, gs.Flags.ContainsKey("ok_flag") && gs.Flags["ok_flag"] == 5, "N-2 normal flag still works");
                d.Handle("flag", new string[] { new string('k', 65) });
                Expect(C, !gs.Flags.ContainsKey(new string('k', 65)), "N-3 65-char flag key ignored");
                for (int i = 0; i < 3000; i++) d.Handle("flag", new string[] { "f" + i });
                Expect(C, gs.Flags.Count <= DialogueCommandDispatcher.MaxFlagCount, "N-3 flag count capped at " + DialogueCommandDispatcher.MaxFlagCount + " (got " + gs.Flags.Count + ")");
                Expect(C, d.Warnings.Count > 0, "N-2/N-3 warnings recorded");
            }
            else Expect(C, false, "balance dir missing");
        }

        // N-1/N-7/N-8: 소스·스크립트 정적 확인(Unity/네트워크 없이 확인 가능한 범위)
        Expect(C, Slurp(S + "UI/UiKit.cs").Contains("richText = false"), "N-1 UiKit.Label sets richText=false");
        string vn = Slurp(S + "UI/VnStage.cs");
        Expect(C, vn.Contains("_nameText.richText = false") && vn.Contains("_bodyText.richText = false"), "N-1 VnStage name/body richText=false");
        string run = Slurp("tools/unity_compile_check/run.sh");
        Expect(C, run.Contains("mktemp -d") && run.Contains("UM_SHA256=") && run.Contains("sha256sum -c") && run.Contains("UGUI_SHA=") && run.Contains("ISYS_SHA=") && !run.Contains("-b 6000.0"), "N-7 compile check: mktemp work dir, pinned SHAs, sha256 check, no floating branch");
        string gi = Slurp(".gitignore");
        foreach (string pat in new string[] { "*.keystore", "*.jks", "*.pfx", "*.p12", "*.pem", "*.key", "*.p8", "keystore*.properties", "local.properties", "google-services.json", ".env", "*.apk", "*.aab", "Builds/", "UnityProject/[Ll]ibrary/" })
            Expect(C, gi.Contains(pat), "N-8 .gitignore contains " + pat);
        string pkg = Slurp("tools/package_source.sh");
        Expect(C, pkg.Contains("pem|key|p8") && pkg.Contains("keystore[^/]*") && pkg.Contains("google-services"), "N-8 package_source.sh secret filter matches release_check.sh");
        Console.WriteLine("[{0}] review-finding regression assertions executed", C);
    }

    private static void CampaignSubprocessProbes()
    {
        const string C = "subprocess.depth_and_loops";
        Console.WriteLine("[{0}] isolated child-process probes (a stack overflow kills the whole process, so these cannot run in-process)", C);
        string[] kinds = { "paren_expr", "unary_expr", "neg_expr", "call_nest", "nested_options", "nested_if" };
        int[] depths = { 100, 1000, 5000, 20000, 100000 };
        foreach (string kind in kinds)
        {
            // nested_options 는 들여쓰기 때문에 입력 크기가 깊이의 제곱이라 깊이를 제한한다.
            if (kind == "nested_options") depths = new int[] { 100, 1000, 5000, 10000 }; else depths = new int[] { 100, 1000, 5000, 20000, 100000 };
            string firstBad = null;
            StringBuilder line = new StringBuilder();
            foreach (int d in depths)
            {
                Count(C);
                string res = RunProbe(kind, d, 20000);
                bool bad = res.StartsWith("CRASH") || res.StartsWith("HANG") || res.StartsWith("UNEXPECTED");
                line.Append(d).Append('=').Append(bad ? res : (res.Length > 24 ? res.Substring(0, 24) : res)).Append(" | ");
                if (bad && firstBad == null) firstBad = d + ":" + res;
            }
            Console.WriteLine("    {0,-15} {1}", kind, line.ToString());
            if (firstBad != null)
            {
                Obs("재귀 깊이 제한 없음: " + kind + " 입력이 깊이 " + firstBad.Split(':')[0] + " 에서 " + firstBad.Substring(firstBad.IndexOf(':') + 1) + " (DialogueParseException 이 아니라 프로세스 크래시/중단)");
                Fail(C, kind + " first failing depth " + firstBad);
            }
        }
        foreach (string kind in new string[] { "jump_loop", "set_loop" })
        {
            Count(C);
            string res = RunProbe(kind, 0, 4000);
            Console.WriteLine("    {0,-15} first runner.Next() => {1}", kind, res);
            if (res.StartsWith("HANG"))
            {
                Obs("실행기 무한 루프: <<jump>> 순환 스크립트는 DialogueRunner.Next() 한 번 호출 안에서 반환하지 않음(스텝 상한/순환 감지 없음). 스크립트는 신뢰 콘텐츠(번들)지만 린트가 순환을 막지 못하면 게임이 멈춤.");
                Fail(C, kind + " -> runner hang (no step budget)");
            }
        }
    }
}
