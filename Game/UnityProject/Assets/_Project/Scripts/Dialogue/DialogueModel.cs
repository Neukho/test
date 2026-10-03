using System;
using System.Collections.Generic;

namespace MorpheHouse.Dialogue
{
    /// <summary>노드 본문의 한 문장(구문 트리의 기반 클래스).</summary>
    public abstract class Statement
    {
        /// <summary>원본 .yarn 의 줄 번호(1부터).</summary>
        public int LineNumber;
    }

    /// <summary>대사 줄. Speaker 가 null 이면 지문(narration).</summary>
    public sealed class LineStatement : Statement
    {
        public string Speaker;
        public TextTemplate Text;
        /// <summary>줄 끝 태그(# 제외). `line:xxxx` 포함.</summary>
        public List<string> Tags = new List<string>();
        /// <summary>#line:xxxx 의 xxxx. 없으면 null.</summary>
        public string LineId;
        /// <summary>원본 줄에서 내용(주석·후행 공백 제외)이 끝나는 열 위치. LineIdTagger 가 사용.</summary>
        public int ContentEnd;
    }

    /// <summary>선택지 하나(`-> 텍스트`)와 들여쓴 본문.</summary>
    public sealed class OptionItem
    {
        public int LineNumber;
        public TextTemplate Text;
        public List<string> Tags = new List<string>();
        public string LineId;
        /// <summary>`<<if cond>>` 접미사 조건. 없으면 null(항상 선택 가능).</summary>
        public DialogueExpression Condition;
        public List<Statement> Body = new List<Statement>();
        public int ContentEnd;
    }

    /// <summary>같은 들여쓰기의 연속된 선택지 묶음.</summary>
    public sealed class OptionsStatement : Statement
    {
        public List<OptionItem> Items = new List<OptionItem>();
    }

    /// <summary>`<<set $var to expr>>` (복합 대입 += -= *= /= 지원).</summary>
    public sealed class SetStatement : Statement
    {
        public string Variable;
        /// <summary>"=" "+=" "-=" "*=" "/=".</summary>
        public string Operator;
        public DialogueExpression Value;
    }

    /// <summary>if/elseif/else 의 한 분기. Condition 이 null 이면 else.</summary>
    public sealed class IfBranch
    {
        public int LineNumber;
        public DialogueExpression Condition;
        public List<Statement> Body = new List<Statement>();
    }

    /// <summary>`<<if>> ... <<endif>>` 블록.</summary>
    public sealed class IfStatement : Statement
    {
        public List<IfBranch> Branches = new List<IfBranch>();
    }

    /// <summary>`<<jump Node>>`.</summary>
    public sealed class JumpStatement : Statement
    {
        public string Target;
    }

    /// <summary>`<<stop>>`.</summary>
    public sealed class StopStatement : Statement
    {
    }

    /// <summary>호스트에 전달되는 사용자 정의 커맨드 `<<name arg arg>>`.</summary>
    public sealed class CommandStatement : Statement
    {
        public string Name;
        public List<TextTemplate> Args = new List<TextTemplate>();
    }

    /// <summary>하나의 노드(title ~ ===).</summary>
    public sealed class DialogueNode
    {
        public string Title;
        /// <summary>title 을 제외한 헤더(tags 등)의 원문.</summary>
        public Dictionary<string, string> Headers = new Dictionary<string, string>();
        /// <summary>`tags:` 헤더를 공백으로 나눈 목록.</summary>
        public List<string> Tags = new List<string>();
        public List<Statement> Body = new List<Statement>();
        public string SourceName = string.Empty;
        /// <summary>노드 첫 헤더 줄 번호.</summary>
        public int LineNumber;
    }

    /// <summary>구문 트리를 순회하는 도우미.</summary>
    public static class StatementWalker
    {
        /// <summary>문서 순서대로 모든 문장을 방문한다. 선택지는 항목 → 그 본문 순서.</summary>
        public static void Walk(List<Statement> body, Action<Statement> onStatement, Action<OptionItem> onOption)
        {
            for (int i = 0; i < body.Count; i++)
            {
                Statement s = body[i];
                if (onStatement != null) onStatement(s);
                OptionsStatement os = s as OptionsStatement;
                if (os != null)
                {
                    for (int k = 0; k < os.Items.Count; k++)
                    {
                        if (onOption != null) onOption(os.Items[k]);
                        Walk(os.Items[k].Body, onStatement, onOption);
                    }
                    continue;
                }
                IfStatement ifs = s as IfStatement;
                if (ifs != null)
                {
                    for (int k = 0; k < ifs.Branches.Count; k++) Walk(ifs.Branches[k].Body, onStatement, onOption);
                }
            }
        }
    }

    /// <summary>여러 .yarn 소스를 합친 대화 프로젝트(노드 모음).</summary>
    public sealed class DialogueProject
    {
        private readonly List<DialogueNode> _all = new List<DialogueNode>();
        private readonly Dictionary<string, DialogueNode> _byTitle = new Dictionary<string, DialogueNode>();

        /// <summary>제목이 중복된 노드까지 포함한 전체 노드(추가 순서).</summary>
        public List<DialogueNode> AllNodes { get { return _all; } }

        /// <summary>.yarn 텍스트를 파싱해 새 프로젝트를 만든다.</summary>
        public static DialogueProject Parse(string text, string sourceName)
        {
            DialogueProject p = new DialogueProject();
            p.AddSource(text, sourceName);
            return p;
        }

        /// <summary>.yarn 텍스트를 추가한다. 같은 제목이 이미 있으면 먼저 등록된 것이 실행에 쓰인다(린트가 경고).</summary>
        public void AddSource(string text, string sourceName)
        {
            List<DialogueNode> nodes = YarnParser.Parse(text, sourceName);
            for (int i = 0; i < nodes.Count; i++)
            {
                _all.Add(nodes[i]);
                if (!_byTitle.ContainsKey(nodes[i].Title)) _byTitle[nodes[i].Title] = nodes[i];
            }
        }

        public bool Contains(string title)
        {
            return title != null && _byTitle.ContainsKey(title);
        }

        public DialogueNode GetNode(string title)
        {
            DialogueNode n;
            return title != null && _byTitle.TryGetValue(title, out n) ? n : null;
        }
    }
}
