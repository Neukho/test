# 5단계 가이드 — 오디오 · 설정/언어 · 모바일 · 의상 정책 · 성능

> 코드: `UnityProject/Assets/_Project/`. `12_Stage3_Guide.md` 이후 **새로 생긴 것만** 다룬다.
> Unity 가 없는 환경에서 작성했다. **순수 로직은 하네스로 실행해 확인했고, Unity 의존 코드(오디오 재생·입력·레이아웃·화면 조립)는 실제 UnityEngine/uGUI/TMP/InputSystem 정의로 컴파일만 확인했다.** 실행하지 못한 부분은 §5 에 모아 두었다.

## 1. 무엇이 생겼나

| 영역 | 동작 | 코드 |
|---|---|---|
| 오디오 계산 | 마스터/BGM/효과음/목소리 슬라이더(0..1) → **진폭 = 값²**(0.5 → -12 dB), 음소거(전체/채널), 목소리 구간 BGM 덕킹(-9 dB, 0.15초 감쇠·0.6초 복귀), BGM 등전력 교차 페이드, 같은 소리 간격 제한 | `Game/AudioMixerModel.cs`(순수) |
| 오디오 카탈로그 | `Resources/Audio/audio_manifest.csv`(id,kind,path,loop,default_volume,notes) → id→경로. 잘못된 행은 오류로 기록하고 건너뜀 | `Game/AudioCatalog.cs`(순수), `AudioCues`(맥락→id) |
| 오디오 서비스 | BGM 2채널 교차 페이드, 효과음 풀 8개, 설정 볼륨 반영. **클립 파일이 없으면 경고 한 번 후 무음** — 지금은 소리 파일이 하나도 없어도 정상 동작 | `UI/AudioService.cs`(MonoBehaviour), `UI/LocalizedLabel.cs` 의 `UiAudio` |
| 오디오 훅 | 버튼 클릭(뒤로/확정/일반 3종), 타자기 틱(0.07초 간격 제한, 스킵 중 제외), 영업 결과 코인음, 가게 확장 레벨업음, 챕터 시작음, 엔딩 BGM(종류별), 선물·데이트·저장·오류음, CG 표시음, 문 종소리(영업 시작), 화면별 BGM(타이틀/낮/영업/저녁/밤/루트 선택) | `UiKit.MakeButton`, `VnStage`, `GameScreen`, `AppContext.OnScreenChanged` |
| 대사 커맨드(선택) | `<<bgm id>>`, `<<bgm_stop>>`, `<<se id>>`. 알 수 없는 id·종류 불일치·인자 없음 = **경고만**(오류 아님). 현재 시나리오는 사용하지 않음 | `DialogueCommandDispatcher`, `IAudioCommandSink` |
| 요청서 | 소리 26개(BGM 13 + SE 13)의 분위기·길이·루프·쓰이는 곳. 생성기가 매니페스트와 문서를 함께 만든다 | `docs/14_Audio_Request_List.md`, `tools/audio_request_list.py` |
| 설정 화면 | 마스터/BGM/효과음/텍스트 속도/자동 진행 속도 슬라이더, 언어(한국어/English), UI 크기(90/100/110 %), 전체 화면(데스크톱). 닫을 때·앱이 백그라운드로 갈 때 저장 | `UI/SettingsPanel.cs`, `Game/Settings.cs`(필드 추가, 옛 파일 호환) |
| 언어 | `UiText` 속성 이름 = 문자열 키, 코드 값 = 한국어 기본값. 표는 `Resources/Localization/ui_ko.csv`, `ui_en.csv`(101키, 영어 전부 번역). 조회 순서: 현재 언어 → 한국어 → 코드 기본값 → 키. 언어를 바꾸면 **표의 글자와 같은 모든 라벨이 즉시 바뀌고**(`LocalizedLabel`), 대사 호칭 언어(`PronounResolver`)는 **다음 장면 시작 때** 바뀐다 | `Game/UiStrings.cs`, `UI/UiText.cs`, `UI/LocalizedLabel.cs`, `DialogueDirector.EnsureLocale` |
| 일시정지 메뉴 | Esc / Android 뒤로 키 / 게임패드 B / 화면의 "메뉴" 버튼(HUD·VN 대사창). 계속하기 · 설정 · 저장(**아침에만**, 아니면 안내 문구) · 타이틀로(확인 상자). 열려 있는 동안 대사·타자기·연출·플레이 시간 정지 | `UI/PauseMenu.cs`, `Game/BackKeyRules.cs`(순수 판정), `AppContext.HandleBack` |
| 뒤로가기 규칙 | 일시정지 중: 확인 상자 → 설정 → 메뉴 순으로 한 단계씩 닫기. 게임 화면/VN: 일시정지 열기. 타이틀 하위 패널 → 메뉴, 주인공 생성 → 타이틀, 루트 선택 → 보류. **타이틀 최상위·엔딩 화면: 아무 일 없음**(실수로 종료되지 않게) | `ScreenBase.HandleBack/CanHandleBack` |
| 모바일 입력 | VN 화면: 탭 = 다음 줄, **터치 길게 누르기(0.5초) = 누르는 동안 스킵**, 손을 떼면 멈춤. 마우스는 길게 눌러도 그냥 클릭. 이동 한계 10dp 넘으면 탭 취소(스와이프). 가로 고정·화면 꺼짐 방지 | `Game/TouchGesture.cs`(순수), `UI/VnTouchSurface.cs`, `GameBootstrap.ConfigureMobile` |
| 최소 터치 | `UiKit.MakeButton` 이 가로·세로를 **48 기준 해상도 px 이상**으로 올린다. 슬라이더는 투명한 전체 크기 터치 영역 + 핸들 48 | `LayoutRules.ClampTouch` |
| 배치 검사기 | 코드의 패널 수치 표(25개 패널·12개 스택·22개 버튼)로 16:9(1920×1080)와 20:9(2400×1080, 안전 영역 절단)에서 **들어가는가·겹치는가·쌓은 자식이 패널에 들어가는가** 계산. 표가 코드와 어긋나면 하네스가 잡는다 | `Game/LayoutRules.cs`(순수) |
| UI 크기 | `CanvasScaler.referenceResolution = 1920×1080 ÷ 배율`. **현재 화면에서 레이아웃에 안 들어가는 배율은 설정에서 비활성**, 이미 저장된 값이면 들어가는 가장 가까운 배율로 낮춰 적용 | `UI/DisplayService.cs` |
| 의상 정책 | 스크립트는 의상을 지정하지 않으므로 호스트가 고른다: **명시한 의상 > `_Date_` 노드=Date > 직원(howl, gomseok, haru, nabi)+카페 낮=Uniform > Casual** | `Game/OutfitPolicy.cs`(순수), `VnStage.ShowStandingAuto`, `IVnOutfitSink` |
| 아트 체크리스트 | `tools/art_checklist.py` 가 같은 규칙으로 필요한 의상을 센다(정책 반영 후 필수 바디 7→16, 얼굴 37→72, 합계 115→159). `--selftest` 로 규칙 표 검증 | `tools/art_checklist.py`, `docs/13_Art_Checklist.md`, `docs/art_checklist.csv` |
| 메모리 | 체크리스트를 읽어 장면별 최악 텍스처 메모리(PC/Android)를 블록 압축 규칙대로 계산 | `Game/MemoryBudget.cs`(순수), `docs/15_Performance_Guide.md` |

