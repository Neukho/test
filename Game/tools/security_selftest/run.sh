#!/bin/sh
# 보안 자가 테스트(퍼징). 필요: mono-mcs, mono-runtime. 사용법: sh tools/security_selftest/run.sh
# 고정 시드 → 결정적 입력. 종료 코드 0=모든 단언 통과, 1=단언 실패(발견 사항), 2=하네스 오류.
# 단언 실패는 "대상 모듈에서 실제로 발견된 문제"를 뜻한다(자세한 해석은 portfolio/SELFTEST_RESULTS.md).
cd "$(dirname "$0")/../.."
S=UnityProject/Assets/_Project/Scripts
OUT="$(mktemp -d)/morphe_security_selftest.exe"   # 병렬 실행 충돌 방지
mcs -r:System.dll -r:System.Xml.dll -out:"$OUT" tools/security_selftest/Program.cs \
  $S/Core/*.cs $S/Data/*.cs $S/Tycoon/*.cs $S/Romance/*.cs $S/Dialogue/*.cs \
  $S/ArtPipeline/ArtManifest.cs $S/Game/*.cs \
  $S/Save/SaveData.cs $S/Save/SaveMapper.cs $S/Save/SaveValidator.cs $S/Save/SaveIntegrity.cs $S/Save/SaveService.cs || exit 2
mono "$OUT" "UnityProject/Assets/_Project/Dialogue"
