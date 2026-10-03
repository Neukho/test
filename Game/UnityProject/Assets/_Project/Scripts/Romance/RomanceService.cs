using System;
using System.Collections.Generic;
using MorpheHouse.Core;
using MorpheHouse.Data;

namespace MorpheHouse.Romance
{
    /// <summary>
    /// 연애 행동 서비스: 대화·선물·데이트·개인 이벤트·루트 확정·플래그.
    /// - 모든 밤 행동은 Night 단계 + ActionsLeft &gt; 0 이어야 하며, 행동 1회·피로·마지막 접촉일을 갱신한다.
    /// - 일상적인 거절은 예외 없이 ActionResult(Success=false, Reason)로 돌려준다.
    /// - 방치 감쇠는 DayCycle 이 적용한다(여기서는 중복 적용하지 않는다).
    /// - 난수는 Func&lt;double&gt;([0,1))로 주입한다(Tycoon 어셈블리에 의존하지 않는다).
    /// </summary>
    public sealed class RomanceService
    {
        private readonly GameState _state;
        private readonly BalanceTable _balance;

        /// <summary>선물·데이트 취향/스팟 조회용 카탈로그. 없으면 선물·데이트는 no_catalog 로 거절된다.</summary>
        public RomanceCatalog Catalog { get; set; }

        public RomanceService(GameState state, BalanceTable balance, Func<double> rng)
            : this(state, balance, null, rng)
        {
        }

        public RomanceService(GameState state, BalanceTable balance, RomanceCatalog catalog, Func<double> rng)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (balance == null) throw new ArgumentNullException("balance");
            if (rng == null) throw new ArgumentNullException("rng");
            _state = state;
            _balance = balance;
            Catalog = catalog;
        }

        // ---------- 조회 ----------

        /// <summary>루트 확정 후 선택되지 않은 인물인지(성장 정지·감쇠 면제 대상). 공동 루트면 구성원 전원이 동결 대상에서 빠진다.</summary>
        public bool IsFrozen(string charId)
        {
            return _state.IsLockedOut(charId);
        }

        /// <summary>이 인물이 이미 이야기에 등장했는지(characters.csv first_chapter &lt;= 현재 챕터). 등장 전에는 모든 밤 행동이 not_met 으로 거절된다.</summary>
        public bool IsAvailable(string charId)
        {
            return _balance.IsCharAvailable(charId, _state.Chapter);
        }

        /// <summary>선물 쿨다운이 지났는지.</summary>
        public bool GiftReady(string charId)
        {
            CharState cs;
            if (!TryGetChar(charId, out cs)) return false;
            return _state.Day - cs.LastGiftDay >= _balance.Actions["gift_like"].CooldownDays;
        }

        /// <summary>데이트 쿨다운이 지났는지.</summary>
        public bool DateReady(string charId)
        {
            CharState cs;
            if (!TryGetChar(charId, out cs)) return false;
            return _state.Day - cs.LastDateDay >= _balance.Actions["date"].CooldownDays;
        }

        public float GiftCost()
        {
            return _balance.P("gift_cost_base") + _balance.P("gift_cost_per_chapter") * _state.Chapter;
        }

        public float DateCost()
        {
            return _balance.P("date_cost_base") + _balance.P("date_cost_per_chapter") * _state.Chapter;
        }

        // ---------- 대화 ----------

