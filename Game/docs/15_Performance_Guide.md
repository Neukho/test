# 성능·메모리 가이드 (5단계)

> `01_TechDesign.md` §8 의 목표를 **실제 코드와 체크리스트 수치로 구체화**한 문서. 모든 메모리 수치는 순수 계산기 `Game/MemoryBudget.cs`(검증: `tools/csharp_check/polish_check`)가 `docs/art_checklist.csv` 를 읽어 낸 값이다.
> Unity 가 없는 환경이라 **실기기 프로파일링은 하지 못했다.** 아래는 "계산으로 확인한 것"과 "실기기에서 확인해야 할 것"을 구분해 적었다(§8).

## 1. 결론 먼저

| 질문 | 답 |
|---|---|
| 최악의 VN 한 장면(스탠딩 2명 + 배경 전환 중 2장 + CG 1장 + UI 아틀라스)은? | **PC 23.9 MB, Android 16.2 MB**. 예산(PC 1 GB, 모바일 350 MB)의 각각 2.3 %, 4.6 % |
| 최종 아트 규격(스탠딩 1024×2048, CG/배경 1920×1080)을 전부 한 번씩 올리면? | Android 188.4 MB, PC 410.3 MB. **예산 안이지만** 저가 모바일 RAM 에는 부담 |
| 지금 코드가 그 상태로 수렴하는가? | **아니다(고침).** `ArtLoader` 는 이제 분류별 **LRU 캐시**(스탠딩 12조각 · 배경 4 · CG 3 · 아이콘 64, 바이트 상한 병행)라 플레이 시간과 무관하게 **정상 상태 Android 32.9 MB, PC 53.8 MB**(최종 아트 규격, §3.5) 이하로 수렴한다. 예전 무한 캐시의 "전부 올린" 값(Android 188.4 MB)과 비교한 수치다 |
| TechDesign 의 "스탠딩 한 명 3~4 MB(ASTC 6x6)" 는? | 과대 추정이었다. 1024×2048 ASTC 6x6 한 장은 **0.89 MB**, 몸+얼굴 한 명은 1.8 MB |
| 가장 효과가 큰 개선은? | ① 얼굴 레이어를 작은 조각으로 자르기(§3.3) ② 챕터/등장 인물 단위 해제(Addressables, §5) ③ 밉맵 계속 끄기(켜면 +13 %) |

## 2. 현재 상태(코드 리뷰로 확인한 사실)

| 항목 | 현재 | 영향 |
|---|---|---|
| 로딩 | `Resources.Load` + `ArtLoader` 의 분류별 `LruCache`(§3.5). 밀려난 항목은 우리가 만든 플레이스홀더만 파괴하고, 화면 전환 때 `Resources.UnloadUnusedAssets` 를 제한적으로 부른다. `ClearCache()` 는 에디터 확인용 | 정상 상태에서 상한 이하로 유지 |
| 스탠딩 | 몸 + 얼굴 **같은 크기의 전체 캔버스** 두 장(`StandingView`, 얼굴은 눈·눈썹·입만 불투명) | 얼굴 한 장이 몸과 같은 메모리(0.89 MB)를 쓴다 |
| 스탠딩 생성 | `StandingView` 생성자가 먼저 Neutral 얼굴을 읽고 곧바로 요청한 표정으로 바꾼다 | 의상마다 Neutral 얼굴이 한 번은 로드된다(스크립트가 안 써도) |
| 플레이스홀더 | 파일이 없으면 `RGBA32` 텍스처를 즉석 생성(스탠딩 512×1024 = 2.0 MB, 배경 960×540 = 2.0 MB) | **압축되지 않아** 실제 일러스트보다 클 수 있다. 최종 아트가 들어오면 사라진다 |
| 임포트 규칙 | `ArtImportPostprocessor`: Sprite, 밉맵 없음, 최대 2048(아이콘 512), PC=BC7, Android=ASTC 6x6(아이콘 4x4) | 아래 계산의 전제 |
| VN 무대 | 스탠딩 최대 2칸(`VnStandingState.MaxSlots`), 배경 전환 중 2장 동시, CG 1장 | 위 "최악 장면"의 구성 |
| 밤 화면 | 인물 1명(`ShowStandingFor`)을 매번 다시 만든다 | 스탠딩은 캐시되어 있어 재생성 비용만 있다 |
| 오디오 | `AudioService`: BGM 2채널(스트리밍), SE 풀 8개, 클립은 `Resources.Load` 후 사전 캐시 | 클립 파일이 없으면 무음. BGM 은 임포트 규칙이 Streaming 으로 강제 |

