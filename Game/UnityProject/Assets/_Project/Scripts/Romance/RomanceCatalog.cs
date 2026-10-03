using System;
using System.Collections.Generic;
using MorpheHouse.Data;

namespace MorpheHouse.Romance
{
    /// <summary>
    /// 연애 모듈용 콘텐츠 표(characters / prefs / gifts / spots). BalanceTable 과 분리해 Data 어셈블리를 건드리지 않는다.
    /// </summary>
    public sealed class RomanceCatalog
    {
        public readonly List<CharacterRow> Characters = new List<CharacterRow>();
        public readonly List<PrefRow> Prefs = new List<PrefRow>();
        public readonly Dictionary<string, GiftRow> Gifts = new Dictionary<string, GiftRow>();
        public readonly Dictionary<string, SpotRow> Spots = new Dictionary<string, SpotRow>();

        /// <summary>인물별 이벤트 일정 덮어쓰기(event_schedule.csv, 선택). 없으면 빈 표 = events.csv 규칙만 쓴다.</summary>
        public EventSchedule Schedule = new EventSchedule();

        /// <summary>장면 게이트 규칙 원문(scene_rules.csv, 선택). 파싱은 Game 어셈블리(SceneRuleTable)가 한다. 없으면 null.</summary>
        public string SceneRulesText;

        // 조회 가속용: "char|kind|tag" -> 취향
        private readonly Dictionary<string, Preference> _prefIndex = new Dictionary<string, Preference>();

        public static RomanceCatalog Load(ITextSource source)
        {
            if (source == null) throw new ArgumentNullException("source");
            RomanceCatalog cat = new RomanceCatalog();

            foreach (CsvRow r in Require(source, "characters").Rows)
            {
                CharacterRow c = new CharacterRow();
                c.Id = r.Str("id");
                c.NameKo = r.Str("name_ko");
                c.Species = r.Str("species");
                c.Age = r.Int("age");
                c.Gender = r.Str("gender");
                c.Role = r.Str("role");
                c.StaffJoinChapter = r.Int("staff_join_chapter");
                c.FirstChapter = Math.Max(1, r.Int("first_chapter"));
                c.Keyword = r.Str("keyword");
                cat.Characters.Add(c);
            }

            foreach (CsvRow r in Require(source, "gifts").Rows)
            {
                GiftRow g = new GiftRow();
                g.Id = r.Str("id");
                g.NameKo = r.Str("name_ko");
                g.PriceTier = r.Int("price_tier");
                cat.Gifts[g.Id] = g;
            }

            foreach (CsvRow r in Require(source, "spots").Rows)
            {
                SpotRow s = new SpotRow();
                s.Id = r.Str("id");
                s.NameKo = r.Str("name_ko");
                s.UnlockChapter = r.Int("unlock_chapter");
                s.Period = r.Str("period");
                cat.Spots[s.Id] = s;
            }

            foreach (CsvRow r in Require(source, "prefs").Rows)
            {
                PrefRow p = new PrefRow();
                p.CharId = r.Str("char");
                p.Kind = ParseKind(r.Str("kind"));
                p.Tag = r.Str("tag");
                p.Value = r.Str("value") == "like" ? Preference.Like : Preference.Dislike;
                cat.Prefs.Add(p);
                cat._prefIndex[Key(p.CharId, p.Kind, p.Tag)] = p.Value;
            }

            cat.Schedule = EventSchedule.Load(source);
            cat.SceneRulesText = source.Load("scene_rules");

            if (cat.Characters.Count == 0) throw new InvalidOperationException("characters.csv is empty");
            return cat;
        }

        /// <summary>취향 판정. 표에 없으면 보통(Neutral).</summary>
        public Preference GetPreference(string charId, PrefKind kind, string tag)
        {
            Preference value;
            if (charId == null || tag == null) return Preference.Neutral;
            return _prefIndex.TryGetValue(Key(charId, kind, tag), out value) ? value : Preference.Neutral;
        }

        public CharacterRow FindCharacter(string id)
        {
            for (int i = 0; i < Characters.Count; i++)
                if (Characters[i].Id == id) return Characters[i];
            return null;
        }

        public GiftRow FindGift(string id)
        {
            GiftRow g;
            return id != null && Gifts.TryGetValue(id, out g) ? g : null;
        }

        public SpotRow FindSpot(string id)
        {
            SpotRow s;
            return id != null && Spots.TryGetValue(id, out s) ? s : null;
        }

        /// <summary>해당 인물에 대한 특정 취향의 태그 목록(예: howl 이 좋아하는 선물).</summary>
        public List<string> TagsFor(string charId, PrefKind kind, Preference value)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < Prefs.Count; i++)
            {
                PrefRow p = Prefs[i];
                if (p.CharId == charId && p.Kind == kind && p.Value == value) result.Add(p.Tag);
            }
            return result;
        }

        /// <summary>현재 챕터에 해금된 스팟 목록.</summary>
        public List<SpotRow> UnlockedSpots(int chapter)
        {
            List<SpotRow> result = new List<SpotRow>();
            foreach (KeyValuePair<string, SpotRow> pair in Spots)
                if (pair.Value.UnlockChapter <= chapter) result.Add(pair.Value);
            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result;
        }

        private static PrefKind ParseKind(string text)
        {
            if (text == "menu") return PrefKind.Menu;
            if (text == "spot") return PrefKind.Spot;
            return PrefKind.Gift;
        }

        private static string Key(string charId, PrefKind kind, string tag)
        {
            return charId + "|" + (int)kind + "|" + tag;
        }

        private static CsvTable Require(ITextSource source, string name)
        {
            string text = source.Load(name);
            if (text == null) throw new InvalidOperationException("romance csv not found: " + name);
            return CsvTable.Parse(text);
        }
    }
}
