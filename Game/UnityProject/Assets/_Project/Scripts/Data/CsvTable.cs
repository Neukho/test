using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MorpheHouse.Data
{
    /// <summary>CSV 한 줄. 열 이름으로 값을 읽는다.</summary>
    public sealed class CsvRow
    {
        private readonly CsvTable _table;
        private readonly string[] _cells;

        internal CsvRow(CsvTable table, string[] cells)
        {
            _table = table;
            _cells = cells;
        }

        public string Str(string column)
        {
            int index = _table.IndexOf(column);
            if (index < 0 || index >= _cells.Length) return string.Empty;
            return _cells[index];
        }

        public float Float(string column, float fallback = 0f)
        {
            float value;
            string cell = Str(column);
            if (float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
            if (cell.Trim().Length != 0) throw new FormatException("CSV column '" + column + "': not a number: '" + cell + "'"); // 오타가 조용히 0 이 되어 게이트를 여는 일 방지(QA-12)
            return fallback;
        }

        public int Int(string column, int fallback = 0)
        {
            float value;
            string cell = Str(column);
            if (float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return (int)Math.Round(value);
            if (cell.Trim().Length != 0) throw new FormatException("CSV column '" + column + "': not an integer: '" + cell + "'");
            return fallback;
        }
    }

    /// <summary>헤더가 있는 간단한 CSV 파서(따옴표·BOM·CRLF 지원).</summary>
    public sealed class CsvTable
    {
        private readonly Dictionary<string, int> _columns = new Dictionary<string, int>();
        public readonly List<CsvRow> Rows = new List<CsvRow>();

        internal int IndexOf(string column)
        {
            int index;
            return _columns.TryGetValue(column, out index) ? index : -1;
        }

        public static CsvTable Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            List<string[]> records = ReadRecords(text);
            CsvTable table = new CsvTable();
            if (records.Count == 0) return table;

            string[] header = records[0];
            for (int i = 0; i < header.Length; i++) table._columns[header[i].Trim()] = i;
            for (int r = 1; r < records.Count; r++) table.Rows.Add(new CsvRow(table, records[r]));
            return table;
        }

        private static List<string[]> ReadRecords(string text)
        {
            List<string[]> records = new List<string[]>();
            List<string> cells = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool inQuotes = false;

            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cell.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { cells.Add(cell.ToString()); cell.Length = 0; }
                else if (c == '\r') { /* CRLF 의 CR 은 무시 */ }
                else if (c == '\n') { FlushRecord(records, cells, cell); }
                else cell.Append(c);
            }
            FlushRecord(records, cells, cell);
            return records;
        }

        private static void FlushRecord(List<string[]> records, List<string> cells, StringBuilder cell)
        {
            if (cells.Count == 0 && cell.Length == 0) return; // 빈 줄
            cells.Add(cell.ToString());
            cell.Length = 0;
            records.Add(cells.ToArray());
            cells.Clear();
        }
    }
}
