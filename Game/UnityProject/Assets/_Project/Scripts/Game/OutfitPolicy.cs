using System;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 스탠딩 의상 결정 규칙(순수). 시나리오는 보통 의상을 지정하지 않으므로(&lt;&lt;show_standing howl smile&gt;&gt;) 호스트가 상황으로 고른다.
    /// 우선순위:
    /// 1) 스크립트가 의상을 명시했으면 항상 그 의상(명시 &gt; 규칙).
    /// 2) 노드 제목에 "_Date_" 가 들어 있으면(예 Howl_Date_Lighthouse) Date.
    /// 3) 직원 인물(howl, gomseok, haru, nabi)이고 현재 배경이 카페(Cafe_Interior/Cafe_Exterior)의 낮(Day)이면 Uniform.
    /// 4) 그 밖에는 Casual(요한·범준은 손님이라 항상 Casual).
    /// "영업 맥락" 은 카페 낮 배경으로 근사한다: 호스트는 아침·영업 단계에서 Day, 밤 단계에서 Night 배경으로 장면을 시작한다.
    /// tools/art_checklist.py 는 같은 규칙으로 필요한 의상 에셋을 센다(규칙을 바꾸면 양쪽을 함께 고칠 것).
    /// </summary>
    public static class OutfitPolicy
    {
        public const string Casual = "Casual";
        public const string Uniform = "Uniform";
        public const string Date = "Date";

        /// <summary>제복을 입는 직원 인물 id(소문자).</summary>
        public static readonly string[] StaffChars = { "howl", "gomseok", "haru", "nabi" };

        public static bool IsStaff(string charId)
        {
            if (string.IsNullOrEmpty(charId)) return false;
            return Array.IndexOf(StaffChars, charId.Trim().ToLowerInvariant()) >= 0;
        }

        /// <summary>노드 제목이 데이트 장면(…_Date_…)인가. 대소문자 무시, 밑줄로 구분된 "date" 토큰이 있어야 한다.</summary>
        public static bool IsDateNode(string node)
        {
            if (string.IsNullOrEmpty(node)) return false;
            return node.IndexOf("_date_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>배경이 카페 낮인가(영업·아침 맥락의 근사).</summary>
        public static bool IsCafeDaytime(string place, string time)
        {
            if (string.IsNullOrEmpty(place) || string.IsNullOrEmpty(time)) return false;
            bool cafe = string.Equals(place, "Cafe_Interior", StringComparison.OrdinalIgnoreCase)
                || string.Equals(place, "Cafe_Exterior", StringComparison.OrdinalIgnoreCase);
            return cafe && string.Equals(time, "Day", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 의상 결정. explicitOutfit 이 ArtKeys 의 의상 이름(대소문자 무시)이면 그대로 정규화해 돌려준다.
        /// 비었거나 알 수 없는 값이면 규칙으로 정한다(알 수 없는 값의 경고는 디스패처가 이미 남긴다).
        /// </summary>
        public static string Resolve(string charId, string explicitOutfit, string node, string place, string time)
        {
            string given = ArtNames.Outfit(explicitOutfit);
            if (given != null) return given;
            if (IsDateNode(node)) return Date;
            if (IsStaff(charId) && IsCafeDaytime(place, time)) return Uniform;
            return Casual;
        }
    }
}
