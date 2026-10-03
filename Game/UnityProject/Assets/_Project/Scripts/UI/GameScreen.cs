using System;
using System.Collections;
using System.Collections.Generic;
using MorpheHouse.ArtPipeline;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using MorpheHouse.Tycoon;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 하루 사이클 화면(아침 → 영업 → 밤 → 정산). 상단 HUD + 중앙 콘텐츠 패널을 단계마다 다시 만든다.
    /// 로직은 DayCycle(경영)과 NightRomanceActions(밤 연애 행동)가 담당하고, 이 클래스는 표시와 입력 전달만 한다.
    /// 밤 단계: 대화 · 선물 · 데이트 · 개인 이벤트 · 휴식 · 연구 · 밤 마무리 + 오른쪽 관계 패널 + 7장 이후 루트 확정.
    /// </summary>
    public sealed class GameScreen : ScreenBase
    {
        private const float DefaultButtonWidth = 980f;
        private const float DefaultButtonHeight = 72f;
        private const float DefaultSpacing = 14f;
        /// <summary>밤 메뉴에서 인라인으로 보여 줄 수 있는 복구/동의 항목 수(이보다 많으면 한 버튼으로 묶는다).</summary>
        private const int MaxInlineExtras = 2;

        private enum PickMode
        {
            Talk,
            Gift,
            Date,
            Event
        }

        private sealed class PickItem
        {
            public string Label;
            public bool Enabled = true;
            public bool Accent;
            public Action OnPick;
        }

        private Image _background;
        private TextMeshProUGUI _hudDay;
        private TextMeshProUGUI _hudStats;
        private RectTransform _content;
        private RectTransform _standingLayer;
        private TextMeshProUGUI _toast;
        private Coroutine _running;
        private bool _fast;
        private RelationPanel _side;
        private float _buttonWidth = DefaultButtonWidth;
        private float _buttonHeight = DefaultButtonHeight;
        private string _nextToast = string.Empty;
        private int _routePromptDay = -1;
        /// <summary>버튼을 붙일 부모(기본 _content, 모바일에서 넘치면 스크롤 열).</summary>
        private RectTransform _buttonParent;
        /// <summary>_content 안에서 머리(제목·정보 줄 + 간격)가 차지한 높이.</summary>
        private float _headerUsed;
        /// <summary>지금 화면 내용을 다시 그리는 동작(언어가 바뀔 때 호출). 영업 연출 중에는 null.</summary>
        private Action _rebuild;

        private readonly DayFlowDriver _flow;

        public GameScreen(AppContext ctx) : base(ctx)
        {
            // 공통 장면 스케줄(balance/scene_rules.csv, SceneGate)을 밤 컨트롤러에서 받는다. 순수 DayFlowDriver 가 판단하고 이 화면은 재생만 한다.
            _flow = new DayFlowDriver(delegate (SceneSlot slot) { return Ctx.Night != null ? Ctx.Night.NextCommonScene(slot) : ChapterStep.NoStep; },
                delegate (string node) { return Ctx.Dialogue.HasVisited(node); });
        }

        /// <summary>
        /// 판(세션)이 바뀔 때(새 게임·불러오기·타이틀 복귀) 이 화면이 들고 있던 "이번 판" 상태를 비운다(QA-10): 루트 안내를 띄운 날, 대기 중 토스트,
        /// 배속, 진행 중 영업 연출 코루틴, 다시 그리기 동작, 공통 장면 구동기의 막힘 방지 상태.
        /// </summary>
        public void ResetSession()
        {
            _routePromptDay = -1;
            _nextToast = string.Empty;
            _fast = false;
            _rebuild = null;
            _headerUsed = 0f;
            _buttonParent = null;
            if (_running != null && Ctx != null && Ctx.Host != null) Ctx.Host.StopCoroutine(_running);
            _running = null;
            _flow.Reset();
        }

        /// <summary>
        /// 슬롯의 다음 공통 장면이 있으면 VN 으로 재생하고 true. 재생이 끝나 게임 화면으로 돌아오면 ShowPhase 가 다시 이 함수를 불러
        /// 더 없을 때까지 이어진다(방문 기록은 DialogueDirector 가 재생 시작 때 남긴다 = 정확히 한 번).
        /// </summary>
        private bool PlayNextCommon(SceneSlot slot)
        {
            if (Ctx.State == null || Ctx.Night == null) return false;
            ChapterStep s = _flow.Next(slot);
            if (s.Kind != ChapterStepKind.CommonScene) return false;
            Ctx.Dialogue.Play(s.Node, null, null, null, null);
            return true;
        }

        /// <summary>다음에 게임 화면이 그려질 때 한 번 보여 줄 안내 문구(다른 화면에서 돌아올 때 사용).</summary>
        public void QueueToast(string text)
        {
            _nextToast = text ?? string.Empty;
        }

        protected override void OnBuild()
        {
            _background = UiKit.NewImage("Background", FullRoot, Color.white);
            UiKit.Stretch(_background.rectTransform);
            _background.preserveAspect = false;
            _background.raycastTarget = false;

            Image dim = UiKit.NewImage("Dim", FullRoot, UiTheme.Overlay);
            UiKit.Stretch(dim.rectTransform);
            dim.raycastTarget = false;

            // HUD (세이프 에어리어 안)
            Image hud = UiKit.NewImage("Hud", SafeRoot, UiTheme.Panel);
            UiKit.Place(hud.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 96f), Vector2.zero);
            hud.rectTransform.anchorMin = new Vector2(0f, 1f);
            hud.rectTransform.anchorMax = new Vector2(1f, 1f);
            hud.rectTransform.sizeDelta = new Vector2(0f, 96f);
            hud.raycastTarget = false;

            _hudDay = UiKit.Label(hud.transform, string.Empty, 36, UiTheme.Text, TextAlignmentOptions.MidlineLeft);
            _hudDay.rectTransform.anchorMin = new Vector2(0f, 0f);
            _hudDay.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            _hudDay.rectTransform.offsetMin = new Vector2(32f, 0f);
            _hudDay.rectTransform.offsetMax = Vector2.zero;

            _hudStats = UiKit.Label(hud.transform, string.Empty, 32, UiTheme.Accent, TextAlignmentOptions.MidlineRight);
            _hudStats.rectTransform.anchorMin = new Vector2(0.4f, 0f);
            _hudStats.rectTransform.anchorMax = new Vector2(1f, 1f);
            _hudStats.rectTransform.offsetMin = Vector2.zero;
            _hudStats.rectTransform.offsetMax = new Vector2(-160f, 0f);

            // 터치 기기에는 Esc 가 없으므로 HUD 에 일시정지 메뉴 버튼을 둔다(키보드는 Esc).
            // 모바일: 버튼이 물리 48 dp 로 커져 HUD(96)보다 높아지면 위쪽에 붙이고 아래로 늘린다.
            float menuH = Mathf.Max(64f, UiMetrics.MinButtonHeight);
            float menuAnchorY = menuH > 96f ? 1f : 0.5f;
            Button menu = UiKit.MakeButton(hud.transform, UiText.PauseButton, Ctx.OpenPause, 130f, menuH, UiTheme.Button);
            UiKit.Place((RectTransform)menu.transform, new Vector2(1f, menuAnchorY), new Vector2(1f, menuAnchorY), new Vector2(Mathf.Max(130f, menuH), menuH), new Vector2(-16f, 0f));

            // 스탠딩 레이어(왼쪽 아래) — 콘텐츠 패널 뒤에 그린다.
            _standingLayer = UiKit.NewRect("StandingLayer", SafeRoot);
            UiKit.Stretch(_standingLayer);

            // 중앙 콘텐츠 패널
            Image panel = UiKit.NewImage("ContentPanel", SafeRoot, UiTheme.Panel);
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1100f, 760f), new Vector2(160f, -30f));
            panel.raycastTarget = false;
            _content = panel.rectTransform;
            UiKit.VerticalLayout(_content, 14f, 36, TextAnchor.UpperLeft);

            _side = new RelationPanel(SafeRoot);
            _side.SetActive(false);

            _toast = UiKit.Label(SafeRoot, string.Empty, 30, UiTheme.Accent, TextAlignmentOptions.Center);
            UiKit.Place(_toast.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(1000f, 60f), new Vector2(160f, 20f));
        }

        /// <summary>밤에는 콘텐츠를 왼쪽으로 줄이고 오른쪽에 관계 패널을 연다. 다른 단계는 기본 배치.</summary>
        private void ApplyLayout(bool night)
        {
            Vector2 center = new Vector2(0.5f, 0.5f);
            VerticalLayoutGroup menuLayout = _content.GetComponent<VerticalLayoutGroup>();
            if (menuLayout != null) menuLayout.spacing = DefaultSpacing;
            if (night)
            {
                UiKit.Place(_content, center, center, new Vector2(840f, 790f), new Vector2(-120f, -48f));
                UiKit.Place(_toast.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(1000f, 60f), new Vector2(-120f, 14f));
                _buttonWidth = 740f;
                _buttonHeight = 56f;
            }
            else
            {
                UiKit.Place(_content, center, center, new Vector2(1100f, 760f), new Vector2(160f, -30f));
                UiKit.Place(_toast.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(1000f, 60f), new Vector2(160f, 20f));
                _buttonWidth = DefaultButtonWidth;
                _buttonHeight = DefaultButtonHeight;
            }
            _side.SetActive(night);
        }

        protected override void OnShow()
        {
            ShowPhase();
        }

        protected override void OnHide()
        {
            if (_running != null)
            {
                Ctx.Host.StopCoroutine(_running);
                _running = null;
            }
        }

        // ---------------- 공통 ----------------

        private void ShowPhase()
        {
            switch (Ctx.State.Phase)
            {
                case DayPhase.Night: ShowNight(); break;
                case DayPhase.Settlement: ShowSettlement(); break;
                default: ShowMorning(); break;
            }
        }

        private void Begin(string time)
        {
            _rebuild = null;
            ResetContent();
            UiKit.ClearChildren(_standingLayer);
            ApplyLayout(Ctx.State.Phase == DayPhase.Night);
            _toast.text = _nextToast;
            _nextToast = string.Empty;
            _background.sprite = ArtLoader.Background("Cafe_Interior", time);
            Ctx.PlayBgm(AudioCues.BgmForTime(time));
            RefreshHud();
        }

        private void ResetContent()
        {
            UiKit.ClearChildren(_content);
            _buttonParent = _content;
            _headerUsed = 0f;
        }

        /// <summary>언어가 바뀌면 지금 보이는 내용을 새 언어로 다시 그린다(영업 연출 중에는 건너뛴다 — 끝나면 새 언어로 그려진다).</summary>
        public override void OnLocaleChanged()
        {
            if (!IsVisible || _rebuild == null || Ctx.State == null) return;
            Action redraw = _rebuild;
            ResetContent();
            RefreshHud();
            redraw();
        }

        /// <summary>
        /// 버튼 count 개를 놓기 전에 호출. 모바일(버튼이 물리 48 dp 로 커짐)에서 남은 높이에 안 들어가면 스크롤 열을 만들어 버튼을 거기에 붙인다.
        /// 데스크톱은 기존 그대로(_content 에 직접).
        /// </summary>
        private void PrepareButtons(int count)
        {
            _buttonParent = _content;
            if (!UiMetrics.UseScroll || count <= 0) return;
            float h = TouchTargetPolicy.Apply(_buttonHeight, UiMetrics.MinButtonHeight);
            VerticalLayoutGroup lg = _content.GetComponent<VerticalLayoutGroup>();
            float spacing = lg != null ? lg.spacing : DefaultSpacing;
            float avail = _content.sizeDelta.y - 72f - _headerUsed;
            float need = count * h + (count - 1) * spacing;
            if (need > avail) _buttonParent = UiKit.MakeScrollColumn(_content, _buttonWidth, Mathf.Max(avail, 3f * h + 2f * spacing), spacing);
        }

        private void RefreshHud()
        {
            GameState s = Ctx.State;
            ChapterRow ch = Ctx.Balance.Chapter(s.Chapter);
            _hudDay.text = Loc.F("hud_day", "제{0}장 {1}  ·  {2}일차 ({3}/{4})", s.Chapter, Loc.ChapterName(s.Chapter, ch.Name), s.Day, s.DayInChapter, ch.Days);
            _hudStats.text = Loc.F("hud_stats", "{0} G   평판 {1}   Lv {2}   피로 {3}", s.Gold.ToString("N0"), s.Reputation.ToString("F0"), s.ShopLevel, s.Fatigue.ToString("F0"));
        }

        private TextMeshProUGUI Title(string text)
        {
            TextMeshProUGUI t = UiKit.Label(_content, text, 48, UiTheme.Accent, TextAlignmentOptions.Left);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 70f;
            _headerUsed += 70f + DefaultSpacing;
            return t;
        }

        private void Info(string text)
        {
            UiKit.Line(_content, text, 30, UiTheme.Text);
            _headerUsed += 44f + DefaultSpacing;
        }

        private Button AddButton(string label, System.Action onClick, bool accent)
        {
            return UiKit.MakeButton(_buttonParent != null ? _buttonParent : _content, label, onClick, _buttonWidth, _buttonHeight, accent ? UiTheme.ButtonAccent : UiTheme.Button);
        }

        // ---------------- 아침 ----------------

        private void ShowMorning()
        {
            Begin("Day");
            // 챕터 전환 흐름: 이전 챕터 Outro → 이번 챕터 Intro(VN) → (노드가 없으면) 정보 카드. 상태 기반이라 불러오기 후에도 이어진다.
            ChapterStep step = ChapterFlow.NextMorningStep(Ctx.State.Chapter, Ctx.State.DayInChapter, Ctx.Cycle.ChapterIntroPending,
                Ctx.Dialogue.HasNode, Ctx.Dialogue.HasVisited);
            // 데모: 마지막 데모 챕터를 마쳤으면(새 챕터 첫 아침) 마무리 장면만 보여 주고 데모 종료 화면으로 간다. 불러오기로 들어와도 같다.
            if (DemoConfig.Current.IsDemoOver(Ctx.State.Chapter, Ctx.State.Finished) && step.Kind != ChapterStepKind.Outro)
            {
                Ctx.ShowDemoEnd();
                return;
            }
            switch (step.Kind)
            {
                case ChapterStepKind.Outro:
                    Ctx.Dialogue.Play(step.Node, null, null, null, null);
                    return;
                case ChapterStepKind.Intro:
                    Ctx.PlaySe(AudioCues.SeChapterStart);
                    Ctx.Dialogue.Play(step.Node, null, null, null, delegate { Ctx.Cycle.AckChapterIntro(); });
                    return;
                case ChapterStepKind.InfoCard:
                    ShowChapterIntro();
                    return;
            }
            // 챕터 도입/마무리가 끝났다 → 아침의 공통 장면(CE01, CE02, CE03, CE05, CE06, CE07, CE08). 루트 선택 화면에서 돌아온 같은 아침에도 여기로 와서 CE07 이 이어진다.
            if (PlayNextCommon(SceneSlot.Morning)) return;

            RenderMorning();
        }

        private void RenderMorning()
        {
            _rebuild = RenderMorning;
            GameState s = Ctx.State;
            ChapterRow ch = Ctx.Balance.Chapter(s.Chapter);
            Title(Loc.F("day_phase_title", "{0}일차 · {1}", s.Day, UiText.Morning));
            float seats = Ctx.Balance.P("seat_base") + Ctx.Balance.P("seat_per_level") * s.ShopLevel;
            Info(Loc.F("morning_info", "오늘의 메뉴 {0}종 · 좌석 {1}석 · 직원 {2}명", ch.MenuCount, seats.ToString("F0"), ch.StaffCount));
            Info(Loc.F("morning_fatigue", "피로도 {0}", s.Fatigue.ToString("F0")) + (s.Fatigue >= Ctx.Balance.P("fatigue_limit") ? Loc.T("morning_overwork", " (과로: 밤 행동 1회)") : string.Empty));

            float cost;
            bool canUpgrade = Ctx.Cycle.CanUpgradeShop(out cost);
            PrepareButtons(4);
            Button up;
            if (Ctx.Cycle.AtLevelCap())
            {
                up = AddButton(UiText.UpgradeShopCap, null, false);
                up.interactable = false;
            }
            else
            {
                up = AddButton(Loc.F("upgrade_shop_btn", "{0}  Lv{1} → Lv{2}  ({3} G)", UiText.UpgradeShop, s.ShopLevel, s.ShopLevel + 1, cost.ToString("N0")), OnUpgrade, false);
                up.interactable = canUpgrade;
            }

            AddButton(UiText.StartBusiness, OnStartBusiness, true);
            AddButton(UiText.Save, OnSave, false);
            AddButton(UiText.ToTitle, Ctx.ExitToTitle, false);
        }

        private void ShowChapterIntro()
        {
            Ctx.PlaySe(AudioCues.SeChapterStart);
            RenderChapterIntro();
        }

        private void RenderChapterIntro()
        {
            _rebuild = RenderChapterIntro;
            ChapterRow ch = Ctx.Balance.Chapter(Ctx.State.Chapter);
            Title(Loc.F("chapter_title", "제{0}장  {1}", ch.Chapter, Loc.ChapterName(ch.Chapter, ch.Name)));
            Info(Loc.F("chapter_goal", "이번 챕터의 목표 일 매출 {0} → {1} G", ch.TargetRevStart.ToString("N0"), ch.TargetRevEnd.ToString("N0")));
            Info(Loc.F("chapter_unlocks_line", "해금: {0}", Loc.ChapterUnlocks(ch.Chapter, ch.Unlocks).Replace(";", " · ")));
            PrepareButtons(1);
            AddButton(Loc.T("start_button", "시작"), () => { Ctx.Cycle.AckChapterIntro(); ShowMorning(); }, true);
        }

        private void OnUpgrade()
        {
            if (Ctx.Cycle.TryUpgradeShop())
            {
                Ctx.PlaySe(AudioCues.SeLevelUp);
                ShowMorning();
            }
        }

        private void OnSave()
        {
            bool ok = Ctx.SaveGame(1);
            _toast.text = ok ? UiText.Saved : UiText.SaveFailed;
            Ctx.PlaySe(ok ? AudioCues.SeSave : AudioCues.SeError);
        }

        // ---------------- 영업(낮) ----------------

        private void OnStartBusiness()
        {
            if (_running != null) Ctx.Host.StopCoroutine(_running);
            _running = Ctx.Host.StartCoroutine(BusinessRoutine());
        }

        private IEnumerator BusinessRoutine()
        {
            Begin("Day");
            Ctx.PlayBgm(AudioCues.BgmBusiness);
            Ctx.PlaySe(AudioCues.SeDoorBell);
            DayResult result = Ctx.Cycle.RunBusiness();
            RefreshHud();

            Title(Loc.F("day_phase_title", "{0}일차 · {1}", Ctx.State.Day, UiText.Business));
            TextMeshProUGUI status = UiKit.Line(_content, Loc.T("biz_running", "영업 중…"), 32, UiTheme.Text);

            // 진행 막대: Filled Image 는 스프라이트가 필요하므로 앵커 폭으로 표현한다.
            Image barBg = UiKit.NewImage("BarBg", _content, new Color(1f, 1f, 1f, 0.15f));
            LayoutElement barLe = barBg.gameObject.AddComponent<LayoutElement>();
            barLe.preferredWidth = 980f;
            barLe.preferredHeight = 28f;
            Image bar = UiKit.NewImage("Bar", barBg.transform, UiTheme.Accent);
            bar.rectTransform.anchorMin = Vector2.zero;
            bar.rectTransform.anchorMax = new Vector2(0f, 1f);
            bar.rectTransform.offsetMin = Vector2.zero;
            bar.rectTransform.offsetMax = Vector2.zero;

            Button speed = AddButton(_fast ? UiText.Normal : UiText.Fast, null, false);
            speed.onClick.AddListener(() =>
            {
                _fast = !_fast;
                UiKit.SetButtonLabel(speed, _fast ? UiText.Normal : UiText.Fast);
            });

            float t = 0f;
            int shown = -1; // 손님 수가 바뀔 때만 글자를 새로 만든다(프레임마다 문자열을 만들면 3초 동안 GC 가 쌓인다).
            const float duration = 3f;
            while (t < duration)
            {
                if (Ctx.Pause != null && Ctx.Pause.IsOpen)
                {
                    yield return null; // 일시정지 중에는 연출을 멈춘다.
                    continue;
                }
                t += Time.unscaledDeltaTime * (_fast ? 2f : 1f);
                float p = Mathf.Clamp01(t / duration);
                bar.rectTransform.anchorMax = new Vector2(p, 1f);
                int served = Mathf.RoundToInt(result.Served * p);
                if (served != shown)
                {
                    shown = served;
                    status.text = Loc.F("biz_running_guests", "영업 중… 손님 {0}명", served);
                }
                yield return null;
            }

            ResetContent();
            if (result.Net > 0f) Ctx.PlaySe(AudioCues.SeCoin);
            RenderBusinessResult(result);
            _running = null;
        }

        private void RenderBusinessResult(DayResult result)
        {
            _rebuild = delegate { RenderBusinessResult(result); };
            Title(Loc.F("day_phase_title", "{0}일차 · {1}", Ctx.State.Day, Loc.T("biz_result", "영업 결과")));
            Info(Loc.F("biz_customers", "방문 고객 {0}명  (수용 {1} / 수요 {2})", result.Served.ToString("F0"), result.Capacity.ToString("F0"), result.Demand.ToString("F0")));
            Info(Loc.F("biz_price", "평균 객단가 {0} G · 만족도 {1}%", result.Price.ToString("F0"), (result.Satisfaction * 100f).ToString("F0")));
            Info(Loc.F("biz_revenue", "매출 {0} G  (재료비 -{1}, 유지비 -{2})", result.Gross.ToString("N0"), result.Ingredient.ToString("N0"), result.Upkeep.ToString("N0")));
            Info(Loc.F("biz_net", "순수익 {0} G · 평판 {1}", result.Net.ToString("N0"), (result.RepDelta >= 0f ? "+" : string.Empty) + result.RepDelta.ToString("F1")));
            if (result.EventText.Length > 0) Info(Loc.F("biz_event", "이벤트: {0}", Loc.Tr(result.EventText)));
            PrepareButtons(1);
            AddButton(UiText.ToNight, () => { Ctx.Cycle.BeginNight(); ShowNight(); }, true);
        }

        // ---------------- 밤 ----------------

        private void ShowNight()
        {
            // 밤 메뉴를 열 때와 밤 행동 하나를 마치고 돌아올 때(CE04, Ch5_Show, Ch6_LedgerTruth), 행동을 다 쓴 뒤(Ch5_Show_After).
            if (PlayNextCommon(SceneSlot.Night)) return;
            if (Ctx.State.ActionsLeft <= 0 && PlayNextCommon(SceneSlot.NightEnd)) return;
            Begin("Night");
            ShowStandingFor(FavoriteChar(), Ctx.State.ActionsLeft > 0 ? "Smile" : "Neutral");
            RefreshSide();
            if (RouteFlow.ShouldAutoPrompt(Ctx.State, EligibleRouteCount(), _routePromptDay))
            {
                _routePromptDay = Ctx.State.Day;
                ShowRoutePrompt();
                return;
            }
            ShowNightMenu();
        }

        /// <summary>밤 서브 화면 공통: 콘텐츠만 비우고 HUD·관계 패널을 새로 고친다(스탠딩·배경은 유지).</summary>
        private void NightContent()
        {
            UiKit.ClearChildren(_content);
            RefreshHud();
            RefreshSide();
        }

        private void RefreshSide()
        {
            _side.Refresh(Ctx.State, Ctx.Night, Ctx.Romance, Ctx.Balance.Chapter(Ctx.State.Chapter));
        }

        private void ShowStandingFor(string charId, string expression)
        {
            UiKit.ClearChildren(_standingLayer);
            StandingView view = new StandingView(_standingLayer, charId, "Casual", 760f);
            UiKit.Place(view.Root, new Vector2(0f, 0f), new Vector2(0f, 0f), view.Root.sizeDelta, new Vector2(30f, 0f));
            view.SetExpression(expression);
        }

        /// <summary>밤 화면에 세워 둘 인물: 루트 확정 인물(공동이면 구성원 중 호감이 가장 높은 인물), 없으면 호감이 가장 높은 인물(동률이면 앞쪽).</summary>
        private string FavoriteChar()
        {
            GameState s = Ctx.State;
            List<string> pool = s.HasRouteLock ? s.LockedIds() : Ctx.Night.AvailableChars();
            string best = pool.Count > 0 ? pool[0] : CharIds.Howl;
            float bestAff = -1f;
            for (int i = 0; i < pool.Count; i++)
            {
                float a = s.AffinityOf(pool[i]);
                if (a > bestAff)
                {
                    bestAff = a;
                    best = pool[i];
                }
            }
            return best;
        }

        private void ShowNightMenu()
        {
            _rebuild = ShowNightMenu;
            NightContent();
            GameState s = Ctx.State;
            bool can = s.ActionsLeft > 0;
            bool anyEvent = false;
            for (int i = 0; i < CharIds.All.Length; i++)
                if (Ctx.Night.HasEvent(CharIds.All[i])) anyEvent = true;

            Title(Loc.F("day_phase_title", "{0}일차 · {1}", s.Day, UiText.Night));
            Info(Loc.F("night_info", "남은 행동 {0}회 · 피로 {1} · {2} G", s.ActionsLeft, s.Fatigue.ToString("F0"), s.Gold.ToString("N0")));

            // 복구/동의 항목이 인라인으로 붙으면 버튼을 최소 터치 높이(48)로 줄여 패널 높이(790)에 맞춘다(LayoutRules 스택 검사와 같은 수치).
            float savedHeight = _buttonHeight;
            int inlineExtras = Math.Min(MaxInlineExtras, CurrentExtras().Count);
            if (inlineExtras > 0)
            {
                _buttonHeight = 48f;
                VerticalLayoutGroup menuLayout = _content.GetComponent<VerticalLayoutGroup>();
                if (menuLayout != null) menuLayout.spacing = 8f;
            }

            int extraCount = CurrentExtras().Count;
            int nightButtons = 7 + (RouteFlow.MenuEntryVisible(s) ? 1 : 0) + (extraCount == 0 ? 0 : (extraCount > MaxInlineExtras ? 1 : extraCount));
            PrepareButtons(nightButtons);
            AddButton(UiText.NightTalk, delegate { ShowCharPicker(PickMode.Talk); }, false).interactable = can;
            AddButton(UiText.NightGift, delegate { ShowCharPicker(PickMode.Gift); }, false).interactable = can && Ctx.Catalog != null;
            AddButton(UiText.NightDate, delegate { ShowCharPicker(PickMode.Date); }, false).interactable = can && Ctx.Catalog != null;
            AddButton(UiText.NightEvent + (anyEvent ? "  ★" : string.Empty), delegate { ShowCharPicker(PickMode.Event); }, anyEvent).interactable = can;
            // 피로 제한 밤의 무료 휴식(QA-08): 행동을 쓰지 않으므로 행동이 0이어도 누를 수 있다(밤당 1회).
            bool freeRest = Ctx.Cycle.FreeRestAvailable;
            AddButton(freeRest ? Loc.T("rest_free", "휴식 (무료, 행동 소모 없음)") : UiText.Rest,
                delegate { Ctx.Cycle.DoNightAction(NightAction.Rest); ShowNight(); }, freeRest).interactable = can || freeRest;
            AddButton(UiText.Research, delegate { Ctx.Cycle.DoNightAction(NightAction.Research); ShowNight(); }, false).interactable = can;
            if (RouteFlow.MenuEntryVisible(s))
            {
                bool star = RouteFlow.MenuEntryHighlighted(s, EligibleRouteCount());
                AddButton(UiText.NightRouteLock + (star ? "  ★" : string.Empty), Ctx.OpenRouteSelect, star);
            }
            AddExtraButtons();
            AddButton(UiText.FinishNight, delegate
            {
                if (PlayNextCommon(SceneSlot.NightEnd)) return; // 밤을 일찍 끝내도 정산 전에 밤 끝 장면을 먼저
                Ctx.Cycle.Settle();
                ShowSettlement();
            }, !can);

            _buttonHeight = savedHeight;
        }

        /// <summary>복구 장면·동의 확인 대화 항목(행동 횟수를 쓰지 않는다). 많으면 한 버튼으로 묶어 목록 화면을 연다.</summary>
        private void AddExtraButtons()
        {
            List<NightExtraEntry> extras = CurrentExtras();
            if (extras.Count == 0) return;
            if (extras.Count > MaxInlineExtras)
            {
                AddButton(UiText.ExtraScenes + " (" + extras.Count + ")  ★", ShowExtraList, true);
                return;
            }
            for (int i = 0; i < extras.Count; i++)
            {
                NightExtraEntry e = extras[i];
                AddButton(Loc.Tr(e.Label) + "  ★", delegate { PlayExtra(e); }, true);
            }
        }

        private List<NightExtraEntry> CurrentExtras()
        {
            return NightExtras.Collect(Ctx.State, Ctx.Recovery, Ctx.Dialogue.HasNode, Ctx.Dialogue.HasVisited, Ctx.Night.CharName);
        }

        private void ShowExtraList()
        {
            _rebuild = ShowExtraList;
            List<NightExtraEntry> extras = CurrentExtras();
            List<PickItem> items = new List<PickItem>();
            for (int i = 0; i < extras.Count; i++)
            {
                NightExtraEntry e = extras[i];
                PickItem it = new PickItem();
                it.Label = Loc.Tr(e.Label);
                it.Accent = true;
                it.OnPick = delegate { PlayExtra(e); };
                items.Add(it);
            }
            ShowPaged(UiText.ExtraScenes, string.Empty, items, 0, 6, delegate (int p) { ShowExtraList(); }, ShowNightMenu);
        }

        /// <summary>
        /// 복구/동의 노드 재생. 호스트는 결과 플래그(harem_ok_*, howl_e4_trust_late 등)를 설정하지 않는다 — 노드가 설정한다.
        /// 돌아오면 밤 메뉴가 다시 그려지며 조건이 바뀐 항목은 사라진다.
        /// </summary>
        private void PlayExtra(NightExtraEntry entry)
        {
            Ctx.Dialogue.Play(entry.NodeName, null, null, null, null);
        }

        private int EligibleRouteCount()
        {
            int n = 0;
            List<RouteLockOption> options = Ctx.Night.RouteLockOptions();
            for (int i = 0; i < options.Count; i++) if (options[i].Eligible) n++;
            return n;
        }

        // ---- 선택 화면(페이지 지원) ----

        private void ShowPaged(string title, string info, List<PickItem> items, int page, int pageSize, Action<int> rerender, Action onBack)
        {
            NightContent();
            int pages = Math.Max(1, (items.Count + pageSize - 1) / pageSize);
            if (page >= pages) page = pages - 1;
            if (page < 0) page = 0;
            int onPage = Math.Max(0, Math.Min(items.Count - page * pageSize, pageSize));
            Title(pages > 1 ? title + "  (" + (page + 1) + "/" + pages + ")" : title);
            if (!string.IsNullOrEmpty(info)) Info(info);
            PrepareButtons(onPage + (pages > 1 ? 1 : 0) + 1);

            for (int i = page * pageSize; i < items.Count && i < (page + 1) * pageSize; i++)
            {
                PickItem it = items[i];
                AddButton(it.Label, it.OnPick, it.Accent).interactable = it.Enabled;
            }

            if (pages > 1)
            {
                RectTransform row = UiKit.NewRect("PageRow", _buttonParent);
                UiKit.HorizontalLayout(row, 20f, 0, TextAnchor.MiddleCenter);
                int current = page;
                Button prev = UiKit.MakeButton(row, UiText.PrevPage, delegate { rerender(current - 1); }, 390f, 56f, UiTheme.Button);
                prev.interactable = current > 0;
                Button next = UiKit.MakeButton(row, UiText.NextPage, delegate { rerender(current + 1); }, 390f, 56f, UiTheme.Button);
                next.interactable = current < pages - 1;
            }
            AddButton(UiText.Back, onBack, false);
        }

        private void ShowCharPicker(PickMode mode)
        {
            _rebuild = delegate { ShowCharPicker(mode); };
            GameState s = Ctx.State;
            List<PickItem> items = new List<PickItem>();
            // 아직 등장하지 않은 인물은 목록에서 숨긴다(스포일러 방지).
            List<string> met = Ctx.Night.AvailableChars();
            for (int i = 0; i < met.Count; i++)
            {
                string id = met[i];
                string cid = id;
                PickItem it = new PickItem();
                string label = Ctx.CharDisplayName(id);
                bool star = Ctx.Night.HasEvent(id);
                it.Enabled = s.ActionsLeft > 0;

                if (mode == PickMode.Event)
                {
                    it.Enabled = it.Enabled && star;
                    it.Accent = star;
                    label = (star ? "★ " : string.Empty) + label + " — "
                        + (star ? Loc.F("event_label", "개인 이벤트 E{0}", s.Chars[id].EventsDone + 1) : Loc.Tr(Ctx.Night.EventHint(id)));
                }
                else
                {
                    if (star) label = "★ " + label;
                    string reason = null;
                    if (mode == PickMode.Gift) reason = Ctx.Night.GiftBlockReason(id);
                    else if (mode == PickMode.Date) reason = Ctx.Night.DateBlockReason(id);
                    if (reason != null)
                    {
                        it.Enabled = false;
                        label += "  (" + ReasonShort(reason) + ")";
                    }
                }
                it.Label = label;
                it.OnPick = delegate { OnCharPicked(mode, cid); };
                items.Add(it);
            }
            string title = mode == PickMode.Talk ? UiText.NightTalk : (mode == PickMode.Gift ? UiText.NightGift
                : (mode == PickMode.Date ? UiText.NightDate : UiText.PickEvent));
            string info = mode == PickMode.Event ? string.Empty : UiText.PickWho;
            ShowPaged(title, info, items, 0, 6, delegate (int p) { ShowCharPicker(mode); }, ShowNightMenu);
        }

        private static string ReasonShort(string reason)
        {
            switch (reason)
            {
                case RomanceRefusal.Cooldown: return Loc.T("reason_cooldown", "쿨다운");
                case RomanceRefusal.NoGold: return Loc.T("reason_no_gold", "골드 부족");
                case RomanceRefusal.NoActions: return Loc.T("reason_no_actions", "행동 없음");
                default: return reason;
            }
        }

        private void OnCharPicked(PickMode mode, string charId)
        {
            ShowStandingFor(charId, "Smile");
            switch (mode)
            {
                case PickMode.Talk: RunOutcome(Ctx.Night.Talk(charId)); break;
                case PickMode.Gift: ShowGiftPicker(charId, 0); break;
                case PickMode.Date: ShowSpotPicker(charId, 0); break;
                default: RunOutcome(Ctx.Night.Event(charId)); break;
            }
        }

        private void ShowGiftPicker(string charId, int page)
        {
            _rebuild = delegate { ShowGiftPicker(charId, page); };
            List<GiftChoice> gifts = Ctx.Night.GiftChoices();
            List<PickItem> items = new List<PickItem>();
            for (int i = 0; i < gifts.Count; i++)
            {
                string gid = gifts[i].Id;
                PickItem it = new PickItem();
                it.Label = Loc.F("gift_label", "{0}   (등급 {1})", Loc.GiftName(gid, gifts[i].NameKo), gifts[i].PriceTier);
                it.OnPick = delegate { RunOutcome(Ctx.Night.Gift(charId, gid), AudioCues.SeGift); };
                items.Add(it);
            }
            string info = Loc.F("gift_info", "{0}에게 · 선물 비용 {1} G (보유 아이템과 무관)", Ctx.CharDisplayName(charId), Ctx.Romance.GiftCost().ToString("N0"));
            ShowPaged(UiText.PickGift, info, items, page, 5, delegate (int p) { ShowGiftPicker(charId, p); },
                delegate { ShowCharPicker(PickMode.Gift); });
        }

        private void ShowSpotPicker(string charId, int page)
        {
            _rebuild = delegate { ShowSpotPicker(charId, page); };
            List<SpotChoice> spots = Ctx.Night.SpotChoices();
            List<PickItem> items = new List<PickItem>();
            for (int i = 0; i < spots.Count; i++)
            {
                string sid = spots[i].Id;
                PickItem it = new PickItem();
                it.Label = spots[i].Locked
                    ? Loc.F("spot_label_locked", "{0}   ({1}장 해금)", Loc.SpotName(sid, spots[i].NameKo), spots[i].UnlockChapter)
                    : Loc.SpotName(sid, spots[i].NameKo);
                it.Enabled = !spots[i].Locked;
                it.OnPick = delegate { RunOutcome(Ctx.Night.Date(charId, sid), AudioCues.SeDateStart); };
                items.Add(it);
            }
            string info = Loc.F("date_info", "{0}와(과) · 데이트 비용 {1} G", Ctx.CharDisplayName(charId), Ctx.Romance.DateCost().ToString("N0"));
            ShowPaged(UiText.PickSpot, info, items, page, 5, delegate (int p) { ShowSpotPicker(charId, p); },
                delegate { ShowCharPicker(PickMode.Date); });
        }

        /// <summary>행동 결과 처리: 거절이면 안내만, 성공이면 노드(또는 대체 대사)를 VN 으로 재생하고 돌아와 수치 요약을 토스트로 보여 준다.</summary>
        private void RunOutcome(NightActionOutcome outcome, string successSe = null)
        {
            if (!outcome.Success)
            {
                Ctx.PlaySe(AudioCues.SeError);
                _nextToast = Loc.Tr(outcome.RefusalMessage);
                ShowNight();
                return;
            }
            string summary = outcome.Summary;
            if (successSe != null) Ctx.PlaySe(successSe);
            Ctx.Dialogue.Play(outcome.NodeName, outcome.FallbackSpeaker, outcome.FallbackText, outcome.Temps,
                delegate { _nextToast = summary; });
        }

        // ---- 루트 확정(선택 화면은 RouteSelectScreen) ----

        /// <summary>밤 시작 시 하루 한 번 뜨는 안내: 루트 선택 화면을 열지 보류할지.</summary>
        private void ShowRoutePrompt()
        {
            _rebuild = ShowRoutePrompt;
            NightContent();
            Title(UiText.RouteSelectTitle);
            Info(UiText.RoutePromptBody);
            Info(UiText.RouteLockWarn);
            PrepareButtons(2);
            AddButton(UiText.OpenRouteSelect, Ctx.OpenRouteSelect, true);
            AddButton(UiText.RouteSelectLater, ShowNightMenu, false);
        }

        // ---------------- 정산 ----------------

        private void ShowSettlement()
        {
            Begin("Evening");

            RenderSettlement();
        }

        private void RenderSettlement()
        {
            _rebuild = RenderSettlement;
            GameState s = Ctx.State;
            DayResult r = Ctx.Cycle.Pending;
            Title(Loc.F("day_phase_title", "{0}일차 · {1}", s.Day, UiText.Settlement));
            if (r != null)
            {
                Info(Loc.F("settle_net", "오늘 순수익 {0} G → 보유 {1} G", r.Net.ToString("N0"), s.Gold.ToString("N0")));
                Info(Loc.F("settle_stats", "평판 {0} · 피로 {1}", s.Reputation.ToString("F0"), s.Fatigue.ToString("F0")));
            }
            PrepareButtons(1);
            AddButton(UiText.NextDay, OnNextDay, true);
        }

        private void OnNextDay()
        {
            Ctx.Cycle.NextDay();
            if (Ctx.State.Finished)
            {
                // 마지막 정산 직후: Ch8_Outro → 엔딩 장면 → 엔딩 화면. 엔딩 표를 못 읽었으면 기본 종료 카드.
                if (!Ctx.BeginEnding()) ShowEnd();
            }
            else ShowMorning();
        }

        private void ShowEnd()
        {
            Begin("Day");
            GameState s = Ctx.State;
            Title(UiText.EndTitle);
            Info(Loc.F("end_card_days", "{0}일을 달려 8챕터를 완주했습니다.", s.Day - 1));
            Info(Loc.F("end_final_stats", "최종 골드 {0} G · 가게 레벨 {1} · 평판 {2}", s.Gold.ToString("N0"), s.ShopLevel, s.Reputation.ToString("F0")));
            Info(Loc.F("end_card_time", "플레이 시간 {0}분", (s.PlayTimeSec / 60.0).ToString("F1")));
            PrepareButtons(1);
            AddButton(UiText.ToTitle, Ctx.ExitToTitle, true);
        }
    }
}
