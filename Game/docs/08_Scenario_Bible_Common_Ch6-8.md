# 08. 시나리오 바이블 — 공통 6~8장 + 공유 다자/커뮤니티

> 파일: `Dialogue/common/ch06.yarn` `ch07.yarn` `ch08.yarn` `shared_routes.yarn`, 진입 목록 `common/entries_ch6_8.txt`. 근거: GDD §3, `04_Update_v0.2.md §1`, `10_Route_Structure.md §3`, `11_Ending_Logic.md`.
> 공통 장면은 호감/신뢰(`aff`/`trust`)를 바꾸지 않는다. 상태 변화는 플래그뿐. 줄 id 접두사 `ch6_` `ce06_` `ch7_` `ce07_` `ch8_` `ce08_` `shr_`.

## 1. 플롯
- **6장 삼촌의 장부**: Ch6_Intro(뒷방 정리) → CE06_OldPhoto(책상 뒤 낡은 사진: 흰 봉투를 든 이웃들, 곰석의 침묵. 선택: 돋보기로 자세히 보기 = `drawer_key_clue`, 창가 화분 밑 초승달 열쇠 발견 / 곰석에게 묻기 / 기다리기) → Ch6_LedgerTruth(말일 밤 이웃의 봉투 → 모레나의 안식처 기금 공개 → `Ch6_Ledger_Plan`(재개발 열람공고 스크랩, '대응 적립', 체인 소문) → `Ch6_Ledger_Roles`(하울·범준·요한·하루의 반응) → `Ch6_Ledger_Choice`(기금 운영 3택)) → Ch6_Outro(장부 이후의 홀, 블루하버 가림막; CE06 에서 단서를 놓쳤으면 재도전 선택지) = `ch6_cleared`.
- **7장 위기**: Ch7_Intro(달빛 컴퍼니 설립, 직함은 동등한 제안, **끝에 `<<route_choice>>` 정확히 1회**) → CE07(블루하버 개점식: 모든 인물의 E4 압박 심기) → [각 E4] → Ch7_Outro(주 결산, 플래그별 후일).
- **8장 달빛 모퉁이**: Ch8_Intro(연합 영업 준비, 초대 선택) → CE08_TableTogether(길 위의 긴 식탁, 이웃 명부 서명, 구청 보류, 체인 철수가 아닌 공존) → [각 E5] → Ch8_Outro(나비 복귀 보충) → 엔딩.
- **공유**: Harem_Table(구성원 간 우정·존중 + 키워드 재확인 → 각 Consent 로) / Ending_Harem2 / Ending_Harem3 / Ending_Community.

## 2. 플래그 표
| 플래그 | 설정 | 읽는 곳 |
|---|---|---|
| `drawer_key_clue` | CE06 선택 / Ch6_Outro 재도전 | howl E4·E5 |
| `ledger_truth` | Ch6_LedgerTruth(미설정일 때만 공개 연출), 곰석 E4 | 각 `*_End_True`, EndingEvaluator |
| `ce06_done`, `ce06_looked_closely`, `ce06_asked_gomseok`, `ce06_waited` | CE06 | Outro 재도전 가드 |
| `ch6_fund_open` / `ch6_fund_report` / `ch6_fund_board`, `ch6_fund_choice`, `ch6_ledger_done`, `ch6_ledger_known_before` | Ch6 선택 | Ch7/Ch8/엔딩의 후일 변형 |
| `ch6_started`, `ch6_cleared`, `ch7_started`, `ch7_cleared`, `ch8_started`, `ch8_cleared` | 챕터 경계 | 호스트 |
| `company_founded`, `ch7_titles_self`/`_together` | Ch7_Intro | 참고 |
| `ce07_done`, `blue_harbor_open` | CE07 | 참고 |
| `ch8_invite_chain`/`ch8_plan_table`, `ce08_toast_*`, `chain_crisis_resolved`, `ce08_done` | Ch8/CE08 | 곰석 E5 가정('위기 해소') |
| `nabi_returned_common` | Ch8_Outro(E5 미재생 보충) | 참고 |
| `harem_table_done`, `harem_table_individual`/`_slow` | Harem_Table | 호스트 |
| `end_harem2`, `end_harem3`, `end_community` | 엔딩 | 참고 |

