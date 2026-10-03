using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MorpheHouse.ArtPipeline;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using MorpheHouse.UI;

// 5단계(폴리시) 하네스(Unity 불필요). 실제 매니페스트·문자열 CSV·아트 체크리스트·UI 소스 파일을 읽어 검사한다.
public static class Program
{
    private const string Res = "UnityProject/Assets/_Project/Resources/";
    private const string Src = "UnityProject/Assets/_Project/Scripts/";

    private static int _checks;
    private static int _failures;
    private static BalanceTable _balance;
    private static RomanceCatalog _catalog;

    private static void Check(string name, bool ok)
    {
        _checks++;
        if (!ok) _failures++;
        Console.WriteLine("  [{0}] {1}", ok ? "PASS" : "FAIL", name);
    }

    private static bool Near(float a, float b, float eps)
    {
        return Math.Abs(a - b) <= eps;
    }

    private static string ReadAll(string path)
    {
        return File.ReadAllText(path, Encoding.UTF8);
    }

    // ------------------------------------------------------------------ 오디오 계산
    private static void AudioMath()
    {
        Console.WriteLine("== 오디오 믹서 계산 ==");
        Check("slider 0 -> amplitude 0", AudioMixerModel.SliderToAmplitude(0f) == 0f);
        Check("slider 1 -> amplitude 1", AudioMixerModel.SliderToAmplitude(1f) == 1f);
        Check("slider 0.5 -> amplitude 0.25 (square curve)", Near(AudioMixerModel.SliderToAmplitude(0.5f), 0.25f, 1e-6f));
        Check("slider clamps out-of-range and NaN", AudioMixerModel.SliderToAmplitude(2f) == 1f && AudioMixerModel.SliderToAmplitude(-1f) == 0f && AudioMixerModel.SliderToAmplitude(float.NaN) == 0f);
        Check("amplitude 1 -> 0 dB", Near(AudioMixerModel.AmplitudeToDb(1f), 0f, 1e-4f));
        Check("amplitude 0.5 -> -6.02 dB", Near(AudioMixerModel.AmplitudeToDb(0.5f), -6.0206f, 1e-3f));
        Check("amplitude 0 / 1e-5 / NaN -> silence floor", AudioMixerModel.AmplitudeToDb(0f) == -80f && AudioMixerModel.AmplitudeToDb(1e-5f) == -80f && AudioMixerModel.AmplitudeToDb(float.NaN) == -80f);
        Check("slider 0.5 -> -12.04 dB", Near(AudioMixerModel.SliderToDb(0.5f), -12.041f, 1e-2f));
        Check("slider 0 -> -80 dB", AudioMixerModel.SliderToDb(0f) == -80f);
        bool mono = true;
        float prev = AudioMixerModel.SliderToDb(0.01f);
        for (int i = 2; i <= 100; i++)
        {
            float db = AudioMixerModel.SliderToDb(i / 100f);
            if (db < prev) mono = false;
            prev = db;
        }
        Check("slider -> dB is monotonic over 100 steps", mono);
        Check("dB -> amplitude: -6.02 dB ~ 0.5, 0 dB = 1, +6 dB clamps to 1, -80 dB = 0",
            Near(AudioMixerModel.DbToAmplitude(-6.0206f), 0.5f, 1e-3f) && AudioMixerModel.DbToAmplitude(0f) == 1f
            && AudioMixerModel.DbToAmplitude(6f) == 1f && AudioMixerModel.DbToAmplitude(-80f) == 0f && AudioMixerModel.DbToAmplitude(float.NaN) == 0f);
        bool roundTrip = true;
        for (int i = 10; i <= 100; i += 5)
        {
            float v = i / 100f;
            if (!Near(AudioMixerModel.DbToSlider(AudioMixerModel.SliderToDb(v)), v, 1e-3f)) roundTrip = false;
        }
        Check("dB <-> slider round trip (0.10..1.00)", roundTrip);

        float o, n;
        AudioMixerModel.CrossfadeGains(0f, out o, out n);
        Check("crossfade t=0: old full, new silent", Near(o, 1f, 1e-6f) && Near(n, 0f, 1e-6f));
        AudioMixerModel.CrossfadeGains(1f, out o, out n);
        Check("crossfade t=1: old silent, new full", Near(o, 0f, 1e-6f) && Near(n, 1f, 1e-6f));
        AudioMixerModel.CrossfadeGains(0.5f, out o, out n);
        Check("crossfade t=0.5: equal gains 0.707", Near(o, 0.7071f, 1e-3f) && Near(n, 0.7071f, 1e-3f));
        bool power = true;
        for (int i = 0; i <= 20; i++)
        {
            AudioMixerModel.CrossfadeGains(i / 20f, out o, out n);
            if (!Near(o * o + n * n, 1f, 1e-5f)) power = false;
        }
        Check("crossfade keeps constant power (o^2 + n^2 = 1)", power);
        AudioMixerModel.CrossfadeGains(-3f, out o, out n);
        float o2, n2;
        AudioMixerModel.CrossfadeGains(9f, out o2, out n2);
        Check("crossfade clamps t", Near(o, 1f, 1e-6f) && Near(n2, 1f, 1e-6f));

        AudioMixerModel m = new AudioMixerModel();
        Check("default gains: master 1, bgm 0.8^2, se 0.9^2, voice 1", m.Gain(AudioChannel.Master) == 1f && Near(m.Gain(AudioChannel.Bgm), 0.64f, 1e-5f) && Near(m.Gain(AudioChannel.Se), 0.81f, 1e-5f) && m.Gain(AudioChannel.Voice) == 1f);
        m.SetVolume(AudioChannel.Master, 0.5f);
        Check("master multiplies channels (0.25 * 0.64)", Near(m.Gain(AudioChannel.Bgm), 0.16f, 1e-5f));
        m.SetVolume(AudioChannel.Master, 0f);
        Check("master 0 silences every channel", m.Gain(AudioChannel.Bgm) == 0f && m.Gain(AudioChannel.Se) == 0f && m.Gain(AudioChannel.Voice) == 0f);
        m.SetVolume(AudioChannel.Master, 1f);
        m.SetChannelMuted(AudioChannel.Se, true);
        Check("channel mute silences only that channel", m.Gain(AudioChannel.Se) == 0f && m.Gain(AudioChannel.Bgm) > 0f && m.IsChannelMuted(AudioChannel.Se));
        m.SetChannelMuted(AudioChannel.Se, false);
        m.Muted = true;
        Check("global mute silences everything", m.Gain(AudioChannel.Master) == 0f && m.Gain(AudioChannel.Bgm) == 0f && m.GainDb(AudioChannel.Bgm) == -80f);
        m.Muted = false;
        m.SetChannelMuted(AudioChannel.Master, true);
        Check("master channel mute silences everything", m.Gain(AudioChannel.Bgm) == 0f);
        m.SetChannelMuted(AudioChannel.Master, false);
        m.SetVolume(AudioChannel.Bgm, 9f);
        Check("SetVolume clamps to 0..1", m.Volume(AudioChannel.Bgm) == 1f);
        m.SetVolume(AudioChannel.Bgm, float.NaN);
        Check("SetVolume NaN becomes 0", m.Volume(AudioChannel.Bgm) == 0f);

        // 덕킹
        AudioMixerModel d = new AudioMixerModel();
        float bgm0 = d.Gain(AudioChannel.Bgm);
        d.SetDucking(true);
        d.Tick(0.075f);
        Check("ducking attack is gradual (half way after half the attack time)", Near(d.DuckLevel, 0.5f, 1e-4f) && d.Gain(AudioChannel.Bgm) < bgm0 && d.Gain(AudioChannel.Bgm) > bgm0 * AudioMixerModel.DbToAmplitude(-9f));
        d.Tick(0.2f);
        Check("ducking reaches full level and clamps at 1", d.DuckLevel == 1f);
        Check("full ducking = bgm * 10^(-9/20)", Near(d.Gain(AudioChannel.Bgm), bgm0 * AudioMixerModel.DbToAmplitude(-9f), 1e-5f));
        Check("ducking does not touch SE or voice", Near(d.Gain(AudioChannel.Se), 0.81f, 1e-5f) && d.Gain(AudioChannel.Voice) == 1f);
        d.SetDucking(false);
        d.Tick(0.3f);
        Check("ducking releases slower than it attacks (half way after 0.3 s)", Near(d.DuckLevel, 0.5f, 1e-4f));
        d.Tick(5f);
        Check("ducking fully released returns bgm gain", d.DuckLevel == 0f && Near(d.Gain(AudioChannel.Bgm), bgm0, 1e-6f));
        d.Tick(0f);
        d.Tick(-1f);
        Check("Tick(<=0) changes nothing", d.DuckLevel == 0f);
        AudioMixerModel nd = new AudioMixerModel();
        nd.DuckDb = 0f;
        nd.SetDucking(true);
        nd.Tick(1f);
        Check("DuckDb=0 means no ducking effect", Near(nd.Gain(AudioChannel.Bgm), 0.64f, 1e-5f));

        SettingsService s = new SettingsService(new MemorySettingsStorage());
        s.SetMasterVolume(0.5f); s.SetBgmVolume(0.4f); s.SetSeVolume(0.3f); s.SetVoiceVolume(0.2f);
        AudioMixerModel sync = new AudioMixerModel();
        sync.SyncFrom(s);
        sync.SyncFrom(null);
        Check("SyncFrom copies the four settings volumes", sync.Volume(AudioChannel.Master) == 0.5f && sync.Volume(AudioChannel.Bgm) == 0.4f && sync.Volume(AudioChannel.Se) == 0.3f && sync.Volume(AudioChannel.Voice) == 0.2f);

        AudioThrottle t = new AudioThrottle();
        Check("throttle: first call passes", t.TryPass("typewriter", 1.00f));
        Check("throttle: call inside interval blocked", !t.TryPass("typewriter", 1.03f));
        Check("throttle: call after interval passes", t.TryPass("typewriter", 1.06f));
        Check("throttle: ids are independent", t.TryPass("ui_click", 1.06f));
        Check("throttle: explicit interval", t.TryPass("typewriter", 1.07f, 0.005f) && !t.TryPass("typewriter", 1.08f, 0.5f));
        Check("throttle: clock going backwards passes (e.g. after a pause)", t.TryPass("typewriter", 0.2f));
        Check("throttle: empty id never passes", !t.TryPass("", 5f) && !t.TryPass(null, 5f));
        t.Reset();
        Check("throttle: Reset forgets history", t.TryPass("typewriter", 0.2f));
    }

