using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MorpheHouse.Save
{
    /// <summary>
    /// HMAC-SHA256 기반 무결성 서명.
    /// 한계(문서화): 키가 클라이언트에 존재하므로 의지 있는 변조를 막지 못한다.
    /// 목적은 ‘우발적 손상·단순 편집 탐지’이며, 싱글 플레이 게임에서 강한 DRM 은 의도하지 않는다.
    /// </summary>
    public static class SaveIntegrity
    {
        private const string KeyMaterial = "MorpheHouse|save|v1|moonlight-corner";

        private static byte[] Key()
        {
            using (SHA256 sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(KeyMaterial));
        }

        public static string Compute(string json)
        {
            using (HMACSHA256 hmac = new HMACSHA256(Key()))
            {
                byte[] mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
                StringBuilder sb = new StringBuilder(mac.Length * 2);
                for (int i = 0; i < mac.Length; i++) sb.Append(mac[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// 서명 대상 문자열(정규 페이로드)을 만든다. 서명 필드는 파일의 Version 별로 명시된 목록(SignedFields)이며,
        /// 직렬화기가 새/누락 필드를 어떻게 찍는지와 무관하다. 새 *선택* 필드를 SaveData 에 추가해도 기존 저장의 서명은 유효하다.
        /// 서명 대상 필드를 바꾸려면 Version 을 올리고 SignedFields 에 새 case 와 SaveMigrations 단계를 추가한다.
        /// 지원하지 않는 버전이면 null.
        /// </summary>
        public static string CanonicalPayload(SaveData d)
        {
            List<KeyValuePair<string, string>> f = SignedFields(d);
            if (f == null) return null;
            StringBuilder sb = new StringBuilder(512);
            sb.Append("MorpheHouse.Save|v").Append(d.Version.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < f.Count; i++)
                sb.Append('\n').Append(f[i].Key).Append('=').Append(f[i].Value);
            return sb.ToString();
        }

        /// <summary>HMAC(정규 페이로드). 지원하지 않는 버전이면 null.</summary>
        public static string Sign(SaveData d)
        {
            string payload = CanonicalPayload(d);
            return payload == null ? null : Compute(payload);
        }

        /// <summary>서명된 필드 이름 목록(버전별). 테스트가 SaveData 의 모든 필드가 서명되었는지/명시적으로 제외되었는지 확인하는 데 쓴다.</summary>
        public static string[] SignedFieldNames(int version)
        {
            SaveData probe = new SaveData();
            probe.Version = version;
            List<KeyValuePair<string, string>> f = SignedFields(probe);
            if (f == null) return null;
            string[] names = new string[f.Count];
            for (int i = 0; i < f.Count; i++) names[i] = f[i].Key;
            return names;
        }

        /// <summary>서명 대상에서 의도적으로 뺀 필드(서명 자체).</summary>
        public static readonly string[] ExcludedFields = { "Hash" };

        private static List<KeyValuePair<string, string>> SignedFields(SaveData d)
        {
            switch (d.Version)
            {
                case 2: return SignedFieldsV2(d);
                default: return null;
            }
        }

        // 순서·이름은 서명 형식의 일부다. v2 를 고치지 말고 새 버전을 추가할 것.
        private static List<KeyValuePair<string, string>> SignedFieldsV2(SaveData d)
        {
            List<KeyValuePair<string, string>> f = new List<KeyValuePair<string, string>>();
            Add(f, "Version", d.Version);
            Add(f, "Slot", d.Slot);
            Add(f, "SavedAtUtc", d.SavedAtUtc);
            Add(f, "AppVersion", d.AppVersion);
            Add(f, "PlayerName", d.PlayerName);
            Add(f, "Gender", d.Gender);
            Add(f, "PlayerSpecies", d.PlayerSpecies);
            Add(f, "PlayerAppearance", d.PlayerAppearance);
            Add(f, "Difficulty", d.Difficulty);
            Add(f, "Chapter", d.Chapter);
            Add(f, "Day", d.Day);
            Add(f, "DayInChapter", d.DayInChapter);
            Add(f, "Gold", d.Gold);
            Add(f, "Reputation", d.Reputation);
            Add(f, "Fatigue", d.Fatigue);
            Add(f, "ShopLevel", d.ShopLevel);
            Add(f, "SatisfactionBonus", d.SatisfactionBonus);
            Add(f, "LockedRoute", d.LockedRoute);
            Add(f, "LockedGroup", d.LockedGroup);
            Add(f, "PlayTimeSec", d.PlayTimeSec);
            string chars;
            if (d.Chars == null) chars = "null";
            else
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(d.Chars.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < d.Chars.Count; i++)
                {
                    CharSave c = d.Chars[i];
                    if (c == null) { sb.Append("[null]"); continue; }
                    sb.Append('[').Append(Esc(c.Id)).Append(',').Append(Num(c.Aff)).Append(',').Append(Num(c.Trust)).Append(',')
                      .Append(c.EventsDone.ToString(CultureInfo.InvariantCulture)).Append(',').Append(c.LastGiftDay.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(c.LastDateDay.ToString(CultureInfo.InvariantCulture)).Append(',').Append(c.LastContactDay.ToString(CultureInfo.InvariantCulture)).Append(']');
                }
                chars = sb.ToString();
            }
            f.Add(new KeyValuePair<string, string>("Chars", chars));
            string flags;
            if (d.Flags == null) flags = "null";
            else
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(d.Flags.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < d.Flags.Count; i++)
                {
                    FlagSave fl = d.Flags[i];
                    if (fl == null) { sb.Append("[null]"); continue; }
                    sb.Append('[').Append(Esc(fl.Key)).Append(',').Append(fl.Value.ToString(CultureInfo.InvariantCulture)).Append(']');
                }
                flags = sb.ToString();
            }
            f.Add(new KeyValuePair<string, string>("Flags", flags));
            Add(f, "VisitedNodes", d.VisitedNodes);
            return f;
        }

        private static void Add(List<KeyValuePair<string, string>> f, string name, string v) { f.Add(new KeyValuePair<string, string>(name, v == null ? "null" : "s" + Esc(v))); }
        private static void Add(List<KeyValuePair<string, string>> f, string name, int v) { f.Add(new KeyValuePair<string, string>(name, v.ToString(CultureInfo.InvariantCulture))); }
        private static void Add(List<KeyValuePair<string, string>> f, string name, float v) { f.Add(new KeyValuePair<string, string>(name, Num(v))); }
        private static void Add(List<KeyValuePair<string, string>> f, string name, double v) { f.Add(new KeyValuePair<string, string>(name, v.ToString("R", CultureInfo.InvariantCulture))); }
        private static void Add(List<KeyValuePair<string, string>> f, string name, List<string> v)
        {
            if (v == null) { f.Add(new KeyValuePair<string, string>(name, "null")); return; }
            StringBuilder sb = new StringBuilder();
            sb.Append(v.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < v.Count; i++) sb.Append('[').Append(v[i] == null ? "null" : Esc(v[i])).Append(']');
            f.Add(new KeyValuePair<string, string>(name, sb.ToString()));
        }

        private static string Num(float v) { return ((double)v).ToString("R", CultureInfo.InvariantCulture); }

        /// <summary>구분자 모호성 제거: 역슬래시·줄바꿈·대괄호·쉼표를 이스케이프(길이/구조가 문자열 내용으로 바뀌지 않게).</summary>
        private static string Esc(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' || c == '\n' || c == '\r' || c == '[' || c == ']' || c == ',' || c == '=')
                    sb.Append('\\').Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>타이밍 공격에 둔감한 상수 시간 비교.</summary>
        public static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    /// <summary>
    /// 구버전 SaveData 를 현재 버전으로 올리는 단계 모음. 로드 후(서명 검증 뒤)에 호출한다.
    /// v1 → v2: 필드 변경 없음. 서명 방식이 "재직렬화 텍스트의 HMAC"(v1, SaveService 가 구형 해시 변형들로 검증)에서
    /// "버전별 정규 페이로드의 HMAC"(v2)로 바뀐 것을 표시한다. 다음 저장부터 v2 로 서명된다.
    /// 새 버전을 추가하는 법: CurrentVersion++, SaveIntegrity.SignedFields 에 case 추가, 여기 Step 추가, 테스트(qa_hunt save) 갱신.
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>d 를 CurrentVersion 까지 올린다. 성공하면 true(이미 최신이어도 true), 지원하지 않는 버전이면 false.</summary>
        public static bool Migrate(SaveData d)
        {
            if (d == null || d.Version < 1 || d.Version > SaveData.CurrentVersion) return false;
            while (d.Version < SaveData.CurrentVersion)
            {
                switch (d.Version)
                {
                    case 1: d.Version = 2; break;
                    default: return false;
                }
            }
            return true;
        }
    }
}
