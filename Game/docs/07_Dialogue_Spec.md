# 07. 대화 모듈 명세 (MiniYarn / Dialogue)

> 대상: `UnityProject/Assets/_Project/Scripts/Dialogue/` (네임스페이스 `MorpheHouse.Dialogue`, 어셈블리 `MorpheHouse.Dialogue`, `noEngineReferences: true`).
> 근거: `01_TechDesign.md` §1.1(Yarn Spinner 채택), §9(로컬라이제이션), `00_GDD.md` §1.2(성별 중립 대사).
> 검증: `sh tools/csharp_check/dialogue_check/run.sh` (Mono `mcs`/`mono`, Unity 불필요).

## 0. 왜 MiniYarn 인가

실제 Yarn Spinner 패키지는 이 환경에서 설치·검증할 수 없다. 그래서 **Yarn 호환 부분집합**을 순수 C#으로 구현한 `MiniYarn` 으로 대사를 먼저 쓰고,
UI·게임 코드는 `IDialogueRunner` 에만 의존하게 한다. 나중에 Yarn Spinner 로 교체해도 `.yarn` 스크립트는 그대로 동작하도록, 문법은 부분집합 안에서만 쓴다(§8).

## 1. 지원하는 Yarn 부분집합

### 1.1 문법 표

| 기능 | 문법 | 비고 |
|---|---|---|
| 노드 | `title: Name` 헤더, `---` 본문 시작, `===` 끝 | title 필수. 파일 하나에 노드 여러 개 |
| 기타 헤더 | `tags: a b`, `position: 1,2` 등 `key: value` | `DialogueNode.Headers`/`Tags` 에 보관(실행에는 영향 없음) |
| 주석 | `// ...` | 줄 시작 또는 **공백 뒤**의 `//`. `http://x` 처럼 붙은 `//`, `{}`/`<< >>`/따옴표 안은 주석 아님 |
| 대사 | `speaker: 텍스트` | 화자 id = 글자/숫자/`_`(첫 글자는 글자), 콜론 뒤 **공백 필수**. 공백 있는 이름 불가 |
| 지문 | 화자 없는 줄 | `12:30 도착.`(숫자 시작)·`url http://..` 도 지문. `시간: ...` 처럼 `단어: ` 로 시작하면 화자로 해석되니 주의 |
| 태그 | 줄 끝 ` #line:ab12 #mood:sad` | 줄 **끝**의 공백으로 구분된 `#토큰`만. `line:` 은 `LineId`, 나머지는 `Tags`(전체 목록에는 `line:` 도 포함) |
| 인라인 표현식 | `{$gold}`, `{$a + 1}`, `{calls_player("howl")}` | `\{` `\}` 로 중괄호 이스케이프 |
| 이름 없는 식별자 | `{name}`, `{pronoun_subj}` | `$` 도 `(` 도 없는 식별자 = 인자 없는 **함수 호출**(`name()`). 호스트 함수/PronounResolver 가 처리 |
| 선택지 | `-> 텍스트` + 들여쓴 본문 | 같은 들여쓰기의 연속 `->` 가 한 묶음. 본문 끝나면 묶음 **다음 줄**로 이어짐. 중첩 가능 |
| 선택지 조건 | `-> 텍스트 <<if 식>>` | 거짓이면 목록에 **포함하되** `Available=false`. `#line` 태그는 `<<if>>` 앞/뒤 어느 쪽이든 가능 |
| 변수 대입 | `<<set $v to 식>>`, `<<set $v = 식>>`, `+=` `-=` `*=` `/=` | 변수 이름은 `$` 로 시작 |
| 선언 | `<<declare $v = 0 as number>>` | **무시**(타입 검사·기본값 없음). 정의 안 된 변수는 숫자 0 |
| 조건 | `<<if>> <<elseif>> <<else>> <<endif>>` | 중첩 가능 |
| 점프 | `<<jump NodeName>>` | 노드 이름 리터럴만 |
| 종료 | `<<stop>>` | 대화 즉시 종료. 노드 끝에 도달해도 종료 |
| 커스텀 커맨드 | `<<name arg arg>>` | 인자는 공백 구분, `"따옴표 인자"`, `{$v}` 보간 가능. 호스트가 처리하거나 `Command` 단계로 UI 에 전달 |

### 1.2 표현식

