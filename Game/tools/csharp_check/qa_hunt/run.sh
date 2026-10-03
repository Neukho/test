#!/bin/sh
# QA bug-hunt harness (adversarial / property-based). Needs mono-mcs + mono-runtime. Unity not needed.
# Usage: sh tools/csharp_check/qa_hunt/run.sh [mode ...]
#   modes: content save ending flags gain timing fuzz play fatigue   (default: all)
#   env:   QA_ROOT=<dir>   run against a copy of the repo (UnityProject/Assets/_Project/{Scripts,Dialogue,Resources}, balance/) - use a snapshot
#                          while other agents are editing; default = this repo
#          QA_RUNS=n       games per configuration in 'play' (default 2)      QA_SEED=n  base seed
#          QA_FOCUS=1      play: python-policy parity table (QA_FS=0.35,0.5 focus fractions, QA_DETAIL=1)
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
ROOT="${QA_ROOT:-$REPO}"
cd "$ROOT"
S=UnityProject/Assets/_Project/Scripts
OUT="$(mktemp -d)/morphe_qa_hunt.exe"
mcs -r:System.dll -r:System.Xml.dll -out:"$OUT" $HERE/*.cs \
  $S/Core/*.cs $S/Data/*.cs $S/Tycoon/*.cs $S/Romance/*.cs $S/Dialogue/*.cs $S/ArtPipeline/ArtManifest.cs \
  $S/Game/*.cs $S/Save/SaveData.cs $S/Save/SaveMapper.cs $S/Save/SaveValidator.cs $S/Save/SaveIntegrity.cs $S/Save/SaveService.cs
mono "$OUT" balance UnityProject/Assets/_Project/Dialogue UnityProject/Assets/_Project/Resources/Dialogue/address_terms.csv "$@"
