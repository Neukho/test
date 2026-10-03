using System;
using System.Collections.Generic;
using MorpheHouse.Data;

namespace MorpheHouse.Romance
{
    /// <summary>엔딩 종류. 문자열 id 는 <see cref="EndingResult.Id"/>(endings.csv 의 ending 열).</summary>
    public enum EndingKind
    {
        Community = 0,
        Normal = 1,
        Good = 2,
        True = 3,
        HaremDuo = 4,
        HaremTrio = 5
    }

    /// <summary>엔딩 판정 결과. UI·시나리오는 NodeName 을 재생하고 CgIds 를 갤러리에 해금하면 된다.</summary>
    public sealed class EndingResult
    {
        public EndingKind Kind = EndingKind.Community;
        /// <summary>endings.csv 의 ending 값(normal / good / true / community / harem_duo / harem_trio).</summary>
        public string Id = EndingTable.Community;
        /// <summary>재생할 Yarn 노드: &lt;Char&gt;_End_Normal|Good|True, Ending_Harem2, Ending_Harem3, Ending_Community.</summary>
        public string NodeName = EndingNodes.Community;
        /// <summary>이 엔딩에서 해금하는 CG id(트루 → &lt;CHAR&gt;_E6, 다자 → HAREM_E1). 없으면 빈 목록.</summary>
        public readonly List<string> CgIds = new List<string>();
        /// <summary>판정에 쓰인 루트 구성원(미확정이면 빈 목록).</summary>
        public readonly List<string> Members = new List<string>();
        /// <summary>대표 인물(단독이면 그 인물, 공동이면 첫 구성원, 미확정이면 빈 문자열).</summary>
        public string PrimaryChar = string.Empty;
        /// <summary>
        /// 판정 사유 코드: ok(개인·다자 엔딩 성립) / no_lock(루트 미확정) / below_normal(단독인데 노멀 기준 미달) / group_unmet(공동인데 전원 미충족).
        /// </summary>
        public string Reason = "ok";
        /// <summary>커뮤니티로 떨어졌을 때 미달 항목("howl:aff", "johan:flag harem_ok_johan" ...). 디버그·안내용.</summary>
        public readonly List<string> Shortfalls = new List<string>();

        public bool IsHarem { get { return Kind == EndingKind.HaremDuo || Kind == EndingKind.HaremTrio; } }
        public bool IsCommunity { get { return Kind == EndingKind.Community; } }
    }

    /// <summary>엔딩 노드 이름 규칙(docs/10 §4).</summary>
    public static class EndingNodes
    {
        public const string Harem2 = "Ending_Harem2";
        public const string Harem3 = "Ending_Harem3";
        public const string Community = "Ending_Community";
        /// <summary>다자 엔딩 공통 CG id(CG ID 규칙: 다자 HAREM_E1).</summary>
        public const string HaremCg = "HAREM_E1";

        /// <summary>&lt;Char&gt;_End_Normal|Good|True (howl → Howl_End_Good).</summary>
        public static string Personal(string charId, EndingKind kind)
        {
            string suffix = kind == EndingKind.True ? "True" : (kind == EndingKind.Good ? "Good" : "Normal");
            return Title(charId) + "_End_" + suffix;
        }

        /// <summary>트루 엔딩 추가 CG id(howl → HOWL_E6).</summary>
        public static string TrueCg(string charId)
        {
            return (charId ?? string.Empty).ToUpperInvariant() + "_E6";
        }

        private static string Title(string charId)
        {
            if (string.IsNullOrEmpty(charId)) return string.Empty;
            return char.ToUpperInvariant(charId[0]) + charId.Substring(1);
        }
    }

    /// <summary>
    /// 엔딩 판정(순수·결정적, 난수·이벤트 버스·상태 변경 없음). 규칙은 docs/10 §2 와 balance/endings.csv.
    /// - 미확정 → 커뮤니티.
    /// - 단독 확정 → 트루(호감·신뢰·이벤트 + ledger_truth + &lt;char&gt;_key) → 굿 → 노멀 → 미달이면 커뮤니티.
    /// - 공동 확정(2/3명) → 구성원 전원이 harem_duo/harem_trio 기준과 harem_ok_&lt;char&gt; 를 만족하면 다자 엔딩,
    ///   한 명이라도 미달이면 부분·노멀 엔딩 없이 커뮤니티.
    /// 임계값은 모두 EndingTable(CSV)에서 읽는다. 비교는 이상(&gt;=)이라 경계값은 통과한다.
    /// </summary>
    public static class EndingEvaluator
    {
        /// <summary>6장 장부 진실 플래그 이름.</summary>
        public const string LedgerTruthFlag = "ledger_truth";

        public static EndingResult Evaluate(GameState state, EndingTable table)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (table == null) throw new ArgumentNullException("table");

            EndingResult result = new EndingResult();
            List<string> members = state.LockedIds();
            for (int i = 0; i < members.Count; i++) result.Members.Add(members[i]);
            if (members.Count == 0)
            {
                result.Reason = "no_lock";
                return result;
            }
            result.PrimaryChar = members[0];

            if (members.Count == 1) return EvaluateSingle(state, table, members[0], result);
            return EvaluateGroup(state, table, members, result);
        }