### 1.1 설계 메모
- **UI 크기는 "Match" 가 아니라 기준 해상도 나누기 배율**로 구현했다. `matchWidthOrHeight` 만 바꾸면 균일 확대가 되지 않는다(가로/세로 중 한쪽 기준으로 늘어날 뿐). 요구사항의 "via CanvasScaler" 는 `referenceResolution` 으로 충족했다.
- **20:9 에서 Match 0.5 의 캔버스 높이는 966(안전 영역 절단 후 918)** 이다. 이전 코드는 높이 940·960 패널을 쓰고 있어 20:9 폰에서 잘렸다. 검사기가 이를 잡아 다음을 고쳤다.
  - 타이틀: 제목 블록을 위로 올리고 메뉴를 위쪽 정렬(원래 20:9 에서 메뉴가 "도달한 엔딩" 줄과 겹쳤다).
  - 주인공 생성: 폼·미리보기 높이 960 → 880, 폭 1060/720 → 980/640, 성별 버튼 230 → 200(4개가 폼 폭을 넘었다).
  - 밤 메뉴: 패널 840 → 790(HUD 아래), 버튼 64 → 56(인라인 항목 있으면 54 → 48), 폭 900 → 840 + 왼쪽으로 60 이동(관계 패널과 겹침 해소). 관계 패널 840 → 790.
  - 루트 선택: 패널 940 → 880, 후보 버튼 82 → 78.
  - VN 선택지: 화면 중앙 고정(대사창과 겹침) → **대사창 바로 위 아래 정렬**.
- 20:9 에서 **110 % 는 쓸 수 없다**(캔버스 높이 835 < 가장 큰 패널 880). 16:9 에서는 90/100/110 모두 가능. 설정 화면이 이를 자동으로 반영한다.
- 일시정지는 화면이 아니라 **오버레이**라서 VN 장면·밤 메뉴 상태를 건드리지 않는다. "타이틀로" 는 VN 중이면 장면을 버린다(저장 없음, 이어하기는 마지막 아침 자동 저장).

## 2. Unity 에서 새로 필요한 클릭

**필수 클릭 없음.** 씬 변경 없음. 폴더를 갱신하고 컴파일을 기다린 뒤 Play 하면 된다(새 `Resources/Localization`, `Resources/Audio` 폴더는 Unity 가 .meta 를 만든다).

