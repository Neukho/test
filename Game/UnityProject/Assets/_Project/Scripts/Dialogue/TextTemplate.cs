using System.Collections.Generic;
using System.Text;

namespace MorpheHouse.Dialogue
{
    /// <summary>
    /// `{expr}` 인라인 표현식이 들어 있는 텍스트를 미리 파싱한 것. `\{`, `\}` 로 중괄호를 그대로 쓸 수 있다.
    /// `{name}` 처럼 '$' 도 '(' 도 없는 식별자는 인자 없는 함수 호출(name())로 평가된다.
    /// </summary>
    public sealed class TextTemplate
    {
        private readonly List<string> _literals = new List<string>();
        private readonly List<DialogueExpression> _exprs = new List<DialogueExpression>();

        /// <summary>원본 텍스트.</summary>
        public string Source { get; private set; }

        /// <summary>표현식이 하나도 없으면 true.</summary>
        public bool IsLiteral { get { return _exprs.Count == 0; } }

        private TextTemplate(string source)
        {
            Source = source;
        }

        /// <summary>텍스트를 파싱한다. 닫히지 않은 중괄호·잘못된 표현식은 DialogueParseException(줄 번호 0).</summary>
        public static TextTemplate Parse(string text)
        {
            string src = text ?? string.Empty;
            TextTemplate t = new TextTemplate(src);
            StringBuilder lit = new StringBuilder();
            int i = 0;
            while (i < src.Length)
            {
                char c = src[i];
                if (c == '\\' && i + 1 < src.Length && (src[i + 1] == '{' || src[i + 1] == '}'))
                {
                    lit.Append(src[i + 1]);
                    i += 2;
                    continue;
                }
                if (c != '{')
                {
                    lit.Append(c);
                    i++;
                    continue;
                }
                int end = FindClose(src, i);
                if (end < 0) throw new DialogueParseException(null, 0, "'{' 가 닫히지 않았습니다: " + src);
                string inner = src.Substring(i + 1, end - i - 1).Trim();
                if (inner.Length == 0) throw new DialogueParseException(null, 0, "빈 '{}' 표현식입니다: " + src);
                DialogueExpression e;
                try
                {
                    e = DialogueExpression.Parse(inner);
                }
                catch (DialogueParseException ex)
                {
                    throw new DialogueParseException(null, 0, "{" + inner + "}: " + ex.RawMessage);
                }
                t._literals.Add(lit.ToString());
                lit.Length = 0;
                t._exprs.Add(e);
                i = end + 1;
            }
            t._literals.Add(lit.ToString());
            return t;
        }

        private static int FindClose(string s, int open)
        {
            int depth = 0;
            bool inQuote = false;
            for (int i = open; i < s.Length; i++)
            {
                char c = s[i];
                if (inQuote)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inQuote = false;
                    continue;
                }
                if (c == '"') inQuote = true;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        /// <summary>표현식을 계산해 최종 문자열을 만든다.</summary>
        public string Render(IExpressionContext ctx)
        {
            if (_exprs.Count == 0) return _literals[0];
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _exprs.Count; i++)
            {
                sb.Append(_literals[i]);
                sb.Append(_exprs[i].Evaluate(ctx).AsString);
            }
            sb.Append(_literals[_exprs.Count]);
            return sb.ToString();
        }
    }
}
