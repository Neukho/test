using System;
using System.Collections.Generic;
using System.IO;
using MorpheHouse.Core;
using MorpheHouse.GameFlow;
using NUnit.Framework;
using UnityEngine;

namespace MorpheHouse.Tests
{
    /// <summary>
    /// 수정 묶음(데모 모드 · LRU 캐시 · 현지화 · 터치 목표 · 저장 최신성) EditMode 테스트.
    /// 전체 검증은 tools/csharp_check/fixes_check 가 하고(키·자리표시자 전수 검사 포함), 여기서는 Unity 안에서 같은 순수 로직이 도는지 핵심만 확인한다.
    /// 이 환경에서는 컴파일만 확인했다(실행은 Unity 에서).
    /// </summary>
    public sealed class StageFixTests
    {
        private static string Res(string relative)
        {
            return Path.Combine(Application.dataPath, "_Project/Resources/" + relative);
        }

        [Test]
        public void LruCache_EvictsOldestByCount_AndCallsCallback()
        {
            List<string> evicted = new List<string>();
            LruCache<string, int> c = new LruCache<string, int>(2, 0, delegate (string k, int v) { evicted.Add(k); });
            c.Put("a", 1, 1);
            c.Put("b", 2, 1);
            int got;
            c.TryGet("a", out got);
            c.Put("c", 3, 1);
            Assert.IsTrue(c.Contains("a"));
            Assert.IsFalse(c.Contains("b"));
            CollectionAssert.AreEqual(new[] { "b" }, evicted);
        }

        [Test]
        public void LruCache_ByteCap_AndPin()
        {
            LruCache<string, int> c = new LruCache<string, int>(10, 25, null);
            c.Put("a", 1, 10);
            c.Pin("a");
            c.Put("b", 2, 10);
            c.Put("c", 3, 10);
            Assert.IsTrue(c.Contains("a"), "pinned item must survive");
            Assert.IsFalse(c.Contains("b"));
            Assert.LessOrEqual(c.TotalBytes, 25);
        }

        [Test]
        public void DemoConfig_ParsesAndDecidesEnd()
        {
            DemoConfig on = DemoConfig.Parse("key,value\ndemo,1\n");
            Assert.IsTrue(on.Enabled);
            Assert.AreEqual(3, on.LastChapter);
            Assert.IsFalse(on.IsDemoOver(3, false));
            Assert.IsTrue(on.IsDemoOver(4, false));
            Assert.IsFalse(DemoConfig.Parse("demo,0").IsDemoOver(8, true));
        }

        [Test]
        public void DemoConfig_ShippedCsvIsDisabled()
        {
            DemoConfig c = DemoConfig.Parse(File.ReadAllText(Res("Config/build_config.csv")));
            Assert.IsFalse(c.Enabled, "release builds must ship with demo,0");
        }

        [Test]
        public void DemoConfig_ToggleRoundTrip_AndFlag()
        {
            bool now;
            string on = DemoConfig.ToggleDemo("key,value\ndemo,0\n", out now);
            Assert.IsTrue(now);
            string off = DemoConfig.ToggleDemo(on, out now);
            Assert.IsFalse(now);
            Assert.AreEqual("key,value\ndemo,0\n", off);
            Dictionary<string, int> flags = new Dictionary<string, int>();
            DemoConfig.MarkFlag(flags);
            Assert.IsTrue(DemoConfig.IsDemoSave(flags));
        }

        [Test]
        public void TouchTargetPolicy_MobileScalesWithDensity_DesktopKeepsFloor()
        {
            Assert.AreEqual(48f, TouchTargetPolicy.MinButtonHeight(420f, 2400, 1080, 1f, false));
            Assert.AreEqual(113f, TouchTargetPolicy.MinButtonHeight(420f, 2400, 1080, 1f, true));
            Assert.AreEqual(48f, TouchTargetPolicy.MinButtonHeight(160f, 1920, 1080, 1f, true));
        }

        [Test]
        public void LayoutRules_TallerButtonsFitWhereScrollIsDeclared()
        {
            float m = TouchTargetPolicy.MinButtonHeight(LayoutRules.Screen20x9, 1f, true);
            Assert.AreEqual(0, LayoutRules.CheckTouchStacks(LayoutRules.Screen20x9, 1f, m).Count);
            Assert.AreEqual(0, LayoutRules.CheckTouchStacks(LayoutRules.Screen16x9, 1f, 48f).Count);
        }

        [Test]
        public void SaveRecency_ParsesDatesInsteadOfComparingStrings()
        {
            Assert.IsTrue(SaveRecency.IsNewer("2026-10-03T09:00:00Z", "2026-10-03T10:00:00+02:00"));
            Assert.IsTrue(SaveRecency.IsNewer("2020-01-01T00:00:00Z", "garbage"));
            Assert.IsFalse(SaveRecency.IsNewer("garbage", "2020-01-01T00:00:00Z"));
        }

        [Test]
        public void DayFlowDriver_ReturnsCommonSceneOnce_AndGuardsRepeats()
        {
            HashSet<string> visited = new HashSet<string>();
            DayFlowDriver d = new DayFlowDriver(
                delegate (SceneSlot slot) { return visited.Contains("CE01") ? ChapterStep.NoStep : ChapterStep.Of(ChapterStepKind.CommonScene, "CE01"); },
                visited.Contains);
            Assert.AreEqual(ChapterStepKind.CommonScene, d.Next(SceneSlot.Morning).Kind);
            visited.Add("CE01");
            Assert.AreEqual(ChapterStepKind.None, d.Next(SceneSlot.Morning).Kind);

            DayFlowDriver stuck = new DayFlowDriver(delegate (SceneSlot slot) { return ChapterStep.Of(ChapterStepKind.CommonScene, "X"); }, null);
            stuck.Next(SceneSlot.Night);
            Assert.AreEqual(ChapterStepKind.None, stuck.Next(SceneSlot.Night).Kind);
            Assert.AreEqual(1, stuck.StuckCount);
        }

        [Test]
        public void Loc_FallsBackToKoreanDefault_AndFormats()
        {
            UiStrings saved = UiStrings.Current;
            try
            {
                UiStrings.Current = new UiStrings();
                Assert.AreEqual("제1장 상속", Loc.F("hud_day_test", "제{0}장 {1}", 1, "상속"));
                UiStrings en = new UiStrings();
                en.LoadLocale("ko", "key,text\nstart_button,시작\n");
                en.LoadLocale("en", "key,text\nstart_button,Start\n");
                en.SetLocale("en");
                UiStrings.Current = en;
                Assert.AreEqual("Start", Loc.T("start_button", "시작"));
            }
            finally
            {
                UiStrings.Current = saved;
            }
        }

        [Test]
        public void LocalizationTables_HaveSameKeysAndPlaceholders()
        {
            UiStrings u = new UiStrings();
            u.LoadLocale("ko", File.ReadAllText(Res("Localization/ui_ko.csv")));
            u.LoadLocale("en", File.ReadAllText(Res("Localization/ui_en.csv")));
            Assert.AreEqual(0, u.Errors.Count);
            Assert.AreEqual(0, u.MissingIn("ko", "en").Count);
            Assert.AreEqual(0, u.MissingIn("en", "ko").Count);
            Assert.AreEqual(0, u.PlaceholderMismatches("ko", "en").Count);
        }
    }
}
