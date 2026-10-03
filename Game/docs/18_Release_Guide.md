# 18. 배포 가이드 (Release Guide)

> 대상: itch.io(데모·유료) → Steam → Android(Google Play). 가격 철학은 `docs/04` §3(매입형 + 콘텐츠 DLC, **가챠·확률형·시간 압박 과금 없음**), 수위는 T0/T1(`docs/04` §4, 노골적 묘사 없음).
> **이 문서의 원칙:** 스토어 정책·수수료·화면 구성·설문 문항은 자주 바뀝니다. 아래에서 *"(확인 필요)"* 로 표시했거나 §0 목록에 있는 항목은 제가 정책을 보증하지 않으며, **제출 직전에 공식 페이지의 현재 내용을 직접 확인**해야 합니다. 확인한 날짜와 URL 은 `docs/legal/AI_AND_RIGHTS_CHECKLIST.md` 양식에 기록하세요.
> **한계:** 이 환경에는 Unity·스토어 계정이 없어 아래 절차 중 어떤 것도 실제로 실행해 보지 못했습니다(빌드 메뉴 포함). 명령어는 공식 도구의 일반적 사용법에 기반한 초안입니다.

## 0. 제출 전에 반드시 직접 확인할 공식 페이지 (정확히 이 목록)

| # | 플랫폼 | 확인할 페이지(이름으로 검색) | 무엇을 확인하나 |
|---|---|---|---|
| 1 | Steam | **Steamworks 문서 (partner.steamgames.com → Documentation)** 의 "Steam Direct / 앱 등록 비용", "Store Page Requirements(캡슐·스크린샷·트레일러 규격)", "Release Process(출시 전 대기 기간·심사)" | 비용, 최소 대기 일수, 이미지 규격(수치는 *역사적으로* 달랐으므로 현재값 확인) |
| 2 | Steam | **Steamworks "Content Survey(콘텐츠 설문)"** 및 **"AI generated content disclosure(AI 생성물 공개)"** 안내 페이지 | 설문 문항, AI 공개 분류·표시 방식 |
| 3 | Steam | **Steam 판매 지침 / Steam Distribution Agreement / Steam Online Conduct 중 성적 콘텐츠·"Adult Only Sexual Content" 관련 규정** | 성인 콘텐츠 허용 범위, 연령 확인, 지역 제한 |
| 4 | Steam | **Steamworks "Pricing / Regional pricing", "Revenue share(수수료)", "Tax & banking(세무 정보)"** | 수수료·지역 가격·세금 양식 |
| 5 | itch.io | **itch.io Creator 문서 (itch.io/docs/creators)**, **butler 문서 (itch.io/docs/butler)** | 페이지 필드, 채널 이름 규칙, API 키 사용법 |
| 6 | itch.io | **itch.io Content Policy / Terms of Service / 성인(Mature) 콘텐츠 및 AI 생성물 공개 관련 안내와 프로젝트 편집 화면의 해당 필드** | 성인 콘텐츠 표시 방식, AI 공개 필드 유무 |
| 7 | Google Play | **Play Console 도움말: "Data safety section", "Content ratings(IARC 설문)", "Target audience and content", "Target API level requirements", "Use Play App Signing", "Create and set up your app"** | 설문 문항, 필수 API 레벨, 서명 방식, 계정 요건 |
| 8 | Google Play | **Google Play 개발자 프로그램 정책: "Sexual Content", "AI-Generated Content", "User Data/Privacy Policy" 항목** | 성적 콘텐츠 허용 범위, AI 생성물 규정, 개인정보처리방침 URL 요건 |
| 9 | Google Play | **Play Console 도움말: 개인 개발자 계정의 신규 앱 출시 요건(비공개 테스트 요건 포함)과 개발자 등록비** | 출시 전 필수 테스트 기간·인원, 계정 확인 절차 |
| 10 | 한국 | **게임물관리위원회(GRAC, grac.or.kr) 등급분류 안내**, **공정거래위원회/지자체의 통신판매업 신고 안내**, **국세청 사업자등록/부가세 안내** | 국내 이용자에게 PC 게임을 판매·배포할 때의 등급분류·사업자·신고 의무 (확인 필요 — 이 문서는 법적 판단을 하지 않음) |
| 11 | 도구 | **Unity 문서: "Build for Android(AAB, 서명)", Unity 요금제/약관(Unity Personal/Pro 매출 한도 및 로고 조건)** | Unity 6 의 Android 빌드 요건, 사용 요금제 조건 |

## 1. 공통: 빌드 → 무결성 → 보관

1. 버전 올리기: `Edit → Project Settings → Player → Version`(= `PlayerSettings.bundleVersion`, **형식 `MAJOR.MINOR.PATCH`**). Android 는 `Bundle Version Code` 도 **스토어 업로드마다 증가**(자동 아님).
2. `sh tools/release_check.sh` → PASS, Unity 에서 `MorpheHouse/Build/Preflight Check` → FAIL 0 (`docs/17` §11 게이트).
3. 빌드 메뉴: `MorpheHouse/Build/Windows (Release)`, `…/Android APK (Debug)`, `…/Android AAB (Release)`. 출력: `UnityProject/Builds/<Windows|Android>/<버전>/`.
   - Preflight 가 FAIL 이면 빌드를 시작하지 않습니다.
   - 배치 모드(CI)에서는 종료 코드로 성패 반환: `Unity -batchmode -nographics -quit -projectPath UnityProject -executeMethod MorpheHouse.EditorTools.BuildTools.BuildWindowsRelease` (Android AAB 는 §4 의 환경 변수 필요). *(미검증 — 첫 실행 시 확인)*
