using System;
using MorpheHouse.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 설정 패널(타이틀·일시정지 메뉴 공용): 마스터/BGM/효과음 볼륨, 텍스트 속도, 자동 진행 속도 슬라이더, 언어(한국어/English),
    /// UI 크기(90/100/110%, 지금 화면에서 레이아웃에 안 들어가는 크기는 비활성), 전체 화면(데스크톱만). 닫을 때 settings.json 에 저장한다.
    /// 크기·배치 수치는 LayoutRules 표(Settings/SettingsPanel)와 같아야 한다.
    /// </summary>
    public sealed class SettingsPanel
    {
        private const float RowHeight = 56f;
        private const float LabelWidth = 370f;
        private const float SliderWidth = 520f;
        private const float ValueWidth = 130f;

        private readonly AppContext _ctx;
        private readonly Action _onClosed;
        private readonly RectTransform _root;
        private RectTransform _body; // 모바일에서는 스크롤 내용 사각형, 데스크톱에서는 _root

        private Slider _master;
        private Slider _bgm;
        private Slider _se;
        private Slider _speed;
        private Slider _auto;
        private TextMeshProUGUI _masterValue;
        private TextMeshProUGUI _bgmValue;
        private TextMeshProUGUI _seValue;
        private TextMeshProUGUI _speedValue;
        private TextMeshProUGUI _autoValue;
        private Button _localeButton;
        private readonly Button[] _scaleButtons = new Button[SettingsService.UiScales.Length];
        private RectTransform _fullscreenRow;
        private Button _fullscreenButton;
        private TextMeshProUGUI _note;

        public SettingsPanel(AppContext ctx, Transform parent, Action onClosed)
        {
            _ctx = ctx;
            _onClosed = onClosed;
            Image panel = UiKit.NewImage("SettingsPanel", parent, UiTheme.Panel);
            _root = panel.rectTransform;
            UiKit.Place(_root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1100f, 780f), Vector2.zero);
            _body = UiKit.VerticalLayoutScroll(_root, 12f, 24, TextAnchor.UpperCenter);
            Build();
            _ctx.Settings.Changed += OnSettingsChanged;
            _root.gameObject.SetActive(false);
        }

        public bool IsOpen { get { return _root.gameObject.activeSelf; } }

        public void Show()
        {
            _root.gameObject.SetActive(true);
            SyncControls();
        }

        public void Hide()
        {
            _root.gameObject.SetActive(false);
        }

        /// <summary>저장하고 닫은 뒤 onClosed 를 부른다(뒤로가기 키·닫기 버튼 공용).</summary>
        public void Close()
        {
            _ctx.Settings.Save();
            Hide();
            if (_onClosed != null) _onClosed();
        }

        // ---------------- 조립 ----------------

        private void Build()
        {
            TextMeshProUGUI title = UiKit.Label(_body, UiText.SettingsTitle, 44, UiTheme.Text, TextAlignmentOptions.Center);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;

            SettingsService s = _ctx.Settings;
            _master = SliderRow(UiText.SettingsVolume, 0f, 1f, s.MasterVolume, delegate (float v) { s.SetMasterVolume(v); }, out _masterValue);
            _bgm = SliderRow(UiText.SettingsBgm, 0f, 1f, s.BgmVolume, delegate (float v) { s.SetBgmVolume(v); }, out _bgmValue);
            _se = SliderRow(UiText.SettingsSe, 0f, 1f, s.SeVolume, delegate (float v) { s.SetSeVolume(v); }, out _seValue);
            _speed = SliderRow(UiText.SettingsTextSpeed, SettingsService.MinTextSpeed, SettingsService.MaxTextSpeed, s.TextSpeed,
                delegate (float v) { s.SetTextSpeed(v); }, out _speedValue);
            // 자동 진행은 "빠를수록 오른쪽" 이 되도록 대기 시간을 뒤집어 슬라이더에 싣는다.
            _auto = SliderRow(UiText.SettingsAutoSpeed, SettingsService.MinAutoDelay, SettingsService.MaxAutoDelay, AutoToSlider(s.AutoDelay),
                delegate (float v) { s.SetAutoDelay(AutoToSlider(v)); }, out _autoValue);

            RectTransform localeRow = Row("LocaleRow", UiText.SettingsLocale);
            _localeButton = UiKit.MakeButton(localeRow, LocaleName(), OnToggleLocale, 340f, RowHeight, UiTheme.Button);

            RectTransform scaleRow = Row("ScaleRow", UiText.SettingsUiScale);
            for (int i = 0; i < _scaleButtons.Length; i++)
            {
                float scale = SettingsService.UiScales[i];
                _scaleButtons[i] = UiKit.MakeButton(scaleRow, Mathf.RoundToInt(scale * 100f) + "%",
                    delegate { _ctx.Settings.SetUiScale(scale); }, 150f, RowHeight, UiTheme.Button);
            }

            _fullscreenRow = Row("FullscreenRow", UiText.SettingsFullscreen);
            _fullscreenButton = UiKit.MakeButton(_fullscreenRow, UiText.SettingsOn, delegate { _ctx.Settings.SetFullscreen(!_ctx.Settings.Fullscreen); }, 340f, RowHeight, UiTheme.Button);
            _fullscreenRow.gameObject.SetActive(!Application.isMobilePlatform);

            _note = UiKit.Label(_body, UiText.SettingsDialogueNote, 24, UiTheme.TextDim, TextAlignmentOptions.Center);
            _note.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

            UiKit.MakeButton(_body, UiText.Close, Close, 520f, 64f, UiTheme.ButtonAccent);
        }

        private RectTransform Row(string name, string labelText)
        {
            RectTransform row = UiKit.NewRect(name, _body);
            LayoutElement le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = Mathf.Max(RowHeight, UiMetrics.MinButtonHeight); // 줄 안의 버튼이 모바일 최소 터치 높이로 커져도 눌리지 않도록
            le.preferredWidth = 1040f;
            UiKit.HorizontalLayout(row, 12f, 0, TextAnchor.MiddleLeft);
            TextMeshProUGUI label = UiKit.Label(row, labelText, 28, UiTheme.Text, TextAlignmentOptions.MidlineLeft);
            LayoutElement ll = label.gameObject.AddComponent<LayoutElement>();
            ll.preferredWidth = LabelWidth;
            ll.preferredHeight = RowHeight;
            return row;
        }

        private Slider SliderRow(string labelText, float min, float max, float value, Action<float> onChanged, out TextMeshProUGUI valueLabel)
        {
            RectTransform row = Row("SliderRow", labelText);
            Slider slider = UiKit.MakeSlider(row, SliderWidth, RowHeight, min, max, value, onChanged);
            valueLabel = UiKit.Label(row, string.Empty, 26, UiTheme.Accent, TextAlignmentOptions.MidlineRight);
            LayoutElement vl = valueLabel.gameObject.AddComponent<LayoutElement>();
            vl.preferredWidth = ValueWidth;
            vl.preferredHeight = RowHeight;
            return slider;
        }

        private static float AutoToSlider(float v)
        {
            return SettingsService.MinAutoDelay + SettingsService.MaxAutoDelay - v;
        }

        // ---------------- 값 표시 ----------------

        private static string LocaleName()
        {
            return UiStrings.Current.Locale == "en" ? "English" : "한국어";
        }

        private void OnToggleLocale()
        {
            _ctx.Settings.SetLocale(_ctx.Settings.Locale == "en" ? "ko" : "en");
        }

        private void OnSettingsChanged()
        {
            if (IsOpen) RefreshLabels();
        }

        private void SyncControls()
        {
            SettingsService s = _ctx.Settings;
            _master.SetValueWithoutNotify(s.MasterVolume);
            _bgm.SetValueWithoutNotify(s.BgmVolume);
            _se.SetValueWithoutNotify(s.SeVolume);
            _speed.SetValueWithoutNotify(s.TextSpeed);
            _auto.SetValueWithoutNotify(AutoToSlider(s.AutoDelay));
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            SettingsService s = _ctx.Settings;
            _masterValue.text = Percent(s.MasterVolume);
            _bgmValue.text = Percent(s.BgmVolume);
            _seValue.text = Percent(s.SeVolume);
            _speedValue.text = s.InstantText ? UiText.SettingsInstant
                : UiStrings.FormatText(UiText.SettingsCharsPerSec, Mathf.RoundToInt(s.TextSpeed));
            _autoValue.text = UiStrings.FormatText(UiText.SettingsSeconds, s.AutoDelay.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

            UiKit.SetButtonLabel(_localeButton, LocaleName());
            UiKit.SetButtonLabel(_fullscreenButton, s.Fullscreen ? UiText.SettingsOn : UiText.SettingsOff);

            bool anyUnavailable = false;
            for (int i = 0; i < _scaleButtons.Length; i++)
            {
                float scale = SettingsService.UiScales[i];
                bool available = DisplayService.IsScaleAvailable(scale);
                bool selected = Mathf.Approximately(scale, s.UiScale);
                _scaleButtons[i].interactable = available;
                Image img = _scaleButtons[i].targetGraphic as Image;
                if (img != null) img.color = selected ? UiTheme.ButtonAccent : UiTheme.Button;
                if (!available) anyUnavailable = true;
            }
            _note.text = anyUnavailable ? UiText.SettingsUiScaleUnavailable : UiText.SettingsDialogueNote;
        }

        private static string Percent(float v)
        {
            return UiStrings.FormatText(UiText.SettingsPercent, Mathf.RoundToInt(v * 100f));
        }
    }
}
