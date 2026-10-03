# 보안 자가 테스트 결과 (SELFTEST_RESULTS)

> 실행: `sh tools/security_selftest/run.sh` (필요: `mono-mcs`, `mono-runtime`; Unity 불필요) · 실제 실행일 2026-10-03 · 고정 시드 `20260101`
> 이 문서의 숫자는 **전부 실제 실행 출력**에서 옮긴 것입니다(마지막 절에 원본 출력 전체를 첨부). 시드가 고정이므로 시드 기반 캠페인은 같은 입력으로 다시 실행되며, 저는 이 하네스를 여러 번 실행해 변이·위조 캠페인의 횟수가 같게 나오는 것을 확인했습니다(시간 제한 경계에 걸리는 항목만 환경에 따라 달라질 수 있음).
> English summary: a seeded mutation/fuzz harness (35,163 iterations/cases, ~70 s, 17 campaigns) against the real save loader, YARN parser/runner, MiniJson/profile/settings loaders and file storage. **No crash, hang or integrity bypass in the save loader; 12 real findings elsewhere (all Low to Low-Medium for a local single-player game)**. The harness exits 1 on purpose while those findings are unfixed.

## 1. 한눈에 보기

| 항목 | 결과 |
|---|---|
| 총 반복/케이스 | **35,163** (캠페인당 3,000회 × 다수 + 표적 케이스), 실행 시간 약 70초 |
| 세이브 변이 퍼징(JSON 대역 3,000 + XML 대역 3,000) | `SaveService.Load` **미처리 예외 0 · 시간 초과 0**. 수락된 파일(Ok 147 + 백업 복구 2,955)은 **전부 원본과 동일한 정규형**(= 서명을 맞추지 못한 변조 수락 0건) |
| 서명을 맞춘 위조 파일(범위 밖 값) | JSON 2,241 + XML 2,651 = **4,892건 전부 `Corrupt` 로 거부**, 수락 0건. `NaN`/`Infinity` 는 XML 대역에서 시험(JSON 대역은 표현 불가) |
| 검증기 직접 호출(null 필드) | 9건 모두 사유를 돌려주고 예외 없음 |
| 저장 → 불러오기 왕복 + 잘못된 데이터 저장 거부 | 유효 1,454×2 왕복 동일, 잘못된 1,546×2 건은 `InvalidOperationException` 으로 거부되고 **파일이 생기지 않음**. 슬롯 번호 경계(−1, 4, `int.Min/Max`, 1000) 거부 |
| `.yarn` 변이 퍼징(실제 시나리오 87개 + 합성 1개를 변형) | 3,000회: 파싱 성공 1,526 / `DialogueParseException` 1,474 / **다른 예외 0 · 시간 초과 0**. 파싱된 입력은 실행까지 → 정상 1,382 · 문서화된 실행 오류 1,562 |
| 대규모·초장문 입력 | 10만 줄 단일 노드 0.44초, 10,000 노드 0.15초, 1MB 대사 줄 0.03초, 5만 보간 0.06초 |
| `FileSaveStorage` 경로 이탈 | 3,031개 이름(`../`, 절대 경로, `..\\`, 널 문자, 긴 이름 등). **외부 파일 읽기 누출 0 · 기존 외부 파일 변경 0**, 그러나 **외부에 새 파일 생성 213건(결함 F-1)** |
| `MiniJson`/프로필/설정 | 각 3,000회: 허용 예외(`FormatException`)만, 프로필·설정은 예외 0, 항목 수·값 범위 위반 0 |

## 2. 단언(assertion)과 판정 방식

하네스는 아래를 **단언**으로 검사하고, 어긋나면 `FAIL` 을 출력한 뒤 종료 코드 1을 반환합니다.

1. 문서화된 예외 외 미처리 예외 없음: 파서 → `DialogueParseException`, 실행기 → `DialogueRuntimeException`/`InvalidOperationException`/`ArgumentOutOfRangeException`, `MiniJson` → `FormatException`, `SaveService.Load`/`ProfileService.Load`/`SettingsService.Load` → **예외 없음**
2. 시간 제한(케이스당 3초) 초과 없음 = hang 없음 (작업 스레드 + `Join(timeout)`)
3. 검증기가 범위 밖 값을 거부(서명을 맞춘 위조 파일 포함)
4. 수락된 세이브는 항상 원본과 같은 값(변조 수락 없음)
5. `FileSaveStorage` 가 자신의 디렉터리 밖에 쓰거나 밖의 내용을 읽지 않음
6. 재귀 깊이·무한 루프는 프로세스를 죽이므로 **별도 프로세스(자기 자신 재실행)** 로 격리해 시험

현재 **12개 단언이 실패**합니다. 이는 하네스 오류가 아니라 대상 모듈에서 **실제로 발견된 문제**이며(§3), 종료 코드 1은 수정 전까지 의도된 상태입니다. 수정 후에는 `0` 이 되어 회귀 테스트로 쓸 수 있습니다.

## 3. 발견 사항 (실제 버그/결함)

심각도는 "싱글 플레이 로컬 게임 기준"입니다. **모듈 코드는 수정하지 않았고**(작업 범위 밖), 아래에 정확한 패치안만 적습니다.