4. 같은 폴더에 `BUILD_INFO.txt` 가 생깁니다: 버전, 플랫폼, UTC 일시, Unity 버전, git 커밋(없으면 `unavailable`), 배포 파일 각각의 SHA-256(`sha256sum` 형식). **이 파일 내용(또는 zip 의 SHA-256)을 릴리스 노트/페이지에 게시**하면 이용자가 무결성을 검증할 수 있습니다.
   - Windows: 폴더 전체를 zip(예: `MoonCornerHouse-<버전>-win64.zip`), `sha256sum` 으로 zip 해시를 따로 기록. `*_BurstDebugInformation_DoNotShip`, `*_BackUpThisFolder_ButDontShipItWithYourGame` 폴더는 **배포하지 않음**(BUILD_INFO 에서도 제외).
5. 소스 보관: `sh tools/package_source.sh <버전>` → `dist/MorpheHouse-src-<버전>.tar.gz` + `.sha256`.
6. 서명: Windows 코드 서명은 선택(없으면 SmartScreen 경고가 뜰 수 있음 — 배포 페이지에 해시와 안내 문구). Android 는 필수(§4). **비밀(키스토어·API 키·Steam 로그인)은 저장소에 절대 커밋하지 않습니다** — `release_check` 의 `secret_scan` 이 파일/문자열 패턴을 점검합니다(휴리스틱).

## 2. itch.io

### 2.1 butler 푸시 흐름 (공식 도구, 명령은 확인 필요)
```
# 1회: butler 설치 후
butler login                       # 브라우저 인증. CI 에서는 환경 변수 BUTLER_API_KEY 사용(비밀로 관리)
# 빌드마다 (<user>/<game> 은 itch 프로젝트 URL 에서)
butler push UnityProject/Builds/Windows/0.1.0  <user>/<game>:windows-demo --userversion 0.1.0   # 데모
butler push UnityProject/Builds/Windows/1.0.0  <user>/<game>:windows      --userversion 1.0.0   # 정식
butler status <user>/<game>        # 채널별 최신 빌드/처리 상태 확인
```
- **채널 이름**: 플랫폼을 알 수 있는 이름(`windows`, `android` 등)을 쓰면 itch 가 대상 플랫폼을 자동 인식하는 것으로 알려져 있음(확인 필요). 데모/정식을 같은 프로젝트의 다른 채널로 두거나 **별도 프로젝트(데모 전용 페이지)** 로 분리 가능 — 위시리스트 유도가 목적이면 데모를 *무료 별도 프로젝트* 로 두는 편이 가격 변경이 자유롭습니다.
- `butler push` 는 델타 패치를 올립니다. 폴더에 `BUILD_INFO.txt` 가 있으므로 이용자에게 해시도 노출됩니다.
- 업로드 후: itch 페이지의 **Uploads** 에서 플랫폼 체크, 실행 파일 지정 여부 확인, 본인이 itch 앱/브라우저로 내려받아 `sha256sum` 이 `BUILD_INFO.txt` 와 일치하는지 확인.

### 2.2 페이지 필드 체크 (필드명은 편집 화면 기준 — 확인 필요)
- [ ] Title: 달빛 모퉁이의 수인들 / Moon Corner House (상표 점검 후 확정: `docs/legal/AI_AND_RIGHTS_CHECKLIST.md` §5)
- [ ] Project URL, Short description/tagline(한 줄), Classification(Games), Kind of project(Downloadable)
- [ ] Release status(데모: *In development* 또는 *Prototype* 계열 / 정식: *Released*), Pricing(데모 무료, 정식 유료 — §5)
- [ ] Description: 소개, 줄거리 요약, 기능, 플레이 시간(시뮬 기준 약 13h, `docs/03`), 언어(한국어; 영어는 예정이면 "planned"), 입력(마우스/키보드/터치), 시스템 요구사항 [TODO: 실측 후], 세이브 위치와 **무수집(계정·텔레메트리 없음)**, 해시 게시 위치, 문의처
- [ ] **콘텐츠 정직 표기**: 연애 중심, 암시적 성인 취향 요소(노골적 묘사 없음), 다자(공동) 루트 존재(전원 합의) — 페이지에서 성인 콘텐츠/연령 관련 필드가 있으면 §0 #6 확인 후 truthfully 선택
- [ ] **AI 공개**: itch 편집 화면에 AI 사용 공개 필드가 있으면 `art_log.csv` 의 `ai_assisted` 집계와 **일치**하게(§3.3 의 표 참고). 설명문에도 사용 범위를 적습니다.
- [ ] Cover image, Screenshots(3~5장, 스탠딩·CG·경영 화면 균형), (선택) GIF/트레일러 — 규격은 편집 화면 안내 확인
- [ ] Genre/Tags(최대 개수는 화면 안내 확인), Community(댓글/게시판 설정), Custom noun(`game`), Accessibility/Mature 관련 설정
- [ ] 다운로드 파일 이름에 버전 포함, 지원하지 않는 플랫폼(Mac/Linux)은 "Windows only" 명시

### 2.3 태그 제안 (itch 는 자유 태그 + 추천 목록 — 실제 사용 가능한 태그 확인)
`Visual Novel`, `Dating Sim`, `Romance`, `Furry`, `Anthropomorphic`, `Management`, `Simulation`, `Cute`, `Story Rich`, `Multiple Endings`, `Choices Matter`, `Singleplayer`, `2D`, `Korean`, `Relaxing`, `LGBT`(주인공 성별·호칭 선택과 연애 구성이 실제 콘텐츠와 맞을 때만 — 오해를 부르지 않게), `Customizable`(주인공 설정).

### 2.4 데모 계획: 1~2장 + 하울 E1/E2 슬라이스 (파일 구성 문서화)

