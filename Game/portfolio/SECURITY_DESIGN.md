# 보안·개인정보 설계 문서 (Security & Privacy Design)

> 대상: 달빛 모퉁이의 수인들 / Moon Corner House (Unity 6 · 싱글 플레이 · 오프라인 · 로컬 저장)
> 문서 성격: **게임 프로젝트의 설계 문서**입니다. 상용 보안 제품의 인증·감사 문서가 아니며, 아래 "통제"는 이 저장소에서 실제로 구현·시험된 것과 **계획만 있는 것**을 구분해 적었습니다.
> 작성 기준일: 2026-10-03. 수치는 `portfolio/EVIDENCE_INDEX.md` 의 명령으로 재현할 수 있습니다.
> English summary: A short threat-model-driven design note for a single-player, offline, local-save game. It lists assets, trust boundaries, a STRIDE table per surface (save files, content scripts, profile/settings, build/distribution, third-party code, AI-asset provenance, privacy), where each control lives in the repo, how it is tested, and the residual risk. Section 10 lists limitations honestly (client-side HMAC key, no code signing, no continuous fuzzing, etc.).

---

## 1. 범위와 전제

| 항목 | 내용 |
|---|---|
| 제품 | PC(Windows 우선) → Android 의 싱글 플레이 게임. **네트워크 코드 없음**(아래 §8.1 에서 grep 으로 확인 방법 제시) |
| 사용자 | 게임을 플레이하는 한 명(로컬). 계정·서버·다른 사용자와의 상호작용 없음 |
| 공격자 모델 | (A) 자신의 세이브를 고치려는 **플레이어**, (B) 공유/동기화된 폴더나 인터넷에서 받은 **변조·손상 파일**, (C) 실수로 깨진 파일(전원 차단·디스크 오류), (D) 제작 중 **잘못된 시나리오/에셋**(작성 실수), (E) 빌드·배포물 바꿔치기·의존성 오염(공급망) |
| 범위 밖 | 서버·결제·멀티플레이 보안, 디바이스 루팅/메모리 조작을 막는 치트 방지(DRM), 난독화 우회 방지, 법률 자문 |

## 2. 자산(Assets)

| ID | 자산 | 기밀성 | 무결성 | 가용성 | 비고 |
|---|---|---|---|---|---|
| A1 | 세이브 파일 `saves/slot{0..3}.json` + `.bak` | 낮음~중간(주인공 이름 포함) | 중간(진행 손실 방지) | **높음**(플레이 진행) | 소스: `Scripts/Save/*` |
| A2 | 프로필 `profile.json`(도달 엔딩·본 CG·해금) | 낮음 | 낮음 | 낮음 | 깨져도 빈 프로필로 시작 |
| A3 | 설정 `settings.json`(텍스트 속도·음량·언어) | 낮음 | 낮음 | 낮음 | |
| A4 | 콘텐츠 스크립트 `.yarn`(87개 파일, 번들) | 낮음(공개 콘텐츠) | 중간 | 중간 | 번들 리소스. 외부에서 로드하지 않음 |
| A5 | 플레이어가 입력한 이름·성별 선택·종족 선택 | **개인정보로 볼 수 있는 입력값** | 낮음 | — | 로컬에만 저장, 전송 없음 |
| A6 | 빌드 산출물·소스 아카이브 | — | **높음**(바꿔치기 방지) | 중간 | 해시: `tools/package_source.sh`, `BuildTools.cs` |
| A7 | 서명 키·키스토어·API 키(향후) | **높음** | 높음 | — | 현재 저장소에 없음(점검 §7) |
| A8 | 에셋 출처 기록(AI 생성 이력·라이선스) | 낮음 | **높음**(권리 주장 근거) | — | `docs/legal/*.csv`(양식만 존재) |

## 3. 신뢰 경계(Trust boundaries)와 데이터 흐름

```mermaid
flowchart LR
  P[플레이어 입력\n이름/성별/종족/선택지] -->|TB1: 입력 검증\nProfileValidator| G[게임 로직\nCore/Data/Tycoon/Romance]
  Y[(.yarn 번들\nResources)] -->|TB2: 파서=비신뢰 입력 취급\nYarnParser + DialogueLint| D[Dialogue 엔진\nDialogueRunner]
  D --> G
  G -->|SaveMapper.ToSave\nSaveValidator.Validate| S[SaveService\nHMAC-SHA256 서명]
  S -->|TB3: 파일 시스템 경계\nFileSaveStorage (파일명만 허용)| F[(persistentDataPath\nsaves/ profile.json settings.json)]
  F -->|Load: 파싱→서명 검증→범위 검증| S
  S --> G
  B[빌드 파이프라인\nUnity + tools/*] -->|TB4: 해시·SBOM·(서명 계획)| R[배포물]
```

| 경계 | 넘어오는 데이터 | 신뢰 가정 |
|---|---|---|
| TB1 플레이어 → 게임 | 이름 문자열, 선택지 인덱스 | **비신뢰**: 길이·문자·범위 검증 |
| TB2 콘텐츠 → 엔진 | `.yarn` 텍스트 | 저자는 신뢰하지만 **실수는 일어난다** → 파서를 비신뢰 입력 파서처럼 방어(줄 번호 오류, 린트) |
| TB3 파일 시스템 → 게임 | 세이브·프로필·설정 파일 바이트 | **비신뢰**: 손상·변조·초대형·악성 JSON 가능 |
| TB4 개발환경 → 사용자 | 빌드·설치 파일 | 사용자는 출처를 해시/서명으로 확인할 수 있어야 함(일부만 구현) |

## 4. 위협 모델 (STRIDE)

심각도는 "이 게임의 위협 모델에서" 상대적으로 매긴 값입니다(실제 서비스 기준 아님). 상태: **구현·시험됨 / 구현됨(시험은 제한적) / 계획**.

