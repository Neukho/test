# 시나리오 바이블 — 공통 본문 2~5장

위치: `UnityProject/Assets/_Project/Dialogue/common/ch02.yarn` ~ `ch05.yarn`, 진입 목록 `common/entries_ch2_5.txt`. 줄 id 접두사 `ch02_`~`ch05_`. 화자는 `player`, 여섯 인물 id, `npc_baker/fisher/florist/applicant`만 사용. CG 키는 `COMMON_*`.

## 1. 챕터별 줄거리와 노드 인덱스

| 노드 | 파일 | 줄(정적) | 내용 |
|---|---|---|---|
| `Ch2_Intro` | ch02 | 56 | 개업 첫날의 대실패(주문 정체·재료 낭비), 창가의 은여우 손님(요한)이 점수를 안 매기고 간다, 손님 한마디 노트(평판 UI), 시식회 계획 |
| `CE02_Tasting` | ch02 | 44 | 시식회(요한·곰석). 메뉴 개발 튜토리얼: 맛·비주얼·가격 세 눈금, 세트 메뉴 이름 짓기, 새 메뉴 노트. 6일차 이후 권장 |
| `Ch2_Outro` | ch02 | 12 | 평판 상승, 단골 형성, 문 앞 계단의 기타 소리 3줄(나비는 말하지 않음) |
| `Ch3_Intro` | ch03 | 38 | 임대료 인상 통지서(18퍼센트, 규약 제14조), 낮의 퓨즈 차단, 대응 방향 선택 |
| `CE03_RentMeeting` | ch03 | 50 | 범준·하울 회의. 통지서 철회, 인상은 네 퍼센트, 지붕·보일러·배선은 건물주 부담, 2층 개방 확장 계약(레벨 5까지), 분기별 얼굴 회의, 등불제 무대 제안 |
| `Ch3_Outro` | ch03 | 11 | 2층 개방, 신문에 기사가 실리지 않음, 구인 공고 예고 |
| `Ch4_Intro` | ch04 | 55 | 무대 소문, 구인 공고 문구 선택, 지원자들, 하루 면접, 합격 전화(사흘 뒤 7시 30분 출근) |
| `CE04_StaffWelcome` | ch04 | 42 | 직원 환영회(하루·곰석). 마들렌 소금·설탕 실수를 하루가 즉시 보고, 직원 배치 튜토리얼(주방/홀/카운터, 보너스 0/3/6/10퍼센트), 목요일 단 과자 20개 주문 카드, 직원 머그잔 |
| `Ch4_Outro` | ch04 | 10 | 레벨 6, 무대 자리 확정(안쪽 모퉁이), 배선은 하울 |
| `Ch5_Intro` | ch05 | 35 | 무대 설치 계획, 이웃 소음 양해(20시 시작, 22시 전 종료), 나비가 날짜(등불제 전날)·곡 수(두 곡)·앵콜 없음을 정함, 무대 방향 선택 |
| `CE05_FestivalPrep` | ch05 | 55 | 축제 준비(나비·범준). 매트로 소음 줄이기, 조명은 노란 전구, 범준의 눈치 없는 곡 제안과 중재 선택, 이벤트 영업 허가(임시 가판) |
| `Ch5_Show` | ch05 | 18 | 공연 당일 준비와 만석의 저녁. 공연 십 분 전에서 끝난다 |
| `Ch5_Show_After` | ch05 | 17 | 공연 직후. 개인 이벤트 플래그로 공백을 메움, 앙코르 없음, 정리 시작 |
| `Ch5_Outro` | ch05 | 12 | 등불제 당일 임시 가판, 이벤트 영업 해금 |

### 5장 공연 기준 시각표(개인 이벤트가 따르는 기준)
전날 밤 Haru_E2(케이크) -> 당일 오전 9시 꽃집 케이크 수령 -> `Ch5_Show`(19:30, 만석, 하울은 가장 뒤쪽 구석 배전함 옆 테이블) -> `Johan_E3`(20시 십 분 전부터 첫 곡 끝) -> `Nabi_E3`(두 번째 곡, 줄 끊김, 무대 뒤) -> `Ch5_Show_After` -> 한 시간 뒤 `Howl_E3` -> 다음 날 등불제(`Ch5_Outro`). 범준은 쪽지와 화환만 보낸다.

## 2. 플래그 표

