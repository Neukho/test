# 21b. 장면 게이트 · 인물별 이벤트 일정 · 공통 장면 스케줄

> 코드: `Romance/EventSchedule.cs`, `Romance/RomanceService.cs`(일정·게이트 연동), `Game/SceneGate.cs`, `Game/ChapterFlow.cs`(`NextCommonScene`), `Game/NightRomanceActions.cs`(자동 연결).
> 데이터: `balance/event_schedule.csv`(30행), `balance/scene_rules.csv`(50행) — 둘 다 `Resources/Balance/` 에 복사돼 있다(`MorpheHouse/Data/Sync Balance CSV` 가 `*.csv` 전체를 복사하므로 이후에도 유지됨).
> 검증: `sh tools/csharp_check/gate_check/run.sh` (162 checks).
> 근거: `10_Route_Structure.md`, `12_Stage3_Guide.md §5-1`, `08_Scenario_Bible_*.md`, `Dialogue/**` 헤더 주석(작가가 적은 의존 관계).

## 1. 인물별 이벤트 일정 (`event_schedule.csv`)

`events.csv` 는 단계(1~5)별 일반 최소 챕터(1/3/5/6/8)만 준다. 스크립트·GDD §2 는 인물마다 다르게 배치하므로, 인물·단계별 선택 덮어쓰기를 둔다.

- 열: `char,step,chapter_min,chapter_max,note`. 행이 없는 (인물, 단계)는 `events.csv` 를 그대로 쓴다. 표 파일 자체가 없어도 동작한다(빈 표).
- `chapter_max`: 이 챕터를 넘으면 이벤트가 **만료**(닫힘)된다. 기본 99 = 만료 없음. 지금은 30행 모두 99다(설계가 만료를 정한 이벤트는 없다. 단계가 순서대로 열리므로 한 번 만료되면 그 루트의 이후 단계도 모두 막힌다 — 쓰려면 신중히).
- `chapter_min` 비움/0 = `events.csv` 값. `chapter_max` 비움/0 = 99.
- `RomanceCatalog.Load` 가 읽고(`Catalog.Schedule`), `RomanceService.GetAvailableEvent` 가 적용한다: 일정이 있으면 `ChapterMin` 이 바뀐 **사본** `EventRow` 를 돌려준다(원본 `events.csv` 행은 그대로). 보조: `EventChapterMin/Max(char, step)`, `IsEventExpired(char)`, `EffectiveRow`.
- 파이썬 시뮬(`tools/simulate_balance.py`)도 같은 파일을 읽는다(`Data.schedule`, `event_window`).

| 인물 | E1 | E2 | E3 | E4 | E5 | 일반(events.csv)과 다른 곳 |
|---|---|---|---|---|---|---|
| 하울 | 1 | 3 | 5 | 7 | 8 | E4: 6 → 7 |
| 요한 | 2 | 3 | 5 | 7 | 8 | E1: 1 → 2, E4: 6 → 7 |
| 범준 | 3 | 4 | 6 | 7 | 8 | E1: 1 → 3, E2: 3 → 4, E3: 5 → 6, E4: 6 → 7 |
| 하루 | 4 | 5 | 6 | 7 | 8 | E1: 1 → 4(4장 합류), E2: 3 → 5, E3: 5 → 6, E4: 6 → 7 |
| 곰석 | 1 | 3 | 5 | 6 | 8 | 없음 |
| 나비 | 2 | 4 | 5 | 7 | 8 | E1: 1 → 2, E2: 3 → 4, E4: 6 → 7 |

출처: 각 바이블 이벤트 표(챕터), 공통 바이블 §3(`Nabi_E2` 모순 기록), GDD §2 챕터별 루트 목록. 호감·신뢰 하한은 `events.csv` 그대로.

## 2. 장면 게이트 (`scene_rules.csv`, `SceneGate`)

호스트가 장면(공통 장면·개인 이벤트)을 재생하기 **전에** 묻는 순수 데이터 기반 판정. 규칙이 없는 노드는 항상 허용.

### 열

