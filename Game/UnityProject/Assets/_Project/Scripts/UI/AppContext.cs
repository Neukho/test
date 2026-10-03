using System;
using System.Collections.Generic;
using MorpheHouse.ArtPipeline;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using MorpheHouse.Save;
using MorpheHouse.Tycoon;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 앱 전역 컨텍스트: 현재 게임 세션(상태·밸런스·사이클)과 저장 서비스, 화면 전환을 묶는다.
    /// 화면은 이 객체를 통해서만 게임 로직에 접근한다.
    /// </summary>
    public sealed class AppContext
    {
        public const string AppVersion = "0.2.0";

        public readonly MonoBehaviour Host;
        public readonly ITextSource TextSource;
        public readonly SaveService Saves;
        public readonly ScreenManager Nav = new ScreenManager();

        public readonly SettingsService Settings;
        public readonly DialogueDirector Dialogue;

        /// <summary>플레이 간 이어지는 프로필(도달 엔딩·본 CG·해금). profile.json.</summary>
        public readonly ProfileService Profile;
        public readonly UnlockService Unlocks;

        /// <summary>호스트가 호출하는 복구 장면 규칙표(밤 메뉴 항목).</summary>
        public readonly RecoveryTable Recovery = RecoveryTable.Default();

        /// <summary>이번 실행에서 본 CG id(대문자, 예 HOWL_E1). 저장·불러오기를 넘어 이어지지는 않는다.</summary>
        public readonly HashSet<string> RunCgs = new HashSet<string>();

        public TitleScreen Title;
        public GameScreen Game;
        public CharacterCreateScreen Create;
        public VnScreen Vn;
        public RouteSelectScreen RouteSelect;
        public EndScreen End;
        public DemoEndScreen DemoEnd;

        /// <summary>오디오 서비스(GameBootstrap 이 만든다). 없으면(null) 모든 소리 호출이 조용히 무시된다.</summary>
        public AudioService Audio;

        /// <summary>UI 크기·전체 화면 적용(GameBootstrap 이 만든다).</summary>
        public DisplayService Display;

        /// <summary>일시정지 메뉴(GameBootstrap 이 만든다).</summary>
        public PauseMenu Pause;

        public BalanceTable Balance { get; private set; }
        public GameState State { get; private set; }
        public DayCycle Cycle { get; private set; }

        /// <summary>연애 콘텐츠 표(characters/prefs/gifts/spots). 로드 실패 시 null(선물·데이트만 막힌다).</summary>
        public RomanceCatalog Catalog { get; private set; }
        public RomanceService Romance { get; private set; }

        /// <summary>엔딩 조건 표(balance/endings.csv). 로드 실패 시 null(엔딩 화면 대신 기본 종료 카드).</summary>
        public EndingTable Endings { get; private set; }

        /// <summary>마지막으로 계획한 엔딩(엔딩 화면이 읽는다). 엔딩 흐름이 아닐 때는 null.</summary>
        public EndingPlan CurrentEnding { get; private set; }
        public NightRomanceActions Night { get; private set; }

        /// <summary>타이틀에서 고른 난이도(주인공 생성 화면이 끝나면 이 값으로 새 게임을 시작).</summary>
        public Difficulty PendingDifficulty { get; private set; }

        public AppContext(MonoBehaviour host, ITextSource textSource, SaveService saves, SettingsService settings, ProfileService profile)
        {
            Host = host;
            TextSource = textSource;
            Saves = saves;
            Settings = settings;
            Profile = profile ?? new ProfileService(null);
            Unlocks = UnlockService.WithDefaults(Profile);
            EventBus.Subscribe<DayPhaseChanged>(OnPhaseChanged);

            try
            {
                Catalog = RomanceCatalog.Load(textSource);
            }
            catch (Exception ex)
            {
                Debug.LogError("[AppContext] 연애 카탈로그 로드 실패: " + ex.Message);
            }

            try
            {
                Endings = EndingTable.Load(textSource);
            }
            catch (Exception ex)
            {
                Debug.LogError("[AppContext] 엔딩 표 로드 실패: " + ex.Message);
            }

            Dialogue = new DialogueDirector(this);
            Dialogue.LoadResources();
            UiStrings.Current.SetLocale(Settings.Locale);
            Settings.Changed += OnSettingsChanged;
            Nav.Changed += OnScreenChanged;
        }

        // ---------------- 소리 ----------------

        /// <summary>효과음 한 번. 오디오 서비스가 없거나 소리 파일이 없으면 아무 일도 없다.</summary>
        public void PlaySe(string id)
        {
            if (Audio != null) Audio.PlaySe(id);
        }

        /// <summary>BGM 교차 페이드 재생(같은 곡이면 무시).</summary>
        public void PlayBgm(string id)
        {
            if (Audio != null) Audio.PlayBgm(id);
        }

        // 화면이 바뀔 때의 BGM: 타이틀·주인공 생성 = title, 루트 선택 = event_tense, 엔딩 = 엔딩 종류별.
        // 게임 화면은 GameScreen 이 단계(낮/영업/저녁/밤)마다 고르고, VN 은 이전 곡을 이어 간다(대사의 <<bgm>> 이 바꿀 수 있다).
        private void OnScreenChanged(ScreenBase screen)
        {
            if (screen != Nav.Current) return; // 중첩 전환(화면을 보이는 도중 다른 화면으로 넘어간 경우)에서는 마지막 화면만
            ArtLoader.OnScreenTransition(); // 아트 캐시(LRU)에서 밀려난 플레이스홀더 파괴 + (제한적으로) UnloadUnusedAssets
            if (screen == Title || screen == Create) PlayBgm(AudioCues.BgmTitle);
            else if (screen == RouteSelect) PlayBgm(AudioCues.BgmRouteSelect);
            else if (screen == End && CurrentEnding != null) PlayBgm(AudioCues.BgmForEnding(CurrentEnding.Kind));
        }

        // ---------------- 언어 ----------------

        // 언어가 바뀌면 문자열 표의 언어를 바꾸고, 표의 글자와 같은 모든 라벨을 새 언어로 다시 쓴다(보이지 않는 화면 포함).
        // 대사 세션의 호칭 언어는 다음 장면을 시작할 때 DialogueDirector 가 다시 맞춘다(재생 중인 대사는 건드리지 않는다).
        private void OnSettingsChanged()
        {
            string locale = UiStrings.NormalizeLocale(Settings.Locale);
            if (locale == UiStrings.Current.Locale) return;
            UiStrings.Current.SetLocale(locale);
            LocalizedLabel.RefreshAll();
            if (Nav.Current != null) Nav.Current.OnLocaleChanged();
        }

        // ---------------- 뒤로가기 · 일시정지 ----------------

        /// <summary>현재 화면 종류(뒤로가기 규칙 판정용).</summary>
        public BackScreen CurrentBackScreen()
        {
            ScreenBase c = Nav.Current;
            if (c == null || c == Title) return BackScreen.Title;
            if (c == Create) return BackScreen.Create;
            if (c == Game) return BackScreen.Game;
            if (c == Vn) return BackScreen.Vn;
            if (c == RouteSelect) return BackScreen.RouteSelect;
            return BackScreen.End;
        }

        /// <summary>뒤로가기 입력 처리(Esc / Android 뒤로 키 / 게임패드 B). 결정은 순수 BackKeyRules 가 한다.</summary>
        public void HandleBack()
        {
            bool pauseOpen = Pause != null && Pause.IsOpen;
            bool canHandle = Nav.Current != null && Nav.Current.CanHandleBack;
            BackAction action = BackKeyRules.Decide(CurrentBackScreen(), pauseOpen,
                pauseOpen && Pause.SubPanelOpen, pauseOpen && Pause.ConfirmOpen, canHandle);
            switch (action)
            {
                case BackAction.OpenPause:
                    if (Pause != null) Pause.Open();
                    break;
                case BackAction.ScreenHandles:
                    Nav.Current.HandleBack();
                    break;
                case BackAction.None:
                    break;
                default:
                    if (Pause != null) Pause.HandleBack();
                    break;
            }
        }

        /// <summary>화면의 메뉴 버튼(터치용)이 호출한다.</summary>
        public void OpenPause()
        {
            if (Pause != null) Pause.Open();
        }

        /// <summary>
        /// 진행 중이던 판을 버리고 타이틀로 간다(일시정지 메뉴의 "타이틀로" 확인 후). VN 장면 중이면 재생을 중단한다.
        /// 저장은 하지 않는다 — 마지막 아침 자동 저장이 이어하기에 쓰인다.
        /// </summary>
        public void ReturnToTitle()
        {
            if (Vn != null) Vn.Abort();
            ExitToTitle();
        }

        /// <summary>인물 id → 표시 이름(카탈로그가 없으면 id 그대로).</summary>
        public string CharDisplayName(string charId)
        {
            CharacterRow row = Catalog == null ? null : Catalog.FindCharacter(charId);
            return Loc.CharName(charId, row != null ? row.NameKo : charId);
        }

        /// <summary>난이도 선택 직후: 주인공 생성 화면으로 이동.</summary>
        public void OpenCharacterCreate(Difficulty difficulty)
        {
            PendingDifficulty = difficulty;
            Nav.Show(Create);
        }

        /// <summary>기본 주인공으로 바로 시작(개발·테스트용).</summary>
        public void StartNewGame(Difficulty difficulty)
        {
            StartNewGame(difficulty, ProtagonistProfile.Default());
        }

        public void StartNewGame(Difficulty difficulty, ProtagonistProfile profile)
        {
            Balance = BalanceTable.Load(TextSource, difficulty);
            State = GameState.NewGame(Balance, difficulty, profile);
            if (DemoConfig.Current.Enabled) DemoConfig.MarkFlag(State.Flags); // 데모 저장 표시(정식 버전은 그대로 불러온다)
            RunCgs.Clear();
            Cycle = new DayCycle(State, Balance, new SystemRandomAdapter(Environment.TickCount));
            BindSession();
            Cycle.Begin();
            ResetSession();
            Nav.Show(Game);
        }

        /// <summary>현재 State 에 맞는 연애 서비스·밤 컨트롤러·대화 세션을 만든다(새 게임/불러오기 공통).</summary>
        private void BindSession()
        {
            System.Random rng = new System.Random(Environment.TickCount * 31 + 7);
            Romance = new RomanceService(State, Balance, Catalog, rng.NextDouble);
            Dialogue.Bind(State, Romance);
            Night = new NightRomanceActions(State, Balance, Romance, Catalog, Dialogue.HasNode, Dialogue.HasVisited);
        }

        /// <summary>매 프레임(GameBootstrap.Update): 보이는 화면의 Tick 을 호출한다.</summary>
        public void Tick(float deltaTime)
        {
            PollBackKey();
            if (Display != null) Display.Tick();
            if (Pause != null && Pause.IsOpen) return; // 일시정지 중에는 화면(대사·타자기·연출)을 멈춘다.
            Nav.Tick(deltaTime);
        }

        // Esc 는 데스크톱 키보드와 Android 뒤로 키(Input System 이 Escape 로 보고)를 함께 덮는다. 게임패드 B 도 같은 동작.
        private void PollBackKey()
        {
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (back) HandleBack();
        }

        /// <summary>
        /// 가장 최근 저장(슬롯 0~3)을 불러온다. 성공하면 게임 화면으로 이동.
        /// 보안 검토 N-6: ① 슬롯 하나의 읽기·상태 복원이 예외를 던져도 타이틀이 죽지 않는다(그 슬롯은 손상으로 보고 다른 슬롯을 계속 본다).
        /// ② "가장 최근"은 SavedAtUtc 를 DateTime 으로 파싱해 고른다(SaveRecency; 파싱 실패 = 가장 오래됨, 문자열 비교 아님).
        /// 사용할 수 있는 저장이 없으면 첫 오류 사유를 돌려준다.
        /// </summary>
        public LoadResult LoadLatest()
        {
            List<LoadResult> usable = new List<LoadResult>();
            LoadResult firstError = null;
            for (int slot = 0; slot <= SaveService.MaxSlot; slot++)
            {
                LoadResult r;
                try
                {
                    if (!Saves.Exists(slot)) continue;
                    r = Saves.Load(slot);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AppContext] 슬롯 " + slot + " 읽기 중 예외(손상으로 처리): " + ex.GetType().Name);
                    r = new LoadResult { Status = LoadStatus.Corrupt };
                }
                if (r == null) r = new LoadResult { Status = LoadStatus.Corrupt };
                bool ok = (r.Status == LoadStatus.Ok || r.Status == LoadStatus.RestoredFromBackup) && r.Data != null;
                if (ok) usable.Add(r);
                else if (firstError == null && r.Status != LoadStatus.NotFound)
                    firstError = r.Status == LoadStatus.Ok || r.Status == LoadStatus.RestoredFromBackup ? new LoadResult { Status = LoadStatus.Corrupt } : r;
            }

            // 최근 순(같으면 앞 슬롯 먼저)으로 시도한다. 복원에 실패하면 다음 후보로.
            List<LoadResult> order = new List<LoadResult>();
            for (int i = 0; i < usable.Count; i++)
            {
                int at = order.Count;
                while (at > 0 && SaveRecency.IsNewer(usable[i].Data.SavedAtUtc, order[at - 1].Data.SavedAtUtc)) at--;
                order.Insert(at, usable[i]);
            }
            for (int i = 0; i < order.Count; i++)
            {
                if (TryStartFromSave(order[i])) return order[i];
                if (firstError == null) firstError = new LoadResult { Status = LoadStatus.Corrupt };
            }
            return firstError ?? new LoadResult { Status = LoadStatus.NotFound };
        }

        // 저장 한 건으로 세션을 만든다. 예외가 나면 이전 세션 값을 되돌리고 false(화면 전환 없음).
        private bool TryStartFromSave(LoadResult best)
        {
            BalanceTable balance;
            GameState state;
            DayCycle cycle;
            try
            {
                balance = BalanceTable.Load(TextSource, (Difficulty)best.Data.Difficulty);
                state = SaveMapper.FromSave(best.Data);
                cycle = new DayCycle(state, balance, new SystemRandomAdapter(Environment.TickCount));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AppContext] 저장 복원 실패(손상으로 처리): " + ex.GetType().Name);
                return false;
            }

            BalanceTable oldBalance = Balance;
            GameState oldState = State;
            DayCycle oldCycle = Cycle;
            try
            {
                Balance = balance;
                State = state;
                Cycle = cycle;
                RunCgs.Clear();
                BindSession();
                Dialogue.ImportVisited(State.VisitedNodes);
                Cycle.Resume();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AppContext] 저장 적용 실패(손상으로 처리): " + ex.GetType().Name);
                Balance = oldBalance;
                State = oldState;
                Cycle = oldCycle;
                if (oldState != null)
                {
                    try { BindSession(); } catch (Exception) { /* 이전 세션도 못 되살리면 타이틀에 머문다 */ }
                }
                return false;
            }
            ResetSession();
            // 변조·손상된 원본 대신 백업으로 복구했다면 조용히 넘기지 않고 알린다(QA-06: 호출자에게 PrimaryRejected 로 전달됨).
            if (best.Status == LoadStatus.RestoredFromBackup && best.PrimaryRejected && Game != null)
                Game.QueueToast(Loc.Tr(UiText.LoadRestored));
            Nav.Show(Game);
            return true;
        }

        public bool HasAnySave()
        {
            for (int slot = 0; slot <= SaveService.MaxSlot; slot++)
                if (Saves.Exists(slot)) return true;
            return false;
        }

        /// <summary>수동 저장. 아침에만 가능하다.</summary>
        public bool SaveGame(int slot)
        {
            try
            {
                Dialogue.SyncVisitedToState();
                Saves.Save(slot, SaveMapper.ToSave(State, slot, AppVersion));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        /// <summary>
        /// 판이 바뀔 때(StartNewGame / 불러오기 성공 / ExitToTitle) 한 번 호출: 이전 판의 화면 상태·엔딩 계획·이번 실행 CG 목록을 비운다(QA-10).
        /// 이벤트 구독(DayPhaseChanged)은 AppContext 생성 때 한 번만 하므로 누적되지 않는다. 대사 방문 기록은 Dialogue.Bind / ImportVisited 가 맡는다.
        /// </summary>
        public void ResetSession()
        {
            CurrentEnding = null;
            RunCgs.Clear();
            if (Game != null) Game.ResetSession();
        }

        public void ExitToTitle()
        {
            ResetSession();
            Nav.Show(Title);
        }

        /// <summary>데모 종료 화면을 연다(DemoConfig 가 켜져 있고 마지막 데모 챕터를 마쳤을 때 GameScreen 이 호출).</summary>
        public void ShowDemoEnd()
        {
            if (DemoEnd == null) { ExitToTitle(); return; }
            Nav.Show(DemoEnd);
        }

        /// <summary>루트 선택 화면을 연다(대사의 route_choice 또는 7~8장 밤 메뉴).</summary>
        public void OpenRouteSelect()
        {
            if (RouteSelect == null || State == null) return;
            Nav.Show(RouteSelect);
        }

        /// <summary>대사가 CG 를 띄울 때 호출: 이번 실행 목록과 프로필(누적)에 기록한다.</summary>
        public void NoteCgSeen(string id)
        {
            RunCgs.Add(id);
            if (Profile.RecordCg(id)) Profile.Save();
        }

        /// <summary>
        /// 8챕터 마지막 정산 직후 호출: (남은) Ch8_Outro → 엔딩 장면들 → 엔딩 화면. 엔딩 표가 없으면 false(호출자가 기본 종료 카드를 보여 준다).
        /// 엔딩 도달은 장면을 재생하기 전에 프로필에 기록한다(중간에 종료해도 남도록).
        /// </summary>
        public bool BeginEnding()
        {
            if (Endings == null || State == null) return false;
            CurrentEnding = EndingPlan.Create(State, Endings, Catalog, Dialogue.HasNode);

            List<SceneItem> scenes = new List<SceneItem>();
            string outro = ChapterFlow.FinalOutro(Balance.MaxChapter, Dialogue.HasNode, Dialogue.HasVisited);
            if (outro != null) scenes.Add(SceneItem.Of(outro, true, string.Empty));
            scenes.AddRange(EndingFlow.Scenes(CurrentEnding, Dialogue.HasNode));

            Profile.RecordEnding(CurrentEnding.Result);
            Unlocks.Evaluate();
            Profile.Save();

            PlayBgm(AudioCues.BgmForEnding(CurrentEnding.Kind));
            Dialogue.PlayScenes(scenes, delegate { if (End != null) Nav.Show(End); else Nav.Show(Title); }, false);
            return true;
        }

        // 매일 아침 시작 시 자동 저장(슬롯 0).
        private void OnPhaseChanged(DayPhaseChanged e)
        {
            if (e.Phase != DayPhase.Morning || e.Day <= 1 || State == null || State.Finished) return;
            SaveGame(SaveService.AutoSlot);
        }
    }
}
