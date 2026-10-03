using System.IO;
using MorpheHouse.Core;
using MorpheHouse.GameFlow;
using MorpheHouse.Save;
using MorpheHouse.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MorpheHouse.Boot
{
    /// <summary>
    /// 앱 진입점. 서비스를 조립하고 Canvas/EventSystem 을 만든 뒤 타이틀을 연다.
    /// Boot 씬의 빈 GameObject 에 붙이면 된다(MorpheHouse/Setup/Create Boot Scene 이 자동으로 만든다).
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        private AppContext _ctx;

        private void Awake()
        {
            Application.targetFrameRate = Application.isMobilePlatform ? 30 : 60;
            EventBus.OnHandlerException = ex => Debug.LogException(ex);
            if (Application.isMobilePlatform) ConfigureMobile();

            EnsureEventSystem();
            CanvasScaler scaler;
            Canvas canvas = CreateCanvas(out scaler);

            // 전체 화면용 레이어를 세이프 에어리어보다 먼저 만든다(형제 순서가 곧 그리기 순서: 뒤에 오는 것이 위).
            RectTransform fullRoot = UiKit.NewRect("FullLayer", canvas.transform);
            UiKit.Stretch(fullRoot);
            RectTransform safeRoot = UiKit.NewRect("SafeArea", canvas.transform);
            UiKit.Stretch(safeRoot);
            safeRoot.gameObject.AddComponent<SafeAreaFitter>();

            // 일시정지 메뉴용 오버레이: 모든 화면 위(형제 순서상 마지막). 전체 화면 어둡게 + 세이프 에어리어 안의 패널.
            RectTransform overlayFull = UiKit.NewRect("OverlayFull", canvas.transform);
            UiKit.Stretch(overlayFull);
            RectTransform overlaySafe = UiKit.NewRect("OverlaySafe", canvas.transform);
            UiKit.Stretch(overlaySafe);
            overlaySafe.gameObject.AddComponent<SafeAreaFitter>();

            SaveService saves = new SaveService(
                new JsonUtilitySerializer(),
                new FileSaveStorage(Path.Combine(Application.persistentDataPath, "saves")));

            SettingsService settings = new SettingsService(
                new FileSettingsStorage(Path.Combine(Application.persistentDataPath, "settings.json")));
            settings.Load();

            // 문자열 표(ui_ko/ui_en)를 화면 조립보다 먼저 읽는다. 파일이 없어도 UiText 의 한국어 기본값으로 동작한다.
            UiStrings.Current = LoadStrings(settings.Locale);

            // 데모 설정(Resources/Config/build_config.csv: demo,0|1). 없으면 데모 꺼짐 = 정식 빌드.
            TextAsset demoCsv = Resources.Load<TextAsset>("Config/build_config");
            DemoConfig.Current = DemoConfig.Parse(demoCsv != null ? demoCsv.text : null);
            for (int i = 0; i < DemoConfig.Current.Warnings.Count; i++) Debug.LogWarning("[DemoConfig] " + DemoConfig.Current.Warnings[i]);

            ProfileService profile = new ProfileService(
                new FileProfileStorage(Path.Combine(Application.persistentDataPath, "profile.json")));
            profile.Load();

            _ctx = new AppContext(this, new ResourcesTextSource("Balance"), saves, settings, profile);
            AudioService audio = new GameObject("Audio").AddComponent<AudioService>();
            audio.transform.SetParent(transform, false);
            audio.Init(settings);
            _ctx.Audio = audio;
            _ctx.Display = new DisplayService(scaler, settings);

            _ctx.Title = new TitleScreen(_ctx);
            _ctx.Game = new GameScreen(_ctx);
            _ctx.Create = new CharacterCreateScreen(_ctx);
            _ctx.Vn = new VnScreen(_ctx);
            _ctx.RouteSelect = new RouteSelectScreen(_ctx);
            _ctx.End = new EndScreen(_ctx);
            _ctx.DemoEnd = new DemoEndScreen(_ctx);
            _ctx.Title.Build(fullRoot, safeRoot);
            _ctx.Game.Build(fullRoot, safeRoot);
            _ctx.Create.Build(fullRoot, safeRoot);
            _ctx.Vn.Build(fullRoot, safeRoot);
            _ctx.RouteSelect.Build(fullRoot, safeRoot);
            _ctx.End.Build(fullRoot, safeRoot);
            _ctx.DemoEnd.Build(fullRoot, safeRoot);
            _ctx.Nav.Register(_ctx.Title);
            _ctx.Nav.Register(_ctx.Game);
            _ctx.Nav.Register(_ctx.Create);
            _ctx.Nav.Register(_ctx.Vn);
            _ctx.Nav.Register(_ctx.RouteSelect);
            _ctx.Nav.Register(_ctx.End);
            _ctx.Nav.Register(_ctx.DemoEnd);
            _ctx.Pause = new PauseMenu(_ctx, overlayFull, overlaySafe);
            _ctx.Nav.Show(_ctx.Title);
        }

        // 가로 고정(레이아웃이 가로 전용), 화면 꺼짐 방지(자동 진행·스킵 중).
        private static void ConfigureMobile()
        {
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        private static UiStrings LoadStrings(string locale)
        {
            UiStrings strings = new UiStrings();
            string[] locales = { "ko", "en" };
            for (int i = 0; i < locales.Length; i++)
            {
                TextAsset asset = Resources.Load<TextAsset>("Localization/ui_" + locales[i]);
                strings.LoadLocale(locales[i], asset != null ? asset.text : null);
            }
            strings.SetLocale(locale);
            for (int i = 0; i < strings.Errors.Count; i++) Debug.LogWarning("[Localization] " + strings.Errors[i]);
            return strings;
        }

        // 앱이 백그라운드로 가거나 종료될 때 설정을 저장한다(설정 화면을 닫지 않고 나가도 남도록).
        private void OnApplicationPause(bool paused)
        {
            if (paused && _ctx != null) _ctx.Settings.Save();
        }

        private void OnApplicationQuit()
        {
            if (_ctx != null) _ctx.Settings.Save();
        }

        private void Update()
        {
            if (_ctx == null) return;
            if (_ctx.State != null && !(_ctx.Pause != null && _ctx.Pause.IsOpen) && ((_ctx.Game != null && _ctx.Game.IsVisible) || (_ctx.Vn != null && _ctx.Vn.IsVisible) || (_ctx.RouteSelect != null && _ctx.RouteSelect.IsVisible)))
                _ctx.State.PlayTimeSec += Time.unscaledDeltaTime;
            _ctx.Tick(Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            EventBus.ClearAll();
            EventBus.OnHandlerException = null;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            GameObject go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            InputSystemUIInputModule module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private Canvas CreateCanvas(out CanvasScaler scaler)
        {
            GameObject go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }
    }
}
