using System;
using System.Collections.Generic;
using System.Globalization;
using MorpheHouse.Data;
using MorpheHouse.Romance;

namespace MorpheHouse.GameFlow
{
    public enum AudioKind
    {
        Bgm,
        Se,
        Voice
    }

    /// <summary>오디오 한 개의 명세(audio_manifest.csv 의 한 행).</summary>
    public sealed class AudioEntry
    {
        public string Id = string.Empty;
        public AudioKind Kind;
        /// <summary>Resources 기준 경로(확장자 없음). 예: Audio/BGM/bgm_title.</summary>
        public string Path = string.Empty;
        public bool Loop;
        /// <summary>이 소리의 기본 음량 보정(0..1). 채널 볼륨에 곱해진다.</summary>
        public float DefaultVolume = 1f;
        public string Notes = string.Empty;
    }

    /// <summary>
    /// 오디오 id → Resources 경로 표. Resources/Audio/audio_manifest.csv(열: id,kind,path,loop,default_volume,notes)를 읽는다.
    /// 잘못된 행은 예외 대신 <see cref="Errors"/> 에 이유를 남기고 건너뛴다(나머지 행은 정상 사용). id 는 소문자로 정규화하며,
    /// 클립 파일이 실제로 있는지는 여기서 모른다(AudioService 가 로드 시 확인하고 없으면 조용히 무시).
    /// Unity 에 의존하지 않는다.
    /// </summary>
    public sealed class AudioCatalog
    {
        /// <summary>Resources.Load 에 쓰는 매니페스트 경로(확장자 없음).</summary>
        public const string ManifestResourcePath = "Audio/audio_manifest";

        private readonly Dictionary<string, AudioEntry> _byId = new Dictionary<string, AudioEntry>(StringComparer.Ordinal);
        private readonly List<AudioEntry> _all = new List<AudioEntry>();
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        /// <summary>건너뛴 행과 그 이유(행 번호는 헤더가 1번).</summary>
        public List<string> Errors { get { return _errors; } }

        /// <summary>쓸 수는 있지만 고친 값(음량 범위 보정 등).</summary>
        public List<string> Warnings { get { return _warnings; } }

        public int Count { get { return _all.Count; } }

        /// <summary>등록 순서대로의 전체 항목.</summary>
        public List<AudioEntry> All { get { return _all; } }

        public static string NormalizeId(string id)
        {
            return (id ?? string.Empty).Trim().ToLowerInvariant();
        }

        /// <summary>id 는 소문자 영문·숫자·밑줄만, 영문으로 시작, 40자 이하.</summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 40) return false;
            if (id[0] < 'a' || id[0] > 'z') return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        public static bool TryParseKind(string text, out AudioKind kind)
        {
            kind = AudioKind.Se;
            string k = (text ?? string.Empty).Trim().ToLowerInvariant();
            if (k == "bgm") { kind = AudioKind.Bgm; return true; }
            if (k == "se" || k == "sfx") { kind = AudioKind.Se; return true; }
            if (k == "voice") { kind = AudioKind.Voice; return true; }
            return false;
        }

        /// <summary>CSV 텍스트를 읽는다. null/빈 텍스트/헤더 불량은 빈 카탈로그 + 오류 한 줄.</summary>
        public static AudioCatalog Parse(string csv)
        {
            AudioCatalog catalog = new AudioCatalog();
            if (string.IsNullOrEmpty(csv))
            {
                catalog._errors.Add("매니페스트가 비어 있거나 없습니다(소리 없이 실행됩니다).");
                return catalog;
            }

            CsvTable table = CsvTable.Parse(csv);
            if (table.Rows.Count == 0)
            {
                catalog._errors.Add("매니페스트에 데이터 행이 없습니다.");
                return catalog;
            }

            bool headerOk = false;
            for (int i = 0; i < table.Rows.Count; i++)
                if (table.Rows[i].Str("id").Length > 0 && table.Rows[i].Str("path").Length > 0) { headerOk = true; break; }
            if (!headerOk)
            {
                catalog._errors.Add("헤더에 id,kind,path 열이 필요합니다(또는 모든 행이 비어 있습니다).");
                return catalog;
            }

            for (int i = 0; i < table.Rows.Count; i++) catalog.AddRow(table.Rows[i], i + 2);
            return catalog;
        }