### 4.1 세이브 파일 (A1) — `Scripts/Save/SaveService.cs`, `SaveValidator.cs`, `SaveIntegrity.cs`, `SaveMapper.cs`

| STRIDE | 위협 | 통제 (구현 위치) | 시험 방법 | 상태 | 잔여 위험 |
|---|---|---|---|---|---|
| **T**ampering | 수치 편집(골드·호감도) | HMAC-SHA256 서명(`SaveIntegrity.Sign` = HMAC(버전별 정규 페이로드)) + 상수 시간 비교(`ConstantTimeEquals`), 불일치 → `LoadStatus.Tampered` | `csharp_check/Program.cs` "tamper detected"; 자가 테스트 변이 퍼징 6,000회: **서명이 맞고 내용이 바뀐 파일을 수락한 사례 0건** | 구현·시험 | **키가 클라이언트(소스)에 있어 의지 있는 변조는 막지 못함**(설계상 한계, §10) |
| T | 서명을 맞춘 위조 파일(키 공개 가정) | 범위 검증 `SaveValidator.Validate`(난이도·챕터·일차·골드·평판·피로·호감도·신뢰도·리스트 크기·문자열 길이·`NaN/Infinity` 거부) | 자가 테스트: 서명된 범위 밖 파일 **4,892회 전부 거부**(`Corrupt`, 수락 0) | 구현·시험 | 검증하지 않는 필드가 있음(`SatisfactionBonus`, `PlayTimeSec`, 캐릭터 ID 화이트리스트 등 — SELFTEST_RESULTS.md §4) |
| **R**epudiation | (해당 낮음) 단일 사용자 로컬 | `SavedAtUtc`·`AppVersion` 기록 | — | 구현 | 기록 자체는 서명 범위지만 감사 로그는 아님 |
| **I**nfo disclosure | 세이브에 이름 평문 저장 | 이름은 로컬에만 저장·전송 코드 없음; 이름 최대 12 텍스트 요소(`ProfileValidator`) | 코드 grep(§8.1) | 구현 | OS 수준 접근(같은 PC 사용자)은 막지 않음. 암호화는 하지 않음(의도적: 기밀 데이터 없음) |
| **D**enial of service | 손상·잘림 파일, 초대형 JSON, 깊은 중첩 | 모든 예외를 `LoadStatus.Corrupt` 로 변환, `.bak` 자동 복구(`RestoredFromBackup`, 주 파일이 변조/손상이었다면 `PrimaryRejected`·`Detail` 로 통지), 리스트·문자열 한도 | 자가 테스트: 변이/잘림/널바이트/100k자 이름/50k 깊이 중첩 입력 **6,000회 중 미처리 예외 0, 시간 초과 0** | 구현·시험 | **파일 크기 상한 없음**: 64MB 프로필을 그대로 읽음(약 2.9초). 로컬 단일 사용자에선 낮음 |
| D | 저장 도중 전원 차단 | 임시 파일(`.tmp`) 기록 후 교체 + 직전 파일을 `.bak` 으로 보존(`FileSaveStorage.Write`) | `csharp_check` "restored from backup" | 구현·시험 | 교체가 단일 원자 연산이 아님(Copy→Delete→Move). 중간에 끊기면 `.bak` 로 복구하는 구조 |
| **S**poofing | 다른 슬롯/다른 앱의 파일을 끼워 넣기 | `Slot` 필드 저장(서명 포함) | — | 부분 | `Slot` 값과 파일명 일치 여부는 검증하지 않음(관찰 O-4) |
| **E**levation | **슬롯 이름을 통한 경로 조작(`../`)** | 파일명은 `SlotName(int)` 로만 생성(`slot0..3.json`, 범위 밖 → `ArgumentOutOfRangeException`); 저장소는 추가로 `Path.GetFileName` 만 사용 | 자가 테스트: 슬롯 경계(−1, 4, `int.Min/Max`) 시험; 저장소에 `../x`, 절대 경로, `..\`, 널 문자 등 3,031개 이름 → **외부 파일 읽기/덮어쓰기 0건** | 구현·시험 | **발견된 결함**: 빈 이름·`/` 로 끝나는 이름은 `<저장폴더>.tmp` 를 폴더 **바깥**에 만듦(213/3,031). 앱 경로(정수 슬롯)에서는 도달 불가 → 방어 심층 결함. 패치안은 SELFTEST_RESULTS.md |
| E | 악성 JSON 역직렬화(타입 혼동·다형성) | `JsonUtility`(Unity)는 **알려진 필드만, 고정 타입으로** 채움 — 임의 타입 인스턴스화·콜백 없음(`SaveData` 는 `[Serializable]` 평문 데이터 클래스) | 설계 검토. 하네스는 JsonUtility 를 못 쓰므로 `MiniJson`/`XmlSerializer` 대역으로 시험 | 설계·부분 시험 | **JsonUtility 자체의 파서 동작은 이 저장소에서 시험하지 못함**(Unity 필요) |

### 4.2 콘텐츠 스크립트 `.yarn` (A4) — `Scripts/Dialogue/YarnParser.cs`, `DialogueRunner.cs`, `DialogueExpression.cs`, `TextTemplate.cs`, `DialogueLint.cs`

콘텐츠는 번들되어 신뢰할 수 있지만, 저자의 실수·향후 모드/현지화 파일 도입 가능성을 고려해 **파서를 비신뢰 입력 파서로** 다룹니다.

| STRIDE | 위협 | 통제 | 시험 방법 | 상태 | 잔여 위험 |
|---|---|---|---|---|---|
| T | 잘못된 구문이 조용히 통과 | 줄 번호가 붙은 `DialogueParseException` 으로 모든 구문 오류 보고; 린트(`DialogueLint`: 점프 대상·`#line:` id·도달성) | `dialogue_check`(302 검사 중 ParseErrors·Lint), `scenario_check`(87개 파일 전체 파싱·린트) | 구현·시험 | 린트는 순환 점프를 막지 않음(아래 D) |
| **E** | 스크립트가 임의 코드를 실행 | **표현식 언어가 닫혀 있음**: 내장 함수 9개 + 호스트가 허용한 함수/커맨드만. 리플렉션·`eval`·파일/네트워크 접근 없음. 알 수 없는 함수는 `DialogueRuntimeException` | 코드 리뷰, `dialogue_check` TestRuntimeErrors | 구현·시험 | 호스트 커맨드 구현(`DialogueCommandDispatcher`)이 안전하다는 가정 |
| D | 자원 고갈: 거대한 줄·수십만 줄·초장문 | 선형 시간 파싱 | 자가 테스트: 10만 줄 0.44초, 1MB 대사 줄 0.03초, 5만 보간 0.06초, 1만 노드 0.15초 | 시험 | **#태그 20,000개 한 줄: 약 2.9초(이차 시간)** |
| D | **깊게 중첩된 구조(재귀)** | **통제 없음** | 자가 테스트(별도 프로세스): 괄호 `(((…` 20,000단계, 호출 중첩 20,000단계, `!!!…`·`<<if>>` 100,000단계에서 **StackOverflowException** | 시험(결함 발견) | **미해결**: 깊이 상한 없음. 번들 콘텐츠에서는 도달 불가, 외부 스크립트를 받는다면 필수 수정 |
| D | **무한 점프 루프**(`<<jump a>>` 순환) | **통제 없음** | 자가 테스트: 자기 점프·상호 점프 스크립트에서 `Runner.Next()` 가 반환하지 않음 | 시험(결함 발견) | **미해결**: 스텝 상한·순환 감지 없음(스크립트 린트에서 잡는 정적 검사도 아직 없음) |
| T | 거대한 숫자 리터럴 | — | 자가 테스트: 400자리 숫자 리터럴이 `DialogueParseException` 이 아니라 `OverflowException`(Mono) | 시험(결함 발견) | 문서화된 예외 계약 위반. `double.TryParse` 로 바꾸면 해결 |
| I | 오류 메시지로 내부 정보 노출 | 메시지는 스크립트 파일명·줄 번호·표현식 원문만 포함(스택 트레이스 없음) | 코드 리뷰 | 구현 | 낮음 |
| R | — | 해당 없음 | | | |
| S | 다른 노드 제목과 충돌(중복 노드) | `DialogueProjectLoader` 가 중복 노드를 오류로 보고(먼저 로드된 것 사용) | `ui_logic_check`/`flow_check` 로드 검사 | 구현·시험 | |

