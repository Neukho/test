#!/bin/sh
# 출시 전 단일 점검: 모든 순수 하네스 + Unity API 컴파일 검사 + 플래그 감사 + 아트 체크리스트 + 보조 검사를 실행하고
# 마지막에 PASS/FAIL 요약 표를 출력한다. 종료 코드: FAIL 이 하나라도 있으면 1.
#
# 사용:  sh tools/release_check.sh [--fast] [--skip-unity] [--skip-scenario] [--logs DIR]
#   --fast           느린 검사(scenario_check 약 2분, unity_compile 네트워크 필요)를 건너뜀(SKIP 으로 표시)
#   --skip-unity     Unity API 컴파일 검사만 건너뜀(인터넷/dotnet 없는 환경)
#   --skip-scenario  scenario_check 만 건너뜀
#   --logs DIR       단계별 전체 로그를 DIR 에 남김(기본: 임시 폴더, 실패한 단계만 마지막 줄을 요약에 표시)
# 필요: mono-mcs, mono-runtime, python3. Unity 컴파일 검사는 추가로 dotnet-sdk-8.0, git, curl, unzip, 인터넷.
# 한계: 이 스크립트가 PASS 해도 Unity 에디터 안에서의 빌드·렌더링·입력·Editor 스크립트 컴파일은 검증되지 않는다(docs/17_Test_Plan.md).
cd "$(dirname "$0")/.." || exit 2
ROOT=$(pwd)
SKIP_UNITY=0; SKIP_SCEN=0; LOG=""
while [ $# -gt 0 ]; do
  case "$1" in
    --fast) SKIP_UNITY=1; SKIP_SCEN=1 ;;
    --skip-unity) SKIP_UNITY=1 ;;
    --skip-scenario) SKIP_SCEN=1 ;;
    --logs) shift; LOG="$1" ;;
    *) echo "unknown option: $1"; exit 2 ;;
  esac
  shift
done
[ -n "$LOG" ] || LOG=$(mktemp -d)
mkdir -p "$LOG"
S=UnityProject/Assets/_Project

# 단계 결과 기록: st <이름> <PASS|FAIL|WARN|SKIP> <상세>
st() { printf '%s' "$2" > "$LOG/$1.status"; printf '%s' "$3" > "$LOG/$1.detail"; }

# 하네스 공통: rc 0 이고 로그에 FAIL 줄이 없으면 PASS. 상세는 로그에서 검사 수를 추출.
harness() { # name cmd...
  n=$1; shift
  "$@" > "$LOG/$n.log" 2>&1; rc=$?
  d=$(grep -Eo 'checks=[0-9]+ failures=[0-9]+|[0-9]+ checks, [0-9]+ failures' "$LOG/$n.log" | tail -1)
  [ -n "$d" ] || d="$(grep -c 'PASS' "$LOG/$n.log") PASS lines"
  if [ $rc -eq 0 ] && ! grep -Eq '(^|\[)FAIL' "$LOG/$n.log"; then st "$n" PASS "$d"; else st "$n" FAIL "rc=$rc; $(tail -1 "$LOG/$n.log" | cut -c1-60)"; fi
}

step_unity() {
  if [ "$SKIP_UNITY" = 1 ]; then st unity_compile SKIP "skipped by option"; return; fi
  if ! command -v dotnet >/dev/null 2>&1; then st unity_compile SKIP "dotnet not installed"; return; fi
  WORK="${WORK:-/tmp/unitycheck_work}" sh tools/unity_compile_check/run.sh > "$LOG/unity_compile.log" 2>&1; rc=$?
  if grep -q "UNITY COMPILE CHECK: ALL OK" "$LOG/unity_compile.log"; then st unity_compile PASS "5 projects built (ugui,tmp,isys,game,tests)"
  elif grep -Eq "Could not resolve host|unable to access|curl: \(|Failed to download|NU1301|nuget.org" "$LOG/unity_compile.log"; then st unity_compile SKIP "network/package source unavailable (see log)"
  else st unity_compile FAIL "rc=$rc; $(grep -E 'error' "$LOG/unity_compile.log" | head -1 | cut -c1-60)"; fi
}

