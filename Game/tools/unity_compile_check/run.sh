#!/bin/sh
# Unity 없이 "실제 Unity API 정의"를 상대로 프로젝트 런타임 코드 전체를 컴파일하는 검증 스크립트.
#  - UnityEngine: NuGet 'UnityEngine.Modules'(2021.3.33 참조 어셈블리)
#  - uGUI/TMP: Unity-Technologies/uGUI 6000.0 브랜치 소스를 컴파일
#  - Input System: Unity-Technologies/InputSystem 소스를 컴파일
#  필요: dotnet-sdk-8.0, git, curl, unzip, python3, 인터넷(nuget.org, github.com)
# 한계: Unity 6 가 아닌 2021.3 참조라 Unity 6 전용 API 는 일부 보정(shim)했고, Editor 스크립트(UnityEditor)는 포함하지 않으며,
#       실제 실행(렌더링·입력)은 검증하지 못한다. "컴파일 가능성"만 보증한다.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
# 작업 폴더: WORK 가 없으면 매번 새 비공개 임시 폴더(예측 가능한 /tmp 경로를 재사용하지 않는다 — 다른 사용자가 미리 만든 폴더를 신뢰하지 않기 위함).
# WORK 를 지정해 캐시를 재사용하는 경우에도 아래에서 고정된 해시/커밋과 일치하는지 매번 검증한다.
W="${WORK:-$(mktemp -d "${TMPDIR:-/tmp}/unitycheck.XXXXXX")}"
mkdir -p "$W" && cd "$W"

# ---- 공급망 고정(관찰한 값, 2026-10-03): 바꾸려면 새 값을 직접 확인하고 갱신한다 ----
UM_VERSION=2021.3.33
UM_SHA256=d32d34526d89958c63220d3a5ce26c5c44c11bfd411a2e1fbc3200088ef160c4        # unityengine.modules.2021.3.33.nupkg
UGUI_SHA=08a19d2cb9de6bfaefbd2e301ac9e5518a78fc6d                                  # Unity-Technologies/uGUI, 브랜치 6000.0 에서 관찰한 커밋
ISYS_SHA=bed688c784147da5c0694c97fe4f0080d0810375                                  # Unity-Technologies/InputSystem 기본 브랜치에서 관찰한 커밋

if [ ! -d um ]; then
  curl -sS -o um.nupkg "https://api.nuget.org/v3-flatcontainer/unityengine.modules/$UM_VERSION/unityengine.modules.$UM_VERSION.nupkg"
  echo "$UM_SHA256  um.nupkg" | sha256sum -c - >/dev/null || { echo "ERROR: um.nupkg sha256 mismatch (expected $UM_SHA256)"; rm -rf um um.nupkg; exit 1; }
  unzip -q -o um.nupkg -d um
else
  echo "$UM_SHA256  um.nupkg" | sha256sum -c - >/dev/null || { echo "ERROR: cached um.nupkg sha256 mismatch"; exit 1; }
fi

# 고정 커밋만 가져온다(shallow fetch by SHA). 캐시된 폴더는 HEAD 가 고정 SHA 인지 확인한다.
pin_clone() { # dir url sha
  if [ -d "$1/.git" ]; then
    [ "$(git -C "$1" rev-parse HEAD)" = "$3" ] || { echo "ERROR: $1 is not at pinned commit $3"; exit 1; }
    return
  fi
  git init -q "$1" && git -C "$1" remote add origin "$2" \
    && git -C "$1" fetch -q --depth 1 origin "$3" && git -C "$1" checkout -q --detach FETCH_HEAD \
    && [ "$(git -C "$1" rev-parse HEAD)" = "$3" ] || { echo "ERROR: could not fetch pinned commit $3 from $2"; exit 1; }
}
pin_clone ugui6 https://github.com/Unity-Technologies/uGUI.git "$UGUI_SHA"
pin_clone inputsys https://github.com/Unity-Technologies/InputSystem.git "$ISYS_SHA"

# ---- 소스 복사 + 호환 패치(2021.3 참조 어셈블리에 없는 Unity 6/.NET 신규 API 보정) ----
if [ ! -d src_ready ]; then
  mkdir src_ready
  cp -r ugui6/com.unity.ugui/Runtime src_ready/ugui
  cp -r inputsys/Packages/com.unity.inputsystem src_ready/isys
  python3 "$HERE/patch_sources.py" "$W/src_ready"
fi

cp "$HERE/shims/"*.cs "$W/"

# 프로젝트 파일 생성: proj <이름> <어셈블리> <추가 참조> <정의 심볼> <컴파일 항목>
proj() {
  mkdir -p "$W/$1"
  sed -e "s|@ASM@|$2|; s|@DEFINES@|$4|; s|@UM@|$W/um/lib/net45/*.dll|" "$HERE/template.csproj" \
    | python3 -c "import sys; t=sys.stdin.read(); t=t.replace('@REFS@', sys.argv[1]).replace('@COMPILE@', sys.argv[2]); print(t)" "$3" "$5" \
    > "$W/$1/$1.csproj"
}

R_UI="<Reference Include=\"$W/ugui/bin/Debug/net48/UnityEngine.UI.dll\" />"
R_TMP="<Reference Include=\"$W/tmp/bin/Debug/net48/Unity.TextMeshPro.dll\" />"
R_IS="<Reference Include=\"$W/isys/bin/Debug/net48/Unity.InputSystem.dll\" />"
S="$ROOT/UnityProject/Assets/_Project"

proj ugui UnityEngine.UI "" "UNITY_2021_3_OR_NEWER" "<Compile Include=\"$W/src_ready/ugui/UGUI/**/*.cs\" /><Compile Include=\"$W/shim_ugui.cs\" />"
proj tmp Unity.TextMeshPro "$R_UI" "UNITY_2021_3_OR_NEWER" "<Compile Include=\"$W/src_ready/ugui/TMP/**/*.cs\" /><Compile Include=\"$W/shim_tmp.cs\" />"
proj isys Unity.InputSystem "$R_UI<PackageReference Include=\"System.Memory\" Version=\"4.5.5\" />" "UNITY_INPUT_SYSTEM_ENABLE_UI;PACKAGE_UGUI;UNITY_2021_3_OR_NEWER;UNITY_STANDALONE_WIN;UNITY_STANDALONE" "<Compile Include=\"$W/src_ready/isys/InputSystem/**/*.cs\" Exclude=\"$W/src_ready/isys/InputSystem/Plugins/InputForUI/AssemblyInfo.cs;$W/src_ready/isys/InputSystem/Editor/**\" /><Compile Include=\"$W/shim_isys.cs\" />"
proj game MorpheHouse.All "$R_UI$R_TMP$R_IS" "" "<Compile Include=\"$S/Scripts/**/*.cs\" Exclude=\"$S/Scripts/Editor/**\" />"
proj tests MorpheHouse.Tests "<PackageReference Include=\"NUnit\" Version=\"3.14.0\" /><Reference Include=\"$W/game/bin/Debug/net48/MorpheHouse.All.dll\" />" "" "<Compile Include=\"$S/Tests/**/*.cs\" />"

STATUS=0
for p in ugui tmp isys game tests; do
  echo "== build $p"
  OUT=$(cd "$W/$p" && dotnet build -nologo -v q 2>&1 | grep -E "error|Build succeeded" | sed 's| \[.*||' | sort -u | head -20)
  echo "$OUT"
  echo "$OUT" | grep -q "Build succeeded" || STATUS=1
done
[ "$STATUS" = "0" ] && echo "UNITY COMPILE CHECK: ALL OK" || echo "UNITY COMPILE CHECK: FAILED"
exit $STATUS
