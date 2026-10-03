using System;
using System.Collections.Generic;
using MorpheHouse.Core;

namespace MorpheHouse.Data
{
    public sealed class ChapterRow
    {
        public int Chapter;
        public string Name;
        public int Days;
        public int LevelCap;
        public int MenuCount;
        public int StaffCount;
        public float MainStoryMin;
        public int CommonEvents;
        public float AffCap;
        public float TrustCap;
        public float TargetRevStart;
        public float TargetRevEnd;
        public string Unlocks;
    }

    public sealed class StaffBand
    {
        public float AffMin;
        public float AffMax;
        public float QualityBonus;
        public float SpeedBonus;
        public string Label;
    }

    public sealed class ActionRow
    {
        public string Action;
        public float AffGain;
        public float TrustGain;
        public int CooldownDays;
        public float Minutes;
        public float Fatigue;
    }

    public sealed class EventRow
    {
        public int Step;
        public int ChapterMin;
        public float AffMin;
        public float TrustMin;
        public float AffGain;
        public float TrustGain;
        public float Minutes;
    }

    /// <summary>characters.csv 의 밸런스 관련 열: 이야기에 처음 등장하는 챕터(first_chapter)와 가게 직원 합류 챕터(staff_join_chapter, 0 = 직원 아님).</summary>
    public sealed class CharAvailabilityRow
    {
        public string Id;
        public int FirstChapter = 1;
        public int StaffJoinChapter;
        /// <summary>만회 배율: 늦게 등장하는 인물이 더 짧은 기간에 같은 호감·신뢰에 닿도록 양수 증가분에 곱한다(characters.csv gain_mult, 기본 1).</summary>
        public float GainMult = 1f;
    }

    /// <summary>
    /// balance/*.csv 를 읽어 메모리에 올린 밸런스 표. 난이도 열(difficulty.csv)은 params 를 덮어쓴다.
    /// 콘텐츠 수치는 코드가 아니라 CSV 에서만 바꾼다.
    /// </summary>
    public sealed class BalanceTable
    {
        public Difficulty Difficulty { get; private set; }
        public readonly Dictionary<string, float> Params = new Dictionary<string, float>();
        public readonly List<ChapterRow> Chapters = new List<ChapterRow>();
        public readonly List<StaffBand> Bands = new List<StaffBand>();
        public readonly Dictionary<string, ActionRow> Actions = new Dictionary<string, ActionRow>();
        public readonly List<EventRow> Events = new List<EventRow>();
        public readonly Dictionary<int, int> SeatsByLevel = new Dictionary<int, int>();
        /// <summary>characters.csv 의 등장/합류 챕터(파일 순서). 직원 영업 보너스와 인물 선택 가능 여부의 단일 출처.</summary>
        public readonly List<CharAvailabilityRow> Characters = new List<CharAvailabilityRow>();

        public int MaxChapter { get { return Chapters.Count; } }

        /// <summary>파라미터 값을 읽는다. 없으면 예외(오타를 조용히 넘기지 않는다).</summary>
        public float P(string key)
        {
            float value;
            if (!Params.TryGetValue(key, out value))
                throw new KeyNotFoundException("balance param not found: " + key);
            return value;
        }

        public ChapterRow Chapter(int chapter)
        {
            for (int i = 0; i < Chapters.Count; i++)
                if (Chapters[i].Chapter == chapter) return Chapters[i];
            throw new KeyNotFoundException("chapter not found: " + chapter);
        }

        /// <summary>이 인물이 이야기에 처음 등장하는 챕터. 표에 없으면 1(처음부터 선택 가능).</summary>
        public int FirstChapterOf(string charId)
        {
            for (int i = 0; i < Characters.Count; i++)
                if (Characters[i].Id == charId) return Characters[i].FirstChapter;
            return 1;
        }

        /// <summary>지정 챕터에 이미 등장했는지(= 대화·선물·데이트·이벤트 대상이 될 수 있는지).</summary>
        public bool IsCharAvailable(string charId, int chapter)
        {
            return chapter >= FirstChapterOf(charId);
        }

        /// <summary>인물별 호감·신뢰 만회 배율(표에 없으면 1).</summary>
        public float GainMultOf(string charId)
        {
            for (int i = 0; i < Characters.Count; i++)
                if (Characters[i].Id == charId) return Characters[i].GainMult;
            return 1f;
        }

