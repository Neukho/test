# 증거 색인 (Evidence Index)

> 각 주장 → 근거 파일 → 재현 명령 → **제가 2026-10-03 에 직접 실행해서 관찰한 결과**.
> 모든 명령은 저장소 루트(`MorpheHouse/`)에서 실행합니다. 필요: `mono-mcs`, `mono-runtime`, `python3` (Unity 불필요).
> 코드가 계속 바뀌므로 숫자가 달라질 수 있습니다 — 달라졌다면 **이 표의 값이 아니라 새로 실행한 값을 쓰세요.**
> 표의 "상태": ✅ 직접 실행·확인 / 📄 코드·문서 확인(실행 안 함) / ⏸ 미실행(Unity·네트워크 필요) / ❌ 존재하지 않음.

## A. 규모 주장

| # | 주장 | 근거 | 재현 | 관찰 결과 | 상태 |
|---|---|---|---|---|---|
| A1 | C# 소스 규모 | `UnityProject/Assets/_Project/Scripts/**/*.cs` | `find UnityProject/Assets/_Project/Scripts -name '*.cs' \| wc -l` / `… \| xargs cat \| wc -l` | 102 파일 / 16,111 줄 (런타임 15,314 + Editor 797) | ✅ |
| A2 | NUnit 테스트 코드 존재(실행은 못 함) | `UnityProject/Assets/_Project/Tests/EditMode/*.cs` | `grep -c "\[Test\]" UnityProject/Assets/_Project/Tests/EditMode/*.cs` | StageOne 6 · StageTwo 9 · StageThree 9 = 24 | ✅(개수) / ⏸(실행) |
| A3 | 시나리오 규모 | `UnityProject/Assets/_Project/Dialogue/**/*.yarn` | `find UnityProject/Assets/_Project/Dialogue -name '*.yarn' \| wc -l`, `… \| xargs cat \| wc -l`, `grep -rh '^title:' … \| wc -l` | 87 파일 / 9,085 줄 / 178 노드 | ✅ |
| A4 | 시나리오 분량(대사·선택지) | `tools/csharp_check/scenario_check/` | `sh tools/csharp_check/scenario_check/run.sh \| grep "합계"` | 합계 행: 노드 178 · 대사·지문 4,562 · 선택지 544 · 어절 56,184 · 글자 156,210 | ✅ |
| A5 | 하네스 검사 합계 958 | 아래 B1~B7 | 각 `run.sh` 실행 | 16+46+302+140+186+169+99 = **958**, 실패 0 | ✅ |

## B. 순수 로직 하네스 (Unity 불필요)

| # | 주장 | 근거 | 재현 | 관찰 결과 | 상태 |
|---|---|---|---|---|---|
| B1 | 경영 시뮬레이터·세이브 왕복·변조 탐지·`.bak` 복구·잘못된 데이터 저장 거부 | `tools/csharp_check/Program.cs`(`SaveTests`) | `sh tools/csharp_check/run.sh` | `PASS` 16줄 + `ALL OK` — 포함: `save roundtrip`, `tamper detected (no backup)`, `restored from backup`, `invalid data refused`, `non-morning save refused`, `missing slot` | ✅ |
| B2 | 연애 규칙·몬테카를로 패리티 | `tools/csharp_check/romance_check/` | `sh tools/csharp_check/romance_check/run.sh` | `[PASS]` 46줄 + `ALL OK` | ✅ |
| B3 | 대화 엔진 파서/실행/린트/스트레스 | `tools/csharp_check/dialogue_check/` | `sh tools/csharp_check/dialogue_check/run.sh \| tail -3` | `checks=302 failures=0` / `ALL PASSED` | ✅ |
| B4 | 엔딩·공동 루트·저장 호환 | `tools/csharp_check/ending_check/` | `sh tools/csharp_check/ending_check/run.sh \| tail -3` | `checks=140 failures=0` / `ALL PASSED` | ✅ |
| B5 | 챕터 흐름·프로필·루트 선택 | `tools/csharp_check/flow_check/` | `sh tools/csharp_check/flow_check/run.sh \| tail -2` | `flow_check: 186 checks, 0 failures` | ✅ |
| B6 | 프로필 검증·설정·시나리오 전수 실행 | `tools/csharp_check/ui_logic_check/` | `sh tools/csharp_check/ui_logic_check/run.sh \| tail -3` | `checks=169 failures=0` / `ALL PASSED` | ✅ |
| B7 | 전체 `.yarn` 린트·도달성·대량 실행 | `tools/csharp_check/scenario_check/` | `sh tools/csharp_check/scenario_check/run.sh \| tail -3` (약 2분) | `checks=99 failures=0`; 줄 536: `실행 오류 0건 (실행 1595664회, 줄 32306640)` | ✅ |
| B8 | 빠른 릴리스 점검 | `tools/release_check.sh` | `sh tools/release_check.sh --fast` | PASS 15 / WARN 1(에디터 스크립트 타입 검사는 Unity 필요) / SKIP 2 / FAIL 0, `RELEASE CHECK: PASS`, 약 7초 | ✅ |
| B9 | Unity API 컴파일 검사(5개 프로젝트) | `tools/unity_compile_check/run.sh`, `docs/17_Test_Plan.md` | `sh tools/unity_compile_check/run.sh` (dotnet 8·인터넷 필요) | **제가 재실행하지 않음.** `docs/17_Test_Plan.md` 가 실행했다고 기록 | ⏸ |

