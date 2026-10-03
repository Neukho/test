# 3단계 가이드 — 챕터 흐름 · 루트 선택 · 엔딩 · 프로필

> 코드: `UnityProject/Assets/_Project/`. `09_Stage2_Guide.md`(VN·밤 메뉴) 이후 **새로 생긴 것만** 다룬다. 규칙 원문은 `10_Route_Structure.md`, `11_Ending_Logic.md`.
> Unity 가 없는 환경에서 작성되어 **Unity 의존 코드(화면 조립·입력·레이아웃)는 컴파일만 확인했고 실행하지 못했다**(§6).

## 1. 새 흐름 한눈에

| 흐름 | 동작 | 코드 |
|---|---|---|
| 챕터 전환 | 챕터 첫 아침마다 `Ch<N-1>_Outro`(있고 안 봤으면) → `Ch<N>_Intro`(있고 안 봤으면) → 둘 다 없고 정보 카드가 남았으면 카드. **상태 기반**(챕터·챕터 내 일차·방문 기록)이라 아침 저장 후 불러와도 못 본 장면이 이어진다. 노드가 없으면 조용히 건너뜀 | `Game/ChapterFlow.cs`, `GameScreen.ShowMorning` |
| 루트 선택 | `RouteSelectScreen`. 대사의 `<<route_choice>>`(7장 인트로 끝, 장면이 끝난 뒤 열림) 또는 7~8장 밤 메뉴 "루트 확정"(확정 전까지 항상 보임, 확정 가능자가 있으면 ★)로 진입. 1명=단독, 2~3명=공동, "정하지 않고 계속(커뮤니티 엔딩)". 확인 대화 상자에 동결 경고 | `Game/RouteSelect.cs`(순수 모델), `UI/RouteSelectScreen.cs` |
| 공동 확정 후 | `Harem_Table`(있으면) → 구성원 순서대로 `<Char>_Harem_Consent`(있으면). 호스트는 `harem_ok_<char>` 를 **절대 설정하지 않는다**(노드가 설정) | `RouteFlow.PostLockScenes` |
| 동의 재시도 | 공동(2명+) 확정 + 해당 구성원 `harem_ok_*` 없음 + 노드 존재 + 게임 진행 중이면 밤 메뉴에 "동의 확인 대화(이름)"(행동 횟수 소모 없음). 8장이 끝나면(게임 종료) 사라짐 | `ConsentRetry`(`Game/NightExtras.cs`) |
| 복구 장면 | 데이터 표 `RecoveryTable`. 기본 규칙: `howl_e4_fail` 가 켜져 있고 `howl_e4_trust`·`howl_e4_trust_late` 둘 다 없으면 `Howl_E4_Retry`(한 번만, 방문 기록 기준, 행동 소모 없음) | `RecoveryTable.Default()` |
| 밤 메뉴 항목 수 | 복구/동의 항목이 2개 이하면 인라인(버튼 높이 54), 3개 이상이면 "이어지는 장면 (N)" 한 버튼 → 목록 | `GameScreen.AddExtraButtons` |
| 관계 패널·대표 인물 | 공동 구성원 전원 `(루트)`. 밤 스탠딩은 구성원 중 호감 최고 인물 | `RelationPanel`, `GameScreen.FavoriteChar` |
| 엔딩 | 8챕터 마지막 정산(`다음 날`) 직후 `AppContext.BeginEnding`: 남은 `Ch8_Outro` → `EndingPlan.Create` → 장면 목록 재생 → `EndScreen` | `Game/EndingFlow.cs`, `UI/EndScreen.cs` |
| 프로필 | `profile.json`(`persistentDataPath`): 도달 엔딩(키→횟수)·본 CG·해금 id. 타이틀에 "도달한 엔딩 N / 21" + 갤러리 스텁 | `Game/ProfileService.cs` |
| 선택지 | 조건 미충족은 회색 + " (조건 미충족)"(숨은 열쇠 조건은 노출 안 함). `VnStage.ShowUnavailableChoices=false` 면 숨김 | `ChoiceLabel`, `VnStage` |

### 엔딩 장면 목록(`EndingFlow.Scenes`)

| 종류 | 재생 순서 | 노드 없을 때 |
|---|---|---|
| 단독(노멀/굿/트루) | `<Char>_End_*` 한 노드(안에서 `<<jump>>` 해도 호스트는 한 노드) | `EndingPlan.FallbackText` |
| 공동 2·3인 | `Ending_Harem2|3` → 구성원 순서대로 `<Char>_Harem_Epilogue` | 주 노드는 `FallbackText`, 에필로그는 구성원 이름이 든 짧은 문구 |
| 커뮤니티 | `Ending_Community` | `FallbackText` |

