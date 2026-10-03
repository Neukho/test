using System;
using System.Collections.Generic;

namespace MorpheHouse.Dialogue
{
    /// <summary>
    /// MiniYarn 실행기. Yarn 호환 부분집합을 한 단계씩 실행한다.
    /// 내장 함수: visited(node) visited_count(node) min max floor ceil round abs cap(str).
    /// 그 외 함수는 IDialogueHost 가 처리한다(affinity("howl"), flag("x"), pronoun_subj() ...).
    /// </summary>
    public sealed class DialogueRunner : IDialogueRunner, IExpressionContext
    {
        private sealed class Frame
        {
            public List<Statement> Stmts;
            public int Pos;
        }

        /// <summary>대사·선택지·커맨드·끝을 돌려주지 않고 Next() 한 번에 실행할 수 있는 내부 문장(set/if/jump 등) 상한. 넘으면 DialogueRuntimeException.</summary>
        public const int MaxInternalSteps = 10000;

        private readonly DialogueProject _project;
        private readonly IVariableStore _vars;
        private readonly IDialogueHost _host;
        private readonly ITextLocalizer _localizer;
        private readonly List<Frame> _stack = new List<Frame>();
        private readonly Dictionary<string, int> _visitCounts = new Dictionary<string, int>();
        private readonly List<string> _visitOrder = new List<string>();
        private readonly Dictionary<string, TextTemplate> _localizedCache = new Dictionary<string, TextTemplate>();
        private readonly List<string> _warnings = new List<string>();

        private List<OptionItem> _pendingItems;
        private bool _running;
        private string _currentNode = string.Empty;
        private int _currentLine;

        public DialogueRunner(DialogueProject project, IVariableStore vars, IDialogueHost host)
            : this(project, vars, host, null)
        {
        }

        public DialogueRunner(DialogueProject project, IVariableStore vars, IDialogueHost host, ITextLocalizer localizer)
        {
            if (project == null) throw new ArgumentNullException("project");
            _project = project;
            _vars = vars ?? new InMemoryVariableStore();
            _host = host ?? new NullDialogueHost();
            _localizer = localizer;
        }

        public bool IsRunning { get { return _running; } }

        public string CurrentNode { get { return _currentNode; } }

        /// <summary>실행 중 발생한 비치명적 경고(선택 가능한 선택지가 없어 건너뜀, 현지화 텍스트 구문 오류 등).</summary>
        public List<string> Warnings { get { return _warnings; } }

        public void Start(string nodeTitle)
        {
            _pendingItems = null;
            EnterNode(nodeTitle);
            _running = true;
        }

        private void EnterNode(string title)
        {
            DialogueNode node = _project.GetNode(title);
            if (node == null) throw new DialogueRuntimeException(Where() + "노드 '" + title + "' 를 찾을 수 없습니다.");
            _stack.Clear();
            Frame f = new Frame();
            f.Stmts = node.Body;
            _stack.Add(f);
            _currentNode = title;
            int n;
            _visitCounts.TryGetValue(title, out n);
            if (n == 0) _visitOrder.Add(title);
            _visitCounts[title] = n + 1;
        }

        public bool HasVisited(string nodeTitle)
        {
            int n;
            return nodeTitle != null && _visitCounts.TryGetValue(nodeTitle, out n) && n > 0;
        }

        public List<string> ExportVisited()
        {
            return new List<string>(_visitOrder);
        }

        public void ImportVisited(IEnumerable<string> nodeTitles)
        {
            if (nodeTitles == null) return;
            foreach (string t in nodeTitles)
            {
                if (string.IsNullOrEmpty(t) || _visitCounts.ContainsKey(t)) continue;
                _visitCounts[t] = 1;
                _visitOrder.Add(t);
            }
        }

