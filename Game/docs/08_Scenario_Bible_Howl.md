# 08. 시나리오 바이블 — 하울 (스테이지 2)

> 범위: 1장 프롤로그, CE01, 하울 대화/선물/데이트, 하울 E1·E2 대본. E3~E5·엔딩·다자 조각은 부록 A 참조(아웃라인은 대본으로 변환 후 삭제됨).
> 규칙 근거: `00_GDD.md` §1.3·§2.1, `07_Dialogue_Spec.md` §4·§5. 검증: `sh tools/csharp_check/scenario_check/run.sh`.
> 파일 위치: `UnityProject/Assets/_Project/Dialogue/` (`common/`, `routes/howl/`).

## 1. 하울 음성 시트

회색 늑대, 31세, 심야 바텐더. 키워드 ‘신뢰’. 말보다 행동. 애정은 ‘지키는 일’과 ‘손이 먼저 가는 일’로 드러난다.

| 항목 | 규칙 |
|---|---|
| 문장 | 짧게. 한 줄 1~2문장. 설명보다 사실(“의자, 닦아놨다.”) |
| 말줄임 | 대사 첫머리나 감정이 흔들릴 때 `…` 를 쓴다. 한 줄에 두 번 이상 남발하지 않는다 |
| 어미 | 반말 기본(“~야/~다/~지”), 낯선 사이(호감 30 미만)에서는 존댓말이 반쯤 섞인다(“그럼 들어와도 돼. 여긴 이제 당신 가게니까”). 호감이 오를수록 반말 비율 증가 |
| 감사 | “고맙다”는 한 번. 두 번 말하면 결정적 장면(바이블 규칙) |
| 칭찬 | 받는 것이 서툴다(과한 칭찬 = 오답). 하는 것도 짧게 |
| 호칭 | 주인공을 `{calls_player("howl")}`(호감 구간에 따라 “이름 씨” → 이름). 2인칭은 최소화하고, 필요하면 “당신”(낯선 사이)/“너”(가까운 사이) |
| 감정 표현 | 귀와 손짓(“귀가 먼저 알려 주겠군”)·사물(얼음, 불, 의자, 잔)로 우회 |
| 은유 | 얼음 한 조각(‘한 잔 더/곁에 있겠다’), 켜 둔 불(기다림), 의자(환대), 잠긴 서랍(감추고 싶은 과거) |

### 샘플 대사(GDD §2.1 5줄과 같은 결)
- “…왔어? 의자, 닦아놨다.”
- “필요하면 말해. 안 해도 필요한 건 챙겨 둘 테니까.”
- “그 잔은 내가 닦지. 손 다친다.”
- “…고맙다는 말은 한 번이면 돼. 두 번 들으면 어색해.”
- “가게 문 닫을 때까지 있을게. 오늘은 그냥, 그러고 싶어.”
- (신규) “조용한 거랑 외로운 건 달라. 가끔은 구분해 줬으면 한다.”
- (신규) “그건 걱정이 아니라 거리야.”

### Do / Don't

| Do | Don't |
|---|---|
| 행동·사물로 감정을 말한다(얼음, 퓨즈, 의자) | 긴 독백, 감정의 직설 설명 |
| 선택지의 정답 = 정직·시간 약속·강요 없는 곁 | 아첨/과한 칭찬, 위험 숨기기, 약속 불명확, 소음 강요, 사고 경위 캐묻기를 정답으로 두기 |
| 오답에도 하울은 화내지 않고 ‘조용해진다’(−1/−1, 상처가 아니라 거리) | 오답을 폭발·조롱·벌처럼 연출 |
| 호감 구간별로 같은 사건을 다른 온도로(낯섦→익숙→가까움) | 낮은 호감에서 연인 같은 대사 |
| 접촉은 동의 후, 스킨십은 손끝~포옹(키스는 E5 이후, 페이드) | 소유·집착·질투 표현, ‘내 거’ 류의 대사 |
| 늑대=위험 같은 종 클리셰를 쓰지 않는다(그는 ‘귀가 예민한 사람’일 뿐) | 종 특성을 결론/놀림에 쓰기, 주인공의 성별로 감정선 바꾸기 |

## 2. 플래그 목록

