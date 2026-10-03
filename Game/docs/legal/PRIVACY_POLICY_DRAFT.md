# 개인정보 처리방침 / Privacy Policy — 초안 (DRAFT)

> **초안 — 법률 자문이 아닙니다. 게시 전 반드시 전문가(변호사 등)의 검토를 받으세요.**
> **Draft — not legal advice, have it reviewed.** 이 문서는 2026-10-03 기준 게임 코드의 실제 동작(무수집 설계, `docs/01_TechDesign.md` §11)을 설명하도록 작성되었습니다. 기능이 바뀌면(클라우드 저장, 분석, 광고, 온라인 기능 추가 등) 이 문서도 반드시 고쳐야 합니다.
> 대괄호 `[TODO ...]` 는 게시 전에 채워야 하는 값입니다.

---

## 한국어

**서비스:** 달빛 모퉁이의 수인들 (Moon Corner House) — 이하 "본 게임"
**제공자:** [TODO: 개인/사업자 이름]
**시행일:** [TODO: YYYY-MM-DD]　**버전:** [TODO]
**문의:** [TODO: 이메일 주소]

### 1. 수집하는 개인정보
본 게임은 **개인정보를 수집·전송하지 않습니다.** 계정, 로그인, 분석(애널리틱스), 광고, 충돌 보고 전송 기능이 없습니다. 개발자는 이용자의 이름·연락처·기기 식별자·위치·IP 주소를 받지 않습니다.

### 2. 기기에 저장되는 정보 (외부로 전송되지 않음)
게임 진행을 위해 다음 파일이 **이용자 기기의 로컬 저장소**(Unity `Application.persistentDataPath`)에만 저장됩니다.

| 파일 | 내용 | 비고 |
|---|---|---|
| `saves/slot*.json`, `.bak` | 진행 상황, 주인공이 입력한 이름·호칭·성별 선택, 플레이 시간 | 무결성 검사용 서명 포함(변조 방지가 아니라 손상 탐지 목적) |
| `profile.json` | 도달한 엔딩·본 CG 기록 | |
| `settings.json` | 음량·텍스트 속도 등 환경 설정 | 설정 기능이 개발 중 — 최종 필드 확인 [TODO] |

주인공 이름 등은 이용자가 직접 입력하는 값이며 게임 안에서만 사용됩니다. 이 파일들은 이용자가 직접 삭제할 수 있으며, **게임을 삭제하면 함께 삭제**됩니다(Windows: `%USERPROFILE%\AppData\LocalLow\<회사명>\<제품명>\`, Android: 앱 데이터 삭제 또는 앱 제거). [TODO: 실제 회사명/제품명 경로 확인]

### 3. 제3자 제공·국외 이전
수집하는 정보가 없으므로 제3자에게 제공하거나 국외로 이전하는 개인정보가 없습니다.

### 4. 배포 플랫폼의 처리
게임을 내려받거나 구매하는 과정에서 **각 배포 플랫폼(itch.io, Steam, Google Play 등)이 자체 정책에 따라** 구매·계정·기기 정보를 처리할 수 있습니다. 이는 해당 플랫폼의 개인정보 처리방침이 적용되며 본 게임 제공자는 그 정보를 받지 않습니다. [TODO: 실제 배포한 플랫폼만 남기고, 각 플랫폼의 현재 방침 링크를 기재]
Steam 클라우드 저장 등 플랫폼 기능을 켜면 저장 파일이 해당 플랫폼으로 동기화될 수 있습니다 — **현재 버전은 해당 기능을 사용하지 않습니다.** [TODO: 출시 구성에 맞게 확인]

### 5. 권한 (Android)
인터넷 권한을 요청하지 않습니다. [TODO: 최종 AndroidManifest/빌드 결과에서 실제 권한 목록 확인 — `aapt dump permissions` 등]

### 6. 아동
본 게임은 성인 취향의 로맨스 요소(암시적 표현, 노골적 묘사 없음)를 포함하며 **[TODO: 대상 연령(예: 만 18세 이상 권장)]** 을 대상으로 합니다. 연령 등급은 각 스토어의 등급 설문 결과를 따릅니다.

### 7. 이용자의 권리
수집된 개인정보가 없으므로 열람·정정·삭제 요청의 대상이 되는 정보가 개발자에게 없습니다. 기기 안의 파일은 이용자가 직접 관리·삭제할 수 있습니다. 그래도 문의가 있으면 위 연락처로 보내 주세요.

### 8. 변경
기능 변경으로 처리 내용이 달라지면 이 방침을 개정하고 시행일을 갱신합니다.

---

## English

**Product:** Moon Corner House (달빛 모퉁이의 수인들) — "the Game"
**Publisher:** [TODO: name]　**Effective date:** [TODO]　**Contact:** [TODO: email]

1. **Data we collect: none.** The Game has no accounts, analytics, advertising, telemetry or crash-report upload. The publisher does not receive your name, contact details, device identifiers, location or IP address.
2. **Data stored on your device only.** Save files (progress, the protagonist name/address terms/gender option you type in, play time), a profile file (endings reached, CGs seen) and settings are written to the app's local data folder and are never sent anywhere. Uninstalling the Game removes them (on Windows you may also delete the folder under `AppData\LocalLow`). [TODO: confirm paths]
3. **Third parties.** None receive data from the Game. Storefronts (itch.io, Steam, Google Play) process purchase/account/device data under their own policies. [TODO: keep only the stores you ship on and link each current policy]
4. **Permissions (Android).** The Game does not request the INTERNET permission. [TODO: verify in the final build]
5. **Audience.** [TODO: intended age group]. The Game contains romance themes with suggestive but non-explicit content.
6. **Your rights.** Because no personal data reaches the publisher, there is nothing for us to access, correct or delete; local files are under your control.
7. **Changes.** If features change (cloud sync, analytics, online features) this policy will be updated first.

---

### 게시 전 체크 (작성자용, 게시하지 말 것)
- [ ] 코드에 네트워크 호출·분석·광고 SDK 가 없음을 확인 (`grep -rn "UnityWebRequest\|HttpClient\|Analytics\|Advertisement" UnityProject/Assets` 결과 0건 — 서드파티 패키지를 추가했다면 SBOM 도 갱신)
- [ ] 최종 빌드의 Android 권한 목록 확인(INTERNET 없음), Windows 빌드에 별도 네트워크 동작 없음
- [ ] 세이브(`saves/`)·`profile.json`·`settings.json` 경로와 내용을 실제 빌드에서 확인해 §2 표 갱신
- [ ] 게시할 URL 확보(Google Play 데이터 보안 양식과 스토어 페이지에 개인정보처리방침 URL 필요 — `docs/18_Release_Guide.md`)
- [ ] 법률 검토 완료 날짜: [TODO]