## 3. 텍스처 예산표 (계산값)

### 3.1 계산식
- 블록 압축: `바이트 = ceil(가로/블록가로) × ceil(세로/블록세로) × 16` (BC7·ETC2 = 4×4, ASTC 4×4/6×6/8×8). 비압축 RGBA32 = `가로×세로×4`.
- Unity Max Size: 긴 변이 최대 크기를 넘으면 비율을 유지하며 줄인다(PC/Android 모두 2048, 아이콘 512 → 이번 규격은 모두 이 안이다).
- 밉맵 끔(현재 규칙). 켜면 체인 합계가 약 4/3 배.

### 3.2 분류별 표 (체크리스트 규격: 스탠딩 1024×2048, CG·배경 1920×1080, 아이콘 512×512)

| 분류 | 장수(전체 / 필수) | PC BC7 한 장 | PC 전체 합계 | Android 한 장 | Android 전체 합계 | Android 필수 합계 |
|---|---|---|---|---|---|---|
| 스탠딩 바디 | 18 / 16 | 2.00 MB | 36.0 MB | 0.89 MB (ASTC 6x6) | 16.1 MB | 14.3 MB |
| 스탠딩 얼굴 | 108 / 72 | 2.00 MB | 216.0 MB | 0.89 MB | 96.4 MB | 64.3 MB |
| 이벤트 CG | 36 / 36 | 1.98 MB | 71.2 MB | 0.88 MB | 31.6 MB | 31.6 MB |
| 공통 CG | 3 / 3 | 1.98 MB | 5.9 MB | 0.88 MB | 2.6 MB | 2.6 MB |
| 배경 | 36 / 18 | 1.98 MB | 71.2 MB | 0.88 MB | 31.6 MB | 15.8 MB |
| 메뉴 아이콘 | 40 / 12 | 0.25 MB | 10.0 MB | 0.25 MB (ASTC 4x4) | 10.0 MB | 3.0 MB |
| **합계(UI 아틀라스 제외)** | 241 / 157 | | **410.3 MB** | | **188.4 MB** | **131.6 MB** |

"필수" 는 `docs/13_Art_Checklist.md` 기준이며 **의상 정책 반영 후**(Uniform·Date 의상 포함)의 값이다(필수 바디 16, 얼굴 72).

### 3.3 장면 단위 최악값(`MemoryBudget.PeakScene`)

| 구성 | PC | Android |
|---|---|---|
| 스탠딩 2명(몸+얼굴 × 2) | 8.0 MB | 3.6 MB |
| 배경 2장(전환 중 이전·다음) | 4.0 MB | 1.8 MB |
| CG 1장(스탠딩은 숨겨도 메모리에 남음) | 2.0 MB | 0.9 MB |
| UI 스프라이트 아틀라스(추정 고정) | 10.0 MB | 10.0 MB |
| **합계** | **23.9 MB** | **16.2 MB** |
| 예산 | 1024 MB | 350 MB |

민감도(Android, 같은 장면):

| 변경 | 최악 장면 합계 | 비고 |
|---|---|---|
| 현재 규칙 | 16.2 MB | 기준 |
| 밉맵 켬 | 18.3 MB | 스프라이트는 밉맵이 필요 없다 → 계속 끔 |
| 스탠딩 Max Size 1024(캔버스 512×1024) | 13.5 MB | 화질이 줄어든다. 저사양 전용 품질 옵션 후보 |
| 비압축 RGBA32 | 65.7 MB | 압축을 끄면 4배 — `ArtImportPostprocessor` 가 막고 있다 |
| PC 스탠딩 4명 동시(예산표 한도) | 31.9 MB | |

