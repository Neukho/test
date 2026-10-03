#!/bin/sh
# Unity 없이 순수 로직을 컴파일·실행한다. 필요: mono-mcs (apt install mono-mcs mono-runtime)
set -e
cd "$(dirname "$0")/../.."
S=UnityProject/Assets/_Project/Scripts
mcs -r:System.Xml.dll -out:/tmp/morphe_check.exe tools/csharp_check/Program.cs $S/Core/*.cs $S/Data/*.cs $S/Tycoon/*.cs $S/Save/SaveData.cs $S/Save/SaveMapper.cs $S/Save/SaveValidator.cs $S/Save/SaveIntegrity.cs $S/Save/SaveService.cs
mono /tmp/morphe_check.exe balance
