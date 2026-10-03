#!/usr/bin/env python3
"""uGUI(6000.0)·InputSystem 소스를 2021.3 참조 어셈블리로 컴파일할 수 있게 하는 최소 패치."""
import glob
import sys

base = sys.argv[1].rstrip('/') + '/'

# 1) Unity 6 전용 RectTransform.sendChildDimensionsChange 제거
p = base + 'ugui/UGUI/UI/Core/Layout/LayoutGroup.cs'
lines = open(p, encoding='utf-8-sig').read().split('\n')
lines = [('// shim' if 'sendChildDimensionsChange' in l else l) for l in lines]
open(p, 'w', encoding='utf-8').write('\n'.join(lines))

# 2) 신규 RuntimePlatform.Switch2 → Switch (중복 case 라벨 제거 포함)
for f in glob.glob(base + 'ugui/TMP/*.cs'):
    s = open(f, encoding='utf-8-sig').read().replace('RuntimePlatform.Switch2', 'RuntimePlatform.Switch')
    open(f, 'w', encoding='utf-8').write(s)
p = base + 'ugui/TMP/TMP_InputField.cs'
lines = open(p, encoding='utf-8').read().split('\n')
out = []
for l in lines:
    if out and l.strip() == 'case RuntimePlatform.Switch:' and out[-1].strip() == 'case RuntimePlatform.Switch:':
        continue
    out.append(l)
open(p, 'w', encoding='utf-8').write('\n'.join(out))

# 3) net48 에 없는 Math.Clamp 대체
p = base + 'isys/InputSystem/Runtime/Actions/InputAction.cs'
s = open(p, encoding='utf-8-sig').read().replace('Math.Clamp(', 'UnityEngine.InputSystem.ClampHolder2.Clamp(')
open(p, 'w', encoding='utf-8').write(s)