엔딩 도달은 **장면을 재생하기 전에** 프로필에 기록·저장한다(중간에 끄더라도 남는다). 엔딩 키: `normal:howl`·`good:howl`·`true:howl`(6명×3=18), `harem_duo`, `harem_trio`, `community` = 21칸.

### route_choice 처리 방식
`DialogueCommandDispatcher` 가 `route_choice` 를 알려진 커맨드로 처리해 **요청 플래그만** 세운다(선택을 하지 않음). `DialogueDirector.Play` 는 장면이 끝난 뒤 `ConsumeRouteChoice()` 가 true 이고 아직 루트 확정 전이면 게임 화면 대신 루트 선택 화면을 연다. 즉 커맨드가 노드 중간에 있어도 그 장면이 먼저 끝난다. 이미 확정된 뒤의 요청은 무시.

### "정하지 않고 계속"
확정 가능한 인물이 있는데도 이 버튼을 누르면 호스트 전용 플래그 `sys_route_declined=1` 을 저장하고 밤 시작 자동 안내를 멈춘다(밤 메뉴 항목은 계속 있음). 확정 가능자가 없을 때 누르면 플래그를 세우지 않아 나중에 적격자가 생기면 다시 안내한다. "보류(뒤로)" 는 아무 기록도 남기지 않는다(그날은 다시 안 뜸).

## 2. 시나리오 작가와의 계약(호스트가 기대하는 것)

- 노드: `Ch<N>_Intro` / `Ch<N>_Outro`(N=1..8), `Harem_Table`, `<Char>_Harem_Consent`, `<Char>_Harem_Epilogue`, `Ending_Harem2|3`, `Ending_Community`, `<Char>_End_Normal|Good|True`, `Howl_E4_Retry`. 모두 없어도 게임은 진행된다.
- `Ch8_Outro` 는 일반 챕터 흐름이 아니라 **엔딩 직전**에 재생된다(`ChapterFlow.FinalOutro`).
- 동의 노드는 동의했을 때만 `<<flag harem_ok_<char>>>` 를 설정해야 한다. 설정 없이 끝나면 위 "동의 확인 대화" 로 재시도된다. 복구 노드는 결과 플래그(`howl_e4_trust_late` 등)를 스스로 설정.
- `<<show_cg X>>`: 인물 CG 는 `<CHAR>_E<1..6>`. 그 밖에 `^[A-Z][A-Z0-9]*(_[A-Z0-9]+)+$`(대소문자 무관, 대문자로 정규화)는 **공통 CG 키**(`HAREM_E1`, `COMMON_CE03`, `COMMON_CH6_PHOTO`)로 `IVnCommandSink.ShowCommonCg` 로 간다. 파일은 `Resources/Art/CG/Common/CG_<키>.png`, 없으면 플레이스홀더(중립 그라데이션 + 키 해시 색). 첫 토큰이 인물 id 인 id(`HOWL_E9` 같은 오타)와 형식 위반은 경고 후 무시. 플레이스홀더 생성기는 `HAREM_E1` 만 미리 만든다. 본 CG 추적(프로필·`RunCgs`)에도 키 그대로 기록된다.

## 3. 새로 필요한 Unity 클릭
없음(씬 변경 없음). 폴더 갱신 후 컴파일 대기 → (시나리오를 고쳤다면) MorpheHouse → Dialogue → Sync Scripts → Play.
`AppContext` 생성자에 `ProfileService` 인자가 추가되어 `GameBootstrap` 이 `profile.json` 을 만든다(직접 `new AppContext` 하는 코드가 있다면 인자를 추가).

## 4. 수동 QA 체크리스트

**챕터 흐름**
- [ ] 새 게임 → `Ch1_Intro`(VN) → 아침. 정산 후 챕터가 바뀌면 `Ch<N>_Outro`(있다면) → `Ch<N+1>_Intro`(있다면) → 아침. 노드가 없는 챕터는 정보 카드.
- [ ] 챕터 첫 아침에 자동 저장된 슬롯을 불러오면, 아직 안 본 Outro/Intro 가 이어서 나온다. 이미 본 장면은 다시 나오지 않는다.
- [ ] 저장은 여전히 아침에만 된다(저장 버튼은 아침 화면에만).

