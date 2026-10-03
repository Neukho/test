namespace MorpheHouse.Romance
{
    /// <summary>취향 종류. prefs.csv 의 kind 열과 일치한다.</summary>
    public enum PrefKind
    {
        Gift = 0,
        Menu = 1,
        Spot = 2
    }

    /// <summary>취향 판정 결과.</summary>
    public enum Preference
    {
        Dislike = -1,
        Neutral = 0,
        Like = 1
    }

    /// <summary>characters.csv 한 줄. 공략 대상 6명의 기본 정보.</summary>
    public sealed class CharacterRow
    {
        public string Id;
        public string NameKo;
        public string Species;
        public int Age;
        public string Gender;
        public string Role;
        /// <summary>가게 직원으로 합류하는 챕터. 0 이면 직원이 아니다.</summary>
        public int StaffJoinChapter;
        /// <summary>이야기에 처음 등장하는 챕터. 이전에는 대화·선물·데이트·이벤트 대상이 될 수 없다.</summary>
        public int FirstChapter = 1;
        public string Keyword;
    }

    /// <summary>prefs.csv 한 줄.</summary>
    public sealed class PrefRow
    {
        public string CharId;
        public PrefKind Kind;
        public string Tag;
        public Preference Value;
    }

    /// <summary>gifts.csv 한 줄.</summary>
    public sealed class GiftRow
    {
        public string Id;
        public string NameKo;
        public int PriceTier;
    }

    /// <summary>spots.csv 한 줄. Period: dawn / day / dusk / night.</summary>
    public sealed class SpotRow
    {
        public string Id;
        public string NameKo;
        public int UnlockChapter;
        public string Period;
    }
}
