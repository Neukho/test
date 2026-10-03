using System;
using System.Collections.Generic;
using System.IO;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using MorpheHouse.Tycoon;

// 장면 게이트 검증 하네스(Unity 불필요). 빌드/실행: tools/csharp_check/gate_check/run.sh
//  A 규칙 표(실제 파일) · B 게이트 판정(가짜 컨텍스트) · C 실제 규칙의 핵심 계약 · D 자동 재생 스케줄러
//  E RomanceService 일정/게이트 연동 · F NightRomanceActions 연동 · G 64일 봇 플레이(모든 CE 한 번씩, 모든 루트 5이벤트) · H 게이트 켠 패리티
public static class Program
{
    private static int _checks;
    private static int _failures;

    private static void Check(string name, bool ok)
    {
        _checks++;
        if (!ok) _failures++;
        Console.WriteLine("  [{0}] {1}", ok ? "PASS" : "FAIL", name);
    }

    // ---------------------------------------------------------------- 공용

    private static string _dir;
    private static string _resDir;
    private static BalanceTable _balance;
    private static RomanceCatalog _catalog;
    private static SceneRuleTable _rules;

    private static readonly string[] ExpectedAuto =
    {
        "CE01_FirstGuest", "CE02_Tasting", "CE03_RentMeeting", "CE04_StaffWelcome", "CE05_FestivalPrep",
        "Ch5_Show", "Ch5_Show_After", "CE06_OldPhoto", "Ch6_LedgerTruth", "CE07_BlueHarborOpening", "CE08_TableTogether"
    };

