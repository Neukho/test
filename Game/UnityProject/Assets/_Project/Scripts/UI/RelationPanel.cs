using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 밤 화면 오른쪽의 관계 패널: 인물별 호감/신뢰 막대와 호감 구간, 볼 수 있는 이벤트(★), 루트 확정 표시(공동 루트는 구성원 전원을 (루트) 로 표시).
    /// 어두운 띠는 현재 챕터의 상한(chapters.csv aff_cap / trust_cap)이다.
    /// </summary>
    public sealed class RelationPanel
    {
        private static readonly Color AffColor = new Color32(0xE8, 0x7A, 0x90, 255);
        private static readonly Color TrustColor = new Color32(0x6F, 0xC3, 0xD9, 255);
        private const float AffMax = 100f;
        private const float TrustMax = 50f;
        private const float BarWidth = 460f;

        private readonly RectTransform _root;

        public RelationPanel(Transform parent)
        {
            Image panel = UiKit.NewImage("RelationPanel", parent, UiTheme.Panel);
            panel.raycastTarget = false;
            _root = panel.rectTransform;
            UiKit.Place(_root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(520f, 790f), new Vector2(-20f, -48f));
            UiKit.VerticalLayout(_root, 6f, 20, TextAnchor.UpperLeft);
        }

        public RectTransform Root { get { return _root; } }

        public void SetActive(bool active)
        {
            _root.gameObject.SetActive(active);
        }

        public void Refresh(GameState state, NightRomanceActions night, RomanceService romance, ChapterRow chapter)
        {
            UiKit.ClearChildren(_root);
            Heading(Loc.T("relation_heading", "관계"), 30, UiTheme.Accent);
            for (int i = 0; i < CharIds.All.Length; i++)
            {
                string id = CharIds.All[i];
                CharState cs;
                if (!state.Chars.TryGetValue(id, out cs)) continue;

                RectTransform row = UiKit.NewRect("Row_" + id, _root);
                UiKit.VerticalLayout(row, 3f, 0, TextAnchor.UpperLeft);

                if (!night.IsAvailable(id))
                {
                    UiKit.Line(row, Loc.CharName(id, night.CharName(id)) + "  " + Loc.T("relation_not_met", "아직 만나지 못했다"), 24, UiTheme.TextDim);
                    continue;
                }
                string band = BandLabel(AddressBook.BandOf(cs.Aff));
                string star = night.HasEvent(id) ? "  ★" : string.Empty;
                string frozen = romance.IsFrozen(id) ? Loc.T("relation_frozen", "  (고정)") : (state.IsRouteMember(id) ? Loc.T("relation_route", "  (루트)") : string.Empty);
                TextMeshProUGUI name = UiKit.Line(row, Loc.CharName(id, night.CharName(id)) + "  " + band + star + frozen
                    + "   " + Loc.F("aff_trust", "호감 {0} · 신뢰 {1}", cs.Aff.ToString("F0"), cs.Trust.ToString("F0")), 24, UiTheme.Text);
                name.rectTransform.sizeDelta = new Vector2(BarWidth, 30f);

                Bar(row, chapter.AffCap / AffMax, cs.Aff / AffMax, AffColor);
                Bar(row, chapter.TrustCap / TrustMax, cs.Trust / TrustMax, TrustColor);
            }
        }

        private void Heading(string text, int size, Color color)
        {
            TextMeshProUGUI t = UiKit.Line(_root, text, size, color);
            t.rectTransform.sizeDelta = new Vector2(BarWidth, size + 12f);
        }

        private static void Bar(Transform parent, float cap01, float fill01, Color fillColor)
        {
            Image bg = UiKit.NewImage("Bar", parent, new Color(1f, 1f, 1f, 0.07f));
            bg.raycastTarget = false;
            LayoutElement le = bg.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = BarWidth;
            le.preferredHeight = 12f;

            Image cap = UiKit.NewImage("Cap", bg.transform, new Color(1f, 1f, 1f, 0.14f));
            cap.raycastTarget = false;
            cap.rectTransform.anchorMin = Vector2.zero;
            cap.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(cap01), 1f);
            cap.rectTransform.offsetMin = Vector2.zero;
            cap.rectTransform.offsetMax = Vector2.zero;

            Image fill = UiKit.NewImage("Fill", bg.transform, fillColor);
            fill.raycastTarget = false;
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(fill01), 1f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
        }

        public static string BandLabel(AffinityBand band)
        {
            switch (band)
            {
                case AffinityBand.Close: return Loc.T("band_close", "가까움");
                case AffinityBand.Friendly: return Loc.T("band_friendly", "친밀");
                default: return Loc.T("band_stranger", "낯섦");
            }
        }
    }
}