| 플래그 | 설정 위치 | 의미/용도 |
|---|---|---|
| `call_pref_name` / `call_pref_honorific` / `call_pref_later` | Ch1_Intro | 호칭 선호 질문의 답. `call_pref_name` 이면 하울은 즉시 이름만 부른다(`{name()}`). 호스트가 호칭 설정 UI와 연결할 수 있음 |
| `prologue_resolve_open` / `prologue_resolve_unsure` | Ch1_Kitchen | 가게를 이을 마음의 정도(분위기용) |
| `ch1_prologue_done` | Ch1_Kitchen | 프롤로그 완료 |
| `ce01_asked_uncle`, `ce01_done` | CE01 | 이웃 대화/완료 |
| `howl_party_bar_seat` | Talk_04 | 파티 날 하울의 ‘바 뒤 자리’ 약속 |
| `howl_promised_to_tell` | Talk_05 | “문제가 생기면 바로 말하겠다” 약속(E4에서 회수) |
| `howl_wants_lighthouse` | Talk_06 | 등대 동행 약속(데이트 해금 연출용) |
| `howl_date_lighthouse_done`, `howl_date_fish_market_done` | 데이트 노드 | 재방문 변형 분기용. 호스트가 읽음(`flag(...)`) |
| `howl_lighthouse_listen/shop/quiet`, `howl_market_try/watch/ask`, `howl_generic_comfy/ask` | 데이트 노드 | 데이트 중 취향 선택 기록(수치 없음) |
| `howl_e1_trust` | E1 | 얼음 제스처의 뜻을 읽음(+3/+1) |
| `howl_e1_asked` | E1 | 말로 듣고 싶다고 함(+1) |
| `howl_e1_doubt` | E1 | 자기 비하로 받음(보너스 없음) |
| `howl_e2_accepted` | E2 | 도움을 받아들임(+3/+1) |
| `howl_e2_debt` | E2 | ‘갚겠다’로 받음(보너스 없음, 장부 모티프) |
| `howl_e2_refused` | E2 | ‘혼자 할 수 있다’(−1/−1) |
| `howl_drawer_seen` | E2 | 서랍 존재 확인(항상) |
| `howl_drawer_together` / `howl_drawer_forced` / `howl_drawer_waiting` | E2 | 서랍 접근 방식(함께 +5/+1.5 · 강제 −1/−1 · 보류) |
| (추가) `howl_e3_*`, `howl_e4_*`, `howl_key_photo`/`howl_key`, `howl_e5_*`, `howl_key_given` | E3~E5 | 부록 A.1 참조 |

수치 규칙: 대화 선택지 정답 aff +5 / trust +1.5, 오답 aff −1 / trust −1(`balance/actions.csv`). 이벤트 완료 보상(+12/+4), 선물(+8/+3/−5), 데이트(+12/+1.5)는 **호스트가 적용**하므로 해당 노드는 aff/trust 를 바꾸지 않는다. 이벤트 안의 `<<aff>>`는 선택지 보너스(1~5)만. 한 번의 대화 노드가 최대 +10/+3 까지 줄 수 있으므로 호스트는 `AdjustAffection`(감쇠·챕터 캡)을 통과시킬 것(`06_Romance_Notes.md` §4).

## 3. 노드 인덱스

| 노드 | 파일 | 줄(대사+선택지) | 진입 조건/비고 |
|---|---|---|---|
| `Ch1_Intro` → `Ch1_Kitchen` | common/prologue_ch1.yarn | 25 + 29 (실행 시 42~43) | 1장 시작. 호칭 선택 포함 |
| `CE01_FirstGuest` | common/CE01_FirstGuest.yarn | 27 | 1장, 개업 전. 화자 `npc_baker/npc_fisher/npc_florist` |
| `Howl_Talk_01` 마감 후 의자 | routes/howl/howl_talk.yarn | 28 | 밤 행동 대화. `$aff_howl` 로 3구간 변형 |
| `Howl_Talk_02` 새벽 어시장 | 〃 | 26 | 시간 약속 |
| `Howl_Talk_03` 잔과 도움 | 〃 | 27 | 돌봄 vs 거리두기 |
| `Howl_Talk_04` 소음/파티 | 〃 | 25 | 강요 금지 |
| `Howl_Talk_05` 숨기지 않기 | 〃 | 24 | 위험 공유 |
| `Howl_Talk_06` 등대 사진 | 〃 | 27 | 과거 암시, 캐묻기 금지 |
| `Howl_Gift_Like/Neutral/Dislike` | howl_gift.yarn | 14/13/13 | 호스트가 `$gift_name`(문자열) 설정 후 시작 |
| `Howl_Date_Lighthouse` | howl_date_lighthouse.yarn | 21 | 등대 언덕(1장 해금). `bg lighthouse dusk` |
| `Howl_Date_FishMarket` | howl_date_fish_market.yarn | 23 | 새벽 어시장(1장). `bg fish_market dawn` |
| `Howl_Date_Generic` | howl_date_generic.yarn | 19 | 그 외 스팟 |
| `Howl_E1` 첫 잔 | howl_E1.yarn | 52 (실행 42~43) | 1장, 호감 10. 얼음 CG `HOWL_E1` |
| `Howl_E2` 잠긴 서랍 | howl_E2.yarn | 57 (실행 40~43) | 3장, 호감 30/신뢰 8. CG `HOWL_E2` |