        /// <summary>
        /// 대화. 서비스는 기본값(actions.csv talk: +2, 피로 5)만 준다. 선택지 보상(정답 +5/+1.5, 오답 -1/-1)은 대화 노드의 소유라서
        /// 노드가 <c>&lt;&lt;aff&gt;&gt;</c>/<c>&lt;&lt;trust&gt;&gt;</c> → <see cref="AdjustAffection"/> 으로 직접 준다(이중 계산 방지, QA-05).
        /// 따라서 ChoiceOffered/ChoiceSucceeded 는 항상 false 이고 choice_prob 는 C# 에서 읽지 않는다(파이썬 시뮬 전용).
        /// </summary>
        public ActionResult Talk(string charId)
        {
            CharState cs;
            ActionResult refused = BeginNightAction("talk", charId, out cs);
            if (refused != null) return refused;
            if (IsFrozen(charId)) return FrozenContact("talk", charId, cs);

            ActionRow talk = _balance.Actions["talk"];
            ActionResult r = NewResult("talk", charId);
            r.Minutes = talk.Minutes;
            ApplyGain(charId, cs, talk.AffGain, talk.TrustGain, true, r);
            FinishNightAction(cs, talk.Fatigue);
            return r;
        }

        // ---------- 선물 ----------

        /// <summary>선물. 취향(like/neutral/dislike)에 따라 gift_* 수치를 쓰고, 골드(기본+챕터당)를 소모한다.</summary>
        public ActionResult Gift(string charId, string giftId)
        {
            CharState cs;
            ActionResult refused = BeginNightAction("gift", charId, out cs);
            if (refused != null) return refused;
            if (IsFrozen(charId)) return FrozenContact("gift", charId, cs);
            if (Catalog == null) return ActionResult.Refused("gift", charId, RomanceRefusal.NoCatalog);
            if (Catalog.FindGift(giftId) == null) return ActionResult.Refused("gift", charId, RomanceRefusal.UnknownGift);
            if (!GiftReady(charId)) return ActionResult.Refused("gift", charId, RomanceRefusal.Cooldown);
            float cost = GiftCost();
            if (_state.Gold < cost) return ActionResult.Refused("gift", charId, RomanceRefusal.NoGold);

            Preference pref = Catalog.GetPreference(charId, PrefKind.Gift, giftId);
            ActionRow row = _balance.Actions[RomanceRules.GiftActionKey(pref)];

            ActionResult r = NewResult("gift", charId);
            r.Minutes = row.Minutes;
            SpendGold(cost, r);
            cs.LastGiftDay = _state.Day;
            ApplyGain(charId, cs, row.AffGain, row.TrustGain, true, r);
            FinishNightAction(cs, row.Fatigue);
            return r;
        }

        // ---------- 데이트 ----------

        /// <summary>데이트. 스팟은 현재 챕터에 해금되어야 하며, 좋아하는 스팟은 호감도 +25%(싫어하면 -25%).</summary>
        public ActionResult Date(string charId, string spotId)
        {
            CharState cs;
            ActionResult refused = BeginNightAction("date", charId, out cs);
            if (refused != null) return refused;
            if (IsFrozen(charId)) return FrozenContact("date", charId, cs);
            if (Catalog == null) return ActionResult.Refused("date", charId, RomanceRefusal.NoCatalog);
            SpotRow spot = Catalog.FindSpot(spotId);
            if (spot == null) return ActionResult.Refused("date", charId, RomanceRefusal.UnknownSpot);
            if (spot.UnlockChapter > _state.Chapter) return ActionResult.Refused("date", charId, RomanceRefusal.SpotLocked);
            if (!DateReady(charId)) return ActionResult.Refused("date", charId, RomanceRefusal.Cooldown);
            float cost = DateCost();
            if (_state.Gold < cost) return ActionResult.Refused("date", charId, RomanceRefusal.NoGold);

            ActionRow row = _balance.Actions["date"];
            Preference pref = Catalog.GetPreference(charId, PrefKind.Spot, spotId);
            float affGain = row.AffGain * RomanceRules.SpotMultiplier(pref);

            ActionResult r = NewResult("date", charId);
            r.Minutes = row.Minutes;
            SpendGold(cost, r);
            cs.LastDateDay = _state.Day;
            ApplyGain(charId, cs, affGain, row.TrustGain, true, r);
            FinishNightAction(cs, row.Fatigue);
            return r;
        }

        // ---------- 개인 이벤트 ----------

