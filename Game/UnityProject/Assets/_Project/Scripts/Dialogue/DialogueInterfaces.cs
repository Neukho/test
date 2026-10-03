using System.Collections.Generic;

namespace MorpheHouse.Dialogue
{
    /// <summary>
    /// 대화 실행기 추상화. UI 는 이 인터페이스만 의존한다(MiniYarn → 실제 Yarn Spinner 교체 대비).
    /// </summary>
    public interface IDialogueRunner
    {
        /// <summary>노드를 시작한다(진행 중이던 대화는 버린다). 방문 기록은 유지.</summary>
        void Start(string nodeTitle);

        /// <summary>다음 단계를 얻는다. Choices 를 받았다면 Choose 호출 전에는 호출할 수 없다.</summary>
        DialogueStep Next();

        /// <summary>마지막 Choices 에서 index 번째 선택지를 고른다(비활성 선택지는 예외).</summary>
        void Choose(int index);

        /// <summary>End 에 도달하기 전이면 true.</summary>
        bool IsRunning { get; }

        /// <summary>현재 노드 제목(없으면 빈 문자열).</summary>
        string CurrentNode { get; }

        /// <summary>노드를 한 번이라도 방문(진입)했는가.</summary>
        bool HasVisited(string nodeTitle);

        /// <summary>저장용 방문 노드 목록(첫 방문 순서).</summary>
        List<string> ExportVisited();

        /// <summary>불러오기용: 방문 목록 복원(기존 기록에 합친다).</summary>
        void ImportVisited(IEnumerable<string> nodeTitles);
    }

    /// <summary>스크립트 변수($name) 저장소. name 은 '$' 를 포함한다.</summary>
    public interface IVariableStore
    {
        bool TryGet(string name, out DialogueValue value);

        void Set(string name, DialogueValue value);
    }

    /// <summary>메모리 변수 저장소(테스트·미리보기용).</summary>
    public sealed class InMemoryVariableStore : IVariableStore
    {
        private readonly Dictionary<string, DialogueValue> _values = new Dictionary<string, DialogueValue>();

        public bool TryGet(string name, out DialogueValue value)
        {
            return _values.TryGetValue(name, out value);
        }

        public void Set(string name, DialogueValue value)
        {
            _values[name] = value;
        }

        public int Count { get { return _values.Count; } }
    }

    /// <summary>스크립트 함수 제공자(PronounResolver 등). 처리하지 않는 이름이면 false.</summary>
    public interface IDialogueFunctionProvider
    {
        bool TryCall(string name, DialogueValue[] args, out DialogueValue result);
    }

    /// <summary>게임 쪽 함수·커맨드 연결점.</summary>
    public interface IDialogueHost
    {
        /// <summary>스크립트 함수(affinity("howl") 등)를 호출한다. 모르는 함수면 false.</summary>
        bool TryCallFunction(string name, DialogueValue[] args, out DialogueValue result);

        /// <summary>
        /// 커맨드를 즉시 처리했으면 true(러너는 Command 단계를 노출하지 않고 계속 진행).
        /// false 면 러너가 DialogueStep(Command) 로 호출자에게 넘긴다.
        /// </summary>
        bool TryHandleCommand(string name, string[] args);
    }

    /// <summary>줄 id → 현지화 텍스트. 없으면 원문으로 폴백한다. Unity Localization 은 이 인터페이스를 구현해 연결.</summary>
    public interface ITextLocalizer
    {
        bool TryGetText(string lineId, out string text);
    }

    /// <summary>딕셔너리 기반 현지화(테스트·CSV 로드용).</summary>
    public sealed class DictionaryTextLocalizer : ITextLocalizer
    {
        private readonly Dictionary<string, string> _table = new Dictionary<string, string>();

        public void Set(string lineId, string text)
        {
            _table[lineId] = text;
        }

        public bool TryGetText(string lineId, out string text)
        {
            return _table.TryGetValue(lineId, out text);
        }
    }
}