### 3.4 얼굴 레이어 자르기 (가장 값싼 개선)
얼굴 PNG 는 몸과 같은 1024×2048 캔버스에 눈·눈썹·입만 그려져 있다. 실제 불투명 영역은 대략 512×256 정도이므로, **얼굴을 잘라 따로 저장하고 피벗/오프셋으로 붙이면** 얼굴 한 장이 0.89 MB → 약 0.06 MB(ASTC 6x6, 512×256)가 된다. 표정이 6개 × 의상 3개 × 6명 = 108장이 거의 사라진다(전체 Android 합계 188 → 약 98 MB).
- 필요한 코드 변경: `StandingView` 가 얼굴 스프라이트의 위치(`ArtManifest` 에 인물별 얼굴 앵커)를 알아야 한다. 아트 제작 규격을 바꾸는 일이라 **아트 담당과 합의 후** 진행한다(지금 체크리스트는 전체 캔버스 규격).

### 3.5 LRU 캐시와 정상 상태 메모리 (구현함)

**구조** — `Core/LruCache<K,V>`(순수, mcs 로 검증): 개수 상한 **그리고** 추정 바이트 상한, 퇴출 콜백, `Pin/Unpin`. `ArtPipeline/ArtLoader` 가 분류별로 따로 둔다.

| 캐시 | 개수 상한 | 바이트 상한(RGBA32 추정) | 근거(동시에 화면에 오르는 최악) |
|---|---|---|---|
| 스탠딩(몸+얼굴 조각) | **12** | 96 MiB | VN 2칸×(몸+얼굴)=4 + 밤 화면 1명=2 + 표정 교체 여유 |
| 배경 | **4** | 40 MiB | 전환 중 2장 + 직전 화면 1~2장 |
| CG | **3** | 28 MiB | 한 번에 1장 + 직전 장면 |
| 메뉴 아이콘 | **64** | 64 MiB | 메뉴 40종 + 여유 |

**화면에 올라가 있는 스프라이트는 퇴출하지 않는다 — 방법(선택과 이유)**
- 선택: **"사용 시 최근으로 올리기(touch on use) + 넉넉한 상한 + 파괴 지연"**. `Acquire/Release` 참조 카운트는 호출부(`StandingView`·`VnStage`·화면 6곳)를 모두 고쳐야 하고 하나만 빠뜨려도 누수·깨짐이 생겨서 이번에는 쓰지 않았다(`LruCache.Pin` 은 준비돼 있다).
- 화면이 쓰는 스프라이트는 방금 `Get` 으로 받은 것이라 항상 가장 최근이고, 상한이 동시 사용 최악의 약 2배라서 쓰는 도중 밀려나지 않는다.
- 그래도 밀려난 경우를 위해 **파괴는 화면 전환 때까지 미룬다**(`ArtLoader.OnScreenTransition`, `AppContext.OnScreenChanged` 가 호출). 우리가 `Sprite.Create`/`new Texture2D` 로 만든 것(플레이스홀더, Texture Type 구제용 스프라이트)만 파괴하고, `Resources.Load` 에셋은 참조만 놓는다(`Resources.UnloadUnusedAssets` 가 회수).
- 화면이 한 번만 만들고 계속 들고 있던 배경(타이틀·주인공 생성·루트 선택·엔딩)은 **`OnShow` 마다 다시 받도록** 고쳤다(안 그러면 캐시에서 밀려난 뒤 파괴된 스프라이트를 가리킨다).
- `Resources.UnloadUnusedAssets` 는 프레임을 끊을 수 있어 **화면 전환 때, 퇴출이 있었을 때만, 20초 이상 간격으로** 부른다(`Throttle.Elapsed`).

**정상 상태 메모리 추정(최종 아트 규격, `MemoryBudget.SteadyStateCacheBytes` = 분류별 "가장 큰 텍스처 × 캐시 개수")**