### 4.3 프로필·설정 파일 (A2, A3) — `Scripts/Game/ProfileService.cs`, `MiniJson.cs`, `Settings.cs`

| STRIDE | 위협 | 통제 | 시험 방법 | 상태 | 잔여 위험 |
|---|---|---|---|---|---|
| T/D | 변조·손상된 JSON | `MiniJson` 은 **깊이 12 상한**, 문법 오류 → `FormatException` 만 던짐; `ProfileService.Load` 는 예외를 던지지 않고 빈 프로필(`Corrupt`)로 폴백, 항목 수 512·키 길이 64·횟수 9999 상한 | 자가 테스트: MiniJson 변이/깊은 중첩(최대 20만 단계)/2MB 문자열 3,000회 → 허용 예외(`FormatException`)만, 프로필 3,000회·설정 3,000회 → **예외 0, 한도·범위 위반 0**(설정값은 `[10,130]`·`[0,1]` 로 클램프, `NaN` 처리) | 구현·시험 | 서명 없음(의도적: 가치가 낮은 데이터) |
| D | 초대형 파일 | 없음 | 자가 테스트: 64MB 프로필 로드 2.9초 | 관찰 | 상한(예: 1MB) 추가 권장 |
| I | 프로필에 개인정보 | 엔딩 키·CG ID·해금 ID 만 저장(이름 없음) | 코드 리뷰 | 구현 | |
| E | `FileProfileStorage` 경로 | 경로는 코드가 고정 생성(`GameBootstrap`: `persistentDataPath/profile.json`), 사용자 입력이 경로에 들어가지 않음 | 코드 리뷰 | 구현 | |

### 4.4 빌드·배포 (A6, A7) — 일부는 **계획**입니다

| STRIDE | 위협 | 통제 | 상태 | 잔여 위험 |
|---|---|---|---|---|
| T | 배포물 바꿔치기 | 소스 아카이브 SHA-256(`tools/package_source.sh`: 결정적 tar + `.sha256`), 빌드 후 `BUILD_INFO.txt`(버전·UTC·git 커밋·파일별 SHA-256, `Scripts/Editor/BuildTools.cs`) | `package_source.sh` 는 쉘로 실행 가능. **`BuildTools.cs`(UnityEditor)는 Unity 에서 한 번도 실행되지 않음** | 해시는 "같은 파일인지" 만 확인. 해시가 배포 경로와 **다른 경로**로 공개되지 않으면 무의미 |
| T/S | 배포자 신원 | **코드 서명: 아직 없음**(Windows Authenticode/Android 키스토어는 계획, `docs/01_TechDesign.md` §11) | 계획 | SmartScreen 경고·사칭 가능성 |
| E | 비밀 유출 | 루트 `.gitignore`(Unity 생성물 `Library/Temp/Logs/Builds`, `*.apk`·`*.aab`, 키스토어·인증서·`.env*` 패턴), `release_check.sh` 의 비밀 검사(키스토어 확장자 파일, 따옴표 안의 password/key/token 대입 휴리스틱), `package_source.sh` 가 비밀 유사 파일 발견 시 중단 | 구현(휴리스틱) | 정밀 시크릿 스캐너가 아님 |
| I | 빌드 메타데이터 노출 | `BUILD_INFO.txt` 에 사용자 경로 미포함(설계) | 코드 리뷰 | Unity 가 빌드에 넣는 기본 메타(회사명 등) 확인 필요 |

