# 기술 설계서 (0단계)

> Unity 6 LTS · 2D URP · C# · 새 Input System · TextMeshPro · Addressables · Unity Localization
> 이 문서는 1단계 구현 전에 합의할 **설계**입니다. 패키지 버전은 프로젝트 생성 시 Package Manager에서 확인하고, 호환성이 불확실한 항목은 §12에 모았습니다.

---

## 1. 핵심 결정

### 1.1 대화 엔진: **Yarn Spinner 추천**

| 기준 | Yarn Spinner | Ink |
|---|---|---|
| Unity Localization 연동 | 라인 ID 자동 생성 → String Table 연동 지원 | 기본 지원 약함(직접 구현 필요) |
| 게임 변수 연동(호감도·플래그) | `<<set $aff_howl to ...>>`, C# 함수/커맨드 등록이 간단 | 외부 함수 바인딩 가능하나 변수 동기화 코드 필요 |
| 작가 친화성 | 노드 기반 스크립트, VS Code 확장·미리보기 | 위브(weave) 문법이 강력하나 학습 필요 |
| 저장 | 변수 저장소를 우리 SaveData와 통합 가능 | 스토리 상태 JSON 직렬화가 우수 |
| 분기 많은 연애 시뮬레이션 | 노드·조건 분기가 직관적 | 매우 강력(특히 긴 선형 서사) |
| 리스크 | 버전별 API 변동 | 로컬라이제이션 도구 직접 필요 |

**결론**: 이 프로젝트는 ① 한/영 로컬라이제이션 필수 ② 호감도·신뢰도·플래그와 대화 분기의 잦은 상호작용 ③ 작가(본인)가 노드별로 이벤트를 관리 → **Yarn Spinner**가 적합. Ink는 장문 서사 위주 프로젝트에 더 강하지만, 이 프로젝트의 병목은 ‘로컬라이징+상태 연동’이다.

규약:
- 라인 ID는 **자동 생성 태그** 사용(`#line:xxxx`), 번역 CSV는 Yarn 스크립트에서 추출.
- 대사의 성별 치환은 `{pronoun_subj}` 같은 **인라인 보간**으로 처리하고, 한국어에선 호칭 중심으로 작성.
- 게임 호출은 **Yarn 커맨드**로 한정: `<<aff howl +5>>`, `<<trust howl +2>>`, `<<flag ledger_truth>>`, `<<show_standing howl smile>>`, `<<show_cg HOWL_E3>>`.

### 1.2 UI: **uGUI 추천** (UI Toolkit은 에디터 툴 전용)

| 기준 | uGUI(Canvas) | UI Toolkit |
|---|---|---|
| 대화창·스탠딩·CG 연출(트윈, 레이어 순서) | 매우 적합 | 가능하나 월드 연출 연동이 번거로움 |
| Yarn Spinner 기본 뷰 | uGUI + TMP 기준 샘플 풍부 | 직접 구현 |
| 모바일 세이프 에어리어 | `Screen.safeArea` 를 앵커로 변환(간단) | 가능 |
| 성능 | Canvas 분할로 관리 | 대량 리스트에 유리 |
| 팀 숙련도/자료 | 많음 | 증가 중 |

**결론**: 런타임 UI는 uGUI(Canvas Scaler 1920×1080, Match 0.5), 에디터 도구(콘텐츠 검증기, 밸런스 뷰어)는 UI Toolkit.

### 1.3 데이터 주도: SO + JSON 하이브리드

- **ScriptableObject**: 에디터에서 보기 좋은 정의(캐릭터, 메뉴, 재료, 의상, 데이트 스팟, 이벤트 정의, 상점 레벨).
- **JSON**: 대량 표 데이터(밸런스 CSV 가져오기 결과), 로컬 모딩/핫픽스용 `StreamingAssets/Data/*.json`.
- **임포터**: `balance/*.csv` → SO/JSON 자동 변환 에디터 메뉴(`MorpheHouse/Data/Import CSV`).
- 새 캐릭터·메뉴·이벤트는 **SO 에셋 생성 + Yarn 파일 추가**만으로 반영(코드 수정 없음).

---

## 2. 씬 구성

| 씬 | 역할 | 로드 방식 |
|---|---|---|
| `Boot` | 서비스 초기화(이벤트 버스, 저장, 로컬라이제이션, Addressables 초기화) | 첫 씬, 단독 |
| `Title` | 타이틀, 새 게임/이어하기/설정 | Boot → Single |
| `Game` | 영속 씬: GameState, DayCycleController, 공통 HUD, 오디오 | Title → Single |
| `Business` | 낮 영업 뷰(손님·직원 시각화, 개입 카드) | `Game` 위 Additive |
| `Interior` | 인테리어 그리드 편집 | Additive |
| `VNStage` | 대화·스탠딩·CG·선택지 UI | Additive |
| `DateSpot` | 데이트 스팟 선택/연출(배경 + VN 재사용) | Additive |
| `Ending` | 엔딩 롤 및 크레딧 | Single |

