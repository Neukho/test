# 08. 시나리오 바이블 — 범준

> 범위: 대화 9, 선물 3, 데이트 4(도장·바닷가·어시장·공통), 개인 이벤트 E1~E5, 엔딩 3, 다자 조각 2. 위치 `UnityProject/Assets/_Project/Dialogue/routes/beomjun/` (엔트리 `entries.txt`).
> 근거: `00_GDD.md` §2.3, `10_Route_Structure.md`, `07_Dialogue_Spec.md`, `WRITER_BRIEF.md`. 검증: `SCENARIO_ONLY=routes/beomjun sh tools/csharp_check/scenario_check/run.sh`.

## 1. 범준 음성 시트

호랑이, 34세, 건물주 겸 합기도 도장 관장. 키워드 ‘의지’(意志이자 의지(依支)). 힘이 센 사람이 아니라 **책임을 지는 사람**이다.

| 항목 | 규칙 |
|---|---|
| 문장 | 큰 소리, 짧은 감탄(“하하!”), 직설 비유(컵·매트·얼음 상자·사탕). 한 줄 1~2문장 |
| 어미 | 호감 30 미만: 해요체가 섞인 호탕함(“~요!”). 30 이상: 반말 위주(“~야/~지/~잖아”). 진지해지면 문장이 짧아지고 소리가 *낮아진다* |
| 감탄 | “하하!”는 기쁨·얼버무림 모두에 쓴다. 얼버무릴 때는 직후에 “…”가 붙는다 |
| 호칭 | 주인공 `{calls_player("beomjun")}`(이름 씨 → 사장 → 이름). 본인 호칭은 `{player_calls("beomjun")}`(범준 씨 → 관장님 → 범준 형/오빠/범준). 함수 뒤에 한국어 조사를 직접 붙이지 않는다(‘{...}한테’만 사용, ‘{...}가/는/라면’ 금지) |
| 눈치 | 모르는 것을 숨기지 않고 **묻는다**(“눈치는 어디서부터 챙기는 거야? 진짜로 궁금해서 묻는 거야”). 오답이어도 화내지 않고 조용해질 뿐이다 |
| 외로움 | “…아니, 안 외로워. 그냥 도장이 조금 조용해서 그래.” 조용한 장면(E2·Talk_05)에서만 새어 나온다 |
| 은유 | 낙법(넘어지는 법=믿는 법), 받아서 흘리기(위로·듣기), 이름표(학생·출석), 단 과자 스무 개(희망), 주황 탭 파일철(특약), 손바닥 신호(볼륨 조절) |

### 샘플 대사
- “하하! 그래, 바로 그거야! 일단 먹고 생각하자고!” (GDD)
- “임대료? …그, 뭐냐, 사실 올리는 건 나도 마음이 불편해. 근데 규정이 말이지.” (E1에 사용)
- “강하다고 안 아픈 건 아니야.” (Harem_Consent)
- “낙법은 넘어지는 법이 아니라 믿는 법이었네.” (E5)
- “나도 기대도 돼? 서투르지만.” (E5, GDD 샘플 5번의 뒤집기)

### 서사 규칙(요약)
- 눈치 없음 → **개선**: Talk_01(볼륨 신호) → E3(위로 연습) → 엔딩(우는 아이에게 “아팠구나” 하고 눕혀 말하기). 조롱·희화화 금지, 본인이 먼저 묻고 고친다.
- 강함 = 책임: 건물 매각 거절(E4), 새벽 짐 나르기(Talk_02), 쓰레기 줍기(Date_Beach). 위압·완력으로 문제를 해결하는 장면 없음. 합기도는 ‘다치지 않게 끝내는’ 기술로만 쓴다.
- 접촉은 항상 먼저 묻고 동의 후(데이트·E5·엔딩·다자). 질투는 인정하되 통제·소리로 번지지 않는다.

## 2. 플래그표

