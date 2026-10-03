# 19. 보안·개인정보 수동 리뷰 (Whole-repo Manual Review)

> 기준일 2026-10-03 · 리뷰어: 수동 코드 리뷰(AppSec 관점) · 대상: `UnityProject/Assets/_Project/Scripts/**`, `tools/**`, `.gitignore`, `docs/legal/*`, `portfolio/SECURITY_DESIGN.md`
> 이 문서는 **읽기 전용 리뷰의 결과물**이며 코드는 수정하지 않았다. 다른 작업자가 동시에 수정 중인 파일이 있어(아래 §6) 줄 번호는 리뷰 시점 기준 근사값이다.
> 결론 요약: **Critical/High/Medium 없음.** 이 위협 모델(오프라인·단일 사용자·계정/네트워크 없음)에서 실질적 위험은 낮고, 발견 사항은 대부분 Low/Info(방어 심층·문서 정확성·개발 도구 공급망)이다.

## 1. 범위와 방법

| 항목 | 내용 |
|---|---|
| 읽은 것 | `docs/01_TechDesign.md` §11, `portfolio/SECURITY_DESIGN.md` 전체, `Scripts/Save`, `Data`(CsvTable·BalanceTable·ProtagonistProfile), `Game`(MiniJson·ProfileService·Settings·DialogueCommandDispatcher·DialogueProjectLoader·ArtNames·AudioCatalog·UiStrings), `Dialogue`(TextTemplate·Runner·Hosts·PronounResolver·AddressBook), `UI`(UiKit·VnScreen·VnStage·CharacterCreate·Title·AppContext), `Boot`, `Editor`(BuildTools·BuildPreflight·DialogueTools), `tools/*.sh|py`, `.gitignore`, `docs/legal/PRIVACY_POLICY_DRAFT.md`, `docs/18_Release_Guide.md` |
| 못 본 것 | `UnityProject/ProjectSettings`·`Packages`·AndroidManifest(저장소에 없음) → Android 권한·백업·키스토어 저장 위치는 **검증 불가**. TMP/JsonUtility 내부 동작은 Unity 없이 시험 불가. YarnParser/DialogueExpression 내부는 "알려진 항목"(§6) 위주로만 확인 |
| 실행한 증명(Proof) | mcs/mono 로 실제 소스를 컴파일해 실행(§4 각 항목). 스크래치 코드는 저장소 밖에 둠 |
| 분류 | CWE 기반. 심각도는 "이 게임의 위협 모델" 기준 상대값 |

## 2. 위협 모델 가정

- 자산: 세이브(`slot0..3.json`+`.bak`), `profile.json`, `settings.json`, 번들 콘텐츠(`.yarn`, CSV), 개발자 PC·서명 키, 플레이어 이름(개인정보로 볼 수 있는 유일한 입력).
- 공격자: (A) 자기 세이브를 고치는 플레이어, (B) **다른 사람이 공유한 세이브/폴더**(변조·악성 파일), (C) 우발 손상(전원 차단·디스크 오류), (D) 작성 실수(콘텐츠), (E) 개발 도구·빌드 공급망.
- 범위 밖: 서버·결제·멀티플레이, 치트 방지(HMAC 키가 클라이언트 소스에 있음은 설계상 수용), 루팅 기기.
- 런타임 콘텐츠(`.yarn`·CSV)는 **번들 리소스**이며 외부에서 로드하지 않는다(확인: 코드에 `Resources.Load`/`TextAsset` 외 로더 없음, `UnityWebRequest|HttpClient|Socket|System.Net|Process|Activator|Assembly|PlayerPrefs` 0건 — Editor 의 `git rev-parse` 제외).

## 3. 발견 사항 표 (심각도 순)