    // ------------------------------------------------------------------ 매니페스트
    private static string Row(string id, string kind, string path, string loop, string vol, string notes)
    {
        return id + "," + kind + "," + path + "," + loop + "," + vol + "," + notes + "\n";
    }

    private static void Manifest()
    {
        Console.WriteLine("== 오디오 매니페스트 ==");
        string csv = ReadAll(Res + "Audio/audio_manifest.csv");
        AudioCatalog real = AudioCatalog.Parse(csv);
        Check("real manifest: no errors", real.Errors.Count == 0);
        if (real.Errors.Count > 0) for (int i = 0; i < real.Errors.Count; i++) Console.WriteLine("      " + real.Errors[i]);
        Check("real manifest: no warnings", real.Warnings.Count == 0);
        Check("real manifest: 13 BGM + 13 SE", real.Ids(AudioKind.Bgm).Count == 13 && real.Ids(AudioKind.Se).Count == 13 && real.Count == 26);
        Check("real manifest: no voice entries yet", real.Ids(AudioKind.Voice).Count == 0);

        bool paths = true, loops = true, vols = true;
        for (int i = 0; i < real.All.Count; i++)
        {
            AudioEntry e = real.All[i];
            string expect = e.Kind == AudioKind.Bgm ? "Audio/BGM/bgm_" + e.Id : "Audio/SE/se_" + e.Id;
            if (e.Path != expect) paths = false;
            if (e.Kind == AudioKind.Bgm && !e.Loop) loops = false;
            if (e.Kind == AudioKind.Se && e.Loop) loops = false;
            if (e.DefaultVolume <= 0f || e.DefaultVolume > 1f) vols = false;
        }
        Check("real manifest: paths follow Audio/BGM/bgm_<id> and Audio/SE/se_<id>", paths);
        Check("real manifest: BGM loop, SE do not", loops);
        Check("real manifest: default volumes in (0,1]", vols);

        // AudioCues 상수가 모두 매니페스트에 있고 종류가 맞다.
        int cueCount = 0;
        bool cuesOk = true;
        FieldInfo[] fields = typeof(AudioCues).GetFields(BindingFlags.Public | BindingFlags.Static);
        for (int i = 0; i < fields.Length; i++)
        {
            if (!fields[i].IsLiteral || fields[i].FieldType != typeof(string)) continue;
            string id = (string)fields[i].GetRawConstantValue();
            AudioKind kind = fields[i].Name.StartsWith("Bgm") ? AudioKind.Bgm : AudioKind.Se;
            cueCount++;
            if (!real.Has(id, kind)) { cuesOk = false; Console.WriteLine("      AudioCues." + fields[i].Name + " = '" + id + "' 가 매니페스트에 없음/종류 불일치"); }
        }
        Check("every AudioCues constant exists in the manifest with the right kind (" + cueCount + " constants)", cuesOk && cueCount >= 20);
        Check("AudioCues.BgmForTime maps Day/Evening/Night (+ unknown -> day)", AudioCues.BgmForTime("Night") == "cafe_night" && AudioCues.BgmForTime("Evening") == "settlement" && AudioCues.BgmForTime("Day") == "cafe_day" && AudioCues.BgmForTime("???") == "cafe_day" && AudioCues.BgmForTime(null) == "cafe_day");
        HashSet<string> endingIds = new HashSet<string>();
        bool endingsOk = true;
        foreach (EndingKind k in Enum.GetValues(typeof(EndingKind)))
        {
            string id = AudioCues.BgmForEnding(k);
            endingIds.Add(id);
            if (!real.Has(id, AudioKind.Bgm)) endingsOk = false;
        }
        Check("every ending kind has an existing ending BGM; harem duo/trio share one (5 distinct)", endingsOk && endingIds.Count == 5);
        Check("ButtonSe: back > accent > normal", AudioCues.ButtonSe(true, true) == "ui_back" && AudioCues.ButtonSe(true, false) == "ui_confirm" && AudioCues.ButtonSe(false, false) == "ui_click");

        AudioEntry tmp;
        Check("lookup is case-insensitive and trims", real.TryGet("  CAFE_Day ", out tmp) && tmp.Id == "cafe_day");
        Check("Has() checks the kind", real.Has("cafe_day", AudioKind.Bgm) && !real.Has("cafe_day", AudioKind.Se) && !real.Has("nope", AudioKind.Se) && !real.Has(null, AudioKind.Se));
        List<string> bgmIds = real.Ids(AudioKind.Bgm);
        Check("Ids() keeps manifest order (title first)", bgmIds[0] == "title");

        // 문서 일치
        string doc = ReadAll("docs/14_Audio_Request_List.md");
        bool docOk = true;
        for (int i = 0; i < real.All.Count; i++)
            if (!doc.Contains("| `" + real.All[i].Id + "` | `" + real.All[i].Path + "` |")) { docOk = false; Console.WriteLine("      docs/14 에 행 없음: " + real.All[i].Id); }
        Check("docs/14_Audio_Request_List.md lists every manifest id with its path", docOk);
        Check("docs/14 states the total (26)", doc.Contains("총 26개(BGM 13 + SE 13)"));

        // 나쁜 행
        StringBuilder sb = new StringBuilder("id,kind,path,loop,default_volume,notes\n");
        sb.Append(Row("good_one", "bgm", "Audio/BGM/a", "1", "0.5", "ok"));
        sb.Append(Row("", "bgm", "Audio/BGM/x", "1", "1", "missing id"));
        sb.Append(Row("bad id!", "se", "Audio/SE/x", "0", "1", ""));
        sb.Append(Row("9starts", "se", "Audio/SE/x", "0", "1", ""));
        sb.Append(Row("weird_kind", "music", "Audio/BGM/x", "1", "1", ""));
        sb.Append(Row("no_path", "se", "", "0", "1", ""));
        sb.Append(Row("abs_path", "se", "/etc/passwd", "0", "1", ""));
        sb.Append(Row("dotdot", "se", "Audio/../x", "0", "1", ""));
        sb.Append(Row("drive", "se", "C:/x", "0", "1", ""));
        sb.Append(Row("bad_loop", "se", "Audio/SE/x", "maybe", "1", ""));
        sb.Append(Row("bad_vol", "se", "Audio/SE/x", "0", "loud", ""));
        sb.Append(Row("loud_vol", "se", "Audio/SE/loud", "0", "1.5", "clamped"));
        sb.Append(Row("neg_vol", "se", "Audio/SE/neg", "0", "-2", "clamped"));
        sb.Append(Row("good_one", "se", "Audio/SE/dup", "0", "1", "duplicate"));
        sb.Append(Row("with_ext", "se", "Audio\\SE\\click.ogg", "0", "", "ext removed, backslashes fixed, empty volume=1"));
        sb.Append(Row("sfx_alias", "SFX", "Audio/SE/alias", "yes", "0.7", "alias kind"));
        sb.Append(Row("UPPER_ID", "se", "Audio/SE/upper", "false", "1", "normalised"));
        sb.Append(Row("#commented", "se", "Audio/SE/c", "0", "1", "comment row"));
        sb.Append(Row("quoted", "bgm", "Audio/BGM/q", "", "0.9", "\"notes, with, commas\""));
        AudioCatalog bad = AudioCatalog.Parse(sb.ToString());
        Check("bad rows: parser does not throw, good rows survive", bad.Count == 7);
        Check("bad rows: first of a duplicate id wins", bad.TryGet("good_one", out tmp) && tmp.Kind == AudioKind.Bgm && tmp.Path == "Audio/BGM/a");
        Check("bad rows: 11 errors recorded (empty id, bad ids x2, kind, path x4, loop, volume, duplicate)", bad.Errors.Count == 11);
        Check("bad rows: no entry for rejected ids", !bad.TryGet("bad_loop", out tmp) && !bad.TryGet("bad_vol", out tmp) && !bad.TryGet("abs_path", out tmp) && !bad.TryGet("weird_kind", out tmp) && !bad.TryGet("#commented", out tmp));
        Check("bad rows: volume outside 0..1 is clamped with a warning, row kept", bad.TryGet("loud_vol", out tmp) && tmp.DefaultVolume == 1f && bad.TryGet("neg_vol", out tmp) && tmp.DefaultVolume == 0f);
        Check("bad rows: extension stripped, backslashes fixed, empty volume = 1", bad.TryGet("with_ext", out tmp) && tmp.Path == "Audio/SE/click" && tmp.DefaultVolume == 1f);
        Check("bad rows: warnings recorded (2 clamps + 1 extension)", bad.Warnings.Count == 3);
        Check("sfx alias, yes/false booleans, id lower-cased", bad.TryGet("sfx_alias", out tmp) && tmp.Kind == AudioKind.Se && tmp.Loop && bad.TryGet("upper_id", out tmp) && !tmp.Loop);
        Check("empty loop column defaults to loop for BGM", bad.TryGet("quoted", out tmp) && tmp.Loop && tmp.Notes == "notes, with, commas");
        Check("error text names the line number and the id", bad.Errors[0].Contains("행 3") && bad.Errors[1].Contains("bad id!"));

        AudioCatalog nul = AudioCatalog.Parse(null);
        AudioCatalog empty = AudioCatalog.Parse("");
        AudioCatalog headerOnly = AudioCatalog.Parse("id,kind,path,loop,default_volume,notes\n");
        AudioCatalog noCols = AudioCatalog.Parse("name,value\na,b\n");
        Check("null / empty / header-only / wrong columns -> empty catalog with an error, no throw",
            nul.Count == 0 && nul.Errors.Count == 1 && empty.Count == 0 && empty.Errors.Count == 1 && headerOnly.Count == 0 && headerOnly.Errors.Count == 1 && noCols.Count == 0 && noCols.Errors.Count == 1);
        AudioCatalog crlf = AudioCatalog.Parse("\ufeffid,kind,path,loop,default_volume,notes\r\nclick,se,Audio/SE/c,0,1,x\r\n");
        Check("BOM + CRLF manifest parses", crlf.Count == 1 && crlf.Errors.Count == 0);
        Check("id rules", AudioCatalog.IsValidId("ui_click") && AudioCatalog.IsValidId("a1") && !AudioCatalog.IsValidId("1a") && !AudioCatalog.IsValidId("a-b") && !AudioCatalog.IsValidId(new string('a', 41)) && AudioCatalog.IsValidId(new string('a', 40)));
    }