| 열 | 뜻 |
|---|---|
| `node` | 장면 노드 이름 |
| `kind` | `chapter`(도입/마무리) · `common`(공통 장면) · `event`(개인 이벤트) — 정보용 |
| `chapter` | `min_day_in_chapter` 와 자동 재생 창이 속한 챕터(0 = 챕터·일차 조건 없음) |
| `slot` | 자동 재생 시점: `morning`(아침) · `night`(밤 메뉴 열기 전) · `night_end`(행동을 다 쓴 뒤, 정산 전) |
| `auto_play` | 1 이면 호스트가 `NextAutoScene` 으로 직접 재생 |
| `trigger_day_min/max` | 자동 재생 창(챕터 내 일차). 마지막 날(max)이 마감 |
| `min_day_in_chapter` | `chapter` 안에서 열리는 가장 이른 일차. 이후 챕터에서는 항상 통과(자동 장면은 `max(min_day, trigger_day_min)`) |
| `requires_flags` / `forbids_flags` | `;` 구분. 켜져 있어야/없어야 하는 플래그 |
| `after_node` | `;` 구분. 이미 재생되어 있어야 하는 노드(프로젝트에 없는 노드는 충족으로 본다). `~` 접두 = soft |
| `before_node` | `;` 구분. 이 중 하나라도 이미 재생되었으면 이 장면은 너무 늦다(순서 보호) |
| `note` | 근거 |

### 판정 `SceneGate.Check(node)` → `Allowed` / `Blocked(reason, detail)`

순서: 챕터·일차(`chapter_early`/`day_early`) → 플래그(`needs_flag`/`forbidden_flag`) → 선행 노드(`needs_node`) → 너무 늦음(`too_late`). 가짜 상태를 넣을 수 있게 `ISceneContext`(챕터, 챕터 내 일차, 방문 여부, 노드 존재, 플래그)로 추상화했고, 기본 구현 `StateSceneContext` 는 `GameState` + 방문/존재 함수를 쓴다.

**soft(`~`) 선행**: 자동 재생 창의 **마지막 날(trigger_day_max)** 부터 요구를 접는다. 개인 이벤트가 하나도 없는 플레이어도 공통 장면(메뉴 튜토리얼 등)은 반드시 보게 하려는 장치다. 예: `CE02_Tasting` 은 `Johan_E1` 이후 + 2장 6일차 이후에 재생하되, 8일차에는 `Johan_E1` 없이도 재생한다(CE02 대사는 요한의 칼럼 내용을 인용하지 않는다).

**교착 방지 규칙(표 불변식, 하네스가 검사)**: 하드 `after_node` 는 항상 결국 재생되는 노드(챕터 도입/마무리·자동 공통 장면)만 가리킨다. 개인 이벤트끼리의 하드 의존은 없다. 그래서 어떤 개인 이벤트도 "영원히 막히지" 않는다(§5).

### 자동 재생 `SceneGate.NextAutoScene(slot)`

표 순서(= 시간순 우선순위)로 첫 후보: `auto_play`, 아직 안 봤고, 노드가 존재하고, (장면의 `slot` ≤ 현재 슬롯 **이거나** 창이 지남) 이며 `Check` 허용. 창이 지난(overdue) 장면은 슬롯과 상관없이 가장 먼저 나온다(예전 저장·밤 끝 호출 누락 대비). 방문 기록이 남으면 다시 나오지 않는다 = 정확히 한 번.

### 호스트 연결(요청)