장면 커맨드 약속: `bg` 장소 id = `cafe`, `harbor`, 그리고 `balance/spots.csv` 의 id(`lighthouse`, `fish_market`…); 시간 = `dawn|morning|day|dusk|night`. `show_standing howl <neutral|smile|shy|angry|sad|surprised>`(의상 인자는 쓰지 않음). 스탠딩 숨기기 커맨드는 카탈로그에 없으므로 쓰지 않았다(필요 시 UI 쪽에서 `bg` 전환 때 정리).

## 4. 성별 변형 노트

- 스크립트는 성별 분기가 **없다**. 변하는 것은 `address_terms.csv` 의 `calls_player`/`player_calls` 뿐(남성 close에서만 “하울 형”, 여성·논바이너리·중립은 “하울”).
- 주인공 대사/지문에 그/그녀/남자/여자/오빠/언니/형/누나 등을 직접 쓰지 않는다. 사람을 부르는 말은 `{name()}`, `{calls_player("howl")}`, `{player_calls("howl")}`, `{honorific()}` 만 사용. 다른 NPC 호칭(`{player_calls("beomjun")}` 등)도 함수로.
- 한국어 조사 문제 회피: 함수 값 뒤에는 조사를 붙이지 않고 쉼표/띄어쓰기로 끊는다(“{player_calls("howl")}, …”). 예외로 받침과 무관한 “한테/하고/도”는 허용. 선물 이름 `{$gift_name}` 뒤에도 조사를 붙이지 않았다(“{$gift_name}, 드리고 싶어서…”).
- 영어 폴백: `en` 로케일에서도 모든 노드가 실행된다(텍스트는 한국어 원문 폴백, 호칭은 en 열). 번역 시 `they are` 형태를 피한다(07 §4-4).
- 주인공 시점 문장은 해요체 고정(성별·친밀도와 무관). 하울의 어투만 호감 구간에 따라 변한다.

## 5. 톤 체크리스트(적용한 것)

- [x] 하울의 짧은 문장·`…`·행동 중심. 샘플 5줄과 같은 결
- [x] 주인공 성별 중립(정적 검사 0건 + 논바이너리/중립 렌더 검사 0건)
- [x] 수치 규칙이 성별과 무관(분기는 호칭뿐)
- [x] 종 클리셰 없음(늑대=위험/충성 같은 결론 없음; 귀의 민감함은 인물 특성으로만)
- [x] 정체성 희화화/외모 비하/성별 개그 없음
- [x] 소유·집착·츤데레식 가학 없음. 오답 반응은 ‘조용해짐’ 정도로 제한, 곧 풀림
- [x] 동의 존중: 접촉은 E3 아웃라인에서 동의 대사 필수, E1~E2는 접촉 없음
- [x] 수위: 키스·포옹 이하, 장면 전환은 페이드
- [x] 갈등은 임대료·재개발·체인점·상실 같은 생활 문제(종 갈등 없음)
- [x] 모든 대사/선택지/지문에 고유 `#line:` id(접두사: `pro_` `ce01_` `howl_talk_` `howl_gift_` `howl_dl_` `howl_dfm_` `howl_dg_` `howl_e1_` `howl_e2_`; 부여 도구 `tools/csharp_check/scenario_check/number_lines.py`)
- [x] 린트 오류·중복 id·누락 id·도달불가 노드 0건

## 6. QA 실행

```
sh tools/csharp_check/scenario_check/run.sh
DUMP="Howl_E2,female,45,02" mono /tmp/morphe_scenario_check.exe <Dialogue 폴더> <address_terms.csv>   # 한 경로 텍스트 출력
```
하네스는 (a) 전 `.yarn` 파싱·린트, (b) 진입 노드 16개를 성별 4 × 로케일 2 × 호감 3(5/45/75) × 플래그 사전설정 2 × 선택지 전 경로로 실행(커맨드 인자 검증, 미해결 `{}` 검사), (c) 성별 단어 정적/렌더 검사와 파일별 분량표를 낸다. 새 노드를 만들면 `Program.cs` 의 `Entries` 에 추가한다.

---

# 부록 A. 스테이지 3 확장 — E3~E5, 엔딩, 다자 조각

