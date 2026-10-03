using System;
using System.Globalization;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 저장 파일의 SavedAtUtc 문자열로 "가장 최근" 슬롯을 고르는 순수 도우미. 문자열 사전순 비교 대신 DateTime 으로 파싱한다
    /// (형식이 조금 달라도 올바르게 비교, 파싱 실패 = 가장 오래된 것으로 취급).
    /// </summary>
    public static class SaveRecency
    {
        /// <summary>ISO 8601("o") 등을 UTC DateTime 으로. 실패하면 false.</summary>
        public static bool TryParseUtc(string text, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrEmpty(text)) return false;
            DateTime d;
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)) return false;
            if (d.Kind == DateTimeKind.Local) d = d.ToUniversalTime();
            else if (d.Kind == DateTimeKind.Unspecified) d = DateTime.SpecifyKind(d, DateTimeKind.Utc);
            utc = d;
            return true;
        }

        /// <summary>파싱한 UTC 시각. 실패하면 DateTime.MinValue(가장 오래됨).</summary>
        public static DateTime ParseOrOldest(string text)
        {
            DateTime d;
            return TryParseUtc(text, out d) ? d : DateTime.MinValue;
        }

        /// <summary>candidate 가 current 보다 엄밀히 더 최근인가. 같거나 둘 다 잘못이면 false(먼저 본 슬롯 유지).</summary>
        public static bool IsNewer(string candidate, string current)
        {
            return ParseOrOldest(candidate) > ParseOrOldest(current);
        }
    }
}
