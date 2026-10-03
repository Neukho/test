using System;
using System.Collections.Generic;
using System.IO;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using NUnit.Framework;
using UnityEngine;

namespace MorpheHouse.Tests
{
    /// <summary>3단계 EditMode 테스트: 챕터 흐름, 루트 선택 모델, 동의 재시도·복구 표, 엔딩 장면 계획, 프로필·해금·갤러리.</summary>
    public sealed class StageThreeTests
    {
        private static ITextSource Source()
        {
            return new FileTextSource(Path.Combine(Application.dataPath, "_Project/Resources/Balance"));
        }

        private static GameState Make(int chapter, out NightRomanceActions night)
        {
            BalanceTable balance = BalanceTable.Load(Source(), Difficulty.Normal);
            RomanceCatalog catalog = RomanceCatalog.Load(Source());
            GameState s = GameState.NewGame(balance, Difficulty.Normal, "민준");
            s.Chapter = chapter;
            s.Day = 10;
            s.Phase = DayPhase.Night;
            s.ActionsLeft = 2;
            RomanceService romance = new RomanceService(s, balance, catalog, new System.Random(1).NextDouble);
            night = new NightRomanceActions(s, balance, romance, catalog, null, null);
            return s;
        }

        private static Func<string, bool> Nodes(params string[] names)
        {
            List<string> list = new List<string>(names);
            return delegate (string n) { return list.Contains(n); };
        }

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
        }

        [Test]
        public void ChapterFlow_OutroThenIntroThenCard()
        {
            List<string> seen = new List<string>();
            Func<string, bool> visited = delegate (string n) { return seen.Contains(n); };
            Func<string, bool> exists = Nodes("Ch1_Outro", "Ch2_Intro");
            ChapterStep a = ChapterFlow.NextMorningStep(2, 1, true, exists, visited);
            Assert.AreEqual(ChapterStepKind.Outro, a.Kind);
            Assert.AreEqual("Ch1_Outro", a.Node);
            seen.Add("Ch1_Outro");
            Assert.AreEqual(ChapterStepKind.Intro, ChapterFlow.NextMorningStep(2, 1, true, exists, visited).Kind);
            seen.Add("Ch2_Intro");
            Assert.AreEqual(ChapterStepKind.InfoCard, ChapterFlow.NextMorningStep(2, 1, true, exists, visited).Kind);
            Assert.AreEqual(ChapterStepKind.None, ChapterFlow.NextMorningStep(2, 2, true, exists, visited).Kind);
            Assert.AreEqual(ChapterStepKind.InfoCard, ChapterFlow.NextMorningStep(3, 1, true, Nodes(), visited).Kind);
        }

        [Test]
        public void ChapterFlow_FinalOutro()
        {
            Assert.AreEqual("Ch8_Outro", ChapterFlow.FinalOutro(8, Nodes("Ch8_Outro"), delegate (string n) { return false; }));
            Assert.IsNull(ChapterFlow.FinalOutro(8, Nodes("Ch8_Outro"), delegate (string n) { return true; }));
            Assert.IsNull(ChapterFlow.FinalOutro(8, Nodes(), null));
        }

        [Test]
        public void RouteSelectModel_SingleGroupAndRefusals()
        {
            NightRomanceActions night;
            GameState s = Make(7, out night);
            s.Chars["howl"].Aff = 80f; s.Chars["howl"].Trust = 36f;
            s.Chars["johan"].Aff = 78f; s.Chars["johan"].Trust = 35f;
            s.Chars["nabi"].Aff = 70f; s.Chars["nabi"].Trust = 5f;
            EndingTable table = EndingTable.Load(Source());
            RouteSelectModel m = new RouteSelectModel(night, table);

            string msg;
            Assert.AreEqual(6, m.Options.Count);
            Assert.IsFalse(m.Toggle("nabi", out msg));
            StringAssert.Contains("신뢰도", msg);
            Assert.IsTrue(m.Toggle("howl", out msg));
            Assert.AreEqual(RouteChoiceMode.Single, m.Mode);
            Assert.IsTrue(m.Toggle("johan", out msg));
            Assert.AreEqual(RouteChoiceMode.Group, m.Mode);
            StringAssert.Contains("동의", m.ModeExplanation());
            Assert.IsTrue(m.Commit(out msg));
            Assert.AreEqual(2, s.LockedCount);
            Assert.IsTrue(s.IsRouteMember("johan"));
            Assert.AreEqual("howl,johan", string.Join(",", m.LockedMembers.ToArray()));
        }

