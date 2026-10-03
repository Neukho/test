#!/usr/bin/env python3
"""
일러스트 제작 체크리스트 생성기.
시나리오(.yarn)를 읽어 실제로 쓰이는 스탠딩 표정·CG·배경만 '필수'로, 나머지는 '선택'으로 나눠
docs/13_Art_Checklist.md 와 docs/art_checklist.csv 를 만든다.

의상: 시나리오는 `<<show_standing char expr [outfit]>>` 에서 의상을 거의 지정하지 않는다. 지정하지 않은 호출은 게임(Game/OutfitPolicy.cs)이
상황으로 의상을 고르므로, 이 스크립트도 같은 규칙(resolve_outfit)으로 필요한 의상을 센다:
  1) 명시한 의상이 항상 우선
  2) 노드 제목에 _Date_ 가 있으면 Date
  3) 직원 인물(howl, gomseok, haru, nabi)이고 현재 배경이 카페(Cafe_Interior/Cafe_Exterior) 낮(Day)이면 Uniform
  4) 그 외 Casual
"현재 배경" 은 노드 안의 `<<bg>>` 를 순서대로 따라가 추적하고, 노드 시작 배경은 게임이 정하는 대로(카페 내부, 낮 단계 노드면 Day / 밤 단계 노드면 Night)
노드 종류로 추정한다(Ch*_/CE*_/Ending_/*_End_*/*_Harem_Epilogue = Day, 나머지 인물 노드 = Night). 실제 단계는 런타임에만 확정되므로 정적 추정이다.

사용: python3 tools/art_checklist.py            (체크리스트 재생성)
      python3 tools/art_checklist.py --selftest  (의상 규칙 표 검증: tools/csharp_check/polish_check/outfit_cases.txt)
"""
import collections
import csv
import glob
import os
import re
import sys

D = "UnityProject/Assets/_Project/Dialogue"
RES = "UnityProject/Assets/_Project/Resources/Art"

CHARS = ["howl", "johan", "beomjun", "haru", "gomseok", "nabi"]
NAMES_KO = {"howl": "하울", "johan": "요한", "beomjun": "범준", "haru": "하루", "gomseok": "곰석", "nabi": "나비"}
OUTFITS = ["Casual", "Uniform", "Date"]
EXPRS = {"neutral": "Neutral", "smile": "Smile", "shy": "Shy", "angry": "Angry", "sad": "Sad", "surprised": "Surprised"}
STAFF = ["howl", "gomseok", "haru", "nabi"]
DEFAULT_PLACE = "Cafe_Interior"
PLACES = ["Cafe_Interior", "Cafe_Exterior", "Street", "Lighthouse", "FishMarket", "NightMarket", "Dojo", "LiveClub",
          "Greenhouse", "BookStreet", "Beach", "Rooftop"]
MENU_IDS = [
    "HouseCoffee", "Latte", "CinnamonLatte", "Espresso", "MilkTea", "HerbTea", "SmokedCheeseBoard", "GrilledFish",
    "SteakSandwich", "StrawberryTart", "MacaronSet", "SeasonalParfait", "TomatoStew", "SeafoodSoup", "BreadBasket",
    "HoneyToast", "SpicyNoodle", "FishBurger", "CitrusCocktail", "WhiskyFloat", "HarborPie", "LemonCake",
    "ChocolateFondant", "PotatoGratin", "CreamPasta", "ClamChowder", "RoastChicken", "MushroomRisotto", "ApplePie",
    "PancakeStack", "IceCreamFloat", "GingerTea", "HotChocolate", "BerryScone", "CheeseCake", "SalmonBowl",
    "MoonCookie", "NightCaramel", "StarSoda", "MorenaSpecial",
]


def squash(text):
    return text.replace("_", "").replace("-", "").strip().lower()


def norm_place(text):
    """Game/ArtNames.Place 와 같은 규칙. 알 수 없으면 None."""
    key = squash(text)
    if key in ("cafe", "cafeinside", "shop"):
        return "Cafe_Interior"
    if key == "cafeoutside":
        return "Cafe_Exterior"
    if key in ("harbor", "harbour"):
        return "Street"
    for p in PLACES:
        if squash(p) == key:
            return p
    return None


