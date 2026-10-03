using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 데모 빌드 설정(순수). Resources/Config/build_config.csv 의 key,value 행에서 읽는다:
    /// demo(0/1) · demo_last_chapter(기본 3, 1..8) · store_url(선택, https:// 만 허용).
    /// 데모가 켜지면 마지막 데모 챕터를 마친 뒤(다음 챕터 아침에 들어가려는 시점) DemoEndScreen 으로 끝난다.
    /// 데모 저장은 Flags["sys_demo"]=1 로 표시한다. 정식 버전은 데모 저장을 그대로 불러온다(호환됨): 표시만 남고 아무것도 막지 않는다.
    /// </summary>
    public sealed class DemoConfig
    {
        public const string FlagKey = "sys_demo";
        public const int DefaultLastChapter = 3;
        public const int MaxChapter = 8;

        private static DemoConfig _current = new DemoConfig();

        /// <summary>앱 전체가 쓰는 설정. 부트스트랩이 CSV 를 읽어 채운다(읽기 전에는 데모 꺼짐).</summary>
        public static DemoConfig Current
        {
            get { return _current; }
            set { if (value != null) _current = value; }
        }

        public bool Enabled;
        public int LastChapter = DefaultLastChapter;
        public string StoreUrl = string.Empty;
        public readonly List<string> Warnings = new List<string>();

        public static DemoConfig Parse(string csv)
        {
            DemoConfig c = new DemoConfig();
            if (string.IsNullOrEmpty(csv)) return c;
            string[] lines = csv.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim().TrimStart('﻿');
                if (line.Length == 0 || line[0] == '#') continue;
                int comma = line.IndexOf(',');
                if (comma <= 0) { c.Warnings.Add("행 " + (i + 1) + ": key,value 형식이 아닙니다."); continue; }
                string key = line.Substring(0, comma).Trim().ToLowerInvariant();
                string value = line.Substring(comma + 1).Trim().Trim('"');
                switch (key)
                {
                    case "key": break; // 헤더
                    case "demo": c.Enabled = ParseBool(value); break;
                    case "demo_last_chapter":
                        int n;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) c.LastChapter = Math.Max(1, Math.Min(MaxChapter, n));
                        else c.Warnings.Add("demo_last_chapter 가 숫자가 아닙니다: " + value);
                        break;
                    case "store_url":
                        if (value.Length == 0 || IsSafeUrl(value)) c.StoreUrl = value;
                        else c.Warnings.Add("store_url 은 https:// 로 시작해야 합니다(무시).");
                        break;
                }
            }
            return c;
        }

        private static bool ParseBool(string v)
        {
            v = v.ToLowerInvariant();
            return v == "1" || v == "true" || v == "yes" || v == "on";
        }

        /// <summary>https:// 로 시작하고 공백·제어 문자가 없는 URL 만 연다.</summary>
        public static bool IsSafeUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || url.Length > 512 || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.Length <= 8) return false;
            for (int i = 0; i < url.Length; i++) if (url[i] <= ' ' || url[i] == '"' || url[i] == '<' || url[i] == '>') return false;
            return true;
        }

        /// <summary>
        /// 데모 종료 지점인가: 데모가 켜져 있고, 게임이 끝났거나 현재 챕터가 마지막 데모 챕터를 넘었다(= 마지막 데모 챕터를 마침).
        /// 새 챕터 첫 아침(자동 저장 직후)에서 불러와도 같은 결과라 이어하기로 데모 밖으로 새지 않는다.
        /// </summary>
        public bool IsDemoOver(int chapter, bool finished)
        {
            return Enabled && (finished || chapter > LastChapter);
        }

        public static void MarkFlag(IDictionary<string, int> flags)
        {
            if (flags != null) flags[FlagKey] = 1;
        }

        public static bool IsDemoSave(IDictionary<string, int> flags)
        {
            int v;
            return flags != null && flags.TryGetValue(FlagKey, out v) && v != 0;
        }

        /// <summary>CSV 의 demo 행을 0↔1 로 뒤집은 새 텍스트(다른 행·줄바꿈 유지, 없으면 추가). newValue 가 켜짐인지 반환. 에디터 메뉴가 쓴다.</summary>
        public static string ToggleDemo(string csv, out bool nowEnabled)
        {
            string text = string.IsNullOrEmpty(csv) ? "key,value\n" : csv;
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            bool found = false;
            nowEnabled = false;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string t = line.Trim().TrimStart('﻿');
                if (!found && t.ToLowerInvariant().StartsWith("demo,", StringComparison.Ordinal))
                {
                    found = true;
                    bool cur = ParseBool(t.Substring(5).Trim().Trim('"'));
                    nowEnabled = !cur;
                    line = "demo," + (nowEnabled ? "1" : "0");
                }
                sb.Append(line);
                if (i < lines.Length - 1) sb.Append(nl);
            }
            string result = sb.ToString();
            if (!found)
            {
                nowEnabled = true;
                if (!result.EndsWith(nl, StringComparison.Ordinal)) result += nl;
                result += "demo,1" + nl;
            }
            return result;
        }
    }

    /// <summary>데모 종료 화면에 보여 줄 통계(순수). 문구는 Loc(ui_ko/ui_en 의 demo_* 키)에서 온다.</summary>
    public static class DemoSummary
    {
        public static List<string> Lines(int daysPlayed, int chapter, float gold, int shopLevel, float reputation, double playTimeSec, int cgCount)
        {
            List<string> l = new List<string>();
            l.Add(Loc.F("demo_stat_days", "{0}일 동안 {1}챕터까지 달렸습니다.", Math.Max(0, daysPlayed), Math.Max(1, chapter)));
            l.Add(Loc.F("end_final_stats", "최종 골드 {0} G · 가게 레벨 {1} · 평판 {2}", gold.ToString("N0"), shopLevel, reputation.ToString("F0")));
            l.Add(Loc.F("end_play_time", "플레이 시간 {0}", EndSummary.FormatPlayTime(playTimeSec)));
            l.Add(Loc.F("demo_stat_cg", "이번에 본 CG {0}장", cgCount));
            return l;
        }
    }
}
