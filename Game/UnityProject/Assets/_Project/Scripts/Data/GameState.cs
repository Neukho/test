using System.Collections.Generic;
using MorpheHouse.Core;

namespace MorpheHouse.Data
{
    /// <summary>공략 대상 ID 상수. 새 캐릭터는 여기와 데이터(SO/CSV)만 추가한다.</summary>
    public static class CharIds
    {
        public const string Howl = "howl";
        public const string Johan = "johan";
        public const string Beomjun = "beomjun";
        public const string Haru = "haru";
        public const string Gomseok = "gomseok";
        public const string Nabi = "nabi";

        public static readonly string[] All = { Howl, Johan, Beomjun, Haru, Gomseok, Nabi };
    }

    /// <summary>한 인물과의 관계 수치.</summary>
    public sealed class CharState
    {
        public float Aff;
        public float Trust;
        public int EventsDone;
        public int LastGiftDay = -99;
        public int LastDateDay = -99;
        public int LastContactDay;
    }

    /// <summary>런타임 게임 상태(저장 대상). Unity 에 의존하지 않는다.</summary>
    public sealed class GameState
    {
        public Difficulty Difficulty = Difficulty.Normal;
        public string PlayerName = "주인공";
        public string Gender = "neutral";

        /// <summary>주인공 종(placeholder). human|wolf|fox|tiger|rabbit|bear|cat. 대사 분기에는 아직 쓰지 않는다.</summary>
        public string PlayerSpecies = "human";

        /// <summary>외형 프리셋 1~6.</summary>
        public int PlayerAppearance = 1;

        public int Chapter = 1;
        public int Day = 1;
        public int DayInChapter = 1;
        public DayPhase Phase = DayPhase.Morning;
        public bool Finished;

        public float Gold;
        public float Reputation = 20f;
        public float Fatigue = 10f;
        public int ShopLevel = 1;
        public int ActionsLeft;
        public float SatisfactionBonus;

        /// <summary>
        /// 확정한 루트의 대표 인물 id(단독이면 그 인물, 공동이면 첫 번째 구성원). 미확정이면 빈 문자열.
        /// 기존 저장·코드와의 호환을 위해 유지하며, 구성원 전체는 <see cref="LockedGroup"/> 에 있다.
        /// </summary>
        public string LockedRoute = string.Empty;

        /// <summary>
        /// 공동(다자) 루트 구성원 id 목록(1~3명, 확정 순서). 비어 있으면 <see cref="LockedRoute"/> 만 있는 단독/구형 저장으로 본다.
        /// 항상 <see cref="LockedIds"/> / <see cref="IsRouteMember"/> 로 읽어야 구형 상태(LockedGroup 없음)도 맞게 처리된다.
        /// </summary>
        public readonly List<string> LockedGroup = new List<string>();

        public double PlayTimeSec;

        public readonly Dictionary<string, CharState> Chars = new Dictionary<string, CharState>();
        public readonly Dictionary<string, int> Flags = new Dictionary<string, int>();

        /// <summary>방문한 대화 노드(저장용). 실행 중에는 대화 러너가 기록을 갖고, 저장 직전에 여기로 내보낸다.</summary>
        public readonly List<string> VisitedNodes = new List<string>();

        /// <summary>주인공 생성 화면 결과로 새 게임을 만든다.</summary>
        public static GameState NewGame(BalanceTable balance, Difficulty difficulty, ProtagonistProfile profile)
        {
            ProtagonistProfile p = profile ?? ProtagonistProfile.Default();
            GameState s = NewGame(balance, difficulty, p.Name);
            p.ApplyTo(s);
            return s;
        }

        public static GameState NewGame(BalanceTable balance, Difficulty difficulty, string playerName)
        {
            GameState s = new GameState();
            s.Difficulty = difficulty;
            s.PlayerName = string.IsNullOrEmpty(playerName) ? "주인공" : playerName;
            s.Gold = balance.P("start_gold");
            for (int i = 0; i < CharIds.All.Length; i++) s.Chars[CharIds.All[i]] = new CharState();
            return s;
        }

        /// <summary>루트가 확정되었는지(단독·공동 무관).</summary>
        public bool HasRouteLock
        {
            get { return LockedGroup.Count > 0 || (LockedRoute != null && LockedRoute.Length > 0); }
        }

        /// <summary>확정된 구성원 id 들(복사본). 구형 상태(LockedGroup 비어 있고 LockedRoute 만 있음)는 [LockedRoute]. 미확정이면 빈 목록.</summary>
        public List<string> LockedIds()
        {
            List<string> ids = new List<string>();
            if (LockedGroup.Count > 0) ids.AddRange(LockedGroup);
            else if (LockedRoute != null && LockedRoute.Length > 0) ids.Add(LockedRoute);
            return ids;
        }

        /// <summary>확정된 구성원 수(미확정 0, 단독 1, 공동 2~3).</summary>
        public int LockedCount
        {
            get { return LockedGroup.Count > 0 ? LockedGroup.Count : (LockedRoute != null && LockedRoute.Length > 0 ? 1 : 0); }
        }

        /// <summary>해당 인물이 확정된 루트의 구성원인지. 미확정이면 false.</summary>
        public bool IsRouteMember(string charId)
        {
            if (charId == null) return false;
            if (LockedGroup.Count > 0) return LockedGroup.Contains(charId);
            return LockedRoute != null && LockedRoute.Length > 0 && LockedRoute == charId;
        }

        /// <summary>확정 후 구성원이 아닌 인물(성장 정지·감쇠 면제 대상)인지. 미확정이면 false.</summary>
        public bool IsLockedOut(string charId)
        {
            return HasRouteLock && !IsRouteMember(charId);
        }

        /// <summary>루트 확정 상태를 한 번에 설정한다(대표 id 와 목록을 항상 일치시킨다). 빈 목록이면 확정 해제.</summary>
        public void SetLock(IList<string> ids)
        {
            // ids 가 LockedGroup 자신일 수 있으므로(SetLock(state.LockedGroup)) 비우기 전에 복사한다(QA-15).
            string[] copy = null;
            if (ids != null && ids.Count > 0)
            {
                copy = new string[ids.Count];
                for (int i = 0; i < copy.Length; i++) copy[i] = ids[i];
            }
            LockedGroup.Clear();
            LockedRoute = string.Empty;
            if (copy == null) return;
            for (int i = 0; i < copy.Length; i++) LockedGroup.Add(copy[i]);
            LockedRoute = copy[0];
        }

        public float AffinityOf(string charId)
        {
            CharState c;
            return Chars.TryGetValue(charId, out c) ? c.Aff : 0f;
        }
    }
}
