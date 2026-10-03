using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using MorpheHouse.Save;

// 3단계 엔딩·공동 루트 검증 하네스(Unity 불필요).
// 빌드/실행: tools/csharp_check/ending_check/run.sh
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

    private static BalanceTable _balance;
    private static RomanceCatalog _catalog;
    private static EndingTable _table;

    private sealed class XmlSer : ISerializer
    {
        private readonly XmlSerializer _x = new XmlSerializer(typeof(SaveData));
        public string ToJson(SaveData d) { using (StringWriter w = new StringWriter()) { _x.Serialize(w, d); return w.ToString(); } }
        public SaveData FromJson(string s) { using (StringReader r = new StringReader(s)) { return (SaveData)_x.Deserialize(r); } }
    }

    private static GameState Make(int chapter)
    {
        GameState s = GameState.NewGame(_balance, Difficulty.Normal, "t");
        s.Chapter = chapter;
        s.Day = 10;
        s.Phase = DayPhase.Night;
        s.ActionsLeft = 2;
        s.Fatigue = 0f;
        s.Gold = 5000f;
        return s;
    }

    private static RomanceService Svc(GameState s)
    {
        return new RomanceService(s, _balance, _catalog, new Random(1).NextDouble);
    }

    private static void Set(GameState s, string id, float aff, float trust, int events)
    {
        CharState c = s.Chars[id];
        c.Aff = aff; c.Trust = trust; c.EventsDone = events;
    }

    private static void Flags(GameState s, params string[] names)
    {
        for (int i = 0; i < names.Length; i++) s.Flags[names[i]] = 1;
    }

    /// <summary>단독 확정 상태(레거시 방식: LockedRoute 만 설정)로 평가.</summary>
    private static EndingResult Single(string id, float aff, float trust, int events, params string[] flags)
    {
        GameState s = Make(8);
        Set(s, id, aff, trust, events);
        s.LockedRoute = id;
        Flags(s, flags);
        return EndingEvaluator.Evaluate(s, _table);
    }

    private static EndingResult Group(float aff, float trust, int events, string[] ids, params string[] flags)
    {
        GameState s = Make(8);
        for (int i = 0; i < ids.Length; i++) Set(s, ids[i], aff, trust, events);
        s.SetLock(ids);
        Flags(s, flags);
        return EndingEvaluator.Evaluate(s, _table);
    }

    public static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "balance";
        FileTextSource src = new FileTextSource(dir);
        _balance = BalanceTable.Load(src, Difficulty.Normal);
        _catalog = RomanceCatalog.Load(src);
        _table = EndingTable.Load(src);

        TableChecks();
        GameStateChecks();
        LockGroupChecks();
        DecayChecks();
        SingleEndingChecks();
        GroupEndingChecks();
        SaveChecks();
        NightActionChecks();
        PlanChecks();

        Console.WriteLine();
        Console.WriteLine("checks={0} failures={1}", _checks, _failures);
        Console.WriteLine(_failures == 0 ? "ALL PASSED" : "FAILURES");
        return _failures == 0 ? 0 : 1;
    }

    // ---------- endings.csv ----------

    private static void TableChecks()
    {
        Console.WriteLine("== endings.csv ==");
        Check("table has 6 rows", _table.Rows.Count == 6);
        Check("normal 60/20/3", _table.NormalRow.AffMin == 60f && _table.NormalRow.TrustMin == 20f && _table.NormalRow.RequiredEvents == 3);
        Check("good 85/40/5", _table.GoodRow.AffMin == 85f && _table.GoodRow.TrustMin == 40f && _table.GoodRow.RequiredEvents == 5);
        Check("true 95/46/5 + flags", _table.TrueRow.AffMin == 95f && _table.TrueRow.TrustMin == 46f && _table.TrueRow.RequiredEvents == 5
            && _table.TrueRow.HasFlagToken("ledger_truth") && _table.TrueRow.HasFlagToken("key_flag"));
        Check("duo 72/24/3 (group retune, QA-04 follow-up)", _table.HaremDuoRow.AffMin == 72f && _table.HaremDuoRow.TrustMin == 24f && _table.HaremDuoRow.RequiredEvents == 3);
        Check("trio 65/14/2 (group retune)", _table.HaremTrioRow.AffMin == 65f && _table.HaremTrioRow.TrustMin == 14f && _table.HaremTrioRow.RequiredEvents == 2);
        Check("harem rows need harem_consent", _table.HaremDuoRow.HasFlagToken("harem_consent") && _table.HaremTrioRow.HasFlagToken("harem_consent"));
        Check("Find unknown -> null", _table.Find("nope") == null && _table.Find(null) == null && _table.Find("good") == _table.GoodRow);

        bool threw = false;
        try { EndingTable.Parse("ending,route,aff_min,trust_min,required_events,required_flags,note\nnormal,locked,60,20,3,route_locked,x\n"); }
        catch (InvalidOperationException) { threw = true; }
        Check("missing rows -> InvalidOperationException", threw);
        threw = false;
        try { EndingTable.Load(new FileTextSource("no_such_dir")); } catch (InvalidOperationException) { threw = true; }
        Check("missing file -> InvalidOperationException", threw);

        // CSV 수치를 바꾸면 판정 기준도 따라 바뀐다(하드코딩 없음).
        string custom = "ending,route,aff_min,trust_min,required_events,required_flags,note\n"
            + "normal,locked,10,1,1,route_locked,\ngood,locked,20,2,2,route_locked,\ntrue,locked,30,3,3,route_locked;ledger_truth;key_flag,\n"
            + "community,none,0,0,0,route_unlocked,\nharem_duo,group2,15,5,1,group_locked;harem_consent,\nharem_trio,group3,12,4,1,group_locked;harem_consent,\n";
        EndingTable t2 = EndingTable.Parse(custom);
        GameState s = Make(8);
        Set(s, "howl", 20f, 2f, 2);
        s.SetLock(new string[] { "howl" });
        Check("thresholds come from CSV (custom good 20/2/2)", EndingEvaluator.Evaluate(s, t2).Kind == EndingKind.Good);
    }

    // ---------- GameState 헬퍼 ----------

    private static void GameStateChecks()
    {
        Console.WriteLine("== GameState lock helpers ==");
        GameState s = Make(7);
        Check("fresh: no lock", !s.HasRouteLock && s.LockedCount == 0 && s.LockedIds().Count == 0 && !s.IsLockedOut("howl"));
        s.LockedRoute = "howl"; // 구형 상태
        Check("legacy LockedRoute only -> group of 1", s.HasRouteLock && s.LockedCount == 1 && s.IsRouteMember("howl") && s.IsLockedOut("johan"));
        s.SetLock(new string[] { "johan", "haru" });
        Check("SetLock group: primary = first, members", s.LockedRoute == "johan" && s.LockedCount == 2 && s.IsRouteMember("haru") && !s.IsRouteMember("howl"));
        Check("IsLockedOut for non-member only", s.IsLockedOut("howl") && !s.IsLockedOut("haru"));
        s.SetLock(null);
        Check("SetLock(null) clears", !s.HasRouteLock && s.LockedRoute.Length == 0 && s.LockedGroup.Count == 0);
        Check("IsRouteMember(null) false", !s.IsRouteMember(null));
    }

    // ---------- TryLockGroup ----------

    private static GameState Ready(int chapter, params string[] ids)
    {
        GameState s = Make(chapter);
        for (int i = 0; i < ids.Length; i++) Set(s, ids[i], 60f, 20f, 0);
        return s;
    }

    private static void LockGroupChecks()
    {
        Console.WriteLine("== TryLockGroup ==");
        string reason;

        GameState s = Ready(7, "howl", "johan", "haru");
        RomanceService svc = Svc(s);
        int events = 0;
        string[] seen = null;
        EventBus.Subscribe<RouteLocked>(e => { events++; seen = e.CharIds; });
        bool ok = svc.TryLockGroup(new string[] { "howl", "johan", "haru" }, out reason);
        Check("trio lock at exact limits (ch7, 60/20)", ok && reason.Length == 0 && s.LockedCount == 3 && s.LockedRoute == "howl");
        Check("RouteLocked event carries all members", events == 1 && seen != null && seen.Length == 3 && seen[2] == "haru");
        Check("members not frozen / others frozen", !svc.IsFrozen("howl") && !svc.IsFrozen("haru") && svc.IsFrozen("nabi"));
        Check("second lock refused (already_locked)", !svc.TryLockGroup(new string[] { "howl" }, out reason) && reason == "already_locked");
        Check("single TryLockRoute also refused after group", !svc.TryLockRoute("johan", out reason) && reason == "already_locked");

        s = Ready(7, "howl", "johan");
        svc = Svc(s);
        Check("duo lock ok", svc.TryLockGroup(new string[] { "johan", "howl" }) && s.LockedRoute == "johan" && s.LockedGroup[1] == "howl");

        s = Ready(7, "howl");
        svc = Svc(s);
        Check("group of 1 == single lock", svc.TryLockGroup(new string[] { "howl" }) && s.LockedRoute == "howl" && s.LockedCount == 1);

        s = Ready(7, "howl", "johan", "haru", "nabi");
        svc = Svc(s);
        Check("size 4 refused", !svc.TryLockGroup(new string[] { "howl", "johan", "haru", "nabi" }, out reason) && reason == "group_too_large" && !s.HasRouteLock);
        Check("empty refused", !svc.TryLockGroup(new string[0], out reason) && reason == "group_empty");
        Check("null refused", !svc.TryLockGroup(null, out reason) && reason == "group_empty");
        Check("duplicate refused", !svc.TryLockGroup(new string[] { "howl", "howl" }, out reason) && reason == "duplicate_member");
        Check("unknown char refused", !svc.TryLockGroup(new string[] { "howl", "ghost" }, out reason) && reason == "unknown_char");

        s = Ready(6, "howl", "johan");
        svc = Svc(s);
        Check("chapter 6 refused", !svc.TryLockGroup(new string[] { "howl", "johan" }, out reason) && reason == "chapter_too_early" && !s.HasRouteLock);

        s = Ready(7, "howl", "johan");
        Set(s, "johan", 49.9f, 7f, 0);
        svc = Svc(s);
        Check("one member aff 49.9 refused (group min 50)", !svc.TryLockGroup(new string[] { "howl", "johan" }, out reason) && reason == "affection_too_low" && !s.HasRouteLock);
        Set(s, "johan", 50f, 6.9f, 0);
        Check("one member trust 6.9 refused (group min 7)", !svc.TryLockGroup(new string[] { "howl", "johan" }, out reason) && reason == "trust_too_low");
        Check("CheckGroupLock has no side effects", svc.CheckGroupLock(new string[] { "howl", "johan" }) == "trust_too_low" && !s.HasRouteLock);
        Set(s, "johan", 50f, 7f, 0);
        Check("CheckGroupLock null when ok", svc.CheckGroupLock(new string[] { "howl", "johan" }) == null);

        // 확정 후: 구성원은 계속 성장, 비구성원은 동결
        s = Ready(7, "howl", "johan");
        s.Phase = DayPhase.Night; s.ActionsLeft = 3;
        svc = Svc(s);
        svc.TryLockGroup(new string[] { "howl", "johan" });
        float before = s.Chars["johan"].Aff;
        ActionResult r = svc.AdjustAffection("johan", 3f, 0f, "t");
        Check("member keeps gaining after group lock", r.AffDelta > 0f && s.Chars["johan"].Aff > before && !r.Frozen);
        ActionResult rf = svc.AdjustAffection("nabi", 3f, 1f, "t");
        Check("non-member frozen after group lock", rf.Frozen && s.Chars["nabi"].Aff == 0f);
        Check("non-member has no event available", svc.GetAvailableEvent("nabi") == null);

        // 단독 TryLockRoute 는 그대로
        s = Ready(7, "howl");
        svc = Svc(s);
        Check("TryLockRoute sets group of 1", svc.TryLockRoute("howl") && s.LockedGroup.Count == 1 && s.LockedGroup[0] == "howl" && s.LockedRoute == "howl");
    }

    // ---------- DayCycle 감쇠 ----------

    private static void DecayChecks()
    {
        Console.WriteLine("== neglect decay ==");
        GameState s = Make(8);
        s.Day = 30; // 모두 접촉 없이 오래됨
        for (int i = 0; i < CharIds.All.Length; i++) Set(s, CharIds.All[i], 70f, 25f, 0);
        s.SetLock(new string[] { "howl", "johan" });
        MorpheHouse.Tycoon.DayCycle cycle = new MorpheHouse.Tycoon.DayCycle(s, _balance, new MorpheHouse.Tycoon.SystemRandomAdapter(1));
        cycle.Begin();
        float decay = _balance.P("decay_per_day");
        Check("non-members exempt from decay (group)", s.Chars["haru"].Aff == 70f && s.Chars["nabi"].Aff == 70f && s.Chars["beomjun"].Aff == 70f);
        Check("members still decay (existing semantics)", Math.Abs(s.Chars["howl"].Aff - (70f - decay)) < 0.001f && Math.Abs(s.Chars["johan"].Aff - (70f - decay)) < 0.001f);

        GameState legacy = Make(8);
        legacy.Day = 30;
        for (int i = 0; i < CharIds.All.Length; i++) Set(legacy, CharIds.All[i], 70f, 25f, 0);
        legacy.LockedRoute = "howl"; // 구형 저장 상태
        new MorpheHouse.Tycoon.DayCycle(legacy, _balance, new MorpheHouse.Tycoon.SystemRandomAdapter(1)).Begin();
        Check("legacy single lock: others exempt, locked decays", legacy.Chars["johan"].Aff == 70f && legacy.Chars["howl"].Aff < 70f);

        GameState none = Make(8);
        none.Day = 30;
        for (int i = 0; i < CharIds.All.Length; i++) Set(none, CharIds.All[i], 70f, 25f, 0);
        new MorpheHouse.Tycoon.DayCycle(none, _balance, new MorpheHouse.Tycoon.SystemRandomAdapter(1)).Begin();
        Check("no lock: everyone decays", none.Chars["haru"].Aff < 70f && none.Chars["howl"].Aff < 70f);
    }

    // ---------- 단독 엔딩 ----------

    private static void SingleEndingChecks()
    {
        Console.WriteLine("== single route endings ==");
        EndingResult r = Single("howl", 95f, 46f, 5, "ledger_truth", "howl_key");
        Check("true at exact limits", r.Kind == EndingKind.True && r.Id == "true" && r.NodeName == "Howl_End_True" && r.Reason == "ok");
        Check("true CG HOWL_E6", r.CgIds.Count == 1 && r.CgIds[0] == "HOWL_E6" && r.PrimaryChar == "howl" && r.Members.Count == 1);
        Check("true needs ledger_truth (else good)", Single("howl", 100f, 50f, 5, "howl_key").Kind == EndingKind.Good);
        Check("true needs <char>_key (else good)", Single("howl", 100f, 50f, 5, "ledger_truth").Kind == EndingKind.Good);
        Check("other char's key does not count", Single("howl", 100f, 50f, 5, "ledger_truth", "johan_key").Kind == EndingKind.Good);
        Check("true aff 94.9 -> good", Single("howl", 94.9f, 46f, 5, "ledger_truth", "howl_key").Kind == EndingKind.Good);
        Check("true trust 45.9 -> good", Single("howl", 95f, 45.9f, 5, "ledger_truth", "howl_key").Kind == EndingKind.Good);
        Check("true events 4 -> normal/good fails events", Single("howl", 95f, 46f, 4, "ledger_truth", "howl_key").Kind == EndingKind.Normal);
        Check("flag value 0 counts as missing", Single("howl", 100f, 50f, 5, "ledger_truth").Kind == EndingKind.Good && SingleFlagZero());

        r = Single("johan", 85f, 40f, 5);
        Check("good at exact limits", r.Kind == EndingKind.Good && r.NodeName == "Johan_End_Good" && r.CgIds.Count == 0);
        Check("good aff 84.9 -> normal", Single("johan", 84.9f, 40f, 5).Kind == EndingKind.Normal);
        Check("good trust 39.9 -> normal", Single("johan", 85f, 39.9f, 5).Kind == EndingKind.Normal);
        Check("good events 4 -> normal", Single("johan", 85f, 40f, 4).Kind == EndingKind.Normal);

        r = Single("nabi", 60f, 20f, 3);
        Check("normal at exact limits", r.Kind == EndingKind.Normal && r.NodeName == "Nabi_End_Normal" && r.Id == "normal");
        Check("normal aff 59.9 -> community", Single("nabi", 59.9f, 20f, 3).Kind == EndingKind.Community);
        Check("normal trust 19.9 -> community", Single("nabi", 60f, 19.9f, 3).Kind == EndingKind.Community);
        r = Single("nabi", 60f, 20f, 2);
        Check("normal events 2 -> community (below_normal)", r.Kind == EndingKind.Community && r.Reason == "below_normal" && r.NodeName == "Ending_Community");
        Check("shortfalls list events", r.Shortfalls.Contains("nabi:events") && r.Shortfalls.Count == 1);
        Check("beomjun/haru/gomseok node names", Single("beomjun", 60f, 20f, 3).NodeName == "Beomjun_End_Normal"
            && Single("haru", 85f, 40f, 5).NodeName == "Haru_End_Good" && Single("gomseok", 95f, 46f, 5, "ledger_truth", "gomseok_key").NodeName == "Gomseok_End_True");
        Check("harem flags irrelevant for single", Single("howl", 85f, 40f, 5, "harem_ok_howl", "harem_ok_johan").Kind == EndingKind.Good);

        // 미확정
        GameState s = Make(8);
        for (int i = 0; i < CharIds.All.Length; i++) Set(s, CharIds.All[i], 100f, 50f, 5);
        Flags(s, "ledger_truth", "howl_key");
        r = EndingEvaluator.Evaluate(s, _table);
        Check("no lock -> community even with perfect stats", r.Kind == EndingKind.Community && r.Reason == "no_lock" && r.NodeName == "Ending_Community" && r.Members.Count == 0 && r.PrimaryChar.Length == 0);
        Check("community has no CG", r.CgIds.Count == 0 && r.IsCommunity && !r.IsHarem);

        // 결정성 / 상태 불변
        EndingResult a = Single("howl", 90f, 42f, 5);
        EndingResult b = Single("howl", 90f, 42f, 5);
        Check("deterministic", a.Kind == b.Kind && a.NodeName == b.NodeName);
        GameState before = Make(8);
        Set(before, "howl", 90f, 42f, 5); before.LockedRoute = "howl";
        EndingEvaluator.Evaluate(before, _table);
        Check("evaluate does not mutate state", before.Chars["howl"].Aff == 90f && before.LockedGroup.Count == 0 && before.Flags.Count == 0);

        bool threw = false;
        try { EndingEvaluator.Evaluate(null, _table); } catch (ArgumentNullException) { threw = true; }
        try { EndingEvaluator.Evaluate(before, null); } catch (ArgumentNullException) { threw = threw && true; }
        Check("null args throw", threw);
    }

    private static bool SingleFlagZero()
    {
        GameState s = Make(8);
        Set(s, "howl", 100f, 50f, 5);
        s.LockedRoute = "howl";
        s.Flags["ledger_truth"] = 1;
        s.Flags["howl_key"] = 0;
        return EndingEvaluator.Evaluate(s, _table).Kind == EndingKind.Good;
    }

    // ---------- 다자 엔딩 ----------

    private static void GroupEndingChecks()
    {
        Console.WriteLine("== group (harem) endings ==");
        string[] duo = { "howl", "johan" };
        string[] trio = { "howl", "johan", "haru" };

        EndingResult r = Group(72f, 24f, 3, duo, "harem_ok_howl", "harem_ok_johan");
        Check("duo harem at exact limits", r.Kind == EndingKind.HaremDuo && r.Id == "harem_duo" && r.NodeName == "Ending_Harem2" && r.Reason == "ok");
        Check("duo CG + members", r.CgIds.Count == 1 && r.CgIds[0] == "HAREM_E1" && r.Members.Count == 2 && r.PrimaryChar == "howl" && r.IsHarem);

        r = Group(65f, 14f, 2, trio, "harem_ok_howl", "harem_ok_johan", "harem_ok_haru");
        Check("trio harem at exact limits", r.Kind == EndingKind.HaremTrio && r.Id == "harem_trio" && r.NodeName == "Ending_Harem3" && r.Members.Count == 3);

        r = Group(100f, 50f, 5, trio, "harem_ok_howl", "harem_ok_johan", "harem_ok_haru", "ledger_truth", "howl_key");
        Check("trio perfect stats still harem3 (no personal true)", r.Kind == EndingKind.HaremTrio && r.CgIds.Contains("HAREM_E1") && !r.CgIds.Contains("HOWL_E6"));

        // 경계: 한 단계 아래
        Check("duo aff 71.9 -> community", Group(71.9f, 24f, 3, duo, "harem_ok_howl", "harem_ok_johan").Kind == EndingKind.Community);
        Check("duo trust 23.9 -> community", Group(72f, 23.9f, 3, duo, "harem_ok_howl", "harem_ok_johan").Kind == EndingKind.Community);
        Check("duo events 2 -> community", Group(72f, 24f, 2, duo, "harem_ok_howl", "harem_ok_johan").Kind == EndingKind.Community);
        Check("trio aff 64.9 -> community", Group(64.9f, 14f, 2, trio, "harem_ok_howl", "harem_ok_johan", "harem_ok_haru").Kind == EndingKind.Community);
        Check("trio trust 13.9 -> community", Group(65f, 13.9f, 2, trio, "harem_ok_howl", "harem_ok_johan", "harem_ok_haru").Kind == EndingKind.Community);
        Check("trio events 1 -> community", Group(65f, 14f, 1, trio, "harem_ok_howl", "harem_ok_johan", "harem_ok_haru").Kind == EndingKind.Community);

        // 3인에게 2인 기준을 적용하면 안 된다 / 2인에게 3인 기준은 느슨하다
        Check("trio uses trio thresholds, not duo (68/20/3 passes trio)", Group(68f, 20f, 3, trio, "harem_ok_howl", "harem_ok_johan", "harem_ok_haru").Kind == EndingKind.HaremTrio);
        Check("duo uses duo thresholds, not trio (68/20/3 fails duo)", Group(68f, 20f, 3, duo, "harem_ok_howl", "harem_ok_johan").Kind == EndingKind.Community);

        // 동의 플래그
        r = Group(90f, 45f, 5, duo, "harem_ok_howl");
        Check("one member missing consent -> community", r.Kind == EndingKind.Community && r.Reason == "group_unmet" && r.Shortfalls.Contains("johan:flag harem_ok_johan"));
        Check("community fallback names Ending_Community, never normal", r.NodeName == "Ending_Community" && r.Id == "community");
        Check("no consent at all -> community", Group(90f, 45f, 5, duo).Kind == EndingKind.Community);
        Check("trio, third lacks consent -> community", Group(90f, 45f, 5, trio, "harem_ok_howl", "harem_ok_johan").Kind == EndingKind.Community);
        Check("consent flag of a non-member does not help", Group(90f, 45f, 5, duo, "harem_ok_howl", "harem_ok_haru").Kind == EndingKind.Community);

        // 한 명만 미달 → 부분 엔딩 없이 커뮤니티
        GameState s = Make(8);
        Set(s, "howl", 100f, 50f, 5);
        Set(s, "johan", 71f, 24f, 3);
        s.SetLock(duo);
        Flags(s, "harem_ok_howl", "harem_ok_johan", "ledger_truth", "howl_key");
        r = EndingEvaluator.Evaluate(s, _table);
        Check("strong + weak member -> community (not howl normal/good/true)", r.Kind == EndingKind.Community && r.Shortfalls.Count == 1 && r.Shortfalls[0] == "johan:aff");

        s = Make(8);
        Set(s, "howl", 70f, 30f, 4); Set(s, "johan", 70f, 30f, 4); Set(s, "haru", 70f, 13f, 4);
        s.SetLock(trio);
        Flags(s, "harem_ok_howl", "harem_ok_johan", "harem_ok_haru");
        r = EndingEvaluator.Evaluate(s, _table);
        Check("trio one member trust short -> community, reported", r.Kind == EndingKind.Community && r.Shortfalls.Count == 1 && r.Shortfalls[0] == "haru:trust");

        // 여러 미달이 모두 보고된다
        s = Make(8);
        Set(s, "howl", 10f, 5f, 0); Set(s, "johan", 10f, 5f, 0);
        s.SetLock(duo);
        r = EndingEvaluator.Evaluate(s, _table);
        Check("all shortfalls collected (2 members x (aff,trust,events,flag))", r.Shortfalls.Count == 8);

        // 구성원 순서가 달라도 같은 결과, 대표 인물만 바뀐다
        EndingResult o1 = Group(80f, 36f, 4, new string[] { "howl", "nabi" }, "harem_ok_howl", "harem_ok_nabi");
        EndingResult o2 = Group(80f, 36f, 4, new string[] { "nabi", "howl" }, "harem_ok_howl", "harem_ok_nabi");
        Check("member order: same ending, primary differs", o1.Kind == o2.Kind && o1.PrimaryChar == "howl" && o2.PrimaryChar == "nabi");

        // 구성원이 Chars 에 없는 비정상 상태
        s = Make(8);
        s.Chars.Remove("haru");
        Set(s, "howl", 90f, 45f, 5); Set(s, "johan", 90f, 45f, 5);
        s.SetLock(trio);
        Flags(s, "harem_ok_howl", "harem_ok_johan", "harem_ok_haru");
        Check("member missing from state -> community, no crash", EndingEvaluator.Evaluate(s, _table).Kind == EndingKind.Community);

        // 실제 RomanceService 로 확정한 상태에서 판정(통합)
        s = Make(7);
        for (int i = 0; i < trio.Length; i++) Set(s, trio[i], 60f, 20f, 0);
        RomanceService svc = Svc(s);
        svc.TryLockGroup(trio);
        for (int i = 0; i < trio.Length; i++) { Set(s, trio[i], 72f, 31f, 4); svc.SetFlag("harem_ok_" + trio[i]); }
        Check("integration: lock via service then evaluate -> harem3", EndingEvaluator.Evaluate(s, _table).Kind == EndingKind.HaremTrio);
    }

    // ---------- 저장 ----------

    private static void SaveChecks()
    {
        Console.WriteLine("== save compatibility ==");
        GameState s = Make(7);
        s.Phase = DayPhase.Morning;
        Set(s, "howl", 70f, 25f, 3); Set(s, "johan", 70f, 25f, 3); Set(s, "haru", 70f, 25f, 3);
        Svc(s).TryLockGroup(new string[] { "johan", "howl", "haru" });
        SaveData d = SaveMapper.ToSave(s, 1, "t");
        Check("ToSave copies group in order", d.LockedGroup.Count == 3 && d.LockedGroup[0] == "johan" && d.LockedGroup[2] == "haru" && d.LockedRoute == "johan");
        Check("validator accepts group save", SaveValidator.Validate(d) == null);
        GameState back = SaveMapper.FromSave(d);
        Check("FromSave restores group + primary", back.LockedCount == 3 && back.LockedRoute == "johan" && back.LockedGroup[1] == "howl" && back.IsRouteMember("haru"));
        Check("roundtrip evaluates identically", EndingEvaluator.Evaluate(back, _table).Kind == EndingEvaluator.Evaluate(s, _table).Kind);

        // 구형 저장: LockedGroup 없음
        SaveData old = new SaveData();
        old.PlayerName = "x"; old.Reputation = 20; old.Fatigue = 10;
        old.LockedRoute = "howl";
        old.LockedGroup = null;
        Check("old save (null group) validates", SaveValidator.Validate(old) == null);
        GameState oldState = SaveMapper.FromSave(old);
        Check("old save loads as single lock", oldState.LockedRoute == "howl" && oldState.LockedCount == 1 && oldState.IsRouteMember("howl") && oldState.IsLockedOut("johan"));
        old.LockedGroup = new List<string>();
        Check("old save (empty group) loads as single lock", SaveMapper.FromSave(old).IsRouteMember("howl") && SaveValidator.Validate(old) == null);
        SaveData unlocked = new SaveData();
        unlocked.PlayerName = "x"; unlocked.Reputation = 20; unlocked.Fatigue = 10;
        Check("no lock save stays unlocked", !SaveMapper.FromSave(unlocked).HasRouteLock && SaveValidator.Validate(unlocked) == null);
        Check("version is current (v2 = canonical signed payload; LockedGroup is part of it)", d.Version == SaveData.CurrentVersion && SaveData.CurrentVersion == 2);

        // 검증기
        SaveData bad = SaveMapper.ToSave(s, 1, "t");
        bad.LockedGroup.Add("nabi");
        Check("validator rejects group size 4", SaveValidator.Validate(bad) == "lockedGroup size");
        bad = SaveMapper.ToSave(s, 1, "t");
        bad.LockedGroup[1] = "johan";
        Check("validator rejects duplicate member", SaveValidator.Validate(bad) == "lockedGroup duplicate");
        bad = SaveMapper.ToSave(s, 1, "t");
        bad.LockedRoute = "howl";
        Check("validator rejects primary mismatch", SaveValidator.Validate(bad) == "lockedGroup primary");
        bad = SaveMapper.ToSave(s, 1, "t");
        bad.LockedGroup[2] = new string('x', 40);
        Check("validator rejects over-long member id", SaveValidator.Validate(bad) == "lockedGroup member");
        bad = SaveMapper.ToSave(s, 1, "t");
        bad.LockedGroup[2] = string.Empty;
        Check("validator rejects empty member id", SaveValidator.Validate(bad) == "lockedGroup member");

        // SaveService: 신형 라운드트립 + 구형 해시(필드 없이 서명된 파일) 로드
        string dir = Path.Combine(Path.GetTempPath(), "morphe_ending_test_" + Guid.NewGuid().ToString("N"));
        XmlSer ser = new XmlSer();
        SaveService svc = new SaveService(ser, new FileSaveStorage(dir));
        svc.Save(1, SaveMapper.ToSave(s, 1, "t"));
        LoadResult ok = svc.Load(1);
        Check("SaveService roundtrip with group", ok.Status == LoadStatus.Ok && ok.Data.LockedGroup.Count == 3);

        GameState single = Make(7);
        single.Phase = DayPhase.Morning;
        single.LockedRoute = "howl";
        SaveData sd = SaveMapper.ToSave(single, 2, "t");
        sd.Version = 1; // v1 = 구형 텍스트 해시 서명(정규 페이로드는 v2 부터)
        sd.Hash = string.Empty;
        string stripped = SaveService.StripEmptyLockedGroup(ser.ToJson(sd));
        Check("strip removes empty group element", stripped.IndexOf("LockedGroup") < 0 && ser.ToJson(sd).IndexOf("LockedGroup") >= 0);
        string legacyHash = SaveIntegrity.Compute(stripped);
        string legacyText = stripped.Replace("<Hash />", "<Hash>" + legacyHash + "</Hash>");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "slot2.json"), legacyText);
        LoadResult lr = svc.Load(2);
        Check("pre-group save (hash without LockedGroup) still loads", lr.Status == LoadStatus.Ok && SaveMapper.FromSave(lr.Data).IsRouteMember("howl"));
        File.WriteAllText(Path.Combine(dir, "slot2.json"), legacyText.Replace("<LockedRoute>howl</LockedRoute>", "<LockedRoute>johan</LockedRoute>"));
        Check("tampered pre-group save still detected", svc.Load(2).Status == LoadStatus.Tampered);

        string jsonOne = "{\"A\":1,\"LockedRoute\":\"\",\"LockedGroup\":[],\"PlayTimeSec\":0}";
        string jsonPretty = "{\n  \"LockedRoute\": \"\",\n  \"LockedGroup\": [],\n  \"PlayTimeSec\": 0\n}";
        Check("strip handles compact + pretty JSON", SaveService.StripEmptyLockedGroup(jsonOne) == "{\"A\":1,\"LockedRoute\":\"\",\"PlayTimeSec\":0}"
            && SaveService.StripEmptyLockedGroup(jsonPretty) == "{\n  \"LockedRoute\": \"\",\n  \"PlayTimeSec\": 0\n}");
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    // ---------- Game 어셈블리: NightRomanceActions ----------

    private static NightRomanceActions Actions(GameState s, Func<string, bool> exists)
    {
        return new NightRomanceActions(s, _balance, Svc(s), _catalog, exists, null);
    }

    private static void NightActionChecks()
    {
        Console.WriteLine("== NightRomanceActions group lock ==");
        GameState s = Make(7);
        Set(s, "howl", 65f, 22f, 2); Set(s, "johan", 80f, 30f, 3); Set(s, "haru", 45f, 25f, 2); Set(s, "nabi", 70f, 5f, 1);
        s.Flags["harem_ok_johan"] = 1;
        NightRomanceActions act = Actions(s, null);
        Check("RouteLockCandidates = eligible only", string.Join(",", act.RouteLockCandidates().ToArray()) == "howl,johan");
        List<RouteLockOption> opts = act.RouteLockOptions();
        RouteLockOption howl = opts[0], haru = opts[3], nabi = opts[5], gom = opts[4];
        Check("options cover all 6 chars", opts.Count == 6 && howl.Id == "howl" && nabi.Id == "nabi");
        Check("eligible option has no reason", howl.Eligible && howl.Reason.Length == 0 && howl.Message.Length == 0 && howl.Aff == 65f);
        Check("haru aff 45 (below group min 50) -> affection_too_low + message", !haru.Eligible && haru.Reason == "affection_too_low" && haru.Message.Contains("호감"));
        Check("nabi trust 5 (below group min 7) -> trust_too_low", !nabi.Eligible && nabi.Reason == "trust_too_low" && nabi.Message.Contains("신뢰"));
        Check("gomseok at 0/0 not eligible", !gom.Eligible);
        Check("consent flag surfaced", opts[1].HaremConsentDone && !howl.HaremConsentDone);

        string msg;
        Check("ValidateGroup ok pair", act.ValidateGroup(new string[] { "howl", "johan" }, out msg) && msg.Length == 0);
        Check("ValidateGroup size 4 message", !act.ValidateGroup(new string[] { "howl", "johan", "haru", "nabi" }, out msg) && msg.Contains("최대 3명"));
        Check("ValidateGroup weak member message", !act.ValidateGroup(new string[] { "howl", "haru" }, out msg) && msg.Contains("호감도가 부족"));
        Check("ValidateGroup duplicate message", !act.ValidateGroup(new string[] { "howl", "howl" }, out msg) && msg.Contains("두 번"));
        Check("ValidateGroup empty message", !act.ValidateGroup(new string[0], out msg) && msg.Contains("선택"));
        Check("ValidateGroup does not lock", !s.HasRouteLock);

        Check("TryLockGroup refused: message, state untouched", !act.TryLockGroup(new string[] { "howl", "haru" }, out msg) && msg.Length > 0 && !s.HasRouteLock);
        Check("TryLockGroup ok: names in message", act.TryLockGroup(new string[] { "howl", "johan" }, out msg) && msg.Contains("하울") && msg.Contains("요한") && s.LockedCount == 2);
        Check("after lock: candidates empty, options say already_locked", act.RouteLockCandidates().Count == 0 && act.RouteLockOptions()[0].Reason == "already_locked");
        Check("TryLockGroup again -> already locked message", !act.TryLockGroup(new string[] { "howl" }, out msg) && msg.Contains("이미"));

        GameState one = Make(7);
        Set(one, "howl", 65f, 22f, 0);
        NightRomanceActions a1 = Actions(one, null);
        Check("TryLockGroup with 1 id: single-route message", a1.TryLockGroup(new string[] { "howl" }, out msg) && msg.Contains("길을 선택") && one.LockedRoute == "howl");
        GameState early = Make(6);
        Set(early, "howl", 65f, 22f, 0);
        Check("chapter 6 message", !Actions(early, null).TryLockGroup(new string[] { "howl" }, out msg) && msg.Contains("7장"));

        // 기존 단독 API 도 유지
        GameState single = Make(7);
        Set(single, "howl", 65f, 22f, 0);
        Check("legacy TryLockRoute(out msg) still works", Actions(single, null).TryLockRoute("howl", out msg) && single.LockedGroup.Count == 1);

        // 대화 스크립트 커맨드/함수
        GameState ds = Make(7);
        Set(ds, "howl", 65f, 22f, 0); Set(ds, "johan", 65f, 22f, 0);
        RomanceService dsvc = Svc(ds);
        DialogueCommandDispatcher disp = new DialogueCommandDispatcher(ds, dsvc, null);
        disp.Handle("route_lock", new string[] { "howl", "johan" });
        Check("route_lock command with 2 args locks group", ds.LockedCount == 2 && disp.Warnings.Count == 0);
        GameState ds2 = Make(7);
        Set(ds2, "howl", 65f, 22f, 0);
        DialogueCommandDispatcher disp2 = new DialogueCommandDispatcher(ds2, Svc(ds2), null);
        disp2.Handle("route_lock", new string[] { "howl", "johan" });
        Check("route_lock group refused -> warning, no lock", !ds2.HasRouteLock && disp2.Warnings.Count == 1);
    }

    // ---------- Game 어셈블리: EndingPlan ----------

    private static void PlanChecks()
    {
        Console.WriteLine("== EndingPlan ==");
        GameState s = Make(8);
        Set(s, "howl", 96f, 47f, 5);
        s.LockedRoute = "howl";
        Flags(s, "ledger_truth", "howl_key");
        EndingPlan p = EndingPlan.Create(s, _table, _catalog, n => n == "Howl_End_True");
        Check("plan true: node exists", p.Kind == EndingKind.True && p.NodeName == "Howl_End_True" && p.IntendedNode == "Howl_End_True" && p.TitleKo == "트루 엔딩");
        Check("plan true: CG + names", p.CgIds.Count == 1 && p.CgIds[0] == "HOWL_E6" && p.PrimaryName == "하울" && p.MemberNames.Count == 1);

        p = EndingPlan.Create(s, _table, _catalog, n => false);
        Check("plan: missing node -> NodeName null + fallback, intended kept", p.NodeName == null && p.IntendedNode == "Howl_End_True" && p.FallbackText.Contains("하울"));
        p = EndingPlan.Create(s, _table, _catalog, null);
        Check("plan: nodeExists null -> assumes node exists", p.NodeName == "Howl_End_True");

        GameState g = Make(8);
        Set(g, "johan", 80f, 36f, 4); Set(g, "haru", 80f, 36f, 4);
        g.SetLock(new string[] { "johan", "haru" });
        Flags(g, "harem_ok_johan", "harem_ok_haru");
        p = EndingPlan.Create(g, _table, _catalog, n => n == "Ending_Harem2");
        Check("plan duo harem: node + title + both names", p.Kind == EndingKind.HaremDuo && p.NodeName == "Ending_Harem2" && p.MemberNames.Count == 2
            && p.MemberNames[0] == "요한" && p.MemberNames[1] == "하루" && p.TitleKo.Contains("두 사람") && p.CgIds[0] == "HAREM_E1");
        Check("plan harem fallback mentions both", EndingPlan.Create(g, _table, _catalog, n => false).FallbackText.Contains("요한, 하루"));

        GameState c = Make(8);
        p = EndingPlan.Create(c, _table, null, null);
        Check("plan community without catalog", p.Kind == EndingKind.Community && p.NodeName == "Ending_Community" && p.PrimaryName.Length == 0 && p.TitleKo == "달빛 모퉁이");
        Check("TitleOf covers every kind", EndingPlan.TitleOf(EndingKind.Normal) == "노멀 엔딩" && EndingPlan.TitleOf(EndingKind.Good) == "굿 엔딩"
            && EndingPlan.TitleOf(EndingKind.HaremTrio).Contains("세 사람"));

        bool threw = false;
        try { EndingPlan.Create(null, _table, _catalog, null); } catch (ArgumentNullException) { threw = true; }
        Check("plan null state throws", threw);
    }
}
