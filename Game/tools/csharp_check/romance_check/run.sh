#!/bin/sh
# 연애 모듈(2단계) 검증: Core+Data+Tycoon+Romance 를 mcs 로 컴파일해 단위 검사와 몬테카를로 패리티 테스트를 실행한다.
# 사용법: tools/csharp_check/romance_check/run.sh [runs_per_focus(기본 300)]
set -e
cd "$(dirname "$0")/../../.."
S=UnityProject/Assets/_Project/Scripts
OUT="${TMPDIR:-/tmp}/morphe_romance_check.exe"
mcs -out:"$OUT" tools/csharp_check/romance_check/Program.cs $S/Core/*.cs $S/Data/*.cs $S/Tycoon/*.cs $S/Romance/*.cs
mono "$OUT" balance "${1:-300}"
