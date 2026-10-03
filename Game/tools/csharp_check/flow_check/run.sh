#!/bin/sh
# 3단계 UI/흐름 순수 로직 검증: 챕터 흐름 · 루트 선택 모델 · 동의 재시도 · 복구 표 · 엔딩 장면 계획 · 프로필/해금/갤러리 · 선택지 라벨 · route_choice 커맨드.
# 필요: mono-mcs, mono-runtime. 사용법: sh tools/csharp_check/flow_check/run.sh
set -e
cd "$(dirname "$0")/../../.."
S=UnityProject/Assets/_Project/Scripts
OUT="$(mktemp -d)/morphe_flow_check.exe"
mcs -r:System.dll -out:"$OUT" tools/csharp_check/flow_check/Program.cs \
  $S/Core/*.cs $S/Data/*.cs $S/Tycoon/*.cs $S/Romance/*.cs $S/Dialogue/*.cs $S/ArtPipeline/ArtManifest.cs \
  $S/Game/*.cs $S/Save/SaveData.cs $S/Save/SaveMapper.cs $S/Save/SaveValidator.cs
mono "$OUT" balance
