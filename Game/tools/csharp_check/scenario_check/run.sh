#!/bin/sh
# 시나리오(.yarn) QA 하네스. 필요: mono-mcs, mono-runtime. Unity 불필요.
# 사용: sh tools/csharp_check/scenario_check/run.sh
set -e
cd "$(dirname "$0")/../../.."
S=UnityProject/Assets/_Project/Scripts
D=tools/csharp_check/scenario_check
OUT="$(mktemp -d)/morphe_scenario_check.exe"   # 병렬 실행 충돌 방지
mcs -r:System.dll -out:"$OUT" $D/Program.cs $S/Core/*.cs $S/Data/*.cs $S/Dialogue/*.cs
mono "$OUT" "UnityProject/Assets/_Project/Dialogue" "UnityProject/Assets/_Project/Resources/Dialogue/address_terms.csv"
