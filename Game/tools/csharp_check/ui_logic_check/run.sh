#!/bin/sh
# 2단계 UI 로직(순수 C#) 검증: 프로필 검증 · 저장 확장 · VN 상태/타자기 · 대화 커맨드 디스패치 · 밤 행동 컨트롤러 · 설정 · 실제 시나리오 전수 실행.
# 필요: mono-mcs, mono-runtime. 사용법: tools/csharp_check/ui_logic_check/run.sh
set -e
cd "$(dirname "$0")/../../.."
S=UnityProject/Assets/_Project/Scripts
OUT="${TMPDIR:-/tmp}/morphe_ui_logic_check.exe"
mcs -r:System.dll -out:"$OUT" tools/csharp_check/ui_logic_check/Program.cs \
  $S/Core/*.cs $S/Data/*.cs $S/Tycoon/*.cs $S/Romance/*.cs $S/Dialogue/*.cs $S/ArtPipeline/ArtManifest.cs \
  $S/Game/*.cs $S/Save/SaveData.cs $S/Save/SaveMapper.cs $S/Save/SaveValidator.cs
mono "$OUT" balance UnityProject/Assets/_Project/Resources/Dialogue/address_terms.csv UnityProject/Assets/_Project/Dialogue
