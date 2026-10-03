using System;
using MorpheHouse.GameFlow;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace MorpheHouse.UI
{
    /// <summary>
    /// VN 무대 전체를 덮는 입력 면(보이지 않는 Image 에 붙는다). 새 Input System 의 UI 모듈이 보내는 포인터 이벤트를 순수 TouchGesture 상태기계에 넘기고,
    /// 결과(탭 / 길게 누르기 시작·끝 / 취소)를 Raised 로 알린다. 터치만 길게 누르기를 쓰고, 마우스·펜은 느린 클릭도 그냥 탭으로 취급한다.
    /// 버튼·선택지는 이 면보다 위에 그려지므로(레이캐스트 우선) 겹치지 않는다.
    /// </summary>
    public sealed class VnTouchSurface : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        /// <summary>제스처 결과 콜백(VnStage 가 연결).</summary>
        public Action<GestureEvent> Raised;

        private readonly TouchGesture _gesture = new TouchGesture();
        private bool _slopConfigured;

        public bool IsLongPressing { get { return _gesture.IsLongPressing; } }

        public void OnPointerDown(PointerEventData eventData)
        {
            ConfigureSlop();
            Emit(_gesture.Down(Time.unscaledTime, eventData.position.x, eventData.position.y, IsTouch(eventData)));
        }

        public void OnDrag(PointerEventData eventData)
        {
            Emit(_gesture.Move(eventData.position.x, eventData.position.y));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Emit(_gesture.Up(Time.unscaledTime));
        }

        private void Update()
        {
            Emit(_gesture.Tick(Time.unscaledTime));
        }

        private void OnDisable()
        {
            CancelGesture();
        }

        /// <summary>진행 중인 제스처를 버린다(화면 전환·일시정지). 길게 누르던 중이면 끝 이벤트를 낸다.</summary>
        public void CancelGesture()
        {
            Emit(_gesture.Cancel());
        }

        private void Emit(GestureEvent e)
        {
            if (e == GestureEvent.None) return;
            Action<GestureEvent> h = Raised;
            if (h != null) h(e);
        }

        // 이동 한계는 10dp 로 두고 실제 화면 밀도로 픽셀을 계산한다(밀도를 모르면 160dpi 로 가정).
        private void ConfigureSlop()
        {
            if (_slopConfigured) return;
            _slopConfigured = true;
            _gesture.SetSlopDp(10f, Screen.dpi);
        }

        private static bool IsTouch(PointerEventData e)
        {
            ExtendedPointerEventData ext = e as ExtendedPointerEventData;
            if (ext != null) return ext.pointerType == UIPointerType.Touch;
            return Application.isMobilePlatform;
        }
    }
}
