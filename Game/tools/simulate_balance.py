#!/usr/bin/env python3
"""
모피 하우스 밸런스 시뮬레이터 (표준 라이브러리만 사용)

balance/*.csv 를 읽어 8챕터 완주를 몬테카를로로 시뮬레이션한다. C# 게임(DayCycle + RomanceService + NightRomanceActions + SceneGate)과
같은 규칙을 따르며, 기준 플레이어는 `tools/csharp_check/qa_hunt` 의 'focus' 봇이다(QA-04):
  - 밤 행동은 전부 연애 행동이다(타이쿤 '관리 행동' 비율 romance_share 는 C# 에 대응물이 없어 삭제). 피로 80+ 이면 행동 1회, 그 밤은 무료 휴식 1회.
  - 목표(들)의 개인 이벤트가 열려 있으면 항상 가장 먼저 본다(이벤트 일정 event_schedule.csv + 장면 게이트 scene_rules.csv 반영).
  - 나머지는 집중도 focus 확률로 목표, 아니면 다른(이미 등장한·동결되지 않은) 인물에게. 데이트 > 선물 > 대화, 선물·스팟은 prefs.csv 의 '좋아함'만 고른다.
  - 대화 = actions.csv talk(서비스 기본값) + 대화 노드의 선택지 보상 2회(정답 +5/+1.5, 오답 -1/-1, 무난 0; 무작위 선택 확률은 params.csv talk_*). QA-05.
  - 루트 확정은 7장 첫 밤부터 조건(호감 60 · 신뢰 20)을 만족하는 밤에 한다. 확정 뒤 동결된 인물에게는 행동을 쓰지 않는다.
  - characters.csv first_chapter 이전의 인물에게는 행동할 수 없다(QA-03), staff_join_chapter > 0 인 인물만 직원 보너스(QA-14).
숙련 플레이어(선택지 정답률 = difficulty.csv choice_success_prob)는 `--skill params` 로 본다.

사용법:
    python3 tools/simulate_balance.py            # 기본 리포트 출력
    python3 tools/simulate_balance.py --runs 500 --md docs/03_balance_report.md
    python3 tools/simulate_balance.py --group 2  # 공동(다자) 루트 실험(K=2,3) 섹션을 normal 리포트 끝에 추가
"""
from __future__ import annotations

import argparse
import csv
import random
import statistics
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Set, Tuple

ROOT = Path(__file__).resolve().parent.parent
BAL = ROOT / "balance"

CHAR_IDS = ["howl", "johan", "beomjun", "haru", "gomseok", "nabi"]
LIKED_SPOT_MULT = 1.25      # RomanceRules.LikedSpotMultiplier
DISLIKED_SPOT_MULT = 0.75   # RomanceRules.DislikedSpotMultiplier
LOCK_MIN_CHAPTER = 7        # RomanceRules.LockMinChapter / LockMinAff / LockMinTrust
LOCK_MIN_AFF = 60.0
LOCK_MIN_TRUST = 20.0
SLOT_ORDER = {"morning": 0, "night": 1, "night_end": 2}
NO_EXPIRY = 99


def read_csv(name: str) -> List[Dict[str, str]]:
    with open(BAL / name, encoding="utf-8") as f:
        return list(csv.DictReader(f))


def split_list(text: str) -> List[str]:
    return [t.strip() for t in (text or "").split(";") if t.strip()]


@dataclass
class SceneRule:
    node: str
    kind: str
    chapter: int
    slot: str
    auto: bool
    tmin: int
    tmax: int
    min_day: int
    after: List[Tuple[str, bool]]  # (node, soft)
    before: List[str]

    @property
    def effective_min_day(self) -> int:
        return max(self.min_day, self.tmin) if self.auto else self.min_day