| 플랫폼 | 정상 상태 상한 | 예전 무한 캐시("전부") | 예산 |
|---|---|---|---|
| Android(ASTC 6x6) | **32.9 MB** | 188.4 MB | 350 MB(+ UI 아틀라스 10 MB 포함해도 25 % 여유 안) |
| PC(BC7) | **53.8 MB** | 410.3 MB | 1 GB |

- 위 값은 **실제 압축 텍스처** 기준이다. 최종 아트가 없는 지금은 플레이스홀더(RGBA32, 비압축)가 들어가므로 같은 개수에서 더 크다: 바이트 상한이 이를 막는다(스탠딩 12조각 × 512×1024×4 = 24 MiB, 배경 4장 × 960×540×4 = 8 MiB — 상한 안).
- 계산이 아니라 **실기기 스냅샷으로 확인해야 하는 것**: 실제 임포트 압축 크기와 `EstimateBytes`(RGBA32 가정, 과대 추정) 차이, 장면 전환 반복 후 `Texture2D` 수가 일정한지.


## 4. 스프라이트 아틀라스 계획 (SpriteAtlas V2)

| 아틀라스 | 넣을 것 | 크기 | 비고 |
|---|---|---|---|
| `Atlas_Common` | 공통 UI 스프라이트(버튼·패널 9-slice·아이콘·슬라이더 핸들 등 — **지금은 단색 `Image` 만 써서 없음**, 아트가 오면 추가) | ≤ 2048² | 항상 로드 |
| `Atlas_MenuIcons` | 메뉴 아이콘 40종을 **인게임용 256×256 로 축소**해 모음(체크리스트 규격 512 는 원본 보관용) | 2048² 한 장(64칸) | 영업 화면에서만 필요 → Business 그룹 |
| `Atlas_Hud` | HUD 아이콘(골드·평판·피로 등) | ≤ 512² | 항상 로드 |

설정: Include in Build, Allow Rotation **끔**(UI 가장자리 번짐 방지), Tight Packing 끔, Padding 4, 압축 PC=BC7 / Android=ASTC 4x4, 밉맵 끔.

**아틀라스에 넣지 않는 것**: 스탠딩 몸·얼굴, 이벤트·공통 CG, 배경.
- 이유 1: 한 장이 화면 전체(또는 2048 높이)라 아틀라스의 이점(드로콜 병합)이 없고, 여백 때문에 오히려 메모리가 커진다.
- 이유 2: 아틀라스는 안의 스프라이트 하나만 써도 **아틀라스 전체 텍스처가 로드**된다. 큰 그림은 개별 로드/해제가 되어야 한다.
- 이유 3: 얼굴을 표정별로 갈아 끼우는 용도라면 §3.4 처럼 작게 잘라 별도 `Atlas_Faces_<Char>`(인물당 한 장)로 묶는 편이 낫다 — 그때는 인물 그룹과 함께 로드/해제.

## 5. Addressables 이전 계획

### 5.1 전제와 이음매
`ArtLoader` 는 이미 이음매다(`ArtLoader.cs` 주석: "Addressables 로 이전할 때는 이 클래스 안쪽만 바꾼다"). 호출부(`StandingView`, `VnStage`, `TitleScreen` 등)는 `ArtLoader.Get/StandingBody/…` 만 안다. 이전 후에도 **주소(address) = 지금의 `ArtSpec.ResPath`**(예 `Art/Standing/Howl/Std_Howl_Casual_Body`)로 맞추면 파일명 규칙·플레이스홀더 폴백이 그대로 유지된다.

### 5.2 그룹 배치 (Android 크기는 최종 규격 전체 파일 기준 / 괄호 = 필수만)

