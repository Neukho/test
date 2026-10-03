#!/bin/sh
# 엔딩·공동 루트 로직(3단계) 검증: 그룹 확정 · EndingEvaluator 전 분기/경계값 · 저장 호환 · 밤 행동/엔딩 계획(Game 어셈블리).
# 필요: mono-mcs, mono-runtime. 사용법: sh tools/csharp_check/ending_check/run.sh
set -e
cd "$(dirname "$0")/../../.."
S=UnityProject/Assets/_Project/Scripts
OUT="$(mktemp -d)/morphe_ending_check.exe"   # 병렬 실행 충돌 방지
mcs -r:System.dll -r:System.Xml.dll -out:"$OUT" tools/csharp_check/ending_check/Program.cs \
  $S/Core/*.cs $S/Data/*.cs $S/Tycoon/*.cs $S/Romance/*.cs $S/Dialogue/*.cs $S/ArtPipeline/ArtManifest.cs \
  $S/Game/*.cs $S/Save/SaveData.cs $S/Save/SaveMapper.cs $S/Save/SaveValidator.cs $S/Save/SaveIntegrity.cs $S/Save/SaveService.cs
mono "$OUT" balance
