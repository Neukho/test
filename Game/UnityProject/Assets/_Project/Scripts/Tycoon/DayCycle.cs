using System;
using System.Collections.Generic;
using MorpheHouse.Core;
using MorpheHouse.Data;

namespace MorpheHouse.Tycoon
{
    /// <summary>
    /// 하루 사이클 상태 머신: 아침 → 낮(영업) → 밤(자유 시간) → 정산 → 다음 날.
    /// UI 는 이 클래스의 메서드만 호출하고, 상태 변화는 EventBus 로 통지받는다.
    /// </summary>
    public sealed class DayCycle
    {
        private readonly IRandom _rng;

        public GameState State { get; private set; }
        public BalanceTable Balance { get; private set; }

        /// <summary>오늘 영업 결과(영업 후 ~ 다음 날 시작 전까지 유효).</summary>
        public DayResult Pending { get; private set; }

        /// <summary>챕터 소개를 아직 보여 주지 않았는지(UI 가 확인 후 AckChapterIntro 호출).</summary>
        public bool ChapterIntroPending { get; private set; }

        public DayCycle(GameState state, BalanceTable balance, IRandom rng)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (balance == null) throw new ArgumentNullException("balance");
            if (rng == null) throw new ArgumentNullException("rng");
            State = state;
            Balance = balance;
            _rng = rng;
        }

        /// <summary>새 게임 시작: 첫날 아침을 연다.</summary>
        public void Begin()
        {
            ChapterIntroPending = true;
            StartDay();
        }

        /// <summary>저장 데이터에서 이어하기. 저장은 항상 아침에 이루어지므로 피로 회복을 다시 적용하지 않는다.</summary>
        public void Resume()
        {
            State.Phase = DayPhase.Morning;
            _freeRest = false;
            ChapterIntroPending = false;
            Pending = null;
            EventBus.Publish(new DayPhaseChanged(State.Day, State.Chapter, State.Phase));
        }

        public void AckChapterIntro()
        {
            ChapterIntroPending = false;
        }

        private void StartDay()
        {
            State.Phase = DayPhase.Morning;
            Pending = null;
            _freeRest = false;
            State.Fatigue = Math.Max(0f, State.Fatigue - Balance.P("fatigue_daily_recover"));
            ApplyNeglectDecay();
            EventBus.Publish(new DayPhaseChanged(State.Day, State.Chapter, State.Phase));
        }

        /// <summary>일정 기간 접촉이 없는 인물의 호감도를 서서히 낮춘다. 루트 확정 후 비구성원(단독·공동 모두)은 면제, 구성원은 계속 감쇠 대상.</summary>
        private void ApplyNeglectDecay()
        {
            float grace = Balance.P("decay_grace_days");
            float decay = Balance.P("decay_per_day");
            foreach (KeyValuePair<string, CharState> pair in State.Chars)
            {
                if (State.IsLockedOut(pair.Key)) continue;
                if (State.Day - pair.Value.LastContactDay > grace)
                    pair.Value.Aff = Math.Max(0f, pair.Value.Aff - decay);
            }
        }

        // ---------- 아침 ----------

        public bool CanUpgradeShop(out float cost)
        {
            cost = Balance.LevelCost(State.ShopLevel);
            ChapterRow chapter = Balance.Chapter(State.Chapter);
            return State.Phase == DayPhase.Morning
                && State.ShopLevel < chapter.LevelCap
                && State.Gold >= cost;
        }

        /// <summary>현재 챕터의 레벨 상한과 별개로, 이미 상한에 도달했는지.</summary>
        public bool AtLevelCap()
        {
            return State.ShopLevel >= Balance.Chapter(State.Chapter).LevelCap;
        }

        public bool TryUpgradeShop()
        {
            float cost;
            if (!CanUpgradeShop(out cost)) return false;
            State.Gold -= cost;
            State.ShopLevel++;
            EventBus.Publish(new GoldChanged(State.Gold, -cost));
            EventBus.Publish(new ShopUpgraded(State.ShopLevel));
            return true;
        }

        // ---------- 낮 ----------

        public DayResult RunBusiness()
        {
            if (State.Phase != DayPhase.Morning) throw new InvalidOperationException("RunBusiness requires Morning phase");
            State.Phase = DayPhase.Business;
            Pending = BusinessSimulator.Simulate(State, Balance, _rng);
            State.Fatigue = Math.Min(100f, State.Fatigue + Pending.FatigueAdded);
            State.SatisfactionBonus = 0f;
            EventBus.Publish(new DayPhaseChanged(State.Day, State.Chapter, State.Phase));
            return Pending;
        }

