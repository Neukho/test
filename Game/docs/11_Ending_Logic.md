# 엔딩 로직 v0.1 — 공동 루트 확정 · 엔딩 판정

> `docs/10_Route_Structure.md` 의 계약을 구현한 순수 C# 로직 설명서. Unity 비의존(`MorpheHouse.Romance` / `Data` / `Save` / `Game` 어셈블리).

## 1. API 요약

| 대상 | 위치 | 설명 |
|---|---|---|
| `GameState.LockedRoute` | Data | 대표 id(단독이면 그 인물, 공동이면 첫 구성원). **기존 필드·저장 호환 유지** |
| `GameState.LockedGroup` | Data | 구성원 id 목록(1~3명, 확정 순서). 비어 있고 `LockedRoute` 만 있으면 구형 단독 상태 |
| `HasRouteLock` / `LockedIds()` / `LockedCount` / `IsRouteMember(id)` / `IsLockedOut(id)` / `SetLock(ids)` | Data | 항상 이 헬퍼로 읽는다(구형 상태 자동 처리) |
| `RomanceService.TryLockRoute(id)` | Romance | 단독 확정(유지). 이제 `LockedGroup=[id]` 도 채운다 |
| `RomanceService.TryLockGroup(ids[, out reason])` | Romance | 공동 확정. 1명이면 단독과 동일 |
| `RomanceService.CheckGroupLock(ids)` | Romance | 상태 변경 없이 사유 코드(가능하면 null) |
| `RouteLocked` 이벤트 | Core | `CharId`(대표) + `CharIds`(전원) + `IsGroup`. 기존 `new RouteLocked(id)` 호환 |
| `EndingTable.Load(ITextSource)` | Romance | `balance/endings.csv`(`"endings"`) 로드. 필수 6행이 없으면 예외 |
| `EndingEvaluator.Evaluate(state, table)` | Romance | 순수·결정적 판정 → `EndingResult` |
| `EndingResult` | Romance | `Kind`, `Id`, `NodeName`, `CgIds`, `Members`, `PrimaryChar`, `Reason`, `Shortfalls` |
| `EndingPlan.Create(state, table, catalog, nodeExists)` | Game | UI 가 게임 종료 시 호출. 재생 노드(없으면 `null`+대체 문구), 제목, CG, 이름 |
| `NightRomanceActions.RouteLockOptions()` / `ValidateGroup(ids, out msg)` / `TryLockGroup(ids, out msg)` | Game | 루트 확정 화면용 후보·검증·확정(거절 사유 문구 포함) |

### 루트 확정 규칙 (`TryLockGroup`)
챕터 ≥ 7, 인원 1~3명(중복·미지 인물 불가), 확정 전, **구성원 각자 호감 ≥ 60 · 신뢰 ≥ 20**. 성공하면 비구성원은 동결(성장 정지)·방치 감쇠 면제, 구성원은 계속 성장한다(감쇠는 기존처럼 적용).
거절 사유 코드(첫 위반만): `group_empty` · `group_too_large` · `unknown_char` · `duplicate_member` · `already_locked` · `chapter_too_early` · `affection_too_low` · `trust_too_low`.

대사 커맨드 `<<route_lock howl>>` 은 그대로, `<<route_lock howl johan>>` 처럼 인자 2~3개면 공동 확정. 대사 함수 `route_locked("x")` 는 공동 루트 구성원 누구에게나 true, `group_size()` 는 확정 인원 수.

## 2. 판정 규칙 (`balance/endings.csv` 의 수치를 그대로 읽음, 비교는 이상 `>=`)

| 확정 상태 | 조건 | 결과 | 노드 | CG |
|---|---|---|---|---|
| 단독 | 호감 95 · 신뢰 46 · 이벤트 5 + `ledger_truth` + `<char>_key` | 트루 | `<Char>_End_True` | `<CHAR>_E6` |
| 단독 | 호감 85 · 신뢰 40 · 이벤트 5 | 굿 | `<Char>_End_Good` | — |
| 단독 | 호감 60 · 신뢰 20 · 이벤트 3 | 노멀 | `<Char>_End_Normal` | — |
| 단독 | 위 모두 미달 | 커뮤니티 (`below_normal`) | `Ending_Community` | — |
| 공동 2인 | **전원** 호감 75 · 신뢰 34 · 이벤트 4 + 전원 `harem_ok_<char>` | 다자(2) | `Ending_Harem2` | `HAREM_E1` |
| 공동 3인 | **전원** 호감 70 · 신뢰 30 · 이벤트 4 + 전원 `harem_ok_<char>` | 다자(3) | `Ending_Harem3` | `HAREM_E1` |
| 공동 | 한 명이라도 미달·동의 없음 | 커뮤니티 (`group_unmet`, 노멀·부분 엔딩 없음) | `Ending_Community` | — |
| 미확정 | 무엇이든 | 커뮤니티 (`no_lock`) | `Ending_Community` | — |