- 값: 숫자(double), 문자열 `"..."`(이스케이프 `\"` `\\` `\n`), 불 `true/false`, 변수 `$name`.
- 연산자(우선순위 높은 순): 단항 `- ! not` → `* / %` → `+ -` → `< > <= >= (gt lt gte lte)` → `== != (eq neq is)` → `&& and` → `|| or`.
- 함수 호출 `f(a, b)`: 러너 내장 → 호스트 순으로 조회.
  내장: `visited(node)` `visited_count(node)` `min` `max` `floor` `ceil` `round` `abs` `cap(str)`(첫 글자 대문자).
  호스트(예): `affinity("howl")` `flag("x")` + PronounResolver 함수(§5).
- 타입 규칙: `+` 는 한쪽이 문자열이면 연결. 산술은 숫자만(불·문자열은 오류). 숫자와 불의 `==` 는 1/0 으로 비교. 조건에서 숫자 0·빈 문자열·false 는 거짓.
  `not` 은 Yarn 과 같이 **높은 우선순위**(`not 1 > 2` 는 `(not 1) > 2` 라서 오류) → 괄호를 쓴다.
- `&&`/`||` 는 단락 평가.

### 1.3 지원하지 않는 것(known gaps)

| 구문/기능 | 동작 |
|---|---|
| `<<once>>` `<<endonce>>` `<<enum>>` `<<case>>` `<<endenum>>` `<<detour>>` `<<return>>` | **파싱 오류**(줄 번호 포함) |
| 변수 점프 `<<jump {$x}>>` | 파싱 오류 |
| 마크업 `[b]..[/b]`, `[select]`, `[plural]` 등, `{0}`·포맷 지정자 | 그대로 텍스트로 출력(처리 안 함) |
| `<<declare>>` 의 타입 검사·기본값 | 무시 |
| 내장 `random()` `dice()` `format()` 등 Yarn 표준 라이브러리 | 없음(필요하면 호스트 함수로 등록) |
| `<<wait>>` 등 Yarn 내장 커맨드의 자동 처리 | 일반 커스텀 커맨드로 취급 |
| 노드 헤더 `when:`(saliency), 노드 그룹 | 헤더로만 보관, 동작 없음 |
| 변수 타입 강제 | 없음(동적 타입) |
| 탭/스페이스 혼용 들여쓰기 | 탭=4칸으로 계산. 한 파일은 한 가지로 통일할 것 |
| `visited()` 의미 | 실제 Yarn 은 "완료"에 가깝지만 MiniYarn 은 **진입했는가** |
| 모든 선택지가 비활성 | 실제 Yarn 은 오류, MiniYarn 은 묶음을 건너뛰고 `Warnings` 에 기록 |

## 2. 러너 API

```csharp
public interface IDialogueRunner
{
    void Start(string nodeTitle);          // 진행 중 대화 폐기, 방문 기록 유지
    DialogueStep Next();                   // Line / Choices / Command / End
    void Choose(int index);                // Choices 후 필수. 비활성/범위 밖은 예외
    bool IsRunning { get; }
    string CurrentNode { get; }
    bool HasVisited(string nodeTitle);
    List<string> ExportVisited();          // 저장용(첫 방문 순서)
    void ImportVisited(IEnumerable<string> nodeTitles);
}
```

- `DialogueRunner(DialogueProject project, IVariableStore vars, IDialogueHost host)` (+ 4번째 인자 `ITextLocalizer`).
  `vars`/`host` 가 null 이면 메모리 저장소/빈 호스트. `Warnings` 로 비치명적 경고 확인.
- `DialogueProject.Parse(text, sourceName)` / `AddSource(text, sourceName)` 로 여러 `.yarn` 합치기. 같은 title 이 중복되면 **먼저 등록된 노드**가 실행됨(린트가 오류로 보고).
- `DialogueStep` (`Kind` 에 따라 필드 사용):
  - `Line`: `Speaker`(없으면 null=지문) `Text`(**보간·현지화 완료**) `LineId` `Tags`
  - `Choices`: `Options[]{Index, Text, Available, LineId, Tags}`. 러너는 `Choose(index)` 전까지 `Next()` 를 거부(`InvalidOperationException`)
  - `Command`: `CommandName`, `Args[]`(보간 완료 문자열)
  - `End`: 이후 `Next()` 는 계속 `End`
- `IVariableStore`(`TryGet`/`Set`, 이름은 `$` 포함): `InMemoryVariableStore`, `GameFlagsVariableStore(GameState)`.
  `GameFlagsVariableStore` 는 `$met_howl` → `GameState.Flags["met_howl"]`(int; 불=1/0, 숫자=반올림). 문자열 저장 불가.
  읽기 전용 가상 변수: `$gold $reputation $fatigue $day $chapter $aff_<id> $trust_<id>`(같은 키의 플래그가 있으면 플래그 우선). 쓰기는 예외 → 커맨드(`aff`/`trust`)로.