step_scenario() {
  if [ "$SKIP_SCEN" = 1 ]; then st scenario_check SKIP "skipped by option (about 2 min)"; return; fi
  harness scenario_check sh tools/csharp_check/scenario_check/run.sh
}

step_flags() {
  python3 tools/flag_audit.py > "$LOG/flag_audit.log" 2>&1; rc=$?
  u=$(grep -Eo '읽기만 하고 어떤 스크립트도 설정하지 않는 플래그\] \([0-9]+\)' "$LOG/flag_audit.log" | grep -Eo '[0-9]+' | tail -1)
  if [ $rc -ne 0 ] || [ -z "$u" ]; then st flag_audit FAIL "rc=$rc, unparsable"
  elif [ "$u" -gt 0 ]; then st flag_audit FAIL "$u flag(s) read but never set"
  else st flag_audit PASS "0 flags read-but-never-set"; fi
}

# flag_audit.py 의 CG 요청서 매칭은 단순(이름 규칙)해서 오탐이 있으므로, 여기서는 docs/art_requests/*.md 전체를 grep 한다.
step_cg() {
  python3 - > "$LOG/cg_requests.log" 2>&1 <<'PY'
import glob, re, sys
ids = set()
for f in glob.glob("UnityProject/Assets/_Project/Dialogue/**/*.yarn", recursive=True):
    ids |= set(re.findall(r"<<show_cg\s+([A-Za-z0-9_]+)", open(f, encoding="utf-8").read()))
req = "".join(open(f, encoding="utf-8").read() for f in glob.glob("docs/art_requests/*.md"))
missing = sorted(i for i in ids if i not in req)
print("cg ids used: %d, missing from art_requests: %s" % (len(ids), missing or "none"))
sys.exit(1 if missing else 0)
PY
  rc=$?
  if [ $rc -eq 0 ]; then st cg_requests PASS "$(cut -c1-60 "$LOG/cg_requests.log")"; else st cg_requests FAIL "$(cut -c1-70 "$LOG/cg_requests.log")"; fi
}

# 아트 체크리스트가 시나리오와 일치하는지: 임시 폴더에서 재생성해 저장소의 문서와 비교(저장소 파일은 건드리지 않음).
step_art() {
  T=$(mktemp -d); mkdir "$T/docs"; cp -r docs/art_requests "$T/docs/"; ln -s "$ROOT/UnityProject" "$T/UnityProject"
  ( cd "$T" && python3 "$ROOT/tools/art_checklist.py" ) > "$LOG/art_checklist.log" 2>&1; rc=$?
  line=$(head -1 "$LOG/art_checklist.log")
  if [ $rc -ne 0 ]; then st art_checklist FAIL "generator rc=$rc"
  elif cmp -s "$T/docs/13_Art_Checklist.md" docs/13_Art_Checklist.md && cmp -s "$T/docs/art_checklist.csv" docs/art_checklist.csv; then st art_checklist PASS "$line (docs up to date)"
  else st art_checklist FAIL "docs/13 or art_checklist.csv stale: run python3 tools/art_checklist.py"; fi
  rm -rf "$T"
}

step_balance() {
  { python3 tools/simulate_balance.py --runs 100 --group 2 && python3 tools/simulate_balance.py --runs 100 --group 3; } > "$LOG/balance_sim.log" 2>&1; rc=$?
  if [ $rc -eq 0 ] && grep -q "공동 루트 실험" "$LOG/balance_sim.log"; then st balance_sim PASS "normal + group K=2,K=3 (100 runs each) ran"; else st balance_sim FAIL "rc=$rc"; fi
}

