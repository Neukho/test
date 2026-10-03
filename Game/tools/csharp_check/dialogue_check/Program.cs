using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;

// Dialogue(MiniYarn) 모듈 검증 하네스. 빌드/실행: tools/csharp_check/dialogue_check/run.sh
public static class Program
{
    private static int _failures;
    private static int _checks;
    private static string _samples = "samples";
    private static string _addressCsv = "address_terms.csv";

    private sealed class Env
    {
        public GameState State;
        public SimpleDialogueHost Host;
        public PronounResolver Pronouns;
        public AddressBook Book;
        public List<string> HandledCommands = new List<string>();
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length > 0) _samples = args[0];
        if (args.Length > 1) _addressCsv = args[1];

        TestValues();
        TestExpressions();
        TestParseStructure();
        TestSyntaxAllRun();
        TestIfBranches();
        TestOptionsAndCommands();
        TestVisited();
        TestPronouns();
        TestLocalizer();
        TestGameFlagsStore();
        TestParseErrors();
        TestRuntimeErrors();
        TestLint();
        TestLineIdTagger();
        TestAddressTerms();
        TestStress();

        Console.WriteLine();
        Console.WriteLine("checks={0} failures={1}", _checks, _failures);
        Console.WriteLine(_failures == 0 ? "ALL PASSED" : "FAILED");
        return _failures == 0 ? 0 : 1;
    }

    // ---------- 도구 ----------

    private static void Check(string name, bool ok)
    {
        _checks++;
        if (!ok) _failures++;
        Console.WriteLine("  [{0}] {1}", ok ? "PASS" : "FAIL", name);
    }

    private static void CheckEq(string name, string expected, string actual)
    {
        bool ok = expected == actual;
        _checks++;
        if (!ok) _failures++;
        Console.WriteLine("  [{0}] {1}{2}", ok ? "PASS" : "FAIL", name, ok ? string.Empty : "  expected=<" + expected + "> actual=<" + actual + ">");
    }

    private static void Section(string title)
    {
        Console.WriteLine("== " + title);
    }

    private static Exception Catch(Action a)
    {
        try { a(); }
        catch (Exception e) { return e; }
        return null;
    }

    private static DialogueProject Load(string file)
    {
        return DialogueProject.Parse(File.ReadAllText(Path.Combine(_samples, file)), file);
    }

    private static DialogueProject Inline(string text)
    {
        return DialogueProject.Parse(text, "inline");
    }

    private static Env MakeEnv(string gender, string locale, string name)
    {
        Env e = new Env();
        e.State = new GameState();
        e.State.PlayerName = name;
        e.State.Gender = gender;
        for (int i = 0; i < CharIds.All.Length; i++) e.State.Chars[CharIds.All[i]] = new CharState();
        e.Book = File.Exists(_addressCsv) ? AddressBook.FromCsv(File.ReadAllText(_addressCsv)) : new AddressBook();
        e.Pronouns = new PronounResolver(e.State, locale, e.Book);
        e.Host = new SimpleDialogueHost();
        e.Host.AddProvider(e.Pronouns);
        GameState st = e.State;
        e.Host.RegisterFunction("affinity", delegate (DialogueValue[] a) { return DialogueValue.FromNumber(st.AffinityOf(a[0].AsString)); });
        e.Host.RegisterFunction("flag", delegate (DialogueValue[] a)
        {
            int v;
            return DialogueValue.FromBool(st.Flags.TryGetValue(a[0].AsString, out v) && v != 0);
        });
        return e;
    }

    private static string Fmt(DialogueStep s)
    {
        switch (s.Kind)
        {
            case StepKind.Line: return (s.Speaker ?? "-") + "|" + s.Text;
            case StepKind.Choices:
                StringBuilder sb = new StringBuilder("CH:");
                for (int i = 0; i < s.Options.Count; i++)
                {
                    if (i > 0) sb.Append("|");
                    sb.Append(s.Options[i].Available ? "+" : "-").Append(s.Options[i].Text);
                }
                return sb.ToString();
            case StepKind.Command: return "CMD:" + s.CommandName + "[" + string.Join(",", s.Args) + "]";
            default: return "END";
        }
    }

    /// <summary>끝까지 실행하며 기록한다. 선택지는 choices 를 순서대로 사용(부족하면 첫 번째 활성 선택지).</summary>
    private static List<string> Run(IDialogueRunner r, string start, params int[] choices)
    {
        List<string> log = new List<string>();
        r.Start(start);
        int ci = 0;
        for (int guard = 0; guard < 100000; guard++)
        {
            DialogueStep s = r.Next();
            log.Add(Fmt(s));
            if (s.IsEnd) break;
            if (s.IsChoices)
            {
                int pick = -1;
                if (ci < choices.Length) pick = choices[ci++];
                else
                {
                    for (int i = 0; i < s.Options.Count; i++) if (s.Options[i].Available) { pick = i; break; }
                }
                r.Choose(pick);
            }
        }
        return log;
    }

    private static string Join(List<string> l)
    {
        return string.Join("\n", l.ToArray());
    }

    // ---------- 값 / 표현식 ----------

    private static void TestValues()
    {
        Section("DialogueValue");
        CheckEq("number int format", "5", DialogueValue.FromNumber(5).ToString());
        CheckEq("number frac format", "2.5", DialogueValue.FromNumber(2.5).ToString());
        CheckEq("bool format", "True", DialogueValue.FromBool(true).ToString());
        Check("default is number 0", default(DialogueValue).IsNumber && default(DialogueValue).AsNumber == 0);
        Check("truthy string", DialogueValue.FromString("x").IsTruthy && !DialogueValue.FromString("").IsTruthy);
        Check("number==bool coercion", DialogueOps.AreEqual(DialogueValue.FromNumber(1), DialogueValue.FromBool(true)));
        Check("string!=number", !DialogueOps.AreEqual(DialogueValue.FromString("1"), DialogueValue.FromNumber(1)));
    }

    private sealed class MockCtx : IExpressionContext
    {
        public Dictionary<string, DialogueValue> Vars = new Dictionary<string, DialogueValue>();
        public int BoomCalls;

        public DialogueValue GetVariable(string name)
        {
            DialogueValue v;
            return Vars.TryGetValue(name, out v) ? v : default(DialogueValue);
        }

        public DialogueValue CallFunction(string name, DialogueValue[] args)
        {
            if (name == "visited") return DialogueValue.FromBool(args[0].AsString == "A");
            if (name == "affinity") return DialogueValue.FromNumber(args[0].AsString == "howl" ? 42 : 0);
            if (name == "flag") return DialogueValue.FromBool(args[0].AsString == "x");
            if (name == "boom") { BoomCalls++; return DialogueValue.FromBool(true); }
            if (name == "name") return DialogueValue.FromString("N");
            throw new DialogueRuntimeException("정의되지 않은 함수: " + name);
        }
    }

    private static string Ev(string expr, MockCtx ctx)
    {
        return DialogueExpression.Parse(expr).Evaluate(ctx).ToString();
    }

    private static void TestExpressions()
    {
        Section("Expression evaluator");
        MockCtx c = new MockCtx();
        c.Vars["$x"] = DialogueValue.FromNumber(3);
        c.Vars["$s"] = DialogueValue.FromString("hi");
        c.Vars["$b"] = DialogueValue.FromBool(true);
        string[,] table = new string[,]
        {
            { "1 + 2 * 3", "7" }, { "(1 + 2) * 3", "9" }, { "10 / 4", "2.5" }, { "7 % 4", "3" }, { "-5 + 2", "-3" },
            { "2 - 3 - 4", "-5" }, { "8 / 2 / 2", "2" }, { "\"a\" + \"b\"", "ab" }, { "\"a\" + 1", "a1" },
            { "1 < 2 and 2 < 3", "True" }, { "1 > 2 or 3 >= 3", "True" }, { "not true", "False" }, { "!false", "True" },
            { "1 eq 1", "True" }, { "1 neq 2", "True" }, { "2 gt 1", "True" }, { "1 lt 2", "True" }, { "2 gte 2", "True" },
            { "2 lte 1", "False" }, { "true && false", "False" }, { "false || true", "True" }, { "1 == 1.0", "True" },
            { "1 != 1", "False" }, { "3 <= 3", "True" }, { "$x == 3", "True" }, { "$s == \"hi\"", "True" },
            { "$x * 2 + 1", "7" }, { "$b and $x > 2", "True" }, { "$undefined", "0" }, { "$undefined == 0", "True" },
            { "visited(\"A\")", "True" }, { "visited(\"B\")", "False" }, { "affinity(\"howl\") + 1", "43" },
            { "flag(\"x\") and not flag(\"y\")", "True" }, { "name", "N" }, { "name()", "N" }, { "\"q\\\"q\"", "q\"q" },
            { "1 + 2 > 2 and 3 - 1 == 2", "True" }, { "not (1 > 2)", "True" }, { "true == 1", "True" }, { "\"a\" < \"b\"", "True" },
            { "--3", "3" }, { "0.1 + 0.2 < 0.31", "True" }
        };
        for (int i = 0; i < table.GetLength(0); i++)
            CheckEq("expr " + table[i, 0], table[i, 1], Ev(table[i, 0], c));

        Ev("false and boom()", c);
        Ev("true or boom()", c);
        Check("and/or short-circuit", c.BoomCalls == 0);
        Ev("true and boom()", c);
        Check("non-short-circuit calls", c.BoomCalls == 1);

        string[] parseErrors = { "1 +", "(1 + 2", "1 = 2", "foo(1,", "1 2", "\"abc", "$", "1 # 2", "", "and" };
        for (int i = 0; i < parseErrors.Length; i++)
        {
            string e = parseErrors[i];
            Check("parse error: <" + e + ">", Catch(delegate { DialogueExpression.Parse(e); }) is DialogueParseException);
        }
        string[] runtimeErrors = { "\"a\" - 1", "1 / 0", "5 % 0", "nofunc()", "true + 1", "-\"a\"", "1 < \"a\"" };
        for (int i = 0; i < runtimeErrors.Length; i++)
        {
            string e = runtimeErrors[i];
            Check("runtime error: <" + e + ">", Catch(delegate { Ev(e, c); }) is DialogueRuntimeException);
        }
    }

    // ---------- 파서 구조 ----------

    private static void TestParseStructure()
    {
        Section("Parser structure");
        DialogueProject p = Load("syntax_all.yarn");
        Check("2 nodes", p.AllNodes.Count == 2);
        DialogueNode start = p.GetNode("Start");
        Check("node Start found", start != null && start.LineNumber == 2);
        Check("header metadata kept", start.Headers["tags"] == "intro demo" && start.Headers["position"] == "10,20");
        Check("tags header split", start.Tags.Count == 2 && start.Tags[1] == "demo");
        Check("declare ignored (no statement)", !(start.Body[0] is CommandStatement));
        Check("first stmt is set", start.Body[0] is SetStatement && ((SetStatement)start.Body[0]).Variable == "$gold");
        LineStatement nar = start.Body[2] as LineStatement;
        Check("narration speaker null", nar != null && nar.Speaker == null && nar.LineId == "a001");
        Check("other tag kept", nar != null && nar.Tags.Contains("mood:calm") && nar.Tags.Contains("line:a001"));
        LineStatement howl = start.Body[3] as LineStatement;
        Check("speaker line", howl != null && howl.Speaker == "howl" && howl.LineId == "a002");
        Check("line number tracked", howl != null && howl.LineNumber == 10);
        IfStatement ifs = null;
        OptionsStatement os = null;
        for (int i = 0; i < start.Body.Count; i++)
        {
            if (start.Body[i] is IfStatement && ifs == null) ifs = (IfStatement)start.Body[i];
            if (start.Body[i] is OptionsStatement && os == null) os = (OptionsStatement)start.Body[i];
        }
        Check("if has 3 branches", ifs != null && ifs.Branches.Count == 3 && ifs.Branches[2].Condition == null);
        Check("options group: 3 items", os != null && os.Items.Count == 3);
        Check("option nested body has options", os != null && os.Items[0].Body.Count == 3 && os.Items[0].Body[2] is OptionsStatement);
        Check("option condition parsed", os != null && os.Items[1].Condition != null && os.Items[0].Condition == null);
        Check("option line id", os != null && os.Items[2].LineId == "a015");
        StatementWalker.Walk(start.Body, null, null);
        int cmds = 0;
        StatementWalker.Walk(start.Body, delegate (Statement s) { if (s is CommandStatement) cmds++; }, null);
        Check("walker counts custom commands (aff,flag,show_standing,bg,show_cg,custom)", cmds == 6);

        // CRLF / BOM 동일 결과
        string text = File.ReadAllText(Path.Combine(_samples, "syntax_all.yarn"));
        DialogueProject crlf = DialogueProject.Parse("﻿" + text.Replace("\n", "\r\n"), "crlf");
        Check("CRLF+BOM same node count", crlf.AllNodes.Count == 2 && crlf.GetNode("Second").Body.Count == p.GetNode("Second").Body.Count);

        // 주석 처리
        DialogueProject cm = Inline("title: C\n---\n// whole line\nhi // trailing\nurl http://a.b/c ok\n  // indented comment\n===\n");
        List<string> log = Run(new DialogueRunner(cm, null, null), "C");
        CheckEq("comments stripped / url preserved", "-|hi\n-|url http://a.b/c ok\nEND", Join(log));

        // 여러 파일 합치기
        DialogueProject multi = new DialogueProject();
        multi.AddSource("title: A\n---\nfrom a\n===\n", "a.yarn");
        multi.AddSource("title: B\n---\nfrom b\n===\n", "b.yarn");
        Check("AddSource merges", multi.Contains("A") && multi.Contains("B") && multi.GetNode("B").SourceName == "b.yarn");
    }

    // ---------- 전체 실행 ----------

    private static DialogueRunner MakeRunner(DialogueProject p, Env e, IVariableStore vars, ITextLocalizer loc)
    {
        return new DialogueRunner(p, vars, e.Host, loc);
    }

    private static void TestSyntaxAllRun()
    {
        Section("Run syntax_all.yarn");
        DialogueProject p = Load("syntax_all.yarn");
        Env e = MakeEnv("male", "ko", "하늘");
        DialogueRunner r = MakeRunner(p, e, new InMemoryVariableStore(), null);
        List<string> log = Run(r, "Start", 0, 0);
        string[] expected =
        {
            "-|narration line without speaker",
            "howl|어서 와, 하늘.",
            "player|골드는 120 이네요.",
            "howl|계산: 180 그리고 30.",
            "howl|이름은 하늘, 호칭은 하늘 씨.",
            "howl|부자네.",
            "CH:+고맙다고 말한다|+말없이 지나간다|-잠긴 선택지",
            "player|고마워요.",
            "CMD:aff[howl,5]",
            "CH:+한 번 더|-비밀 선택지",
            "player|정말로요.",
            "CMD:flag[thanked_twice]",
            "howl|끝으로.",
            "CMD:bg[lighthouse,night]",
            "CMD:show_cg[HOWL_E1]",
            "CMD:custom[two words,120,x120y]",
            "nar|짧은 화자 id 도 허용.",
            "-|12:30 도착.",
            "-|http://example.com/a 주소",
            "-|brace {escaped} text.",
            "howl|gold now 50",
            "END"
        };
        CheckEq("full transcript (choices 0,0)", string.Join("\n", expected), Join(log));
        Check("runner not running after End", !r.IsRunning && r.Next().IsEnd);
        List<string> visited = r.ExportVisited();
        Check("visited Start,Second", visited.Count == 2 && visited[0] == "Start" && visited[1] == "Second");

        // 두 번째 경로: 말없이 지나간다
        DialogueRunner r2 = MakeRunner(p, e, new InMemoryVariableStore(), null);
        List<string> log2 = Run(r2, "Start", 1);
        Check("path 2 emits show_standing with 3 args", log2.Contains("CMD:show_standing[howl,smile,casual]"));
        Check("path 2 skips nested", !log2.Contains("player|고마워요."));

        // 단계 필드
        DialogueRunner r3 = MakeRunner(p, e, new InMemoryVariableStore(), null);
        r3.Start("Start");
        DialogueStep s1 = r3.Next();
        Check("step.LineId + Tags", s1.LineId == "a001" && s1.Tags.Contains("mood:calm") && s1.Speaker == null && s1.IsLine);
        DialogueStep s2 = r3.Next();
        Check("step tags on speaker line", s2.Speaker == "howl" && s2.Tags.Contains("mood:soft"));
        for (int i = 0; i < 4; i++) r3.Next();
        DialogueStep ch = r3.Next();
        Check("choices step LineIds", ch.IsChoices && ch.Options[0].LineId == "a008" && ch.Options[2].LineId == "a015" && ch.Options[2].Index == 2);
        Check("runner CurrentNode", r3.CurrentNode == "Start");
    }

    // ---------- if / elseif / else ----------

    private static void TestIfBranches()
    {
        Section("if / elseif / else");
        DialogueProject p = Inline(
            "title: T\n---\n" +
            "<<if $v > 100>>\n  big\n<<elseif $v > 50>>\n  mid\n<<elseif $v > 10>>\n  small\n<<else>>\n  tiny\n<<endif>>\n" +
            "after\n" +
            "<<if $v > 1000>>\n  never\n<<endif>>\n" +
            "<<if $v > 5>>\n  <<if $v > 60>>\n    nested-hi\n  <<else>>\n    nested-lo\n  <<endif>>\n<<endif>>\n" +
            "end\n===\n");
        int[] values = { 200, 70, 20, 3 };
        string[] first = { "big", "mid", "small", "tiny" };
        string[] nested = { "nested-hi", "nested-hi", "nested-lo", null };
        for (int i = 0; i < values.Length; i++)
        {
            InMemoryVariableStore vs = new InMemoryVariableStore();
            vs.Set("$v", DialogueValue.FromNumber(values[i]));
            List<string> log = Run(new DialogueRunner(p, vs, null), "T");
            Check("v=" + values[i] + " picks " + first[i], log[0] == "-|" + first[i] && log[1] == "-|after");
            Check("v=" + values[i] + " nested", nested[i] == null ? (log[2] == "-|end") : (log[2] == "-|" + nested[i] && log[3] == "-|end"));
            Check("v=" + values[i] + " false-only-if skipped", !log.Contains("-|never"));
        }
        DialogueProject q = Inline("title: Q\n---\n<<if $f>>\nyes\n<<endif>>\n<<if flag(\"k\") == true>>\nviaFn\n<<endif>>\n===\n");
        Env e = MakeEnv("neutral", "ko", "A");
        e.State.Flags["k"] = 1;
        List<string> l2 = Run(new DialogueRunner(q, new InMemoryVariableStore(), e.Host), "Q");
        Check("undefined var is falsy, host fn in condition", Join(l2) == "-|viaFn\nEND");
    }

    // ---------- 선택지 / 커맨드 ----------

    private static void TestOptionsAndCommands()
    {
        Section("Options & commands");
        DialogueProject p = Inline(
            "title: O\n---\n" +
            "-> A\n    in-A\n    -> A1\n        in-A1\n    -> A2\n        in-A2\n-> B <<if $ok>>\n    in-B\n-> C\n    in-C\nafter-group\n" +
            "<<wait 3>>\n<<host_only x>>\n<<say {$ok} \"a b\">>\nlast\n===\n");
        // 옵션 선택 불가 / 범위
        InMemoryVariableStore vs = new InMemoryVariableStore();
        DialogueRunner r = new DialogueRunner(p, vs, null);
        r.Start("O");
        DialogueStep ch = r.Next();
        Check("B unavailable (undefined var)", ch.IsChoices && ch.Options.Count == 3 && !ch.Options[1].Available && ch.Options[0].Available);
        Check("Next while pending throws", Catch(delegate { r.Next(); }) is InvalidOperationException);
        Check("Choose unavailable throws", Catch(delegate { r.Choose(1); }) is InvalidOperationException);
        Check("Choose out of range throws", Catch(delegate { r.Choose(9); }) is ArgumentOutOfRangeException);
        Check("Choose(-1) throws", Catch(delegate { r.Choose(-1); }) is ArgumentOutOfRangeException);
        r.Choose(0);
        Check("A body then nested", Fmt(r.Next()) == "-|in-A" && Fmt(r.Next()) == "CH:+A1|+A2");
        r.Choose(1);
        Check("nested pick then group continues", Fmt(r.Next()) == "-|in-A2" && Fmt(r.Next()) == "-|after-group");
        Check("Choose with none pending throws", Catch(delegate { r.Choose(0); }) is InvalidOperationException);

        // B 가능해짐
        vs.Set("$ok", DialogueValue.FromBool(true));
        List<string> lb = Run(new DialogueRunner(p, vs, null), "O", 1, 0);
        Check("B available when $ok, runs in-B", lb.Contains("-|in-B") && !lb.Contains("-|in-C"));

        // 커맨드: 호스트 미처리 -> 단계로 노출
        List<string> l1 = Run(new DialogueRunner(p, vs, null), "O", 2);
        Check("custom commands surfaced in order", l1.Contains("CMD:wait[3]") && l1.Contains("CMD:host_only[x]") && l1.Contains("CMD:say[True,a b]"));
        CheckEq("surface order + trailing line", "CMD:wait[3]|CMD:host_only[x]|CMD:say[True,a b]|-|last|END",
            string.Join("|", l1.GetRange(l1.IndexOf("CMD:wait[3]"), 5).ToArray()));

        // 호스트가 처리하면 노출되지 않는다
        SimpleDialogueHost host = new SimpleDialogueHost();
        List<string> handled = new List<string>();
        host.RegisterCommand("wait", delegate (string[] a) { handled.Add("wait:" + string.Join(",", a)); });
        List<string> l2 = Run(new DialogueRunner(p, vs, host), "O", 2);
        Check("host-handled command hidden", !l2.Contains("CMD:wait[3]") && handled.Count == 1 && handled[0] == "wait:3");
        Check("unhandled still surfaced", l2.Contains("CMD:host_only[x]"));

        // 모든 선택지가 비활성이면 건너뛰고 경고
        DialogueProject none = Inline("title: N\n---\n-> a <<if false>>\n    x\n-> b <<if false>>\n    y\nfinish\n===\n");
        DialogueRunner rn = new DialogueRunner(none, null, null);
        List<string> ln = Run(rn, "N");
        Check("all-unavailable group skipped with warning", Join(ln) == "-|finish\nEND" && rn.Warnings.Count == 1);

        // 빈 노드, 마지막 후 End 반복
        DialogueProject empty = Inline("title: E\n---\n===\n");
        DialogueRunner re = new DialogueRunner(empty, null, null);
        re.Start("E");
        Check("empty node ends", re.Next().IsEnd && re.Next().IsEnd);

        // 같은 러너 재시작
        DialogueProject two = Inline("title: S\n---\n-> x\n    in-x\n===\ntitle: T2\n---\nt2\n===\n");
        DialogueRunner rs = new DialogueRunner(two, null, null);
        rs.Start("S");
        rs.Next();
        rs.Start("T2");
        Check("Start discards pending choices", Fmt(rs.Next()) == "-|t2");
    }

    // ---------- visited ----------

    private static void TestVisited()
    {
        Section("visited()");
        DialogueProject p = Load("branching.yarn");
        Env e = MakeEnv("neutral", "ko", "A");
        DialogueRunner r = MakeRunner(p, e, new InMemoryVariableStore(), null);
        r.Start("Hub");
        Check("Hub visited at Start", r.HasVisited("Hub") && !r.HasVisited("Library"));
        r.Next();
        DialogueStep c1 = r.Next();
        Check("Harbor locked before visiting Library", c1.IsChoices && !c1.Options[1].Available);
        r.Choose(0);
        List<string> rest = new List<string>();
        DialogueStep s = r.Next();
        rest.Add(Fmt(s));
        Check("jump Library->Hub shows hub again", Fmt(s) == "-|조용한 도서관.");
        s = r.Next();
        DialogueStep c2 = r.Next();
        Check("Harbor unlocked after Library", c2.IsChoices && c2.Options[1].Available);
        r.Choose(1);
        List<string> tail = new List<string>();
        for (DialogueStep t = r.Next(); !t.IsEnd; t = r.Next()) tail.Add(Fmt(t));
        CheckEq("harbor visited conditions", "-|항구.|-|둘 다 방문.|-|허브 재방문.", string.Join("|", tail.ToArray()));
        List<string> exp = r.ExportVisited();
        CheckEq("export order", "Hub,Library,Harbor", string.Join(",", exp.ToArray()));
        DialogueRunner r2 = MakeRunner(p, e, new InMemoryVariableStore(), null);
        r2.ImportVisited(exp);
        Check("ImportVisited restores", r2.HasVisited("Library") && r2.HasVisited("Harbor") && !r2.HasVisited("Nope"));
        r2.Start("Hub");
        r2.Next();
        Check("imported visit unlocks option immediately", r2.Next().Options[1].Available);
        Check("export after import keeps order", string.Join(",", r2.ExportVisited().ToArray()) == "Hub,Library,Harbor");
        Check("visited_count builtin", new DialogueRunner(p, null, null).GetVariable("$x").AsNumber == 0);
    }

    // ---------- 대명사 ----------

    private static void TestPronouns()
    {
        Section("PronounResolver");
        string[] genders = { "male", "female", "nonbinary", "neutral" };
        string[] locales = { "ko", "en" };
        for (int gi = 0; gi < genders.Length; gi++)
        {
            for (int li = 0; li < locales.Length; li++)
            {
                string g = genders[gi];
                string loc = locales[li];
                PronounResolver pr = new PronounResolver(g, loc, "Rin");
                string subj, obj, poss, hon;
                if (loc == "en")
                {
                    subj = g == "male" ? "he" : g == "female" ? "she" : "they";
                    obj = g == "male" ? "him" : g == "female" ? "her" : "them";
                    poss = g == "male" ? "his" : g == "female" ? "her" : "their";
                    hon = g == "male" ? "Mr. Rin" : g == "female" ? "Ms. Rin" : "Mx. Rin";
                }
                else
                {
                    subj = "Rin"; obj = "Rin"; poss = "Rin의"; hon = "Rin 씨";
                }
                string tag = g + "/" + loc;
                CheckEq(tag + " subj", subj, pr.Subject());
                CheckEq(tag + " obj", obj, pr.Object());
                CheckEq(tag + " poss", poss, pr.Possessive());
                CheckEq(tag + " honorific", hon, pr.Honorific());
                DialogueValue v;
                Check(tag + " via TryCall", pr.TryCall("pronoun_subj", new DialogueValue[0], out v) && v.ToString() == subj
                    && pr.TryCall("name", new DialogueValue[0], out v) && v.ToString() == "Rin");
                if (loc == "ko")
                    Check(tag + " ko has no gendered pronoun", !(pr.Subject() + pr.Object() + pr.Possessive() + pr.Honorific()).Contains("그녀")
                        && !(pr.Subject() + pr.Object() + pr.Possessive()).Contains("그가"));
            }
        }
        DialogueValue unk;
        Check("unknown function not handled", !new PronounResolver("male", "ko", "A").TryCall("zzz", new DialogueValue[0], out unk));
        CheckEq("unknown gender -> they", "they", new PronounResolver("alien", "en", "A").Subject());
        CheckEq("locale en-US -> en", "he", new PronounResolver("M", "en-US", "A").Subject());
        CheckEq("unknown locale -> ko", "A", new PronounResolver("male", "fr", "A").Subject());
        CheckEq("empty name ko fallback", "당신", new PronounResolver("male", "ko", "").Subject());
        CheckEq("empty name en fallback", "you", new PronounResolver("neutral", "en", null).PlayerName);

        // 스크립트에서 사용
        DialogueProject p = Load("pronouns.yarn");
        string[,] expect = new string[,]
        {
            { "female", "en", "She 는 가게 문을 열었다.|손님이 her 를 불렀다.|그건 her 앞치마였다.|안녕하세요, Ms. Rin. 환영합니다 Rin.|bare - she / Rin" },
            { "male", "en", "He 는 가게 문을 열었다.|손님이 him 를 불렀다.|그건 his 앞치마였다.|안녕하세요, Mr. Rin. 환영합니다 Rin.|bare - he / Rin" },
            { "nonbinary", "en", "They 는 가게 문을 열었다.|손님이 them 를 불렀다.|그건 their 앞치마였다.|안녕하세요, Mx. Rin. 환영합니다 Rin.|bare - they / Rin" },
            { "female", "ko", "Rin 는 가게 문을 열었다.|손님이 Rin 를 불렀다.|그건 Rin의 앞치마였다.|안녕하세요, Rin 씨. 환영합니다 Rin.|bare - Rin / Rin" }
        };
        for (int i = 0; i < expect.GetLength(0); i++)
        {
            Env e = MakeEnv(expect[i, 0], expect[i, 1], "Rin");
            List<string> log = Run(new DialogueRunner(p, new InMemoryVariableStore(), e.Host), "Pronouns");
            log.RemoveAt(log.Count - 1);
            List<string> texts = new List<string>();
            for (int k = 0; k < log.Count; k++) texts.Add(log[k].Substring(2));
            CheckEq("script " + expect[i, 0] + "/" + expect[i, 1], expect[i, 2], string.Join("|", texts.ToArray()));
        }
        // GameState 라이브 반영
        Env live = MakeEnv("male", "en", "Rin");
        PronounResolver lp = live.Pronouns;
        string before = lp.Subject();
        live.State.Gender = "female";
        live.State.PlayerName = "Sol";
        Check("live GameState gender/name", before == "he" && lp.Subject() == "she" && lp.Honorific() == "Ms. Sol");
    }

    // ---------- 현지화 ----------

    private static void TestLocalizer()
    {
        Section("Localizer");
        DialogueProject p = Inline(
            "title: L\n---\n" +
            "howl: 원문 {$gold} #line:L1\nhowl: 번역 없음 #line:L2\nhowl: 아이디 없음 {$gold}\n" +
            "-> 옵션 #line:L3\n    howl: 안쪽 #line:L4\nhowl: 끝 #line:L5\n===\n");
        InMemoryVariableStore vs = new InMemoryVariableStore();
        vs.Set("$gold", DialogueValue.FromNumber(7));
        DictionaryTextLocalizer loc = new DictionaryTextLocalizer();
        loc.Set("L1", "Translated {$gold} gold");
        loc.Set("L3", "Option!");
        loc.Set("L4", "bad {");
        loc.Set("L5", "Ending {name}");
        Env e = MakeEnv("neutral", "en", "Rin");
        DialogueRunner r = new DialogueRunner(p, vs, e.Host, loc);
        List<string> log = Run(r, "L", 0);
        CheckEq("localized + fallbacks", "howl|Translated 7 gold\nhowl|번역 없음\nhowl|아이디 없음 7\nCH:+Option!\nhowl|안쪽\nhowl|Ending Rin\nEND", Join(log));
        Check("bad localized text warns & falls back", r.Warnings.Count == 1 && r.Warnings[0].Contains("L4"));
        List<string> plain = Run(new DialogueRunner(p, vs, e.Host), "L", 0);
        Check("no localizer -> source", plain[0] == "howl|원문 7" && plain[3] == "CH:+옵션");
        string t;
        Check("DictionaryTextLocalizer miss", !loc.TryGetText("zzz", out t));
    }

    // ---------- GameFlags 저장소 ----------

    private static void TestGameFlagsStore()
    {
        Section("GameFlagsVariableStore");
        GameState st = new GameState();
        st.Gold = 250;
        st.Day = 9;
        st.Chars["howl"] = new CharState();
        st.Chars["howl"].Aff = 33;
        st.Chars["howl"].Trust = 4;
        GameFlagsVariableStore vs = new GameFlagsVariableStore(st);
        DialogueProject p = Inline(
            "title: F\n---\n<<set $met_howl to true>>\n<<set $visits to 2>>\n<<set $visits += 3>>\n<<set $ratio to 2.6>>\n" +
            "gold={$gold} day={$day} aff={$aff_howl} trust={$trust_howl} met={$met_howl} visits={$visits} ratio={$ratio}\n" +
            "<<if $met_howl and $visits == 5>>\n    ok\n<<endif>>\n<<set $met_howl to false>>\nmet={$met_howl}\n===\n");
        List<string> log = Run(new DialogueRunner(p, vs, null), "F");
        CheckEq("flags read/write transcript", "-|gold=250 day=9 aff=33 trust=4 met=1 visits=5 ratio=3\n-|ok\n-|met=0\nEND", Join(log));
        Check("Flags dictionary updated (int)", st.Flags["visits"] == 5 && st.Flags["ratio"] == 3 && st.Flags["met_howl"] == 0);
        Check("$ prefix maps to key", st.Flags.ContainsKey("visits") && !st.Flags.ContainsKey("$visits"));
        DialogueValue v;
        Check("TryGet missing false", !vs.TryGet("$nope", out v));
        st.Flags["gold"] = 1;
        Check("flag overrides built-in", vs.TryGet("$gold", out v) && v.AsNumber == 1);
        st.Flags.Remove("gold");
        DialogueProject bad = Inline("title: B\n---\n<<set $gold to 1>>\n===\n");
        Exception ex = Catch(delegate { Run(new DialogueRunner(bad, vs, null), "B"); });
        Check("built-in write -> DialogueRuntimeException w/ node:line", ex is DialogueRuntimeException && ex.Message.Contains("[B:3]"));
        Exception ex2 = Catch(delegate { vs.Set("$s", DialogueValue.FromString("x")); });
        Check("string store rejected", ex2 is InvalidOperationException);
        vs.Set("$b", DialogueValue.FromBool(true));
        Check("bool stored as 1", st.Flags["b"] == 1);
    }

    // ---------- 파싱 오류 ----------

    private static void ErrCase(string name, string text, int line, string fragment)
    {
        DialogueParseException ex = Catch(delegate { DialogueProject.Parse(text, "t.yarn"); }) as DialogueParseException;
        bool ok = ex != null && ex.LineNumber == line && ex.Message.Contains("t.yarn(" + line + ")") && (fragment == null || ex.Message.Contains(fragment));
        Check("error " + name + " -> line " + line + (ex == null ? " (NO EXCEPTION)" : ok ? string.Empty : " got: " + ex.Message), ok);
    }

    private static void TestParseErrors()
    {
        Section("Parse errors with line numbers");
        ErrCase("missing ===", "title: A\n---\nhi\n", 1, "===");
        ErrCase("missing endif", "title: A\n---\nhi\n<<if $x>>\nyo\n===\n", 4, "endif");
        ErrCase("stray endif", "title: A\n---\nhi\n<<endif>>\n===\n", 4, "endif");
        ErrCase("set without $", "title: A\n---\n<<set x to 1>>\n===\n", 3, "set");
        ErrCase("bad set expr", "title: A\n---\nhi\n<<set $x to 1 +>>\n===\n", 4, null);
        ErrCase("unclosed brace", "title: A\n---\nhi\n\nhello {$x\n===\n", 5, "닫히지");
        ErrCase("unsupported once", "title: A\n---\n<<once>>\n===\n", 3, "지원하지 않는");
        ErrCase("header w/o colon", "title: A\nfoo\n---\nhi\n===\n", 2, "key: value");
        ErrCase("missing title", "tags: x\n---\nhi\n===\n", 1, "title");
        ErrCase("jump w/o target", "title: A\n---\n<<jump>>\n===\n", 3, "jump");
        ErrCase("unterminated command", "title: A\n---\n<<foo bar\n===\n", 3, ">>");
        ErrCase("empty option", "title: A\n---\nhi\n->\n===\n", 4, "선택지");
        ErrCase("else then elseif", "title: A\n---\n<<if $x>>\n a\n<<else>>\n b\n<<elseif $y>>\n c\n<<endif>>\n===\n", 7, "else");
        ErrCase("if without condition", "title: A\n---\n<<if>>\n<<endif>>\n===\n", 3, "조건");
        ErrCase("= in condition", "title: A\n---\n<<if $x = 3>>\nhi\n<<endif>>\n===\n", 3, "==");
        ErrCase("stray text after ===", "title: A\n---\nhi\n===\nstray\n", 5, "key: value");
        ErrCase("header without ---", "title: A\n===\n", 2, "---");
        ErrCase("bad inline expr", "title: A\n---\nhi\nvalue {1 +} here\n===\n", 4, null);
        ErrCase("empty braces", "title: A\n---\nx {} y\n===\n", 3, "{}");
        ErrCase("jump with expression", "title: A\n---\n<<jump {$x}>>\n===\n", 3, "jump");
        ErrCase("option bad condition", "title: A\n---\n-> a <<if 1 +>>\n    x\n===\n", 3, null);
        ErrCase("empty line after speaker", "title: A\n---\nhowl:\n===\n", 3, "비어");
        ErrCase("unterminated command quote", "title: A\n---\n<<say \"abc>>\n===\n", 3, null);
        ErrCase("error in 2nd node uses abs line", "title: A\n---\nok\n===\n\ntitle: B\n---\n<<set $x>>\n===\n", 8, "set");
        Exception none = Catch(delegate { DialogueProject.Parse("title: A\n---\nok\n===\n", "t"); });
        Check("valid text no error", none == null);
    }

    private static void TestRuntimeErrors()
    {
        Section("Runtime errors");
        DialogueProject p = Inline("title: T\n---\nhi\nvalue {boom()}\n===\ntitle: J\n---\n<<jump Missing>>\n===\n");
        DialogueRunner r = new DialogueRunner(p, null, null);
        Check("Start unknown node throws", Catch(delegate { r.Start("Nope"); }) is DialogueRuntimeException);
        r.Start("T");
        r.Next();
        Exception ex = Catch(delegate { r.Next(); });
        Check("undefined function error has [node:line]", ex is DialogueRuntimeException && ex.Message.Contains("[T:4]") && ex.Message.Contains("boom"));
        DialogueRunner rj = new DialogueRunner(p, null, null);
        rj.Start("J");
        Exception ej = Catch(delegate { rj.Next(); });
        Check("jump to undefined node throws at runtime", ej is DialogueRuntimeException && ej.Message.Contains("Missing"));
        DialogueProject arity = Inline("title: A\n---\n{visited()}\n===\n");
        DialogueRunner ra = new DialogueRunner(arity, null, null);
        ra.Start("A");
        Check("builtin arity checked", Catch(delegate { ra.Next(); }) is DialogueRuntimeException);
        DialogueProject math = Inline("title: M\n---\n<<set $x to 1 / 0>>\n===\n");
        DialogueRunner rm = new DialogueRunner(math, null, null);
        rm.Start("M");
        Exception em = Catch(delegate { rm.Next(); });
        Check("divide by zero -> runtime error w/ line", em is DialogueRuntimeException && em.Message.Contains("[M:3]"));
        Check("null project rejected", Catch(delegate { new DialogueRunner(null, null, null); }) is ArgumentNullException);
        DialogueRunner builtin = new DialogueRunner(Inline("title: B\n---\n{min(3, 9)} {max(3, 9)} {floor(2.7)} {ceil(2.1)} {round(2.5)} {abs(-4)} {cap(\"abc\")}\n===\n"), null, null);
        CheckEq("builtin functions", "-|3 9 2 3 3 4 Abc\nEND", Join(Run(builtin, "B")));
    }

    // ---------- 린트 ----------

    private static void TestLint()
    {
        Section("DialogueLint");
        DialogueProject broken = Load("lint_broken.yarn");
        List<LintIssue> issues = DialogueLint.Run(broken, new string[] { "Entry" });
        foreach (LintIssue i in issues) Console.WriteLine("    " + i);
        
        Check("DUP_NODE x1", DialogueLint.Count(issues, "DUP_NODE") == 1);
        Check("UNDEFINED_JUMP x1 (Nowhere)", DialogueLint.Count(issues, "UNDEFINED_JUMP") == 1 && FindIssue(issues, "UNDEFINED_JUMP").Message.Contains("Nowhere"));
        Check("UNREACHABLE Orphan only", DialogueLint.Count(issues, "UNREACHABLE") == 1 && FindIssue(issues, "UNREACHABLE").Node == "Orphan");
        Check("DUP_LINE_ID x1 (x001)", DialogueLint.Count(issues, "DUP_LINE_ID") == 1 && FindIssue(issues, "DUP_LINE_ID").Message.Contains("x001"));
        Check("MISSING_LINE_ID x2 (line + option)", DialogueLint.Count(issues, "MISSING_LINE_ID") == 2);
        Check("issue carries line number & source", FindIssue(issues, "UNDEFINED_JUMP").Line == 8 && FindIssue(issues, "UNDEFINED_JUMP").Source == "lint_broken.yarn");
        Check("severities", FindIssue(issues, "UNREACHABLE").Severity == LintSeverity.Warning && FindIssue(issues, "DUP_NODE").Severity == LintSeverity.Error);
        List<LintIssue> noEntry = DialogueLint.Run(broken, null);
        Check("no entries -> no reachability check", DialogueLint.Count(noEntry, "UNREACHABLE") == 0);
        List<LintIssue> badEntry = DialogueLint.Run(broken, new string[] { "Ghost" });
        Check("UNDEFINED_ENTRY reported", DialogueLint.Count(badEntry, "UNDEFINED_ENTRY") == 1);
        List<LintIssue> multiEntry = DialogueLint.Run(broken, new string[] { "Entry", "Orphan" });
        Check("Orphan reachable when listed as entry", DialogueLint.Count(multiEntry, "UNREACHABLE") == 0);

        List<LintIssue> clean = DialogueLint.Run(Load("branching.yarn"), new string[] { "Hub" });
        Check("branching.yarn lints clean", clean.Count == 0);
        List<LintIssue> clean2 = DialogueLint.Run(Load("howl_intro.yarn"), new string[] { "HowlIntro" });
        Check("howl_intro.yarn lints clean", clean2.Count == 0);
        List<LintIssue> sa = DialogueLint.Run(Load("syntax_all.yarn"), new string[] { "Start" });
        Check("syntax_all: Second reachable, only MISSING_LINE_ID warnings", DialogueLint.Count(sa, "UNREACHABLE") == 0 && DialogueLint.Count(sa, "UNDEFINED_JUMP") == 0
            && DialogueLint.Count(sa, "MISSING_LINE_ID") == 2);
        DialogueProject cross = new DialogueProject();
        cross.AddSource("title: A\n---\nx #line:q1\n===\n", "a.yarn");
        cross.AddSource("title: B\n---\ny #line:q1\n===\n", "b.yarn");
        Check("DUP_LINE_ID across files", DialogueLint.Count(DialogueLint.Run(cross, new string[] { "A", "B" }), "DUP_LINE_ID") == 1);
        DialogueProject nested = Inline("title: A\n---\n<<if $x>>\n  <<jump Gone>>\n<<endif>>\n-> o\n    <<jump Gone2>>\n===\n");
        Check("jumps inside if/option detected", DialogueLint.Count(DialogueLint.Run(nested, null), "UNDEFINED_JUMP") == 2);
    }

    private static LintIssue FindIssue(List<LintIssue> l, string code)
    {
        for (int i = 0; i < l.Count; i++) if (l[i].Code == code) return l[i];
        return null;
    }

    // ---------- LineIdTagger ----------

    private static void TestLineIdTagger()
    {
        Section("LineIdTagger");
        string src = "title: A\n---\nhowl: 안녕 // 주석\n지문 {$x}\n-> 선택 <<if $x>>\n    howl: 안쪽\n-> 다른 #mood:x\n    <<jump B>>\n<<set $x to 1>>\n===\n\ntitle: B\n---\nhowl: 이어서 #line:keep1\nhowl: 새 줄\n===\n";
        string tagged = LineIdTagger.AddMissing(src, "t");
        Console.WriteLine("    " + tagged.Replace("\n", "\n    "));
        DialogueProject p = DialogueProject.Parse(tagged, "t");
        Check("tagged text parses", p.AllNodes.Count == 2);
        List<LintIssue> li = DialogueLint.Run(p, new string[] { "A" });
        Check("no MISSING_LINE_ID after tagging", DialogueLint.Count(li, "MISSING_LINE_ID") == 0);
        Check("no DUP_LINE_ID after tagging", DialogueLint.Count(li, "DUP_LINE_ID") == 0);
        CheckEq("idempotent", tagged, LineIdTagger.AddMissing(tagged, "t"));
        CheckEq("deterministic", tagged, LineIdTagger.AddMissing(src, "t"));
        Check("existing id untouched", tagged.Contains("#line:keep1") && tagged.IndexOf("#line:keep1") == tagged.LastIndexOf("#line:keep1"));
        Check("tag inserted before trailing comment", tagged.Contains("howl: 안녕 #line:") && tagged.Contains(" // 주석"));
        int idx = tagged.IndexOf("howl: 안녕 #line:");
        Check("comment follows tag", tagged.IndexOf("// 주석") > idx);
        Check("option tag placed after <<if>> suffix", System.Text.RegularExpressions.Regex.IsMatch(tagged, @"-> 선택 <<if \$x>> #line:[0-9a-f]{8}\n"));
        Check("existing #mood tag kept w/ line tag after", System.Text.RegularExpressions.Regex.IsMatch(tagged, @"-> 다른 #mood:x #line:[0-9a-f]{8}\n"));
        Check("id format #line:<8 hex>", System.Text.RegularExpressions.Regex.Matches(tagged, @"#line:[0-9a-f]{8}").Count == 6);
        // 의미 보존: 태그를 떼면 같은 대본
        List<string> a = Run(new DialogueRunner(DialogueProject.Parse(src, "t"), null, null), "A", 1);
        List<string> b = Run(new DialogueRunner(p, null, null), "A", 1);
        CheckEq("same transcript before/after tagging", Join(a), Join(b));
        // 고정 해시 값(회귀 방지)
        CheckEq("pinned hash FNV-1a('')", "811c9dc5", LineIdTagger.Hash(string.Empty));
        CheckEq("pinned hash FNV-1a('a')", "e40c292c", LineIdTagger.Hash("a"));
        // CRLF 보존
        string crlf = src.Replace("\n", "\r\n");
        string ct = LineIdTagger.AddMissing(crlf, "t");
        Check("CRLF preserved", ct.Contains("\r\n") && !ct.Replace("\r\n", "").Contains("\n") && ct.Replace("\r\n", "\n") == tagged);
        // 다른 노드 제목 => 다른 id, 같은 제목+순번 => 같은 id
        string t1 = LineIdTagger.AddMissing("title: X\n---\na\n===\n", "t");
        string t2 = LineIdTagger.AddMissing("title: Y\n---\na\n===\n", "t");
        Check("different node title -> different id", t1 != t2.Replace("title: Y", "title: X"));
        CheckEq("same title+index -> same id", t1, LineIdTagger.AddMissing("title: X\n---\na\n===\n", "other"));
        // 기존 id 와 충돌 시 재계산
        string idForX0 = LineIdTagger.Hash("X\u001F0");
        string collide = LineIdTagger.AddMissing("title: X\n---\na #line:" + idForX0 + "\nb\n===\n", "t");
        DialogueProject cp = DialogueProject.Parse(collide, "t");
        Check("collision avoided", DialogueLint.Count(DialogueLint.Run(cp, null), "DUP_LINE_ID") == 0 && DialogueLint.Count(DialogueLint.Run(cp, null), "MISSING_LINE_ID") == 0);
        // 파싱 오류가 있으면 예외
        Check("tagger propagates parse errors", Catch(delegate { LineIdTagger.AddMissing("title: A\n---\n<<if $x>>\n===\n", "t"); }) is DialogueParseException);
        // 큰 파일에서 id 유일성
        StringBuilder big = new StringBuilder();
        for (int n = 0; n < 50; n++)
        {
            big.Append("title: N").Append(n).Append("\n---\n");
            for (int k = 0; k < 40; k++) big.Append("howl: line ").Append(k).Append("\n");
            big.Append("===\n");
        }
        string bt = LineIdTagger.AddMissing(big.ToString(), "big");
        DialogueProject bp = DialogueProject.Parse(bt, "big");
        List<LintIssue> bi = DialogueLint.Run(bp, null);
        Check("2000 generated ids unique & complete", DialogueLint.Count(bi, "DUP_LINE_ID") == 0 && DialogueLint.Count(bi, "MISSING_LINE_ID") == 0);
    }

    // ---------- 호칭 ----------

    private static readonly string[] BannedKo = { "형", "오빠", "누나", "언니", "아저씨", "아가씨", "아줌마", "총각", "도련님", "아가", "소년", "소녀" };
    private static readonly string[] BannedEn = { "sir", "ma'am", "madam", "miss", "mr", "mrs", "ms.", "bro", "dude", "lady", "gentleman", "girl", "boy", "sis", "handsome", "pretty", "beautiful", "guys", "king", "queen" };

    private static bool ContainsAny(string s, string[] banned)
    {
        string low = s.ToLowerInvariant();
        for (int i = 0; i < banned.Length; i++) if (low.Contains(banned[i])) return true;
        return false;
    }

    private static void TestAddressTerms()
    {
        Section("Address terms (호칭)");
        Check("address_terms.csv exists", File.Exists(_addressCsv));
        string csv = File.ReadAllText(_addressCsv);
        AddressBook book = AddressBook.FromCsv(csv);
        Check("72 rows (6 chars x 4 genders x 3 bands)", book.Count == 72);
        Check("band thresholds 29.9/30/59.9/60", AddressBook.BandOf(29.9f) == AffinityBand.Stranger && AddressBook.BandOf(30f) == AffinityBand.Friendly
            && AddressBook.BandOf(59.9f) == AffinityBand.Friendly && AddressBook.BandOf(60f) == AffinityBand.Close && AddressBook.BandOf(-5f) == AffinityBand.Stranger);

        string[] genders = { "male", "female", "nonbinary", "neutral" };
        string[] locales = { "ko", "en" };
        float[] affs = { 0f, 40f, 80f };
        int resolved = 0;
        int empty = 0;
        int leftoverBrace = 0;
        int bannedHits = 0;
        List<string> bannedWhere = new List<string>();
        for (int ci = 0; ci < CharIds.All.Length; ci++)
        {
            for (int gi = 0; gi < genders.Length; gi++)
            {
                for (int bi = 0; bi < 3; bi++)
                {
                    for (int li = 0; li < locales.Length; li++)
                    {
                        Env e = MakeEnv(genders[gi], locales[li], "하늘");
                        e.State.Chars[CharIds.All[ci]].Aff = affs[bi];
                        string a = e.Pronouns.CallsPlayer(CharIds.All[ci]);
                        string b = e.Pronouns.PlayerCalls(CharIds.All[ci]);
                        resolved += 2;
                        if (a.Length == 0 || b.Length == 0) empty++;
                        if (a.Contains("{") || b.Contains("{")) leftoverBrace++;
                        bool neutralRow = genders[gi] == "nonbinary" || genders[gi] == "neutral";
                        bool haru = CharIds.All[ci] == CharIds.Haru;
                        if (neutralRow || haru)
                        {
                            string[] banned = locales[li] == "ko" ? BannedKo : BannedEn;
                            if (ContainsAny(a, banned) || ContainsAny(b, banned))
                            {
                                bannedHits++;
                                bannedWhere.Add(CharIds.All[ci] + "/" + genders[gi] + "/" + bi + "/" + locales[li] + ": " + a + " | " + b);
                            }
                        }
                    }
                }
            }
        }
        CheckEq("all 6 x 4 x 3 x 2 x 2 terms resolved", "288", resolved.ToString());
        Check("none empty", empty == 0);
        Check("no unreplaced {name}", leftoverBrace == 0);
        foreach (string w in bannedWhere) Console.WriteLine("    BANNED: " + w);
        Check("neutral/nonbinary player rows & haru rows have no gendered kinship terms (ko+en)", bannedHits == 0);

        // CSV 원본 열도 직접 검사(리졸버 폴백에 가려지지 않도록)
        CsvTable table = CsvTable.Parse(csv);
        int rawBanned = 0;
        int rawEmpty = 0;
        for (int i = 0; i < table.Rows.Count; i++)
        {
            CsvRow r = table.Rows[i];
            string g = r.Str("player_gender");
            bool neutralRow = g == "nonbinary" || g == "neutral" || r.Str("char") == "haru";
            if (r.Str("calls_player_ko").Length == 0 || r.Str("player_calls_ko").Length == 0 || r.Str("calls_player_en").Length == 0 || r.Str("player_calls_en").Length == 0) rawEmpty++;
            if (neutralRow && (ContainsAny(r.Str("calls_player_ko") + r.Str("player_calls_ko"), BannedKo) || ContainsAny(r.Str("calls_player_en") + r.Str("player_calls_en"), BannedEn))) rawBanned++;
        }
        Check("raw CSV: no empty cells", rawEmpty == 0);
        Check("raw CSV: no gendered kinship in neutral/nonbinary/haru rows", rawBanned == 0);

        // 구체 기대값
        Env m = MakeEnv("male", "ko", "하늘");
        m.State.Chars["howl"].Aff = 10;
        CheckEq("male->howl stranger", "하울 씨", m.Pronouns.PlayerCalls("howl"));
        m.State.Chars["howl"].Aff = 45;
        CheckEq("male->howl friendly", "하울 씨", m.Pronouns.PlayerCalls("howl"));
        m.State.Chars["howl"].Aff = 60;
        CheckEq("male->howl close = 하울 형", "하울 형", m.Pronouns.PlayerCalls("howl"));
        CheckEq("howl calls male player close", "하늘", m.Pronouns.CallsPlayer("howl"));
        Env f = MakeEnv("female", "ko", "하늘");
        f.State.Chars["howl"].Aff = 90;
        f.State.Chars["beomjun"].Aff = 90;
        CheckEq("female->howl close has no 오빠", "하울", f.Pronouns.PlayerCalls("howl"));
        CheckEq("female->beomjun close = 범준 오빠", "범준 오빠", f.Pronouns.PlayerCalls("beomjun"));
        Env nb = MakeEnv("nonbinary", "ko", "하늘");
        nb.State.Chars["beomjun"].Aff = 90;
        nb.State.Chars["haru"].Aff = 90;
        CheckEq("nonbinary->beomjun close neutral", "범준", nb.Pronouns.PlayerCalls("beomjun"));
        CheckEq("anyone->haru always 하루 씨", "하루 씨", nb.Pronouns.PlayerCalls("haru"));
        m.State.Chars["haru"].Aff = 95;
        f.State.Chars["haru"].Aff = 95;
        Check("male/female->haru 하루 씨 too", m.Pronouns.PlayerCalls("haru") == "하루 씨" && f.Pronouns.PlayerCalls("haru") == "하루 씨");
        m.State.Chars["haru"].Aff = 0;
        CheckEq("haru calls player stranger", "하늘 님", m.Pronouns.CallsPlayer("haru"));
        Env en = MakeEnv("female", "en", "Rin");
        en.State.Chars["gomseok"].Aff = 70;
        CheckEq("en player_calls close", "Gomseok", en.Pronouns.PlayerCalls("gomseok"));
        en.State.Chars["johan"].Aff = 40;
        CheckEq("en calls_player friendly (johan)", "Chef Rin", en.Pronouns.CallsPlayer("johan"));
        CheckEq("unknown character falls back to id", "zed", en.Pronouns.PlayerCalls("zed"));
        CheckEq("unknown character calls_player fallback (en)", "Rin", en.Pronouns.CallsPlayer("zed"));
        Env unk = MakeEnv("alien", "ko", "하늘");
        unk.State.Chars["johan"].Aff = 80;
        CheckEq("unknown gender -> neutral row", "요한", unk.Pronouns.PlayerCalls("johan"));

        // 숫자 규칙은 성별 무관: 구간은 호감만으로 결정
        bool sameBands = true;
        for (int gi = 0; gi < genders.Length; gi++)
        {
            Env g = MakeEnv(genders[gi], "ko", "A");
            g.State.Chars["nabi"].Aff = 59.9f;
            if (g.Pronouns.BandFor("nabi") != AffinityBand.Friendly) sameBands = false;
            g.State.Chars["nabi"].Aff = 60f;
            if (g.Pronouns.BandFor("nabi") != AffinityBand.Close) sameBands = false;
        }
        Check("band thresholds identical for every player gender", sameBands);

        // 스크립트 경유
        DialogueProject p = Inline("title: A\n---\nhowl: {calls_player(\"howl\")}, {player_calls(\"howl\")} / {affinity_band(\"howl\")} / {affinity(\"howl\")}\n===\n");
        Env se = MakeEnv("male", "ko", "하늘");
        se.State.Chars["howl"].Aff = 75;
        CheckEq("script calls_player/player_calls/affinity_band", "howl|하늘, 하울 형 / close / 75\nEND", Join(Run(new DialogueRunner(p, new InMemoryVariableStore(), se.Host), "A")));
        Exception wrongArgs = Catch(delegate
        {
            DialogueProject bad = Inline("title: A\n---\n{calls_player()}\n===\n");
            Run(new DialogueRunner(bad, new InMemoryVariableStore(), se.Host), "A");
        });
        Check("calls_player() arity error", wrongArgs is DialogueRuntimeException);
    }

    // ---------- 스트레스 ----------

    private static string GenerateBig(int nodes, int linesPerNode)
    {
        StringBuilder sb = new StringBuilder();
        for (int n = 0; n < nodes; n++)
        {
            sb.Append("title: Node").Append(n).Append("\n---\n");
            for (int k = 0; k < linesPerNode - 3; k++)
            {
                switch (k % 5)
                {
                    case 0: sb.Append("howl: 대사 ").Append(n).Append("-").Append(k).Append(" 골드 {$gold} #line:g").Append(n).Append("_").Append(k).Append("\n"); break;
                    case 1: sb.Append("<<set $gold += ").Append(k).Append(">>\n"); break;
                    case 2: sb.Append("player: {name} 이(가) 말한다 {$gold * 2 + ").Append(k).Append("} // 주석\n"); break;
                    case 3: sb.Append("<<if $gold > 100000>>\n    howl: 아주 부자 {pronoun_subj()}\n<<else>>\n    howl: 평범\n<<endif>>\n"); break;
                    default: sb.Append("<<show_standing howl smile>>\n"); break;
                }
            }
            sb.Append("<<jump Node").Append(n + 1 < nodes ? n + 1 : 0).Append(">>\n===\n\n");
        }
        return sb.ToString();
    }

    private static void TestStress()
    {
        Section("Stress (2,000+ lines)");
        string text = GenerateBig(40, 50);
        int lineCount = text.Split('\n').Length;
        Env e = MakeEnv("female", "en", "Rin");
        // 워밍업(JIT)
        DialogueProject warm = DialogueProject.Parse(GenerateBig(2, 10), "warm");
        Run(new DialogueRunner(warm, new InMemoryVariableStore(), e.Host), "Node0");

        Stopwatch sw = Stopwatch.StartNew();
        DialogueProject p = DialogueProject.Parse(text, "big");
        long parseMs = sw.ElapsedMilliseconds;
        DialogueRunner r = new DialogueRunner(p, new InMemoryVariableStore(), e.Host);
        r.Start("Node0");
        int steps = 0;
        int nodesSeen = 0;
        string last = string.Empty;
        // 40 노드를 한 번씩 돌고 Node0 으로 되돌아오면 중단
        while (steps < 100000)
        {
            DialogueStep s = r.Next();
            if (s.IsEnd) break;
            steps++;
            last = s.ToString(); if (last.Length < 0) break;
            if (r.CurrentNode == "Node0" && steps > 10 && nodesSeen > 0) break;
            if (r.CurrentNode != "Node0") nodesSeen = 1;
        }
        sw.Stop();
        List<LintIssue> li = DialogueLint.Run(p, new string[] { "Node0" });
        Console.WriteLine("    source lines={0} nodes={1} parse={2}ms parse+run={3}ms steps={4} lint issues={5}", lineCount, p.AllNodes.Count, parseMs, sw.ElapsedMilliseconds, steps, li.Count);
        Check("2000+ line script parsed", lineCount >= 2000 && p.AllNodes.Count == 40);
        Check("parse+run well under 1s", sw.ElapsedMilliseconds < 1000);
        Check("ran through all nodes", steps > 1000 && r.ExportVisited().Count == 40);
        Check("lint on big script: reachable, no errors", DialogueLint.Count(li, "UNREACHABLE") == 0 && DialogueLint.Count(li, "UNDEFINED_JUMP") == 0);
        Stopwatch sw2 = Stopwatch.StartNew();
        string tagged = LineIdTagger.AddMissing(text, "big");
        sw2.Stop();
        Check("tagger on 2000 lines < 1s", sw2.ElapsedMilliseconds < 1000 && tagged.Length > text.Length);
    }
}