흐름: `Boot → Title → Game(+Business/Interior/VNStage/DateSpot) → Ending → Title`.

---

## 3. 폴더 구조

```
Assets/
  _Project/
    Art/                  (원본·임포트: Standing, CG, BG, Icons, UI)
      Placeholders/       (코드 자동 생성)
    Audio/
    Data/                 (SO 에셋: Characters, Menus, Events, Spots, Shop)
    Dialogue/             (Yarn: common/, routes/{howl,johan,...}/)
    Localization/         (String Tables, Locale)
    Prefabs/
    Scenes/
    Scripts/
      Core/               (MorpheHouse.Core)
      Data/               (MorpheHouse.Data)
      Save/               (MorpheHouse.Save)
      Tycoon/             (MorpheHouse.Tycoon)
      Romance/            (MorpheHouse.Romance)
      Dialogue/           (MorpheHouse.Dialogue)
      UI/                 (MorpheHouse.UI)
      Art/                (MorpheHouse.ArtPipeline: 플레이스홀더, 로더)
      Audio/              (MorpheHouse.Audio)
      Boot/               (MorpheHouse.Boot)
      Editor/             (MorpheHouse.Editor)
    Tests/
      EditMode/ PlayMode/
  AddressableAssetsData/
StreamingAssets/Data/
```

---

## 4. 어셈블리/의존성 규칙

```
Core  ←  Data  ←  Save
              ↖
               Tycoon ──────┐
               Romance ─────┤→ (이벤트 버스로 통신)
               Dialogue ────┤
                            ↓
                            UI  ←  Boot (조립)
ArtPipeline ← Data, Core         Audio ← Core
Editor ← 전체(Editor 전용)       Tests ← 대상 모듈
```

| 어셈블리 | 참조 허용 | 금지 |
|---|---|---|
| `MorpheHouse.Core` | 없음 | 다른 모듈 참조 |
| `MorpheHouse.Data` | Core | Tycoon/Romance/UI 참조 |
| `MorpheHouse.Save` | Core, Data | UI 참조 |
| `MorpheHouse.Tycoon` | Core, Data | Romance/UI 직접 참조(→ 이벤트 버스) |
| `MorpheHouse.Romance` | Core, Data | Tycoon/UI 직접 참조 |
| `MorpheHouse.Dialogue` | Core, Data, Romance(읽기 인터페이스) | UI 직접 참조 |
| `MorpheHouse.UI` | Core, Data(읽기), 각 모듈의 **읽기 인터페이스** | 상태 직접 변경(→ Command 이벤트) |
| `MorpheHouse.Boot` | 전체 | — |

규칙 요약
1. 모듈 간 통신은 **이벤트 버스(구조체 이벤트)** 또는 Core의 인터페이스(`IGameClock`, `IAffectionReader` 등).
2. 상태 변경은 **도메인 서비스만** 수행. UI는 Command 이벤트(`RequestGift`, `RequestDate` 등)를 발행.
3. 순환 의존이 필요해 보이면 인터페이스를 Core로 올린다.
4. `UnityEngine`에 의존하지 않는 순수 로직(밸런스 계산, 호감도 규칙)은 **EditMode 테스트 가능**하게 분리.

---

## 5. 이벤트 버스

- 제네릭 구조체 이벤트: `EventBus.Publish<T>(in T evt) where T : struct, IGameEvent`
- 구독: `EventBus.Subscribe<T>(Action<T>)` / `Unsubscribe`
- 할당 최소화: 타입별 정적 핸들러 리스트, 순회 중 변경은 큐잉 처리.
- 주요 이벤트(예): `DayPhaseChanged`, `BusinessSettled`, `AffectionChanged`, `TrustChanged`, `EventTriggered`, `FlagSet`, `GoldChanged`, `ReputationChanged`, `SaveRequested`, `LocaleChanged`

---

## 6. GameState / SaveData 스키마

### 6.1 구조 (C# 개요 → 2단계 이전에 전체 코드 제공)

```
SaveData
 ├ meta        { version, slot, savedAtUtc, playTimeSec, appVersion }
 ├ player      { name, gender, speciesId, appearance }
 ├ progress    { chapter, day, phase }
 ├ resources   { gold, reputation, shopLevel, fatigue }
 ├ shop        { menus[], interiorGrid[], staff[], unlocks[] }
 ├ romance     { chars: { id → { aff, trust, eventsDone[], lastGiftDay, lastDateDay, lastContactDay } }, lockedRoute }
 ├ flags       { name → bool/int }
 ├ dialogue    { yarnVariables{}, visitedNodes[] }
 ├ settings    { volume{master,bgm,se,voice}, textSpeed, locale }  (※ 설정은 별도 파일로 분리 권장)
 └ integrity   { hash }
```

