using System;
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
    /// 주인공 생성 화면(난이도 선택 직후): 이름 · 성별 · 종(임시) · 외형 프리셋 + NPC 호칭 미리보기.
    /// 검증 규칙은 ProfileValidator(순수), 미리보기는 ProfilePreview(순수)가 계산하고 이 클래스는 표시만 한다.
    /// </summary>
    public sealed class CharacterCreateScreen : ScreenBase
    {
        private static readonly Color[] Swatches =
        {
            new Color32(0x3B, 0x2F, 0x2F, 255), new Color32(0xC9, 0x8F, 0x2B, 255), new Color32(0xB5, 0x51, 0x3C, 255),
            new Color32(0x6A, 0x8F, 0xB5, 255), new Color32(0x9B, 0x7B, 0xB8, 255), new Color32(0xD9, 0xD4, 0xC7, 255)
        };

        private readonly ProtagonistProfile _profile = ProtagonistProfile.Default();
        private readonly List<Button> _genderButtons = new List<Button>();
        private readonly List<Button> _speciesButtons = new List<Button>();
        private readonly List<Button> _swatchButtons = new List<Button>();

        private TMP_InputField _nameInput;
        private TextMeshProUGUI _error;
        private TextMeshProUGUI _preview;
        private Button _confirm;
        private Image _bg;

        public CharacterCreateScreen(AppContext ctx) : base(ctx) { }

        protected override void OnBuild()
        {
            Image bg = UiKit.NewImage("Background", FullRoot, Color.white);
            UiKit.Stretch(bg.rectTransform);
            _bg = bg;
            bg.sprite = ArtLoader.Background("Street", "Night");
            bg.preserveAspect = false;
            bg.raycastTarget = false;
            Image dim = UiKit.NewImage("Dim", FullRoot, new Color(0f, 0f, 0f, 0.55f));
            UiKit.Stretch(dim.rectTransform);
            dim.raycastTarget = false;

            // 왼쪽: 입력 폼
            Image form = UiKit.NewImage("Form", SafeRoot, UiTheme.Panel);
            form.raycastTarget = false;
            UiKit.Place(form.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(980f, 880f), new Vector2(24f, 0f));
            RectTransform f = UiKit.VerticalLayoutScroll(form.rectTransform, 10f, 28, TextAnchor.UpperLeft);

            Heading(f, UiText.CreateTitle, 48, UiTheme.Accent);

            Heading(f, UiText.CreateName, 28, UiTheme.TextDim);
            _nameInput = UiKit.MakeInput(f, ProtagonistProfile.DefaultName, UiText.CreateNameHint, ProfileValidator.MaxNameLength, 640f, 70f, OnNameChanged);

            Heading(f, UiText.CreateGender, 28, UiTheme.TextDim);
            RectTransform genderRow = Row(f);
            for (int i = 0; i < ProfileValidator.GenderIds.Length; i++)
            {
                int idx = i;
                _genderButtons.Add(UiKit.MakeButton(genderRow, Loc.Gender(ProfileValidator.GenderIds[i], ProfileValidator.GenderLabels[i]), delegate { PickGender(idx); }, 200f, 64f, UiTheme.Button));
            }

            Heading(f, UiText.CreateSpecies, 28, UiTheme.TextDim);
            RectTransform speciesRow1 = Row(f);
            RectTransform speciesRow2 = Row(f);
            for (int i = 0; i < ProfileValidator.SpeciesIds.Length; i++)
            {
                int idx = i;
                RectTransform parent = i < 4 ? speciesRow1 : speciesRow2;
                _speciesButtons.Add(UiKit.MakeButton(parent, Loc.Species(ProfileValidator.SpeciesIds[i], ProfileValidator.SpeciesLabels[i]), delegate { PickSpecies(idx); }, 200f, 60f, UiTheme.Button));
            }

            Heading(f, UiText.CreateAppearance, 28, UiTheme.TextDim);
            RectTransform swatchRow = Row(f);
            for (int i = 0; i < ProfileValidator.AppearanceCount; i++)
            {
                int idx = i;
                Button b = UiKit.MakeButton(swatchRow, (i + 1).ToString(), delegate { PickAppearance(idx); }, 96f, 72f, Swatches[i]);
                _swatchButtons.Add(b);
            }

            _error = UiKit.Line(f, string.Empty, 26, new Color32(0xE8, 0x7A, 0x7A, 255));

            RectTransform buttons = Row(f);
            UiKit.MakeButton(buttons, UiText.Back, delegate { Ctx.ExitToTitle(); }, 260f, 76f, UiTheme.Button);
            _confirm = UiKit.MakeButton(buttons, UiText.CreateConfirm, OnConfirm, 520f, 76f, UiTheme.ButtonAccent);

            // 오른쪽: 호칭 미리보기
            Image side = UiKit.NewImage("Preview", SafeRoot, UiTheme.Panel);
            side.raycastTarget = false;
            UiKit.Place(side.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(640f, 880f), new Vector2(-24f, 0f));
            UiKit.VerticalLayout(side.rectTransform, 14f, 28, TextAnchor.UpperLeft);
            Heading(side.rectTransform, UiText.CreatePreviewTitle, 40, UiTheme.Accent);
            _preview = UiKit.Line(side.rectTransform, string.Empty, 30, UiTheme.Text);
            _preview.lineSpacing = 12f;
        }

        /// <summary>뒤로가기 = 타이틀로(타이틀의 난이도 선택으로 돌아가지는 않는다).</summary>
        public override bool CanHandleBack { get { return true; } }

        public override bool HandleBack()
        {
            Ctx.ExitToTitle();
            return true;
        }

        protected override void OnShow()
        {
            // 캐시(LRU)에서 밀려났을 수 있으므로 보일 때마다 다시 받는다.
            _bg.sprite = ArtLoader.Background("Street", "Night");
            _profile.Name = ProtagonistProfile.DefaultName;
            _profile.Gender = "neutral";
            _profile.Species = "human";
            _profile.Appearance = 1;
            _nameInput.SetTextWithoutNotify(_profile.Name);
            Refresh();
        }

        public override void OnLocaleChanged()
        {
            if (_confirm != null && IsVisible) Refresh();
        }

        private static void Heading(Transform parent, string text, int size, Color color)
        {
            TextMeshProUGUI t = UiKit.Label(parent, text, size, color, TextAlignmentOptions.Left);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = size + 18f;
        }

        private static RectTransform Row(Transform parent)
        {
            RectTransform row = UiKit.NewRect("Row", parent);
            UiKit.HorizontalLayout(row, 12f, 0, TextAnchor.MiddleLeft);
            return row;
        }

        // ---------------- 입력 ----------------

        private void OnNameChanged(string value)
        {
            _profile.Name = value;
            Refresh();
        }

        private void PickGender(int index)
        {
            _profile.Gender = ProfileValidator.GenderIds[index];
            Refresh();
        }

        private void PickSpecies(int index)
        {
            _profile.Species = ProfileValidator.SpeciesIds[index];
            Refresh();
        }

        private void PickAppearance(int index)
        {
            _profile.Appearance = index + 1;
            Refresh();
        }

        private void OnConfirm()
        {
            ProfileValidation v = ProfileValidator.Validate(_profile);
            if (!v.IsValid)
            {
                _error.text = Loc.Tr(v.FirstMessage);
                return;
            }
            Ctx.StartNewGame(Ctx.PendingDifficulty, v.Profile);
        }

        // ---------------- 표시 ----------------

        private void Refresh()
        {
            ProfileValidation v = ProfileValidator.Validate(_profile);
            _error.text = Loc.Tr(v.FirstMessage);
            _confirm.interactable = v.IsValid;

            Mark(_genderButtons, Array.IndexOf(ProfileValidator.GenderIds, _profile.Gender));
            Mark(_speciesButtons, Array.IndexOf(ProfileValidator.SpeciesIds, _profile.Species));
            for (int i = 0; i < _swatchButtons.Count; i++)
                _swatchButtons[i].transform.localScale = (i + 1 == _profile.Appearance) ? new Vector3(1.18f, 1.18f, 1f) : Vector3.one;

            // 이름이 잘못돼도 미리보기는 기본 이름으로 계속 보여 준다.
            ProtagonistProfile shown = v.IsValid ? v.Profile : _profile.Clone();
            if (!v.IsValid && v.NameError.Length > 0) shown.Name = ProtagonistProfile.DefaultName;
            List<PreviewEntry> entries = ProfilePreview.Build(shown, Ctx.Dialogue.Book, Ctx.Settings.Locale, Ctx.CharDisplayName);
            _preview.text = Loc.Gender(shown.Gender, ProfileValidator.GenderLabel(shown.Gender)) + " · " + Loc.Species(shown.Species, ProfileValidator.SpeciesLabel(shown.Species))
                + " · " + Loc.F("create_appearance_n", "외형 {0}", shown.Appearance) + "\n\n" + ProfilePreview.ToText(entries);
        }

        private static void Mark(List<Button> buttons, int selected)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                Image img = buttons[i].targetGraphic as Image;
                if (img != null) img.color = i == selected ? UiTheme.ButtonAccent : UiTheme.Button;
            }
        }
    }
}
