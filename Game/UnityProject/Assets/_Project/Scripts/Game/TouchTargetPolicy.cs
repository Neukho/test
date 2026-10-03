using System;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 최소 터치 목표 정책(순수). 모바일에서는 버튼이 물리적으로 48 dp(≈ 7.6 mm) 이상이어야 한다.
    /// 캔버스는 기준 해상도 1920×1080 + CanvasScaler(Match 0.5)이므로, 화면 밀도·크기에서 "기준 해상도 px" 로 환산해 최소 높이를 돌려준다.
    /// 데스크톱/에디터(mobile=false)는 기존 하한(LayoutRules.MinTouchRefPx = 48 기준 px)만 쓴다.
    /// </summary>
    public static class TouchTargetPolicy
    {
        public const float MinDp = 48f;
        public const float MmPerInch = 25.4f;
        /// <summary>한 버튼이 이보다 커지면 화면을 못 쓰게 되므로 두는 상한(기준 px). 밀도가 비정상적으로 높게 보고되는 기기 대비.</summary>
        public const float MaxRefPx = 150f;
        public const float FallbackDpi = 160f;

        /// <summary>48 dp 의 실제 길이(mm). dp 는 160 dpi 기준이라 밀도와 무관하게 7.62 mm.</summary>
        public static float MinMm { get { return MinDp / 160f * MmPerInch; } }

        /// <summary>
        /// 물리 48 dp 를 만족하는 최소 버튼 높이(기준 해상도 px).
        /// 기준 px × 캔버스 배율 = 물리 px, 물리 px / dpi × 160 = dp  →  refPx = dp × dpi / 160 / 캔버스배율.
        /// </summary>
        public static float MinButtonHeight(float dpi, int pxW, int pxH, float uiScale, bool mobile)
        {
            if (!mobile) return LayoutRules.MinTouchRefPx;
            if (dpi <= 0f) dpi = FallbackDpi;
            float scale = LayoutRules.CanvasScale(pxW, pxH, uiScale);
            float need = MinDp * dpi / 160f / scale;
            return Math.Min(MaxRefPx, Math.Max(LayoutRules.MinTouchRefPx, (float)Math.Ceiling(need)));
        }

        /// <summary>검사기용: 명세 화면(ReferenceScreen)에 대한 같은 계산.</summary>
        public static float MinButtonHeight(ReferenceScreen screen, float uiScale, bool mobile)
        {
            return MinButtonHeight(screen.Dpi, screen.PxW, screen.PxH, uiScale, mobile);
        }

        /// <summary>요청한 높이를 정책 하한으로 올린다.</summary>
        public static float Apply(float requested, float minHeight)
        {
            return Math.Max(LayoutRules.ClampTouch(requested), minHeight);
        }

        /// <summary>기준 px 높이가 이 화면에서 실제로 몇 dp 인가(검증·로그용).</summary>
        public static float HeightDp(float refPx, ReferenceScreen screen, float uiScale)
        {
            return LayoutRules.RefPxToDp(refPx, screen, uiScale);
        }
    }
}
