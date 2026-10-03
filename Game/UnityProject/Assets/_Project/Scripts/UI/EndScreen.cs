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
    /// 엔딩 화면: 엔딩 이름(한국어) · 부제 · 핵심 통계 · 플레이 시간 · 본 CG 수 · 도달 엔딩 수와 타이틀 버튼.
    /// 문구 조립은 순수 EndSummary 가 한다. AppContext.CurrentEnding 이 비어 있으면 아무것도 보여 주지 않고 타이틀로 가는 버튼만 둔다.
    /// </summary>
    public sealed class EndScreen : ScreenBase
    {
        private RectTransform _content;
        private Image _bg;

        public EndScreen(AppContext ctx) : base(ctx) { }

        protected override void OnBuild()
        {
            Image bg = UiKit.NewImage("Background", FullRoot, Color.white);
            UiKit.Stretch(bg.rectTransform);
            _bg = bg;
            bg.sprite = ArtLoader.Background("Cafe_Interior", "Evening");
            bg.preserveAspect = false;
            bg.raycastTarget = false;

            Image dim = UiKit.NewImage("Dim", FullRoot, new Color(0f, 0f, 0f, 0.5f));
            UiKit.Stretch(dim.rectTransform);
            dim.raycastTarget = false;

            Image panel = UiKit.NewImage("Panel", SafeRoot, UiTheme.Panel);
            panel.raycastTarget = false;
            _content = panel.rectTransform;
            UiKit.Place(_content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1200f, 780f), Vector2.zero);
            UiKit.VerticalLayout(_content, 16f, 40, TextAnchor.UpperCenter);
        }

        public override void OnLocaleChanged()
        {
            if (IsVisible) OnShow();
        }

        protected override void OnShow()
        {
            // 캐시(LRU)에서 밀려났을 수 있으므로 보일 때마다 다시 받는다.
            _bg.sprite = ArtLoader.Background("Cafe_Interior", "Evening");
            UiKit.ClearChildren(_content);
            EndingPlan plan = Ctx.CurrentEnding;
            TextMeshProUGUI small = UiKit.Label(_content, UiText.EndScreenTitle, 32, UiTheme.TextDim, TextAlignmentOptions.Center);
            small.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;

            if (plan != null && Ctx.State != null)
            {
                EndSummary s = EndSummary.Build(plan, Ctx.State, Ctx.RunCgs.Count, Ctx.Profile.SeenCgs.Count,
                    Ctx.Profile.ReachedCount, EndingGallery.TotalSlots);
                TextMeshProUGUI title = UiKit.Label(_content, s.Title, 72, UiTheme.Accent, TextAlignmentOptions.Center);
                title.gameObject.AddComponent<LayoutElement>().preferredHeight = 100f;
                TextMeshProUGUI sub = UiKit.Label(_content, s.Subtitle, 38, UiTheme.Text, TextAlignmentOptions.Center);
                sub.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
                for (int i = 0; i < s.Lines.Count; i++) UiKit.Line(_content, s.Lines[i], 30, UiTheme.Text);
            }
            UiKit.MakeButton(_content, UiText.ToTitle, Ctx.ExitToTitle, 600f, 76f, UiTheme.ButtonAccent);
        }
    }
}