| 그룹 | 내용 | 로드 시점 | 해제 시점 | Android 크기 |
|---|---|---|---|---|
| `Core` | 타이틀·카페·거리 배경 6장(낮/저녁/밤), 처음 12개 메뉴 아이콘, `Atlas_Common`, `Atlas_Hud` | 앱 시작(동기) | 해제 안 함 | 배경·아이콘 약 8.3 MB + 아틀라스 |
| `Std_Howl` | 하울 몸 3 + 얼굴 18 | 하울이 나오는 장면 직전(비동기 프리로드) | 장면 후 LRU 2명을 넘기면 | 18.7 MB (14.3) |
| `Std_Johan` | 요한 | 〃 | 〃 | 18.7 MB (8.9) |
| `Std_Beomjun` | 범준 | 〃 | 〃 | 18.7 MB (9.8) |
| `Std_Haru` | 하루 | 〃 | 〃 | 18.7 MB (15.2) |
| `Std_Gomseok` | 곰석 | 〃 | 〃 | 18.7 MB (15.2) |
| `Std_Nabi` | 나비 | 〃 | 〃 | 18.7 MB (15.2) |
| `CG_<인물>` ×6 | 개인 CG E1~E6 6장 | 이벤트 노드 시작 직전 | 이벤트 종료 | 5.3 MB |
| `Spot_<장소>` | 데이트 장소 배경 낮/저녁/밤(`ArtKeys.Places` 의 Lighthouse, FishMarket, NightMarket, Dojo, LiveClub, Greenhouse, BookStreet, Beach, Rooftop) | 데이트 시작 | 데이트 종료 | 장소당 약 2.6 MB |
| `Ch01` … `Ch08` | 그 챕터에서만 쓰는 공통 CG·배경(`COMMON_CE03` = 3장, `COMMON_SHOW` = 5장, `HAREM_E1` = 7~8장 등) | 챕터 시작(`ChapterFlow.NextMorningStep` 의 Intro/InfoCard 직전) | 다음 챕터 시작 때 이전 챕터 그룹 해제 | 챕터당 0.9~2.6 MB |
| `Audio_BGM`, `Audio_SE` | 오디오(지금은 `Resources/Audio`) | BGM = 요청 시, SE = 앱 시작 | BGM 은 교차 페이드 후 | BGM 스트리밍(메모리 거의 없음) |

개인 이벤트 CG 의 챕터(`balance/events.csv` `chapter_min`): E1=1장, E2=3장, E3=5장, E4=6장, E5=8장, E6=트루 엔딩. 위 표에서 인물 CG 를 챕터로 쪼개지 않은 이유는 이벤트가 "그 챕터 이후 아무 때나" 열리기 때문이다(`chapter_min` 은 최소값).

**동시 로드 규칙**: 모바일은 `Std_*` 가 한 번에 2개를 넘지 않게(예산표 한도) 프리로드 큐가 LRU 로 해제한다. PC 는 4개.

### 5.3 이전 순서 (위험 낮은 것부터)
1. **카탈로그만 먼저**: 에디터 도구로 `Resources/Art` 의 모든 PNG 를 Addressables 주소(`ResPath` 와 동일)로 등록하고 위 그룹으로 분류(라벨: `char:howl`, `chapter:03`, `place:lighthouse`). 런타임은 아직 `Resources` 사용. → 빌드 크기·그룹 크기를 먼저 눈으로 확인.
2. (일부 완료) `ArtLoader` 의 무한 증가는 **LRU 로 이미 끊었다**(§3.5). Addressables 로 옮길 때는 `Acquire(address)` / `Release(address)` 참조 카운트로 바꾼다 — `LruCache` 의 `Pin/Unpin` 이 같은 모양이다. 기존 `Get` 은 "이미 로드된 것을 동기로 돌려주고 없으면 플레이스홀더"로 유지.
3. 장면 경계에 프리로드 훅 추가: `DialogueDirector.PlayCore` 가 노드를 시작하기 전에 필요한 `Std_*`/`CG_*` 그룹을 `Addressables.LoadAssetsAsync(label)` 로 기다리고(첫 프레임 끊김 방지: 로딩 중에는 어두운 화면 유지), 장면 종료 시 `Release`.
4. 챕터 전환 훅: 아침 시작(`GameScreen.ShowMorning`)에서 `ChN-1` 해제, `ChN` 프리로드.
5. `Core` 만 동기 로드(`WaitForCompletion`) 허용 — 타이틀 첫 화면용.
6. 빌드: Android 는 `Core` + `Ch01~02` 를 install-time, 나머지를 on-demand 로 나누는 Play Asset Delivery 를 검토(앱 크기 목표 별도).
7. `ArtImportPostprocessor.Root` 와 `ArtManifest` 의 `Art/…` 경로는 `Resources` 기준이므로, 파일을 `Assets/_Project/Art/…` 로 옮기면 **임포트 규칙의 루트 경로도 같이 바꿔야** 한다(까먹기 쉬움).

