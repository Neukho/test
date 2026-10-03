using System;
using System.Collections.Generic;
using MorpheHouse.Data;

namespace MorpheHouse.Dialogue
{
    /// <summary>호감도 구간. stranger(&lt;30) / friendly(30~59) / close(&gt;=60).</summary>
    public enum AffinityBand
    {
        Stranger = 0,
        Friendly = 1,
        Close = 2
    }

    /// <summary>address_terms.csv 한 행(캐릭터 × 주인공 성별 × 호감 구간).</summary>
    public sealed class AddressTerms
    {
        public string CallsPlayerKo = string.Empty;
        public string PlayerCallsKo = string.Empty;
        public string CallsPlayerEn = string.Empty;
        public string PlayerCallsEn = string.Empty;
    }

    /// <summary>
    /// 호칭 표. NPC 가 주인공을 부르는 말(calls_player)과 주인공이 NPC 를 부르는 말(player_calls)을
    /// 캐릭터·주인공 성별·호감 구간별로 조회한다. 숫자 규칙(호감 증감)은 성별과 무관하고 호칭·어조만 다르다.
    /// 용어 안의 `{name}` 은 주인공 이름으로 치환된다.
    /// </summary>
    public sealed class AddressBook
    {
        /// <summary>호감 30 미만은 stranger.</summary>
        public const float FriendlyMin = 30f;
        /// <summary>호감 60 이상은 close.</summary>
        public const float CloseMin = 60f;

        private readonly Dictionary<string, AddressTerms> _rows = new Dictionary<string, AddressTerms>();

        public int Count { get { return _rows.Count; } }

        public static AffinityBand BandOf(float affinity)
        {
            if (affinity >= CloseMin) return AffinityBand.Close;
            if (affinity >= FriendlyMin) return AffinityBand.Friendly;
            return AffinityBand.Stranger;
        }

        public static string BandName(AffinityBand band)
        {
            switch (band)
            {
                case AffinityBand.Close: return "close";
                case AffinityBand.Friendly: return "friendly";
                default: return "stranger";
            }
        }

        private static string Key(string charId, string gender, string band)
        {
            return charId + "|" + gender + "|" + band;
        }

        /// <summary>CSV 텍스트(열: char,player_gender,band,calls_player_ko,player_calls_ko,calls_player_en,player_calls_en)에서 만든다.</summary>
        public static AddressBook FromCsv(string csvText)
        {
            CsvTable table = CsvTable.Parse(csvText);
            AddressBook book = new AddressBook();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                CsvRow r = table.Rows[i];
                AddressTerms t = new AddressTerms();
                t.CallsPlayerKo = r.Str("calls_player_ko");
                t.PlayerCallsKo = r.Str("player_calls_ko");
                t.CallsPlayerEn = r.Str("calls_player_en");
                t.PlayerCallsEn = r.Str("player_calls_en");
                book._rows[Key(r.Str("char").ToLowerInvariant(), r.Str("player_gender").ToLowerInvariant(), r.Str("band").ToLowerInvariant())] = t;
            }
            return book;
        }

        /// <summary>표에서 행을 찾는다. 없으면 null.</summary>
        public AddressTerms Find(string charId, string gender, AffinityBand band)
        {
            AddressTerms t;
            string id = (charId ?? string.Empty).ToLowerInvariant();
            return _rows.TryGetValue(Key(id, gender, BandName(band)), out t) ? t : null;
        }
    }
}