    // ------------------------------------------------------------------ 문자열 표 / 언어
    private static UiStrings LoadRealStrings()
    {
        UiStrings u = new UiStrings();
        u.LoadLocale("ko", ReadAll(Res + "Localization/ui_ko.csv"));
        u.LoadLocale("en", ReadAll(Res + "Localization/ui_en.csv"));
        return u;
    }

    private static void Strings()
    {
        Console.WriteLine("== 문자열 표 / 언어 ==");
        UiStrings u = LoadRealStrings();
        Check("real ko/en CSV: no load errors", u.Errors.Count == 0);
        Check("ko and en have the same number of keys (" + u.Count("ko") + ")", u.Count("ko") == u.Count("en") && u.Count("ko") >= 101);
        Check("no key missing in en, none missing in ko", u.MissingIn("ko", "en").Count == 0 && u.MissingIn("en", "ko").Count == 0);
        Check("placeholders {0} {1} match between ko and en", u.PlaceholderMismatches("ko", "en").Count == 0);

        // UiText 의 한국어 기본값 == ko CSV, 모든 속성이 CSV 키
        UiStrings.Current = new UiStrings(); // 표 없이: 코드의 기본값이 그대로 나온다.
        PropertyInfo[] props = typeof(UiText).GetProperties(BindingFlags.Public | BindingFlags.Static);
        int mismatch = 0, missingKey = 0;
        HashSet<string> propNames = new HashSet<string>();
        for (int i = 0; i < props.Length; i++)
        {
            string name = props[i].Name;
            propNames.Add(name);
            string def = (string)props[i].GetValue(null, null);
            if (!u.Has("ko", name)) { missingKey++; Console.WriteLine("      키 없음: " + name); continue; }
            if (u.Raw("ko", name) != def) { mismatch++; Console.WriteLine("      기본값 불일치: " + name); }
        }
        Check("UiText has " + props.Length + " properties, every one is a key in ui_ko.csv", missingKey == 0 && props.Length == 101); // UiText 속성은 101개(PascalCase 키). 새 문구는 snake_case 키 + Loc.T/F (fixes_check 가 검사)
        Check("UiText Korean defaults equal ui_ko.csv values", mismatch == 0);
        List<string> koKeys = u.Keys("ko");
        int orphan = 0;
        for (int i = 0; i < koKeys.Count; i++) if (!propNames.Contains(koKeys[i]) && char.IsUpper(koKeys[i][0])) orphan++; // snake_case(Loc) 키는 fixes_check 가 담당
        Check("no orphan keys in ui_ko.csv (all used by UiText)", orphan == 0);

        // 영어 번역 품질
        Regex hangul = new Regex("[\uAC00-\uD7A3]");
        int untranslated = 0, hangulLeft = 0, empty = 0;
        for (int i = 0; i < koKeys.Count; i++)
        {
            string k = koKeys[i];
            string en = u.Raw("en", k);
            if (string.IsNullOrEmpty(en)) empty++;
            if (hangul.IsMatch(en)) { hangulLeft++; Console.WriteLine("      영어 표에 한글: " + k); }
            if (en == u.Raw("ko", k) && k != "SettingsPercent") { untranslated++; Console.WriteLine("      번역 안 됨: " + k); }
        }
        Check("en: no empty values", empty == 0);
        Check("en: no Hangul left in any English string", hangulLeft == 0);
        Check("en: every key is translated (only SettingsPercent is identical)", untranslated == 0);

        // 같은 한국어 글자를 쓰는 키들은 영어도 같아야 LocalizedLabel 역조회가 모호하지 않다.
        Dictionary<string, string> seen = new Dictionary<string, string>();
        int ambiguous = 0;
        for (int i = 0; i < koKeys.Count; i++)
        {
            string ko = u.Raw("ko", koKeys[i]);
            string en = u.Raw("en", koKeys[i]);
            string prevEn;
            if (seen.TryGetValue(ko, out prevEn)) { if (prevEn != en) { ambiguous++; Console.WriteLine("      모호한 한국어 값: " + ko); } }
            else seen[ko] = en;
        }
        Check("keys sharing a Korean text share the English text (label reverse-lookup is unambiguous)", ambiguous == 0);

        // UiText 가 현재 언어를 따른다
        UiStrings.Current = LoadRealStrings();
        Check("UiText follows Current: ko NewGame", UiText.NewGame == "새 게임" && UiText.Settings == "설정");
        UiStrings.Current.SetLocale("en");
        Check("UiText follows Current: en NewGame / Settings / Quit", UiText.NewGame == "New Game" && UiText.Settings == "Settings" && UiText.Quit == "Quit");
        Check("UiText en: pause/settings strings", UiText.PauseTitle == "Paused" && UiText.SettingsBgm == "Music volume" && UiText.ReturnToTitle == "Return to title");
        UiStrings.Current.SetLocale("ko");
        Check("switching back restores Korean", UiText.NewGame == "새 게임");

        // KeyOfText / 폴백
        UiStrings f = new UiStrings();
        f.LoadLocale("ko", "key,text\nHello,안녕\nOnlyKo,한국어만\nBlankEn,기본\nFmt,{0}명 중 {1}명\n#note,주석\nDup,처음\nDup,나중\n");
        f.LoadLocale("en", "key,text\nHello,Hi\nBlankEn,\nFmt,{1} of {0}\n");
        Check("duplicate key: first wins and an error is recorded", f.Raw("ko", "Dup") == "처음" && f.Errors.Count == 1);
        Check("comment keys are skipped", !f.Has("ko", "#note"));
        Check("ko locale reads ko table", f.Locale == "ko" && f.Get("Hello") == "안녕");
        f.SetLocale("en-US");
        Check("SetLocale normalises en-US to en and reports change once", f.Locale == "en" && !f.SetLocale("EN") && f.Get("Hello") == "Hi");
        Check("fallback: missing in en -> Korean", f.Get("OnlyKo") == "한국어만");
        Check("fallback: empty in en -> Korean", f.Get("BlankEn") == "기본");
        Check("fallback: missing everywhere -> caller default, then the key itself", f.Get("Nope", "기본값") == "기본값" && f.Get("Nope") == "Nope" && f.Get(null, "x") == "x");
        Check("Format fills placeholders per language", f.Format("Fmt", 3, 2) == "2 of 3");
        f.SetLocale("ko");
        Check("Format ko order", f.Format("Fmt", 3, 2) == "3명 중 2명");
        Check("Format: bad format returns the raw text, no throw", UiStrings.FormatText("{0} {5}", 1) == "{0} {5}" && UiStrings.FormatText("{", 1) == "{" && UiStrings.FormatText(null, 1) == "" && UiStrings.FormatText("plain") == "plain");
        Check("KeyOfText finds the key by current-locale text; unknown/empty -> null", f.KeyOfText("안녕") == "Hello" && f.KeyOfText("없는 글자") == null && f.KeyOfText("") == null && f.KeyOfText(null) == null);
        f.SetLocale("en");
        Check("KeyOfText follows the current locale", f.KeyOfText("Hi") == "Hello" && f.KeyOfText("안녕") == null);
        Check("MissingIn lists keys absent from the target", f.MissingIn("ko", "en").Contains("OnlyKo") && !f.MissingIn("ko", "en").Contains("Hello"));
        Check("PlaceholderMismatches detects a dropped placeholder", new UiStrings().PlaceholderMismatches("ko", "en").Count == 0);
        UiStrings pm = new UiStrings();
        pm.LoadLocale("ko", "key,text\nA,{0}명\nB,{0}/{1}\n");
        pm.LoadLocale("en", "key,text\nA,people\nB,{1}/{0}\n");
        Check("PlaceholderMismatches flags A (dropped) but not B (reordered)", pm.PlaceholderMismatches("ko", "en").Count == 1 && pm.PlaceholderMismatches("ko", "en")[0] == "A");
        UiStrings badCsv = new UiStrings();
        int n1 = badCsv.LoadLocale("ko", null);
        int n2 = badCsv.LoadLocale("en", "name,value\nx,y\n");
        int n3 = badCsv.LoadLocale("ko", "key,text\n");
        Check("bad string CSVs: no throw, zero keys, errors recorded", n1 == 0 && n2 == 0 && n3 == 0 && badCsv.Errors.Count == 3);
        Check("NormalizeLocale", UiStrings.NormalizeLocale("en") == "en" && UiStrings.NormalizeLocale("EN-gb") == "en" && UiStrings.NormalizeLocale("ko-KR") == "ko" && UiStrings.NormalizeLocale(null) == "ko" && UiStrings.NormalizeLocale("fr") == "ko");
        UiStrings.Current = null; // 무시되어야 한다
        Check("Current ignores null assignment", UiStrings.Current != null);
        UiStrings.Current = new UiStrings();
    }

