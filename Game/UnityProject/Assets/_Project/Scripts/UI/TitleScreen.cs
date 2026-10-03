using System.Collections.Generic;
using System.Text;
using MorpheHouse.GameFlow;
using MorpheHouse.ArtPipeline;
using MorpheHouse.Core;
using MorpheHouse.Data;
using MorpheHouse.Save;
using MorpheHouse.Tycoon;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorpheHouse.UI
{
    /// <summary>타이틀: 새 게임(난이도 선택) · 이어하기 · 개발용 자동 플레이 검증 · 종료.</summary>
    public sealed class TitleScreen : ScreenBase
    {
        private RectTransform _menu;
        private RectTransform _difficulty;
        private TextMeshProUGUI _message;
        private Button _continueButton;
        private SettingsPanel _settings;
        private RectTransform _gallery;
        private TextMeshProUGUI _galleryBody;
        private TextMeshProUGUI _endingCount;
        private Button _galleryButton;
        private Image _bg;

        public TitleScreen(AppContext ctx) : base(ctx) { }

        protected override void OnBuild()
        {
            Image bg = UiKit.NewImage("Background", FullRoot, Color.white);
            UiKit.Stretch(bg.rectTransform);
            _bg = bg;
            bg.sprite = ArtLoader.Background("Street", "Night");
            bg.preserveAspect = false;
            bg.raycastTarget = false;

            Image dim = UiKit.NewImage("Dim", FullRoot, UiTheme.Overlay);
            UiKit.Stretch(dim.rectTransform);
            dim.raycastTarget = false;

            TextMeshProUGUI title = UiKit.Label(SafeRoot, UiText.Title, 92, UiTheme.Accent, TextAlignmentOptions.Center);
            UiKit.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(1600f, 140f), new Vector2(0f, -70f));
            TextMeshProUGUI sub = UiKit.Label(SafeRoot, UiText.Subtitle, 34, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiKit.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(1200f, 60f), new Vector2(0f, -210f));

            _menu = UiKit.NewRect("Menu", SafeRoot);
            UiKit.Place(_menu, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(560f, 520f), new Vector2(0f, -335f));
            RectTransform menuBody = UiKit.VerticalLayoutScroll(_menu, 12f, 0, TextAnchor.UpperCenter);

            UiKit.MakeButton(menuBody, UiText.NewGame, ShowDifficulty, 520f, 84f, UiTheme.ButtonAccent);
            _continueButton = UiKit.MakeButton(menuBody, UiText.Continue, OnContinue, 520f, 84f, UiTheme.Button);
            _galleryButton = UiKit.MakeButton(menuBody, UiText.Gallery, OnGallery, 520f, 72f, UiTheme.Button);
            UiKit.MakeButton(menuBody, UiText.Settings, OnSettings, 520f, 72f, UiTheme.Button);
            UiKit.MakeButton(menuBody, UiText.DevAutoPlay, OnAutoPlay, 520f, 72f, UiTheme.Button);
            UiKit.MakeButton(menuBody, UiText.Quit, OnQuit, 520f, 72f, UiTheme.Button);

            _difficulty = UiKit.NewRect("Difficulty", SafeRoot);
            UiKit.Place(_difficulty, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(1000f, 560f), new Vector2(0f, -335f));
            RectTransform diffBody = UiKit.VerticalLayoutScroll(_difficulty, 16f, 0, TextAnchor.UpperCenter);
            UiKit.Label(diffBody, UiText.ChooseDifficulty, 40, UiTheme.Text, TextAlignmentOptions.Center)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            UiKit.MakeButton(diffBody, UiText.DifficultyStory, () => Ctx.OpenCharacterCreate(Difficulty.Story), 900f, 84f, UiTheme.Button);
            UiKit.MakeButton(diffBody, UiText.DifficultyNormal, () => Ctx.OpenCharacterCreate(Difficulty.Normal), 900f, 84f, UiTheme.ButtonAccent);
            UiKit.MakeButton(diffBody, UiText.DifficultyHard, () => Ctx.OpenCharacterCreate(Difficulty.Hard), 900f, 84f, UiTheme.Button);
            UiKit.MakeButton(diffBody, UiText.Back, ShowMenu, 400f, 64f, UiTheme.Button);

            _settings = new SettingsPanel(Ctx, SafeRoot, ShowMenu);

            // 도달한 엔딩 수(타이틀 아래) + 갤러리 목록(단순 스텁)
            _endingCount = UiKit.Label(SafeRoot, string.Empty, 30, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiKit.Place(_endingCount.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(1200f, 50f), new Vector2(0f, -270f));

            Image galleryPanel = UiKit.NewImage("Gallery", SafeRoot, UiTheme.Panel);
            _gallery = galleryPanel.rectTransform;
            UiKit.Place(_gallery, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(1100f, 575f), new Vector2(0f, -325f));
            RectTransform galBody = UiKit.VerticalLayoutScroll(_gallery, 10f, 30, TextAnchor.UpperCenter);
            UiKit.Label(galBody, UiText.GalleryTitle, 40, UiTheme.Accent, TextAlignmentOptions.Center)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
            _galleryBody = UiKit.Label(galBody, string.Empty, 28, UiTheme.Text, TextAlignmentOptions.TopLeft);
            LayoutElement bodyLe = _galleryBody.gameObject.AddComponent<LayoutElement>();
            bodyLe.preferredWidth = 1000f;
            bodyLe.preferredHeight = 360f;
            if (UiMetrics.UseScroll)
            {
                // 모바일: 버튼이 48 dp 로 커진 만큼 본문을 줄인다(9줄 × 글자 24 ≈ 270).
                bodyLe.preferredHeight = 270f;
                _galleryBody.fontSize = 24f;
            }
            UiKit.MakeButton(galBody, UiText.Back, ShowMenu, 400f, 64f, UiTheme.Button);
            _gallery.gameObject.SetActive(false);

            _message = UiKit.Label(SafeRoot, string.Empty, 28, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiKit.Place(_message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(1500f, 56f), new Vector2(0f, 6f));
        }

        protected override void OnShow()
        {
            // 캐시(LRU)에서 밀려났을 수 있으므로 보일 때마다 다시 받는다.
            _bg.sprite = ArtLoader.Background("Street", "Night");
            _message.text = string.Empty;
            _continueButton.interactable = Ctx.HasAnySave();
            RefreshDynamic();
            ShowMenu();
        }

        // 표의 글자와 같지 않은(숫자가 섞인) 문구: 처음 보일 때와 언어가 바뀔 때 다시 만든다.
        private void RefreshDynamic()
        {
            _endingCount.text = UiStrings.FormatText(UiText.TitleEndingsReached, Ctx.Profile.ReachedCount, EndingGallery.TotalSlots);
            UiKit.SetButtonLabel(_galleryButton, UiText.Gallery + " (" + Ctx.Profile.ReachedCount + "/" + EndingGallery.TotalSlots + ")");
        }

        public override void OnLocaleChanged()
        {
            RefreshDynamic();
            if (_gallery.gameObject.activeSelf) OnGallery(); // 갤러리 본문은 동적 문구라 다시 만든다
        }

        /// <summary>하위 패널(난이도·갤러리·설정)이 열려 있으면 뒤로가기는 메뉴로 돌아간다. 최상위 메뉴에서는 처리하지 않는다(실수로 종료되지 않게).</summary>
        public override bool CanHandleBack { get { return !_menu.gameObject.activeSelf; } }

        public override bool HandleBack()
        {
            if (_menu.gameObject.activeSelf) return false;
            if (_settings.IsOpen) _settings.Close();
            else ShowMenu();
            return true;
        }

        private void ShowMenu()
        {
            _menu.gameObject.SetActive(true);
            _difficulty.gameObject.SetActive(false);
            _gallery.gameObject.SetActive(false);
            _settings.Hide();
        }

        private void ShowDifficulty()
        {
            _menu.gameObject.SetActive(false);
            _difficulty.gameObject.SetActive(true);
            _gallery.gameObject.SetActive(false);
            _settings.Hide();
        }

        /// <summary>엔딩 갤러리(스텁): 인물별 노멀/굿/트루, 공동 2·3인, 커뮤니티의 도달 여부를 글자로 보여 준다.</summary>
        private void OnGallery()
        {
            _menu.gameObject.SetActive(false);
            _difficulty.gameObject.SetActive(false);
            _settings.Hide();
            List<EndingSlot> slots = EndingGallery.Slots(Ctx.Profile, Ctx.CharDisplayName);
            StringBuilder sb = new StringBuilder();
            for (int c = 0; c < CharIds.All.Length; c++)
            {
                sb.Append(Ctx.CharDisplayName(CharIds.All[c])).Append(":  ");
                for (int k = 0; k < 3; k++)
                {
                    EndingSlot s = slots[c * 3 + k];
                    sb.Append(k == 0 ? Loc.T("ending_kind_normal", "노멀") : (k == 1 ? Loc.T("ending_kind_good", "굿") : Loc.T("ending_kind_true", "트루"))).Append(' ').Append(s.Reached ? "[O]" : "[ ]").Append(k < 2 ? "   " : string.Empty);
                }
                sb.Append('\n');
            }
            for (int i = CharIds.All.Length * 3; i < slots.Count; i++)
                sb.Append(slots[i].Title).Append(":  ").Append(slots[i].Reached ? "[O]" : "[ ]").Append('\n');
            _galleryBody.text = sb.ToString();
            _gallery.gameObject.SetActive(true);
        }

        private void OnSettings()
        {
            _menu.gameObject.SetActive(false);
            _difficulty.gameObject.SetActive(false);
            _gallery.gameObject.SetActive(false);
            _settings.Show();
        }

        private void OnContinue()
        {
            LoadResult r = Ctx.LoadLatest();
            switch (r.Status)
            {
                case LoadStatus.Ok:
                    break;
                case LoadStatus.RestoredFromBackup:
                    // 알림은 AppContext 가 게임 화면에 토스트로 예약한다(여기서는 화면이 이미 전환됨).
                    Debug.Log(UiText.LoadRestored);
                    break;
                case LoadStatus.Tampered:
                    _message.text = UiText.LoadTampered;
                    break;
                case LoadStatus.Corrupt:
                    _message.text = UiText.LoadCorrupt;
                    break;
                default:
                    _message.text = UiText.LoadNotFound;
                    break;
            }
        }

        /// <summary>경영 로직만으로 8챕터를 자동 완주해 곡선을 확인한다(개발용). 결과는 Python 시뮬레이터 리포트와 비교한다.</summary>
        private void OnAutoPlay()
        {
            StringBuilder sb = new StringBuilder();
            Difficulty[] all = { Difficulty.Story, Difficulty.Normal, Difficulty.Hard };
            for (int i = 0; i < all.Length; i++)
            {
                BalanceTable b = BalanceTable.Load(Ctx.TextSource, all[i]);
                AutoPlayer.Summary s = AutoPlayer.Run(b, 30, 777);
                sb.Append(all[i]).Append(": ").Append(Loc.F("dev_autoplay_row", "{0}일 · Lv {1} · 일 매출 ", s.Days, s.FinalLevel.ToString("F1")));
                for (int c = 0; c < s.ChapterAverageGross.Length; c++)
                {
                    if (c > 0) sb.Append(" / ");
                    sb.Append(s.ChapterAverageGross[c].ToString("F0"));
                }
                sb.Append('\n');
            }
            _message.text = sb.ToString();
            Debug.Log("[AutoPlay]\n" + sb);
        }

        private static void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
