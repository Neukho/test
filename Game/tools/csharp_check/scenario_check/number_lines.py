#!/usr/bin/env python3
"""대사/선택지/지문 줄에 #line:<접두사>_NNN 을 붙인다(이미 있으면 유지, 멱등).
사용: number_lines.py <파일.yarn> <접두사>   (번호는 파일 안에서 기존 최댓값 이후로 이어짐)"""
import re, sys
path, prefix = sys.argv[1], sys.argv[2]
lines = open(path, encoding='utf-8').read().split('\n')
tag_re = re.compile(r'#line:' + re.escape(prefix) + r'_(\d+)')
mx = 0
for l in lines:
    for m in tag_re.finditer(l):
        mx = max(mx, int(m.group(1)))
in_body = False
out = []
for l in lines:
    st = l.strip()
    if st == '---':
        in_body = True; out.append(l); continue
    if st == '===':
        in_body = False; out.append(l); continue
    if not in_body or not st or st.startswith('//') or (st.startswith('<<') and st.endswith('>>') and not st.startswith('->')):
        out.append(l); continue
    if '#line:' in l:
        out.append(l); continue
    mx += 1
    tag = ' #line:%s_%03d' % (prefix, mx)
    m = re.search(r'\s<<if .*>>\s*$', l) if st.startswith('->') else None
    if m:
        l = l[:m.start()] + tag + l[m.start():]
    else:
        l = l.rstrip() + tag
    out.append(l)
open(path, 'w', encoding='utf-8').write('\n'.join(out))
print(path, 'last id', mx)