**루트 선택**
- [ ] 6장까지 밤 메뉴에 루트 항목이 없다. 7장부터 항목이 보이고, 확정 가능자가 있으면 ★.
- [ ] 화면에 6명의 호감/신뢰와 "선택 가능" 또는 "선택 불가 — (사유: 7장부터 / 호감도 부족(필요 60) / 신뢰도 부족(필요 20))".
- [ ] 1명 선택 → 단독 설명. 2·3명 선택 → 공동 요건 설명(호감 75·신뢰 34 / 70·30, 이벤트 4단계, 전원 동의, 미달 시 커뮤니티). 4번째 선택은 "최대 3명" 안내.
- [ ] "이 선택으로 확정" → 확인 대화 상자(동결 경고, 공동이면 요건 재안내) → 확정 후 토스트. 비구성원은 관계 패널에서 "(고정)", 구성원은 "(루트)".
- [ ] 공동 확정 직후 `Harem_Table` → 구성원별 동의 장면 순서 재생(노드가 있을 때). 동의가 설정되지 않은 노드는 밤 메뉴에 "동의 확인 대화(이름)" 로 남고, 다시 보면 동의 플래그가 설정되면서 항목이 사라진다.
- [ ] "정하지 않고 계속" 후 같은 밤에 자동 안내가 다시 뜨지 않고, 이후 밤에도 안 뜬다(확정 가능자가 있었던 경우). 메뉴 항목으로는 계속 열 수 있다.
- [ ] 7장 인트로가 `<<route_choice>>` 로 끝나면 장면이 끝난 뒤 루트 선택 화면이 열리고, 닫으면 아침 화면으로 돌아온다.
- [ ] `howl_e4_fail` 가 있는 세이브: 밤 메뉴에 "하울과 다시 마주하기" → 재생 후 사라진다(한 번만).

**엔딩(종류별)**
- [ ] 미확정으로 8장 완주 → `Ending_Community`(없으면 대체 문구) → 엔딩 화면 "달빛 모퉁이".
- [ ] 단독 노멀/굿/트루 각각: `<Char>_End_*` 재생(트루는 내부 jump 포함) → 엔딩 화면에 해당 이름.
- [ ] 공동 2인/3인(동의 완료·기준 충족): `Ending_Harem2|3` → 구성원 순서 에필로그 → "두/세 사람과 함께하는 엔딩". 동의 누락이나 기준 미달이면 커뮤니티 엔딩.
- [ ] 엔딩 화면: 제목·부제·최종 골드/레벨/평판·플레이 시간·본 CG·도달 엔딩 수, "타이틀로" 버튼.
- [ ] `Ch8_Outro` 가 있으면 엔딩 장면들 맨 앞에 한 번만 재생.

**프로필/타이틀**
- [ ] 첫 실행: "도달한 엔딩 0 / 21". 엔딩 후 타이틀: 1 이상, 갤러리 버튼 → 목록에 `[O]`.
- [ ] 앱 재시작 후 유지(`persistentDataPath/profile.json`). 파일을 깨뜨려도(아무 글자나 입력) 오류 없이 0 으로 시작하고 다음 엔딩 때 복구 저장.
- [ ] 선택지: 조건 미충족 선택지가 회색 + "(조건 미충족)".

## 5. 검증 결과(이 환경에서 실제 실행)

| 항목 | 결과 |
|---|---|
| `sh tools/csharp_check/flow_check/run.sh`(신규) | 183 checks, 0 failures — 챕터 흐름(1..8 전체 시뮬레이션), 루트 선택 모델(적격·거절 문구·최대 3명·확정·새로고침), 확정 후 장면 순서, 자동 안내 규칙, 동의 재시도, 복구 표, 엔딩 장면 계획(가짜 노드 집합), 요약, 프로필(왕복·깨진 파일 9종·깊은 중첩·필드별 오류·저장 실패·실제 파일), 해금, 갤러리, MiniJson, 선택지 라벨, `route_choice` 커맨드(스크립트 경유 포함) |
| NUnit `Tests/EditMode/StageThreeTests.cs` | 9개 테스트(위 순수 로직의 일부) — **Unity 에서 실행은 못 함**, 컴파일만 확인 |
| `tools/csharp_check/run.sh`, `romance_check`, `dialogue_check`(302), `ending_check`(140) | 전부 통과 |
| `ui_logic_check` | 169 checks, 0 failures (공통 CG 지원 포함) |
| `scenario_check` | **실패(내 변경 아님)** — 다른 에이전트가 `Dialogue/common` 을 작성 중이라 `#line:` id 형식·MISSING_LINE_ID·UNREACHABLE(Ch*_Intro/Outro 가 entries.txt 에 없음) 위반. 수정하지 않음 |
| `WORK=/tmp/unitycheck_repro sh tools/unity_compile_check/run.sh` | `UNITY COMPILE CHECK: ALL OK` (런타임 코드 + 테스트를 실제 UnityEngine/uGUI/TMP/InputSystem 정의로 컴파일) |

