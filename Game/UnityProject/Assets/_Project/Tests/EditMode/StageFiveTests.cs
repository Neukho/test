using System.Collections.Generic;
using System.IO;
using MorpheHouse.GameFlow;
using NUnit.Framework;
using UnityEngine;

namespace MorpheHouse.Tests
{
    /// <summary>
    /// 5단계(폴리시) EditMode 테스트: 오디오 계산·매니페스트, 문자열 표·언어 유지, 의상 정책, 화면 배치 규칙, 터치 제스처·뒤로가기, 텍스처 메모리 추정.
    /// 전체 검증은 tools/csharp_check/polish_check(243개)가 하고, 여기서는 Unity 안에서 같은 순수 로직이 돌아가는지 핵심만 확인한다.
    /// </summary>
    public sealed class StageFiveTests
    {
        private static string Res(string relative)
        {
            return Path.Combine(Application.dataPath, "_Project/Resources/" + relative);
        }

        private sealed class OutfitSink : IVnCommandSink, IVnOutfitSink
        {
            public readonly List<string> Log = new List<string>();
            public void ShowStanding(string c, string e, string o) { Log.Add("stand:" + c + ":" + e + ":" + o); }
            public void ShowStandingAuto(string c, string e) { Log.Add("auto:" + c + ":" + e); }
            public void HideStanding(string c) { }
            public void ShowCg(string c, int n) { }
            public void ShowCommonCg(string k) { }
            public void HideCg() { }
            public void SetBackground(string p, string t) { }
        }

        // ---------------- 오디오 ----------------

        [Test]
        public void AudioCurve_SquareLawAndDb()
        {
            Assert.AreEqual(0.25f, AudioMixerModel.SliderToAmplitude(0.5f), 1e-6f);
            Assert.AreEqual(-12.04f, AudioMixerModel.SliderToDb(0.5f), 0.02f);
            Assert.AreEqual(-80f, AudioMixerModel.SliderToDb(0f));
            Assert.AreEqual(0.5f, AudioMixerModel.DbToSlider(AudioMixerModel.SliderToDb(0.5f)), 1e-3f);
        }

        [Test]
        public void AudioCrossfade_ConstantPower()
        {
            for (int i = 0; i <= 10; i++)
            {
                float o, n;
                AudioMixerModel.CrossfadeGains(i / 10f, out o, out n);
                Assert.AreEqual(1f, o * o + n * n, 1e-5f);
            }
        }

        [Test]
        public void AudioMixer_MuteDuckingAndMaster()
        {
            AudioMixerModel m = new AudioMixerModel();
            float bgm = m.Gain(AudioChannel.Bgm);
            m.SetDucking(true);
            m.Tick(1f);
            Assert.Less(m.Gain(AudioChannel.Bgm), bgm);
            Assert.AreEqual(m.Gain(AudioChannel.Se), 0.81f, 1e-5f);
            m.Muted = true;
            Assert.AreEqual(0f, m.Gain(AudioChannel.Bgm));
        }

        [Test]
        public void AudioThrottle_BlocksFastRepeats()
        {
            AudioThrottle t = new AudioThrottle();
            Assert.IsTrue(t.TryPass("typewriter", 1f));
            Assert.IsFalse(t.TryPass("typewriter", 1.02f));
            Assert.IsTrue(t.TryPass("typewriter", 1.1f));
        }

        [Test]
        public void Manifest_RealFileParsesAndMatchesCues()
        {
            AudioCatalog c = AudioCatalog.Parse(File.ReadAllText(Res("Audio/audio_manifest.csv")));
            Assert.AreEqual(0, c.Errors.Count);
            Assert.AreEqual(13, c.Ids(AudioKind.Bgm).Count);
            Assert.AreEqual(13, c.Ids(AudioKind.Se).Count);
            Assert.IsTrue(c.Has(AudioCues.BgmTitle, AudioKind.Bgm));
            Assert.IsTrue(c.Has(AudioCues.SeClick, AudioKind.Se));
            Assert.IsTrue(c.Has(AudioCues.BgmForEnding(MorpheHouse.Romance.EndingKind.HaremTrio), AudioKind.Bgm));
        }