### 6.2 JSON 예시

```json
{
  "meta": { "version": 1, "slot": 1, "savedAtUtc": "2026-10-03T12:00:00Z", "playTimeSec": 5400, "appVersion": "0.1.0" },
  "player": { "name": "Ryan", "gender": "nonbinary", "speciesId": "human", "appearance": { "preset": 2, "palette": 4 } },
  "progress": { "chapter": 3, "day": 15, "phase": "Night" },
  "resources": { "gold": 8240, "reputation": 41, "shopLevel": 4, "fatigue": 35 },
  "romance": {
    "lockedRoute": "",
    "chars": {
      "howl": { "aff": 32, "trust": 11, "eventsDone": [1, 2], "lastGiftDay": 13, "lastDateDay": 12, "lastContactDay": 14 }
    }
  },
  "flags": { "ledger_truth": false, "howl_key_photo": true },
  "dialogue": { "yarnVariables": { "$met_howl": true }, "visitedNodes": ["Prologue", "CE01"] },
  "integrity": { "hash": "…" }
}
```

### 6.3 저장 정책
- 슬롯 3 + 자동 저장 1(`slot0`). 저장 위치: `Application.persistentDataPath/saves/slot{N}.json`.
- **원자적 저장**: 임시 파일에 쓰고 `File.Replace`/Move로 교체 + `.bak` 유지.
- **버전 마이그레이션**: `meta.version` 별 `IMigration` 체인.
- **변조 탐지**: HMAC-SHA256(고정 키는 난독화 수준, 목적은 *우발적 손상·단순 변조 탐지*. 싱글 플레이이므로 강한 DRM은 하지 않음). 불일치 시 경고 후 `.bak` 복구 제안.

---

## 7. 핵심 서비스(모듈) 목록

