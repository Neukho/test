using System;
using System.Collections.Generic;
using MorpheHouse.Data;

namespace MorpheHouse.GameFlow
{
    /// <summary>공통 장면을 호스트가 자동 재생하는 시점. 아침(영업 전) → 밤 시작(밤 메뉴 열기 전) → 밤 끝(행동을 다 쓴 뒤, 정산 전).</summary>
    public enum SceneSlot
    {
        Morning = 0,
        Night = 1,
        NightEnd = 2
    }

    /// <summary>선행 노드 한 개. Soft 면 자동 재생 창의 마지막 날(trigger_day_max)이 되면 요구를 접는다(없어도 재생).</summary>
    public sealed class SceneRef
    {
        public string Node = string.Empty;
        public bool Soft;
    }

    /// <summary>scene_rules.csv 한 줄(데이터).</summary>
    public sealed class SceneRule
    {
        public string Node = string.Empty;
        /// <summary>chapter(챕터 도입/마무리) / common(공통 장면) / event(개인 이벤트). 정보용.</summary>
        public string Kind = string.Empty;
        /// <summary>min_day_in_chapter / 자동 재생 창이 속한 챕터. 0 이면 챕터·일차 조건 없음.</summary>
        public int Chapter;
        public SceneSlot Slot = SceneSlot.Morning;
        /// <summary>true 면 호스트가 <see cref="SceneGate.NextAutoScene"/> 로 자동 재생한다.</summary>
        public bool AutoPlay;
        public int TriggerDayMin;
        public int TriggerDayMax;
        public int MinDayInChapter;
        public readonly List<string> RequiresFlags = new List<string>();
        public readonly List<string> ForbidsFlags = new List<string>();
        public readonly List<SceneRef> After = new List<SceneRef>();
        /// <summary>이 노드들 중 하나라도 이미 재생되었으면 이 장면은 너무 늦다(순서 보호).</summary>
        public readonly List<string> Before = new List<string>();
        public string Note = string.Empty;

        /// <summary>챕터 안에서 열리는 가장 이른 일차(자동 재생이면 min_day 와 trigger_day_min 중 큰 값).</summary>
        public int EffectiveMinDay
        {
            get { return AutoPlay ? Math.Max(MinDayInChapter, TriggerDayMin) : MinDayInChapter; }
        }
    }

    /// <summary>scene_rules.csv 를 읽어 둔 표(순수 데이터). 행 순서가 자동 재생 우선순위다.</summary>
    public sealed class SceneRuleTable
    {
        public const string TableName = "scene_rules";

        public readonly List<SceneRule> Rules = new List<SceneRule>();
        private readonly Dictionary<string, SceneRule> _index = new Dictionary<string, SceneRule>();

        public int Count { get { return Rules.Count; } }

        public static SceneRuleTable Load(ITextSource source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return Parse(source.Load(TableName));
        }

        /// <summary>CSV 텍스트를 읽는다. null/빈 문자열이면 빈 표(게이트 없음). 노드가 빈 줄은 건너뛰고 같은 노드는 나중 줄이 이긴다.</summary>
        public static SceneRuleTable Parse(string text)
        {
            SceneRuleTable table = new SceneRuleTable();
            if (string.IsNullOrEmpty(text)) return table;
            foreach (CsvRow r in CsvTable.Parse(text).Rows)
            {
                string node = r.Str("node").Trim();
                if (node.Length == 0) continue;
                SceneRule rule = new SceneRule();
                rule.Node = node;
                rule.Kind = r.Str("kind").Trim();
                rule.Chapter = r.Int("chapter");
                rule.Slot = ParseSlot(r.Str("slot"));
                rule.AutoPlay = ParseBool(r.Str("auto_play"));
                rule.TriggerDayMin = r.Int("trigger_day_min");
                rule.TriggerDayMax = r.Int("trigger_day_max");
                rule.MinDayInChapter = r.Int("min_day_in_chapter");
                SplitInto(r.Str("requires_flags"), rule.RequiresFlags);
                SplitInto(r.Str("forbids_flags"), rule.ForbidsFlags);
                List<string> afters = new List<string>();
                SplitInto(r.Str("after_node"), afters);
                for (int i = 0; i < afters.Count; i++)
                {
                    SceneRef a = new SceneRef();
                    string n = afters[i];
                    if (n.StartsWith("~")) { a.Soft = true; n = n.Substring(1).Trim(); }
                    if (n.Length == 0) continue;
                    a.Node = n;
                    rule.After.Add(a);
                }
                SplitInto(r.Str("before_node"), rule.Before);
                rule.Note = r.Str("note");

                SceneRule old;
                if (table._index.TryGetValue(node, out old)) table.Rules.Remove(old);
                table._index[node] = rule;
                table.Rules.Add(rule);
            }
            return table;
        }