@dataclass
class Data:
    chapters: List[Dict[str, float]]
    actions: Dict[str, Dict[str, float]]
    params: Dict[str, float]
    bands: List[Dict[str, float]]
    events: List[Dict[str, float]]
    # balance/event_schedule.csv(선택): (인물, 단계) -> (chapter_min, chapter_max). 행이 없으면 events.csv 의 chapter_min, 만료 없음.
    schedule: Dict[tuple, tuple] = field(default_factory=dict)
    # balance/characters.csv: 인물 -> (first_chapter, staff_join_chapter). QA-03/QA-14: 코드가 아니라 데이터가 단일 출처.
    first_chapter: Dict[str, int] = field(default_factory=dict)
    gain_mult: Dict[str, float] = field(default_factory=dict)  # characters.csv gain_mult: 늦게 등장하는 인물의 만회 배율
    staff_join: Dict[str, int] = field(default_factory=dict)
    # balance/scene_rules.csv (SceneGate 와 같은 규칙) / prefs.csv / spots.csv / endings.csv
    rules: List[SceneRule] = field(default_factory=list)
    liked_spots: Dict[str, Set[str]] = field(default_factory=dict)
    spot_unlock: Dict[str, int] = field(default_factory=dict)
    spot_pref: Dict[Tuple[str, str], str] = field(default_factory=dict)
    endings: Dict[str, Dict[str, float]] = field(default_factory=dict)

    def event_window(self, char: str, step: int, default_min: float) -> tuple:
        row = self.schedule.get((char, step))
        if row is None:
            return default_min, NO_EXPIRY
        return (row[0] if row[0] > 0 else default_min), row[1]

    def spot_multiplier(self, char: str, chapter: int) -> float:
        """NightRomanceActions/qa_hunt 와 같은 선택: 해금된 '좋아함' 스팟이 있으면 그중 하나(x1.25), 없으면 해금된 첫 스팟(정렬: 해금 챕터, id)."""
        liked = [s for s in self.liked_spots.get(char, set()) if self.spot_unlock.get(s, 99) <= chapter]
        if liked:
            return LIKED_SPOT_MULT
        open_spots = sorted((u, sid) for sid, u in self.spot_unlock.items() if u <= chapter)
        pref = self.spot_pref.get((char, open_spots[0][1]), "neutral") if open_spots else "neutral"
        return LIKED_SPOT_MULT if pref == "like" else (DISLIKED_SPOT_MULT if pref == "dislike" else 1.0)

    @staticmethod
    def load(difficulty: str = "normal") -> "Data":
        def num(row: Dict[str, str]) -> Dict[str, float]:
            out: Dict[str, float] = {}
            for k, v in row.items():
                try:
                    out[k] = float(v)
                except ValueError:
                    out[k] = v  # type: ignore[assignment]
            return out

        params = {r["key"]: float(r["value"]) for r in read_csv("params.csv")}
        for r in read_csv("difficulty.csv"):  # 난이도별 덮어쓰기
            params[r["param"]] = float(r[difficulty])
        schedule: Dict[tuple, tuple] = {}
        if (BAL / "event_schedule.csv").exists():
            for r in read_csv("event_schedule.csv"):
                if not r["char"].strip() or not r["step"].strip():
                    continue
                cmin = int(float(r["chapter_min"] or 0))
                cmax = int(float(r["chapter_max"] or 0))
                schedule[(r["char"].strip(), int(float(r["step"])))] = (cmin, cmax if cmax > 0 else NO_EXPIRY)
        first = {}
        staff = {}
        gmul = {}
        for r in read_csv("characters.csv"):
            first[r["id"]] = max(1, int(float(r["first_chapter"] or 1)))
            staff[r["id"]] = int(float(r["staff_join_chapter"] or 0))
            gmul[r["id"]] = float(r.get("gain_mult") or 1.0)
        rules: List[SceneRule] = []
        if (BAL / "scene_rules.csv").exists():
            for r in read_csv("scene_rules.csv"):
                if not r["node"].strip():
                    continue
                after = []
                for a in split_list(r["after_node"]):
                    soft = a.startswith("~")
                    after.append((a.lstrip("~").strip(), soft))
                rules.append(SceneRule(
                    node=r["node"].strip(), kind=r["kind"].strip(), chapter=int(float(r["chapter"] or 0)),
                    slot=(r["slot"] or "morning").strip().lower().replace("nightend", "night_end") or "morning",
                    auto=(r["auto_play"] or "0").strip().lower() in ("1", "true", "yes"),
                    tmin=int(float(r["trigger_day_min"] or 0)), tmax=int(float(r["trigger_day_max"] or 0)),
                    min_day=int(float(r["min_day_in_chapter"] or 0)), after=after, before=split_list(r["before_node"])))
        liked_spots: Dict[str, Set[str]] = {}
        spot_pref: Dict[Tuple[str, str], str] = {}
        for r in read_csv("prefs.csv"):
            if r["kind"] == "spot":
                spot_pref[(r["char"], r["tag"])] = r["value"]
                if r["value"] == "like":
                    liked_spots.setdefault(r["char"], set()).add(r["tag"])
        spot_unlock = {r["id"]: int(float(r["unlock_chapter"])) for r in read_csv("spots.csv")}
        endings = {r["ending"]: {"aff": float(r["aff_min"]), "trust": float(r["trust_min"]), "events": float(r["required_events"])}
                   for r in read_csv("endings.csv")}
        return Data(
            schedule=schedule,
            chapters=[num(r) for r in read_csv("chapters.csv")],
            actions={r["action"]: num(r) for r in read_csv("actions.csv")},
            params=params,
            bands=[num(r) for r in read_csv("staff_bands.csv")],
            events=[num(r) for r in read_csv("events.csv")],
            first_chapter=first, gain_mult=gmul, staff_join=staff, rules=rules,
            liked_spots=liked_spots, spot_unlock=spot_unlock, spot_pref=spot_pref, endings=endings,
        )


