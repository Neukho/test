# 20. QA bug-hunt report (adversarial / property-based)

> Date 2026-10-03. Scope: pure-logic assemblies (Core/Data/Tycoon/Save/Romance/Dialogue/Game) + real `.yarn` content + balance CSVs + a read-only review of UI/Boot flow code.
> Nothing under `Scripts/**`, `Dialogue/**`, `balance/**` was edited by this pass. All tooling lives in `tools/csharp_check/qa_hunt/`.
> Unity is not installed: UI rendering, input, coroutines and Editor code were **not executed** (see §6).

## 0. How this was done

`tools/csharp_check/qa_hunt/` is a Mono/`mcs` harness (C# 7.3-safe) that links the real game sources and plays them like a host would:

| Mode | What it does |
|---|---|
| `content` | host-called node names for 6 chars/9 spots/25 gifts/8 chapters, repo-wide `#line:` uniqueness, **every choice path of every node** (856 paths) for end-reachability / script errors / speaker ids, ending nodes show the CG the evaluator promises, all 15 pairs + 20 triples through `Harem_Table`/`Ending_Harem2/3`, consent/epilogue nodes never show or name a non-member, `scene_rules.csv` self-consistency |
| `save` | 2 000 random valid states through `ToSave -> SaveService (HMAC) -> Load -> FromSave` (all fields incl. `LockedGroup`, legacy single lock, flags, visited), idempotence, hand-made v1 saves, validator limits |
| `ending` | `EndingEvaluator` vs an **independent oracle** typed from docs/11 (single grid 6x12x13x7x8 = 52k cases + 40k random group cases, exactly-at-threshold values, events>5, trust>cap, duplicate / 4-member / unknown groups), `EndingPlan`/`EndingFlow`/profile-gallery consistency, route-lock boundary through the real `DayCycle` (ch6 last night vs ch7 first night), freeze invariant through ch7-8 |
| `flags` | **engine-exact flag reachability**: runs every host-startable node under every choice path to a fixpoint (no regex); which flags/keys/endings can a real player ever get |
| `play` | headless bot plays full 64-day games through `DayCycle + RomanceService + NightRomanceActions + DialogueSession + ChapterFlow + SceneGate + EndingPlan` with the real scripts: 4 genders x 3 difficulties x {single(6) smart/random, duo, trio, none, idle, chaos}; invariants after every step (see §4); mid-run save/load at ~9 %/day (incl. after the lock) |
| `timing`, `dead`, `gain`, `fuzz`, `fatigue` | story-timing enforcement, "flag read before it can be set" scan, script-granted affection, parser/CSV/profile fuzz, fatigue-limit reachability |
| `sync_check.py` | are `Resources/Dialogue/*.txt` + `_index.txt` + `Resources/Balance` in sync with their sources? |

Run: `sh tools/csharp_check/qa_hunt/run.sh [content|save|ending|flags|gain|timing|fuzz|dead|play|fatigue|all]`.
Other agents were editing the tree during this pass, so results were produced on **snapshots** (`QA_ROOT=<copy>`; last full run 07:06 UTC: 396 games, 126 checks, 7 failures = exactly findings QA-03, 06, 13, 15 below). Re-run on the live tree before relying on any "open" item.
`QA_RUNS=n QA_SEED=n` scale the `play` matrix; `QA_FOCUS=1 QA_FS=0.05,0.1,0.2 QA_RUNS=30` produces the QA-04 table.

## 1. Summary

| Severity | Count | Open | Fixed in tree during this pass |
|---|---|---|---|
| Blocker | 1 | 0 | 1 (QA-01) |
| Major | 5 | 3 (QA-03, 04, 05) | 1 (QA-02) + QA-06 latent |
| Minor | 6 | 6 | 0 |
| Cosmetic | 4 | 4 | 0 |

"Confirmed" = reproduced by running code; "Read" = found by reading only.

### Top 8
1. **QA-01 Blocker (fixed)** host never started CE01-CE08 / `Ch5_Show*` / `Ch6_LedgerTruth` -> Howl's True ending was impossible, every other True needed 4 Gomseok events.
2. **QA-04 Major (open)** Good ending is reachable with ~5-20 % focus; docs/03 numbers do not describe the game (simulator vs host drift).
3. **QA-05 Major (open)** `RomanceService.Talk` rolls a random "choice" bonus *and* every Talk node grants its own aff (avg +2.3): talk is worth ~2x the model.
4. **QA-03 Major (open)** Haru (joins ch4) and Nabi (ch5) can be talked to / dated on day 1; `Haru_Talk_01` plays the uniformed pastry cook closing the kitchen in chapter 1.
5. **QA-02 Major (fixed)** `events.csv` is per step, scripts are per character: 14 of 30 personal events could fire before their story chapter.
6. **QA-06 Major-latent (FIXED 2026-10-03)** save HMAC covers the re-serialised current schema: any new field without a version bump makes every older save "Tampered"; a tampered primary is silently replaced by `.bak`.
7. **QA-07/08 Minor** Hard revenue is 25-45 % under `chapters.csv` targets from ch3; fatigue-limit mechanic is reachable (~9 % of nights) but toothless.
8. **QA-09 Minor** no CI guard for `Resources/Dialogue` / `Balance` copies (18 stale + 3 unindexed scripts seen mid-session; `_index.txt` had 0 entries at 06:20).

## 2. Findings - confirmed by running code

### QA-01 [Blocker, FIXED] Common-event scenes were never started by the host
- **Repro (pre-fix tree, ~06:35):** `sh tools/csharp_check/qa_hunt/run.sh flags`.
- **Expected:** every authored scene (`CE01..CE08`, `Ch5_Show`, `Ch5_Show_After`, `Ch6_LedgerTruth`) is reachable in a normal playthrough.
- **Actual:** no code path starts them (host entry points = `Ch<N>_Intro/Outro`, `NightRomanceActions` node builders, harem/ending nodes). 14 dead nodes. Consequences proven by the fixpoint: `drawer_key_clue` (set only in `CE06_OldPhoto`; `Ch6_Outro` retry needs `ce06_done`) never settable -> `howl_key_photo`/`howl_key` never settable -> **Howl True impossible**; `ledger_truth` only via `Gomseok_E4`, so a True ending with anyone else required Gomseok E1-E4 (bot without the Gomseok detour: `ledger_truth` in 0 of 3 runs for every non-Gomseok route); `ch6_fund_open/board`, `ce06_done` readers dead. docs/12 §5-1 ordering rules (CE02 after Johan E1, Ch5 order) could not be enforced because nothing scheduled them.
- **Root cause:** `Scripts/Game/ChapterFlow.cs`, `UI/GameScreen.cs` only knew chapter intro/outro (`ChapterFlow` handled `Outro -> Intro -> info card` only).
- **Status:** the other agent added `balance/scene_rules.csv`, `Game/SceneGate.cs`, `Game/DayFlowDriver.cs`, `NightRomanceActions.NextCommonScene`, and `GameScreen` calls `PlayNextCommon(Morning|Night|NightEnd)` (checked in source 07:03). Re-verified on the 07:06 snapshot: all 6 `<char>_key`, `drawer_key_clue`, `ledger_truth`, `harem_ok_*` settable; 0 "auto_play scene never played" and 0 ordering violations across 396 games (incl. idle and chaos bots); 6/6 True endings reached by the smart bot on all difficulties.

### QA-03 [Major, FIXED] Characters are selectable before they exist in the story
- **Repro:** `sh tools/csharp_check/qa_hunt/run.sh timing` (last block). Chapter 1 day 1: `Night.Talk("haru")` -> `Haru_Talk_01`: "마감이 끝난 주방. 오븐 앞에 쪼그려 앉은 하루가..." (uniform sprite). `Night.Talk("nabi")` -> `Nabi_Talk_01` (seated by the door). `Date(lighthouse)` succeeds for both. chapters.csv: ch4 "하루 입사", ch5 "나비 공연"; `staff_join_chapter` haru 4 / nabi 5.
- **Expected:** unintroduced characters are not offered (or get a "not met yet" gate). **Actual:** `RomanceService.Talk/Gift/Date` and `GameScreen.ShowCharPicker` have no availability gate; `characters.csv.staff_join_chapter` is not consumed by anyone (BusinessSimulator hard-codes a copy).
- **Fix:** add a per-character `first_chapter` to characters.csv and enforce it in `RomanceService.BeginNightAction` + `GameScreen.ShowCharPicker` (grey out with "not met yet").

### QA-04 [Major, FIXED - group-route follow-up open] The balance simulator no longer predicts the game (Good ending at 5-20 % focus)
- **Repro:** `QA_FOCUS=1 QA_RUNS=30 QA_FS=0.05,0.1,0.2 sh tools/csharp_check/qa_hunt/run.sh play` vs `python3 tools/simulate_balance.py`.
- **Expected (docs/03, docs/17 §6):** focus 20 % -> good-or-better 0-2 %; 35 % -> 35 %; 50 % -> 84 %. **Actual (real scripts, random dialogue choices, one target, remaining actions spread over the other five):**

| focus on target | Story good+ | Normal good+ | Hard good+ |
|---|---|---|---|
| 5 % | 83-93 % | 73-87 % | 53-63 % |
| 10 % | 93-97 % | 83-97 % | 77-83 % |
| 20 % | 97-100 % | 93-100 % | 93-97 % |

  (3 targets x 30 runs each; `normal/community` shares are the remainder; ablation with the simulator's "keep spending the other share on frozen characters after the lock" (Normal, Howl, lock on ch7 night 1): good+ 37 % at 10 %, 87 % at 20 %, 100 % at 35 % (sim: 0 / 0 / 35 %), so the post-lock cram window explains only part of the gap.) Difficulty ordering holds (Story >= Normal >= Hard) but the spread is small.
- **Root cause (several, all in the model-vs-host gap):** `romance_share` (35 % of actions are "management") has no counterpart in C# (`DayCycle` only offers Rest/Research); sim wastes actions on frozen chars after ch7 (`simulate_balance.py:255`) while a human puts them all on the target; sim gives liked gifts with p=0.6 (`gift_like_prob`, unused in C#), a player reading `prefs.csv` always can; Talk double-count (QA-05); sim assumes `ledger_truth=True` (`:336`).
- **Fix:** model the host (romance_share -> real rest/research cost or remove it; no waste after lock), or retune `endings.csv`/gains and regenerate docs/03; keep `qa_hunt play` focus table as the regression.

### QA-05 [Major, FIXED] Talk is double-counted (random choice bonus + script choice bonus)
- **Repro:** `run.sh gain`. Talk nodes (339 paths): script-granted aff avg **+2.29**, max +8.1, trust avg +0.45, on top of `RomanceService.Talk` (`Romance/RomanceService.cs:89` rolls `choice_prob`/`choice_success_prob` and applies +5/+1.5 or -1/-1 *before* the node plays). The Talk-node headers state they implement the same +5/+1.5 / -1/-1 rule. Expected talk value ~2.6 aff (model) -> actual ~4.9.
- **Fix:** remove the random roll from `RomanceService.Talk` (keep only +2) or strip `<<aff>>/<<trust>>` from Talk nodes.

### QA-06 [Major-latent, FIXED] Save integrity is coupled to the exact current schema
- **Repro:** `run.sh save` -> `[BUG-PROBE] save written BEFORE stage-2 fields ... still loads :: status=Tampered`; `[PROBE] tampered primary ...`: returns `RestoredFromBackup`.
- `Save/SaveService.cs:254` only special-cases an empty `LockedGroup` (regex). A v1 file written before `PlayerSpecies/PlayerAppearance/VisitedNodes` existed (hash over fewer fields) fails the HMAC after re-serialisation -> "Tampered". Stage 2 and 3 added fields without bumping `SaveData.CurrentVersion = 1`, contradicting docs/17 §5.4. Also `SaveService.Load` falls through to `.bak` when the primary is *Tampered*, silently rolling progress back one day (`:184`).
- **Fix (2026-10-03):** v2 signs a canonical, versioned payload (`SaveIntegrity.CanonicalPayload`/`SignedFields(version)`, serializer independent); v1 saves (stage-1/2/3 text-hash variants) verified by `SaveService.VerifyLegacyV1` then migrated via `SaveMigrations`; a rejected primary restored from `.bak` reports `Status=RestoredFromBackup`, `Detail='primary: Tampered'`, `PrimaryRejected=true` (UI still only logs it: follow-up for the UI owner). Tests: `qa_hunt save` (23 checks), `security_selftest` `save.signing`.
- **Impact today:** none (no shipped saves). **Impact on first post-release field addition:** every player's saves rejected. **Fix:** hash the raw text that was written (store `Hash` outside the signed body or sign the original string), bump `CurrentVersion` + migrations per docs/17; surface `Tampered` instead of masking it with `.bak`.

### QA-07 [Minor, FIXED] Hard (and Story) revenue vs `chapters.csv` targets
- `run.sh play` telemetry (gross/day, 120 non-chaos games each): Hard ch3 1 680 (target 2 200-3 600), ch4 2 234 (3 600-5 200), ch5 3 176 (5 200-7 000), ch8 7 808 (10 500-12 000); final shop level 8.7. Story ch7 12 077 (9 000-10 500), ch8 13 669 (10 500-12 000). Normal is on target (ch6 sits exactly at the lower bound 7 000). C# revenue matches `simulate_balance.py` within ~5 % (parity OK). Targets are not difficulty-scaled; docs/17 §6 only says "check".

### QA-08 [Minor, FIXED] Fatigue limit: reachable, but no teeth; simulator says 0 %
- `run.sh fatigue`: "date every action" bot is limited on 94-106 of 192 nights; focused bots ~9 % of nights (`play` telemetry), sim reports 0 % because it auto-rests at >=70 and spends 35 % of actions on management. When limited you get 1 action; Rest also costs 1 action, so resting is never better than being limited. `romance_share`, `mgmt_action_min`, `min_*`, `random_event_min`, `menu_dev_min`, `common_event_min`, `gift_like_prob`, `gift_neutral_prob` are read only by Python (grep over `Scripts/`).

### QA-12 [Minor, FIXED] Malformed balance cells silently become 0
- `Data/CsvTable.cs:27-39`: `CsvRow.Float/Int` return the fallback (0) on a non-numeric cell (`Float('abc') = 0`). A typo in `aff_min`/`trust_min` silently opens a gate. Missing *keys* throw (`BalanceTable.P`), but cells do not. Fix: throw (or collect errors) for non-empty unparseable numeric cells.

### QA-13 [Cosmetic, OPEN - writer content (Ch1_Outro has since been added)] Content gaps (all fall back sanely)
- `Ch1_Outro` does not exist (silently skipped at ch2 day 1).
- Liked spot without a dedicated date node -> `<Char>_Date_Generic`: howl/dojo, haru/rooftop, nabi/lighthouse (three other gaps were filled during the pass).
- 6 trap-only gifts (`party_horn_set`, `promo_voucher_bundle`, `etiquette_manual`, `performance_scorecard`, `uncle_portrait_frame`, `performance_merch_set`) are liked by nobody; fine as designed, listed for the writer.

### QA-15 [Cosmetic, FIXED] Hardening of states the validator already forbids
- **Status 2026-10-03:** `GameState.SetLock` aliasing fixed (copies before clearing; `qa_hunt save`). `EndingEvaluator.EvaluateGroup` hardening NOT done (Romance/ is owned by the gameplay agent): `qa_hunt ending` still has 2 PROBE failures.
- `EndingEvaluator.EvaluateGroup` (`Romance/EndingEvaluator.cs:142`) turns `[howl,howl]` into `harem_duo` and a 4-member list into `harem_trio`; `GameState.SetLock(state.LockedGroup)` wipes the lock (`Data/GameState.cs:131`: `Clear()` empties the list being copied). No caller does either today; `run.sh ending` probes them.

## 3. Findings - read only (not reproduced; UI is Unity-only)

### QA-02 [Major, FIXED] Per-step event thresholds vs per-character story timing
- `balance/events.csv` has one `chapter_min` per step (1,3,5,6,8); the E-node headers are per character (e.g. `Beomjun_E1 (3장)`, `Haru_E1 (4장)`, `Johan_E1 (2장)`, `Nabi_E1 (2장)`, five `E4` in ch7, `Beomjun_E3`/`Haru_E3` ch6): **14 of 30 events could be started earlier than their story** (a rent-notice scene in ch1, Howl's "chain store already open" E4 in ch6). The fix agent's `event_schedule.csv` notes list the same mismatches. Re-verified: `run.sh timing` shows all 30 events first playable exactly at their script chapter for an all-in bot (e.g. Haru E1 day 26 = ch4, Howl E4 day 51 = ch7 day 3).

### QA-09 [Minor, FIXED] Nothing guards the Unity `Resources` copies
- **Fix 2026-10-03:** `tools/release_check.sh` step `resources_sync` (sync_dialogue.py --check + new sync_balance.py --check + qa_hunt sync_check.py) fails when copies are stale; `package_source.sh` refuses to package a stale tree; both syncs were run.
- `Resources/Dialogue/_index.txt` had 0 entries at the start of the session (game would run on fallback lines only), later synced; at 07:04 `python3 tools/csharp_check/qa_hunt/sync_check.py` reported 24 problems (18 stale `.txt`, 3 new scripts with neither index entry nor copy; balance copies were identical). `tools/release_check.sh` has no equivalent; only the Unity-side Preflight (`BuildPreflight.CheckDialogueIndex`, checks "non-empty" only). Fix: run `sync_check.py` in `release_check.sh`.

### QA-10 [Minor, OPEN] Per-game UI state is not reset
- `UI/GameScreen.cs:58,458` `_routePromptDay` (and the `DayFlowDriver` stuck-guard `_last`) live as long as the screen object; `StartNewGame`/`LoadLatest` never reset them. Continue on a save from the same day on which the route prompt was already shown in this session suppresses the prompt that night.

### QA-11 [Minor, FIXED] Gate-closed events show an empty reason
- `Game/NightRomanceActions.cs:266-267` (`EventHint`): when aff/trust/chapter are fine but the scene gate (`after_node`, min day) is closed it returns `""` ("특별한 표시를 하지 않는다"): the event picker row shows a disabled "Howl - " with no explanation (e.g. Howl_E4 waiting for CE07 / day 3).

### QA-14 [Cosmetic, FIXED] Dead or duplicated data
- `prefs.csv` has 18 `menu` rows no code reads; `spots.period` is only copied to a UI struct (dates ignore it); `BusinessSimulator.StaffJoinChapter/StaffIds` duplicate `characters.csv`; `helper_bonus` ("7장 루트 확정 후", params.csv note) is applied in chapter 7 to everyone, locked or not (`Tycoon/BusinessSimulator.cs:50`, identical in Python).

### QA-16 [Cosmetic, by design] Soft ordering folds
- `CE02_Tasting` / `CE03_RentMeeting` / `CE04_StaffWelcome` use soft `after_node`s that fold on the last window day, so CE02 can precede `Johan_E1` (~10 of the 396 simulated games) and docs/12 §5-1's Ch5 order (Johan_E3 -> Nabi_E3 -> Ch5_Show_After) is not enforced. Content copes (`ch5_snap_told` recall branch in `Nabi_E3`); CE02 mentions the 3-star review as public fact even when E1 has not happened.

## 4. Verified clean (negative results worth knowing)

Invariants asserted after every step of 396 full games (+ ~1 000 earlier-snapshot games): 0 violations of
aff `0..cap(ch)`, trust `0..cap(ch)`, events 0..5 and never replayed, gold finite, fatigue 0..100, `ActionsLeft` 0..2 and never increasing, phase order Morning->Business->Night->Settlement->Morning, 64 days exactly, shop level <= chapter cap, gift/date cooldowns, refused actions leave state byte-identical, wrong-phase calls refused/throw without mutation, `harem_ok_*` set only by its own consent node (host never writes flags), no flag ever cleared by a script, frozen non-members bit-identical from lock to the end (random actions + dialogue adjustments), lock rejected in ch1-6 and accepted on ch7 day 1 (= day 49), lock survives mid-run save/load, scheduled scenes played exactly once inside their windows with hard `after/before` respected, every ending resolves to an existing node and shows the CGs it promises, harem endings for all 35 member combinations, 2 000 save round-trips + idempotence, 52k+40k ending-oracle cases (exact thresholds), engine-exact flag fixpoint: **every flag any script reads is now settable**; 3 000 mutated `.yarn` files, 20 000 random expressions, 6 000 mutated `profile.json` -> only documented exception types.
Existing suites at 07:0x (tail of each run): see §7.

## 5. Per-area answers (hunt list)

1. Full playthrough: done (above). Smart bot (path-enumerating choice planner) reaches True for all 6 characters on all 3 difficulties after QA-01's fix; random choices reach True in 0-4 of 6 runs depending on the character (hidden keys), Good otherwise. Duo/trio harem succeed with consent via smart choices. No dead-end where a required node is unreachable (post-fix).
2. Save/load: round-trip property holds; backward-compat gap = QA-06; mid-chapter-transition autosave (taken in `StartDay` *before* Outro/Intro/CE) correctly replays unseen scenes (state-based `ChapterFlow`/`SceneGate`).
3. Ending logic: matches oracle everywhere; unusual-state hardening = QA-15; negative/non-1 flag values count as set.
4. Content QA: QA-01, 03, 13; keys settable; no dead-branch reads at chapter granularity (`run.sh dead`); `#line:` ids unique repo-wide; all `entries*.txt` names defined.
5. Balance: QA-04, 05, 07, 08; revenue Python-vs-C# parity ~5 %; romance parity harness passes; difficulty ordering OK for revenue and (weakly) for romance.
6. Ordering rules: now enforced by `SceneGate` except the soft/ordering gaps in QA-16 and CE02/CE03 not forced after the E1/E2 they cite.
7. UI flow (read only): QA-10, 11; no `EventBus` leak (single subscription in `AppContext`, `ClearAll` in `OnDestroy`); double clicks cannot hit a destroyed button (`ClearChildren` deactivates first); `ReturnToTitle` mid-business is safe (coroutine stopped, only morning autosaves exist).

## 6. Not covered, and why
- UI/Boot/Editor code at runtime (layout, input, coroutines, `VnStage`, audio, art loading): no Unity. `tools/unity_compile_check` result is in §7; compilation only.
- `yarn_en` localisation CSVs and the English text path appeared during the session and were not fuzzed; `locale = "ko"` only in the bot (the existing `scenario_check` covers en/ko rendering).
- Gender coverage: 4 genders are rotated across runs and every script runs on all 4 without errors, but text quality/pronoun wording is only checked by the existing `scenario_check` heuristics.
- `LayoutRules`, `MemoryBudget`, `TouchGesture`, `AudioMixerModel`: read, spot-checked; not property-tested.
- The tree was changing under the test (scene scheduler, demo mode `DemoConfig`, localisation, security-review fixes): findings marked FIXED were re-checked on the 07:06 snapshot; open items should be re-run (`run.sh all`) before triage.

## 7. Appendix - status of the existing suites on the live tree (07:12 UTC)

| Suite | Result |
|---|---|
| `tools/csharp_check/run.sh` (core) | ALL OK |
| `romance_check` (incl. Monte-Carlo parity) | ALL OK |
| `ending_check` | 140 checks, 0 failures |
| `flow_check` | 186 checks, 0 failures |
| `ui_logic_check` | 169 checks, 0 failures (one transient failure earlier while files were being edited) |
| `scenario_check` | 102 checks, 0 failures (it failed at ~06:50 on a gender-word heuristic hit in `Beomjun_Date_FishMarket`, fixed by the writer since) |
| `WORK=/tmp/unitycheck_repro sh tools/unity_compile_check/run.sh` | UNITY COMPILE CHECK: ALL OK (ugui, tmp, isys, game, tests) |
| `qa_hunt all` (snapshot 07:06) | 126 checks, 7 expected failures (QA-03, 06 x2, 13, 15 x3) |

None of these suites covers QA-03..QA-08: they pass while those are open, which is the point of the new harness.

## 8. Fix status - gameplay/balance pass (2026-10-03, after the report)

| ID | Status | Evidence (reproduce with `sh tools/csharp_check/qa_hunt/run.sh <mode>`) |
|---|---|---|
| QA-03 | FIXED | `characters.csv` gets `first_chapter` (howl/gomseok 1, johan/nabi 2, beomjun 3, haru 4; staff join unchanged) and `gain_mult`. `RomanceService` refuses talk/gift/date/event for a not-yet-met character with `not_met` (state untouched); `NightRomanceActions.IsAvailable/AvailableChars/CharUnavailableText/GiftBlockReason/DateBlockReason/EventHint` expose it for the pick lists (UI must hide or grey the `ShowCharPicker` rows with `AvailableChars()`; text "아직 만나지 못한 인물입니다." = ui msg_16). `timing`: before the `[PROBE]` failed, after 0 leaks over 6 chars x 8 chapters x 4 action kinds. Late joiners get a catch-up `gain_mult` (beomjun 1.1, haru 1.2, johan 1.1) so their good-ending rate matches the others. |
| QA-04 | FIXED | `tools/simulate_balance.py` rewritten as the same policy as the qa_hunt focus bot (no `romance_share`, no waste after the lock, event schedule + `scene_rules` gate port, `first_chapter`, liked gifts/spots from prefs, free rest, thresholds from endings.csv, measured `ledger_truth_prob`/`key_flag_prob`). Good-or-better at focus 20/35/50/60 % (Normal): before (C# bot) ~95 / 100 / 100 / 100, after C# bot 6 / 44 / 76 / 97, Python 6 / 52 / 88 / 95 (docs/03). Story 24/74/96/100, Hard 2/33/72/88 (C#). `romance_check` parity within 5.3 pp, `gate_check` parity within 3 pp. Residual: ~6 % at 20 % (the post-lock cram window), skilled players (`--skill params`) are far above the curve. |
| QA-05 | FIXED | `RomanceService.Talk` = `talk` row only (the `choice_prob` roll is gone). Dialogue nodes own choice rewards. `gain`: 600/600 talks equal the base; `TALK-MODEL`: 2 choice points per node, script +2.95 aff / +0.44 trust per talk (random options). docs/06 section 2. |
| QA-07 | FIXED | Hard gross/day, qa_hunt telemetry: ch3-5 was 1680/2234/3176 (25-45 % under) now 2630/3624/5220 (inside the bands), ch6-8 6899/9118/11243. Normal ch4 3752, Story ch8 12612. `difficulty.csv`: Hard `rev_mult` 0.85 -> 1.0, `ingredient_cost_rate` 0.42 -> 0.385, `start_gold` 3500 -> 4000; Story `rev_mult` 1.2 -> 1.1, ingredient 0.32 -> 0.34. |
| QA-08 | FIXED | On a fatigue-limited night Rest is free once (no action spent, -25 fatigue, usable even after the action): `DayCycle.FreeRestAvailable`; unlimited nights unchanged. `fatigue`: 'date spam' bot limited 66/192 -> 19/192 nights, romance actions 246 -> 293 of 384. UI should offer the free Rest when `FreeRestAvailable`. |
| QA-11 | FIXED | `EventHint` returns `NightRomanceActions.EventNotYetText` ("아직 때가 아니다", ui msg_15) when only the scene gate is closed; gate_check asserts it is never blank. |
| QA-14 | FIXED | `BusinessSimulator` reads staff from `BalanceTable.Characters` (characters.csv); `helper_bonus` applies in ch7 only after the route lock (Python identical). Reserved data documented in docs/06: `prefs.csv` menu rows, `spots.period`. Unused model params `romance_share`, `mgmt_action_min`, `choice_prob`, `gift_like_prob`, `gift_neutral_prob` deleted. |
| QA-15 | FIXED | `EndingEvaluator.EvaluateGroup` gives community (`invalid_group`) for >3 members or duplicates. `qa_hunt ending`: 0 failures. |
| QA-04 follow-up | FIXED | Group routes reachable with group-only data (params `group_lock_min_aff/trust` 50/7, `group_gain_mult_2/3` 1.0/1.7; endings `harem_duo` 72/24/3, `harem_trio` 65/14/2). Normal, random answers, f=100 %: duo 6 % -> 57 %, trio 0 % -> 32 % (Python); C# bot (qa_hunt play group campaign, 60 runs) duo 53 %, trio 27 %; trio at f=60 % 2 %. Single curve unchanged. See docs/10 section 2. |
