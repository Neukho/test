using System;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 하루 흐름에서 "지금 자동 재생할 공통 장면이 있는가"를 묻는 얇은 순수 구동기(GameScreen 이 위임한다).
    /// 슬롯(아침 / 밤 시작·행동 뒤 / 밤 끝)마다 SceneGate 가 정한 다음 장면(<see cref="ChapterFlow.NextCommonScene"/>)을 돌려준다.
    /// 호스트는 장면 하나를 재생(방문 기록이 남음)하고 게임 화면으로 돌아올 때마다 다시 호출하므로, 결국 NoStep 이 나올 때까지 반복된다.
    /// 안전장치: 방문 기록이 남지 않았는데 같은 노드가 연달아 나오면(재생 실패 등) 무한 반복을 막으려고 NoStep 을 돌려주고 <see cref="StuckCount"/> 를 올린다.
    /// </summary>
    public sealed class DayFlowDriver
    {
        private readonly Func<SceneSlot, ChapterStep> _source;
        private readonly Func<string, bool> _hasVisited;
        private string _last = string.Empty;
        private SceneSlot _lastSlot;

        public DayFlowDriver(Func<SceneSlot, ChapterStep> source, Func<string, bool> hasVisited)
        {
            if (source == null) throw new ArgumentNullException("source");
            _source = source;
            _hasVisited = hasVisited ?? delegate (string n) { return false; };
        }

        /// <summary>같은 노드가 방문 기록 없이 되풀이되어 건너뛴 횟수(0 이어야 정상).</summary>
        public int StuckCount { get; private set; }

        /// <summary>마지막으로 건너뛴(막힌) 노드.</summary>
        public string LastStuck { get; private set; }

        /// <summary>새 게임·불러오기·타이틀 복귀 때 호출: 막힘 방지 상태와 카운터를 비운다(QA-10).</summary>
        public void Reset()
        {
            _last = string.Empty;
            _lastSlot = SceneSlot.Morning;
            StuckCount = 0;
            LastStuck = null;
        }

        /// <summary>해당 슬롯에서 다음에 재생할 공통 장면(없으면 ChapterStep.NoStep).</summary>
        public ChapterStep Next(SceneSlot slot)
        {
            ChapterStep step = _source(slot);
            if (step == null || step.Kind != ChapterStepKind.CommonScene || string.IsNullOrEmpty(step.Node))
            {
                _last = string.Empty;
                return ChapterStep.NoStep;
            }
            if (step.Node == _last && _lastSlot == slot && !_hasVisited(step.Node))
            {
                StuckCount++;
                LastStuck = step.Node;
                _last = string.Empty;
                return ChapterStep.NoStep;
            }
            _last = step.Node;
            _lastSlot = slot;
            return step;
        }
    }
}