        private static EndingResult EvaluateSingle(GameState state, EndingTable table, string id, EndingResult result)
        {
            CharState cs;
            if (!state.Chars.TryGetValue(id, out cs))
            {
                result.Reason = "below_normal";
                result.Shortfalls.Add(id + ":unknown_char");
                return result;
            }

            EndingRow tr = table.TrueRow;
            if (Meets(cs, tr) && FlagsMet(state, tr, id))
            {
                result.Kind = EndingKind.True;
                result.Id = EndingTable.True;
                result.NodeName = EndingNodes.Personal(id, EndingKind.True);
                result.CgIds.Add(EndingNodes.TrueCg(id));
                return result;
            }
            if (Meets(cs, table.GoodRow) && FlagsMet(state, table.GoodRow, id))
            {
                result.Kind = EndingKind.Good;
                result.Id = EndingTable.Good;
                result.NodeName = EndingNodes.Personal(id, EndingKind.Good);
                return result;
            }
            if (Meets(cs, table.NormalRow) && FlagsMet(state, table.NormalRow, id))
            {
                result.Kind = EndingKind.Normal;
                result.Id = EndingTable.Normal;
                result.NodeName = EndingNodes.Personal(id, EndingKind.Normal);
                return result;
            }
            result.Reason = "below_normal";
            AddShortfalls(result, id, cs, state, table.NormalRow);
            return result;
        }

        private static EndingResult EvaluateGroup(GameState state, EndingTable table, List<string> members, EndingResult result)
        {
            // QA-15: 유효하지 않은 묶음(4명 이상, 중복 구성원)은 SaveValidator 가 막지만 평가기도 다자 엔딩을 주지 않는다 -> 커뮤니티(reason invalid_group)
            bool valid = members.Count <= RomanceRules.MaxGroupSize;
            for (int i = 0; i < members.Count && valid; i++)
                for (int j = 0; j < i; j++)
                    if (members[i] == members[j]) valid = false;
            if (!valid)
            {
                result.Reason = "invalid_group";
                return result;
            }
            bool trio = members.Count >= 3;
            EndingRow row = trio ? table.HaremTrioRow : table.HaremDuoRow;
            for (int i = 0; i < members.Count; i++)
            {
                string id = members[i];
                CharState cs;
                if (!state.Chars.TryGetValue(id, out cs))
                {
                    result.Shortfalls.Add(id + ":unknown_char");
                    continue;
                }
                AddShortfalls(result, id, cs, state, row);
            }
            if (result.Shortfalls.Count > 0)
            {
                result.Reason = "group_unmet";
                return result;
            }
            result.Kind = trio ? EndingKind.HaremTrio : EndingKind.HaremDuo;
            result.Id = row.Id;
            result.NodeName = trio ? EndingNodes.Harem3 : EndingNodes.Harem2;
            result.CgIds.Add(EndingNodes.HaremCg);
            return result;
        }

        /// <summary>호감·신뢰·이벤트 수 기준만 확인(플래그 제외).</summary>
        public static bool Meets(CharState cs, EndingRow row)
        {
            return cs != null && cs.Aff >= row.AffMin && cs.Trust >= row.TrustMin && cs.EventsDone >= row.RequiredEvents;
        }

        /// <summary>
        /// 행의 required_flags 중 플래그 토큰을 확인한다. route_locked / group_locked 는 호출 전제라 항상 통과,
        /// ledger_truth → 같은 이름 플래그, key_flag → &lt;char&gt;_key, harem_consent → harem_ok_&lt;char&gt;, 그 밖의 토큰은 같은 이름 플래그.
        /// </summary>
        public static bool FlagsMet(GameState state, EndingRow row, string charId)
        {
            for (int i = 0; i < row.RequiredFlags.Count; i++)
            {
                string name = FlagName(row.RequiredFlags[i], charId);
                if (name != null && !HasFlag(state, name)) return false;
            }
            return true;
        }

        /// <summary>토큰 → 실제 플래그 이름. 확인이 필요 없는 토큰이면 null.</summary>
        public static string FlagName(string token, string charId)
        {
            switch (token)
            {
                case "route_locked":
                case "route_unlocked":
                case "group_locked": return null;
                case "key_flag": return charId + "_key";
                case "harem_consent": return "harem_ok_" + charId;
                default: return token;
            }
        }

        private static bool HasFlag(GameState state, string name)
        {
            int v;
            return state.Flags.TryGetValue(name, out v) && v != 0;
        }

        private static void AddShortfalls(EndingResult result, string id, CharState cs, GameState state, EndingRow row)
        {
            if (cs.Aff < row.AffMin) result.Shortfalls.Add(id + ":aff");
            if (cs.Trust < row.TrustMin) result.Shortfalls.Add(id + ":trust");
            if (cs.EventsDone < row.RequiredEvents) result.Shortfalls.Add(id + ":events");
            for (int i = 0; i < row.RequiredFlags.Count; i++)
            {
                string name = FlagName(row.RequiredFlags[i], id);
                if (name != null && !HasFlag(state, name)) result.Shortfalls.Add(id + ":flag " + name);
            }
        }
    }
}
