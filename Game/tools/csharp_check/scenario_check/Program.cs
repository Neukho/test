using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;

// 시나리오 QA 하네스: 린트 + 헤드리스 실행(성별 4 x 로케일 2 x 호감 구간 3, 선택지 전 경로) + 성별 중립 검사 + 분량표.
// 빌드/실행: tools/csharp_check/scenario_check/run.sh
public static class Program
{
    // 작가가 직접 시작하는 진입 노드(호스트가 Start 하는 노드).
    // 진입 노드 목록: Dialogue 폴더 아래의 모든 entries*.txt(한 줄에 노드 하나, # 주석)에서 자동 수집한다.
    // 작가별로 파일을 나눠 두면 병렬 작업 시 충돌하지 않는다.
    private static string[] Entries = new string[0];

    // 환경변수 SCENARIO_ONLY=routes/johan 이면 common/ 과 해당 폴더만 검사한다(병렬 작업 시 다른 작가의 작업 중 파일을 제외).
    private static bool InScope(string path)
    {
        string only = Environment.GetEnvironmentVariable("SCENARIO_ONLY");
        if (string.IsNullOrEmpty(only)) return true;
        string norm = path.Replace('\\', '/');
        return norm.Contains("/common/") || norm.Contains("/" + only.Trim('/') + "/");
    }

