using System;
using System.Collections.Generic;
using System.IO;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;

// 3단계 UI/흐름 순수 로직 검증 하네스(Unity 불필요). 빌드/실행: tools/csharp_check/flow_check/run.sh
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

    private static NightRomanceActions Night(GameState s, Func<string, bool> exists)
    {
        return new NightRomanceActions(s, _balance, Svc(s), _catalog, exists, null);
    }

    private static void Set(GameState s, string id, float aff, float trust, int events)
    {
        CharState c = s.Chars[id];
        c.Aff = aff; c.Trust = trust; c.EventsDone = events;
    }

    private static Func<string, bool> Nodes(params string[] names)
    {
        List<string> list = new List<string>(names);
        return delegate (string n) { return list.Contains(n); };
    }

    private static string Join(IList<SceneItem> items)
    {
        List<string> names = new List<string>();
        for (int i = 0; i < items.Count; i++) names.Add(items[i].Node ?? ("~" + items[i].IntendedNode));
        return string.Join(",", names.ToArray());
    }

    // ---------- 챕터 흐름 ----------

    private static void ChapterChecks()
    {
        Console.WriteLine("== ChapterFlow ==");
        Check("intro node name", ChapterFlow.IntroNode(3) == "Ch3_Intro");
        Check("outro node name", ChapterFlow.OutroNode(8) == "Ch8_Outro");

        List<string> seen = new List<string>();
        Func<string, bool> visited = delegate (string n) { return seen.Contains(n); };
        Func<string, bool> all = Nodes("Ch1_Intro", "Ch1_Outro", "Ch2_Intro", "Ch2_Outro");

        ChapterStep st = ChapterFlow.NextMorningStep(1, 1, true, all, visited);
        Check("ch1 day1: intro node plays", st.Kind == ChapterStepKind.Intro && st.Node == "Ch1_Intro");
        seen.Add("Ch1_Intro");
        st = ChapterFlow.NextMorningStep(1, 1, true, all, visited);
        Check("ch1 day1 after intro visited, pending card -> info card", st.Kind == ChapterStepKind.InfoCard);
        st = ChapterFlow.NextMorningStep(1, 1, false, all, visited);
        Check("ch1 day1 after intro, acked -> none", st.Kind == ChapterStepKind.None);
        st = ChapterFlow.NextMorningStep(1, 2, true, all, visited);
        Check("day 2 never triggers", st.Kind == ChapterStepKind.None);

        seen.Clear();
        st = ChapterFlow.NextMorningStep(2, 1, true, all, visited);
        Check("ch2 day1: outro of ch1 first", st.Kind == ChapterStepKind.Outro && st.Node == "Ch1_Outro");
        seen.Add("Ch1_Outro");
        st = ChapterFlow.NextMorningStep(2, 1, true, all, visited);
        Check("ch2 day1: then intro", st.Kind == ChapterStepKind.Intro && st.Node == "Ch2_Intro");
        seen.Add("Ch2_Intro");
        st = ChapterFlow.NextMorningStep(2, 1, true, all, visited);
        Check("ch2 after both: card fallback while pending", st.Kind == ChapterStepKind.InfoCard);

        st = ChapterFlow.NextMorningStep(3, 1, true, Nodes(), visited);
        Check("no nodes at all, pending -> info card", st.Kind == ChapterStepKind.InfoCard && st.Node.Length == 0);
        st = ChapterFlow.NextMorningStep(3, 1, false, Nodes(), visited);
        Check("no nodes, not pending (loaded save) -> none", st.Kind == ChapterStepKind.None);
        st = ChapterFlow.NextMorningStep(3, 1, true, Nodes("Ch3_Intro"), visited);
        Check("outro missing -> intro directly", st.Kind == ChapterStepKind.Intro);
        st = ChapterFlow.NextMorningStep(3, 1, false, Nodes("Ch2_Outro"), null);
        Check("loaded save at day1: unseen outro still plays (null visited = unseen)", st.Kind == ChapterStepKind.Outro && st.Node == "Ch2_Outro");
        st = ChapterFlow.NextMorningStep(1, 1, true, null, null);
        Check("null callbacks tolerated", st.Kind == ChapterStepKind.InfoCard);
        st = ChapterFlow.NextMorningStep(1, 1, true, Nodes("Ch0_Outro"), null);
        Check("chapter 1 never asks for an outro", st.Kind != ChapterStepKind.Outro);

        // 챕터 1→8 전체를 상태 기반으로 시뮬레이션: 각 챕터 첫 아침마다 outro→intro 1회씩, 중복 없음
        List<string> played = new List<string>();
        List<string> nodes = new List<string>();
        for (int c = 1; c <= 8; c++) { nodes.Add(ChapterFlow.IntroNode(c)); nodes.Add(ChapterFlow.OutroNode(c)); }
        Func<string, bool> ex = delegate (string n) { return nodes.Contains(n); };
        Func<string, bool> vis = delegate (string n) { return played.Contains(n); };
        bool dup = false;
        for (int c = 1; c <= 8; c++)
        {
            for (int guard = 0; guard < 5; guard++)
            {
                ChapterStep x = ChapterFlow.NextMorningStep(c, 1, true, ex, vis);
                if (x.Kind == ChapterStepKind.Outro || x.Kind == ChapterStepKind.Intro)
                {
                    if (played.Contains(x.Node)) dup = true;
                    played.Add(x.Node);
                }
                else break;
            }
        }
        Check("full 1..8 simulation plays 15 nodes (Ch8_Outro is the ending's), no duplicates", played.Count == 15 && !dup && !played.Contains("Ch8_Outro"));
        Check("simulation order starts Ch1_Intro, Ch1_Outro, Ch2_Intro", played[0] == "Ch1_Intro" && played[1] == "Ch1_Outro" && played[2] == "Ch2_Intro");

        Check("FinalOutro exists & unseen", ChapterFlow.FinalOutro(8, Nodes("Ch8_Outro"), delegate (string n) { return false; }) == "Ch8_Outro");
        Check("FinalOutro already seen -> null", ChapterFlow.FinalOutro(8, Nodes("Ch8_Outro"), delegate (string n) { return true; }) == null);
        Check("FinalOutro missing -> null", ChapterFlow.FinalOutro(8, Nodes(), null) == null);
    }

    // ---------- 루트 선택 모델 ----------

    private static void RouteSelectChecks()
    {
        Console.WriteLine("== RouteSelectModel ==");
        string msg;
        GameState early = Make(6);
        Set(early, "howl", 70f, 30f, 0);
        RouteSelectModel m0 = new RouteSelectModel(Night(early, null), _table);
        Check("six options in CharIds order", m0.Options.Count == 6 && m0.Options[0].Id == "howl" && m0.Options[5].Id == "nabi");
        Check("chapter 6: nobody eligible, message mentions 7장", !m0.AnyEligible && m0.Options[0].Message.Contains("7장"));
        Check("toggle ineligible refused with Korean reason", !m0.Toggle("howl", out msg) && msg.Contains("7장") && m0.Selected.Count == 0);
        Check("confirm with nothing selected refused", !m0.CanConfirm(out msg) && msg.Contains("선택"));
        Check("toggle unknown id refused", !m0.Toggle("nobody", out msg) && msg.Length > 0);

        GameState s = Make(7);
        Set(s, "howl", 80f, 36f, 4); Set(s, "johan", 78f, 35f, 4); Set(s, "beomjun", 72f, 31f, 4);
        Set(s, "haru", 66f, 22f, 2); Set(s, "gomseok", 45f, 30f, 0); Set(s, "nabi", 70f, 5f, 0);
        RouteSelectModel m = new RouteSelectModel(Night(s, null), _table);
        Check("eligible: howl johan beomjun haru; not gomseok(aff) not nabi(trust)",
            m.Find("howl").Eligible && m.Find("haru").Eligible && !m.Find("gomseok").Eligible && !m.Find("nabi").Eligible);
        Check("gomseok refusal text is affection", m.Find("gomseok").Message.Contains("호감도"));
        Check("nabi refusal text is trust", m.Find("nabi").Message.Contains("신뢰도"));
        Check("Mode undecided at start", m.Mode == RouteChoiceMode.Undecided);
        Check("select howl -> Single", m.Toggle("howl", out msg) && m.Mode == RouteChoiceMode.Single && m.IsSelected("howl"));
        Check("single confirmable", m.CanConfirm(out msg) && msg.Length == 0);
        Check("select johan -> Group", m.Toggle("johan", out msg) && m.Mode == RouteChoiceMode.Group);
        Check("select beomjun -> 3 members", m.Toggle("beomjun", out msg) && m.Selected.Count == 3);
        Check("4th selection refused (max 3)", !m.Toggle("haru", out msg) && msg.Contains("3명") && m.Selected.Count == 3);
        Check("selection order kept", string.Join(",", m.Selected.ToArray()) == "howl,johan,beomjun");
        Check("deselect middle", m.Toggle("johan", out msg) && string.Join(",", m.Selected.ToArray()) == "howl,beomjun");
        Check("explanation for group mentions consent and community fallback", m.ModeExplanation().Contains("동의") && m.ModeExplanation().Contains("커뮤니티"));
        Check("duo explanation uses CSV numbers 72/24/3", m.ModeExplanation().Contains("72") && m.ModeExplanation().Contains("24") && m.ModeExplanation().Contains("3단계"));
        m.Toggle("johan", out msg);
        Check("trio explanation uses 65/14", m.ModeExplanation().Contains("65") && m.ModeExplanation().Contains("14"));
        Check("group rules text without a table has no numbers but still explains", !RouteSelectModel.GroupRulesText(null, 2).Contains("75") && RouteSelectModel.GroupRulesText(null, 2).Contains("동의"));
        Check("single/undecided explanations differ", new RouteSelectModel(Night(s, null), _table).ModeExplanation().Contains("최대 3명"));
        Check("lock warning mentions freeze", m.LockWarning().Contains("변하지 않"));
        Check("readiness: howl meets trio criteria", m.ReadinessHint("howl", 3) == "공동 기준 충족");
        m.Toggle("beomjun", out msg); m.Toggle("johan", out msg); m.Toggle("howl", out msg);
        m.Toggle("haru", out msg);
        Check("readiness: haru below duo criteria", m.ReadinessHint("haru", 2).Contains("미달"));
        m.ClearSelection();
        Check("cleared", m.Selected.Count == 0 && m.Mode == RouteChoiceMode.Undecided);

        // 확정(공동 2명)
        m.Toggle("howl", out msg); m.Toggle("johan", out msg);
        bool ok = m.Commit(out msg);
        Check("group commit ok, message names members", ok && msg.Contains("하울") && msg.Contains("요한"));
        Check("state locked as group", s.LockedCount == 2 && s.IsRouteMember("howl") && s.IsRouteMember("johan") && !s.IsRouteMember("haru"));
        Check("LockedMembers filled in order", string.Join(",", m.LockedMembers.ToArray()) == "howl,johan");
        Check("after lock all options say already locked", !m.AnyEligible && m.Find("haru").Reason == "already_locked");
        Check("after commit the selection is cleared and a second commit is refused", m.Selected.Count == 0 && !m.Commit(out msg) && msg.Length > 0 && m.LockedMembers.Count == 0 && s.LockedCount == 2);

        // 단독 확정
        GameState s1 = Make(7);
        Set(s1, "haru", 66f, 22f, 0);
        RouteSelectModel m1 = new RouteSelectModel(Night(s1, null), _table);
        m1.Toggle("haru", out msg);
        Check("single commit ok", m1.Commit(out msg) && s1.LockedRoute == "haru" && s1.LockedCount == 1 && msg.Contains("하루"));

        // 새로고침이 선택에서 부적격자를 뺀다
        GameState s2 = Make(7);
        Set(s2, "howl", 70f, 30f, 0); Set(s2, "johan", 70f, 30f, 0);
        RouteSelectModel m2 = new RouteSelectModel(Night(s2, null), _table);
        m2.Toggle("howl", out msg); m2.Toggle("johan", out msg);
        s2.Chars["johan"].Aff = 10f;
        m2.Refresh();
        Check("Refresh drops members that became ineligible", m2.Selected.Count == 1 && m2.Selected[0] == "howl");
        Check("model needs a night controller", Throws(delegate { new RouteSelectModel(null, null); }));
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (ArgumentNullException) { return true; }
        catch (Exception) { return false; }
    }

    // ---------- 루트 흐름/동의/복구 ----------

    private static void RouteFlowChecks()
    {
        Console.WriteLine("== RouteFlow / Consent / Recovery ==");
        string[] duo = { "howl", "johan" };
        Func<string, bool> full = Nodes("Harem_Table", "Howl_Harem_Consent", "Johan_Harem_Consent");
        Check("post-lock: table then consents in member order", Join(RouteFlow.PostLockScenes(duo, full)) == "Harem_Table,Howl_Harem_Consent,Johan_Harem_Consent");
        Check("post-lock: reversed member order follows members", Join(RouteFlow.PostLockScenes(new string[] { "johan", "howl" }, full)) == "Harem_Table,Johan_Harem_Consent,Howl_Harem_Consent");
        Check("post-lock: missing table skipped", Join(RouteFlow.PostLockScenes(duo, Nodes("Howl_Harem_Consent", "Johan_Harem_Consent"))) == "Howl_Harem_Consent,Johan_Harem_Consent");
        Check("post-lock: missing one consent skipped", Join(RouteFlow.PostLockScenes(duo, Nodes("Harem_Table", "Johan_Harem_Consent"))) == "Harem_Table,Johan_Harem_Consent");
        Check("post-lock: nothing exists -> empty", RouteFlow.PostLockScenes(duo, Nodes()).Count == 0);
        Check("post-lock: single member -> empty", RouteFlow.PostLockScenes(new string[] { "howl" }, full).Count == 0);
        Check("post-lock: null members/exists -> empty", RouteFlow.PostLockScenes(null, full).Count == 0 && RouteFlow.PostLockScenes(duo, null).Count == 0);
        Check("post-lock: trio has 4 scenes", RouteFlow.PostLockScenes(new string[] { "howl", "johan", "haru" }, Nodes("Harem_Table", "Howl_Harem_Consent", "Johan_Harem_Consent", "Haru_Harem_Consent")).Count == 4);

        GameState c6 = Make(6), c7 = Make(7), c8 = Make(8);
        Check("menu entry hidden in ch6, shown ch7/ch8", !RouteFlow.MenuEntryVisible(c6) && RouteFlow.MenuEntryVisible(c7) && RouteFlow.MenuEntryVisible(c8));
        c8.Finished = true;
        Check("menu entry hidden after the game finished", !RouteFlow.MenuEntryVisible(c8));
        GameState locked = Make(7); locked.SetLock(new string[] { "howl" });
        Check("menu entry hidden once locked", !RouteFlow.MenuEntryVisible(locked));
        Check("highlight only with eligible candidates", RouteFlow.MenuEntryHighlighted(c7, 1) && !RouteFlow.MenuEntryHighlighted(c7, 0));
        Check("auto prompt: eligible, new day", RouteFlow.ShouldAutoPrompt(c7, 2, -1));
        Check("auto prompt: not twice the same day", !RouteFlow.ShouldAutoPrompt(c7, 2, c7.Day));
        Check("auto prompt: needs eligible", !RouteFlow.ShouldAutoPrompt(c7, 0, -1));
        Check("auto prompt: not before ch7", !RouteFlow.ShouldAutoPrompt(c6, 3, -1));
        c7.Flags[RouteFlow.DeclinedFlag] = 1;
        Check("auto prompt: stops after declining", !RouteFlow.ShouldAutoPrompt(c7, 2, -1) && RouteFlow.MenuEntryVisible(c7));
        Check("dialogue request accepted only while unlocked", RouteFlow.AcceptDialogueRequest(Make(7)) && !RouteFlow.AcceptDialogueRequest(locked) && !RouteFlow.AcceptDialogueRequest(null));

        // 동의 재시도
        Func<string, string> names = delegate (string id) { return id == "howl" ? "하울" : (id == "johan" ? "요한" : id); };
        GameState g = Make(8);
        g.SetLock(duo);
        List<NightExtraEntry> e = ConsentRetry.Entries(g, full, names);
        Check("consent retry: two entries when no flags", e.Count == 2 && e[0].NodeName == "Howl_Harem_Consent" && e[1].NodeName == "Johan_Harem_Consent");
        Check("consent retry label format", e[0].Label == "동의 확인 대화(하울)" && e[0].Kind == NightExtraEntry.KindConsent && e[0].CharId == "howl");
        g.Flags["harem_ok_howl"] = 1;
        e = ConsentRetry.Entries(g, full, names);
        Check("consent retry: member who consented drops out", e.Count == 1 && e[0].CharId == "johan");
        g.Flags["harem_ok_johan"] = 0;
        Check("consent flag value 0 still counts as not consented", ConsentRetry.Entries(g, full, names).Count == 1);
        g.Flags["harem_ok_johan"] = 1;
        Check("consent retry: all consented -> none", ConsentRetry.Entries(g, full, names).Count == 0);
        GameState g2 = Make(8); g2.SetLock(duo);
        Check("consent retry: node missing -> no entry", ConsentRetry.Entries(g2, Nodes("Howl_Harem_Consent"), names).Count == 1);
        GameState single = Make(8); single.SetLock(new string[] { "howl" });
        Check("consent retry: single route has none", ConsentRetry.Entries(single, full, names).Count == 0);
        Check("consent retry: no lock has none", ConsentRetry.Entries(Make(8), full, names).Count == 0);
        g2.Finished = true;
        Check("consent retry: gone after the game ends", ConsentRetry.Entries(g2, full, names).Count == 0);
        GameState legacy = Make(8); legacy.LockedRoute = "howl";
        Check("consent retry: legacy single lock has none", ConsentRetry.Entries(legacy, full, names).Count == 0);
        GameState g3 = Make(8); g3.SetLock(duo);
        int before = g3.Flags.Count;
        NightExtras.Collect(g3, null, full, null, names);
        Check("host never sets consent flags (collect leaves flags untouched)", g3.Flags.Count == before && !g3.Flags.ContainsKey("harem_ok_howl"));
        Check("consent flag/node names", ConsentRetry.ConsentFlag("nabi") == "harem_ok_nabi" && ConsentRetry.ConsentNode("nabi") == "Nabi_Harem_Consent");

        // 복구 표
        RecoveryTable table = RecoveryTable.Default();
        Func<string, bool> retry = Nodes("Howl_E4_Retry");
        Func<string, bool> notVisited = delegate (string n) { return false; };
        GameState r = Make(5);
        Check("recovery: no fail flag -> none", table.Entries(r, retry, notVisited).Count == 0);
        r.Flags["howl_e4_fail"] = 1;
        List<NightExtraEntry> re = table.Entries(r, retry, notVisited);
        Check("recovery: fail flag -> Howl_E4_Retry entry", re.Count == 1 && re[0].NodeName == "Howl_E4_Retry" && re[0].Kind == NightExtraEntry.KindRecovery && re[0].Label.Length > 0);
        Check("recovery: node missing -> none", table.Entries(r, Nodes(), notVisited).Count == 0);
        Check("recovery: plays once (visited -> none)", table.Entries(r, retry, delegate (string n) { return n == "Howl_E4_Retry"; }).Count == 0);
        r.Flags["howl_e4_trust"] = 1;
        Check("recovery: trust flag blocks", table.Entries(r, retry, notVisited).Count == 0);
        r.Flags["howl_e4_trust"] = 0;
        Check("recovery: trust flag value 0 does not block", table.Entries(r, retry, notVisited).Count == 1);
        r.Flags["howl_e4_trust_late"] = 1;
        Check("recovery: trust_late flag blocks", table.Entries(r, retry, notVisited).Count == 0);
        GameState r2 = Make(5); r2.Flags["howl_e4_fail"] = 0;
        Check("recovery: fail flag value 0 is not set", table.Entries(r2, retry, notVisited).Count == 0);

        RecoveryTable custom = new RecoveryTable();
        RecoveryRule rule = new RecoveryRule();
        rule.Id = "x"; rule.NodeName = "X_Node"; rule.Label = "엑스"; rule.RequireFlags.Add("a"); rule.RequireFlags.Add("b"); rule.MinChapter = 3; rule.Once = false;
        custom.Add(rule);
        GameState cs = Make(2); cs.Flags["a"] = 1; cs.Flags["b"] = 1;
        Check("custom rule: chapter gate", custom.Entries(cs, Nodes("X_Node"), notVisited).Count == 0);
        cs.Chapter = 3;
        Check("custom rule: all required flags + chapter -> entry", custom.Entries(cs, Nodes("X_Node"), notVisited).Count == 1);
        cs.Flags["b"] = 0;
        Check("custom rule: one required flag missing -> none", custom.Entries(cs, Nodes("X_Node"), notVisited).Count == 0);
        cs.Flags["b"] = 1;
        Check("custom rule: Once=false ignores visited", custom.Entries(cs, Nodes("X_Node"), delegate (string n) { return true; }).Count == 1);
        RecoveryRule replaced = new RecoveryRule(); replaced.Id = "x"; replaced.NodeName = "Y_Node";
        custom.Add(replaced);
        Check("rule with the same id replaces", custom.Rules.Count == 1 && custom.Rules[0].NodeName == "Y_Node");
        Check("default table has exactly the howl rule", RecoveryTable.Default().Rules.Count == 1 && RecoveryTable.Default().Rules[0].Id == "howl_e4_retry");

        GameState both = Make(8); both.SetLock(duo); both.Flags["howl_e4_fail"] = 1;
        Check("NightExtras: recovery + 2 consents = 3", NightExtras.Collect(both, null, Nodes("Howl_E4_Retry", "Howl_Harem_Consent", "Johan_Harem_Consent"), notVisited, names).Count == 3);
        both.Finished = true;
        Check("NightExtras: nothing after the game finished", NightExtras.Collect(both, null, full, notVisited, names).Count == 0);
        Check("NightExtras: null state -> empty", NightExtras.Collect(null, null, full, notVisited, names).Count == 0);
    }

    // ---------- 엔딩 장면 계획 ----------

    private static void EndingFlowChecks()
    {
        Console.WriteLine("== EndingFlow / EndSummary ==");
        GameState com = Make(8);
        EndingPlan pc = EndingPlan.Create(com, _table, _catalog, Nodes("Ending_Community"));
        List<SceneItem> sc = EndingFlow.Scenes(pc, Nodes("Ending_Community"));
        Check("community: one scene Ending_Community", sc.Count == 1 && sc[0].Node == "Ending_Community");
        pc = EndingPlan.Create(com, _table, _catalog, Nodes());
        sc = EndingFlow.Scenes(pc, Nodes());
        Check("community node missing -> fallback text, Node null, intended kept", sc.Count == 1 && sc[0].Node == null && sc[0].FallbackText.Contains("공통 엔딩") && sc[0].IntendedNode == "Ending_Community");

        GameState one = Make(8);
        Set(one, "howl", 90f, 42f, 5);
        one.LockedRoute = "howl";
        EndingPlan pg = EndingPlan.Create(one, _table, _catalog, Nodes("Howl_End_Good"));
        sc = EndingFlow.Scenes(pg, Nodes("Howl_End_Good"));
        Check("single good: exactly Howl_End_Good", sc.Count == 1 && sc[0].Node == "Howl_End_Good" && pg.Kind == EndingKind.Good);
        sc = EndingFlow.Scenes(EndingPlan.Create(one, _table, _catalog, Nodes()), Nodes());
        Check("single good missing -> fallback mentions 하울", sc.Count == 1 && sc[0].Node == null && sc[0].FallbackText.Contains("하울"));

        GameState tr = Make(8);
        Set(tr, "gomseok", 99f, 48f, 5);
        tr.LockedRoute = "gomseok";
        tr.Flags["ledger_truth"] = 1; tr.Flags["gomseok_key"] = 1;
        EndingPlan pt = EndingPlan.Create(tr, _table, _catalog, Nodes("Gomseok_End_True"));
        Check("true ending: only the entry node (jumps happen inside the script)", pt.Kind == EndingKind.True && EndingFlow.Scenes(pt, Nodes("Gomseok_End_True")).Count == 1 && EndingFlow.Scenes(pt, null)[0].Node == "Gomseok_End_True");

        GameState h = Make(8);
        Set(h, "howl", 80f, 36f, 5); Set(h, "johan", 80f, 36f, 5);
        h.SetLock(new string[] { "howl", "johan" });
        h.Flags["harem_ok_howl"] = 1; h.Flags["harem_ok_johan"] = 1;
        Func<string, bool> hn = Nodes("Ending_Harem2", "Howl_Harem_Epilogue", "Johan_Harem_Epilogue");
        EndingPlan ph = EndingPlan.Create(h, _table, _catalog, hn);
        sc = EndingFlow.Scenes(ph, hn);
        Check("harem2: Ending_Harem2 then epilogues in member order", ph.Kind == EndingKind.HaremDuo && Join(sc) == "Ending_Harem2,Howl_Harem_Epilogue,Johan_Harem_Epilogue");
        Func<string, bool> partial = Nodes("Ending_Harem2", "Johan_Harem_Epilogue");
        sc = EndingFlow.Scenes(EndingPlan.Create(h, _table, _catalog, partial), partial);
        Check("harem2: missing epilogue -> fallback with the member name", Join(sc) == "Ending_Harem2,~Howl_Harem_Epilogue,Johan_Harem_Epilogue" && sc[1].FallbackText.Contains("하울"));
        sc = EndingFlow.Scenes(EndingPlan.Create(h, _table, _catalog, Nodes()), Nodes());
        Check("harem2: everything missing -> 3 fallback scenes", sc.Count == 3 && sc[0].Node == null && sc[1].Node == null && sc[2].Node == null && sc[0].FallbackText.Length > 0);

        GameState h3 = Make(8);
        string[] trio = { "howl", "johan", "beomjun" };
        for (int i = 0; i < 3; i++) { Set(h3, trio[i], 80f, 36f, 5); h3.Flags["harem_ok_" + trio[i]] = 1; }
        h3.SetLock(trio);
        Func<string, bool> tn = Nodes("Ending_Harem3", "Howl_Harem_Epilogue", "Johan_Harem_Epilogue", "Beomjun_Harem_Epilogue");
        sc = EndingFlow.Scenes(EndingPlan.Create(h3, _table, _catalog, tn), tn);
        Check("harem3: 4 scenes in order", Join(sc) == "Ending_Harem3,Howl_Harem_Epilogue,Johan_Harem_Epilogue,Beomjun_Harem_Epilogue");

        GameState hu = Make(8);
        Set(hu, "howl", 80f, 36f, 5); Set(hu, "johan", 80f, 36f, 5);
        hu.SetLock(new string[] { "howl", "johan" });
        sc = EndingFlow.Scenes(EndingPlan.Create(hu, _table, _catalog, null), null);
        Check("group without consent -> community only (no epilogues)", sc.Count == 1 && sc[0].Node == "Ending_Community");
        Check("null plan throws", Throws(delegate { EndingFlow.Scenes(null, null); }));

        EndSummary es = EndSummary.Build(ph, h, 3, 10, 2, EndingGallery.TotalSlots);
        Check("summary: Korean title and members subtitle", es.Title == "두 사람과 함께하는 엔딩" && es.Subtitle.Contains("하울") && es.Subtitle.Contains("요한"));
        Check("summary: has gold, play time, CG, endings lines", es.Lines.Count == 5 && es.Lines[3].Contains("3장") && es.Lines[3].Contains("누적 10장") && es.Lines[4].Contains("2 / 21"));
        h.PlayTimeSec = 3725.0;
        Check("summary: play time formatted 1시간 2분", EndSummary.Build(ph, h, 0, -1, -1, 0).Lines[2].Contains("1시간 2분"));
        Check("summary: unknown profile counts omitted", EndSummary.Build(ph, h, 0, -1, -1, 0).Lines.Count == 4);
        Check("play time formatting edge cases", EndSummary.FormatPlayTime(59.0) == "0분" && EndSummary.FormatPlayTime(-5.0) == "0분" && EndSummary.FormatPlayTime(double.NaN) == "0분" && EndSummary.FormatPlayTime(7200.0) == "2시간 0분");
        EndSummary sg = EndSummary.Build(pg, one, 0, 0, 0, 21);
        Check("summary: single subtitle names the character", sg.Title == "굿 엔딩" && sg.Subtitle.Contains("하울"));
        Check("summary: community subtitle", EndSummary.Build(pc, com, 0, 0, 0, 21).Subtitle.Contains("달빛 모퉁이"));
    }

    // ---------- 프로필 / 해금 / 갤러리 ----------

    private static EndingResult Result(GameState s)
    {
        return EndingEvaluator.Evaluate(s, _table);
    }

    private static void ProfileChecks()
    {
        Console.WriteLine("== ProfileService / UnlockService / Gallery ==");
        MemoryProfileStorage st = new MemoryProfileStorage();
        ProfileService p = new ProfileService(st);
        Check("fresh: Missing status, empty", p.Load() == ProfileLoadStatus.Missing && p.ReachedCount == 0 && p.SeenCgs.Count == 0);

        GameState com = Make(8);
        GameState good = Make(8); Set(good, "howl", 90f, 42f, 5); good.LockedRoute = "howl";
        GameState tr = Make(8); Set(tr, "nabi", 99f, 48f, 5); tr.LockedRoute = "nabi"; tr.Flags["ledger_truth"] = 1; tr.Flags["nabi_key"] = 1;
        GameState duo = Make(8); Set(duo, "howl", 80f, 36f, 5); Set(duo, "haru", 80f, 36f, 5); duo.SetLock(new string[] { "howl", "haru" });
        duo.Flags["harem_ok_howl"] = 1; duo.Flags["harem_ok_haru"] = 1;
        GameState trio = Make(8); string[] t3 = { "howl", "johan", "haru" };
        for (int i = 0; i < 3; i++) { Set(trio, t3[i], 80f, 36f, 5); trio.Flags["harem_ok_" + t3[i]] = 1; }
        trio.SetLock(t3);

        Check("endingKey community", ProfileService.EndingKey(Result(com)) == "community");
        Check("endingKey good:howl", ProfileService.EndingKey(Result(good)) == "good:howl");
        Check("endingKey true:nabi", ProfileService.EndingKey(Result(tr)) == "true:nabi");
        Check("endingKey harem_duo / harem_trio", ProfileService.EndingKey(Result(duo)) == "harem_duo" && ProfileService.EndingKey(Result(trio)) == "harem_trio");
        Check("normal key", ProfileService.EndingKey(new EndingResult { Kind = EndingKind.Normal, PrimaryChar = "gomseok" }) == "normal:gomseok");

        Check("record: first time returns true", p.RecordEnding(Result(good)));
        Check("record: repeat returns false, counts 2", !p.RecordEnding(Result(good)) && p.ReachCount("good:howl") == 2 && p.ReachedCount == 1);
        p.RecordEnding(Result(com)); p.RecordEnding(Result(duo));
        Check("three distinct endings", p.ReachedCount == 3 && p.HasReached("community") && p.HasReached("harem_duo") && !p.HasReached("harem_trio") && !p.HasReached(null));
        Check("cg: normalized to upper case, dedup", p.RecordCg("howl_e1") && !p.RecordCg("HOWL_E1") && p.SeenCgs.Count == 1 && p.SeenCgs[0] == "HOWL_E1");
        Check("cg: empty / overlong ignored", !p.RecordCg("  ") && !p.RecordCg(null) && !p.RecordCg(new string('x', 200)));
        Check("save succeeds", p.Save() && st.WriteCount == 1);

        ProfileService q = new ProfileService(st);
        Check("round trip: Loaded", q.Load() == ProfileLoadStatus.Loaded);
        Check("round trip: endings + counts", q.ReachedCount == 3 && q.ReachCount("good:howl") == 2 && q.HasReached("harem_duo"));
        Check("round trip: cgs", q.SeenCgs.Count == 1 && q.SeenCgs[0] == "HOWL_E1");
        Check("serialization is stable", q.ToJson() == p.ToJson());
        Check("json contains version", st.Text.Contains("\"version\": 1"));

        // 깨진 파일들
        string[] bad = { "{", "not json at all", "[1,2,3]", "{\"endings\": }", "{\"endings\": {\"a\": 1}", "\u0000\u0001garbage", "{\"a\":1} trailing", "\"just a string\"", "{\"endings\": {\"x\": 1,}}" };
        bool allCorrupt = true; bool noThrow = true;
        for (int i = 0; i < bad.Length; i++)
        {
            MemoryProfileStorage b = new MemoryProfileStorage(); b.Text = bad[i];
            ProfileService bp = new ProfileService(b);
            try { if (bp.Load() != ProfileLoadStatus.Corrupt || bp.ReachedCount != 0) allCorrupt = false; }
            catch (Exception) { noThrow = false; }
        }
        Check("corrupted files: never throw", noThrow);
        Check("corrupted files: Corrupt status and empty profile", allCorrupt);
        MemoryProfileStorage deep = new MemoryProfileStorage(); deep.Text = new string('[', 5000) + new string(']', 5000);
        ProfileService dp = new ProfileService(deep);
        Check("absurdly nested file -> Corrupt without crash", dp.Load() == ProfileLoadStatus.Corrupt);
        MemoryProfileStorage empty = new MemoryProfileStorage(); empty.Text = "   ";
        Check("whitespace-only file -> Corrupt (not Missing)", new ProfileService(empty).Load() == ProfileLoadStatus.Corrupt);
        MemoryProfileStorage blank = new MemoryProfileStorage(); blank.Text = "";
        Check("empty-string file -> Missing", new ProfileService(blank).Load() == ProfileLoadStatus.Missing);

        // 필드별로 잘못된 값은 무시하고 나머지는 살린다
        MemoryProfileStorage partial = new MemoryProfileStorage();
        partial.Text = "{\"version\":1,\"endings\":{\"good:howl\":3,\"bad\":\"x\",\"neg\":-2,\"zero\":0,\"\":5},\"cgs\":[\"howl_e1\",7,null,\"HOWL_E1\",\"\"],\"unlocks\":\"oops\"}";
        ProfileService pp = new ProfileService(partial);
        Check("partial: loads valid fields only", pp.Load() == ProfileLoadStatus.Loaded && pp.ReachedCount == 1 && pp.ReachCount("good:howl") == 3);
        Check("partial: cg list sanitized and deduped", pp.SeenCgs.Count == 1 && pp.SeenCgs[0] == "HOWL_E1");
        Check("partial: wrong-typed unlocks ignored", pp.Unlocks.Count == 0);
        MemoryProfileStorage huge = new MemoryProfileStorage(); huge.Text = "{\"endings\":{\"a\":999999999999}}";
        ProfileService hp = new ProfileService(huge); hp.Load();
        Check("absurd count is clamped to 9999", hp.ReachCount("a") == 9999);

        // 깨진 파일 위로 새로 기록하면 복구된다
        MemoryProfileStorage fix = new MemoryProfileStorage(); fix.Text = "{ broken";
        ProfileService fp = new ProfileService(fix);
        fp.Load(); fp.RecordEnding(Result(com)); fp.Save();
        ProfileService fp2 = new ProfileService(fix);
        Check("save over a corrupt file repairs it", fp2.Load() == ProfileLoadStatus.Loaded && fp2.HasReached("community"));

        // 저장 실패 / 저장소 없음
        MemoryProfileStorage fail = new MemoryProfileStorage(); fail.FailWrites = true;
        ProfileService failP = new ProfileService(fail); failP.RecordEnding(Result(com));
        Check("write failure -> Save false, in-memory state intact", !failP.Save() && failP.ReachedCount == 1);
        ProfileService none = new ProfileService(null);
        Check("null storage: Load Missing, Save false, no throw", none.Load() == ProfileLoadStatus.Missing && !none.Save());
        Check("strings with quotes survive the round trip", RoundTripUnlock("a\"b\\c"));

        // 파일 저장소(임시 폴더)
        string dir = Path.Combine(Path.GetTempPath(), "morphe_profile_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "profile.json");
        FileProfileStorage fs = new FileProfileStorage(path);
        Check("file storage: missing -> null", fs.Read() == null);
        ProfileService f1 = new ProfileService(fs); f1.RecordEnding(Result(good)); f1.RecordCg("haru_e2);");
        Check("file storage: write creates folder + file", f1.Save() && File.Exists(path) && !File.Exists(path + ".tmp"));
        ProfileService f2 = new ProfileService(fs);
        Check("file storage: reload", f2.Load() == ProfileLoadStatus.Loaded && f2.HasReached("good:howl"));
        File.WriteAllText(path, "{{{{");
        Check("file storage: corrupted file on disk -> Corrupt", new ProfileService(fs).Load() == ProfileLoadStatus.Corrupt);
        try { Directory.Delete(dir, true); } catch (Exception) { }

        // 해금 서비스
        MemoryProfileStorage us = new MemoryProfileStorage();
        ProfileService up = new ProfileService(us);
        UnlockService u = UnlockService.WithDefaults(up);
        Check("unlock: nothing before any ending", u.Evaluate().Count == 0 && !u.IsUnlocked(UnlockService.ExtraEpilogues));
        up.RecordEnding(Result(com));
        List<string> added = u.Evaluate();
        Check("unlock: extra_epilogues after first ending", added.Count == 1 && added[0] == "extra_epilogues" && u.IsUnlocked("extra_epilogues"));
        Check("unlock: second evaluate adds nothing", u.Evaluate().Count == 0);
        up.Save();
        ProfileService up2 = new ProfileService(us); up2.Load();
        UnlockService u2 = UnlockService.WithDefaults(up2);
        Check("unlock: persists across reload, not re-reported", u2.IsUnlocked("extra_epilogues") && u2.Evaluate().Count == 0);
        u2.Define("pack_a", delegate (ProfileService pr) { return pr.SeenCgs.Count >= 2; });
        Check("custom unlock: condition not met", u2.Evaluate().Count == 0 && !u2.IsUnlocked("pack_a"));
        up2.RecordCg("HOWL_E1"); up2.RecordCg("HOWL_E2");
        Check("custom unlock: met -> reported once", u2.Evaluate().Count == 1 && u2.IsUnlocked("pack_a") && u2.Evaluate().Count == 0);
        u2.Define("boom", delegate (ProfileService pr) { throw new InvalidOperationException("x"); });
        Check("unlock: throwing condition is treated as not met", u2.Evaluate().Count == 0 && !u2.IsUnlocked("boom"));
        u2.Define("boom", delegate (ProfileService pr) { return true; });
        Check("unlock: redefining an id replaces the condition", u2.Evaluate().Count == 1 && u2.IsUnlocked("boom"));
        Check("unlock: invalid arguments rejected", Throws(delegate { new UnlockService(null); }) && ThrowsArg(delegate { u2.Define("", delegate (ProfileService pr) { return true; }); }));

        // 갤러리
        List<EndingSlot> slots = EndingGallery.Slots(p, delegate (string id) { return id == "howl" ? "하울" : id; });
        Check("gallery: 21 slots", slots.Count == 21 && EndingGallery.TotalSlots == 21);
        Check("gallery: first slot title", slots[0].Title == "하울 노멀 엔딩" && slots[0].Key == "normal:howl");
        Check("gallery: reached flags (good:howl, community, harem_duo)", Reached(slots, "good:howl") && Reached(slots, "community") && Reached(slots, "harem_duo") && !Reached(slots, "true:nabi"));
        Check("gallery: counts", slots[1].Count == 2 && slots[1].Key == "good:howl");
        Check("gallery: null profile -> all unreached", AllUnreached(EndingGallery.Slots(null, null)));
        Check("gallery: keys unique", UniqueKeys(slots));
    }

    private static bool ThrowsArg(Action a)
    {
        try { a(); return false; }
        catch (ArgumentException) { return true; }
        catch (Exception) { return false; }
    }

    private static bool RoundTripUnlock(string id)
    {
        MemoryProfileStorage s = new MemoryProfileStorage();
        ProfileService p = new ProfileService(s);
        p.AddUnlock(id); p.Save();
        ProfileService q = new ProfileService(s);
        return q.Load() == ProfileLoadStatus.Loaded && q.IsUnlocked(id);
    }

    private static bool Reached(List<EndingSlot> slots, string key)
    {
        for (int i = 0; i < slots.Count; i++) if (slots[i].Key == key) return slots[i].Reached;
        return false;
    }

    private static bool AllUnreached(List<EndingSlot> slots)
    {
        for (int i = 0; i < slots.Count; i++) if (slots[i].Reached) return false;
        return true;
    }

    private static bool UniqueKeys(List<EndingSlot> slots)
    {
        HashSet<string> set = new HashSet<string>();
        for (int i = 0; i < slots.Count; i++) if (!set.Add(slots[i].Key)) return false;
        return true;
    }

    // ---------- MiniJson / 선택지 / 커맨드 ----------

    private static void MiscChecks()
    {
        Console.WriteLine("== MiniJson / ChoiceLabel / route_choice ==");
        Dictionary<string, object> o = MiniJson.Parse("{\"a\": [1, 2.5, \"x\\n\\u0041\", true, null], \"b\": {\"c\": false}}") as Dictionary<string, object>;
        List<object> a = o != null ? o["a"] as List<object> : null;
        Check("MiniJson parses nested values", o != null && a != null && a.Count == 5 && (double)a[1] == 2.5 && (string)a[2] == "x\nA" && (bool)a[3] && a[4] == null);
        Check("MiniJson escape round trip", (string)MiniJson.Parse(MiniJson.Quote("한글 \"q\" \\ \n\t\u0001")) == "한글 \"q\" \\ \n\t\u0001");
        Check("MiniJson rejects trailing garbage", ThrowsFormat(delegate { MiniJson.Parse("{} x"); }));
        Check("MiniJson rejects unterminated string", ThrowsFormat(delegate { MiniJson.Parse("\"abc"); }));
        Check("MiniJson rejects null input", ThrowsFormat(delegate { MiniJson.Parse(null); }));
        Check("MiniJson empty containers", ((Dictionary<string, object>)MiniJson.Parse("{ }")).Count == 0 && ((List<object>)MiniJson.Parse("[ ]")).Count == 0);

        string ck;
        Check("common cg key: valid forms normalised", ArtNames.TryParseCommonCgKey("harem_e1", out ck) && ck == "HAREM_E1" && ArtNames.TryParseCommonCgKey("COMMON_CH6_PHOTO", out ck) && ck == "COMMON_CH6_PHOTO");
        Check("common cg key: invalid forms rejected", !ArtNames.TryParseCommonCgKey("HAREM", out ck) && !ArtNames.TryParseCommonCgKey("A B_C", out ck) && !ArtNames.TryParseCommonCgKey("NABI_E9", out ck) && !ArtNames.TryParseCommonCgKey(new string('A', 60) + "_B", out ck));
        MemoryProfileStorage cgs = new MemoryProfileStorage();
        ProfileService cp = new ProfileService(cgs);
        cp.RecordCg("HAREM_E1"); cp.RecordCg("COMMON_CE03"); cp.Save();
        ProfileService cp2 = new ProfileService(cgs); cp2.Load();
        Check("profile tracks common cg keys across reload", cp2.SeenCgs.Count == 2 && cp2.SeenCgs[0] == "HAREM_E1" && cp2.SeenCgs[1] == "COMMON_CE03");
        Check("choice: available shows text unchanged", ChoiceLabel.Format("가자", true, true) == "가자" && ChoiceLabel.Format("가자", true, false) == "가자");
        Check("choice: unavailable greyed with generic hint", ChoiceLabel.Format("가자", false, true) == "가자 (조건 미충족)");
        Check("choice: unavailable hidden when setting off", ChoiceLabel.Format("가자", false, false) == null);
        Check("choice hint reveals no specifics", !ChoiceLabel.LockedHint.Contains("key") && !ChoiceLabel.LockedHint.Contains("호감") && !ChoiceLabel.LockedHint.Contains("플래그"));

        GameState s = Make(7);
        DialogueCommandDispatcher d = new DialogueCommandDispatcher(s, Svc(s), null);
        Check("route_choice is a known command", DialogueCommandDispatcher.IsKnown("route_choice"));
        Check("route_choice not requested initially", !d.RouteChoiceRequested);
        Check("route_choice handled, request recorded, no warning", d.Handle("route_choice", new string[0]) && d.RouteChoiceRequested && d.Warnings.Count == 0);
        Check("route_choice does not lock anything", !s.HasRouteLock);
        Check("consume returns true once then false", d.ConsumeRouteChoice() && !d.ConsumeRouteChoice() && !d.RouteChoiceRequested);

        // 세션 경유: 노드 재생 후 요청이 보인다
        string src = "title: T\n---\nplayer: 시작 #line:t1\n<<route_choice>>\n===\n";
        DialogueProject project = new DialogueProject();
        project.AddSource(src, "t.yarn");
        GameState s2 = Make(7);
        RomanceService svc2 = Svc(s2);
        MorpheHouse.GameFlow.DialogueSession sess = new MorpheHouse.GameFlow.DialogueSession(project, s2, svc2, new AddressBook(), "ko", null);
        sess.Start("T", null, null);
        int guard = 0;
        while (guard++ < 10 && sess.Next().Kind != StepKind.End) { }
        Check("session: route_choice command surfaces through the script", sess.ConsumeRouteChoice() && !sess.ConsumeRouteChoice());
    }

    private static bool ThrowsFormat(Action a)
    {
        try { a(); return false; }
        catch (FormatException) { return true; }
        catch (Exception) { return false; }
    }

    public static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "balance";
        FileTextSource src = new FileTextSource(dir);
        _balance = BalanceTable.Load(src, Difficulty.Normal);
        _catalog = RomanceCatalog.Load(src);
        _table = EndingTable.Load(src);

        ChapterChecks();
        RouteSelectChecks();
        RouteFlowChecks();
        EndingFlowChecks();
        ProfileChecks();
        MiscChecks();

        Console.WriteLine();
        Console.WriteLine("flow_check: {0} checks, {1} failures", _checks, _failures);
        return _failures == 0 ? 0 : 1;
    }
}