| 플래그 | 설정 위치 | 의미 / 수치 | 읽는 곳 |
|---|---|---|---|
| `beomjun_e1_listen` | E1 | 출처를 물음 (+3/+1) | – |
| `beomjun_e1_deal` | E1 | 거래를 제안(거절당함, 수치 없음) | – |
| `beomjun_e1_clash` | E1 | 인정머리 없다고 비난(−1/−1) | – |
| `beomjun_e1_respect` | E1 | 서류철을 묻지 않음(+3/+1) | – |
| **`beomjun_e1_contract`** | E1 | 서류철 ‘특약’ 면을 봄(+1) | **E5 숨은 선택지 조건** |
| `beomjun_e2_gentle` / `_ask` / `_confront` | E2 | 도장이 빈 이유를 대하는 태도(+3/+1 · 0 · −1/−1) | – |
| `beomjun_e2_attend` | E2 | 출석 이름 부르기에 응함(+3/+1) | – |
| **`beomjun_e2_folder`** | E2 | 신발장 위 파일철을 알아봄(+1) | **E5 숨은 선택지 조건** |
| `beomjun_e3_ask` / `_gift` / `_excuse` | E3 | 조언 선택(+4/+1 · 0 · −1/−1) | – |
| `beomjun_e3_practice` / `_soft` / `_harsh` | E3 | 위로 연습 피드백(+4/+1 · 0 · −1/−1) | – |
| `beomjun_e4_honest` / `_together` / `_pressure` | E4 | 매각 앞의 선택(+3/+1 · +4/+1 · −1/−1) | – |
| `beomjun_dojo_open` | E4 | ‘넘어져도 괜찮은 일요일’ 이웃 교실 개설(항상) | 호스트(참고) |
| `beomjun_e5_offer` / `_mutual` / `_misstep` | E5 | 의지 고백 응답(+4/+1 · +3/+2 · −1/−1) | – |
| `beomjun_e5_enough` | E5 | 서류철을 보지 않음(수치 없음) | – |
| **`beomjun_key`** | E5 | **트루 열쇠**: 임대 계약서 특약(+3/+2) | `Beomjun_End_True` 전제(호스트 판정) |
| `beomjun_dojo_fall/stance/tea`, `beomjun_date_dojo_done` | Date_Dojo | 데이트 선택 기록 / 재방문 | 재방문 분기 |
| `beomjun_beach_trash/walk/uncle`, `beomjun_date_beach_done` | Date_Beach | 데이트 선택 기록 / 재방문 | 재방문 분기 |
| `beomjun_generic_comfy/ask` | Date_Generic | 기록용 | – |
| `beomjun_normal_slow/partner`, `beomjun_good_throw/afraid`, `beomjun_true_soft/loud` | 엔딩 | 기록용(수치 없음) | – |
| `beomjun_harem_noexit/strong` | Harem_Consent | 동의 확인 방식 기록 | – |
| **`harem_ok_beomjun`** | Harem_Consent 끝 | 다자 동의 | `Ending_Harem2/3`(공통) |

수치 규칙: 대화 선택지 정답 +5/+1.5, 오답 −1/−1. 이벤트 완료 보상(+12/+4)·선물·데이트는 호스트가 적용. 이벤트 안의 `<<aff>>`는 선택지 보너스(1~4)만.

### 트루 열쇠 설계 (`beomjun_key`)
- 설정 위치: E5 두 번째 선택지 ‘특약 9조요. 처음부터 읽고 싶었어요.’ `<<if flag("beomjun_e1_contract") or flag("beomjun_e2_folder")>>`. 조건이 거짓이면 선택지는 `Available=false`(UI가 숨기거나 비활성 표시).
- 진상: 육 년 전 건물 상속 직후, 범준이 임대차 계약서의 특약을 새로 썼다(이웃을 위해 열려 있는 한 임대료는 협의 없이 인상 불가, 매각 시 가게에 우선 매수 질의). 이유는 어린 시절 삼촌이 값 없이 내준 국물에 대한 ‘이자’. E1의 통지서는 이 조항을 어길 뻔한 것이라 철회가 진심이 된다.
- `ledger_truth` 와의 결합: 삼촌 장부의 연필 메모가 ‘받는 쪽도 알고 있었다’를 밝힌다(`Beomjun_End_True`).

## 3. 노드 인덱스

| 노드 | 파일 | 줄(대사+지문+선택지) | 비고 |
|---|---|---|---|
| `Beomjun_Talk_01` 큰 목소리 | beomjun_talk.yarn | 26 | 볼륨을 ‘함께 맞춘다’ |
| `Beomjun_Talk_02` 새벽 어시장 | 〃 | 23 | 약속·책임 나누기 |
| `Beomjun_Talk_03` 이기지 않는 운동 | 〃 | 21 | 합기도의 철학 |
| `Beomjun_Talk_04` 주머니 속 사탕 | 〃 | 20 | 단 걸 좋아함, 외모 놀림=오답 |
| `Beomjun_Talk_05` 조용한 도장 | 〃 | 23 | 외로움 암시, 캐묻기=오답 |
| `Beomjun_Talk_06` 눈치 없다는 말 | 〃 | 19 | E3 예고 |
| `Beomjun_Talk_07` 복도 전구 | 〃 | 32 | 이름을 아는 책임, 전구만 갈면 건물/사람을 보면 이웃. 오답=업체에 맡기기·오지랖. 플래그 `beomjun_talk07_names/_share` |
| `Beomjun_Talk_08` 동그라미 달력 | 〃 | 31 | 의지=어제 졌어도 다시 일어나기, 빠진 날도 기록(돌아올 자리). 오답=‘실패’·‘의지 약하다’. 플래그 `beomjun_talk08_return/_secret` |
| `Beomjun_Talk_09` 삔 손목 | 〃 | 30 | 쉬는 것도 의지. 오답=지도만이라도 나가기·쉬는 건 약하다. 플래그 `beomjun_talk09_rest/_circle`. 분량 균형용 추가 노드 |
| `Beomjun_Gift_Like/Neutral/Dislike` | beomjun_gift.yarn | 14/13/13 | `$gift_name`. 싫은 선물(눈치 교본)은 ‘내 얘기인가?’로 받음 |
| `Beomjun_Date_Dojo` | beomjun_date_dojo.yarn | 30 | 낙법·균형, 접촉은 먼저 묻기 |
| `Beomjun_Date_Beach` | beomjun_date_beach.yarn | 30 | 쓰레기 줍기, 삼촌의 국물 이야기 |
| `Beomjun_Date_FishMarket` | beomjun_date_fish_market.yarn | 30 | 어시장(낮). `bg fish_market day`. 시장에서는 큰 목소리가 평균, 아이들 이름 기억하기, 소매 잡기 전 질문. 플래그 `beomjun_fm_voice/snack/hold`, `beomjun_date_fish_market_done`(접두사 `beomjun_dfm`) |
| `Beomjun_Date_Generic` | beomjun_date_generic.yarn | 19 | |
| `Beomjun_E1` 임대 통지서 | beomjun_E1.yarn | 44 | 3장, CG `BEOMJUN_E1` |
| `Beomjun_E2` 도장의 밤 | beomjun_E2.yarn | 43 | 4장, 하루의 심부름, CG `BEOMJUN_E2` |
| `Beomjun_E3` 서툰 위로 | beomjun_E3.yarn | 43 | 6장, CG `BEOMJUN_E3` |
| `Beomjun_E4` 개업 연습생 | beomjun_E4.yarn | 41 | 7장, CG `BEOMJUN_E4` |
| `Beomjun_E5` 달빛 아래의 약속 | beomjun_E5.yarn | 42 | 8장, CG `BEOMJUN_E5`(굿 겸용) |
| `Beomjun_End_Normal/Good/True` | beomjun_endings.yarn | 35/35/36 | True: CG `BEOMJUN_E6` |
| `Beomjun_Harem_Consent` / `_Epilogue` | beomjun_harem.yarn | 26 / 9 | 끝에 `harem_ok_beomjun` |

