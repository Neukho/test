# 시나리오 작가 공통 브리프 (병렬 작업용)

대상: 한국어 수인 카페 타이쿤 + 연애 비주얼 노벨. 전연령~암시 수위(노골적 성적 묘사 없음), 모든 캐릭터는 성인.
필독: `docs/00_GDD.md`(§1 세계관·다양성 규칙, §2 캐릭터·이벤트 5단계, §3 챕터), `docs/10_Route_Structure.md`(루트 구조·다자 가드레일·이름 규칙), `docs/07_Dialogue_Spec.md`(MiniYarn 문법·명령), `docs/08_Scenario_Bible_Howl.md` + `UnityProject/Assets/_Project/Dialogue/routes/howl/*.yarn`(품질·형식 기준 샘플), `Resources/Dialogue/address_terms.csv`, `balance/prefs.csv|gifts.csv|spots.csv|events.csv|actions.csv`.

## 작성 범위(캐릭터당)
저장 위치: `UnityProject/Assets/_Project/Dialogue/routes/<char>/` (엔트리: `entries.txt`에 노드명 한 줄씩)
1. 대화 `<Char>_Talk_01..06` (호감 구간 3단계 분기, 노드당 선택 지점 2~3개, 보상은 정답 `<<aff N>>`=+5/`<<trust>>`=+1.5, 오답 -1/-1)
2. 선물 반응 `_Gift_Like/_Neutral/_Dislike`(`$gift_name` 사용, 수치 변경 없음)
3. 데이트 `<Char>_Date_<PascalSpot>` 2곳(그 캐릭터의 호감 스팟, `balance/prefs.csv` 참조) + `<Char>_Date_Generic`
4. 개인 이벤트 `<Char>_E1..E5`: GDD §2 의 비트를 따른다. 각 40~70줄, 의미 있는 선택 1~2회(플래그 설정), 마지막에 `<<end_event <char> N>>`, CG 연출(`<<show_cg CHAR_E1>>`/`<<hide_cg>>`), `<<show_standing ...>>` 사용. E5 는 굿 엔딩 CG 와 겸용, 트루 열쇠 플래그 `<char>_key` 를 획득하는 선택지(숨은 조건)를 포함.
5. 엔딩 `<Char>_End_Normal/_Good/_True` 각 35~60줄(`_True` 는 `<char>_key` 와 `ledger_truth` 플래그 전제, CG `<CHAR>_E6`). 호스트가 8장 종료 시 시작하는 진입 노드.
6. 다자 조각: `<Char>_Harem_Consent`(25~35줄, 이 인물과 ‘함께 사랑해도 되는지’ 서로 동의를 확인, 끝에 `<<flag harem_ok_<char>>>`)와 `<Char>_Harem_Epilogue`(8~12줄). 가드레일은 `docs/10_Route_Structure.md §3` 엄수.
7. 문서: `docs/08_Scenario_Bible_<Char>.md`(목소리 시트·플래그표·노드 인덱스·성별 변형 메모·톤 체크리스트), `docs/art_requests/<Char>.md`(사용자가 직접 일러스트를 그리므로 **프롬프트가 아니라 제작 요청서**: 각 CG ID·파일명 `CG_<Char>_E<N>.png`·구도·조명·감정·등장인물/소품/배경·대사 위치·표정 키, 그리고 의상/표정 사용 현황)

## 작법 규칙
- 목소리가 선명하게 구분될 것(GDD §2 의 5줄 샘플의 말투 유지). 종결어미·문장 길이·감탄사 패턴을 지킨다.
- 주인공 대사는 성별 중립: `{name()}`, `{calls_player("id")}`, `{player_calls("id")}` 와 대명사 함수만 사용. 하드코딩된 성별어(그/그녀/남자/여자/오빠/언니/형/누나 등) 금지(함수 호출 안은 예외).
- 클리셰·고정관념 금지(종 고정관념을 결론으로 삼기, 정체성 희화화, 집착·소유욕 미화, 억지 ‘츤데레’). 로맨스는 동의 중심, 수위는 키스·포옹·페이드 아웃까지.
- 모든 대사줄에 고유 `#line:` id (`<char>_<노드약어>_NNN`; 기존 도구 `tools/csharp_check/scenario_check/number_lines.py` 사용 가능). 화자 id: 소문자 캐릭터 id, 주인공 `player`, 조연 `npc_*`.
- 챕터 맞춤: 이벤트가 발생하는 챕터는 GDD §2 를 따른다(`balance/events.csv` 의 최소 챕터/호감/신뢰 조건과 모순 금지).
- 공통 챕터 본문(2~8장 메인 스토리)은 별도 작업이다. 필요한 경우에만 플래그로 연결하고 임시로 `<<flag ledger_truth>>` 같은 공유 플래그를 직접 세팅해도 된다(문서에 의존성으로 표기).
- 다른 캐릭터를 언급할 때 그 캐릭터의 말투·성격을 해치지 않는다.

## QA(반드시 실행)
`SCENARIO_ONLY=routes/<char> sh tools/csharp_check/scenario_check/run.sh` — 린트 0건(중복 id/줄 id 누락/미정의 점프/미도달 노드 포함), 4성별×2로케일×호감 구간×선택지 전 경로 실행 오류 0, 성별 중립 검사 통과, 분량표 확인. (다른 작가의 작업 중 파일은 환경변수로 제외되므로 영향이 없다.) 실패하면 스크립트를 고친다.

## 금지/소유
- 수정 가능: `Dialogue/routes/<char>/**`, `docs/08_Scenario_Bible_<Char>.md`, `docs/art_requests/<Char>.md`. 다른 폴더·`Scripts/**`·`balance/**`·다른 작가의 파일은 수정하지 않는다. 모듈 버그를 발견하면 수정하지 말고 보고한다.
- 최종 보고(35줄 이내): 만든 파일, 노드/줄 수, 실제 실행한 QA 결과, 발견한 모순·의존성·모듈 버그.