> 범위: `howl_E3.yarn` `howl_E4.yarn` `howl_E5.yarn` `howl_endings.yarn` `howl_harem.yarn`. 아웃라인 파일(`howl_E3_to_E5_outline.md`)은 대본으로 변환되어 삭제(superseded)됨.
> 일러스트 요청서: `docs/art_requests/Howl.md`. 검증: `SCENARIO_ONLY=routes/howl sh tools/csharp_check/scenario_check/run.sh`.

## A.1 추가 플래그

| 플래그 | 설정 위치 | 의미/용도 |
|---|---|---|
| `howl_e3_comfort` | E3_Confide | 위로(+4/+1). 하울이 처음 ‘고맙다’를 한 번 말함 |
| `howl_e3_stay` | E3_Confide | 말없이 얼음 잔 건네며 곁에 앉기(+3/+3, 신뢰 가중). E1 `howl_e1_trust` 있으면 ‘알아듣는군’ 추가 |
| `howl_e3_pressed` | E3_Confide | 사고 경위 캐묻기(오답 −1/−1). 흉터 접촉·CG 없이 마감 |
| `howl_guilt_shared` | E3 세 선택지 모두 | 죄책감을 털어놓았다는 공통 기록(E5·엔딩 참조 가능) |
| `howl_e4_trust` | E4_Offer | ‘같이 해요’(+5/+2). 하울의 위치가 ‘지켜 주는 쪽’에서 ‘함께 서는 쪽’으로 |
| `howl_e4_fail` | E4_Offer | ‘혼자 해결’(−2/−2). 얼음 제스처 없이 퇴장 |
| `howl_e4_trust_late` | E4_Retry | 다음 날 만회(+3/+1, 정식 수락의 절반). 스크립트에 flag 해제 커맨드가 없으므로 `howl_e4_fail` 은 남는다 → 판정은 `howl_e4_trust` 또는 `howl_e4_trust_late` 로 한다 |
| `howl_key_photo` + `howl_key` | E4_Drawer(함께/보류 후 수락, 단서 보유), E5_PhotoRetry(숨은 조건 선택) | 서랍 속 사진을 함께 본 기록. GDD 이름 `howl_key_photo` 와 공통 계약 별칭 `howl_key` 를 **같은 분기에서 같이** 설정 |
| `howl_e5_promise` / `howl_e5_stay_only` | E5_Ask | 지킬게요(+4/+2, 굿 CG) / 곁에 있을게요(+2/+1, 노멀 분위기, 굿 CG 없음) |
| `howl_key_given` | E5_Key | 트루 조건 충족 시 열쇠 복사본(초승달 문양) 수여 |
| `end_howl_normal/good/true` | 엔딩 노드 | 엔딩 도달 기록 |
| `harem_ok_howl` | Howl_Harem_Consent | 다자 동의 확인 완료 |

외부 의존 플래그(다른 작가/공통 챕터가 설정): `howl_promised_to_tell`(Talk_05), `drawer_key_clue`(6장 CE06), `ledger_truth`(6장 장부). `drawer_key_clue` 가 없으면 E4/E5 의 서랍 사진 분기는 보이지 않으며 트루는 불가능해진다(의도).

수치 규칙: 이벤트 완료 보상(+12/+4)은 호스트가 `<<end_event howl N>>` 에서 적용하므로 스크립트의 aff/trust 는 선택지 보너스만. 엔딩 노드는 수치를 바꾸지 않는다(`end_event` 미사용).

## A.2 노드 인덱스(추가분)

| 노드 | 파일 | 비고 |
|---|---|---|
| `Howl_E3` → `Howl_E3_Confide` → `Howl_E3_End` | howl_E3.yarn | 5장, 호감 50/신뢰 16. 호감 60 이상이면 하울이 `{name()}` 만 부른다(1회). CG `HOWL_E3`(동의 후 접촉 컷) |
| `Howl_E4` → `_Offer` → `_Drawer`(수락 시) → `_End` | howl_E4.yarn | 7장, 호감 68/신뢰 25. `Howl_E4_Retry` 는 실패 다음 날 호스트가 호출하는 진입 노드. CG `HOWL_E4`(서랍 속 사진) |
| `Howl_E5` → `_Ask` → `_Answer` → `_PhotoRetry` → `_Key` → `_End` | howl_E5.yarn | 8장, 호감 84/신뢰 32. 키스는 동의 후 암전. CG `HOWL_E5`(굿 겸용) |
| `Howl_End_Normal` / `_Good` / `_True` | howl_endings.yarn | 각 35~38줄. True 는 CG `HOWL_E6`, ‘고맙다’ 두 번(둘째는 작게) |
| `Howl_Harem_Consent` / `Howl_Harem_Epilogue` | howl_harem.yarn | 30줄 / 9줄. 끝에 `harem_ok_howl` |

