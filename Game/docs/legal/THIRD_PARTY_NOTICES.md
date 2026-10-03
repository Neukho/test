# 서드파티 고지 (Third-Party Notices) — 초안

> **초안 — 법률 자문이 아닙니다. 게시 전 검토하세요. (Draft — not legal advice, have it reviewed.)**
> 정확한 버전·전문(全文)은 **빌드 시점에 확인해 채웁니다**(`[TODO]`). 기계가 읽는 목록은 `docs/legal/SBOM.cdx.json`(`python3 tools/generate_sbom.py`). 라이선스 이름은 알려진 범위에서만 적었고, 확실하지 않은 것은 "확인 필요"로 남겼습니다.

## 1. 게임에 포함되는 구성 요소

| 구성 요소 | 용도 | 버전 | 라이선스 | 비고 |
|---|---|---|---|---|
| Unity Engine (Unity 6) | 게임 엔진·플레이어 런타임 | [TODO: ProjectSettings/ProjectVersion.txt] | Unity 소프트웨어 약관(요금제에 따라 다름 — 확인 필요) | 요금제별 조건(스플래시 로고 등)은 Unity 공식 약관 페이지에서 확인 |
| com.unity.ugui (uGUI) + TextMesh Pro | UI, 텍스트 | 2.0.0 계열(Unity 6) — [TODO: 에디터가 해석한 정확한 버전] | Unity Companion License | Unity 6 에서 TMP 는 ugui 패키지에 통합 |
| com.unity.inputsystem (Input System) | 입력 | 1.x — [TODO: manifest.json 에 고정한 버전] | Unity Companion License | |
| 한글 폰트 (선택 예정): Noto Sans KR | 본문 폰트 | [TODO: 내려받은 파일의 버전/날짜] | SIL Open Font License 1.1 (OFL-1.1) | **아직 번들되지 않음.** 번들 시 폰트 파일과 같은 폴더에 OFL 전문 및 저작권 문구 포함. "Reserved Font Name" 조건에 따라 폰트를 수정해 재배포하면 이름 변경 필요(서브셋 생성 시 확인) |
| Yarn Spinner | — | **사용하지 않음** | — | 이 프로젝트는 자체 구현 대화 엔진(MiniYarn)을 사용 (`docs/07_Dialogue_Spec.md`) |

Unity Companion License / Unity 약관의 전문은 각 패키지의 `LICENSE.md`(Package Manager 설치 폴더) 및 Unity 공식 페이지에서 확인해 아래 §4 에 붙입니다. [TODO]

## 2. 개발 도구 (배포물에 포함되지 않음)
Mono(mcs), .NET SDK, NUnit 3.14.0(MIT), System.Memory 4.5.5(MIT), Python 3 — 검증 하네스용. 배포물에 들어가지 않으므로 고지 의무 대상이 아니지만 SBOM 에는 `scope: excluded` 로 기록.

## 3. 에셋 (별도 로그에서 관리)
- 일러스트(사용자 직접 제작): `docs/legal/art_log.csv`
- 음악·효과음: `docs/legal/music_sfx_log.csv` — 사용하는 곡마다 라이선스·출처·표기 의무를 기록하고, 표기가 필요한 것은 아래 §5 크레딧에 옮김
- 폰트: `docs/legal/font_log.csv`

## 4. 라이선스 전문
[TODO: 번들 폰트의 OFL-1.1 전문, Unity Companion License 전문(또는 링크), 사용하는 음악/효과음의 표기 문구를 여기에 붙인다. 게임 내 "크레딧/라이선스" 화면 또는 배포물의 `THIRD_PARTY_NOTICES.txt` 로도 동봉할 것.]

## 5. 게임 내 크레딧에 넣을 문구 (초안)
```
Made with Unity.
Font: Noto Sans KR — SIL Open Font License 1.1   [TODO: 폰트 확정 후]
Music/SFX: [TODO: 곡명 — 작곡가 — 라이선스 — URL]
```
