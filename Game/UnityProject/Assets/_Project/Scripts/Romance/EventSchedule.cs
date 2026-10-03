using System;
using System.Collections.Generic;
using MorpheHouse.Data;

namespace MorpheHouse.Romance
{
    /// <summary>event_schedule.csv 한 줄: 인물별·단계별 이벤트 챕터 창(chapter_min ~ chapter_max).</summary>
    public sealed class EventScheduleRow
    {
        public string CharId = string.Empty;
        public int Step;
        /// <summary>0 이하면 events.csv 의 chapter_min 을 그대로 쓴다.</summary>
        public int ChapterMin;
        /// <summary>이 챕터를 넘으면 이벤트가 닫힌다(만료). 기본 99 = 만료 없음.</summary>
        public int ChapterMax = EventSchedule.NoExpiry;
        public string Note = string.Empty;
    }

    /// <summary>
    /// 선택 표: 인물별 이벤트 일정 덮어쓰기(balance/event_schedule.csv). 행이 없는 (인물, 단계)는 events.csv 의 일반 규칙을 따른다.
    /// 표 자체가 없으면(null 텍스트) 빈 표 = 모든 이벤트가 events.csv 규칙. 순수 데이터(난수·상태 없음).
    /// </summary>
    public sealed class EventSchedule
    {
        public const int NoExpiry = 99;

        public readonly List<EventScheduleRow> Rows = new List<EventScheduleRow>();
        private readonly Dictionary<string, EventScheduleRow> _index = new Dictionary<string, EventScheduleRow>();

        public int Count { get { return Rows.Count; } }

        public static EventSchedule Load(ITextSource source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return Parse(source.Load("event_schedule"));
        }

        /// <summary>CSV 텍스트를 읽는다. null/빈 문자열이면 빈 표. 인물이 비었거나 단계가 1 미만인 줄은 건너뛴다. 같은 (인물, 단계)는 나중 줄이 이긴다.</summary>
        public static EventSchedule Parse(string text)
        {
            EventSchedule table = new EventSchedule();
            if (string.IsNullOrEmpty(text)) return table;
            foreach (CsvRow r in CsvTable.Parse(text).Rows)
            {
                EventScheduleRow row = new EventScheduleRow();
                row.CharId = r.Str("char").Trim();
                row.Step = r.Int("step");
                row.ChapterMin = r.Int("chapter_min");
                int max = r.Int("chapter_max");
                row.ChapterMax = max <= 0 ? NoExpiry : max;
                row.Note = r.Str("note");
                if (row.CharId.Length == 0 || row.Step < 1) continue;
                string key = Key(row.CharId, row.Step);
                EventScheduleRow old;
                if (table._index.TryGetValue(key, out old)) table.Rows.Remove(old);
                table._index[key] = row;
                table.Rows.Add(row);
            }
            return table;
        }

        /// <summary>(인물, 단계)의 행. 없으면 null.</summary>
        public EventScheduleRow Find(string charId, int step)
        {
            EventScheduleRow row;
            if (charId == null) return null;
            return _index.TryGetValue(Key(charId, step), out row) ? row : null;
        }

        private static string Key(string charId, int step)
        {
            return charId + "|" + step;
        }
    }
}
