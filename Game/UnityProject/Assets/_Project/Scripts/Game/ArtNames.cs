using System;
using MorpheHouse.ArtPipeline;
using MorpheHouse.Data;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 스크립트에서 쓰는 소문자·별칭 표기(smile, lighthouse, HOWL_E1)를 ArtKeys 의 파일명 표기로 바꾼다.
    /// 알 수 없는 값은 null 을 돌려주고, 호출자가 경고만 남기고 무시한다(게임이 멈추지 않게).
    /// </summary>
    public static class ArtNames
    {
        public const string DefaultExpression = "Neutral";
        public const string DefaultOutfit = "Casual";
        public const string DefaultTime = "Day";
        public const string DefaultPlace = "Cafe_Interior";

        public static string Expression(string text)
        {
            return MatchIgnoreCase(ArtKeys.Expressions, text);
        }

        public static string Outfit(string text)
        {
            return MatchIgnoreCase(ArtKeys.Outfits, text);
        }

        /// <summary>장소. 대소문자·밑줄 무시(night_market → NightMarket), 별칭 cafe/cafe_inside → Cafe_Interior.</summary>
        public static string Place(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string key = Squash(text);
            if (key == "cafe" || key == "cafeinside" || key == "shop") return "Cafe_Interior";
            if (key == "cafeoutside") return "Cafe_Exterior";
            if (key == "harbor" || key == "harbour") return "Street";
            for (int i = 0; i < ArtKeys.Places.Length; i++)
                if (Squash(ArtKeys.Places[i]) == key) return ArtKeys.Places[i];
            return null;
        }

        /// <summary>시간대. dawn/morning → Day, dusk/sunset → Evening. 알 수 없으면 null.</summary>
        public static string Time(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string key = Squash(text);
            if (key == "dawn" || key == "morning" || key == "noon") return "Day";
            if (key == "dusk" || key == "sunset") return "Evening";
            return MatchIgnoreCase(ArtKeys.Times, text);
        }

        /// <summary>
        /// CG id 해석: "HOWL_E1", "howl_e3", "howl_6" → (howl, 번호). 공략 대상 id 가 아니거나 번호가 1~6 밖이면 false.
        /// </summary>
        public static bool TryParseCgId(string id, out string charId, out int number)
        {
            charId = null;
            number = 0;
            if (string.IsNullOrEmpty(id)) return false;
            int us = id.LastIndexOf('_');
            if (us <= 0 || us >= id.Length - 1) return false;
            string who = id.Substring(0, us).ToLowerInvariant();
            string tail = id.Substring(us + 1);
            if (tail.Length > 1 && (tail[0] == 'E' || tail[0] == 'e')) tail = tail.Substring(1);
            int n;
            if (!int.TryParse(tail, out n)) return false;
            if (n < 1 || n > 6) return false;
            if (Array.IndexOf(CharIds.All, who) < 0) return false;
            charId = who;
            number = n;
            return true;
        }

        /// <summary>
        /// 공통(인물 아닌) CG 키 해석: 글자·숫자와 밑줄로 된 2토큰 이상(첫 글자는 영문)이면 대문자로 정규화해 돌려준다
        /// (HAREM_E1, COMMON_CE03, common_ch6_photo → COMMON_CH6_PHOTO). 첫 토큰이 공략 대상 id 인 것은 인물 CG 규칙을 따르므로
        /// (예 HOWL_E9 오타) 여기서 거부한다. 인물 CG 로 해석되는 id 는 <see cref="TryParseCgId"/> 가 먼저 처리해야 한다.
        /// </summary>
        public static bool TryParseCommonCgKey(string id, out string key)
        {
            key = null;
            if (string.IsNullOrEmpty(id) || id.Length > 48) return false;
            string up = id.Trim().ToUpperInvariant();
            if (up.Length < 3 || up[0] < 'A' || up[0] > 'Z') return false;
            int tokens = 1;
            for (int i = 0; i < up.Length; i++)
            {
                char c = up[i];
                if (c == '_')
                {
                    if (i == 0 || i == up.Length - 1 || up[i - 1] == '_') return false;
                    tokens++;
                }
                else if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
            }
            if (tokens < 2) return false;
            string first = up.Substring(0, up.IndexOf('_')).ToLowerInvariant();
            if (Array.IndexOf(CharIds.All, first) >= 0) return false;
            key = up;
            return true;
        }

        private static string Squash(string text)
        {
            return text.Replace("_", string.Empty).Replace("-", string.Empty).Trim().ToLowerInvariant();
        }

        private static string MatchIgnoreCase(string[] list, string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string key = text.Trim();
            for (int i = 0; i < list.Length; i++)
                if (string.Equals(list[i], key, StringComparison.OrdinalIgnoreCase)) return list[i];
            return null;
        }
    }
}