def norm_time(text):
    """Game/ArtNames.Time 와 같은 규칙. 알 수 없으면 None."""
    key = squash(text)
    if key in ("dawn", "morning", "noon"):
        return "Day"
    if key in ("dusk", "sunset"):
        return "Evening"
    for t in ("Day", "Evening", "Night"):
        if t.lower() == key:
            return t
    return None


def resolve_outfit(ch, explicit, node, place, time):
    """Game/OutfitPolicy.Resolve 와 같은 규칙(규칙을 바꾸면 양쪽을 함께 고칠 것)."""
    if explicit:
        for o in OUTFITS:
            if o.lower() == explicit.strip().lower():
                return o
    if node and "_date_" in node.lower():
        return "Date"
    if ch in STAFF and (place or "").lower() in ("cafe_interior", "cafe_exterior") and (time or "").lower() == "day":
        return "Uniform"
    return "Casual"


def node_start_time(title):
    """노드가 어느 단계에서 재생되는지의 정적 추정: 낮 단계 노드면 Day, 밤 단계 노드면 Night."""
    if re.match(r"^(Ch\d+_|CE\d+_|Ending_)", title):
        return "Day"
    if re.search(r"_End_|_Harem_Epilogue$", title):
        return "Day"
    return "Night"


def selftest():
    """outfit_cases.txt 의 각 줄(node|place|time|char|explicit|expected)을 resolve_outfit 으로 검증한다."""
    path = "tools/csharp_check/polish_check/outfit_cases.txt"
    bad = 0
    n = 0
    for line in open(path, encoding="utf-8"):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        node, place, time, ch, explicit, expected = line.split("|")
        got = resolve_outfit(ch, explicit, node, place, time)
        n += 1
        if got != expected:
            bad += 1
            print("FAIL: %s -> %s (expected %s)" % (line, got, expected))
    print("selftest: %d cases, %d failures" % (n, bad))
    return 1 if bad else 0


def pascal(c):
    return c[0].upper() + c[1:]


def exists(rel):
    return os.path.exists(os.path.join(RES, rel + ".png"))


def scan():
    expr_use = collections.defaultdict(collections.Counter)    # char -> expr -> count
    outfit_use = collections.defaultdict(collections.Counter)  # char -> outfit -> count(명시 + 정책)
    policy_use = collections.defaultdict(collections.Counter)  # char -> outfit -> 정책이 정한 횟수
    face_use = collections.Counter()                           # (char, outfit, expr) -> 횟수(의상별 표정 사용)
    cg_use = collections.defaultdict(set)                      # key -> files
    bg_use = collections.defaultdict(set)                      # (place,time) -> files
    for f in glob.glob(D + "/**/*.yarn", recursive=True):
        rel = os.path.relpath(f, D)
        txt = open(f, encoding="utf-8").read()
        for m in re.finditer(r"<<show_cg\s+(\w+)\s*>>", txt):
            cg_use[m.group(1).upper()].add(rel)
        # 노드별로 <<bg>> 를 순서대로 추적하면서 <<show_standing>> 의 의상을 정한다.
        node, place, time = "", DEFAULT_PLACE, "Day"
        cmd = re.compile(r"^\s*title:\s*(\S+)|<<(show_standing|bg)\s+([^>]*?)\s*>>")
        for line in txt.splitlines():
            for m in cmd.finditer(line):
                if m.group(1):
                    node = m.group(1)
                    place, time = DEFAULT_PLACE, node_start_time(node)
                    continue
                kind, args = m.group(2), m.group(3).split()
                if kind == "bg" and args:
                    p = norm_place(args[0])
                    t2 = norm_time(args[1]) if len(args) > 1 else "Day"
                    if p:
                        place = p
                        time = t2 or "Day"
                        bg_use[(p, time)].add(rel)
                elif kind == "show_standing" and len(args) >= 2:
                    ch, ex = args[0].lower(), args[1].lower()
                    if ch in CHARS and ex in EXPRS:
                        expr_use[ch][EXPRS[ex]] += 1
                        explicit = args[2] if len(args) > 2 else None
                        of = resolve_outfit(ch, explicit, node, place, time)
                        outfit_use[ch][of] += 1
                        face_use[(ch, of, EXPRS[ex])] += 1
                        if not (explicit and explicit.capitalize() in OUTFITS):
                            policy_use[ch][of] += 1
    return expr_use, outfit_use, cg_use, bg_use, policy_use, face_use