        /// <summary>
        /// 장면 게이트(선택). 노드 이름("Howl_E3")을 받아 지금 재생해도 되는지 알려 준다. null 이면 게이트 없음.
        /// 닫힌 게이트의 이벤트는 GetAvailableEvent 가 null 을 돌려주므로 밤 메뉴에 나타나지 않고 완료도 거절된다.
        /// </summary>
        public Func<string, bool> EventGate { get; set; }

        /// <summary>이벤트 노드 이름 규칙(&lt;Char&gt;_E&lt;step&gt;, 첫 글자 대문자).</summary>
        public static string EventNode(string charId, int step)
        {
            if (string.IsNullOrEmpty(charId)) return string.Empty;
            return char.ToUpperInvariant(charId[0]) + charId.Substring(1) + "_E" + step;
        }

        private EventScheduleRow ScheduleRow(string charId, int step)
        {
            return Catalog == null || Catalog.Schedule == null ? null : Catalog.Schedule.Find(charId, step);
        }

        /// <summary>이 인물·단계의 최소 챕터. event_schedule.csv 행이 있으면 그 값, 없으면 events.csv(일반 규칙). 단계가 표에 없으면 0.</summary>
        public int EventChapterMin(string charId, int step)
        {
            EventScheduleRow s = ScheduleRow(charId, step);
            if (s != null && s.ChapterMin > 0) return s.ChapterMin;
            for (int i = 0; i < _balance.Events.Count; i++)
                if (_balance.Events[i].Step == step) return _balance.Events[i].ChapterMin;
            return 0;
        }

        /// <summary>이 인물·단계가 닫히는 챕터(이 챕터까지 열림). 기본 99 = 만료 없음.</summary>
        public int EventChapterMax(string charId, int step)
        {
            EventScheduleRow s = ScheduleRow(charId, step);
            return s != null ? s.ChapterMax : EventSchedule.NoExpiry;
        }

        /// <summary>다음 단계 이벤트가 chapter_max 를 넘겨 만료되었는지(디자인이 만료를 정한 경우에만 true).</summary>
        public bool IsEventExpired(string charId)
        {
            CharState cs;
            if (!TryGetChar(charId, out cs)) return false;
            return _state.Chapter > EventChapterMax(charId, cs.EventsDone + 1);
        }

        /// <summary>
        /// 지금 볼 수 있는 개인 이벤트. step == EventsDone+1 이고 chapter_min(인물별 일정이 있으면 그 값) / aff_min / trust_min 을 모두 만족하고
        /// chapter_max 를 넘기지 않았으며 장면 게이트(EventGate)가 열려 있어야 한다. 루트 확정 후 비선택 인물은 null.
        /// 일정 덮어쓰기가 있으면 chapter_min 이 바뀐 사본을 돌려준다(없으면 events.csv 행 그대로).
        /// </summary>
        public EventRow GetAvailableEvent(string charId)
        {
            CharState cs;
            if (!TryGetChar(charId, out cs)) return null;
            if (IsFrozen(charId) || !IsAvailable(charId)) return null;
            for (int i = 0; i < _balance.Events.Count; i++)
            {
                EventRow e = _balance.Events[i];
                if (e.Step != cs.EventsDone + 1) continue;
                EventRow eff = EffectiveRow(charId, e);
                if (_state.Chapter > EventChapterMax(charId, e.Step)) return null;
                if (!RomanceRules.EventUnlocked(eff, _state.Chapter, cs.Aff, cs.Trust)) return null;
                if (EventGate != null && !EventGate(EventNode(charId, e.Step))) return null;
                return eff;
            }
            return null;
        }

