using MorpheHouse.ArtPipeline;
using MorpheHouse.GameFlow;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 데모 종료 화면: 마지막 데모 챕터를 마친 뒤 이어서 보이는 카드. 감사 문구 · 위시리스트 안내(현지화 문자열) · 통계 · 스토어 버튼(store_url 이 https 일 때만) · 타이틀로.
    /// 문구는 모두 ui_ko/ui_en.csv 의 demo_* 키. 데모 설정은 DemoConfig(Resources/Config/build_config.csv).
    /// </summary>
    public sealed class DemoEndScreen : ScreenBase
    {
        private RectTransform _content;
        private Image _bg;

        public DemoEndScreen(AppContext ctx) : base(ctx) { }

        protected override void OnBuild()
        {
            Image bg = UiKit.NewImage("Background", FullRoot, Color.white);
            UiKit.Stretch(bg.rectTransform);
            _bg = bg;
            bg.preserveAspect = false;
            bg.raycastTarget = false;

            Image dim = UiKit.NewImage("Dim", FullRoot, new Color(0f, 0f, 0f, 0.5f));
            UiKit.Stretch(dim.rectTransform);
            dim.raycastTarget = false;

            Image panel = UiKit.NewImage("Panel", SafeRoot, UiTheme.Panel);
            panel.raycastTarget = false;
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1200f, 780f), Vector2.zero);
            _content = UiKit.VerticalLayoutScroll(panel.rectTransform, 16f, 40, TextAnchor.UpperCenter);
        }

        public override void OnLocaleChanged()
        {
            if (IsVisible) OnShow();
        }

        protected override void OnShow()
        {
            _bg.sprite = ArtLoader.Background("Cafe_Interior", "Evening");
            UiKit.ClearChildren(_content);

            TextMeshProUGUI title = UiKit.Label(_content, Loc.T("demo_end_title", "데모는 여기까지예요"), 64, UiTheme.Accent, TextAlignmentOptions.Center);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 90f;
            TextMeshProUGUI thanks = UiKit.Label(_content, Loc.T("demo_end_thanks", "플레이해 주셔서 감사합니다!"), 38, UiTheme.Text, TextAlignmentOptions.Center);
            thanks.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;

            if (Ctx.State != null)
            {
                List<string> lines = DemoSummary.Lines(Ctx.State.Day - 1, Ctx.State.Chapter - 1 < 1 ? 1 : Ctx.State.Chapter - 1, Ctx.State.Gold,
                    Ctx.State.ShopLevel, Ctx.State.Reputation, Ctx.State.PlayTimeSec, Ctx.RunCgs.Count);
                for (int i = 0; i < lines.Count; i++) UiKit.Line(_content, lines[i], 30, UiTheme.Text);
            }

            TextMeshProUGUI cta = UiKit.Label(_content, Loc.T("demo_end_cta", "정식 버전에서는 8개 챕터 전체와 모든 인물의 엔딩을 만날 수 있어요. 스토어 페이지에서 위시리스트에 담아 주세요!"), 32, UiTheme.Accent, TextAlignmentOptions.Center);
            LayoutElement cl = cta.gameObject.AddComponent<LayoutElement>();
            cl.preferredWidth = 1060f;
            cl.preferredHeight = 120f;
            TextMeshProUGUI note = UiKit.Label(_content, Loc.T("demo_end_save_note", "이 저장 데이터는 정식 버전에서도 그대로 이어서 할 수 있어요."), 24, UiTheme.TextDim, TextAlignmentOptions.Center);
            note.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

            string url = DemoConfig.Current.StoreUrl;
            if (DemoConfig.IsSafeUrl(url))
                UiKit.MakeButton(_content, Loc.T("demo_end_wishlist", "스토어에서 위시리스트에 담기"), delegate { Application.OpenURL(url); }, 760f, 76f, UiTheme.ButtonAccent);
            UiKit.MakeButton(_content, UiText.ToTitle, Ctx.ExitToTitle, 600f, 76f, UiTheme.Button);
        }
    }
}