@dataclass
class CharState:
    aff: float = 0.0
    trust: float = 0.0
    events_done: int = 0
    last_gift_day: int = -99
    last_date_day: int = -99
    last_contact_day: int = 0


@dataclass
class RunResult:
    minutes: float
    chapter_minutes: List[float]
    chapter_revenue_avg: List[float]
    final_level: int
    final_rep: float
    fatigue_limited_ratio: float
    target_aff: float
    target_trust: float
    target_events: int
    ending: str
    other_max_aff: float
    # 공동 루트 실험(--group)에서만 의미가 있다. 기본 모드는 단독 목표이므로 목표 1명의 값과 같다.
    group_ok: bool = False
    group_min_aff: float = 0.0
    group_min_trust: float = 0.0
    group_min_events: int = 0
    lock_day: int = -1
    actions_on_target: float = 0.0


def band(d: Data, aff: float) -> Dict[str, float]:
    for b in d.bands:
        if b["aff_min"] <= aff <= b["aff_max"]:
            return b
    return d.bands[-1]


def harem_thresholds(k: int, rows=None) -> Dict[str, float]:
    """balance/endings.csv 의 harem_duo(k=2) / harem_trio(k=3) 행."""
    row = next(r for r in (rows or read_csv("endings.csv")) if r["ending"] == ("harem_duo" if k == 2 else "harem_trio"))
    return {"aff": float(row["aff_min"]), "trust": float(row["trust_min"]), "events": float(row["required_events"])}


def group_success(d: Data, states: List["CharState"]) -> bool:
    """공동 루트 성공: 구성원 전원이 harem 기준(호감·신뢰·이벤트)을 충족. 동의 플래그(harem_ok_*)는 항상 획득한다고 가정(상한 추정)."""
    t = d.endings["harem_duo" if len(states) == 2 else "harem_trio"]
    return all(c.aff >= t["aff"] and c.trust >= t["trust"] and c.events_done >= t["events"] for c in states)


class SceneState:
    """SceneGate 의 파이썬 이식: 방문 기록 + 챕터/일차 + after/before/soft 규칙으로 개인 이벤트 가능 여부와 공통 장면 자동 재생을 판단한다."""

    def __init__(self, d: Data):
        self.rules = {r.node: r for r in d.rules}
        self.order = d.rules
        self.visited: Set[str] = set()
        self.chapter = 1
        self.day = 1

    def deadline_passed(self, r: SceneRule) -> bool:
        if not r.auto or r.tmax < 1 or r.chapter < 1:
            return False
        return self.chapter > r.chapter or (self.chapter == r.chapter and self.day >= r.tmax)

    def overdue(self, r: SceneRule) -> bool:
        if not r.auto or r.chapter < 1:
            return False
        if self.chapter > r.chapter:
            return True
        return self.chapter == r.chapter and r.tmax > 0 and self.day > r.tmax

    def allowed(self, node: str) -> bool:
        r = self.rules.get(node)
        if r is None:
            return True
        if r.chapter > 0:
            if self.chapter < r.chapter:
                return False
            md = r.effective_min_day
            if self.chapter == r.chapter and md > 0 and self.day < md:
                return False
        deadline = self.deadline_passed(r)
        for n, soft in r.after:
            if n in self.visited or n not in self.rules and not self._is_event_or_known(n):
                continue
            if soft and deadline:
                continue
            return False
        for b in r.before:
            if b in self.visited:
                return False
        return True

    def _is_event_or_known(self, node: str) -> bool:
        return True  # 모든 선행 노드는 프로젝트에 존재한다고 본다(Ch1_Outro 같은 없는 노드는 규칙의 선행 조건에 쓰이지 않는다)

    def play_scenes(self, slot: str) -> None:
        guard = 0
        while guard < 40:
            guard += 1
            nxt = None
            for r in self.order:
                if not r.auto or r.node in self.visited:
                    continue
                if not self.overdue(r) and SLOT_ORDER[r.slot] > SLOT_ORDER[slot]:
                    continue
                if not self.allowed(r.node):
                    continue
                nxt = r
                break
            if nxt is None:
                return
            self.visited.add(nxt.node)

    def enter_chapter_day(self, chapter: int, day: int) -> None:
        self.chapter, self.day = chapter, day
        if day == 1:  # 챕터 마무리/도입 노드는 첫날 아침에 모두 재생된다
            for r in self.order:
                if r.kind == "chapter" and r.chapter == chapter:
                    self.visited.add(r.node)