        private void AddRow(CsvRow row, int line)
        {
            string rawId = row.Str("id").Trim();
            if (rawId.Length == 0) { _errors.Add("행 " + line + ": id 가 비어 있습니다."); return; }
            if (rawId[0] == '#') return; // 주석 행

            string id = NormalizeId(rawId);
            if (!IsValidId(id)) { _errors.Add("행 " + line + ": id '" + rawId + "' 형식 오류(소문자 영문·숫자·밑줄, 영문 시작, 40자 이하)."); return; }

            AudioKind kind;
            if (!TryParseKind(row.Str("kind"), out kind)) { _errors.Add("행 " + line + ": '" + id + "' kind '" + row.Str("kind") + "' 를 알 수 없습니다(bgm|se|voice)."); return; }

            string path = row.Str("path").Trim().Replace('\\', '/');
            if (path.Length == 0) { _errors.Add("행 " + line + ": '" + id + "' path 가 비어 있습니다."); return; }
            if (path.StartsWith("/", StringComparison.Ordinal) || path.IndexOf("..", StringComparison.Ordinal) >= 0 || path.IndexOf(':') >= 0)
            {
                _errors.Add("행 " + line + ": '" + id + "' path '" + path + "' 는 Resources 기준 상대 경로여야 합니다.");
                return;
            }
            int dot = path.LastIndexOf('.');
            if (dot > path.LastIndexOf('/') + 1)
            {
                string ext = path.Substring(dot);
                if (ext.Length <= 5)
                {
                    path = path.Substring(0, dot); // Resources.Load 는 확장자를 받지 않는다.
                    _warnings.Add("행 " + line + ": '" + id + "' path 의 확장자 '" + ext + "' 를 제거했습니다.");
                }
            }

            bool loop;
            if (!TryParseBool(row.Str("loop"), kind == AudioKind.Bgm, out loop)) { _errors.Add("행 " + line + ": '" + id + "' loop '" + row.Str("loop") + "' 는 0/1/true/false 여야 합니다."); return; }

            float volume = 1f;
            string vtext = row.Str("default_volume").Trim();
            if (vtext.Length > 0)
            {
                if (!float.TryParse(vtext, NumberStyles.Float, CultureInfo.InvariantCulture, out volume) || float.IsNaN(volume) || float.IsInfinity(volume))
                {
                    _errors.Add("행 " + line + ": '" + id + "' default_volume '" + vtext + "' 는 숫자가 아닙니다.");
                    return;
                }
                if (volume < 0f || volume > 1f)
                {
                    _warnings.Add("행 " + line + ": '" + id + "' default_volume " + vtext + " 를 0..1 로 보정했습니다.");
                    volume = Math.Max(0f, Math.Min(1f, volume));
                }
            }

            if (_byId.ContainsKey(id)) { _errors.Add("행 " + line + ": id '" + id + "' 중복(먼저 나온 행 사용)."); return; }

            AudioEntry e = new AudioEntry();
            e.Id = id;
            e.Kind = kind;
            e.Path = path;
            e.Loop = loop;
            e.DefaultVolume = volume;
            e.Notes = row.Str("notes").Trim();
            _byId[id] = e;
            _all.Add(e);
        }

        private static bool TryParseBool(string text, bool fallback, out bool value)
        {
            string t = (text ?? string.Empty).Trim().ToLowerInvariant();
            value = fallback;
            if (t.Length == 0) return true;
            if (t == "1" || t == "true" || t == "yes" || t == "y") { value = true; return true; }
            if (t == "0" || t == "false" || t == "no" || t == "n") { value = false; return true; }
            return false;
        }

        // ---------------- 조회 ----------------

        public bool TryGet(string id, out AudioEntry entry)
        {
            entry = null;
            if (id == null) return false;
            return _byId.TryGetValue(NormalizeId(id), out entry);
        }

        /// <summary>id 가 있고 종류까지 맞는가.</summary>
        public bool Has(string id, AudioKind kind)
        {
            AudioEntry e;
            return TryGet(id, out e) && e.Kind == kind;
        }

        /// <summary>한 종류의 항목 id 목록(등록 순서).</summary>
        public List<string> Ids(AudioKind kind)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < _all.Count; i++) if (_all[i].Kind == kind) ids.Add(_all[i].Id);
            return ids;
        }
    }

    /// <summary>
    /// 게임 맥락 → 소리 id 대응(순수). 실제 소리 id 는 audio_manifest.csv 에 있어야 하며, 없으면 AudioService 가 조용히 무시한다.
    /// docs/14_Audio_Request_List.md 의 "쓰이는 곳" 과 같은 규칙을 코드로 옮긴 것.
    /// </summary>
    public static class AudioCues
    {
        // BGM
        public const string BgmTitle = "title";
        public const string BgmCafeDay = "cafe_day";
        public const string BgmCafeNight = "cafe_night";
        public const string BgmBusiness = "business";
        public const string BgmSettlement = "settlement";
        public const string BgmEventWarm = "event_warm";
        public const string BgmEventTense = "event_tense";
        public const string BgmEventSad = "event_sad";
        public const string BgmRouteSelect = "event_tense";
        public const string BgmEndingNormal = "ending_normal";
        public const string BgmEndingGood = "ending_good";
        public const string BgmEndingTrue = "ending_true";
        public const string BgmEndingHarem = "ending_harem";
        public const string BgmEndingCommunity = "ending_community";

        // SE
        public const string SeClick = "ui_click";
        public const string SeConfirm = "ui_confirm";
        public const string SeBack = "ui_back";
        public const string SeTypewriter = "typewriter";
        public const string SeCoin = "coin";
        public const string SeLevelUp = "level_up";
        public const string SeGift = "gift";
        public const string SeDoorBell = "door_bell";
        public const string SeChapterStart = "chapter_start";
        public const string SeSave = "save_done";
        public const string SeError = "error";
        public const string SeCgReveal = "cg_reveal";
        public const string SeDateStart = "date_start";

        /// <summary>게임 화면 배경 시간대(Day/Evening/Night) → 기본 BGM. 알 수 없으면 낮.</summary>
        public static string BgmForTime(string time)
        {
            string t = (time ?? string.Empty).Trim().ToLowerInvariant();
            if (t == "night") return BgmCafeNight;
            if (t == "evening") return BgmSettlement;
            return BgmCafeDay;
        }

        /// <summary>엔딩 종류 → 엔딩 BGM.</summary>
        public static string BgmForEnding(EndingKind kind)
        {
            switch (kind)
            {
                case EndingKind.True: return BgmEndingTrue;
                case EndingKind.Good: return BgmEndingGood;
                case EndingKind.Normal: return BgmEndingNormal;
                case EndingKind.HaremDuo:
                case EndingKind.HaremTrio: return BgmEndingHarem;
                default: return BgmEndingCommunity;
            }
        }

        /// <summary>버튼 클릭음 선택: 뒤로/닫기/취소 계열은 ui_back, 강조(확정) 버튼은 ui_confirm, 나머지는 ui_click.</summary>
        public static string ButtonSe(bool accent, bool isBack)
        {
            if (isBack) return SeBack;
            return accent ? SeConfirm : SeClick;
        }
    }
}
