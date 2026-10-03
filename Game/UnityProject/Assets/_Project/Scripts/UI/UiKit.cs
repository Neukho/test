using System;
using MorpheHouse.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorpheHouse.UI
{
    /// <summary>화면 전체에서 쓰는 색상 토큰.</summary>
    public static class UiTheme
    {
        public static readonly Color Background = new Color32(0x14, 0x20, 0x2E, 255);
        public static readonly Color Panel = new Color32(0x1F, 0x30, 0x44, 235);
        public static readonly Color Button = new Color32(0x3B, 0x57, 0x7A, 255);
        public static readonly Color ButtonAccent = new Color32(0xC9, 0x8F, 0x2B, 255);
        public static readonly Color Text = new Color32(0xF2, 0xEE, 0xE4, 255);
        public static readonly Color TextDim = new Color32(0xB5, 0xBE, 0xC9, 255);
        public static readonly Color Accent = new Color32(0xF2, 0xB8, 0x4B, 255);
        public static readonly Color Overlay = new Color(0f, 0f, 0f, 0.38f);
    }

    /// <summary>코드로 uGUI 를 조립하는 도우미. 1단계는 프리팹 없이 코드로 UI 를 만든다(에디터 작업 최소화).</summary>
    public static class UiKit
    {
        public static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>앵커·피벗·크기를 한 번에 지정한다.</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
        }

        public static Image NewImage(string name, Transform parent, Color color)
        {
            RectTransform rt = NewRect(name, parent);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, int size, Color color, TextAlignmentOptions align)
        {
            RectTransform rt = NewRect("Label", parent);
            TextMeshProUGUI tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = FontProvider.Asset;
            tmp.richText = false; // 플레이어 이름 등이 섞이는 모든 라벨에서 TMP 태그 해석 끔(콘텐츠에 TMP 서식 태그 사용 없음)
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            LocalizedLabel.Attach(tmp);
            return tmp;
        }

        public static Button MakeButton(Transform parent, string label, Action onClick, float width, float height, Color color)
        {
            // 최소 터치 목표(LayoutRules.MinTouchRefPx = 48): 더 작게 요청해도 이만큼은 확보한다.
            // 모바일에서는 물리 48 dp(UiMetrics.MinButtonHeight)까지 올린다. 데스크톱은 48 그대로.
            width = TouchTargetPolicy.Apply(LayoutRules.ClampTouch(width), UiMetrics.MinButtonHeight);
            height = TouchTargetPolicy.Apply(LayoutRules.ClampTouch(height), UiMetrics.MinButtonHeight);
            RectTransform rt = NewRect("Button_" + label, parent);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;

            Button btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.88f, 0.93f, 1f, 1f);
            cb.pressedColor = new Color(0.72f, 0.78f, 0.88f, 1f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            btn.colors = cb;

            LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;

            TextMeshProUGUI text = Label(rt, label, 30, UiTheme.Text, TextAlignmentOptions.Center);
            Stretch((RectTransform)text.transform);

            // 클릭 효과음: 뒤로/닫기/취소 계열 → ui_back, 강조색 → ui_confirm, 그 밖 → ui_click. 소리가 없으면 아무 일도 없다.
            string sfx = AudioCues.ButtonSe(color == UiTheme.ButtonAccent, IsBackLabel(label));
            btn.onClick.AddListener(() => UiAudio.Play(sfx));
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        public static void SetButtonLabel(Button button, string label)
        {
            TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>();
            if (text == null) return;
            text.text = label;
            LocalizedLabel.Rebind(text);
        }

        private static bool IsBackLabel(string label)
        {
            return label == UiText.Back || label == UiText.Close || label == UiText.Cancel || label == UiText.RouteLockLater;
        }

        public static VerticalLayoutGroup VerticalLayout(RectTransform rt, float spacing, int padding, TextAnchor alignment)
        {
            VerticalLayoutGroup v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childAlignment = alignment;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = false;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HorizontalLayout(RectTransform rt, float spacing, int padding, TextAnchor alignment)
        {
            HorizontalLayoutGroup h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(padding, padding, padding, padding);
            h.childAlignment = alignment;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            return h;
        }

        /// <summary>
        /// 세로 버튼 목록용 레이아웃. 데스크톱은 panel 자신에 VerticalLayoutGroup 을 붙이고 panel 을 돌려준다(기존과 같음).
        /// 모바일(UiMetrics.UseScroll)은 panel 안에 뷰포트(RectMask2D + ScrollRect)와 내용 사각형을 만들어 그 안에 레이아웃을 붙이고 내용 사각형을 돌려준다
        /// — 버튼이 48 dp 로 커져 패널을 넘쳐도 스크롤로 닿는다. 호출자는 돌려받은 RectTransform 을 자식의 부모로 쓴다.
        /// </summary>
        public static RectTransform VerticalLayoutScroll(RectTransform panel, float spacing, int padding, TextAnchor alignment)
        {
            if (!UiMetrics.UseScroll)
            {
                VerticalLayout(panel, spacing, padding, alignment);
                return panel;
            }
            RectTransform viewport = NewRect("Viewport", panel);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, 0f);
            VerticalLayout(content, spacing, padding, alignment);
            ContentSizeFitter fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            ScrollRect scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            return content;
        }

        /// <summary>
        /// 이미 높이가 정해진 레이아웃 안에서 "남은 높이만큼" 스크롤되는 세로 열(GameScreen 의 버튼 영역). height 가 0 이하면 감싸지 않고 parent 를 돌려준다.
        /// </summary>
        public static RectTransform MakeScrollColumn(RectTransform parent, float width, float height, float spacing)
        {
            if (height <= 0f) return parent;
            RectTransform root = NewRect("ScrollColumn", parent);
            LayoutElement le = root.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.minHeight = height;
            Image hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            RectTransform viewport = NewRect("Viewport", root);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VerticalLayout(content, spacing, 0, TextAnchor.UpperLeft);
            ContentSizeFitter fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            return content;
        }

        /// <summary>자식을 모두 제거한다. Destroy 는 프레임 끝에 실행되므로 먼저 비활성화해 레이아웃에서 제외한다.</summary>
        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        /// <summary>코드로 만드는 가로 슬라이더(배경·채움·핸들). onChanged 는 값이 바뀔 때마다 호출된다.</summary>
        public static Slider MakeSlider(Transform parent, float width, float height, float min, float max, float value, Action<float> onChanged)
        {
            RectTransform root = NewRect("Slider", parent);
            LayoutElement le = root.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = LayoutRules.ClampTouch(height);

            // 투명한 전체 크기 터치 영역: 막대(높이의 30%)만 눌리면 손가락으로 맞추기 어렵다.
            Image hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            Image bg = NewImage("Background", root, new Color(1f, 1f, 1f, 0.18f));
            bg.rectTransform.anchorMin = new Vector2(0f, 0.35f);
            bg.rectTransform.anchorMax = new Vector2(1f, 0.65f);
            bg.rectTransform.offsetMin = Vector2.zero;
            bg.rectTransform.offsetMax = Vector2.zero;

            RectTransform fillArea = NewRect("Fill Area", root);
            fillArea.anchorMin = new Vector2(0f, 0.35f);
            fillArea.anchorMax = new Vector2(1f, 0.65f);
            fillArea.offsetMin = Vector2.zero;
            fillArea.offsetMax = Vector2.zero;
            Image fill = NewImage("Fill", fillArea, UiTheme.Accent);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            fill.raycastTarget = false;

            RectTransform handleArea = NewRect("Handle Slide Area", root);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(24f, 0f);
            handleArea.offsetMax = new Vector2(-24f, 0f);
            Image handle = NewImage("Handle", handleArea, UiTheme.Text);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);
            handle.rectTransform.sizeDelta = new Vector2(48f, 0f);

            Slider slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.value = value;
            if (onChanged != null) slider.onValueChanged.AddListener(delegate (float v) { onChanged(v); });
            return slider;
        }

        /// <summary>코드로 만드는 한 줄 입력창(TMP_InputField). 글자 수 제한·값 변경 콜백 포함.</summary>
        public static TMP_InputField MakeInput(Transform parent, string initial, string placeholder, int maxLength, float width, float height, Action<string> onChanged)
        {
            RectTransform root = NewRect("Input", parent);
            Image bg = root.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.14f);
            LayoutElement le = root.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;

            RectTransform viewport = NewRect("Text Area", root);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(18f, 6f);
            viewport.offsetMax = new Vector2(-18f, -6f);
            viewport.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI text = Label(viewport, string.Empty, 34, UiTheme.Text, TextAlignmentOptions.MidlineLeft);
            Stretch((RectTransform)text.transform);
            TextMeshProUGUI hint = Label(viewport, placeholder, 34, UiTheme.TextDim, TextAlignmentOptions.MidlineLeft);
            Stretch((RectTransform)hint.transform);

            TMP_InputField input = root.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = hint;
            input.characterLimit = maxLength;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = initial ?? string.Empty;
            if (onChanged != null) input.onValueChanged.AddListener(delegate (string v) { onChanged(v); });
            return input;
        }

        /// <summary>줄 단위 정보 라벨(레이아웃 그룹 안에서 사용).</summary>
        public static TextMeshProUGUI Line(Transform parent, string text, int size, Color color)
        {
            TextMeshProUGUI t = Label(parent, text, size, color, TextAlignmentOptions.Left);
            LayoutElement le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 1000f;
            le.minHeight = size + 14f;
            return t;
        }
    }
}
