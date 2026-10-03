using System;
using System.Collections.Generic;
using System.Globalization;
using MorpheHouse.Data;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 화면 문자열 표(키 → 텍스트). 언어별 CSV(열 key,text)를 읽어 둔다. Unity 에 의존하지 않는다.
    /// 조회 순서: 현재 언어 표 → 한국어 표 → 호출자가 준 기본값 → 키 자체. 즉 번역이 비어 있어도 화면에 빈 글자가 나오지 않는다.
    /// 문자열 안의 {0} {1} 자리는 <see cref="Format"/> 이 채운다.
    /// </summary>
    public sealed class UiStrings
    {
        public const string FallbackLocale = "ko";

        private static UiStrings _current = new UiStrings();

        private readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>();
        private readonly List<string> _errors = new List<string>();
        private string _locale = FallbackLocale;

        /// <summary>
        /// 앱 전체가 쓰는 표. 부트스트랩이 CSV 를 읽어 채우며, 읽기 전에는 빈 표라서 UiText 의 한국어 기본값이 그대로 나온다.
        /// 교체할 때는 새 인스턴스를 넣는다(null 은 무시).
        /// </summary>
        public static UiStrings Current
        {
            get { return _current; }
            set { if (value != null) _current = value; }
        }

        /// <summary>현재 언어("ko" 또는 "en").</summary>
        public string Locale { get { return _locale; } }

        /// <summary>CSV 읽기 중 발견한 문제(키 없음·중복·열 누락).</summary>
        public List<string> Errors { get { return _errors; } }

        /// <summary>"en", "en-US", "EN" → "en", 그 밖(빈 값 포함)은 "ko".</summary>
        public static string NormalizeLocale(string locale)
        {
            string l = (locale ?? string.Empty).Trim().ToLowerInvariant();
            return l.StartsWith("en", StringComparison.Ordinal) ? "en" : FallbackLocale;
        }

        /// <summary>현재 언어를 바꾼다. 바뀌었으면 true.</summary>
        public bool SetLocale(string locale)
        {
            string l = NormalizeLocale(locale);
            if (l == _locale) return false;
            _locale = l;
            return true;
        }

        /// <summary>
        /// 한 언어의 CSV 를 읽어 표를 만든다(이미 있던 같은 언어 표는 교체). 반환값은 읽힌 키 수.
        /// 헤더에 key·text 열이 없으면 아무것도 읽지 않고 오류를 남긴다. 빈 키·'#' 로 시작하는 키(주석)는 건너뛰고, 중복 키는 먼저 나온 것을 쓴다.
        /// </summary>
        public int LoadLocale(string locale, string csv)
        {
            string l = NormalizeLocale(locale);
            Dictionary<string, string> table = new Dictionary<string, string>(StringComparer.Ordinal);
            _tables[l] = table;
            if (string.IsNullOrEmpty(csv))
            {
                _errors.Add(l + ": 문자열 CSV 가 비어 있거나 없습니다.");
                return 0;
            }

            CsvTable parsed = CsvTable.Parse(csv);
            if (parsed.Rows.Count == 0)
            {
                _errors.Add(l + ": 문자열 CSV 에 행이 없습니다.");
                return 0;
            }
            if (!HasColumns(parsed))
            {
                _errors.Add(l + ": 헤더에 key,text 열이 필요합니다.");
                return 0;
            }

            for (int i = 0; i < parsed.Rows.Count; i++)
            {
                CsvRow row = parsed.Rows[i];
                string key = row.Str("key").Trim();
                if (key.Length == 0 || key[0] == '#') continue;
                if (table.ContainsKey(key))
                {
                    _errors.Add(l + ": 키 중복 '" + key + "' (먼저 나온 값 사용)");
                    continue;
                }
                table[key] = row.Str("text");
            }
            return table.Count;
        }

        private static bool HasColumns(CsvTable t)
        {
            // CsvRow 는 열이 없으면 빈 문자열을 돌려주므로, 첫 행의 key 가 모두 비었는지로 열 존재를 판정한다.
            for (int i = 0; i < t.Rows.Count; i++)
                if (t.Rows[i].Str("key").Trim().Length > 0) return true;
            return false;
        }

        /// <summary>해당 언어 표에 키가 있는가.</summary>
        public bool Has(string locale, string key)
        {
            Dictionary<string, string> t;
            return key != null && _tables.TryGetValue(NormalizeLocale(locale), out t) && t.ContainsKey(key);
        }

        /// <summary>해당 언어 표의 키 수.</summary>
        public int Count(string locale)
        {
            Dictionary<string, string> t;
            return _tables.TryGetValue(NormalizeLocale(locale), out t) ? t.Count : 0;
        }

        /// <summary>해당 언어 표의 모든 키(복사본).</summary>
        public List<string> Keys(string locale)
        {
            Dictionary<string, string> t;
            if (!_tables.TryGetValue(NormalizeLocale(locale), out t)) return new List<string>();
            return new List<string>(t.Keys);
        }

        /// <summary>한 언어 표의 값(대체 없이). 없으면 null.</summary>
        public string Raw(string locale, string key)
        {
            Dictionary<string, string> t;
            string v;
            if (key != null && _tables.TryGetValue(NormalizeLocale(locale), out t) && t.TryGetValue(key, out v)) return v;
            return null;
        }

        /// <summary>현재 언어 → 한국어 → 키 순으로 조회.</summary>
        public string Get(string key)
        {
            return Get(key, null);
        }

        /// <summary>현재 언어 → 한국어 → defaultText → 키 순으로 조회. 값이 빈 문자열이면 "없음"으로 보고 다음 단계로 넘어간다.</summary>
        public string Get(string key, string defaultText)
        {
            if (key == null) return defaultText ?? string.Empty;
            string v = Raw(_locale, key);
            if (string.IsNullOrEmpty(v) && _locale != FallbackLocale) v = Raw(FallbackLocale, key);
            if (string.IsNullOrEmpty(v)) v = defaultText;
            return string.IsNullOrEmpty(v) ? key : v;
        }

        /// <summary>조회한 문자열의 {0} {1} 자리를 채운다. 형식이 잘못되면 채우지 않은 원문을 돌려준다(예외 없음).</summary>
        public string Format(string key, params object[] args)
        {
            return FormatText(Get(key), args);
        }

        /// <summary>이미 얻은 형식 문자열에 인자를 채운다(기본값을 쓰는 UiText 용). 잘못된 형식은 원문 그대로.</summary>
        public static string FormatText(string format, params object[] args)
        {
            if (format == null) return string.Empty;
            if (args == null || args.Length == 0) return format;
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        /// <summary>
        /// 화면에 이미 놓인 글자가 어떤 키의 값인지 되짚는다(현재 언어 표 기준, 정확히 일치할 때만). 언어를 바꿀 때 라벨을 다시 쓰기 위한 용도.
        /// 같은 글자를 쓰는 키가 여럿이면 사전순으로 가장 앞의 키. 없으면 null.
        /// </summary>
        public string KeyOfText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Dictionary<string, string> t;
            if (!_tables.TryGetValue(_locale, out t)) return null;
            string best = null;
            foreach (KeyValuePair<string, string> p in t)
            {
                if (p.Value != text) continue;
                if (best == null || string.CompareOrdinal(p.Key, best) < 0) best = p.Key;
            }
            return best;
        }

        /// <summary>한국어 표에서 값이 정확히 text 인 키(사전순 첫째)를 찾아 현재 언어 문구로. 못 찾으면 text 그대로(현재 언어가 한국어여도 그대로).</summary>
        public string TranslateFromKorean(string text)
        {
            if (string.IsNullOrEmpty(text) || _locale == FallbackLocale) return text;
            Dictionary<string, string> ko;
            if (!_tables.TryGetValue(FallbackLocale, out ko)) return text;
            string best = null;
            foreach (KeyValuePair<string, string> p in ko)
            {
                if (p.Value != text) continue;
                if (best == null || string.CompareOrdinal(p.Key, best) < 0) best = p.Key;
            }
            if (best == null) return text;
            string v = Raw(_locale, best);
            return string.IsNullOrEmpty(v) ? text : v;
        }

        /// <summary>from 언어에는 있는데 to 언어에는 없는 키(번역 누락 점검).</summary>
        public List<string> MissingIn(string from, string to)
        {
            List<string> missing = new List<string>();
            List<string> keys = Keys(from);
            for (int i = 0; i < keys.Count; i++)
                if (!Has(to, keys[i])) missing.Add(keys[i]);
            missing.Sort(StringComparer.Ordinal);
            return missing;
        }

        /// <summary>두 언어에서 {0} {1} 자리 표시자 집합이 다른 키(번역하다 자리를 빠뜨린 경우).</summary>
        public List<string> PlaceholderMismatches(string a, string b)
        {
            List<string> bad = new List<string>();
            List<string> keys = Keys(a);
            for (int i = 0; i < keys.Count; i++)
            {
                string va = Raw(a, keys[i]);
                string vb = Raw(b, keys[i]);
                if (va == null || vb == null) continue;
                if (Placeholders(va) != Placeholders(vb)) bad.Add(keys[i]);
            }
            bad.Sort(StringComparer.Ordinal);
            return bad;
        }

        private static string Placeholders(string text)
        {
            List<int> found = new List<int>();
            for (int i = 0; i + 2 < text.Length; i++)
            {
                if (text[i] != '{') continue;
                int j = i + 1;
                int n = 0;
                bool digits = false;
                while (j < text.Length && text[j] >= '0' && text[j] <= '9') { n = n * 10 + (text[j] - '0'); j++; digits = true; }
                if (digits && j < text.Length && text[j] == '}' && !found.Contains(n)) found.Add(n);
            }
            found.Sort();
            return string.Join(",", found.ConvertAll<string>(delegate (int v) { return v.ToString(CultureInfo.InvariantCulture); }).ToArray());
        }
    }
}