목적: Steam 위시리스트·itch 팔로우 유도(`docs/04` §3). 아래는 **어떤 콘텐츠를 포함할지의 명세**이며, 데모 전용 빌드 장치(3장 상한 + 종료 화면 + 저장 표시)는 구현되어 있습니다(§2.5, Unity 실행 확인은 아직).

**포함할 시나리오 파일** (`UnityProject/Assets/_Project/Dialogue/`), `#line:` 줄 수는 2026-10-03 실측:

| 파일 | 노드 | 줄 수 |
|---|---|---|
| `common/prologue_ch1.yarn` | `Ch1_Intro`, `Ch1_Kitchen` | 54 |
| `common/CE01_FirstGuest.yarn` | `CE01_FirstGuest` | 27 |
| `common/ch02.yarn` | `Ch2_Intro`, `CE02_Tasting`, `Ch2_Outro` | 112 |
| `routes/howl/howl_E1.yarn` | `Howl_E1` (CG `HOWL_E1`) | 52 |
| `routes/howl/howl_E2.yarn` | `Howl_E2` (CG `HOWL_E2`) | 57 |
| `routes/howl/howl_talk.yarn` | `Howl_Talk_01`…`06` | 157 |
| `routes/howl/howl_gift.yarn` | `Howl_Gift_Like/Neutral/Dislike` | 40 |
| **합계** | | **499줄** |

선택 포함(분량 보강): `routes/howl/howl_date_*.yarn`(하울 데이트 3종). **제외**: 3장 이후 공통(`ch03`~`ch08`, `shared_routes`), 다른 5명의 `routes/*`, 모든 엔딩·공동 루트 파일.

**데모에 필요한 아트(스크립트가 실제 호출하는 것, 실측):** 스탠딩 10종(하울 6표정 neutral/smile/shy/angry/sad/surprised, 곰석 2, 요한 2), 배경 `cafe`(새벽/낮/해질녘/밤 변형 → 낮·저녁·밤) + `harbor`(해질녘), 이벤트 CG 2장(`HOWL_E1`, `HOWL_E2`), 타이틀 배경, 메뉴 아이콘(초기 12종). 목록과 파일명은 `docs/13_Art_Checklist.md`·`docs/art_checklist.csv` 에서 해당 행만 골라 사용. **데모 빌드에 플레이스홀더 아트가 남으면 스토어 페이지에서 정직하게 표기**(`docs/17` 게이트 4).

**데모 범위 설계 제약(중요, 현재 데이터 기준):**
- `balance/events.csv` 에서 개인 이벤트 2단계는 `chapter_min=3`, 호감 30·신뢰 8 이상입니다. **1~2장만으로는 `Howl_E2` 가 열리지 않습니다.** 선택지: (a) 데모 전용 밸런스에서 2단계 `chapter_min` 을 2 로 낮추고 2장 난이도에 맞는 `aff_min` 조정(+ `simulate_balance.py` 재검증), (b) 데모를 3장 첫 며칠까지 확장, (c) E2 를 데모 전용 이벤트로 별도 연결. 어느 쪽이든 **런타임/밸런스 소유자와 결정 필요**.
- ~~챕터 상한·데모 종료 화면~~ → **구현함**(아래 §2.5). 데모는 **3장까지**(기본 N=3)라 `Howl_E2`(개인 이벤트 2단계, `chapter_min=3`)와 `CE03_RentMeeting`(3장 공통 이벤트)에 닿는다. 루트 확정은 7장부터라 데모에서는 나오지 않는다.
- ~~데모 세이브 호환 정책~~ → **정함**: 데모 저장은 `Flags["sys_demo"]=1` 로 표시되고, 데모 세이브는 정식 버전과 **완전히 호환**된다(같은 `SaveData`·밸런스·챕터 번호). 정식 버전은 경고·거부 없이 그대로 이어서 하게 한다(표시 플래그만 남는다). 반대로 정식 세이브를 데모 빌드에서 불러 4장 이상이면 곧바로 데모 종료 화면이 뜬다.

### 2.5 데모 빌드 — 구현한 방식과 남은 수동 단계

**구현(코드, `docs/16` §8 참고):**
| 항목 | 내용 |
|---|---|
| 설정 파일 | `Resources/Config/build_config.csv` — `key,value` 행. `demo`(0/1, 출고 기본 **0**), `demo_last_chapter`(기본 3, 1~8로 보정), `store_url`(선택, `https://` 만 허용, 비어 있으면 스토어 버튼을 숨김). 파일이 없으면 정식 빌드로 동작 |
| 순수 로직 | `Game/DemoConfig.cs` — 파싱, `IsDemoOver(chapter, finished)`(= 켜져 있고 `chapter > demo_last_chapter` 이거나 게임 종료), `ToggleDemo`, `sys_demo` 플래그 표시/조회 |
| 흐름 | `GameScreen.ShowMorning`: 마지막 데모 챕터를 마치고 다음 챕터 첫 아침에 들어가면, 아직 안 본 `Ch<N>_Outro` 는 재생한 뒤 **DemoEndScreen** 으로 간다(다음 챕터 도입·정보 카드는 건너뜀). 새 챕터 첫 아침 자동 저장 뒤에 이어하기를 해도 같은 결과라 데모 밖으로 새지 않는다 |
| 종료 화면 | `UI/DemoEndScreen.cs` — 감사 문구, **위시리스트 안내(현지화 키 `demo_end_*`, ko/en)**, 통계(일수·골드·가게 레벨·평판·플레이 시간·본 CG), 스토어 버튼(`store_url` 이 있을 때), 저장 호환 안내, 타이틀로 |
| 저장 표시 | 새 게임 시작 때 데모가 켜져 있으면 `State.Flags["sys_demo"]=1` |
| 에디터 | 메뉴 **`MorpheHouse/Build/Toggle Demo Config`**(`Scripts/Editor/DemoConfigTool.cs`) — csv 의 `demo` 행을 0↔1 로 뒤집고 에셋을 새로고침한다(Unity 에서 실행해 보지 못함) |
| 검증 | `tools/csharp_check/fixes_check`(파싱·종료 판정·토글·URL·플래그), `Tests/EditMode/StageFixTests.cs`. 실제 화면 전환·종료 화면 렌더링은 Unity 에서 확인하지 못했다 |

