# 일러스트 제작 체크리스트 (자동 생성)

> `python3 tools/art_checklist.py` 로 시나리오에서 실제 사용하는 에셋을 다시 계산합니다. 같은 파일명으로 PNG 를 덮어쓰면 코드 수정 없이 반영됩니다.
> 전체 목록과 진행 체크는 `docs/art_checklist.csv`(엑셀로 열기)에서 관리하세요.

## 1. 최소 제작 범위(필수) 요약

| 분류 | 필수 | 전체 |
|---|---|---|
| 스탠딩 바디 | 16 | 18 |
| 스탠딩 얼굴 | 73 | 108 |
| 이벤트 CG | 36 | 36 |
| 공통 CG | 3 | 3 |
| 배경 | 18 | 36 |
| 메뉴 아이콘 | 12 | 40 |
| UI | 2 | 2 |
| **합계** | **160** | **243** |

필수 = 스크립트가 실제로 호출하는 것 + 첫 실행에 반드시 필요한 것(기본 의상 Casual의 Neutral 표정, 초기 메뉴 12종, 타이틀 배경).
선택 = 아직 스크립트가 쓰지 않는 의상·표정·배경·CG. 나중에 콘텐츠가 늘면 필수가 됩니다.

## 2. 캐릭터별 스탠딩 필수 표정

의상은 시나리오가 거의 지정하지 않으므로 게임의 `OutfitPolicy` 가 정한다(데이트 노드=Date, 직원+카페 낮=Uniform, 그 외 Casual, 명시한 의상이 항상 우선).
이 표의 의상 횟수는 같은 규칙으로 센 값이다(괄호 = 그중 정책이 정한 횟수). 규칙을 바꾸면 `Game/OutfitPolicy.cs` 와 `tools/art_checklist.py` 의 `resolve_outfit` 을 함께 고친다.

| 캐릭터 | 사용 표정(횟수) | 사용 의상(정책 결정 횟수) |
|---|---|---|
| 하울 | Neutral 75, Smile 48, Shy 15, Sad 14, Surprised 12, Angry 1 | Casual 123 (정책 123), Uniform 33 (정책 33), Date 9 (정책 9) |
| 요한 | Smile 68, Neutral 56, Shy 26, Surprised 25, Sad 6 | Casual 164 (정책 164), Date 17 (정책 17) |
| 범준 | Smile 75, Neutral 45, Surprised 23, Shy 22, Sad 19 | Casual 162 (정책 162), Date 22 (정책 22) |
| 하루 | Neutral 57, Smile 47, Shy 31, Surprised 18, Sad 18 | Casual 90 (정책 84), Uniform 69 (정책 54), Date 12 (정책 12) |
| 곰석 | Neutral 84, Smile 62, Shy 17, Sad 13, Surprised 11 | Casual 121 (정책 121), Uniform 48 (정책 48), Date 18 (정책 18) |
| 나비 | Neutral 80, Smile 48, Sad 15, Shy 10, Surprised 7, Angry 4 | Casual 127 (정책 127), Uniform 24 (정책 24), Date 13 (정책 13) |

## 3. 이벤트 CG (스크립트 사용 현황)

