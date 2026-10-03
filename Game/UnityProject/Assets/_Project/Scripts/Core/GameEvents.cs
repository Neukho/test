namespace MorpheHouse.Core
{
    public struct DayPhaseChanged : IGameEvent
    {
        public readonly int Day;
        public readonly int Chapter;
        public readonly DayPhase Phase;

        public DayPhaseChanged(int day, int chapter, DayPhase phase)
        {
            Day = day;
            Chapter = chapter;
            Phase = phase;
        }
    }

    public struct BusinessSettled : IGameEvent
    {
        public readonly int Day;
        public readonly float Gross;
        public readonly float Net;
        public readonly float Satisfaction;

        public BusinessSettled(int day, float gross, float net, float satisfaction)
        {
            Day = day;
            Gross = gross;
            Net = net;
            Satisfaction = satisfaction;
        }
    }

    public struct GoldChanged : IGameEvent
    {
        public readonly float Gold;
        public readonly float Delta;

        public GoldChanged(float gold, float delta)
        {
            Gold = gold;
            Delta = delta;
        }
    }

    public struct ReputationChanged : IGameEvent
    {
        public readonly float Reputation;
        public readonly float Delta;

        public ReputationChanged(float reputation, float delta)
        {
            Reputation = reputation;
            Delta = delta;
        }
    }

    public struct ShopUpgraded : IGameEvent
    {
        public readonly int NewLevel;

        public ShopUpgraded(int newLevel)
        {
            NewLevel = newLevel;
        }
    }

    public struct ChapterChanged : IGameEvent
    {
        public readonly int Chapter;

        public ChapterChanged(int chapter)
        {
            Chapter = chapter;
        }
    }

    public struct GameEnded : IGameEvent
    {
        public readonly int TotalDays;

        public GameEnded(int totalDays)
        {
            TotalDays = totalDays;
        }
    }

    // ---------- 2단계: 연애 ----------

    /// <summary>호감도가 변했을 때(캡·감쇠 적용 후 실제 값).</summary>
    public struct AffectionChanged : IGameEvent
    {
        public readonly string CharId;
        public readonly float NewValue;
        public readonly float Delta;

        public AffectionChanged(string charId, float newValue, float delta)
        {
            CharId = charId;
            NewValue = newValue;
            Delta = delta;
        }
    }

    /// <summary>신뢰도가 변했을 때(캡 적용 후 실제 값).</summary>
    public struct TrustChanged : IGameEvent
    {
        public readonly string CharId;
        public readonly float NewValue;
        public readonly float Delta;

        public TrustChanged(string charId, float newValue, float delta)
        {
            CharId = charId;
            NewValue = newValue;
            Delta = delta;
        }
    }

    /// <summary>개인 이벤트(1~5단계) 완료.</summary>
    public struct RomanceEventCompleted : IGameEvent
    {
        public readonly string CharId;
        public readonly int Step;

        public RomanceEventCompleted(string charId, int step)
        {
            CharId = charId;
            Step = step;
        }
    }

    /// <summary>
    /// 루트 확정(7장 이후). CharId 는 대표 인물(단독이면 그 인물, 공동이면 첫 구성원).
    /// 공동 루트면 CharIds 에 구성원 전체(1~3명, 확정 순서)가 들어 있다. 단독이면 CharIds 는 [CharId].
    /// </summary>
    public struct RouteLocked : IGameEvent
    {
        public readonly string CharId;
        public readonly string[] CharIds;

        public RouteLocked(string charId)
        {
            CharId = charId;
            CharIds = new string[] { charId };
        }

        public RouteLocked(string[] charIds)
        {
            CharIds = charIds ?? new string[0];
            CharId = CharIds.Length > 0 ? CharIds[0] : string.Empty;
        }

        /// <summary>공동(2명 이상) 루트인지.</summary>
        public bool IsGroup { get { return CharIds != null && CharIds.Length > 1; } }
    }

    /// <summary>스토리 플래그 값 변경.</summary>
    public struct FlagChanged : IGameEvent
    {
        public readonly string Key;
        public readonly int Value;

        public FlagChanged(string key, int value)
        {
            Key = key;
            Value = value;
        }
    }
}
