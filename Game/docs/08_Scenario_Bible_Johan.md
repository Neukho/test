# 08. 시나리오 바이블 — 요한

> 범위: Talk×8, Gift×3, Date×4(헌책방 거리·야시장 부두·온실 식물원·공통), E1~E5, 엔딩 3종, 다자 조각 2개. 전부 `UnityProject/Assets/_Project/Dialogue/routes/johan/` (진입 노드는 `entries.txt`).
> 근거: `00_GDD.md` §1.3·§2.2, `07_Dialogue_Spec.md` §4·§5, `10_Route_Structure.md` §3, 품질 기준은 `08_Scenario_Bible_Howl.md`.
> 검증: `SCENARIO_ONLY=routes/johan sh tools/csharp_check/scenario_check/run.sh` (결과는 §7).

## 1. 요한 음성 시트

은여우, 28세, 푸드 칼럼니스트(칼럼 「혀끝의 항구」). 키워드 ‘진심’. 말이 많은 이유는 진짜 말을 숨기기 위해서이고, 조용해지는 순간이 무섭다(‘냉장고 소리’ 모티프). 칭찬은 쉽게 하지만 자기 칭찬은 못 받는다.

| 항목 | 규칙 |
|---|---|
| 문장 | 길고 수사가 많다. 한 대사에 비유·말장난·자기 정정(“…아니, 삭제.”)이 들어간다. 진심이 되면 문장이 **짧아진다**(E5 고백, 엔딩 일부) — ‘말이 줄어든다 = 진짜’가 이 루트의 문법 |
| 어미 | 호감 30 미만: 해요체 + 능청스러운 반말 섞기(“~예요/~죠”, 점주님·사장님 호칭). 30~59: 반말 위주, 가끔 요. 60 이상: 반말. 이벤트·엔딩은 장면 온도에 맞춰 혼용 |
| 호칭 | `{calls_player("johan")}`(씨→사장님→이름) + 즉흥 직함(점주님, 사장님). 성별 암시 애칭 없음 |
| 비유 | 별점·퇴고·각주·첫 모금·항구·마감 등 글/음식 어휘. 별점 해석표: 3점=다시 올 이유가 있다 / 4점=다시 올 거다 / 5점=말려도 온다 |
| 외로움 표현 | **농담 속에 숨긴다**: 냉장고 소리, 빈 의자(‘빈 이유를 내가 정할 수 있다’), 팔로워 만 이천 명의 분모, 각주(아무도 안 읽는 진짜 얘기) |
| 진담 신호 | 안경을 올린다(“진담이면 안경을 올릴게”). 가장 솔직할 때(E5·트루)는 안경을 **벗는다** |
| 고맙다 | “이 말은 한 번만”을 농담처럼 반복하다가, E5에서 ‘두 번째 고맙다’가 나오면 결정적 장면 |
| 플러팅 | 장난·과장 칭찬(“내 혀가 벌써 마음을 정했어”)이지만 **상대의 몸·성별·종을 소재로 삼지 않는다**. 선을 확인하는 장치: “선 넘으면 ‘여기까지’라고 해 줘. 접는 건 자신 있어”(Talk_03), 접촉은 항상 질문 후(“싫으면 싫다고 해, 정말 괜찮아”) |

### 샘플 대사
- “오, 오늘의 시그니처가 새로 나왔네? 내 혀가 벌써 마음을 정했어.” (GDD)
- “솔직한 별점은 애정이야. 거짓 5점은 구경이고.”
- “농담은 빈자리에 깔아 두는 방석 같은 거라서. 오늘은 방석이 다 떨어졌어.”
- “숫자랑 식탁은 별개야. 만 명이 있어도 의자는 하나일 수 있어.”
- “좋아해. 점수도 칭찬도 없는 채로, 그냥 내가.” (E5, 가장 짧은 문장들)

### Do / Don't

