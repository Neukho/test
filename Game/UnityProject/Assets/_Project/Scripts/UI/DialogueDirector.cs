using System;
using System.Collections.Generic;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using MorpheHouse.Romance;
using UnityEngine;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 대화 ↔ 게임 연결(Unity 쪽): Resources/Dialogue 에서 스크립트를 읽어 프로젝트를 만들고,
    /// 판마다 DialogueSession(순수)을 만들어 VN 화면으로 재생한다. 연출 커맨드(스탠딩·CG·배경)는 이 클래스가 받아 VnStage 로 넘긴다.
    /// 커맨드 해석·게임 상태 변경은 전부 순수 클래스(DialogueCommandDispatcher, DialogueSession)가 한다.
    /// </summary>
    public sealed class DialogueDirector : IVnCommandSink, IVnOutfitSink
    {
        private const string Folder = "Dialogue/";

        private readonly AppContext _ctx;
        private DialogueProject _project = new DialogueProject();
        private AddressBook _book = new AddressBook();
        private DialogueSession _session;
        // 세션을 다시 만들 때(언어 변경) 필요한 게임 참조와, 지금 세션이 쓰는 호칭 언어.
        private GameState _boundState;
        private RomanceService _boundRomance;
        private string _boundLocale = "ko";
        // 이번 판에서 재생을 시도한 노드(방문 기록이 남지 않는 예외 상황에서도 챕터 흐름이 같은 노드를 되풀이하지 않게 한다).
        private readonly HashSet<string> _attempted = new HashSet<string>();

        /// <summary>스크립트 로드 중 생긴 오류(파일 누락·구문 오류·중복 노드).</summary>
        public readonly List<string> LoadErrors = new List<string>();

        public int NodeCount { get; private set; }

        public DialogueDirector(AppContext ctx)
        {
            _ctx = ctx;
        }

        public AddressBook Book { get { return _book; } }

        /// <summary>_index.txt 에 적힌 모든 스크립트와 address_terms.csv 를 읽는다(앱 시작 시 한 번).</summary>
        public void LoadResources()
        {
            LoadErrors.Clear();

            TextAsset csv = Resources.Load<TextAsset>(Folder + "address_terms");
            if (csv != null)
            {
                try { _book = AddressBook.FromCsv(csv.text); }
                catch (Exception ex) { LoadErrors.Add("address_terms.csv 파싱 실패: " + ex.Message); }
            }
            else
            {
                LoadErrors.Add("Resources/Dialogue/address_terms.csv 가 없습니다(호칭은 기본형으로 표시).");
            }

            TextAsset index = Resources.Load<TextAsset>(Folder + DialogueProjectLoader.IndexName);
            List<string> names = DialogueProjectLoader.ParseIndex(index != null ? index.text : null);
            if (index == null) LoadErrors.Add("Resources/Dialogue/_index.txt 가 없습니다. 메뉴 MorpheHouse > Dialogue > Sync Scripts 를 실행하세요.");

            List<KeyValuePair<string, string>> sources = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < names.Count; i++)
            {
                TextAsset asset = Resources.Load<TextAsset>(Folder + names[i]);
                if (asset == null)
                {
                    LoadErrors.Add("인덱스에 있으나 에셋이 없음: " + names[i]);
                    continue;
                }
                sources.Add(new KeyValuePair<string, string>(names[i], asset.text));
            }

            DialogueLoadResult result = DialogueProjectLoader.Build(sources);
            _project = result.Project;
            NodeCount = result.NodeCount;
            LoadErrors.AddRange(result.Errors);
            for (int i = 0; i < LoadErrors.Count; i++) Debug.LogWarning("[Dialogue] " + LoadErrors[i]);
            Debug.Log("[Dialogue] 스크립트 " + result.FileCount + "개 · 노드 " + result.NodeCount + "개 로드");
        }

        /// <summary>새 판(또는 불러온 판)에 맞춰 세션을 만든다.</summary>
        public void Bind(GameState state, RomanceService romance)
        {
            _boundState = state;
            _boundRomance = romance;
            _boundLocale = PronounResolver.NormalizeLocale(_ctx.Settings.Locale);
            _session = new DialogueSession(_project, state, romance, _book, _boundLocale, this, _ctx.Audio);
            _attempted.Clear();
        }

        /// <summary>
        /// 설정의 언어가 세션이 만들어질 때와 달라졌으면 세션을 다시 만든다(호칭 해석기의 언어는 생성 시 고정). 방문 기록은 그대로 옮긴다.
        /// 재생 중인 대사를 끊지 않도록 새 장면을 시작하기 직전(PlayCore)에만 호출한다.
        /// </summary>
        private void EnsureLocale()
        {
            if (_session == null || _boundState == null || _boundRomance == null) return;
            string want = PronounResolver.NormalizeLocale(_ctx.Settings.Locale);
            if (want == _boundLocale) return;
            List<string> visited = _session.ExportVisited();
            _boundLocale = want;
            _session = new DialogueSession(_project, _boundState, _boundRomance, _book, want, this, _ctx.Audio);
            _session.ImportVisited(visited);
        }

        public bool HasNode(string title)
        {
            return _session != null && _session.HasNode(title);
        }

        public bool HasVisited(string title)
        {
            return (_session != null && _session.HasVisited(title)) || (title != null && _attempted.Contains(title));
        }

        /// <summary>저장 직전 호출: 러너의 방문 기록을 GameState 로 내보낸다.</summary>
        public void SyncVisitedToState()
        {
            if (_session == null || _ctx.State == null) return;
            List<string> visited = _session.ExportVisited();
            _ctx.State.VisitedNodes.Clear();
            _ctx.State.VisitedNodes.AddRange(visited);
        }

        public void ImportVisited(IEnumerable<string> nodes)
        {
            if (_session != null) _session.ImportVisited(nodes);
        }

        /// <summary>
        /// 노드를 VN 화면으로 재생한다. 노드가 없으면 fallbackText 한 줄을 대신 보여 준다(게임이 막히지 않게).
        /// onFinished 는 화면이 어두워진 뒤, 게임 화면으로 돌아가기 직전에 호출된다.
        /// 장면 중 <c>route_choice</c> 커맨드가 있었다면(그리고 아직 루트 확정 전이면) 게임 화면 대신 루트 선택 화면으로 간다.
        /// </summary>
        public void Play(string node, string fallbackSpeaker, string fallbackText,
            Dictionary<string, DialogueValue> temps, Action onFinished)
        {
            PlayCore(node, fallbackSpeaker, fallbackText, temps, onFinished, true);
        }

        /// <summary>
        /// 장면 여러 개를 이어서 재생한다(장면 사이에는 게임 화면으로 나가지 않고 어둠으로 이어진다).
        /// 모두 끝나면 onAllDone 을 부르고, returnToGame 이 true 면 게임 화면으로 돌아간다(false 면 호출자가 화면을 전환한다).
        /// </summary>
        public void PlayScenes(IList<SceneItem> scenes, Action onAllDone, bool returnToGame)
        {
            PlayStep(scenes, 0, onAllDone, returnToGame);
        }

        private void PlayStep(IList<SceneItem> scenes, int index, Action onAllDone, bool returnToGame)
        {
            if (scenes == null || index >= scenes.Count)
            {
                if (onAllDone != null) onAllDone();
                if (returnToGame) _ctx.Nav.Show(_ctx.Game);
                return;
            }
            SceneItem item = scenes[index];
            PlayCore(item.Node, item.FallbackSpeaker, item.FallbackText, null,
                delegate { PlayStep(scenes, index + 1, onAllDone, returnToGame); }, false);
        }

        private void PlayCore(string node, string fallbackSpeaker, string fallbackText,
            Dictionary<string, DialogueValue> temps, Action onFinished, bool navigateAfter)
        {
            if (_session == null || _ctx.Vn == null)
            {
                if (onFinished != null) onFinished();
                return;
            }
            EnsureLocale();
            if (!string.IsNullOrEmpty(node)) _attempted.Add(node);
            _session.Vars.ClearTemps();
            if (temps != null)
            {
                foreach (KeyValuePair<string, DialogueValue> p in temps) _session.SetTemp(p.Key, p.Value);
            }
            _session.Start(node, fallbackSpeaker, fallbackText);

            string time = _ctx.State != null && _ctx.State.Phase == MorpheHouse.Core.DayPhase.Night ? "Night" : "Day";
            _ctx.Vn.Play(_session, "Cafe_Interior", time, delegate
            {
                for (int i = 0; i < _session.Errors.Count; i++) Debug.LogWarning("[Dialogue] " + _session.Errors[i]);
                _session.Errors.Clear();
                bool routeChoice = _session.ConsumeRouteChoice();
                if (onFinished != null) onFinished();
                if (!navigateAfter) return;
                if (routeChoice && _ctx.State != null && RouteFlow.AcceptDialogueRequest(_ctx.State)) _ctx.OpenRouteSelect();
                else _ctx.Nav.Show(_ctx.Game);
            });
        }

        // ---------- IVnCommandSink: VN 무대로 전달 ----------

        private VnStage Stage { get { return _ctx.Vn != null ? _ctx.Vn.Stage : null; } }

        public void ShowStanding(string charId, string expression, string outfit)
        {
            if (Stage != null) Stage.ShowStanding(charId, expression, outfit);
        }

        public void ShowStandingAuto(string charId, string expression)
        {
            if (Stage != null) Stage.ShowStandingAuto(charId, expression);
        }

        public void HideStanding(string charId)
        {
            if (Stage != null) Stage.HideStanding(charId);
        }

        public void ShowCg(string charId, int number)
        {
            _ctx.NoteCgSeen((charId ?? string.Empty).ToUpperInvariant() + "_E" + number);
            if (Stage != null) Stage.ShowCg(charId, number);
        }

        public void ShowCommonCg(string key)
        {
            _ctx.NoteCgSeen(key);
            if (Stage != null) Stage.ShowCommonCg(key);
        }

        public void HideCg()
        {
            if (Stage != null) Stage.HideCg();
        }

        public void SetBackground(string place, string time)
        {
            if (Stage != null) Stage.SetBackground(place, time);
        }
    }
}