        /// <summary>events.csv 행에 인물별 chapter_min 덮어쓰기를 적용한 행(덮어쓸 게 없으면 같은 객체).</summary>
        public EventRow EffectiveRow(string charId, EventRow e)
        {
            EventScheduleRow s = ScheduleRow(charId, e.Step);
            if (s == null || s.ChapterMin <= 0 || s.ChapterMin == e.ChapterMin) return e;
            EventRow copy = new EventRow();
            copy.Step = e.Step;
            copy.ChapterMin = s.ChapterMin;
            copy.AffMin = e.AffMin;
            copy.TrustMin = e.TrustMin;
            copy.AffGain = e.AffGain;
            copy.TrustGain = e.TrustGain;
            copy.Minutes = e.Minutes;
            return copy;
        }

        /// <summary>이벤트 완료(밤 행동 1회 소모). 성공 여부만 필요할 때 쓴다.</summary>
        public bool CompleteEvent(string charId, int step)
        {
            return CompleteEventResult(charId, step).Success;
        }

        /// <summary>이벤트 완료 + 상세 결과. 호감 +12 / 신뢰 +4(events.csv), 감쇠 없이 캡만 적용.</summary>
        public ActionResult CompleteEventResult(string charId, int step)
        {
            CharState cs;
            ActionResult refused = BeginNightAction("event", charId, out cs);
            if (refused != null) return refused;
            if (IsFrozen(charId)) return ActionResult.Refused("event", charId, RomanceRefusal.Frozen);
            EventRow e = GetAvailableEvent(charId);
            if (e == null || e.Step != step) return ActionResult.Refused("event", charId, RomanceRefusal.EventNotAvailable);

            ActionResult r = NewResult("event", charId);
            r.Minutes = e.Minutes;
            cs.EventsDone++;
            ApplyGain(charId, cs, e.AffGain, e.TrustGain, false, r);
            FinishNightAction(cs, _balance.Actions["event"].Fatigue);
            EventBus.Publish(new RomanceEventCompleted(charId, step));
            return r;
        }

        // ---------- 루트 확정 ----------

        /// <summary>루트 확정: 챕터 7 이상 + 호감 60 이상 + 신뢰 20 이상. 확정 후 다른 인물은 성장 정지.</summary>
        public bool TryLockRoute(string charId)
        {
            string reason;
            return TryLockRoute(charId, out reason);
        }

        public bool TryLockRoute(string charId, out string reason)
        {
            reason = string.Empty;
            CharState cs;
            if (!TryGetChar(charId, out cs)) { reason = RomanceRefusal.UnknownChar; return false; }
            if (_state.HasRouteLock) { reason = "already_locked"; return false; }
            if (_state.Chapter < RomanceRules.LockMinChapter) { reason = "chapter_too_early"; return false; }
            if (cs.Aff < RomanceRules.LockMinAff) { reason = "affection_too_low"; return false; }
            if (cs.Trust < RomanceRules.LockMinTrust) { reason = "trust_too_low"; return false; }
            _state.SetLock(new string[] { charId });
            EventBus.Publish(new RouteLocked(charId));
            return true;
        }

        /// <summary>공동(다자) 루트 확정. 사유 코드가 필요 없을 때 쓴다.</summary>
        public bool TryLockGroup(IList<string> ids)
        {
            string reason;
            return TryLockGroup(ids, out reason);
        }