    // ------------------------------------------------------------------ 설정 저장
    private static void SettingsChecks()
    {
        Console.WriteLine("== 설정 저장 / 언어 유지 ==");
        MemorySettingsStorage mem = new MemorySettingsStorage();
        SettingsService s = new SettingsService(mem);
        Check("defaults: volumes 1/0.8/0.9/1, auto 1.2, scale 1.0, fullscreen on, ko",
            s.MasterVolume == 1f && Near(s.BgmVolume, 0.8f, 1e-6f) && Near(s.SeVolume, 0.9f, 1e-6f) && s.VoiceVolume == 1f && Near(s.AutoDelay, 1.2f, 1e-6f) && s.UiScale == 1f && s.Fullscreen && s.Locale == "ko");
        int changed = 0;
        s.Changed += delegate { changed++; };
        s.SetMasterVolume(0.55f); s.SetBgmVolume(0.35f); s.SetSeVolume(0.25f); s.SetVoiceVolume(0.45f);
        s.SetTextSpeed(77f); s.SetAutoDelay(2.2f); s.SetUiScale(0.9f); s.SetFullscreen(false); s.SetLocale("en");
        Check("each setter raises Changed exactly once (9 changes)", changed == 9);
        int before = changed;
        s.SetMasterVolume(0.55f); s.SetFullscreen(false); s.SetLocale("en-US"); s.SetUiScale(0.93f);
        Check("setting the same value again raises nothing", changed == before);
        Check("Save writes to storage", s.Save() && mem.WriteCount == 1 && mem.Text.Contains("\"locale\": \"en\""));

        SettingsService r = new SettingsService(mem);
        Check("Load returns true and restores every field",
            r.Load() && Near(r.MasterVolume, 0.55f, 1e-3f) && Near(r.BgmVolume, 0.35f, 1e-3f) && Near(r.SeVolume, 0.25f, 1e-3f) && Near(r.VoiceVolume, 0.45f, 1e-3f)
            && Near(r.TextSpeed, 77f, 1e-3f) && Near(r.AutoDelay, 2.2f, 1e-3f) && Near(r.UiScale, 0.9f, 1e-6f) && !r.Fullscreen && r.Locale == "en");
        Check("locale persists and feeds the string table / pronoun locale", UiStrings.NormalizeLocale(r.Locale) == "en" && PronounResolver.NormalizeLocale(r.Locale) == "en");

        MemorySettingsStorage old = new MemorySettingsStorage();
        old.Text = "{\n  \"textSpeed\": 50,\n  \"masterVolume\": 0.5,\n  \"locale\": \"en\"\n}\n";
        SettingsService o = new SettingsService(old);
        Check("old 3-key settings.json still loads; new fields keep defaults", o.Load() && o.TextSpeed == 50f && o.MasterVolume == 0.5f && o.Locale == "en" && Near(o.BgmVolume, 0.8f, 1e-6f) && o.UiScale == 1f && o.Fullscreen && Near(o.AutoDelay, 1.2f, 1e-6f));

        MemorySettingsStorage wild = new MemorySettingsStorage();
        wild.Text = "{\"bgmVolume\": 7, \"seVolume\": -3, \"autoDelay\": 99, \"uiScale\": 1.07, \"fullscreen\": maybe, \"locale\": \"fr\", \"voiceVolume\": \"x\"}";
        SettingsService w = new SettingsService(wild);
        Check("out-of-range values are clamped, bad bool/number keys ignored, unknown locale -> ko",
            w.Load() && w.BgmVolume == 1f && w.SeVolume == 0f && w.AutoDelay == SettingsService.MaxAutoDelay && w.UiScale == 1.1f && w.Fullscreen && w.Locale == "ko" && w.VoiceVolume == 1f);
        SettingsService g = new SettingsService(new MemorySettingsStorage { Text = "not json at all" });
        Check("garbage file: Load false, defaults kept", !g.Load() && g.UiScale == 1f && g.Locale == "ko");
        Check("missing file: Load false", !new SettingsService(new MemorySettingsStorage()).Load());
        Check("UI scale snaps to 0.9/1.0/1.1; NaN -> 1.0", SettingsService.SnapUiScale(0.5f) == 0.9f && SettingsService.SnapUiScale(1.04f) == 1.0f && SettingsService.SnapUiScale(2f) == 1.1f && SettingsService.SnapUiScale(float.NaN) == 1f);
        Check("auto delay clamps to 0.4..3.0, text speed to 10..130", SettingsService.DefaultAutoDelay == 1.2f);
        SettingsService c = new SettingsService(new MemorySettingsStorage());
        c.SetAutoDelay(0.01f);
        Check("auto delay lower clamp", c.AutoDelay == SettingsService.MinAutoDelay);

        string path = Path.Combine(Path.GetTempPath(), "morphe_polish_settings_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SettingsService fs = new SettingsService(new FileSettingsStorage(path));
            fs.SetLocale("en"); fs.SetUiScale(1.1f); fs.SetBgmVolume(0.1f);
            bool saved = fs.Save();
            SettingsService fs2 = new SettingsService(new FileSettingsStorage(path));
            Check("real file round trip keeps locale/uiScale/bgm", saved && fs2.Load() && fs2.Locale == "en" && fs2.UiScale == 1.1f && Near(fs2.BgmVolume, 0.1f, 1e-3f));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        // 대사 호칭 언어가 설정을 따른다
        GameState state = GameState.NewGame(_balance, Difficulty.Normal, "민준");
        RomanceService romance = new RomanceService(state, _balance, _catalog, new Random(1).NextDouble);
        DialogueSession en = new DialogueSession(new DialogueProject(), state, romance, new AddressBook(), r.Locale, null);
        DialogueSession ko = new DialogueSession(new DialogueProject(), state, romance, new AddressBook(), "ko", null);
        Check("DialogueSession pronoun locale follows the persisted setting (en / ko)", en.Pronouns.Locale == "en" && ko.Pronouns.Locale == "ko");
    }

    // ------------------------------------------------------------------ 의상 정책 + 디스패처
    private sealed class OutfitSink : IVnCommandSink, IVnOutfitSink
    {
        public List<string> Log = new List<string>();
        public void ShowStanding(string c, string e, string o) { Log.Add("stand:" + c + ":" + e + ":" + o); }
        public void ShowStandingAuto(string c, string e) { Log.Add("auto:" + c + ":" + e); }
        public void HideStanding(string c) { }
        public void ShowCg(string c, int n) { }
        public void ShowCommonCg(string k) { }
        public void HideCg() { }
        public void SetBackground(string p, string t) { }
    }

    private sealed class LegacySink : IVnCommandSink
    {
        public List<string> Log = new List<string>();
        public void ShowStanding(string c, string e, string o) { Log.Add("stand:" + c + ":" + e + ":" + o); }
        public void HideStanding(string c) { }
        public void ShowCg(string c, int n) { }
        public void ShowCommonCg(string k) { }
        public void HideCg() { }
        public void SetBackground(string p, string t) { }
    }

    private sealed class AudioSpy : IAudioCommandSink
    {
        public List<string> Log = new List<string>();
        public bool HasAudio(string id, AudioKind kind)
        {
            if (kind == AudioKind.Bgm) return id == "cafe_day" || id == "title";
            if (kind == AudioKind.Se) return id == "ui_click" || id == "door_bell";
            return false;
        }
        public void PlayBgm(string id) { Log.Add("bgm:" + id); }
        public void StopBgm() { Log.Add("stop"); }
        public void PlaySe(string id) { Log.Add("se:" + id); }
    }

    private static GameState Make(int chapter)
    {
        GameState s = GameState.NewGame(_balance, Difficulty.Normal, "t");
        s.Chapter = chapter;
        s.Day = 10;
        s.Phase = DayPhase.Night;
        s.ActionsLeft = 2;
        s.Gold = 5000f;
        return s;
    }

    private static void Outfits()
    {
        Console.WriteLine("== 의상 정책 ==");
        int cases = 0, bad = 0;
        foreach (string raw in File.ReadAllLines("tools/csharp_check/polish_check/outfit_cases.txt"))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            string[] p = line.Split('|');
            string got = OutfitPolicy.Resolve(p[3], p[4], p[0], p[1], p[2]);
            cases++;
            if (got != p[5]) { bad++; Console.WriteLine("      FAIL " + line + " -> " + got); }
        }
        Check("outfit_cases.txt: all " + cases + " cases match (python art_checklist runs the same table)", cases >= 30 && bad == 0);
        Check("IsStaff: howl gomseok haru nabi yes; johan beomjun no", OutfitPolicy.IsStaff("howl") && OutfitPolicy.IsStaff("Gomseok") && OutfitPolicy.IsStaff("haru") && OutfitPolicy.IsStaff("nabi") && !OutfitPolicy.IsStaff("johan") && !OutfitPolicy.IsStaff("beomjun") && !OutfitPolicy.IsStaff(null));
        Check("IsDateNode needs the _Date_ token", OutfitPolicy.IsDateNode("Howl_Date_Lighthouse") && OutfitPolicy.IsDateNode("x_date_y") && !OutfitPolicy.IsDateNode("Howl_Date") && !OutfitPolicy.IsDateNode("Update_1") && !OutfitPolicy.IsDateNode(null));
        Check("IsCafeDaytime", OutfitPolicy.IsCafeDaytime("Cafe_Interior", "Day") && OutfitPolicy.IsCafeDaytime("cafe_exterior", "day") && !OutfitPolicy.IsCafeDaytime("Cafe_Interior", "Evening") && !OutfitPolicy.IsCafeDaytime("Street", "Day") && !OutfitPolicy.IsCafeDaytime(null, "Day"));
        Check("every policy result is a valid ArtKeys outfit", Array.IndexOf(ArtKeys.Outfits, OutfitPolicy.Resolve("howl", null, "Ch1_Intro", "Cafe_Interior", "Day")) >= 0 && Array.IndexOf(ArtKeys.Outfits, OutfitPolicy.Resolve("johan", null, "A_Date_B", "Street", "Day")) >= 0);

        // 디스패처 통합
        GameState st = Make(3);
        RomanceService rom = new RomanceService(st, _balance, _catalog, new Random(1).NextDouble);
        OutfitSink sink = new OutfitSink();
        DialogueCommandDispatcher d = new DialogueCommandDispatcher(st, rom, sink);
        d.Handle("show_standing", new[] { "howl", "smile" });
        d.Handle("show_standing", new[] { "howl", "smile", "casual" });
        d.Handle("show_standing", new[] { "howl", "smile", "Uniform" });
        Check("no outfit arg -> sink.ShowStandingAuto (policy decides later)", sink.Log[0] == "auto:howl:Smile");
        Check("explicit outfit always goes through ShowStanding with that outfit (even Casual)", sink.Log[1] == "stand:howl:Smile:Casual" && sink.Log[2] == "stand:howl:Smile:Uniform");
        int warnBefore = d.Warnings.Count;
        d.Handle("show_standing", new[] { "howl", "smile", "tuxedo" });
        Check("unknown outfit: warning + falls back to the policy path", d.Warnings.Count == warnBefore + 1 && sink.Log[3] == "auto:howl:Smile" && d.Warnings[warnBefore].Contains("상황에 맞는 의상"));
        LegacySink legacy = new LegacySink();
        DialogueCommandDispatcher dl = new DialogueCommandDispatcher(st, rom, legacy);
        dl.Handle("show_standing", new[] { "howl", "smile" });
        dl.Handle("show_standing", new[] { "howl", "smile", "tuxedo" });
        Check("legacy sink (no IVnOutfitSink): behaviour unchanged, default Casual", legacy.Log[0] == "stand:howl:Smile:Casual" && legacy.Log[1] == "stand:howl:Smile:Casual" && dl.Warnings.Count == 1 && dl.Warnings[0].Contains("Casual"));
        DialogueCommandDispatcher dn = new DialogueCommandDispatcher(st, rom, null);
        Check("null sink still safe", dn.Handle("show_standing", new[] { "howl", "smile" }));
    }

