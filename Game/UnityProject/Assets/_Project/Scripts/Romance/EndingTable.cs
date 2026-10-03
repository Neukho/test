using System;
using System.Collections.Generic;
using MorpheHouse.Data;

namespace MorpheHouse.Romance
{
    /// <summary>endings.csv 한 줄. required_flags 는 ';' 로 나눈 토큰 목록(route_locked / ledger_truth / key_flag / group_locked / harem_consent ...).</summary>
    public sealed class EndingRow
    {
        public string Id = string.Empty;
        public string Route = string.Empty;
        public float AffMin;
        public float TrustMin;
        public int RequiredEvents;
        public readonly List<string> RequiredFlags = new List<string>();
        public string Note = string.Empty;

        public bool HasFlagToken(string token)
        {
            return RequiredFlags.Contains(token);
        }
    }

    /// <summary>
    /// 엔딩 조건 표(balance/endings.csv). RomanceCatalog 처럼 BalanceTable 과 분리해 로드한다.
    /// 필수 행: normal / good / true / community / harem_duo / harem_trio (없으면 로드 예외).
    /// </summary>
    public sealed class EndingTable
    {
        public const string Normal = "normal";
        public const string Good = "good";
        public const string True = "true";
        public const string Community = "community";
        public const string HaremDuo = "harem_duo";
        public const string HaremTrio = "harem_trio";

        public static readonly string[] RequiredIds = { Normal, Good, True, Community, HaremDuo, HaremTrio };

        public readonly List<EndingRow> Rows = new List<EndingRow>();
        private readonly Dictionary<string, EndingRow> _byId = new Dictionary<string, EndingRow>();

        /// <summary>ITextSource 에서 "endings" 를 읽는다. 표가 없거나 필수 행이 빠졌으면 InvalidOperationException.</summary>
        public static EndingTable Load(ITextSource source)
        {
            if (source == null) throw new ArgumentNullException("source");
            string text = source.Load("endings");
            if (text == null) throw new InvalidOperationException("endings csv not found: endings");
            return Parse(text);
        }

        /// <summary>CSV 텍스트에서 직접 만든다(테스트용).</summary>
        public static EndingTable Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            EndingTable table = new EndingTable();
            foreach (CsvRow r in CsvTable.Parse(text).Rows)
            {
                EndingRow row = new EndingRow();
                row.Id = r.Str("ending").Trim();
                if (row.Id.Length == 0) continue;
                row.Route = r.Str("route").Trim();
                row.AffMin = r.Float("aff_min");
                row.TrustMin = r.Float("trust_min");
                row.RequiredEvents = r.Int("required_events");
                string[] tokens = r.Str("required_flags").Split(';');
                for (int i = 0; i < tokens.Length; i++)
                {
                    string t = tokens[i].Trim();
                    if (t.Length > 0) row.RequiredFlags.Add(t);
                }
                row.Note = r.Str("note");
                table.Rows.Add(row);
                table._byId[row.Id] = row;
            }
            for (int i = 0; i < RequiredIds.Length; i++)
                if (!table._byId.ContainsKey(RequiredIds[i]))
                    throw new InvalidOperationException("endings.csv missing row: " + RequiredIds[i]);
            return table;
        }

        /// <summary>id 로 행을 찾는다. 없으면 null.</summary>
        public EndingRow Find(string id)
        {
            EndingRow row;
            return id != null && _byId.TryGetValue(id, out row) ? row : null;
        }

        public EndingRow NormalRow { get { return _byId[Normal]; } }
        public EndingRow GoodRow { get { return _byId[Good]; } }
        public EndingRow TrueRow { get { return _byId[True]; } }
        public EndingRow CommunityRow { get { return _byId[Community]; } }
        public EndingRow HaremDuoRow { get { return _byId[HaremDuo]; } }
        public EndingRow HaremTrioRow { get { return _byId[HaremTrio]; } }
    }
}