### 4.5 서드파티 의존성·라이선스 (공급망)

| 위협 | 통제 | 상태 | 잔여 위험 |
|---|---|---|---|
| 취약/악성 패키지, 라이선스 위반 | **SBOM**: `tools/generate_sbom.py`(CycloneDX 1.5, 표준 라이브러리만) → `docs/legal/SBOM.cdx.json`; `--check` 로 최신 여부·필수 필드 검사; `docs/legal/THIRD_PARTY_NOTICES.md` | 초안(10 구성요소, **버전 `unspecified` 4개는 사람이 채워야 함**) | 패키지 버전이 `Packages/manifest.json` 으로 고정되지 않음(저장소에 `Packages/`·`ProjectSettings/` 없음). 취약점(CVE) 자동 대조 없음 |
| 외부 자동 다운로드 | `tools/unity_compile_check/run.sh` 는 NuGet·GitHub 에서 **고정되지 않은 브랜치**를 받음(개발용, 배포물 아님) | 문서화(SBOM scope=`excluded`) | 개발 도구 공급망은 미고정 |
| 서드파티 대화 엔진 | Yarn Spinner 미사용, 자체 구현(MiniYarn) → 외부 의존 최소화, 대신 **직접 구현한 파서의 결함 책임**(§4.2) | 설계 | |

### 4.6 AI 생성 에셋의 출처(Provenance) (A8)

| 위협 | 통제 | 상태 | 잔여 위험 |
|---|---|---|---|
| 권리 침해·정책 위반(스토어 AI 공개 의무 등) | 점검표 `docs/02_Art_Plan.md` §8, `docs/legal/AI_AND_RIGHTS_CHECKLIST.md`; 기록 양식 `docs/legal/art_log.csv`(asset_id·date·tool·model·license 근거·prompt·seed·`ai_assisted` …), `font_log.csv`, `music_sfx_log.csv` | **양식·점검표만 있고 기록은 비어 있음**(`art_log.csv` 에 데이터 행 0) | 실제 일러스트가 제작·기록되기 전까지는 "출처 추적 체계가 설계됨" 이상은 주장할 수 없음. 약관·정책은 수시로 바뀌므로 확인 날짜·URL 기록이 필수 |
| 플레이스홀더가 출시 빌드에 섞임 | `BuildPreflight`("Art placeholders" 검사, Unity 에디터 메뉴) | 작성됨, **미실행** | |

### 4.7 개인정보 (A5) — 아래 §8 에서 상세

