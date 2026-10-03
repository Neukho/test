# AI · 권리 · 상표 점검표 — 초안

> **초안 — 법률 자문이 아닙니다. 검토를 받으세요. (Draft — not legal advice, have it reviewed.)**
> 이 문서는 "무엇을 기록하고 무엇을 확인할지"의 점검표입니다. 어떤 약관·정책도 여기서 결론을 내리지 않습니다 — **약관과 정책은 자주 바뀌므로 확인한 날짜와 URL 을 직접 기록**하세요. 근거 문서: `docs/02_Art_Plan.md` §8, `docs/01_TechDesign.md` §11, `docs/18_Release_Guide.md`.

## 1. 일러스트 출처 로그 — `docs/legal/art_log.csv`

일러스트는 사용자가 직접 제작한다는 전제입니다. 직접 그림이든 AI 보조든 **같은 로그에 한 장당 한 줄**을 남깁니다(스토어 AI 공개 설문에 truthfully 답하기 위한 근거 자료).

| 열 | 뜻 |
|---|---|
| `asset_id` | 최종 파일명(예: `CG_Howl_E1`, `Std_Howl_Smile`) — `docs/13_Art_Checklist.md` 와 동일 |
| `date_utc` | 완성/생성 일시(UTC) |
| `creator` | 제작자(본인 또는 외주자 이름) — 외주면 계약서 위치도 notes 대신 `license_or_rights_basis` 에 |
| `tool_and_version` | 사용 소프트웨어(그림 툴, AI 도구 등)와 버전 |
| `model_or_brush_or_source` | AI 사용 시 체크포인트/LoRA 이름·해시·출처. 직접 그림이면 `hand-drawn` 등 |
| `license_or_rights_basis` | 권리 근거(본인 창작 / 외주 계약 / 도구·모델의 상업 이용 조건과 확인 날짜·URL) |
| `prompt`, `negative`, `seed`, `params` | AI 사용 시에만 |
| `refs` | 참고한 자료(본인 제작물 ID만 권장) |
| `postproc` | 후처리(보정·합성·업스케일) 내역 |
| `ai_assisted` | `yes` / `no` — **러프·선화·채색·배경·업스케일·보정 중 어느 단계든 생성형 AI 를 썼다면 yes** (세부 단계는 postproc/notes 에 서술) |
| `reviewer_date` | 점검자와 날짜 |

점검:
- [ ] 출시 대상 에셋 전부가 로그에 있다 (`docs/13_Art_Checklist.md` 필수 항목 대비)
- [ ] `ai_assisted` 합계로 스토어 AI 공개 설문 답변을 정했다(`docs/18` §3.3 의 두 초안 중 해당 쪽)
- [ ] AI 를 쓴 경우: 도구의 상업 이용 조건과 모델/LoRA 라이선스를 **확인한 날짜·URL** 기록, 학습 데이터에 타인 저작물(실존 작가 작품)을 쓰지 않음(`docs/02` §8.1)
- [ ] 실존 작가 이름·화풍을 프롬프트/요청에 직접 지정하지 않음, 기존 캐릭터·실존 인물을 연상시키지 않음(`docs/02` §8.2)
- [ ] 모든 캐릭터는 성인 외형(`docs/02` §8.2, `docs/04` §4)
- [ ] 플레이스홀더(코드 생성 실루엣)가 출시 빌드에 0장 — `MorpheHouse/Build/Preflight Check` 의 "Art placeholders" 가 PASS

## 2. 음악·효과음 로그 — `docs/legal/music_sfx_log.csv`
- [ ] 곡/효과음 **파일마다 한 줄**: 출처 URL, 라이선스 이름·URL, 상업 이용 가능 여부, 표기 의무와 문구, 확인한 날짜
- [ ] `commercial_use_ok` 나 `attribution_required` 가 `unknown` 인 줄이 없다
- [ ] 표기 의무가 있는 항목은 게임 내 크레딧 / `THIRD_PARTY_NOTICES` 에 문구를 그대로 옮겼다
- [ ] 직접 작곡·녹음한 것도 한 줄(출처=본인)로 기록
- [ ] AI 음악/음성 생성 도구를 쓴다면 §1 과 같은 방식으로 도구·약관 확인 날짜를 남기고, 스토어 공개 설문이 오디오도 묻는지 확인(`docs/18`)
- [ ] 콘텐츠 ID(유튜브 등)에 등록된 곡이 아닌지 — 방송/영상 홍보 시 분쟁 방지