- 이미 자동: `NightRomanceActions` 는 방문 함수(`hasVisited`)가 주어지고 카탈로그에 `scene_rules` 가 있으면 게이트를 스스로 만들고 `RomanceService.EventGate` 에 연결한다. 그 결과 밤 메뉴의 ★(`HasEvent`)·`Event()`·`CompleteEvent` 가 게이트를 따른다. 닫힌 이벤트는 제공되지 않고, 조건은 충족했지만 게이트만 닫힌 경우 `EventHint` 는 스포일러 없는 "아직 때가 아니다"(`NightRomanceActions.EventNotYetText`, ui_ko/en msg_15)를 돌려준다(QA-11, 더는 빈 문자열이 아니다). `hasVisited == null` 이거나 규칙 표가 없으면 게이트는 꺼진 채(기존 동작)다. 수동 제어: `EnableSceneGate(table)` / `DisableSceneGate()` / `Gate`.
- **UI 쪽에서 해야 할 것**(내가 수정할 수 없는 영역): 아래 세 지점에서 `Ctx.Night.NextCommonScene(slot)` 을 `NoStep` 이 나올 때까지 반복 호출하고, 반환된 `Node` 를 재생(`ChapterStepKind.CommonScene`)한다.
  1. **아침**: 챕터 도입/마무리(`ChapterFlow.NextMorningStep`)가 `None` 이 된 뒤, `SceneSlot.Morning`.
  2. **밤 메뉴를 열 때와 밤 행동 하나를 마치고 메뉴로 돌아올 때**: `SceneSlot.Night`.
  3. **밤 행동을 다 썼거나 밤을 끝낼 때(정산 전)**: `SceneSlot.NightEnd`. 호출이 빠져도 다음 날 아침에 `overdue` 로 재생된다.
  - `Ch7_Intro` 끝의 `route_choice` 화면이 닫힌 직후에도 (1)을 한 번 더 호출하면 같은 아침에 `CE07` 이 이어진다.
  - `Ch8_Outro` 는 기존대로 `ChapterFlow.FinalOutro`(엔딩 직전)가 맡는다. 규칙 표에는 넣지 않았다.

## 3. 규칙 표 전체 (50행)

### 3-1. 자동 재생 공통 장면 (11행, `auto_play=1`)

| 노드 | 챕터 | 슬롯 | 창(일차) | 선행 | 근거 |
|---|---|---|---|---|---|
| `CE01_FirstGuest` | 1 | morning | 5 | `Ch1_Intro` | "개업을 이틀 앞둔 오전"(1장 6일 중 5일차) |
| `CE02_Tasting` | 2 | morning | 6~8 | soft `Johan_E1` (+ 6일차 이후) | docs/12 §5-1, ch02 주석 |
| `CE03_RentMeeting` | 3 | morning | 4~6 | `Ch3_Intro`, soft `Beomjun_E1`·`Howl_E2` | ch03 시간표 "4~6일차 CE03" |
| `CE04_StaffWelcome` | 4 | night | 5~8 | `Ch4_Intro`, soft `Haru_E1` | "하루 첫 출근 뒤, 마감 뒤에 연다" |
| `CE05_FestivalPrep` | 5 | morning | 3~5 | `Ch5_Intro`, before `Ch5_Show` | "4일차 전후 낮" |
| `Ch5_Show` | 5 | night | 7 | `CE05_FestivalPrep`, before `Ch5_Show_After` | 공연 = 5장 7일차 저녁 |
| `Ch5_Show_After` | 5 | night_end | 7 | `Ch5_Show`, before `Howl_E3` | 공연 날 밤 행동이 끝난 뒤 |
| `CE06_OldPhoto` | 6 | morning | 2~4 | `Ch6_Intro`, before `Ch6_LedgerTruth` | 뒷방 정리, `Gomseok_E4`·`Howl_E4/E5` 전제 |
| `Ch6_LedgerTruth` | 6 | night | 10 | `CE06_OldPhoto`, before `Ch6_Outro` | "달의 마지막 날 밤" |
| `CE07_BlueHarborOpening` | 7 | morning | 1~2 | `Ch7_Intro` | ch07 주석 "개점식, 7장 1~2일째" |
| `CE08_TableTogether` | 8 | morning | 3~5 | `Ch8_Intro` | ch08 주석, 각 E5 의 전제 |

### 3-2. 챕터 도입/마무리 (14행, 정보·순서 참조용; 재생은 `ChapterFlow`)
`Ch1..Ch8_Intro`(각 챕터 1일차), `Ch2..Ch7_Outro`(다음 챕터 1일차). `Ch6_Outro` 는 `drawer_key_clue` 재도전 선택지가 있어 `Howl_E4` 보다 먼저여야 하므로 참조된다.