def simulate(d: Data, focus: float, rng: random.Random, target: str = "howl",
             targets: Optional[List[str]] = None, skill: str = "random") -> RunResult:
    """targets 가 None 이면 단독 루트(target 1명). targets 가 2~3명이면 공동 루트: focus 를 구성원에게 균등 분배하고 비구성원만 동결한다.
    skill: 'random' = qa_hunt 봇처럼 대화 선택지를 무작위로 고른다(기본, params.csv talk_*), 'params' = 정답률 choice_success_prob 인 숙련 플레이어."""
    group = targets is not None and len(targets) > 1
    members = list(targets) if targets else [target]
    target = members[0]
    p = d.params
    A = d.actions
    chars = {c: CharState() for c in CHAR_IDS}
    scenes = SceneState(d)
    gold = p["start_gold"]
    level = 1
    rep = 20.0
    fatigue = 10.0
    day_global = 0
    minutes = 0.0
    chapter_minutes: List[float] = []
    chapter_revenue: List[float] = []
    limited_days = 0
    total_days = 0
    locked = False
    lock_day = -1
    prev_menus = 0
    target_actions = 0
    soft = p["soft_diminish"]
    free_rest = -A["rest"]["fatigue"]

    talk_points = int(p["talk_choice_points"])
    if skill == "params":
        right_p = p["choice_success_prob"]
        wrong_p = (1.0 - right_p) * p["talk_wrong_prob"] / max(1e-9, 1.0 - p["talk_right_prob"])
    else:
        right_p, wrong_p = p["talk_right_prob"], p["talk_wrong_prob"]

    gmult = p["romance_gain_mult"]  # 난이도 배율: 양수 증가분(서비스·대사 보상 공통)

    def dim(gain: float, aff: float) -> float:
        return gain * (1.0 - soft * aff / 100.0) if gain > 0 else gain

    def apply(st: CharState, ch: Dict[str, float], aff_gain: float, trust_gain: float, diminish: bool = True, who: str = "") -> None:
        m = gmult * d.gain_mult.get(who, 1.0)
        if locked and len(members) >= 2 and who in members:  # 공동 루트 확정 뒤 구성원 증가 배율
            m *= p["group_gain_mult_2"] if len(members) == 2 else p["group_gain_mult_3"]
        if aff_gain > 0:
            aff_gain *= m
        if trust_gain > 0:
            trust_gain *= m
        a = dim(aff_gain, st.aff) if diminish else aff_gain
        st.aff = max(0.0, min(ch["aff_cap"], st.aff + a))
        st.trust = max(0.0, min(ch["trust_cap"], st.trust + trust_gain))

    def available(c: str, ch_no: int) -> bool:
        return ch_no >= d.first_chapter.get(c, 1)

    def event_open(c: str, ch_no: int) -> Optional[Dict[str, float]]:
        st = chars[c]
        if (locked and c not in members) or not available(c, ch_no):
            return None
        for e in d.events:
            step = int(e["step"])
            if step != st.events_done + 1:
                continue
            ev_min, ev_max = d.event_window(c, step, e["chapter_min"])
            if ch_no > ev_max:
                return None
            if not (ch_no >= ev_min and st.aff >= e["aff_min"] and st.trust >= e["trust_min"]):
                return None
            if not scenes.allowed(c.capitalize() + "_E" + str(step)):
                return None
            return e
        return None

    for ch in d.chapters:
        ch_no = int(ch["chapter"])
        ch_min_start = minutes
        revenues: List[float] = []
        staff_count = ch["staff_count"]

        # 챕터 고정 시간: 메인 스토리 + 공통 이벤트 + 신규 메뉴 개발
        minutes += ch["main_story_min"] + ch["common_events"] * p["common_event_min"]
        new_menus = int(ch["menu_count"]) - (prev_menus if prev_menus else 12)
        minutes += max(0, new_menus) * p["menu_dev_min"]
        prev_menus = int(ch["menu_count"])

        for day_in_ch in range(1, int(ch["days"]) + 1):
            day_global += 1
            total_days += 1
            scenes.enter_chapter_day(ch_no, day_in_ch)

            # --- 아침 --- (DayCycle.StartDay: 피로 회복 후 방치 감쇠)
            fatigue = max(0.0, fatigue - p["fatigue_daily_recover"])
            for c in CHAR_IDS:
                cs = chars[c]
                if locked and c not in members:
                    continue
                if day_global - cs.last_contact_day > p["decay_grace_days"]:
                    cs.aff = max(0.0, cs.aff - p["decay_per_day"])
            minutes += p["min_prep"]
            scenes.play_scenes("morning")

            # --- 아침: 상점 확장(가능하면 바로) ---
            while level < ch["level_cap"]:
                lvl_cost = p["level_cost_base"] * (p["level_cost_growth"] ** (level - 1))
                if gold < lvl_cost:
                    break
                gold -= lvl_cost
                level += 1

            # --- 낮: 영업 (BusinessSimulator) ---
            seats = p["seat_base"] + p["seat_per_level"] * level
            turns = p["turns_base"] + p["turns_per_level"] * level
            q_bonus = 0.0
            s_bonus = 0.0
            for c, join in d.staff_join.items():
                if join > 0 and ch_no >= join:
                    b = band(d, chars[c].aff)
                    q_bonus += b["quality_bonus"]
                    s_bonus += b["speed_bonus"]
            capacity = seats * turns * (1 + s_bonus)
            demand = p["demand_base"] + p["demand_per_level"] * level + p["demand_per_rep"] * rep
            if ch_no == 7:
                penalty = p["chain_demand_penalty"] * (1.0 if rep < 60 else 0.5)
                demand *= 1 - penalty
                if locked:  # 루트 확정 후 도움 인물 보너스(확정 전에는 없음, QA-14)
                    capacity *= 1 + p["helper_bonus"]
            served = min(capacity, demand)
            price = p["price_base"] + p["price_per_menu"] * ch["menu_count"]
            sat = min(1.0, max(0.3, p["sat_base"] + p["prep_bonus"] + q_bonus + rng.gauss(0, p["sat_noise"])))
            gross = served * price * (0.6 + 0.4 * sat) * (1.0 / 0.96) * p["rev_mult"]  # 만족도 0.96 기준 정규화
            ingredient = gross * p["ingredient_cost_rate"]
            upkeep = p["upkeep_per_level"] * level
            net = gross - ingredient - upkeep
            revenues.append(gross)
            minutes += p["min_business"]
            event_rep = 0.0
            if rng.random() < p["random_event_prob"]:
                minutes += p["random_event_min"]
                if rng.random() < p["rep_event_prob"] / p["random_event_prob"]:
                    event_rep = rng.choice([-1, 1, 1]) * p["rep_event_amp"]
            rep_delta = max(-3.0, min(3.0, (sat - p["rep_sat_center"]) * 18)) + event_rep
            fatigue = min(100.0, fatigue + max(8.0, p["fatigue_business_base"] - p["fatigue_per_staff"] * staff_count))

            # --- 밤 ---
            limited = fatigue >= p["fatigue_limit"]
            actions_left = 1 if limited else max(1, int(p["actions_per_night"]))
            if limited:
                limited_days += 1
            scenes.play_scenes("night")

            # 루트 확정(7장부터, 대상 전원이 조건을 만족하는 밤)
            lock_aff = p["group_lock_min_aff"] if group else LOCK_MIN_AFF
            lock_trust = p["group_lock_min_trust"] if group else LOCK_MIN_TRUST
            if not locked and ch_no >= LOCK_MIN_CHAPTER and all(
                    chars[m].aff >= lock_aff and chars[m].trust >= lock_trust for m in members):
                locked = True
                lock_day = day_global

            if limited:
                # 피로 제한 밤의 무료 휴식(밤 1회, 행동을 쓰지 않는다): 봇은 항상 먼저 쉰다
                fatigue = max(0.0, fatigue - free_rest)
                minutes += A["rest"]["minutes"]

            while actions_left > 0:
                actions_left -= 1

                # 1) 목표들의 개인 이벤트가 열려 있으면 가장 먼저(이벤트가 적은 인물부터)
                done_event = False
                for m in sorted(members, key=lambda c: chars[c].events_done):
                    e = event_open(m, ch_no)
                    if e is not None:
                        st = chars[m]
                        st.last_contact_day = day_global
                        st.events_done += 1
                        step = int(e["step"])
                        apply(st, ch, e["aff_gain"], e["trust_gain"], diminish=False, who=m)
                        apply(st, ch, e.get("script_aff", 0.0), e.get("script_trust", 0.0), diminish=True, who=m)
                        scenes.visited.add(m.capitalize() + "_E" + str(step))
                        minutes += e["minutes"]
                        fatigue += A["event"]["fatigue"]
                        target_actions += 1
                        done_event = True
                        break
                if done_event:
                    continue

                # 2) 대상 고르기: 집중도 확률로 목표(구성원), 아니면 다른 사람(등장했고 동결되지 않은 인물)
                tgt_active = [m for m in members if available(m, ch_no) and not (locked and m not in members)]
                who = None
                if rng.random() < focus and tgt_active:
                    who = rng.choice(tgt_active) if group else tgt_active[0]
                else:
                    pool = [c for c in CHAR_IDS if c not in members and available(c, ch_no) and not (locked and c not in members)]
                    if not pool:
                        pool = tgt_active
                    if not pool:
                        pool = [c for c in CHAR_IDS if available(c, ch_no)]
                    who = rng.choice(pool)
                    if who not in members:
                        e = event_open(who, ch_no)
                        if e is not None:
                            st = chars[who]
                            st.last_contact_day = day_global
                            st.events_done += 1
                            apply(st, ch, e["aff_gain"], e["trust_gain"], diminish=False, who=who)
                            apply(st, ch, e.get("script_aff", 0.0), e.get("script_trust", 0.0), diminish=True, who=who)
                            scenes.visited.add(who.capitalize() + "_E" + str(int(e["step"])))
                            minutes += e["minutes"]
                            fatigue += A["event"]["fatigue"]
                            continue

                def do_date(c: str) -> None:
                    nonlocal gold, fatigue, minutes, target_actions
                    st = chars[c]
                    st.last_contact_day = day_global
                    st.last_date_day = day_global
                    gold -= p["date_cost_base"] + p["date_cost_per_chapter"] * ch_no
                    apply(st, ch, A["date"]["aff_gain"] * d.spot_multiplier(c, ch_no), A["date"]["trust_gain"], who=c)
                    minutes += A["date"]["minutes"]
                    fatigue += A["date"]["fatigue"]
                    if c in members:
                        target_actions += 1

                def do_gift(c: str) -> None:
                    nonlocal gold, fatigue, minutes, target_actions
                    st = chars[c]
                    st.last_contact_day = day_global
                    st.last_gift_day = day_global
                    gold -= p["gift_cost_base"] + p["gift_cost_per_chapter"] * ch_no
                    apply(st, ch, A["gift_like"]["aff_gain"], A["gift_like"]["trust_gain"], who=c)
                    minutes += A["gift_like"]["minutes"]
                    fatigue += A["gift_like"]["fatigue"]
                    if c in members:
                        target_actions += 1

                def do_talk(c: str) -> None:
                    nonlocal fatigue, minutes, target_actions
                    st = chars[c]
                    st.last_contact_day = day_global
                    apply(st, ch, A["talk"]["aff_gain"], A["talk"]["trust_gain"], who=c)
                    minutes += A["talk"]["minutes"]
                    fatigue += A["talk"]["fatigue"]
                    for _ in range(talk_points):  # 대화 노드의 선택지(<<aff>>/<<trust>> 는 AdjustAffection: 호감 둔화, 신뢰는 캡만)
                        r = rng.random()
                        if r < right_p:
                            apply(st, ch, A["choice_success"]["aff_gain"], A["choice_success"]["trust_gain"], who=c)
                        elif r < right_p + wrong_p:
                            apply(st, ch, A["choice_fail"]["aff_gain"], A["choice_fail"]["trust_gain"], who=c)
                        minutes += A["choice_success"]["minutes"]
                    if c in members:
                        target_actions += 1

                st = chars[who]
                date_ready = day_global - st.last_date_day >= A["date"]["cooldown_days"]
                gift_ready = day_global - st.last_gift_day >= A["gift_like"]["cooldown_days"]
                date_cost = p["date_cost_base"] + p["date_cost_per_chapter"] * ch_no
                gift_cost = p["gift_cost_base"] + p["gift_cost_per_chapter"] * ch_no
                if date_ready and gold >= date_cost and rng.random() < 0.8:
                    do_date(who)
                elif gift_ready and gold >= gift_cost:
                    do_gift(who)
                else:
                    done = False
                    for m in tgt_active:  # 다른 목표에게 준비된 데이트/선물이 있으면 그쪽으로
                        sm = chars[m]
                        if day_global - sm.last_date_day >= A["date"]["cooldown_days"] and gold >= date_cost:
                            do_date(m)
                            done = True
                            break
                        if day_global - sm.last_gift_day >= A["gift_like"]["cooldown_days"] and gold >= gift_cost:
                            do_gift(m)
                            done = True
                            break
                    if not done:
                        do_talk(who)

            scenes.play_scenes("night_end")
            minutes += p["min_settle"]
            # --- 정산 ---
            gold += net
            rep = max(0.0, min(100.0, rep + rep_delta))

        chapter_minutes.append(minutes - ch_min_start)
        chapter_revenue.append(statistics.mean(revenues))

    t = chars[target]
    others_pool = [c for c in CHAR_IDS if c not in members]
    others = max(chars[c].aff for c in others_pool)
    e_true, e_good, e_norm = d.endings["true"], d.endings["good"], d.endings["normal"]
    ledger_truth = rng.random() < p["ledger_truth_prob"]  # Ch6_LedgerTruth 가 설정(공통 장면)
    key_flag = rng.random() < p["key_flag_prob"]          # 캐릭터 고유 열쇠: 무작위 선택 봇이 얻는 비율
    if not locked:
        ending = "community"
    elif t.aff >= e_true["aff"] and t.trust >= e_true["trust"] and t.events_done >= e_true["events"] and ledger_truth and key_flag:
        ending = "true"
    elif t.aff >= e_good["aff"] and t.trust >= e_good["trust"] and t.events_done >= e_good["events"]:
        ending = "good"
    elif t.aff >= e_norm["aff"] and t.trust >= e_norm["trust"] and t.events_done >= e_norm["events"]:
        ending = "normal"
    else:
        ending = "community"
    return RunResult(
        minutes=minutes,
        chapter_minutes=chapter_minutes,
        chapter_revenue_avg=chapter_revenue,
        final_level=level,
        final_rep=rep,
        fatigue_limited_ratio=limited_days / max(1, total_days),
        target_aff=t.aff,
        target_trust=t.trust,
        target_events=t.events_done,
        ending=ending,
        other_max_aff=others,
        group_ok=(locked and group_success(d, [chars[c] for c in members])) if group else ending != "community",
        group_min_aff=min(chars[c].aff for c in members),
        group_min_trust=min(chars[c].trust for c in members),
        group_min_events=min(chars[c].events_done for c in members),
        lock_day=lock_day,
        actions_on_target=target_actions,
    )