        [Test]
        public void Manifest_BadRowsAreReportedNotThrown()
        {
            string csv = "id,kind,path,loop,default_volume,notes\n" +
                "ok,se,Audio/SE/ok,0,1,\n" +
                ",se,Audio/SE/x,0,1,\n" +
                "bad_kind,music,Audio/x,0,1,\n" +
                "bad_path,se,/abs,0,1,\n" +
                "ok,se,Audio/SE/dup,0,1,\n";
            AudioCatalog c = AudioCatalog.Parse(csv);
            Assert.AreEqual(1, c.Count);
            Assert.AreEqual(4, c.Errors.Count);
            Assert.AreEqual(0, AudioCatalog.Parse(null).Count);
        }

        // ---------------- 문자열 / 설정 ----------------

        [Test]
        public void UiStrings_RealFilesAreComplete()
        {
            UiStrings u = new UiStrings();
            u.LoadLocale("ko", File.ReadAllText(Res("Localization/ui_ko.csv")));
            u.LoadLocale("en", File.ReadAllText(Res("Localization/ui_en.csv")));
            Assert.AreEqual(0, u.Errors.Count);
            Assert.AreEqual(u.Count("ko"), u.Count("en"));
            Assert.AreEqual(0, u.MissingIn("ko", "en").Count);
            Assert.AreEqual(0, u.PlaceholderMismatches("ko", "en").Count);
        }

        [Test]
        public void UiStrings_FallbackOrder()
        {
            UiStrings u = new UiStrings();
            u.LoadLocale("ko", "key,text\nA,가\nB,나\n");
            u.LoadLocale("en", "key,text\nA,a\nB,\n");
            u.SetLocale("en");
            Assert.AreEqual("a", u.Get("A"));
            Assert.AreEqual("나", u.Get("B"));
            Assert.AreEqual("기본", u.Get("C", "기본"));
            Assert.AreEqual("C", u.Get("C"));
        }

        [Test]
        public void Settings_NewFieldsAndLocalePersist()
        {
            MemorySettingsStorage mem = new MemorySettingsStorage();
            SettingsService s = new SettingsService(mem);
            s.SetLocale("en");
            s.SetBgmVolume(0.3f);
            s.SetUiScale(1.07f);
            s.SetFullscreen(false);
            s.SetAutoDelay(2f);
            Assert.IsTrue(s.Save());
            SettingsService r = new SettingsService(mem);
            Assert.IsTrue(r.Load());
            Assert.AreEqual("en", r.Locale);
            Assert.AreEqual(0.3f, r.BgmVolume, 1e-3f);
            Assert.AreEqual(1.1f, r.UiScale, 1e-6f);
            Assert.IsFalse(r.Fullscreen);
            Assert.AreEqual(2f, r.AutoDelay, 1e-3f);
        }

        [Test]
        public void Settings_OldFileStillLoads()
        {
            MemorySettingsStorage old = new MemorySettingsStorage();
            old.Text = "{ \"textSpeed\": 50, \"masterVolume\": 0.5, \"locale\": \"ko\" }";
            SettingsService s = new SettingsService(old);
            Assert.IsTrue(s.Load());
            Assert.AreEqual(1f, s.UiScale);
            Assert.IsTrue(s.Fullscreen);
        }

        // ---------------- 의상 정책 / 커맨드 ----------------

        [Test]
        public void OutfitPolicy_Rules()
        {
            Assert.AreEqual("Uniform", OutfitPolicy.Resolve("howl", null, "Ch1_Intro", "Cafe_Interior", "Day"));
            Assert.AreEqual("Casual", OutfitPolicy.Resolve("johan", null, "Ch1_Intro", "Cafe_Interior", "Day"));
            Assert.AreEqual("Casual", OutfitPolicy.Resolve("howl", null, "Howl_Talk_01", "Cafe_Interior", "Night"));
            Assert.AreEqual("Date", OutfitPolicy.Resolve("howl", null, "Howl_Date_Lighthouse", "Cafe_Interior", "Day"));
            Assert.AreEqual("Casual", OutfitPolicy.Resolve("howl", "casual", "Ch1_Intro", "Cafe_Interior", "Day"));
            Assert.AreEqual("Uniform", OutfitPolicy.Resolve("johan", "Uniform", "Howl_Date_X", "Street", "Night"));
        }

