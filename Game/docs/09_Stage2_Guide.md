# 2단계 가이드 — 주인공 생성 · VN 무대 · 대화/연애 연동

> 코드: `UnityProject/Assets/_Project/`. 이 문서는 1단계(`05_Stage1_Guide.md`) 이후 **새로 생긴 것만** 다룬다.
> Unity 가 없는 환경에서 작성되어 **Unity 의존 코드는 실행하지 못했다**(§6 참고).

## 1. 바뀐 것

| 영역 | 내용 |
|---|---|
| 주인공 생성 | 타이틀 → 난이도 → **주인공 만들기**(이름 12자·성별 4종·종 7종(임시)·외형 6색) + NPC 호칭 미리보기 → 새 게임. 규칙은 순수 `ProtagonistProfile`/`ProfileValidator`(Data). `GameState.PlayerSpecies`/`PlayerAppearance` 추가 |
| VN 무대 | `VnStage`(배경·스탠딩 2명·CG·페이드·대사창·선택지) + `VnScreen`(진행/자동/스킵). 타자기 속도는 설정값 |
| 대화 연동 | 순수 `MorpheHouse.Game` 어셈블리(네임스페이스 `MorpheHouse.GameFlow`): `DialogueSession`, `DialogueCommandDispatcher`, `NightRomanceActions`, `OverlayVariableStore`, `DialogueProjectLoader`, `ProfilePreview`, `SettingsService` 등. Unity 쪽은 `DialogueDirector`(Resources 로드·VN 재생) |
| 밤 메뉴 | 대화/선물/데이트/개인 이벤트(★)/휴식/연구/밤 마무리 + 오른쪽 관계 패널(호감·신뢰 막대) + 7장 이후 루트 확정 안내 |
| 1장 인트로 | `Ch1_Intro` 노드를 VN 으로 재생 후 아침 화면(노드가 없으면 기존 정보 카드). 2장 이후는 정보 카드 |
| 저장 | `SaveData` 에 `PlayerSpecies`, `PlayerAppearance`, `VisitedNodes` 추가(버전 그대로, 이전 저장은 기본값으로 로드). 루트 확정·플래그는 기존 필드 |
| 설정 | `SettingsService`(텍스트 속도·마스터 볼륨·언어) → `settings.json`(`persistentDataPath`). 타이틀의 설정 팝업(슬라이더) |
| 버그 수정 | `GameBootstrap`: 전체 화면 레이어를 세이프 에어리어보다 **먼저** 생성(이전엔 화면별 배경이 UI 위에 그려질 수 있는 순서였음) |

## 2. 새로 필요한 Unity 클릭

1. 폴더를 복사/갱신한 뒤 컴파일 대기(새 asmdef `MorpheHouse.Game` 포함). 빨간 에러가 있으면 전문을 알려 주세요.
2. 메뉴 **MorpheHouse → Dialogue → Sync Scripts** — `Assets/_Project/Dialogue/**/*.yarn` 을 `Resources/Dialogue/` 에 평탄화한 `.txt`(예 `routes__howl__howl_E1.txt`)로 복사하고 `_index.txt` 를 다시 만든 뒤, 전체를 파싱해 오류·린트를 Console 에 보여 준다. **시나리오를 고칠 때마다 다시 실행.**
3. (수동으로 `.txt` 를 넣었다면) **MorpheHouse → Dialogue → Rebuild Index**.
4. ▶ Play. 기존 Boot 씬 그대로 사용(씬 변경 없음).

## 3. 조작

| 입력 | 동작 |
|---|---|
| Space / Enter / 화면 클릭·터치 | 타자 중이면 문장 즉시 완성, 완성돼 있으면 다음 |
| Ctrl 누름 | 스킵(선택지에서 멈춤) |
| 자동 / 스킵 버튼 | 토글(자동: 문장 완성 후 1.2초) |
| 선택지 | 클릭. 조건 미충족은 회색 + "(선택 불가)" |

마우스·터치는 `VnStage` 의 전체 화면 투명 버튼을 통해 새 Input System UI 모듈로 들어온다(버튼 클릭이 동시에 "진행"으로 처리되지 않게 하기 위해). 키보드만 `Keyboard.current` 를 `Update` 에서 읽는다. 레거시 `Input` 은 쓰지 않는다.

## 4. 스크립트 ↔ 호스트 규약

