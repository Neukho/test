#!/bin/sh
# 장면 게이트(SceneGate) + 인물별 이벤트 일정(event_schedule.csv) 검증: 규칙 표 · 게이트 판정 · 자동 재생 스케줄러 · RomanceService 연동 · 64일 봇 플레이.
# 필요: mono-mcs, mono-runtime. 사용법: sh tools/csharp_check/gate_check/run.sh [parity_runs(기본 150)]
set -e
cd "$(dirname "$0")/../../.."
S=UnityProject/Assets/_Project/Scripts
OUT="$(mktemp -d)/morphe_gate_check.exe"
mcs -r:System.dll -out:"$OUT" tools/csharp_check/gate_check/Program.cs \
  $S/Core/*.cs $S/Data/*.cs $S/Tycoon/*.cs $S/Romance/*.cs $S/Dialogue/*.cs $S/ArtPipeline/ArtManifest.cs \
  $S/Game/*.cs $S/Save/SaveData.cs $S/Save/SaveMapper.cs $S/Save/SaveValidator.cs
mono "$OUT" balance UnityProject/Assets/_Project/Resources/Balance "${1:-150}"