step_parity() {
  python3 tools/route_parity.py > "$LOG/route_parity.log" 2>&1; rc=$?
  m=$(grep -Eo '평균 줄 수: [0-9.]+' "$LOG/route_parity.log")
  if [ $rc -eq 0 ]; then st route_parity PASS "6 routes within +-25% ($m)"
  elif [ $rc -eq 1 ]; then st route_parity WARN "$(grep '편차 초과' "$LOG/route_parity.log" | cut -c1-60)"
  else st route_parity FAIL "rc=$rc"; fi
}

# Unity 는 Resources 사본만 읽는다: Dialogue/**/*.yarn 과 balance/*.csv 의 사본이 최신이어야 한다(QA-09). 고치려면 sync_dialogue.py / sync_balance.py 를 --check 없이 실행.
step_sync() {
  python3 tools/sync_dialogue.py --check > "$LOG/resources_sync.log" 2>&1; r1=$?
  python3 tools/sync_balance.py --check >> "$LOG/resources_sync.log" 2>&1; r2=$?
  python3 tools/csharp_check/qa_hunt/sync_check.py >> "$LOG/resources_sync.log" 2>&1; r3=$?
  if [ $r1 -eq 0 ] && [ $r2 -eq 0 ] && [ $r3 -eq 0 ]; then st resources_sync PASS "Resources/Dialogue + Resources/Balance match sources"
  else st resources_sync FAIL "out of sync (dlg=$r1 bal=$r2 idx=$r3): run python3 tools/sync_dialogue.py && python3 tools/sync_balance.py"; fi
}

step_sbom() {
  python3 tools/generate_sbom.py --check > "$LOG/sbom.log" 2>&1; rc=$?
  if [ $rc -eq 0 ]; then st sbom PASS "$(cut -c1-70 "$LOG/sbom.log")"; else st sbom FAIL "$(cut -c1-70 "$LOG/sbom.log")"; fi
}

# 휴리스틱: 주인공 대사/지문에 성별이 드러나는 하드코딩 호칭이 없어야 한다(docs/07 §4-4, docs/17 content QA).
step_gender() {
  grep -rEn '^player:.*(그녀|소녀|소년|아가씨|도련님|누나|오빠|언니|아저씨|아줌마|여자|남자|형님)' UnityProject/Assets/_Project/Dialogue --include=*.yarn > "$LOG/gender_grep.log" 2>&1
  c=$(grep -c . "$LOG/gender_grep.log")
  if [ "$c" -eq 0 ]; then st gender_grep PASS "0 gendered terms in player lines (heuristic)"; else st gender_grep FAIL "$c hit(s), see log"; fi
}

step_syntax() {
  bad=0; n=0
  for f in tools/*.sh tools/*/*.sh tools/*/*/*.sh; do [ -f "$f" ] || continue; n=$((n+1)); sh -n "$f" 2>>"$LOG/syntax.log" || { bad=$((bad+1)); echo "sh -n failed: $f" >> "$LOG/syntax.log"; }; done
  p=0
  for f in tools/*.py tools/*/*.py; do [ -f "$f" ] || continue; p=$((p+1)); python3 -c "import ast,sys; ast.parse(open(sys.argv[1],encoding='utf-8').read())" "$f" 2>>"$LOG/syntax.log" || { bad=$((bad+1)); echo "py syntax failed: $f" >> "$LOG/syntax.log"; }; done
  if [ $bad -eq 0 ]; then st script_syntax PASS "$n sh + $p py files parse"; else st script_syntax FAIL "$bad file(s) failed, see log"; fi
}

# Editor 스크립트는 UnityEditor 가 없어 컴파일 불가 -> 문법 오류(CS1xxx 등)만 걸러낸다. 타입 해석 오류(CS0246/0234/0103/0117/0012/0006 등)는 예상되므로 제외.
step_editor() {
  E=$S/Scripts/Editor
  command -v mcs >/dev/null 2>&1 || { st editor_syntax SKIP "mcs missing"; return; }
  mcs -target:library -out:"$LOG/editor_probe.dll" $E/BuildTools.cs $E/BuildPreflight.cs > "$LOG/editor_syntax.log" 2>&1
  real=$(grep -E 'error CS' "$LOG/editor_syntax.log" | grep -Ev 'CS0246|CS0234|CS0103|CS0117|CS0012|CS0006|CS0433|CS0305|CS0122|CS1061|CS0841|CS0119|CS1503|CS0029|CS1729' | head -3)
  if [ -z "$real" ]; then st editor_syntax WARN "BuildTools/BuildPreflight: no syntax errors; type-check needs Unity (unverified)"; else st editor_syntax FAIL "$(echo "$real" | head -1 | cut -c1-70)"; fi
}