## A.3 서랍/열쇠 흐름(요약)

1. E2: `howl_drawer_seen`, 접근 방식(함께/강제/보류).
2. E4_Drawer(수락 경로만): `together`+단서 → 함께 열어 사진 확인 → `howl_key_photo`+`howl_key`. `waiting` → 하울이 먼저 제안, 승낙 + 단서면 동일. `forced` → 하울이 사진을 보지 않으려 함(플래그 없음). 혼자 해결(실패) 경로는 서랍 장면 자체가 없다.
3. E5_PhotoRetry: 아직 키 플래그가 없고 `drawer_key_clue` 가 있으면 하울이 먼저 제안 → ‘같이 봐요’ 선택(조건부 선택지)으로 같은 두 플래그 획득. 트루 불가 방지용 안전망.
4. E5_Key: `howl_key_photo` + `ledger_truth` + 호감 95 + 신뢰 46 → 열쇠 복사본. 이 값들은 `balance/endings.csv` true 행과 동일하다.

## A.4 목소리 시트 보강

- ‘고맙다’ 규율: E3 첫 감사(한 번), 엔딩 노멀에서 한 번 쓰고, 트루 마지막에 두 번(둘째는 작게). 트루의 동료 화해 장면에서는 ‘다행이다’로 대체해 두 번째 고맙다의 무게를 지킴.
- 약속 어휘: ‘약속은 어기지 않는다’, ‘숨기지 않기로 했으니까’(E4·Harem_Consent), ‘돌아올 자리’(E5·굿).
- 얼음 한 조각의 뜻 변천: E1 한 잔 더 → E3 곁에 있겠다 → E4 같이 서자(주인공이 건넴) → E5 돌아오겠다 → 굿/노멀 두 잔 → 다자 ‘같이 마시자’.
- 다자: 질투는 ‘내 몫’으로 다루고 상대를 통제하지 않는다. 비밀이 되면 ‘신뢰가 아니라 거래’라며 전원 인지를 요구(가드레일 1·2·3 충족). 서로 멈출 수 있어야 계속할 수 있다는 합의.

## A.5 성별 변형 메모(추가분)

- 신규 노드도 성별 분기 없음. 주인공 지칭은 `{name()}`/`{calls_player("howl")}`/`{player_calls("howl")}` 만 사용(남성 close 에서만 ‘하울 형’). 함수 뒤에는 조사 대신 쉼표/‘도’만 붙임.
- CG 는 모두 주인공의 얼굴 없이 손·뒷모습으로 구성 → 어떤 주인공 프리셋에도 재사용 가능.

## A.6 톤 체크리스트(추가분)

- [x] 오답(캐묻기, 혼자 해결, 비밀 유지 제안) 반응은 화냄이 아니라 ‘조용해짐’ + 다음 장면에서 회복 장치(E4_Retry, Consent 내 정정)
- [x] 접촉은 항상 말로 동의(E3 ‘만져 봐도 돼요?’, E5 ‘안아도 될까’·‘해도 돼요?’ 후 암전)
- [x] 키스는 E5 이후 암전 처리, 노골적 묘사 없음
- [x] 사고(크레인)는 경위 묘사 최소화, 죄책감의 ‘늦음’에 초점. 늑대/경호=충직·위험 같은 종 클리셰 없음
- [x] 소유·집착 표현 없음(‘제 사람’ 표현은 하울 스스로 무겁다고 인정하는 장면으로 처리)
- [x] 다자 가드레일(전원 인지, 자발적 동의, 키워드 ‘신뢰’ 유지, 멈출 수 있는 권리)
- [x] 모든 줄에 고유 `#line:` id(접두사 `howl_e3_` `howl_e4_` `howl_e5_` `howl_end_` `howl_harem_`)

## A.7 QA 결과(`SCENARIO_ONLY=routes/howl`)

린트 0건(진입 노드 25개, 미도달 0), 실행 오류 0(성별 4×로케일 2×호감 3×플래그 2×전 경로), 성별 중립 0건. 하네스의 호감 레벨(5/45/75)로는 E5_Key(호감 95·신뢰 46) 가 실행되지 않아, 임시로 호감 97/신뢰 48 로 바꾼 하네스 사본으로 별도 실행하여 오류 0 을 확인했다(저장소 파일 변경 없음).
