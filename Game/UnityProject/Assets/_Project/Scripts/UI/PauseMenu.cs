using MorpheHouse.Core;
using MorpheHouse.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 게임 중 일시정지 메뉴(Esc / Android 뒤로 키 / 화면의 메뉴 버튼): 계속하기 · 설정 · 저장(아침에만) · 타이틀로(확인 상자).
    /// 화면(ScreenBase)이 아니라 다른 화면 위에 덮이는 오버레이라서 VN·게임 화면 상태를 건드리지 않고 열고 닫는다.
    /// 열려 있는 동안 GameBootstrap 이 화면 Tick 을 멈추고(대사 진행·타자기·플레이 시간 정지), 영업 연출 코루틴도 기다린다.
    /// 어떤 뒤로가기 입력이 무엇을 하는지는 순수 BackKeyRules 가 정한다.
    /// </summary>
    public sealed class PauseMenu
    {
        private readonly AppContext _ctx;
        private readonly RectTransform _shade;
        private readonly RectTransform _menu;
        private readonly RectTransform _confirm;
        private readonly SettingsPanel _settings;
        private TextMeshProUGUI _message;

        public PauseMenu(AppContext ctx, RectTransform overlayFull, RectTransform overlaySafe)
        {
            _ctx = ctx;

            // 전체 화면 어둡게(클릭도 막는다).
            Image shade = UiKit.NewImage("PauseShade", overlayFull, new Color(0f, 0f, 0f, 0.6f));
            UiKit.Stretch(shade.rectTransform);
            _shade = shade.rectTransform;

            Image menu = UiKit.NewImage("PauseMenu", overlaySafe, UiTheme.Panel);
            _menu = menu.rectTransform;
            UiKit.Place(_menu, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(760f, 600f), Vector2.zero);
            RectTransform menuBody = UiKit.VerticalLayoutScroll(_menu, 14f, 24, TextAnchor.UpperCenter);
            TextMeshProUGUI title = UiKit.Label(menuBody, UiText.PauseTitle, 44, UiTheme.Accent, TextAlignmentOptions.Center);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
            UiKit.MakeButton(menuBody, UiText.Resume, Close, 640f, 72f, UiTheme.ButtonAccent);
            UiKit.MakeButton(menuBody, UiText.Settings, OnSettings, 640f, 72f, UiTheme.Button);
            UiKit.MakeButton(menuBody, UiText.PauseSave, OnSave, 640f, 72f, UiTheme.Button);
            UiKit.MakeButton(menuBody, UiText.ReturnToTitle, OnReturn, 640f, 72f, UiTheme.Button);
            _message = UiKit.Label(menuBody, string.Empty, 26, UiTheme.Accent, TextAlignmentOptions.Center);
            LayoutElement ml = _message.gameObject.AddComponent<LayoutElement>();
            ml.preferredWidth = 680f;
            ml.preferredHeight = 60f;

            Image confirm = UiKit.NewImage("PauseConfirm", overlaySafe, UiTheme.Panel);
            _confirm = confirm.rectTransform;
            UiKit.Place(_confirm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(900f, 420f), Vector2.zero);
            if (UiMetrics.UseScroll) _confirm.sizeDelta = new Vector2(900f, 540f); // 모바일: 버튼 2개가 48 dp 로 커진 만큼
            UiKit.VerticalLayout(_confirm, 14f, 36, TextAnchor.UpperCenter);
            TextMeshProUGUI ct = UiKit.Label(_confirm, UiText.ReturnConfirmTitle, 44, UiTheme.Accent, TextAlignmentOptions.Center);
            ct.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            TextMeshProUGUI cb = UiKit.Label(_confirm, UiText.ReturnConfirmBody, 28, UiTheme.Text, TextAlignmentOptions.Center);
            LayoutElement cbl = cb.gameObject.AddComponent<LayoutElement>();
            cbl.preferredWidth = 800f;
            cbl.preferredHeight = 110f;
            UiKit.MakeButton(_confirm, UiText.ReturnConfirmYes, OnReturnConfirmed, 640f, 64f, UiTheme.ButtonAccent);
            UiKit.MakeButton(_confirm, UiText.Cancel, delegate { _confirm.gameObject.SetActive(false); _menu.gameObject.SetActive(true); }, 640f, 64f, UiTheme.Button);

            _settings = new SettingsPanel(ctx, overlaySafe, delegate { _menu.gameObject.SetActive(true); });

            SetAllHidden();
        }

        public bool IsOpen { get { return _shade.gameObject.activeSelf; } }

        public bool SubPanelOpen { get { return _settings.IsOpen; } }

        public bool ConfirmOpen { get { return _confirm.gameObject.activeSelf; } }

        /// <summary>메뉴를 연다. 이미 열려 있거나 이 화면에서는 열 수 없으면 아무 일도 하지 않는다.</summary>
        public void Open()
        {
            if (IsOpen || !BackKeyRules.CanPause(_ctx.CurrentBackScreen())) return;
            _message.text = string.Empty;
            _shade.gameObject.SetActive(true);
            _menu.gameObject.SetActive(true);
            _confirm.gameObject.SetActive(false);
            _settings.Hide();
        }

        public void Close()
        {
            _settings.Hide();
            SetAllHidden();
        }

        private void SetAllHidden()
        {
            _shade.gameObject.SetActive(false);
            _menu.gameObject.SetActive(false);
            _confirm.gameObject.SetActive(false);
        }

        /// <summary>메뉴가 열려 있을 때의 뒤로가기: 확인 상자 → 설정 → 메뉴 순으로 한 단계씩 닫는다.</summary>
        public void HandleBack()
        {
            BackAction a = BackKeyRules.Decide(_ctx.CurrentBackScreen(), IsOpen, SubPanelOpen, ConfirmOpen, false);
            switch (a)
            {
                case BackAction.CloseConfirm:
                    _confirm.gameObject.SetActive(false);
                    _menu.gameObject.SetActive(true);
                    break;
                case BackAction.PauseBackToMenu:
                    _settings.Close();
                    break;
                case BackAction.ClosePause:
                    Close();
                    break;
            }
        }

        private void OnSettings()
        {
            _menu.gameObject.SetActive(false);
            _settings.Show();
        }

        private void OnSave()
        {
            bool morning = _ctx.State != null && _ctx.State.Phase == DayPhase.Morning;
            if (!BackKeyRules.CanSaveNow(_ctx.CurrentBackScreen(), morning, false))
            {
                _message.text = UiText.PauseSaveMorningOnly;
                UiAudio.Play(AudioCues.SeError);
                return;
            }
            bool ok = _ctx.SaveGame(1);
            _message.text = ok ? UiText.Saved : UiText.SaveFailed;
            UiAudio.Play(ok ? AudioCues.SeSave : AudioCues.SeError);
        }

        private void OnReturn()
        {
            _menu.gameObject.SetActive(false);
            _confirm.gameObject.SetActive(true);
        }

        private void OnReturnConfirmed()
        {
            Close();
            _ctx.ReturnToTitle();
        }
    }
}
