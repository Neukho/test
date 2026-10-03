using System.Collections.Generic;
using MorpheHouse.ArtPipeline;
using MorpheHouse.Data;
using MorpheHouse.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 루트 선택 화면(코드로 조립). 후보 6명의 호감·신뢰·적격 여부(불가 사유 포함)를 보여 주고
    /// 1명(단독) / 2~3명(공동) / 정하지 않고 계속(커뮤니티) 중에서 고르게 한다. 확정 전 확인 대화 상자가 있다.
    /// 규칙·검증은 전부 순수 RouteSelectModel(→ NightRomanceActions)이 하고, 이 클래스는 그리기와 입력 전달만 한다.
    /// 공동 확정 후에는 Harem_Table → 구성원별 &lt;Char&gt;_Harem_Consent 를 재생한다(동의 플래그는 노드가 설정, 호스트는 설정하지 않음).
    /// </summary>
    public sealed class RouteSelectScreen : ScreenBase
    {
        private const float PanelWidth = 1560f;
        private const float PanelHeight = 880f;

        private RouteSelectModel _model;
        private RectTransform _panel;
        private RectTransform _list;
        private RectTransform _actions;
        private TextMeshProUGUI _explain;
        private TextMeshProUGUI _message;
        private RectTransform _dialog;
        private RectTransform _listBody; // 데스크톱: _list 자신, 모바일: 스크롤 내용 사각형
        private Image _bg;
        private TextMeshProUGUI _dialogBody;

        public RouteSelectScreen(AppContext ctx) : base(ctx) { }

        protected override void OnBuild()
        {
            Image bg = UiKit.NewImage("Background", FullRoot, Color.white);
            UiKit.Stretch(bg.rectTransform);
            _bg = bg;
            bg.sprite = ArtLoader.Background("Cafe_Interior", "Night");
            bg.preserveAspect = false;
            bg.raycastTarget = false;

            Image dim = UiKit.NewImage("Dim", FullRoot, new Color(0f, 0f, 0f, 0.55f));
            UiKit.Stretch(dim.rectTransform);
            dim.raycastTarget = false;

            Image panel = UiKit.NewImage("Panel", SafeRoot, UiTheme.Panel);
            panel.raycastTarget = false;
            _panel = panel.rectTransform;
            UiKit.Place(_panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(PanelWidth, PanelHeight), Vector2.zero);

            TextMeshProUGUI title = UiKit.Label(_panel, UiText.RouteSelectTitle, 52, UiTheme.Accent, TextAlignmentOptions.Left);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(900f, 70f), new Vector2(40f, -20f));

            // 왼쪽: 후보 목록 + 동작 버튼
            _list = UiKit.NewRect("List", _panel);
            UiKit.Place(_list, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(880f, 520f), new Vector2(40f, -100f));
            if (UiMetrics.UseScroll)
            {
                // 모바일: 동작 버튼 3개가 물리 48 dp 로 커지므로 아래 영역을 키우고 후보 목록은 줄여 스크롤한다(패널 높이 880 안).
                float actionsH = 3f * UiMetrics.MinButtonHeight + 16f;
                _list.sizeDelta = new Vector2(880f, 880f - 100f - 24f - actionsH - 12f);
            }
            _listBody = UiKit.VerticalLayoutScroll(_list, 8f, 0, TextAnchor.UpperLeft);

            _actions = UiKit.NewRect("Actions", _panel);
            UiKit.Place(_actions, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(880f, 236f), new Vector2(40f, 24f));
            if (UiMetrics.UseScroll) _actions.sizeDelta = new Vector2(880f, 3f * UiMetrics.MinButtonHeight + 16f);
            UiKit.VerticalLayout(_actions, 8f, 0, TextAnchor.LowerLeft);

            // 오른쪽: 설명 + 안내 메시지
            _explain = UiKit.Label(_panel, string.Empty, 28, UiTheme.Text, TextAlignmentOptions.TopLeft);
            UiKit.Place(_explain.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(560f, 520f), new Vector2(-40f, -100f));
            _message = UiKit.Label(_panel, string.Empty, 28, UiTheme.Accent, TextAlignmentOptions.TopLeft);
            UiKit.Place(_message.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(560f, 220f), new Vector2(-40f, 24f));

            // 확인 대화 상자(맨 위)
            Image shade = UiKit.NewImage("DialogShade", SafeRoot, new Color(0f, 0f, 0f, 0.6f));
            UiKit.Stretch(shade.rectTransform);
            _dialog = shade.rectTransform;
            Image box = UiKit.NewImage("DialogBox", _dialog, UiTheme.Panel);
            UiKit.Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1100f, 600f), Vector2.zero);
            if (UiMetrics.UseScroll) box.rectTransform.sizeDelta = new Vector2(1100f, 780f); // 버튼 2개가 커진 만큼
            UiKit.VerticalLayout(box.rectTransform, 14f, 36, TextAnchor.UpperCenter);
            TextMeshProUGUI dialogTitle = UiKit.Label(box.rectTransform, Loc.T("route_confirm_title", "정말 확정할까요?"), 44, UiTheme.Accent, TextAlignmentOptions.Center);
            dialogTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            _dialogBody = UiKit.Label(box.rectTransform, string.Empty, 28, UiTheme.Text, TextAlignmentOptions.TopLeft);
            LayoutElement bodyLe = _dialogBody.gameObject.AddComponent<LayoutElement>();
            bodyLe.preferredWidth = 1000f;
            bodyLe.preferredHeight = 330f;
            UiKit.MakeButton(box.rectTransform, UiText.RouteLockConfirm, OnCommit, 700f, 64f, UiTheme.ButtonAccent);
            UiKit.MakeButton(box.rectTransform, UiText.Cancel, delegate { _dialog.gameObject.SetActive(false); }, 700f, 64f, UiTheme.Button);
            _dialog.gameObject.SetActive(false);
        }

        /// <summary>뒤로가기 = 보류(아무 기록도 남기지 않고 닫기). 확인 대화 상자가 열려 있으면 그것만 닫는다.</summary>
        public override bool CanHandleBack { get { return true; } }

        public override bool HandleBack()
        {
            if (_dialog.gameObject.activeSelf) _dialog.gameObject.SetActive(false);
            else OnHold();
            return true;
        }

        public override void OnLocaleChanged()
        {
            if (_model != null && IsVisible) Render();
        }

        protected override void OnShow()
        {
            // 캐시(LRU)에서 밀려났을 수 있으므로 보일 때마다 다시 받는다.
            _bg.sprite = ArtLoader.Background("Cafe_Interior", "Night");
            _model = new RouteSelectModel(Ctx.Night, Ctx.Endings);
            _message.text = string.Empty;
            _dialog.gameObject.SetActive(false);
            Render();
        }

        // ---------------- 그리기 ----------------

        private void Render()
        {
            UiKit.ClearChildren(_listBody);
            UiKit.ClearChildren(_actions);

            int groupSize = _model.Selected.Count;
            for (int i = 0; i < _model.Options.Count; i++)
            {
                RouteLockOption o = _model.Options[i];
                string id = o.Id;
                bool selected = _model.IsSelected(id);
                string line1 = Loc.CharName(id, o.NameKo) + "   " + Loc.F("aff_trust", "호감 {0} · 신뢰 {1}", o.Aff.ToString("F0"), o.Trust.ToString("F0"))
                    + (selected ? "   " + Loc.F("route_selected_n", "[선택 {0}]", _model.Selected.IndexOf(id) + 1) : string.Empty);
                string line2;
                if (!o.Eligible) line2 = Loc.F("route_unavailable", "선택 불가 — {0}", Loc.Tr(o.Message));
                else if (selected && groupSize >= 2) line2 = _model.ReadinessHint(id, groupSize) + (o.HaremConsentDone ? Loc.T("route_consent_done", " · 동의 완료") : string.Empty);
                else line2 = Loc.T("route_available", "선택 가능");
                Button b = UiKit.MakeButton(_listBody, line1 + "\n" + line2, delegate { OnToggle(id); }, 880f, 78f,
                    selected ? UiTheme.ButtonAccent : UiTheme.Button);
                TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.fontSize = 26f;
                b.interactable = o.Eligible;
            }

            string confirmMessage;
            bool canConfirm = _model.CanConfirm(out confirmMessage);
            string confirmLabel = UiText.RouteSelectConfirm;
            if (_model.Mode == RouteChoiceMode.Single) confirmLabel += Loc.T("route_single_suffix", " (단독 루트)");
            else if (_model.Mode == RouteChoiceMode.Group) confirmLabel += Loc.F("route_group_suffix", " (공동 {0}명)", groupSize);
            Button confirm = UiKit.MakeButton(_actions, confirmLabel, OnConfirm, 880f, 68f, UiTheme.ButtonAccent);
            confirm.interactable = canConfirm;
            UiKit.MakeButton(_actions, UiText.RouteSelectCommunity, OnCommunity, 880f, 68f, UiTheme.Button);
            UiKit.MakeButton(_actions, UiText.RouteSelectHold, OnHold, 880f, 60f, UiTheme.Button);

            _explain.text = _model.ModeExplanation() + "\n\n" + _model.LockWarning();
        }

        // ---------------- 입력 ----------------

        private void OnToggle(string id)
        {
            string message;
            _model.Toggle(id, out message);
            _message.text = Loc.Tr(message);
            Render();
        }

        private void OnConfirm()
        {
            string message;
            if (!_model.CanConfirm(out message))
            {
                _message.text = Loc.Tr(message);
                return;
            }
            List<string> names = new List<string>();
            for (int i = 0; i < _model.Selected.Count; i++) names.Add(Ctx.CharDisplayName(_model.Selected[i]));
            string who = string.Join(", ", names.ToArray());
            string body = (_model.Selected.Count == 1
                ? Loc.F("route_commit_single", "{0}과(와)의 단독 루트로 확정합니다.", who)
                : Loc.F("route_commit_group", "{0}과(와) 함께하는 공동 루트로 확정합니다.", who))
                + "\n\n" + _model.LockWarning();
            if (_model.Selected.Count >= 2) body += "\n\n" + RouteSelectModel.GroupRulesText(Ctx.Endings, _model.Selected.Count);
            _dialogBody.text = body;
            _dialog.gameObject.SetActive(true);
        }

        private void OnCommit()
        {
            _dialog.gameObject.SetActive(false);
            string message;
            bool ok = _model.Commit(out message);
            if (!ok)
            {
                _message.text = Loc.Tr(message);
                Render();
                return;
            }
            Ctx.Game.QueueToast(Loc.Tr(message));
            List<SceneItem> scenes = RouteFlow.PostLockScenes(_model.LockedMembers, Ctx.Dialogue.HasNode);
            if (scenes.Count > 0) Ctx.Dialogue.PlayScenes(scenes, null, true);
            else Ctx.Nav.Show(Ctx.Game);
        }

        private void OnCommunity()
        {
            // 확정 가능한 인물이 있는데도 정하지 않기로 했을 때만 자동 안내를 멈춘다(나중에 적격자가 생기면 다시 안내).
            if (_model.AnyEligible) Ctx.Romance.SetFlag(RouteFlow.DeclinedFlag, 1);
            Ctx.Game.QueueToast(Loc.T("route_community_toast", "루트를 정하지 않고 계속합니다. 밤 메뉴에서 언제든 다시 정할 수 있습니다."));
            Ctx.Nav.Show(Ctx.Game);
        }

        private void OnHold()
        {
            Ctx.Nav.Show(Ctx.Game);
        }
    }
}