선택 사항:
1. **소리 넣기**: `docs/14_Audio_Request_List.md` 의 경로·이름대로 `Resources/Audio/BGM/bgm_<id>.ogg`, `Resources/Audio/SE/se_<id>.ogg` 를 넣는다. `AudioImportPostprocessor` 가 BGM=Streaming, SE=Decompress On Load 로 자동 설정한다. 매니페스트를 바꾸려면 `tools/audio_request_list.py` 의 표를 고치고 `python3 tools/audio_request_list.py`.
2. **영어 문구 고치기**: `Resources/Localization/ui_en.csv` 를 열어 `text` 열만 수정(키는 건드리지 않는다). 새 문구는 `UiText` 에 속성을 추가하고 두 CSV 에 같은 키를 넣은 뒤 `sh tools/csharp_check/polish_check/run.sh` 로 키 누락·한국어 기본값 불일치·한글 잔존을 검사한다.
3. **Android 빌드**: Player Settings → Resolution and Presentation → Default Orientation 은 코드가 런타임에 가로 자동 회전으로 정한다(별도 설정 불필요). Input System 패키지가 활성(Active Input Handling = New 또는 Both)이어야 한다(기존과 동일).
4. 의상 아트가 늘었으므로 `python3 tools/art_checklist.py` 로 `docs/13_Art_Checklist.md` 를 다시 만든다(이미 한 번 실행해 반영됨).

## 3. 작가 / 아트 담당에게

- **작가**: 의상은 지정하지 않아도 된다(데이트 노드·카페 낮이면 알아서 Date/Uniform). 특정 의상이 꼭 필요하면 `<<show_standing howl smile uniform>>` 처럼 세 번째 인자로 명시하면 **항상 그것이 이긴다**. BGM/효과음은 선택: `<<bgm event_warm>>`, `<<bgm_stop>>`, `<<se door_bell>>`(id 목록은 `docs/14`). 없는 id 는 경고만 남는다.
- **아트**: 정책 반영 후 필수는 바디 16(인물별 Casual + 직원 Uniform + 데이트가 있는 인물 Date), 얼굴 72. 얼굴 레이어를 작게 자르는 규격은 `docs/15` §3.4 에서 제안(아트를 그리기 전에 결정하면 메모리가 크게 준다).
- **오디오**: `docs/14` 가 요청서다. 최소 세트(클릭 3종 + 낮/밤/타이틀 BGM)부터.

## 4. 수동 QA 체크리스트

**오디오 (소리 파일을 넣은 뒤)**
- [ ] 파일이 하나도 없을 때: 콘솔에 클립 없음 경고가 id 당 **한 번**만 나오고 게임은 정상 진행
- [ ] 타이틀 BGM → 새 게임 → 아침 `cafe_day` → 영업 `business` + 문 종소리 → 정산 `settlement` → 밤 `cafe_night`. VN 이 끝나고 돌아오면 그 단계의 곡으로 복귀
- [ ] 같은 곡으로 전환할 때 끊기거나 다시 시작하지 않는다. 다른 곡은 약 1초 교차 페이드
- [ ] 설정의 마스터/BGM/효과음 슬라이더가 즉시 반영, 0 이면 완전 무음, 재시작 후 유지
- [ ] 타자기 틱이 너무 빽빽하지 않고(0.07초 간격), 스킵·"즉시" 속도에서는 나지 않는다
- [ ] 영업 결과(순수익 > 0) 코인음, 가게 확장 레벨업음, 선물/데이트 성공음, 거절·저장 실패 오류음, CG 표시음
- [ ] 엔딩 종류별 BGM(공동 2·3인은 같은 곡)
- [ ] (스크립트에 임시로 넣어) `<<bgm event_warm>>`, `<<bgm_stop>>`, `<<se door_bell>>` 동작, 오타 id 는 콘솔 경고만
- [ ] 모바일에서 앱을 백그라운드로 보내면 소리가 꺼지고 돌아오면 이어진다

**설정 / 언어**
- [ ] 설정 열기: 타이틀 "설정" 과 일시정지 메뉴 "설정" 모두 같은 패널. 슬라이더 5개 + 언어 + UI 크기 + 전체 화면(모바일에서는 안 보임)
- [ ] 언어를 English 로: **타이틀·설정·일시정지·난이도·주인공 생성·정산 버튼 등 표의 문구가 즉시 영어**로. 한국어로 되돌리면 복귀. 앱을 껐다 켜도 유지
- [ ] 영어에서도 레이아웃이 넘치지 않는다(긴 영어 문장: 난이도 설명 3개, 루트 선택 문구, 일시정지 안내)
- [ ] 영어로 바꾼 뒤 **다음 장면부터** 대사 호칭("you" 등)이 영어 규칙으로 바뀐다(대사 본문은 한국어 그대로 — 영어 대본이 아직 없다)
- [ ] UI 크기 90/100/110 %: 즉시 적용, 선택한 값이 강조. 20:9 폰에서는 110 % 버튼이 비활성이고 안내 문구가 바뀐다
- [ ] 전체 화면 끔 → 창 모드(1600×900), 켬 → 전체 화면(데스크톱 빌드에서만 확인 가능, 에디터에서는 무시됨)
- [ ] `settings.json` 에 `bgmVolume`, `seVolume`, `voiceVolume`, `autoDelay`, `uiScale`, `fullscreen`, `locale` 이 저장되고, 옛 3키 파일도 읽힌다