def main():
    expr_use, outfit_use, cg_use, bg_use, policy_use, face_use = scan()
    rows = []  # category, file, size, priority, used_in, exists_in_repo

    for ch in CHARS:
        n = pascal(ch)
        for of in OUTFITS:
            used_of = outfit_use[ch][of] > 0 or of == "Casual"
            rows.append(("스탠딩 바디", "Std_%s_%s_Body.png" % (n, of), "1024x2048 이상(세로 2048+)", "필수" if used_of else "선택",
                         "의상 %s" % of, exists("Standing/%s/Std_%s_%s_Body" % (n, n, of))))
            for ex in EXPRS.values():
                need = face_use[(ch, of, ex)] > 0
                if ex == "Neutral" and of == "Casual":
                    need = True
                rows.append(("스탠딩 얼굴", "Std_%s_%s_Face_%s.png" % (n, of, ex), "바디와 같은 캔버스(투명)",
                             "필수" if need else "선택", "스크립트 사용 %d회(이 의상 %d회)" % (expr_use[ch][ex], face_use[(ch, of, ex)]),
                             exists("Standing/%s/Std_%s_%s_Face_%s" % (n, n, of, ex))))

    char_cgs = sorted(k for k in cg_use if re.match(r"^(%s)_E[1-6]$" % "|".join(c.upper() for c in CHARS), k))
    for ch in CHARS:
        for i in range(1, 7):
            key = "%s_E%d" % (ch.upper(), i)
            files = cg_use.get(key, set())
            rows.append(("이벤트 CG", "CG_%s_E%d.png" % (pascal(ch), i), "1920x1080",
                         "필수" if files else "선택", ", ".join(sorted(files))[:70] or "스크립트 미사용",
                         exists("CG/%s/CG_%s_E%d" % (pascal(ch), pascal(ch), i))))
    for key in sorted(k for k in cg_use if k not in char_cgs):
        rows.append(("공통 CG", "CG_%s.png" % key, "1920x1080", "필수", ", ".join(sorted(cg_use[key]))[:70],
                     exists("CG/Common/CG_%s" % key)))

    places = ["Cafe_Interior", "Cafe_Exterior", "Street", "Lighthouse", "FishMarket", "NightMarket", "Dojo", "LiveClub",
              "Greenhouse", "BookStreet", "Beach", "Rooftop"]
    for p in places:
        for t in ["Day", "Evening", "Night"]:
            files = bg_use.get((p, t), set())
            rows.append(("배경", "BG_%s_%s.png" % (p, t), "1920x1080", "필수" if files else "선택",
                         ", ".join(sorted(files))[:70] or "스크립트 미사용", exists("BG/BG_%s_%s" % (p, t))))

    for i, mid in enumerate(MENU_IDS):
        rows.append(("메뉴 아이콘", "ICON_Menu_%s.png" % mid, "512x512(투명)", "필수" if i < 12 else "선택",
                     "초기 메뉴" if i < 12 else "챕터 진행 해금", exists("Icons/Menu/ICON_Menu_%s" % mid)))
    rows.append(("UI", "Title 배경(BG_Street_Night.png 사용)", "1920x1080", "필수", "타이틀", exists("BG/BG_Street_Night")))
    rows.append(("UI", "앱 아이콘 / 가게 아이콘", "1024, 512, 256", "필수", "스토어·앱", False))

    os.makedirs("docs", exist_ok=True)
    with open("docs/art_checklist.csv", "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["분류", "파일명", "규격", "우선순위", "사용처", "저장소에 PNG 있음(플레이스홀더 포함)", "제작 완료(직접 체크)"])
        for r in rows:
            w.writerow([r[0], r[1], r[2], r[3], r[4], "Y" if r[5] else "N", ""])

    must = collections.Counter(r[0] for r in rows if r[3] == "필수")
    total = collections.Counter(r[0] for r in rows)
    lines = ["# 일러스트 제작 체크리스트 (자동 생성)", "",
             "> `python3 tools/art_checklist.py` 로 시나리오에서 실제 사용하는 에셋을 다시 계산합니다. 같은 파일명으로 PNG 를 덮어쓰면 코드 수정 없이 반영됩니다.",
             "> 전체 목록과 진행 체크는 `docs/art_checklist.csv`(엑셀로 열기)에서 관리하세요.", "",
             "## 1. 최소 제작 범위(필수) 요약", "", "| 분류 | 필수 | 전체 |", "|---|---|---|"]
    for cat in ["스탠딩 바디", "스탠딩 얼굴", "이벤트 CG", "공통 CG", "배경", "메뉴 아이콘", "UI"]:
        lines.append("| %s | %d | %d |" % (cat, must[cat], total[cat]))
    lines.append("| **합계** | **%d** | **%d** |" % (sum(must.values()), sum(total.values())))
    lines += ["", "필수 = 스크립트가 실제로 호출하는 것 + 첫 실행에 반드시 필요한 것(기본 의상 Casual의 Neutral 표정, 초기 메뉴 12종, 타이틀 배경).",
              "선택 = 아직 스크립트가 쓰지 않는 의상·표정·배경·CG. 나중에 콘텐츠가 늘면 필수가 됩니다.", "",
              "## 2. 캐릭터별 스탠딩 필수 표정", "",
              "의상은 시나리오가 거의 지정하지 않으므로 게임의 `OutfitPolicy` 가 정한다(데이트 노드=Date, 직원+카페 낮=Uniform, 그 외 Casual, 명시한 의상이 항상 우선).",
              "이 표의 의상 횟수는 같은 규칙으로 센 값이다(괄호 = 그중 정책이 정한 횟수). 규칙을 바꾸면 `Game/OutfitPolicy.cs` 와 `tools/art_checklist.py` 의 `resolve_outfit` 을 함께 고친다.", "",
              "| 캐릭터 | 사용 표정(횟수) | 사용 의상(정책 결정 횟수) |", "|---|---|---|"]
    for ch in CHARS:
        ex = ", ".join("%s %d" % (k, v) for k, v in sorted(expr_use[ch].items(), key=lambda x: -x[1])) or "-"
        of = ", ".join("%s %d (정책 %d)" % (k, outfit_use[ch][k], policy_use[ch][k]) for k in OUTFITS if outfit_use[ch][k] > 0) or "Casual(기본)"
        lines.append("| %s | %s | %s |" % (NAMES_KO[ch], ex, of))
    lines += ["", "## 3. 이벤트 CG (스크립트 사용 현황)", "", "| CG | 파일명 | 사용 스크립트 |", "|---|---|---|"]
    for r in rows:
        if r[0] in ("이벤트 CG", "공통 CG") and r[3] == "필수":
            lines.append("| %s | `%s` | %s |" % (r[0], r[1], r[4]))
    lines += ["", "## 4. 제작 요청서(장면 지문) 위치", "",
              "각 CG 의 구도·조명·감정·소품·대사 위치는 `docs/art_requests/<캐릭터>.md`, 공통 CG 는 `docs/art_requests/Common_*.md` 에 있습니다.",
              "", "## 5. 규격 요약", "",
              "| 종류 | 규격 | 비고 |", "|---|---|---|",
              "| 스탠딩 | 투명 PNG, 세로 2048px+, 비율 1:2 권장(1024x2048) | 바디와 얼굴은 **같은 캔버스**, 얼굴은 눈·눈썹·입만 투명 배경에 |",
              "| 이벤트 CG | 1920x1080 | 호스트가 전체 화면에 맞춤. 중앙 안전 영역에 인물 배치 |",
              "| 배경 | 1920x1080 | 낮/저녁/밤 변형은 같은 구도에 조명·색만 변경 |",
              "| 메뉴 아이콘 | 512x512 투명 | 40종 |",
              "| 색감 | 전 에셋 동일 LUT/팔레트 | `02_Art_Plan.md` §5 |", ""]
    open("docs/13_Art_Checklist.md", "w", encoding="utf-8").write("\n".join(lines))
    print("필수 %d / 전체 %d" % (sum(must.values()), sum(total.values())))
    for cat in ["스탠딩 바디", "스탠딩 얼굴", "이벤트 CG", "공통 CG", "배경", "메뉴 아이콘", "UI"]:
        print("  %s: 필수 %d / 전체 %d" % (cat, must[cat], total[cat]))


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        sys.exit(selftest())
    main()