## 3. 폰트 로그 — `docs/legal/font_log.csv`
- [ ] 폰트별: 라이선스(예: Noto Sans KR = SIL OFL 1.1 — 확인 후 기록), **앱 임베딩 허용 여부**, 수정/서브셋 허용 여부, Reserved Font Name 조건
- [ ] 게임에 번들하는 폰트 파일 전부가 로그에 있다 (OS 폰트 동적 생성 폴백은 번들이 아니므로 대상 아님 — 단, 폴백에 의존한 채 출시하지 말 것: `FontProvider`)
- [ ] 라이선스 전문 파일을 폰트와 함께 배포물에 동봉
- [ ] 한글 완성형 + 게임에 쓰는 특수문자(★ 등) 글리프 포함 확인 (`docs/12` §6 위험 8)

## 4. 코드·패키지
- [ ] `python3 tools/generate_sbom.py` 로 SBOM 재생성, `unspecified` 버전을 실제 값으로 채움
- [ ] 에셋 스토어 에셋을 쓰면 해당 라이선스(재배포 가능 범위) 확인 후 SBOM/NOTICES 에 추가
- [ ] 소스 공개 시: 포함된 서드파티 소스의 라이선스 호환 여부

## 5. 제목 · 상표 · 이름 충돌 점검 — '달빛 모퉁이의 수인들' / 'Moon Corner House'

> 목적은 **분쟁 위험을 일찍 발견**하는 것이지, 등록 가능성이나 안전을 보증하는 것이 아닙니다. 결과는 날짜와 함께 기록하고, 사업화 규모가 크다면 변리사 상담을 권합니다.

기록 양식: `검색처 / 검색어 / 검색일 / 결과 요약 / 스크린샷 또는 URL / 판단`

- [ ] **한국**: 특허정보검색서비스 KIPRIS(kipris.or.kr) 상표 검색 — 검색어: `달빛 모퉁이`, `달빛모퉁이`, `달빛 모퉁이의 수인들`, `Moon Corner House`, 유사 표기·발음. 분류(게임/소프트웨어 등 해당 류) 확인 [TODO: 해당 류 번호는 변리사/KIPO 안내로 확인]
- [ ] **해외 시장 가능성**: WIPO Global Brand Database, EUIPO TMview, 미국 USPTO 상표 검색 — 검색어 `Moon Corner House`, `Moon Corner`, `MoonCornerHouse`
- [ ] **스토어·플랫폼 중복**: Steam, itch.io, Google Play, App Store 에서 같은 이름/유사 이름의 게임·앱 검색
- [ ] **일반 웹/SNS 검색**: 검색 엔진 상위 결과, X(트위터)·Bluesky·Discord·YouTube·pixiv 등 핸들 사용 여부
- [ ] **도메인/핸들 확보**: [TODO: 선택한 도메인], 소셜 계정 이름
- [ ] **지시적 단어 점검**: '달빛', '모퉁이', '수인', 'Moon', 'Corner', 'House' 는 흔한 단어일 가능성이 높아 단독으로 독점되기 어렵고, 조합이 유사한 선등록이 있는지가 관건 — 결과를 보고 판단(결론은 전문가에게)
- [ ] **캐릭터 이름**: 하울, 요한, 범준, 하루, 곰석, 나비 + 가게 이름 '모피 하우스'/'달빛 컴퍼니' 도 같은 방식으로 간단 검색(특히 '하울'은 유명 작품 캐릭터명과 겹칠 수 있음 — 의도치 않은 연상 여부 확인) [TODO: 판단]
- [ ] **영문 부제/현지화 제목**: 번역 제목이 확정되면 §5 를 다시 수행
- [ ] **상표 출원 여부 결정**: 출원 / 보류 — 결정일 [TODO]. 출원 시 출원번호를 이 문서에 기록
- [ ] **로고·아이콘**: 로고에 쓴 폰트·이미지의 권리(§3), 유사 로고 검색

## 6. 출시 전 최종 서명 (본인 확인)

| 항목 | 확인자 | 날짜 |
|---|---|---|
| art_log 완결 | | |
| music_sfx_log 완결, unknown 없음 | | |
| font_log 완결, 라이선스 동봉 | | |
| SBOM 갱신, THIRD_PARTY_NOTICES TODO 0건 | | |
| 상표/이름 점검 기록 | | |
| 개인정보처리방침 법률 검토 | | |
| 스토어 AI 공개 답변이 art_log 와 일치 | | |
