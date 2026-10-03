# 달빛 모퉁이의 수인들 (Moon Corner House) — 작업본

항구 도시 모레나의 낡은 카페 겸 바 **‘달빛 모퉁이’** 를 물려받아 되살리는 **수인 타이쿤 + 연애 시뮬레이션(비주얼 노벨)** 입니다. Unity 6 / C#, PC(Windows) 우선, 이후 Android.

> **상태: 개발 중인 작업본(프로토타입 이후 단계).** 아직 Unity 에디터에서 실행·빌드해 보지 못했습니다(아래 “검증 범위” 참고).

## ⚠ 꼭 읽어 주세요

### AI 사용 공개
- **코드, 시나리오(대본), 기획·기술·보안 문서는 AI(Claude Code, 여러 에이전트)가 작성했습니다.** 저장소 소유자는 요구사항을 정하고 방향을 결정·승인했습니다.
- **일러스트는 포함되어 있지 않습니다.** 모든 그림은 코드가 생성하는 단색 플레이스홀더이며, 소유자가 직접 제작해 같은 파일명으로 덮어쓸 예정입니다.
- 스토어 공개 시 AI 사용 설문 답변 초안은 `docs/18_Release_Guide.md` §3.3 에 있습니다.

### 대상 연령
- 수위는 **전연령 ~ 성인 로맨스(암시)** 입니다. 노골적인 성적 묘사와 노출 일러스트는 없고, 장면은 키스·포옹·페이드 아웃까지입니다. 모든 인물은 성인입니다.
- 서로 알고 동의하는 **다자(공동) 연애 엔딩**이 포함됩니다(`docs/10_Route_Structure.md` §3 가드레일).
- 성인 취향의 주제를 다루므로 **청소년 대상으로 배포하지 않는 것**을 권장합니다.

### 라이선스
- 아직 정하지 않았습니다(LICENSE 파일 없음 = 모든 권리 보유). 이용·재배포 전에 소유자에게 문의하세요.

## 구성
```
UnityProject/Assets/_Project/   Unity 에셋(코드·대본·밸런스 CSV·플레이스홀더 생성기). 새 Unity 프로젝트의 Assets/ 안에 복사해서 사용
balance/                        밸런스 CSV 원본(난이도 3단계, 이벤트 일정, 장면 규칙 등)
docs/                           기획서·기술 설계서·시나리오 설정집·일러스트 요청서·테스트/릴리스 가이드
tools/                          시뮬레이터, 검증 하네스(Mono), Unity API 컴파일 검사, 릴리스 점검
portfolio/                      보안·개인정보 설계 문서와 자체 퍼징 결과(정보보호 직무 포트폴리오용)
```

## 사용법 (Unity)
1. Unity 6 LTS 설치 → **Universal 2D** 템플릿으로 새 프로젝트 생성 → Package Manager 에서 **Input System** 설치
2. 이 저장소의 `UnityProject/Assets/_Project` 를 새 프로젝트의 `Assets/` 안으로 복사
3. 메뉴 `MorpheHouse → Setup → Apply Recommended Project Settings`(재시작) → `MorpheHouse → Setup → Create Boot Scene` → ▶ Play
4. 자세한 순서와 확인 목록: `docs/05_Stage1_Guide.md`, `docs/09_Stage2_Guide.md`, `docs/12_Stage3_Guide.md`, `docs/16_Stage5_Guide.md`

대본(`Dialogue/**/*.yarn`)이나 밸런스(`balance/*.csv`)를 고친 뒤에는 `python3 tools/sync_dialogue.py`, `python3 tools/sync_balance.py` 로 `Resources/` 복사본을 갱신합니다(Unity 메뉴로도 가능).

## 검증 범위 (정직하게)
`sh tools/release_check.sh` 한 번으로 아래를 모두 돌립니다(결과는 `docs/17_Test_Plan.md`).

| 검증된 것 | 방법 |
|---|---|
| 게임 로직(하루 사이클, 호감·신뢰, 이벤트, 엔딩, 저장) | Mono 하네스 수천 개 검사 + 실제 대본으로 64일을 플레이하는 QA 봇 |
| 시나리오 약 5424줄 | 린트, 4개 성별 × 모든 선택 경로 × 루트 잠금 42가지 실행(오류 0), 주인공 성별 중립 검사 |
| Unity 의존 코드 컴파일 가능성 | 실제 UnityEngine·uGUI·TextMeshPro·Input System 소스를 상대로 컴파일 |
| 보안 | 자체 퍼징 3.5만 건(결함 8건 발견·수정), 수동 보안 리뷰(`docs/19_Security_Review.md`) |

| **검증하지 못한 것** | |
|---|---|
| Unity 에디터에서의 실제 실행 | 화면 배치, 장면 전환, 입력, 오디오, 폰트 표시 |
| 에디터 전용 스크립트(씬 생성·빌드) | UnityEditor 가 없어 컴파일 불가 |
| 빌드(IL2CPP/APK/AAB), 서명, 스토어 업로드, 실기기 | 미실행 |
| 영어 | 대본은 한국어뿐(UI 영어는 일부 초안) |

## 주요 문서
- 기획: `docs/00_GDD.md`, `docs/10_Route_Structure.md`(개별 루트·다자 엔딩), `docs/04_Update_v0.2.md`
- 기술: `docs/01_TechDesign.md`, `docs/07_Dialogue_Spec.md`, `docs/11_Ending_Logic.md`, `docs/21b_Scene_Gating.md`
- 밸런스: `docs/03_balance_report.md`
- 일러스트 제작 요청서: `docs/13_Art_Checklist.md`(필수 160개), `docs/art_requests/`
- 품질·보안: `docs/17_Test_Plan.md`, `docs/19_Security_Review.md`, `docs/20_QA_Bug_Report.md`
- 배포: `docs/18_Release_Guide.md`, `docs/legal/`(초안, 법률 검토 필요)
- 포트폴리오: `portfolio/README_PORTFOLIO.md`
