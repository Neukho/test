using System;
using System.Collections.Generic;
using System.Globalization;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.Romance;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 대사 커맨드(docs/07 §3 카탈로그)를 게임 로직·VN 연출로 보내는 순수 디스패처.
    /// aff / trust / flag / route_lock / end_event → RomanceService·GameState, show_standing / hide_standing / show_cg / hide_cg / bg → IVnCommandSink, route_choice → 요청 플래그(UI 가 루트 선택 화면을 연다).
    /// bgm / bgm_stop / se → IAudioCommandSink(5단계, 선택 커맨드: 소리가 없거나 sink 가 없으면 조용히 무시, 알 수 없는 id 는 경고).
    /// 잘못된 인자(없는 인물·숫자 아님 등)는 예외 대신 Warnings 에 기록하고 무시한다(스크립트 오류로 게임이 멈추지 않게).
    /// </summary>
    public sealed class DialogueCommandDispatcher
    {
        public static readonly string[] CommandNames =
        {
            "aff", "trust", "flag", "route_lock", "end_event",
            "show_standing", "hide_standing", "show_cg", "hide_cg", "bg", "route_choice",
            "bgm", "bgm_stop", "se"
        };

        private readonly GameState _state;
        private readonly RomanceService _romance;
        private readonly IVnCommandSink _sink;
        private readonly List<string> _warnings = new List<string>();

        /// <summary>현재 실행 중인 노드 이름(AdjustAffection 의 source 표시용).</summary>
        public string CurrentSource = string.Empty;

        /// <summary>
        /// 대사가 &lt;&lt;route_choice&gt;&gt; 를 실행했는가(7장 인트로 끝). 호스트(UI)가 장면이 끝난 뒤 <see cref="ConsumeRouteChoice"/> 로 읽어
        /// 루트 선택 화면을 연다. 디스패처는 선택 자체를 하지 않는다.
        /// </summary>
        public bool RouteChoiceRequested { get; private set; }

        /// <summary>route_choice 요청이 있었으면 true 를 돌려주고 요청을 지운다.</summary>
        public bool ConsumeRouteChoice()
        {
            bool r = RouteChoiceRequested;
            RouteChoiceRequested = false;
            return r;
        }

        public DialogueCommandDispatcher(GameState state, RomanceService romance, IVnCommandSink sink)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (romance == null) throw new ArgumentNullException("romance");
            _state = state;
            _romance = romance;
            _sink = sink ?? new NullVnSink();
        }

        public List<string> Warnings { get { return _warnings; } }

        /// <summary>
        /// 오디오 커맨드를 받는 쪽. null(기본)이면 bgm/bgm_stop/se 는 아무 일도 하지 않고 경고도 남기지 않는다(소리 없는 실행).
        /// 의상을 지정하지 않은 show_standing 은 <see cref="IVnOutfitSink"/> 를 구현한 sink 가 있으면 그쪽(정책으로 의상 선택)으로 간다.
        /// </summary>
        public IAudioCommandSink AudioSink;

        /// <summary>호스트에 모든 커맨드를 등록한다(등록된 커맨드는 러너에 노출되지 않고 즉시 실행).</summary>
        public void Register(SimpleDialogueHost host)
        {
            for (int i = 0; i < CommandNames.Length; i++)
            {
                string name = CommandNames[i];
                host.RegisterCommand(name, delegate (string[] args) { Handle(name, args); });
            }
        }

        public static bool IsKnown(string name)
        {
            return Array.IndexOf(CommandNames, name) >= 0;
        }

        /// <summary>커맨드를 처리한다. 알려진 이름이면 true(인자 오류여도 true), 모르는 이름이면 false.</summary>
        public bool Handle(string name, string[] args)
        {
            if (args == null) args = new string[0];
            switch (name)
            {
                case "aff": return Adjust(args, true);
                case "trust": return Adjust(args, false);
                case "flag": return SetFlag(args);
                case "route_lock": return LockRoute(args);
                case "end_event": return EndEvent(args);
                case "show_standing": return ShowStanding(args);
                case "hide_standing": return HideStanding(args);
                case "show_cg": return ShowCg(args);
                case "hide_cg": _sink.HideCg(); return true;
                case "bg": return Background(args);
                case "route_choice": RouteChoiceRequested = true; return true;
                case "bgm": return PlayBgm(args);
                case "bgm_stop": if (AudioSink != null) AudioSink.StopBgm(); return true;
                case "se": return PlaySe(args);
                default: return false;
            }
        }

        private bool Adjust(string[] args, bool affection)
        {
            string cmd = affection ? "aff" : "trust";
            if (args.Length < 2) return Warn(cmd + ": 인자 2개(인물 수치)가 필요합니다.");
            string id = Id(args[0]);
            float n;
            if (!TryNumber(args[1], out n)) return Warn(cmd + ": 숫자가 아닙니다 '" + args[1] + "'");
            if (!_state.Chars.ContainsKey(id)) return Warn(cmd + ": 알 수 없는 인물 '" + args[0] + "'");
            string source = "dialogue:" + CurrentSource;
            if (affection) _romance.AdjustAffection(id, n, 0f, source);
            else _romance.AdjustAffection(id, 0f, n, source);
            return true;
        }

        private bool SetFlag(string[] args)
        {
            if (args.Length < 1 || args[0].Length == 0) return Warn("flag: 플래그 이름이 필요합니다.");
            string key = args[0].StartsWith("$") ? args[0].Substring(1) : args[0];
            int value = 1;
            if (args.Length >= 2)
            {
                string v = args[1].ToLowerInvariant();
                float f;
                if (v == "true") value = 1;
                else if (v == "false") value = 0;
                else if (TryNumber(v, out f)) value = (int)Math.Round(f);
                else return Warn("flag: 값을 해석할 수 없습니다 '" + args[1] + "'");
            }
            if (key.Length > MaxFlagKeyLength) return Warn("flag: 이름이 너무 깁니다(최대 " + MaxFlagKeyLength + "자) — 무시");
            if (!_state.Flags.ContainsKey(key) && _state.Flags.Count >= MaxFlagCount) return Warn("flag: 플래그 개수 상한(" + MaxFlagCount + ")에 도달해 '" + key + "' 를 무시");
            _romance.SetFlag(key, value);
            return true;
        }

        private bool LockRoute(string[] args)
        {
            if (args.Length < 1) return Warn("route_lock: 인물 id 가 필요합니다.");
            string reason;
            if (args.Length == 1)
            {
                if (!_romance.TryLockRoute(Id(args[0]), out reason)) Warn("route_lock: 거절됨(" + reason + ") " + args[0]);
                return true;
            }
            // 인자가 2~3개면 공동(다자) 루트 확정: <<route_lock howl johan>>
            List<string> ids = new List<string>();
            for (int i = 0; i < args.Length; i++) ids.Add(Id(args[i]));
            if (!_romance.TryLockGroup(ids, out reason)) Warn("route_lock: 거절됨(" + reason + ") " + string.Join(" ", args));
            return true;
        }

        /// <summary>
        /// 이벤트 완료. 밤 메뉴가 이미 CompleteEvent 로 완료 처리한 뒤 노드를 재생하므로(EventsDone &gt;= step) 그 경우는 조용히 무시한다.
        /// 메뉴 없이 노드만 실행한 경우에만 여기서 실제로 완료 처리한다.
        /// </summary>
        private bool EndEvent(string[] args)
        {
            if (args.Length < 2) return Warn("end_event: 인물과 단계가 필요합니다.");
            string id = Id(args[0]);
            int step;
            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out step)) return Warn("end_event: 단계가 숫자가 아닙니다 '" + args[1] + "'");
            CharState cs;
            if (!_state.Chars.TryGetValue(id, out cs)) return Warn("end_event: 알 수 없는 인물 '" + args[0] + "'");
            if (cs.EventsDone >= step) return true;
            ActionResult r = _romance.CompleteEventResult(id, step);
            if (!r.Success) Warn("end_event: 거절됨(" + r.Reason + ") " + id + " " + step);
            return true;
        }

        private bool ShowStanding(string[] args)
        {
            if (args.Length < 1) return Warn("show_standing: 인물 id 가 필요합니다.");
            string id = Id(args[0]);
            if (Array.IndexOf(CharIds.All, id) < 0) return Warn("show_standing: 스탠딩이 없는 인물 '" + args[0] + "'");
            string expr = ArtNames.DefaultExpression;
            if (args.Length >= 2)
            {
                string e = ArtNames.Expression(args[1]);
                if (e == null) Warn("show_standing: 알 수 없는 표정 '" + args[1] + "' → Neutral");
                else expr = e;
            }
            string outfit = ArtNames.DefaultOutfit;
            bool explicitOutfit = false;
            if (args.Length >= 3)
            {
                string o = ArtNames.Outfit(args[2]);
                if (o == null) Warn("show_standing: 알 수 없는 의상 '" + args[2] + "' → " + (_sink is IVnOutfitSink ? "상황에 맞는 의상" : "Casual"));
                else { outfit = o; explicitOutfit = true; }
            }
            // 명시한 의상은 항상 우선. 명시가 없으면(또는 알 수 없는 값이면) 의상 정책을 아는 sink 에게 맡긴다.
            IVnOutfitSink auto = _sink as IVnOutfitSink;
            if (!explicitOutfit && auto != null) auto.ShowStandingAuto(id, expr);
            else _sink.ShowStanding(id, expr, outfit);
            return true;
        }

        private bool HideStanding(string[] args)
        {
            if (args.Length < 1) return Warn("hide_standing: 인물 id 가 필요합니다.");
            _sink.HideStanding(Id(args[0]));
            return true;
        }

        private bool ShowCg(string[] args)
        {
            if (args.Length < 1) return Warn("show_cg: CG id 가 필요합니다.");
            string who;
            int number;
            if (ArtNames.TryParseCgId(args[0], out who, out number))
            {
                _sink.ShowCg(who, number);
                return true;
            }
            string key;
            if (ArtNames.TryParseCommonCgKey(args[0], out key))
            {
                _sink.ShowCommonCg(key);
                return true;
            }
            return Warn("show_cg: CG id 를 해석할 수 없습니다 '" + args[0] + "' (예: HOWL_E1, HAREM_E1, COMMON_CE03)");
        }

        private bool PlayBgm(string[] args)
        {
            if (args.Length < 1 || args[0].Trim().Length == 0) return Warn("bgm: BGM id 가 필요합니다.");
            if (AudioSink == null) return true;
            string id = Id(args[0]);
            if (!AudioSink.HasAudio(id, AudioKind.Bgm)) return Warn("bgm: 알 수 없는 BGM id '" + args[0] + "' (무시)");
            AudioSink.PlayBgm(id);
            return true;
        }

        private bool PlaySe(string[] args)
        {
            if (args.Length < 1 || args[0].Trim().Length == 0) return Warn("se: 효과음 id 가 필요합니다.");
            if (AudioSink == null) return true;
            string id = Id(args[0]);
            if (!AudioSink.HasAudio(id, AudioKind.Se)) return Warn("se: 알 수 없는 효과음 id '" + args[0] + "' (무시)");
            AudioSink.PlaySe(id);
            return true;
        }

        private bool Background(string[] args)
        {
            if (args.Length < 1) return Warn("bg: 장소가 필요합니다.");
            string place = ArtNames.Place(args[0]);
            if (place == null)
            {
                Warn("bg: 알 수 없는 장소 '" + args[0] + "' → " + ArtNames.DefaultPlace);
                place = ArtNames.DefaultPlace;
            }
            string time = ArtNames.DefaultTime;
            if (args.Length >= 2)
            {
                string t = ArtNames.Time(args[1]);
                if (t == null) Warn("bg: 알 수 없는 시간대 '" + args[1] + "' → Day");
                else time = t;
            }
            _sink.SetBackground(place, time);
            return true;
        }

        private static string Id(string text)
        {
            return (text ?? string.Empty).Trim().ToLowerInvariant();
        }

        /// <summary>커맨드 숫자 인자 절댓값 상한(= 저장 검증의 플래그 값 상한).</summary>
        public const float MaxNumericArg = 1000000f;
        /// <summary>플래그 키 길이·개수 상한(저장 검증: 64자 / 1024개, 여유를 두고 1000).</summary>
        public const int MaxFlagKeyLength = 64;
        public const int MaxFlagCount = 1000;

        private static bool TryNumber(string text, out float value)
        {
            // NaN/Infinity/거대한 값은 상태를 망가뜨리므로 숫자로 인정하지 않는다(저장 검증 범위와 맞춤).
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= MaxNumericArg;
        }

        private bool Warn(string message)
        {
            _warnings.Add("[" + CurrentSource + "] " + message);
            return true;
        }
    }
}