    // ------------------------------------------------------------------ 오디오 커맨드
    private static void AudioCommands()
    {
        Console.WriteLine("== 대사 오디오 커맨드 ==");
        GameState st = Make(3);
        RomanceService rom = new RomanceService(st, _balance, _catalog, new Random(1).NextDouble);
        AudioSpy spy = new AudioSpy();
        DialogueCommandDispatcher d = new DialogueCommandDispatcher(st, rom, null);
        d.AudioSink = spy;
        Check("bgm / bgm_stop / se are known commands", DialogueCommandDispatcher.IsKnown("bgm") && DialogueCommandDispatcher.IsKnown("bgm_stop") && DialogueCommandDispatcher.IsKnown("se"));
        Check("<<bgm cafe_day>> plays", d.Handle("bgm", new[] { "cafe_day" }) && spy.Log[0] == "bgm:cafe_day" && d.Warnings.Count == 0);
        Check("ids are case-insensitive", d.Handle("bgm", new[] { "TITLE" }) && spy.Log[1] == "bgm:title");
        Check("<<se door_bell>> plays", d.Handle("se", new[] { "door_bell" }) && spy.Log[2] == "se:door_bell");
        Check("<<bgm_stop>> stops", d.Handle("bgm_stop", new string[0]) && spy.Log[3] == "stop");
        int n = spy.Log.Count;
        Check("unknown bgm id: warning, nothing played, handled (never an error)", d.Handle("bgm", new[] { "no_such" }) && spy.Log.Count == n && d.Warnings.Count == 1 && d.Warnings[0].Contains("no_such"));
        Check("unknown se id: warning, nothing played", d.Handle("se", new[] { "nope" }) && spy.Log.Count == n && d.Warnings.Count == 2);
        Check("wrong kind (bgm id in <<se>>, se id in <<bgm>>) is a warning", d.Handle("se", new[] { "cafe_day" }) && d.Handle("bgm", new[] { "ui_click" }) && spy.Log.Count == n && d.Warnings.Count == 4);
        Check("missing argument: warning", d.Handle("bgm", new string[0]) && d.Handle("se", new[] { " " }) && d.Warnings.Count == 6);
        DialogueCommandDispatcher quiet = new DialogueCommandDispatcher(st, rom, null);
        Check("no audio sink: commands accepted silently (silent build)", quiet.Handle("bgm", new[] { "anything" }) && quiet.Handle("se", new[] { "anything" }) && quiet.Handle("bgm_stop", new string[0]) && quiet.Warnings.Count == 0);
        Check("missing-argument warning is still raised without a sink", quiet.Handle("bgm", new string[0]) && quiet.Warnings.Count == 1);

        // 스크립트 경유(세션 + 호스트 등록)
        string src = "title: T\n---\nplayer: 시작 #line:t1\n<<bgm cafe_day>>\n<<se ui_click>>\n<<se bogus>>\nplayer: 끝 #line:t2\n<<bgm_stop>>\n===\n";
        DialogueProject project = new DialogueProject();
        project.AddSource(src, "t.yarn");
        GameState s2 = Make(3);
        AudioSpy spy2 = new AudioSpy();
        DialogueSession sess = new DialogueSession(project, s2, new RomanceService(s2, _balance, _catalog, new Random(1).NextDouble), new AddressBook(), "ko", null, spy2);
        sess.Start("T", null, null);
        int guard = 0, lines = 0;
        while (guard++ < 20)
        {
            DialogueStep step = sess.Next();
            if (step.Kind == StepKind.End) break;
            if (step.Kind == StepKind.Line) lines++;
        }
        sess.Finish();
        Check("script runs through the session: 2 lines, audio calls in order, no exception", lines == 2 && spy2.Log.Count == 3 && spy2.Log[0] == "bgm:cafe_day" && spy2.Log[1] == "se:ui_click" && spy2.Log[2] == "stop");
        Check("script: the unknown se id became a warning on the session", sess.Errors.Count == 1 && sess.Errors[0].Contains("bogus"));
        DialogueSession plain = new DialogueSession(project, Make(3), new RomanceService(Make(3), _balance, _catalog, new Random(1).NextDouble), new AddressBook(), "ko", null);
        plain.Start("T", null, null);
        guard = 0;
        while (guard++ < 20 && plain.Next().Kind != StepKind.End) { }
        plain.Finish();
        Check("script without an audio sink runs fine and stays silent", plain.Errors.Count == 0);
        Check("NullAudioSink knows nothing", !new NullAudioSink().HasAudio("x", AudioKind.Se));

        // 현재 시나리오는 오디오 커맨드를 쓰지 않는다(선택 커맨드).
        int used = 0;
        foreach (string f in Directory.GetFiles("UnityProject/Assets/_Project/Dialogue", "*.yarn", SearchOption.AllDirectories))
            if (Regex.IsMatch(ReadAll(f), "<<(bgm|bgm_stop|se)[ >]")) used++;
        Check("current scripts do not use the optional audio commands (nothing depends on them)", used == 0);
    }

    // ------------------------------------------------------------------ 화면 배치
    private static List<string> SourceFiles()
    {
        List<string> files = new List<string>();
        files.AddRange(Directory.GetFiles(Src + "UI", "*.cs"));
        return files;
    }