        /// <summary>
        /// 공동 루트 확정: 인원 1~3명(중복·알 수 없는 인물 불가) + 아직 확정 전 + 챕터 7 이상 + 구성원 각자 호감 60 이상·신뢰 20 이상.
        /// 1명이면 TryLockRoute 와 같다. 성공하면 LockedRoute=첫 구성원, LockedGroup=전원(순서 유지)이며 비구성원은 동결된다.
        /// 거절 사유: group_empty / group_too_large / duplicate_member / unknown_char / already_locked / chapter_too_early /
        /// affection_too_low / trust_too_low (검사 순서 그대로, 첫 위반만 보고).
        /// </summary>
        public bool TryLockGroup(IList<string> ids, out string reason)
        {
            reason = CheckGroupLock(ids);
            if (reason != null) return false;
            string[] members = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) members[i] = ids[i];
            _state.SetLock(members);
            EventBus.Publish(new RouteLocked(members));
            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// 공동 루트 확정이 가능한지 상태 변경 없이 검사한다. 가능하면 null, 아니면 첫 번째 거절 사유 코드(TryLockGroup 과 같은 순서).
        /// </summary>
        public string CheckGroupLock(IList<string> ids)
        {
            if (ids == null || ids.Count == 0) return "group_empty";
            if (ids.Count > RomanceRules.MaxGroupSize) return "group_too_large";
            for (int i = 0; i < ids.Count; i++)
            {
                CharState probe;
                if (!TryGetChar(ids[i], out probe)) return RomanceRefusal.UnknownChar;
                for (int j = 0; j < i; j++)
                    if (ids[j] == ids[i]) return "duplicate_member";
            }
            if (_state.HasRouteLock) return "already_locked";
            if (_state.Chapter < RomanceRules.LockMinChapter) return "chapter_too_early";
            float minAff = GroupLockMinAff(ids.Count), minTrust = GroupLockMinTrust(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                CharState cs = _state.Chars[ids[i]];
                if (cs.Aff < minAff) return "affection_too_low";
                if (cs.Trust < minTrust) return "trust_too_low";
            }
            return null;
        }

        /// <summary>루트 확정에 필요한 호감. 1명은 상수(60), 2~3명(공동)은 params.csv group_lock_min_aff(데이터 기반, 단독 곡선은 그대로).</summary>
        public float GroupLockMinAff(int memberCount)
        {
            return memberCount >= 2 ? _balance.P("group_lock_min_aff") : RomanceRules.LockMinAff;
        }

        /// <summary>루트 확정에 필요한 신뢰. 1명은 상수(20), 2~3명(공동)은 params.csv group_lock_min_trust.</summary>
        public float GroupLockMinTrust(int memberCount)
        {
            return memberCount >= 2 ? _balance.P("group_lock_min_trust") : RomanceRules.LockMinTrust;
        }

        // ---------- 대화 스크립트용 직접 조정 ----------

        /// <summary>
        /// 대사 명령(선택지 결과 등)에서 호감·신뢰를 직접 바꾼다. 밤 행동을 소모하지 않고 단계 제한도 없다.
        /// 양수 호감은 후반 둔화(soft_diminish)와 챕터 캡을 적용하고, 신뢰는 캡만 적용한다. 동결 인물은 변화 없음.
        /// </summary>
        public ActionResult AdjustAffection(string charId, float aff, float trust, string source)
        {
            CharState cs;
            if (!TryGetChar(charId, out cs)) return ActionResult.Refused("adjust", charId, RomanceRefusal.UnknownChar);
            ActionResult r = NewResult("adjust", charId);
            r.Source = source ?? string.Empty;
            if (IsFrozen(charId))
            {
                r.Frozen = true;
                r.Reason = RomanceRefusal.Frozen;
                return r;
            }
            ApplyGain(charId, cs, aff, trust, true, r);
            return r;
        }

        // ---------- 플래그 ----------

        /// <summary>플래그 설정. 값이 바뀔 때만 FlagChanged 를 발행한다.</summary>
        public void SetFlag(string key, int value)
        {
            if (string.IsNullOrEmpty(key)) return;
            int old;
            bool had = _state.Flags.TryGetValue(key, out old);
            if (had && old == value) return;
            _state.Flags[key] = value;
            EventBus.Publish(new FlagChanged(key, value));
        }

        public void SetFlag(string key)
        {
            SetFlag(key, 1);
        }

        /// <summary>플래그 값. 없으면 0.</summary>
        public int GetFlag(string key)
        {
            int value;
            return key != null && _state.Flags.TryGetValue(key, out value) ? value : 0;
        }

        public bool HasFlag(string key)
        {
            return GetFlag(key) != 0;
        }