| 모듈 | 서비스 | 설명 |
|---|---|---|
| Core | `EventBus`, `Services`(경량 로케이터), `Logger`, `Result<T>` | 공통 인프라 |
| Data | `CharacterDef`, `MenuDef`, `IngredientDef`, `DateSpotDef`, `EventDef`, `ShopLevelDef`, `BalanceTable` | SO/JSON |
| Save | `SaveService`, `SaveSerializer`, `Migrations` | 저장 |
| Tycoon | `DayCycle`, `BusinessSimulator`, `MenuService`, `StaffService`, `InteriorService`, `RandomEventService` | 영업 시뮬레이션(순수 C# 코어 + MonoBehaviour 뷰) |
| Romance | `AffectionService`, `EventTriggerService`, `GiftService`, `DateService`, `RouteLockService` | 호감/신뢰/이벤트 규칙 |
| Dialogue | `DialogueRunner` 어댑터, `YarnCommands`, `VariableBridge` | Yarn ↔ 게임 상태 |
| ArtPipeline | `PlaceholderGenerator`, `ArtLoader` | 파일명 규칙 로딩/대체 |
| UI | 화면별 Presenter(MVP) | uGUI |

---

## 8. 성능·메모리 가이드

| 항목 | PC 목표 | 모바일(중저가) 목표 |
|---|---|---|
| 프레임 | 60fps | 30fps 이상 |
| 총 텍스처 메모리 | ≤ 1 GB | ≤ 350 MB |
| 해상도 | 1920×1080 기준 | 렌더 스케일 0.75~1.0 |
| 한 시점 로드 스탠딩 | 최대 4명 | 최대 2명 |
| 이벤트 CG | 로드 시 1장(해제 후 다음) | 동일 |

- **스프라이트 아틀라스**: UI는 SpriteAtlas V2(기능 단위: Common, Business, Menu Icons). 스탠딩/CG/배경은 **아틀라스 제외**(큰 이미지, Addressables 개별 로드).
- **Addressables 그룹**: `Core`(항상), `Ch01~Ch08`(챕터 단위 캐릭터·배경·CG), `Icons`, `Audio_BGM`, `Audio_SE`. 챕터 전환 시 이전 챕터 그룹 Release.
- **압축**: PC BC7, Android ASTC 6x6(스탠딩/배경) / 4x4(UI 아이콘). CG 2048 한계 → 1920×1080은 NPOT 허용 + 압축 시 2048×1152로 패딩하거나 `Sprite` Single + Max Size 2048 유지.
- **텍스처 예산 예시**(Android): 스탠딩 6명 중 동시 2명(2048 높이, ASTC 6x6 ≈ 3~4 MB 각), 배경 1장(≈ 3 MB), CG 1장(≈ 3 MB), UI 아틀라스 ≈ 10 MB → 활성 합계 **약 30 MB 내외**, 여유분 포함 상한 350 MB.
- GC: 이벤트 버스 구조체화, 문자열 보간 최소화(TMP `SetText` 사용), 업데이트 루프에서 LINQ 금지.
- 프로파일링: 각 단계 종료 시 Profiler/Memory Profiler 스냅샷 기록.

### 해상도/세이프 에어리어
- Canvas Scaler: Scale With Screen Size, 기준 1920×1080, Match 0.5.
- `SafeAreaFitter` 컴포넌트(1단계 제공): `Screen.safeArea` 를 RectTransform 앵커로 변환. 대화창·HUD만 적용, 배경/CG는 전체 화면.
- 종횡비 16:9 ~ 21:9 대응: 배경은 **Fill(크롭)**, 스탠딩은 **기준선 하단 고정 + 중앙 정렬**.

---

## 9. 로컬라이제이션

- Unity Localization: `Locale` ko(기본), en(확장). String Table: `UI`, `Menu`, `Character`, `Yarn_ko`, `Yarn_en`.
- 폰트: 한국어(본문 가독성 높은 산세리프) + 영문 폴백. TMP Font Asset은 **Dynamic SDF**(모바일에서는 Static 서브셋 추천).
- 하드코딩 문자열 금지. 에디터 검증기가 키 누락을 빌드 전에 검사.

---

## 10. 에디터 자동화 계획 (Menu Item)

| 단계 | 메뉴 | 기능 |
|---|---|---|
| 1 | `MorpheHouse/Setup/Create Folders` | 폴더·asmdef·기본 씬 생성 |
| 1 | `MorpheHouse/Setup/Create Scenes` | Boot/Title/Game 등 씬과 Build Settings 등록 |
| 1 | `MorpheHouse/Art/Generate Placeholders` | 캐릭터·배경·아이콘 실루엣 PNG 생성, 임포트 설정 적용 |
| 1 | `MorpheHouse/Data/Import CSV` | `balance/*.csv` → SO |
| 2 | `MorpheHouse/Dialogue/Validate` | Yarn 라인 ID·변수·커맨드 검증 |
| 5 | `MorpheHouse/Build/Pipeline` | 플랫폼별 빌드, Addressables 빌드 |

플레이스홀더 규칙: 파일명 **동일**(`Std_Howl_Smile` 등) → 실제 PNG로 교체하면 Addressables 주소가 같아 자동 반영(주소는 **파일명 기반 자동 주소 지정**).

---

## 11. 보안·개인정보 설계 (포트폴리오 연계)

> 정보보호 직무 지원자의 포트폴리오로 활용할 수 있도록 **설계 의도와 근거**를 문서로 남깁니다.

| 영역 | 설계 | 포트폴리오 설명 포인트 |
|---|---|---|
| 개인정보 | **무수집 원칙**(텔레메트리·계정·광고 SDK 없음). 이름은 로컬 세이브에만 저장 | 데이터 최소화, 개인정보 처리방침 초안(수집 항목 ‘없음’ 명시) |
| 세이브 무결성 | HMAC + `.bak` + 원자적 쓰기 | 위협 모델링(우발 손상 vs 악의적 변조), 한계 명시 |
| 입력 검증 | 세이브/JSON 로드 시 스키마·범위 검증(역직렬화 공격 방지, 알 수 없는 필드 무시) | 안전한 파싱, 퍼징 테스트 |
| 공급망 | **SBOM**(패키지·라이선스 목록) 생성, 에셋 스토어/오픈소스 라이선스 점검 | 라이선스 컴플라이언스, 취약점 이력 관리 |
| 빌드 | 서명(Windows 코드서명은 선택), 해시 공개, 비밀정보 저장소 커밋 금지(`.gitignore`, secret scan) | 시크릿 관리 |
| AI 에셋 | 생성 이력(프롬프트·시드·날짜) 로그 | 출처 추적(Provenance) |
| 모바일 | 최소 권한(인터넷 권한 없음), 백업 제외 설정 검토 | 최소 권한 원칙 |

---

## 12. 확인 필요 / 리스크

1. **Yarn Spinner 최신 버전과 Unity 6 LTS 호환**: 프로젝트 생성 시 Package Manager/공식 문서에서 확인.
2. **Unity Localization ↔ Yarn 연동 방식**(String Table 자동 생성 vs CSV 임포트): 1~2단계에서 프로토타입으로 확정.
3. **Addressables 파일명 기반 주소 자동화**: 에디터 스크립트로 구현 예정. 대안: Resources 폴더(소규모 한정).
4. **모바일 입력**: 새 Input System의 터치 → 대화 진행(탭), 길게 누르기 = 스킵.
5. **웹/콘솔**: 범위 외.

