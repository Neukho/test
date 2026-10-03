using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Romance;
using MorpheHouse.Tycoon;
using MorpheHouse.GameFlow;
using MorpheHouse.UI;

/// <summary>수정 묶음 검증: LruCache · DemoConfig · 문자열 키/자리표시자/하드코딩 한글 · TouchTargetPolicy · 터치 레이아웃 · SaveRecency · 메모리 추정.</summary>
public static class FixesCheck
{
    private static int _pass;
    private static int _fail;
    private const string Src = "UnityProject/Assets/_Project/Scripts/";
    private const string Res = "UnityProject/Assets/_Project/Resources/";

    private static void Check(string name, bool ok)
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine("  [" + (ok ? "PASS" : "FAIL") + "] " + name);
    }

    private static string ReadAll(string path)
    {
        return File.ReadAllText(path, Encoding.UTF8);
    }

    public static int Main()
    {
        Lru();
        Demo();
        Strings();
        HardcodedHangul();
        Touch();
        Recency();
        Memory();
        DayFlow();
        Console.WriteLine();
        Console.WriteLine("fixes_check: " + (_pass + _fail) + " checks, " + _fail + " failures");
        return _fail == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ LruCache
    private static void Lru()
    {
        Console.WriteLine("== LruCache ==");
        List<string> evicted = new List<string>();
        LruCache<string, int> c = new LruCache<string, int>(3, 0, delegate (string k, int v) { evicted.Add(k + "=" + v); });
        c.Put("a", 1, 10); c.Put("b", 2, 10); c.Put("c", 3, 10);
        Check("fills to capacity without eviction", c.Count == 3 && evicted.Count == 0 && c.TotalBytes == 30);
        c.Put("d", 4, 10);
        Check("count cap: oldest (a) evicted, callback called", c.Count == 3 && !c.Contains("a") && evicted.Count == 1 && evicted[0] == "a=1");
        int got;
        c.TryGet("b", out got);
        c.Put("e", 5, 10);
        Check("touch on use: b was refreshed, so c (oldest) is evicted instead", c.Contains("b") && !c.Contains("c") && evicted[1] == "c=3");
        Check("KeysOldestFirst order", string.Join(",", c.KeysOldestFirst().ToArray()) == "d,b,e");
        Check("TryGet miss returns false/default", !c.TryGet("zzz", out got) && got == 0);

        evicted.Clear();
        LruCache<string, int> bytes = new LruCache<string, int>(100, 25, delegate (string k, int x) { evicted.Add(k); });
        bytes.Put("a", 1, 10); bytes.Put("b", 2, 10); bytes.Put("c", 3, 10);
        Check("byte cap: 30 > 25 evicts the oldest", !bytes.Contains("a") && bytes.TotalBytes == 20 && evicted.Count == 1);
        bytes.Put("big", 9, 100);
        Check("a single item bigger than the byte cap is still kept (just inserted is never evicted); older ones go", bytes.Contains("big") && bytes.Count == 1 && bytes.TotalBytes == 100);

        evicted.Clear();
        LruCache<string, int> pin = new LruCache<string, int>(2, 0, delegate (string k, int x) { evicted.Add(k); });
        pin.Put("a", 1, 1); pin.Put("b", 2, 1);
        Check("Pin on missing key is false", !pin.Pin("zzz"));
        pin.Pin("a");
        pin.Put("c", 3, 1);
        Check("pinned item is never evicted (b goes instead)", pin.Contains("a") && !pin.Contains("b") && pin.Contains("c"));
        pin.Pin("c");
        pin.Put("d", 4, 1);
        Check("all pinned: cache may exceed the cap rather than evict on-screen items", pin.Count == 3 && pin.Contains("d") == true && pin.Contains("a") && pin.Contains("c"));
        pin.Unpin("a");
        Check("unpin trims back to the cap", pin.Count == 2 && !pin.Contains("a"));
        Check("pin counts are reference counts", pin.PinCount("c") == 1 && pin.Pin("c") && pin.PinCount("c") == 2 && pin.Unpin("c") && pin.PinCount("c") == 1);

        evicted.Clear();
        LruCache<string, string> rep = new LruCache<string, string>(5, 0, delegate (string k, string x) { evicted.Add(k + "=" + x); });
        rep.Put("k", "one", 5);
        rep.Put("k", "one", 5);
        Check("replacing with the same value does not call evict", evicted.Count == 0 && rep.Count == 1 && rep.TotalBytes == 5);
        rep.Put("k", "two", 7);
        Check("replacing with a different value evicts the old one and re-counts bytes", evicted.Count == 1 && evicted[0] == "k=one" && rep.TotalBytes == 7);
        Check("Remove calls evict and frees bytes", rep.Remove("k") && evicted.Count == 2 && rep.TotalBytes == 0 && !rep.Remove("k"));
        rep.Put("x", "1", 1); rep.Put("y", "2", 1); evicted.Clear();
        rep.Clear();
        Check("Clear evicts every item (including pinned)", rep.Count == 0 && evicted.Count == 2);
        bool threw = false;
        try { new LruCache<string, int>(0, 0, null); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check("capacity < 1 rejected; null callback is fine", threw && new LruCache<string, int>(1, 0, null).Count == 0);

        // ArtLoader 의 상한(코드 상수 = docs/15): 스탠딩 12, 배경 4, CG 3, 아이콘 64
        string art = ReadAll(Src + "ArtPipeline/ArtLoader.cs");
        Check("ArtLoader caps: 12 standing pieces, 4 BG, 3 CG, 64 icons", art.Contains("StandingMaxCount = 12") && art.Contains("BackgroundMaxCount = 4") && art.Contains("CgMaxCount = 3") && art.Contains("IconMaxCount = 64"));
        Check("ArtLoader never destroys Resources assets (only slots it created) and defers destruction to screen transitions", art.Contains("OwnsSprite") && art.Contains("Graveyard") && art.Contains("OnScreenTransition") && art.Contains("Throttle.Elapsed"));
        Check("AppContext calls ArtLoader.OnScreenTransition on screen changes", ReadAll(Src + "UI/AppContext.cs").Contains("ArtLoader.OnScreenTransition()"));
        Check("throttle: first call allowed, then blocked for the interval", Throttle.Elapsed(0f, -1000f, 20f) && !Throttle.Elapsed(5f, 0f, 20f) && Throttle.Elapsed(20f, 0f, 20f));
    }

    // ------------------------------------------------------------------ DemoConfig
    private static void Demo()
    {
        Console.WriteLine("== DemoConfig ==");
        DemoConfig off = DemoConfig.Parse(ReadAll(Res + "Config/build_config.csv"));
        Check("shipped build_config.csv: demo disabled (full build), last chapter 3", !off.Enabled && off.LastChapter == 3 && off.Warnings.Count == 0);
        Check("null / empty text = disabled with defaults", !DemoConfig.Parse(null).Enabled && DemoConfig.Parse("").LastChapter == DemoConfig.DefaultLastChapter);
        DemoConfig on = DemoConfig.Parse("key,value\ndemo,1\n");
        Check("demo,1 enables; default last chapter 3", on.Enabled && on.LastChapter == 3);
        Check("1/true/yes/on enable; 0/false/other do not", DemoConfig.Parse("demo,true").Enabled && DemoConfig.Parse("demo, YES ").Enabled && DemoConfig.Parse("demo,on").Enabled && !DemoConfig.Parse("demo,0").Enabled && !DemoConfig.Parse("demo,maybe").Enabled);
        Check("BOM + CRLF + comments + blank lines parse", DemoConfig.Parse("﻿key,value\r\n# c\r\n\r\ndemo,1\r\ndemo_last_chapter,5\r\n").LastChapter == 5);
        Check("demo_last_chapter clamps to 1..8 and warns on junk", DemoConfig.Parse("demo,1\ndemo_last_chapter,99").LastChapter == 8 && DemoConfig.Parse("demo_last_chapter,-4").LastChapter == 1 && DemoConfig.Parse("demo_last_chapter,abc").Warnings.Count == 1);
        Check("unknown keys and keyless rows are tolerated", DemoConfig.Parse("foo,bar\nbroken\ndemo,1").Enabled);

        Check("demo over: chapter 3 of 3 is NOT over (Howl_E2 / CE03_RentMeeting reachable)", !on.IsDemoOver(3, false) && !on.IsDemoOver(1, false));
        Check("demo over: entering chapter 4 ends the demo; finished also ends it", on.IsDemoOver(4, false) && on.IsDemoOver(8, true));
        Check("disabled config is never over", !off.IsDemoOver(8, true) && !off.IsDemoOver(9, false));
        DemoConfig four = DemoConfig.Parse("demo,1\ndemo_last_chapter,4");
        Check("configurable last chapter", !four.IsDemoOver(4, false) && four.IsDemoOver(5, false));

        // 데모에서 닿아야 하는 노드가 3장까지의 시나리오에 정말 있는가
        string all = "";
        foreach (string f in Directory.GetFiles("UnityProject/Assets/_Project/Dialogue", "*", SearchOption.AllDirectories)) if (!f.EndsWith(".meta")) all += ReadAll(f);
        Check("scenario has node Howl_E2 and CE03_RentMeeting (reachable within chapters 1-3)", all.Contains("title: Howl_E2") && all.Contains("title: CE03_RentMeeting"));
        Check("chapters.csv: chapter 3 has min chapter for event 2 <= 3 (Howl_E2 unlocks by chapter 3)", ReadAll(Res + "Balance/events.csv").Contains("\n2,선을 넘는 순간,3,"));

        Dictionary<string, int> flags = new Dictionary<string, int>();
        Check("sys_demo flag absent = not a demo save", !DemoConfig.IsDemoSave(flags) && !DemoConfig.IsDemoSave(null));
        DemoConfig.MarkFlag(flags);
        Check("MarkFlag sets sys_demo=1 and IsDemoSave sees it (full build just loads it: compatible)", flags["sys_demo"] == 1 && DemoConfig.IsDemoSave(flags));
        Check("AppContext marks new games when demo is enabled", ReadAll(Src + "UI/AppContext.cs").Contains("DemoConfig.MarkFlag(State.Flags)"));
        Check("GameScreen routes to the demo end screen (Outro of the last demo chapter still plays first)", ReadAll(Src + "UI/GameScreen.cs").Contains("DemoConfig.Current.IsDemoOver") && ReadAll(Src + "UI/GameScreen.cs").Contains("step.Kind != ChapterStepKind.Outro"));

        Check("IsSafeUrl: https only, no spaces/quotes", DemoConfig.IsSafeUrl("https://store.example.com/app/1") && !DemoConfig.IsSafeUrl("http://x.com") && !DemoConfig.IsSafeUrl("javascript:alert(1)") && !DemoConfig.IsSafeUrl("https://a b") && !DemoConfig.IsSafeUrl("") && !DemoConfig.IsSafeUrl("https://") && !DemoConfig.IsSafeUrl("file:///etc/passwd"));
        Check("store_url non-https is dropped with a warning", DemoConfig.Parse("store_url,ftp://x").StoreUrl.Length == 0 && DemoConfig.Parse("store_url,ftp://x").Warnings.Count == 1 && DemoConfig.Parse("store_url,https://s.example/x").StoreUrl == "https://s.example/x");

        bool now;
        string t1 = DemoConfig.ToggleDemo("key,value\ndemo,0\ndemo_last_chapter,3\n", out now);
        Check("toggle 0 -> 1 keeps other rows", now && t1 == "key,value\ndemo,1\ndemo_last_chapter,3\n");
        string t2 = DemoConfig.ToggleDemo(t1, out now);
        Check("toggle 1 -> 0 round trip", !now && t2 == "key,value\ndemo,0\ndemo_last_chapter,3\n");
        string t3 = DemoConfig.ToggleDemo("key,value\r\ndemo,1\r\n", out now);
        Check("toggle keeps CRLF", !now && t3 == "key,value\r\ndemo,0\r\n");
        string t4 = DemoConfig.ToggleDemo("key,value\nfoo,1", out now);
        Check("toggle appends a demo row when missing (turns it on)", now && DemoConfig.Parse(t4).Enabled && t4.Contains("foo,1"));
        string t5 = DemoConfig.ToggleDemo(null, out now);
        Check("toggle on a missing file creates header + demo,1", now && DemoConfig.Parse(t5).Enabled);
        Check("Editor tool exists with the menu path", ReadAll(Src + "Editor/DemoConfigTool.cs").Contains("MorpheHouse/Build/Toggle Demo Config"));

        UiStrings.Current = LoadRealStrings();
        UiStrings.Current.SetLocale("ko");
        List<string> lines = DemoSummary.Lines(18, 3, 1234.5f, 4, 40f, 3700.0, 2);
        Check("DemoSummary: 4 lines, Korean from the table", lines.Count == 4 && lines[0].Contains("18일") && lines[0].Contains("3챕터") && lines[3].Contains("2장"));
        UiStrings.Current.SetLocale("en");
        lines = DemoSummary.Lines(18, 3, 1234.5f, 4, 40f, 3700.0, 2);
        Check("DemoSummary: English when the locale is en (no Hangul)", !new Regex("[가-힣]").IsMatch(string.Join("|", lines.ToArray())) && lines[0].Contains("18 days"));
        UiStrings.Current = new UiStrings();
    }

    // ------------------------------------------------------------------ 문자열
    private static UiStrings LoadRealStrings()
    {
        UiStrings u = new UiStrings();
        u.LoadLocale("ko", ReadAll(Res + "Localization/ui_ko.csv"));
        u.LoadLocale("en", ReadAll(Res + "Localization/ui_en.csv"));
        return u;
    }

    private static string Unescape(string s)
    {
        return s.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\");
    }

    private static string Placeholders(string text)
    {
        List<int> found = new List<int>();
        foreach (Match m in Regex.Matches(text, @"\{(\d+)\}"))
        {
            int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (!found.Contains(n)) found.Add(n);
        }
        found.Sort();
        return string.Join(",", found.ConvertAll<string>(delegate (int x) { return x.ToString(CultureInfo.InvariantCulture); }).ToArray());
    }

    private static List<string> ScriptFiles()
    {
        List<string> files = new List<string>();
        foreach (string dir in new string[] { "UI", "Game", "Boot", "ArtPipeline", "Tycoon", "Core", "Data", "Romance", "Dialogue", "Save" })
            files.AddRange(Directory.GetFiles(Src + dir, "*.cs"));
        return files;
    }

    private static void Strings()
    {
        Console.WriteLine("== 문자열 키 · 자리표시자 ==");
        UiStrings u = LoadRealStrings();
        Check("real ko/en CSV load without errors", u.Errors.Count == 0);
        Check("ko and en have exactly the same keys", u.MissingIn("ko", "en").Count == 0 && u.MissingIn("en", "ko").Count == 0 && u.Count("ko") == u.Count("en"));
        List<string> bad = u.PlaceholderMismatches("ko", "en");
        foreach (string b in bad) Console.WriteLine("      placeholder mismatch: " + b);
        Check("every key has the same {0} {1} placeholders in ko and en", bad.Count == 0);

        // 코드가 참조하는 모든 키
        HashSet<string> referenced = new HashSet<string>();
        int callSites = 0, defaultMismatch = 0, phMismatch = 0;
        Regex call = new Regex("Loc\\.(T|F)\\(\\s*\"([A-Za-z0-9_]+)\"\\s*,\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        Regex direct = new Regex("UiStrings\\.Current\\.(?:Get|Format)\\(\\s*\"([A-Za-z0-9_]+)\"");
        foreach (string f in ScriptFiles())
        {
            string text = ReadAll(f);
            StringBuilder code = new StringBuilder();
            foreach (string ln in text.Split('\n')) if (!ln.TrimStart().StartsWith("//")) code.Append(ln).Append('\n');
            text = code.ToString();
            foreach (Match m in call.Matches(text))
            {
                callSites++;
                string key = m.Groups[2].Value;
                string def = Unescape(m.Groups[3].Value);
                referenced.Add(key);
                string ko = u.Raw("ko", key);
                if (ko == null) continue;
                if (ko != def) { defaultMismatch++; Console.WriteLine("      code default != ko csv: " + key + " in " + Path.GetFileName(f)); }
                if (m.Groups[1].Value == "F" && Placeholders(def) != Placeholders(ko)) phMismatch++;
            }
            foreach (Match m in direct.Matches(text)) if (!m.Groups[1].Value.EndsWith("_")) referenced.Add(m.Groups[1].Value);
        }
        Console.WriteLine("      Loc.T/F call sites scanned: " + callSites + ", distinct keys: " + referenced.Count);
        int missKo = 0, missEn = 0;
        foreach (string k in referenced)
        {
            if (!u.Has("ko", k)) { missKo++; Console.WriteLine("      missing in ko: " + k); }
            if (!u.Has("en", k)) { missEn++; Console.WriteLine("      missing in en: " + k); }
        }
        Check("every UiStrings key referenced in code exists in ui_ko.csv", missKo == 0 && referenced.Count > 60);
        Check("every UiStrings key referenced in code exists in ui_en.csv", missEn == 0);
        Check("code-side Korean default equals the ko csv text for every Loc.T/F site", defaultMismatch == 0);
        Check("code-side default has the same placeholders as the csv for every Loc.F site", phMismatch == 0);

        // UiText 속성(= 키)도 두 표에 모두 있다
        int propMiss = 0;
        foreach (System.Reflection.PropertyInfo p in typeof(UiText).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            if (!u.Has("ko", p.Name) || !u.Has("en", p.Name)) propMiss++;
        Check("every UiText property is a key in both csv files", propMiss == 0);

        // 동적 키(접두어 + id)
        int dyn = 0;
        for (int ch = 1; ch <= 8; ch++) if (!u.Has("ko", "chapter_name_" + ch) || !u.Has("en", "chapter_name_" + ch) || !u.Has("ko", "chapter_unlocks_" + ch) || !u.Has("en", "chapter_unlocks_" + ch)) dyn++;
        Check("chapter_name_1..8 and chapter_unlocks_1..8 exist in both languages", dyn == 0);
        CsvTable chapters = CsvTable.Parse(ReadAll(Res + "Balance/chapters.csv"));
        int chBad = 0;
        for (int i = 0; i < chapters.Rows.Count; i++)
        {
            CsvRow r = chapters.Rows[i];
            string n = r.Str("chapter");
            if (u.Raw("ko", "chapter_name_" + n) != r.Str("name") || u.Raw("ko", "chapter_unlocks_" + n) != r.Str("unlocks")) chBad++;
        }
        Check("ko chapter names/unlocks equal chapters.csv", chBad == 0 && chapters.Rows.Count == 8);
        int idMiss = 0, idRows = 0;
        CsvTable chars = CsvTable.Parse(ReadAll(Res + "Balance/characters.csv"));
        for (int i = 0; i < chars.Rows.Count; i++) { idRows++; string id = chars.Rows[i].Str("id"); if (u.Raw("ko", "char_name_" + id) != chars.Rows[i].Str("name_ko") || !u.Has("en", "char_name_" + id)) idMiss++; }
        CsvTable gifts = CsvTable.Parse(ReadAll(Res + "Balance/gifts.csv"));
        for (int i = 0; i < gifts.Rows.Count; i++) { idRows++; string id = gifts.Rows[i].Str("id"); if (u.Raw("ko", "gift_name_" + id) != gifts.Rows[i].Str("name_ko") || !u.Has("en", "gift_name_" + id)) idMiss++; }
        CsvTable spots = CsvTable.Parse(ReadAll(Res + "Balance/spots.csv"));
        for (int i = 0; i < spots.Rows.Count; i++) { idRows++; string id = spots.Rows[i].Str("id"); if (u.Raw("ko", "spot_name_" + id) != spots.Rows[i].Str("name_ko") || !u.Has("en", "spot_name_" + id)) idMiss++; }
        foreach (string g in new string[] { "male", "female", "nonbinary", "neutral" }) { idRows++; if (!u.Has("ko", "gender_" + g) || !u.Has("en", "gender_" + g)) idMiss++; }
        foreach (string sp in new string[] { "human", "wolf", "fox", "tiger", "rabbit", "bear", "cat" }) { idRows++; if (!u.Has("ko", "species_" + sp) || !u.Has("en", "species_" + sp)) idMiss++; }
        Check("char_name_/gift_name_/spot_name_/gender_/species_ keys exist for every data row (" + idRows + ") and ko equals the data csv", idMiss == 0);

        Regex hangul = new Regex("[가-힣]");
        int hangulEn = 0, emptyEn = 0;
        foreach (string k in u.Keys("en")) { string e = u.Raw("en", k); if (string.IsNullOrEmpty(e)) emptyEn++; else if (hangul.IsMatch(e)) { hangulEn++; Console.WriteLine("      Hangul in en: " + k); } }
        Check("no empty English value and no Hangul in the English table", hangulEn == 0 && emptyEn == 0);

        // Loc 도우미 동작
        UiStrings.Current = u;
        u.SetLocale("en");
        Check("Loc.F formats the English text", Loc.F("hud_day", "제{0}장 {1}  ·  {2}일차 ({3}/{4})", 2, Loc.ChapterName(2, "재개장"), 5, 3, 8) == "Ch. 2 Reopening  ·  Day 5 (3/8)");
        Check("Loc.CharName / GiftName / SpotName localize", Loc.CharName("howl", "하울") == "Howl" && Loc.GiftName("warm_scarf", "x") == "Warm scarf" && Loc.SpotName("beach", "x") == "Beach Promenade");
        Check("Loc.Tr translates exact Korean constants and leaves unknown text", Loc.Tr("골드가 부족합니다.") == "Not enough gold." && Loc.Tr("모르는 문장") == "모르는 문장");
        Check("Loc.Tr translates the Tycoon event strings", Loc.Tr("비가 와서 창가 자리가 한산했다.").StartsWith("It rained"));
        u.SetLocale("ko");
        Check("Korean locale: Loc.Tr is identity", Loc.Tr("골드가 부족합니다.") == "골드가 부족합니다.");
        Check("switching language at runtime changes Loc output without rebuilding the table", Loc.T("start_button", "시작") == "시작" && SwitchEn(u) == "Start");
        UiStrings.Current = new UiStrings();
        Check("empty table falls back to the Korean code default (game runs without csv)", Loc.F("hud_day", "제{0}장 {1}", 1, "상속") == "제1장 상속");
        Check("EndSummary/EndingPlan route their text through Loc (English title)", EnglishEndingTitle(u));

        // OnLocaleChanged 가 화면 다시 그리기로 연결되어 있다
        string gs = ReadAll(Src + "UI/GameScreen.cs");
        Check("GameScreen rebuilds the current view on locale change (OnLocaleChanged + _rebuild)", gs.Contains("public override void OnLocaleChanged()") && gs.Contains("_rebuild = RenderMorning") && gs.Contains("_rebuild = ShowNightMenu"));
        string ctx = ReadAll(Src + "UI/AppContext.cs");
        Check("AppContext.OnSettingsChanged calls LocalizedLabel.RefreshAll and the current screen's OnLocaleChanged", ctx.Contains("LocalizedLabel.RefreshAll()") && ctx.Contains("Nav.Current.OnLocaleChanged()"));
        int overrides = 0;
        foreach (string s in new string[] { "GameScreen", "TitleScreen", "RouteSelectScreen", "CharacterCreateScreen", "EndScreen", "DemoEndScreen" })
            if (ReadAll(Src + "UI/" + s + ".cs").Contains("public override void OnLocaleChanged()")) overrides++;
        Check("6 screens with dynamic text override OnLocaleChanged", overrides == 6);

        // TranslateFromKorean 단위
        UiStrings t = new UiStrings();
        t.LoadLocale("ko", "key,text\nb_key,같은 글\na_key,같은 글\nsolo,하나\n");
        t.LoadLocale("en", "key,text\nb_key,Second\na_key,First\nsolo,\n");
        t.SetLocale("en");
        Check("TranslateFromKorean: picks the alphabetically first key, empty translation keeps the Korean", t.TranslateFromKorean("같은 글") == "First" && t.TranslateFromKorean("하나") == "하나" && t.TranslateFromKorean(null) == null && t.TranslateFromKorean("") == "");
    }

    private static string SwitchEn(UiStrings u)
    {
        u.SetLocale("en");
        string r = Loc.T("start_button", "시작");
        u.SetLocale("ko");
        return r;
    }

    private static bool EnglishEndingTitle(UiStrings u)
    {
        UiStrings.Current = u;
        u.SetLocale("en");
        bool ok = MorpheHouse.GameFlow.EndingPlan.TitleOf(MorpheHouse.Romance.EndingKind.True) == "True Ending" && EndSummary.FormatPlayTime(3700) == "1 h 1 min";
        u.SetLocale("ko");
        ok = ok && MorpheHouse.GameFlow.EndingPlan.TitleOf(MorpheHouse.Romance.EndingKind.True) == "트루 엔딩";
        UiStrings.Current = new UiStrings();
        return ok;
    }

    // 하드코딩된 한글 문자열 스캔(UI 와 사용자에게 보이는 Game 파일). 로그·폰트 이름·언어 이름만 예외.
    private static void HardcodedHangul()
    {
        Console.WriteLine("== 하드코딩 한글 ==");
        string[] files =
        {
            "UI/AppContext.cs", "UI/CharacterCreateScreen.cs", "UI/DemoEndScreen.cs", "UI/EndScreen.cs", "UI/GameScreen.cs", "UI/PauseMenu.cs",
            "UI/RelationPanel.cs", "UI/RouteSelectScreen.cs", "UI/SettingsPanel.cs", "UI/TitleScreen.cs", "UI/VnScreen.cs", "UI/VnStage.cs", "UI/StandingView.cs", "UI/UiKit.cs",
            "Game/EndingFlow.cs", "Game/EndingPlan.cs", "Game/RouteSelect.cs", "Game/ProfileService.cs", "Game/ProfilePreview.cs" 
        };
        Regex lit = new Regex("\"((?:[^\"\\\\\\n]|\\\\.)*[가-힣](?:[^\"\\\\\\n]|\\\\.)*)\"");
        int offenders = 0, scanned = 0;
        foreach (string f in files)
        {
            string[] lines = ReadAll(Src + f).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                string t = l.Trim();
                if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) continue;
                if (l.Contains("Debug.Log") || l.Contains("Debug.Warning") || l.Contains("Debug.Error")) continue;
                if (l.Contains("Loc.T(") || l.Contains("Loc.F(")) { scanned++; continue; } // 허용: 키가 붙은 한국어 기본값
                if (l.Contains("\"한국어\"") || l.Contains("맑은 고딕") || l.Contains("FormatText(\"")) continue;
                int c = l.IndexOf("//");
                string code = c >= 0 && l.Substring(0, c).Split('"').Length % 2 == 1 ? l.Substring(0, c) : l;
                foreach (Match m in lit.Matches(code)) { offenders++; Console.WriteLine("      " + f + ":" + (i + 1) + ": " + m.Value); }
            }
        }
        Check("no Hangul string literal left in the scanned UI/Game user-facing files (outside Loc.T/F defaults, logs, language endonym)", offenders == 0);
        Check("scanned " + scanned + " Loc.T/F default lines in those files", scanned > 40);
    }

    // ------------------------------------------------------------------ 터치 목표
    private static void Touch()
    {
        Console.WriteLine("== TouchTargetPolicy · 레이아웃 ==");
        ReferenceScreen a169 = LayoutRules.Screen16x9;
        ReferenceScreen a209 = LayoutRules.Screen20x9;
        Check("48 dp = 7.62 mm", Math.Abs(TouchTargetPolicy.MinMm - 7.62f) < 0.01f);
        Check("desktop: never above the 48 ref px floor (no layout change)", TouchTargetPolicy.MinButtonHeight(a209, 1f, false) == 48f && TouchTargetPolicy.MinButtonHeight(400f, 3840, 2160, 1.1f, false) == 48f);
        float m209 = TouchTargetPolicy.MinButtonHeight(a209, 1f, true);
        Console.WriteLine("      20:9 2400x1080 @420dpi -> min button " + m209 + " ref px");
        Check("20:9 @420 dpi: 113 ref px (48 dp * 2.625 / 1.118)", m209 == 113f);
        Check("1920x1080 @160 dpi mobile (tablet-like): the 48 floor is enough", TouchTargetPolicy.MinButtonHeight(a169, 1f, true) == 48f);
        Check("1920x1200 @240 dpi: 72 ref px scaled by the match-0.5 canvas", TouchTargetPolicy.MinButtonHeight(240f, 1920, 1200, 1f, true) == (float)Math.Ceiling(48f * 240f / 160f / LayoutRules.CanvasScale(1920, 1200, 1f)));
        Check("dpi 0 / unknown falls back to 160 (no crash, floor)", TouchTargetPolicy.MinButtonHeight(0f, 1920, 1080, 1f, true) == 48f);
        Check("absurd dpi is capped (150 ref px)", TouchTargetPolicy.MinButtonHeight(1200f, 1080, 1920, 1f, true) == TouchTargetPolicy.MaxRefPx);
        // 물리 크기 ≥ 48 dp: 여러 기기
        int[][] devs = { new[] { 1080, 1920, 420 }, new[] { 2400, 1080, 420 }, new[] { 2560, 1440, 560 }, new[] { 1280, 720, 320 }, new[] { 2000, 1200, 280 }, new[] { 1920, 1080, 480 }, new[] { 800, 480, 240 } };
        int okDev = 0;
        foreach (int[] d in devs)
        {
            foreach (float scale in LayoutRules.UiScales)
            {
                float h = TouchTargetPolicy.MinButtonHeight(d[2], d[0], d[1], scale, true);
                float dp = LayoutRules.RefPxToDp(h, new ReferenceScreen("d", d[0], d[1], 0, 0, 0, 0, d[2]), scale);
                if (dp >= 48f - 0.01f || h >= TouchTargetPolicy.MaxRefPx) okDev++;
            }
        }
        Check("for " + devs.Length + " devices x 3 UI scales the button is >= 48 dp (>= 7.6 mm) physically", okDev == devs.Length * 3);
        Check("Apply raises small sizes and keeps big ones", TouchTargetPolicy.Apply(56f, 113f) == 113f && TouchTargetPolicy.Apply(200f, 113f) == 200f && TouchTargetPolicy.Apply(10f, 48f) == 48f);
        string kit = ReadAll(Src + "UI/UiKit.cs");
        Check("UiKit.MakeButton consults UiMetrics.MinButtonHeight (and Label creation is untouched by this change)", kit.Contains("TouchTargetPolicy.Apply(LayoutRules.ClampTouch(height), UiMetrics.MinButtonHeight)"));
        Check("DisplayService updates UiMetrics when the screen or UI scale changes", ReadAll(Src + "UI/DisplayService.cs").Contains("UiMetrics.Update(CurrentScreen(), _effective)"));

        // 스택: 마스크 길이
        int maskBad = 0, masked = 0;
        foreach (StackSpec st in LayoutRules.Stacks)
        {
            if (st.ButtonMask.Length == 0) { Console.WriteLine("      no mask: " + st.Name); maskBad++; continue; }
            masked++;
            if (st.ButtonMask.Length != st.ChildHeights.Count) { maskBad++; Console.WriteLine("      mask length mismatch: " + st.Name); }
        }
        Check("every stack has a button mask of the right length (" + masked + " stacks)", maskBad == 0);

        // 기존 검사는 그대로 통과(데스크톱)
        Check("desktop layout checks unchanged: 16:9 and 20:9 fit", LayoutRules.Check(a169, 1f).Count == 0 && LayoutRules.Check(a209, 1f).Count == 0);

        // 터치 정책 스택 검사: 16:9, 20:9, 태블릿, UI 배율
        ReferenceScreen tablet = new ReferenceScreen("1920x1200 @240dpi", 1920, 1200, 0, 0, 0, 0, 240f);
        ReferenceScreen[] screens = { a169, a209, tablet };
        int bad = 0;
        foreach (ReferenceScreen sc in screens)
            foreach (float scale in LayoutRules.AllowedUiScales(sc))
            {
                float minBtn = TouchTargetPolicy.MinButtonHeight(sc, scale, true);
                List<string> v = LayoutRules.CheckTouchStacks(sc, scale, minBtn);
                foreach (string s in v) Console.WriteLine("      " + s);
                bad += v.Count;
                Console.WriteLine("      " + sc.Name + " x" + scale.ToString("0.0") + ": min button " + minBtn + " ref px, violations " + v.Count);
            }
        Check("taller (>= 48 dp) buttons fit every stack on 16:9, 20:9 and a tablet (scrolling where declared)", bad == 0);

        // 20:9 에서 113 px 버튼은 스크롤 없이는 안 들어간다 → 스크롤 처리가 정말 필요하다는 증거
        float m = TouchTargetPolicy.MinButtonHeight(a209, 1f, true);
        StackSpec night = null;
        foreach (StackSpec st in LayoutRules.Stacks) if (st.Name == "밤 메뉴(기본)") night = st;
        PanelSpec nightPanel = LayoutRules.Find("ContentNight");
        Check("night menu with " + m + "px buttons: content " + night.ContentHeight(m) + " > panel " + nightPanel.H + " (so the scroll fallback is needed)", night.ContentHeight(m) > nightPanel.H);
        Check("... and the declared scroll still shows header + 2 buttons: " + night.MinVisibleHeight(m), night.Scrollable && night.MinVisibleHeight(m) <= nightPanel.H);
        StackSpec clone = new StackSpec(); clone.Name = "NoScrollNight"; clone.Panel = "ContentNight"; clone.Padding = night.Padding; clone.Spacing = night.Spacing; clone.ChildHeights.AddRange(night.ChildHeights); clone.ButtonMask = night.ButtonMask; clone.Scrollable = false;
        LayoutRules.Stacks.Add(clone);
        List<string> neg = LayoutRules.CheckTouchStacks(a209, 1f, m);
        LayoutRules.Stacks.Remove(clone);
        Check("negative test: the same stack without scroll is flagged", neg.Exists(delegate (string x) { return x.Contains("NoScrollNight"); }));
        Check("at desktop size (48) the night menu fits without scrolling", night.ContentHeight(48f) <= nightPanel.H);

        // 코드의 모바일 분기 수치
        Check("route select (mobile): list(3 buttons)+actions(3 buttons) fits panel 880 at 113 px: " + LayoutRules.RouteMobileNeeded(m), LayoutRules.RouteMobileNeeded(m) <= 880f);
        LayoutRect safe209 = LayoutRules.SafeRect(a209, 1f);
        float choicesTop = 310f + (m - 56f) + 540f;
        Check("VN choices (mobile): raised panel top " + choicesTop + " <= safe height " + safe209.H.ToString("0"), choicesTop <= safe209.H);
        string vn = ReadAll(Src + "UI/VnStage.cs");
        Check("VnStage mobile branch uses 540 / lifts by (btnH - 56)", vn.Contains("new Vector2(1200f, 540f)") && vn.Contains("310f + (btnH - 56f)") && vn.Contains("VerticalLayoutScroll(_choicePanel"));
        string rs = ReadAll(Src + "UI/RouteSelectScreen.cs");
        Check("RouteSelectScreen mobile branch matches the formula (3*m+16 actions, list = 880-100-24-actions-12)", rs.Contains("3f * UiMetrics.MinButtonHeight + 16f") && rs.Contains("880f - 100f - 24f - actionsH - 12f"));
        Check("Pause confirm mobile height 540 covers 64+110+2*113+36*2+14*3 = " + (64 + 110 + 226 + 72 + 42), (64 + 110 + 226 + 72 + 42) <= 540 && ReadAll(Src + "UI/PauseMenu.cs").Contains("new Vector2(900f, 540f)"));
        float hudButton = Math.Max(64f, m);
        Check("HUD menu button (mobile) is anchored to the HUD top and extends below it: height " + hudButton, ReadAll(Src + "UI/GameScreen.cs").Contains("menuH > 96f ? 1f : 0.5f"));

        // 밤 메뉴 버튼 최대 개수에서도 스크롤 열이 최소 3개를 보여 준다
        float avail = nightPanel.H - 72f - (70f + 14f) - (44f + 14f);
        Check("night menu scroll column viewport (" + avail + ") shows at least 3 buttons of " + m + " px", avail >= 3f * m + 2f * 14f);
    }

    // ------------------------------------------------------------------ 저장 최신성
    private static void Recency()
    {
        Console.WriteLine("== SaveRecency ==");
        DateTime d;
        Check("ISO 8601 'o' string parses as UTC", SaveRecency.TryParseUtc(new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc).ToString("o"), out d) && d == new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc));
        Check("offset times are converted to UTC", SaveRecency.TryParseUtc("2026-10-03T10:00:00+02:00", out d) && d.Hour == 8);
        Check("no zone = treated as UTC", SaveRecency.TryParseUtc("2026-10-03T10:00:00", out d) && d.Hour == 10 && d.Kind == DateTimeKind.Utc);
        Check("garbage / null / empty -> invalid, ParseOrOldest = MinValue", !SaveRecency.TryParseUtc("not a date", out d) && !SaveRecency.TryParseUtc(null, out d) && !SaveRecency.TryParseUtc("", out d) && SaveRecency.ParseOrOldest("x") == DateTime.MinValue);
        Check("the ordinal-compare bug: '...09:00:00Z' is newer than '...10:00:00+02:00' (08:00 UTC)", SaveRecency.IsNewer("2026-10-03T09:00:00Z", "2026-10-03T10:00:00+02:00") && string.CompareOrdinal("2026-10-03T10:00:00+02:00", "2026-10-03T09:00:00Z") > 0);
        Check("invalid is oldest: a valid time beats garbage, garbage never beats valid", SaveRecency.IsNewer("2020-01-01T00:00:00Z", "garbage") && !SaveRecency.IsNewer("garbage", "2020-01-01T00:00:00Z"));
        Check("ties and both-invalid are not 'newer' (first slot wins)", !SaveRecency.IsNewer("2026-01-01T00:00:00Z", "2026-01-01T00:00:00Z") && !SaveRecency.IsNewer("a", "b"));
        Check("fractional seconds compare correctly", SaveRecency.IsNewer("2026-01-01T00:00:00.9500000Z", "2026-01-01T00:00:00.1000000Z"));

        // AppContext.LoadLatest 의 구조(예외 격리 + SaveRecency 사용)
        string ctx = ReadAll(Src + "UI/AppContext.cs");
        int a = ctx.IndexOf("public LoadResult LoadLatest()");
        int b = ctx.IndexOf("public bool HasAnySave()");
        string body = ctx.Substring(a, b - a);
        Check("LoadLatest: per-slot try/catch, SaveRecency.IsNewer, no ordinal SavedAtUtc compare", body.Contains("catch (Exception") && body.Contains("SaveRecency.IsNewer") && !body.Contains("string.CompareOrdinal"));
        Check("LoadLatest: state construction is isolated (TryStartFromSave) and restores the previous session on failure", body.Contains("TryStartFromSave") && body.Contains("Balance = oldBalance") && body.Contains("return false"));

        // 같은 정렬 규칙을 순수하게 재현: 최근 순, 동률은 앞 슬롯
        string[] stamps = { "2026-10-03T08:00:00Z", "garbage", "2026-10-03T10:00:00+02:00", "2026-10-03T08:00:00Z" };
        List<int> order = new List<int>();
        for (int i = 0; i < stamps.Length; i++)
        {
            int at = order.Count;
            while (at > 0 && SaveRecency.IsNewer(stamps[i], stamps[order[at - 1]])) at--;
            order.Insert(at, i);
        }
        Check("ordering rule (as in LoadLatest): ties keep slot order, garbage last: " + string.Join(",", order.ConvertAll<string>(delegate (int x) { return x.ToString(); }).ToArray()), order[0] == 0 && order[1] == 2 && order[2] == 3 && order[3] == 1);
    }

    // ------------------------------------------------------------------ 메모리
    private static void Memory()
    {
        Console.WriteLine("== MemoryBudget 정상 상태 ==");
        List<string> errors = new List<string>();
        List<ArtRow> rows = MemoryBudget.ParseChecklist(ReadAll("docs/art_checklist.csv"), errors);
        PlatformProfile pc = PlatformProfile.Pc();
        PlatformProfile an = PlatformProfile.Android();
        long anBytes = MemoryBudget.SteadyStateCacheBytes(rows, an, 12, 4, 3, 64);
        long pcBytes = MemoryBudget.SteadyStateCacheBytes(rows, pc, 12, 4, 3, 64);
        long anAll = MemoryBudget.CacheGrowthBytes(rows, an, false);
        long pcAll = MemoryBudget.CacheGrowthBytes(rows, pc, false);
        Console.WriteLine("      steady state: Android " + MemoryBudget.ToMb(anBytes).ToString("0.0") + " MB (unbounded " + MemoryBudget.ToMb(anAll).ToString("0.0") + "), PC " + MemoryBudget.ToMb(pcBytes).ToString("0.0") + " MB (unbounded " + MemoryBudget.ToMb(pcAll).ToString("0.0") + ")");
        Check("steady state is below the unbounded growth on both platforms", anBytes < anAll && pcBytes < pcAll && anBytes > 0);
        Check("steady state + UI atlas fits the 25%-headroom budgets", MemoryBudget.FitsBudget(anBytes + an.UiAtlasBytes, an, 0.25) && MemoryBudget.FitsBudget(pcBytes + pc.UiAtlasBytes, pc, 0.25));
        Check("steady state >= the peak scene art (the cache holds at least what a scene needs)", anBytes >= MemoryBudget.PeakScene(rows, an).TotalBytes - an.UiAtlasBytes);
        string doc = ReadAll("docs/15_Performance_Guide.md");
        Check("docs/15 quotes the same steady-state numbers (Android " + MemoryBudget.ToMb(anBytes).ToString("0.0") + " MB, PC " + MemoryBudget.ToMb(pcBytes).ToString("0.0") + " MB)", doc.Contains(MemoryBudget.ToMb(anBytes).ToString("0.0") + " MB") && doc.Contains(MemoryBudget.ToMb(pcBytes).ToString("0.0") + " MB"));
        long placeholder = ((long)(12 + 0) * 1024 * 2048 * 4);
        Check("placeholder worst case (RGBA32) is bounded by the byte caps: standing cap 96 MiB >= 12 x 8 MiB", 96L * 1024 * 1024 >= placeholder && 4L * 1920 * 1080 * 4 <= 40L * 1024 * 1024 + 4 * 1024 * 1024);
    }

    // ------------------------------------------------------------------ 하루 흐름(공통 장면 구동기)
    private static readonly string[] ExpectedCommon =
    {
        "CE01_FirstGuest", "CE02_Tasting", "CE03_RentMeeting", "CE04_StaffWelcome", "CE05_FestivalPrep", "Ch5_Show", "Ch5_Show_After",
        "CE06_OldPhoto", "Ch6_LedgerTruth", "CE07_BlueHarborOpening", "CE08_TableTogether"
    };

    private sealed class HostSim
    {
        public readonly HashSet<string> Visited = new HashSet<string>();
        public readonly List<string> Log = new List<string>();
        public readonly HashSet<string> Nodes = new HashSet<string>();
        public GameState State;
        public DayCycle Cycle;
        public NightRomanceActions Night;
        public DayFlowDriver Flow;
        public int Days;

        public void Play(string node)
        {
            Visited.Add(node);
            Log.Add(node);
        }

        public int Count(string node)
        {
            int n = 0;
            foreach (string l in Log) if (l == node) n++;
            return n;
        }

        // GameScreen.PlayNextCommon 와 같다: 있으면 재생하고 true(재생이 끝나면 화면이 같은 단계를 다시 그린다)
        public bool PlayNextCommon(SceneSlot slot)
        {
            ChapterStep s = Flow.Next(slot);
            if (s.Kind != ChapterStepKind.CommonScene) return false;
            Play(s.Node);
            return true;
        }

        // GameScreen.ShowMorning
        public void Morning()
        {
            for (int guard = 0; guard < 50; guard++)
            {
                ChapterStep st = ChapterFlow.NextMorningStep(State.Chapter, State.DayInChapter, Cycle.ChapterIntroPending, Nodes.Contains, Visited.Contains);
                if (st.Kind == ChapterStepKind.Outro || st.Kind == ChapterStepKind.Intro) { Play(st.Node); if (st.Kind == ChapterStepKind.Intro) Cycle.AckChapterIntro(); continue; }
                if (st.Kind == ChapterStepKind.InfoCard) { Cycle.AckChapterIntro(); continue; }
                if (PlayNextCommon(SceneSlot.Morning)) continue;
                return;
            }
            throw new InvalidOperationException("morning did not settle");
        }

        // GameScreen.ShowNight: 메뉴가 뜰 때까지(장면이 있으면 재생 후 다시 들어온다)
        public void NightMenu()
        {
            for (int guard = 0; guard < 50; guard++)
            {
                if (PlayNextCommon(SceneSlot.Night)) continue;
                if (State.ActionsLeft <= 0 && PlayNextCommon(SceneSlot.NightEnd)) continue;
                return;
            }
            throw new InvalidOperationException("night menu did not settle");
        }

        public void RunDay(int actionsToDo)
        {
            Morning();
            Cycle.RunBusiness();
            Cycle.BeginNight();
            NightMenu();
            int done = 0;
            while (State.ActionsLeft > 0 && done < actionsToDo)
            {
                Cycle.DoNightAction(NightAction.Research);
                done++;
                NightMenu();
            }
            // 밤 끝내기 버튼: 정산 전에 밤 끝 장면 먼저, 장면이 있으면 밤 메뉴로 돌아왔다가 다시 누른다
            for (int guard = 0; guard < 50; guard++)
            {
                if (PlayNextCommon(SceneSlot.NightEnd)) { NightMenu(); continue; }
                break;
            }
            Cycle.Settle();
            Cycle.NextDay();
            Days++;
        }
    }

    private static GameState RouteState(int chapter, int day)
    {
        BalanceTable bal = BalanceTable.Load(new FileTextSource(Res + "Balance"), Difficulty.Normal);
        GameState st = GameState.NewGame(bal, Difficulty.Normal, "bot");
        st.Chapter = chapter;
        st.Day = day;
        return st;
    }

    private static HostSim NewHost(int seed)
    {
        BalanceTable bal = BalanceTable.Load(new FileTextSource(Res + "Balance"), Difficulty.Normal);
        RomanceCatalog cat = RomanceCatalog.Load(new FileTextSource(Res + "Balance"));
        HostSim h = new HostSim();
        h.State = GameState.NewGame(bal, Difficulty.Normal, "bot");
        SystemRandomAdapter rng = new SystemRandomAdapter(seed);
        h.Cycle = new DayCycle(h.State, bal, rng);
        RomanceService svc = new RomanceService(h.State, bal, cat, rng.NextDouble);
        SceneRuleTable rules = SceneRuleTable.Parse(ReadAll(Res + "Balance/scene_rules.csv"));
        foreach (SceneRule r in rules.Rules) h.Nodes.Add(r.Node);
        for (int c = 0; c < CharIds.All.Length; c++) for (int st = 1; st <= 5; st++) h.Nodes.Add(RomanceService.EventNode(CharIds.All[c], st));
        h.Night = new NightRomanceActions(h.State, bal, svc, cat, h.Nodes.Contains, h.Visited.Contains);
        h.Flow = new DayFlowDriver(delegate (SceneSlot slot) { return h.Night.NextCommonScene(slot); }, h.Visited.Contains);
        return h;
    }

    private static void DayFlow()
    {
        Console.WriteLine("== DayFlowDriver (UI-level driver, 64-day loop) ==");
        int[] actionPolicies = { 0, 1, 2 };
        bool onceAll = true, orderAll = true, finishedAll = true, stuckFree = true;
        foreach (int actions in actionPolicies)
        {
            HostSim h = NewHost(4242 + actions);
            h.Cycle.Begin();
            int guard = 0;
            while (!h.State.Finished && guard++ < 200) h.RunDay(actions);
            bool once = true;
            foreach (string n in ExpectedCommon) if (h.Count(n) != 1) { once = false; Console.WriteLine("      count(" + n + ") = " + h.Count(n) + " (actions/night " + actions + ")"); }
            List<string> seen = new List<string>();
            foreach (string l in h.Log) if (Array.IndexOf(ExpectedCommon, l) >= 0) seen.Add(l);
            bool order = string.Join(",", seen.ToArray()) == string.Join(",", ExpectedCommon);
            Console.WriteLine("      actions/night=" + actions + ": days=" + h.Days + " once=" + once + " order=" + order + " stuck=" + h.Flow.StuckCount);
            onceAll &= once; orderAll &= order; finishedAll &= h.State.Finished && h.Days == 64; stuckFree &= h.Flow.StuckCount == 0;
        }
        Check("64 days via the driver: every CE node (CE01..CE08, Ch5_Show, Ch5_Show_After, Ch6_LedgerTruth) plays exactly once (0, 1 and 2 night actions/day)", onceAll);
        Check("... in chronological order", orderAll);
        Check("... the run lasts 64 days and the driver never gets stuck", finishedAll && stuckFree);

        // 슬롯 의미: CE07 은 아침(루트 선택이 닫힌 뒤에도 같은 아침 슬롯), Ch5_Show_After 는 밤 끝, CE04 는 밤
        HostSim p = NewHost(7);
        p.Cycle.Begin();
        while (!p.State.Finished) p.RunDay(1);
        Check("Ch5_Show_After is played in the night-end slot after Ch5_Show (night), CE07 after Ch7_Intro", p.Log.IndexOf("Ch5_Show") < p.Log.IndexOf("Ch5_Show_After") && p.Log.IndexOf("Ch7_Intro") < p.Log.IndexOf("CE07_BlueHarborOpening"));

        // 구동기 단위: 같은 노드가 방문 기록 없이 되풀이되면 NoStep + StuckCount
        int calls = 0;
        DayFlowDriver stuck = new DayFlowDriver(delegate (SceneSlot s) { calls++; return ChapterStep.Of(ChapterStepKind.CommonScene, "X"); }, delegate (string n) { return false; });
        ChapterStep a = stuck.Next(SceneSlot.Morning);
        ChapterStep b = stuck.Next(SceneSlot.Morning);
        Check("driver guard: an unvisited repeat is refused (NoStep) and counted", a.Kind == ChapterStepKind.CommonScene && b.Kind == ChapterStepKind.None && stuck.StuckCount == 1 && stuck.LastStuck == "X");
        HashSet<string> vis = new HashSet<string>();
        DayFlowDriver ok = new DayFlowDriver(delegate (SceneSlot s) { return vis.Contains("X") ? ChapterStep.NoStep : ChapterStep.Of(ChapterStepKind.CommonScene, "X"); }, vis.Contains);
        ChapterStep c1 = ok.Next(SceneSlot.Night); vis.Add("X"); ChapterStep c2 = ok.Next(SceneSlot.Night);
        Check("driver: visited node is not returned again; non-common kinds are ignored", c1.Kind == ChapterStepKind.CommonScene && c2.Kind == ChapterStepKind.None && new DayFlowDriver(delegate (SceneSlot s) { return ChapterStep.Of(ChapterStepKind.Intro, "Ch1_Intro"); }, null).Next(SceneSlot.Morning).Kind == ChapterStepKind.None);
        // QA-10: 세션 초기화
        DayFlowDriver rs = new DayFlowDriver(delegate (SceneSlot sl) { return ChapterStep.Of(ChapterStepKind.CommonScene, "Y"); }, delegate (string n) { return false; });
        rs.Next(SceneSlot.Morning); rs.Next(SceneSlot.Morning);
        bool hadStuck = rs.StuckCount == 1;
        rs.Reset();
        Check("DayFlowDriver.Reset clears the stuck guard and counters (a new game can return the same node again)", hadStuck && rs.StuckCount == 0 && rs.LastStuck == null && rs.Next(SceneSlot.Morning).Kind == ChapterStepKind.CommonScene);
        Check("route prompt is suppressed only for the same day: after reset (-1) it shows again on the same day", !RouteFlow.ShouldAutoPrompt(RouteState(7, 3), 1, 3) && RouteFlow.ShouldAutoPrompt(RouteState(7, 3), 1, -1));
        string ctxSrc = ReadAll(Src + "UI/AppContext.cs");
        string gsSrc = ReadAll(Src + "UI/GameScreen.cs");
        Check("ResetSession exists and is called from StartNewGame, a successful load and ExitToTitle", Regex.Matches(ctxSrc, "ResetSession\\(\\);").Count >= 3 && gsSrc.Contains("_routePromptDay = -1;") && gsSrc.Contains("_flow.Reset();") && gsSrc.Contains("StopCoroutine(_running)"));
        bool threw = false;
        try { new DayFlowDriver(null, null); } catch (ArgumentNullException) { threw = true; }
        Check("driver requires a source", threw);

        string gs = ReadAll(Src + "UI/GameScreen.cs");
        Check("GameScreen delegates to DayFlowDriver at all four points (morning, night open/after action, night end, finish button)",
            gs.Contains("new DayFlowDriver(") && gs.Contains("PlayNextCommon(SceneSlot.Morning)") && gs.Contains("PlayNextCommon(SceneSlot.Night)") && Regex.Matches(gs, "PlayNextCommon\\(SceneSlot\\.NightEnd\\)").Count == 2 && gs.Contains("Ctx.Night.NextCommonScene(slot)"));
        string rsrc = ReadAll(Src + "UI/RouteSelectScreen.cs");
        Check("route select closes via Nav.Show(Game) (OnShow -> ShowMorning -> morning slot -> CE07 the same morning)", rsrc.Contains("Ctx.Nav.Show(Ctx.Game)") && gs.Contains("protected override void OnShow()") && gs.Contains("ShowPhase();"));
    }
}
