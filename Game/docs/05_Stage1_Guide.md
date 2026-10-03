# 1단계 가이드 — 프로젝트 세팅 · 플레이스홀더 프로토타입

> 목표: 일러스트 없이 **타이틀 → 하루 사이클(아침·영업·밤·정산) → 8챕터 완주**까지 돌아가는 프로토타입.
> 코드 위치: `UnityProject/Assets/_Project/` (그대로 새 Unity 프로젝트의 `Assets/` 안에 복사)

---

## 1. 이번 단계에서 바뀐 설계 (기술 설계서 대비)

| 항목 | 설계서(0단계) | 1단계 구현 | 이유 |
|---|---|---|---|
| 씬 | Boot / Title / Game 분리 | **Boot 단일 씬**, 화면은 코드로 전환(Title/Game 스크린) | 프리팹·씬 연결 클릭을 없애 에디터 작업 최소화. 5단계에서 분리 검토 |
| UI 생성 | 프리팹 | **코드 조립(UiKit)** | 같은 이유. 디자인 확정 후 프리팹화 |
| 문자열 | Unity Localization | `UiText` 한 곳에 모음 | 5단계에서 String Table 이전 |
| 데이터 로드 | Addressables | `Resources/Balance` + `ITextSource` | 나중에 구현체만 교체 |
| 한글 폰트 | 번들 폰트 | OS 한글 폰트 동적 생성 + `Resources/Fonts/MainFont` 우선 | 출시 전 번들 폰트로 고정 필요 |
| 저장 | 아무 때나 | **아침 단계에서만** 저장(자동 저장은 매일 아침) | 영업 도중 상태 복원 복잡도 제거 |

## 2. 모듈 구성 (asmdef)

| 어셈블리 | 파일 | 비고 |
|---|---|---|
| `MorpheHouse.Core` | `EventBus`, `GameEvents`, `GameEnums` | Unity 비의존 |
| `MorpheHouse.Data` | `CsvTable`, `BalanceTable`, `GameState`, `ITextSource` | Unity 비의존, CSV → 메모리 |
| `MorpheHouse.Tycoon` | `BusinessSimulator`, `DayCycle`, `AutoPlayer`, `IRandom` | Unity 비의존, **Python 시뮬레이터와 같은 수식** |
| `MorpheHouse.Save` | `SaveData`, `SaveMapper`, `SaveValidator`, `SaveIntegrity`, `SaveService`, `JsonUtilitySerializer` | HMAC 서명·원자적 쓰기·백업 복구·범위 검증 |
| `MorpheHouse.ArtPipeline` | `ArtManifest`(파일명 규칙 단일 출처), `PlaceholderPainter`, `ArtLoader` | 같은 파일명으로 교체하면 자동 반영 |
| `MorpheHouse.UI` | `UiKit`, `FontProvider`, `SafeAreaFitter`, `StandingView`, `TitleScreen`, `GameScreen`, `AppContext` … | uGUI 코드 조립 |
| `MorpheHouse.Boot` | `GameBootstrap`, `ResourcesTextSource` | 진입점 |
| `MorpheHouse.Editor` | `SceneSetup`, `ProjectSetup`, `PlaceholderGenerator`, `ArtImportPostprocessor`, `BalanceTools` | 메뉴 자동화 |
| `MorpheHouse.Tests.EditMode` | `StageOneTests` | NUnit 6개 |

## 3. Unity 에디터 작업 순서 (클릭)

1. **Unity Hub** → *Installs* → *Install Editor* → **Unity 6 LTS** 선택. 모듈에서 *Windows Build Support*, (나중에) *Android Build Support* 체크 → Install.
2. Hub → *Projects* → *New project* → 템플릿 **Universal 2D**(URP 2D) → 이름 `MoonCornerHouse` → *Create project*.
3. 에디터가 열리면 상단 **Window → Package Manager** → 좌측 *Unity Registry* → **Input System** 검색 → *Install*. (이미 설치돼 있으면 생략. 설치 후 "에디터 재시작" 팝업이 뜨면 *Yes*)
4. 파일 탐색기에서 이 저장소의 `UnityProject/Assets/_Project` 폴더를, 새 프로젝트의 `Assets` 폴더 안으로 **복사**.
5. Unity 창으로 돌아가 컴파일이 끝나길 기다린다(하단 우측 스피너). **Console 에 빨간 에러가 있으면 전문을 복사해 알려 주세요.**
6. (TMP 팝업이 뜨면) **Import TMP Essentials** 클릭. 안 뜨면 **Window → TextMeshPro → Import TMP Essential Resources**.
7. 메뉴 **MorpheHouse → Setup → Apply Recommended Project Settings** → 확인 팝업 → **에디터 재시작**.
8. 메뉴 **MorpheHouse → Setup → Create Boot Scene** (Boot 씬 생성 + Build Settings 등록).
9. *(선택)* 메뉴 **MorpheHouse → Art → Generate Placeholders** — 238장의 PNG 생성. 생략해도 런타임 폴백으로 동일하게 보입니다.
10. 상단 **▶ Play**.

### 확인 체크리스트
- [ ] 타이틀에 한글이 정상 표시된다(네모로 깨지면 §5 의 폰트 참고).
- [ ] 새 게임 → 난이도 선택 → *제1장 상속* 소개 → 아침 화면.
- [ ] 영업 시작 → 진행 막대 → 영업 결과 → 밤(하울 플레이스홀더 스탠딩, 표정 전환) → 정산 → 다음 날.
- [ ] 아침 화면 *저장* → 타이틀로 → *이어하기* 로 복원.
- [ ] 타이틀 *자동 플레이 검증* 의 숫자가 §4 의 곡선과 비슷하다.
- [ ] **Window → General → Test Runner → EditMode → Run All** 전부 초록.
- [ ] 메뉴 **MorpheHouse → Balance → Run AutoPlay Report** 출력 확인.