**데모 빌드 절차(수동):**
1. 메뉴 `MorpheHouse/Build/Toggle Demo Config` 로 `demo,1` 로 만든다(필요하면 `demo_last_chapter`, `store_url` 편집). Git 에는 `demo,0` 만 커밋한다.
2. 위시리스트 URL 을 `store_url` 에 넣는다(스토어 페이지가 공개된 뒤). 비워 두면 안내 문구만 나온다.
3. 데모용 빌드 이름·App ID 를 정식과 분리한다(§2.1~2.3, §3 — 스토어 쪽 작업).
4. 빌드 후 **직접 확인**: 새 게임 → 3장 마지막 날 정산 → 3장 `Ch3_Outro` → 데모 종료 화면 → 타이틀로 → 이어하기가 다시 종료 화면으로 가는지, `saves` 의 슬롯 json 에 `sys_demo` 가 있는지.
5. **정식 빌드 전에 반드시 다시 `demo,0`** 으로 되돌린다(출고 체크리스트 항목으로 추가할 것; `build_config.csv` 의 `demo` 값을 `release_check.sh` 에 넣는 것은 아직 안 했다).

### 2.5.1 아직 남은 수동 항목 (데모)
- [ ] 데모용 `Resources/Dialogue` 부분 집합: 이 장치는 **챕터 상한으로만** 막는다(4장 이후 대사 파일도 빌드에 들어간다). 용량·스포일러가 걱정이면 제외 파일을 빼고 `_index.txt` 재생성 후 빌드(빠진 노드는 "정보 카드"로 처리되는지 `docs/12` §4 에서 확인)
- [ ] `release_check.sh`/`BuildPreflight` 에 "정식 빌드인데 `demo,1`" 경고 추가(런타임/빌드 담당과 결정)
- [ ] `BuildTools` 에 데모 빌드 전용 메뉴(이름·출력 폴더 분리) 추가 여부 결정(지금은 토글 메뉴 + 기존 빌드 메뉴)
- [ ] 스토어 URL·위시리스트 문구 최종 확정(현지화 키 `demo_end_cta`, `demo_end_wishlist` 수정)
- [ ] Unity 에서 위 4번 흐름 실행 확인(이 환경에서는 못 함)

## 3. Steam