### 3-3. 개인 이벤트 게이트 (25행)

| 이벤트 | 챕터·최소 일차 | 선행(하드) | 근거(스크립트/바이블) |
|---|---|---|---|
| `Johan_E1` | 2장 5일차~ | `Ch2_Intro` | 공통 바이블 §3 "첫 영업 5일 뒤" |
| `Nabi_E1` | 2장 6일차~ | `Ch2_Intro` | "2장 말미", Ch2_Outro 3줄과 순서 무관 |
| `Johan_E2` | 3장 2일차~ | `Ch3_Intro` | ch03 "2일차 Johan_E2" |
| `Howl_E2` | 3장 3일차~ | `Ch3_Intro` | "3일차 새벽 Howl_E2" |
| `Beomjun_E1` | 3장 3일차~ | `Ch3_Intro` | "3일차 아침 Beomjun_E1" |
| `Haru_E1` | 4장 4일차~ | `Ch4_Intro` | 면접 사흘 뒤 첫 출근 |
| `Beomjun_E2` | 4장 | `CE04_StaffWelcome` | 주문 카드(목요일 20개) 복선 |
| `Nabi_E2` | 4장 | `Ch4_Intro` | 안쪽 무대 소문을 Intro 가 깖 |
| `Haru_E2` | 5장 6일차~ | `Ch5_Intro` | 공연 전날 밤(만료 없음) |
| `Johan_E3` | 5장 7일차~ | `Ch5_Show` | docs/12 §5-1 순서 |
| `Nabi_E3` | 5장 7일차~ | `Ch5_Show` | 〃 |
| `Howl_E3` | 5장 8일차~ | `Ch5_Show_After` | 〃 "한 시간 뒤" = 밤 행동 한 번 뒤(다음 밤) |
| `Haru_E3` | 6장 | `Ch6_LedgerTruth` | 매트릭스 "하루 E3 '장부 얘기 들었다'" |
| `Gomseok_E4` | 6장 | `CE06_OldPhoto` | E4 서두 "낡은 사진을 본 날 이후" |
| `Howl_E4` | 7장 3일차~ | `CE06_OldPhoto`, `Ch6_Outro`, `CE07_BlueHarborOpening` | `drawer_key_clue`(CE06 또는 Outro 재도전) |
| `Johan_E4` `Beomjun_E4` `Haru_E4` `Nabi_E4` | 7장 3일차~ | `CE07_BlueHarborOpening` | ch07 "각 E4 는 개점 사흘째 이후"; E4 압박은 CE07 에서 심김. CE07 은 `Ch7_Intro`(끝의 `route_choice`) 뒤이므로 E4 는 항상 루트 선택 이후 |
| `Howl_E5` `Johan_E5` `Beomjun_E5` `Haru_E5` `Gomseok_E5` `Nabi_E5` | 8장 | `Ch8_Intro`, `CE08_TableTogether` | ch08 "각 인물 E5(모두 CE08 이후)", `Gomseok_E5` 는 `chain_crisis_resolved`(CE08 설정), `Nabi_E5` 는 `Ch8_Intro` 이후 |

의도적으로 **게이트하지 않은 것**: `Howl_E1`("개업 전날 밤" 일차 제한은 1장 시작 직후 이벤트를 막아 호감 10 을 일찍 모은 플레이어에게 불리하고 서사 손실이 작아 두지 않음), `Gomseok_E1~E3`(날짜 단서 없음), 요한→나비 E3 순서(아래 §4).

## 4. 알려진 불일치와 의도

