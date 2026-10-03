using System;
using System.Collections.Generic;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.Romance;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 한 판(GameState) 동안 쓰는 대화 세션: 러너 + 변수 저장소 + 호스트(함수·커맨드)를 묶는다. Unity 에 의존하지 않는다.
    /// - 노드가 없거나 실행 중 예외가 나도 게임이 멈추지 않는다(대체 대사 / 조용한 종료, Errors 에 기록).
    /// - 방문 기록은 러너가 갖고, ExportVisited / ImportVisited 로 저장 데이터와 오간다.
    /// </summary>
    public sealed class DialogueSession
    {
        private readonly DialogueProject _project;
        private readonly Queue<DialogueStep> _queue = new Queue<DialogueStep>();
        private readonly List<string> _errors = new List<string>();
        private bool _usingQueue;

        public DialogueRunner Runner { get; private set; }
        public OverlayVariableStore Vars { get; private set; }
        public DialogueCommandDispatcher Commands { get; private set; }
        public PronounResolver Pronouns { get; private set; }

        public DialogueSession(DialogueProject project, GameState state, RomanceService romance, AddressBook book, string locale, IVnCommandSink sink, IAudioCommandSink audio = null)
        {
            if (project == null) throw new ArgumentNullException("project");
            if (state == null) throw new ArgumentNullException("state");
            if (romance == null) throw new ArgumentNullException("romance");
            _project = project;

            Vars = new OverlayVariableStore(new GameFlagsVariableStore(state));
            Commands = new DialogueCommandDispatcher(state, romance, sink);
            Commands.AudioSink = audio;
            Pronouns = new PronounResolver(state, locale, book);

            SimpleDialogueHost host = new SimpleDialogueHost();
            host.AddProvider(Pronouns);
            // call_pref_name 플래그(Ch1_Intro 가 설정): 주인공이 호칭 없이 이름만 불러 달라고 한 경우 calls_player 는 이름만 돌려준다.
            host.RegisterFunction("calls_player", delegate (DialogueValue[] a)
            {
                int pref;
                if (state.Flags.TryGetValue("call_pref_name", out pref) && pref != 0)
                    return DialogueValue.FromString(Pronouns.PlayerName);
                DialogueValue r;
                Pronouns.TryCall("calls_player", a, out r);
                return r;
            });
            host.RegisterFunction("affinity", delegate (DialogueValue[] a)
            {
                return DialogueValue.FromNumber(a.Length > 0 ? state.AffinityOf(a[0].AsString) : 0.0);
            });
            host.RegisterFunction("trust", delegate (DialogueValue[] a)
            {
                CharState c;
                return DialogueValue.FromNumber(a.Length > 0 && state.Chars.TryGetValue(a[0].AsString, out c) ? c.Trust : 0f);
            });
            host.RegisterFunction("flag", delegate (DialogueValue[] a)
            {
                int v;
                return DialogueValue.FromBool(a.Length > 0 && state.Flags.TryGetValue(a[0].AsString, out v) && v != 0);
            });
            host.RegisterFunction("locked_route", delegate (DialogueValue[] a)
            {
                return DialogueValue.FromString(state.LockedRoute ?? string.Empty);
            });
            host.RegisterFunction("route_locked", delegate (DialogueValue[] a)
            {
                // 공동 루트면 구성원 누구를 물어도 true. 인자가 없으면 단독·공동 무관하게 확정 여부.
                return DialogueValue.FromBool(a.Length > 0 ? state.IsRouteMember(a[0].AsString) : state.HasRouteLock);
            });
            host.RegisterFunction("group_size", delegate (DialogueValue[] a)
            {
                return DialogueValue.FromNumber(state.LockedCount);
            });
            Commands.Register(host);

            Runner = new DialogueRunner(project, Vars, host);
        }

        /// <summary>실행 중 쌓인 오류·경고(커맨드 인자 오류 포함). 디버그 로그용.</summary>
        public List<string> Errors { get { return _errors; } }

        public bool HasNode(string title)
        {
            return _project.Contains(title);
        }

        public bool IsRunning
        {
            get { return _usingQueue ? _queue.Count > 0 : Runner.IsRunning; }
        }

        /// <summary>true 면 실제 노드가 아니라 대체 대사(또는 빈 종료)를 재생 중.</summary>
        public bool UsingFallback { get { return _usingQueue; } }

        /// <summary>임시 변수($gift_name 등)를 넣는다. Start 직전에 호출한다.</summary>
        public void SetTemp(string name, DialogueValue value)
        {
            Vars.SetTemp(name, value);
        }

        /// <summary>
        /// 노드를 시작한다. 노드가 없으면 fallbackText 한 줄(화자 fallbackSpeaker, null 이면 지문)을 재생하고 끝난다.
        /// fallbackText 가 비어 있으면 곧바로 End.
        /// </summary>
        public void Start(string node, string fallbackSpeaker, string fallbackText)
        {
            _queue.Clear();
            _usingQueue = false;
            Commands.CurrentSource = node ?? string.Empty;
            if (_project.Contains(node))
            {
                try
                {
                    Runner.Start(node);
                    return;
                }
                catch (Exception ex)
                {
                    _errors.Add("시작 실패 " + node + ": " + ex.Message);
                }
            }
            else
            {
                _errors.Add("노드 없음(대체 대사 사용): " + node);
            }
            _usingQueue = true;
            if (!string.IsNullOrEmpty(fallbackText))
            {
                DialogueStep line = new DialogueStep();
                line.Kind = StepKind.Line;
                line.Speaker = string.IsNullOrEmpty(fallbackSpeaker) ? null : fallbackSpeaker;
                line.Text = fallbackText;
                _queue.Enqueue(line);
            }
        }

        public DialogueStep Next()
        {
            if (_usingQueue)
                return _queue.Count > 0 ? _queue.Dequeue() : DialogueStep.MakeEnd();
            try
            {
                return Runner.Next();
            }
            catch (Exception ex)
            {
                _errors.Add("실행 오류 " + Runner.CurrentNode + ": " + ex.Message);
                _usingQueue = true;
                _queue.Clear();
                return DialogueStep.MakeEnd();
            }
        }

        public void Choose(int index)
        {
            if (_usingQueue) return;
            try
            {
                Runner.Choose(index);
            }
            catch (Exception ex)
            {
                _errors.Add("선택 오류 " + Runner.CurrentNode + ": " + ex.Message);
                _usingQueue = true;
                _queue.Clear();
            }
        }

        /// <summary>대화가 끝났을 때 호출: 임시 변수를 비우고 커맨드 경고를 오류 목록에 옮긴다.</summary>
        public void Finish()
        {
            Vars.ClearTemps();
            for (int i = 0; i < Commands.Warnings.Count; i++) _errors.Add(Commands.Warnings[i]);
            Commands.Warnings.Clear();
            for (int i = 0; i < Runner.Warnings.Count; i++) _errors.Add(Runner.Warnings[i]);
            Runner.Warnings.Clear();
        }

        public List<string> ExportVisited()
        {
            return Runner.ExportVisited();
        }

        public void ImportVisited(IEnumerable<string> nodes)
        {
            if (nodes != null) Runner.ImportVisited(nodes);
        }

        /// <summary>대사가 route_choice 커맨드를 실행했는지 읽고 지운다(장면이 끝난 뒤 UI 가 호출).</summary>
        public bool ConsumeRouteChoice()
        {
            return Commands.ConsumeRouteChoice();
        }

        public bool HasVisited(string node)
        {
            return Runner.HasVisited(node);
        }
    }
}