| CG | 파일명 | 사용 스크립트 |
|---|---|---|
| 이벤트 CG | `CG_Howl_E1.png` | routes/howl/howl_E1.yarn |
| 이벤트 CG | `CG_Howl_E2.png` | routes/howl/howl_E2.yarn |
| 이벤트 CG | `CG_Howl_E3.png` | routes/howl/howl_E3.yarn |
| 이벤트 CG | `CG_Howl_E4.png` | routes/howl/howl_E4.yarn |
| 이벤트 CG | `CG_Howl_E5.png` | routes/howl/howl_E5.yarn, routes/howl/howl_endings.yarn |
| 이벤트 CG | `CG_Howl_E6.png` | routes/howl/howl_endings.yarn |
| 이벤트 CG | `CG_Johan_E1.png` | routes/johan/johan_E1.yarn |
| 이벤트 CG | `CG_Johan_E2.png` | routes/johan/johan_E2.yarn |
| 이벤트 CG | `CG_Johan_E3.png` | routes/johan/johan_E3.yarn |
| 이벤트 CG | `CG_Johan_E4.png` | routes/johan/johan_E4.yarn |
| 이벤트 CG | `CG_Johan_E5.png` | routes/johan/johan_E5.yarn, routes/johan/johan_end_good.yarn |
| 이벤트 CG | `CG_Johan_E6.png` | routes/johan/johan_end_true.yarn |
| 이벤트 CG | `CG_Beomjun_E1.png` | routes/beomjun/beomjun_E1.yarn |
| 이벤트 CG | `CG_Beomjun_E2.png` | routes/beomjun/beomjun_E2.yarn |
| 이벤트 CG | `CG_Beomjun_E3.png` | routes/beomjun/beomjun_E3.yarn |
| 이벤트 CG | `CG_Beomjun_E4.png` | routes/beomjun/beomjun_E4.yarn |
| 이벤트 CG | `CG_Beomjun_E5.png` | routes/beomjun/beomjun_E5.yarn, routes/beomjun/beomjun_endings.yarn |
| 이벤트 CG | `CG_Beomjun_E6.png` | routes/beomjun/beomjun_endings.yarn |
| 이벤트 CG | `CG_Haru_E1.png` | routes/haru/haru_E1.yarn |
| 이벤트 CG | `CG_Haru_E2.png` | routes/haru/haru_E2.yarn |
| 이벤트 CG | `CG_Haru_E3.png` | routes/haru/haru_E3.yarn |
| 이벤트 CG | `CG_Haru_E4.png` | routes/haru/haru_E4.yarn |
| 이벤트 CG | `CG_Haru_E5.png` | routes/haru/haru_E5.yarn, routes/haru/haru_endings.yarn |
| 이벤트 CG | `CG_Haru_E6.png` | routes/haru/haru_endings.yarn |
| 이벤트 CG | `CG_Gomseok_E1.png` | routes/gomseok/gomseok_E1.yarn |
| 이벤트 CG | `CG_Gomseok_E2.png` | routes/gomseok/gomseok_E2.yarn |
| 이벤트 CG | `CG_Gomseok_E3.png` | routes/gomseok/gomseok_E3.yarn |
| 이벤트 CG | `CG_Gomseok_E4.png` | routes/gomseok/gomseok_E4.yarn |
| 이벤트 CG | `CG_Gomseok_E5.png` | routes/gomseok/gomseok_E5.yarn, routes/gomseok/gomseok_endings.yarn |
| 이벤트 CG | `CG_Gomseok_E6.png` | routes/gomseok/gomseok_endings.yarn |
| 이벤트 CG | `CG_Nabi_E1.png` | routes/nabi/nabi_E1.yarn |
| 이벤트 CG | `CG_Nabi_E2.png` | routes/nabi/nabi_E2.yarn |
| 이벤트 CG | `CG_Nabi_E3.png` | routes/nabi/nabi_E3.yarn |
| 이벤트 CG | `CG_Nabi_E4.png` | routes/nabi/nabi_E4.yarn |
| 이벤트 CG | `CG_Nabi_E5.png` | routes/nabi/nabi_E5.yarn, routes/nabi/nabi_end_good.yarn |
| 이벤트 CG | `CG_Nabi_E6.png` | routes/nabi/nabi_end_true.yarn |
| 공통 CG | `CG_COMMON_CE03.png` | common/ch03.yarn |
| 공통 CG | `CG_COMMON_SHOW.png` | common/ch05.yarn |
| 공통 CG | `CG_HAREM_E1.png` | common/shared_routes.yarn |

## 4. 제작 요청서(장면 지문) 위치

각 CG 의 구도·조명·감정·소품·대사 위치는 `docs/art_requests/<캐릭터>.md`, 공통 CG 는 `docs/art_requests/Common_*.md` 에 있습니다.

## 5. 규격 요약

| 종류 | 규격 | 비고 |
|---|---|---|
| 스탠딩 | 투명 PNG, 세로 2048px+, 비율 1:2 권장(1024x2048) | 바디와 얼굴은 **같은 캔버스**, 얼굴은 눈·눈썹·입만 투명 배경에 |
| 이벤트 CG | 1920x1080 | 호스트가 전체 화면에 맞춤. 중앙 안전 영역에 인물 배치 |
| 배경 | 1920x1080 | 낮/저녁/밤 변형은 같은 구도에 조명·색만 변경 |
| 메뉴 아이콘 | 512x512 투명 | 40종 |
| 색감 | 전 에셋 동일 LUT/팔레트 | `02_Art_Plan.md` §5 |
