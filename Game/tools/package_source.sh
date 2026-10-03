#!/bin/sh
# 소스 아카이브(재현 가능한 tar.gz) + SHA-256 을 dist/ 에 만든다. 라이선스 공개·백업·감사용.
# 사용: sh tools/package_source.sh [버전(기본: ProjectSettings 의 bundleVersion 또는 'dev')]
# 제외: Library/ Temp/ obj/ Logs/ UserSettings/ Builds/ dist/ .git/ *.apk *.aab *.keystore *.jks *.pfx *.p12 .env __pycache__ *.csproj *.sln
# git 저장소면 `git ls-files`(추적 파일만), 아니면 find 로 위 규칙을 적용한다.
set -e
cd "$(dirname "$0")/.."
# 출시 소스에는 최신 Resources 사본이 들어가야 한다(QA-09). 오래됐으면 중단.
python3 tools/sync_dialogue.py --check >/dev/null && python3 tools/sync_balance.py --check >/dev/null \
  || { echo "ERROR: Resources/Dialogue or Resources/Balance out of sync: run python3 tools/sync_dialogue.py && python3 tools/sync_balance.py"; exit 1; }
VER="$1"
if [ -z "$VER" ] && [ -f UnityProject/ProjectSettings/ProjectSettings.asset ]; then
  VER=$(sed -n 's/^[[:space:]]*bundleVersion:[[:space:]]*//p' UnityProject/ProjectSettings/ProjectSettings.asset | head -1)
fi
[ -n "$VER" ] || VER=dev
NAME="MorpheHouse-src-$VER"
mkdir -p dist
LIST=$(mktemp)
if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  COMMIT=$(git rev-parse HEAD 2>/dev/null || echo unavailable)
  git ls-files > "$LIST"
else
  COMMIT=unavailable
  find . \( -path ./.git -o -path ./dist -o -path ./Builds -o -path '*/Library' -o -path '*/Temp' -o -path '*/obj' -o -path '*/Logs' -o -path '*/UserSettings' -o -name __pycache__ \) -prune -o -type f \
    ! -name '*.apk' ! -name '*.aab' ! -name '*.keystore' ! -name '*.jks' ! -name '*.pfx' ! -name '*.p12' ! -name '*.pem' ! -name '*.key' ! -name '*.p8' ! -name 'keystore*.properties' ! -name 'local.properties' ! -name 'google-services.json' ! -name '.env' ! -name '.env.*' ! -name '*.csproj' ! -name '*.sln' -print | sed 's|^\./||' > "$LIST"
fi
# 비밀 파일이 목록에 섞였으면 중단(안전장치)
# release_check.sh step_secrets 와 같은 패턴(+ 흔한 키/자격 파일)을 막는다
if grep -Eq '\.(keystore|jks|pfx|p12|pem|key|p8)$|(^|/)keystore[^/]*\.properties$|(^|/)local\.properties$|(^|/)google-services\.json$|(^|/)\.env($|\.)' "$LIST"; then echo "ERROR: secret-like file in source list"; exit 1; fi
LC_ALL=C sort -o "$LIST" "$LIST"
{
  echo "MorpheHouse source archive"
  echo "version: $VER"
  echo "git_commit: $COMMIT"
  echo "created_utc: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "files: $(wc -l < "$LIST" | tr -d ' ')"
} > dist/SOURCE_INFO.txt
# 결정적 순서·소유자·시간으로 묶는다(GNU tar). 시간은 SOURCE_DATE_EPOCH 가 있으면 그것을 쓴다.
MT="${SOURCE_DATE_EPOCH:-0}"
tar --sort=name --owner=0 --group=0 --numeric-owner --mtime="@$MT" -czf "dist/$NAME.tar.gz" -T "$LIST" --transform "s|^|$NAME/|" 2>/dev/null \
  || tar -czf "dist/$NAME.tar.gz" -T "$LIST" --transform "s|^|$NAME/|"
( cd dist && sha256sum "$NAME.tar.gz" > "$NAME.tar.gz.sha256" )
rm -f "$LIST"
echo "wrote dist/$NAME.tar.gz ($(wc -c < "dist/$NAME.tar.gz") bytes)"
cat "dist/$NAME.tar.gz.sha256"