| ID | 심각도 | CWE | 요약 | 위치 | 확신도 |
|---|---|---|---|---|---|
| N-1 | Low | CWE-79 유사(TMP 리치텍스트)/CWE-74 | 모든 TMP 라벨이 `richText` 기본값(true). 이름 검증은 새 게임 생성 때만 → **공유된 세이브**의 이름 `<size=999>` 등이 그대로 렌더 | `UI/UiKit.cs:63`, `UI/VnStage.cs:251-252`, `Save/SaveValidator.cs:24` | 높음(검증 증명) / 중간(TMP 렌더 영향) |
| N-2 | Low | CWE-1284/CWE-20 | `<<aff howl NaN>>`·`<<flag k 1e30>>` 가 NaN/`int.MinValue` 상태를 만든다 → 이후 `SaveValidator` 가 거부해 **저장 불가** | `Game/DialogueCommandDispatcher.cs:109,128,267`, `Data/CsvTable.cs:37` | 높음(증명) |
| N-3 | Low | CWE-770 | 콘텐츠가 만드는 플래그 수·키 길이에 상한 없음 ↔ 저장 상한(1024/64). 3,001개에서 저장 거부 | `Game/DialogueCommandDispatcher.cs:~120`, `Save/SaveValidator.cs` | 높음(증명) |
| N-4 | Low | CWE-367/CWE-459 | 프로필·설정 쓰기가 `Delete → Move`(백업 없음), 고정 `.tmp` 이름, 크기 초과/손상 시 빈 값으로 **덮어씀** | `Game/ProfileService.cs:77-80`, `Game/Settings.cs:72-75` | 높음 |
| N-5 | Low | CWE-367/CWE-664 | 세이브 쓰기: 검증 전 `.bak` 를 현재(손상 가능) primary 로 덮어씀, `Copy→Delete→Move` 비원자 | `Save/SaveService.cs:71-79` | 높음 |
| N-6 | Low | CWE-754/CWE-20 | `LoadLatest` 가 예외에 무방비(FromSave·BalanceTable·DayCycle)이고 "최신" 판정을 검증 안 된 `SavedAtUtc` 문자열 비교로 함 | `UI/AppContext.cs:262-292` | 중간 |
| N-7 | Low | CWE-829/CWE-377/CWE-494 | `unity_compile_check/run.sh`: 미고정 브랜치/HEAD 클론, NuGet 해시 미검증, 예측 가능한 `/tmp/unitycheck_work` 재사용, 내려받은 코드를 `dotnet build` 로 실행 | `tools/unity_compile_check/run.sh:12-20` | 높음 |
| N-8 | Low | CWE-540/CWE-1295 | `.gitignore` 가 문서가 주장하는 것보다 훨씬 좁음(7줄). `package_source.sh` 비밀 필터도 `release_check.sh` 보다 좁음 | `.gitignore`, `tools/package_source.sh:30` | 높음 |
| N-9 | Low(문서/프라이버시) | CWE-359 유사 | 개인정보 처리방침 초안의 "외부 전송 없음"이 Android **자동 백업**(allowBackup 기본 true)·**개발 빌드 INTERNET 권한 자동 추가**와 충돌할 수 있음. 매니페스트 미확인 | `docs/legal/PRIVACY_POLICY_DRAFT.md` §2·§5, `01_TechDesign.md` §11 | 중간(검증 불가) |
| N-10 | Info | CWE-20 | 이름 검증이 U+2028/2029, U+061C, U+2060, U+206A–206F, U+00AD, 사설영역을 허용(방향·줄바꿈·보이지 않는 문자) | `Data/ProtagonistProfile.cs:79-82,135` | 높음(증명) |
| N-11 | Info | CWE-489 | 릴리스 타이틀에 "자동 플레이 검증 (개발용)" 버튼이 무조건 생성됨 | `UI/TitleScreen.cs:55` | 높음 |
| N-12 | Info | CWE-22/CWE-426 | BuildTools: `Sanitize` 가 `..` 통과, `git` 를 PATH 에서 실행, `PlayerSettings.Android.keystorePass` 가 ProjectSettings 에 남는지 미확인 | `Editor/BuildTools.cs:60,115,159,187` | 낮음~중간 |
| N-13 | Info | — | HMAC 키 상수·공개(문서화된 한계), 상수 시간 비교 OK, 레거시 해시 폴백 존재 | `Save/SaveIntegrity.cs:14`, `Save/SaveService.cs:215` | 높음 |

심각도 집계: Critical 0 · High 0 · Medium 0 · Low 9 · Info 4.

## 4. 상세 발견

