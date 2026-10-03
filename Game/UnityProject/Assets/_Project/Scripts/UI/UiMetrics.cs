using MorpheHouse.GameFlow;
using UnityEngine;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 지금 화면에 맞는 UI 치수. 모바일에서는 버튼 최소 크기를 물리 48 dp 로 맞춘다(순수 계산은 TouchTargetPolicy).
    /// DisplayService 가 화면·UI 배율이 바뀔 때마다 <see cref="Update"/> 를 부른다. UiKit.MakeButton 이 <see cref="MinButtonHeight"/> 를 본다.
    /// 데스크톱·에디터에서는 기존 하한(48 기준 px)이라 레이아웃이 달라지지 않는다.
    /// </summary>
    public static class UiMetrics
    {
        /// <summary>테스트·에디터 확인용: true 로 두면 데스크톱에서도 모바일 정책을 쓴다.</summary>
        public static bool ForceMobile;

        /// <summary>버튼의 최소 높이·너비(기준 해상도 px).</summary>
        public static float MinButtonHeight { get; private set; }

        static UiMetrics()
        {
            MinButtonHeight = LayoutRules.MinTouchRefPx;
        }

        public static bool IsMobile { get { return Application.isMobilePlatform || ForceMobile; } }

        /// <summary>세로 버튼 목록 패널을 스크롤로 감쌀지(모바일 = 버튼이 커져서 넘칠 수 있음).</summary>
        public static bool UseScroll { get { return IsMobile; } }

        public static void Update(ReferenceScreen screen, float effectiveUiScale)
        {
            MinButtonHeight = TouchTargetPolicy.MinButtonHeight(screen, effectiveUiScale, IsMobile);
        }
    }
}
