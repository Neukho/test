using System;
using System.Collections.Generic;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Romance;
using MorpheHouse.Tycoon;

// 연애 모듈 검증 하네스: (a) 단위 검사, (b) simulate_balance.py 정책을 실제 DayCycle + RomanceService 위에 이식한 몬테카를로 패리티.
// 빌드/실행: tools/csharp_check/romance_check/run.sh
public static class Program
{
    private static int _checks;

    public static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "balance";
        int runs = args.Length > 1 ? int.Parse(args[1]) : 300;
        FileTextSource src = new FileTextSource(dir);
        BalanceTable normal = BalanceTable.Load(src, Difficulty.Normal);
        RomanceCatalog catalog = RomanceCatalog.Load(src);

        int failures = 0;
        failures += CatalogChecks(catalog);
        failures += UnitChecks(normal, catalog);
        Console.WriteLine("unit checks: {0} executed, {1} failed", _checks, failures);

        int parityFailures = Parity(src, catalog, runs);

        int total = failures + parityFailures;
        Console.WriteLine(total == 0 ? "ALL OK" : total + " FAILURE(S)");
        return total == 0 ? 0 : 1;
    }

    private static int Check(string name, bool ok)
    {
        _checks++;
        Console.WriteLine("  [{0}] {1}", ok ? "PASS" : "FAIL", name);
        return ok ? 0 : 1;
    }

    private static bool Near(float a, float b)
    {
        return Math.Abs(a - b) < 0.001f;
    }

    /// <summary>정해 둔 값을 순서대로 돌려주는 난수. 다 쓰면 0.99.</summary>
    private sealed class SeqRng
    {
        private readonly Queue<double> _q = new Queue<double>();
        public SeqRng(params double[] values) { foreach (double v in values) _q.Enqueue(v); }
        public double Next() { return _q.Count > 0 ? _q.Dequeue() : 0.99; }
    }

    private sealed class Fixture
    {
        public GameState State;
        public RomanceService Svc;
        public SeqRng Rng;
    }

    private static Fixture Make(BalanceTable b, RomanceCatalog cat, int chapter, params double[] rngValues)
    {
        Fixture f = new Fixture();
        f.State = GameState.NewGame(b, Difficulty.Normal, "t");
        f.State.Chapter = chapter;
        f.State.Day = 10;
        f.State.Phase = DayPhase.Night;
        f.State.ActionsLeft = 2;
        f.State.Fatigue = 0f;
        f.Rng = new SeqRng(rngValues);
        f.Svc = new RomanceService(f.State, b, cat, f.Rng.Next);
        return f;
    }

    // ---------- 카탈로그 ----------

    private static int CatalogChecks(RomanceCatalog cat)
    {
        Console.WriteLine("== catalog ==");
        int f = 0;
        f += Check("6 characters, ids match CharIds", cat.Characters.Count == 6 && cat.FindCharacter("nabi") != null && cat.FindCharacter("howl").NameKo == "하울");
        bool allIds = true;
        for (int i = 0; i < CharIds.All.Length; i++) if (cat.FindCharacter(CharIds.All[i]) == null) allIds = false;
        f += Check("all CharIds present in characters.csv", allIds);

        bool tagsOk = true;
        bool eachHasLikes = true;
        for (int i = 0; i < cat.Prefs.Count; i++)
        {
            PrefRow p = cat.Prefs[i];
            if (p.Kind == PrefKind.Gift && cat.FindGift(p.Tag) == null) tagsOk = false;
            if (p.Kind == PrefKind.Spot && cat.FindSpot(p.Tag) == null) tagsOk = false;
            if (cat.FindCharacter(p.CharId) == null) tagsOk = false;
        }
        for (int i = 0; i < CharIds.All.Length; i++)
        {
            string id = CharIds.All[i];
            if (cat.TagsFor(id, PrefKind.Gift, Preference.Like).Count < 3) eachHasLikes = false;
            if (cat.TagsFor(id, PrefKind.Menu, Preference.Like).Count < 3) eachHasLikes = false;
            if (cat.TagsFor(id, PrefKind.Spot, Preference.Like).Count < 3) eachHasLikes = false;
        }
        f += Check("every gift/spot tag in prefs.csv exists in gifts.csv/spots.csv", tagsOk);
        f += Check("each character has >=3 liked gifts/menus/spots", eachHasLikes);
        f += Check("spots: 9 rows incl. 8 GDD spots", cat.Spots.Count == 9 && cat.FindSpot("lighthouse").UnlockChapter == 1);
        f += Check("preference lookup like/dislike/neutral",
            cat.GetPreference("howl", PrefKind.Gift, "leather_wristguard") == Preference.Like
            && cat.GetPreference("howl", PrefKind.Gift, "party_horn_set") == Preference.Dislike
            && cat.GetPreference("howl", PrefKind.Gift, "baking_tools") == Preference.Neutral);
        return f;
    }

    // ---------- 단위 검사 ----------

    private static int UnitChecks(BalanceTable b, RomanceCatalog cat)
    {
        Console.WriteLine("== unit ==");
        EventBus.ClearAll();
        int f = 0;

        // 챕터별 캡
        float[] affCaps = { 18, 32, 45, 55, 65, 78, 92, 100 };
        float[] trustCaps = { 8, 14, 20, 25, 30, 36, 43, 50 };
        bool capsOk = true;
        for (int ch = 1; ch <= 8; ch++)
        {
            Fixture fx = Make(b, cat, ch);
            fx.Svc.AdjustAffection("howl", 500f, 500f, "test");
            CharState cs = fx.State.Chars["howl"];
            if (!Near(cs.Aff, affCaps[ch - 1]) || !Near(cs.Trust, trustCaps[ch - 1])) capsOk = false;
            if (!Near(RomanceRules.AffCap(b, ch), affCaps[ch - 1]) || !Near(RomanceRules.TrustCap(b, ch), trustCaps[ch - 1])) capsOk = false;
        }
        f += Check("caps per chapter (aff 18..100, trust 8..50) via AdjustAffection", capsOk);
        {
            Fixture fx = Make(b, cat, 1);
            fx.Svc.AdjustAffection("howl", -999f, -999f, "test");
            f += Check("floor clamp at 0", fx.State.Chars["howl"].Aff == 0f && fx.State.Chars["howl"].Trust == 0f);
        }

        // 후반 둔화
        f += Check("DiminishedGain formula (aff 50, soft 0.6, gain 10 -> 7)", Near(RomanceRules.DiminishedGain(10f, 50f, 0.6f), 7f));
        f += Check("negative gain not diminished", Near(RomanceRules.DiminishedGain(-5f, 80f, 0.6f), -5f));
        {
            // QA-05: 서비스의 대화 = talk 행(+2, 신뢰 talk.trust_gain) 하나. 난수를 쓰지 않고 선택지 보상은 대화 노드가 AdjustAffection 으로 준다.
            Fixture fx = Make(b, cat, 8, 0.0, 0.0, 0.0);
            fx.State.Chars["howl"].Aff = 50f;
            ActionResult r = fx.Svc.Talk("howl");
            float tt = b.Actions["talk"].TrustGain;
            f += Check("talk at aff 50: base only +2*(1-0.6*0.5)=+1.4 aff, +talk trust, no random choice roll (rng stays unconsumed)",
                r.Success && !r.ChoiceOffered && !r.ChoiceSucceeded && Near(r.AffDelta, 1.4f) && Near(r.TrustDelta, tt) && Near(fx.State.Chars["howl"].Aff, 51.4f) && fx.Rng.Next() == 0.0);
        }
        {
            Fixture fx = Make(b, cat, 8);
            fx.State.Chars["howl"].Aff = 50f;
            fx.State.Chars["howl"].Trust = 10f;
            ActionResult good = fx.Svc.AdjustAffection("howl", 5f, 1.5f, "dialogue:howl_talk_01");
            ActionResult bad = fx.Svc.AdjustAffection("howl", -1f, -1f, "dialogue:howl_talk_01");
            f += Check("node choice rewards via AdjustAffection: right +5*0.7=3.5 / +1.5 trust, wrong -1/-1 undiminished", Near(good.AffDelta, 3.5f) && Near(good.TrustDelta, 1.5f) && Near(bad.AffDelta, -1f) && Near(bad.TrustDelta, -1f));
        }
        {
            Fixture fx = Make(b, cat, 3);
            ActionResult early = fx.Svc.Talk("haru");
            ActionResult nabiOk = fx.Svc.Talk("nabi");
            fx.State.ActionsLeft = 2;
            fx.State.Chapter = 4;
            ActionResult haruOk = fx.Svc.Talk("haru");
            f += Check("QA-03: haru refused (not_met, state untouched) before ch4, talk works from first_chapter; nabi from ch2",
                !early.Success && early.Reason == RomanceRefusal.NotMet && nabiOk.Success && haruOk.Success && fx.State.Chars["haru"].LastContactDay == 10);
        }

        // 선물
        {
            Fixture fx = Make(b, cat, 3);
            ActionResult like = fx.Svc.Gift("howl", "leather_wristguard");
            f += Check("gift like: +8 aff at aff 0, gold 100+60*3=280", like.Success && Near(like.AffDelta, 8f) && Near(like.GoldSpent, 280f) && Near(fx.State.Gold, 5000f - 280f));
        }
        {
            Fixture fx = Make(b, cat, 3);
            fx.State.Chars["howl"].Aff = 20f;
            fx.State.Chars["howl"].Trust = 5f;
            ActionResult r = fx.Svc.Gift("howl", "party_horn_set");
            f += Check("gift dislike: -5 aff / -2 trust", r.Success && Near(r.AffDelta, -5f) && Near(r.TrustDelta, -2f));
        }
        {
            Fixture fx = Make(b, cat, 3);
            ActionResult r = fx.Svc.Gift("howl", "baking_tools");
            f += Check("gift neutral: +3 aff", r.Success && Near(r.AffDelta, 3f));
        }
        {
            Fixture fx = Make(b, cat, 3);
            f += Check("unknown gift refused", !fx.Svc.Gift("howl", "nope").Success && fx.Svc.Gift("howl", "nope").Reason == RomanceRefusal.UnknownGift && fx.State.ActionsLeft == 2);
        }
        {
            Fixture fx = Make(b, cat, 3);
            fx.State.ActionsLeft = 9;
            ActionResult first = fx.Svc.Gift("howl", "warm_scarf");
            ActionResult second = fx.Svc.Gift("howl", "warm_scarf");
            fx.State.Day += 1;
            ActionResult day1 = fx.Svc.Gift("howl", "warm_scarf");
            fx.State.Day += 1;
            ActionResult day2 = fx.Svc.Gift("howl", "warm_scarf");
            f += Check("gift cooldown 2d: same day & +1d refused, +2d ok",
                first.Success && !second.Success && second.Reason == RomanceRefusal.Cooldown && !day1.Success && day2.Success);
        }
        {
            Fixture fx = Make(b, cat, 3);
            fx.State.Gold = 10f;
            ActionResult r = fx.Svc.Gift("howl", "warm_scarf");
            f += Check("gift refused when gold short (no action consumed)", !r.Success && r.Reason == RomanceRefusal.NoGold && fx.State.ActionsLeft == 2 && fx.State.Gold == 10f);
        }

        // 데이트
        {
            Fixture fx = Make(b, cat, 1);
            ActionResult locked = fx.Svc.Date("howl", "dojo");
            f += Check("spot lock: dojo (ch3) refused at ch1, no action/gold used", !locked.Success && locked.Reason == RomanceRefusal.SpotLocked && fx.State.ActionsLeft == 2 && fx.State.Gold == 5000f);
        }
        {
            Fixture fx = Make(b, cat, 3);
            ActionResult liked = fx.Svc.Date("howl", "lighthouse");
            f += Check("liked spot +25%: 12*1.25=15 aff, trust +1.5, gold 200+50*3=350", liked.Success && Near(liked.AffDelta, 15f) && Near(liked.TrustDelta, b.Actions["date"].TrustGain) && Near(liked.GoldSpent, 350f));
            Fixture fy = Make(b, cat, 3);
            ActionResult neutral = fy.Svc.Date("howl", "beach");
            f += Check("neutral spot: 12 aff", neutral.Success && Near(neutral.AffDelta, 12f));
        }
        {
            Fixture fx = Make(b, cat, 8);
            fx.State.ActionsLeft = 9;
            ActionResult a = fx.Svc.Date("howl", "beach");
            ActionResult b1 = fx.Svc.Date("howl", "beach");
            fx.State.Day += 2;
            ActionResult c2 = fx.Svc.Date("howl", "beach");
            fx.State.Day += 1;
            ActionResult c3 = fx.Svc.Date("howl", "beach");
            f += Check("date cooldown 3d: +2d refused, +3d ok", a.Success && !b1.Success && b1.Reason == RomanceRefusal.Cooldown && !c2.Success && c3.Success);
        }

        // 이벤트 게이팅
        {
            Fixture fx = Make(b, cat, 1);
            CharState cs = fx.State.Chars["howl"];
            cs.Aff = 9f;
            bool a = fx.Svc.GetAvailableEvent("howl") == null;
            cs.Aff = 10f;
            EventRow e = fx.Svc.GetAvailableEvent("howl");
            f += Check("event 1 gate: aff 9 none, aff 10 -> step 1", a && e != null && e.Step == 1);

            cs.EventsDone = 1; cs.Aff = 40f; cs.Trust = 10f;
            fx.State.Chapter = 2;
            bool chapterBlock = fx.Svc.GetAvailableEvent("howl") == null;
            fx.State.Chapter = 3; cs.Trust = 7f;
            bool trustBlock = fx.Svc.GetAvailableEvent("howl") == null;
            cs.Trust = 8f; cs.Aff = 29f;
            bool affBlock = fx.Svc.GetAvailableEvent("howl") == null;
            cs.Aff = 30f;
            EventRow e2 = fx.Svc.GetAvailableEvent("howl");
            f += Check("event 2 gate: chapter<3 / trust<8 / aff<30 each block; all met -> step 2",
                chapterBlock && trustBlock && affBlock && e2 != null && e2.Step == 2);
        }
        {
            Fixture fx = Make(b, cat, 3);
            CharState cs = fx.State.Chars["howl"];
            cs.EventsDone = 1; cs.Aff = 40f; cs.Trust = 10f;
            bool wrongStep = !fx.Svc.CompleteEvent("howl", 3) && fx.State.ActionsLeft == 2;
            bool ok = fx.Svc.CompleteEvent("howl", 2);
            f += Check("CompleteEvent wrong step refused; right step applies +12 aff / events.csv trust (cap ch3 45/20), consumes action",
                wrongStep && ok && cs.EventsDone == 2 && Near(cs.Aff, 45f) && Near(cs.Trust, 10f + b.Events[1].TrustGain) && fx.State.ActionsLeft == 1 && cs.LastContactDay == 10 && Near(fx.State.Fatigue, 4f));
            bool noRepeat = !fx.Svc.CompleteEvent("howl", 2);
            f += Check("event cannot be completed twice", noRepeat && cs.EventsDone == 2);
        }
        {
            Fixture fx = Make(b, cat, 3);
            fx.State.Phase = DayPhase.Morning;
            fx.State.Chars["howl"].Aff = 40f;
            f += Check("CompleteEvent refused outside Night", !fx.Svc.CompleteEvent("howl", 1));
        }

        // 루트 확정 + 동결
        {
            Fixture fx = Make(b, cat, 6);
            fx.State.Chars["howl"].Aff = 80f; fx.State.Chars["howl"].Trust = 30f;
            bool early = !fx.Svc.TryLockRoute("howl");
            fx.State.Chapter = 7;
            fx.State.Chars["howl"].Aff = 59f;
            bool lowAff = !fx.Svc.TryLockRoute("howl");
            fx.State.Chars["howl"].Aff = 60f; fx.State.Chars["howl"].Trust = 19f;
            bool lowTrust = !fx.Svc.TryLockRoute("howl");
            fx.State.Chars["howl"].Trust = 20f;
            bool unknown = !fx.Svc.TryLockRoute("ghost");
            bool locked = fx.Svc.TryLockRoute("howl");
            bool again = !fx.Svc.TryLockRoute("johan");
            f += Check("lock rules: ch<7, aff<60, trust<20, unknown refused; 60/20 at ch7 ok; no second lock",
                early && lowAff && lowTrust && unknown && locked && again && fx.State.LockedRoute == "howl");
        }
        {
            Fixture fx = Make(b, cat, 7, 0.99, 0.99);
            fx.State.Chars["howl"].Aff = 70f; fx.State.Chars["howl"].Trust = 25f;
            fx.State.Chars["johan"].Aff = 40f; fx.State.Chars["johan"].Trust = 10f;
            fx.Svc.TryLockRoute("howl");
            ActionResult talk = fx.Svc.Talk("johan");
            f += Check("frozen: talk with non-chosen consumes action, no gains, contact day set",
                talk.Success && talk.Frozen && talk.AffDelta == 0f && talk.TrustDelta == 0f && fx.State.ActionsLeft == 1 && fx.State.Chars["johan"].Aff == 40f && fx.State.Chars["johan"].LastContactDay == 10);
            fx.State.ActionsLeft = 5;
            float gold = fx.State.Gold;
            ActionResult gift = fx.Svc.Gift("johan", "fountain_pen_ink");
            ActionResult date = fx.Svc.Date("johan", "beach");
            f += Check("frozen: gift/date no gains and no gold, no cooldown set",
                gift.Frozen && date.Frozen && fx.State.Gold == gold && fx.State.Chars["johan"].Aff == 40f && fx.State.Chars["johan"].LastGiftDay == -99);
            ActionResult adj = fx.Svc.AdjustAffection("johan", 10f, 5f, "dialogue");
            f += Check("frozen: AdjustAffection no change; no available event", adj.Frozen && fx.State.Chars["johan"].Aff == 40f && fx.Svc.GetAvailableEvent("johan") == null);
            ActionResult chosen = fx.Svc.AdjustAffection("howl", 10f, 5f, "dialogue");
            f += Check("chosen character still gains after lock", chosen.AffDelta > 0f && chosen.TrustDelta == 5f && chosen.Source == "dialogue");
        }

        // ActionsLeft / 단계
        {
            Fixture fx = Make(b, cat, 3, 0.99, 0.99, 0.99);
            ActionResult r1 = fx.Svc.Talk("howl");
            ActionResult r2 = fx.Svc.Talk("howl");
            ActionResult r3 = fx.Svc.Talk("howl");
            f += Check("ActionsLeft consumption: 2 talks ok, 3rd refused no_actions; fatigue +5 each; contact day",
                r1.Success && r2.Success && !r3.Success && r3.Reason == RomanceRefusal.NoActions && fx.State.ActionsLeft == 0
                && Near(fx.State.Fatigue, 10f) && fx.State.Chars["howl"].LastContactDay == 10);
        }
        {
            foreach (DayPhase ph in new[] { DayPhase.Morning, DayPhase.Business, DayPhase.Settlement })
            {
                Fixture fx = Make(b, cat, 3);
                fx.State.Phase = ph;
                bool ok = true;
                try
                {
                    ok &= !fx.Svc.Talk("howl").Success;
                    ok &= !fx.Svc.Gift("howl", "warm_scarf").Success;
                    ok &= !fx.Svc.Date("howl", "beach").Success;
                    ok &= fx.Svc.Talk("howl").Reason == RomanceRefusal.WrongPhase;
                }
                catch (Exception) { ok = false; }
                ok &= fx.State.ActionsLeft == 2 && fx.State.Fatigue == 0f && fx.State.Gold == 5000f;
                f += Check("invalid phase " + ph + ": refused without throwing or side effects", ok);
            }
            Fixture fz = Make(b, cat, 3);
            f += Check("unknown character refused", fz.Svc.Talk("ghost").Reason == RomanceRefusal.UnknownChar && fz.Svc.Talk(null).Reason == RomanceRefusal.UnknownChar);
        }

        // 플래그 + 이벤트 버스
        {
            EventBus.ClearAll();
            int affEvents = 0, flagEvents = 0, lockEvents = 0, doneEvents = 0, trustEvents = 0;
            EventBus.Subscribe<AffectionChanged>(e => affEvents++);
            EventBus.Subscribe<TrustChanged>(e => trustEvents++);
            EventBus.Subscribe<FlagChanged>(e => flagEvents++);
            EventBus.Subscribe<RouteLocked>(e => lockEvents++);
            EventBus.Subscribe<RomanceEventCompleted>(e => doneEvents++);
            Fixture fx = Make(b, cat, 7, 0.99);
            fx.Svc.SetFlag("ledger_truth");
            fx.Svc.SetFlag("ledger_truth", 1);
            fx.Svc.SetFlag("key_howl", 3);
            f += Check("flags: set/get/default and FlagChanged only on change", fx.Svc.GetFlag("ledger_truth") == 1 && fx.Svc.GetFlag("key_howl") == 3 && fx.Svc.GetFlag("none") == 0 && fx.Svc.HasFlag("ledger_truth") && flagEvents == 2 && fx.State.Flags["key_howl"] == 3);
            fx.State.Chars["howl"].Aff = 70f; fx.State.Chars["howl"].Trust = 25f; fx.State.Chars["howl"].EventsDone = 3;
            fx.Svc.Talk("howl");
            fx.Svc.CompleteEvent("howl", 4);
            fx.Svc.TryLockRoute("howl");
            f += Check("events published: AffectionChanged/TrustChanged/RomanceEventCompleted/RouteLocked", affEvents == 2 && trustEvents == 2 && doneEvents == 1 && lockEvents == 1);
            EventBus.ClearAll();
        }

        // 방치 감쇠 헬퍼(순수)
        f += Check("decay helpers: 6d grace -> none at 6, -1 at 7; days until decay",
            RomanceRules.DailyDecay(6, 6f, 1f) == 0f && RomanceRules.DailyDecay(7, 6f, 1f) == 1f && RomanceRules.DaysUntilDecay(6, 6f) == 1 && RomanceRules.DaysUntilDecay(9, 6f) == 0);
        return f;
    }

    // ---------- 몬테카를로 패리티 ----------

    private sealed class RunOutcome
    {
        public float Aff;
        public float Trust;
        public int Events;
        public bool GoodOrBetter;
        public int GoldRefusals;
        public bool Locked;
    }

    private static readonly double[] Focuses = { 0.2, 0.35, 0.5, 0.6, 0.8 };
    // docs/03_balance_report.md (normal) '굿 이상' / 평균 호감 / 평균 신뢰 / 이벤트
    private static readonly double[] DocGoodPct = { 6, 52, 88, 95, 99 };
    private static readonly double[] DocAff = { 100, 100, 100, 100, 100 };
    private static readonly double[] DocTrust = { 17, 37, 46, 48, 49 };
    private static readonly double[] DocEvents = { 2.5, 4.3, 4.9, 5.0, 5.0 };

    private static int Parity(ITextSource src, RomanceCatalog cat, int runs)
    {
        Console.WriteLine("== Monte-Carlo parity (Normal, target=howl, {0} runs/focus; reference player of docs/03) ==", runs);
        BalanceTable b = BalanceTable.Load(src, Difficulty.Normal);
        foreach (CsvRow r in CsvTable.Parse(src.Load("events")).Rows) { ScriptGain g; g.Aff = r.Float("script_aff"); g.Trust = r.Float("script_trust"); _script[r.Int("step")] = g; }
        int failures = 0;
        Console.WriteLine("focus | C# good+ | doc good+ | diff | C# aff/trust/events | doc aff/trust/events | gold refusals");
        for (int i = 0; i < Focuses.Length; i++)
        {
            int good = 0;
            double sa = 0, st = 0, se = 0;
            int goldRef = 0;
            for (int r = 0; r < runs; r++)
            {
                RunOutcome o = PlayOnce(b, cat, Focuses[i], 20261003 + i * 100000 + r);
                if (o.GoodOrBetter) good++;
                sa += o.Aff; st += o.Trust; se += o.Events;
                goldRef += o.GoldRefusals;
            }
            double pct = 100.0 * good / runs;
            double diff = pct - DocGoodPct[i];
            Console.WriteLine("{0,4:F0}% | {1,7:F1}% | {2,8:F0}% | {3,6}pp | {4:F0}/{5:F0}/{6:F1} | {7:F0}/{8:F0}/{9:F1} | {10}",
                Focuses[i] * 100, pct, DocGoodPct[i], diff.ToString("+0.0;-0.0"), sa / runs, st / runs, se / runs, DocAff[i], DocTrust[i], DocEvents[i], goldRef);
            failures += Check("parity focus " + (int)(Focuses[i] * 100) + "%: |diff| <= 10pp (" + diff.ToString("+0.0;-0.0") + ")", Math.Abs(diff) <= 10.0);
        }
        return failures;
    }

    /// <summary>simulate_balance.py 의 밤 행동 정책을 실제 DayCycle + RomanceService 위에서 재현한다.</summary>
    private static RunOutcome PlayOnce(BalanceTable b, RomanceCatalog cat, double focus, int seed)
    {
        const string target = "howl";
        SystemRandomAdapter rng = new SystemRandomAdapter(seed);
        Func<double> next = rng.NextDouble;
        GameState state = GameState.NewGame(b, Difficulty.Normal, "mc");
        DayCycle cycle = new DayCycle(state, b, rng);
        RomanceService svc = new RomanceService(state, b, cat, next);
        RunOutcome o = new RunOutcome();

        List<string> others = new List<string>();
        for (int i = 0; i < CharIds.All.Length; i++) if (CharIds.All[i] != target) others.Add(CharIds.All[i]);

        // 대상별 선물/스팟 후보(파이썬은 취향 비율만 모델링: like 0.6 / neutral 0.3 / dislike 0.1, 데이트는 기본 수치)
        Dictionary<string, string[]> gifts = new Dictionary<string, string[]>();
        for (int i = 0; i < CharIds.All.Length; i++)
        {
            string id = CharIds.All[i];
            string like = cat.TagsFor(id, PrefKind.Gift, Preference.Like)[0];
            string dislike = cat.TagsFor(id, PrefKind.Gift, Preference.Dislike)[0];
            string neutral = null;
            foreach (KeyValuePair<string, GiftRow> g in cat.Gifts)
                if (cat.GetPreference(id, PrefKind.Gift, g.Key) == Preference.Neutral) { neutral = g.Key; break; }
            gifts[id] = new string[] { like, neutral, dislike };
        }

        cycle.Begin();
        while (!state.Finished)
        {
            // 레벨업: 파이썬은 하루 1회, 여유 15% 이상일 때
            float cost;
            if (cycle.CanUpgradeShop(out cost) && state.Gold >= cost * 1.15f) cycle.TryUpgradeShop();

            // 7장: 루트 확정(대상이 조건을 만족하는 날 즉시)
            if (state.Chapter >= RomanceRules.LockMinChapter && state.LockedRoute.Length == 0) svc.TryLockRoute(target);

            cycle.RunBusiness();
            cycle.BeginNight();
            while (state.ActionsLeft > 0)
            {
                // 밤 행동은 전부 연애 행동(QA-04: romance_share 삭제). 피로 제한 밤의 무료 휴식은 먼저 쓴다.
                if (cycle.FreeRestAvailable) cycle.DoNightAction(NightAction.Rest);

                // 목표의 개인 이벤트가 열려 있으면 가장 먼저(+ 이벤트 노드의 선택지 보상 평균: events.csv script_aff/script_trust)
                EventRow ev0 = svc.GetAvailableEvent(target);
                if (ev0 != null && svc.CompleteEvent(target, ev0.Step)) { ScriptEvent(svc, b, target, ev0); continue; }

                string who;
                if (next() < focus && svc.IsAvailable(target) && !svc.IsFrozen(target)) who = target;
                else
                {
                    List<string> pool = new List<string>();
                    for (int i = 0; i < others.Count; i++) if (svc.IsAvailable(others[i]) && !svc.IsFrozen(others[i])) pool.Add(others[i]);
                    if (pool.Count == 0) pool.Add(target);
                    who = pool[(int)(next() * pool.Count)];
                }
                if (svc.IsFrozen(who)) { svc.Talk(who); continue; }

                EventRow ev = svc.GetAvailableEvent(who);
                if (ev != null && svc.CompleteEvent(who, ev.Step)) { ScriptEvent(svc, b, who, ev); continue; }

                ActionResult res = null;
                if (svc.DateReady(who) && state.Gold >= svc.DateCost() && next() < 0.8)
                {
                    res = svc.Date(who, NeutralSpot(cat, who, state.Chapter));
                }
                else if (svc.GiftReady(who) && state.Gold >= svc.GiftCost())
                {
                    res = svc.Gift(who, gifts[who][0]); // 기준 플레이어는 prefs.csv 의 '좋아함' 선물만 고른다
                }
                else if (svc.IsAvailable(target) && !svc.IsFrozen(target))
                {
                    // 고른 인물에게 준비된 게 없으면 목표에게 준비된 데이트/선물을 쓴다(qa_hunt 봇과 같다)
                    if (svc.DateReady(target) && state.Gold >= svc.DateCost()) { res = svc.Date(target, NeutralSpot(cat, target, state.Chapter)); who = target; }
                    else if (svc.GiftReady(target) && state.Gold >= svc.GiftCost()) { res = svc.Gift(target, gifts[target][0]); who = target; }
                }
                if (res == null || !res.Success)
                {
                    if (res != null && res.Reason == RomanceRefusal.NoGold) o.GoldRefusals++;
                    res = svc.Talk(who);
                    // 대화 노드의 선택지 보상(무작위 선택 봇 모델: params.csv talk_*): 서비스가 아니라 노드가 준다.
                    if (res.Success && !res.Frozen)
                        for (int k = 0; k < (int)b.P("talk_choice_points"); k++)
                        {
                            double q = next();
                            if (q < b.P("talk_right_prob")) svc.AdjustAffection(who, b.Actions["choice_success"].AffGain, b.Actions["choice_success"].TrustGain, "dialogue:talk");
                            else if (q < b.P("talk_right_prob") + b.P("talk_wrong_prob")) svc.AdjustAffection(who, b.Actions["choice_fail"].AffGain, b.Actions["choice_fail"].TrustGain, "dialogue:talk");
                        }
                }
            }
            cycle.Settle();
            cycle.NextDay();
        }

        CharState t = state.Chars[target];
        o.Aff = t.Aff;
        o.Trust = t.Trust;
        o.Events = t.EventsDone;
        o.Locked = state.LockedRoute == target;
        o.GoodOrBetter = t.Aff >= 85f && t.Trust >= 40f && t.EventsDone >= 5;
        return o;
    }

    private static void ScriptEvent(RomanceService svc, BalanceTable b, string who, EventRow ev)
    {
        // events.csv 의 모델 열(script_aff/script_trust): 이벤트 노드가 AdjustAffection 으로 주는 선택지 보상의 평균(qa_hunt gain 실측)
        ScriptGain g;
        if (_script.TryGetValue(ev.Step, out g)) svc.AdjustAffection(who, g.Aff, g.Trust, "dialogue:event");
    }

    private struct ScriptGain { public float Aff; public float Trust; }
    private static readonly Dictionary<int, ScriptGain> _script = new Dictionary<int, ScriptGain>();

    /// <summary>파이썬/qa_hunt 봇과 같은 선택: 해금된 '좋아함' 스팟이 있으면 그것(x1.25), 없으면 보통 스팟.</summary>
    private static string NeutralSpot(RomanceCatalog cat, string charId, int chapter)
    {
        List<SpotRow> open = cat.UnlockedSpots(chapter);
        for (int i = 0; i < open.Count; i++)
            if (cat.GetPreference(charId, PrefKind.Spot, open[i].Id) == Preference.Like) return open[i].Id;
        for (int i = 0; i < open.Count; i++)
            if (cat.GetPreference(charId, PrefKind.Spot, open[i].Id) == Preference.Neutral) return open[i].Id;
        return open[0].Id;
    }
}