FOCUS_STEPS = [0.05, 0.1, 0.2, 0.35, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0]


def report(runs: int, seed: int, difficulty: str = "normal", skill: str = "random") -> str:
    d = Data.load(difficulty)
    rng = random.Random(seed)
    lines: List[str] = []
    lines.append(f"# 밸런스 시뮬레이션 리포트 — 난이도: {difficulty}\n")
    lines.append(f"- 실행: `python3 tools/simulate_balance.py --runs {runs} --seed {seed}" + (f" --skill {skill}`" if skill != "random" else "`"))
    lines.append(f"- 입력: `balance/*.csv` (몬테카를로 {runs}회/시나리오). 기준 플레이어 = qa_hunt 'focus' 봇"
                 + (" (대화 선택지 무작위)" if skill == "random" else f" (선택지 정답률 {d.params['choice_success_prob']:.2f})") + "\n")

    # 1) 기준 시나리오: focus 0.65
    base = [simulate(d, 0.65, rng, skill=skill) for _ in range(runs)]
    hours = [r.minutes / 60 for r in base]
    lines.append("## 1. 플레이타임 (집중도 65% 기준 시나리오)\n")
    lines.append(f"- 평균 **{statistics.mean(hours):.1f}h** (표준편차 {statistics.pstdev(hours):.2f}h, "
                 f"최소 {min(hours):.1f}h ~ 최대 {max(hours):.1f}h) → 목표 12~15h")
    lines.append("")
    lines.append("| 챕터 | 이름 | 일수 | 평균 소요(분) | 일 평균 매출(골드) | 목표 매출(시작→끝) | 목표 대비 |")
    lines.append("|---|---|---|---|---|---|---|")
    for i, ch in enumerate(d.chapters):
        cm = statistics.mean(r.chapter_minutes[i] for r in base)
        cr = statistics.mean(r.chapter_revenue_avg[i] for r in base)
        lo, hi = ch["target_rev_start"], ch["target_rev_end"]
        mid = (lo + hi) / 2
        lines.append(f"| {int(ch['chapter'])} | {ch['name']} | {int(ch['days'])} | {cm:.0f} | {cr:,.0f} | "
                     f"{int(lo):,} → {int(hi):,} | {cr / mid * 100 - 100:+.0f}% |")
    lines.append("")
    lines.append(f"- 최종 가게 레벨 평균 {statistics.mean(r.final_level for r in base):.1f} / 평판 평균 "
                 f"{statistics.mean(r.final_rep for r in base):.0f}")
    lines.append(f"- 피로도 {int(d.params['fatigue_limit'])}+ 로 행동 1회 제한된 밤 비율(봇은 휴식 없이 연애 행동만): "
                 f"{statistics.mean(r.fatigue_limited_ratio for r in base) * 100:.0f}%\n")

    # 2) 집중도별 엔딩
    lines.append("## 2. 루트 집중도별 엔딩 도달 확률\n")
    lines.append("집중도 = 밤 행동 중 목표 인물에게 쓰는 비율(목표의 개인 이벤트는 열리는 즉시 추가로 본다). 나머지는 이미 등장한 다른 인물에게 균등 분배, "
                 "루트 확정(7장 첫 밤~조건 충족) 뒤에는 동결된 인물에게 쓰지 않는다.\n")
    lines.append("| 집중도 | 목표 행동 수 | 평균 호감 | 평균 신뢰 | 이벤트 완료 | 노멀 | 굿 | 트루 | **굿 이상** | 커뮤니티 | 평균 시간(h) |")
    lines.append("|---|---|---|---|---|---|---|---|---|---|---|")
    for focus in FOCUS_STEPS:
        rs = [simulate(d, focus, rng, skill=skill) for _ in range(runs)]
        cnt = {k: sum(1 for r in rs if r.ending == k) / runs * 100 for k in ["normal", "good", "true", "community"]}
        lines.append(
            f"| {int(focus * 100)}% | {statistics.mean(r.actions_on_target for r in rs):.0f} | {statistics.mean(r.target_aff for r in rs):.0f} | "
            f"{statistics.mean(r.target_trust for r in rs):.0f} | {statistics.mean(r.target_events for r in rs):.1f}/5 | "
            f"{cnt['normal']:.0f}% | {cnt['good']:.0f}% | {cnt['true']:.0f}% | **{cnt['good'] + cnt['true']:.0f}%** | {cnt['community']:.0f}% | "
            f"{statistics.mean(r.minutes for r in rs) / 60:.1f} |"
        )
    lines.append("")
    lines.append("> 해석 가이드: Normal 에서 ‘굿 이상’이 집중도 20%에서 0%에 가깝고, 35%에서 35~45%, 50~60%에서 80% 이상이어야 "
                 "‘여러 명과 친해질 수 있되, 한 명을 선택해야 한다’는 설계 의도에 맞다(Story 는 더 쉽게, Hard 는 더 어렵게). "
                 f"트루는 굿의 부분집합(장부 진실 {d.params['ledger_truth_prob'] * 100:.0f}% · 고유 열쇠 {d.params['key_flag_prob'] * 100:.0f}% 가정: 무작위 선택 봇의 실측).")
    return "\n".join(lines) + "\n"


