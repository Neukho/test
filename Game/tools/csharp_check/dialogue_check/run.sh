#!/bin/sh
# Dialogue(MiniYarn) 모듈 검증. 필요: mono-mcs, mono-runtime (apt install mono-mcs mono-runtime)
set -e
cd "$(dirname "$0")/../../.."
S=UnityProject/Assets/_Project/Scripts
D=tools/csharp_check/dialogue_check
OUT="${TMPDIR:-/tmp}/morphe_dialogue_check.exe"
mcs -r:System.dll -out:"$OUT" $D/Program.cs $S/Core/*.cs $S/Data/*.cs $S/Dialogue/*.cs
mono "$OUT" "$D/samples" "UnityProject/Assets/_Project/Resources/Dialogue/address_terms.csv"