        [Test]
        public void RouteFlow_PostLockScenesAndPrompts()
        {
            Func<string, bool> all = Nodes("Harem_Table", "Howl_Harem_Consent", "Johan_Harem_Consent");
            List<SceneItem> scenes = RouteFlow.PostLockScenes(new string[] { "howl", "johan" }, all);
            Assert.AreEqual(3, scenes.Count);
            Assert.AreEqual("Harem_Table", scenes[0].Node);
            Assert.AreEqual("Johan_Harem_Consent", scenes[2].Node);
            Assert.AreEqual(0, RouteFlow.PostLockScenes(new string[] { "howl" }, all).Count);

            NightRomanceActions night;
            GameState s = Make(7, out night);
            Assert.IsTrue(RouteFlow.ShouldAutoPrompt(s, 1, -1));
            Assert.IsFalse(RouteFlow.ShouldAutoPrompt(s, 1, s.Day));
            s.Flags[RouteFlow.DeclinedFlag] = 1;
            Assert.IsFalse(RouteFlow.ShouldAutoPrompt(s, 1, -1));
            Assert.IsTrue(RouteFlow.MenuEntryVisible(s));
        }

        [Test]
        public void ConsentRetry_AndRecoveryTable()
        {
            NightRomanceActions night;
            GameState s = Make(8, out night);
            s.SetLock(new string[] { "howl", "johan" });
            Func<string, bool> exists = Nodes("Howl_Harem_Consent", "Johan_Harem_Consent", "Howl_E4_Retry");
            List<NightExtraEntry> e = ConsentRetry.Entries(s, exists, night.CharName);
            Assert.AreEqual(2, e.Count);
            Assert.AreEqual("동의 확인 대화(하울)", e[0].Label);
            s.Flags["harem_ok_howl"] = 1;
            Assert.AreEqual(1, ConsentRetry.Entries(s, exists, night.CharName).Count);
            Assert.IsFalse(s.Flags.ContainsKey("harem_ok_johan"));

            s.Flags["howl_e4_fail"] = 1;
            Func<string, bool> never = delegate (string n) { return false; };
            Assert.AreEqual(1, RecoveryTable.Default().Entries(s, exists, never).Count);
            s.Flags["howl_e4_trust_late"] = 1;
            Assert.AreEqual(0, RecoveryTable.Default().Entries(s, exists, never).Count);
            s.Finished = true;
            Assert.AreEqual(0, NightExtras.Collect(s, null, exists, never, night.CharName).Count);
        }

        [Test]
        public void EndingFlow_PlansNodesInOrder()
        {
            NightRomanceActions night;
            GameState s = Make(8, out night);
            s.Chars["howl"].Aff = 80f; s.Chars["howl"].Trust = 36f; s.Chars["howl"].EventsDone = 5;
            s.Chars["johan"].Aff = 80f; s.Chars["johan"].Trust = 36f; s.Chars["johan"].EventsDone = 5;
            s.SetLock(new string[] { "howl", "johan" });
            s.Flags["harem_ok_howl"] = 1; s.Flags["harem_ok_johan"] = 1;
            EndingTable table = EndingTable.Load(Source());
            RomanceCatalog catalog = RomanceCatalog.Load(Source());
            Func<string, bool> nodes = Nodes("Ending_Harem2", "Howl_Harem_Epilogue");
            EndingPlan plan = EndingPlan.Create(s, table, catalog, nodes);
            List<SceneItem> scenes = EndingFlow.Scenes(plan, nodes);
            Assert.AreEqual(3, scenes.Count);
            Assert.AreEqual("Ending_Harem2", scenes[0].Node);
            Assert.AreEqual("Howl_Harem_Epilogue", scenes[1].Node);
            Assert.IsNull(scenes[2].Node);
            StringAssert.Contains("요한", scenes[2].FallbackText);

            NightRomanceActions n2;
            GameState community = Make(8, out n2);
            EndingPlan cp = EndingPlan.Create(community, table, catalog, Nodes());
            Assert.AreEqual(1, EndingFlow.Scenes(cp, Nodes()).Count);
            Assert.AreEqual("달빛 모퉁이", EndSummary.Build(cp, community, 0, 0, 0, 21).Title);
        }

