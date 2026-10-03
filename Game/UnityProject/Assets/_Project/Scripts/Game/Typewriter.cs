using System;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 타자기 효과의 진행 계산(초당 글자 수). 화면은 VisibleCount 만큼만 보여 주면 된다.
    /// charsPerSecond 가 InstantThreshold 이상이거나 0 이하이면 즉시 전부 표시한다.
    /// </summary>
    public sealed class Typewriter
    {
        /// <summary>이 값 이상이면 "즉시 표시"로 취급한다(설정 슬라이더의 최댓값과 같다).</summary>
        public const float InstantThreshold = 130f;

        private string _text = string.Empty;
        private float _cps;
        private float _progress;

        public string Text { get { return _text; } }

        public int VisibleCount { get; private set; }

        public bool IsDone { get { return VisibleCount >= _text.Length; } }

        public void Reset(string text, float charsPerSecond)
        {
            _text = text ?? string.Empty;
            _cps = charsPerSecond;
            _progress = 0f;
            VisibleCount = (_cps <= 0f || _cps >= InstantThreshold) ? _text.Length : 0;
        }

        public void Tick(float deltaSeconds)
        {
            if (IsDone || deltaSeconds <= 0f) return;
            _progress += deltaSeconds * _cps;
            int n = (int)Math.Floor(_progress);
            if (n > _text.Length) n = _text.Length;
            if (n > VisibleCount) VisibleCount = n;
        }

        /// <summary>남은 글자를 즉시 모두 표시한다.</summary>
        public void Complete()
        {
            VisibleCount = _text.Length;
            _progress = _text.Length;
        }
    }
}
