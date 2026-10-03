#!/bin/sh
# 수정 묶음(데모 모드 · 아트 캐시 · 문자열 현지화 · 터치 목표 · 저장 최신성) 순수 로직 검증. 필요: mono-mcs, mono-runtime.
# 사용법: sh tools/csharp_check/fixes_check/run.sh
set -e
cd "$(dirname "$0")/../../.."
S=UnityProject/Assets/_Project/Scripts
OUT="$(mktemp -d)/morphe_fixes_check.exe"
mcs -r:System.dll -out:"$OUT" tools/csharp_check/fixes_check/Program.cs \
  $S/Core/*.cs $S/Data/*.cs $S/Tycoon/*.cs $S/Romance/*.cs $S/Dialogue/*.cs $S/ArtPipeline/ArtManifest.cs \
  $S/Game/*.cs $S/Save/SaveData.cs $S/Save/SaveMapper.cs $S/Save/SaveValidator.cs $S/UI/UiText.cs
mono "$OUT"