- **노드 이름**(`<Char>` = 첫 글자 대문자 id): 대화 `Howl_Talk_01..40`(존재하는 노드를 호감 구간 3등분 → 미방문 우선 → 소진 시 횟수로 회전), 선물 `Howl_Gift_Like|Neutral|Dislike`(`$gift_name`, `$gift_pref` 설정), 데이트 `Howl_Date_<PascalSpot>`(`fish_market`→`FishMarket`, 원본 id 도 시도) 없으면 `Howl_Date_Generic`(`$spot_name`, `$spot_id`), 이벤트 `Howl_E<step>`(`$event_step`), 인트로 `Ch1_Intro`.
- 밤 행동은 **서비스가 먼저 실행**(행동 1회·피로·골드·수치 반영)된 뒤 노드를 재생한다. 노드가 없으면 짧은 대체 대사 한 줄(게임이 막히지 않음).
- `end_event`: 메뉴가 이미 완료 처리하므로(EventsDone ≥ step) 노드의 `<<end_event>>` 는 **무시(멱등)**. 노드만 단독 실행했을 때만 실제 완료한다.
- `aff`/`trust` → `RomanceService.AdjustAffection`(후반 둔화·챕터 캡 적용, 동결 인물 변화 없음). `flag` → 플래그(값 선택). `route_lock` → `TryLockRoute`.
- `bg <장소> <시간>`: `cafe→Cafe_Interior`, `harbor→Street`, 스팟 id(`lighthouse`, `fish_market`, `night_market`, `dojo`, `live_club`, `greenhouse`, `book_street`, `beach`, `rooftop`)→ArtKeys 이름, 시간 `dawn|morning|day→Day`, `dusk→Evening`, `night→Night`(대소문자 무관). 모르는 장소는 경고 후 `Cafe_Interior`, 모르는 시간은 `Day`.
- `show_standing <char> <expr> [outfit]`(소문자 가능), `hide_standing <char>`(선택), `show_cg HOWL_E1`/`hide_cg`. 스탠딩은 최대 2명(왼쪽 먼저, 꽉 차면 가장 오래된 칸 교체), 장면 시작·종료 시 비움, 말하는 쪽이 아닌 인물은 어둡게.
- `$gift_name` 같은 문자열은 `OverlayVariableStore`(임시 층)에만 저장된다. 스크립트가 `<<set $x to "문자열">>` 해도 저장 파일에 들어가지 않는다.
- `call_pref_name` 플래그(Ch1_Intro 설정)가 있으면 호스트의 `calls_player()` 가 호칭 표 대신 **이름만** 돌려준다(`DialogueSession` 에서 처리). `call_pref_honorific`/`call_pref_later` 는 아직 반영하지 않음(요청 사항, §7).

## 5. 수동 QA 체크리스트

- [ ] 타이틀에 **설정** 버튼. 텍스트 속도·볼륨 슬라이더가 움직이고 닫을 때 저장, 재실행 후 유지. 언어 버튼 ko/en.
- [ ] 새 게임 → 난이도 → **주인공 만들기**: 이름 입력(한글 IME), 13자 이상 입력 불가, 빈 이름이면 시작 버튼 비활성, `<` 입력 시 경고.
- [ ] 성별/종/외형 선택 표시, 오른쪽 호칭 미리보기가 성별·이름에 따라 즉시 바뀐다(논바이너리는 형/오빠 등 없음).
- [ ] 시작 → **Ch1_Intro** 가 VN 으로 재생(타자기, 클릭/Space 로 완성→다음, 선택지, 자동/스킵, 배경 페이드, 스탠딩 표정, CG) → 아침 화면. (Sync Scripts 를 안 했다면 정보 카드가 나온다.)
- [ ] 영업 → 밤: 오른쪽 관계 패널에 6명의 막대. 대화/선물/데이트/이벤트/휴식/연구/마무리 버튼.
- [ ] 대화 → 노드 재생(없으면 대체 대사) → 복귀 후 토스트에 호감/신뢰 변화, 행동 횟수 감소.
- [ ] 선물: 5개씩 페이지, 쿨다운/골드 부족은 회색. `Howl_Gift_*` 의 `{$gift_name}` 이 선물 이름으로 표시.
- [ ] 데이트: 1장에는 스팟 3곳만 활성, 나머지 "(N장 해금)" 회색.
- [ ] 개인 이벤트: 호감 10 이상이면 ★(밤 메뉴·인물 목록·관계 패널), 재생 후 EventsDone 증가, 이벤트 CG 표시.
- [ ] 7장 도달 + 호감 60/신뢰 20 이상: 밤 시작 시 **루트 확정** 안내(하루 1회) → 확인 단계 → 확정 후 다른 인물 "(고정)" 표시, 접촉해도 수치 변화 없음 문구.
- [ ] 아침 저장 → 타이틀 → 이어하기: 이름·성별·종·호감·플래그·루트 확정·방문 노드(대화 `visited()`)가 복원.
- [ ] Console 에 `[Dialogue]` 경고(없는 노드·잘못된 커맨드 인자)가 반복되지 않는다.