1. **`Johan_E3` → `Nabi_E3` 순서는 강제하지 않는다.** 둘을 동시에 노리는 공동 루트에서 하나를 숨기면 한 명 루트 플레이어에게 우회를 강요하게 된다. 둘 다 `Ch5_Show` 이후에만 열린다.
2. **`Ch5_Show_After` 는 공연 날 밤 행동이 끝난 뒤 한 번** 재생된다(`night_end`). 그 밤에 `Nabi_E3` 를 못 봤다면 대사의 `nabi_e3_*` 플래그 분기가 공백을 메우고, `Nabi_E3` 는 이후에도 열려 있다(만료 없음).
3. **만료 없음의 대가**: `Haru_E2`(전날 밤)·`Beomjun_E1`(CE03 전) 같은 이벤트를 늦게 보면 장면의 시간 배경이 어긋난다. CE03 은 soft 로 `Beomjun_E1`/`Howl_E2` 를 6일차까지 기다려 어긋남을 줄인다.
4. **공통 장면 조건**: `CE02/CE03/CE04` 는 마감일에는 선행 개인 이벤트 없이도 재생된다(튜토리얼 보장).
5. 인물 등장(가용성)은 `characters.csv first_chapter` 로 별도 처리한다(QA-03, docs/06 §2): 하울·곰석 1장, 요한·나비 2장, 범준 3장, 하루 4장. 이벤트 일정(위 표)은 항상 이 값 이후다.

## 5. 검증 요약

`sh tools/csharp_check/gate_check/run.sh` — 162 checks, 0 failures.

| 구역 | 내용 |
|---|---|
| A 규칙 표 | 파싱·일관성(`Validate`: 일차 ≤ 챕터 길이, 창 순서), 자동 11개 목록·시간순, 하드 선행 교착 없음, soft 는 자동 장면에만, 일정 30행·만료 없음·단계 단조, `Resources/Balance` 사본 동일 |
| B 판정 | 가짜 컨텍스트: 챕터/일차/플래그/선행/후행/soft 마감/없는 노드 관대/판정 순서/CSV 파싱/중복 행 |
| C 실제 규칙 | CE02·CE03·CE04, 3장 시간표, 5장 순서 전체(Show → Johan/Nabi E3 → Show_After → Howl_E3 다음 밤), `Gomseok_E4`↔CE06, `Howl_E4` 서랍 선행, 모든 E4↔CE07, 모든 E5↔CE08, `Nabi_E5`↔`Ch8_Intro` |
| D 스케줄러 | 슬롯별 제공·한 번만·지난 장면 우선·없는 노드 건너뜀·`ChapterFlow.NextCommonScene` |
| E 서비스 | 나비 E2 가 3장에 안 열림(events.csv 는 3), 사본·폴백·만료(`chapter_max`)·`EventGate`(닫힘 → null/거절/행동 미소모)·`EventNode` |
| F 밤 메뉴 | 게이트 자동 연결·`HasEvent`/`Event` 가 게이트를 따름·`EventHint`·끄기/켜기·방문 정보 없음 |
| G 64일 봇 | 정책 3종 + 20 시드: **모든 CE 노드가 정확히 한 번, 시간순**, 마지막에 막힌 자동 장면 없음, 도입/마무리 한 번씩. 6개 단독 루트 + 공동(3인 하울·나비·곰석, 2인 요한·하루): 구성원 전원 **5이벤트 완료**, 이벤트 순서·창 계약, 끝에서 30개 이벤트 게이트 모두 열림 |
| H 패리티 | 게이트를 켠 봇(하울 목표, 150회) vs 문서 `03_balance_report.md` |

### 패리티(Normal, 하울 목표, 굿 이상 %; 문서 0/37/84/94/99)

| 집중도 | 문서 | C# 일정 도입 전(300회) | C# 일정 도입 후(300회) | C# 게이트+일정 봇(150회) | 파이썬 전 | 파이썬 후 |
|---|---|---|---|---|---|---|
| 20% | 0 | 1.0 | 0.3 | 0.0 | 0 | 1 |
| 35% | 37 | 45.7 | 37.0 | 42.7 | 41 | 38 |
| 50% | 84 | 90.7 | 87.7 | 85.3 | 88 | 85 |
| 60% | 94 | 95.7 | 94.3 | 96.0 | 95 | 95 |
| 80% | 99 | 99.3 | 99.3 | 100.0 | 99 | 99 |

모두 문서 대비 ±10pp 이내이고, 인물별 일정이 오히려 문서에 더 가까워졌다(35%: +8.7pp → 0.0pp). 이벤트 평균도 3.5~3.6 / 4.8 / 5.0 으로 동일.
