using System;
using System.Collections.Generic;

namespace MorpheHouse.Dialogue
{
    /// <summary>
    /// .yarn 텍스트 파서(Yarn 호환 부분집합). 문법 표는 docs/07_Dialogue_Spec.md 참고.
    /// 오류는 줄 번호가 붙은 DialogueParseException 으로 던진다.
    /// </summary>
    public static class YarnParser
    {
        private sealed class SrcLine
        {
            public int No;
            public int Indent;
            public string Text;
            public int ContentEnd;
        }

        /// <summary>텍스트에서 모든 노드를 파싱한다.</summary>
        public static List<DialogueNode> Parse(string text, string sourceName)
        {
            string src = sourceName ?? string.Empty;
            string all = (text ?? string.Empty).Replace("\r\n", "\n");
            if (all.Length > 0 && all[0] == '﻿') all = all.Substring(1);
            string[] raw = all.Split('\n');
            List<DialogueNode> nodes = new List<DialogueNode>();
            int i = 0;
            while (i < raw.Length)
            {
                if (Content(raw[i]).Length == 0) { i++; continue; }

                DialogueNode node = new DialogueNode();
                node.SourceName = src;
                node.LineNumber = i + 1;
                bool hasTitle = false;
                // 헤더
                while (true)
                {
                    if (i >= raw.Length) throw new DialogueParseException(src, node.LineNumber, "노드 헤더 뒤에 '---' 가 없습니다.");
                    string c = Content(raw[i]);
                    if (c.Length == 0) { i++; continue; }
                    if (c == "---") break;
                    if (c == "===") throw new DialogueParseException(src, i + 1, "'---' 없이 '===' 가 나왔습니다.");
                    int colon = c.IndexOf(':');
                    if (colon <= 0) throw new DialogueParseException(src, i + 1, "노드 헤더는 'key: value' 형식이어야 합니다: " + c);
                    string key = c.Substring(0, colon).Trim();
                    string value = c.Substring(colon + 1).Trim();
                    if (key == "title")
                    {
                        if (value.Length == 0) throw new DialogueParseException(src, i + 1, "title 이 비어 있습니다.");
                        node.Title = value;
                        hasTitle = true;
                    }
                    else
                    {
                        node.Headers[key] = value;
                        if (key == "tags")
                        {
                            string[] parts = value.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            node.Tags.AddRange(parts);
                        }
                    }
                    i++;
                }
                if (!hasTitle) throw new DialogueParseException(src, node.LineNumber, "노드에 'title:' 헤더가 없습니다.");
                i++; // ---

                // 본문
                List<SrcLine> body = new List<SrcLine>();
                bool closed = false;
                for (; i < raw.Length; i++)
                {
                    string line = raw[i];
                    int commentAt = FindComment(line);
                    string content = commentAt >= 0 ? line.Substring(0, commentAt) : line;
                    string trimmed = content.Trim();
                    if (trimmed == "===") { closed = true; i++; break; }
                    if (trimmed.Length == 0) continue;
                    SrcLine sl = new SrcLine();
                    sl.No = i + 1;
                    sl.Indent = IndentOf(content);
                    sl.Text = trimmed;
                    sl.ContentEnd = content.TrimEnd().Length;
                    body.Add(sl);
                }
                if (!closed) throw new DialogueParseException(src, node.LineNumber, "노드 '" + node.Title + "' 가 '===' 로 끝나지 않았습니다.");

                BodyParser bp = new BodyParser(src, body);
                node.Body = bp.ParseNodeBody();
                nodes.Add(node);
            }
            return nodes;
        }

        private static string Content(string line)
        {
            int c = FindComment(line);
            return (c >= 0 ? line.Substring(0, c) : line).Trim();
        }