### 5.4 이전 후 검증
- 장면 전환 10회 반복 후 Memory Profiler 의 "Sprite/Texture2D 수" 가 늘지 않는다(누수 없음).
- 챕터 1 → 8 을 자동 진행(`AutoPlayer` 확장)하며 텍스처 합계가 §3.3 의 최악값 + 챕터 그룹 크기 안에 머문다.
- 그룹이 없는 주소를 요청하면 플레이스홀더로 폴백(게임이 멈추지 않음).

## 6. 오디오 메모리·로딩

| 종류 | 설정(`AudioImportPostprocessor` 가 자동 적용) | 이유 |
|---|---|---|
| BGM | Streaming + Vorbis(품질 0.6), 백그라운드 로드 | 메모리에 통째로 올리지 않는다. 13곡이 2~4분이어도 메모리는 버퍼 정도 |
| SE | Decompress On Load + Vorbis(품질 0.7), 모노 | 짧아서 즉시 재생돼야 하고 메모리도 작다(0.5초 모노 ≈ 수십 KB) |

BGM 교차 페이드 동안에는 두 곡이 동시에 스트리밍된다(약 1초). 클립은 `AudioService` 의 사전에 캐시되므로 곡 수만큼 `AudioClip` 객체가 남는다(스트리밍이라 가볍다). Addressables 이전 때 `Audio_BGM` 그룹으로 옮기고 사용 후 해제한다.

## 7. GC 규칙

1. **프레임마다 도는 코드(`Tick`, `Update`, 코루틴 루프)에서 문자열을 만들지 않는다.** 값이 바뀔 때만 만든다. (이번에 `GameScreen.BusinessRoutine` 의 손님 수 문구를 이렇게 고쳤다 — 3초 동안 매 프레임 새 문자열을 만들고 있었다.)
2. 자주 갱신하는 TMP 텍스트는 가능하면 `SetText("{0}", n)`(숫자 포맷) 사용. 한 줄짜리 UI 라벨은 값이 바뀔 때만 `.text =`.
3. Update 루프에 **LINQ·`foreach` 로 컬렉션 열거(박싱)·람다 생성·`new List`** 금지. 필요한 버퍼는 필드로 재사용(`List.Clear()`).
4. 이벤트 버스: 이벤트는 구조체(`struct`)로 정의, 구독자 목록 순회 중 할당 금지(`EventBus` 가 이미 그렇게 짜여 있다).
5. 화면 재구성(`UiKit.ClearChildren` + 새로 만들기)은 **단계 전환 때만**. `ClearChildren` 은 `Destroy` 를 쓰므로 한 프레임에 수십 개를 지우면 스파이크 — 반복되는 메뉴는 풀링을 검토(밤 메뉴 버튼이 우선 후보).
6. `Resources.Load` 를 Update/Tick 에서 부르지 않는다(첫 호출은 디스크 I/O). `AudioService` 는 클립을 사전에 캐시한다.
7. `Debug.Log` 는 릴리스 빌드에서 비용이 있다. 프레임 단위 로그 금지, 장면 단위 로그만.
8. `FindObjectsByType` 같은 씬 전체 검색은 언어 변경(`LocalizedLabel.RefreshAll`)처럼 드문 이벤트에서만.
9. 코루틴의 `yield return new WaitForSeconds` 는 할당한다 — 반복되면 필드로 캐시. (현재 코드는 `yield return null` 만 쓴다.)
10. 문자열 키는 상수/`const` 로. `UiText` 는 속성이라 호출 때마다 사전 조회(할당 없음)이지만 **프레임 루프 안에서 반복 호출은 피한다**.

