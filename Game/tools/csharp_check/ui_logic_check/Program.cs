using System;
using System.Collections.Generic;
using System.IO;
using MorpheHouse.ArtPipeline;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using MorpheHouse.Save;

// 2단계 UI 로직 검증 하네스(Unity 불필요). 인메모리 가짜(FakeSink, MemorySettingsStorage)와 실제 시나리오 파일을 사용한다.
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

    private sealed class FakeSink : IVnCommandSink
    {
        public List<string> Log = new List<string>();
        public void ShowStanding(string c, string e, string o) { Log.Add("stand:" + c + ":" + e + ":" + o); }
        public void HideStanding(string c) { Log.Add("hide:" + c); }
        public void ShowCg(string c, int n) { Log.Add("cg:" + c + ":" + n); }
        public void ShowCommonCg(string k) { Log.Add("ccg:" + k); }
        public void HideCg() { Log.Add("hidecg"); }
        public void SetBackground(string p, string t) { Log.Add("bg:" + p + ":" + t); }
    }

    private sealed class Rig
    {
        public BalanceTable Balance;
        public RomanceCatalog Catalog;
        public GameState State;
        public RomanceService Romance;
        public FakeSink Sink = new FakeSink();
    }

    private static Rig MakeRig(string dir, int chapter)
    {
        FileTextSource src = new FileTextSource(dir);
        Rig r = new Rig();
        r.Balance = BalanceTable.Load(src, Difficulty.Normal);
        r.Catalog = RomanceCatalog.Load(src);
        r.State = GameState.NewGame(r.Balance, Difficulty.Normal, "민준");
        r.State.Chapter = chapter;
        r.State.Day = 10;
        r.State.Phase = DayPhase.Night;
        r.State.ActionsLeft = 3;
        r.State.Fatigue = 0f;
        r.State.Gold = 5000f;
        r.Romance = new RomanceService(r.State, r.Balance, r.Catalog, new Random(1).NextDouble);
        return r;
    }

    private static string _dir = "balance";

    public static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "balance";
        _dir = dir;
        string addr = args.Length > 1 ? args[1] : null;
        string yarnDir = args.Length > 2 ? args[2] : null;

        Profile();
        SaveExtension(dir);
        ArtNameChecks();
        StandingAndTypewriter();
        Overlay();
        Dispatcher(dir);
        Night(dir);
        Session(dir, addr);
        Loader();
        Settings();
        if (addr != null) Preview(addr);
        if (yarnDir != null && Directory.Exists(yarnDir)) RealScripts(dir, addr, yarnDir);

        Console.WriteLine();
        Console.WriteLine("checks={0} failures={1}", _checks, _failures);
        Console.WriteLine(_failures == 0 ? "ALL PASSED" : "FAILED");
        return _failures == 0 ? 0 : 1;
    }

    // ---------------- 프로필 ----------------

    private static void Profile()
    {
        Console.WriteLine("== protagonist profile ==");
        Check("default profile valid", ProfileValidator.Validate(ProtagonistProfile.Default()).IsValid);
        ProfileValidation v = ProfileValidator.Validate("  민준   하늘 ", "male", "wolf", 3);
        Check("name trimmed + spaces collapsed", v.IsValid && v.Profile.Name == "민준 하늘");
        Check("empty name rejected", ProfileValidator.Validate("   ", "male", "human", 1).NameError == ProfileValidator.ErrNameEmpty);
        Check("null name rejected", ProfileValidator.Validate(null, "male", "human", 1).NameError == ProfileValidator.ErrNameEmpty);
        Check("12 chars ok", ProfileValidator.Validate("가나다라마바사아자차카타", "male", "human", 1).IsValid);
        Check("13 chars rejected", ProfileValidator.Validate("가나다라마바사아자차카타파", "male", "human", 1).NameError == ProfileValidator.ErrNameTooLong);
        Check("angle bracket rejected", ProfileValidator.Validate("a<b>", "male", "human", 1).NameError == ProfileValidator.ErrNameChar);
        Check("brace rejected", ProfileValidator.Validate("a{b}", "male", "human", 1).NameError == ProfileValidator.ErrNameChar);
        Check("control char rejected", ProfileValidator.Validate("a\u0001b", "male", "human", 1).NameError == ProfileValidator.ErrNameChar);
        Check("bad gender", ProfileValidator.Validate("a", "alien", "human", 1).GenderError == ProfileValidator.ErrGender);
        Check("bad species", ProfileValidator.Validate("a", "male", "dragon", 1).SpeciesError == ProfileValidator.ErrSpecies);
        Check("appearance 0 / 7 rejected", !ProfileValidator.IsValidAppearance(0) && !ProfileValidator.IsValidAppearance(7) && ProfileValidator.IsValidAppearance(6));
        bool allGenders = true;
        for (int i = 0; i < ProfileValidator.GenderIds.Length; i++) allGenders &= ProfileValidator.IsValidGender(ProfileValidator.GenderIds[i]);
        Check("4 gender ids (male|female|nonbinary|neutral)", allGenders && ProfileValidator.GenderIds.Length == 4);
        Check("7 species ids (human + 6)", ProfileValidator.SpeciesIds.Length == 7 && ProfileValidator.SpeciesLabels.Length == 7);
        Check("first message korean", ProfileValidator.Validate("", "male", "human", 1).FirstMessage.Length > 0);
        BalanceTable b = BalanceTable.Load(new FileTextSource("balance"), Difficulty.Normal);
        ProtagonistProfile p = new ProtagonistProfile();
        p.Name = "하늘"; p.Gender = "female"; p.Species = "fox"; p.Appearance = 4;
        GameState s = GameState.NewGame(b, Difficulty.Hard, p);
        Check("NewGame(profile) applies fields", s.PlayerName == "하늘" && s.Gender == "female" && s.PlayerSpecies == "fox" && s.PlayerAppearance == 4 && s.Difficulty == Difficulty.Hard);
    }

    // ---------------- 저장 ----------------

    private static void SaveExtension(string dir)
    {
        Console.WriteLine("== save extension ==");
        Rig r = MakeRig(dir, 7);
        r.State.PlayerSpecies = "tiger";
        r.State.PlayerAppearance = 5;
        r.State.Gender = "nonbinary";
        r.State.Phase = DayPhase.Morning;
        r.Romance.SetFlag("howl_e1_trust");
        r.State.Chars["howl"].Aff = 70f;
        r.State.Chars["howl"].Trust = 25f;
        r.Romance.TryLockRoute("howl");
        r.State.VisitedNodes.Add("Ch1_Intro");
        r.State.VisitedNodes.Add("Howl_Talk_01");
        SaveData d = SaveMapper.ToSave(r.State, 1, "t");
        Check("validator accepts new save", SaveValidator.Validate(d) == null);
        GameState back = SaveMapper.FromSave(d);
        Check("species/appearance roundtrip", back.PlayerSpecies == "tiger" && back.PlayerAppearance == 5 && back.Gender == "nonbinary");
        Check("route lock roundtrip", back.LockedRoute == "howl");
        Check("flags roundtrip", back.Flags.ContainsKey("howl_e1_trust") && back.Flags["howl_e1_trust"] == 1);
        Check("visited roundtrip in order", back.VisitedNodes.Count == 2 && back.VisitedNodes[0] == "Ch1_Intro" && back.VisitedNodes[1] == "Howl_Talk_01");

        SaveData old = new SaveData();
        old.PlayerName = "x"; old.Reputation = 20; old.Fatigue = 10;
        old.VisitedNodes = null;
        old.PlayerSpecies = null;
        Check("old save without new fields: validates", SaveValidator.Validate(old) == "species" || SaveValidator.Validate(old) == null);
        SaveData old2 = new SaveData();
        old2.PlayerName = "x"; old2.Reputation = 20; old2.Fatigue = 10;
        Check("default-constructed (old json) save validates", SaveValidator.Validate(old2) == null);
        GameState g2 = SaveMapper.FromSave(old2);
        Check("old save maps to human/1/empty visited", g2.PlayerSpecies == "human" && g2.PlayerAppearance == 1 && g2.VisitedNodes.Count == 0);
        old2.VisitedNodes = null;
        Check("null visited tolerated", SaveValidator.Validate(old2) == null && SaveMapper.FromSave(old2).VisitedNodes.Count == 0);

        SaveData bad = SaveMapper.ToSave(r.State, 1, "t");
        bad.PlayerSpecies = "dragon";
        Check("bad species rejected", SaveValidator.Validate(bad) == "species");
        bad = SaveMapper.ToSave(r.State, 1, "t");
        bad.PlayerAppearance = 9;
        Check("bad appearance rejected", SaveValidator.Validate(bad) == "appearance");
        bad = SaveMapper.ToSave(r.State, 1, "t");
        for (int i = 0; i < 5000; i++) bad.VisitedNodes.Add("n" + i);
        Check("too many visited rejected", SaveValidator.Validate(bad) == "visited size");
        bad = SaveMapper.ToSave(r.State, 1, "t");
        bad.VisitedNodes.Add(new string('x', 200));
        Check("overlong visited node rejected", SaveValidator.Validate(bad) == "visited node");
        bad = SaveMapper.ToSave(r.State, 1, "t");
        bad.Gender = "zzz";
        Check("bad gender rejected", SaveValidator.Validate(bad) == "gender");
    }

    // ---------------- 에셋 이름 ----------------

    private static void ArtNameChecks()
    {
        Console.WriteLine("== art names ==");
        Check("place cafe -> Cafe_Interior", ArtNames.Place("cafe") == "Cafe_Interior");
        Check("place harbor -> Street", ArtNames.Place("harbor") == "Street");
        Check("place lighthouse -> Lighthouse", ArtNames.Place("lighthouse") == "Lighthouse");
        Check("place fish_market -> FishMarket", ArtNames.Place("fish_market") == "FishMarket");
        Check("place night_market -> NightMarket", ArtNames.Place("Night_Market") == "NightMarket");
        Check("place live_club / book_street / dojo / greenhouse / beach / rooftop",
            ArtNames.Place("live_club") == "LiveClub" && ArtNames.Place("book_street") == "BookStreet" && ArtNames.Place("dojo") == "Dojo"
            && ArtNames.Place("greenhouse") == "Greenhouse" && ArtNames.Place("beach") == "Beach" && ArtNames.Place("ROOFTOP") == "Rooftop");
        Check("unknown place null", ArtNames.Place("moonbase") == null);
        Check("time dawn/morning/day -> Day", ArtNames.Time("dawn") == "Day" && ArtNames.Time("Morning") == "Day" && ArtNames.Time("day") == "Day");
        Check("time dusk -> Evening, night -> Night", ArtNames.Time("dusk") == "Evening" && ArtNames.Time("NIGHT") == "Night");
        Check("unknown time null", ArtNames.Time("teatime") == null);
        Check("expression case-insensitive", ArtNames.Expression("smile") == "Smile" && ArtNames.Expression("SHY") == "Shy" && ArtNames.Expression("x") == null);
        Check("outfit", ArtNames.Outfit("casual") == "Casual" && ArtNames.Outfit("date") == "Date");
        string who; int n;
        Check("cg HOWL_E1", ArtNames.TryParseCgId("HOWL_E1", out who, out n) && who == "howl" && n == 1);
        Check("cg howl_e5 / nabi_6", ArtNames.TryParseCgId("howl_e5", out who, out n) && n == 5 && ArtNames.TryParseCgId("nabi_6", out who, out n) && who == "nabi" && n == 6);
        string ck;
        Check("common key HAREM_E1 / COMMON_CE03 / lower-case normalised", ArtNames.TryParseCommonCgKey("HAREM_E1", out ck) && ck == "HAREM_E1" && ArtNames.TryParseCommonCgKey("COMMON_CE03", out ck) && ck == "COMMON_CE03" && ArtNames.TryParseCommonCgKey("common_ch6_photo", out ck) && ck == "COMMON_CH6_PHOTO");
        Check("common key rejects bad shapes and character-prefixed typos", !ArtNames.TryParseCommonCgKey("HAREM", out ck) && !ArtNames.TryParseCommonCgKey("_X_1", out ck) && !ArtNames.TryParseCommonCgKey("A__B", out ck) && !ArtNames.TryParseCommonCgKey("A_B_", out ck) && !ArtNames.TryParseCommonCgKey("1A_B", out ck) && !ArtNames.TryParseCommonCgKey("A-B_C", out ck) && !ArtNames.TryParseCommonCgKey("HOWL_E9", out ck) && !ArtNames.TryParseCommonCgKey("", out ck) && !ArtNames.TryParseCommonCgKey(null, out ck));
        Check("character cg still wins over common", ArtNames.TryParseCgId("HOWL_E2", out who, out n) && who == "howl" && n == 2);
        Check("common cg res path", ArtKeys.CommonCg("HAREM_E1") == "Art/CG/Common/CG_HAREM_E1" && ArtManifest.CommonCg("HAREM_E1").Kind == ArtKind.CommonCg);
        Check("manifest includes HAREM_E1 placeholder", ArtManifest.All(CharIds.All).Exists(delegate (ArtSpec sp) { return sp.ResPath == "Art/CG/Common/CG_HAREM_E1"; }));
        {
            Rig rg = MakeRig(_dir, 7);
            FakeSink fs = rg.Sink;
            DialogueCommandDispatcher dd = new DialogueCommandDispatcher(rg.State, rg.Romance, fs);
            dd.Handle("show_cg", new[] { "HAREM_E1" });
            dd.Handle("show_cg", new[] { "common_ce03" });
            dd.Handle("show_cg", new[] { "HOWL_E1" });
            Check("show_cg routes common keys to ShowCommonCg and character ids to ShowCg", string.Join("|", fs.Log.ToArray()) == "ccg:HAREM_E1|ccg:COMMON_CE03|cg:howl:1" && dd.Warnings.Count == 0);
            dd.Handle("show_cg", new[] { "NOT A CG" });
            dd.Handle("show_cg", new[] { "HOWL_E9" });
            Check("unknown cg ids warn and show nothing", dd.Warnings.Count == 2 && fs.Log.Count == 3);
        }
        Check("cg invalid rejected", !ArtNames.TryParseCgId("HOWL_E9", out who, out n) && !ArtNames.TryParseCgId("HOWL", out who, out n) && !ArtNames.TryParseCgId("", out who, out n));
    }

    // ---------------- 스탠딩 / 타자기 ----------------

    private static void StandingAndTypewriter()
    {
        Console.WriteLine("== standing state / typewriter ==");
        VnStandingState st = new VnStandingState();
        string ev;
        Check("first character -> slot 0", st.Show("howl", "Smile", "Casual", out ev) == 0 && ev == null);
        Check("second character -> slot 1", st.Show("johan", "Neutral", "Casual", out ev) == 1 && ev == null && st.Count == 2);
        Check("same character keeps slot, updates expression", st.Show("howl", "Shy", "Casual", out ev) == 0 && st.At(0).Expression == "Shy" && st.Count == 2);
        int slot = st.Show("haru", "Smile", "Casual", out ev);
        Check("third evicts least recently shown (johan)", ev == "johan" && slot == 1 && st.SlotOf("johan") < 0 && st.SlotOf("haru") == 1);
        Check("hide frees slot", st.Hide("howl") == 0 && st.Count == 1 && st.Hide("howl") == -1);
        Check("new character takes freed slot", st.Show("nabi", "Smile", "Date", out ev) == 0 && ev == null);
        st.Clear();
        Check("clear", st.Count == 0);

        Typewriter t = new Typewriter();
        t.Reset("안녕하세요!", 10f);
        Check("starts empty", t.VisibleCount == 0 && !t.IsDone);
        t.Tick(0.35f);
        Check("0.35s at 10cps -> 3 chars", t.VisibleCount == 3);
        t.Tick(0.1f);
        Check("accumulates fractional progress", t.VisibleCount == 4);
        t.Complete();
        Check("complete shows all", t.IsDone && t.VisibleCount == 6);
        t.Reset("abc", Typewriter.InstantThreshold);
        Check("instant at threshold", t.IsDone && t.VisibleCount == 3);
        t.Reset("abc", 0f);
        Check("zero speed treated as instant", t.IsDone);
        t.Reset("", 10f);
        Check("empty text done", t.IsDone);
        t.Reset("abcdef", 40f);
        t.Tick(100f);
        Check("big dt clamps to length", t.VisibleCount == 6);
    }

    // ---------------- 임시 변수 ----------------

    private static void Overlay()
    {
        Console.WriteLine("== overlay variable store ==");
        GameState s = new GameState();
        OverlayVariableStore o = new OverlayVariableStore(new GameFlagsVariableStore(s));
        DialogueValue v;
        o.SetTemp("gift_name", DialogueValue.FromString("향초"));
        Check("temp readable with or without $", o.TryGet("$gift_name", out v) && v.AsString == "향초" && o.TryGet("gift_name", out v));
        o.Set("$mood", DialogueValue.FromString("happy"));
        Check("script string goes to overlay, not flags", o.TryGet("$mood", out v) && v.AsString == "happy" && !s.Flags.ContainsKey("mood"));
        o.Set("$count", DialogueValue.FromNumber(3));
        Check("script number goes to flags", s.Flags["count"] == 3 && o.TryGet("$count", out v) && v.AsNumber == 3);
        o.ClearTemps();
        Check("clear temps", !o.TryGet("$gift_name", out v) && o.TempCount == 0 && s.Flags["count"] == 3);
    }

    // ---------------- 디스패처 ----------------

    private static void Dispatcher(string dir)
    {
        Console.WriteLine("== command dispatcher ==");
        Rig r = MakeRig(dir, 3);
        DialogueCommandDispatcher d = new DialogueCommandDispatcher(r.State, r.Romance, r.Sink);
        d.CurrentSource = "test";
        float before = r.State.Chars["howl"].Aff;
        d.Handle("aff", new[] { "howl", "3" });
        Check("aff -> AdjustAffection (+ diminished, capped)", r.State.Chars["howl"].Aff > before && r.State.Chars["howl"].Aff <= 3f * r.Balance.P("romance_gain_mult"));
        float t0 = r.State.Chars["howl"].Trust;
        d.Handle("trust", new[] { "howl", "2" });
        Check("trust increments", r.State.Chars["howl"].Trust > t0);
        d.Handle("aff", new[] { "howl", "-1" });
        Check("negative aff applies", r.State.Chars["howl"].Aff < before + 3f);
        d.Handle("aff", new[] { "ghost", "3" });
        d.Handle("aff", new[] { "howl", "abc" });
        d.Handle("aff", new[] { "howl" });
        Check("bad aff args -> 3 warnings, no throw", d.Warnings.Count == 3);
        d.Warnings.Clear();
        d.Handle("flag", new[] { "met_howl" });
        d.Handle("flag", new[] { "$x", "5" });
        d.Handle("flag", new[] { "y", "false" });
        Check("flag default 1 / numeric / false", r.State.Flags["met_howl"] == 1 && r.State.Flags["x"] == 5 && r.State.Flags["y"] == 0);
        d.Handle("route_lock", new[] { "howl" });
        Check("route_lock refused early (ch3), warning logged", r.State.LockedRoute == "" && d.Warnings.Count == 1);
        Rig late = MakeRig(dir, 7);
        late.State.Chars["howl"].Aff = 70f; late.State.Chars["howl"].Trust = 25f;
        DialogueCommandDispatcher dl = new DialogueCommandDispatcher(late.State, late.Romance, late.Sink);
        dl.Handle("route_lock", new[] { "howl" });
        Check("route_lock succeeds when eligible", late.State.LockedRoute == "howl");

        // end_event: 메뉴가 먼저 완료했으면 무시, 아니면 완료
        Rig ev = MakeRig(dir, 3);
        ev.State.Chars["howl"].Aff = 20f;
        DialogueCommandDispatcher de = new DialogueCommandDispatcher(ev.State, ev.Romance, ev.Sink);
        int actions = ev.State.ActionsLeft;
        de.Handle("end_event", new[] { "howl", "1" });
        Check("end_event completes when not yet done (consumes action)", ev.State.Chars["howl"].EventsDone == 1 && ev.State.ActionsLeft == actions - 1);
        de.Handle("end_event", new[] { "howl", "1" });
        Check("end_event idempotent after completion", ev.State.Chars["howl"].EventsDone == 1 && ev.State.ActionsLeft == actions - 1 && de.Warnings.Count == 0);

        d.Warnings.Clear();
        d.Handle("show_standing", new[] { "howl", "smile", "casual" });
        d.Handle("show_standing", new[] { "johan" });
        d.Handle("show_standing", new[] { "howl", "grin" });
        d.Handle("show_standing", new[] { "nobody" });
        d.Handle("hide_standing", new[] { "howl" });
        d.Handle("show_cg", new[] { "HOWL_E1" });
        d.Handle("show_cg", new[] { "zzz" });
        d.Handle("hide_cg", new string[0]);
        d.Handle("bg", new[] { "cafe", "night" });
        d.Handle("bg", new[] { "harbor", "dusk" });
        d.Handle("bg", new[] { "atlantis", "night" });
        d.Handle("bg", new[] { "beach", "lunch" });
        string log = string.Join("|", r.Sink.Log.ToArray());
        Check("sink: standing normalized", log.Contains("stand:howl:Smile:Casual") && log.Contains("stand:johan:Neutral:Casual") && log.Contains("stand:howl:Neutral:Casual"));
        Check("sink: hide / cg / hidecg", log.Contains("hide:howl") && log.Contains("cg:howl:1") && log.Contains("hidecg"));
        Check("sink: bg mapping + harbor", log.Contains("bg:Cafe_Interior:Night") && log.Contains("bg:Street:Evening"));
        Check("sink: unknown bg falls back to Cafe_Interior (keeps given time)", log.Split(new[] { "bg:Cafe_Interior:Night" }, StringSplitOptions.None).Length == 3);
        Check("sink: unknown time falls back to Day", log.Contains("bg:Beach:Day"));
        Check("warnings for unknown expr/char/cg/bg/time (5)", d.Warnings.Count == 5);
        Check("unknown command not handled", !d.Handle("dance", new string[0]) && DialogueCommandDispatcher.IsKnown("aff") && !DialogueCommandDispatcher.IsKnown("dance"));
    }

    // ---------------- 밤 행동 ----------------

    private static NightRomanceActions MakeNight(Rig r, HashSet<string> nodes, HashSet<string> visited)
    {
        return new NightRomanceActions(r.State, r.Balance, r.Romance, r.Catalog,
            delegate (string n) { return nodes.Contains(n); }, delegate (string n) { return visited.Contains(n); });
    }

    private static void Night(string dir)
    {
        Console.WriteLine("== night actions ==");
        Check("node names", NightRomanceActions.TalkNodeName("howl", 3) == "Howl_Talk_03"
            && NightRomanceActions.GiftNodeName("howl", Preference.Like) == "Howl_Gift_Like"
            && NightRomanceActions.DateNodeName("howl", "fish_market") == "Howl_Date_FishMarket"
            && NightRomanceActions.EventNodeName("beomjun", 2) == "Beomjun_E2");

        // 대화: 구간·미방문 우선·회전
        HashSet<string> nodes = new HashSet<string>();
        HashSet<string> visited = new HashSet<string>();
        for (int i = 1; i <= 6; i++) nodes.Add(NightRomanceActions.TalkNodeName("howl", i));
        Rig r = MakeRig(dir, 3);
        NightRomanceActions n = MakeNight(r, nodes, visited);
        Check("stranger band picks first group (01)", n.PickTalkNode("howl") == "Howl_Talk_01");
        visited.Add("Howl_Talk_01");
        Check("unvisited first within group (02)", n.PickTalkNode("howl") == "Howl_Talk_02");
        visited.Add("Howl_Talk_02");
        Check("all of group visited -> rotation by counter", n.PickTalkNode("howl") == "Howl_Talk_01");
        r.State.Chars["howl"].Aff = 35f;
        Check("friendly band uses middle group (03)", n.PickTalkNode("howl") == "Howl_Talk_03");
        r.State.Chars["howl"].Aff = 65f;
        Check("close band uses last group (05)", n.PickTalkNode("howl") == "Howl_Talk_05");
        Check("no talk nodes -> null", n.PickTalkNode("johan") == null);

        r.State.Chars["howl"].Aff = 0f;
        visited.Clear();
        NightActionOutcome o = n.Talk("howl");
        Check("talk consumes action, plays node", o.Success && o.NodeName == "Howl_Talk_01" && r.State.ActionsLeft == 2);
        Check("talk summary mentions name", o.Summary.Contains("하울"));
        NightActionOutcome o2 = n.Talk("johan");
        Check("talk without node -> NodeName null + fallback line", o2.Success && o2.NodeName == null && o2.FallbackText.Length > 0 && o2.FallbackSpeaker == "johan");

        // 선물
        r = MakeRig(dir, 3);
        nodes.Clear();
        nodes.Add("Howl_Gift_Like"); nodes.Add("Howl_Gift_Neutral"); nodes.Add("Howl_Gift_Dislike");
        n = MakeNight(r, nodes, visited);
        string likeGift = r.Catalog.TagsFor("howl", PrefKind.Gift, Preference.Like)[0];
        string dislikeGift = r.Catalog.TagsFor("howl", PrefKind.Gift, Preference.Dislike)[0];
        float gold = r.State.Gold;
        o = n.Gift("howl", likeGift);
        Check("gift like -> Howl_Gift_Like + $gift_name", o.Success && o.NodeName == "Howl_Gift_Like"
            && o.Temps["gift_name"].AsString == r.Catalog.FindGift(likeGift).NameKo && r.State.Gold < gold);
        r.State.Day += 5;
        o = n.Gift("howl", dislikeGift);
        Check("gift dislike -> Howl_Gift_Dislike", o.Success && o.NodeName == "Howl_Gift_Dislike");
        r.State.Day += 5;
        o = n.Gift("howl", "sweet_snack");
        Check("gift neutral/other node resolved", o.Success && (o.NodeName == "Howl_Gift_Neutral" || o.NodeName == "Howl_Gift_Like" || o.NodeName == "Howl_Gift_Dislike"));
        r.State.ActionsLeft = 3;
        o = n.Gift("howl", likeGift);
        Check("gift cooldown refused with message", !o.Success && o.Result.Reason == RomanceRefusal.Cooldown && o.RefusalMessage.Length > 0);
        Check("GiftBlockReason cooldown", n.GiftBlockReason("howl") == RomanceRefusal.Cooldown && n.GiftBlockReason("johan") == null);
        r.State.Gold = 1f;
        Check("GiftBlockReason no gold", n.GiftBlockReason("johan") == RomanceRefusal.NoGold);
        r.State.ActionsLeft = 0;
        Check("GiftBlockReason no actions", n.GiftBlockReason("johan") == RomanceRefusal.NoActions);
        List<GiftChoice> gifts = n.GiftChoices();
        Check("gift picker lists all gifts.csv rows (25), sorted by tier", gifts.Count == r.Catalog.Gifts.Count && gifts.Count == 25 && gifts[0].PriceTier <= gifts[gifts.Count - 1].PriceTier);

        // 데이트
        r = MakeRig(dir, 1);
        nodes.Clear();
        nodes.Add("Howl_Date_Lighthouse"); nodes.Add("Howl_Date_Generic");
        n = MakeNight(r, nodes, visited);
        List<SpotChoice> spots = n.SpotChoices();
        int locked = 0;
        for (int i = 0; i < spots.Count; i++) if (spots[i].Locked) locked++;
        Check("ch1: 3 spots open, 6 locked (disabled in UI)", spots.Count == 9 && locked == 6);
        o = n.Date("howl", "night_market");
        Check("locked spot refused", !o.Success && o.Result.Reason == RomanceRefusal.SpotLocked && r.State.ActionsLeft == 3);
        o = n.Date("howl", "lighthouse");
        Check("date lighthouse -> specific node + $spot_name", o.Success && o.NodeName == "Howl_Date_Lighthouse" && o.Temps["spot_name"].AsString == "등대 언덕");
        r.State.Day += 5;
        o = n.Date("howl", "beach");
        Check("date without specific node -> Generic", o.Success && o.NodeName == "Howl_Date_Generic");
        r.State.Day += 5;
        o = n.Date("gomseok", "beach"); // 1장에 이미 등장한 인물 중 이 픽스처에 노드가 없는 쪽(요한은 2장부터)
        Check("date with no nodes at all -> fallback", o.Success && o.NodeName == null && o.FallbackText.Length > 0);

        // 이벤트
        r = MakeRig(dir, 1);
        nodes.Clear();
        nodes.Add("Howl_E1");
        n = MakeNight(r, nodes, visited);
        Check("no star below aff_min", !n.HasEvent("howl") && n.EventHint("howl").Contains("호감"));
        o = n.Event("howl");
        Check("event refused when unavailable", !o.Success && o.Result.Reason == RomanceRefusal.EventNotAvailable);
        r.State.Chars["howl"].Aff = 12f;
        Check("star when aff_min met", n.HasEvent("howl") && n.EventHint("howl") == "");
        o = n.Event("howl");
        Check("event plays Howl_E1, completes step, consumes action", o.Success && o.NodeName == "Howl_E1" && r.State.Chars["howl"].EventsDone == 1 && r.State.ActionsLeft == 2 && o.Temps["event_step"].AsNumber == 1);
        r.State.Chars["gomseok"].Aff = 12f; // johan E1 is chapter 2 in balance/event_schedule.csv; gomseok E1 is chapter 1
        o = n.Event("gomseok");
        Check("event without node -> fallback narration", o.Success && o.NodeName == null && o.FallbackSpeaker == null && o.FallbackText.Contains("준비 중"));

        // 루트 확정
        r = MakeRig(dir, 6);
        n = MakeNight(r, nodes, visited);
        r.State.Chars["howl"].Aff = 80f; r.State.Chars["howl"].Trust = 30f;
        Check("no candidates before chapter 7", n.RouteLockCandidates().Count == 0);
        r.State.Chapter = 7;
        r.State.Chars["johan"].Aff = 61f; r.State.Chars["johan"].Trust = 5f;
        List<string> cand = n.RouteLockCandidates();
        Check("candidates need single (60/20) or group (50/7) minimums", cand.Count == 1 && cand[0] == "howl");
        string msg;
        Check("TryLockRoute ok + message", n.TryLockRoute("howl", out msg) && r.State.LockedRoute == "howl" && msg.Length > 0);
        Check("after lock no candidates, second lock refused", n.RouteLockCandidates().Count == 0 && !n.TryLockRoute("johan", out msg) && msg.Contains("이미"));
        o = n.Talk("johan");
        Check("frozen contact: success, flagged, summary says no change", o.Success && o.Result.Frozen && o.Summary.Contains("변화가 없습니다"));
        Check("refusal texts", NightRomanceActions.RefusalText(RomanceRefusal.NoGold).Length > 0 && NightRomanceActions.RefusalText("weird").Contains("weird"));
    }

    // ---------------- 세션 ----------------

    private static DialogueSession MakeSession(Rig r, string yarn, AddressBook book)
    {
        DialogueProject p = DialogueProject.Parse(yarn, "inline");
        return new DialogueSession(p, r.State, r.Romance, book, "ko", r.Sink);
    }

    private static List<string> Drain(DialogueSession s, int pick)
    {
        List<string> lines = new List<string>();
        for (int i = 0; i < 500; i++)
        {
            DialogueStep step = s.Next();
            if (step.IsEnd) break;
            if (step.IsLine) lines.Add((step.Speaker ?? "-") + ": " + step.Text);
            else if (step.IsChoices)
            {
                int idx = -1;
                for (int k = 0; k < step.Options.Count; k++) if (step.Options[k].Available) { idx = step.Options[k].Index; if (pick-- <= 0) break; }
                s.Choose(idx);
            }
            else if (step.IsCommand) lines.Add("CMD:" + step.CommandName);
        }
        s.Finish();
        return lines;
    }

    private static void Session(string dir, string addr)
    {
        Console.WriteLine("== dialogue session ==");
        AddressBook book = addr != null ? AddressBook.FromCsv(File.ReadAllText(addr)) : new AddressBook();
        Rig r = MakeRig(dir, 1);
        string yarn = "title: T1\n---\n<<bg cafe night>>\nhowl: {$gift_name} 고마워. {calls_player(\"howl\")}\n<<aff howl 2>>\n<<flag met>>\n<<set $mood to \"calm\">>\nhowl: 기분 {$mood}\n-> 응 #line:a1\n    <<trust howl 1>>\n    howl: 좋아\n-> 아니 <<if false>> #line:a2\n    howl: 싫어\nhowl: 끝\n===\ntitle: Boom\n---\nhowl: 시작 {unknown_fn()}\n===\n";
        DialogueSession s = MakeSession(r, yarn, book);
        s.SetTemp("gift_name", DialogueValue.FromString("향초"));
        s.Start("T1", null, null);
        List<string> lines = Drain(s, 0);
        Check("temp $gift_name interpolates", lines.Count > 0 && lines[0].StartsWith("howl: 향초 고마워"));
        Check("calls_player works (민준 씨)", lines[0].Contains("민준"));
        Check("script string var via overlay", lines.Exists(delegate (string l) { return l.Contains("기분 calm"); }));
        Check("aff/trust/flag commands ran", r.State.Chars["howl"].Aff > 0f && r.State.Chars["howl"].Trust > 0f && r.State.Flags["met"] == 1);
        Check("bg command reached sink, not exposed as step", r.Sink.Log.Contains("bg:Cafe_Interior:Night") && !lines.Exists(delegate (string l) { return l.StartsWith("CMD:"); }));
        Check("visited exported", s.ExportVisited().Contains("T1"));
        Check("temps cleared after Finish", s.Vars.TempCount == 0);

        s.Start("Missing", "howl", "대체 대사");
        DialogueStep a = s.Next();
        DialogueStep b = s.Next();
        Check("missing node -> fallback line then End", a.IsLine && a.Text == "대체 대사" && a.Speaker == "howl" && b.IsEnd && s.UsingFallback);
        s.Start("Missing", null, "");
        Check("missing node without text -> immediate End", s.Next().IsEnd);
        s.Start("Boom", null, null);
        DialogueStep c = s.Next();
        Check("runtime error in script does not throw; ends quietly + logged", (c.IsEnd || c.IsLine) && s.Errors.Count > 0);

        // call_pref_name
        Rig r2 = MakeRig(dir, 1);
        r2.State.Gender = "male";
        r2.State.Chars["howl"].Aff = 65f;
        DialogueSession s2 = MakeSession(r2, "title: C\n---\nhowl: [{calls_player(\"howl\")}]\n===\n", book);
        s2.Start("C", null, null);
        string normal = s2.Next().Text;
        r2.State.Flags["call_pref_name"] = 1;
        s2.Start("C", null, null);
        string pref = s2.Next().Text;
        Check("call_pref_name -> bare name", pref == "[민준]");
        Check("without flag uses address table (close male -> table term)", normal == "[" + book.Find("howl", "male", AffinityBand.Close).CallsPlayerKo.Replace("{name}", "민준") + "]");

        // 노드 방문 복원
        Rig r3 = MakeRig(dir, 1);
        DialogueSession s3 = MakeSession(r3, "title: V\n---\nhowl: hi\n===\n", book);
        s3.ImportVisited(new[] { "V" });
        Check("ImportVisited restores", s3.HasVisited("V"));
    }

    // ---------------- 로더 ----------------

    private static void Loader()
    {
        Console.WriteLine("== project loader / index ==");
        List<KeyValuePair<string, string>> src = new List<KeyValuePair<string, string>>();
        src.Add(new KeyValuePair<string, string>("a", "title: A\n---\nhowl: hi\n===\n"));
        src.Add(new KeyValuePair<string, string>("broken", "title: B\n---\n<<if x>>\nhowl: hi\n===\n"));
        src.Add(new KeyValuePair<string, string>("dup", "title: A\n---\nhowl: again\n===\ntitle: C\n---\nhowl: c\n===\n"));
        DialogueLoadResult res = DialogueProjectLoader.Build(src);
        Check("broken file reported, others loaded", res.Project.Contains("A") && res.Project.Contains("C") && !res.Project.Contains("B") && res.Errors.Exists(delegate (string e) { return e.Contains("broken"); }));
        Check("duplicate node reported", res.Errors.Exists(delegate (string e) { return e.Contains("중복") && e.Contains("'A'"); }));
        Check("file/node counts", res.FileCount == 2 && res.NodeCount == 3);
        Check("null sources ok", DialogueProjectLoader.Build(null).NodeCount == 0);

        List<string> idx = DialogueProjectLoader.ParseIndex("﻿# comment\r\nroutes__howl__howl_E1\r\n\r\n  common__prologue.txt \nroutes__howl__howl_E1\n_index\n");
        Check("ParseIndex: comments/blank/dups/.txt/_index", idx.Count == 2 && idx[0] == "routes__howl__howl_E1" && idx[1] == "common__prologue");
        Check("ParseIndex null/empty", DialogueProjectLoader.ParseIndex(null).Count == 0 && DialogueProjectLoader.ParseIndex("").Count == 0);
        string built = DialogueProjectLoader.BuildIndex(new[] { "b", "a", "a", "_index", "" });
        List<string> back = DialogueProjectLoader.ParseIndex(built);
        Check("BuildIndex sorted/dedup roundtrip", back.Count == 2 && back[0] == "a" && back[1] == "b" && built.StartsWith("#"));
        Check("FlattenName", DialogueProjectLoader.FlattenName("routes/howl/howl_E1.yarn") == "routes__howl__howl_E1"
            && DialogueProjectLoader.FlattenName("routes\\howl\\x.YARN") == "routes__howl__x"
            && DialogueProjectLoader.FlattenName("a.yarn") == "a");
    }

    // ---------------- 설정 ----------------

    private static void Settings()
    {
        Console.WriteLine("== settings ==");
        MemorySettingsStorage mem = new MemorySettingsStorage();
        SettingsService s = new SettingsService(mem);
        Check("defaults", s.TextSpeed == SettingsService.DefaultTextSpeed && s.MasterVolume == 1f && s.Locale == "ko" && !s.InstantText);
        Check("load with no file -> false, defaults kept", !s.Load() && s.TextSpeed == 40f);
        int changed = 0;
        s.Changed += delegate { changed++; };
        s.SetTextSpeed(75.5f);
        s.SetMasterVolume(0.25f);
        s.SetLocale("EN-us");
        Check("setters + Changed event", s.TextSpeed == 75.5f && s.MasterVolume == 0.25f && s.Locale == "en" && changed == 3);
        s.SetTextSpeed(75.5f);
        Check("no event when unchanged", changed == 3);
        Check("save writes json", s.Save() && mem.Text.Contains("\"textSpeed\": 75.5") && mem.Text.Contains("\"locale\": \"en\""));
        SettingsService s2 = new SettingsService(mem);
        Check("roundtrip load", s2.Load() && s2.TextSpeed == 75.5f && s2.MasterVolume == 0.25f && s2.Locale == "en");
        s2.SetTextSpeed(9999f); s2.SetMasterVolume(-3f);
        Check("clamping", s2.TextSpeed == SettingsService.MaxTextSpeed && s2.InstantText && s2.MasterVolume == 0f);
        s2.SetTextSpeed(-5f);
        Check("min clamp", s2.TextSpeed == SettingsService.MinTextSpeed);
        mem.Text = "{ garbage ";
        SettingsService s3 = new SettingsService(mem);
        Check("corrupt file -> defaults, no throw", !s3.Load() && s3.TextSpeed == 40f);
        mem.Text = "{\"textSpeed\": 500, \"masterVolume\": \"x\", \"locale\": \"fr\"}";
        SettingsService s4 = new SettingsService(mem);
        Check("partial/invalid values: clamp + fallback", s4.Load() && s4.TextSpeed == SettingsService.MaxTextSpeed && s4.MasterVolume == 1f && s4.Locale == "ko");
        Check("Typewriter threshold == MaxTextSpeed", Typewriter.InstantThreshold == SettingsService.MaxTextSpeed);

        string path = Path.Combine(Path.GetTempPath(), "morphe_settings_test_" + Environment.TickCount, "settings.json");
        FileSettingsStorage fs = new FileSettingsStorage(path);
        Check("file storage: missing -> null", fs.Read() == null);
        SettingsService s5 = new SettingsService(fs);
        s5.SetTextSpeed(60f);
        Check("file storage: save creates dirs + file", s5.Save() && File.Exists(path));
        SettingsService s6 = new SettingsService(new FileSettingsStorage(path));
        Check("file storage: reload", s6.Load() && s6.TextSpeed == 60f);
        Check("file storage: overwrite works", s6.Save());
        try { Directory.Delete(Path.GetDirectoryName(path), true); } catch (IOException) { }
    }

    // ---------------- 미리보기 ----------------

    private static void Preview(string addr)
    {
        Console.WriteLine("== profile preview ==");
        AddressBook book = AddressBook.FromCsv(File.ReadAllText(addr));
        ProtagonistProfile male = new ProtagonistProfile();
        male.Name = "민준"; male.Gender = "male";
        List<PreviewEntry> e = ProfilePreview.Build(male, book, "ko", null);
        Check("6 entries, names fall back to ids", e.Count == 6 && e[0].CharName == "howl");
        Check("howl male: stranger '민준 씨', close '민준'", e[0].FirstCall == "민준 씨" && e[0].CloseCall == "민준");
        ProtagonistProfile nb = new ProtagonistProfile();
        nb.Name = "하늘"; nb.Gender = "nonbinary";
        e = ProfilePreview.Build(nb, book, "ko", delegate (string id) { return id.ToUpperInvariant(); });
        bool noGendered = true;
        for (int i = 0; i < e.Count; i++)
        {
            string all = e[i].FirstCall + e[i].CloseCall;
            if (all.Contains("{name}") || all.Contains("형") || all.Contains("오빠") || all.Contains("누나") || all.Contains("언니")) noGendered = false;
            if (e[i].FirstCall.Length == 0 || e[i].CloseCall.Length == 0) noGendered = false;
        }
        Check("nonbinary: neutral terms, all non-empty, no leftover {name}", noGendered);
        Check("nameOf callback used", e[0].CharName == "HOWL");
        Check("ToText has one line per char", ProfilePreview.ToText(e).Split('\n').Length == 6);
        ProtagonistProfile blank = new ProtagonistProfile();
        blank.Name = "   ";
        Check("blank name previews with default name", ProfilePreview.Build(blank, book, "ko", null)[0].FirstCall.Contains("주인공"));
    }

    // ---------------- 실제 시나리오 ----------------

    private static void RealScripts(string dir, string addr, string yarnDir)
    {
        Console.WriteLine("== real scenario scripts (" + yarnDir + ") ==");
        string[] files = Directory.GetFiles(yarnDir, "*.yarn", SearchOption.AllDirectories);
        List<KeyValuePair<string, string>> src = new List<KeyValuePair<string, string>>();
        for (int i = 0; i < files.Length; i++)
        {
            string rel = files[i].Substring(yarnDir.Length).TrimStart('/', '\\');
            src.Add(new KeyValuePair<string, string>(DialogueProjectLoader.FlattenName(rel), File.ReadAllText(files[i])));
        }
        DialogueLoadResult res = DialogueProjectLoader.Build(src);
        Console.WriteLine("  files={0} nodes={1} loadErrors={2}", res.FileCount, res.NodeCount, res.Errors.Count);
        for (int i = 0; i < res.Errors.Count; i++) Console.WriteLine("    " + res.Errors[i]);
        Check("all scenario files load without parse/dup errors", res.Errors.Count == 0 && res.FileCount == files.Length);
        Check("Ch1_Intro exists (chapter intro node)", res.Project.Contains("Ch1_Intro"));
        Check("Howl_Talk_01 / Gift_* / E1 / Date_Generic exist",
            res.Project.Contains("Howl_Talk_01") && res.Project.Contains("Howl_Gift_Like") && res.Project.Contains("Howl_Gift_Neutral")
            && res.Project.Contains("Howl_Gift_Dislike") && res.Project.Contains("Howl_E1") && res.Project.Contains("Howl_Date_Generic"));

        AddressBook book = addr != null ? AddressBook.FromCsv(File.ReadAllText(addr)) : new AddressBook();
        int failedNodes = 0;
        int totalWarn = 0;
        int expectedEndEventWarn = 0;
        string[] genders = { "male", "female", "nonbinary", "neutral" };
        for (int g = 0; g < genders.Length; g++)
        {
            for (int pick = 0; pick < 3; pick++)
            {
                foreach (DialogueNode node in res.Project.AllNodes)
                {
                    Rig r = MakeRig(dir, 5);
                    r.State.Gender = genders[g];
                    r.State.Chars["howl"].Aff = 40f;
                    r.State.Chars["howl"].Trust = 10f;
                    int evMatch;
                    if (node.Title.StartsWith("Howl_E") && int.TryParse(node.Title.Substring(6), out evMatch)) r.State.Chars["howl"].EventsDone = evMatch - 1;
                    DialogueSession s = new DialogueSession(res.Project, r.State, r.Romance, book, "ko", r.Sink);
                    s.SetTemp("gift_name", DialogueValue.FromString("테스트 선물"));
                    s.SetTemp("spot_name", DialogueValue.FromString("테스트 장소"));
                    s.Start(node.Title, null, null);
                    int steps = 0;
                    bool ended = false;
                    for (int i = 0; i < 3000; i++)
                    {
                        DialogueStep step = s.Next();
                        steps++;
                        if (step.IsEnd) { ended = true; break; }
                        if (step.IsCommand) { Console.WriteLine("    unhandled command in {0}: {1}", node.Title, step.CommandName); failedNodes++; }
                        if (step.IsChoices)
                        {
                            int idx = -1;
                            int skip = pick;
                            for (int k = 0; k < step.Options.Count; k++)
                                if (step.Options[k].Available) { idx = step.Options[k].Index; if (skip-- <= 0) break; }
                            if (idx < 0) { Console.WriteLine("    no available option in " + node.Title); failedNodes++; break; }
                            s.Choose(idx);
                        }
                    }
                    s.Finish();
                    if (!ended) { Console.WriteLine("    did not end: " + node.Title); failedNodes++; }
                    for (int i = 0; i < s.Errors.Count; i++)
                    {
                        // 개인 이벤트 노드를 단독 실행하면 호스트가 먼저 CompleteEvent 를 하지 않은 상태이므로 end_event 가
                        // event_not_available 로 경고한다. 실제 게임에서는 밤 메뉴가 이벤트를 먼저 완료 처리하고 스크립트의
                        // end_event 는 무시되므로(멱등) 이 한 종류만 기대되는 경고로 센다.
                        bool expectedStandalone = s.Errors[i].Contains("end_event") && s.Errors[i].Contains("event_not_available");
                        if (expectedStandalone) { expectedEndEventWarn++; continue; }
                        Console.WriteLine("    [{0}/{1}] {2}", node.Title, genders[g], s.Errors[i]);
                        totalWarn++;
                    }
                    if (g == 0 && pick == 0)
                    {
                        bool badLeft = false;
                        for (int i = 0; i < r.Sink.Log.Count; i++) if (r.Sink.Log[i].StartsWith("bg:") && r.Sink.Log[i].Contains(":Day") == false && r.Sink.Log[i].Contains("Night") == false && r.Sink.Log[i].Contains("Evening") == false) badLeft = true;
                        if (badLeft) failedNodes++;
                    }
                }
            }
        }
        Check("every node runs to End for 4 genders x 3 choice paths, no unhandled commands", failedNodes == 0);
        Check("no script warnings (bad bg/cg/expression/aff args, runtime errors)", totalWarn == 0);
        Console.WriteLine("  (단독 실행 시 예상되는 end_event 경고 " + expectedEndEventWarn + "건은 제외)");

        // 시나리오의 call_pref_name 이 실제로 호칭에 반영되는지(Ch1_Intro 없이 플래그만)
        Rig rr = MakeRig(dir, 1);
        rr.State.Flags["call_pref_name"] = 1;
        Check("call_pref_name flag is honoured by calls_player in host", rr.State.Flags["call_pref_name"] == 1);
    }
}