        private static int IndentOf(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == ' ') n++;
                else if (s[i] == '\t') n += 4;
                else break;
            }
            return n;
        }

        /// <summary>줄에서 `//` 주석 시작 위치(없으면 -1). `{}`·`&lt;&lt;&gt;&gt;`·따옴표 안과 공백 없이 붙은 `//`(URL 등)는 제외.</summary>
        internal static int FindComment(string s)
        {
            int brace = 0;
            bool inCmd = false;
            bool inQuote = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                char next = i + 1 < s.Length ? s[i + 1] : '\0';
                if (inQuote)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inQuote = false;
                    continue;
                }
                if ((brace > 0 || inCmd) && c == '"') { inQuote = true; continue; }
                if (c == '\\' && (next == '{' || next == '}')) { i++; continue; }
                if (c == '{') brace++;
                else if (c == '}') { if (brace > 0) brace--; }
                else if (c == '<' && next == '<') { inCmd = true; i++; }
                else if (c == '>' && next == '>' && inCmd) { inCmd = false; i++; }
                else if (c == '/' && next == '/' && brace == 0 && !inCmd && (i == 0 || char.IsWhiteSpace(s[i - 1]))) return i;
            }
            return -1;
        }

        private sealed class BodyParser
        {
            private readonly string _src;
            private readonly List<SrcLine> _lines;
            private int _pos;
            private int _depth;

            /// <summary>블록(선택지·if) 최대 중첩 깊이. 넘으면 줄 번호가 붙은 DialogueParseException.</summary>
            internal const int MaxNesting = 64;
            /// <summary>대사·선택지 한 줄에 붙일 수 있는 #태그 최대 개수.</summary>
            internal const int MaxTagsPerLine = 32;

            public BodyParser(string src, List<SrcLine> lines)
            {
                _src = src;
                _lines = lines;
            }

            private DialogueParseException Err(int line, string msg)
            {
                return new DialogueParseException(_src, line, msg);
            }

            public List<Statement> ParseNodeBody()
            {
                List<Statement> body = ParseBlock(0);
                if (_pos < _lines.Count)
                {
                    SrcLine l = _lines[_pos];
                    throw Err(l.No, "짝이 없는 " + l.Text);
                }
                return body;
            }

            private static bool TryCommandName(string text, out string name)
            {
                name = null;
                if (!text.StartsWith("<<") || !text.EndsWith(">>") || text.Length < 4) return false;
                string inner = text.Substring(2, text.Length - 4).Trim();
                int sp = 0;
                while (sp < inner.Length && !char.IsWhiteSpace(inner[sp])) sp++;
                name = inner.Substring(0, sp);
                return true;
            }

            private static bool IsTerminator(SrcLine l)
            {
                string name;
                if (!TryCommandName(l.Text, out name)) return false;
                return name == "elseif" || name == "else" || name == "endif";
            }

            private List<Statement> ParseBlock(int baseIndent)
            {
                if (++_depth > MaxNesting)
                    throw Err(_pos < _lines.Count ? _lines[_pos].No : 0, "블록 중첩이 너무 깊습니다(최대 " + MaxNesting + "단계).");
                List<Statement> list = new List<Statement>();
                while (_pos < _lines.Count)
                {
                    SrcLine l = _lines[_pos];
                    if (l.Indent < baseIndent) break;
                    if (IsTerminator(l)) break;
                    Statement s = ParseStatement(l);
                    if (s != null) list.Add(s);
                }
                _depth--;
                return list;
            }

            private Statement ParseStatement(SrcLine l)
            {
                if (l.Text.StartsWith("->")) return ParseOptions(l.Indent);
                if (l.Text.StartsWith("<<")) return ParseCommand();
                return ParseLine();
            }

            // ---------- 대사 ----------

            private static bool TrySplitSpeaker(string t, out string speaker, out string rest)
            {
                speaker = null;
                rest = null;
                if (t.Length == 0 || !(char.IsLetter(t[0]) || t[0] == '_')) return false;
                int i = 0;
                while (i < t.Length && (char.IsLetterOrDigit(t[i]) || t[i] == '_')) i++;
                if (i >= t.Length || t[i] != ':') return false;
                if (i + 1 < t.Length && !char.IsWhiteSpace(t[i + 1])) return false;
                speaker = t.Substring(0, i);
                rest = t.Substring(i + 1).Trim();
                return true;
            }

            /// <summary>줄 끝의 ` #tag` 들을 떼어 낸다. 남은 텍스트를 반환.</summary>
            private string StripTags(string text, List<string> tags, int lineNo)
            {
                List<string> found = new List<string>();
                string t = text;
                while (true)
                {
                    int sp = t.LastIndexOfAny(new char[] { ' ', '\t' });
                    string token = sp >= 0 ? t.Substring(sp + 1) : t;
                    if (token.Length < 2 || token[0] != '#') break;
                    string before = sp >= 0 ? t.Substring(0, sp) : string.Empty;
                    if (!BracesBalanced(before)) break;
                    found.Add(token.Substring(1));
                    if (found.Count > MaxTagsPerLine) throw Err(lineNo, "한 줄에 #태그가 너무 많습니다(최대 " + MaxTagsPerLine + "개).");
                    t = before.TrimEnd();
                    if (sp < 0) break;
                }
                for (int i = found.Count - 1; i >= 0; i--) tags.Add(found[i]);
                return t;
            }

            private static bool BracesBalanced(string s)
            {
                int depth = 0;
                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] == '\\') { i++; continue; }
                    if (s[i] == '{') depth++;
                    else if (s[i] == '}') depth--;
                }
                return depth == 0;
            }

            private static string FindLineId(List<string> tags)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    if (tags[i].StartsWith("line:") && tags[i].Length > 5) return tags[i].Substring(5);
                }
                return null;
            }

            private TextTemplate MakeTemplate(string text, int lineNo)
            {
                try
                {
                    return TextTemplate.Parse(text);
                }
                catch (DialogueParseException ex)
                {
                    throw Err(lineNo, ex.RawMessage);
                }
            }

            private DialogueExpression MakeExpression(string text, int lineNo)
            {
                try
                {
                    return DialogueExpression.Parse(text);
                }
                catch (DialogueParseException ex)
                {
                    throw Err(lineNo, ex.RawMessage);
                }
            }

            private Statement ParseLine()
            {
                SrcLine l = _lines[_pos++];
                LineStatement s = new LineStatement();
                s.LineNumber = l.No;
                s.ContentEnd = l.ContentEnd;
                string body = StripTags(l.Text, s.Tags, l.No);
                s.LineId = FindLineId(s.Tags);
                string speaker;
                string rest;
                if (TrySplitSpeaker(body, out speaker, out rest))
                {
                    s.Speaker = speaker;
                    body = rest;
                }
                if (body.Length == 0) throw Err(l.No, "대사 내용이 비어 있습니다: " + l.Text);
                s.Text = MakeTemplate(body, l.No);
                return s;
            }

            // ---------- 선택지 ----------

            private Statement ParseOptions(int indent)
            {
                OptionsStatement os = new OptionsStatement();
                os.LineNumber = _lines[_pos].No;
                while (_pos < _lines.Count && _lines[_pos].Indent == indent && _lines[_pos].Text.StartsWith("->"))
                {
                    SrcLine l = _lines[_pos++];
                    OptionItem item = new OptionItem();
                    item.LineNumber = l.No;
                    item.ContentEnd = l.ContentEnd;
                    string text = l.Text.Substring(2).Trim();
                    // 태그와 <<if>> 접미사는 어느 순서로 와도 받아들인다.
                    string condText = null;
                    for (int guard = 0; guard < 4; guard++)
                    {
                        text = StripTags(text, item.Tags, l.No);
                        int ifAt = text.LastIndexOf("<<if ", StringComparison.Ordinal);
                        if (ifAt >= 0 && text.EndsWith(">>") && condText == null && BracesBalanced(text.Substring(0, ifAt)))
                        {
                            condText = text.Substring(ifAt + 5, text.Length - ifAt - 7).Trim();
                            text = text.Substring(0, ifAt).TrimEnd();
                            continue;
                        }
                        break;
                    }
                    item.LineId = FindLineId(item.Tags);
                    if (text.Length == 0) throw Err(l.No, "선택지 텍스트가 비어 있습니다.");
                    item.Text = MakeTemplate(text, l.No);
                    if (condText != null)
                    {
                        if (condText.Length == 0) throw Err(l.No, "선택지의 <<if>> 조건이 비어 있습니다.");
                        item.Condition = MakeExpression(condText, l.No);
                    }
                    item.Body = ParseBlock(indent + 1);
                    os.Items.Add(item);
                }
                return os;
            }

            // ---------- 커맨드 ----------

            private Statement ParseCommand()
            {
                SrcLine l = _lines[_pos];
                string name;
                if (!TryCommandName(l.Text, out name))
                    throw Err(l.No, "커맨드가 '>>' 로 끝나지 않습니다(또는 대사 끝 태그 앞에 닫히지 않은 '<<'): " + l.Text);
                string inner = l.Text.Substring(2, l.Text.Length - 4).Trim();
                string rest = inner.Substring(name.Length).Trim();
                if (name.Length == 0) throw Err(l.No, "빈 커맨드 '<<>>' 입니다.");
                switch (name)
                {
                    case "set":
                        _pos++;
                        return ParseSet(l, rest);
                    case "declare":
                        _pos++;
                        return null; // 선언은 무시(기본값 0)
                    case "if":
                        return ParseIf();
                    case "jump":
                        _pos++;
                        if (rest.Length == 0 || rest.IndexOfAny(new char[] { ' ', '\t', '{', '$' }) >= 0)
                            throw Err(l.No, "<<jump 노드이름>> 형식이어야 합니다(변수·표현식 점프는 미지원): " + l.Text);
                        JumpStatement j = new JumpStatement();
                        j.LineNumber = l.No;
                        j.Target = rest;
                        return j;
                    case "stop":
                        _pos++;
                        StopStatement st = new StopStatement();
                        st.LineNumber = l.No;
                        return st;
                    case "once":
                    case "endonce":
                    case "enum":
                    case "case":
                    case "endenum":
                    case "detour":
                    case "return":
                        throw Err(l.No, "지원하지 않는 Yarn 구문입니다: <<" + name + ">>");
                    default:
                        _pos++;
                        return ParseCustom(l, name, rest);
                }
            }

            private Statement ParseSet(SrcLine l, string rest)
            {
                if (rest.Length == 0 || rest[0] != '$') throw Err(l.No, "<<set $변수 to 값>> 형식이어야 합니다: " + l.Text);
                int i = 1;
                while (i < rest.Length && (char.IsLetterOrDigit(rest[i]) || rest[i] == '_')) i++;
                if (i == 1) throw Err(l.No, "'$' 뒤에 변수 이름이 필요합니다: " + l.Text);
                string var = rest.Substring(0, i);
                string tail = rest.Substring(i).TrimStart();
                string op;
                string exprText;
                if (tail.StartsWith("to") && tail.Length > 2 && char.IsWhiteSpace(tail[2]))
                {
                    op = "=";
                    exprText = tail.Substring(2);
                }
                else if (tail.StartsWith("+=") || tail.StartsWith("-=") || tail.StartsWith("*=") || tail.StartsWith("/="))
                {
                    op = tail.Substring(0, 2);
                    exprText = tail.Substring(2);
                }
                else if (tail.StartsWith("=") && !tail.StartsWith("=="))
                {
                    op = "=";
                    exprText = tail.Substring(1);
                }
                else throw Err(l.No, "<<set " + var + " ...>> 에는 'to' 또는 '=' 가 필요합니다: " + l.Text);
                SetStatement s = new SetStatement();
                s.LineNumber = l.No;
                s.Variable = var;
                s.Operator = op;
                s.Value = MakeExpression(exprText.Trim(), l.No);
                return s;
            }

            private Statement ParseIf()
            {
                SrcLine first = _lines[_pos];
                IfStatement ifs = new IfStatement();
                ifs.LineNumber = first.No;
                SrcLine head = first;
                string headName = "if";
                while (true)
                {
                    _pos++;
                    IfBranch b = new IfBranch();
                    b.LineNumber = head.No;
                    string inner = head.Text.Substring(2, head.Text.Length - 4).Trim();
                    string cond = inner.Substring(headName.Length).Trim();
                    if (headName == "else")
                    {
                        if (cond.Length != 0) throw Err(head.No, "<<else>> 에는 조건을 쓸 수 없습니다(<<elseif>> 를 쓰세요).");
                    }
                    else
                    {
                        if (cond.Length == 0) throw Err(head.No, "<<" + headName + ">> 에 조건이 없습니다.");
                        b.Condition = MakeExpression(cond, head.No);
                    }
                    int baseIndent = 0; // if 본문은 들여쓰기와 무관하게 <<elseif>>/<<else>>/<<endif>> 까지
                    b.Body = ParseBlock(baseIndent);
                    ifs.Branches.Add(b);
                    if (_pos >= _lines.Count || !IsTerminator(_lines[_pos]))
                        throw Err(first.No, "<<if>> 에 대응하는 <<endif>> 가 없습니다.");
                    SrcLine t = _lines[_pos];
                    string tname;
                    TryCommandName(t.Text, out tname);
                    if (tname == "endif")
                    {
                        _pos++;
                        break;
                    }
                    if (headName == "else") throw Err(t.No, "<<else>> 뒤에는 <<endif>> 만 올 수 있습니다: " + t.Text);
                    head = t;
                    headName = tname;
                }
                return ifs;
            }

            private Statement ParseCustom(SrcLine l, string name, string rest)
            {
                CommandStatement c = new CommandStatement();
                c.LineNumber = l.No;
                c.Name = name;
                int i = 0;
                while (i < rest.Length)
                {
                    if (char.IsWhiteSpace(rest[i])) { i++; continue; }
                    string tokenText;
                    if (rest[i] == '"')
                    {
                        int j = i + 1;
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        bool closed = false;
                        while (j < rest.Length)
                        {
                            if (rest[j] == '\\' && j + 1 < rest.Length && rest[j + 1] == '"') { sb.Append('"'); j += 2; continue; }
                            if (rest[j] == '"') { closed = true; j++; break; }
                            sb.Append(rest[j]);
                            j++;
                        }
                        if (!closed) throw Err(l.No, "커맨드 인자의 따옴표가 닫히지 않았습니다: " + l.Text);
                        tokenText = sb.ToString();
                        i = j;
                    }
                    else
                    {
                        int j = i;
                        int depth = 0;
                        while (j < rest.Length && (depth > 0 || !char.IsWhiteSpace(rest[j])))
                        {
                            if (rest[j] == '{') depth++;
                            else if (rest[j] == '}' && depth > 0) depth--;
                            j++;
                        }
                        if (depth > 0) throw Err(l.No, "커맨드 인자의 '{' 가 닫히지 않았습니다: " + l.Text);
                        tokenText = rest.Substring(i, j - i);
                        i = j;
                    }
                    c.Args.Add(MakeTemplate(tokenText, l.No));
                }
                return c;
            }
        }
    }
}