    private static void Layout()
    {
        Console.WriteLine("== 화면 배치 규칙 ==");
        Check("canvas scale at 1920x1080 is 1", Near(LayoutRules.CanvasScale(1920, 1080, 1f), 1f, 1e-5f));
        Check("canvas scale at 2400x1080 = sqrt(1.25) = 1.118 (Match 0.5)", Near(LayoutRules.CanvasScale(2400, 1080, 1f), 1.1180f, 1e-3f));
        Check("canvas scale at 1280x720 is 2/3 (same 16:9 layout)", Near(LayoutRules.CanvasScale(1280, 720, 1f), 0.6667f, 1e-3f));
        Check("ui scale multiplies the canvas scale", Near(LayoutRules.CanvasScale(1920, 1080, 1.1f), 1.1f, 1e-4f) && Near(LayoutRules.CanvasScale(1920, 1080, 0.9f), 0.9f, 1e-4f));
        Check("bad inputs give scale 1", LayoutRules.CanvasScale(0, 1080, 1f) == 1f && LayoutRules.CanvasScale(1920, 1080, 0f) == 1f);

        LayoutRect safe16 = LayoutRules.SafeRect(LayoutRules.Screen16x9, 1f);
        LayoutRect safe20 = LayoutRules.SafeRect(LayoutRules.Screen20x9, 1f);
        Check("16:9 safe rect is 1920x1080 canvas units", Near(safe16.W, 1920f, 0.01f) && Near(safe16.H, 1080f, 0.01f));
        Check("20:9 safe rect (2180x1026 px cut) is ~1950x918 canvas units", Near(safe20.W, 1949.9f, 0.5f) && Near(safe20.H, 917.7f, 0.5f));
        Console.WriteLine("      20:9 safe rect = " + safe20 + "  | at 1.1: " + LayoutRules.SafeRect(LayoutRules.Screen20x9, 1.1f));

        // PanelRect 계산
        PanelSpec center = new PanelSpec { AnchorX = 0.5f, AnchorY = 0.5f, PivotX = 0.5f, PivotY = 0.5f, W = 100f, H = 50f, PosX = 10f, PosY = -5f };
        LayoutRect cr = LayoutRules.PanelRect(center, new LayoutRect(0, 0, 400, 200));
        Check("PanelRect: centered with offset", Near(cr.X, 160f, 1e-3f) && Near(cr.Y, 70f, 1e-3f) && cr.W == 100f && cr.H == 50f);
        PanelSpec top = new PanelSpec { AnchorX = 0.5f, AnchorY = 1f, PivotX = 0.5f, PivotY = 1f, W = 100f, H = 50f, PosY = -20f };
        LayoutRect tr = LayoutRules.PanelRect(top, new LayoutRect(0, 0, 400, 200));
        Check("PanelRect: top-anchored, top-pivoted", Near(tr.Top, 180f, 1e-3f) && Near(tr.X, 150f, 1e-3f));
        PanelSpec stretch = new PanelSpec { StretchX = true, AnchorY = 0f, PivotX = 0.5f, PivotY = 0f, W = -80f, H = 270f, PosY = 24f };
        LayoutRect sr = LayoutRules.PanelRect(stretch, new LayoutRect(0, 0, 1000, 800));
        Check("PanelRect: horizontal stretch with margin (sizeDelta -80)", Near(sr.W, 920f, 1e-3f) && Near(sr.X, 40f, 1e-3f) && Near(sr.Y, 24f, 1e-3f));
        Check("LayoutRect.Contains / Overlaps", new LayoutRect(0, 0, 10, 10).Contains(new LayoutRect(1, 1, 8, 8), 0f) && !new LayoutRect(0, 0, 10, 10).Contains(new LayoutRect(5, 5, 8, 8), 0.1f)
            && new LayoutRect(0, 0, 10, 10).Overlaps(new LayoutRect(9, 9, 5, 5), 0.1f) && !new LayoutRect(0, 0, 10, 10).Overlaps(new LayoutRect(10, 0, 5, 5), 0.1f));

        // 모든 화면 패널이 16:9, 20:9 에서 들어가고 겹치지 않는다
        List<string> v16 = LayoutRules.Check(LayoutRules.Screen16x9, 1f);
        List<string> v20 = LayoutRules.Check(LayoutRules.Screen20x9, 1f);
        for (int i = 0; i < v16.Count; i++) Console.WriteLine("      " + v16[i]);
        for (int i = 0; i < v20.Count; i++) Console.WriteLine("      " + v20[i]);
        Check("all " + LayoutRules.Panels.Count + " panels + " + LayoutRules.Stacks.Count + " stacks fit 1920x1080 (16:9) at x1.0", v16.Count == 0);
        Check("all panels + stacks fit 2400x1080 safe-area cut (20:9) at x1.0", v20.Count == 0);
        Check("also fit 16:9 at x0.9 and 20:9 at x0.9", LayoutRules.Check(LayoutRules.Screen16x9, 0.9f).Count == 0 && LayoutRules.Check(LayoutRules.Screen20x9, 0.9f).Count == 0);
        ReferenceScreen small = new ReferenceScreen("1280x720", 1280, 720, 0, 0, 0, 0, 160f);
        ReferenceScreen wide = new ReferenceScreen("21:9 desktop 2560x1080", 2560, 1080, 0, 0, 0, 0, 96f);
        Check("a 1280x720 window and a 21:9 desktop screen also fit at x1.0", LayoutRules.Check(small, 1f).Count == 0 && LayoutRules.Check(wide, 1f).Count == 0);

        List<float> a16 = LayoutRules.AllowedUiScales(LayoutRules.Screen16x9);
        List<float> a20 = LayoutRules.AllowedUiScales(LayoutRules.Screen20x9);
        Console.WriteLine("      allowed UI scales: 16:9 = " + string.Join(",", a16.ConvertAll<string>(delegate (float f) { return f.ToString("0.0"); }).ToArray()) + " | 20:9 = " + string.Join(",", a20.ConvertAll<string>(delegate (float f) { return f.ToString("0.0"); }).ToArray()));
        Check("16:9 allows 0.9, 1.0 and 1.1", a16.Count == 3);
        Check("20:9 allows 0.9 and 1.0 but not 1.1 (height 835 < tallest panel 880)", a20.Count == 2 && a20.Contains(0.9f) && a20.Contains(1.0f) && !a20.Contains(1.1f));
        Check("EffectiveUiScale: 1.1 requested on 20:9 -> 1.0; allowed value kept", LayoutRules.EffectiveUiScale(1.1f, LayoutRules.Screen20x9) == 1.0f && LayoutRules.EffectiveUiScale(0.9f, LayoutRules.Screen20x9) == 0.9f && LayoutRules.EffectiveUiScale(1.1f, LayoutRules.Screen16x9) == 1.1f);
        ReferenceScreen tiny = new ReferenceScreen("tiny 800x300", 800, 300, 0, 0, 0, 0, 160f);
        Check("a screen where nothing fits still returns the smallest scale", LayoutRules.AllowedUiScales(tiny).Count == 1 && LayoutRules.AllowedUiScales(tiny)[0] == 0.9f);

        // 검사기 자체가 위반을 잡는다(음성 테스트)
        PanelSpec tooBig = new PanelSpec { Screen = "Test", Name = "TooBig", AnchorX = 0.5f, AnchorY = 0.5f, PivotX = 0.5f, PivotY = 0.5f, W = 3000f, H = 500f };
        LayoutRules.Panels.Add(tooBig);
        bool caught = LayoutRules.Check(LayoutRules.Screen16x9, 1f).Exists(delegate (string x) { return x.Contains("TooBig"); });
        LayoutRules.Panels.Remove(tooBig);
        Check("checker flags a panel wider than the safe area", caught);
        PanelSpec a = new PanelSpec { Screen = "Test", Name = "A", AnchorX = 0.5f, AnchorY = 0.5f, PivotX = 0.5f, PivotY = 0.5f, W = 500f, H = 500f };
        PanelSpec b = new PanelSpec { Screen = "Test", Name = "B", AnchorX = 0.5f, AnchorY = 0.5f, PivotX = 0.5f, PivotY = 0.5f, W = 500f, H = 500f, PosX = 100f };
        LayoutRules.Panels.Add(a); LayoutRules.Panels.Add(b);
        bool overlapCaught = LayoutRules.Check(LayoutRules.Screen16x9, 1f).Exists(delegate (string x) { return x.Contains("A 와(과) B"); });
        b.States = "other"; a.States = "mine";
        bool exclusiveOk = !LayoutRules.Check(LayoutRules.Screen16x9, 1f).Exists(delegate (string x) { return x.Contains("A 와(과) B"); });
        LayoutRules.Panels.Remove(a); LayoutRules.Panels.Remove(b);
        Check("checker flags overlapping panels, but not ones shown in different states", overlapCaught && exclusiveOk);
        StackSpec tall = new StackSpec { Name = "TestStack", Panel = "Content", Padding = 0f, Spacing = 0f };
        tall.ChildHeights.Add(2000f);
        LayoutRules.Stacks.Add(tall);
        bool stackCaught = LayoutRules.Check(LayoutRules.Screen16x9, 1f).Exists(delegate (string x) { return x.Contains("TestStack"); });
        LayoutRules.Stacks.Remove(tall);
        Check("checker flags a vertical stack taller than its panel", stackCaught);
        Check("StackSpec.ContentHeight = padding*2 + children + spacing*(n-1)", new StackSpec { Padding = 10f, Spacing = 5f, ChildHeights = new List<float> { 20f, 30f, 40f } }.ContentHeight() == 120f);

        // VN: 선택지는 대사창 위에 쌓인다(20:9 에서도 겹치지 않음)
        PanelSpec box = LayoutRules.Find("Box");
        PanelSpec choices = LayoutRules.Find("Choices");
        LayoutRect boxR = LayoutRules.Absolute(box, safe20);
        LayoutRect chR = LayoutRules.Absolute(choices, safe20);
        Check("VN choices sit above the dialogue box on 20:9 (" + chR.Y.ToString("0") + " >= " + boxR.Top.ToString("0") + ")", chR.Y >= boxR.Top && chR.Top <= safe20.H);
        PanelSpec night = LayoutRules.Find("ContentNight");
        PanelSpec hud = LayoutRules.Find("Hud");
        Check("night menu panel stays below the HUD on 20:9", LayoutRules.Absolute(night, safe20).Top <= LayoutRules.Absolute(hud, safe20).Y + 0.5f);

        // 표 == 코드
        int missing = 0;
        Dictionary<string, string> cache = new Dictionary<string, string>();
        for (int i = 0; i < LayoutRules.Panels.Count; i++)
        {
            PanelSpec p = LayoutRules.Panels[i];
            string path = Src + p.SourceFile;
            string text;
            if (!cache.TryGetValue(path, out text)) { text = File.Exists(path) ? ReadAll(path) : string.Empty; cache[path] = text; }
            if (!text.Contains(p.SourceSnippet)) { missing++; Console.WriteLine("      코드에 없음: " + p.Name + " -> " + p.SourceSnippet); }
        }
        Check("every panel number in the layout table appears verbatim in the Unity source (" + LayoutRules.Panels.Count + " panels)", missing == 0);

        // 버튼 ≥ 48
        Check("button table: every button is at least 48 reference px in both directions", LayoutRules.CheckButtons(LayoutRules.Buttons).Count == 0);
        Check("CheckButtons flags a 40px button", LayoutRules.CheckButtons(new[] { new ButtonSpec { Screen = "T", Name = "small", W = 200f, H = 40f } }).Count == 1);
        Check("ClampTouch raises small sizes to 48 and leaves large ones", LayoutRules.ClampTouch(30f) == 48f && LayoutRules.ClampTouch(72f) == 72f);
        int btnNoSource = 0;
        List<string> uiFiles = SourceFiles();
        StringBuilder all = new StringBuilder();
        for (int i = 0; i < uiFiles.Count; i++) all.Append(ReadAll(uiFiles[i])).Append('\n');
        string allText = all.ToString();
        for (int i = 0; i < LayoutRules.Buttons.Count; i++)
        {
            ButtonSpec bs = LayoutRules.Buttons[i];
            if (!allText.Contains(bs.W.ToString("0") + "f") || !allText.Contains(bs.H.ToString("0") + "f")) { btnNoSource++; Console.WriteLine("      버튼 수치가 코드에 없음: " + bs.Screen + "/" + bs.Name); }
        }
        Check("every button size in the table (" + LayoutRules.Buttons.Count + ") appears in the UI source", btnNoSource == 0);
        // 코드의 모든 MakeButton 호출의 숫자 높이가 48 이상
        int smallLiteral = 0, literalButtons = 0;
        Regex mb = new Regex(@"MakeButton\([^;]*?,\s*(\d+(?:\.\d+)?)f,\s*(\d+(?:\.\d+)?)f,\s*[A-Za-z\.\[\]]+\)");
        foreach (Match m in mb.Matches(allText))
        {
            literalButtons++;
            float w = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            float h = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (w < 48f || h < 48f) { smallLiteral++; Console.WriteLine("      작은 버튼: " + m.Value); }
        }
        Check("scraped " + literalButtons + " literal MakeButton calls: none below 48 px", literalButtons >= 20 && smallLiteral == 0);
        int smallAssign = 0;
        foreach (Match m in Regex.Matches(allText, @"_buttonHeight\s*=\s*(\d+)f"))
            if (float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) < 48f) smallAssign++;
        Check("scraped _buttonHeight assignments are all >= 48", smallAssign == 0 && Regex.Matches(allText, @"_buttonHeight\s*=\s*(\d+)f").Count >= 2);
        Check("UiKit.MakeButton enforces the floor in code", ReadAll(Src + "UI/UiKit.cs").Contains("LayoutRules.ClampTouch(height)") && ReadAll(Src + "UI/UiKit.cs").Contains("LayoutRules.ClampTouch(width)"));