| Do | Don't |
|---|---|
| 정답 선택지 = 솔직한 말, 수첩을 덮고 곁에 있기, 약속을 정확히 지키기, 말 안 한 이유를 캐묻지 않고 기다리기 | 홍보 부탁, 별점 흥정, 칼럼 이용 요구, “또 농담이죠?”로 진심 넘기기, 그의 수다를 놀리기 |
| 오답에도 요한은 화내지 않고 **말이 짧아진다/농담으로 접는다**(−1/−1) | 오답을 조롱·벌·폭발로 연출 |
| 요한이 쓸 수 있는데 **쓰지 않는** 장면(E2, E4)으로 ‘이용하지 않음’을 행동으로 증명 | 주인공/가게를 소재·발판으로 쓰는 전개, 가게 홍보 칼럼으로 위기를 푸는 전개 |
| 접촉은 동의 후, 속도는 주인공이 정함(E3 손/어깨, E5 천천히 선택지, 굿 엔딩 분기) | 소유·집착·질투의 미화, 압박(“대답해 줘”) |
| 여우 종 클리셰(교활·여우짓)를 쓰지 않는다. 귀·꼬리는 감정의 몸짓으로만 | 종 특성/성별을 놀림·결론으로 사용 |

## 2. 플래그 목록

수치: 대화 정답 +5/+1.5, 오답 −1/−1(`actions.csv`). 이벤트 완료 +12/+4는 호스트가 `end_event` 에서 적용, 이벤트 안의 `aff/trust` 는 선택지 보너스만.

| 플래그 | 설정 위치 | 의미 |
|---|---|---|
| `johan_wants_honest_score` / `johan_shared_follower_worry` / `johan_boundary_asked` / `johan_fridge_joke` / `johan_thursday_review` / `johan_footnote_reader` / `johan_footnote_wait` | Talk 01~06 정답 | 분위기·재방문 변형용 기록 |
| `johan_date_book_street_done` / `johan_date_night_market_done` | 데이트 | 호스트가 읽는 재방문 분기(`flag()`) |
| `johan_bookstreet_margin/gift/wander`, `johan_market_share/order/notes`, `johan_generic_quiet/listen/plan` | 데이트 선택 | 취향 기록(수치 없음) |
| `johan_e1_honest`(+3/+1) / `johan_e1_oneline`(+1) / `johan_e1_plead`(0) | E1 | 별 셋 리뷰에 대한 반응 |
| `johan_e1_draft_noticed` | E1 | **초고(구겨진 종이)를 알아봄**. 묻기·묻지 않고 존중하기 둘 다 설정(`johan_e1_draft_respected` 는 후자). 트루 열쇠 숨은 조건 |
| `johan_e2_asked_plain`(+3/+1) / `johan_e2_cleared`(0) / `johan_e2_suspect`(−1/−1) | E2 | 기사를 쓸 거냐는 질문의 방식 |
| `johan_e2_waited`(+3/+1) / `johan_e2_pushed`(−1/−1) / `johan_notebook_closed`(항상) | E2 | 안 쓰는 이유를 기다림 vs 캐묻기 |
| `johan_seat_reserved`(항상), `johan_e3_together`(+4/+1) / `johan_e3_door`(+3/+3) / `johan_e3_pushed`(−1/−1) | E3 | 빈 좌석, 곁에 가는 방식 |
| `johan_e3_hand` / `johan_e3_side`(각 +1) | E3 | 접촉 방식(주인공이 정함) |
| `johan_e4_asked_cost`(+4/+1) / `johan_e4_praise`(0) / `johan_e4_bought`(−1/−1) | E4 | 블루하버 제안 거절에 대한 반응 |
| `johan_e4_factcheck`(+3/+1) / `johan_e4_trust`(+3/+1) / `johan_e4_asked_first_page`(+1) | E4 | 칼럼 처리. 마지막은 트루 열쇠 **보조 경로** |
| `johan_e4_column_published`(항상) | E4 | 칼럼 「다시 올 이유」 발행 = 7장 위기를 푸는 열쇠(호스트/공통 7장이 읽음) |
| `johan_e5_pace_slow` | E5 | 고백을 한 문장씩 듣기 선택 |
| `johan_e5_promise`(+4/+2) / `johan_e5_slow`(+3/+2) | E5 | 응답. `slow` 는 굿 엔딩에서 ‘주인공이 먼저 말하는’ 분기로 이어짐 |
| **`johan_key`**(+5/+2) | E5 숨은 선택지 | **트루 열쇠: 요한이 간직한 ‘처음 쓴 리뷰 원고’**. 조건 `flag("johan_e1_draft_noticed") or flag("johan_e4_asked_first_page")` |
| `johan_end_normal_dates/oneline`, `johan_good_first_reader/rest`, `johan_true_hang/together`, `johan_true_ledger_yes/wait` | 엔딩 | 엔딩 내 선택 기록 |
| `johan_consent_renew` / `johan_consent_afraid` | Harem_Consent | 동의 갱신 방식 |
| **`harem_ok_johan`** | Harem_Consent 끝 | 다자 동의 완료. ‘확인 못 한 사람 있음’ 선택 시 설정되지 않고 종료 |

