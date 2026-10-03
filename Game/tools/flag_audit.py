#!/usr/bin/env python3
"""시나리오(.yarn) 전체의 플래그·CG 정합성 점검: 읽기만 하고 설정되지 않는 플래그, CG ID와 요청서 일치 여부."""
import re, glob, os, collections

D = "UnityProject/Assets/_Project/Dialogue"
set_flags, read_flags = collections.defaultdict(set), collections.defaultdict(set)
cgs = collections.defaultdict(set)
nodes = collections.Counter()
for f in glob.glob(D + "/**/*.yarn", recursive=True):
    rel = os.path.relpath(f, D)
    txt = open(f, encoding="utf-8").read()
    nodes[rel.split("/")[1] if rel.startswith("routes/") else "common"] += len(re.findall(r"^title:", txt, re.M))
    for m in re.finditer(r"<<flag\s+([A-Za-z0-9_]+)\s*>>", txt): set_flags[m.group(1)].add(rel)
    for m in re.finditer(r'flag\("([A-Za-z0-9_]+)"\)', txt): read_flags[m.group(1)].add(rel)
    for m in re.finditer(r"<<show_cg\s+([A-Za-z0-9_]+)\s*>>", txt): cgs[m.group(1)].add(rel)

print("노드 수:", dict(nodes))
unset = sorted(k for k in read_flags if k not in set_flags)
print("\n[읽기만 하고 어떤 스크립트도 설정하지 않는 플래그] (%d)" % len(unset))
for k in unset: print("  ", k, "<- 읽는 곳:", ", ".join(sorted(read_flags[k]))[:90])
unread = sorted(k for k in set_flags if k not in read_flags)
print("\n[설정만 하고 읽지 않는 플래그] (%d, 호스트·엔딩 판정이 읽을 수 있음)" % len(unread))
print("  ", ", ".join(unread)[:600])

print("\n[CG ID] 스크립트에서 쓰는 CG %d종" % len(cgs))
all_req = ""
for rf in glob.glob("docs/art_requests/*.md"):
    all_req += open(rf, encoding="utf-8").read() + "\n"
missing = []
for cg in sorted(cgs):
    file_name = "CG_%s" % ("_".join([cg.split("_")[0].capitalize()] + cg.split("_")[1:]) if cg.split("_")[0].lower() in ("howl","johan","beomjun","haru","gomseok","nabi") else cg)
    if cg not in all_req and file_name not in all_req:
        missing.append(cg)
print("  모든 art_requests/*.md 에서 찾지 못한 CG:", missing if missing else "없음")
