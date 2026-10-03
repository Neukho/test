using System;

namespace MorpheHouse.GameFlow
{
    /// <summary>터치 제스처 결과.</summary>
    public enum GestureEvent
    {
        None,
        /// <summary>짧게 눌렀다 뗐다(다음 줄로).</summary>
        Tap,
        /// <summary>오래 누르기 시작(스킵 시작).</summary>
        LongPressBegin,
        /// <summary>오래 누르던 손가락을 뗐다(스킵 끝).</summary>
        LongPressEnd,
        /// <summary>미끄러져 이동 한계를 넘었다(탭이 아니므로 아무것도 하지 않는다).</summary>
        Cancelled
    }

    /// <summary>
    /// VN 화면 터치 제스처 상태기계(순수): 탭 = 다음 줄, 길게 누르기 = 누르고 있는 동안 스킵.
    /// 한 번에 한 손가락만 추적한다(두 번째 손가락은 무시). 시간은 호출자가 넘기는 초 단위 단조 증가 값, 거리는 화면 픽셀.
    /// 마우스는 길게 누르기를 쓰지 않도록 Down 의 longPressEnabled 에 false 를 넘긴다(느린 클릭이 스킵으로 바뀌지 않게).
    /// </summary>
    public sealed class TouchGesture
    {
        /// <summary>이 시간(초) 이상 누르고 있으면 길게 누르기.</summary>
        public float LongPressSeconds = 0.5f;

        /// <summary>이 거리(픽셀) 이상 움직이면 탭·길게 누르기가 취소된다. 기본 24px(약 10dp@240dpi).</summary>
        public float SlopPixels = 24f;

        private bool _down;
        private bool _longPressLatched;
        private bool _longPressEnabled;
        private bool _cancelled;
        private float _downTime;
        private float _downX;
        private float _downY;

        public bool IsDown { get { return _down; } }

        /// <summary>길게 누르기가 시작되어 아직 손가락을 떼지 않았는가(= 스킵 중).</summary>
        public bool IsLongPressing { get { return _down && _longPressLatched; } }

        /// <summary>이 슬롭을 dp 로 지정한다(dpi 는 화면 밀도, 0 이하면 160 으로 본다).</summary>
        public void SetSlopDp(float dp, float dpi)
        {
            float d = dpi <= 0f ? 160f : dpi;
            SlopPixels = Math.Max(4f, dp * d / 160f);
        }

        public GestureEvent Down(float now, float x, float y, bool longPressEnabled)
        {
            if (_down) return GestureEvent.None;
            _down = true;
            _longPressLatched = false;
            _cancelled = false;
            _longPressEnabled = longPressEnabled;
            _downTime = now;
            _downX = x;
            _downY = y;
            return GestureEvent.None;
        }

        /// <summary>손가락 이동. 한계를 처음 넘는 순간 Cancelled(길게 누르기 중이었다면 LongPressEnd).</summary>
        public GestureEvent Move(float x, float y)
        {
            if (!_down || _cancelled) return GestureEvent.None;
            float dx = x - _downX;
            float dy = y - _downY;
            if (dx * dx + dy * dy <= SlopPixels * SlopPixels) return GestureEvent.None;
            _cancelled = true;
            if (_longPressLatched)
            {
                _longPressLatched = false;
                return GestureEvent.LongPressEnd;
            }
            return GestureEvent.Cancelled;
        }

        /// <summary>매 프레임. 누른 채로 LongPressSeconds 가 지나면 LongPressBegin 을 한 번 낸다.</summary>
        public GestureEvent Tick(float now)
        {
            if (!_down || _cancelled || _longPressLatched || !_longPressEnabled) return GestureEvent.None;
            if (now - _downTime < LongPressSeconds) return GestureEvent.None;
            _longPressLatched = true;
            return GestureEvent.LongPressBegin;
        }

        /// <summary>손가락을 뗐다. 길게 누르기 중이면 LongPressEnd, 취소되지 않았고 길게 누르기 기준 시간 전에 뗐으면 Tap.</summary>
        public GestureEvent Up(float now)
        {
            if (!_down) return GestureEvent.None;
            bool latched = _longPressLatched;
            bool cancelled = _cancelled;
            _down = false;
            _longPressLatched = false;
            if (latched) return GestureEvent.LongPressEnd;
            if (cancelled) return GestureEvent.None;
            // 길게 누르기를 쓰지 않는 포인터(마우스)는 오래 눌러도 탭. 쓰는 포인터가 Tick 을 놓치고(프레임 지연) 기준 시간을 넘겨 뗐다면
            // 탭도 스킵도 아닌 애매한 입력이므로 아무것도 하지 않는다.
            if (_longPressEnabled && now - _downTime >= LongPressSeconds) return GestureEvent.None;
            return GestureEvent.Tap;
        }

        /// <summary>포인터가 사라졌을 때(앱 일시정지·화면 전환): 상태를 비우고, 길게 누르기 중이었다면 LongPressEnd.</summary>
        public GestureEvent Cancel()
        {
            bool latched = _longPressLatched;
            _down = false;
            _longPressLatched = false;
            _cancelled = false;
            return latched ? GestureEvent.LongPressEnd : GestureEvent.None;
        }
    }
}
