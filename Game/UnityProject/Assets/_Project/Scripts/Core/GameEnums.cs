namespace MorpheHouse.Core
{
    /// <summary>하루 사이클 단계.</summary>
    public enum DayPhase
    {
        Morning = 0,
        Business = 1,
        Night = 2,
        Settlement = 3
    }

    /// <summary>난이도. balance/difficulty.csv 의 열 이름(소문자)과 일치한다.</summary>
    public enum Difficulty
    {
        Story = 0,
        Normal = 1,
        Hard = 2
    }

    /// <summary>
    /// 콘텐츠 등급 스위치. 선택 콘텐츠 팩의 노출 여부 판단에 쓴다.
    /// AllAges: 전연령, AdultRomance: 성인 로맨스(암시 수준, 노골적 묘사 없음).
    /// </summary>
    public enum ContentRating
    {
        AllAges = 0,
        AdultRomance = 1
    }

    /// <summary>밤 행동 종류(1단계). 연애 행동은 2단계에서 추가한다.</summary>
    public enum NightAction
    {
        Rest = 0,
        Research = 1
    }
}
