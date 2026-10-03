using System;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 코드에서 만든 문구용 조회 도우미(순수). 키는 Resources/Localization/ui_ko.csv·ui_en.csv 에 있고, 코드의 한국어 값은 표가 비었을 때의 기본값이다
    /// (fixes_check 가 코드 기본값 == ko CSV, ko/en 자리표시자 일치, 모든 키 존재를 검사한다).
    /// 호출은 항상 문자열 리터럴 키로 쓴다: Loc.T("key", "한국어") / Loc.F("key", "{0}일차", n).
    /// </summary>
    public static class Loc
    {
        public static string T(string key, string korean)
        {
            return UiStrings.Current.Get(key, korean);
        }

        public static string F(string key, string korean, params object[] args)
        {
            return UiStrings.FormatText(UiStrings.Current.Get(key, korean), args);
        }

        /// <summary>chapter_name_1..8 (기본값 = chapters.csv 의 한국어 이름).</summary>
        public static string ChapterName(int chapter, string korean)
        {
            return UiStrings.Current.Get("chapter_name_" + chapter, korean);
        }

        /// <summary>chapter_unlocks_1..8 (';' 로 구분된 해금 목록).</summary>
        public static string ChapterUnlocks(int chapter, string korean)
        {
            return UiStrings.Current.Get("chapter_unlocks_" + chapter, korean);
        }

        /// <summary>char_name_&lt;id&gt;.</summary>
        public static string CharName(string id, string korean)
        {
            return UiStrings.Current.Get("char_name_" + id, korean);
        }

        public static string GiftName(string id, string korean)
        {
            return UiStrings.Current.Get("gift_name_" + id, korean);
        }

        public static string SpotName(string id, string korean)
        {
            return UiStrings.Current.Get("spot_name_" + id, korean);
        }

        public static string Gender(string id, string korean)
        {
            return UiStrings.Current.Get("gender_" + id, korean);
        }

        public static string Species(string id, string korean)
        {
            return UiStrings.Current.Get("species_" + id, korean);
        }

        /// <summary>코드가 한국어 상수로 돌려주는 문구(거절 사유·경영 이벤트 등)를, 한국어 표에서 같은 값을 가진 키를 찾아 현재 언어로 바꾼다. 표에 없으면 그대로.</summary>
        public static string Tr(string korean)
        {
            return UiStrings.Current.TranslateFromKorean(korean);
        }
    }
}