### 3.1 Steamworks 앱 설정 — 순서 (개념 순서. 화면·요건은 §0 #1~#4 확인)
1. Steamworks 파트너 계정 만들기 → 개인/사업자 정보, **세금 정보, 은행 정보** 제출(국가별 양식 상이, 심사에 시간이 걸릴 수 있음 → 출시 목표일보다 훨씬 일찍 시작)
2. 신원/사업자 확인 절차 완료(요구 서류는 현재 안내 확인)
3. 앱 등록비(Steam Direct) 결제 — 금액·환급 조건은 §0 #1 에서 확인
4. 앱 생성 → **App ID** 확보 (게임 App ID 와 DLC 별도 App ID 는 다름)
5. 스토어 페이지 작성(§3.5 체크리스트) — 캡슐 이미지 규격은 현재 문서 기준으로 제작
6. 기본 패키지/가격/지역 가격 설정, 출시 예정일 설정 → **"Coming Soon" 페이지 공개**(공개 후 위시리스트 수집; 출시까지 최소 대기 기간 규정은 §0 #1 확인)
7. 콘텐츠 설문(§3.4) + AI 공개(§3.3) 작성
8. 빌드 업로드(SteamPipe, §3.2) → 기본 브랜치/베타 브랜치 구성 → **빌드 심사 제출**과 **스토어 페이지 심사 제출**(각각 별도 심사로 알려져 있음, 소요 시간은 안내 확인)
9. 출시 버튼(릴리스) — 승인 후 날짜에 맞춰 직접 누르는 단계가 있음(확인 필요)

### 3.2 디포·빌드 구성 개념 (SteamPipe)
- **App** = 게임 전체, **Depot** = 파일 묶음(OS/언어/DLC 단위). 이 프로젝트: Windows 디포 1개(+ 나중에 DLC 디포).
- **Package/Launch option**: 어떤 디포를 어떤 실행 파일(`MoonCornerHouse.exe`)로 시작할지 정의.
- **Branch**: `default`(공개), `beta`(비공개 테스트, 비밀번호). 새 빌드는 `beta` 에 먼저 올려 검증 후 `default` 로 승격하는 흐름을 권장. 롤백 = 이전 빌드를 다시 `default` 로 지정(빌드 ID 기록).
- **빌드 스크립트 예시(자리표시자, 값은 Steamworks 에서 확인):**
```
// app_build_<APPID>.vdf
"AppBuild" {
  "AppID" "<APPID>"
  "Desc" "0.1.0 <커밋 해시 앞 8자리>"
  "ContentRoot" "..\UnityProject\Builds\Windows\0.1.0\"
  "BuildOutput" "..\steam_output\"
  "SetLive" "beta"                 // 처음엔 default 로 바로 올리지 않기
  "Depots" { "<DEPOTID>" "depot_build_<DEPOTID>.vdf" }
}
// depot_build_<DEPOTID>.vdf
"DepotBuild" {
  "DepotID" "<DEPOTID>"
  "FileMapping" { "LocalPath" "*" "DepotPath" "." "Recursive" "1" }
  "FileExclusion" "*_BurstDebugInformation_DoNotShip*"
  "FileExclusion" "*_BackUpThisFolder_ButDontShipItWithYourGame*"
}
```
  실행: `steamcmd +login <빌드 전용 계정> +run_app_build <경로>\app_build_<APPID>.vdf +quit` — **로그인 정보는 저장소/스크립트에 쓰지 말고** 비밀 관리(Steam Guard 처리 포함). *(미검증)*
- Steam 은 자체 파일 검증/업데이트를 하므로 itch 용 `BUILD_INFO.txt` 해시 게시는 보조 수단입니다(설명문에 Steam 은 자체 검증 사용 명시).
- Steam 클라우드/업적/통계 같은 Steamworks 기능은 **현재 사용하지 않음**(`docs/legal/PRIVACY_POLICY_DRAFT.md` §4 와 일치 유지 — 도입하면 처리방침·SBOM(Steamworks SDK) 갱신).

### 3.3 AI 생성물 공개 — 두 경우의 답안 초안 (**본인이 진실에 맞는 쪽을 고르세요**)

> 먼저 읽기: Steam 은 *AI 생성물 사용 여부 공개*를 요구하며, 개발 중 미리 생성한 콘텐츠("pre-generated")와 실행 중 생성하는 콘텐츠("live-generated")를 구분하고, 스토어 페이지에 공개 내용이 표시되는 것으로 제가 알고 있습니다. **문항 이름·분류·표시 방식은 §0 #2 의 현재 페이지로 확인**하고, 아래 문장은 *초안*으로만 쓰세요.
>
> **중요한 사실 확인(이 저장소 기준):** 일러스트는 사용자가 직접 만든다고 하셨지만, **이 저장소의 시나리오 대사·스토리(`Dialogue/**/*.yarn`, `docs/08_*`)와 코드는 AI(Claude) 에이전트가 작성**했습니다. 따라서 "AI 를 전혀 쓰지 않았다"는 게임 *전체* 기준으로는 **사실이 아닐 가능성이 큽니다.** 스토어 공개는 일러스트만이 아니라 *게임에 들어가는 모든 콘텐츠(텍스트·음악·음성·코드 등 설문이 묻는 범위)* 를 기준으로 판단해야 하며, 어떤 범위까지 공개 대상인지는 해당 페이지의 정의를 읽고 결정하세요. 아래 표를 먼저 채워 진실을 정리한 뒤 해당하는 경우를 고르세요.

**진실 정리표 (art_log.csv·music_sfx_log.csv 와 일치시키기)**

| 콘텐츠 | 제작 방식(직접 / AI 보조 / AI 생성) | 근거 | 현재 기록 |
|---|---|---|---|
| 일러스트(스탠딩·CG·배경·아이콘·타이틀) | [본인 기입] | `art_log.csv` | 사용자 직접 제작(진술) |
| 시나리오 텍스트 | **AI 생성(이 저장소 작성 방식)**, 본인이 검수·수정한 범위 [본인 기입] | `WRITER_BRIEF.md`, 커밋 이력 | |
| 코드 | **AI 보조/생성**(개발 도구로서의 사용) | 저장소 이력 | |
| 음악/효과음/음성 | [본인 기입] | `music_sfx_log.csv` | 미정 |
| 폰트·UI 아이콘 | [본인 기입] | `font_log.csv` | 미정 |
| 실행 중(live) 생성 | 없음(오프라인, 네트워크 호출 없음 — 코드 검색으로 확인) | `PRIVACY_POLICY_DRAFT.md` 체크 | 해당 없음 |

**경우 A — "AI 미사용"** (위 표의 *모든 행*이 진실로 '직접'일 때만 선택 가능. 이 저장소의 시나리오/코드 작성 방식을 감안하면 **현 상태로는 선택하기 어려울 수 있음**)
- 사전 생성 AI 콘텐츠 사용: **아니오**
- 실행 중 AI 생성 콘텐츠: **아니오**
- 스토어 설명 문구 초안(영/한): "All art, writing, music and code in this game were created by humans without generative AI." / "이 게임의 모든 아트, 글, 음악, 코드는 생성형 AI 없이 사람이 만들었습니다."
- 근거 보관: `art_log.csv`(`ai_assisted=no` 전부), `music_sfx_log.csv`, 작업 이력.

**경우 B — "AI 보조 사용"** (이 저장소의 현재 작성 방식과 일치하는 쪽. 일러스트가 직접 제작이어도 텍스트가 AI 생성이면 이쪽)
- 사전 생성 AI 콘텐츠 사용: **예** — 사용 범위 서술란 초안(영): "Illustrations are hand-made by the developer. The story script/dialogue was drafted with a generative AI model (Claude by Anthropic) and edited by the developer [edit as applicable: edited/reviewed by ___]. AI coding assistance was used in development. No AI-generated content is created during gameplay; the game runs offline." / (한): "일러스트는 개발자가 직접 제작했습니다. 스토리 대사/시나리오는 생성형 AI(Anthropic Claude)로 초안을 작성하고 개발자가 [수정/검수한 범위 기입]했습니다. 개발에는 AI 코딩 보조를 사용했습니다. 게임 실행 중 AI 가 콘텐츠를 생성하지 않으며 오프라인으로 동작합니다."
- 실행 중 AI 생성 콘텐츠: **아니오**
- 확인 사항: 사용한 AI 도구의 약관이 출력물의 상업적 이용·권리 귀속을 허용하는지(**확인한 날짜·URL 기록**), 생성물이 타인의 권리를 침해하지 않는지 검토했다는 진술이 요구될 수 있음(문항 확인).
- 진실하게 쓰는 것이 우선입니다: **일부만 AI 를 썼다면 "미사용"으로 답하지 마세요.** 사후에 문제가 되면 심사 거절·삭제 등의 위험이 있습니다(정책 확인).
- itch.io/Google Play 에도 같은 사실을 일관되게 쓰세요(§0 #6, #8).

### 3.4 콘텐츠 설문 · 연령 등급 가이드
- 설문은 **최종 콘텐츠를 실제로 읽은 뒤** 사실대로 작성합니다. 아래는 사실 정리(제출용 답안이 아님):

| 항목 | 이 게임의 실제 상태 |
|---|---|
| 노출/나체 | 없음(`docs/02` §2 "노출 없는 의상", `docs/10` §3) |
| 성적 콘텐츠 | **암시 수위(T1)**: 키스·포옹·유혹적 대사·장면 전환(페이드 아웃). 노골적 묘사 없음, 외설적 일러스트 없음 |
| 폭력 | [TODO: 최종 시나리오 확인 후 기입 — 현재 가벼운 갈등 수준으로 보이나 미확인] |
| 약물/음주 | 카페/칵테일 소재(하울 E1 에서 칵테일 제조·시음) — 음주 묘사 있음 |
| 도박/현금 과금 | 없음(가챠·확률형 없음). 가상 재화는 게임 내 골드만, 실결제 연동 없음 |
| 다자(공동) 연애 | 합의된 성인 관계만(`docs/10` §3) |
| 캐릭터 연령 | 전원 성인 설정 |
| 실행 중 사용자 생성 콘텐츠/온라인 | 없음 |

- 성적 콘텐츠 질문에서 *"암시 수준"을 어느 단계로 분류할지*는 각 스토어의 정의가 다르므로 §0 #2, #3, #7(IARC), #8 의 정의를 읽고 선택하세요. 의심되면 **더 보수적(높은) 등급**으로 신고하는 쪽이 심사 리스크가 낮습니다.
- **Google Play**: IARC 설문 → 결과 등급을 그대로 사용. 성적 콘텐츠 정책 해당 여부 확인(§0 #8).
- **한국**: 국내 이용자 대상 PC 게임 배포 시 등급분류 의무/면제 여부, 청소년 이용불가 분류 가능성은 **GRAC 안내로 직접 확인**(§0 #10). 이 문서는 판단하지 않습니다.
- T2(노골적) 콘텐츠를 추가하는 일은 이 프로젝트 범위 밖이며(`docs/04` §4), 추가하면 모든 스토어 정책·연령 확인·결제사 제한을 직접 검토해야 합니다.

### 3.5 스토어 페이지 요구사항 체크리스트 (규격은 §0 #1 현재 문서로)
- [ ] 이름/제목, 짧은 설명, 상세 설명(마크업), 언어 지원(인터페이스/음성/자막 구분: 한국어 인터페이스·자막, 음성 없음)
- [ ] 캡슐 이미지 전 규격 세트, 헤더, 라이브러리 이미지(히어로/로고 포함 여부 확인), 스크린샷(최소 개수 확인), (선택) 트레일러
- [ ] 장르/태그(§2.3 유사), 시스템 요구사항(최소/권장 — 실측 후)
- [ ] 가격·지역 가격, 출시 예정일
- [ ] 콘텐츠 설문 · AI 공개 · 연령 확인(필요 시) · 성인 콘텐츠 고지
- [ ] **개인정보처리방침 URL**(Steam 은 필요 시 링크 — `PRIVACY_POLICY_DRAFT.md` 법률 검토 후 호스팅)
- [ ] 크레딧/서드파티 고지(`THIRD_PARTY_NOTICES.md`) — 게임 내 화면 또는 동봉 파일
- [ ] 지원 연락처, 커뮤니티 허브 설정, 업적/클라우드 미사용 명시
- [ ] 빌드: 실행 시 크래시 없음, 설치 폴더에 개발용 파일 없음, 디버그 심볼 제외

## 4. Android (Google Play)

> 출시 우선순위는 PC 이후입니다. 모바일 해상도·터치·세이프 에어리어는 `docs/17` §4 B 등급·§8 을 먼저 통과해야 합니다.

### 4.1 서명 키스토어 취급
- **Play App Signing 사용을 전제**(§0 #7). 로컬에서 **업로드 키**만 만들고 Google 이 앱 서명 키를 보관하는 방식이 일반적입니다.
- 키스토어 생성(JDK `keytool`; Unity 번들 OpenJDK 에도 포함):
  `keytool -genkeypair -v -keystore moonhouse-upload.jks -alias upload -keyalg RSA -keysize 2048 -validity 10000`
- **저장소 밖**에 보관(예: 암호 관리자 + 오프라인 백업 최소 2곳). 루트 `.gitignore` 가 `*.keystore *.jks *.pfx *.p12 *.pem *.key *.p8 keystore*.properties local.properties google-services.json .env*` 와 `*.apk *.aab Builds/ UnityProject/Library/` 등을 막지만, 그래도 **저장소 폴더에 두지 마세요**(`release_check` 가 해당 파일 발견 시 FAIL, `package_source.sh` 도 같은 패턴이면 중단).
- `MorpheHouse/Build/Android AAB (Release)` 는 **환경 변수**로만 서명 정보를 받습니다(없으면 빌드 중단): `MORPHE_KEYSTORE_PATH`, `MORPHE_KEYSTORE_PASS`, `MORPHE_KEY_ALIAS`, `MORPHE_KEY_PASS`. 셸 히스토리에 비밀번호가 남지 않게 설정하세요(세션 한정 변수 입력, CI 비밀 저장소).
- 업로드 키 분실 시 복구 절차는 Play Console 도움말 확인(§0 #7). 앱 서명 키는 앱 생성 후 변경 불가이므로 **패키지 이름(`PlayerSettings` Android Application Identifier)을 먼저 확정**(Preflight 가 `com.DefaultCompany.*` 면 WARN).
- Debug APK 는 Unity 기본 디버그 서명(또는 설정된 키)으로 빌드되며 **스토어 업로드 불가, 테스트 전용**.

### 4.2 Play Console 절차 (순서, §0 #7~#9 확인)
1. 개발자 계정 등록(등록비, 신원 확인; 개인 계정의 신규 앱 비공개 테스트 요건 여부 확인 — 요건이 있으면 일정에 반영)
2. 앱 만들기(이름, 기본 언어 ko-KR, 앱/게임, 무료/유료 — **유료 앱↔무료 전환은 제한적이므로 신중히**, 확인 필요)
3. 패키지 이름 확정 → Play App Signing 등록 → 첫 **AAB** 업로드(내부 테스트 트랙부터)
4. 스토어 등록정보: 제목, 간단/자세한 설명, 아이콘 512×512, 대표 그래픽 1024×500, 스크린샷(휴대전화), 카테고리, 연락처, **개인정보처리방침 URL**
5. 앱 콘텐츠 선언: 개인정보처리방침, 광고 포함 여부(**아니오**), 앱 액세스(제한 없음), **콘텐츠 등급(IARC)**, 타겟층 및 콘텐츠, **데이터 보안(§4.3)**, 기타 선언(정부/금융/건강 등 해당 없음 확인)
6. 가격·배포 국가, 내부 → 비공개 → (공개 테스트 선택) → 프로덕션 순 출시, **단계적 출시(%)** 사용 권장
7. 출시 전 보고서(사전 출시 테스트)의 크래시/호환성 경고 확인
8. 업로드된 AAB 의 대상 API 레벨·64비트 요건 충족 확인(§0 #7: Target API 요건은 매년 상향되므로 **현재값 확인**). 이 프로젝트는 IL2CPP + ARM64 로 빌드(`BuildTools`).

### 4.3 데이터 보안(Data safety) 양식 답안 — "수집 데이터 없음" 기준
**전제(확인 후에만 사용):** 최종 빌드에 네트워크 호출·분석·광고 SDK 가 없고(`grep -rn "UnityWebRequest\|HttpClient\|Analytics\|Advertisement" UnityProject/Assets` 결과 0건, 서드파티 패키지 추가 시 재확인), `INTERNET` 권한이 없으며(`bundletool dump manifest` 또는 `aapt2 dump permissions` 로 최종 AAB 확인), 모든 세이브는 기기 로컬에만 있을 때.

| 양식 질문(요지, 현재 문구는 §0 #7 확인) | 초안 답 |
|---|---|
| 앱이 필수 사용자 데이터 유형을 **수집하거나 공유**합니까? | 아니요 |
| 데이터 유형 선택(위치, 개인 정보, 금융, 건강, 메시지, 사진·동영상, 오디오, 파일, 캘린더, 연락처, 앱 활동, 웹 탐색, 앱 정보 및 성능, 기기 또는 기타 ID) | 선택 없음 |
| 전송 중 암호화 / 데이터 삭제 요청 방법 | 데이터를 수집하지 않으므로 해당 없음(양식이 요구하면 그 취지로 기재) |
| 개인정보처리방침 URL | `PRIVACY_POLICY_DRAFT.md` 를 법률 검토 후 호스팅한 URL [TODO] |
| 광고 | 광고 없음 |

- 이 답은 **사실과 일치해야** 합니다. 이후 업데이트에서 분석/크래시 리포팅/클라우드를 추가하면 양식·처리방침·SBOM 을 먼저 고치세요.
- 주의: Play 스토어 자체가 플랫폼 수준에서 수집하는 정보(예: 설치 통계, 안정성 지표)는 개발자 앱의 "수집"과 별개로 Google 정책에 따라 처리됩니다 — 양식 안내가 이를 어떻게 구분하는지 확인(§0 #7).
- **DLC/유료 콘텐츠를 Android 에서 판매**하려면 Play 결제 연동이 필요할 수 있습니다(확인 필요). 현재 구현에는 결제 모듈이 없으며 도입하면 데이터 보안 양식과 처리방침 변경 대상입니다.

## 5. 가격 · DLC 가이드 (`docs/04` §3 과 일관)

- **모델: 본편 매입형(Premium), 가챠·랜덤 확률형·무한 소비·시간 압박 과금 없음.** 타이쿤 루프에 결제 압박을 넣지 않습니다(`docs/04` §3 "윤리").
- 가격대(제안, `docs/04`): **₩16,000~22,000**. 시뮬레이션상 플레이타임 약 13시간(`docs/03`), 엔딩 21종, 난이도 3단계, 공동 루트가 가치 포인트. 최종 가격은 완성된 아트·오디오 분량을 보고 결정 [TODO]. 지역별 가격은 Steam 지역 가격 도구 활용(§0 #4).
- 출시 할인: 소폭(예: 10~20%) 단기 출시 할인은 일반적이나 규정(빈도·기간·가격 인하 제한)은 §0 #4 확인.
- **DLC(콘텐츠 가치 중심):** 후일담 챕터, 캐릭터별 에필로그, 의상 팩 — 해금 서비스에 `extra_epilogues` 규칙은 있으나 실제 콘텐츠 팩은 아직 없음(`docs/12` §6-6). DLC 도입 시: Steam DLC 별도 App ID/디포, 세이브 호환(§7), 본편 구매자 가격 정책, **본편을 불완전하게 잘라 파는 구성 금지**(엔딩 21종은 본편 포함).
- 디럭스 에디션: 아트북 PDF/OST/설정집(`docs/04` §3). 음악·아트의 상업 배포 권리를 `music_sfx_log.csv`/`art_log.csv` 에서 확인한 뒤 판매.
- 후원 티어(크레딧 등재 등)는 약속한 혜택을 이행 가능한 범위로, 환불/지연 정책을 명시. 외부 후원 플랫폼 이용 약관·세금은 별도 확인.
- 수수료·세금: 스토어 수수료율은 변동 가능(§0 #4, itch 는 판매자가 비율을 설정할 수 있는 것으로 알려져 있음 — 확인). 한국 거주 개인/사업자 세무·통신판매업 신고는 §0 #10 및 세무 전문가 확인.
- 영어 현지화: 해외 시장 우선 과제(`docs/04` §3). 제목 영문표기 확정 후 상표 점검 재수행.

## 6. 출시 타임라인 체크리스트 (T = 출시일, 일수는 *목표치이며 정책 대기 기간은 §0 확인 후 조정*)

| 시점 | 항목 |
|---|---|
| T-16주~ | 상표/이름 점검 시작(`AI_AND_RIGHTS_CHECKLIST` §5), Steamworks/Play 계정·세무·은행·신원 확인 시작(길게 걸릴 수 있음), 개인정보처리방침 법률 검토 의뢰 |
| T-12주 | 데모 범위 확정(§2.4·§2.5.1 열린 항목 해결), 아트·오디오·폰트 권리 로그 작성 시작, 가격 가설 |
| T-10주 | itch 데모 페이지 공개 + butler 채널 검증, 위시리스트/팔로우 유도, 커뮤니티 채널(디스코드/SNS) 개설 |
| T-8주 | Steam 앱 생성, 스토어 페이지 초안, 캡슐/스크린샷 제작, 콘텐츠 설문·AI 공개 초안 작성(§3.3) |
| T-6주 | **Steam "Coming Soon" 공개**(최소 대기 기간 충족 확인), 베타 브랜치로 클로즈드 테스트(`docs/17` §4 A 등급 시작) |
| T-4주 | 콘텐츠 동결(RC 진입, `docs/17` §11), 스토어 페이지 심사 제출, Android 비공개 테스트 시작(필요 시) |
| T-3주 | 빌드 심사 제출, 회귀 전체(`docs/17` §5), 성능 측정, 접근성 확인 |
| T-2주 | 심사 피드백 대응, 게이트 표 20항 점검, 해시·릴리스 노트 초안, 롤백 계획 점검 |
| T-1주 | Gold 후보 확정, 최종 `release_check` + Preflight + 클린 설치 스모크, 소스 보관(`package_source.sh`), 키스토어 백업 확인 |
| T-0 | 출시 버튼, `BUILD_INFO.txt`/해시 게시, 공지, 모니터링(이메일/커뮤니티) |
| T+1~7일 | 크래시/세이브 관련 버그 우선 처리, 핫픽스 판단(§7), 리뷰/문의 응답 |
| T+4~8주 | 첫 콘텐츠 업데이트/DLC 로드맵 공개, 설문 기반 밸런스 조정 |

## 7. 출시 후 패치 절차

**버전 규칙:** `MAJOR.MINOR.PATCH`. PATCH = 세이브 호환 버그 수정, MINOR = 콘텐츠/기능 추가(세이브 호환 유지), MAJOR = 세이브 구조 변경 등(마이그레이션 필수). Android 는 매 업로드 `Bundle Version Code` 증가.

1. **이슈 수집:** 이메일/커뮤니티/스토어 리뷰(게임은 텔레메트리가 없으므로 신고에 의존) → `docs/17` §10 템플릿으로 정리, 심각도 판정.
2. **핫픽스 기준:** Blocker(진행 불가·세이브 손상)·Critical 은 즉시 패치, Major 는 다음 정기 패치, Minor 는 모아서.
3. **수정 & 회귀:** 수정 범위만큼 `docs/17` §5.4 수행. 세이브·시나리오·밸런스를 건드렸다면 해당 절 전체. `release_check` PASS 필수.
4. **버전 올리기 → 빌드 → BUILD_INFO/해시 생성**(§1).
5. **배포:** Steam: `beta` 브랜치에 업로드 → 검증 → `default` 로 승격(이전 빌드 ID 기록, 롤백 = 이전 빌드 지정). itch: `butler push`(채널별), 이전 zip 보관. Play: 내부 → 단계적 출시, 문제가 있으면 출시 중단/이전 버전 복귀 절차 확인.
6. **세이브 호환 확인:** 이전 버전 세이브 픽스처로 로드(`docs/17` §7).
7. **공지:** 패치 노트(수정 내용, 알려진 문제, 해시), 데이터 처리가 바뀌면 개인정보처리방침/Play 데이터 보안 양식/SBOM 갱신.
8. **사후:** 신규 크래시 보고 모니터링 24~72시간, `docs/17` §11.3 예외 기록 갱신, 다음 마이너 업데이트 계획.

## 8. 요약: 이 문서가 못 하는 것
- 어떤 스토어 정책도 보증하지 않음(§0). 특히 **AI 공개 문항, 성적 콘텐츠 분류, 국내 등급분류/사업자 의무, Play 계정 요건, 수수료**는 직접 확인.
- 빌드 메뉴·butler·steamcmd·Play 업로드를 실제로 실행해 보지 못했다.
- 법률·세무 자문이 아님.