        public SceneRule Find(string node)
        {
            SceneRule rule;
            return node != null && _index.TryGetValue(node, out rule) ? rule : null;
        }

        /// <summary>표 자체의 일관성 검사(빈 목록이면 이상 없음). daysOfChapter 가 null 이 아니면 일차 범위도 본다.</summary>
        public List<string> Validate(Func<int, int> daysOfChapter)
        {
            List<string> errors = new List<string>();
            for (int i = 0; i < Rules.Count; i++)
            {
                SceneRule r = Rules[i];
                if (r.AutoPlay)
                {
                    if (r.Chapter < 1) errors.Add(r.Node + ": auto_play needs chapter");
                    if (r.TriggerDayMin < 1) errors.Add(r.Node + ": auto_play needs trigger_day_min>=1");
                    if (r.TriggerDayMax < r.TriggerDayMin) errors.Add(r.Node + ": trigger_day_max < trigger_day_min");
                }
                if (r.MinDayInChapter > 0 && r.Chapter < 1) errors.Add(r.Node + ": min_day_in_chapter needs chapter");
                if (daysOfChapter != null && r.Chapter > 0)
                {
                    int days = daysOfChapter(r.Chapter);
                    if (days > 0 && (r.MinDayInChapter > days || r.TriggerDayMin > days || r.TriggerDayMax > days))
                        errors.Add(r.Node + ": day beyond chapter length " + days);
                }
                for (int a = 0; a < r.After.Count; a++)
                    if (r.After[a].Node == r.Node) errors.Add(r.Node + ": after_node refers to itself");
                for (int b = 0; b < r.Before.Count; b++)
                    if (r.Before[b] == r.Node) errors.Add(r.Node + ": before_node refers to itself");
            }
            return errors;
        }

        private static SceneSlot ParseSlot(string text)
        {
            string t = (text ?? string.Empty).Trim().ToLowerInvariant();
            if (t == "night") return SceneSlot.Night;
            if (t == "night_end" || t == "nightend") return SceneSlot.NightEnd;
            return SceneSlot.Morning;
        }

        private static bool ParseBool(string text)
        {
            string t = (text ?? string.Empty).Trim().ToLowerInvariant();
            return t == "1" || t == "true" || t == "yes";
        }