- `IDialogueHost`: `TryCallFunction(name,args,out result)`, `TryHandleCommand(name,args)`.
  `TryHandleCommand` 가 **true 면 커맨드를 즉시 처리한 것**이라 `Command` 단계가 UI 로 나오지 않는다. false 면 `Command` 단계로 전달.
  구현체: `SimpleDialogueHost`(람다 등록 + `AddProvider`), `NullDialogueHost`.
- `ITextLocalizer.TryGetText(lineId, out text)`: 줄/선택지의 `#line` id 로 조회, 없으면 **원문 폴백**. 현지화 텍스트도 `{}` 보간이 적용됨(구문 오류면 원문 + 경고). Unity Localization(`Yarn_ko`/`Yarn_en` String Table)은 이 인터페이스를 구현해 연결. 테스트용 `DictionaryTextLocalizer`.
- 오류: 구문 오류 `DialogueParseException`(`LineNumber`, `SourceName`, 메시지 `파일(줄): ...`), 실행 오류 `DialogueRuntimeException`(메시지 앞에 `[노드:줄]`).

### 기본 연결 예

```csharp
GameState state = ...; AddressBook book = AddressBook.FromCsv(addressCsvText);
PronounResolver pr = new PronounResolver(state, "ko", book);
SimpleDialogueHost host = new SimpleDialogueHost();
host.AddProvider(pr);
host.RegisterFunction("affinity", a => DialogueValue.FromNumber(state.AffinityOf(a[0].AsString)));
host.RegisterFunction("flag", a => { int v; return DialogueValue.FromBool(state.Flags.TryGetValue(a[0].AsString, out v) && v != 0); });
host.RegisterCommand("aff", a => /* Romance 모듈 호출 */ { });
IDialogueRunner runner = new DialogueRunner(project, new GameFlagsVariableStore(state), host, localizer);
```

## 3. 커맨드 카탈로그(관례)

러너는 커맨드 이름을 해석하지 않는다. 아래는 **작가-호스트 간 약속**이며, 호스트(Romance/UI 쪽 구현)가 처리한다.

| 커맨드 | 의미 |
|---|---|
| `<<aff <char> <n>>>` | 호감도 += n (음수 가능). 성별과 무관하게 같은 수치 규칙 |
| `<<trust <char> <n>>>` | 신뢰도 += n |
| `<<flag <name>>>` | 플래그 설정(= `<<set $name to true>>` 과 같은 키). 선택 인자로 값 허용 가능 |
| `<<show_standing <char> <expr> [outfit]>>` | 스탠딩 표정/의상 표시. 예 `show_standing howl smile casual` |
| `<<show_cg <id>>>` / `<<hide_cg>>` | CG 표시/숨김. 예 `show_cg HOWL_E1` |
| `<<bg <place> <time>>>` | 배경. 예 `bg lighthouse night` |
| `<<route_lock <char>>>` | 루트 확정 |
| `<<end_event <char> <step>>>` | 개인 이벤트 단계 완료 처리 |
| `<<route_lock <char> [<char> [<char>]]>>` | 루트 확정. 인자 1개 = 단독, 2~3개 = 공동(다자) 루트. 호스트가 자격(7장 이후, 호감 60·신뢰 20 이상)을 검증하고 미충족이면 경고만 남긴다 |
| `<<route_choice>>` | (호스트 전용) 루트 선택 화면을 연다. `Ch7_Intro` 끝에 한 번만 쓴다 |
| `<<hide_standing <char>>>` | 해당 캐릭터 스탠딩을 숨김(선택) |
| `<<show_cg <KEY>>>` | 인물 CG는 `<CHAR>_E<1..6>`, 그 밖의 공통 CG는 `HAREM_E1`, `COMMON_CE03` 처럼 대문자 키(파일 `CG_<KEY>.png`) |
| `<<bgm <id>>>` / `<<bgm_stop>>` / `<<se <id>>>` | 배경음/효과음(선택). id 는 `Resources/Audio/audio_manifest.csv`; 모르는 id 는 경고만 남기고 무음으로 진행. 현재 스크립트는 사용하지 않음 |

인자에 공백이 있으면 `"따옴표"`, 값은 `{$var}` 로 보간한다(`<<aff howl {$bonus}>>`).

