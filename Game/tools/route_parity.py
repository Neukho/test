#!/usr/bin/env python3
"""루트별 분량 패리티 표: .yarn 에서 노드 / 대사 줄 / 선택지 수를 센다 (표준 라이브러리만).

줄 = 노드 본문(--- 와 === 사이)에서 빈 줄·주석(//)·커맨드(<<..>>)·선택지(->) 를 제외한 텍스트 줄.
태그줄 = 그중 '#line:' 태그가 있는 줄. 선택지 = '->' 로 시작하는 줄.

사용: python3 tools/route_parity.py [--md] [--tolerance 0.25]
종료 코드: 개별 루트 6개 중 총 줄 수가 평균 대비 허용 편차(기본 ±25%)를 벗어나는 루트가 있으면 1 (정보용; release_check 에서는 WARN 취급).
"""
import argparse
import glob
import os
import re
import sys

D = "UnityProject/Assets/_Project/Dialogue"
CHARS = ["howl", "johan", "beomjun", "haru", "gomseok", "nabi"]
CATS = ["event", "talk", "gift", "date", "ending", "harem"]


def category(fname):
    n = fname.lower()
    if re.search(r"_e[1-6]\.yarn$", n): return "event"
    if "_talk" in n: return "talk"
    if "_gift" in n: return "gift"
    if "_date" in n: return "date"
    if "_end" in n: return "ending"
    if "harem" in n: return "harem"
    return "other"


def count_file(path):
    nodes = lines = tagged = choices = 0
    in_body = False
    for raw in open(path, encoding="utf-8"):
        s = raw.strip()
        if not in_body:
            if s.startswith("title:"): nodes += 1
            if s == "---": in_body = True
            continue
        if s == "===":
            in_body = False
            continue
        if not s or s.startswith("//") or s.startswith("<<"): continue
        if s.startswith("->"):
            choices += 1
            continue
        lines += 1
        if "#line:" in s: tagged += 1
    return nodes, lines, tagged, choices


def collect():
    rows = {}
    for f in sorted(glob.glob(D + "/**/*.yarn", recursive=True)):
        rel = os.path.relpath(f, D).replace(os.sep, "/")
        route = rel.split("/")[1] if rel.startswith("routes/") else "common"
        n, l, t, c = count_file(f)
        r = rows.setdefault(route, {"nodes": 0, "lines": 0, "tagged": 0, "choices": 0, "files": 0, "cat": {k: 0 for k in CATS + ["other"]}})
        r["nodes"] += n; r["lines"] += l; r["tagged"] += t; r["choices"] += c; r["files"] += 1
        r["cat"][category(os.path.basename(f))] += l
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--md", action="store_true", help="마크다운 표로 출력")
    ap.add_argument("--tolerance", type=float, default=0.25)
    a = ap.parse_args()
    rows = collect()
    order = [c for c in CHARS if c in rows] + (["common"] if "common" in rows else [])
    mean = sum(rows[c]["lines"] for c in CHARS if c in rows) / max(1, len([c for c in CHARS if c in rows]))

    def pct(c):
        return (rows[c]["lines"] / mean - 1.0) * 100.0 if mean else 0.0

    hdr = ["route", "files", "nodes", "lines", "tagged", "choices", "vs mean"] + CATS
    out = []
    for c in order:
        r = rows[c]
        vs = "%+.0f%%" % pct(c) if c != "common" else "-"
        out.append([c, r["files"], r["nodes"], r["lines"], r["tagged"], r["choices"], vs] + [r["cat"][k] for k in CATS])
    tot = ["TOTAL"] + [sum(rows[c][k] for c in order) for k in ("files", "nodes", "lines", "tagged", "choices")] + ["", ] + [sum(rows[c]["cat"][k] for c in order) for k in CATS]
    out.append(tot)
    if a.md:
        print("| " + " | ".join(hdr) + " |")
        print("|" + "---|" * len(hdr))
        for r in out: print("| " + " | ".join(str(x) for x in r) + " |")
    else:
        w = [max(len(str(x)) for x in col) for col in zip(hdr, *out)]
        print("  ".join(str(h).ljust(w[i]) for i, h in enumerate(hdr)))
        for r in out: print("  ".join(str(x).ljust(w[i]) for i, x in enumerate(r)))
    print("\n개별 루트 6명 평균 줄 수: %.1f (허용 편차 ±%d%%)" % (mean, int(a.tolerance * 100)))
    bad = [c for c in CHARS if c in rows and abs(pct(c)) > a.tolerance * 100]
    if bad:
        print("편차 초과 루트:", ", ".join("%s(%+.0f%%)" % (c, pct(c)) for c in bad))
        return 1
    print("모든 개별 루트가 허용 편차 이내")
    return 0


if __name__ == "__main__":
    sys.exit(main())
