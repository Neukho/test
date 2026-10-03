using System;
using MorpheHouse.ArtPipeline;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MorpheHouse.UI
{
    /// <summary>
    /// VN 화면: DialogueSession 에서 단계를 하나씩 받아 VnStage 에 표시한다.
    /// 진행 입력: Space / Enter(Keyboard.current), 화면 클릭·터치(VnStage 의 입력 면 VnTouchSurface → 새 Input System 의 UI 모듈).
    /// 첫 입력은 타자 중이면 문장을 즉시 완성하고, 완성돼 있으면 다음 단계로 간다.
    /// 스킵: Ctrl 을 누르고 있는 동안, 스킵 버튼 토글, 또는 터치로 화면을 길게 누르고 있는 동안(손을 떼면 멈춤).
    /// </summary>
    public sealed class VnScreen : ScreenBase
    {
        private const float SkipInterval = 0.06f;

        private VnStage _stage;
        private DialogueSession _session;
        private Action _onFinished;
        private bool _active;
        private bool _waitingChoice;
        private bool _auto;
        private bool _skip;
        private bool _skipHold;
        private float _autoTimer;
        private float _skipTimer;

        public VnScreen(AppContext ctx) : base(ctx) { }

        public VnStage Stage { get { return _stage; } }

        protected override void OnBuild()
        {
            _stage = new VnStage(FullRoot, SafeRoot);
            _stage.AdvanceRequested = OnAdvance;
            _stage.ChoiceChosen = OnChoice;
            _stage.AutoToggled = delegate { _auto = !_auto; _autoTimer = 0f; _stage.SetAutoVisual(_auto); };
            _stage.SkipToggled = delegate { _skip = !_skip; _stage.SetSkipVisual(_skip); };
            _stage.MenuRequested = delegate { Ctx.OpenPause(); };
            _stage.GestureRaised = OnGesture;
            _stage.TypeTick = OnTypeTick;
        }

        /// <summary>장면이 재생 중인가(저장 가능 판정 등).</summary>
        public bool IsPlaying { get { return _active; } }

        private void OnGesture(GestureEvent e)
        {
            if (e == GestureEvent.LongPressBegin && _active && !_waitingChoice) _skipHold = true;
            else if (e == GestureEvent.LongPressEnd) _skipHold = false;
        }

        // 타자기 틱 효과음: 스킵·즉시 표시 중에는 내지 않고, 0.07초 이상 간격으로만 낸다.
        private void OnTypeTick()
        {
            if (_skip || _skipHold || Ctx.Settings.InstantText || Ctx.Audio == null) return;
            Ctx.Audio.PlaySeThrottled(AudioCues.SeTypewriter, 0.07f);
        }

        /// <summary>장면을 중간에 버린다(타이틀로 돌아가기). onFinished 는 부르지 않는다.</summary>
        public void Abort()
        {
            if (!_active) return;
            _active = false;
            _waitingChoice = false;
            _skip = false;
            _skipHold = false;
            _onFinished = null;
            if (_session != null) _session.Finish();
            _stage.Abort();
        }

        /// <summary>세션을 이어서 재생한다(세션은 이미 Start 된 상태). onFinished 는 장면이 어두워진 뒤 호출된다.</summary>
        public void Play(DialogueSession session, string place, string time, Action onFinished)
        {
            _session = session;
            _onFinished = onFinished;
            _active = true;
            _waitingChoice = false;
            _auto = false;
            _skip = false;
            _skipHold = false;
            _autoTimer = 0f;
            _skipTimer = 0f;
            _stage.SetContext(CurrentNodeName, place, time);
            Ctx.Nav.Show(this);
            _stage.BeginScene(ArtLoader.Background(place, time));
            PullNext();
        }

        // 의상 정책이 쓰는 "지금 노드": 실행 중이면 러너의 현재 노드(점프 반영), 대체 대사 중이면 시작한 노드 이름.
        private string CurrentNodeName()
        {
            if (_session == null) return null;
            return _session.Runner.IsRunning ? _session.Runner.CurrentNode : _session.Commands.CurrentSource;
        }

        public override void Tick(float deltaTime)
        {
            _stage.Tick(deltaTime);
            if (!_active) return;

            Keyboard kb = Keyboard.current;
            bool ctrlHeld = false;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                    OnAdvance();
                ctrlHeld = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            }
            if (!_active || _waitingChoice || _stage.IsFading) return;

            if (_skip || ctrlHeld || _skipHold)
            {
                _skipTimer += deltaTime;
                if (_skipTimer >= SkipInterval)
                {
                    _skipTimer = 0f;
                    OnAdvance();
                }
                return;
            }

            if (_auto && !_stage.IsTyping)
            {
                _autoTimer += deltaTime;
                if (_autoTimer >= Ctx.Settings.AutoDelay)
                {
                    _autoTimer = 0f;
                    OnAdvance();
                }
            }
        }

        private void OnAdvance()
        {
            if (!_active || _waitingChoice) return;
            if (_stage.IsTyping)
            {
                _stage.CompleteTyping();
                return;
            }
            _autoTimer = 0f;
            PullNext();
        }

        private void OnChoice(int index)
        {
            if (!_active || !_waitingChoice) return;
            _session.Choose(index);
            _waitingChoice = false;
            _stage.HideChoices();
            PullNext();
        }

        private void PullNext()
        {
            for (int guard = 0; guard < 256; guard++)
            {
                DialogueStep step = _session.Next();
                switch (step.Kind)
                {
                    case StepKind.Line:
                        ShowLine(step);
                        return;
                    case StepKind.Choices:
                        _waitingChoice = true;
                        _skip = false;
                        _skipHold = false;
                        _stage.SetSkipVisual(false);
                        _stage.ShowChoices(step.Options);
                        return;
                    case StepKind.Command:
                        Debug.LogWarning("[VN] 처리되지 않은 커맨드: " + step.CommandName);
                        break;
                    default:
                        Finish();
                        return;
                }
            }
            Finish();
        }

        private void ShowLine(DialogueStep step)
        {
            string speakerId = step.Speaker;
            string display = null;
            if (!string.IsNullOrEmpty(speakerId))
            {
                if (speakerId == "player") display = Ctx.State != null ? Ctx.State.PlayerName : Loc.T("speaker_player", "주인공");
                else display = Ctx.CharDisplayName(speakerId);
            }
            bool isChar = !string.IsNullOrEmpty(speakerId) && Array.IndexOf(CharIds.All, speakerId) >= 0;
            _stage.SetLine(isChar ? speakerId : null, display, step.Text, Ctx.Settings.TextSpeed);
        }

        private void Finish()
        {
            if (!_active) return;
            _active = false;
            _session.Finish();
            Action done = _onFinished;
            _onFinished = null;
            _stage.EndScene(done);
        }
    }
}