## 4. 작성 규칙

1. 화자 id 는 **소문자 영문**: `howl johan beomjun haru gomseok nabi` (`CharIds`와 동일). 주인공은 `player`. 지문은 화자 없이 쓴다.
2. 모든 대사·선택지 줄에 `#line:` id 를 둔다. 비어 있으면 `LineIdTagger.AddMissing(text, name)` 으로 자동 부여(`#line:<FNV-1a 8hex>`, 노드 제목 + 노드 내 순번에서 결정 → 재실행해도 같은 결과, 이미 붙은 id 는 불변, 멱등). id 를 손으로 바꾸면 번역 키가 끊어지니 편집 후에는 린트로 확인.
3. 노드 이름은 `<캐릭터>_<이벤트>_<단계>` 처럼 유일하게(예 `Howl_E1_Intro`). 다른 파일 포함 전체에서 중복 금지.
4. 주인공 대사/지문은 성별 중립으로. 사람 호칭은 `{name}`, 대명사는 `{pronoun_subj()}` 등 함수로(§5). 영어 `they` 는 동사 일치를 자동 처리하지 않으므로 `they are` 같은 문형을 피한다.
5. 한국어 조사(이/가, 을/를)는 자동 처리하지 않는다. `{name}` 뒤에는 조사 없는 문형(“{name}, 왔어?”)이나 호칭 함수를 쓴다.
6. 상태 변경은 커맨드(`aff`, `trust`, `flag`)로만 하고, 스크립트 안의 `<<set>>` 은 대화 내 임시 값·플래그에 쓴다.
7. 린트(§6)에 오류(DUP_NODE/UNDEFINED_JUMP/DUP_LINE_ID)가 없어야 커밋한다.

## 5. 대명사·호칭

### 5.1 PronounResolver

`new PronounResolver(gender, locale, name)` 또는 `new PronounResolver(GameState, locale, AddressBook)`(매 호출마다 상태 읽음). 성별 `male|female|nonbinary|neutral`(그 외 → neutral), 로케일 `ko`/`en*`(그 외 → ko).

| 함수 | ko | en (male / female / nb·neutral) |
|---|---|---|
| `pronoun_subj()` | `{name}` | he / she / they |
| `pronoun_obj()` | `{name}` | him / her / them |
| `pronoun_poss()` | `{name}의` | his / her / their |
| `honorific()` | `{name} 씨` | Mr. {name} / Ms. {name} / Mx. {name} |
| `name()` · `player_name()` | 주인공 이름(비었으면 `당신`) | 이름(비었으면 `you`) |

`{name}` 처럼 `$` 없이 쓸 수 있고 `{pronoun_subj}`(괄호 생략)도 동작. 문장 시작은 `{cap(pronoun_subj())}`.

### 5.2 호칭 표 `Resources/Dialogue/address_terms.csv`

열: `char, player_gender, band, calls_player_ko, player_calls_ko, calls_player_en, player_calls_en`. 6캐릭터 × 4성별 × 3구간 = 72행. 용어 안의 `{name}` 은 주인공 이름으로 치환.