## 8. 프로파일링 체크리스트

실기기(Android 중저가 1대 + PC)에서 **각 단계 끝에** 아래를 기록한다. 이 환경에서는 하나도 실행하지 못했다.

**메모리 (Memory Profiler 스냅샷)**
- [ ] 타이틀 직후 / 새 게임 첫 아침 / VN 장면 중(스탠딩 2명) / CG 표시 중 / 엔딩 화면 — 각 스냅샷의 Texture2D·Sprite 합계가 §3 계산값(+ 플레이스홀더)과 ±20 % 이내인가
- [ ] 8챕터를 자동 진행한 뒤 텍스처 합계(누수 확인). LRU 캐시이므로 §3.5 의 정상 상태 값에서 **더 늘지 않아야** 한다(늘면 `ArtLoader.TotalEvictions`·`CachedCount` 와 `PH_` 텍스처 수를 같이 본다)
- [ ] 플레이스홀더 텍스처(`PH_` 이름)가 최종 아트 교체 뒤에 하나도 남지 않았는가
- [ ] AudioClip 이 BGM 곡 수만큼 스트리밍(Load Type)으로 잡히는가(Decompressed 로 잡히면 임포트 규칙이 안 먹은 것)

**CPU / 프레임 (Profiler, Deep Profile 끔)**
- [ ] VN 장면 중 평균/최악 프레임: PC 60 fps, 모바일 30 fps 유지(`Application.targetFrameRate`)
- [ ] 타자기 구간에서 TMP 메시 재생성 시간(`maxVisibleCharacters` 갱신)이 프레임 예산의 10 % 이내인가
- [ ] 화면 전환(타이틀→게임, 게임→VN, VN→게임) 한 번당 최대 프레임 시간(스파이크). 특히 `ClearChildren` + 재구성 직후
- [ ] 영업 연출 3초 동안 `GC.Alloc` 0 B/프레임(이번 수정 확인)
- [ ] `DisplayService.Tick`/`AppContext.PollBackKey` 가 프레임당 0 할당인가

**GC**
- [ ] VN 10분 플레이 중 GC.Collect 횟수와 총 할당량(스냅샷). 목표: 프레임당 평균 < 1 KB
- [ ] 밤 메뉴를 열고 닫는 반복에서 할당량(버튼 생성 비용)

**입력·UI (모바일)**
- [ ] 터치 응답 지연(탭 → 다음 줄)이 눈에 띄지 않는가, 길게 누르기 스킵이 0.5초 뒤 시작하는가
- [ ] 안전 영역(노치 기기)에서 대사창·HUD·메뉴 버튼이 잘리지 않는가(`SafeAreaFitter`)
- [ ] 발열/배터리: 30분 연속 자동 진행(`NeverSleep`) 후 온도

**로딩**
- [ ] 첫 실행 콜드 스타트 → 타이틀 표시까지 시간(CSV·`audio_manifest`·`ui_*.csv`·대사 스크립트 파싱 포함)
- [ ] 스크립트 로드(`DialogueDirector.LoadResources`) 시간 — 노드 수가 늘수록 선형 증가하므로 기록해 두기

## 9. 다음 단계 제안 (우선순위)

1. 최종 아트가 들어오기 전에 **얼굴 자르기 규격**(§3.4)을 아트 담당과 확정 — 아트를 다 그린 뒤에는 바꾸기 어렵다.
2. ~~`ArtLoader` 에 캐시 상한 + LRU~~ — **했다**(§3.5). 남은 것: Addressables 이전 때 참조 카운트(`Acquire`/`Release`)로 바꿀지 결정.
3. 실기기 프로파일(§8)을 한 번 돌려 §3.3 계산값과 비교, 어긋나면 `MemoryBudget` 의 UI 아틀라스 추정(10 MB)을 실측으로 교체.