def group_report(runs: int, seed: int, k: int, difficulty: str = "normal", skill: str = "random") -> str:
    """--group K 전용: 앞 K명(CHAR_IDS[:K])을 공동 루트로 확정하는 실험. 성공 = harem 기준 전원 충족(동의 플래그 가정)."""
    d = Data.load(difficulty)
    rng = random.Random(seed + 1000 + k)
    t = harem_thresholds(k)
    members = CHAR_IDS[:k]
    lines: List[str] = []
    lines.append(f"## 공동 루트 실험 — {k}인 ({', '.join(members)}), 난이도 {difficulty}\n")
    lines.append(f"- 실행: `python3 tools/simulate_balance.py --runs {runs} --seed {seed} --group {k}`")
    lines.append(f"- 성공 기준(`balance/endings.csv` {'harem_duo' if k == 2 else 'harem_trio'}): 구성원 전원 "
                 f"호감 {t['aff']:.0f} / 신뢰 {t['trust']:.0f} / 이벤트 {t['events']:.0f}. "
                 "동의 이벤트 플래그(`harem_ok_<char>`)는 항상 획득한다고 가정(상한 추정).")
    lines.append(f"- 집중도 = 밤 행동 중 {k}명 구성원에게 쓰는 비율(구성원에게 균등 분배, 나머지는 비구성원에게 분배). 구성원 전원이 호감 60 · 신뢰 20 이상이 되는 밤에 루트를 확정한다.\n")
    lines.append("| 집중도 | 구성원 최저 호감 | 최저 신뢰 | 최저 이벤트 | 확정 비율 | 공동 엔딩 성공 | 커뮤니티 폴백 | (참고) 숙련 플레이어 성공 |")
    lines.append("|---|---|---|---|---|---|---|---|")
    for focus in [0.6, 0.7, 0.8, 0.9, 1.0]:
        rs = [simulate(d, focus, rng, targets=members, skill=skill) for _ in range(runs)]
        sk = [simulate(d, focus, rng, targets=members, skill="params") for _ in range(runs)]
        ok = sum(1 for r in rs if r.group_ok) / runs * 100
        oks = sum(1 for r in sk if r.group_ok) / runs * 100
        locked = sum(1 for r in rs if r.lock_day > 0) / runs * 100
        lines.append(
            f"| {int(focus * 100)}% | {statistics.mean(r.group_min_aff for r in rs):.0f} | "
            f"{statistics.mean(r.group_min_trust for r in rs):.0f} | {statistics.mean(r.group_min_events for r in rs):.1f}/5 | {locked:.0f}% | "
            f"**{ok:.0f}%** | {100 - ok:.0f}% | {oks:.0f}% |"
        )
    lines.append("")
    return "\n".join(lines) + "\n"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--runs", type=int, default=300)
    ap.add_argument("--seed", type=int, default=20261003)
    ap.add_argument("--md", type=str, default="")
    ap.add_argument("--skill", choices=["random", "params"], default="random",
                    help="대화 선택지 숙련도: random = 무작위(qa_hunt 봇, 기본), params = difficulty.csv choice_success_prob")
    ap.add_argument("--group", type=int, default=0, choices=[0, 2, 3],
                    help="공동(다자) 루트 실험 K=2,3: 앞 K명을 목표로 harem 기준 성공률을 리포트 끝에 추가 (기본 0=끔)")
    args = ap.parse_args()
    text = "\n".join(report(args.runs, args.seed, dif, args.skill) for dif in ["story", "normal", "hard"])
    if args.group:
        text += "\n" + group_report(args.runs, args.seed, args.group, skill=args.skill)
    print(text)
    if args.md:
        out = ROOT / args.md
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(text, encoding="utf-8")
        print(f"[saved] {out}")


if __name__ == "__main__":
    main()
