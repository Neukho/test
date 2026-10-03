#!/usr/bin/env python3
"""
대본 동기화(Unity 메뉴 MorpheHouse/Dialogue/Sync Scripts 와 같은 규칙, Unity 없이 실행 가능).
Assets/_Project/Dialogue/**/*.yarn → Assets/_Project/Resources/Dialogue/<평탄화 이름>.txt (폴더 구분 '__'),
사라진 원본의 .txt 삭제, _index.txt 재생성(이름 정렬).

사용:
    python3 tools/sync_dialogue.py          # 동기화
    python3 tools/sync_dialogue.py --check  # 변경 없이 최신인지만 검사(최신이 아니면 종료코드 1)
"""
import os
import sys

SRC = "UnityProject/Assets/_Project/Dialogue"
DST = "UnityProject/Assets/_Project/Resources/Dialogue"
INDEX = "_index"
HEADER = "# MorpheHouse/Dialogue/Rebuild Index 가 생성한 목록입니다. 한 줄에 Resources/Dialogue 기준 에셋 이름 하나.\n"


def flatten(rel):
    p = rel.replace("\\", "/").strip("/")
    if p.lower().endswith(".yarn"):
        p = p[:-5]
    return p.replace("/", "__")


def collect():
    wanted = {}
    for root, _, files in os.walk(SRC):
        for f in sorted(files):
            if f.endswith(".yarn"):
                path = os.path.join(root, f)
                rel = os.path.relpath(path, SRC)
                with open(path, "r", encoding="utf-8-sig", newline="") as fh:
                    wanted[flatten(rel)] = fh.read()
    return wanted


def main():
    check = "--check" in sys.argv
    wanted = collect()
    os.makedirs(DST, exist_ok=True)
    stale = 0
    changed = 0
    for name, text in wanted.items():
        target = os.path.join(DST, name + ".txt")
        old = None
        if os.path.exists(target):
            with open(target, "r", encoding="utf-8", newline="") as fh:
                old = fh.read()
        if old != text:
            changed += 1
            if not check:
                with open(target, "w", encoding="utf-8", newline="") as fh:
                    fh.write(text)
    for f in os.listdir(DST):
        if f.endswith(".txt") and f[:-4] != INDEX and f[:-4] not in wanted:
            stale += 1
            if not check:
                os.remove(os.path.join(DST, f))
    index_text = HEADER + "".join(n + "\n" for n in sorted(wanted))
    index_path = os.path.join(DST, INDEX + ".txt")
    old_index = open(index_path, encoding="utf-8").read() if os.path.exists(index_path) else None
    index_changed = old_index != index_text
    if index_changed and not check:
        with open(index_path, "w", encoding="utf-8", newline="") as fh:
            fh.write(index_text)
    state = "변경 필요" if (changed or stale or index_changed) else "최신"
    print("대본 %d개 · 갱신 %d · 오래된 파일 %d · 인덱스 %s → %s" % (len(wanted), changed, stale, "변경" if index_changed else "동일", state))
    if check and (changed or stale or index_changed):
        sys.exit(1)


if __name__ == "__main__":
    main()