## 5-1. 호스트 재생 타이밍 규칙 (작가 보고에서 수집)

| 규칙 | 이유 |
|---|---|
| `CE02_Tasting` 은 요한 E1(첫 리뷰) 이후(약 6일차 이후)에 재생 | 요한의 3점 리뷰를 공개 사실로 언급 |
| 5장 권장 순서: `Ch5_Show` → `Johan_E3` → `Nabi_E3` → `Ch5_Show_After` → (한 시간 뒤) `Howl_E3` | 공연 사건(기타 줄·귀 이야기)의 시간 순서와 일치. `Ch5_Show_After` 는 `nabi_e3_*` 플래그가 없을 때만 줄 끊김을 서술하므로 `Nabi_E3` 를 먼저 재생해야 중복되지 않는다 |
| `Ch2_Outro` 는 `Nabi_E1` 전후 어느 쪽이든 가능 | 3줄짜리라 순서 무관 |
| 개인 이벤트 노드의 `<<end_event>>` 는 멱등 | 밤 메뉴가 이벤트를 먼저 완료 처리하고, 스크립트 쪽 호출은 무시된다 |
| `Nabi_E2` 는 4장 이후 재생이 자연스러움 | `events.csv` 최소 챕터는 3이지만 대사는 4장 서술 |

## 6. 알려진 위험 (정직하게)

1. **화면은 한 번도 실행하지 못했다.** 컴파일 가능성만 보증된다. `RouteSelectScreen`(1560×940 패널, 2열), `EndScreen`, 타이틀 갤러리, 밤 메뉴의 인라인 항목(버튼 높이 54·간격 8)은 1920×1080 기준 계산값이라 넘치거나 겹칠 수 있다.
2. **화면 전환 순서**: 루트 선택·엔딩은 `ScreenManager.Show` 를 VN 장면 종료 콜백(`VnStage.Tick` 안)에서 호출한다. 기존 게임 복귀와 같은 경로지만, 장면 사이를 어둠으로 잇는 `PlayScenes` 연쇄(루트 확정 후 동의 장면, 엔딩 다중 노드)는 실제로 보지 못했다. 장면 사이에서 배경이 한 번 깜빡이거나 대사창이 잠깐 비는지 확인 필요.
3. **불러오기와 마무리 장면**: 챕터 첫 아침에 자동 저장이 마무리 재생 *전에* 일어난다. 그래서 그 저장을 불러오면 못 본 Outro/Intro 를 상태로 다시 판단해 재생한다(위 체크리스트). 방문 기록은 `SaveData.VisitedNodes`(기존 필드)에 의존하므로, 이 기능 이전의 저장은 Outro 를 한 번 늦게 볼 수 있다.
4. **엔딩 직후 저장 없음**: 엔딩 후 이어하기는 마지막 아침 자동 저장(8챕터 마지막 날 아침)으로 간다. 엔딩 도달은 프로필에 남는다.
5. **CG 추적**: 이번 실행의 본 CG 수는 저장·불러오기를 넘어 이어지지 않는다(`AppContext.RunCgs`). 누적은 프로필의 `SeenCgs`(ID 목록뿐, 이미지 갤러리 없음).
6. **프로필 깨짐**: 깨진 `profile.json` 은 빈 프로필로 시작하고 다음 저장이 덮어쓴다(백업 안 남김). 해금 서비스는 규칙(`extra_epilogues`)만 있고 실제 콘텐츠 팩은 없다.
7. **`sys_route_declined`, `sys_talk_*`** 같은 `sys_` 플래그는 호스트 전용이다. 작가는 읽거나 쓰지 않는다.
8. 새 글리프는 쓰지 않았지만(`★` 는 기존 사용) 폰트에 따라 네모로 보일 수 있다.
9. **동의 재시도는 행동을 소모하지 않고 횟수 제한이 없다**(8장 끝까지). 의도와 다르면 `ConsentRetry`/`RecoveryRule.Once` 에서 조정.

### 다른 모듈에 대한 요청(수정하지 않음)
- **Dialogue(작가)**: (공통 CG id 는 이제 호스트가 지원한다, §2.)  `Ch<N>_Intro/Outro`, `Harem_*`, `*_Harem_*`, `Ending_*`, `Howl_E4_Retry` 는 호스트가 직접 시작하므로 `entries.txt` 에 넣어야 UNREACHABLE 경고가 사라진다.
- **scenario_check 하네스**: `route_choice` 커맨드를 호스트 모의에 등록해야 할 수 있다(호스트는 이제 알려진 커맨드로 처리).
- **Romance**: 요청 없음.
