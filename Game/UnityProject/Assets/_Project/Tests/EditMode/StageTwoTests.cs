using System.Collections.Generic;
using System.IO;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using MorpheHouse.Save;
using NUnit.Framework;
using UnityEngine;

namespace MorpheHouse.Tests
{
    /// <summary>2단계 EditMode 테스트: 주인공 검증, 저장 확장, 커맨드 디스패치, 밤 행동, 설정, 스크립트 로더.</summary>
    public sealed class StageTwoTests
    {
        private sealed class Sink : IVnCommandSink
        {
            public readonly List<string> Log = new List<string>();
            public void ShowStanding(string c, string e, string o) { Log.Add("stand:" + c + ":" + e + ":" + o); }
            public void HideStanding(string c) { Log.Add("hide:" + c); }
            public void ShowCg(string c, int n) { Log.Add("cg:" + c + ":" + n); }
            public void ShowCommonCg(string k) { Log.Add("ccg:" + k); }
            public void HideCg() { Log.Add("hidecg"); }
            public void SetBackground(string p, string t) { Log.Add("bg:" + p + ":" + t); }
        }

        private static ITextSource Source()
        {
            return new FileTextSource(Path.Combine(Application.dataPath, "_Project/Resources/Balance"));
        }

        private static GameState NightState(out BalanceTable balance, out RomanceCatalog catalog, out RomanceService romance, int chapter)
        {
            balance = BalanceTable.Load(Source(), Difficulty.Normal);
            catalog = RomanceCatalog.Load(Source());
            GameState s = GameState.NewGame(balance, Difficulty.Normal, "민준");
            s.Chapter = chapter;
            s.Day = 10;
            s.Phase = DayPhase.Night;
            s.ActionsLeft = 3;
            s.Fatigue = 0f;
            s.Gold = 5000f;
            romance = new RomanceService(s, balance, catalog, new System.Random(1).NextDouble);
            return s;
        }

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
        }

        [Test]
        public void ProfileValidator_Rules()
        {
            Assert.IsTrue(ProfileValidator.Validate(ProtagonistProfile.Default()).IsValid);
            Assert.AreEqual("민준 하늘", ProfileValidator.Validate("  민준   하늘 ", "male", "wolf", 3).Profile.Name);
            Assert.AreEqual(ProfileValidator.ErrNameEmpty, ProfileValidator.Validate("  ", "male", "human", 1).NameError);
            Assert.AreEqual(ProfileValidator.ErrNameTooLong, ProfileValidator.Validate("가나다라마바사아자차카타파", "male", "human", 1).NameError);
            Assert.AreEqual(ProfileValidator.ErrNameChar, ProfileValidator.Validate("a<b>", "male", "human", 1).NameError);
            Assert.AreEqual(ProfileValidator.ErrGender, ProfileValidator.Validate("a", "x", "human", 1).GenderError);
            Assert.AreEqual(ProfileValidator.ErrSpecies, ProfileValidator.Validate("a", "male", "dragon", 1).SpeciesError);
            Assert.AreEqual(ProfileValidator.ErrAppearance, ProfileValidator.Validate("a", "male", "human", 7).AppearanceError);
        }

        [Test]
        public void Save_RoundTrips_StageTwoFields_And_OldSavesStayValid()
        {
            BalanceTable b; RomanceCatalog c; RomanceService r;
            GameState s = NightState(out b, out c, out r, 7);
            s.Phase = DayPhase.Morning;
            s.PlayerSpecies = "tiger";
            s.PlayerAppearance = 5;
            s.VisitedNodes.Add("Ch1_Intro");
            r.SetFlag("howl_e1_trust");
            SaveData d = SaveMapper.ToSave(s, 1, "t");
            Assert.IsNull(SaveValidator.Validate(d));
            GameState back = SaveMapper.FromSave(d);
            Assert.AreEqual("tiger", back.PlayerSpecies);
            Assert.AreEqual(5, back.PlayerAppearance);
            Assert.AreEqual("Ch1_Intro", back.VisitedNodes[0]);
            Assert.AreEqual(1, back.Flags["howl_e1_trust"]);

            SaveData old = new SaveData();
            old.PlayerName = "x";
            old.Reputation = 20f;
            old.Fatigue = 10f;
            Assert.IsNull(SaveValidator.Validate(old));
            d.PlayerSpecies = "dragon";
            Assert.AreEqual("species", SaveValidator.Validate(d));
        }

        [Test]
        public void Dispatcher_RunsCommands_And_NeverThrows()
        {
            BalanceTable b; RomanceCatalog c; RomanceService r;
            GameState s = NightState(out b, out c, out r, 3);
            Sink sink = new Sink();
            DialogueCommandDispatcher d = new DialogueCommandDispatcher(s, r, sink);
            d.Handle("aff", new[] { "howl", "3" });
            Assert.Greater(s.Chars["howl"].Aff, 0f);
            d.Handle("flag", new[] { "met_howl" });
            Assert.AreEqual(1, s.Flags["met_howl"]);
            d.Handle("aff", new[] { "ghost", "1" });
            d.Handle("bg", new[] { "atlantis", "night" });
            Assert.AreEqual(2, d.Warnings.Count);
            CollectionAssert.Contains(sink.Log, "bg:Cafe_Interior:Night");
            d.Handle("show_standing", new[] { "howl", "smile" });
            CollectionAssert.Contains(sink.Log, "stand:howl:Smile:Casual");
            d.Handle("bg", new[] { "harbor", "dusk" });
            CollectionAssert.Contains(sink.Log, "bg:Street:Evening");
        }