    public static int Main(string[] args)
    {
        _dir = args.Length > 0 ? args[0] : "balance";
        _resDir = args.Length > 1 ? args[1] : "UnityProject/Assets/_Project/Resources/Balance";
        int parityRuns = args.Length > 2 ? int.Parse(args[2]) : 150;
        FileTextSource src = new FileTextSource(_dir);
        _balance = BalanceTable.Load(src, Difficulty.Normal);
        _catalog = RomanceCatalog.Load(src);
        _rules = SceneRuleTable.Load(src);

        TableChecks();
        FakeChecks();
        RealRuleChecks();
        SchedulerChecks();
        ScheduleServiceChecks();
        NightChecks();
        BotChecks();
        ParityChecks(parityRuns);

        Console.WriteLine();
        Console.WriteLine("gate_check: {0} checks, {1} failures", _checks, _failures);
        Console.WriteLine(_failures == 0 ? "ALL OK" : "FAILED");
        return _failures == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- 가짜 컨텍스트

    private sealed class FakeCtx : ISceneContext
    {
        public int Chapter { get; set; }
        public int DayInChapter { get; set; }
        public readonly HashSet<string> Visited = new HashSet<string>();
        public readonly HashSet<string> Missing = new HashSet<string>();
        public readonly HashSet<string> Flags = new HashSet<string>();
        public bool IsVisited(string node) { return Visited.Contains(node); }
        public bool NodeExists(string node) { return !Missing.Contains(node); }
        public bool HasFlag(string flag) { return Flags.Contains(flag); }
        public FakeCtx(int chapter, int day) { Chapter = chapter; DayInChapter = day; }
    }

    private const string Header = "node,kind,chapter,slot,auto_play,trigger_day_min,trigger_day_max,min_day_in_chapter,requires_flags,forbids_flags,after_node,before_node,note\n";

    private static SceneGate GateOf(string rows, FakeCtx ctx)
    {
        return new SceneGate(SceneRuleTable.Parse(Header + rows), ctx);
    }

    private static bool Blocked(SceneCheck c, string reason, string detail)
    {
        return !c.Allowed && c.Reason == reason && (detail == null || c.Detail == detail);
    }

    // ---------------------------------------------------------------- A. 규칙 표

    private static void TableChecks()
    {
        Console.WriteLine("== A. tables (real files) ==");
        Check("scene_rules.csv parses (rows > 40)", _rules.Count > 40);
        List<string> errs = _rules.Validate(delegate (int c) { return _balance.Chapter(c).Days; });
        if (errs.Count > 0) Console.WriteLine("    validate: " + string.Join(" | ", errs.ToArray()));
        Check("scene_rules.csv validates (days within chapter length, windows ordered)", errs.Count == 0);

        List<string> auto = new List<string>();
        for (int i = 0; i < _rules.Rules.Count; i++) if (_rules.Rules[i].AutoPlay) auto.Add(_rules.Rules[i].Node);
        Check("auto_play scenes are exactly CE01..CE08 + Ch5_Show/_After + Ch6_LedgerTruth in chronological order",
            string.Join(",", auto.ToArray()) == string.Join(",", ExpectedAuto));

        bool dayOrder = true;
        int prevKey = 0;
        for (int i = 0; i < _rules.Rules.Count; i++)
        {
            SceneRule r = _rules.Rules[i];
            if (!r.AutoPlay) continue;
            int key = r.Chapter * 100 + r.TriggerDayMin;
            if (key < prevKey) dayOrder = false;
            prevKey = key;
        }
        Check("auto table order is chronological (priority = table order)", dayOrder);

        // 하드 선행(~ 없음)은 항상 결국 재생되는 노드(챕터/공통)만 가리켜야 교착이 없다.
        bool deadlockFree = true;
        for (int i = 0; i < _rules.Rules.Count; i++)
        {
            SceneRule r = _rules.Rules[i];
            for (int a = 0; a < r.After.Count; a++)
            {
                if (r.After[a].Soft) continue;
                SceneRule dep = _rules.Find(r.After[a].Node);
                bool alwaysPlayed = dep != null && (dep.Kind == "chapter" || dep.AutoPlay);
                if (!alwaysPlayed) { deadlockFree = false; Console.WriteLine("    deadlock risk: " + r.Node + " hard-after " + r.After[a].Node); }
            }
        }
        Check("every hard after_node is a chapter/auto node (no personal event waits forever on another personal event)", deadlockFree);

        bool softOnlyAuto = true;
        for (int i = 0; i < _rules.Rules.Count; i++)
            for (int a = 0; a < _rules.Rules[i].After.Count; a++)
                if (_rules.Rules[i].After[a].Soft && !_rules.Rules[i].AutoPlay) softOnlyAuto = false;
        Check("soft (~) prerequisites appear only on auto-play scenes (they need a deadline)", softOnlyAuto);

        Check("event_schedule.csv has all 30 (char, step) rows", _catalog.Schedule.Count == 30 && AllThirty());
        bool noExpiry = true;
        bool monotone = true;
        for (int c = 0; c < CharIds.All.Length; c++)
        {
            int prev = 0;
            for (int s = 1; s <= 5; s++)
            {
                EventScheduleRow row = _catalog.Schedule.Find(CharIds.All[c], s);
                if (row == null) continue;
                if (row.ChapterMax != 99) noExpiry = false;
                if (row.ChapterMin < prev) monotone = false;
                prev = row.ChapterMin;
            }
        }
        Check("default: no event expires (chapter_max = 99 everywhere)", noExpiry);
        Check("per-character chapter_min never decreases with step", monotone);
        Check("nabi E2 = chapter 4 (events.csv says 3)", _catalog.Schedule.Find("nabi", 2).ChapterMin == 4 && _balance.Events[1].ChapterMin == 3);
        Check("haru E1 = chapter 4 (joins in ch4), johan E1 = chapter 2, beomjun E2 = chapter 4",
            _catalog.Schedule.Find("haru", 1).ChapterMin == 4 && _catalog.Schedule.Find("johan", 1).ChapterMin == 2 && _catalog.Schedule.Find("beomjun", 2).ChapterMin == 4);
        Check("every E4 is chapter 7 except gomseok (6); every E5 is chapter 8",
            _catalog.Schedule.Find("howl", 4).ChapterMin == 7 && _catalog.Schedule.Find("johan", 4).ChapterMin == 7 && _catalog.Schedule.Find("beomjun", 4).ChapterMin == 7
            && _catalog.Schedule.Find("haru", 4).ChapterMin == 7 && _catalog.Schedule.Find("nabi", 4).ChapterMin == 7 && _catalog.Schedule.Find("gomseok", 4).ChapterMin == 6
            && AllStep5Is8());

        // 선택 표가 없을 때
        Check("missing event_schedule / scene_rules tables are optional (empty, no throw)",
            EventSchedule.Parse(null).Count == 0 && SceneRuleTable.Parse(null).Count == 0 && SceneRuleTable.Parse("").Count == 0);

        // Resources 복사본
        Check("Resources/Balance copies are identical (event_schedule, scene_rules, events)",
            Same("event_schedule.csv") && Same("scene_rules.csv") && Same("events.csv"));
    }

    private static bool AllThirty()
    {
        for (int c = 0; c < CharIds.All.Length; c++)
            for (int s = 1; s <= 5; s++)
                if (_catalog.Schedule.Find(CharIds.All[c], s) == null) return false;
        return true;
    }

    private static bool AllStep5Is8()
    {
        for (int c = 0; c < CharIds.All.Length; c++)
            if (_catalog.Schedule.Find(CharIds.All[c], 5).ChapterMin != 8) return false;
        return true;
    }

    private static bool Same(string file)
    {
        string a = Path.Combine(_dir, file);
        string b = Path.Combine(_resDir, file);
        return File.Exists(a) && File.Exists(b) && File.ReadAllText(a) == File.ReadAllText(b);
    }

    // ---------------------------------------------------------------- B. 게이트 판정(가짜 컨텍스트)

    private static void FakeChecks()
    {
        Console.WriteLine("== B. gate semantics (fake context) ==");
        FakeCtx c = new FakeCtx(3, 1);
        SceneGate g = GateOf("X,event,3,,0,0,0,4,,,,,\n", c);
        Check("unknown node is always allowed", g.Check("Nothing").Allowed && g.Check(null).Allowed);
        Check("day gate: blocked before min_day, reason day_early:4", Blocked(g.Check("X"), SceneBlock.DayEarly, "4"));
        c.DayInChapter = 4;
        Check("day gate: allowed on min_day", g.Check("X").Allowed);
        c.Chapter = 4; c.DayInChapter = 1;
        Check("day gate: later chapter ignores the day", g.Check("X").Allowed);
        c.Chapter = 2;
        Check("chapter gate: earlier chapter blocked chapter_early", Blocked(g.Check("X"), SceneBlock.ChapterEarly, "3"));

        c = new FakeCtx(5, 5);
        g = GateOf("F,event,0,,0,0,0,0,flagA;flagB,,,,\n", c);
        Check("requires_flags: missing both -> needs_flag:flagA", Blocked(g.Check("F"), SceneBlock.NeedsFlag, "flagA"));
        c.Flags.Add("flagA");
        Check("requires_flags: one of two -> needs_flag:flagB", Blocked(g.Check("F"), SceneBlock.NeedsFlag, "flagB"));
        c.Flags.Add("flagB");
        Check("requires_flags: all present -> allowed", g.Check("F").Allowed);

        g = GateOf("N,event,0,,0,0,0,0,,bad,,,\n", c);
        Check("forbids_flags: absent -> allowed", g.Check("N").Allowed);
        c.Flags.Add("bad");
        Check("forbids_flags: present -> forbidden_flag:bad", Blocked(g.Check("N"), SceneBlock.ForbiddenFlag, "bad"));

        c = new FakeCtx(5, 5);
        g = GateOf("A,event,0,,0,0,0,0,,,P;Q,,\n", c);
        Check("after_node: nothing visited -> needs_node:P", Blocked(g.Check("A"), SceneBlock.NeedsNode, "P"));
        c.Visited.Add("P");
        Check("after_node: P visited, Q missing -> needs_node:Q", Blocked(g.Check("A"), SceneBlock.NeedsNode, "Q"));
        c.Visited.Add("Q");
        Check("after_node: both visited -> allowed", g.Check("A").Allowed);

        c = new FakeCtx(5, 5);
        g = GateOf("A,event,0,,0,0,0,0,,,P,,\n", c);
        c.Missing.Add("P");
        Check("after_node: a prerequisite node that does not exist in the project counts as satisfied", g.Check("A").Allowed);

        c = new FakeCtx(5, 5);
        g = GateOf("B,event,0,,0,0,0,0,,,,Late,\n", c);
        Check("before_node: later node not yet played -> allowed", g.Check("B").Allowed);
        c.Visited.Add("Late");
        Check("before_node: later node already played -> too_late:Late", Blocked(g.Check("B"), SceneBlock.TooLate, "Late"));

        // soft 선행: 자동 재생 창의 마지막 날부터 접힌다
        c = new FakeCtx(2, 6);
        g = GateOf("S,common,2,morning,1,6,8,6,,,~E1,,\n", c);
        Check("soft after: day 6 without E1 -> blocked needs_node:E1", Blocked(g.Check("S"), SceneBlock.NeedsNode, "E1"));
        c.DayInChapter = 7;
        Check("soft after: day 7 without E1 -> still blocked", !g.Check("S").Allowed);
        c.DayInChapter = 8;
        Check("soft after: deadline day 8 -> allowed without E1", g.Check("S").Allowed);
        c.DayInChapter = 6; c.Visited.Add("E1");
        Check("soft after: E1 visited -> allowed on day 6", g.Check("S").Allowed);
        c = new FakeCtx(3, 1);
        g = GateOf("S,common,2,morning,1,6,8,6,,,~E1,,\n", c);
        Check("soft after: a later chapter (overdue) -> allowed", g.Check("S").Allowed);

        // 효과적 최소 일차 = max(min_day, trigger_day_min)
        c = new FakeCtx(2, 5);
        g = GateOf("M,common,2,morning,1,7,8,5,,,,,\n", c);
        Check("auto scene min day = max(min_day_in_chapter, trigger_day_min) = 7", Blocked(g.Check("M"), SceneBlock.DayEarly, "7"));

        // 판정 순서
        c = new FakeCtx(1, 1);
        g = GateOf("O,event,3,,0,0,0,0,fl,,P,,\n", c);
        Check("order of checks: chapter first", Blocked(g.Check("O"), SceneBlock.ChapterEarly, null));
        c.Chapter = 3;
        Check("order of checks: flags before after_node", Blocked(g.Check("O"), SceneBlock.NeedsFlag, "fl"));

        // CSV 파싱
        SceneRuleTable t = SceneRuleTable.Parse(Header + "a,event,1,NIGHT_END,TRUE,2,3,1,x;y,z,~p;q,r;s,hello\n,,,,,,,,,,,,\nb,common,2,weird,0,0,0,0,,,,,\n");
        SceneRule ra = t.Find("a");
        Check("parse: slot/auto/flags/after(soft)/before", ra != null && ra.Slot == SceneSlot.NightEnd && ra.AutoPlay && ra.RequiresFlags.Count == 2
            && ra.ForbidsFlags[0] == "z" && ra.After.Count == 2 && ra.After[0].Soft && ra.After[0].Node == "p" && !ra.After[1].Soft && ra.Before.Count == 2 && ra.Note == "hello");
        Check("parse: blank node rows skipped, unknown slot defaults to morning", t.Count == 2 && t.Find("b").Slot == SceneSlot.Morning);
        SceneRuleTable dup = SceneRuleTable.Parse(Header + "d,event,1,,0,0,0,2,,,,,first\nd,event,1,,0,0,0,5,,,,,second\n");
        Check("parse: duplicate node, last row wins", dup.Count == 1 && dup.Find("d").MinDayInChapter == 5);
        List<string> bad = SceneRuleTable.Parse(Header + "a,common,0,morning,1,0,0,0,,,,,\nb,event,0,,0,0,0,3,,,,,\nc,common,2,morning,1,5,4,0,,,c,,\n").Validate(null);
        Check("validate: auto without chapter/trigger, min_day without chapter, window reversed, self reference", bad.Count >= 5);
    }

    // ---------------------------------------------------------------- C. 실제 규칙의 계약

    /// <summary>표에서 node 보다 앞선 자동 재생 장면을 모두 방문한 것으로 표시한다(그 장면만 보고 싶을 때).</summary>
    private static void VisitAutoBefore(FakeCtx c, string node)
    {
        for (int i = 0; i < _rules.Rules.Count; i++)
        {
            if (_rules.Rules[i].Node == node) return;
            if (_rules.Rules[i].AutoPlay) c.Visited.Add(_rules.Rules[i].Node);
        }
    }

    private static SceneGate RealGate(FakeCtx c)
    {
        return new SceneGate(_rules, c);
    }

    private static void RealRuleChecks()
    {
        Console.WriteLine("== C. real rules ==");
        FakeCtx c = new FakeCtx(2, 6);
        SceneGate g = RealGate(c);
        // CE02
        Check("CE02: day 6 of ch2 without Johan_E1 -> blocked (needs Johan_E1)", Blocked(g.Check("CE02_Tasting"), SceneBlock.NeedsNode, "Johan_E1"));
        c.DayInChapter = 5; c.Visited.Add("Johan_E1");
        Check("CE02: Johan_E1 played but day 5 -> blocked day_early:6", Blocked(g.Check("CE02_Tasting"), SceneBlock.DayEarly, "6"));
        c.DayInChapter = 6;
        Check("CE02: Johan_E1 played and day 6 -> allowed", g.Check("CE02_Tasting").Allowed);
        c = new FakeCtx(2, 8); g = RealGate(c);
        Check("CE02: last day 8 plays even without Johan_E1 (menu tutorial guaranteed)", g.Check("CE02_Tasting").Allowed);

        // Johan_E1 / Nabi_E1
        c = new FakeCtx(2, 4); g = RealGate(c);
        Check("Johan_E1: blocked before day 5 of ch2", Blocked(g.Check("Johan_E1"), SceneBlock.DayEarly, "5"));
        c.DayInChapter = 5;
        Check("Johan_E1: allowed from day 5 (Ch2_Intro never visited but node missing is fine only if absent) - needs Ch2_Intro", Blocked(g.Check("Johan_E1"), SceneBlock.NeedsNode, "Ch2_Intro"));
        c.Visited.Add("Ch2_Intro");
        Check("Johan_E1: allowed from day 5 after Ch2_Intro", g.Check("Johan_E1").Allowed);
        c.DayInChapter = 5;
        Check("Nabi_E1: blocked on day 5 (end of ch2 = day 6+)", Blocked(g.Check("Nabi_E1"), SceneBlock.DayEarly, "6"));

        // 3장: CE03 / 개인 이벤트
        c = new FakeCtx(3, 4); g = RealGate(c);
        c.Visited.Add("Ch3_Intro");
        Check("CE03: day 4 without Beomjun_E1/Howl_E2 -> waits (soft)", Blocked(g.Check("CE03_RentMeeting"), SceneBlock.NeedsNode, "Beomjun_E1"));
        c.Visited.Add("Beomjun_E1");
        Check("CE03: Beomjun_E1 only -> still waits for Howl_E2", Blocked(g.Check("CE03_RentMeeting"), SceneBlock.NeedsNode, "Howl_E2"));
        c.Visited.Add("Howl_E2");
        Check("CE03: both played -> allowed", g.Check("CE03_RentMeeting").Allowed);
        c.Visited.Clear(); c.Visited.Add("Ch3_Intro"); c.DayInChapter = 6;
        Check("CE03: day 6 deadline -> plays without them", g.Check("CE03_RentMeeting").Allowed);
        c.DayInChapter = 2;
        Check("Johan_E2: allowed day 2 after Ch3_Intro, Howl_E2/Beomjun_E1 need day 3",
            g.Check("Johan_E2").Allowed && Blocked(g.Check("Howl_E2"), SceneBlock.DayEarly, "3") && Blocked(g.Check("Beomjun_E1"), SceneBlock.DayEarly, "3"));

        // 4장
        c = new FakeCtx(4, 3); c.Visited.Add("Ch4_Intro"); g = RealGate(c);
        Check("Haru_E1: blocked on day 3 (3 days after interview = day 4)", Blocked(g.Check("Haru_E1"), SceneBlock.DayEarly, "4"));
        c.DayInChapter = 4;
        Check("Haru_E1: allowed day 4", g.Check("Haru_E1").Allowed);
        Check("Beomjun_E2: needs CE04_StaffWelcome (order card foreshadow)", Blocked(g.Check("Beomjun_E2"), SceneBlock.NeedsNode, "CE04_StaffWelcome"));
        c.Visited.Add("CE04_StaffWelcome");
        Check("Beomjun_E2: allowed after CE04", g.Check("Beomjun_E2").Allowed);
        Check("Nabi_E2: allowed after Ch4_Intro (stage rumour)", g.Check("Nabi_E2").Allowed);

        // 5장 순서
        c = new FakeCtx(5, 7); g = RealGate(c);
        c.Visited.Add("Ch5_Intro"); c.Visited.Add("CE05_FestivalPrep");
        Check("Ch5: Johan_E3 and Nabi_E3 blocked until Ch5_Show", Blocked(g.Check("Johan_E3"), SceneBlock.NeedsNode, "Ch5_Show") && Blocked(g.Check("Nabi_E3"), SceneBlock.NeedsNode, "Ch5_Show"));
        Check("Ch5: Ch5_Show_After blocked until Ch5_Show", Blocked(g.Check("Ch5_Show_After"), SceneBlock.NeedsNode, "Ch5_Show"));
        c.Visited.Add("Ch5_Show");
        Check("Ch5: after Ch5_Show -> Johan_E3 and Nabi_E3 open", g.Check("Johan_E3").Allowed && g.Check("Nabi_E3").Allowed);
        c.DayInChapter = 8;
        Check("Ch5: Howl_E3 blocked until Ch5_Show_After (even on day 8)", Blocked(g.Check("Howl_E3"), SceneBlock.NeedsNode, "Ch5_Show_After"));
        c.DayInChapter = 7;
        c.Visited.Add("Ch5_Show_After");
        Check("Ch5: Howl_E3 still blocked the same night (day 7, one night later = day 8)", Blocked(g.Check("Howl_E3"), SceneBlock.DayEarly, "8"));
        c.DayInChapter = 8;
        Check("Ch5: Howl_E3 allowed on day 8 once Ch5_Show_After has played", g.Check("Howl_E3").Allowed);
        Check("Ch5_Show may not replay after Ch5_Show_After (before_node)", Blocked(g.Check("Ch5_Show"), SceneBlock.TooLate, "Ch5_Show_After"));
        c = new FakeCtx(5, 6); c.Visited.Add("Ch5_Intro"); g = RealGate(c);
        Check("Haru_E2: night before the show = day 6 (blocked day 5, allowed day 6)", g.Check("Haru_E2").Allowed);
        c.DayInChapter = 5;
        Check("Haru_E2: blocked on day 5", Blocked(g.Check("Haru_E2"), SceneBlock.DayEarly, "6"));
        c = new FakeCtx(5, 7); g = RealGate(c);
        Check("Ch5_Show needs CE05_FestivalPrep first", Blocked(g.Check("Ch5_Show"), SceneBlock.NeedsNode, "CE05_FestivalPrep"));

        // 6장
        c = new FakeCtx(6, 4); g = RealGate(c);
        Check("Gomseok_E4 is blocked until CE06_OldPhoto has been played", Blocked(g.Check("Gomseok_E4"), SceneBlock.NeedsNode, "CE06_OldPhoto"));
        c.Visited.Add("Ch6_Intro"); c.Visited.Add("CE06_OldPhoto");
        Check("Gomseok_E4 allowed after CE06_OldPhoto", g.Check("Gomseok_E4").Allowed);
        Check("Haru_E3 waits for the ledger night (Ch6_LedgerTruth)", Blocked(g.Check("Haru_E3"), SceneBlock.NeedsNode, "Ch6_LedgerTruth"));
        Check("Ch6_LedgerTruth is a day-10 night scene (blocked day 4)", Blocked(g.Check("Ch6_LedgerTruth"), SceneBlock.DayEarly, "10"));
        c.DayInChapter = 10; c.Visited.Add("Ch6_LedgerTruth");
        Check("Haru_E3 allowed after Ch6_LedgerTruth", g.Check("Haru_E3").Allowed);
        Check("CE06 may not replay after Ch6_LedgerTruth (before_node)", Blocked(g.Check("CE06_OldPhoto"), SceneBlock.TooLate, "Ch6_LedgerTruth"));

        // 7장
        c = new FakeCtx(7, 3); g = RealGate(c);
        string[] e4 = { "Howl_E4", "Johan_E4", "Beomjun_E4", "Haru_E4", "Nabi_E4" };
        bool allNeedCe07 = true;
        for (int i = 0; i < e4.Length; i++) if (g.Check(e4[i]).Allowed) allNeedCe07 = false;
        Check("every E4 pressure event waits for CE07_BlueHarborOpening (and thus Ch7_Intro + route_choice)", allNeedCe07);
        Check("CE07 itself waits for Ch7_Intro", Blocked(g.Check("CE07_BlueHarborOpening"), SceneBlock.NeedsNode, "Ch7_Intro"));
        c.Visited.Add("Ch7_Intro"); c.Visited.Add("CE07_BlueHarborOpening"); c.Visited.Add("CE06_OldPhoto"); c.Visited.Add("Ch6_Outro");
        bool allOpen = true;
        for (int i = 0; i < e4.Length; i++) if (!g.Check(e4[i]).Allowed) allOpen = false;
        Check("after CE07 (and the Howl drawer clue prerequisites) all E4 open on day 3", allOpen);
        c.DayInChapter = 2;
        Check("E4 stays closed on days 1-2 of ch7 (CE07 day, 'third day after the opening')", Blocked(g.Check("Johan_E4"), SceneBlock.DayEarly, "3"));
        c = new FakeCtx(7, 5); c.Visited.Add("Ch7_Intro"); c.Visited.Add("CE07_BlueHarborOpening"); g = RealGate(c);
        Check("Howl_E4 needs CE06_OldPhoto and Ch6_Outro (drawer_key_clue retry) as well",
            Blocked(g.Check("Howl_E4"), SceneBlock.NeedsNode, "CE06_OldPhoto"));
        c.Visited.Add("CE06_OldPhoto");
        Check("Howl_E4 needs Ch6_Outro (drawer clue retry choice)", Blocked(g.Check("Howl_E4"), SceneBlock.NeedsNode, "Ch6_Outro"));

        // 8장
        c = new FakeCtx(8, 4); g = RealGate(c);
        c.Visited.Add("Ch8_Intro");
        bool allE5Blocked = true;
        string[] e5 = { "Howl_E5", "Johan_E5", "Beomjun_E5", "Haru_E5", "Gomseok_E5", "Nabi_E5" };
        for (int i = 0; i < e5.Length; i++) if (g.Check(e5[i]).Allowed) allE5Blocked = false;
        Check("every E5 waits for CE08_TableTogether", allE5Blocked);
        Check("Nabi_E5 needs Ch8_Intro first", RealGate(new FakeCtx(8, 4)).Check("Nabi_E5").Reason == SceneBlock.NeedsNode && RealGate(new FakeCtx(8, 4)).Check("Nabi_E5").Detail == "Ch8_Intro");
        c.Visited.Add("CE08_TableTogether");
        Check("Gomseok_E5 needs no text-set flag (CE08 visited implies chain_crisis_resolved)", g.Check("Gomseok_E5").Allowed);
        bool allE5Open = true;
        for (int i = 0; i < e5.Length; i++) if (!g.Check(e5[i]).Allowed) allE5Open = false;
        Check("after CE08 all six E5 open", allE5Open);
        Check("CE08 itself waits for Ch8_Intro", Blocked(RealGate(new FakeCtx(8, 3)).Check("CE08_TableTogether"), SceneBlock.NeedsNode, "Ch8_Intro"));

        // 챕터 노드
        Check("Ch6_Outro belongs to chapter 7 day 1 (blocked in chapter 6)", Blocked(RealGate(new FakeCtx(6, 10)).Check("Ch6_Outro"), SceneBlock.ChapterEarly, "7"));
        Check("Howl_E1 / Gomseok_E1 / Gomseok_E2 have no day gates (events.csv + schedule chapter only)",
            RealGate(new FakeCtx(1, 1)).Check("Howl_E1").Allowed && RealGate(new FakeCtx(1, 1)).Check("Gomseok_E1").Allowed && RealGate(new FakeCtx(3, 1)).Check("Gomseok_E2").Allowed);
    }

    // ---------------------------------------------------------------- D. 자동 재생 스케줄러

    private static void SchedulerChecks()
    {
        Console.WriteLine("== D. scheduler ==");
        FakeCtx c = new FakeCtx(1, 4);
        SceneGate g = RealGate(c);
        Check("ch1 day 4 morning: nothing (CE01 is day 5)", g.NextAutoScene(SceneSlot.Morning) == null);
        c.DayInChapter = 5; c.Visited.Add("Ch1_Intro");
        SceneRule r = g.NextAutoScene(SceneSlot.Morning);
        Check("ch1 day 5 morning: CE01_FirstGuest", r != null && r.Node == "CE01_FirstGuest");
        c.Visited.Add("CE01_FirstGuest");
        Check("after it is visited it does not come back (exactly once)", g.NextAutoScene(SceneSlot.Morning) == null && g.NextAutoScene(SceneSlot.NightEnd) == null);

        c = new FakeCtx(4, 5); c.Visited.Add("Ch4_Intro"); VisitAutoBefore(c, "CE04_StaffWelcome"); g = RealGate(c);
        Check("CE04 is a night scene: not offered in the morning slot", g.NextAutoScene(SceneSlot.Morning) == null);
        r = g.NextAutoScene(SceneSlot.Night);
        Check("CE04 waits for Haru_E1 until the last window day (day 5: nothing offered)", r == null);
        c.Visited.Add("Haru_E1");
        r = g.NextAutoScene(SceneSlot.Night);
        Check("CE04 offered at night start after Haru_E1", r != null && r.Node == "CE04_StaffWelcome");
        Check("CE04 is also offered at night end (missed slots catch up the same day)", g.NextAutoScene(SceneSlot.NightEnd) != null);

        // Ch5_Show_After 는 night_end
        c = new FakeCtx(5, 7);
        foreach (string n in new string[] { "Ch5_Intro", "CE05_FestivalPrep", "Ch5_Show" }) c.Visited.Add(n);
        VisitAutoBefore(c, "Ch5_Show_After");
        g = RealGate(c);
        Check("Ch5_Show_After is not offered at night start", g.NextAutoScene(SceneSlot.Night) == null);
        r = g.NextAutoScene(SceneSlot.NightEnd);
        Check("Ch5_Show_After is offered at night end of day 7", r != null && r.Node == "Ch5_Show_After");
        c.DayInChapter = 8;
        r = g.NextAutoScene(SceneSlot.Morning);
        Check("Ch5_Show_After overdue next morning (night end never reported) -> plays then", r != null && r.Node == "Ch5_Show_After");

        // 지연 장면 우선순위
        c = new FakeCtx(6, 1);
        c.Visited.Add("Ch6_Intro"); c.Visited.Add("Ch1_Intro");
        g = RealGate(c);
        r = g.NextAutoScene(SceneSlot.Morning);
        Check("old-save catch-up: from ch6 day 1 the oldest unplayed scene (CE01) comes first", r != null && r.Node == "CE01_FirstGuest");
        Check("PendingAutoScenes lists unplayed auto scenes in table order", g.PendingAutoScenes().Count == 11 && g.PendingAutoScenes()[0] == "CE01_FirstGuest");

        // 노드 없음
        c = new FakeCtx(1, 5); c.Visited.Add("Ch1_Intro"); c.Missing.Add("CE01_FirstGuest");
        g = RealGate(c);
        Check("a node missing from the project is skipped silently", g.NextAutoScene(SceneSlot.Morning) == null);

        // ChapterFlow 연동
        c = new FakeCtx(1, 5); c.Visited.Add("Ch1_Intro"); g = RealGate(c);
        ChapterStep st = ChapterFlow.NextCommonScene(g, SceneSlot.Morning);
        Check("ChapterFlow.NextCommonScene returns kind CommonScene + node", st.Kind == ChapterStepKind.CommonScene && st.Node == "CE01_FirstGuest");
        Check("ChapterFlow.NextCommonScene(null gate) -> NoStep", ChapterFlow.NextCommonScene(null, SceneSlot.Morning).Kind == ChapterStepKind.None);
        Check("existing ChapterFlow.NextMorningStep unchanged (day>1 -> NoStep)", ChapterFlow.NextMorningStep(2, 2, true, null, null).Kind == ChapterStepKind.None);

        // 창 판정
        c = new FakeCtx(2, 8); g = RealGate(c);
        SceneRule ce02 = _rules.Find("CE02_Tasting");
        Check("window: day 8 is the deadline but not overdue; day 9 impossible -> ch3 is overdue",
            g.DeadlinePassed(ce02) && !g.IsOverdue(ce02) && RealGate(new FakeCtx(3, 1)).IsOverdue(ce02));
    }

    // ---------------------------------------------------------------- E. RomanceService 일정 + 게이트

    private static GameState Mk(int chapter, int day)
    {
        GameState s = GameState.NewGame(_balance, Difficulty.Normal, "t");
        s.Chapter = chapter;
        s.DayInChapter = day;
        s.Day = 20;
        s.Phase = DayPhase.Night;
        s.ActionsLeft = 2;
        s.Fatigue = 0f;
        s.Gold = 5000f;
        return s;
    }

    private static void Set(GameState s, string id, float aff, float trust, int events)
    {
        CharState c = s.Chars[id];
        c.Aff = aff; c.Trust = trust; c.EventsDone = events;
    }

    private static RomanceService Svc(GameState s, RomanceCatalog cat)
    {
        return new RomanceService(s, _balance, cat, new Random(5).NextDouble);
    }

    private static void ScheduleServiceChecks()
    {
        Console.WriteLine("== E. RomanceService schedule + gate ==");
        GameState s = Mk(3, 1);
        Set(s, "nabi", 40, 10, 1);
        RomanceService svc = Svc(s, _catalog);
        Check("nabi E2 NOT offered in ch3 although events.csv allows chapter 3", svc.GetAvailableEvent("nabi") == null);
        Check("EventChapterMin(nabi,2)=4 and EventChapterMin(howl,2)=3", svc.EventChapterMin("nabi", 2) == 4 && svc.EventChapterMin("howl", 2) == 3);
        s.Chapter = 4;
        EventRow e = svc.GetAvailableEvent("nabi");
        Check("nabi E2 offered in ch4 (aff 40 / trust 10)", e != null && e.Step == 2 && e.ChapterMin == 4);
        Check("overridden row is a copy: events.csv row unchanged (3)", _balance.Events[1].ChapterMin == 3);
        Check("CompleteEvent works in ch4 and refuses in ch3",
            svc.CompleteEvent("nabi", 2) && RefusedInCh3());

        // 카탈로그 없이/표 없이 = events.csv
        GameState s2 = Mk(3, 1);
        Set(s2, "nabi", 40, 10, 1);
        Check("no catalog: falls back to events.csv (nabi E2 at ch3)", new RomanceService(s2, _balance, new Random(1).NextDouble).GetAvailableEvent("nabi") != null);
        RomanceCatalog bare = RomanceCatalog.Load(new FileTextSource(_dir));
        bare.Schedule = new EventSchedule();
        Check("empty schedule: falls back to events.csv", Svc(s2, bare).GetAvailableEvent("nabi") != null);
        Check("an unrelated character with no row uses the generic rule", NoRowFallback());

        // 만료
        RomanceCatalog exp = RomanceCatalog.Load(new FileTextSource(_dir));
        exp.Schedule = EventSchedule.Parse("char,step,chapter_min,chapter_max,note\nhowl,3,5,5,test\nhowl,4,0,,test\n");
        GameState s3 = Mk(5, 1);
        Set(s3, "howl", 60, 20, 2);
        RomanceService svc3 = Svc(s3, exp);
        Check("chapter_max: open in its last chapter", svc3.GetAvailableEvent("howl") != null && !svc3.IsEventExpired("howl"));
        s3.Chapter = 6;
        Check("chapter_max: expired after the last chapter (null + IsEventExpired)", svc3.GetAvailableEvent("howl") == null && svc3.IsEventExpired("howl"));
        Check("chapter_max blank means no expiry (99); chapter_min blank falls back to events.csv",
            svc3.EventChapterMax("howl", 4) == 99 && svc3.EventChapterMin("howl", 4) == _balance.Events[3].ChapterMin);
        Check("EventSchedule.Parse: skips blank char and step<1 rows", EventSchedule.Parse("char,step,chapter_min,chapter_max,note\n,1,2,3,x\nhowl,0,1,1,y\nhowl,2,3,4,z\n").Count == 1);

        // 게이트
        GameState s4 = Mk(5, 8);
        Set(s4, "howl", 60, 20, 2);
        RomanceService svc4 = Svc(s4, _catalog);
        List<string> asked = new List<string>();
        bool open = false;
        svc4.EventGate = delegate (string n) { asked.Add(n); return open; };
        Check("EventGate closed: Howl_E3 not offered, gate asked with node name", svc4.GetAvailableEvent("howl") == null && asked.Count == 1 && asked[0] == "Howl_E3");
        Check("EventGate closed: CompleteEvent refused event_not_available, no action spent",
            svc4.CompleteEventResult("howl", 3).Reason == RomanceRefusal.EventNotAvailable && s4.ActionsLeft == 2 && s4.Chars["howl"].EventsDone == 2);
        open = true;
        Check("EventGate open: offered and completes", svc4.GetAvailableEvent("howl") != null && svc4.CompleteEvent("howl", 3) && s4.Chars["howl"].EventsDone == 3);
        GameState s5 = Mk(5, 8);
        Set(s5, "howl", 10, 0, 2);
        RomanceService svc5 = Svc(s5, _catalog);
        bool asked5 = false;
        svc5.EventGate = delegate (string n) { asked5 = true; return true; };
        Check("EventGate is only consulted after thresholds pass (aff too low -> null without asking)", svc5.GetAvailableEvent("howl") == null && !asked5);
        Check("RomanceService.EventNode naming", RomanceService.EventNode("howl", 3) == "Howl_E3" && RomanceService.EventNode("beomjun", 1) == "Beomjun_E1");
    }

    private static bool RefusedInCh3()
    {
        GameState s = Mk(3, 1);
        Set(s, "nabi", 40, 10, 1);
        RomanceService svc = Svc(s, _catalog);
        return !svc.CompleteEvent("nabi", 2) && s.ActionsLeft == 2;
    }

    private static bool NoRowFallback()
    {
        RomanceCatalog cat = RomanceCatalog.Load(new FileTextSource(_dir));
        cat.Schedule = EventSchedule.Parse("char,step,chapter_min,chapter_max,note\nnabi,2,4,99,x\n");
        GameState s = Mk(3, 1);
        Set(s, "howl", 40, 10, 1);
        Set(s, "nabi", 40, 10, 1);
        RomanceService svc = Svc(s, cat);
        return svc.GetAvailableEvent("howl") != null && svc.GetAvailableEvent("nabi") == null;
    }

    // ---------------------------------------------------------------- F. NightRomanceActions 연동

    private static HashSet<string> AllNodes()
    {
        HashSet<string> nodes = new HashSet<string>();
        for (int i = 0; i < _rules.Rules.Count; i++) nodes.Add(_rules.Rules[i].Node);
        for (int c = 0; c < CharIds.All.Length; c++)
            for (int st = 1; st <= 5; st++) nodes.Add(RomanceService.EventNode(CharIds.All[c], st));
        return nodes;
    }

    private static void NightChecks()
    {
        Console.WriteLine("== F. NightRomanceActions wiring ==");
        HashSet<string> nodes = AllNodes();
        HashSet<string> visited = new HashSet<string>();

        GameState s = Mk(6, 3);
        Set(s, "gomseok", 70, 26, 3);
        RomanceService svc = Svc(s, _catalog);
        NightRomanceActions n = new NightRomanceActions(s, _balance, svc, _catalog, nodes.Contains, visited.Contains);
        Check("gate auto-enabled from catalog scene_rules when visit info is given", n.Gate != null && n.Gate.Table.Count == _rules.Count);
        Check("night menu: Gomseok_E4 hidden before CE06 (HasEvent false, Event refused, no action spent)",
            !n.HasEvent("gomseok") && !n.Event("gomseok").Success && s.ActionsLeft == 2);
        Check("QA-11: hint for a gated-but-threshold-met event is the non-spoiling reason, never blank", n.EventHint("gomseok") == NightRomanceActions.EventNotYetText && NightRomanceActions.EventNotYetText.Length > 0);
        visited.Add("Ch6_Intro"); visited.Add("CE06_OldPhoto");
        Check("after CE06 the star appears and the event plays Gomseok_E4", n.HasEvent("gomseok") && n.Event("gomseok").NodeName == "Gomseok_E4" && s.Chars["gomseok"].EventsDone == 4);

        GameState s2 = Mk(3, 1);
        Set(s2, "nabi", 40, 10, 1);
        NightRomanceActions n2 = new NightRomanceActions(s2, _balance, Svc(s2, _catalog), _catalog, nodes.Contains, new HashSet<string>().Contains);
        Check("hint uses the per-character schedule: nabi E2 says chapter 4", n2.EventHint("nabi").Contains("4장"));
        GameState s2b = Mk(4, 1);
        Set(s2b, "nabi", 40, 10, 1);
        Check("... and once ch4 thresholds are met it shows no threshold text (gate reason only, never blank)", new NightRomanceActions(s2b, _balance, Svc(s2b, _catalog), _catalog, nodes.Contains, new HashSet<string>().Contains).EventHint("nabi") == NightRomanceActions.EventNotYetText);

        GameState s3 = Mk(6, 3);
        Set(s3, "gomseok", 70, 26, 3);
        NightRomanceActions n3 = new NightRomanceActions(s3, _balance, Svc(s3, _catalog), _catalog, nodes.Contains, null);
        Check("no visit info (null hasVisited): gate stays off, behaviour unchanged", n3.Gate == null && n3.HasEvent("gomseok"));
        RomanceCatalog noRules = RomanceCatalog.Load(new FileTextSource(_dir));
        noRules.SceneRulesText = null;
        GameState s4 = Mk(6, 3);
        Set(s4, "gomseok", 70, 26, 3);
        Check("no scene_rules table: gate off", new NightRomanceActions(s4, _balance, Svc(s4, noRules), noRules, nodes.Contains, visited.Contains).Gate == null);
        n.DisableSceneGate();
        Check("DisableSceneGate clears RomanceService.EventGate", svc.EventGate == null && n.Gate == null);
        n.EnableSceneGate(_rules);
        Check("EnableSceneGate(table) re-arms the gate", n.Gate != null && svc.EventGate != null);

        GameState s5 = Mk(5, 7);
        HashSet<string> v5 = new HashSet<string>();
        v5.Add("Ch1_Intro");
        NightRomanceActions n5 = new NightRomanceActions(s5, _balance, Svc(s5, _catalog), _catalog, nodes.Contains, v5.Contains);
        Check("NightRomanceActions.NextCommonScene: ch5 day 7 with nothing visited -> oldest overdue scene (CE01)", n5.NextCommonScene(SceneSlot.Night).Node == "CE01_FirstGuest");
        Check("route lock is unaffected by the gate (candidates still computed)", n5.RouteLockCandidates().Count == 0);
    }

    // ---------------------------------------------------------------- G. 64일 봇 플레이

    private sealed class Played
    {
        public string Node;
        public int Chapter;
        public int Day;
        public int Global;
        public string Kind;
    }

    private sealed class BotResult
    {
        public readonly List<Played> Log = new List<Played>();
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        public GameState State;
        public SceneGate Gate;
        public int TargetEvents;
        public int[] GroupEvents;
        public float Aff;
        public float Trust;
        public bool Good;

        public Played Find(string node)
        {
            for (int i = 0; i < Log.Count; i++) if (Log[i].Node == node) return Log[i];
            return null;
        }

        public int IndexOf(string node)
        {
            for (int i = 0; i < Log.Count; i++) if (Log[i].Node == node) return i;
            return -1;
        }
    }

    private sealed class Bot
    {
        public GameState State;
        public DayCycle Cycle;
        public RomanceService Svc;
        public NightRomanceActions Night;
        public HashSet<string> Nodes;
        public HashSet<string> Visited = new HashSet<string>();
        public BotResult Res = new BotResult();
        public bool GateOn;

        public void Play(string node, string kind)
        {
            Visited.Add(node);
            Played p = new Played();
            p.Node = node; p.Kind = kind; p.Chapter = State.Chapter; p.Day = State.DayInChapter; p.Global = State.Day;
            Res.Log.Add(p);
            int count;
            Res.Counts.TryGetValue(node, out count);
            Res.Counts[node] = count + 1;
        }

        public void DrainScenes(SceneSlot slot)
        {
            if (!GateOn) return;
            for (int guard = 0; guard < 30; guard++)
            {
                ChapterStep st = Night.NextCommonScene(slot);
                if (st.Kind == ChapterStepKind.None) return;
                Play(st.Node, "common");
            }
            throw new InvalidOperationException("scene drain did not terminate");
        }

        public void DrainMorning()
        {
            if (!GateOn) return;
            for (int guard = 0; guard < 30; guard++)
            {
                ChapterStep st = ChapterFlow.NextMorningStep(State.Chapter, State.DayInChapter, Cycle.ChapterIntroPending, Nodes.Contains, Visited.Contains);
                if (st.Kind == ChapterStepKind.None) break;
                if (st.Kind == ChapterStepKind.InfoCard) { Cycle.AckChapterIntro(); continue; }
                Play(st.Node, "chapter");
            }
            DrainScenes(SceneSlot.Morning);
        }
    }

    private static string NeutralSpot(string charId, int chapter)
    {
        List<SpotRow> open = _catalog.UnlockedSpots(chapter);
        for (int i = 0; i < open.Count; i++)
            if (_catalog.GetPreference(charId, PrefKind.Spot, open[i].Id) == Preference.Neutral) return open[i].Id;
        return open[0].Id;
    }

    /// <summary>romance_check 의 밤 행동 정책(simulate_balance.py 이식) + 장면 호스트(아침/밤 시작/밤 끝 자동 재생 + 개인 이벤트 방문 기록).</summary>
    /// <summary>대화/이벤트 노드가 주는 선택지 보상의 모델(params.csv talk_*, events.csv script_*): 이 봇은 노드를 실행하지 않고 AdjustAffection 으로 대신한다.</summary>
    private static void ScriptModel(RomanceService svc, string who, int eventStep, bool isEvent)
    {
        if (isEvent)
        {
            if (_scriptEv == null)
            {
                _scriptEv = new Dictionary<int, float[]>();
                foreach (CsvRow r in CsvTable.Parse(File.ReadAllText("balance/events.csv")).Rows) _scriptEv[r.Int("step")] = new float[] { r.Float("script_aff"), r.Float("script_trust") };
            }
            float[] g;
            if (_scriptEv.TryGetValue(eventStep, out g)) svc.AdjustAffection(who, g[0], g[1], "dialogue:event");
            return;
        }
        for (int k = 0; k < (int)_balance.P("talk_choice_points"); k++)
        {
            double q = _modelRng.NextDouble();
            if (q < _balance.P("talk_right_prob")) svc.AdjustAffection(who, _balance.Actions["choice_success"].AffGain, _balance.Actions["choice_success"].TrustGain, "dialogue:talk");
            else if (q < _balance.P("talk_right_prob") + _balance.P("talk_wrong_prob")) svc.AdjustAffection(who, _balance.Actions["choice_fail"].AffGain, _balance.Actions["choice_fail"].TrustGain, "dialogue:talk");
        }
    }

    private static Dictionary<int, float[]> _scriptEv;
    private static readonly Random _modelRng = new Random(12345);

    private static BotResult Play(string target, double focus, int seed, bool gateOn)
    {
        return Play(new string[] { target }, focus, seed, gateOn);
    }

    /// <summary>group 이 2~3명이면 공동 루트: 밤 행동을 구성원에게 돌아가며 쓰고 7장에 TryLockGroup 으로 확정한다.</summary>
    private static BotResult Play(string[] group, double focus, int seed, bool gateOn)
    {
        string target = group[0];
        int turn = 0;
        SystemRandomAdapter rng = new SystemRandomAdapter(seed);
        Func<double> next = rng.NextDouble;
        Bot bot = new Bot();
        bot.GateOn = gateOn;
        bot.State = GameState.NewGame(_balance, Difficulty.Normal, "bot");
        GameState state = bot.State;
        bot.Cycle = new DayCycle(state, _balance, rng);
        bot.Svc = new RomanceService(state, _balance, _catalog, next);
        bot.Nodes = AllNodes();
        if (gateOn) bot.Night = new NightRomanceActions(state, _balance, bot.Svc, _catalog, bot.Nodes.Contains, bot.Visited.Contains);
        DayCycle cycle = bot.Cycle;
        RomanceService svc = bot.Svc;

        List<string> others = new List<string>();
        for (int i = 0; i < CharIds.All.Length; i++) if (Array.IndexOf(group, CharIds.All[i]) < 0) others.Add(CharIds.All[i]);
        Dictionary<string, string[]> gifts = new Dictionary<string, string[]>();
        for (int i = 0; i < CharIds.All.Length; i++)
        {
            string id = CharIds.All[i];
            string like = _catalog.TagsFor(id, PrefKind.Gift, Preference.Like)[0];
            string dislike = _catalog.TagsFor(id, PrefKind.Gift, Preference.Dislike)[0];
            string neutral = null;
            foreach (KeyValuePair<string, GiftRow> g in _catalog.Gifts)
                if (_catalog.GetPreference(id, PrefKind.Gift, g.Key) == Preference.Neutral) { neutral = g.Key; break; }
            gifts[id] = new string[] { like, neutral, dislike };
        }

        cycle.Begin();
        while (!state.Finished)
        {
            bot.DrainMorning();
            float cost;
            if (cycle.CanUpgradeShop(out cost) && state.Gold >= cost * 1.15f) cycle.TryUpgradeShop();
            if (state.Chapter >= RomanceRules.LockMinChapter && state.LockedRoute.Length == 0)
            {
                if (group.Length == 1) svc.TryLockRoute(target); else svc.TryLockGroup(group);
            }

            cycle.RunBusiness();
            cycle.BeginNight();
            bot.DrainScenes(SceneSlot.Night);
            while (state.ActionsLeft > 0)
            {
                if (cycle.FreeRestAvailable) cycle.DoNightAction(NightAction.Rest);

                // 목표들의 개인 이벤트가 열려 있으면 가장 먼저(qa_hunt/파이썬 기준 플레이어)
                bool didEvent = false;
                for (int gi = 0; gi < group.Length && !didEvent; gi++)
                {
                    EventRow ev0 = svc.GetAvailableEvent(group[gi]);
                    if (ev0 != null && svc.CompleteEvent(group[gi], ev0.Step))
                    {
                        bot.Play(RomanceService.EventNode(group[gi], ev0.Step), "event");
                        ScriptModel(svc, group[gi], ev0.Step, true);
                        bot.DrainScenes(SceneSlot.Night);
                        didEvent = true;
                    }
                }
                if (didEvent) continue;

                string who;
                {
                    List<string> pool = new List<string>();
                    if (next() < focus) { for (int gi = 0; gi < group.Length; gi++) if (svc.IsAvailable(group[gi])) pool.Add(group[gi]); }
                    if (pool.Count == 0)
                    {
                        for (int oi = 0; oi < others.Count; oi++) if (svc.IsAvailable(others[oi]) && !svc.IsFrozen(others[oi])) pool.Add(others[oi]);
                        if (pool.Count == 0) for (int gi = 0; gi < group.Length; gi++) pool.Add(group[gi]);
                    }
                    who = pool[(int)(next() * pool.Count) % pool.Count];
                    turn++;
                }
                if (svc.IsFrozen(who)) { svc.Talk(who); continue; }

                EventRow ev = svc.GetAvailableEvent(who);
                if (ev != null)
                {
                    int step = ev.Step;
                    if (svc.CompleteEvent(who, step))
                    {
                        bot.Play(RomanceService.EventNode(who, step), "event");
                        ScriptModel(svc, who, step, true);
                        bot.DrainScenes(SceneSlot.Night);
                        continue;
                    }
                }

                ActionResult res = null;
                if (svc.DateReady(who) && state.Gold >= svc.DateCost() && next() < 0.8) res = svc.Date(who, NeutralSpot(who, state.Chapter));
                else if (svc.GiftReady(who) && state.Gold >= svc.GiftCost()) res = svc.Gift(who, gifts[who][0]);
                else
                {
                    // 고른 인물에게 준비된 게 없으면 준비된 구성원에게(qa_hunt 봇과 같다)
                    for (int gi = 0; gi < group.Length && res == null; gi++)
                    {
                        string m = group[gi];
                        if (!svc.IsAvailable(m) || svc.IsFrozen(m)) continue;
                        if (svc.DateReady(m) && state.Gold >= svc.DateCost()) { res = svc.Date(m, NeutralSpot(m, state.Chapter)); who = m; }
                        else if (svc.GiftReady(m) && state.Gold >= svc.GiftCost()) { res = svc.Gift(m, gifts[m][0]); who = m; }
                    }
                }
                if (res == null || !res.Success)
                {
                    res = svc.Talk(who);
                    if (res.Success && !res.Frozen) ScriptModel(svc, who, 0, false);
                }
                bot.DrainScenes(SceneSlot.Night);
            }
            bot.DrainScenes(SceneSlot.NightEnd);
            cycle.Settle();
            cycle.NextDay();
        }

        CharState t = state.Chars[target];
        BotResult br = bot.Res;
        br.GroupEvents = new int[group.Length];
        for (int gi = 0; gi < group.Length; gi++) br.GroupEvents[gi] = state.Chars[group[gi]].EventsDone;
        br.State = state;
        br.TargetEvents = t.EventsDone;
        br.Aff = t.Aff;
        br.Trust = t.Trust;
        br.Good = t.Aff >= 85f && t.Trust >= 40f && t.EventsDone >= 5;
        if (gateOn) br.Gate = new SceneGate(_rules, new StateSceneContext(state, bot.Visited.Contains, bot.Nodes.Contains));
        return br;
    }

    private static bool CommonOnce(BotResult r)
    {
        for (int i = 0; i < ExpectedAuto.Length; i++)
        {
            int count;
            if (!r.Counts.TryGetValue(ExpectedAuto[i], out count) || count != 1) return false;
        }
        return true;
    }

    private static bool CommonInOrder(BotResult r)
    {
        List<string> seen = new List<string>();
        for (int i = 0; i < r.Log.Count; i++)
            if (Array.IndexOf(ExpectedAuto, r.Log[i].Node) >= 0) seen.Add(r.Log[i].Node);
        return string.Join(",", seen.ToArray()) == string.Join(",", ExpectedAuto);
    }

    private static bool Before(BotResult r, string a, string b)
    {
        int ia = r.IndexOf(a), ib = r.IndexOf(b);
        return ia >= 0 && ib >= 0 && ia < ib;
    }

    private static bool Reached(BotResult r, string node, int chapter, int day)
    {
        Played p = r.Find(node);
        return p != null && (p.Chapter > chapter || (p.Chapter == chapter && p.Day >= day));
    }

    private static void BotChecks()
    {
        Console.WriteLine("== G. 64-day bot autoplay ==");
        // 서로 다른 정책 3개: 하울 집중 20% / 35%(요한을 거의 안 만난다) / 100%
        double[] foci = { 0.2, 0.35, 1.0 };
        string[] targets = { "howl", "nabi", "haru" };
        bool onceAll = true, orderAll = true, neverBlocked = true, noPlayAfterEnd = true;
        for (int i = 0; i < foci.Length; i++)
        {
            BotResult r = Play(targets[i], foci[i], 777 + i, true);
            bool once = CommonOnce(r);
            bool order = CommonInOrder(r);
            Console.WriteLine("    policy target={0} focus={1}: common once={2} order={3} log={4}", targets[i], foci[i], once, order, r.Log.Count);
            if (!once) onceAll = false;
            if (!order) orderAll = false;
            if (!r.State.Finished) noPlayAfterEnd = false;
            if (r.Gate.PendingAutoScenes().Count != 0) neverBlocked = false;
        }
        Check("64-day autoplay x3 policies: every CE node (CE01..CE08, Ch5_Show, Ch5_Show_After, Ch6_LedgerTruth) played exactly once", onceAll);
        Check("... and in chronological order every time", orderAll);
        Check("... and no auto scene is left pending/blocked at the end", neverBlocked);
        Check("... and the 64 days complete (state finished)", noPlayAfterEnd);

        // 독립 시드 20개: 모든 CE 한 번 + 순서
        int fails = 0;
        for (int seed = 0; seed < 20; seed++)
        {
            BotResult r = Play(CharIds.All[seed % 6], 0.3 + 0.07 * (seed % 10), 9000 + seed, true);
            if (!CommonOnce(r) || !CommonInOrder(r)) fails++;
        }
        Check("20 more seeds / targets / focuses: common scenes exactly once, in order (0 violations)", fails == 0);

        // 챕터·일차 창
        BotResult w = Play("howl", 0.6, 31337, true);
        Check("windows: CE01 ch1 d5, CE02 ch2 d6..8, CE03 ch3 d4..6, CE04 ch4 d5..8, CE05 ch5 d3..5",
            In(w, "CE01_FirstGuest", 1, 5, 5) && In(w, "CE02_Tasting", 2, 6, 8) && In(w, "CE03_RentMeeting", 3, 4, 6) && In(w, "CE04_StaffWelcome", 4, 5, 8) && In(w, "CE05_FestivalPrep", 5, 3, 5));
        Check("windows: Ch5_Show/_After ch5 d7, CE06 ch6 d2..4, Ch6_LedgerTruth ch6 d10, CE07 ch7 d1..2, CE08 ch8 d3..5",
            In(w, "Ch5_Show", 5, 7, 7) && In(w, "Ch5_Show_After", 5, 7, 8) && In(w, "CE06_OldPhoto", 6, 2, 4) && In(w, "Ch6_LedgerTruth", 6, 10, 10) && In(w, "CE07_BlueHarborOpening", 7, 1, 2) && In(w, "CE08_TableTogether", 8, 3, 5));
        Check("chapter intros/outros still play through ChapterFlow once each (8 intros + Ch2..Ch7 outros)", ChapterNodesOnce(w));

        // 모든 루트 5이벤트 + 순서 계약
        bool all5 = true;
        string detail = string.Empty;
        BotResult[] routes = new BotResult[6];
        for (int c = 0; c < CharIds.All.Length; c++)
        {
            routes[c] = Play(CharIds.All[c], 1.0, 4242 + c, true);
            bool five = routes[c].TargetEvents == 5;
            bool stuckFree = routes[c].Gate.Check(RomanceService.EventNode(CharIds.All[c], 5)).Allowed;
            Console.WriteLine("    route {0}: events={1} aff={2:F0} trust={3:F0} good+={4}", CharIds.All[c], routes[c].TargetEvents, routes[c].Aff, routes[c].Trust, routes[c].Good);
            if (!five || !stuckFree) { all5 = false; detail += CharIds.All[c] + " "; }
        }
        Check("a focused playthrough completes all five events on every one of the six routes " + detail, all5);
        bool allGood = true;
        for (int c = 0; c < 6; c++) if (!routes[c].Good) allGood = false;
        Check("... and reaches the good-ending thresholds (aff 85 / trust 40 / 5 events) on every route", allGood);

        BotResult howl = routes[0], johan = routes[1], beomjun = routes[2], haru = routes[3], gomseok = routes[4], nabi = routes[5];
        Check("order howl: Ch5_Show < Ch5_Show_After < Howl_E3 and Howl_E3 on ch5 day>=8", Before(howl, "Ch5_Show", "Ch5_Show_After") && Before(howl, "Ch5_Show_After", "Howl_E3") && Reached(howl, "Howl_E3", 5, 8));
        Check("order howl: CE06 < Howl_E4 (after CE07, day>=3) < CE08 < Howl_E5", Before(howl, "CE06_OldPhoto", "Howl_E4") && Before(howl, "CE07_BlueHarborOpening", "Howl_E4") && Reached(howl, "Howl_E4", 7, 3) && Before(howl, "CE08_TableTogether", "Howl_E5"));
        Check("order johan: Johan_E1 (ch2 day>=5) < CE02_Tasting; Ch5_Show < Johan_E3 (day>=7)", Reached(johan, "Johan_E1", 2, 5) && Before(johan, "Johan_E1", "CE02_Tasting") && Before(johan, "Ch5_Show", "Johan_E3") && Reached(johan, "Johan_E3", 5, 7));
        Check("order beomjun: CE04 < Beomjun_E2; Beomjun_E1 (ch3 day>=3) < CE03", Before(beomjun, "CE04_StaffWelcome", "Beomjun_E2") && Reached(beomjun, "Beomjun_E1", 3, 3) && Before(beomjun, "Beomjun_E1", "CE03_RentMeeting"));
        Check("order haru: Haru_E1 (ch4 day>=4) < CE04; Haru_E2 ch5 day>=6; Ch6_LedgerTruth < Haru_E3 < Haru_E4", Reached(haru, "Haru_E1", 4, 4) && Before(haru, "Haru_E1", "CE04_StaffWelcome") && Reached(haru, "Haru_E2", 5, 6) && Before(haru, "Ch6_LedgerTruth", "Haru_E3") && Before(haru, "Haru_E3", "Haru_E4"));
        Check("order gomseok: CE06_OldPhoto < Gomseok_E4; CE08 < Gomseok_E5", Before(gomseok, "CE06_OldPhoto", "Gomseok_E4") && Before(gomseok, "CE08_TableTogether", "Gomseok_E5"));
        Check("order nabi: Nabi_E2 in ch4+, Ch5_Show < Nabi_E3, CE07 < Nabi_E4, Ch8_Intro < CE08 < Nabi_E5",
            Reached(nabi, "Nabi_E2", 4, 1) && Before(nabi, "Ch5_Show", "Nabi_E3") && Before(nabi, "CE07_BlueHarborOpening", "Nabi_E4") && Before(nabi, "Ch8_Intro", "Nabi_E5") && Before(nabi, "CE08_TableTogether", "Nabi_E5"));

        bool eventOrder = true;
        for (int c = 0; c < 6; c++)
            for (int st = 2; st <= 5; st++)
                if (!Before(routes[c], RomanceService.EventNode(CharIds.All[c], st - 1), RomanceService.EventNode(CharIds.All[c], st))) eventOrder = false;
        Check("each route's events play in step order E1..E5", eventOrder);

        // 한 번도 안 만나는 정책에서도 막히지 않음: 대상 이외 인물과 0% 접촉(focus 1.0)에서 모든 E-노드 게이트가 마지막에 열려 있다
        bool endOpen = true;
        for (int c = 0; c < 6; c++)
            for (int st = 1; st <= 5; st++)
            {
                SceneCheck chk = routes[c].Gate.Check(RomanceService.EventNode(CharIds.All[c], st));
                if (!chk.Allowed) endOpen = false;
            }
        Check("end of game: no personal event gate (6 x 5) is closed under any focused playthrough (nothing blocked forever)", endOpen);

        // 공동 루트(2인/3인): 구성원 전원 5이벤트
        string[] trio = { "howl", "nabi", "gomseok" };
        string[] duo = { "johan", "haru" };
        BotResult rt = Play(trio, 1.0, 515, true);
        BotResult rd = Play(duo, 1.0, 616, true);
        Console.WriteLine("    trio events: {0}  duo events: {1}", string.Join("/", Array.ConvertAll(rt.GroupEvents, delegate (int v) { return v.ToString(); })), string.Join("/", Array.ConvertAll(rd.GroupEvents, delegate (int v) { return v.ToString(); })));
        bool groupFive = true;
        for (int i = 0; i < rd.GroupEvents.Length; i++) if (rd.GroupEvents[i] < 4) groupFive = false; // harem_duo needs 4 events; the 5th depends on the late-joining member's action budget (balance, not gates)
        // trio: each member only gets a third of the actions, so completing all five events is a balance question (docs/10 section 2), not a gate question:
        // here we only require that no member's event gate is still closed at the end and that every member did progress
        bool trioOpen = true;
        for (int i = 0; i < trio.Length; i++)
            for (int st = 1; st <= 5; st++)
                if (!rt.Gate.Check(RomanceService.EventNode(trio[i], st)).Allowed) trioOpen = false;
        for (int i = 0; i < rt.GroupEvents.Length; i++) if (rt.GroupEvents[i] < 1) trioOpen = false;
        Check("joint routes (duo johan+haru): every member completes >= 4 events (harem_duo requirement) through the gates; trio howl+nabi+gomseok: no gate left closed, every member progressed", groupFive && trioOpen);
        Check("joint routes: common scenes still exactly once and in order", CommonOnce(rt) && CommonInOrder(rt) && CommonOnce(rd) && CommonInOrder(rd));

        // 게이트 없는 봇과 비교(게이트가 이벤트 가능성을 해치지 않음)
        BotResult off = Play("howl", 1.0, 4242, false);
        Check("same seed without gate/scenes also completes 5 events (gate costs no events for a focused player)", off.TargetEvents == 5 && howl.TargetEvents == 5);
    }

    private static bool In(BotResult r, string node, int chapter, int dayMin, int dayMax)
    {
        Played p = r.Find(node);
        return p != null && p.Chapter == chapter && p.Day >= dayMin && p.Day <= dayMax;
    }

    private static bool ChapterNodesOnce(BotResult r)
    {
        for (int c = 1; c <= 8; c++)
        {
            int n;
            if (!r.Counts.TryGetValue("Ch" + c + "_Intro", out n) || n != 1) return false;
            if (c >= 2 && c <= 7 && (!r.Counts.TryGetValue("Ch" + c + "_Outro", out n) || n != 1)) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- H. 게이트를 켠 패리티

    private static readonly double[] Foci = { 0.2, 0.35, 0.5, 0.6, 0.8 };
    private static readonly double[] DocGoodPct = { 6, 52, 88, 95, 99 };

    private static void ParityChecks(int runs)
    {
        Console.WriteLine("== H. parity with gates on (target=howl, {0} runs/focus) ==", runs);
        Console.WriteLine("focus | good+ (gates on) | good+ (gates off) | doc | events on/off");
        for (int i = 0; i < Foci.Length; i++)
        {
            int goodOn = 0, goodOff = 0;
            double evOn = 0, evOff = 0;
            for (int r = 0; r < runs; r++)
            {
                BotResult on = Play("howl", Foci[i], 20261003 + i * 100000 + r, true);
                BotResult off = Play("howl", Foci[i], 20261003 + i * 100000 + r, false);
                if (on.Good) goodOn++;
                if (off.Good) goodOff++;
                evOn += on.TargetEvents; evOff += off.TargetEvents;
            }
            double pOn = 100.0 * goodOn / runs, pOff = 100.0 * goodOff / runs;
            Console.WriteLine("{0,4:F0}% | {1,6:F1}% | {2,6:F1}% | {3,3:F0}% | {4:F2}/{5:F2}", Foci[i] * 100, pOn, pOff, DocGoodPct[i], evOn / runs, evOff / runs);
            Check("parity with gates on, focus " + (int)(Foci[i] * 100) + "%: within 10pp of the doc (" + pOn.ToString("F1") + " vs " + DocGoodPct[i] + ")", Math.Abs(pOn - DocGoodPct[i]) <= 10.0);
        }
    }
}