        // ---------- 밤 ----------

        /// <summary>
        /// 피로 제한(fatigue_limit 이상)으로 오늘 밤 행동이 1회로 줄었고 무료 휴식을 아직 쓰지 않았는가. 이때 Rest 는 행동을 소모하지 않고
        /// (밤 1회) 피로만 낮춘다(QA-08): 제한된 밤에도 "쉬거나 / 연애 행동" 중 하나를 고르는 낭비가 없고, 내일 아침의 피로가 줄어 제한을 벗어난다.
        /// 제한되지 않은 밤의 Rest 는 전과 같이 행동 1회를 쓴다.
        /// </summary>
        public bool FreeRestAvailable
        {
            get { return State.Phase == DayPhase.Night && _freeRest; }
        }

        private bool _freeRest;

        public void BeginNight()
        {
            if (State.Phase != DayPhase.Business) throw new InvalidOperationException("BeginNight requires Business phase");
            State.Phase = DayPhase.Night;
            bool limited = State.Fatigue >= Balance.P("fatigue_limit");
            _freeRest = limited;
            State.ActionsLeft = limited ? 1 : Math.Max(1, (int)Balance.P("actions_per_night"));
            EventBus.Publish(new DayPhaseChanged(State.Day, State.Chapter, State.Phase));
        }

        public bool DoNightAction(NightAction action)
        {
            if (State.Phase != DayPhase.Night) return false;
            if (action == NightAction.Rest && _freeRest)
            {
                // 피로 제한 밤의 무료 휴식: 행동 횟수를 건드리지 않는다(행동을 이미 다 썼어도 가능).
                _freeRest = false;
                State.Fatigue = Math.Max(0f, State.Fatigue + Balance.Actions["rest"].Fatigue);
                return true;
            }
            if (State.ActionsLeft <= 0) return false;
            switch (action)
            {
                case NightAction.Rest:
                    State.Fatigue = Math.Max(0f, State.Fatigue + Balance.Actions["rest"].Fatigue);
                    break;
                case NightAction.Research:
                    State.SatisfactionBonus = Math.Min(0.06f, State.SatisfactionBonus + 0.03f);
                    State.Fatigue = Math.Min(100f, State.Fatigue + 3f);
                    break;
                default:
                    return false;
            }
            State.ActionsLeft--;
            return true;
        }

        // ---------- 정산 ----------

        /// <summary>밤을 끝내고 정산한다. 골드·평판이 이 시점에 반영된다.</summary>
        public void Settle()
        {
            if (State.Phase != DayPhase.Night) throw new InvalidOperationException("Settle requires Night phase");
            State.Phase = DayPhase.Settlement;
            State.ActionsLeft = 0;
            _freeRest = false;

            DayResult r = Pending;
            State.Gold += r.Net;
            float oldRep = State.Reputation;
            State.Reputation = Math.Max(0f, Math.Min(100f, State.Reputation + r.RepDelta));

            EventBus.Publish(new GoldChanged(State.Gold, r.Net));
            EventBus.Publish(new ReputationChanged(State.Reputation, State.Reputation - oldRep));
            EventBus.Publish(new BusinessSettled(State.Day, r.Gross, r.Net, r.Satisfaction));
            EventBus.Publish(new DayPhaseChanged(State.Day, State.Chapter, State.Phase));
        }

        /// <summary>다음 날로 넘어간다. 챕터 전환·게임 종료도 여기서 처리한다.</summary>
        public void NextDay()
        {
            if (State.Phase != DayPhase.Settlement) throw new InvalidOperationException("NextDay requires Settlement phase");

            State.Day++;
            State.DayInChapter++;
            ChapterRow chapter = Balance.Chapter(State.Chapter);
            if (State.DayInChapter > chapter.Days)
            {
                if (State.Chapter >= Balance.MaxChapter)
                {
                    State.Finished = true;
                    EventBus.Publish(new GameEnded(State.Day - 1));
                    return;
                }
                State.Chapter++;
                State.DayInChapter = 1;
                ChapterIntroPending = true;
                EventBus.Publish(new ChapterChanged(State.Chapter));
            }
            StartDay();
        }
    }
}
