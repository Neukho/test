#!/usr/bin/env python3
"""QA: are the Unity Resources copies in sync with their sources?  (Unity only syncs them via editor menus, nothing in release_check.sh verifies it.)
   Dialogue/**/*.yarn -> Resources/Dialogue/<flattened>.txt + _index.txt ;  balance/*.csv -> Resources/Balance/*.csv"""
import os, sys
root = sys.argv[1] if len(sys.argv) > 1 else "."
P = os.path.join(root, "UnityProject/Assets/_Project")
src, dst = os.path.join(P, "Dialogue"), os.path.join(P, "Resources/Dialogue")
bad = 0
idx = set()
ip = os.path.join(dst, "_index.txt")
if os.path.exists(ip):
    idx = {l.strip() for l in open(ip, encoding="utf-8") if l.strip() and not l.startswith("#")}
yarn = {}
for dp, _, fs in os.walk(src):
    for f in fs:
        if f.endswith(".yarn"):
            rel = os.path.relpath(os.path.join(dp, f), src)[:-5].replace(os.sep, "__")
            yarn[rel] = os.path.join(dp, f)
for rel, p in sorted(yarn.items()):
    t = os.path.join(dst, rel + ".txt")
    if rel not in idx: print("NOT IN _index.txt:", rel); bad += 1
    if not os.path.exists(t): print("NO RESOURCES COPY:", rel); bad += 1
    elif open(t, encoding="utf-8").read() != open(p, encoding="utf-8").read(): print("STALE COPY:", rel); bad += 1
for n in sorted(idx - set(yarn)): print("INDEX ENTRY WITHOUT SOURCE:", n); bad += 1
bsrc, bdst = os.path.join(root, "balance"), os.path.join(P, "Resources/Balance")
for f in sorted(os.listdir(bsrc)):
    if f.endswith(".csv"):
        t = os.path.join(bdst, f)
        if not os.path.exists(t): print("NO RESOURCES BALANCE COPY:", f); bad += 1
        elif open(t, encoding="utf-8").read() != open(os.path.join(bsrc, f), encoding="utf-8").read(): print("STALE BALANCE COPY:", f); bad += 1
print("sync_check:", "OK" if bad == 0 else "%d problem(s)" % bad)
sys.exit(1 if bad else 0)