- 플래그가 없거나 값이 0이면 없는 것으로 본다. 단독 판정은 harem 플래그를 보지 않고, 공동 판정은 `ledger_truth`/`_key` 를 보지 않는다(공동에는 트루 없음).
- `Shortfalls` 에 미달 항목이 `howl:aff`, `johan:flag harem_ok_johan` 형태로 모두 담긴다(안내·QA용).
- CSV 의 `required_flags` 토큰: `route_locked`/`group_locked`/`route_unlocked` 는 전제(검사 안 함), `key_flag` → `<char>_key`, `harem_consent` → `harem_ok_<char>`, 그 외는 같은 이름의 플래그.

## 3. 저장 호환

- `SaveData.LockedGroup`(리스트) 추가, (당시 `CurrentVersion` 1 유지, 이후 서명 재설계로 2). 필드가 없는 구형 저장은 빈 목록 → `LockedRoute` 만으로 단독 루트로 복원.
- `SaveValidator`: 구성원 ≤ 3, 빈 id/32자 초과/중복 불가, 목록이 있으면 `LockedRoute == LockedGroup[0]`.
- 서명(HMAC)은 v2 부터 버전별 정규 페이로드(`SaveIntegrity.CanonicalPayload`, `LockedGroup` 포함)에 대해 계산하므로 새 선택 필드가 구 저장 서명을 깨지 않는다. `CurrentVersion` 은 2(서명 방식 전환). 3단계 이전 v1 저장은 `SaveService.VerifyLegacyV1`(구형 해시 변형 3종, `StripEmptyLockedGroup` 포함)로 검증한 뒤 `SaveMigrations` 로 v2 가 된다. 내용을 고친 파일은 여전히 `Tampered`.

## 4. 시뮬레이터 (`--group K`)

`python3 tools/simulate_balance.py --group 2|3` 은 기본 리포트 뒤에 공동 루트 섹션을 덧붙인다(옵션이 없으면 출력 동일). 목표 = 앞 K명(howl, johan, beomjun), 집중도를 구성원에게 균등 분배, 성공 = `harem_duo`/`harem_trio` 기준 전원 충족(동의 플래그는 항상 획득 가정). 1000회 결과(docs/10 §2 의 300회 수치와 ±10%p 이내):

| 그룹 | 확정 최소 | 엔딩 조건 | f=60% | f=80% | f=100% | 숙련 f=100% |
|---|---|---|---|---|---|---|
| 2인 | 50/7 | 72/24/3 | 2% | 9% | 57% | 99% |
| 3인 | 50/7 | 65/14/2 | 1% | 12% | 32% | 57% |

(2026-10-03 다자 전용 재조정 후 300회, 기준 플레이어 = 무작위 선택. 확정 최소·배율은 params.csv `group_*`, 설명은 docs/10 §2.) 잘못된 묶음(4명 이상·중복 구성원)은 `SaveValidator` 가 막고, `EndingEvaluator` 도 다자 엔딩을 주지 않고 커뮤니티로 돌린다(`reason = invalid_group`, QA-15).

## 5. 테스트 요약

`sh tools/csharp_check/ending_check/run.sh` — 140 검사: 표 로드·CSV 수치 의존 / `GameState` 헬퍼 / 그룹 확정(경계 60·20, 챕터 6·7, 크기 0·4, 중복, 미지 인물, 이벤트, 동결) / 감쇠 면제(그룹·구형·미확정) / 단독 엔딩 전 분기·경계(94.9↔95 등)·플래그 누락 / 다자 엔딩 전 분기·경계·동의 누락·부분 미달 → 커뮤니티 / 저장 왕복·구형 저장·검증기·구형 서명 / 밤 행동 후보·검증 문구 / `EndingPlan`.
