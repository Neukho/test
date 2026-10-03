#!/bin/sh
# 5단계(폴리시) 순수 로직 검증: 오디오 계산·매니페스트·문자열 표/언어·설정 저장·의상 정책·화면 배치 규칙·제스처/뒤로가기·메모리 추정·오디오 커맨드.
# 필요: mono-mcs, mono-runtime, python3(의상 규칙 표 교차 검증). 사용법: sh tools/csharp_check/polish_check/run.sh
set -e
cd "$(dirname "$0")/../../.."
S=UnityProject/Assets/_Project/Scripts
OUT="$(mktemp -d)/morphe_polish_check.exe"
mcs -r:System.dll -out:"$OUT" tools/csharp_check/polish_check/Program.cs \
  $S/Core/*.cs $S/Data/*.cs $S/Tycoon/*.cs $S/Romance/*.cs $S/Dialogue/*.cs $S/ArtPipeline/ArtManifest.cs \
  $S/Game/*.cs $S/Save/SaveData.cs $S/Save/SaveMapper.cs $S/Save/SaveValidator.cs $S/UI/UiText.cs
python3 tools/art_checklist.py --selftest
mono "$OUT" balance