## C. 보안·개인정보 주장

| # | 주장 | 근거 | 재현 | 관찰 결과 | 상태 |
|---|---|---|---|---|---|
| C1 | HMAC-SHA256 서명 + 상수 시간 비교 구현, 한계 문서화 | `Scripts/Save/SaveIntegrity.cs`(클래스 주석에 "키가 클라이언트에 존재 … 우발적 손상·단순 편집 탐지") | 파일 열람 | 구현 확인. `KeyMaterial` 상수가 소스에 하드코딩 | 📄 |
| C2 | 서명이 맞지 않으면 `Tampered`; `.bak` 이 있으면 복구하되 `RestoredFromBackup` + `Detail="primary: Tampered"` + `PrimaryRejected` 로 호출자에 통지 | `Scripts/Save/SaveService.cs` `Load`/`TryParse` | B1 의 `tamper detected`, `restored from backup`; `sh tools/csharp_check/qa_hunt/run.sh save`(QA-06 항목) | PASS | ✅ |
| C2b | 서명은 버전별 정규 페이로드(직렬화기 독립). 필드 단위 변조 전부 거부, 알 수 없는 추가 필드는 허용, 이전 스키마 v1 3변형(1·2·3단계)은 Ok 로 로드되어 v2 로 마이그레이션, 모든 `SaveData` 필드는 서명/제외 목록에 있음 | `Scripts/Save/SaveIntegrity.cs`(`CanonicalPayload`, `SaveMigrations`), `SaveService.VerifyLegacyV1` | `sh tools/csharp_check/qa_hunt/run.sh save`(23 checks), `sh tools/security_selftest/run.sh \| grep save.signing` | 23/23 PASS; `save.signing` 9 assertions, 실패 0 | ✅ |
| C3 | 변조·손상·잘림·초장문·깊은 중첩 세이브 입력에 예외/hang 없음, 서명 안 맞는 변조 수락 0 | `tools/security_selftest/Program.cs` `CampaignSaveMutation` | `sh tools/security_selftest/run.sh \| grep "save.mutation"` | JSON 대역: `RestoredFromBackup=1443 Tampered=315 Corrupt=1149 Ok=93`, XML 대역: `RestoredFromBackup=1511 Ok=57 Corrupt=1278 Tampered=154`, `unexpected-exceptions=0 hangs=0` | ✅ |
| C4 | 서명을 맞춘 범위 밖 위조 파일을 검증기가 거부 | `Scripts/Save/SaveValidator.cs`, `CampaignSaveForged` | `… \| grep "forged_signed"` | JSON 2,241건 + XML 2,651건 거부, 수락 0 (`NaN`/`Infinity` 는 XML 대역) | ✅ |
| C5 | 검증기의 공백(서명된 위조 수락) | `CampaignSaveForged` 의 `GapProbes` | `… \| grep "ACCEPTED"` | 39개 프로브 중 38개 수락(나머지 1개는 XML 직렬화기가 거부)(예: `SatisfactionBonus`, `PlayTimeSec`, 캐릭터 ID, 이름 문자) | ✅ |
| C6 | 저장/불러오기 왕복과 잘못된 데이터의 저장 거부 | `CampaignSaveRoundtripAndRefuse` | `… \| grep "roundtrip_refuse"` | 각 대역 `saved+reloaded=1454 invalid-refused=1546` | ✅ |
| C7 | 저장소가 외부 파일을 읽거나 덮어쓰지 않음 | `FileSaveStorage.PathOf`, `CampaignStoragePaths` | `… \| grep -A8 "storage.path_traversal"` | 3,031개 이름: `read-leaks=0`, 기존 외부 파일 변경 0. **그러나 폴더 바깥에 새 `.tmp` 생성 213건(F-1)** | ✅ |
| C8 | `.yarn` 파서가 비신뢰 입력에서 문서화된 예외만 던짐 | `YarnParser.cs`, `CampaignYarnMutation` | `… \| grep "yarn.mutation"` | `parse ok=1526 DialogueParseException=1474 \| run ok=1382 documented-runtime-errors=1562 \| hangs=0` | ✅ |
| C9 | 파서 결함 발견(숫자 오버플로·재귀 깊이·점프 순환·이차 시간) | `SELFTEST_RESULTS.md` §3 | `… \| grep -E "OverflowException\|StackOverflow\|HANG"` | F-2~F-5 재현됨 | ✅ |
| C10 | `MiniJson`/프로필/설정 로더가 예외 없이 폴백, 한도·범위 준수 | `MiniJson.cs`, `ProfileService.cs`, `Settings.cs` | `… \| grep -E "minijson\|profile.load\|settings.load"` | MiniJson `parsed=586 FormatException=2414`; 프로필 `Loaded=300 Corrupt=2696`; 설정 예외 0 | ✅ |
| C11 | 이름 검증과 저장 검증의 불일치 | `Data/ProtagonistProfile.cs`, `Save/SaveValidator.cs` | `… \| grep "profile.name_vs_save"` | 수락 350 중 19건이 저장 검증에서 거부(F-6) | ✅ |
| C12 | 네트워크·분석·광고 코드 없음 | `UnityProject/Assets/**/*.cs` | `grep -rn "UnityWebRequest\|HttpClient\|WebClient\|System\.Net\|Analytics\|Advertisement\|Firebase" UnityProject/Assets --include=*.cs \| wc -l` | **0** (이 작성 시점; 이후 코드 추가 시 재확인) | ✅ |
| C13 | 빌드 산출물의 실제 권한/네트워크 동작 | 최종 빌드 필요 | `aapt dump permissions app.aab`(Android) 등 | 빌드가 없어 **확인 불가** | ⏸ |
| C14 | 저장소에 키스토어·인증서·비밀 파일 없음 | `tools/release_check.sh` `step_secrets` | `find . \( -name '*.keystore' -o -name '*.jks' -o -name '*.pfx' -o -name '*.p12' -o -name '.env' \) \| wc -l` | **0**; `release_check.sh --fast` 의 `secret_scan PASS`(휴리스틱) | ✅ |
| C15 | 개인정보 처리방침 초안 | `docs/legal/PRIVACY_POLICY_DRAFT.md` | 파일 열람 | 존재. "무수집", 로컬 저장 항목 표, `[TODO]` 다수 — **법률 검토 전 초안** | 📄 |
| C16 | SBOM 생성·최신성 검사 | `tools/generate_sbom.py`, `docs/legal/SBOM.cdx.json` | `python3 tools/generate_sbom.py --check` | `SBOM OK: 10 components (4 with unspecified version), up to date` | ✅ |
| C17 | 서드파티 고지 초안 | `docs/legal/THIRD_PARTY_NOTICES.md` | 파일 열람 | 존재(초안, `[TODO]` 다수) | 📄 |
| C18 | 소스 아카이브 해시 | `tools/package_source.sh` | `sh tools/package_source.sh`(→ `dist/` 생성) | **제가 실행하지 않음**(저장소에 `dist/` 산출물을 만들지 않기 위해). 스크립트 열람만 | 📄 |
| C19 | 빌드 후 해시 목록(`BUILD_INFO.txt`) | `Scripts/Editor/BuildTools.cs` | Unity 에디터 메뉴 | **Unity 에서 실행하지 못함** | ⏸ |
| C20 | AI 에셋 출처 기록 체계 | `docs/02_Art_Plan.md` §8, `docs/legal/AI_AND_RIGHTS_CHECKLIST.md`, `docs/legal/art_log.csv` | `grep -vc '^#' docs/legal/art_log.csv` | 헤더 1줄 외 데이터 행 0 — **양식만 있고 기록은 없음** | ✅ |
| C21 | 코드 서명 | — | — | **없음** (계획 단계, `01_TechDesign.md` §11) | ❌ |
| C22 | 지속적 퍼징/CI | — | — | **없음**(자가 테스트는 수동 1회성 실행) | ❌ |