        private static void SplitInto(string text, List<string> into)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] parts = text.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length > 0) into.Add(p);
            }
        }
    }

    /// <summary>게이트가 장면 판단에 쓰는 세계 상태(추상화). 순수 테스트에서는 가짜로 대체한다.</summary>
    public interface ISceneContext
    {
        int Chapter { get; }
        int DayInChapter { get; }
        /// <summary>노드를 이미 재생했는가(방문 기록).</summary>
        bool IsVisited(string node);
        /// <summary>노드가 프로젝트에 존재하는가. 없는 노드는 선행 조건에서 건너뛴다(호스트는 없는 노드를 조용히 건너뛰므로).</summary>
        bool NodeExists(string node);
        bool HasFlag(string flag);
    }

    /// <summary><see cref="GameState"/> + 방문/존재 함수로 만든 기본 컨텍스트. 함수가 null 이면 방문 없음 / 모든 노드 존재로 본다.</summary>
    public sealed class StateSceneContext : ISceneContext
    {
        private readonly GameState _state;
        private readonly Func<string, bool> _visited;
        private readonly Func<string, bool> _exists;

        public StateSceneContext(GameState state, Func<string, bool> hasVisited, Func<string, bool> nodeExists)
        {
            if (state == null) throw new ArgumentNullException("state");
            _state = state;
            _visited = hasVisited;
            _exists = nodeExists;
        }

        public int Chapter { get { return _state.Chapter; } }
        public int DayInChapter { get { return _state.DayInChapter; } }

        public bool IsVisited(string node)
        {
            return _visited != null && _visited(node);
        }

        public bool NodeExists(string node)
        {
            return _exists == null || _exists(node);
        }

        public bool HasFlag(string flag)
        {
            int v;
            return flag != null && _state.Flags.TryGetValue(flag, out v) && v != 0;
        }
    }

    /// <summary>게이트가 장면을 막는 이유 코드.</summary>
    public static class SceneBlock
    {
        public const string ChapterEarly = "chapter_early";
        public const string DayEarly = "day_early";
        public const string NeedsFlag = "needs_flag";
        public const string ForbiddenFlag = "forbidden_flag";
        public const string NeedsNode = "needs_node";
        public const string TooLate = "too_late";
    }

    /// <summary>게이트 판정 결과: 허용 또는 (사유 코드 + 상세).</summary>
    public sealed class SceneCheck
    {
        public bool Allowed;
        /// <summary><see cref="SceneBlock"/> 코드(허용이면 빈 문자열).</summary>
        public string Reason = string.Empty;
        /// <summary>막은 대상(노드/플래그 이름 또는 일차 숫자).</summary>
        public string Detail = string.Empty;

        public static SceneCheck Open()
        {
            SceneCheck c = new SceneCheck();
            c.Allowed = true;
            return c;
        }

        public static SceneCheck Blocked(string reason, string detail)
        {
            SceneCheck c = new SceneCheck();
            c.Allowed = false;
            c.Reason = reason ?? string.Empty;
            c.Detail = detail ?? string.Empty;
            return c;
        }

        public override string ToString()
        {
            return Allowed ? "Allowed" : "Blocked(" + Reason + ":" + Detail + ")";
        }
    }

    /// <summary>
    /// 장면 게이트(순수·데이터 기반). 호스트가 공통 장면/개인 이벤트를 재생하기 전에 <see cref="Check"/> 로 묻는다.
    /// 규칙이 없는 노드는 항상 허용. 판정 순서: 챕터·일차 → 필요/금지 플래그 → 선행 노드(after_node) → 너무 늦음(before_node).
    /// 선행 노드는 이미 재생되었거나 프로젝트에 없으면 충족. `~` 선행(soft)은 자동 재생 창의 마지막 날부터 접힌다.
    /// </summary>
    public sealed class SceneGate
    {
        private readonly SceneRuleTable _table;
        private readonly ISceneContext _ctx;

        public SceneGate(SceneRuleTable table, ISceneContext context)
        {
            if (table == null) throw new ArgumentNullException("table");
            if (context == null) throw new ArgumentNullException("context");
            _table = table;
            _ctx = context;
        }

        public SceneRuleTable Table { get { return _table; } }

        public SceneCheck Check(string node)
        {
            SceneRule r = _table.Find(node);
            if (r == null) return SceneCheck.Open();

            int ch = _ctx.Chapter;
            int day = _ctx.DayInChapter;
            if (r.Chapter > 0)
            {
                if (ch < r.Chapter) return SceneCheck.Blocked(SceneBlock.ChapterEarly, r.Chapter.ToString());
                int minDay = r.EffectiveMinDay;
                if (ch == r.Chapter && minDay > 0 && day < minDay)
                    return SceneCheck.Blocked(SceneBlock.DayEarly, minDay.ToString());
            }
            for (int i = 0; i < r.RequiresFlags.Count; i++)
                if (!_ctx.HasFlag(r.RequiresFlags[i])) return SceneCheck.Blocked(SceneBlock.NeedsFlag, r.RequiresFlags[i]);
            for (int i = 0; i < r.ForbidsFlags.Count; i++)
                if (_ctx.HasFlag(r.ForbidsFlags[i])) return SceneCheck.Blocked(SceneBlock.ForbiddenFlag, r.ForbidsFlags[i]);
            bool deadline = DeadlinePassed(r);
            for (int i = 0; i < r.After.Count; i++)
            {
                SceneRef a = r.After[i];
                if (_ctx.IsVisited(a.Node) || !_ctx.NodeExists(a.Node)) continue;
                if (a.Soft && deadline) continue;
                return SceneCheck.Blocked(SceneBlock.NeedsNode, a.Node);
            }
            for (int i = 0; i < r.Before.Count; i++)
                if (_ctx.IsVisited(r.Before[i])) return SceneCheck.Blocked(SceneBlock.TooLate, r.Before[i]);
            return SceneCheck.Open();
        }

        public bool IsAllowed(string node)
        {
            return Check(node).Allowed;
        }

        /// <summary>자동 재생 창의 마지막 날이 되었거나 지났는가(soft 선행 조건을 접는 시점).</summary>
        public bool DeadlinePassed(SceneRule r)
        {
            if (r == null || !r.AutoPlay || r.TriggerDayMax < 1 || r.Chapter < 1) return false;
            return _ctx.Chapter > r.Chapter || (_ctx.Chapter == r.Chapter && _ctx.DayInChapter >= r.TriggerDayMax);
        }

        /// <summary>창이 완전히 지났는가(다음 날 이후). 지난 자동 장면은 슬롯과 상관없이 가장 먼저 재생된다.</summary>
        public bool IsOverdue(SceneRule r)
        {
            if (r == null || !r.AutoPlay || r.Chapter < 1) return false;
            if (_ctx.Chapter > r.Chapter) return true;
            return _ctx.Chapter == r.Chapter && r.TriggerDayMax > 0 && _ctx.DayInChapter > r.TriggerDayMax;
        }

        /// <summary>
        /// 지금 자동 재생해야 할 공통 장면(없으면 null). 표 순서대로 첫 후보: auto_play, 아직 안 봤고, 프로젝트에 있고,
        /// (슬롯이 같거나 이미 지난 슬롯이거나 창이 지남) 이며 <see cref="Check"/> 가 허용. 재생한 뒤 호스트가 방문 기록을 남기면 다시 나오지 않는다(정확히 한 번).
        /// </summary>
        public SceneRule NextAutoScene(SceneSlot slot)
        {
            for (int i = 0; i < _table.Rules.Count; i++)
            {
                SceneRule r = _table.Rules[i];
                if (!r.AutoPlay) continue;
                if (_ctx.IsVisited(r.Node) || !_ctx.NodeExists(r.Node)) continue;
                if (!IsOverdue(r) && (int)r.Slot > (int)slot) continue;
                if (!Check(r.Node).Allowed) continue;
                return r;
            }
            return null;
        }

        /// <summary>아직 안 본 자동 재생 장면 노드들(존재하는 것만, 표 순서). 디버그·검증용.</summary>
        public List<string> PendingAutoScenes()
        {
            List<string> list = new List<string>();
            for (int i = 0; i < _table.Rules.Count; i++)
            {
                SceneRule r = _table.Rules[i];
                if (r.AutoPlay && !_ctx.IsVisited(r.Node) && _ctx.NodeExists(r.Node)) list.Add(r.Node);
            }
            return list;
        }
    }
}