## 3. 정합성 매트릭스
| 항목 | 루트 쪽 | 공통 처리 |
|---|---|---|
| 하울 E4 폭풍 전날·조합 지인 | 하울이 직접 인맥 사용을 망설임 | CE07 의 `npc_union` 이 제안, 하울 '지금은 아니야' |
| 하울 True '대충은 알고 있었다' | 장부 공개 후 첫 고백 | Ch6 에서는 '놀라지 않았다, 이유는 나중에' 로만 처리 |
| 하울 E2/E4 서랍 분기 | together/forced/waiting | CE06 에서 분기별 한 줄. 서랍은 열지 않음(E4 에서 개방) |
| 요한 E4 봉투 | 사흘째 마케팅 담당 | CE07 에서 `npc_marketing` 명함 |
| 요한 True '6장에서 본 그 장부' | 당사자 허락 후 집필 | Ch6_Ledger_Roles 에서 '허락 전에는 한 글자도 안 쓴다' |
| 범준 E4 서 팀장 | 검은 차 | CE07 `npc_chain_rep` '서 팀장' + 범준의 불길한 인사 |
| 범준 True 연필 메모/특약 | 엔딩에서 처음 발견 | Ch6 은 '건물 서류는 내가 챙긴다' 까지만 |
| 하루 E3 '장부 얘기 들었다' | 6장 | Ch6_Ledger_Roles 에 하루가 청자로 등장 |
| 하루 E4 스카우트 | 마들렌을 맛봄 | CE07 `npc_recruiter` 가 시식 |
| 나비 E4 개점식 날 밤 명함 | 음반사 직원 | CE07 에서 명함 수령, 나비 '아직, 나중에' |
| 나비 E5 첫 인사 '나흘 반' | 복귀는 E5 | CE08 에서는 빈 의자만, 복귀는 E5 에 위임, 미재생이면 Ch8_Outro 보충 |
| 곰석 E4 장부 단독 열람 | 이게 뭐예요 | 공통은 `flag("ledger_truth")` 가드: 이미 알면 '말일 봉투' 장면만. 곰석 개인 사정(자기 몫 줄, 두 번째 침묵 이유)은 공개하지 않음 |
| 곰석 E5 '위기 지나간 밤' | CE08 이후 | `chain_crisis_resolved` |
| 하울 엔딩 '체인점은 길 건너에 그대로' | 공존 | CE08: 철수 아님, 쿠폰 공세·매입 철회 |
| 하루 E5 '블루하버 간판 불 꺼짐' | 결판 밤 | CE08 마지막: 간판이 일찍 꺼짐 |
| `johan_e4_column_published` | 칼럼 발행 | Ch7_Outro/Ch8_Intro/CE08/Community 에서 분기 |
| 다자 Consent 의 '직접 확인' | 나비 조건 3 | Harem_Table 에서 나비가 구성원 각자에게 직접 묻는다 |

## 4. 가드레일 점검(다자)
- Harem_Table: 전원 인지·자발(오프닝에서 '한 사람이라도 아니면 접는다'), 연인 강요 없음(우정·동료도 좋다), 비교·숨김 금지 약속, 키워드 6종 재확인, 어느 구성원도 서로의 대체·순서가 아님.
- Ending_Harem2/3: 질투는 말로, 멈출 수 있음, 각자 시간. 접촉 묘사 없음. Community 는 무비난·무연애.

## 5. 호스트/하네스 메모
- `route_locked(id)`/`group_size()` 는 `DialogueSession` 에 존재. 레포 하네스(`scenario_check`)는 둘을 등록하지 않지만 미등록 함수는 거짓으로 평가되어 오류가 나지 않으므로 `route_locked` 가지는 **실행되지 않는다**(코드 미커버). 검증은 스크래치패드 사본(함수 등록 + 잠금 조합 42종 × 전 경로)으로 수행.
- `route_choice` 는 레포 하네스가 아직 모르는 커맨드 → `Ch7_Intro` 가 실패한다(하네스 쪽에서 등록 필요).
- `HAREM_E1` 은 호스트의 CG 키 확장(`^[A-Z][A-Z0-9]*(_[A-Z0-9]+)+$`)이 필요.
