using MorpheHouse.GameFlow;
using UnityEngine;
using UnityEngine.UI;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 화면 표시 설정 적용: UI 크기(CanvasScaler 기준 해상도를 배율로 나눔)와 데스크톱 전체 화면.
    /// 선택한 배율이 지금 화면에서 레이아웃에 들어가지 않으면(LayoutRules 판정) 들어가는 가장 가까운 배율로 낮춰 적용한다.
    /// 화면 크기·안전 영역이 바뀌면(회전·창 크기 변경·폴더블) Tick 에서 다시 계산한다. 실행 결과는 Unity 에서 확인하지 못했다.
    /// </summary>
    public sealed class DisplayService
    {
        private readonly CanvasScaler _scaler;
        private readonly SettingsService _settings;
        private int _w;
        private int _h;
        private Rect _safe;
        private float _effective = 1f;
        private float _requested = -1f;
        private bool _lastFullscreen;

        public DisplayService(CanvasScaler scaler, SettingsService settings)
        {
            _scaler = scaler;
            _settings = settings;
            _lastFullscreen = Screen.fullScreen;
            _settings.Changed += Apply;
            Apply();
        }

        /// <summary>실제로 적용 중인 배율(요청과 다를 수 있다).</summary>
        public float EffectiveScale { get { return _effective; } }

        /// <summary>현재 화면 크기·안전 영역·밀도를 LayoutRules 용 명세로.</summary>
        public static ReferenceScreen CurrentScreen()
        {
            Rect safe = Screen.safeArea;
            int w = Mathf.Max(1, Screen.width);
            int h = Mathf.Max(1, Screen.height);
            int l = Mathf.Max(0, Mathf.RoundToInt(safe.xMin));
            int r = Mathf.Max(0, Mathf.RoundToInt(w - safe.xMax));
            int b = Mathf.Max(0, Mathf.RoundToInt(safe.yMin));
            int t = Mathf.Max(0, Mathf.RoundToInt(h - safe.yMax));
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            return new ReferenceScreen("current", w, h, l, r, t, b, dpi);
        }

        /// <summary>이 배율이 지금 화면에서 쓸 수 있는가(설정 화면이 버튼을 비활성화할 때 사용).</summary>
        public static bool IsScaleAvailable(float scale)
        {
            return LayoutRules.AllowedUiScales(CurrentScreen()).Contains(scale);
        }

        public void Tick()
        {
            if (Screen.width != _w || Screen.height != _h || Screen.safeArea != _safe) Apply();
        }

        public void Apply()
        {
            bool sameScreen = Screen.width == _w && Screen.height == _h && Screen.safeArea == _safe;
            if (sameScreen && _requested == _settings.UiScale)
            {
                ApplyFullscreen(); // 볼륨 슬라이더 등 무관한 설정 변경에서는 배치 계산을 다시 하지 않는다.
                return;
            }
            _requested = _settings.UiScale;
            _w = Screen.width;
            _h = Screen.height;
            _safe = Screen.safeArea;
            _effective = LayoutRules.EffectiveUiScale(_settings.UiScale, CurrentScreen());
            if (_scaler != null)
                _scaler.referenceResolution = new Vector2(LayoutRules.RefWidth / _effective, LayoutRules.RefHeight / _effective);
            UiMetrics.Update(CurrentScreen(), _effective);
            ApplyFullscreen();
        }

        private void ApplyFullscreen()
        {
            if (Application.isMobilePlatform) return;
            bool want = _settings.Fullscreen;
            if (want == _lastFullscreen) return;
            _lastFullscreen = want;
            if (want)
            {
                Resolution r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
            }
            else
            {
                Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            }
        }
    }
}