### 외부 의존성
- `ledger_truth`: 공통 6장에서 설정(트루 엔딩 전제). 이 루트는 직접 설정하지 않았다.
- 엔딩 진입 조건(노멀/굿/트루의 호감·신뢰·이벤트·플래그)은 **호스트가 판정**한다. 노드 안에서 재검사하지 않는다.
- E4 `johan_e4_column_published` 를 7장 위기 해소 연출이 참조한다(공통 7장 작가와 합의 필요).
- Harem_Table(공통, 나중 작성)에서 구성원 간 관계를 먼저 쌓는다.

## 3. 노드 인덱스

| 노드 | 파일 | 줄(대사+선택지) | 비고 |
|---|---|---|---|
| `Johan_Talk_01` 오늘의 별점 | johan_talk.yarn | 33 | 솔직한 별점=애정 / 혼자 먹는 식사 |
| `Johan_Talk_02` 필명과 분모 | 〃 | 32 | 팔로워 12,000 / 「혀끝의 항구」. 오답=홍보 부탁 |
| `Johan_Talk_03` 첫 모금 | 〃 | 33 | 선 확인(“여기까지라고 해 줘”), 맛 한 문장 |
| `Johan_Talk_04` 냉장고 | 〃 | 31 | 외로움을 농담으로. 오답=“또 농담이죠?” |
| `Johan_Talk_05` 목요일 리뷰 | 〃 | 31 | 약속 지키기. 오답=이용 의심 |
| `Johan_Talk_06` 각주 | 〃 | 31 | 캐묻지 않고 기다리기 |
| `Johan_Talk_07` 단골의 이름 | 〃 | 32 | 도장 카드·이름 말하기 연습. 정답=고마웠다는 인사 / 오답=칼럼으로 폐업을 막았을 거라는 말(이용·홍보 결). 플래그 `johan_talk07_name/_thanks` |
| `Johan_Talk_08` 퇴고 | 〃 | 29 | 지운 ‘진짜’·마지막 문장 지우기. 오답=말도 퇴고하라(수다 놀리기)·팔릴 문장 권유. 플래그 `johan_talk08_real/_plain` |
| `Johan_Gift_Like/Neutral/Dislike` | johan_gift.yarn | 12/13/13 | 호스트가 `$gift_name` 설정. 싫어함=홍보 쿠폰 묶음(‘사는’ 느낌) |
| `Johan_Date_BookStreet` | johan_date_book_street.yarn | 24 | 헌책방 거리(3장 해금), `bg book_street day` |
| `Johan_Date_NightMarket` | johan_date_night_market.yarn | 24 | 야시장 부두(2장 해금), `bg night_market night` |
| `Johan_Date_Greenhouse` | johan_date_greenhouse.yarn | 24 | 온실 식물원(낮), `bg greenhouse day`. 향·이름표·물방울 소리(냉장고 소리의 반전). 플래그 `johan_greenhouse_smell/label/wander`, `johan_date_greenhouse_done` |
| `Johan_Date_Generic` | johan_date_generic.yarn | 19 | 그 외 스팟 |
| `Johan_E1` 첫 리뷰 | johan_E1.yarn | 41 | 2장 · 호감 10 · CG `JOHAN_E1` |
| `Johan_E2` 취재 노트 | johan_E2.yarn | 41 | 3장 · 호감 30/신뢰 8 · CG `JOHAN_E2` |
| `Johan_E3` 빈 좌석 | johan_E3.yarn | 42 | 5장 · 호감 50/신뢰 16 · CG `JOHAN_E3` |
| `Johan_E4` 쓰지 않은 칼럼 | johan_E4.yarn | 42 | 7장 · 호감 68/신뢰 25 · CG `JOHAN_E4` |
| `Johan_E5` 달빛 아래의 약속 | johan_E5.yarn | 45 | 8장 · 호감 84/신뢰 32 · CG `JOHAN_E5`(굿 엔딩 겸용) |
| `Johan_End_Normal` 단골 자리 | johan_end_normal.yarn | 35 | CG 없음(공통 엔딩 일러스트 재사용) |
| `Johan_End_Good` 다시 올 이유 | johan_end_good.yarn | 36 | CG `JOHAN_E5` 재사용 |
| `Johan_End_True` 별 다섯, 공정하게 | johan_end_true.yarn | 37 | CG `JOHAN_E6`(추가 1장) |
| `Johan_Harem_Consent` | johan_harem.yarn | 26 | 동의 확인, 끝에 `harem_ok_johan` |
| `Johan_Harem_Epilogue` | 〃 | 8 | 다자 엔딩 후 식탁 장면(타 인물 대사 없음) |