챕터·조건(`balance/events.csv`): E1 3장(호감 10 이상 열림, 스크립트는 ‘처음 도장 방문’을 전제), E2 4장 호감 30/신뢰 8, E3 6장, E4 7장, E5 8장. 모순 없음. 스팟 `dojo`는 3장 해금(E1이 3장이므로 Date_Dojo와 시기 일치).

## 4. 의존성

- 공통 본문: 3장 CE03 ‘임대료 회의’(E1이 철회·재협의를 예고), 4장 하루 합류, 5장 공연(E3의 하울·나비 언급), 6장 장부(`ledger_truth`), 7장 ‘블루하버 카페’ 개점, 8장 결판. `ledger_truth` 는 이 폴더에서 설정하지 않는다.
- 타 캐릭터 언급은 전언으로만: 하울은 ‘말없이 잔 닦기’, 나비는 ‘…그렇게 말하는 거 아니야’. 하루는 E2에 직접 등장(말투: GDD §2.4).
- 조연 화자 `npc_chain_rep`(E4), `npc_kid`(엔딩 Normal).

## 5. 성별 변형 메모

- 스크립트에 성별 분기는 없다. 주인공 호칭은 `calls_player`/`player_calls`와 `{name()}`만 사용.
- 호칭 표(`address_terms.csv`): 남성 close ‘범준 형’, 여성 close ‘범준 오빠’, 논바이너리/중립 close ‘범준’. friendly는 모든 성별 ‘범준 관장님’.
- 조사: 호칭 함수 뒤에는 조사를 붙이지 않는 문형만 사용(‘범준 형가’ 방지).
- 엔딩의 키스·포옹은 성별 무관하게 동일(수위: 키스·포옹·페이드 아웃).

## 6. 톤 체크리스트

- [ ] 눈치 없음이 ‘귀엽다’에서 ‘고친다’로 이동했는가(조롱·무능 코미디 아님)
- [ ] 힘·덩치로 문제를 푸는 장면이 없는가(해결은 대화·책임·합기도의 ‘흘리기’)
- [ ] 강한 척이 로맨스의 매력으로 미화되지 않았는가(외로움은 숨기지 않고 말하게 됨)
- [ ] 접촉 전 동의 질문이 있는가(데이트·E5·엔딩·다자)
- [ ] 질투·소유욕을 미화하지 않았는가(Harem_Consent: 질투는 본인 몫, 통제 없음)
- [ ] 종 고정관념(호랑이=맹수/위협) 결론 없음 — 호랑이는 ‘귀가 먼저 올라가는 사람’
- [ ] 큰 목소리는 ‘고칠 점’이 아니라 ‘함께 맞출 볼륨’(손바닥 신호)
- [ ] 키워드 ‘의지’ 두 뜻이 E5·True에서 회수되는가(의지가 있는 사람 → 의지할 사람이 있는 사람)

## 7. QA 결과(작성 시점)

린트 0건(DUP_NODE/DUP_LINE_ID/MISSING_LINE_ID/UNDEFINED_JUMP/UNREACHABLE 모두 0), 성별 단어 0건, 미등록 화자 0건, 실행 오류 0건(4성별×2로케일×호감 3구간×플래그 사전설정 2×전 경로), 미해결 `{}` 0건.