| ID | 심각도 | 위치 | 증상 (재현) | 패치안 |
|---|---|---|---|---|
| F-1 | Low (방어 심층) | `Scripts/Save/SaveService.cs` · `FileSaveStorage.PathOf` | `Write("")`, `Write("/")`, `Write("a/")`, `Write("../")` 는 `Path.GetFileName` 이 빈 문자열을 돌려줘 `path == 저장폴더` 가 되고, `tmp = path + ".tmp"` 가 **저장 폴더 바깥**(`.../saves.tmp`)에 만들어진 뒤 `Move` 가 실패하며 남음. 3,031개 이름 중 213건. `SaveService.SlotName(int)` 로만 이름을 만드는 현재 앱 경로에서는 **도달 불가** | `PathOf` 에서 `string f = name == null ? "" : Path.GetFileName(name); if (f.Length == 0 \|\| f == "." \|\| f == ".." \|\| f != name) throw new ArgumentException("invalid save file name", "name"); return Path.Combine(_directory, f);` |
| F-2 | Low | `Scripts/Dialogue/DialogueExpression.cs` 토크나이저 | 400자리 숫자 리터럴(`{99999…}`, `<<set $x to 9999…>>`)이 `DialogueParseException` 대신 `OverflowException`(Mono 의 `double.Parse`) — **문서화된 예외 계약 위반**. (.NET Core 계열은 `Infinity` 를 돌려주므로 런타임별로 다름. Unity 런타임에서의 동작은 이 환경에서 확인하지 못함) | `double.Parse` → `double.TryParse(..., NumberStyles.Float, CultureInfo.InvariantCulture, out v)` 후 실패 또는 `double.IsInfinity(v)` 면 `throw Err("숫자가 너무 큽니다: " + _src)` |
| F-3 | Low (번들 콘텐츠) / 외부 스크립트를 받는다면 Medium | `YarnParser.BodyParser`·`DialogueExpression.ExprParser` | 재귀 깊이 상한 없음. 별도 프로세스 프로브: `((((…` **20,000단계**, `max(1,max(1,…` **20,000단계**, `!!!…`/`---…`/`<<if true>>` 중첩 **100,000단계**에서 `StackOverflowException`(이 환경의 Mono 는 예외로 올렸지만, CLR 계열에서는 보통 프로세스 종료). 5,000단계까지는 통과, 옵션(`->`) 들여쓰기 중첩은 10,000단계까지 통과 | 파서에 깊이 카운터(예: 최대 64) — `ParseUnary`/`ParsePrimary`(괄호·호출 인자)/`ParseBlock` 진입 시 `if (++_depth > 64) throw Err("중첩이 너무 깊습니다");` 와 `finally { _depth--; }` |
| F-4 | Low~Medium (가용성) | `Scripts/Dialogue/DialogueRunner.cs` · `Next()` | `title: a --- <<jump a>>` 또는 a↔b 상호 점프는 **`Next()` 한 번이 영원히 반환하지 않음**(4초 후 강제 종료). 스텝 상한·순환 감지 없음. 실제 번들 시나리오는 `scenario_check` 가 약 160만 회 실행에서 무한 루프 0건이라 현재는 문제 없음 | `Next()` 의 `while (true)` 안에 `int budget = 100000;` 를 두고 문장 하나 처리할 때마다 `if (--budget < 0) throw new DialogueRuntimeException(Where() + "한 단계에서 문장을 너무 많이 실행했습니다(점프 순환?)");` + 가능하면 `DialogueLint` 에 점프 그래프 순환(대사 없는 순환) 검사 추가 |
| F-5 | Low | `YarnParser.BodyParser.StripTags` / `BracesBalanced` | 한 줄 끝에 ` #t` 가 반복되면 이차 시간: 1,000개 6ms · 5,000개 ~190ms · **20,000개 ~2.9초** (대사·선택지 모두) | 꼬리표 추출 상한(예: 줄당 32개 초과 시 `DialogueParseException`) 또는 한 번의 역방향 스캔으로 구현 |
| F-6 | Low~Medium (기능) | `Scripts/Data/ProtagonistProfile.cs` `ProfileValidator.CheckName` ↔ `Scripts/Save/SaveValidator.cs` | `CheckName` 은 **텍스트 요소 12개 이하**(StringInfo)만 보고 UTF-16 길이는 보지 않음. 조합 문자를 많이 붙인 이름(1 텍스트 요소, UTF-16 132자 등)이 이름 검증을 통과하지만 `SaveValidator` 는 UTF-16 32자 초과를 거부 → 그 이름으로는 **저장이 `InvalidOperationException` 으로 실패**. 무작위 3,000개 중 수락 350 · 이 중 19건 | `CheckName` 에 `if (n.Length > 32) return ErrNameTooLong;` (이상적으로는 두 곳이 같은 상수를 참조) |
| F-7 | Low | `Scripts/Save/SaveValidator.cs` | 서명된 파일에서 **검증하지 않아 수락되는 필드**(관찰, 아래 §4 표) | `SatisfactionBonus`/`PlayTimeSec` 유한·범위 검사, 이름은 `ProfileValidator.CheckName` 재사용(빈 이름·제어 문자·`<>{}\`), 캐릭터 ID 는 `CharIds.All` 화이트리스트·중복 금지, 플래그 키 중복 금지, `SavedAtUtc`/`AppVersion`/`Hash` 길이 상한 |
| F-8 | Info | `FileSaveStorage`/`FileProfileStorage`/`FileSettingsStorage` | `ReadAllText` 전에 크기 상한 없음: 64MB `profile.json` 도 2.9초에 전부 읽음(프로필은 항목 512개 상한으로 정리됨) | 읽기 전에 `FileInfo.Length` 가 상한(예: 세이브 1MB, 프로필·설정 256KB) 초과면 `Corrupt` 로 처리 |

### 해석상 주의 (과장 방지)
- F-1·F-3·F-4 는 **현재 앱의 정상 경로에서는 도달할 수 없습니다.** 각각 정수 슬롯에서만 파일명을 만들고, 스크립트가 번들 콘텐츠이기 때문입니다. "방어 심층/계약 위반" 수준의 발견이며 실제 사용자 피해 사례가 있었던 것이 아닙니다.
- F-6 은 플레이어가 실제로 겪을 수 있는 기능 결함이지만, 일반적인 이름 입력(한글·영문)에서는 나타나지 않고 조합 문자를 의도적으로 많이 넣어야 합니다.
- **`OverflowException`/`StackOverflowException` 동작은 이 환경의 Mono 기준**입니다. Unity(Mono/IL2CPP)의 `double.Parse` 와 스택 한계는 이 환경에서 확인하지 못했습니다.

## 4. 검증기가 제약하지 않는 필드 (서명된 위조 파일이 수락됨 — F-7 상세)

HMAC 키가 소스에 있으므로 위조 파일에 유효한 서명을 붙일 수 있고(`SaveIntegrity.KeyMaterial`), 이때 마지막 방어선은 `SaveValidator` 입니다. 아래 값을 가진 서명된 파일은 `Ok` 로 수락됐습니다(○ = 수락, − = 해당 직렬화 대역으로 표현 불가/다른 사유로 거부).

| 필드 값 | JSON 대역 | XML 대역 |
|---|---|---|
| `PlayerName` = 빈 문자열 | ○ | ○ |
| `SatisfactionBonus` = 1e30 | ○ | ○ |
| `SatisfactionBonus` = NaN / +Infinity | − | ○ |
| `PlayTimeSec` = NaN | − | ○ |
| `PlayTimeSec` = −1e300 | ○ | ○ |
| `Slot` = 999(파일명과 불일치) | ○ | ○ |
| `Chars[0].LastGiftDay` = int.Max / `LastContactDay` = int.Min | ○ | ○ |
| 플래그 값 = int.Min, 중복 플래그 키 | ○ | ○ |
| 중복 캐릭터 ID, 알 수 없는 ID(`../../x` 포함) | ○ | ○ |
| `PlayerName` = `<b>{x}</b>` / 방향 재정의 U+202E / 조합 문자 31개 | ○ | ○ |
| `PlayerName` = 제어 문자(`\u0001`) | ○ | −(XML 이 거부) |
| `VisitedNodes[0]` = `../../etc/passwd`, `LockedRoute` = 알 수 없는 ID | ○ | ○ |
| `SavedAtUtc`/`AppVersion` = 1MB 문자열 | ○ | ○ |

영향 평가(정직하게): 대부분은 표시/기능 이상 수준입니다. `SatisfactionBonus` 에 `NaN` 이 들어가면 경영 계산에 `NaN` 이 전파될 수 있으나, 이 하네스에서 그 전파를 실제로 따라가 확인하지는 않았습니다(시도하지 않음). 방문 노드·캐릭터 ID 가 파일 경로로 쓰이는 코드는 확인되지 않았습니다(코드 리뷰 범위).

## 5. 이 자가 테스트가 증명하지 못하는 것

- **Unity `JsonUtility` 를 시험하지 않았습니다.** 두 직렬화 대역(`MiniJson` 기반, `XmlSerializer`)으로 `SaveService`·`SaveValidator`·`SaveIntegrity` 로직을 시험했을 뿐이며, JsonUtility 의 파서 견고성은 Unity 가 있어야 확인됩니다.
- **퍼저 수준**: 문법 인지 변이(토큰 삽입·삭제·복제·잘라내기·바이트 변형)를 쓴 단순 무작위 퍼저입니다. 커버리지 유도 퍼징(AFL/libFuzzer/SharpFuzz)이 아니며, 35천 회가 "충분하다"는 주장도 하지 않습니다.
- 플랫폼: Linux + Mono 에서만 실행. Windows 의 경로 규칙(`\`, 예약어 `CON`, 대체 데이터 스트림 등)에서의 `FileSaveStorage` 동작은 시험하지 못했습니다(`Path.GetFileName` 의 플랫폼별 동작 차이).
- 유효하지 않은 UTF-8 은 "바이트 변형 → UTF-8 디코딩(치환 문자)" 경로로만 시험했습니다(파일을 바이트로 직접 읽는 경로는 `File.ReadAllText` 가 담당).
- 동시성·파일 잠금·디스크 가득 참·전원 차단은 시험하지 않았습니다.

## 6. 재현

```sh
sh tools/security_selftest/run.sh | tee /tmp/selftest.txt     # 약 85~90초, 종료 코드 1(발견 사항 존재)
grep -E "assertion failure|total_iterations" /tmp/selftest.txt
```

원본 출력(2026-10-03, 이 문서의 모든 수치의 근거):

```text
security_selftest seed=20260101 iterations/campaign=3000 time_limit_ms=3000
[yarn.mutation] corpus files=88
[yarn.mutation] parse ok=1526 DialogueParseException=1474 | run ok=1382 documented-runtime-errors=1562 | hangs=0 max_case_ms=19
[yarn.targeted]
    10k-line single node                           parsed + run ok parse=52ms run=9ms
    10k nodes x 3 lines                            parsed parse=149ms run=0ms
    100k-line single node                          parsed + run ok parse=442ms run=21ms
    10k-line script w/ choices+if                  parsed + run ok parse=58ms run=9ms
    1MB dialogue line                              parsed + run ok parse=34ms run=0ms
    1MB node title                                 parsed parse=62ms run=0ms
    1MB comment line                               parsed + run ok parse=13ms run=0ms
    1MB header value                               parsed parse=67ms run=0ms
    100k-char expression {1+1+1...}                parsed + run ok parse=57ms run=33ms
    50k interpolations on one line                 parsed + run ok parse=55ms run=10ms
    100k unmatched '{'                             DialogueParseException parse=3ms run=0ms
    100k unmatched '<<'                            DialogueParseException parse=5ms run=0ms
    unterminated quote 1MB                         DialogueParseException parse=38ms run=0ms
    line with 1000 ' #t' tags                      parsed + run ok parse=6ms run=0ms
    line with 5000 ' #t' tags                      parsed + run ok parse=189ms run=0ms
    line with 20000 ' #t' tags                     parsed + run ok parse=2924ms run=0ms
    option with 1000 ' #t' tags                    parsed + run ok parse=7ms run=0ms
    option with 5000 ' #t' tags                    parsed + run ok parse=187ms run=0ms
    option with 20000 ' #t' tags                   parsed + run ok parse=2847ms run=0ms
    400-digit number literal                       UNEXPECTED OverflowException
    400-digit number in <<set>>                    UNEXPECTED OverflowException
    Infinity - Infinity -> NaN                     UNEXPECTED OverflowException
    1/0 and 0%0                                    parsed + run DialogueRuntimeException parse=0ms run=0ms
    0%0                                            parsed + run DialogueRuntimeException parse=0ms run=0ms
    NaN / Infinity words as identifiers            parsed + run DialogueRuntimeException parse=0ms run=0ms
    int overflow via GameFlags-like set            parsed + run ok parse=0ms run=0ms
    empty input                                    parsed parse=0ms run=0ms
    only BOM                                       parsed parse=0ms run=0ms
    only NULs                                      DialogueParseException parse=0ms run=0ms
    lone surrogates                                parsed + run ok parse=0ms run=0ms
    bidi override in speaker text                  parsed + run ok parse=0ms run=0ms
    CRLF + mixed tabs                              parsed + run ok parse=0ms run=0ms
[save.mutation.json] statuses: RestoredFromBackup=1443 Tampered=315 Corrupt=1149 Ok=93 | unexpected-exceptions=0 hangs=0
[save.mutation.xml] statuses: RestoredFromBackup=1511 Ok=57 Corrupt=1278 Tampered=154 | unexpected-exceptions=0 hangs=0
[save.forged_signed.json] signed-forged out-of-range files rejected=2241 skipped(non-representable)=759
[save.forged_signed.xml] signed-forged out-of-range files rejected=2651 skipped(non-representable)=349
[save.validator_gaps] fields the validator does NOT constrain (forged + correctly signed file):
    json  PlayerName empty string              ACCEPTED
    json  SatisfactionBonus=1e30               ACCEPTED
    json  PlayTimeSec=-1e300                   ACCEPTED
    json  Slot=999 (file/slot mismatch)        ACCEPTED
    json  Chars[0].LastGiftDay=int.Max         ACCEPTED
    json  Chars[0].LastContactDay=int.Min      ACCEPTED
    json  Flag value=int.Min                   ACCEPTED
    json  Duplicate flag keys                  ACCEPTED
    json  Duplicate/unknown char ids           ACCEPTED
    json  Unknown char id '../../x'            ACCEPTED
    json  PlayerName with <b>{x}</b>           ACCEPTED
    json  PlayerName with control chars        ACCEPTED
    json  PlayerName with bidi override        ACCEPTED
    json  PlayerName 32 combining marks        ACCEPTED
    json  VisitedNodes node contains ../       ACCEPTED
    json  LockedRoute not a known char         ACCEPTED
    json  SavedAtUtc 1MB                       ACCEPTED
    json  AppVersion 1MB                       ACCEPTED
    xml   PlayerName empty string              ACCEPTED
    xml   SatisfactionBonus=1e30               ACCEPTED
    xml   SatisfactionBonus=NaN                ACCEPTED
    xml   SatisfactionBonus=+Inf               ACCEPTED
    xml   PlayTimeSec=NaN                      ACCEPTED
    xml   PlayTimeSec=-1e300                   ACCEPTED
    xml   Slot=999 (file/slot mismatch)        ACCEPTED
    xml   Chars[0].LastGiftDay=int.Max         ACCEPTED
    xml   Chars[0].LastContactDay=int.Min      ACCEPTED
    xml   Flag value=int.Min                   ACCEPTED
    xml   Duplicate flag keys                  ACCEPTED
    xml   Duplicate/unknown char ids           ACCEPTED
    xml   Unknown char id '../../x'            ACCEPTED
    xml   PlayerName with <b>{x}</b>           ACCEPTED
    xml   PlayerName with control chars        Corrupt
    xml   PlayerName with bidi override        ACCEPTED
    xml   PlayerName 32 combining marks        ACCEPTED
    xml   VisitedNodes node contains ../       ACCEPTED
    xml   LockedRoute not a known char         ACCEPTED
    xml   SavedAtUtc 1MB                       ACCEPTED
    xml   AppVersion 1MB                       ACCEPTED
[validator.direct_nulls] 9 direct null-field cases: validator returns a reason without throwing
[save.roundtrip_refuse.json] saved+reloaded=1454 invalid-refused=1546 slot-bounds ok
[save.roundtrip_refuse.xml] saved+reloaded=1454 invalid-refused=1546 slot-bounds ok
[storage.path_traversal] names tried=3031 (Write+Read+ReadBackup+Exists each) | documented-class exceptions=981 [IOException=323 ArgumentException=657 PathTooLongException=1] other-exceptions=0 | read-leaks=0 | writes/creations OUTSIDE saves dir=213
    ESCAPE: Write("") created /outer/saves.tmp
    ESCAPE: Write("/") created /outer/saves.tmp
    ESCAPE: Write("./") created /outer/saves.tmp
    ESCAPE: Write("../") created /outer/saves.tmp
    ESCAPE: Write("a/") created /outer/saves.tmp
    ESCAPE: Write("/~?\가/") created /outer/saves.tmp
[minijson.parse] parsed=586 FormatException=2414
[profile.load] Loaded=300 Corrupt(empty profile fallback)=2696
[settings.load] done (no exceptions / values clamped)
[profile.name_vs_save] names accepted by ProfileValidator=350; of those, rejected later by SaveValidator (player could not save)=19
[oversized_files] profile.json 8MB: status=Loaded ex=none time=309ms cgs_kept=2 (no size cap before ReadAllText)
[oversized_files] profile.json 64MB: status=Loaded ex=none time=2879ms cgs_kept=2 (no size cap before ReadAllText)
[subprocess.depth_and_loops] isolated child-process probes (a stack overflow kills the whole process, so these cannot run in-process)
    paren_expr      100=completed | 1000=completed | 5000=completed | 20000=UNEXPECTED StackOverflowException | 100000=UNEXPECTED StackOverflowException | 
    unary_expr      100=completed | 1000=completed | 5000=completed | 20000=completed | 100000=UNEXPECTED StackOverflowException | 
    neg_expr        100=completed | 1000=completed | 5000=completed | 20000=completed | 100000=UNEXPECTED StackOverflowException | 
    call_nest       100=completed | 1000=completed | 5000=completed | 20000=UNEXPECTED StackOverflowException | 100000=UNEXPECTED StackOverflowException | 
    nested_options  100=completed | 1000=completed | 5000=completed | 10000=completed | 
    nested_if       100=completed | 1000=completed | 5000=completed | 20000=completed | 100000=UNEXPECTED StackOverflowException | 
    jump_loop       first runner.Next() => HANG(killed after 4000ms)
    set_loop        first runner.Next() => HANG(killed after 4000ms)

==== 요약 ====
yarn.mutation                      iterations=  3000 assertion_failures=0
yarn.targeted                      iterations=    32 assertion_failures=3
save.mutation.json                 iterations=  3000 assertion_failures=0
save.mutation.xml                  iterations=  3000 assertion_failures=0
save.signing                       iterations=     9 assertion_failures=0
save.forged_signed.json            iterations=  2241 assertion_failures=0
save.forged_signed.xml             iterations=  2651 assertion_failures=0
save.validator_gaps                iterations=    39 assertion_failures=0
validator.direct_nulls             iterations=     9 assertion_failures=0
save.roundtrip_refuse.json         iterations=  3005 assertion_failures=0
save.roundtrip_refuse.xml          iterations=  3005 assertion_failures=0
storage.path_traversal             iterations=  3031 assertion_failures=1
minijson.parse                     iterations=  3000 assertion_failures=0
profile.load                       iterations=  3000 assertion_failures=0
settings.load                      iterations=  3000 assertion_failures=0
profile.name_vs_save               iterations=  3000 assertion_failures=1
oversized_files                    iterations=     2 assertion_failures=0
subprocess.depth_and_loops         iterations=    31 assertion_failures=7
total_iterations_or_cases=35163 elapsed_s=68.0

==== 단언 실패 12 건 (고유 12 종) ====
  FAIL x1 [yarn.targeted] unexpected OverflowException in 400-digit number literal: Value was either too large or too small for a Double.
  FAIL x1 [yarn.targeted] unexpected OverflowException in 400-digit number in <<set>>: Value was either too large or too small for a Double.
  FAIL x1 [yarn.targeted] unexpected OverflowException in Infinity - Infinity -> NaN: Value was either too large or too small for a Double.
  FAIL x1 [storage.path_traversal] FileSaveStorage wrote outside its directory 213 time(s); e.g. Write("") created /outer/saves.tmp; Write("/") created /outer/saves.tmp; Write("./") created /outer/saves.tmp; Write("../") created /outer/saves.tmp; Write("a/") created /outer/saves.tmp; Write("/~?\가/") created /outer/saves.tmp
  FAIL x1 [profile.name_vs_save] inconsistent name limits: 19 ProfileValidator-valid names fail SaveValidator (name utf16Length=132 textElements=1 -> SaveValidator:playerName; name utf16Length=183 textElements=1 -> SaveValidator:playerName; name utf16Length=60 textElements=3 -> SaveValidator:playerName)
  FAIL x1 [subprocess.depth_and_loops] paren_expr first failing depth 20000:UNEXPECTED StackOverflowException
  FAIL x1 [subprocess.depth_and_loops] unary_expr first failing depth 100000:UNEXPECTED StackOverflowException
  FAIL x1 [subprocess.depth_and_loops] neg_expr first failing depth 100000:UNEXPECTED StackOverflowException
  FAIL x1 [subprocess.depth_and_loops] call_nest first failing depth 20000:UNEXPECTED StackOverflowException
  FAIL x1 [subprocess.depth_and_loops] nested_if first failing depth 100000:UNEXPECTED StackOverflowException
  FAIL x1 [subprocess.depth_and_loops] jump_loop -> runner hang (no step budget)
  FAIL x1 [subprocess.depth_and_loops] set_loop -> runner hang (no step budget)

==== 관찰/발견 54 건(단언이 아닌 보고용) ====
  OBS  느린 파싱: line with 20000 ' #t' tags parse=2924ms (제한 8000ms 이내)
  OBS  느린 파싱: option with 20000 ' #t' tags parse=2847ms (제한 8000ms 이내)
  OBS  검증 공백[json]: PlayerName empty string -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: SatisfactionBonus=1e30 -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: PlayTimeSec=-1e300 -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: Slot=999 (file/slot mismatch) -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: Chars[0].LastGiftDay=int.Max -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: Chars[0].LastContactDay=int.Min -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: Flag value=int.Min -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: Duplicate flag keys -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: Duplicate/unknown char ids -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: Unknown char id '../../x' -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: PlayerName with <b>{x}</b> -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: PlayerName with control chars -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: PlayerName with bidi override -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: PlayerName 32 combining marks -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: VisitedNodes node contains ../ -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: LockedRoute not a known char -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: SavedAtUtc 1MB -> 서명된 세이브가 수용됨
  OBS  검증 공백[json]: AppVersion 1MB -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: PlayerName empty string -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: SatisfactionBonus=1e30 -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: SatisfactionBonus=NaN -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: SatisfactionBonus=+Inf -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: PlayTimeSec=NaN -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: PlayTimeSec=-1e300 -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: Slot=999 (file/slot mismatch) -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: Chars[0].LastGiftDay=int.Max -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: Chars[0].LastContactDay=int.Min -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: Flag value=int.Min -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: Duplicate flag keys -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: Duplicate/unknown char ids -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: Unknown char id '../../x' -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: PlayerName with <b>{x}</b> -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: PlayerName with bidi override -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: PlayerName 32 combining marks -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: VisitedNodes node contains ../ -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: LockedRoute not a known char -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: SavedAtUtc 1MB -> 서명된 세이브가 수용됨
  OBS  검증 공백[xml]: AppVersion 1MB -> 서명된 세이브가 수용됨
  OBS  FileSaveStorage 경로 이탈(방어 심층 결함): 이름이 비거나 '/' 로 끝나면 Path.GetFileName 이 빈 문자열을 돌려줘 `<저장폴더>.tmp` 가 저장 폴더 바깥(형제 경로)에 생성됨. 예: Write("") created /outer/saves.tmp; Write("/") created /outer/saves.tmp; Write("./") created /outer/saves.tmp; Write("../") created /outer/saves.tmp; Write("a/") created /outer/saves.tmp; Write("/~?\가/") created /outer/saves.tmp. SaveService.SlotName 은 정수 슬롯에서만 이름을 만들므로 현재 앱 경로에...(+9)
  OBS  이름 검증 불일치: ProfileValidator.CheckName 은 텍스트 요소(StringInfo) 12개 이하만 보므로 조합 문자가 많은 이름(UTF-16 33자 이상)을 통과시키지만 SaveValidator 는 UTF-16 길이 32 초과를 거부함 -> 저장 시 InvalidOperationException. 예: name utf16Length=132 textElements=1 -> SaveValidator:playerName; name utf16Length=183 textElements=1 -> SaveValidator:playerName; name utf16Length=60 textElements=3 -> SaveValidator:playerName
  OBS  이름 입력이 수용하는 비가시/방향 제어 문자: U+202E (표시 속임/스푸핑 소지, 로컬 단일 사용자 게임이라 위험 낮음)
  OBS  이름 입력이 수용하는 비가시/방향 제어 문자: U+200B (표시 속임/스푸핑 소지, 로컬 단일 사용자 게임이라 위험 낮음)
  OBS  이름 입력이 수용하는 비가시/방향 제어 문자: U+FEFF (표시 속임/스푸핑 소지, 로컬 단일 사용자 게임이라 위험 낮음)
  OBS  이름 입력이 수용하는 비가시/방향 제어 문자: U+D800 (표시 속임/스푸핑 소지, 로컬 단일 사용자 게임이라 위험 낮음)
  OBS  FileProfileStorage/FileSettingsStorage/FileSaveStorage 는 File.ReadAllText 전에 파일 크기 상한이 없음(64MB 프로필도 전부 메모리에 읽음). 로컬 단일 사용자 위협 모델에서는 낮은 위험이나, 외부 입력(클라우드 동기화·공유 폴더)이 생기면 상한(예: 1MB)을 추가해야 함.
  OBS  재귀 깊이 제한 없음: paren_expr 입력이 깊이 20000 에서 UNEXPECTED StackOverflowException (DialogueParseException 이 아니라 프로세스 크래시/중단)
  OBS  재귀 깊이 제한 없음: unary_expr 입력이 깊이 100000 에서 UNEXPECTED StackOverflowException (DialogueParseException 이 아니라 프로세스 크래시/중단)
  OBS  재귀 깊이 제한 없음: neg_expr 입력이 깊이 100000 에서 UNEXPECTED StackOverflowException (DialogueParseException 이 아니라 프로세스 크래시/중단)
  OBS  재귀 깊이 제한 없음: call_nest 입력이 깊이 20000 에서 UNEXPECTED StackOverflowException (DialogueParseException 이 아니라 프로세스 크래시/중단)
  OBS  재귀 깊이 제한 없음: nested_if 입력이 깊이 100000 에서 UNEXPECTED StackOverflowException (DialogueParseException 이 아니라 프로세스 크래시/중단)
  OBS  실행기 무한 루프: <<jump>> 순환 스크립트는 DialogueRunner.Next() 한 번 호출 안에서 반환하지 않음(스텝 상한/순환 감지 없음). 스크립트는 신뢰 콘텐츠(번들)지만 린트가 순환을 막지 못하면 게임이 멈춤.
  OBS  실행기 무한 루프: <<jump>> 순환 스크립트는 DialogueRunner.Next() 한 번 호출 안에서 반환하지 않음(스텝 상한/순환 감지 없음). 스크립트는 신뢰 콘텐츠(번들)지만 린트가 순환을 막지 못하면 게임이 멈춤.
SELFTEST: 12 assertion failure(s) in 12 distinct finding(s) (each is a REAL finding in the module under test, see portfolio/SELFTEST_RESULTS.md)
exit=1
elapsed_s=86
```


---

## 7. 수정 후 재검증 (Before / After) — 2026-10-03

발견 8건 중 **7건(F-1~F-6, F-8)을 모듈에서 수정**하고 `tools/security_selftest` 에 회귀 단언을 추가했습니다. **F-7 은 이 시점에서 보류였고, 사용자 승인 후 §8 에서 수정했습니다(최종: 8건 모두 수정).**

### 7.1 변경 요약

| ID | 수정 위치 | 수정 내용 | 회귀 시험 |
|---|---|---|---|
| F-1 | `Save/SaveService.cs` `FileSaveStorage.PathOf` | 빈 이름·`.`·`..`·경로 구분자 포함 이름은 `ArgumentException` | `regression.fixes` 10개 나쁜 이름 + 3,031개 이름 캠페인에서 폴더 바깥 생성 213 → **0** |
| F-2 | `Dialogue/DialogueExpression.cs` 토크나이저 | `double.TryParse` + 무한대/NaN 거부 → `DialogueParseException` | 400자리 리터럴 3케이스 |
| F-3 | `DialogueExpression.cs`(깊이 64, 토큰 512), `YarnParser.cs`(블록 중첩 64, 줄 번호 포함) | 중첩·길이 상한 | 별도 프로세스 프로브 5종 모두 크래시 → `DialogueParseException`; 단위 단언 7개 |
| F-4 | `DialogueRunner.Next()`(내부 스텝 10,000 상한 → `DialogueRuntimeException`), `DialogueLint`(`SILENT_JUMP_CYCLE` 경고) | 순환 점프 방지 + 작성 시 경고 | 점프 순환 2종: 4초 hang → 즉시 예외; 린트 3 단언(순환 보고/대사 있는 순환 미보고) |
| F-5 | `YarnParser.cs` `StripTags` | 줄당 `#태그` 32개 초과 → 줄 번호 포함 `DialogueParseException` | 2만 개 케이스 2.9초 → 10ms(오류), 32개는 통과 |
| F-6 | `Data/ProtagonistProfile.cs` | UTF-16 ≤ 32 **그리고** 텍스트 요소 ≤ 12, 방향 제어(U+202A–E, U+2066–9)·제로폭(U+200B–F)·U+FEFF·짝 없는 서로게이트 거부 | 이름 8개 거부 + 정상 이름 4개 수락, 무작위 3,000개에서 "이름은 통과·저장은 실패" 19 → **0** |
| F-8 | `FileSaveStorage`, `FileProfileStorage`, `FileSettingsStorage` | 1MB 초과는 읽지 않고 손상 취급; `SaveService.Load` 는 저장소 예외를 `Corrupt` 로 변환 | 64MB 프로필 2.9초 → 0ms(`Corrupt`), 세이브·프로필·설정 각 단언 |
| F-7 | **미수정(보류)** | — | 관찰 39건 중 38건 계속 "수용" |

### 7.2 실측 Before / After

| 항목 | Before | After |
|---|---|---|
| 자가 테스트 단언 실패 | 12 (고유 12종) | **0** (`SELFTEST: assertions all passed`, 종료 코드 0) |
| 총 반복/케이스 | 35,046 | 35,089 (회귀 43개 추가, 기존 반복 수 유지) |
| 폴더 바깥 생성 | 213 / 3,031 | 0 / 3,031 |
| 이름 검증 불일치 | 19 / 350 수락 | 0 / 100 수락(더 엄격해져 수락 수 감소) |
| 거대 입력 | 64MB 프로필 2.9초 | 즉시 `Corrupt` |
| 깊이 프로브 | 5종 `StackOverflow`, 2종 hang | 전부 `DialogueParseException`/`DialogueRuntimeException` |
| 순수 로직 하네스 | 958 검사 PASS | **958 검사 PASS**(16·46·302·140·186·169·99, 실패 0) |
| `release_check.sh --skip-unity` | PASS 15 / WARN 1 | PASS 16 / WARN 1 / SKIP 1 / FAIL 0 (`scenario_check` 포함, 132초) |
| `unity_compile_check` | (미실행) | `UNITY COMPILE CHECK: ALL OK` (5개 프로젝트 빌드) |

주의: 위 "After"는 다른 에이전트가 동시에 수정 중인 코드베이스에서 측정한 값이며(아트 체크리스트 수치 등 일부가 이미 바뀜), 하네스 PASS 수는 변하지 않았습니다. 기존 하네스에 F-1~F-6, F-8 전용 회귀 검사를 새로 넣지는 않고 자가 테스트의 `regression.fixes` 캠페인에 모았습니다.

### 7.3 아직 열려 있는 것
- **F-7**: 검증하지 않는 필드(`SatisfactionBonus`, `PlayTimeSec`, 알 수 없는/중복 캐릭터 ID, 플래그 키 중복·값 범위, 방문 노드 문자, `Slot`, 문자열 길이, 빈 이름 등). 패치안은 §3 F-7 와 같고, 수정 시 자가 테스트의 `GapProbes` 관찰을 단언으로 전환해야 합니다.
- 수정은 같은 Mono 환경에서만 확인했습니다. Unity 런타임(`JsonUtility`, 스택 한계)에서의 동작은 여전히 미확인입니다.


---

## 8. 2차 수정: F-7 + 수동 보안 리뷰(`docs/19_Security_Review.md`) 반영 — 2026-10-03

사용자가 F-7 수정을 승인했고, 별도 수동 리뷰의 N-항목 중 N-1~N-5, N-7, N-8, N-10 을 함께 수정했습니다(상태표: `docs/19_Security_Review.md` §9).

### 8.1 F-7 (SaveValidator)
- `Scripts/Save/SaveValidator.cs`: `Slot` 0~3, `SatisfactionBonus` 유한·[-1,1], `PlayTimeSec` 유한·[0,3.2e8], `SavedAtUtc`/`AppVersion` ≤ 64자·`Hash` ≤ 128자, `PlayerName` 비어 있지 않음 + ≤ 32 + **`ProfileValidator.CheckName` 재사용**(`<>{}\`·제어/방향/제로폭·서로게이트·텍스트 요소 수), `Chars.Id`·`LockedRoute`·`LockedGroup` 은 `CharIds.All` 화이트리스트(캐릭터 ID 중복 금지), 플래그 키 비제어문자·중복 금지·값 ±1,000,000, 방문 노드에 경로 구분자·`..`·제어문자 금지, 일수 필드 [-100, 5000].
- `Data/ProtagonistProfile.cs` `IsInvisibleOrBidi` 확장: U+2028/2029·061C·2060·206A–206F·00AD·사설영역 + 유니코드 범주(서식·줄/문단 구분자·사설·미할당). (리뷰 N-10)
- **결과**: 서명된 위조 프로브가 38/39 수용 → **0 수용**(69개 프로브 실행, 68 `Corrupt` + XML 이 거부한 1건). 기존 저장 호환: `tools/csharp_check/run.sh`·`ending_check`·`ui_logic_check` 의 저장 왕복/호환 검사가 그대로 PASS.

### 8.2 리뷰 항목 수정 요약
| ID | 수정 | 회귀 단언 |
|---|---|---|
| N-1 | `UiKit.Label`, `VnStage` 이름·본문 `richText=false` | 소스 정적 확인 2(TMP 실제 렌더는 Unity 필요) |
| N-2 | `TryNumber` 가 NaN/Infinity/|값|>1e6 거부 | 4 |
| N-3 | 플래그 키 ≤64, 개수 ≤1000 | 3 |
| N-4 | 프로필·설정 `File.Replace` + `.bak` + `.corrupt` 보존 | 6 |
| N-5 | 세이브 `File.Replace`, 비정상 primary 는 `.corrupt` 로 치우고 정상 `.bak` 보존 | 4 |
| N-7 | `unity_compile_check/run.sh`: `mktemp -d`, 커밋 SHA·NuGet sha256 고정 | 정적 1 + 실제 실행(새 폴더, ALL OK) |
| N-8 | `.gitignore`·`package_source.sh` 정렬, docs 정정 | 정적 16 |
| N-6 | 검증기 부분만(F-7) | — (`LoadLatest` 예외 처리·`SavedAtUtc` 형식은 **미해결**) |
| N-9, N-11, N-12 | **미해결**(N-9 는 Unity 빌드 필요) | — |

### 8.3 최종 실측
| 항목 | 값 |
|---|---|
| 자가 테스트 | `SELFTEST: assertions all passed`, 종료 코드 0, **35,154 반복/케이스**(캠페인 3,000회 기준 유지 + 회귀 단언 추가), 약 64초 |
| 하네스 | 16 + 46 + 302 + 140 + 186 + 169 + 99 = **958 PASS**, 실패 0 |
| `release_check.sh --skip-unity` | PASS 16 / WARN 1 / SKIP 1 / FAIL 0 |
| `unity_compile_check` | `UNITY COMPILE CHECK: ALL OK` — 기본(새 임시 폴더) 실행과 `WORK=/tmp/unitycheck_repro` 실행 모두 |

### 8.4 남은 것(정직하게)
- 자가 테스트 발견 8건(F-1~F-8)은 **모두 수정**. 수동 리뷰 13건 중 **8건 수정(N-1·2·3·4·5·7·8·10)**, 1건 부분(N-6), 3건 미해결(N-9·N-11·N-12), 1건 설계상 수용(N-13).
- Unity 런타임(JsonUtility·TMP 렌더·에디터 빌드)에서는 여전히 미검증.

## 부록: 세이브 서명 재설계(QA-06) 이후 갱신 (2026-10-03)

- 서명이 "재직렬화 텍스트"에서 **버전별 정규 페이로드**(v2)로 바뀌어 새 캠페인 `save.signing`(9 단언, 직렬화기 JSON/XML 각각)을 추가했다: (1) 새 저장이 현재 버전으로 Ok, 두 직렬화기의 정규 페이로드 동일, (2) 구형 v1(텍스트 해시) 저장이 Ok + v2 로 마이그레이션되고 값을 고치면 거부, (3) 변조된 주 파일 + 정상 `.bak` → `RestoredFromBackup`, `Detail="primary: Tampered"`, `PrimaryRejected=true`, (4) 모든 `SaveData` 필드가 서명 목록 또는 `ExcludedFields` 에 있음(리플렉션).
- 변이 캠페인 상태 분포는 서명 방식이 바뀌어 약간 달라졌다(위 수치). 결론은 동일: 미처리 예외 0, hang 0, 서명이 맞고 내용이 바뀐 파일의 수락 0.
- 필드별 변조 전수(29개 변이)와 구형 3변형 로드는 `sh tools/csharp_check/qa_hunt/run.sh save`(23 checks)에서 확인한다.