## D. 문서 위치

| 내용 | 파일 |
|---|---|
| 보안·개인정보 설계 의도(원 설계) | `docs/01_TechDesign.md` §11 |
| 저장 정책 | `docs/01_TechDesign.md` §6.3 |
| 대화 문법·오류 계약 | `docs/07_Dialogue_Spec.md` |
| 세이브 서명 호환(LockedGroup) | `docs/11_Ending_Logic.md` |
| 테스트 계획과 정직한 한계 | `docs/17_Test_Plan.md` |
| 수위·연령 정책 | `docs/04_Update_v0.2.md` §4 |
| AI 도구/권리 점검표 | `docs/02_Art_Plan.md` §8, `docs/legal/AI_AND_RIGHTS_CHECKLIST.md` |
| 위협 모델 · 결함 · 로드맵 | `portfolio/SECURITY_DESIGN.md`, `portfolio/SELFTEST_RESULTS.md` |

## E. 일부러 하지 않은 주장

- "보안 인증/심사를 받았다", "실제 사용자·다운로드·침해 사고가 있다", "취약점을 발견해 제보했다(CVE 등)" — 해당 사실 없음.
- "세이브 변조를 막는다" — 키가 클라이언트에 있어 **막지 못합니다**(탐지 용도, 우발적 손상·단순 편집 한정).
- "Unity 에서 정상 동작을 확인했다", "NUnit 24개가 통과했다" — 실행하지 못했습니다.
- "개인정보 처리방침이 법적으로 유효하다", "PIPA 를 준수한다" — 초안이며 법률 검토를 받지 않았습니다.
- "공급망 보안 완비" — SBOM 은 초안이고 패키지 버전이 고정되어 있지 않습니다.