| 플래그 | 설정 위치 | 용도 |
|---|---|---|
| `ch2_open_honest/rush/sort`, `ch2_waste_shared/numbers/scale` | Ch2_Intro | 첫날 대응 선택(수치 변화 없음) |
| `ch2_first_day_done`, `ce02_done`, `ch2_cleared` | ch02 | 진행 표지. `ce02_fix_taste/look/price`, `ce02_name_moon/harbor/johan` |
| `ch3_notice_read`, `ch3_plan_visit/numbers/wait` | Ch3_Intro | 대응 방향 |
| `ce03_credit_howl/mutual/numbers`, `ce03_done`, `ch3_expansion_signed`, `ch3_cleared` | CE03/Outro | `ch3_expansion_signed` = 가게 확장 해금 표지 |
| `ch4_post_open/growth/pay`, `ch4_ask_why/prev/mistake`, `ch4_hire_warm/plain`, `ch4_haru_hired` | Ch4_Intro | 면접 선택 |
| `ce04_place_kitchen/hall/counter`, `ce04_done`, `ch4_cleared` | CE04/Outro | 배치 선택 |
| `ch5_stage_plain/fancy/ask_nabi`, `ch5_stage_planned` | Ch5_Intro | CE05 의 조명 대사 분기 |
| `ce05_split/cover/trim`, `ce05_done` | CE05 | 중재 선택 |
| `ch5_show_started`, `ch5_show_done`, `ch5_festival_unlocked`, `ch5_cleared` | ch05 | 진행 표지 |

읽기만 하는 외부 플래그: `beomjun_e1_listen/deal/clash`(CE03 분기), `nabi_e3_yours/exit/pressed`, `johan_seat_reserved`, `haru_e2_retry`(Ch5_Show_After). `aff`/`trust` 변경은 없다. `ledger_truth`, `drawer_key_clue`는 건드리지 않는다.

## 3. 루트와의 일치 메모
- Johan_E1: 첫 영업 5일 뒤. 공통은 요한이 점수를 말하지 않고 칼럼이 이번 주에 실린다고만 알린다. CE02 에서 요한은 칼럼 내용을 인용하지 않는다(6일차 이후 권장).
- Nabi_E1: 공통은 문 앞 기타 소리 3줄뿐이며, 이 개인 이벤트 전후 어느 쪽에 재생해도 읽힌다.
- Beomjun_E1/Howl_E2/CE03: 통지서 철회, 서명한 종이, 배선 수리(새벽, 퓨즈 두 번), 특약 파일철을 닫아 둠(E5 트루 열쇠 복선을 침범하지 않음). E1 미시청 경로도 CE03 에서 범준이 철회를 말로 밝힌다.
- Haru_E1: 면접 사흘 뒤 7시 30분, 면접 때 곰석의 오븐을 눈여겨봄, `어디서 뭐가 일어났는지` 규칙. 쿠키·앞치마 크기 장면은 쓰지 않았다. 하루의 첫 `다시 해 볼게요`는 Haru_E2 몫이라 공통에서는 하지 않는다.
- Beomjun_E2: CE04 의 주문 카드(목요일 20개)가 복선.
- Nabi_E2: 안쪽 무대 소문을 Ch4_Intro 가 먼저 깐다. 조건 3개를 CE05 와 Intro 가 존중한다.
- Nabi_E3/Howl_E3/Johan_E3/Haru_E2: 위의 시각표 참고.
- 발견한 모순: (1) [해결] Haru_E1 의 `처음 가는 길이라`는 면접 방문과 충돌해 수정(21 Continuity Report). (2) events.csv 의 Nabi_E2 최소 챕터 3과 4장 서술 차이(나비 바이블에 이미 기록).

## 4. 톤 체크리스트
- 곰석은 느린 반말에 `구먼/그려/여`, 하울은 짧은 문장과 말줄임, 범준은 느낌표와 큰 목소리, 요한은 능청, 하루는 존댓말·말끝 흐림, 나비는 건조한 짧은 반말.
- 갈등은 임대료·소음·일손 같은 생활 문제이고 종 고정관념은 없다.
- 주인공 대사는 `{name()}`/`{player_calls()}`/`{calls_player()}`만 사용, 성별어 없음. 하루에게는 대명사를 쓰지 않는다.
- 범준의 눈치 없는 말은 `함께 고치자` 톤에서 수습된다.
- 연속성 보강(21 Continuity Report): `Ch5_Show_After` 의 줄 끊김 서술 분기는 `ch5_snap_told` 를 세운다. Nabi_E3 를 뒤늦게 재생하면 첫머리가 회상 문장이 되어 같은 사건이 새것처럼 두 번 서술되지 않는다. `ch05_109` 는 Nabi_E3 의 `nabi_e3_029` 와 겹치지 않게 다듬었다.