        public DialogueStep Next()
        {
            if (_pendingItems != null)
                throw new InvalidOperationException("선택지가 대기 중입니다. Choose(index) 를 먼저 호출하세요.");
            int budget = MaxInternalSteps;
            while (true)
            {
                if (--budget < 0)
                    throw new DialogueRuntimeException(Where() + "한 단계에서 내부 문장을 " + MaxInternalSteps + "개 넘게 실행했습니다(점프/반복 순환?).");
                if (!_running) return DialogueStep.MakeEnd();
                if (_stack.Count == 0)
                {
                    _running = false;
                    return DialogueStep.MakeEnd();
                }
                Frame f = _stack[_stack.Count - 1];
                if (f.Pos >= f.Stmts.Count)
                {
                    _stack.RemoveAt(_stack.Count - 1);
                    continue;
                }
                Statement s = f.Stmts[f.Pos++];
                _currentLine = s.LineNumber;

                LineStatement ls = s as LineStatement;
                if (ls != null) return MakeLine(ls);

                OptionsStatement os = s as OptionsStatement;
                if (os != null)
                {
                    DialogueStep choices = MakeChoices(os);
                    if (choices != null) return choices;
                    continue;
                }

                SetStatement set = s as SetStatement;
                if (set != null) { ExecSet(set); continue; }

                IfStatement ifs = s as IfStatement;
                if (ifs != null) { ExecIf(ifs); continue; }

                JumpStatement jump = s as JumpStatement;
                if (jump != null)
                {
                    EnterNode(jump.Target);
                    continue;
                }

                if (s is StopStatement)
                {
                    _stack.Clear();
                    _running = false;
                    return DialogueStep.MakeEnd();
                }

                CommandStatement cs = s as CommandStatement;
                if (cs != null)
                {
                    DialogueStep cmd = MakeCommand(cs);
                    if (cmd != null) return cmd;
                    continue;
                }
                throw new DialogueRuntimeException(Where() + "알 수 없는 문장 종류: " + s.GetType().Name);
            }
        }

        public void Choose(int index)
        {
            if (_pendingItems == null) throw new InvalidOperationException("대기 중인 선택지가 없습니다.");
            if (index < 0 || index >= _pendingItems.Count)
                throw new ArgumentOutOfRangeException("index", "선택지 인덱스 범위 밖: " + index);
            OptionItem item = _pendingItems[index];
            if (item.Condition != null && !Eval(item.Condition, item.LineNumber).IsTruthy)
                throw new InvalidOperationException("비활성 선택지는 고를 수 없습니다: " + index);
            _pendingItems = null;
            Frame f = new Frame();
            f.Stmts = item.Body;
            _stack.Add(f);
        }

        // ---------- 문장 실행 ----------

        private DialogueStep MakeLine(LineStatement ls)
        {
            DialogueStep step = new DialogueStep();
            step.Kind = StepKind.Line;
            step.Speaker = ls.Speaker;
            step.LineId = ls.LineId;
            step.Tags = new List<string>(ls.Tags);
            step.SourceLine = ls.LineNumber;
            step.Text = RenderText(ls.Text, ls.LineId, ls.LineNumber);
            return step;
        }

        private DialogueStep MakeChoices(OptionsStatement os)
        {
            DialogueStep step = new DialogueStep();
            step.Kind = StepKind.Choices;
            step.SourceLine = os.LineNumber;
            bool anyAvailable = false;
            for (int i = 0; i < os.Items.Count; i++)
            {
                OptionItem item = os.Items[i];
                DialogueOption o = new DialogueOption();
                o.Index = i;
                o.LineId = item.LineId;
                o.Tags = new List<string>(item.Tags);
                o.Available = item.Condition == null || Eval(item.Condition, item.LineNumber).IsTruthy;
                o.Text = RenderText(item.Text, item.LineId, item.LineNumber);
                if (o.Available) anyAvailable = true;
                step.Options.Add(o);
            }
            if (!anyAvailable)
            {
                _warnings.Add(Where() + "선택 가능한 선택지가 없어 선택지 묶음을 건너뜁니다.");
                return null;
            }
            _pendingItems = os.Items;
            return step;
        }

        private void ExecSet(SetStatement s)
        {
            DialogueValue v = Eval(s.Value, s.LineNumber);
            if (s.Operator != "=")
            {
                DialogueValue cur = GetVariable(s.Variable);
                try
                {
                    v = DialogueOps.Apply(s.Operator.Substring(0, 1), cur, v);
                }
                catch (DialogueRuntimeException ex)
                {
                    throw new DialogueRuntimeException(Where() + ex.Message, ex);
                }
            }
            try
            {
                _vars.Set(s.Variable, v);
            }
            catch (InvalidOperationException ex)
            {
                throw new DialogueRuntimeException(Where() + ex.Message, ex);
            }
        }

