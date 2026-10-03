namespace MorpheHouse.Romance
{
    /// <summary>거절 사유 코드. UI 는 이 코드로 안내 문구를 고른다.</summary>
    public static class RomanceRefusal
    {
        public const string WrongPhase = "wrong_phase";
        public const string NoActions = "no_actions";
        public const string UnknownChar = "unknown_char";
        public const string UnknownGift = "unknown_gift";
        public const string UnknownSpot = "unknown_spot";
        public const string NoCatalog = "no_catalog";
        public const string Cooldown = "cooldown";
        public const string NoGold = "no_gold";
        public const string SpotLocked = "spot_locked";
        public const string EventNotAvailable = "event_not_available";
        public const string Frozen = "frozen";
        /// <summary>아직 이야기에 등장하지 않은 인물(characters.csv first_chapter 이전).</summary>
        public const string NotMet = "not_met";
    }

    /// <summary>
    /// 밤 행동 한 번의 결과. 일상적인 거절(단계 오류·쿨다운·골드 부족 등)은 예외 대신 Success=false + Reason 으로 돌려준다.
    /// Frozen=true 는 루트 확정 후 비선택 인물과의 접촉: 행동은 소모되지만 수치 변화는 없다(Success 는 true).
    /// </summary>
    public sealed class ActionResult
    {
        public bool Success;
        public string Reason = string.Empty;
        public string Kind = string.Empty;
        public string CharId = string.Empty;
        public float AffDelta;
        public float TrustDelta;
        public float GoldSpent;
        /// <summary>actions.csv 의 소요 시간(분). 플레이타임 집계용.</summary>
        public float Minutes;
        public bool ChoiceOffered;
        public bool ChoiceSucceeded;
        public bool Frozen;
        /// <summary>AdjustAffection 호출처 표시(예: "dialogue:howl_e1").</summary>
        public string Source = string.Empty;

        public static ActionResult Refused(string kind, string charId, string reason)
        {
            ActionResult r = new ActionResult();
            r.Success = false;
            r.Kind = kind;
            r.CharId = charId ?? string.Empty;
            r.Reason = reason;
            return r;
        }
    }
}