        [Test]
        public void Profile_RoundTripCorruptionAndUnlocks()
        {
            MemoryProfileStorage storage = new MemoryProfileStorage();
            ProfileService p = new ProfileService(storage);
            Assert.AreEqual(ProfileLoadStatus.Missing, p.Load());
            EndingResult good = new EndingResult();
            good.Kind = EndingKind.Good;
            good.PrimaryChar = "howl";
            Assert.IsTrue(p.RecordEnding(good));
            Assert.IsFalse(p.RecordEnding(good));
            Assert.IsTrue(p.RecordCg("howl_e1"));
            Assert.IsTrue(p.Save());

            ProfileService q = new ProfileService(storage);
            Assert.AreEqual(ProfileLoadStatus.Loaded, q.Load());
            Assert.AreEqual(2, q.ReachCount("good:howl"));
            Assert.AreEqual("HOWL_E1", q.SeenCgs[0]);

            UnlockService u = UnlockService.WithDefaults(q);
            Assert.AreEqual(1, u.Evaluate().Count);
            Assert.IsTrue(u.IsUnlocked(UnlockService.ExtraEpilogues));
            Assert.AreEqual(0, u.Evaluate().Count);

            string[] bad = { "{", "garbage", "[1,2]", "{\"endings\": }", new string('[', 3000) };
            for (int i = 0; i < bad.Length; i++)
            {
                MemoryProfileStorage b = new MemoryProfileStorage();
                b.Text = bad[i];
                ProfileService bp = new ProfileService(b);
                Assert.AreEqual(ProfileLoadStatus.Corrupt, bp.Load(), bad[i]);
                Assert.AreEqual(0, bp.ReachedCount);
            }

            MemoryProfileStorage partial = new MemoryProfileStorage();
            partial.Text = "{\"endings\":{\"good:howl\":3,\"bad\":\"x\"},\"cgs\":[\"a\",5],\"unlocks\":7}";
            ProfileService pp = new ProfileService(partial);
            Assert.AreEqual(ProfileLoadStatus.Loaded, pp.Load());
            Assert.AreEqual(1, pp.ReachedCount);
            Assert.AreEqual(1, pp.SeenCgs.Count);
        }

        [Test]
        public void Gallery_Slots_AndChoiceLabel()
        {
            Assert.AreEqual(21, EndingGallery.TotalSlots);
            Assert.AreEqual(21, EndingGallery.Slots(null, null).Count);
            Assert.AreEqual("가자 (조건 미충족)", ChoiceLabel.Format("가자", false, true));
            Assert.IsNull(ChoiceLabel.Format("가자", false, false));
            Assert.AreEqual("가자", ChoiceLabel.Format("가자", true, false));
        }

        [Test]
        public void RouteChoiceCommand_SetsRequestOnly()
        {
            NightRomanceActions night;
            GameState s = Make(7, out night);
            BalanceTable balance = BalanceTable.Load(Source(), Difficulty.Normal);
            RomanceService romance = new RomanceService(s, balance, null, new System.Random(1).NextDouble);
            DialogueCommandDispatcher d = new DialogueCommandDispatcher(s, romance, null);
            Assert.IsTrue(d.Handle("route_choice", new string[0]));
            Assert.IsFalse(s.HasRouteLock);
            Assert.IsTrue(d.ConsumeRouteChoice());
            Assert.IsFalse(d.ConsumeRouteChoice());
        }
    }
}
