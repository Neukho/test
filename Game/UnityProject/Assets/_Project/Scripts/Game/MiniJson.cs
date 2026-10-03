using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 작은 JSON 파서/작성기(순수). profile.json 처럼 구조가 단순한 파일용이다.
    /// 값은 Dictionary&lt;string, object&gt; / List&lt;object&gt; / string / double / bool / null 로 돌려준다.
    /// 문법 오류·과도한 중첩은 FormatException 이므로 호출자는 반드시 try/catch 로 폴백한다.
    /// </summary>
    public static class MiniJson
    {
        private const int MaxDepth = 12;

        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("null");
            int i = 0;
            object v = ReadValue(text, ref i, 0);
            SkipWs(text, ref i);
            if (i != text.Length) throw new FormatException("trailing characters at " + i);
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object ReadValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("too deep");
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i, depth);
            if (c == '[') return ReadArray(s, ref i, depth);
            if (c == '"') return ReadString(s, ref i);
            if (Match(s, i, "true")) { i += 4; return true; }
            if (Match(s, i, "false")) { i += 5; return false; }
            if (Match(s, i, "null")) { i += 4; return null; }
            return ReadNumber(s, ref i);
        }

        private static bool Match(string s, int i, string word)
        {
            return string.CompareOrdinal(s, i, word, 0, word.Length) == 0;
        }

        private static Dictionary<string, object> ReadObject(string s, ref int i, int depth)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("key expected at " + i);
                string key = ReadString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("':' expected at " + i);
                i++;
                d[key] = ReadValue(s, ref i, depth + 1);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("unexpected end");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("',' or '}' expected at " + i);
            }
        }

        private static List<object> ReadArray(string s, ref int i, int depth)
        {
            List<object> list = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ReadValue(s, ref i, depth + 1));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("unexpected end");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("',' or ']' expected at " + i);
            }
        }

        private static string ReadString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++;
            while (true)
            {
                if (i >= s.Length) throw new FormatException("unterminated string");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("bad escape");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        int code;
                        if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw new FormatException("bad \\u escape");
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: throw new FormatException("bad escape \\" + e);
                }
            }
        }

        private static object ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            if (i == start) throw new FormatException("value expected at " + i);
            double d;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("bad number at " + start);
            return d;
        }

        /// <summary>JSON 문자열 리터럴(따옴표 포함)로 이스케이프한다.</summary>
        public static string Quote(string text)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            string t = text ?? string.Empty;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