## 4. 검증 결과 (이 환경에서 실제 실행한 것)

Unity 가 없어 **Unity 의존 코드(UI·Boot·Editor·ArtLoader)는 실행하지 못했습니다**(문법 검사만 통과). Unity 비의존 계층은 Mono C# 컴파일러로 컴파일해 직접 실행했습니다(`sh tools/csharp_check/run.sh`).

| 검증 | 결과 |
|---|---|
| CSV 파서(BOM·따옴표·CRLF), EventBus 구독/해지 | PASS |
| 난이도 3종 × 200회 자동 플레이: 64일 완주, 골드 양수 | PASS |
| 하루 사이클 단계 순서 강제 | PASS |
| 저장 왕복, **변조 탐지**, **손상 시 백업 복구**, 범위 밖 값 저장 거부, 아침 외 저장 거부 | PASS |
| 플레이스홀더 렌더링(표정 6종·배경·CG·아이콘, 총 238장 명세) | 실행해 이미지 확인 |

경영만 하는 자동 플레이(연애 없음)의 챕터별 일 평균 매출:

| 난이도 | 1장 | 2장 | 3장 | 4장 | 5장 | 6장 | 7장 | 8장 | 최종 레벨 |
|---|---|---|---|---|---|---|---|---|---|
| 스토리 | 1,237 | 1,886 | 3,335 | 4,475 | 5,836 | 7,429 | 10,205 | 11,388 | 10.0 |
| 노멀 | 1,031 | 1,572 | 2,779 | 3,729 | 4,864 | 6,191 | 8,272 | 9,490 | 10.0 |
| 하드 | 876 | 1,336 | 2,157 | 2,952 | 3,859 | 4,955 | 6,153 | 7,180 | 9.6 |

노멀 8장이 목표(12,000)보다 낮은 것은 **의도된 차이**입니다. 직원 호감도 보너스(+최대 10%/명)가 없기 때문이며, 연애 시스템이 붙는 2단계 이후 Python 시뮬레이터 수준(약 11,400)에 접근합니다.

### 이 단계에서 발견해 고친 밸런스 결함
평판 변화 공식이 직원 호감도 보너스를 전제로 하고 있어, **경영만 하면 평판이 3까지 무너졌습니다**(Python 시뮬레이터는 호감도 보너스 때문에 가려져 있었음). `rep_sat_center`(0.82) 파라미터로 중립점을 분리해 양쪽 구현에 반영했고, 이제 경영만 해도 평판은 약 20 근처에서 안정됩니다.

## 5. 알려진 위험 (정직하게)

1. **Unity API 버전 차이**: Unity 6 의 TMP/Input System 시그니처가 제 지식과 다르면 컴파일 에러가 날 수 있습니다. 에러 로그를 주시면 바로 수정합니다.
2. **한글 폰트**: OS 폰트(맑은 고딕 등)를 동적으로 만듭니다. 깨지면 한글 TTF(예: Noto Sans KR, SIL OFL)를 임포트 → *Window → TextMeshPro → Font Asset Creator* 로 *Dynamic* 에셋을 만들어 `Assets/_Project/Resources/Fonts/MainFont.asset` 으로 저장하면 자동 사용됩니다. 안드로이드 출시 전에는 이 방식으로 고정해야 합니다.
3. **Input System**: *Active Input Handling* 이 *Old* 이면 클릭이 먹지 않습니다(7번 단계가 해결, 재시작 필요).
4. **JsonUtility 변조 테스트**: `"Gold":1234` 문자열 치환에 의존합니다(JsonUtility 출력 형식 변경 시 테스트 문자열 조정).

## 6. 이번 단계에서 필요한 일러스트

1단계는 **일러스트 없이 동작**합니다(전부 플레이스홀더). 화면에 쓰이는 파일명만 정리합니다.

| 용도 | 파일명(Resources/Art/…) | 현재 | 실제 제작 시점 |
|---|---|---|---|
| 타이틀 배경 | `BG/BG_Street_Night.png` | 플레이스홀더 | 5단계 |
| 가게 배경 | `BG/BG_Cafe_Interior_{Day,Evening,Night}.png` | 플레이스홀더 | 3단계 |
| 하울 스탠딩 | `Standing/Howl/Std_Howl_Casual_Body.png`, `…_Face_{Neutral,Smile}.png` | 플레이스홀더 | 3단계(프롬프트 키트 포함) |

일러스트 도입 방법: **같은 파일명의 PNG 로 덮어쓰기** → `ArtImportPostprocessor` 가 임포트 규칙(Sprite/PPU 100/압축/피벗)을 자동 적용 → 코드 수정 없음.

## 7. 다음 단계(2단계: 대화·연애 시스템)에 필요한 결정

1. **주인공 생성 화면**(이름·성별·종 선택)을 2단계에 포함할까요? (추천: 포함 — 대사 치환 테스트에 필요)
2. **Yarn Spinner 설치**: 2단계 시작 시 Package Manager 에서 설치 후 버전을 알려 주시면 그 버전에 맞춰 연동 코드를 작성합니다. 진행 가능한가요?
3. **이번 1단계 컴파일 결과**(에러 유무)와 **GPU 보유 여부**(3단계 일러스트 도구 확정용)를 알려 주세요.
