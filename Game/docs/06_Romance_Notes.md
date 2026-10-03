# 2단계 연애 모듈 노트

> 어셈블리 `MorpheHouse.Romance` (Core·Data 만 참조, `noEngineReferences`, C# 7.3 호환). 검증: `tools/csharp_check/romance_check/run.sh`

## 1. API 요약

| 클래스 | 역할 |
|---|---|
| `RomanceRules` | 순수 공식: 챕터 캡, `DiminishedGain`, `Clamp`, 방치 감쇠 미리보기(`DailyDecay`/`DaysUntilDecay`), 스팟 배율, 루트 확정·이벤트 해금 판정 |
| `RomanceService(state, balance, [catalog,] Func<double> rng)` | `Talk`, `Gift`, `Date`, `GetAvailableEvent`, `CompleteEvent`/`CompleteEventResult`, `TryLockRoute`, `AdjustAffection`, `SetFlag/GetFlag/HasFlag`, `IsFrozen`, `GiftReady/DateReady`. `Catalog` 가 없으면 선물·데이트는 `no_catalog` 거절 |
| `RomanceCatalog.Load(ITextSource)` | `characters / prefs / gifts / spots` CSV 로더, `GetPreference`, `TagsFor`, `UnlockedSpots` |
| `ActionResult` | `Success, Reason, AffDelta, TrustDelta, GoldSpent, Minutes, ChoiceOffered, ChoiceSucceeded, Frozen, Source` |

거절 사유 코드(`RomanceRefusal`): `wrong_phase, no_actions, unknown_char, not_met, unknown_gift, unknown_spot, no_catalog, cooldown, no_gold, spot_locked, event_not_available, frozen`. 일상적 거절은 예외를 던지지 않는다.

새 이벤트(`Core/GameEvents.cs`): `AffectionChanged`, `TrustChanged`, `RomanceEventCompleted`, `RouteLocked`, `FlagChanged`.

## 2. 규칙표

| 항목 | 규칙 | 출처 |
|---|---|---|
| 밤 행동 공통 | `Phase==Night && ActionsLeft>0`; 행동 1회·피로(actions.csv)·`LastContactDay` 갱신 | GDD §4.4 |
| 등장 전 인물 (QA-03) | `characters.csv first_chapter`(하울·곰석 1, 요한·나비 2, 범준 3, 하루 4) 이전에는 대화·선물·데이트·이벤트 모두 `not_met` 로 거절(상태 불변). `RomanceService.IsAvailable`, `NightRomanceActions.IsAvailable/AvailableChars/CharUnavailableText/GiftBlockReason/DateBlockReason`(UI 인물 목록은 이 값으로 숨기거나 비활성). `staff_join_chapter`(직원 영업 보너스)와 별개이며 `BusinessSimulator` 도 같은 `BalanceTable.Characters` 를 읽는다(QA-14, 코드 내 중복 상수 없음) | characters.csv |
| 호감 증가 | 양수 증가분 × `romance_gain_mult`(난이도) × 인물 `gain_mult`(만회) × (1 − soft_diminish × aff/100), 음수는 그대로, 0..챕터 캡 | simulate_balance.py |
| 대화 (QA-05) | **서비스는 `talk` 행(+2, 신뢰 `talk.trust_gain`)만 준다.** 선택지 보상(정답 +5/+1.5, 오답 -1/-1, 무난 0)은 대화 노드가 `<<aff>>/<<trust>>`(=AdjustAffection)로 직접 준다. 무작위 선택 굴림 삭제(`choice_prob` 제거). 노드 1개 = 선택지 2회 | actions |
| 선물 | 취향별 gift_like +8 / neutral +3 / dislike -5·신뢰 -2, 쿨다운 2일, 골드 100+60×챕터 | actions/params |
| 데이트 | +12/신뢰 +1.5, 쿨다운 3일, 골드 200+50×챕터, 스팟은 `unlock_chapter <= 현재 챕터`, 좋아하는 스팟 ×1.25 | actions/params |
| 개인 이벤트 | step=EventsDone+1, chapter_min·aff_min·trust_min 모두 충족 + 장면 게이트(21b), events.csv 의 aff_gain/trust_gain(감쇠 없음, 캡 적용) + 이벤트 노드의 선택지 보상 | events.csv |
| 피로 제한 (QA-08) | 영업 뒤 피로 ≥ `fatigue_limit`(80)이면 밤 행동 1회. **그 밤 휴식은 1회 무료**(행동을 쓰지 않고 피로 -25, 행동을 다 쓴 뒤에도 가능, `DayCycle.FreeRestAvailable`); 제한 없는 밤의 휴식은 전과 같이 행동 1회. 'date 만 하는 봇': 제한 밤 66→19/192, 쓴 행동 247→293/384 | DayCycle |
| 루트 확정 | 챕터 ≥7, 1회만. 단독: 호감 ≥60 · 신뢰 ≥20. **공동(2~3명)**: 전원 호감 ≥ `group_lock_min_aff`(50) · 신뢰 ≥ `group_lock_min_trust`(7), 확정 뒤 구성원 증가 x`group_gain_mult_2/3`(1.0/1.7), 엔딩 기준은 endings.csv `harem_*`(72/24/3, 65/14/2). 이후 비선택 인물은 동결(접촉은 행동 소모, 수치·골드·쿨다운 변화 없음, 이벤트 불가) | GDD §3, §4.4 |
| 방치 감쇠 | `DayCycle` 이 적용(서비스는 중복 적용 안 함) | DayCycle |

## 3. 패리티 결과 (Normal, 목표 howl, 300회/집중도, 굿 이상 = 호감 85+ · 신뢰 40+ · 이벤트 5)

기준 플레이어(`tools/simulate_balance.py` = `qa_hunt` 'focus' 봇 = `romance_check` 봇): 밤 행동은 전부 연애, 목표 이벤트 최우선, 데이트>선물>대화(좋아하는 선물·스팟), 대화 선택지는 무작위, 7장 첫 밤부터 조건 충족 시 루트 확정, 확정 뒤 동결 인물에게 낭비 없음. 숫자는 `docs/03_balance_report.md` 와 같은 표다. `sh tools/csharp_check/romance_check/run.sh` 가 ±10pp 를 검사한다(C# 300회: 20% 5.3 / 35% 46.7 / 50% 83.3 / 60% 93.3 / 80% 100 vs 리포트 6 / 52 / 88 / 95 / 99; qa_hunt 실제 대본 봇: Normal 6 / 44 / 76 / 97).

## 4. 알려진 차이 / 공백

- 선물·데이트는 골드가 부족하면 거절한다(파이썬은 골드가 부족하면 해당 행동을 건너뛰고 대화). 하네스는 대화로 대체한다.
- 데이트 스팟 취향: 좋아함 +25%는 명세, **싫어함 -25%는 임의 추가**(명세 없음). 하네스는 파이썬과 같은 기본 수치를 쓰려고 "보통" 스팟을 고른다.
- 동결 인물과의 접촉은 일상 대화 피로(+5)를 더한다(파이썬은 피로 없음). 영향 미미.
- 루트 확정은 7장 첫 밤부터 대상이 조건을 만족하는 밤에 한다(파이썬·하네스·qa_hunt 동일).
- 타이쿤 '관리 행동' 비율(`romance_share`)·`mgmt_action_min`·`gift_like_prob`·`gift_neutral_prob`·`choice_prob` 는 C# 에 대응물이 없어 params.csv 에서 삭제했다(QA-04/14). 새 파이썬 전용 모델 상수: `talk_choice_points`(2) `talk_right_prob`(0.36) `talk_wrong_prob`(0.31) `key_flag_prob` `ledger_truth_prob`, events.csv `script_aff/script_trust`(이벤트 노드 선택지 보상 평균) — `sh tools/csharp_check/qa_hunt/run.sh gain` 이 실측해 출력한다.
- 다자 루트(docs/10 §2, 2026-10-03 재조정): 무작위 선택 기준 f=100% 에서 2인 57% / 3인 32%.
- 숙련 플레이어(정답률 `choice_success_prob`)는 훨씬 쉽다: 대화 1회 = 선택지 2회라 정답만 고르면 신뢰 +3. `python3 tools/simulate_balance.py --skill params` 로 본다(Normal 에서 20% 집중도도 약 64%). 기준 곡선은 무작위 선택(=힌트 없는 최소 숙련) 기준이다.
- `prefs.csv` 의 `menu` 18행과 `spots.csv` 의 `period` 열은 아직 코드가 읽지 않는 예약 데이터다(메뉴 취향·시간대 기능용, QA-14). 삭제하지 않고 여기에 표시만 한다.
- `prefs.csv` 의 선물 **싫어함**(party_horn_set 등 6종)은 GDD §2 의 하락 요인(행동 서술)에서 추정해 만든 값이다. 메뉴 취향은 로드만 하며 서비스에서 아직 쓰지 않는다. 9번째 스팟 `rooftop`(옥상 정원)은 GDD §4.4 의 보너스 스팟이며 6장 해금으로 가정.
- `AdjustAffection` 은 후반 둔화를 적용한다(대사 선택지 보상 취급). 정확한 값 지정이 필요하면 별도 API 가 필요.
- 트루 엔딩 판정(열쇠 플래그 등)은 이 모듈 범위 밖이며 플래그 저장소(`state.Flags`)만 제공한다.
