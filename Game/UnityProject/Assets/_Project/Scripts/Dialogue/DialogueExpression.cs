using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MorpheHouse.Dialogue
{
    /// <summary>표현식이 변수·함수를 조회하는 문맥. DialogueRunner 가 구현한다.</summary>
    public interface IExpressionContext
    {
        /// <summary>변수 값을 읽는다. name 은 '$' 를 포함한다. 정의되지 않았으면 숫자 0.</summary>
        DialogueValue GetVariable(string name);

        /// <summary>함수를 호출한다. 없으면 DialogueRuntimeException.</summary>
        DialogueValue CallFunction(string name, DialogueValue[] args);
    }

    /// <summary>이항 연산 구현(표현식·set 복합 대입 공용).</summary>
    public static class DialogueOps
    {
        public static bool AreEqual(DialogueValue a, DialogueValue b)
        {
            if (a.Kind == b.Kind)
            {
                switch (a.Kind)
                {
                    case DialogueValueKind.Number: return a.AsNumber == b.AsNumber;
                    case DialogueValueKind.String: return string.Equals(a.AsString, b.AsString, StringComparison.Ordinal);
                    default: return a.IsTruthy == b.IsTruthy;
                }
            }
            if (!a.IsString && !b.IsString) return a.AsNumber == b.AsNumber; // 숫자 <-> 불
            return false;
        }

        public static DialogueValue Apply(string op, DialogueValue a, DialogueValue b)
        {
            switch (op)
            {
                case "+":
                    if (a.IsString || b.IsString) return DialogueValue.FromString(a.AsString + b.AsString);
                    RequireNumbers(op, a, b);
                    return DialogueValue.FromNumber(a.AsNumber + b.AsNumber);
                case "-":
                    RequireNumbers(op, a, b);
                    return DialogueValue.FromNumber(a.AsNumber - b.AsNumber);
                case "*":
                    RequireNumbers(op, a, b);
                    return DialogueValue.FromNumber(a.AsNumber * b.AsNumber);
                case "/":
                    RequireNumbers(op, a, b);
                    if (b.AsNumber == 0.0) throw new DialogueRuntimeException("0 으로 나눌 수 없습니다.");
                    return DialogueValue.FromNumber(a.AsNumber / b.AsNumber);
                case "%":
                    RequireNumbers(op, a, b);
                    if (b.AsNumber == 0.0) throw new DialogueRuntimeException("0 으로 나눈 나머지는 구할 수 없습니다.");
                    return DialogueValue.FromNumber(a.AsNumber % b.AsNumber);
                case "==":
                    return DialogueValue.FromBool(AreEqual(a, b));
                case "!=":
                    return DialogueValue.FromBool(!AreEqual(a, b));
                case "<":
                    return DialogueValue.FromBool(Compare(op, a, b) < 0);
                case ">":
                    return DialogueValue.FromBool(Compare(op, a, b) > 0);
                case "<=":
                    return DialogueValue.FromBool(Compare(op, a, b) <= 0);
                case ">=":
                    return DialogueValue.FromBool(Compare(op, a, b) >= 0);
                default:
                    throw new DialogueRuntimeException("알 수 없는 연산자: " + op);
            }
        }

        private static void RequireNumbers(string op, DialogueValue a, DialogueValue b)
        {
            if (a.IsString || b.IsString)
                throw new DialogueRuntimeException("연산자 '" + op + "' 는 문자열에 쓸 수 없습니다.");
            if (a.IsBool || b.IsBool)
                throw new DialogueRuntimeException("연산자 '" + op + "' 는 불(bool)에 쓸 수 없습니다.");
        }

        private static int Compare(string op, DialogueValue a, DialogueValue b)
        {
            if (a.IsNumber && b.IsNumber) return a.AsNumber.CompareTo(b.AsNumber);
            if (a.IsString && b.IsString) return string.CompareOrdinal(a.AsString, b.AsString);
            throw new DialogueRuntimeException("연산자 '" + op + "' 는 같은 종류의 숫자/문자열끼리만 비교할 수 있습니다.");
        }
    }

    /// <summary>파싱된 표현식 트리. Parse 로 만들고 Evaluate 로 계산한다.</summary>
    public abstract class DialogueExpression
    {
        /// <summary>원본 표현식 텍스트(디버그용).</summary>
        public string Source { get; private set; }

        public abstract DialogueValue Evaluate(IExpressionContext ctx);

        /// <summary>표현식을 파싱한다. 문법 오류면 DialogueParseException(줄 번호 0).</summary>
        public static DialogueExpression Parse(string source)
        {
            if (source == null || source.Trim().Length == 0)
                throw new DialogueParseException(null, 0, "비어 있는 표현식입니다.");
            ExprParser p = new ExprParser(source);
            DialogueExpression e = p.ParseAll();
            e.Source = source;
            return e;
        }

        // ---------------- AST ----------------

        private sealed class LiteralExpr : DialogueExpression
        {
            private readonly DialogueValue _value;
            public LiteralExpr(DialogueValue v) { _value = v; }
            public override DialogueValue Evaluate(IExpressionContext ctx) { return _value; }
        }

        private sealed class VariableExpr : DialogueExpression
        {
            private readonly string _name;
            public VariableExpr(string name) { _name = name; }
            public override DialogueValue Evaluate(IExpressionContext ctx) { return ctx.GetVariable(_name); }
        }

        private sealed class CallExpr : DialogueExpression
        {
            private readonly string _name;
            private readonly List<DialogueExpression> _args;
            public CallExpr(string name, List<DialogueExpression> args) { _name = name; _args = args; }

            public override DialogueValue Evaluate(IExpressionContext ctx)
            {
                DialogueValue[] values = new DialogueValue[_args.Count];
                for (int i = 0; i < values.Length; i++) values[i] = _args[i].Evaluate(ctx);
                return ctx.CallFunction(_name, values);
            }
        }

        private sealed class UnaryExpr : DialogueExpression
        {
            private readonly string _op;
            private readonly DialogueExpression _inner;
            public UnaryExpr(string op, DialogueExpression inner) { _op = op; _inner = inner; }

            public override DialogueValue Evaluate(IExpressionContext ctx)
            {
                DialogueValue v = _inner.Evaluate(ctx);
                if (_op == "!") return DialogueValue.FromBool(!v.IsTruthy);
                if (v.IsString) throw new DialogueRuntimeException("단항 '-' 는 문자열에 쓸 수 없습니다.");
                return DialogueValue.FromNumber(-v.AsNumber);
            }
        }

        private sealed class BinaryExpr : DialogueExpression
        {
            private readonly string _op;
            private readonly DialogueExpression _left;
            private readonly DialogueExpression _right;
            public BinaryExpr(string op, DialogueExpression l, DialogueExpression r) { _op = op; _left = l; _right = r; }

            public override DialogueValue Evaluate(IExpressionContext ctx)
            {
                DialogueValue a = _left.Evaluate(ctx);
                if (_op == "&&")
                {
                    if (!a.IsTruthy) return DialogueValue.FromBool(false);
                    return DialogueValue.FromBool(_right.Evaluate(ctx).IsTruthy);
                }
                if (_op == "||")
                {
                    if (a.IsTruthy) return DialogueValue.FromBool(true);
                    return DialogueValue.FromBool(_right.Evaluate(ctx).IsTruthy);
                }
                return DialogueOps.Apply(_op, a, _right.Evaluate(ctx));
            }
        }

        // ---------------- 토크나이저 / 파서 ----------------

        private enum TokKind { Number, String, Variable, Ident, Op, LParen, RParen, Comma, End }

        private struct Token
        {
            public TokKind Kind;
            public string Text;
            public double Number;
            public int Pos;
        }

        private sealed class ExprParser
        {
            private readonly string _src;
            private readonly List<Token> _tokens = new List<Token>();
            private int _pos;
            private int _depth;

            /// <summary>중첩(괄호·함수 인자·단항 연산자) 최대 깊이. 넘으면 DialogueParseException(스택 오버플로 방지).</summary>
            internal const int MaxDepth = 64;
            /// <summary>표현식 하나의 최대 토큰 수(좌결합 이항식 트리의 평가 재귀 깊이도 함께 제한).</summary>
            internal const int MaxTokens = 512;

            public ExprParser(string src)
            {
                _src = src;
                Tokenize();
            }

            private static DialogueParseException Err(string msg)
            {
                return new DialogueParseException(null, 0, msg);
            }

            private void Add(TokKind kind, string text, int pos)
            {
                Token t = new Token();
                t.Kind = kind;
                t.Text = text;
                t.Pos = pos;
                _tokens.Add(t);
            }

            private static bool IsIdentChar(char c)
            {
                return char.IsLetterOrDigit(c) || c == '_';
            }

            private void Tokenize()
            {
                int i = 0;
                while (i < _src.Length)
                {
                    char c = _src[i];
                    if (char.IsWhiteSpace(c)) { i++; continue; }
                    int start = i;
                    if (c >= '0' && c <= '9')
                    {
                        while (i < _src.Length && _src[i] >= '0' && _src[i] <= '9') i++;
                        if (i + 1 < _src.Length && _src[i] == '.' && _src[i + 1] >= '0' && _src[i + 1] <= '9')
                        {
                            i++;
                            while (i < _src.Length && _src[i] >= '0' && _src[i] <= '9') i++;
                        }
                        Token t = new Token();
                        t.Kind = TokKind.Number;
                        t.Text = _src.Substring(start, i - start);
                        double num;
                        if (!double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out num) || double.IsInfinity(num) || double.IsNaN(num))
                            throw Err("숫자가 너무 큽니다: " + (t.Text.Length > 40 ? t.Text.Substring(0, 40) + "..." : t.Text));
                        t.Number = num;
                        t.Pos = start;
                        _tokens.Add(t);
                        continue;
                    }
                    if (c == '"')
                    {
                        StringBuilder sb = new StringBuilder();
                        i++;
                        bool closed = false;
                        while (i < _src.Length)
                        {
                            char d = _src[i];
                            if (d == '\\' && i + 1 < _src.Length)
                            {
                                char n = _src[i + 1];
                                if (n == 'n') sb.Append('\n');
                                else sb.Append(n);
                                i += 2;
                                continue;
                            }
                            if (d == '"') { closed = true; i++; break; }
                            sb.Append(d);
                            i++;
                        }
                        if (!closed) throw Err("문자열이 닫히지 않았습니다: " + _src);
                        Add(TokKind.String, sb.ToString(), start);
                        continue;
                    }
                    if (c == '$')
                    {
                        i++;
                        while (i < _src.Length && IsIdentChar(_src[i])) i++;
                        if (i - start < 2) throw Err("'$' 뒤에 변수 이름이 필요합니다: " + _src);
                        Add(TokKind.Variable, _src.Substring(start, i - start), start);
                        continue;
                    }
                    if (char.IsLetter(c) || c == '_')
                    {
                        while (i < _src.Length && IsIdentChar(_src[i])) i++;
                        string word = _src.Substring(start, i - start);
                        string op = WordOperator(word);
                        if (op != null) Add(TokKind.Op, op, start);
                        else Add(TokKind.Ident, word, start);
                        continue;
                    }
                    if (c == '(') { Add(TokKind.LParen, "(", i); i++; continue; }
                    if (c == ')') { Add(TokKind.RParen, ")", i); i++; continue; }
                    if (c == ',') { Add(TokKind.Comma, ",", i); i++; continue; }
                    string two = i + 1 < _src.Length ? _src.Substring(i, 2) : string.Empty;
                    if (two == "==" || two == "!=" || two == ">=" || two == "<=" || two == "&&" || two == "||")
                    {
                        Add(TokKind.Op, two, i);
                        i += 2;
                        continue;
                    }
                    if (c == '+' || c == '-' || c == '*' || c == '/' || c == '%' || c == '<' || c == '>' || c == '!')
                    {
                        Add(TokKind.Op, c.ToString(), i);
                        i++;
                        continue;
                    }
                    if (c == '=') throw Err("'=' 는 표현식에서 쓸 수 없습니다. 비교는 '==' 또는 'eq' 를 쓰세요: " + _src);
                    throw Err("표현식에 알 수 없는 문자 '" + c + "' 가 있습니다: " + _src);
                }
                Add(TokKind.End, string.Empty, _src.Length);
                if (_tokens.Count > MaxTokens) throw Err("표현식이 너무 깁니다(토큰 " + MaxTokens + "개 초과)");
            }

            private static string WordOperator(string w)
            {
                switch (w)
                {
                    case "and": return "&&";
                    case "or": return "||";
                    case "not": return "!";
                    case "eq": return "==";
                    case "is": return "==";
                    case "neq": return "!=";
                    case "gt": return ">";
                    case "lt": return "<";
                    case "gte": return ">=";
                    case "lte": return "<=";
                    default: return null;
                }
            }

            private Token Peek { get { return _tokens[_pos]; } }

            private bool PeekOp(string op)
            {
                Token t = _tokens[_pos];
                return t.Kind == TokKind.Op && t.Text == op;
            }

            public DialogueExpression ParseAll()
            {
                DialogueExpression e = ParseOr();
                if (Peek.Kind != TokKind.End)
                    throw Err("예상치 못한 토큰 '" + Peek.Text + "' (표현식: " + _src + ")");
                return e;
            }

            private DialogueExpression ParseOr()
            {
                DialogueExpression left = ParseAnd();
                while (PeekOp("||")) { _pos++; left = new BinaryExpr("||", left, ParseAnd()); }
                return left;
            }

            private DialogueExpression ParseAnd()
            {
                DialogueExpression left = ParseEquality();
                while (PeekOp("&&")) { _pos++; left = new BinaryExpr("&&", left, ParseEquality()); }
                return left;
            }

            private DialogueExpression ParseEquality()
            {
                DialogueExpression left = ParseComparison();
                while (PeekOp("==") || PeekOp("!="))
                {
                    string op = Peek.Text;
                    _pos++;
                    left = new BinaryExpr(op, left, ParseComparison());
                }
                return left;
            }

            private DialogueExpression ParseComparison()
            {
                DialogueExpression left = ParseAdditive();
                while (PeekOp("<") || PeekOp(">") || PeekOp("<=") || PeekOp(">="))
                {
                    string op = Peek.Text;
                    _pos++;
                    left = new BinaryExpr(op, left, ParseAdditive());
                }
                return left;
            }

            private DialogueExpression ParseAdditive()
            {
                DialogueExpression left = ParseMultiplicative();
                while (PeekOp("+") || PeekOp("-"))
                {
                    string op = Peek.Text;
                    _pos++;
                    left = new BinaryExpr(op, left, ParseMultiplicative());
                }
                return left;
            }

            private DialogueExpression ParseMultiplicative()
            {
                DialogueExpression left = ParseUnary();
                while (PeekOp("*") || PeekOp("/") || PeekOp("%"))
                {
                    string op = Peek.Text;
                    _pos++;
                    left = new BinaryExpr(op, left, ParseUnary());
                }
                return left;
            }

            private DialogueExpression ParseUnary()
            {
                if (++_depth > MaxDepth) throw Err("표현식 중첩이 너무 깊습니다(최대 " + MaxDepth + "단계): " + (_src.Length > 40 ? _src.Substring(0, 40) + "..." : _src));
                try
                {
                    if (PeekOp("!")) { _pos++; return new UnaryExpr("!", ParseUnary()); }
                    if (PeekOp("-")) { _pos++; return new UnaryExpr("-", ParseUnary()); }
                    return ParsePrimary();
                }
                finally { _depth--; }
            }

            private DialogueExpression ParsePrimary()
            {
                Token t = Peek;
                switch (t.Kind)
                {
                    case TokKind.Number:
                        _pos++;
                        return new LiteralExpr(DialogueValue.FromNumber(t.Number));
                    case TokKind.String:
                        _pos++;
                        return new LiteralExpr(DialogueValue.FromString(t.Text));
                    case TokKind.Variable:
                        _pos++;
                        return new VariableExpr(t.Text);
                    case TokKind.Ident:
                        _pos++;
                        if (t.Text == "true") return new LiteralExpr(DialogueValue.FromBool(true));
                        if (t.Text == "false") return new LiteralExpr(DialogueValue.FromBool(false));
                        List<DialogueExpression> args = new List<DialogueExpression>();
                        if (Peek.Kind == TokKind.LParen)
                        {
                            _pos++;
                            if (Peek.Kind != TokKind.RParen)
                            {
                                args.Add(ParseOr());
                                while (Peek.Kind == TokKind.Comma) { _pos++; args.Add(ParseOr()); }
                            }
                            if (Peek.Kind != TokKind.RParen)
                                throw Err("함수 '" + t.Text + "' 호출에 ')' 가 필요합니다 (표현식: " + _src + ")");
                            _pos++;
                        }
                        // '(' 없는 식별자 {name} 은 인자 없는 함수 호출로 취급한다.
                        return new CallExpr(t.Text, args);
                    case TokKind.LParen:
                        _pos++;
                        DialogueExpression inner = ParseOr();
                        if (Peek.Kind != TokKind.RParen) throw Err("')' 가 필요합니다 (표현식: " + _src + ")");
                        _pos++;
                        return inner;
                    case TokKind.End:
                        throw Err("표현식이 갑자기 끝났습니다: " + _src);
                    default:
                        throw Err("예상치 못한 토큰 '" + t.Text + "' (표현식: " + _src + ")");
                }
            }
        }
    }
}