**일시정지 / 뒤로가기**
- [ ] 게임 화면·VN 에서 Esc → 메뉴. 다시 Esc → 닫힘. 열려 있는 동안 타자기·자동 진행·영업 막대·플레이 시간이 멈춘다
- [ ] 메뉴 → 설정 → Esc → 메뉴로 돌아옴. 메뉴 → 타이틀로 → 확인 상자 → Esc → 닫힘(메뉴로)
- [ ] 저장: **아침 화면**에서 "저장하기" → 저장했습니다. 밤·영업·VN 중이면 "저장은 아침에만 할 수 있어요…" 안내 + 오류음
- [ ] 타이틀로 돌아가기(VN 중 포함) → 이어하기로 마지막 아침 자동 저장을 불러온다
- [ ] 타이틀: 난이도/갤러리/설정에서 Esc → 메뉴. 메뉴에서 Esc 는 **아무 일 없음**. 주인공 생성에서 Esc → 타이틀. 루트 선택에서 Esc → 보류(확인 상자 중이면 상자만 닫기)
- [ ] 엔딩 화면에서 Esc 는 아무 일 없음
- [ ] **Android 뒤로 키**가 같은 동작(Input System 이 Escape 로 보고하는지 확인 — §5 위험 1)
- [ ] 화면의 "메뉴" 버튼(HUD 오른쪽, VN 대사창 자동/스킵 옆)으로도 열린다

**모바일 입력**
- [ ] VN: 화면 아무 곳 탭 = 다음 줄(타자 중이면 문장 완성), 선택지·버튼 탭이 뒤로 새지 않는다
- [ ] 길게 누르기 0.5초 후 스킵 시작, 손을 떼면 멈춤, 선택지가 나오면 멈춘다(다시 길게 눌러야 재개)
- [ ] 손가락을 끌면(스와이프) 탭으로 처리되지 않는다
- [ ] 마우스(데스크톱): 천천히 클릭해도 스킵이 되지 않고 한 줄만 넘어간다
- [ ] 노치 기기: 대사창·HUD·메뉴 버튼이 안전 영역 안에 있다
- [ ] 슬라이더를 손가락으로 막대 아무 곳이나 눌러도 움직인다(투명 터치 영역)

**레이아웃 (16:9 와 20:9 둘 다)**
- [ ] 타이틀: 메뉴 버튼 6개가 제목·"도달한 엔딩" 줄과 겹치지 않는다. 난이도·갤러리·설정 패널이 화면 안
- [ ] 주인공 생성: 폼과 미리보기가 겹치지 않고 아래 버튼까지 보인다(성별 4개 버튼이 한 줄에 들어간다)
- [ ] 밤 메뉴: 버튼이 패널 아래로 넘치지 않는다(복구/동의 항목 2개 인라인 상태에서도). 관계 패널과 겹치지 않는다
- [ ] 루트 선택: 6명 목록과 동작 버튼 3개가 겹치지 않는다
- [ ] VN 선택지(4~5개)가 대사창 위에 쌓이고 대사창을 가리지 않는다
- [ ] 엔딩 화면, 일시정지 메뉴·확인 상자가 화면 안

**의상**
- [ ] 1장 인트로(낮 카페)에서 하울/곰석/하루/나비가 Uniform, 요한/범준은 Casual (아트가 없으면 플레이스홀더 색으로 구분)
- [ ] 밤 대화·선물·이벤트에서는 모두 Casual
- [ ] `*_Date_*` 노드에서는 모두 Date
- [ ] 스크립트가 `<<show_standing howl smile casual>>` 처럼 명시한 경우 정책과 무관하게 그 의상(하루 Uniform 명시 15곳 포함)

## 5. 알려진 위험 (정직하게)

실행하지 못한 것 — 컴파일만 확인했다:

1. **Android 뒤로 키 = `Keyboard.escapeKey`** 라는 가정은 Input System 문서·소스 기억에 의존한다. 기기에서 뒤로 키가 안 먹으면 `AppContext.PollBackKey` 에 `UnityEngine.InputSystem.Android` 쪽 입력을 추가해야 한다.
2. **오디오 재생 전부**: `AudioSource` 교차 페이드·풀·`PlayOneShot` 볼륨, 스트리밍 BGM 로드, 클립 없음 경로. 로직(곡선·페이드 이득·덕킹·간격)은 하네스로 검증했지만 `AudioService` 가 그 값을 올바른 소스에 넣는지는 못 봤다. 빠른 연속 `PlayBgm`(곡이 페이드 중일 때 또 변경)은 나가던 곡을 즉시 끊는다.
3. **`AudioImportPostprocessor`** 는 `UnityEditor` 코드라 컴파일 검사 범위 밖이다(API 는 `AudioImporterSampleSettings` 표준 사용).
4. **터치 제스처**: `ExtendedPointerEventData.pointerType` 으로 터치/마우스를 구분한다. 실제 기기·에뮬레이터에서 터치가 `UIPointerType.Touch` 로 오는지 확인 필요. 틀리면 모바일에서 길게 누르기가 꺼진다(탭은 동작).
5. **`DisplayService`**: `CanvasScaler.referenceResolution` 변경과 `Screen.SetResolution` 전체 화면/창 전환은 에디터에서 무시되고 빌드에서만 확인 가능. 창 크기를 1600×900 으로 고정해 둔 것이 마음에 안 들면 `DisplayService.ApplyFullscreen`.
6. **레이아웃 검사기는 코드의 숫자만 본다.** 텍스트 줄바꿈·폰트 실제 높이·`VerticalLayoutGroup` 의 실제 배치는 모른다. 특히 영어 문장이 길어져 줄이 늘면 패널을 넘칠 수 있다(밤 메뉴는 높이 여유가 크지 않다: 최악 스택 760/790). 하네스는 "패널 크기가 들어가는가" 와 "스택 합이 패널에 들어가는가(내가 센 자식 높이 기준)" 만 보증한다.
7. **언어 전환은 "표의 글자와 같은 라벨" 만 즉시 바뀐다.** `GameScreen`, `RouteSelectScreen`, `RelationPanel`, 갤러리, `VnScreen`, `DialogueDirector` 등에 **인라인 한국어 문자열이 약 85개** 남아 있다(HUD "제N장…", 아침/밤 안내 줄, 루트 선택 후보 줄, 관계 패널 등). 이 문구들은 한국어로 보인다. 챕터·인물·선물 이름은 `balance/*.csv` 의 한국어 이름이고 대사 본문도 한국어뿐이라, **영어 모드는 "UI 뼈대만 영어"** 이다. 완전 영어화에는 인라인 문자열의 `UiText` 이전 + 밸런스 표의 영어 이름 열 + 영어 대본이 필요하다.
8. (§8 에서 고침 — 모바일은 물리 48 dp) 예전 기록: **터치 목표 48dp 는 달성하지 못했다(48 "기준 해상도 px" 만 보장).** 20:9 폰(420 dpi)에서 기준 56 px 버튼은 약 **23.9 dp** 이고, 48 dp 가 되려면 기준 해상도로 약 113 px 가 필요하다. 지금 레이아웃(버튼 48~84 px)은 그 절반 이하다. 완화책: VN 은 화면 아무 곳이나 탭, 길게 누르기 스킵, 슬라이더 투명 터치 영역, UI 크기 설정(16:9 에서만 110 % 가능). 근본 해결은 모바일 전용 레이아웃(버튼 크게·한 화면에 적게)이며 이번 범위 밖이다. `LayoutRules.DpToRefPx` 로 기기별 필요 크기를 계산할 수 있다.
9. **UI 크기 110 %** 는 16:9 에서만 가능하다. 다른 비율(21:9, 4:3, 창 크기 변경)에서는 `LayoutRules` 가 그때그때 판정해 낮춰 적용한다. 낮춰졌다는 사실은 설정 화면의 안내 문구로만 알린다.
10. **의상 정책의 "카페 낮" 은 근사치**다. 호스트는 아침·영업 단계 노드를 `Day`, 밤 단계 노드를 `Night` 배경으로 시작한다(`DialogueDirector.PlayCore`). `tools/art_checklist.py` 는 노드 이름으로 단계를 추정한다(Ch*/CE*/Ending_*/*_End_*/*_Harem_Epilogue = 낮, 나머지 = 밤). 실제 재생 단계와 다르면(예: CE 노드를 밤에 재생) 필요한 의상 아트가 달라진다. 엔딩 장면은 마지막 정산 직후라 `Day` 로 시작해 직원은 Uniform 이 된다(스크립트가 `<<bg>>` 로 장소를 바꾸면 그 장소 기준).
11. **스탠딩 도중 배경이 바뀌어도 이미 나온 인물의 의상은 바꾸지 않는다**(새로 `show_standing` 할 때만 정책 적용).
12. **일시정지 중 영업 연출**은 코루틴이 기다리지만 결과는 이미 계산되어 있다(정산에는 영향 없음). 일시정지 중에도 `AudioService` 는 계속 재생된다(BGM 은 그대로, 메뉴 클릭음 가능).
13. (§8 에서 고침 — LRU 캐시) 예전 기록: `ArtLoader` 캐시는 여전히 줄지 않는다. 의상이 늘어(Uniform·Date) 오래 플레이하면 `docs/15` §1 의 "전부 올린" 상태(Android 약 188 MB)에 수렴한다. 예산 안이지만 `docs/15` §5 의 해제 정책이 필요하다.
14. `SettingsService` 는 슬라이더 변경마다 `Changed` 를 보내고 저장은 닫을 때·백그라운드/종료 때만 한다. 설정 화면 없이 강제 종료(크래시)되면 마지막 변경이 사라질 수 있다.
15. 오디오 `Voice` 채널과 덕킹(`AudioService.SetVoiceActive`)은 계산·API 만 있고 호출하는 곳이 없다(목소리 에셋이 없다).
16. `scenario_check` 의 호스트 모의는 `bgm`/`bgm_stop`/`se` 를 등록하지 않는다. 작가가 이 커맨드를 쓰기 시작하면 하네스에도 등록해야 한다(§6).

## 6. 다른 모듈에 대한 요청 (수정하지 않음)

