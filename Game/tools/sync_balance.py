#!/usr/bin/env python3
"""
밸런스 CSV 동기화(tools/sync_dialogue.py 와 같은 규칙). balance/*.csv → UnityProject/Assets/_Project/Resources/Balance/*.csv
(Unity 는 Resources 사본만 읽는다). 원본에서 사라진 사본 .csv 는 삭제한다.

사용:
    python3 tools/sync_balance.py          # 동기화
    python3 tools/sync_balance.py --check  # 변경 없이 최신인지만 검사(최신이 아니면 종료코드 1)
"""
import os
import sys

SRC = "balance"
DST = "UnityProject/Assets/_Project/Resources/Balance"


def read(path):
    with open(path, "r", encoding="utf-8", newline="") as fh:
        return fh.read()


def main():
    check = "--check" in sys.argv
    wanted = {}
    for f in sorted(os.listdir(SRC)):
        if f.endswith(".csv"):
            wanted[f] = read(os.path.join(SRC, f))
    os.makedirs(DST, exist_ok=True)
    changed = 0
    stale = 0
    for name, text in wanted.items():
        target = os.path.join(DST, name)
        if not os.path.exists(target) or read(target) != text:
            changed += 1
            print("  %s: %s" % ("STALE" if os.path.exists(target) else "MISSING", name))
            if not check:
                with open(target, "w", encoding="utf-8", newline="") as fh:
                    fh.write(text)
    for f in sorted(os.listdir(DST)):
        if f.endswith(".csv") and f not in wanted:
            stale += 1
            print("  ORPHAN: %s" % f)
            if not check:
                os.remove(os.path.join(DST, f))
    state = "변경 필요" if (changed or stale) else "최신"
    print("밸런스 CSV %d개 · 갱신 %d · 원본 없는 사본 %d → %s" % (len(wanted), changed, stale, state))
    if check and (changed or stale):
        sys.exit(1)


if __name__ == "__main__":
    main()