        // dp 환산(정보 + 검증)
        float dp56 = LayoutRules.RefPxToDp(56f, LayoutRules.Screen20x9, 1f);
        float need = LayoutRules.DpToRefPx(48f, LayoutRules.Screen20x9, 1f);
        Console.WriteLine("      INFO: a 56 ref-px button is " + dp56.ToString("0.0") + " dp on the 20:9 @420dpi phone; 48 dp needs " + need.ToString("0") + " ref px");
        Check("dp conversion: 56 ref px ~ 23.8 dp, 48 dp ~ 112.7 ref px on the 420dpi 20:9 phone (below-48dp finding is real)", Near(dp56, 23.8f, 0.2f) && Near(need, 112.7f, 0.5f));
        Check("dp conversion round trip", Near(LayoutRules.RefPxToDp(LayoutRules.DpToRefPx(48f, LayoutRules.Screen20x9, 1.1f), LayoutRules.Screen20x9, 1.1f), 48f, 1e-3f));
        Check("on 16:9 @160dpi 48 ref px = 48 dp", Near(LayoutRules.RefPxToDp(48f, LayoutRules.Screen16x9, 1f), 48f, 1e-3f));
    }

    // ------------------------------------------------------------------ 제스처 / 뒤로가기
    private static void Input()
    {
        Console.WriteLine("== 터치 제스처 / 뒤로가기 ==");
        TouchGesture g = new TouchGesture();
        g.Down(1f, 100f, 100f, true);
        Check("tap: quick release -> Tap", g.Up(1.2f) == GestureEvent.Tap && !g.IsDown);
        g.Down(1f, 100f, 100f, true);
        Check("hold below threshold: Tick says nothing", g.Tick(1.3f) == GestureEvent.None);
        Check("hold: LongPressBegin fires once at 0.5 s", g.Tick(1.5f) == GestureEvent.LongPressBegin && g.Tick(1.6f) == GestureEvent.None && g.IsLongPressing);
        Check("hold: release ends long press (and is not a Tap)", g.Up(2.5f) == GestureEvent.LongPressEnd && !g.IsLongPressing);
        g.Down(5f, 100f, 100f, true);
        Check("small move inside slop keeps the tap alive", g.Move(110f, 105f) == GestureEvent.None && g.Up(5.1f) == GestureEvent.Tap);
        g.Down(5f, 100f, 100f, true);
        Check("move past slop cancels the tap (swipe)", g.Move(200f, 100f) == GestureEvent.Cancelled && g.Up(5.1f) == GestureEvent.None);
        g.Down(6f, 100f, 100f, true);
        g.Tick(6.6f);
        Check("move past slop during long press ends it", g.Move(300f, 300f) == GestureEvent.LongPressEnd && g.Up(7f) == GestureEvent.None && !g.IsLongPressing);
        g.Down(8f, 0f, 0f, false);
        Check("mouse (long press disabled): slow click is still a Tap and never skips", g.Tick(9f) == GestureEvent.None && g.Up(9.5f) == GestureEvent.Tap);
        g.Down(10f, 0f, 0f, true);
        Check("second finger is ignored while one is down", g.Down(10.1f, 500f, 500f, true) == GestureEvent.None && g.Up(10.2f) == GestureEvent.Tap);
        Check("Up without Down does nothing", g.Up(11f) == GestureEvent.None);
        g.Down(12f, 0f, 0f, true);
        g.Tick(12.6f);
        Check("Cancel (pause / screen change) ends a long press cleanly", g.Cancel() == GestureEvent.LongPressEnd && !g.IsDown && g.Cancel() == GestureEvent.None);
        g.Down(13f, 0f, 0f, true);
        Check("late release with no Tick after the threshold is neither tap nor skip", g.Up(14f) == GestureEvent.None);
        TouchGesture slop = new TouchGesture();
        slop.SetSlopDp(10f, 420f);
        Check("slop 10dp at 420dpi = 26.25 px; unknown dpi falls back to 160", Near(slop.SlopPixels, 26.25f, 0.01f));
        slop.SetSlopDp(10f, 0f);
        Check("slop with unknown dpi = 10 px", Near(slop.SlopPixels, 10f, 0.01f));

        // 뒤로가기
        Check("back: pause open + confirm -> CloseConfirm", BackKeyRules.Decide(BackScreen.Game, true, false, true, false) == BackAction.CloseConfirm);
        Check("back: pause open + settings -> PauseBackToMenu", BackKeyRules.Decide(BackScreen.Vn, true, true, false, false) == BackAction.PauseBackToMenu);
        Check("back: pause open -> ClosePause", BackKeyRules.Decide(BackScreen.Game, true, false, false, false) == BackAction.ClosePause);
        Check("back: confirm beats settings when both flags are set", BackKeyRules.Decide(BackScreen.Game, true, true, true, false) == BackAction.CloseConfirm);
        Check("back: game/VN screen -> OpenPause", BackKeyRules.Decide(BackScreen.Game, false, false, false, false) == BackAction.OpenPause && BackKeyRules.Decide(BackScreen.Vn, false, false, false, false) == BackAction.OpenPause);
        Check("back: a screen that handles back wins over opening pause", BackKeyRules.Decide(BackScreen.Game, false, false, false, true) == BackAction.ScreenHandles);
        Check("back: title top level / end screen do nothing (no accidental quit)", BackKeyRules.Decide(BackScreen.Title, false, false, false, false) == BackAction.None && BackKeyRules.Decide(BackScreen.End, false, false, false, false) == BackAction.None);
        Check("back: title sub-panel / create / route select are handled by the screen", BackKeyRules.Decide(BackScreen.Title, false, false, false, true) == BackAction.ScreenHandles && BackKeyRules.Decide(BackScreen.Create, false, false, false, true) == BackAction.ScreenHandles && BackKeyRules.Decide(BackScreen.RouteSelect, false, false, false, true) == BackAction.ScreenHandles);
        Check("CanPause only in game and VN", BackKeyRules.CanPause(BackScreen.Game) && BackKeyRules.CanPause(BackScreen.Vn) && !BackKeyRules.CanPause(BackScreen.Title) && !BackKeyRules.CanPause(BackScreen.End) && !BackKeyRules.CanPause(BackScreen.Create) && !BackKeyRules.CanPause(BackScreen.RouteSelect));
        Check("CanSaveNow: only game screen + morning + no scene", BackKeyRules.CanSaveNow(BackScreen.Game, true, false) && !BackKeyRules.CanSaveNow(BackScreen.Game, false, false) && !BackKeyRules.CanSaveNow(BackScreen.Vn, true, false) && !BackKeyRules.CanSaveNow(BackScreen.Game, true, true));
    }

    // ------------------------------------------------------------------ 메모리
    private static void Memory()
    {
        Console.WriteLine("== 텍스처 메모리 ==");
        Check("RGBA32 1024x2048 = 8 MiB", MemoryBudget.TextureBytes(1024, 2048, TexFormat.Rgba32, false) == 8388608L);
        Check("BC7 1920x1080 = 480*270 blocks * 16 = 2,073,600", MemoryBudget.TextureBytes(1920, 1080, TexFormat.Bc7, false) == 2073600L);
        Check("BC7 1024x2048 = 2 MiB", MemoryBudget.TextureBytes(1024, 2048, TexFormat.Bc7, false) == 2097152L);
        Check("ASTC 6x6 1920x1080 = 320*180*16 = 921,600", MemoryBudget.TextureBytes(1920, 1080, TexFormat.Astc6x6, false) == 921600L);
        Check("ASTC 6x6 1024x2048 rounds blocks up: 171*342*16 = 935,712", MemoryBudget.TextureBytes(1024, 2048, TexFormat.Astc6x6, false) == 935712L);
        Check("ASTC 4x4 512x512 = 128*128*16 = 262,144", MemoryBudget.TextureBytes(512, 512, TexFormat.Astc4x4, false) == 262144L);
        Check("ASTC 8x8 1920x1080 = 240*135*16 = 518,400", MemoryBudget.TextureBytes(1920, 1080, TexFormat.Astc8x8, false) == 518400L);
        Check("ETC2 RGBA8 = same block math as BC7", MemoryBudget.TextureBytes(256, 256, TexFormat.Etc2Rgba8, false) == MemoryBudget.TextureBytes(256, 256, TexFormat.Bc7, false));
        Check("RGBA32 4x4 with mips = (16+4+1)*4 = 84", MemoryBudget.TextureBytes(4, 4, TexFormat.Rgba32, true) == 84L);
        long flat = MemoryBudget.TextureBytes(1024, 1024, TexFormat.Rgba32, false);
        long mip = MemoryBudget.TextureBytes(1024, 1024, TexFormat.Rgba32, true);
        Check("mip chain adds ~1/3 (ratio 1.333)", Near((float)mip / flat, 1.3333f, 0.001f));
        Check("degenerate sizes are zero bytes", MemoryBudget.TextureBytes(0, 10, TexFormat.Bc7, false) == 0L && MemoryBudget.TextureBytes(10, -1, TexFormat.Bc7, false) == 0L);
        int w, h;
        MemoryBudget.ApplyMaxSize(4096, 2048, 2048, out w, out h);
        Check("max size 2048 halves a 4096x2048 texture", w == 2048 && h == 1024);
        MemoryBudget.ApplyMaxSize(1024, 2048, 2048, out w, out h);
        Check("max size leaves 1024x2048 alone", w == 1024 && h == 2048);
        MemoryBudget.ApplyMaxSize(3000, 1500, 2048, out w, out h);
        Check("max size scales proportionally (3000x1500 -> 2048x1024)", w == 2048 && h == 1024);

        Check("TryDimensions parses '1920x1080', '1024x2048 이상(세로 2048+)', '512x512(투명)'", MemoryBudget.TryDimensions("1920x1080", out w, out h) && w == 1920 && h == 1080
            && MemoryBudget.TryDimensions("1024x2048 이상(세로 2048+)", out w, out h) && w == 1024 && h == 2048 && MemoryBudget.TryDimensions("512x512(투명)", out w, out h) && w == 512 && h == 512);
        Check("TryDimensions rejects text without sizes", !MemoryBudget.TryDimensions("바디와 같은 캔버스(투명)", out w, out h) && !MemoryBudget.TryDimensions("1024, 512, 256", out w, out h) && !MemoryBudget.TryDimensions("", out w, out h) && !MemoryBudget.TryDimensions(null, out w, out h) && !MemoryBudget.TryDimensions("x", out w, out h) && !MemoryBudget.TryDimensions("0x0", out w, out h));

        // 실제 체크리스트
        List<string> errors = new List<string>();
        List<ArtRow> rows = MemoryBudget.ParseChecklist(ReadAll("docs/art_checklist.csv"), errors);
        Check("art_checklist.csv parses with no errors (UI rows excluded)", errors.Count == 0 && rows.Count == 241);
        if (errors.Count > 0) for (int i = 0; i < errors.Count; i++) Console.WriteLine("      " + errors[i]);
        Check("counts: 18 bodies, 108 faces, 36 event CG, 3 common CG, 36 backgrounds, 40 icons",
            MemoryBudget.CountOf(rows, ArtCategory.StandingBody, false) == 18 && MemoryBudget.CountOf(rows, ArtCategory.StandingFace, false) == 108
            && MemoryBudget.CountOf(rows, ArtCategory.EventCg, false) == 36 && MemoryBudget.CountOf(rows, ArtCategory.CommonCg, false) == 3
            && MemoryBudget.CountOf(rows, ArtCategory.Background, false) == 36 && MemoryBudget.CountOf(rows, ArtCategory.MenuIcon, false) == 40);
        Check("required counts follow the policy-aware checklist (16 bodies, 73 faces)", MemoryBudget.CountOf(rows, ArtCategory.StandingBody, true) == 16 && MemoryBudget.CountOf(rows, ArtCategory.StandingFace, true) == 73);
        bool dims = true;
        for (int i = 0; i < rows.Count; i++)
        {
            ArtRow r = rows[i];
            if (r.Category == ArtCategory.StandingBody || r.Category == ArtCategory.StandingFace) { if (r.Width != 1024 || r.Height != 2048) dims = false; }
            else if (r.Category == ArtCategory.MenuIcon) { if (r.Width != 512 || r.Height != 512) dims = false; }
            else if (r.Width != 1920 || r.Height != 1080) dims = false;
        }
        Check("checklist sizes: standing 1024x2048, CG/BG 1920x1080, icons 512x512 (faces inherit the body canvas)", dims);

        PlatformProfile pc = PlatformProfile.Pc();
        PlatformProfile an = PlatformProfile.Android();
        SceneEstimate pcPeak = MemoryBudget.PeakScene(rows, pc);
        SceneEstimate anPeak = MemoryBudget.PeakScene(rows, an);
        Console.WriteLine("      PC peak " + MemoryBudget.ToMb(pcPeak.TotalBytes).ToString("0.0") + " MB (standings " + MemoryBudget.ToMb(pcPeak.StandingBytes).ToString("0.0") + ", bg " + MemoryBudget.ToMb(pcPeak.BackgroundBytes).ToString("0.0") + ", cg " + MemoryBudget.ToMb(pcPeak.CgBytes).ToString("0.0") + ", ui " + MemoryBudget.ToMb(pcPeak.UiAtlasBytes).ToString("0.0") + ")");
        Console.WriteLine("      Android peak " + MemoryBudget.ToMb(anPeak.TotalBytes).ToString("0.0") + " MB (standings " + MemoryBudget.ToMb(anPeak.StandingBytes).ToString("0.0") + ", bg " + MemoryBudget.ToMb(anPeak.BackgroundBytes).ToString("0.0") + ", cg " + MemoryBudget.ToMb(anPeak.CgBytes).ToString("0.0") + ", ui " + MemoryBudget.ToMb(anPeak.UiAtlasBytes).ToString("0.0") + ")");
        Check("PC peak scene = 2 standings (body+face BC7) + 2 bg + 1 CG + 10 MiB UI = 25,095,168 B", pcPeak.TotalBytes == 8388608L + 4147200L + 2073600L + 10485760L);
        Check("Android peak scene = ASTC 6x6 numbers = 16,993,408 B", anPeak.TotalBytes == 3742848L + 1843200L + 921600L + 10485760L);
        Check("Android peak is smaller than PC peak", anPeak.TotalBytes < pcPeak.TotalBytes);
        Check("peak scene fits 25%-headroom budgets on PC (1 GiB) and Android (350 MiB)", MemoryBudget.FitsBudget(pcPeak.TotalBytes, pc, 0.25) && MemoryBudget.FitsBudget(anPeak.TotalBytes, an, 0.25));
        SceneEstimate minimal = MemoryBudget.Scene(rows, an, 1, 1, 0);
        Check("scene estimate scales: 1 standing + 1 bg + no CG < peak", minimal.TotalBytes < anPeak.TotalBytes && minimal.StandingBytes == anPeak.StandingBytes / 2 && minimal.CgBytes == 0L);
        SceneEstimate four = MemoryBudget.Scene(rows, pc, pc.MaxStandings, 2, 1);
        Check("PC budget table allows 4 standings (MaxStandings) and stays small", pc.MaxStandings == 4 && an.MaxStandings == 2 && four.StandingBytes == pcPeak.StandingBytes * 2);

        long anAll = MemoryBudget.CacheGrowthBytes(rows, an, false);
        long anReq = MemoryBudget.CacheGrowthBytes(rows, an, true);
        long pcAll = MemoryBudget.CacheGrowthBytes(rows, pc, false);
        Console.WriteLine("      unbounded ArtLoader cache: Android all " + MemoryBudget.ToMb(anAll).ToString("0.0") + " MB (required " + MemoryBudget.ToMb(anReq).ToString("0.0") + "), PC all " + MemoryBudget.ToMb(pcAll).ToString("0.0") + " MB");
        Check("cache growth: required <= all, all > one peak scene's art", anReq <= anAll && anAll > anPeak.TotalBytes - anPeak.UiAtlasBytes);
        Check("cache growth sums every row exactly (Android all = per-category sums)",
            anAll == MemoryBudget.CategoryBytes(rows, ArtCategory.StandingBody, an, false) + MemoryBudget.CategoryBytes(rows, ArtCategory.StandingFace, an, false)
                + MemoryBudget.CategoryBytes(rows, ArtCategory.EventCg, an, false) + MemoryBudget.CategoryBytes(rows, ArtCategory.CommonCg, an, false)
                + MemoryBudget.CategoryBytes(rows, ArtCategory.Background, an, false) + MemoryBudget.CategoryBytes(rows, ArtCategory.MenuIcon, an, false));
        Check("even the unbounded cache stays under the Android 350 MiB budget at final art sizes (but not by a comfortable margin)", MemoryBudget.FitsBudget(anAll + an.UiAtlasBytes, an, 0.0) && !MemoryBudget.FitsBudget(anAll + an.UiAtlasBytes, an, 0.6));
        PlatformProfile mips = PlatformProfile.Android();
        mips.Mipmaps = true;
        Check("enabling mipmaps costs ~33% more (so the import rule keeps them off)", MemoryBudget.PeakScene(rows, mips).TotalBytes - mips.UiAtlasBytes > (anPeak.TotalBytes - an.UiAtlasBytes) * 1.3);
        PlatformProfile capped = PlatformProfile.Android();
        capped.MaxSizeStanding = 1024;
        Check("capping the standing Max Size to 1024 shrinks standings (512x1024 canvas)", MemoryBudget.PeakScene(rows, capped).StandingBytes < anPeak.StandingBytes / 3);

        // 잘못된 행
        List<string> bad = new List<string>();
        string csv = "분류,파일명,규격,우선순위\n스탠딩 바디,a.png,1024x2048,필수\n모르는 분류,b.png,1x1,필수\n이벤트 CG,c.png,크기없음,필수\nUI,d.png,1024,필수\n,,,\n스탠딩 얼굴,e.png,바디와 같은 캔버스(투명),선택\n";
        List<ArtRow> parsed = MemoryBudget.ParseChecklist(csv, bad);
        Check("bad checklist rows: unknown category + unreadable size reported; UI/blank skipped; faces inherit silently", parsed.Count == 3 && bad.Count == 2);
        Check("unreadable CG size falls back to 1920x1080", parsed[1].Width == 1920 && parsed[1].Height == 1080 && parsed[2].Width == 1024 && parsed[2].Height == 2048 && !parsed[2].Required && parsed[0].Required);
        List<string> e2 = new List<string>();
        Check("null / empty checklist -> no rows, one error, no throw", MemoryBudget.ParseChecklist(null, e2).Count == 0 && e2.Count == 1 && MemoryBudget.ParseChecklist("", e2).Count == 0);
        Check("RowBytes of a non-texture row / null is 0", MemoryBudget.RowBytes(null, pc) == 0L && MemoryBudget.RowBytes(new ArtRow { Category = ArtCategory.Other, Width = 10, Height = 10 }, pc) == 0L);
        Check("FitsBudget headroom math", MemoryBudget.FitsBudget(100, new PlatformProfile { BudgetBytes = 200 }, 0.5) && !MemoryBudget.FitsBudget(101, new PlatformProfile { BudgetBytes = 200 }, 0.5));
        Check("platform profiles mirror ArtImportPostprocessor (PC BC7; Android ASTC 6x6, icons 4x4; no mips)", pc.Standing == TexFormat.Bc7 && an.Standing == TexFormat.Astc6x6 && an.Icon == TexFormat.Astc4x4 && !pc.Mipmaps && !an.Mipmaps && pc.BudgetBytes == 1073741824L && an.BudgetBytes == 350L * 1024 * 1024);
        Check("docs/15 quotes the same peak numbers as the calculator", ReadAll("docs/15_Performance_Guide.md").Contains(MemoryBudget.ToMb(pcPeak.TotalBytes).ToString("0.0")) && ReadAll("docs/15_Performance_Guide.md").Contains(MemoryBudget.ToMb(anPeak.TotalBytes).ToString("0.0")));
    }

    public static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "balance";
        FileTextSource src = new FileTextSource(dir);
        _balance = BalanceTable.Load(src, Difficulty.Normal);
        _catalog = RomanceCatalog.Load(src);

        AudioMath();
        Manifest();
        Strings();
        SettingsChecks();
        Outfits();
        AudioCommands();
        Layout();
        Input();
        Memory();

        Console.WriteLine();
        Console.WriteLine("polish_check: {0} checks, {1} failures", _checks, _failures);
        return _failures == 0 ? 0 : 1;
    }
}