## 6. 검증 결과(이 환경에서 실제 실행)

| 하네스 | 결과 |
|---|---|
| `tools/csharp_check/ui_logic_check/run.sh`(신규) | 162 checks, 0 failures — 프로필 검증, 저장 확장·구버전 호환, 에셋 이름/`bg` 매핑, 스탠딩·타자기, 임시 변수, 커맨드 디스패치(가짜 싱크), 밤 행동(노드 선택·대체 대사·쿨다운·골드·잠긴 스팟·이벤트·루트 확정), 세션 폴백, 로더/인덱스, 설정 왕복(메모리·파일), 호칭 미리보기, **실제 시나리오 17노드 × 4성별 × 3선택 경로 전수 실행** |
| 기존 `run.sh`(경영·저장), `romance_check`, `dialogue_check`(302), `scenario_check`(21) | 전부 통과 |
| Unity 의존 파일 | `mcs -target:library` 문법 검사에서 CS1xxx 없음. 추가로 `UnityEngine`/`TMPro`/`UI` 의 **최소 스텁**을 임시로 만들어 UI·Boot 전체를 의미 검사(타입·시그니처 오류 없음). 스텁은 내 기억 기준이라 실제 API 와 다를 수 있음 |

## 7. 알려진 위험 (정직하게)

1. **Unity 에서 한 번도 컴파일/실행하지 못했다.** TMP_InputField·Slider 를 코드로 조립한 부분(`UiKit.MakeInput`/`MakeSlider`), `Keyboard.current` 프로퍼티명, `TMP_InputField.SetTextWithoutNotify`, `Slider.SetValueWithoutNotify` 는 Unity 6 시그니처가 다르면 컴파일 에러가 날 수 있다(에러 전문을 알려 주세요).
2. **레이아웃**: 밤 화면(콘텐츠 900×840 + 관계 패널 520)·주인공 생성(1060+720)은 1920×1080 기준 계산값이며 화면에서 확인하지 못했다. 선택지/목록이 넘치면 높이·페이지 크기(`ShowPaged` 의 5~6)를 조정.
3. **입력**: 화면 클릭/터치는 투명 버튼 + EventSystem 경로다. EventSystem/InputSystemUIInputModule 이 없으면 클릭 진행이 안 된다(`GameBootstrap` 이 생성). 클릭 후 선택 해제를 코드로 하지만, 버튼에 포커스가 남아 Space 가 중복 처리되는 경우가 있으면 알려 주세요.
4. **글리프**: `★`, `▼` 가 선택한 한글 폰트에 없으면 네모로 보인다(폰트 이슈, 로직과 무관).
5. **선물 가격**: `RomanceService.Gift` 는 `gifts.csv` 의 `price_tier` 를 쓰지 않고 `100+60×챕터` 를 받는다. UI 는 실제 비용과 등급을 따로 표시한다.
6. **talk 노드 선택 규칙**(구간 3등분·미방문 우선·회전)은 내가 정한 관례다. 시나리오 작가의 의도와 다르면 `NightRomanceActions.PickTalkNode` 한 곳만 바꾸면 된다.
7. 타자기는 리치 텍스트 태그 없는 평문을 전제로 글자 수를 센다(스크립트에 `<b>` 등을 쓰면 표시가 어긋남).
8. `hide_standing` 은 카탈로그에 없는 확장이다. 스탠딩 페이드 인/아웃·BGM·효과음은 아직 없다.

### 다른 모듈에 대한 요청(수정하지 않음)
- Romance: `Gift` 비용에 `price_tier` 반영 여부 결정. 
- Dialogue/PronounResolver: `call_pref_honorific`(이름 + 씨)·`call_pref_later` 를 `calls_player` 에서 직접 처리할지(현재는 `call_pref_name` 만 호스트에서 가로챔).