### N-1 (Low) TMP 리치텍스트: 이름 태그가 이스케이프 없이 TMP 로 간다
- **현황**: `UiKit.Label` 은 `tmp.text = text` 만 하고 `richText` 를 끄지 않는다(TMP 기본 true). `VnStage` 도 `_nameText/_bodyText.text` 에 그대로 대입. 화면 표시 이름은 `PlayerName`(PronounResolver `{name}`/`honorific`, VnScreen:194 `display`)이 그대로 들어간다.
- **방어**: `ProfileValidator.CheckName` 이 `<>{}\` 를 금지 → **새 게임 생성 경로에서는 태그 주입 불가**(증명 P2: `<size=999>A`, `<link=x>`, `<sprite=0>` 모두 `name_invalid_char`).
- **구멍**: 로드 경로. `SaveValidator.Validate` 는 `PlayerName` 에 길이(≤32)만 검사(증명 P2: `"<size=999><link=x>"` → 수락). HMAC 키는 소스 상수라 누구나 서명 가능. 시나리오: 커뮤니티에 공유된 세이브(공격자 B)를 이어하기 → 모든 화면에서 `<size=99999>`·`<voffset>`·`<cspace>` 로 UI 레이아웃 파괴(해당 세이브를 지우면 복구). `<link>` 는 핸들러가 없어 동작 없음, `<font="…">`/`<sprite>` 는 Resources 내 에셋만 참조 가능 → 정보 유출·코드 실행 경로 없음. **현실 영향: UI 가용성/표시 위조, 낮음.**
- **참고**: 이름 검증 강화는 §6 에서 이미 진행 중이므로, 여기서는 **출력 측 방어(richText 끄기)** 와 로드 경로 검증 공유를 제안.
- **패치**:
```csharp
// UiKit.Label: 기본을 안전하게. 의도적으로 태그를 쓰는 곳만 opt-in.
tmp.richText = false;
// 또는 사용자 입력이 섞이는 라벨(VnStage name/body, HUD)만:
_nameText.richText = false;
// SaveValidator.Validate 에서 생성 화면과 같은 규칙 재사용
if (MorpheHouse.Data.ProfileValidator.CheckName(d.PlayerName).Length != 0) return "playerName";
```
  콘텐츠 대사가 의도적으로 `<b>`/`<i>` 를 쓰는지 `grep -rhoE "<[a-z/][a-z]*[ =>]" Dialogue --include=*.yarn` 로 확인한 결과(리뷰 시점 `<<aff|bg|flag|if|else|jump|stop…>>` 커맨드뿐이고 TMP 서식 태그 `<b>`/`<i>`/`<color>` 사용 없음) 본문은 `richText=false` 로 두어도 된다.
- **Yarn 보간 주입은 불가능**(증명 P1): 이름이 `{1+1}`, `{$x}`, `\{`, `<<jump Evil>>`, `#tag:1`, `Hi: {name}`, `}{` 인 상태로 실제 `DialogueRunner` 를 돌려도 출력은 리터럴(`안녕 {1+1}!`)이고 `Evil` 노드로 점프하지 않으며, `<<flag {name}>>` 의 인자도 리터럴 한 토큰. 이유: `TextTemplate` 은 파싱 시점에 `{}` 를 분해하고 `Render` 는 값을 **재파싱하지 않는다**.

### N-2 (Low) 콘텐츠 숫자 인자의 NaN/Infinity/오버플로
- `TryNumber` = `float.TryParse(..., NumberStyles.Float, Invariant)` 는 `"NaN"`, `"Infinity"` 를 받는다. 증명 P3/Q: `<<aff howl NaN>>` → `Aff=NaN`(`RomanceRules.Clamp(NaN)=NaN`), `<<trust howl Infinity>>` 는 상한(8)으로 클램프됨, `<<flag big 1e30>>` → `(int)Math.Round(1e30f)` = `-2147483648`(C# 미정의 동작, 값이 `int.MinValue`).
- 도달성: **번들 시나리오 작성 실수에서만**(플레이어 입력 아님). 영향: NaN 호감도가 생기면 `SaveValidator` 가 `aff` 로 거부 → 그 판은 **저장/자동저장이 계속 실패**(`SaveGame` 은 `false`).
- **패치**:
```csharp
private static bool TryNumber(string text, out float value)
{
    return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 1e6f;
}
```
  `CsvRow.Int` 도 동일하게 `Math.Abs(value) < int.MaxValue` 확인 후 캐스트(번들 CSV 라 우선순위 낮음). `DialogueLint` 에 `aff|trust|flag` 숫자 리터럴 범위 검사를 추가하면 작성 시점에 잡힌다.

### N-3 (Low) 플래그 수/키 길이 상한 불일치
- 증명 Q: `<<flag k0>>…<<flag k2999>>` 후 `Flags=3001`, `SaveValidator` → `"list size"`(상한 1024). 키 > 64자도 `flag key` 로 거부. 콘텐츠는 번들이고 현재 플래그 수는 이보다 훨씬 적어 **실제 도달은 작성 실수 한정**.
- **패치**: `RomanceService.SetFlag` 에서 `key.Length > 64 || Flags.Count >= 1024` 이면 경고 후 무시(디스패처 `Warn`), 또는 린트에 플래그 이름 길이/총수 검사.

### N-4 (Low) 프로필·설정 파일: 쓰기 창과 덮어쓰기
- `File.Delete(_path); File.Move(tmp, _path)` 사이에 크래시하면 `_path` 가 없음(`.tmp` 만 남음) — 읽기는 `.tmp` 를 보지 않으므로 **엔딩 갤러리/설정 소실**. 세이브와 달리 `.bak` 없음. 또한 크기 초과(`"<file too large>"`)·파싱 실패는 `Corrupt` → 빈 프로필로 시작하고 다음 `Save()` 가 원본을 **그대로 덮어써 복구 불가**.
- 영향: 진행 핵심이 아닌 메타 데이터 → Low.
- **패치**:
```csharp
// 가능하면 원자적 교체(Windows/Mono 지원). 실패 시 현재 방식으로 폴백.
try { File.Replace(tmp, _path, _path + ".bak"); }
catch (Exception) when (!File.Exists(_path)) { File.Move(tmp, _path); }
// Corrupt 로 판정된 파일은 덮어쓰기 전 "<name>.corrupt" 로 이름 변경(1회)해 보존.
```

### N-5 (Low) 세이브 쓰기 순서
- `Write`: `WriteAllText(tmp)` → `Copy(path→bak, overwrite)` → `Delete(path)` → `Move(tmp→path)`. (경로·크기 상한 수정은 이미 반영됨 — §6.)
- 문제 ① `bak` 를 **검증 없이** 현재 primary 로 덮어쓴다. primary 가 손상/변조 상태였다면 직전 정상 백업이 사라진다(이후 `Move` 전 크래시 시 정상본 없음). ② `.tmp` 쓰기 실패(디스크 가득) 시 잔여 `.tmp` 정리 없음. 크래시 창 자체는 `bak` 로 복구되어(`Load` 가 primary 없음→bak) 설계대로 동작 — **데이터 손실은 ①의 겹침 조건에서만**.
- **패치**: `File.Replace(tmp, path, bak)` 사용(원자적, bak 갱신 포함), 폴백에서만 수동 순서. 수동 순서 유지 시 `bak` 갱신 전에 primary 가 파싱·서명 검증을 통과하는지 확인하는 콜백을 `SaveService` 에서 넘긴다.

### N-6 (Low) 이어하기 경로의 예외 안전성·"최신" 판정
- `AppContext.LoadLatest`: `Saves.Load` 는 예외를 안 던지지만 그 뒤 `BalanceTable.Load` / `SaveMapper.FromSave` / `Balance.Chapter(State.Chapter)`(KeyNotFound) / `DayCycle` 는 try/catch 없음 → 버튼 핸들러에서 예외, UI 는 "이어하기" 후 멈춘 듯 보임. 서명된 위조/버전 불일치(챕터 수가 CSV 보다 큰 등) 세이브로 도달 가능.
- `SavedAtUtc` 는 길이·형식 미검증(§6 F-7) 상태로 `string.CompareOrdinal` 로 최신을 고른다 → 서명 가능한 위조 파일이 항상 "최신"(예 `"~"`)이 되어 정상 세이브를 가린다. `LockedRoute` 도 `CharIds` 화이트리스트 검증이 없어 임의 문자열이면 **모든 인물이 `IsLockedOut`** (게임 로직 고착).
- **패치**:
```csharp
// LoadLatest 본문을 try { ... } catch (Exception ex) { Debug.LogException(ex); return new LoadResult{Status=LoadStatus.Corrupt}; }
// SaveValidator: DateTime.TryParse(d.SavedAtUtc, Invariant, RoundtripKind) 필요, LockedRoute/LockedGroup/Chars.Id 는 Array.IndexOf(CharIds.All, id) >= 0, Chapter ≤ chapters.csv 행 수(로드 후 별도 검사)
```

### N-7 (Low) `tools/unity_compile_check/run.sh` 공급망·임시 경로
- 라인 12–20: `W="${WORK:-/tmp/unitycheck_work}"`(예측 가능, **이미 존재하면 그대로 신뢰** `[ ! -d um ]`), `curl` 로 받은 nupkg 해시 검증 없음, `git clone --depth 1 -b 6000.0 …uGUI`, `…InputSystem`(HEAD) **미고정**, 이후 `dotnet build` 가 내려받은 `csproj/targets/소스`를 실행 → 상류 변조 또는 같은 PC 의 다른 사용자(미리 `/tmp/unitycheck_work/um` 생성)가 **개발자 권한 코드 실행** 가능(CWE-829/377/494). `release_check.sh` 는 `--fast` 가 아니면 기본으로 호출한다.
- 위협 모델상 개발 도구(배포물 아님, 단일 개발자)라 Low. SECURITY_DESIGN §4.5 가 "미고정"을 이미 인정.
- **패치**:
```sh
W="${WORK:-$(mktemp -d "${TMPDIR:-/tmp}/unitycheck.XXXXXX")}"   # 새 비공개 디렉터리 (또는 ~/.cache/morphehouse 에 0700)
UM_SHA256="<고정 해시>"; echo "$UM_SHA256  um.nupkg" | sha256sum -c - || exit 1
git clone -q --depth 1 https://github.com/Unity-Technologies/uGUI.git ugui6 && git -C ugui6 checkout --detach <커밋SHA>
# 캐시 재사용 시에도 `git -C ugui6 rev-parse HEAD` 가 고정 SHA 와 같은지 확인
```

### N-8 (Low) `.gitignore`·비밀 필터가 주장보다 좁음
- 실제 `.gitignore` = `/dist/ __pycache__/ *.pyc *.jks *.keystore *.pfx *.p12 .env` (7줄). 그러나 `SECURITY_DESIGN.md` §4.4 는 "`.gitignore`(Library·Temp·Logs·`*.apk`·`*.aab` 등)", `docs/18_Release_Guide.md` 는 "`*.apk`, `*.aab` 는 있으나 키스토어 확장자는 없으므로"라고 서술 → **문서와 현실이 반대**(키스토어는 있고 apk/aab/Library/Builds 는 없음). Unity 프로젝트 생성 후 `Library/`, `Builds/`(서명된 AAB·APK 와 `BUILD_INFO.txt` 출력 위치, `BuildTools.cs:60`), `UserSettings/`, `Logs/`, `obj/` 가 커밋될 위험.
- 비밀 패턴 누락: `*.pem *.key *.p8 keystore*.properties local.properties .env.* google-services.json *.keystore.*`. `release_check.sh` `step_secrets` 는 `keystore*.properties` 까지 보지만 `package_source.sh:30` 의 차단 정규식(`keystore|jks|pfx|p12|.env`)은 더 좁다(비-git 모드 `find` 는 `.pem/.key` 를 포함해 tar 에 넣을 수 있음).
- **패치**: 표준 Unity `.gitignore` 병합 + `Builds/ Library/ Temp/ Logs/ UserSettings/ obj/ *.apk *.aab *.pem *.key *.p8 keystore*.properties local.properties .env.*`, `package_source.sh` 의 정규식을 `release_check.sh` 와 동일하게 맞추고, 문서(§4.4·docs/18)의 서술을 사실에 맞게 수정.

### N-9 (Low, 문서/프라이버시) 처리방침과 Android 기본 동작
- 저장 데이터(주인공 이름 포함)는 `Application.persistentDataPath` 의 파일. Android 의 **Auto Backup**(매니페스트 `allowBackup` 기본 true, Unity 기본 템플릿이 끄지 않음)은 앱 데이터를 사용자 Google 계정 Drive 로 업로드할 수 있다 → 처리방침 §2 "외부로 전송되지 않음"은 **사실과 다를 수 있다**(플랫폼 기능이므로 개발자가 수집하지는 않지만 서술은 정확해야 함). 또 Unity 는 **Development Build 에 `INTERNET` 권한을 자동 추가**하고, 릴리스도 Player Settings > Internet Access 가 Auto 이면 네트워크 API 사용 감지 시 추가한다. 이 저장소에는 `ProjectSettings`/매니페스트가 없어 **검증 불가**(§1).
- **패치/절차**: 커스텀 `AndroidManifest.xml` 로 `android:allowBackup="false"`(또는 `dataExtractionRules`/`fullBackupContent` 로 `saves/`·`profile.json` 제외), Internet Access = Require 가 아닌 **Not Required**, 최종 AAB 에 `aapt2 dump permissions` 로 `INTERNET` 부재 확인 후 처리방침 §5 의 `[TODO]` 해소. 백업을 켠다면 처리방침에 "OS 백업" 문구 추가.
- 로그: `Debug.Log*` 호출을 전수 확인 — 플레이어 이름·세이브 내용을 로그에 쓰는 곳 **없음**(검증 실패 메시지는 필드 이름만, 예 `refusing to save invalid data: playerName`). `LogException` 의 예외 메시지에도 값이 들어가지 않음. 개인정보 저장 항목: 세이브(이름·성별 선택·종·외형·플레이시간 `PlayTimeSec`·`SavedAtUtc`), 프로필/설정에는 이름 없음 — SECURITY_DESIGN §4.3 과 일치.

### N-10 (Info) 이름 검증의 문자 범주 허용
- 증명 P2: `CheckName` 이 빈 문자열(허용)을 반환한 입력 — U+2028(줄 구분자, TMP 가 줄바꿈 처리), U+061C(아랍어 글자 표식, 방향 영향), U+2060(단어 결합자), U+206A–206F(폐기된 서식 문자), U+00AD(소프트 하이픈), U+E000(사설영역), 전각 `＜＞`(TMP 에서 태그 아님 → 무해), `&lt;`/`[b]`(TMP 에서 무해).
- 영향: 보이지 않는/방향·줄바꿈 문자로 이름 표시 위조(자기 자신 화면에만). 이름 검증 수정 작업(§6)에 반영 권장:
```csharp
static bool IsInvisibleOrBidi(char c) => ... || c == '؜' || c == ' ' || c == ' '
    || c == '⁠' || (c >= '⁪' && c <= '⁯') || c == '­' || (c >= '' && c <= '');
// 더 간단: char.GetUnicodeCategory 가 Format/LineSeparator/ParagraphSeparator/PrivateUse/Control/OtherNotAssigned 이면 거부
```

### N-11 (Info) 개발용 버튼
- `TitleScreen.cs:55` 가 모든 빌드에서 "자동 플레이 검증 (개발용)" 을 노출한다(`#if UNITY_EDITOR` 는 216행 일부만 감쌈). 실행해도 밸런스 시뮬레이션과 `Debug.Log` 뿐이라 보안 영향은 없으나 릴리스 UX·최소 노출 원칙상 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 로 감싸고 `BuildPreflight` 에 검사 항목 추가.

### N-12 (Info) 빌드 스크립트
- `Sanitize(version)`: `Path.GetInvalidFileNameChars()` 치환만 하므로 `..`/`.` 은 통과 → `Builds/<플랫폼>/..` (개발자 자신의 `bundleVersion` 이라 외부 입력 아님). 다만 `BuildPreflight.cs:213-214` 가 semver(`^\d+\.\d+\.\d+$`) 아니면 **Fail** 로 빌드를 중단하므로 실제로는 `..` 에 도달하지 못한다(방어 OK, `Sanitize` 단독으로는 불충분하다는 점만 기록).
- `GitCommit()` 은 PATH 의 `git` 실행(`UseShellExecute=false`, 인자 고정 → 명령 주입 없음, CWE-78 해당 없음, PATH 하이재킹만 이론상).
- 환경변수 서명 정보: 코드·저장소에 비밀 없음 ✔. 다만 `PlayerSettings.Android.keystorePass/keyaliasPass` 대입이 `ProjectSettings/ProjectSettings.asset` 에 **비밀번호를 영속화하는지** Unity 6 에서 확인 필요 — `ProjectSettings` 를 커밋하기 전 `grep -i "keystorePass\|keyaliasPass"` 로 점검하고, 가능하면 CLI 인자(`-androidKeystorePass` 등 CI 비밀 주입)로 옮길 것. 키스토어 경로(`MORPHE_KEYSTORE_PATH`)가 asset 에 절대경로로 남는 점도 확인.
- `BUILD_INFO.txt` 의 SHA-256 은 산출물과 같은 폴더에 있으므로 docs/18 의 지침대로 **별도 경로로 게시**할 때만 의미가 있다(이미 문서화됨).

### N-13 (Info) `SaveIntegrity`
- 키: `SHA256("MorpheHouse|save|v1|moonlight-corner")` — 소스 상수, 누구나 서명 가능(문서화된 한계, DRM 아님).
- 서명 범위(2026-10-03 재설계, QA-06): v2 부터 `SaveIntegrity.CanonicalPayload` — 버전별 명시 필드 목록의 정규 문자열(직렬화기 독립). 선택 필드 추가가 구 저장 서명을 깨지 않는다. v1 저장은 `SaveService.VerifyLegacyV1`(구형 텍스트 해시 3변형)로 검증 후 `SaveMigrations` 로 v2 로 올린다. 변조된 주 파일을 `.bak` 로 대체할 때 `LoadResult.PrimaryRejected`/`Detail` 로 통지.
- 비교: `ConstantTimeEquals` 는 길이가 다르면 즉시 false(길이는 비밀 아님), 그 외 `diff |=` 로 상수 시간 ✔. 16진 문자 대소문자 구분(항상 소문자 생성) ✔.
- 권고(선택): 현 상태 유지가 합리적. "변조 방지"가 아니라 "손상·단순 편집 탐지"로만 서술하고 있어 설명은 정확.

## 5. 검토했고 문제없음 (OK)

| 영역 | 확인 내용 |
|---|---|
| 콘텐츠 문자열 → 파일/리소스 경로 (CWE-22) | `ArtNames` 가 표정·의상·장소·시간을 **허용 목록**으로만 해석, CG id 는 `[A-Z0-9_]`·48자·첫 글자 영문(`TryParseCommonCgKey`)·인물 CG 는 `CharIds.All` 매칭 → `ArtLoader` 경로에 `/`·`..` 불가. `AudioCatalog` 는 id 정규식 + path 의 `..`/선행 `/`/`:` 거부. `Resources.Load` 는 어차피 Resources 밖으로 못 나감. `DialogueDirector` 의 `_index.txt` 이름도 Resources 가상 경로 |
| 동적 코드·리플렉션·프로세스 (CWE-94/78/502) | 런타임에 `Activator/Assembly/Type.GetType/Process/BinaryFormatter/SendMessage` 없음. 역직렬화는 `JsonUtility`(고정 타입)+`MiniJson`(깊이 12, 예외 `FormatException`만). Editor 의 `Process` 는 `git rev-parse HEAD` 고정 인자 1곳 |
| 정규식·포맷 문자열 (CWE-1333/134) | 런타임 정규식은 `SaveService.StripEmptyLockedGroup` 2개(고정 패턴, 재직렬화 문자열 대상)뿐. `string.Format` 은 `UiStrings.FormatText` 1곳이며 `FormatException` 포착, 포맷 문자열은 번들 CSV/코드 상수(플레이어 입력 아님) |
| Yarn `{}` 보간·커맨드 주입 | 이름/호칭이 파서·표현식으로 재해석되지 않음(증명 P1). 커맨드 인자는 파싱 시 토큰화 후 렌더 → 이름 안 공백/`<<`가 인자를 분리하지 못함 |
| CSV 신뢰 | `CsvRow.Str` 는 열 수 부족을 빈 문자열로 처리, 큰따옴표 미종결·열 불일치도 예외 없음(증명 P4: 행 3개 파싱 정상). `BalanceTable.P()` 는 누락 키에 예외(fail-fast). 입력은 번들. `AddressBook` 키는 소문자 정규화 |
| 정수/부동소수 오버플로 | `Day` ≤1000·`Chapter` ≤8 은 로드 시 검증, 런타임은 챕터 수로 종료. `Gold` 는 float(상한 1e9 검증), `LevelCost` 의 `Math.Pow` 가 커져도 Infinity → 업그레이드 불가일 뿐 예외 없음. 도달 가능한 정수 오버플로 없음(캐스트 미정의 동작은 N-2) |
| 프로필·설정 파서 | MiniJson 깊이 12·문자열 이스케이프 검사, `1e999`/`NaN` 은 `FormatException`→Corrupt 또는 9999 로 클램프(증명 P5: `Corrupt`, 예외 누출 없음). 항목 512·키 64·횟수 9999 상한. 설정은 `Clamp`(NaN→min)·locale 정규화 |
| 네트워크/원격 | 네트워크 API 사용 0건. 외부 로드 콘텐츠 없음 |
| 로그의 개인정보 | N-9 참조: 이름·세이브 값은 로그에 쓰지 않음 |
| 도구 스크립트 (CWE-78) | `tools/*.py` 에 `subprocess/os.system/eval/pickle/yaml.load/urllib` 없음. `release_check.sh`/`package_source.sh` 는 인자·파일명을 쉘에 보간하지 않고 `mktemp` 사용(`package_source.sh` 의 임시 목록은 `trap` 없음 — 비밀 아님, Info) |

## 6. 알려진 항목 / 진행 중 (신규 아님 — 중복 보고 방지)

| 항목 | 출처 | 상태(리뷰 시점 소스 관찰) |
|---|---|---|
| `FileSaveStorage` 이름 검증(`PathOf`: 빈 이름·구분자·`.`/`..` 거부)과 크기 상한(`MaxFileBytes` 1MB, `ReadCapped`) | SECURITY_DESIGN F-1/F-8 | 트리에 **이미 반영된 것으로 보임**(`Save/SaveService.cs:49-66`). 프로필·설정 파일에도 1MB 상한 반영 |
| 점프 순환 | F-4 | `DialogueRunner.MaxInternalSteps=10000` 이 트리에 있음(반영됨) |
| 파서 재귀 깊이 상한(괄호/호출/`!`/`<<if>>`) | F-3 | 이번 리뷰에서 재확인하지 않음(작업 중) |
| `SaveValidator` 미검증 필드(`SatisfactionBonus`·`PlayTimeSec`·캐릭터 ID 화이트리스트·플래그 값·이름 제어/방향 문자·`SavedAtUtc`/`AppVersion` 길이) | F-7 | 진행 중. **N-1·N-6 의 일부(이름 태그, `LockedRoute` 화이트리스트, `SavedAtUtc` 형식)가 같은 수정으로 닫힘** |
| 이름 검증(조합 문자 vs 세이브 상한, 방향 문자) | F-6 | `MaxNameUtf16=32`·`IsInvisibleOrBidi` 반영됨. **추가 문자 범주는 N-10 참조** |
| 숫자 리터럴 `OverflowException`, `#tag` 2차 시간 | F-2, F-5 | 이번 리뷰 범위 밖 |
| HMAC 키 클라이언트 상수 / 코드 서명 없음 / SBOM 버전 미기재 | SECURITY_DESIGN §10 | 의도된 한계 |

## 7. 권고 우선순위

1. (N-1) `UiKit.Label` 기본 `richText=false` + 로드 경로 이름 검증 공유 — 이름 검증 수정과 함께 처리.
2. (N-2/N-3) 대사 커맨드의 숫자 파싱을 `NaN/Infinity/범위` 안전하게, `DialogueLint` 에 범위·플래그 상한 검사 추가.
3. (N-5/N-4) 파일 교체를 `File.Replace` 기반으로, 손상 파일은 덮어쓰기 전에 `.corrupt` 로 보존.
4. (N-6) `LoadLatest` 예외 처리 + `SavedAtUtc`/`LockedRoute` 검증.
5. (N-8/N-7) `.gitignore` 보강과 문서 정정, `run.sh` 를 커밋 SHA·sha256 고정 + `mktemp -d`.
6. (N-9) Android 빌드 전 `allowBackup`·`INTERNET`·키스토어 비밀번호 영속화 여부를 최종 산출물로 검증하고 처리방침의 `[TODO]` 해소.
7. (N-10~N-12) 이름 문자 범주 확대, 개발 버튼 빌드 제한, `Sanitize`/Preflight 보강.

## 8. 부록: 실행한 증명

| ID | 방법 | 결과 |
|---|---|---|
| P1 | mcs 로 `Core+Data+Dialogue` 컴파일, 이름 7종(`{1+1}`·`{$x}`·`\{`·`<<jump Evil>>`·`#tag:1`·`Hi: {name}`·`}{`)을 실제 `DialogueRunner` 로 렌더 | 전부 리터럴 출력, 점프·보간 없음 |
| P2 | `ProfileValidator.CheckName` 12종 + `SaveValidator.Validate` | 태그 3종 거부, U+2028/061C/2060/00AD/206A/E000 수락, 태그가 든 이름의 `SaveData` 는 검증 통과 |
| P3/Q | 실제 `DialogueCommandDispatcher`+`RomanceService`+`balance/*.csv` 로 `aff NaN`, `trust Infinity`, `flag big 1e30`, 플래그 3,000개 | `aff=NaN`, `trust` 클램프, `flag=int.MinValue`, `Flags=3001`, `SaveValidator`→`list size` |
| P4/P5 | `CsvTable.Parse` 열 불일치·미종결 따옴표, `MiniJson`/`ProfileService.Load` 에 `1e999`·`NaN`·음수 | 예외 누출 없음, `Corrupt`/클램프 |
| (미실행) | TMP 렌더링 영향, Android 매니페스트, `ProjectSettings` 의 키스토어 비밀번호 영속화 | Unity 필요 — "검증 불가"로 표기 |


## 9. 수정 상태 (2026-10-03 갱신)

근거는 모두 `tools/security_selftest`(캠페인 `regression.review`, `save.validator_gaps`)의 직접 단언과 아래 하네스 재실행입니다. 회귀 결과: 자가 테스트 `SELFTEST: assertions all passed`, `release_check.sh --skip-unity` PASS 16 / FAIL 0(하네스 958 검사 전부 PASS), `unity_compile_check` ALL OK.

| ID | 상태 | 수정 내용 / 근거 |
|---|---|---|
| N-1 | **수정(Unity 렌더 미확인)** | `UiKit.Label` 과 `VnStage` 이름·본문 `richText=false`. 콘텐츠에 TMP 서식 태그 사용 0건 확인(grep). 로드 경로는 F-7 의 `CheckName` 재사용으로 `<>{}\` 이름 거부(`save.validator_gaps`). TMP 가 실제로 태그를 무시하는지는 Unity 필요 |
| N-2 | **수정** | `DialogueCommandDispatcher.TryNumber` 가 NaN/Infinity/절댓값 > 1e6 거부(경고). `regression.review` 의 N-2 단언 3개 |
| N-3 | **수정** | 플래그 키 ≤ 64자, 개수 ≤ 1000(초과는 경고 후 무시). 3,000개 플래그 시도에서 상한 유지 단언 |
| N-4 | **수정** | 프로필·설정 쓰기를 `File.Replace`(실패 시 폴백)로 바꾸고 `.bak` 유지, 손상 판정 시 `.corrupt` 로 보존(`ICorruptPreserver`). 단언 6개 |
| N-5 | **수정** | `FileSaveStorage.Write` 가 `File.Replace` 사용, `SaveService.Save` 가 비정상 primary 를 먼저 `.corrupt` 로 옮겨 정상 `.bak` 을 덮어쓰지 않음. 단언 4개 |
| N-6 | **부분 수정** | 검증기 부분(`LockedRoute`/`LockedGroup`/캐릭터 ID 화이트리스트, `SavedAtUtc` 길이)은 F-7 로 닫힘. **미해결: `AppContext.LoadLatest` 예외 처리와 `SavedAtUtc` 형식 검증(최신 판정)** |
| N-7 | **수정** | `run.sh`: 기본 작업 폴더 `mktemp -d`, uGUI·InputSystem 을 관찰한 커밋 SHA 로 고정(`08a19d2c…`, `bed688c7…`), NuGet 패키지 sha256 고정 검사(`d32d3452…`). 새 임시 폴더로 처음부터 실행해 ALL OK |
| N-8 | **수정** | 루트 `.gitignore` 에 키/자격 패턴 추가, `package_source.sh` 비밀 필터를 `release_check.sh` 와 동일하게, 문서(docs/18, SECURITY_DESIGN §4.4/§7) 정정. 정적 단언 17개 |
| N-9 | **미해결(Unity 필요)** | Android 매니페스트·권한·자동 백업은 Unity 빌드 산출물이 있어야 확인 가능 |
| N-10 | **수정** | `IsInvisibleOrBidi` 가 U+2028/2029·061C·2060·206A–206F·00AD·사설영역·서식/미할당 범주까지 거부 |
| N-11 | 미해결 | 개발용 버튼 빌드 제한 미적용 |
| N-12 | 미해결 | `Sanitize`·`git` PATH·키스토어 비밀번호 영속화 확인 미적용(Unity 필요 포함) |
| N-13 | 수용(설계상 한계) | 변경 없음 |