- 구간(호감도 기준, 성별 무관): `stranger` < 30, `friendly` 30~59, `close` >= 60 (`AddressBook.BandOf`).
- 스크립트 함수: `calls_player("howl")`(NPC 가 주인공을 부르는 말), `player_calls("howl")`(주인공이 NPC 를 부르는 말), `affinity_band("howl")`. 호감도는 PronounResolver 에 연결된 `GameState.AffinityOf`.
- 표에 없으면 폴백: `calls_player` → `{name} 씨`/이름, `player_calls` → 캐릭터 id.
- 예: 남성 주인공 → 하울: `하울 씨`(stranger/friendly), `하울 형`(close). 여성 주인공 → 하울: `하울 씨` → `하울`. 여성 주인공 → 범준 close: `범준 오빠`(호탕한 큰형 성격에 맞는 경우만). 논바이너리/중립 주인공: 항상 이름·`~ 씨`·직함(`범준 관장님`, `곰석 셰프님`) 같은 중립형. 하루는 **모든 성별**에게 `하루 씨`.
- 원칙: 호감도 증감·이벤트 해금 같은 **수치 규칙은 성별과 무관**(모든 인물은 주인공 성별과 상관없이 공략 가능). 성별에 따라 달라지는 것은 호칭/어조뿐이다. 성별 고정관념 애칭·놀림은 쓰지 않는다.
- 검증(하네스): 6×4×3×2 조합이 비어 있지 않고 `{name}` 이 남지 않음, nonbinary/neutral 행·하루 행에 성별 호칭(형·오빠·누나·언니·아저씨·sir·ma'am·Mr./Ms. 등) 없음, 구간 경계 29.9/30/59.9/60 이 성별과 무관.

## 6. 린트 / 태거

`DialogueLint.Run(project, entryNodes)` → `List<LintIssue>`(`Severity, Code, Message, Node, Source, Line`).

| 코드 | 심각도 | 의미 |
|---|---|---|
| `DUP_NODE` | Error | 같은 title 의 노드 중복(파일 간 포함) |
| `UNDEFINED_JUMP` | Error | `<<jump X>>` 대상 없음(if/선택지 안 포함) |
| `UNDEFINED_ENTRY` | Error | entry 목록에 없는 노드 |
| `UNREACHABLE` | Warning | entry 노드에서 점프로 도달 불가(게임 코드가 직접 `Start` 하는 노드는 entry 에 포함) |
| `DUP_LINE_ID` | Error | `#line:` id 중복(프로젝트 전체) |
| `MISSING_LINE_ID` | Warning | id 없는 대사/선택지 |

`LineIdTagger.AddMissing(text, sourceName)` — id 없는 줄 끝(주석 앞)에 ` #line:xxxxxxxx` 추가한 사본 반환. CRLF·BOM 보존, 파싱 오류면 `DialogueParseException`.

## 7. 샘플·검증

`tools/csharp_check/dialogue_check/samples/*.yarn`: `syntax_all`(모든 문법), `branching`(visited/점프), `howl_intro`(커맨드·호칭), `pronouns`, `lint_broken`(의도적으로 깨진 스크립트).
`run.sh` 는 Core+Data+Dialogue 를 `mcs` 로 컴파일해 파싱·실행·if/중첩 선택지·커맨드·visited·대명사 8조합·현지화 폴백·GameFlags·린트·태거 멱등성·파싱 오류 줄 번호·호칭 표·2,000줄 스트레스를 검사한다.

## 8. 실제 Yarn Spinner 로의 이관 메모

1:1 로 대응(그대로 동작):
- 노드/헤더/`---`/`===`, 대사 `speaker: text`, `#line:` 태그, `-> ` 선택지(중첩·`<<if>>` 조건), `<<set>>/<<declare>>/<<if>>/<<elseif>>/<<else>>/<<endif>>/<<jump>>/<<stop>>`, 인라인 `{$v}`, 연산자(`and or not eq neq gt lt gte lte`), `visited("Node")`, 커스텀 `<<command args>>`.
- `IDialogueHost.TryCallFunction` ↔ `DialogueRunner.AddFunction`, 커맨드 ↔ `AddCommandHandler`, `IVariableStore` ↔ `VariableStorageBehaviour` 서브클래스, `ITextLocalizer` ↔ Unity Localization 라인 프로바이더(`#line:` id → String Table 키).
- `DialogueStep` 의 Line/Choices/Command/End ↔ `OnLineDeliver`/`OnPrepareForLines`/`OnOptionsStart`/`OnCommand`/`OnDialogueComplete`.

이관 시 손볼 곳:
- 실제 Yarn 은 변수를 **`<<declare>>` 로 선언**해야 한다(MiniYarn 은 무시). 이관 전 스크립트 상단에 선언을 추가하는 도구/규칙 필요. 값 타입은 엄격(숫자/문자열/불) — MiniYarn 의 숫자-불 호환(`flag` 가 1/0)은 불로 바꿔 줄 것.
- `{name}`(식별자만 있는 인라인)·`{pronoun_subj}`(괄호 생략)은 Yarn 에서 불가 → `{name()}` 형태로 쓰거나 Yarn 쪽 변수/함수로 변환. 권장: **처음부터 `{name()}`/`{pronoun_subj()}` 로 작성**.
- `visited()` 는 Yarn 에서 노드 *완료* 기준, `cap()`·`affinity_band()`·`calls_player()` 등은 함수로 재등록.
- `<<jump>>` 의 `{$expr}`, `<<once>>`, `<<detour>>`, 마크업은 MiniYarn 에 없으므로 이관 후에만 사용.
- `IDialogueRunner` 뒤에서 구현만 갈아끼우면 UI 변경은 없어야 한다(`Next()` 풀 방식 ↔ Yarn 의 이벤트 방식은 어댑터가 큐로 변환).
