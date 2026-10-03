using System;
using System.Collections.Generic;
using MorpheHouse.ArtPipeline;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 비주얼 노벨 무대: 배경 · 스탠딩(최대 2명) · CG 오버레이 · 페이드 · 대사창(이름표 + 타자기 텍스트) · 선택지.
    /// 어떤 대사를 보여 줄지는 VnScreen 이 정하고, 이 클래스는 그리기와 연출 상태만 가진다.
    /// 연출 커맨드(IVnCommandSink)는 DialogueDirector 가 이쪽으로 전달한다.
    /// </summary>
    public sealed class VnStage : IVnCommandSink, IVnOutfitSink
    {
        private enum FadeMode
        {
            None,
            ToBlackThenSwap,
            FromBlack,
            ToBlackThenDone
        }

        private const float FadeSeconds = 0.28f;
        private const float StandingHeight = 900f;
        private static readonly Color Dimmed = new Color(0.55f, 0.55f, 0.6f, 1f);

        // 입력 콜백(VnScreen 이 연결)
        public Action AdvanceRequested;
        public Action<int> ChoiceChosen;
        public Action AutoToggled;
        public Action SkipToggled;
        /// <summary>화면의 메뉴 버튼(일시정지)을 눌렀다.</summary>
        public Action MenuRequested;
        /// <summary>터치 제스처(탭 / 길게 누르기 시작·끝). 마우스 클릭도 Tap 으로 온다.</summary>
        public Action<GestureEvent> GestureRaised;
        /// <summary>타자기가 글자를 새로 보여 줄 때마다(효과음용, 호출 쪽이 간격을 제한한다).</summary>
        public Action TypeTick;

        private readonly RectTransform _fullRoot;
        private readonly RectTransform _safeRoot;

        private Image _bg;
        private RectTransform _standingLayer;
        private Image _cg;
        private Image _fade;
        private VnTouchSurface _touch;

        private RectTransform _box;
        private RectTransform _namePlate;
        private TextMeshProUGUI _nameText;
        private TextMeshProUGUI _bodyText;
        private TextMeshProUGUI _indicator;
        private RectTransform _choicePanel;
        private RectTransform _choiceBody; // 데스크톱: _choicePanel 자신, 모바일: 스크롤 내용 사각형
        private Button _autoButton;
        private Button _skipButton;
        private Button _menuButton;

        private readonly VnStandingState _standing = new VnStandingState();
        private readonly StandingView[] _views = new StandingView[VnStandingState.MaxSlots];
        private readonly string[] _viewChars = new string[VnStandingState.MaxSlots];
        private readonly Typewriter _type = new Typewriter();

        private FadeMode _fadeMode = FadeMode.None;
        private float _fadeAlpha;
        private Sprite _pendingBg;
        private Action _fadeDone;
        private bool _cgVisible;

        public VnStage(RectTransform fullRoot, RectTransform safeRoot)
        {
            _fullRoot = fullRoot;
            _safeRoot = safeRoot;
            Build();
        }

        public bool IsTyping { get { return !_type.IsDone; } }

        public bool IsFading { get { return _fadeMode != FadeMode.None; } }

        /// <summary>
        /// true(기본)면 조건 미충족 선택지를 회색 + "(조건 미충족)" 으로 보이고, false 면 숨긴다.
        /// 숨은 열쇠 선택지의 구체적인 조건은 어느 쪽에서도 드러내지 않는다.
        /// </summary>
        public bool ShowUnavailableChoices = true;

        // ---------------- 구성 ----------------

        private void Build()
        {
            _bg = UiKit.NewImage("VnBackground", _fullRoot, Color.white);
            UiKit.Stretch(_bg.rectTransform);
            _bg.preserveAspect = false;
            _bg.raycastTarget = false;

            _standingLayer = UiKit.NewRect("VnStandingLayer", _fullRoot);
            UiKit.Stretch(_standingLayer);

            _cg = UiKit.NewImage("VnCg", _fullRoot, Color.white);
            UiKit.Stretch(_cg.rectTransform);
            _cg.preserveAspect = true;
            _cg.raycastTarget = false;
            _cg.gameObject.SetActive(false);

            _fade = UiKit.NewImage("VnFade", _fullRoot, new Color(0f, 0f, 0f, 0f));
            UiKit.Stretch(_fade.rectTransform);
            _fade.raycastTarget = false;

            // 화면 전체 입력 면(마우스·터치): 거의 투명한 이미지 + VnTouchSurface(탭 · 길게 누르기). UI 위 클릭(버튼)과 겹치지 않는다.
            Image catcherImg = UiKit.NewImage("VnClickCatcher", _fullRoot, new Color(0f, 0f, 0f, 0.01f));
            UiKit.Stretch(catcherImg.rectTransform);
            _touch = catcherImg.gameObject.AddComponent<VnTouchSurface>();
            _touch.Raised = delegate (GestureEvent e)
            {
                if (e == GestureEvent.Tap && AdvanceRequested != null) AdvanceRequested();
                if (GestureRaised != null) GestureRaised(e);
            };

            // 대사창(세이프 에어리어 아래쪽). 클릭이 받이로 통과하도록 raycastTarget 을 끈다.
            Image boxImg = UiKit.NewImage("VnBox", _safeRoot, new Color32(0x10, 0x1A, 0x28, 225));
            boxImg.raycastTarget = false;
            _box = boxImg.rectTransform;
            _box.anchorMin = new Vector2(0f, 0f);
            _box.anchorMax = new Vector2(1f, 0f);
            _box.pivot = new Vector2(0.5f, 0f);
            _box.sizeDelta = new Vector2(-80f, 270f);
            _box.anchoredPosition = new Vector2(0f, 24f);

            Image plate = UiKit.NewImage("VnNamePlate", _box, UiTheme.Button);
            plate.raycastTarget = false;
            _namePlate = plate.rectTransform;
            UiKit.Place(_namePlate, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(320f, 56f), new Vector2(30f, -4f));
            _nameText = UiKit.Label(_namePlate, string.Empty, 32, UiTheme.Accent, TextAlignmentOptions.Center);
            UiKit.Stretch(_nameText.rectTransform);

            _bodyText = UiKit.Label(_box, string.Empty, 36, UiTheme.Text, TextAlignmentOptions.TopLeft);
            _bodyText.rectTransform.anchorMin = Vector2.zero;
            _bodyText.rectTransform.anchorMax = Vector2.one;
            _bodyText.rectTransform.offsetMin = new Vector2(44f, 28f);
            _bodyText.rectTransform.offsetMax = new Vector2(-44f, -32f);

            _indicator = UiKit.Label(_box, "▼", 30, UiTheme.Accent, TextAlignmentOptions.BottomRight);
            _indicator.rectTransform.anchorMin = Vector2.zero;
            _indicator.rectTransform.anchorMax = Vector2.one;
            _indicator.rectTransform.offsetMin = new Vector2(44f, 14f);
            _indicator.rectTransform.offsetMax = new Vector2(-44f, -14f);

            float btnH = Mathf.Max(56f, UiMetrics.MinButtonHeight); // 모바일: 물리 48 dp
            _autoButton = UiKit.MakeButton(_box, UiText.VnAuto, delegate { Deselect(); if (AutoToggled != null) AutoToggled(); }, 150f, 56f, UiTheme.Button);
            UiKit.Place((RectTransform)_autoButton.transform, new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(150f, btnH), new Vector2(-30f, -4f));
            _skipButton = UiKit.MakeButton(_box, UiText.VnSkip, delegate { Deselect(); if (SkipToggled != null) SkipToggled(); }, 150f, 56f, UiTheme.Button);
            UiKit.Place((RectTransform)_skipButton.transform, new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(150f, btnH), new Vector2(-190f, -4f));
            _menuButton = UiKit.MakeButton(_box, UiText.PauseButton, delegate { Deselect(); if (MenuRequested != null) MenuRequested(); }, 150f, 56f, UiTheme.Button);
            UiKit.Place((RectTransform)_menuButton.transform, new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(150f, btnH), new Vector2(-350f, -4f));

            // 선택지는 대사창 바로 위(아래 정렬)에 쌓는다: 대사창 윗선 294 + 여백 16 = 310. 화면 비율(16:9~20:9)과 무관하게 대사창과 겹치지 않는다.
            _choicePanel = UiKit.NewRect("VnChoices", _safeRoot);
            UiKit.Place(_choicePanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(1200f, 480f), new Vector2(0f, 310f));
            if (UiMetrics.UseScroll)
            {
                // 모바일: 버튼이 커지므로 위쪽으로 조금 키우고(자동/스킵/메뉴 버튼이 올라온 만큼 띄움) 넘치면 스크롤.
                _choicePanel.sizeDelta = new Vector2(1200f, 540f);
                _choicePanel.anchoredPosition = new Vector2(0f, 310f + (btnH - 56f));
            }
            _choiceBody = UiKit.VerticalLayoutScroll(_choicePanel, 16f, 0, TextAnchor.LowerCenter);
            _choicePanel.gameObject.SetActive(false);
        }

        /// <summary>버튼을 누른 뒤 선택 상태를 비워, Space/Enter 가 버튼 제출로 중복 처리되지 않게 한다.</summary>
        private static void Deselect()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        // ---------------- 장면 맥락 (의상 정책용) ----------------

        private Func<string> _nodeName;
        private string _place = ArtNames.DefaultPlace;
        private string _time = ArtNames.DefaultTime;

        /// <summary>
        /// 장면 시작 직전에 호출: 지금 실행 중인 노드 이름(점프로 바뀔 수 있어 함수로 받는다)과 시작 배경의 장소·시간대.
        /// 의상을 지정하지 않은 스탠딩은 이 맥락으로 OutfitPolicy 가 의상을 고른다.
        /// </summary>
        public void SetContext(Func<string> nodeName, string place, string time)
        {
            _nodeName = nodeName;
            _place = string.IsNullOrEmpty(place) ? ArtNames.DefaultPlace : place;
            _time = string.IsNullOrEmpty(time) ? ArtNames.DefaultTime : time;
        }

        /// <summary>이번 장면에서 의상을 지정하지 않은 인물이 입을 의상(정책 결과). 디버그·테스트용.</summary>
        public string ResolveOutfit(string charId)
        {
            string node = _nodeName != null ? _nodeName() : null;
            return OutfitPolicy.Resolve(charId, null, node, _place, _time);
        }

        /// <summary>IVnOutfitSink: 의상을 지정하지 않은 &lt;&lt;show_standing&gt;&gt;. 명시된 의상은 이 경로를 거치지 않는다.</summary>
        public void ShowStandingAuto(string charId, string expression)
        {
            ShowStanding(charId, expression, ResolveOutfit(charId));
        }

        // ---------------- 장면 시작/종료 ----------------

        /// <summary>새 대화 시작: 배경을 바꾸고 스탠딩·CG·선택지를 비운 뒤 어둠에서 밝아진다.</summary>
        public void BeginScene(Sprite background)
        {
            ClearStandings();
            HideCg();
            HideChoices();
            _bg.sprite = background;
            _pendingBg = null;
            _fadeDone = null;
            _fadeAlpha = 1f;
            ApplyFade();
            _fadeMode = FadeMode.FromBlack;
            _box.gameObject.SetActive(true);
            SetLine(null, string.Empty, 0f);
            SetToggleVisual(_autoButton, false);
            SetToggleVisual(_skipButton, false);
        }

        /// <summary>어두워진 뒤 done 을 호출한다. 대사창은 바로 숨긴다.</summary>
        public void EndScene(Action done)
        {
            _box.gameObject.SetActive(false);
            HideChoices();
            _touch.CancelGesture();
            _fadeDone = done;
            _fadeMode = FadeMode.ToBlackThenDone;
        }

        /// <summary>장면을 중간에 버린다(타이틀로 돌아가기). 콜백은 부르지 않는다.</summary>
        public void Abort()
        {
            _box.gameObject.SetActive(false);
            HideChoices();
            _touch.CancelGesture();
            _fadeDone = null;
            _fadeMode = FadeMode.None;
            _fadeAlpha = 0f;
            ApplyFade();
        }

        // ---------------- 대사 / 선택지 ----------------

        /// <summary>대사 한 줄 표시. speakerId 는 스탠딩 강조용(없으면 모두 밝게), speakerName 이 비면 이름표 숨김(지문).</summary>
        public void SetLine(string speakerId, string speakerName, string text, float charsPerSecond)
        {
            bool hasName = !string.IsNullOrEmpty(speakerName);
            _namePlate.gameObject.SetActive(hasName);
            _nameText.richText = false;
            _bodyText.richText = false;
            _nameText.text = hasName ? speakerName : string.Empty;
            _bodyText.text = text ?? string.Empty;
            _type.Reset(text, charsPerSecond);
            ApplyTyping();
            HighlightSpeaker(speakerId);
        }

        private void SetLine(string speakerName, string text, float charsPerSecond)
        {
            SetLine(null, speakerName, text, charsPerSecond);
        }

        public void CompleteTyping()
        {
            _type.Complete();
            ApplyTyping();
        }

        private void ApplyTyping()
        {
            _bodyText.maxVisibleCharacters = _type.IsDone ? 99999 : _type.VisibleCount;
            _indicator.gameObject.SetActive(_type.IsDone && _type.Text.Length > 0);
        }

        public void ShowChoices(List<DialogueOption> options)
        {
            UiKit.ClearChildren(_choiceBody);
            _choicePanel.gameObject.SetActive(true);
            _indicator.gameObject.SetActive(false);
            for (int i = 0; i < options.Count; i++)
            {
                DialogueOption o = options[i];
                int index = o.Index;
                string label = ChoiceLabel.Format(o.Text, o.Available, ShowUnavailableChoices);
                if (label == null) continue;
                Button b = UiKit.MakeButton(_choiceBody, label, delegate
                {
                    Deselect();
                    if (ChoiceChosen != null) ChoiceChosen(index);
                }, 1100f, 78f, UiTheme.Button);
                b.interactable = o.Available;
            }
        }

        public void HideChoices()
        {
            UiKit.ClearChildren(_choiceBody);
            _choicePanel.gameObject.SetActive(false);
        }

        public void SetAutoVisual(bool on)
        {
            SetToggleVisual(_autoButton, on);
        }

        public void SetSkipVisual(bool on)
        {
            SetToggleVisual(_skipButton, on);
        }

        private static void SetToggleVisual(Button b, bool on)
        {
            Image img = b.targetGraphic as Image;
            if (img != null) img.color = on ? UiTheme.ButtonAccent : UiTheme.Button;
        }

        // ---------------- 매 프레임 ----------------

        public void Tick(float dt)
        {
            if (!_type.IsDone)
            {
                int before = _type.VisibleCount;
                _type.Tick(dt);
                ApplyTyping();
                if (_type.VisibleCount > before && TypeTick != null) TypeTick();
            }
            TickFade(dt);
        }

        private void TickFade(float dt)
        {
            if (_fadeMode == FadeMode.None) return;
            float step = dt / FadeSeconds;
            switch (_fadeMode)
            {
                case FadeMode.ToBlackThenSwap:
                    _fadeAlpha += step;
                    if (_fadeAlpha >= 1f)
                    {
                        _fadeAlpha = 1f;
                        if (_pendingBg != null) _bg.sprite = _pendingBg;
                        _pendingBg = null;
                        _fadeMode = FadeMode.FromBlack;
                    }
                    break;
                case FadeMode.FromBlack:
                    _fadeAlpha -= step;
                    if (_fadeAlpha <= 0f)
                    {
                        _fadeAlpha = 0f;
                        _fadeMode = FadeMode.None;
                    }
                    break;
                case FadeMode.ToBlackThenDone:
                    _fadeAlpha += step;
                    if (_fadeAlpha >= 1f)
                    {
                        _fadeAlpha = 1f;
                        _fadeMode = FadeMode.None;
                        Action cb = _fadeDone;
                        _fadeDone = null;
                        if (cb != null) cb();
                    }
                    break;
            }
            ApplyFade();
        }

        private void ApplyFade()
        {
            _fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(_fadeAlpha));
        }

        // ---------------- IVnCommandSink ----------------

        public void ShowStanding(string charId, string expression, string outfit)
        {
            string evicted;
            int slot = _standing.Show(charId, expression, outfit, out evicted);
            if (evicted != null) DestroyView(slot);
            if (_views[slot] != null && _viewChars[slot] != charId) DestroyView(slot);
            if (_views[slot] == null)
            {
                StandingView view = new StandingView(_standingLayer, charId, outfit, StandingHeight);
                _views[slot] = view;
                _viewChars[slot] = charId;
                float x = slot == 0 ? 0.27f : 0.73f;
                UiKit.Place(view.Root, new Vector2(x, 0f), new Vector2(0.5f, 0f), view.Root.sizeDelta, Vector2.zero);
            }
            _views[slot].SetOutfit(outfit, expression);
        }

        public void HideStanding(string charId)
        {
            int slot = _standing.Hide(charId);
            if (slot >= 0) DestroyView(slot);
        }

        public void ShowCg(string charId, int number)
        {
            _cg.sprite = ArtLoader.Cg(charId, number);
            UiAudio.Play(AudioCues.SeCgReveal);
            _cg.gameObject.SetActive(true);
            _standingLayer.gameObject.SetActive(false);
            _cgVisible = true;
        }

        public void ShowCommonCg(string key)
        {
            _cg.sprite = ArtLoader.CommonCg(key);
            UiAudio.Play(AudioCues.SeCgReveal);
            _cg.gameObject.SetActive(true);
            _standingLayer.gameObject.SetActive(false);
            _cgVisible = true;
        }

        public void HideCg()
        {
            _cg.gameObject.SetActive(false);
            _standingLayer.gameObject.SetActive(true);
            _cgVisible = false;
        }

        public void SetBackground(string place, string time)
        {
            _place = place;
            _time = time;
            _pendingBg = ArtLoader.Background(place, time);
            _fadeMode = FadeMode.ToBlackThenSwap;
        }

        // ---------------- 내부 ----------------

        private void HighlightSpeaker(string speakerId)
        {
            for (int i = 0; i < _views.Length; i++)
            {
                if (_views[i] == null) continue;
                bool dim = !string.IsNullOrEmpty(speakerId) && _viewChars[i] != speakerId;
                _views[i].SetTint(dim ? Dimmed : Color.white);
            }
        }

        private void DestroyView(int slot)
        {
            if (_views[slot] != null) UnityEngine.Object.Destroy(_views[slot].Root.gameObject);
            _views[slot] = null;
            _viewChars[slot] = null;
        }

        private void ClearStandings()
        {
            _standing.Clear();
            for (int i = 0; i < _views.Length; i++) DestroyView(i);
        }

        /// <summary>CG 가 떠 있는지(테스트·디버그용).</summary>
        public bool CgVisible { get { return _cgVisible; } }
    }
}