# 비밀정보가 저장소에 없는지(키스토어 파일, 따옴표 안의 비밀번호/키 대입).
step_secrets() {
  k=$(find . -path ./.git -prune -o \( -name '*.keystore' -o -name '*.jks' -o -name '*.pfx' -o -name '*.p12' -o -name '*.pem' -o -name '*.key' -o -name '*.p8' -o -name 'keystore*.properties' -o -name 'local.properties' -o -name 'google-services.json' -o -name '.env' -o -name '.env.*' \) -print 2>/dev/null | head -3)
  g=$(grep -rEIn "(password|passwd|secret|api[_-]?key|token)[A-Za-z_]*[[:space:]]*[:=][[:space:]]*['\"][^'\"\$<{ ]{8,}['\"]" docs tools UnityProject/Assets --include=*.md --include=*.sh --include=*.py --include=*.cs --include=*.txt --include=*.json --include=*.csv 2>/dev/null | head -3)
  if [ -z "$k$g" ]; then st secret_scan PASS "no key files / quoted credentials found (heuristic)"; else printf '%s\n%s\n' "$k" "$g" > "$LOG/secret_scan.log"; st secret_scan FAIL "see $LOG/secret_scan.log"; fi
}

echo "release_check: starting (logs: $LOG)"
T0=$(date +%s)
harness core_logic        sh tools/csharp_check/run.sh &
harness romance_check     sh tools/csharp_check/romance_check/run.sh &
harness dialogue_check    sh tools/csharp_check/dialogue_check/run.sh &
harness ending_check      sh tools/csharp_check/ending_check/run.sh &
harness flow_check        sh tools/csharp_check/flow_check/run.sh &
harness ui_logic_check    sh tools/csharp_check/ui_logic_check/run.sh &
step_scenario &
step_unity &
step_flags &
step_cg &
step_art &
step_balance &
step_parity &
step_sync &
step_sbom &
step_gender &
step_syntax &
step_editor &
step_secrets &
wait
T1=$(date +%s)

ORDER="core_logic romance_check dialogue_check ending_check flow_check ui_logic_check scenario_check unity_compile flag_audit cg_requests art_checklist balance_sim route_parity resources_sync sbom gender_grep script_syntax editor_syntax secret_scan"
echo
printf '%-16s %-6s %s\n' CHECK STATUS DETAIL
printf '%-16s %-6s %s\n' ---------------- ------ ------------------------------------------------------
nf=0; nw=0; ns=0; np=0
for n in $ORDER; do
  s=$(cat "$LOG/$n.status" 2>/dev/null); [ -n "$s" ] || s=FAIL
  d=$(cat "$LOG/$n.detail" 2>/dev/null); [ -n "$d" ] || d="no result recorded"
  printf '%-16s %-6s %s\n' "$n" "$s" "$d"
  case "$s" in PASS) np=$((np+1));; FAIL) nf=$((nf+1));; WARN) nw=$((nw+1));; SKIP) ns=$((ns+1));; esac
done
echo
echo "PASS $np / WARN $nw / SKIP $ns / FAIL $nf   (elapsed $((T1-T0))s, logs: $LOG)"
echo "Not covered here: Unity Editor build, Editor scripts type-check, rendering/input, device tests -> docs/17_Test_Plan.md"
if [ $nf -eq 0 ]; then echo "RELEASE CHECK: PASS"; exit 0; else echo "RELEASE CHECK: FAIL"; exit 1; fi