- **Dialogue(작가·린트)**: `<<bgm id>>`, `<<bgm_stop>>`, `<<se id>>` 를 쓸 수 있다(선택). id 목록은 `docs/14_Audio_Request_List.md`. `docs/07_Dialogue_Spec.md` 의 커맨드 카탈로그에 이 세 줄을 추가해 주세요. `DialogueLint` 가 커맨드 이름을 검사한다면 세 이름을 허용 목록에 넣어 주세요(현재 `Dialogue/**` 에서 이름 검사를 찾지 못했다).
- **scenario_check 하네스 주인**: 호스트 모의(`e.Host.RegisterCommand`)에 `bgm`, `bgm_stop`, `se` 를 등록해 주세요(`DialogueCommandDispatcher.CommandNames` 를 순회하면 자동).
- **balance**: 요청 없음. (영어 모드를 완성하려면 `characters.csv` 등에 영어 이름 열이 필요 — 별도 논의.)

## 7. 검증 결과(이 환경에서 실제 실행)

| 항목 | 결과 |
|---|---|
| `sh tools/csharp_check/polish_check/run.sh`(신규) | **243 checks, 0 failures** — 오디오 곡선·dB·교차 페이드·덕킹·간격 제한, 매니페스트(실제 파일 + 잘못된·특수 행 18종), 문자열 표(실제 CSV 101키 완전성·영어 번역·폴백·자리 표시자), 설정 저장·언어 유지(실제 파일 왕복·옛 파일·범위 밖 값), 의상 정책(사례표 30건 + 디스패처 통합), 오디오 커맨드(스크립트 경유 포함), 배치 규칙(16:9·20:9·음성 테스트·코드 수치 동기화·버튼 ≥48 스크레이프), 제스처·뒤로가기, 메모리 계산(블록 수식·실제 체크리스트·플랫폼 비교) |
| `python3 tools/art_checklist.py --selftest` | 30 cases, 0 failures (C# 와 같은 사례표) |
| `tools/csharp_check/run.sh`, `romance_check` | ALL OK |
| `dialogue_check` | 302 checks, 0 failures |
| `ending_check` | 140 checks, 0 failures |
| `ui_logic_check` | 169 checks, 0 failures |
| `flow_check` | 186 checks, 0 failures |
| `scenario_check` | 99 checks, 0 failures |
| NUnit `Tests/EditMode/StageFiveTests.cs` | 17개 테스트 — **Unity 에서 실행은 못 함**, 컴파일만 확인 |
| `WORK=/tmp/unitycheck_repro sh tools/unity_compile_check/run.sh` | `UNITY COMPILE CHECK: ALL OK` |

## 8. 수정 묶음 (데모 모드 · 아트 캐시 · 현지화 · 터치 목표 · 공통 장면 연결)

> 순수 로직은 `tools/csharp_check/fixes_check`(**127 checks**)로 실행해 확인했고, Unity 의존 코드는 컴파일만 확인했다(§8.7). 화면에서 실제로 어떻게 보이는지·눌리는지는 **Unity 에서 확인하지 못했다.**

### 8.1 데모 모드
`Game/DemoConfig.cs`(순수) · `UI/DemoEndScreen.cs` · `Scripts/Editor/DemoConfigTool.cs` · `Resources/Config/build_config.csv`(`demo,0`). 동작·빌드 절차·남은 수동 단계는 `docs/18` §2.5. 요약: 3장(설정 가능)을 마치면 `Ch3_Outro` 뒤 종료 화면(위시리스트 문구는 현지화 키 `demo_end_*`), 새 게임에 `Flags["sys_demo"]=1`, 정식 버전은 데모 저장을 그대로 불러온다(호환).

### 8.2 ArtLoader 캐시
`Core/LruCache<K,V>`(개수 + 바이트 상한, 퇴출 콜백, Pin) 로 교체. 스탠딩 12조각 · 배경 4 · CG 3 · 아이콘 64. 플레이스홀더만 파괴(지연), 화면 전환 때 `Resources.UnloadUnusedAssets`(20초 간격 제한). "화면에 쓰는 스프라이트는 퇴출하지 않는다"는 **touch on use + 넉넉한 상한 + 파괴 지연**으로 풀었다(참조 카운트 아님). 정상 상태 메모리: Android 32.9 MB / PC 53.8 MB(`docs/15` §3.5). 화면이 한 번만 받아 두던 배경 4곳은 `OnShow` 마다 다시 받는다.

### 8.3 인라인 한국어 문자열
- 새 키 **188개**(snake_case)를 `ui_ko.csv`/`ui_en.csv` 에 추가(기존 UiText 101개와 합쳐 289개, 영어 전부 번역). 코드는 `Game/Loc.cs` 의 `Loc.T("key", "한국어 기본값")` / `Loc.F("key", "{0}…", args)` — 표가 비어도 한국어 기본값이 나온다. 챕터 이름·해금 `chapter_name_1..8`, `chapter_unlocks_1..8`; 인물 `char_name_<id>`; 선물 `gift_name_<id>`; 데이트 장소 `spot_name_<id>`; `gender_*`, `species_*`.
- 대상: GameScreen · RouteSelectScreen · RelationPanel · TitleScreen(갤러리) · CharacterCreateScreen · EndScreen/EndSummary · EndingPlan · EndingFlow · RouteSelect(모델 문구) · ProfileService(갤러리 칸 이름·선택지 힌트) · ProfilePreview · VnScreen · DemoEndScreen 등(스캔 스크립트로 찾은 약 110곳).
- 코드가 한국어 상수로 돌려주는 문구(경영 이벤트 6개, 밤 행동 거절 사유 등)는 `Loc.Tr(한국어)` — 한국어 표에서 같은 값의 키를 찾아 현재 언어로 바꾼다(정확히 일치할 때만; `msg_*`, `biz_event_*` 키).
- **언어를 바꾸면 화면이 다시 그려진다**: `AppContext.OnSettingsChanged` → `LocalizedLabel.RefreshAll()`(표와 같은 글자의 라벨) + 현재 화면의 `OnLocaleChanged()`. GameScreen 은 `_rebuild`(지금 보이는 내용을 다시 그리는 동작)를, Title/Route/Create/End/DemoEnd 는 각자 다시 그린다. VN 장면 중에는 바꿀 수 없고, 다음 장면부터 반영된다.
- 검사: `fixes_check` 가 **코드가 참조하는 모든 키(`Loc.T/F` 106곳 · 95키 + UiText 속성)가 두 CSV 에 있는지**, ko/en 의 `{0}` `{1}` 자리표시자가 같은지, **코드의 한국어 기본값 == ko CSV**, 데이터 CSV(chapters/characters/gifts/spots)와 ko 값 일치, 영어에 한글 없음, 그리고 UI·사용자 대면 Game 파일에 **한글 문자열 리터럴이 남지 않았는지**(로그·언어 이름 제외)를 검사한다.
- 기존 `polish_check` 에서 키 수 `101` 고정과 "orphan(UiText 에 없는 PascalCase 키)" 검사를 새 snake_case 키가 허용되도록 3줄 고쳤다(UiText 101개 검사는 그대로).
- **정직한 한계(영어 모드)**: ① **대사·시나리오 본문은 한국어뿐이다**(`SettingsDialogueNote` 가 설정 화면에서 안내). ② 아직 한국어로 남는 사용자 대면 문구: `NightRomanceActions`(게이팅 담당 파일)의 매개변수가 들어간 안내 — 루트 확정 성공/거절 사유("루트 확정은 N장부터 가능합니다", "호감도가 부족합니다. (필요 N)" 등), 행동 결과 요약("호감 +N …"), 이벤트 조건 힌트("조건: N장 · 호감 …"), 행동 대체 대사; `NightExtras`/`RecoveryTable` 의 "동의 확인 대화(이름)" 라벨; `ProfileValidator`(보안 담당 파일)의 "이름은 N자 이하" 오류; `ProtagonistProfile.DefaultName`("주인공" — 이름 값). 이들은 정확히 같은 상수만 `Loc.Tr` 로 바뀐다. 해당 파일 담당과 키 도입을 협의해야 한다. ③ 영어 번역은 직접 작성한 초안이라 원어민 검수 전이다. ④ 영어 모드의 인물 호칭(대사의 `{name}` 등)은 시나리오 쪽 규칙이다.

### 8.4 터치 목표 (모바일 48 dp)
- 순수 `Game/TouchTargetPolicy.cs`: `refPx = 48 dp × dpi / 160 / CanvasScale(해상도, UI 배율)`, 하한 48 기준 px, 상한 150. 데스크톱은 48 그대로(레이아웃 불변). 20:9 2400×1080 @420 dpi → **113 기준 px**(x0.9 에서 126), 태블릿 1920×1200 @240 → 69.
- `UI/UiMetrics.MinButtonHeight`(모바일에서만 커짐, `DisplayService` 가 화면·UI 배율이 바뀔 때 갱신)를 `UiKit.MakeButton` 이 조회한다(가로·세로 모두). Label 생성 코드는 건드리지 않았다.
- 버튼이 커지면 세로 목록이 넘치므로 모바일에서는 **스크롤 폴백**: `UiKit.VerticalLayoutScroll`(패널 안에 뷰포트+ScrollRect, 데스크톱은 이전과 동일)을 타이틀 메뉴·난이도·갤러리, 일시정지, 설정, 주인공 생성 폼, 루트 선택 목록, VN 선택지에 적용했고, GameScreen 은 `PrepareButtons(n)` 이 남은 높이를 계산해 넘칠 때만 `UiKit.MakeScrollColumn` 을 쓴다(아침·밤 메뉴·선택 목록). 그 밖의 모바일 보정: HUD 메뉴 버튼은 위쪽에 붙여 아래로 늘림, VN 자동/스킵/메뉴 버튼 높이와 선택지 패널 위치, 루트 선택 동작 영역·확인 대화·일시정지 확인 대화 높이, 갤러리 본문 24pt/270.
- 검증: `LayoutRules` 에 스택별 버튼 마스크·스크롤 여부·모바일 패널 높이를 추가하고 `CheckTouchStacks` 를 만들었다. `fixes_check` 가 16:9, 20:9(안전 영역 절단), 태블릿, 허용되는 UI 배율에서 위반 0 을 확인하고(스크롤을 빼면 야간 메뉴가 넘친다는 반례도), 7개 기기×3 배율에서 버튼이 물리 48 dp 이상임을 계산한다. **슬라이더는 여전히 48 기준 px 이다**(`MakeButton` 이 아님 — 설정 슬라이더의 터치 영역은 줄 전체 높이라 줄 높이를 키웠지만 막대·핸들은 그대로).
- **정직한 한계**: 렌더링·스크롤 감각·ScrollRect 안의 버튼 터치(드래그 vs 탭 구분)는 Unity/실기기에서 확인하지 못했다. 이미 만들어진 화면은 화면 크기가 바뀌면(폴더블) `UiMetrics` 가 갱신돼도 다시 만들기 전까지 옛 크기를 쓴다.

### 8.5 저장 불러오기 강화 (보안 검토 N-6)
`AppContext.LoadLatest`: 슬롯 하나의 읽기·상태 복원이 예외를 던져도 타이틀이 죽지 않고(그 슬롯은 손상 처리, 다음 후보 시도, 첫 오류 사유를 돌려줌), "가장 최근"은 `Game/SaveRecency.cs`(순수)로 `SavedAtUtc` 를 `DateTime`(RoundtripKind)으로 파싱해 고른다(파싱 실패 = 가장 오래됨, 동률 = 앞 슬롯). 복원 도중 실패하면 이전 세션 값을 되돌린다. 검증은 `fixes_check`(시간대가 다른 문자열이 사전순과 반대로 비교되는 사례 포함).

### 8.6 공통 장면 연결 (게이팅 담당의 요청)
`Game/DayFlowDriver.cs`(순수, `ChapterFlow.NextCommonScene` 위임 + 방문 기록 없이 반복되면 막는 안전장치) 를 `GameScreen` 이 `PlayNextCommon(slot)` 으로 호출한다: ① 아침 — `NextMorningStep` 이 끝난 뒤(`Morning`), ② 밤 메뉴를 열 때와 밤 행동 하나를 마치고 돌아올 때(`Night`), ③ 행동을 다 썼을 때와 "밤 마무리" 버튼(`NightEnd`, 정산 전), ④ 루트 선택 화면이 닫히면 `Nav.Show(Game)` → `ShowMorning` 으로 들어와 같은 아침에 `CE07` 이 이어진다. 장면은 `Dialogue.Play`(→ `PlayCore` 가 방문 기록 `_attempted`/세션 방문 기록을 남김)로 재생하고, 끝나면 게임 화면이 같은 단계를 다시 그려 더 없을 때까지 이어진다. `fixes_check` 의 **64일 모의 호스트**(GameScreen 과 같은 제어 흐름을 순수로 옮김, 실제 `scene_rules.csv`/`SceneGate`/`NightRomanceActions`, 밤 행동 0/1/2회)에서 CE01~CE08·Ch5_Show·Ch5_Show_After·Ch6_LedgerTruth 11개가 **정확히 한 번씩, 시간순으로** 재생된다. 실제 VN 화면 전환은 Unity 에서 확인하지 못했다.

### 8.6b 판 단위 상태 초기화 (QA-10)
`AppContext.ResetSession()` 을 `StartNewGame`, 불러오기 성공(`TryStartFromSave`), `ExitToTitle` 에서 부른다: `CurrentEnding`·`RunCgs` 를 비우고 `GameScreen.ResetSession()` 이 `_routePromptDay`(-1), 대기 토스트, 배속, 영업 연출 코루틴, 다시 그리기 동작, `DayFlowDriver.Reset()` 을 비운다. 점검 결과 나머지는 이미 판 단위로 초기화된다: `DialogueDirector._attempted` 는 `Bind` 에서, `RouteSelectScreen._model` 은 `OnShow` 에서 새로 만든다. `DayPhaseChanged` 구독은 생성자에서 한 번뿐이라 쌓이지 않는다. 순수 부분(`DayFlowDriver.Reset`, 루트 안내 억제는 같은 날만)은 `fixes_check` 가 검사한다.

### 8.7 검증 결과
| 항목 | 결과 |
|---|---|
| `sh tools/csharp_check/fixes_check/run.sh`(신규) | **124 checks, 0 failures** |
| `polish_check` / `flow_check` / `ui_logic_check` / `ending_check` / `dialogue_check` | 243 / 186 / 169 / 140 / 302 checks, 0 failures |
| `tools/csharp_check/run.sh`, `romance_check`, `gate_check` | ALL OK (gate_check 162 checks) |
| NUnit `Tests/EditMode/StageFixTests.cs`(신규) | 11개 테스트 — **컴파일만 확인**(Unity 실행 못 함) |
| `WORK=/tmp/unitycheck_repro sh tools/unity_compile_check/run.sh` | `UNITY COMPILE CHECK: ALL OK` (Editor 폴더의 `DemoConfigTool.cs` 는 UnityEditor 가 없어 **컴파일 검증 못 함**) |