| STRIDE | 위협 | 통제 | 잔여 위험 |
|---|---|---|---|
| I | 불필요한 수집·전송 | **무수집 설계**: 텔레메트리·계정·광고 SDK 없음, 인터넷 권한 요청 안 함(계획·빌드에서 확인 필요) | 새 기능(클라우드·분석) 추가 시 전제가 깨짐 → §8.5 |
| I | 입력 이름의 오·남용 | 이름 검증(길이 12 텍스트 요소, 제어 문자·`<>{}\` 금지, 공백 정규화) | 조합 문자 이름이 세이브 검증과 불일치(발견 §4.8) |
| D | 이용자가 데이터를 지울 수 없음 | 파일 삭제 또는 앱 삭제로 전부 삭제 가능(`PRIVACY_POLICY_DRAFT.md` §2) | 앱 안의 "데이터 삭제" 버튼은 없음(계획 항목으로 §10 에 기재) |

### 4.8 자가 테스트에서 발견한 결함 요약 (자세한 내용: `SELFTEST_RESULTS.md`)

| ID | 위치 | 요약 | 심각도(이 게임 기준) |
|---|---|---|---|
| F-1 | `Save/SaveService.cs` `FileSaveStorage.PathOf/Write` | 빈 이름/`/` 끝 이름 → 저장 폴더 바깥에 `.tmp` 생성. 앱 경로에서는 도달 불가 | Low (방어 심층) |
| F-2 | `Dialogue/DialogueExpression.cs` 토크나이저 | 400자리 숫자 → `OverflowException`(문서화된 예외가 아님) | Low |
| F-3 | `YarnParser`/`DialogueExpression` | 재귀 깊이 상한 없음 → 깊은 중첩에서 스택 오버플로 | Low(번들 콘텐츠) / 외부 스크립트면 Medium |
| F-4 | `DialogueRunner.Next` | 점프 순환 → 반환하지 않음(스텝 상한 없음) | Low~Medium(가용성) |
| F-5 | `YarnParser.StripTags` | 한 줄에 `#tag` 2만 개 → 2.8초(이차 시간) | Low |
| F-6 | `Data/ProtagonistProfile.cs` ↔ `Save/SaveValidator.cs` | 조합 문자 많은 이름은 이름 검증을 통과하지만 저장 검증(UTF-16 32자)에서 거부 → 플레이어가 저장 불가 | Low~Medium(기능) |
| F-7 | `Save/SaveValidator.cs` | 미검증 필드(수용됨): `SatisfactionBonus`(NaN/Inf/1e30 포함), `PlayTimeSec`, 캐릭터 ID 화이트리스트·중복, 플래그 값/중복, 이름의 제어·방향 문자, `SavedAtUtc`/`AppVersion` 길이 | Low |
| F-8 | `File*Storage.Read` | 파일 크기 상한 없음 | Info |

## 5. 통제 상세 — 세이브 파이프라인

```
Save:  GameState ─(Morning 단계만)→ SaveMapper.ToSave → SaveValidator.Validate(실패시 InvalidOperationException, 파일 안 씀)
       → (현재 파일이 Ok 가 아니면 .corrupt 로 격리) → Version=CurrentVersion(2)
       → ToJson→FromJson(실제로 다시 읽힐 값) → Hash = HMAC-SHA256(CanonicalPayload) → ToJson → FileSaveStorage.Write(.tmp → .bak 보존 → 교체)
Load:  Read(primary) → FromJson(예외→Corrupt; Version 범위 밖→Corrupt)
       → Version>=2: 정규 페이로드 HMAC 재계산 / Version==1: 구형 텍스트 해시 3변형 검증 → 상수시간 비교(불일치→Tampered)
       → SaveMigrations.Migrate(서명 검증 뒤에만) → SaveValidator.Validate(실패→Corrupt) → Ok
       primary 가 Ok 가 아니면 .bak 로 같은 절차 → status=RestoredFromBackup, Detail="primary: Tampered|Corrupt|NotFound",
       LoadResult.PrimaryStatus/PrimaryRejected 로 호출자에 알림(UI 가 "백업에서 복구" 안내), 그것도 실패면 primary 의 사유를 반환
```

설계 포인트(근거 파일):
- **서명은 "버전별 정규 페이로드"에 대해 계산(v2, QA-06 재설계)** — `SaveIntegrity.CanonicalPayload` 가 `SignedFields(version)` 의 **명시적 필드 목록**(이름=값, 고정 순서, 불변 문화권 숫자 `R` 형식, 구분자 이스케이프, 리스트는 개수 포함)으로 문자열을 만든다. 직렬화기(JsonUtility/XML)가 새·누락 필드를 어떻게 찍는지와 무관하므로 **`SaveData` 에 선택 필드를 추가해도 기존 저장의 서명은 유효**하고, 서명 필드 값이 하나라도 바뀌면 `Tampered`(변이 테스트: 필드마다 1회 변조, 전부 거부). 서명 대상 필드를 바꿀 때만 `CurrentVersion` 을 올리고 `SignedFields` 에 새 case 와 `SaveMigrations` 단계를 추가한다. 모든 `SaveData` 필드는 "서명됨" 또는 `ExcludedFields`(= `Hash`) 여야 한다는 리플렉션 테스트가 필드 추가 시 결정을 강제한다. 트레이드오프: 서명 목록에 넣지 않은 선택 필드는 변조 검출 대상이 아니다(검증기 범위 검사로만 방어).
- **하위 호환(v1)**: v1 저장은 "Hash 를 비운 재직렬화 텍스트의 HMAC"로 서명되었다. `SaveService.VerifyLegacyV1` 가 이 저장소 이력의 세 변형을 모두 받는다 — 3단계(LockedGroup 포함), 2단계(LockedGroup 없음), 1단계(PlayerSpecies/PlayerAppearance/VisitedNodes/LockedGroup 없음, 뺀 필드가 기본값일 때만 시도). 검증 후 `SaveMigrations`(v1→v2: 필드 변경 없음, 서명 방식 전환 표시)로 올리고 다음 저장부터 v2 로 서명된다. 고친 v1 파일은 여전히 `Tampered`.
- **변조된 주 파일은 조용히 .bak 로 대체되지 않는다**: 복구는 하되 `Status=RestoredFromBackup`, `Detail="primary: Tampered"`, `PrimaryRejected=true`(진행이 한 번 되돌아갔을 수 있음)로 알린다(UI 문구 연결은 UI 담당: 현재 `TitleScreen.OnContinue` 는 `RestoredFromBackup` 에 `Debug.Log` 만 하므로 `PrimaryRejected` 일 때 `UiText.LoadRestored` 를 화면에 표시하도록 후속 필요).
- **저장은 아침 단계에서만**(`SaveMapper.ToSave`), 잘못된 상태는 저장 전에 거부(`SaveService.Save`)해 **손상 데이터가 디스크에 쓰이지 않게** 함.

## 6. 통제 상세 — 비신뢰 입력 파서로서의 `.yarn` 처리

- 문법: Yarn 호환 부분집합(`docs/07_Dialogue_Spec.md`). 지원하지 않는 구문(`<<once>>`, `<<enum>>`, `<<detour>>` 등)은 **조용히 무시하지 않고 오류**로 던짐(`YarnParser.ParseCommand`).
- 오류 계약: 파서는 `DialogueParseException`(줄 번호 포함), 실행기는 `DialogueRuntimeException`/`InvalidOperationException`/`ArgumentOutOfRangeException` 만 던진다고 문서화. 자가 테스트가 이 계약을 퍼징으로 확인하고 **위반 사례(F-2)를 찾음**.
- 한 파일의 오류가 다른 파일 로드를 막지 않음(`DialogueProjectLoader.Build`).
- 변이 퍼징(고정 시드 20260101, 3,000회): 실제 시나리오 87개 + 합성 1개를 변형 → 파싱 성공 1,526 / `DialogueParseException` 1,474 / **다른 예외 0 / 시간 초과 0**, 파싱된 것은 실행까지 시험(§SELFTEST_RESULTS).

## 7. 비밀정보(Secrets) 관리

- 현재 저장소에는 키스토어·API 키·토큰이 **없음**(`release_check.sh` 의 휴리스틱 검사 + 직접 확인 방법: `portfolio/EVIDENCE_INDEX.md`).
- 단, **HMAC 키 재료 문자열은 소스에 하드코딩**(`SaveIntegrity.KeyMaterial`)되어 있고 이는 의도된 설계입니다(비밀이 아님 — "단순 변조·손상 탐지"용). 이를 "비밀 키"라고 부르지 않도록 문서에 명시.
- 향후 Android 키스토어·Windows 인증서가 생기면: 저장소 밖 보관, CI 시크릿 사용. 루트 `.gitignore` 가 `*.keystore *.jks *.pfx *.p12 *.pem *.key *.p8 keystore*.properties local.properties google-services.json .env*` 를 막고, `release_check.sh`(검사)·`package_source.sh`(아카이브 중단)가 같은 패턴을 쓴다(리뷰 N-8 수정, 2026-10-03).

## 8. 개인정보 보호 설계 (Privacy by design)

### 8.1 무엇을 처리하는가 (데이터 인벤토리)

| 데이터 | 어디에 | 수집·전송 | 보관·삭제 |
|---|---|---|---|
| 주인공 이름(최대 12 텍스트 요소) | `saves/slot*.json`(+`.bak`) | **전송 없음**. 로컬 파일에만 | 파일 삭제/앱 삭제 |
| 성별 선택(`male/female/nonbinary/neutral`), 종족 선택(7종), 외형 프리셋(1~6) | 세이브 | 전송 없음 | 〃 |
| 플레이 진행(챕터·일차·골드·호감도·플래그·방문 노드·플레이 시간) | 세이브 | 전송 없음 | 〃 |
| 엔딩·CG·해금 | `profile.json` | 전송 없음 | 〃 |
| 설정 | `settings.json` | 전송 없음 | 〃 |
| 계정·기기 식별자·IP·위치·연락처·결제 | **없음** | — | — |

"네트워크 호출이 없다"의 확인 방법(재현 가능):
`grep -rn "UnityWebRequest\|HttpClient\|WebClient\|System.Net\|Analytics\|Advertisement\|Firebase" UnityProject/Assets --include=*.cs` → 작성 시점 결과 0건. (서드파티 패키지를 추가하면 이 검사와 SBOM 을 다시 해야 합니다. 빌드 결과물의 실제 권한 목록은 Unity 빌드가 있어야 확인 가능 — **아직 미확인**.)

### 8.2 데이터 최소화·목적 제한
- 이름·성별·종족은 **대사에서의 호칭/대명사 처리**(`PronounResolver`, `AddressBook`)와 외형 표시에만 쓰입니다. 성별 "지정 안 함"(`neutral`)과 논바이너리를 기본 선택지로 제공해 **민감할 수 있는 선택을 강제하지 않음**.
- 분석·광고·충돌 보고 전송 없음 → 개인정보 처리방침 초안(`docs/legal/PRIVACY_POLICY_DRAFT.md`)에 "수집 항목 없음"을 명시(초안, 법률 검토 전).

### 8.3 보관·삭제·이용자 통제
- 보관 기간: 이용자가 삭제할 때까지 기기에만 존재. 개발자는 어떤 사본도 갖지 않음.
- 삭제: 세이브 폴더 삭제 또는 앱 삭제. **인앱 "모든 데이터 삭제" 기능은 아직 없음**(로드맵).
- 스토어 클라우드 백업(Steam Cloud, Android 자동 백업)이 켜지면 저장 파일이 플랫폼으로 동기화될 수 있음 → 현재 사용하지 않는 것으로 문서화, Android 백업 제외 설정은 **검토 항목**(`01_TechDesign.md` §11).

### 8.4 아동·연령
- 콘텐츠 정책: 성인 외형 고정, 암시적 표현만, 노골적 성적 묘사 미지원(`docs/04_Update_v0.2.md` §4, `docs/02_Art_Plan.md` §8.2). 대상 연령은 **미정**(처리방침 초안의 `[TODO]`).
- 개인정보 관점: 계정·수집이 없으므로 "만 14세 미만 아동의 개인정보 수집 시 법정대리인 동의" 문제는 현재 구조에서 발생하지 않는다고 **설계상 판단**하지만, 연령 등급·표시는 배포 플랫폼 절차를 따라야 하며 **실제 적용 여부는 전문가/플랫폼 확인이 필요**합니다.

### 8.5 한국 「개인정보 보호법」(PIPA) 관점 — 고수준 정리 (법률 자문 아님)

> 아래는 학습·설계 점검용 일반 정리입니다. 조문·기준(예: 동의 방식, 국외 이전 요건, 아동 연령 기준, 처리방침 기재 사항, 신고 의무 등)은 **개정될 수 있으므로 반드시 개인정보보호위원회 등 공식 자료와 전문가로 확인**하세요.

현재(오프라인·무수집)의 판단:
- 개발자가 개인정보를 **수집·이용·제공·위탁하지 않으므로** 개인정보처리자로서의 의무(동의 획득, 처리방침의 상세 기재, 안전성 확보 조치 등)가 적용되는 범위가 매우 작거나 없을 것으로 **보이지만**, 이 판단은 구성에 따라 달라집니다(플랫폼 SDK, 빌드 설정 등). 그래서 코드에서의 "무수집"을 확인 가능한 형태(grep·권한 목록)로 남기는 것이 설계 포인트입니다.

다음 기능을 추가하면 달라지는 점(체크리스트):

| 추가 기능 | 새로 생기는 일 | 필요한 설계 변경(예) |
|---|---|---|
| 계정/로그인 | 식별자·인증정보 처리 | 수집 항목·목적·보유기간 고지, 동의, 비밀번호 단방향 해시(직접 구현 금지·검증된 서비스 사용), 접근통제·로그, 파기 절차, 이용자 권리 행사 창구 |
| 클라우드 세이브 | 개인정보(이름·진행)의 서버 저장·전송 | 전송 구간 TLS, 저장 시 암호화, 서버 측 검증(**클라이언트 HMAC 에 의존 금지**), 국외 서버면 국외 이전 고지·동의 요건 검토, 보관·삭제 API, 침해 사고 대응 절차 |
| 분석(애널리틱스)/크래시 리포트 | 기기 식별자·행동 정보 | 옵트인/옵트아웃 UI, 수집 최소화·가명화, 보유 기간, 제3자 제공/위탁 고지, 처리방침 개정 |
| 광고 SDK | 광고 식별자·제3자 제공 | 별도 동의·SDK 별 데이터 공개 점검, 아동 대상 여부 판단 |
| 온라인 기능(랭킹·채팅) | 사용자 생성 콘텐츠·타인 정보 | 신고·차단·보관, 닉네임 정책, 로그 보존 기간 |
| 아동 대상 | 법정대리인 동의 등 가중 요건 | 연령 확인 설계(최소 수집), 대상 연령 결정 |

### 8.6 개인정보 영향평가(PIA) 점검표 — 이 게임 / 기능 추가 시 사용
(법정 "개인정보 영향평가" 대상 여부와 별개로, 설계 단계의 자체 점검용 질문 목록입니다.)

- [ ] 처리하는 개인정보 항목을 모두 나열했는가(이름·성별 선택·종족 선택·진행 정보) — §8.1
- [ ] 각 항목의 **수집 목적**과 **필요 최소 여부**를 설명할 수 있는가(예: 성별 선택 없이도 플레이 가능한가 → 예, `neutral`)
- [ ] 외부 전송 경로가 **0** 임을 코드(grep)와 빌드 결과(권한·네트워크 모니터링)로 확인했는가 — 코드 확인 완료 / 빌드 확인 **미완료**
- [ ] 보관 위치·기간·삭제 방법이 문서화되어 있는가 — 처리방침 초안 §2, 인앱 삭제 기능은 로드맵
- [ ] 제3자(스토어·SDK)가 데이터를 받는가, 받는다면 고지했는가 — 플랫폼 정책 링크는 `[TODO]`
- [ ] 아동 이용 가능성을 판단했고 대상 연령을 정했는가 — **미정**
- [ ] 로그·크래시 덤프에 이름 등이 남지 않는가 — `Debug.LogException` 사용(`GameBootstrap`); 이름이 예외 메시지에 들어가는 경로는 점검 필요
- [ ] 백업(Android 자동 백업, 클라우드 동기화)에 개인정보가 섞이는가 — 검토 필요
- [ ] 이용자 권리(열람·정정·삭제) 요청에 응답할 수 있는 구조인가 — 개발자가 데이터를 갖지 않음
- [ ] 개인정보 처리방침이 실제 동작과 **일치**하는가, 변경 시 갱신 절차가 있는가 — 초안 작성됨, 법률 검토 전
- [ ] 침해 사고 시 대응 절차·연락처가 있는가 — 현재 해당 데이터 없음, 온라인 기능 추가 시 필수

## 9. 시험 전략 요약

| 계층 | 도구 | 보증 범위 | 보증하지 않는 것 |
|---|---|---|---|
| 단위·통합 | `tools/csharp_check/*` 7종(합계 958 검사) | 세이브 왕복·변조 탐지·백업 복구·범위 거부, 파서/실행기, 엔딩·로직 | Unity 런타임, 실제 `JsonUtility` |
| 보안 자가 테스트 | `tools/security_selftest/`(고정 시드 퍼징) | 예외 계약, 무한 루프/시간 제한, 검증기 범위, 경로 이탈, 깊이 | **퍼징 커버리지 유도(AFL/libFuzzer 류) 아님**, 단순 변이 퍼저, 수 분 실행 |
| 컴파일 검사 | `tools/unity_compile_check` | 실제 UnityEngine/uGUI/InputSystem 정의로 컴파일 | 실행, Editor 스크립트 |
| 위생 | `tools/release_check.sh` | 시크릿 휴리스틱, SBOM 최신성, 플래그·CG 정합 | 정밀 시크릿 스캔, 취약점 스캔 |
| Unity EditMode(NUnit 24개) | `Tests/EditMode/*` | 작성됨 | **한 번도 실행하지 못함(Unity 필요)** |

## 10. 한계와 개선 로드맵

### 10.1 정직한 한계
1. **HMAC 키가 클라이언트에 있음.** `SaveIntegrity.KeyMaterial` 이 소스(과 빌드)에 들어 있어, 키를 아는 사람은 어떤 값이든 유효 서명을 만들 수 있습니다. 따라서 이 서명은 **우발적 손상·단순 편집 탐지** 용입니다. 의지 있는 변조를 막는다고 주장하지 않습니다. 실제 방어선은 `SaveValidator` 의 범위 검증이며, 이것도 모든 필드를 덮지는 않습니다(F-7).
2. **코드 서명 없음.** 설치 파일의 발행자 증명은 현재 불가. 해시는 별도 경로로 공개해야 의미가 있음.
3. **지속적 퍼징 없음.** 자가 테스트는 고정 시드 변이 퍼저 1회 실행 결과이며, 커버리지 기반 퍼저·CI 상시 실행이 아님.
4. **Unity 측은 미검증.** `JsonUtility` 동작, Editor 빌드 스크립트, 권한 목록, 실제 `persistentDataPath` 동작은 이 환경에서 실행하지 못했습니다.
5. 자가 테스트 발견 8건은 **모두 수정**했고(`SELFTEST_RESULTS.md` §7·§8), 수동 리뷰(`docs/19_Security_Review.md`) 중 N-6(부분)·N-9·N-11·N-12 는 **미해결**입니다.
6. **AI 에셋 출처 로그는 양식 단계**: 실제 에셋·기록 없음.
7. **법률 문서는 초안**: 처리방침·고지문 모두 법률 검토 전이며 `[TODO]` 가 남아 있음.
8. 세이브 암호화 없음(기밀 데이터가 없다는 판단), 이용자 인앱 데이터 삭제 기능 없음.

### 10.2 로드맵(우선순위 순)
| 순서 | 항목 | 효과 | 난이도 |
|---|---|---|---|
| 1 | F-1~F-8 패치(저장소 이름 검증, 숫자 `TryParse`, 재귀 깊이·점프 스텝 상한, 이름 길이 정합, 검증기 보강, 파일 크기 상한) + 자가 테스트를 회귀 테스트로 `release_check.sh` 에 편입 | 예외 계약·가용성 | 하 |
| 2 | Unity EditMode 24개 실행·`JsonUtility` 실제 동작 검증, 자가 테스트에 Unity 직렬화 결과 샘플 추가 | 대역 한계 해소 | 중 |
| 3 | 빌드 결과 검증: 안드로이드 권한 목록(`aapt dump permissions`)·Windows 네트워크 동작 점검을 릴리스 체크에 추가 | "무수집" 주장을 빌드 수준으로 | 중 |
| 4 | 패키지 버전 고정(`manifest.json`)·SBOM `unspecified` 해소·의존성 취약점 대조 | 공급망 | 중 |
| 5 | 코드 서명(Windows)·안드로이드 앱 서명 키 관리 절차 문서화, 해시 공개 절차 | 배포 신뢰 | 중 |
| 6 | 커버리지 기반 퍼징(SharpFuzz 등)·CI 상시 실행 | 퍼징 신뢰도 | 상 |
| 7 | 인앱 데이터 삭제·내보내기, 로그에서 이름 제거 점검 | 이용자 통제 | 하 |
| 8 | (온라인 기능을 넣는다면) 서버 측 검증·TLS·저장 시 암호화·동의 UI·PIA 재수행 | §8.5 | 상 |

## 11. 변경 이력
- 2026-10-03: 최초 작성. 자가 테스트(`tools/security_selftest`) 결과를 반영.


## 12. 수정 이력 (자가 테스트 발견 → 수정, 2026-10-03)

| ID | 상태 | 잔여 위험 |
|---|---|---|
| F-1 저장소 경로 이탈 | **수정·회귀 시험**(`FileSaveStorage.PathOf` 가 비정상 이름에 `ArgumentException`) | Windows 경로 규칙(`CON` 등)은 미시험 |
| F-2 숫자 오버플로 | **수정·회귀 시험** | Unity 런타임에서 재확인 필요 |
| F-3 재귀/길이 | **수정·회귀 시험**(표현식 깊이 64·토큰 512, 블록 중첩 64) | 다른 재귀 경로(`StatementWalker` 등)는 중첩 상한 덕에 제한됨, 별도 시험은 없음 |
| F-4 점프 순환 | **수정·회귀 시험**(런타임 내부 스텝 10,000 상한, 린트 `SILENT_JUMP_CYCLE`) | 대사가 있는 무한 순환은 상한 대상이 아님(플레이어가 진행하므로 hang 아님) |
| F-5 태그 이차 시간 | **수정·회귀 시험**(줄당 32개) | — |
| F-6 이름 불일치 | **수정·회귀 시험** | 보이는 유사 문자(동형 이문자) 스푸핑은 막지 않음(로컬 게임이라 수용) |
| F-7 검증기 공백 | **수정·회귀 시험**(2차, 사용자 승인) — 모든 필드 범위/화이트리스트/유일성/문자 검증, 이름은 생성 화면 규칙 재사용 | 키가 클라이언트에 있으므로 이 검증기가 실제 방어선이라는 점은 그대로. 의미상 "그럴듯하지만 거짓인 값"(예: 호감도 100)은 막지 못함 |
| F-8 파일 크기 | **수정·회귀 시험**(1MB) | 쓰기 크기는 제한하지 않음(자체 생성 데이터) |

§4 의 위협 표에서 위 항목이 "미해결/결함 발견"으로 적힌 행은 이 표가 최신 상태입니다(§4 문장은 발견 시점 기록으로 유지).


## 13. 수동 리뷰(`docs/19_Security_Review.md`) 반영 (2026-10-03)

| 리뷰 ID | 상태 | 잔여 위험 |
|---|---|---|
| N-1 TMP 리치텍스트 | 수정(`richText=false`) | 실제 TMP 렌더 동작은 Unity 필요 |
| N-2/N-3 콘텐츠 숫자·플래그 | 수정(디스패처에서 NaN/Inf/범위·개수 상한) | 린트에는 아직 숫자 범위 검사 없음 |
| N-4/N-5 파일 교체·백업 | 수정(`File.Replace`, `.corrupt` 보존, 정상 `.bak` 보호) | 동시 실행·디스크 가득 시 `.tmp` 잔여물 |
| N-6 이어하기 예외·최신 판정 | **부분**(검증기 화이트리스트) | `LoadLatest` 예외 처리·`SavedAtUtc` 형식 미해결 |
| N-7 개발 도구 공급망 | 수정(커밋 SHA·sha256 고정, `mktemp -d`) | 고정 값은 사람이 갱신해야 함, 업스트림 신뢰는 그대로 |
| N-8 `.gitignore`·비밀 필터 | 수정(루트 `.gitignore` 와 두 스크립트 패턴 정렬) | 휴리스틱 검사 |
| N-9 Android 백업·INTERNET 권한 | **미해결**(Unity 빌드 필요) | 처리방침 "전송 없음"이 OS 백업과 충돌할 수 있음 |
| N-10 이름의 비가시 문자 | 수정 | — |
| N-11/N-12 | **미해결** | 낮음(Info) |