        private void ExecIf(IfStatement ifs)
        {
            for (int i = 0; i < ifs.Branches.Count; i++)
            {
                IfBranch b = ifs.Branches[i];
                if (b.Condition == null || Eval(b.Condition, b.LineNumber).IsTruthy)
                {
                    Frame f = new Frame();
                    f.Stmts = b.Body;
                    _stack.Add(f);
                    return;
                }
            }
        }

        private DialogueStep MakeCommand(CommandStatement cs)
        {
            string[] args = new string[cs.Args.Count];
            for (int i = 0; i < args.Length; i++) args[i] = Render(cs.Args[i], cs.LineNumber);
            if (_host.TryHandleCommand(cs.Name, args)) return null;
            DialogueStep step = new DialogueStep();
            step.Kind = StepKind.Command;
            step.CommandName = cs.Name;
            step.Args = args;
            step.SourceLine = cs.LineNumber;
            return step;
        }

        // ---------- 텍스트·표현식 ----------

        private string RenderText(TextTemplate source, string lineId, int line)
        {
            TextTemplate t = source;
            string localized;
            if (_localizer != null && lineId != null && _localizer.TryGetText(lineId, out localized) && localized != null)
            {
                TextTemplate lt;
                if (!_localizedCache.TryGetValue(localized, out lt))
                {
                    try
                    {
                        lt = TextTemplate.Parse(localized);
                    }
                    catch (DialogueParseException ex)
                    {
                        _warnings.Add(Where() + "현지화 텍스트(" + lineId + ") 구문 오류, 원문 사용: " + ex.RawMessage);
                        lt = null;
                    }
                    _localizedCache[localized] = lt;
                }
                if (lt != null) t = lt;
            }
            return Render(t, line);
        }

        private string Render(TextTemplate t, int line)
        {
            try
            {
                return t.Render(this);
            }
            catch (DialogueRuntimeException ex)
            {
                throw new DialogueRuntimeException(Where(line) + ex.Message, ex);
            }
        }

        private DialogueValue Eval(DialogueExpression e, int line)
        {
            try
            {
                return e.Evaluate(this);
            }
            catch (DialogueRuntimeException ex)
            {
                throw new DialogueRuntimeException(Where(line) + ex.Message, ex);
            }
        }

        private string Where()
        {
            return Where(_currentLine);
        }

        private string Where(int line)
        {
            return "[" + _currentNode + ":" + line + "] ";
        }

        public DialogueValue GetVariable(string name)
        {
            DialogueValue v;
            return _vars.TryGet(name, out v) ? v : default(DialogueValue);
        }

        public DialogueValue CallFunction(string name, DialogueValue[] args)
        {
            switch (name)
            {
                case "visited":
                    RequireArgs(name, args, 1);
                    return DialogueValue.FromBool(HasVisited(args[0].AsString));
                case "visited_count":
                    RequireArgs(name, args, 1);
                    int n;
                    _visitCounts.TryGetValue(args[0].AsString, out n);
                    return DialogueValue.FromNumber(n);
                case "min":
                    RequireArgs(name, args, 2);
                    return DialogueValue.FromNumber(Math.Min(args[0].AsNumber, args[1].AsNumber));
                case "max":
                    RequireArgs(name, args, 2);
                    return DialogueValue.FromNumber(Math.Max(args[0].AsNumber, args[1].AsNumber));
                case "floor":
                    RequireArgs(name, args, 1);
                    return DialogueValue.FromNumber(Math.Floor(args[0].AsNumber));
                case "ceil":
                    RequireArgs(name, args, 1);
                    return DialogueValue.FromNumber(Math.Ceiling(args[0].AsNumber));
                case "round":
                    RequireArgs(name, args, 1);
                    return DialogueValue.FromNumber(Math.Round(args[0].AsNumber, MidpointRounding.AwayFromZero));
                case "abs":
                    RequireArgs(name, args, 1);
                    return DialogueValue.FromNumber(Math.Abs(args[0].AsNumber));
                case "cap":
                    RequireArgs(name, args, 1);
                    string s = args[0].AsString;
                    return DialogueValue.FromString(s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1));
            }
            DialogueValue result;
            if (_host.TryCallFunction(name, args, out result)) return result;
            throw new DialogueRuntimeException("정의되지 않은 함수: " + name + "()");
        }

        private static void RequireArgs(string name, DialogueValue[] args, int count)
        {
            if (args.Length != count)
                throw new DialogueRuntimeException("함수 " + name + "() 는 인자 " + count + "개가 필요합니다(받은 수: " + args.Length + ").");
        }
    }
}
