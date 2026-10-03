using System;
using System.Collections.Generic;
using MorpheHouse.Data;

namespace MorpheHouse.GameFlow
{
    /// <summary>밤 메뉴에 임시로 추가되는 항목 한 줄(복구 장면·동의 확인 대화 등).</summary>
    public sealed class NightExtraEntry
    {
        public const string KindRecovery = "recovery";
        public const string KindConsent = "consent";

        public string Id = string.Empty;
        public string Kind = string.Empty;
        public string NodeName = string.Empty;
        public string Label = string.Empty;
        /// <summary>관련 인물(없으면 빈 문자열).</summary>
        public string CharId = string.Empty;
    }

    /// <summary>
    /// 복구 장면 규칙 한 줄(데이터). 모든 RequireFlags 가 켜져 있고 BlockFlags 가 하나도 켜져 있지 않으며(켜짐 = 값 0 이 아님),
    /// 노드가 존재하고(Once 이면 아직 방문 전이고) 챕터 조건을 만족하면 밤 메뉴에 항목이 나타난다.
    /// 호스트는 플래그를 직접 바꾸지 않는다 — 노드(시나리오)가 결과 플래그를 설정한다.
    /// </summary>
    public sealed class RecoveryRule
    {
        public string Id = string.Empty;
        public string NodeName = string.Empty;
        public string Label = string.Empty;
        public readonly List<string> RequireFlags = new List<string>();
        public readonly List<string> BlockFlags = new List<string>();
        /// <summary>true 면 한 번 재생(방문)하면 다시 나타나지 않는다.</summary>
        public bool Once = true;
        public int MinChapter = 1;
    }

    /// <summary>호스트가 호출하는 복구 노드의 순수 규칙 표. 기본 표는 <see cref="Default"/>.</summary>
    public sealed class RecoveryTable
    {
        private readonly List<RecoveryRule> _rules = new List<RecoveryRule>();

        public List<RecoveryRule> Rules { get { return _rules; } }

        /// <summary>규칙 추가(같은 Id 가 있으면 교체).</summary>
        public RecoveryTable Add(RecoveryRule rule)
        {
            if (rule == null) throw new ArgumentNullException("rule");
            for (int i = 0; i < _rules.Count; i++)
            {
                if (_rules[i].Id == rule.Id) { _rules[i] = rule; return this; }
            }
            _rules.Add(rule);
            return this;
        }

        /// <summary>기본 표: howl_e4_fail 이고 howl_e4_trust / howl_e4_trust_late 가 모두 없으면 Howl_E4_Retry 를 한 번 재생할 수 있다.</summary>
        public static RecoveryTable Default()
        {
            RecoveryTable t = new RecoveryTable();
            RecoveryRule r = new RecoveryRule();
            r.Id = "howl_e4_retry";
            r.NodeName = "Howl_E4_Retry";
            r.Label = "하울과 다시 마주하기";
            r.RequireFlags.Add("howl_e4_fail");
            r.BlockFlags.Add("howl_e4_trust");
            r.BlockFlags.Add("howl_e4_trust_late");
            r.Once = true;
            t.Add(r);
            return t;
        }

        /// <summary>지금 밤 메뉴에 보여 줄 복구 항목들(표 순서).</summary>
        public List<NightExtraEntry> Entries(GameState state, Func<string, bool> nodeExists, Func<string, bool> hasVisited)
        {
            if (state == null) throw new ArgumentNullException("state");
            List<NightExtraEntry> list = new List<NightExtraEntry>();
            for (int i = 0; i < _rules.Count; i++)
            {
                RecoveryRule r = _rules[i];
                if (state.Chapter < r.MinChapter) continue;
                if (nodeExists == null || !nodeExists(r.NodeName)) continue;
                if (r.Once && hasVisited != null && hasVisited(r.NodeName)) continue;
                if (!AllSet(state, r.RequireFlags)) continue;
                if (AnySet(state, r.BlockFlags)) continue;
                NightExtraEntry e = new NightExtraEntry();
                e.Id = r.Id;
                e.Kind = NightExtraEntry.KindRecovery;
                e.NodeName = r.NodeName;
                e.Label = r.Label;
                list.Add(e);
            }
            return list;
        }

        internal static bool IsSet(GameState state, string flag)
        {
            int v;
            return state.Flags.TryGetValue(flag, out v) && v != 0;
        }

        private static bool AllSet(GameState state, List<string> flags)
        {
            for (int i = 0; i < flags.Count; i++) if (!IsSet(state, flags[i])) return false;
            return true;
        }

        private static bool AnySet(GameState state, List<string> flags)
        {
            for (int i = 0; i < flags.Count; i++) if (IsSet(state, flags[i])) return true;
            return false;
        }
    }

    /// <summary>
    /// 공동 루트의 동의 이벤트 재시도 규칙(순수). 동의 노드(&lt;Char&gt;_Harem_Consent)가 harem_ok_&lt;char&gt; 를 설정하지 않고 끝났다면
    /// 게임이 끝나기 전(8장 종료 전)까지 밤 메뉴에서 "동의 확인 대화(이름)" 로 다시 볼 수 있다. 호스트는 동의 플래그를 절대 직접 설정하지 않는다.
    /// </summary>
    public static class ConsentRetry
    {
        public static string ConsentFlag(string charId)
        {
            return "harem_ok_" + charId;
        }

        public static string ConsentNode(string charId)
        {
            return NightRomanceActions.CharTitle(charId) + "_Harem_Consent";
        }

        public static string RetryLabel(string displayName)
        {
            return "동의 확인 대화(" + displayName + ")";
        }

        /// <summary>재시도 가능한 구성원 항목. 공동(2명 이상) 확정 + 동의 플래그 없음 + 노드 존재 + 게임 진행 중일 때만.</summary>
        public static List<NightExtraEntry> Entries(GameState state, Func<string, bool> nodeExists, Func<string, string> nameOf)
        {
            if (state == null) throw new ArgumentNullException("state");
            List<NightExtraEntry> list = new List<NightExtraEntry>();
            if (state.Finished || state.LockedCount < 2) return list;
            List<string> members = state.LockedIds();
            for (int i = 0; i < members.Count; i++)
            {
                string id = members[i];
                if (RecoveryTable.IsSet(state, ConsentFlag(id))) continue;
                string node = ConsentNode(id);
                if (nodeExists == null || !nodeExists(node)) continue;
                NightExtraEntry e = new NightExtraEntry();
                e.Id = "consent_" + id;
                e.Kind = NightExtraEntry.KindConsent;
                e.NodeName = node;
                e.CharId = id;
                e.Label = RetryLabel(nameOf != null ? nameOf(id) : id);
                list.Add(e);
            }
            return list;
        }
    }

    /// <summary>밤 메뉴에 붙는 임시 항목 전체(복구 + 동의 재시도)를 모으는 진입점.</summary>
    public static class NightExtras
    {
        public static List<NightExtraEntry> Collect(GameState state, RecoveryTable table, Func<string, bool> nodeExists,
            Func<string, bool> hasVisited, Func<string, string> nameOf)
        {
            List<NightExtraEntry> list = new List<NightExtraEntry>();
            if (state == null || state.Finished) return list;
            RecoveryTable t = table ?? RecoveryTable.Default();
            list.AddRange(t.Entries(state, nodeExists, hasVisited));
            list.AddRange(ConsentRetry.Entries(state, nodeExists, nameOf));
            return list;
        }
    }
}