## F. 수정 후 재검증 (2026-10-03)

| # | 주장 | 재현 | 관찰 결과 | 상태 |
|---|---|---|---|---|
| F1 | 발견 8건 중 7건 수정(F-7 보류), 회귀 단언 추가 | `sh tools/security_selftest/run.sh; echo $?` | `SELFTEST: assertions all passed`, 종료 코드 0, 35,089 반복/케이스, `regression.fixes iterations=43 assertion_failures=0` | ✅ |
| F2 | 수정이 기존 하네스를 깨지 않음 | `sh tools/release_check.sh --skip-unity` | PASS 16 / WARN 1 / SKIP 1 / FAIL 0, 하네스 958 검사 전부 PASS(`scenario_check` 99 포함) | ✅ |
| F3 | Unity API 컴파일 | `WORK=/tmp/unitycheck_repro sh tools/unity_compile_check/run.sh` | `UNITY COMPILE CHECK: ALL OK` | ✅ |
| F4 | 검증기 공백(F-7)은 아직 열려 있음 | `sh tools/security_selftest/run.sh \| grep "ACCEPTED"` | 39개 프로브 중 38개 수용 | ✅ |

## G. 2차 수정 후 재검증 — F-7 + 리뷰 항목 (2026-10-03)

| # | 주장 | 재현 | 관찰 결과 | 상태 |
|---|---|---|---|---|
| G1 | 자가 테스트 발견 8건 모두 수정, 서명된 위조 프로브 수용 0 | `sh tools/security_selftest/run.sh; echo $?` | `SELFTEST: assertions all passed`, 종료 코드 0, 35,154 반복/케이스; `save.validator_gaps iterations=69 assertion_failures=0`(ACCEPTED 0) | ✅ |
| G2 | 리뷰 N-1~5·7·8·10 회귀 단언 | 위 명령의 `regression.review iterations=35 assertion_failures=0` | 통과(N-1 은 소스 정적 확인) | ✅ |
| G3 | 기존 하네스 무결 | `sh tools/release_check.sh --skip-unity` | PASS 16 / WARN 1 / SKIP 1 / FAIL 0, 958 검사 PASS | ✅ |
| G4 | 컴파일 검사(고정 커밋·해시) | `sh tools/unity_compile_check/run.sh`, `WORK=/tmp/unitycheck_repro sh tools/unity_compile_check/run.sh` | 둘 다 `UNITY COMPILE CHECK: ALL OK` | ✅ |
| G5 | 미해결 | `docs/19_Security_Review.md` §9 | N-6(부분)·N-9·N-11·N-12 | ✅(문서) |