        [Test]
        public void Dispatcher_ExplicitOutfitWinsAndAudioIsOptional()
        {
            // 디스패처는 게임 상태가 필요하다. 상태 없이 확인 가능한 부분만: IsKnown 과 정책 경로 구분은 polish_check 하네스가 전수 검증한다.
            Assert.IsTrue(DialogueCommandDispatcher.IsKnown("bgm"));
            Assert.IsTrue(DialogueCommandDispatcher.IsKnown("bgm_stop"));
            Assert.IsTrue(DialogueCommandDispatcher.IsKnown("se"));
            Assert.IsTrue(DialogueCommandDispatcher.IsKnown("route_choice"));
        }

        // ---------------- 배치 / 입력 ----------------

        [Test]
        public void Layout_AllPanelsFit16x9And20x9()
        {
            Assert.AreEqual(0, LayoutRules.Check(LayoutRules.Screen16x9, 1f).Count);
            Assert.AreEqual(0, LayoutRules.Check(LayoutRules.Screen20x9, 1f).Count);
            Assert.IsFalse(LayoutRules.AllowedUiScales(LayoutRules.Screen20x9).Contains(1.1f));
            Assert.AreEqual(1.0f, LayoutRules.EffectiveUiScale(1.1f, LayoutRules.Screen20x9));
        }

        [Test]
        public void Layout_AllButtonsAtLeast48()
        {
            Assert.AreEqual(0, LayoutRules.CheckButtons(LayoutRules.Buttons).Count);
            Assert.AreEqual(48f, LayoutRules.ClampTouch(20f));
        }

        [Test]
        public void TouchGesture_TapAndLongPress()
        {
            TouchGesture g = new TouchGesture();
            g.Down(0f, 0f, 0f, true);
            Assert.AreEqual(GestureEvent.Tap, g.Up(0.1f));
            g.Down(1f, 0f, 0f, true);
            Assert.AreEqual(GestureEvent.LongPressBegin, g.Tick(1.6f));
            Assert.AreEqual(GestureEvent.LongPressEnd, g.Up(2f));
            g.Down(3f, 0f, 0f, false);
            Assert.AreEqual(GestureEvent.None, g.Tick(4f));
            Assert.AreEqual(GestureEvent.Tap, g.Up(4.1f));
        }

        [Test]
        public void BackKeyRules_Decisions()
        {
            Assert.AreEqual(BackAction.OpenPause, BackKeyRules.Decide(BackScreen.Game, false, false, false, false));
            Assert.AreEqual(BackAction.ClosePause, BackKeyRules.Decide(BackScreen.Vn, true, false, false, false));
            Assert.AreEqual(BackAction.PauseBackToMenu, BackKeyRules.Decide(BackScreen.Game, true, true, false, false));
            Assert.AreEqual(BackAction.CloseConfirm, BackKeyRules.Decide(BackScreen.Game, true, false, true, false));
            Assert.AreEqual(BackAction.None, BackKeyRules.Decide(BackScreen.Title, false, false, false, false));
            Assert.IsTrue(BackKeyRules.CanSaveNow(BackScreen.Game, true, false));
            Assert.IsFalse(BackKeyRules.CanSaveNow(BackScreen.Game, false, false));
        }

        // ---------------- 메모리 ----------------

        [Test]
        public void MemoryBudget_BlockMathAndPeakScene()
        {
            Assert.AreEqual(2073600L, MemoryBudget.TextureBytes(1920, 1080, TexFormat.Bc7, false));
            Assert.AreEqual(935712L, MemoryBudget.TextureBytes(1024, 2048, TexFormat.Astc6x6, false));

            string csv = File.ReadAllText(Path.Combine(Application.dataPath, "../../docs/art_checklist.csv"));
            List<string> errors = new List<string>();
            List<ArtRow> rows = MemoryBudget.ParseChecklist(csv, errors);
            Assert.AreEqual(0, errors.Count);
            SceneEstimate pc = MemoryBudget.PeakScene(rows, PlatformProfile.Pc());
            SceneEstimate android = MemoryBudget.PeakScene(rows, PlatformProfile.Android());
            Assert.Less(android.TotalBytes, pc.TotalBytes);
            Assert.IsTrue(MemoryBudget.FitsBudget(android.TotalBytes, PlatformProfile.Android(), 0.25));
        }
    }
}
