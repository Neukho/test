using System;
using System.IO;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Save;
using MorpheHouse.Tycoon;
using NUnit.Framework;
using UnityEngine;

namespace MorpheHouse.Tests
{
    /// <summary>1단계 EditMode 테스트: 데이터 로드, 하루 사이클, 난이도 곡선, 저장 무결성, 이벤트 버스.</summary>
    public sealed class StageOneTests
    {
        private static ITextSource Source()
        {
            return new FileTextSource(Path.Combine(Application.dataPath, "_Project/Resources/Balance"));
        }

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
        }

        [Test]
        public void BalanceTable_Loads_AllDifficulties()
        {
            foreach (Difficulty d in new[] { Difficulty.Story, Difficulty.Normal, Difficulty.Hard })
            {
                BalanceTable b = BalanceTable.Load(Source(), d);
                Assert.AreEqual(8, b.MaxChapter);
                Assert.Greater(b.P("start_gold"), 0f);
            }
            Assert.Greater(BalanceTable.Load(Source(), Difficulty.Story).P("start_gold"),
                           BalanceTable.Load(Source(), Difficulty.Hard).P("start_gold"));
        }

        [Test]
        public void DayCycle_EnforcesPhaseOrder()
        {
            BalanceTable b = BalanceTable.Load(Source(), Difficulty.Normal);
            GameState s = GameState.NewGame(b, Difficulty.Normal, "t");
            DayCycle c = new DayCycle(s, b, new SystemRandomAdapter(1));
            c.Begin();
            Assert.Throws<InvalidOperationException>(() => c.Settle());
            c.RunBusiness();
            c.BeginNight();
            c.Settle();
            c.NextDay();
            Assert.AreEqual(2, s.Day);
            Assert.AreEqual(DayPhase.Morning, s.Phase);
        }

        [Test]
        public void FirstDayRevenue_IsNearChapterOneTarget()
        {
            BalanceTable b = BalanceTable.Load(Source(), Difficulty.Normal);
            GameState s = GameState.NewGame(b, Difficulty.Normal, "t");
            DayResult r = BusinessSimulator.Simulate(s, b, new SystemRandomAdapter(5));
            Assert.That(r.Gross, Is.InRange(600f, 1500f));
        }

        [Test]
        public void AutoPlay_Finishes64Days_AndDifficultyOrdersIncome()
        {
            float story = AutoPlayer.Run(BalanceTable.Load(Source(), Difficulty.Story), 20, 1).FinalGold;
            float normal = AutoPlayer.Run(BalanceTable.Load(Source(), Difficulty.Normal), 20, 1).FinalGold;
            float hard = AutoPlayer.Run(BalanceTable.Load(Source(), Difficulty.Hard), 20, 1).FinalGold;
            Assert.AreEqual(64, AutoPlayer.Run(BalanceTable.Load(Source(), Difficulty.Normal), 3, 1).Days);
            Assert.Greater(story, normal);
            Assert.Greater(normal, hard);
        }

        [Test]
        public void SaveService_RoundTrip_Tamper_And_Backup()
        {
            string dir = Path.Combine(Path.GetTempPath(), "morphe_" + Guid.NewGuid().ToString("N"));
            try
            {
                SaveService svc = new SaveService(new JsonUtilitySerializer(), new FileSaveStorage(dir));
                BalanceTable b = BalanceTable.Load(Source(), Difficulty.Normal);
                GameState s = GameState.NewGame(b, Difficulty.Normal, "라이언");
                s.Gold = 1234f;
                s.Chars[CharIds.Howl].Aff = 42f;

                svc.Save(1, SaveMapper.ToSave(s, 1, "test"));
                LoadResult ok = svc.Load(1);
                Assert.AreEqual(LoadStatus.Ok, ok.Status);
                Assert.AreEqual(42f, SaveMapper.FromSave(ok.Data).Chars[CharIds.Howl].Aff);

                string path = Path.Combine(dir, "slot1.json");
                File.WriteAllText(path, File.ReadAllText(path).Replace("\"Gold\":1234", "\"Gold\":999999"));
                Assert.AreEqual(LoadStatus.Tampered, svc.Load(1).Status);

                svc.Save(2, SaveMapper.ToSave(s, 2, "test"));
                s.Gold = 2000f;
                svc.Save(2, SaveMapper.ToSave(s, 2, "test"));
                File.WriteAllText(Path.Combine(dir, "slot2.json"), "garbage");
                LoadResult restored = svc.Load(2);
                Assert.AreEqual(LoadStatus.RestoredFromBackup, restored.Status);
                Assert.AreEqual(1234f, restored.Data.Gold);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void EventBus_SubscribeUnsubscribe()
        {
            int hits = 0;
            Action<GoldChanged> h = e => hits++;
            EventBus.Subscribe(h);
            EventBus.Publish(new GoldChanged(1f, 1f));
            EventBus.Unsubscribe(h);
            EventBus.Publish(new GoldChanged(1f, 1f));
            Assert.AreEqual(1, hits);
        }
    }
}