## 4. 서사 설계 메모

- **‘이용하지 않음’의 증명 구조**: E2는 ‘쓸 수 있는 기사’를 쓰지 않고(허락받고 쓰는 글도 광고라고 거절), E4는 ‘우호 리뷰’ 제안을 3초 만에 거절하고 가게가 한 줄도 안 나오는 칼럼을 쓴다(제안받은 **사실**만 공개). 엔딩의 장부도 요한이 폭로하는 게 아니라 **당사자 전원의 동의**를 받아 함께 쓴다(트루).
- **별 두 개의 빚**: E1에서 5점 초고를 구겨 3점으로 고친 뒤 안주머니에 넣는다. 초고 여백 “남은 별 두 개는 방문으로 갚는다”가 E5(트루 열쇠) → 굿 엔딩 마지막 장면 → 트루 엔딩(액자, 별 두 개 완납)으로 회수된다.
- **말의 길이**: 호감이 오를수록 요한의 대사는 오히려 짧고 단정해진다(E5 고백). 장난이 사라지는 지점이 곧 진심.
- **호출 약속**: 안경을 올리면 진담(Talk_03/04), 벗으면 가장 솔직(E5·굿·트루), “이 말은 한 번만”.

## 5. 성별 변형 노트

- 스크립트에 성별 분기가 없다. 변하는 것은 `address_terms.csv` 뿐(남성 close에서만 “요한 형”).
- 주인공 대사·지문에는 이름/함수 호칭만 사용하고, 요한의 이름을 주인공이 직접 부르는 자리는 `{player_calls("johan")}` 로 처리했다(입력된 호칭표가 구간별로 바뀌므로). 함수 값 뒤에는 조사를 붙이지 않는 문형.
- 요한의 주인공 호칭은 ‘점주님/사장님’(직함)과 `{calls_player("johan")}`, 3인칭 대명사는 쓰지 않는다. ‘아저씨’ 등 성별 어휘도 제거(검사 통과).
- 영어 폴백: 모든 노드가 `en` 로케일에서 실행된다(텍스트는 원문 폴백). 번역 시 “they are” 문형을 피한다.
- 주인공의 말투는 해요체 고정.

## 6. 톤 체크리스트

- [x] 말 많음·수사·자기 정정, 진심에서 문장이 짧아짐
- [x] 외로움이 농담으로 드러남(냉장고, 빈 의자, 분모, 각주)
- [x] 플러팅이 누구도 비하하지 않음. 선 확인 장치 + 접촉은 질문 후 + 속도 선택권
- [x] 주인공 압박 없음: 대답 강요 없음(“대답은 안 줘도 돼”), 거절/보류 선택지에도 보상 구조 유지
- [x] 이용하지 않음: E2/E4/트루 모두 거절·동의 중심
- [x] 종 클리셰 없음(여우=교활 불사용). 귀·꼬리는 몸짓으로만
- [x] 다자: 모르는 사람이 있으면 진행하지 않음, 질투는 통제가 아닌 말로 보고, 비교 금지 합의, 시간은 의무가 아닌 선물
- [x] 수위: 포옹·키스(굿/트루, 사전 질문) 이하, 페이드
- [x] 성별 단어 정적/렌더 검사 0건, 모든 줄 고유 `#line:`(접두사 `johan_talk` `johan_gift` `johan_dbs` `johan_dnm` `johan_dg` `johan_e1`~`e5` `johan_en` `johan_eg` `johan_et` `johan_harem`)

## 7. QA 결과

- `SCENARIO_ONLY=routes/johan sh tools/csharp_check/scenario_check/run.sh`: 28 checks, 0 failures(린트 0, 중복/누락 id 0, 미도달 0, 실행 6,600회 오류 0, 미해결 `{}` 0, 성별 단어 0).
- 주의: 공용 하네스는 호감 구간 조작을 `Chars["howl"]` 에만 적용하므로 `$aff_johan` 분기는 0으로만 실행된다. 요한 호감 5/45/75 구간을 모두 돌리기 위해 하네스 사본(`Chars["howl"]`→`["johan"]`)으로 추가 실행했고, 동일하게 28 checks 0 failures(실행 6,600회).