        // ---------- 내부 ----------

        private bool TryGetChar(string charId, out CharState cs)
        {
            cs = null;
            if (charId == null) return false;
            return _state.Chars.TryGetValue(charId, out cs);
        }

        private ActionResult BeginNightAction(string kind, string charId, out CharState cs)
        {
            cs = null;
            if (_state.Phase != DayPhase.Night) return ActionResult.Refused(kind, charId, RomanceRefusal.WrongPhase);
            if (_state.ActionsLeft <= 0) return ActionResult.Refused(kind, charId, RomanceRefusal.NoActions);
            if (!TryGetChar(charId, out cs)) return ActionResult.Refused(kind, charId, RomanceRefusal.UnknownChar);
            if (!IsAvailable(charId)) return ActionResult.Refused(kind, charId, RomanceRefusal.NotMet);
            return null;
        }

        private void FinishNightAction(CharState cs, float fatigue)
        {
            _state.ActionsLeft--;
            _state.Fatigue = Math.Max(0f, Math.Min(100f, _state.Fatigue + fatigue));
            cs.LastContactDay = _state.Day;
        }

        /// <summary>동결 인물과의 접촉: 행동 1회와 일상 대화 피로만 소모하고 수치는 그대로.</summary>
        private ActionResult FrozenContact(string kind, string charId, CharState cs)
        {
            ActionRow talk = _balance.Actions["talk"];
            ActionResult r = NewResult(kind, charId);
            r.Frozen = true;
            r.Reason = RomanceRefusal.Frozen;
            r.Minutes = talk.Minutes;
            FinishNightAction(cs, talk.Fatigue);
            return r;
        }

        private static ActionResult NewResult(string kind, string charId)
        {
            ActionResult r = new ActionResult();
            r.Success = true;
            r.Kind = kind;
            r.CharId = charId;
            return r;
        }

        private void SpendGold(float cost, ActionResult r)
        {
            _state.Gold -= cost;
            r.GoldSpent = cost;
            EventBus.Publish(new GoldChanged(_state.Gold, -cost));
        }

        private void ApplyGain(string charId, CharState cs, float affGain, float trustGain, bool diminish, ActionResult r)
        {
            ChapterRow chapter = _balance.Chapter(_state.Chapter);
            float oldAff = cs.Aff;
            float oldTrust = cs.Trust;
            // 난이도 배율(difficulty.csv romance_gain_mult) × 인물 만회 배율(characters.csv gain_mult): 양수 증가분(서비스·대사 보상 공통)에만 곱한다. 감소는 그대로.
            float mult = _balance.P("romance_gain_mult") * _balance.GainMultOf(charId);
            // 공동 루트 확정 뒤 구성원은 시간을 나눠 쓰므로 증가분을 키운다(params.csv group_gain_mult_2/_3; 단독·미확정은 1).
            int lockedCount = _state.LockedCount;
            if (lockedCount >= 2 && _state.IsRouteMember(charId)) mult *= _balance.P(lockedCount == 2 ? "group_gain_mult_2" : "group_gain_mult_3");
            if (affGain > 0f) affGain *= mult;
            if (trustGain > 0f) trustGain *= mult;
            float a = diminish ? RomanceRules.DiminishedGain(affGain, oldAff, _balance.P("soft_diminish")) : affGain;
            cs.Aff = RomanceRules.Clamp(oldAff + a, chapter.AffCap);
            cs.Trust = RomanceRules.Clamp(oldTrust + trustGain, chapter.TrustCap);
            r.AffDelta = cs.Aff - oldAff;
            r.TrustDelta = cs.Trust - oldTrust;
            if (r.AffDelta != 0f) EventBus.Publish(new AffectionChanged(charId, cs.Aff, r.AffDelta));
            if (r.TrustDelta != 0f) EventBus.Publish(new TrustChanged(charId, cs.Trust, r.TrustDelta));
        }
    }
}