    private static string[] LoadEntries(string dialogueDir)
    {
        List<string> list = new List<string>();
        foreach (string file in Directory.GetFiles(dialogueDir, "entries*.txt", SearchOption.AllDirectories))
        {
            if (!InScope(file)) continue;
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (!list.Contains(line)) list.Add(line);
            }
        }
        return list.ToArray();
    }

    private static readonly string[] Genders = { "male", "female", "nonbinary", "neutral" };
    private static readonly string[] Locales = { "ko", "en" };
    private static readonly float[] AffLevels = { 5f, 45f, 75f };
    private static readonly string[] Expressions = { "neutral", "smile", "shy", "angry", "sad", "surprised" };
    private static readonly string[] GiftNames = { "가죽 손목 보호대", "따뜻한 목도리", "소음 파티 세트" };

    private static readonly HashSet<string> Speakers = new HashSet<string>(new[]
    {
        "player", "howl", "johan", "beomjun", "haru", "gomseok", "nabi", "npc_baker", "npc_fisher", "npc_florist"
    });

    // 하드코딩된 성별 단어(주인공 지칭/호칭). 함수 호출 {...} 바깥에서만 검사한다.
    private static readonly Regex GenderWords = new Regex(
        "(그녀|(?<![가-힣])그(?=(는|가|를|의|에게|도|와|랑|한테)(?![가-힣]))|(?<![가-힣])(오빠|언니|누나|형|남자|여자|아저씨|아줌마)(은|는|이|가|을|를|의|도|한테|에게|랑|와|과)?(?![가-힣]))",
        RegexOptions.Compiled);
    private static readonly Regex Braces = new Regex("\\{[^}]*\\}", RegexOptions.Compiled);

    private static readonly List<string> FlagNames = new List<string>();
    private static int _failures;
    private static int _checks;
    private static readonly List<string> Problems = new List<string>();

    private sealed class Env
    {
        public GameState State;
        public SimpleDialogueHost Host;
        public List<string> Commands = new List<string>();
        public string Error;
    }

    // 문자열($gift_name 등)은 메모리, 나머지는 GameFlags(가상 변수 $aff_howl 포함).
    private sealed class MixedStore : IVariableStore
    {
        private readonly GameFlagsVariableStore _flags;
        private readonly Dictionary<string, DialogueValue> _mem = new Dictionary<string, DialogueValue>();
        public MixedStore(GameState s) { _flags = new GameFlagsVariableStore(s); }
        public bool TryGet(string name, out DialogueValue value)
        {
            if (_mem.TryGetValue(name, out value)) return true;
            return _flags.TryGet(name, out value);
        }
        public void Set(string name, DialogueValue value)
        {
            if (value.IsString) _mem[name] = value; else _flags.Set(name, value);
        }
    }

    private sealed class PathResult
    {
        public int Lines;
        public int Choices;
        public List<int> Picked = new List<int>();
        public List<List<int>> Siblings = new List<List<int>>();
    }

    public static int Main(string[] args)
    {
        Entries = LoadEntries(args[0]);
        if (Entries.Length == 0) { Console.WriteLine("진입 노드 목록(entries*.txt)이 없습니다."); return 1; }

        Console.OutputEncoding = new UTF8Encoding(false);
        string root = args.Length > 0 ? args[0] : "UnityProject/Assets/_Project/Dialogue";
        string csvPath = args.Length > 1 ? args[1] : "address_terms.csv";
        string csv = File.ReadAllText(csvPath);
        AddressBook book = AddressBook.FromCsv(csv);

        Console.WriteLine("== 로드");
        List<string> scoped = new List<string>();
        foreach (string f in Directory.GetFiles(root, "*.yarn", SearchOption.AllDirectories))
            if (InScope(f)) scoped.Add(f);
        string[] files = scoped.ToArray();
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string ff in files)
            foreach (Match fm in Regex.Matches(File.ReadAllText(ff), "flag\\(\"([a-z0-9_]+)\"\\)"))
                if (!FlagNames.Contains(fm.Groups[1].Value)) FlagNames.Add(fm.Groups[1].Value);
        DialogueProject project = new DialogueProject();
        Dictionary<string, List<DialogueNode>> nodesByFile = new Dictionary<string, List<DialogueNode>>();
        foreach (string f in files)
        {
            string rel = f.Substring(root.Length).TrimStart('/', '\\');
            try
            {
                int before = project.AllNodes.Count;
                project.AddSource(File.ReadAllText(f), rel);
                List<DialogueNode> l = new List<DialogueNode>();
                for (int i = before; i < project.AllNodes.Count; i++) l.Add(project.AllNodes[i]);
                nodesByFile[rel] = l;
                Check(rel + " 파싱", true, null);
            }
            catch (DialogueParseException e)
            {
                Check(rel + " 파싱", false, e.Message);
            }
        }

        LintAll(project);
        StaticChecks(project, nodesByFile);
        RunAll(project, book, csv);

        Dump(project, book);
        Console.WriteLine();
        Console.WriteLine("checks={0} failures={1}", _checks, _failures);
        if (_failures > 0)
        {
            Console.WriteLine("-- 문제 목록(최대 40개)");
            for (int i = 0; i < Problems.Count && i < 40; i++) Console.WriteLine("  " + Problems[i]);
        }
        Console.WriteLine(_failures == 0 ? "ALL PASSED" : "FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok, string detail)
    {
        _checks++;
        if (!ok)
        {
            _failures++;
            Problems.Add(name + (detail == null ? string.Empty : " :: " + detail));
        }
        Console.WriteLine("  [{0}] {1}{2}", ok ? "PASS" : "FAIL", name, ok || detail == null ? string.Empty : "  " + detail);
    }

    // ---------- (a) 린트 ----------
    private static void LintAll(DialogueProject project)
    {
        Console.WriteLine("== 린트 (진입 노드 " + Entries.Length + "개)");
        List<LintIssue> issues = DialogueLint.Run(project, Entries);
        int errors = 0, warnings = 0;
        foreach (LintIssue i in issues)
        {
            if (i.Severity == LintSeverity.Error) errors++; else warnings++;
            Console.WriteLine("    " + i);
        }
        Check("린트 오류 0건 (현재 " + errors + ")", errors == 0, null);
        Check("DUP_NODE 0건", DialogueLint.Count(issues, "DUP_NODE") == 0, null);
        Check("DUP_LINE_ID 0건", DialogueLint.Count(issues, "DUP_LINE_ID") == 0, null);
        Check("MISSING_LINE_ID 0건", DialogueLint.Count(issues, "MISSING_LINE_ID") == 0, null);
        Check("UNDEFINED_JUMP/ENTRY 0건", DialogueLint.Count(issues, "UNDEFINED_JUMP") + DialogueLint.Count(issues, "UNDEFINED_ENTRY") == 0, null);
        Check("UNREACHABLE 0건 (경고 총 " + warnings + ")", DialogueLint.Count(issues, "UNREACHABLE") == 0, null);
    }

    // ---------- (c) 정적 검사 + 분량표 ----------
    private static void StaticChecks(DialogueProject project, Dictionary<string, List<DialogueNode>> byFile)
    {
        Console.WriteLine("== 정적 검사(성별 단어·화자·줄 id 형식)");
        Regex idFmt = new Regex("^[a-z][a-z0-9]*(_[a-z0-9]+)*_[0-9]{3}$");
        int badGender = 0, badSpeaker = 0, badId = 0;
        Dictionary<string, int[]> table = new Dictionary<string, int[]>(); // file -> nodes, lines, options, words, chars
        List<string> keys = new List<string>(byFile.Keys);
        foreach (string file in keys)
        {
            int[] t = new int[5];
            t[0] = byFile[file].Count;
            foreach (DialogueNode n in byFile[file])
            {
                DialogueNode node = n;
                string fileName = file;
                StatementWalker.Walk(node.Body, delegate (Statement s)
                {
                    LineStatement ls = s as LineStatement;
                    if (ls == null) return;
                    t[1]++;
                    CountWords(ls.Text.Source, t);
                    if (ls.Speaker != null && !Speakers.Contains(ls.Speaker) && !ls.Speaker.StartsWith("npc_")) { badSpeaker++; Problems.Add("화자 id 미등록 " + ls.Speaker + " @" + fileName + ":" + ls.LineNumber); }
                    if (ls.LineId == null || !idFmt.IsMatch(ls.LineId)) { badId++; Problems.Add("줄 id 형식 " + ls.LineId + " @" + fileName + ":" + ls.LineNumber); }
                    badGender += GenderScan(ls.Text.Source, fileName, ls.LineNumber);
                }, delegate (OptionItem o)
                {
                    t[2]++;
                    CountWords(o.Text.Source, t);
                    if (o.LineId == null || !idFmt.IsMatch(o.LineId)) { badId++; Problems.Add("선택지 id 형식 " + o.LineId + " @" + fileName + ":" + o.LineNumber); }
                    badGender += GenderScan(o.Text.Source, fileName, o.LineNumber);
                });
            }
            table[file] = t;
        }
        Check("하드코딩 성별 단어 0건 (현재 " + badGender + ")", badGender == 0, null);
        Check("미등록 화자 0건 (현재 " + badSpeaker + ")", badSpeaker == 0, null);
        Check("줄 id 형식(접두사_NNN) 위반 0건 (현재 " + badId + ")", badId == 0, null);
        if (badGender + badSpeaker + badId > 0) _failures += 0;

        Console.WriteLine();
        Console.WriteLine("== 분량표 (파일별: 노드 / 대사·지문 줄 / 선택지 / 어절 / 글자(공백 제외))");
        int[] total = new int[5];
        foreach (string file in keys)
        {
            int[] t = table[file];
            Console.WriteLine("  {0,-34} {1,3} {2,5} {3,4} {4,6} {5,7}", file, t[0], t[1], t[2], t[3], t[4]);
            for (int i = 0; i < 5; i++) total[i] += t[i];
        }
        Console.WriteLine("  {0,-34} {1,3} {2,5} {3,4} {4,6} {5,7}", "합계", total[0], total[1], total[2], total[3], total[4]);
        Console.WriteLine();
        Console.WriteLine("== 노드별 정적 줄 수(대사+선택지)");
        foreach (DialogueNode n in project.AllNodes)
        {
            int c = DialogueLint.CollectLines(n).Count;
            Console.WriteLine("  {0,-26} {1,3}  ({2})", n.Title, c, n.SourceName);
        }
    }

    private static void CountWords(string src, int[] t)
    {
        string plain = Braces.Replace(src, "X");
        t[3] += plain.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        for (int i = 0; i < plain.Length; i++) if (!char.IsWhiteSpace(plain[i])) t[4]++;
    }

    private static int GenderScan(string src, string file, int line)
    {
        string outside = Braces.Replace(src, string.Empty);
        Match m = GenderWords.Match(outside);
        if (!m.Success) return 0;
        Problems.Add("성별 단어 '" + m.Value + "' @" + file + ":" + line + " :: " + src);
        return 1;
    }

    // ---------- (b) 헤드리스 실행 ----------
    private static Env MakeEnv(string gender, string locale, float aff, string giftName, AddressBook book, bool preFlags)
    {
        Env e = new Env();
        e.State = new GameState();
        e.State.PlayerName = "하늘";
        e.State.Gender = gender;
        for (int i = 0; i < CharIds.All.Length; i++) e.State.Chars[CharIds.All[i]] = new CharState();
        // 모든 공략 대상의 호감/신뢰를 같은 구간으로 설정해 캐릭터별 호감 분기($aff_<id>)를 전부 실행 검증한다.
        for (int i = 0; i < CharIds.All.Length; i++)
        {
            e.State.Chars[CharIds.All[i]].Aff = aff;
            e.State.Chars[CharIds.All[i]].Trust = 10f;
        }
        if (preFlags) foreach (string fn in FlagNames) e.State.Flags[fn] = 1;
        if (CurLock.Length > 0) e.State.SetLock(new List<string>(CurLock));
        PronounResolver pr = new PronounResolver(e.State, locale, book);
        e.Host = new SimpleDialogueHost();
        e.Host.AddProvider(pr);
        GameState st = e.State;
        Env env = e;
        e.Host.RegisterFunction("affinity", delegate (DialogueValue[] a) { return DialogueValue.FromNumber(st.AffinityOf(a[0].AsString)); });
        e.Host.RegisterFunction("flag", delegate (DialogueValue[] a)
        {
            int v;
            return DialogueValue.FromBool(st.Flags.TryGetValue(a[0].AsString, out v) && v != 0);
        });
        e.Host.RegisterFunction("route_locked", delegate (DialogueValue[] a) { return DialogueValue.FromBool(a.Length > 0 ? st.IsRouteMember(a[0].AsString) : st.HasRouteLock); });
        e.Host.RegisterFunction("group_size", delegate (DialogueValue[] a) { return DialogueValue.FromNumber(st.LockedCount); });
        e.Host.RegisterCommand("route_choice", delegate (string[] a) { env.Commands.Add("route_choice"); });
        // 선택 커맨드(오디오): 현재 스크립트는 쓰지 않지만 쓰게 되면 하네스가 실패하지 않도록 무시 등록한다.
        e.Host.RegisterCommand("bgm", delegate (string[] a) { env.Commands.Add("bgm"); });
        e.Host.RegisterCommand("bgm_stop", delegate (string[] a) { env.Commands.Add("bgm_stop"); });
        e.Host.RegisterCommand("se", delegate (string[] a) { env.Commands.Add("se"); });
        e.Host.RegisterCommand("hide_standing", delegate (string[] a) { env.Commands.Add("hide_standing"); });
        e.Host.RegisterCommand("aff", delegate (string[] a) { Delta(env, a, true); });
        e.Host.RegisterCommand("trust", delegate (string[] a) { Delta(env, a, false); });
        e.Host.RegisterCommand("flag", delegate (string[] a)
        {
            env.Commands.Add("flag " + string.Join(" ", a));
            if (a.Length < 1 || a.Length > 2 || !Regex.IsMatch(a[0], "^[a-z][a-z0-9_]*$")) { env.Error = "flag 인자 오류: " + string.Join(" ", a); return; }
            st.Flags[a[0]] = 1;
        });
        e.Host.RegisterCommand("show_standing", delegate (string[] a)
        {
            env.Commands.Add("show_standing " + string.Join(" ", a));
            if (a.Length < 2 || a.Length > 3 || Array.IndexOf(CharIds.All, a[0]) < 0 || Array.IndexOf(Expressions, a[1]) < 0)
                env.Error = "show_standing 인자 오류: " + string.Join(" ", a);
        });
        e.Host.RegisterCommand("show_cg", delegate (string[] a)
        {
            env.Commands.Add("show_cg " + string.Join(" ", a));
            if (a.Length != 1 || !Regex.IsMatch(a[0], "^[A-Z]+_[A-Z0-9]+$")) env.Error = "show_cg 인자 오류: " + string.Join(" ", a);
        });
        e.Host.RegisterCommand("hide_cg", delegate (string[] a)
        {
            env.Commands.Add("hide_cg");
            if (a.Length != 0) env.Error = "hide_cg 인자 오류";
        });
        e.Host.RegisterCommand("bg", delegate (string[] a)
        {
            env.Commands.Add("bg " + string.Join(" ", a));
            if (a.Length != 2) env.Error = "bg 인자 오류: " + string.Join(" ", a);
        });
        e.Host.RegisterCommand("route_lock", delegate (string[] a)
        {
            env.Commands.Add("route_lock " + string.Join(" ", a));
            if (a.Length != 1 || Array.IndexOf(CharIds.All, a[0]) < 0) env.Error = "route_lock 인자 오류";
        });
        e.Host.RegisterCommand("end_event", delegate (string[] a)
        {
            env.Commands.Add("end_event " + string.Join(" ", a));
            int step;
            if (a.Length != 2 || Array.IndexOf(CharIds.All, a[0]) < 0 || !int.TryParse(a[1], out step) || step < 1 || step > 5)
                env.Error = "end_event 인자 오류: " + string.Join(" ", a);
        });
        return e;
    }

    private static void Delta(Env env, string[] a, bool isAff)
    {
        string name = isAff ? "aff" : "trust";
        env.Commands.Add(name + " " + string.Join(" ", a));
        double n;
        if (a.Length != 2 || Array.IndexOf(CharIds.All, a[0]) < 0 ||
            !double.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out n))
        {
            env.Error = name + " 인자 오류: " + string.Join(" ", a);
            return;
        }
        CharState c = env.State.Chars[a[0]];
        if (isAff) c.Aff = (float)Math.Max(0, Math.Min(100, c.Aff + n));
        else c.Trust = (float)Math.Max(0, Math.Min(50, c.Trust + n));
    }

    private static string[] CurLock = new string[0];
    private static IEnumerable<string[]> Combos()
    {
        yield return new string[0];
        string[] a = CharIds.All;
        for (int i = 0; i < a.Length; i++) yield return new[] { a[i] };
        for (int i = 0; i < a.Length; i++) for (int j = i + 1; j < a.Length; j++) yield return new[] { a[i], a[j] };
        for (int i = 0; i < a.Length; i++) for (int j = i + 1; j < a.Length; j++) for (int k = j + 1; k < a.Length; k++) yield return new[] { a[i], a[j], a[k] };
    }
    private static int _runs;
    private static int _lineSteps;
    private static int _runtimeErrors;
    private static int _unresolved;
    private static int _genderRendered;

    private static void RunAll(DialogueProject project, AddressBook book, string csv)
    {
        Console.WriteLine();
        Console.WriteLine("== 헤드리스 실행: 성별 4 x 로케일 2 x 호감 3 x 플래그 사전설정 2 x 전 경로(선물 노드는 x 선물명 3)");
        Console.WriteLine("  {0,-22} {1,6} {2,9} {3,9} {4,8}", "진입 노드", "경로수", "줄수(최소)", "줄수(최대)", "선택지수");
        foreach (string entry in Entries)
        {
            if (!project.Contains(entry)) continue;
            int minLines = int.MaxValue, maxLines = 0, maxPaths = 0, maxChoices = 0;
            foreach (string[] lk in Combos())
            foreach (string g in Genders)
            foreach (string loc in Locales)
            foreach (float aff in AffLevels)
            foreach (string gift in GiftNames)
            foreach (bool pre in new[] { false, true })
            {
                if (!entry.Contains("_Gift_") && gift != GiftNames[0]) continue;
                CurLock = lk;
                List<PathResult> results = ExploreAll(project, book, entry, g, loc, aff, gift, pre);
                if (results.Count > maxPaths) maxPaths = results.Count;
                foreach (PathResult r in results)
                {
                    if (r.Lines < minLines) minLines = r.Lines;
                    if (r.Lines > maxLines) maxLines = r.Lines;
                    if (r.Choices > maxChoices) maxChoices = r.Choices;
                }
            }
            Console.WriteLine("  {0,-22} {1,6} {2,9} {3,9} {4,8}", entry, maxPaths, minLines, maxLines, maxChoices);
        }
        Check("실행 오류 0건 (실행 " + _runs + "회, 줄 " + _lineSteps + ")", _runtimeErrors == 0, null);
        Check("미해결 { } 0건 (현재 " + _unresolved + ")", _unresolved == 0, null);
        Check("논바이너리/중립 렌더 결과에 성별 호칭 0건 (현재 " + _genderRendered + ")", _genderRendered == 0, null);
    }

    // 선택지 전 경로 열거: 새 선택 지점에서는 첫 활성 선택지를 고르고, 나머지 활성 선택지는 형제 경로로 큐에 넣는다.
    private static List<PathResult> ExploreAll(DialogueProject project, AddressBook book, string entry, string gender, string locale, float aff, string gift, bool pre)
    {
        List<PathResult> done = new List<PathResult>();
        Stack<List<int>> work = new Stack<List<int>>();
        work.Push(new List<int>());
        int cap = 20000;
        while (work.Count > 0 && cap-- > 0)
        {
            List<int> prefix = work.Pop();
            PathResult r = RunPath(project, book, entry, gender, locale, aff, gift, prefix, pre);
            done.Add(r);
            foreach (List<int> sib in r.Siblings) work.Push(sib);
        }
        if (work.Count > 0) { _runtimeErrors++; Problems.Add("경로 폭발: " + entry); }
        return done;
    }

    private static PathResult RunPath(DialogueProject project, AddressBook book, string entry, string gender, string locale, float aff, string gift, List<int> prefix, bool pre)
    {
        PathResult res = new PathResult();
        Env env = MakeEnv(gender, locale, aff, gift, book, pre);
        string tag = entry + "[" + gender + "/" + locale + "/aff" + aff + (pre ? "/preflags" : "") + "] path=" + string.Join(",", prefix.ConvertAll<string>(delegate (int x) { return x.ToString(); }).ToArray());
        _runs++;
        try
        {
            MixedStore store = new MixedStore(env.State);
            store.Set("$gift_name", DialogueValue.FromString(gift));
            DialogueRunner runner = new DialogueRunner(project, store, env.Host);
            runner.Start(entry);
            int depth = 0;
            bool nb = gender == "nonbinary" || gender == "neutral";
            for (int guard = 0; guard < 20000; guard++)
            {
                DialogueStep s = runner.Next();
                if (env.Error != null) { Fail(tag, env.Error); break; }
                if (s.IsEnd) break;
                if (s.IsCommand) { Fail(tag, "호스트가 처리하지 않은 커맨드: " + s.CommandName); break; }
                if (s.IsLine)
                {
                    res.Lines++;
                    _lineSteps++;
                    CheckText(tag, s.Text, nb, s.Speaker);
                }
                else if (s.IsChoices)
                {
                    res.Choices++;
                    List<int> avail = new List<int>();
                    for (int i = 0; i < s.Options.Count; i++)
                    {
                        CheckText(tag, s.Options[i].Text, nb, "choice");
                        if (s.Options[i].Available) avail.Add(i);
                    }
                    if (avail.Count == 0) { Fail(tag, "활성 선택지 없음"); break; }
                    int pick;
                    if (depth < prefix.Count) pick = prefix[depth];
                    else
                    {
                        pick = avail[0];
                        for (int k = 1; k < avail.Count; k++)
                        {
                            List<int> sib = new List<int>(res.Picked);
                            sib.Add(avail[k]);
                            res.Siblings.Add(sib);
                        }
                    }
                    res.Picked.Add(pick);
                    depth++;
                    runner.Choose(pick);
                }
            }
            if (runner.IsRunning) Fail(tag, "종료되지 않음");
            if (runner.Warnings.Count > 0) Fail(tag, "러너 경고: " + runner.Warnings[0]);
        }
        catch (Exception ex)
        {
            Fail(tag, ex.GetType().Name + ": " + ex.Message);
        }
        return res;
    }

    // DUMP=노드,성별,호감,선택(쉼표없이 숫자열 예 01) 환경변수로 한 경로를 텍스트로 출력(작가 검수용).
    private static void Dump(DialogueProject project, AddressBook book)
    {
        string d = Environment.GetEnvironmentVariable("DUMP");
        if (string.IsNullOrEmpty(d)) return;
        string[] p = d.Split(',');
        Env env = MakeEnv(p.Length > 1 ? p[1] : "male", "ko", p.Length > 2 ? float.Parse(p[2], CultureInfo.InvariantCulture) : 45f, GiftNames[0], book, false);
        string picks = p.Length > 3 ? p[3] : string.Empty;
        MixedStore store = new MixedStore(env.State);
        store.Set("$gift_name", DialogueValue.FromString(GiftNames[0]));
        DialogueRunner runner = new DialogueRunner(project, store, env.Host);
        runner.Start(p[0]);
        int pi = 0;
        Console.WriteLine("== DUMP " + d);
        for (int g = 0; g < 5000; g++)
        {
            DialogueStep s = runner.Next();
            if (s.IsEnd) break;
            if (s.IsLine) Console.WriteLine("  " + (s.Speaker ?? "·") + ": " + s.Text);
            else if (s.IsChoices)
            {
                int pick = pi < picks.Length ? picks[pi] - '0' : 0;
                pi++;
                for (int i = 0; i < s.Options.Count; i++) Console.WriteLine("   " + (i == pick ? ">" : " ") + "[" + i + "] " + s.Options[i].Text);
                runner.Choose(pick);
            }
        }
        Console.WriteLine("  (aff=" + env.State.Chars["howl"].Aff + " trust=" + env.State.Chars["howl"].Trust + " cmds=" + env.Commands.Count + ")");
    }

    private static void CheckText(string tag, string text, bool nonGendered, string speaker)
    {
        if (text == null || text.Trim().Length == 0) { Fail(tag, "빈 텍스트"); return; }
        if (text.IndexOf('{') >= 0 || text.IndexOf('}') >= 0)
        {
            _unresolved++;
            Problems.Add("미해결 { } " + tag + " :: " + text);
        }
        if (nonGendered)
        {
            Match m = GenderWords.Match(text);
            if (m.Success)
            {
                _genderRendered++;
                Problems.Add("논바이너리/중립 렌더에 성별 단어 '" + m.Value + "' " + tag + " :: " + text);
            }
        }
    }

    private static void Fail(string tag, string msg)
    {
        _runtimeErrors++;
        Problems.Add(tag + " :: " + msg);
    }
}