        [Test]
        public void EndEvent_IsIdempotent_AfterMenuCompletion()
        {
            BalanceTable b; RomanceCatalog c; RomanceService r;
            GameState s = NightState(out b, out c, out r, 3);
            s.Chars["howl"].Aff = 20f;
            DialogueCommandDispatcher d = new DialogueCommandDispatcher(s, r, new Sink());
            d.Handle("end_event", new[] { "howl", "1" });
            int actions = s.ActionsLeft;
            d.Handle("end_event", new[] { "howl", "1" });
            Assert.AreEqual(1, s.Chars["howl"].EventsDone);
            Assert.AreEqual(actions, s.ActionsLeft);
        }

        [Test]
        public void NightActions_PickNodes_And_Fallbacks()
        {
            BalanceTable b; RomanceCatalog c; RomanceService r;
            GameState s = NightState(out b, out c, out r, 1);
            HashSet<string> nodes = new HashSet<string> { "Howl_Talk_01", "Howl_Date_Generic", "Howl_Gift_Like" };
            NightRomanceActions n = new NightRomanceActions(s, b, r, c,
                delegate (string x) { return nodes.Contains(x); }, delegate (string x) { return false; });
            NightActionOutcome talk = n.Talk("howl");
            Assert.IsTrue(talk.Success);
            Assert.AreEqual("Howl_Talk_01", talk.NodeName);
            NightActionOutcome other = n.Talk("johan");
            Assert.IsNull(other.NodeName);
            Assert.IsNotEmpty(other.FallbackText);
            NightActionOutcome date = n.Date("howl", "beach");
            Assert.AreEqual("Howl_Date_Generic", date.NodeName);
            NightActionOutcome locked = n.Date("howl", "night_market");
            Assert.IsFalse(locked.Success);
        }

        [Test]
        public void Session_FallbackLine_WhenNodeMissing()
        {
            BalanceTable b; RomanceCatalog c; RomanceService r;
            GameState s = NightState(out b, out c, out r, 1);
            DialogueProject p = DialogueProject.Parse("title: A\n---\nhowl: hi\n===\n", "t");
            DialogueSession session = new DialogueSession(p, s, r, new AddressBook(), "ko", new Sink());
            session.Start("Nope", "howl", "대체");
            DialogueStep step = session.Next();
            Assert.IsTrue(step.IsLine);
            Assert.AreEqual("대체", step.Text);
            Assert.IsTrue(session.Next().IsEnd);
        }

        [Test]
        public void Settings_RoundTrip_And_Clamp()
        {
            MemorySettingsStorage mem = new MemorySettingsStorage();
            SettingsService a = new SettingsService(mem);
            a.SetTextSpeed(75f);
            a.SetMasterVolume(0.5f);
            a.SetLocale("en");
            Assert.IsTrue(a.Save());
            SettingsService b = new SettingsService(mem);
            Assert.IsTrue(b.Load());
            Assert.AreEqual(75f, b.TextSpeed);
            Assert.AreEqual("en", b.Locale);
            b.SetTextSpeed(99999f);
            Assert.IsTrue(b.InstantText);
        }

        [Test]
        public void Loader_ReportsBrokenFiles_And_Index()
        {
            List<KeyValuePair<string, string>> src = new List<KeyValuePair<string, string>>();
            src.Add(new KeyValuePair<string, string>("ok", "title: A\n---\nhowl: hi\n===\n"));
            src.Add(new KeyValuePair<string, string>("bad", "title: B\n---\n<<if x>>\nhowl: hi\n===\n"));
            DialogueLoadResult res = DialogueProjectLoader.Build(src);
            Assert.IsTrue(res.Project.Contains("A"));
            Assert.AreEqual(1, res.Errors.Count);
            Assert.AreEqual("routes__howl__x", DialogueProjectLoader.FlattenName("routes/howl/x.yarn"));
            CollectionAssert.AreEqual(new[] { "a", "b" }, DialogueProjectLoader.ParseIndex(DialogueProjectLoader.BuildIndex(new[] { "b", "a" })));
        }

        [Test]
        public void Typewriter_And_StandingState()
        {
            Typewriter t = new Typewriter();
            t.Reset("안녕하세요", 10f);
            t.Tick(0.25f);
            Assert.AreEqual(2, t.VisibleCount);
            t.Complete();
            Assert.IsTrue(t.IsDone);

            VnStandingState st = new VnStandingState();
            string ev;
            Assert.AreEqual(0, st.Show("howl", "Smile", "Casual", out ev));
            Assert.AreEqual(1, st.Show("johan", "Smile", "Casual", out ev));
            st.Show("howl", "Shy", "Casual", out ev);
            st.Show("haru", "Smile", "Casual", out ev);
            Assert.AreEqual("johan", ev);
        }
    }
}