        /// <summary>가게 직원으로 합류하는 챕터. 직원이 아니면 0.</summary>
        public int StaffJoinChapterOf(string charId)
        {
            for (int i = 0; i < Characters.Count; i++)
                if (Characters[i].Id == charId) return Characters[i].StaffJoinChapter;
            return 0;
        }

        /// <summary>현재 레벨에서 다음 레벨로 올리는 비용.</summary>
        public float LevelCost(int level)
        {
            return P("level_cost_base") * (float)Math.Pow(P("level_cost_growth"), level - 1);
        }

        /// <summary>직원 호감도 구간별 보너스.</summary>
        public StaffBand BandFor(float affinity)
        {
            for (int i = 0; i < Bands.Count; i++)
                if (affinity >= Bands[i].AffMin && affinity <= Bands[i].AffMax) return Bands[i];
            return Bands[Bands.Count - 1];
        }

        public static BalanceTable Load(ITextSource source, Difficulty difficulty)
        {
            BalanceTable table = new BalanceTable();
            table.Difficulty = difficulty;

            foreach (CsvRow r in Require(source, "params").Rows)
                table.Params[r.Str("key")] = r.Float("value");

            string column = difficulty.ToString().ToLowerInvariant();
            foreach (CsvRow r in Require(source, "difficulty").Rows)
                table.Params[r.Str("param")] = r.Float(column);

            foreach (CsvRow r in Require(source, "chapters").Rows)
            {
                ChapterRow c = new ChapterRow();
                c.Chapter = r.Int("chapter");
                c.Name = r.Str("name");
                c.Days = r.Int("days");
                c.LevelCap = r.Int("level_cap");
                c.MenuCount = r.Int("menu_count");
                c.StaffCount = r.Int("staff_count");
                c.MainStoryMin = r.Float("main_story_min");
                c.CommonEvents = r.Int("common_events");
                c.AffCap = r.Float("aff_cap");
                c.TrustCap = r.Float("trust_cap");
                c.TargetRevStart = r.Float("target_rev_start");
                c.TargetRevEnd = r.Float("target_rev_end");
                c.Unlocks = r.Str("unlocks");
                table.Chapters.Add(c);
            }

            foreach (CsvRow r in Require(source, "staff_bands").Rows)
            {
                StaffBand b = new StaffBand();
                b.AffMin = r.Float("aff_min");
                b.AffMax = r.Float("aff_max");
                b.QualityBonus = r.Float("quality_bonus");
                b.SpeedBonus = r.Float("speed_bonus");
                b.Label = r.Str("label");
                table.Bands.Add(b);
            }

            foreach (CsvRow r in Require(source, "actions").Rows)
            {
                ActionRow a = new ActionRow();
                a.Action = r.Str("action");
                a.AffGain = r.Float("aff_gain");
                a.TrustGain = r.Float("trust_gain");
                a.CooldownDays = r.Int("cooldown_days");
                a.Minutes = r.Float("minutes");
                a.Fatigue = r.Float("fatigue");
                table.Actions[a.Action] = a;
            }

            foreach (CsvRow r in Require(source, "events").Rows)
            {
                EventRow e = new EventRow();
                e.Step = r.Int("step");
                e.ChapterMin = r.Int("chapter_min");
                e.AffMin = r.Float("aff_min");
                e.TrustMin = r.Float("trust_min");
                e.AffGain = r.Float("aff_gain");
                e.TrustGain = r.Float("trust_gain");
                e.Minutes = r.Float("minutes");
                table.Events.Add(e);
            }

            foreach (CsvRow r in Require(source, "characters").Rows)
            {
                CharAvailabilityRow c = new CharAvailabilityRow();
                c.Id = r.Str("id");
                c.StaffJoinChapter = r.Int("staff_join_chapter");
                c.FirstChapter = r.Int("first_chapter");
                if (c.FirstChapter < 1) c.FirstChapter = 1;
                c.GainMult = r.Float("gain_mult");
                if (c.GainMult <= 0f) c.GainMult = 1f;
                table.Characters.Add(c);
            }

            foreach (CsvRow r in Require(source, "shop_levels").Rows)
                table.SeatsByLevel[r.Int("level")] = r.Int("seats");

            if (table.Chapters.Count == 0) throw new InvalidOperationException("chapters.csv is empty");
            if (table.Bands.Count == 0) throw new InvalidOperationException("staff_bands.csv is empty");
            return table;
        }

        private static CsvTable Require(ITextSource source, string name)
        {
            string text = source.Load(name);
            if (text == null) throw new InvalidOperationException("balance csv not found: " + name);
            return CsvTable.Parse(text);
        }
    }
}
