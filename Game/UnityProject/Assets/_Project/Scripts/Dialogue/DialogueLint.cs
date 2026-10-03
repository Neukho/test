using System;
using System.Collections.Generic;
using System.Text;

namespace MorpheHouse.Dialogue
{
    /// <summary>린트 심각도.</summary>
    public enum LintSeverity
    {
        Warning = 0,
        Error = 1
    }

    /// <summary>린트 결과 한 건.</summary>
    public sealed class LintIssue
    {
        public LintSeverity Severity;
        /// <summary>DUP_NODE UNDEFINED_JUMP UNDEFINED_ENTRY UNREACHABLE DUP_LINE_ID MISSING_LINE_ID.</summary>
        public string Code;
        public string Message;
        public string Node;
        public string Source;
        public int Line;

        public override string ToString()
        {
            return Severity + " " + Code + " " + Source + "(" + Line + ") [" + Node + "] " + Message;
        }
    }

    /// <summary>대화 프로젝트 정적 검사.</summary>
    public static class DialogueLint
    {
        /// <summary>
        /// 검사 항목: 중복 노드 제목, 정의되지 않은 점프 대상, entryNodes 에서 도달 불가능한 노드,
        /// 중복 #line id, #line id 없는 줄/선택지. entryNodes 가 null/빈 목록이면 도달성 검사는 생략한다.
        /// </summary>
        public static List<LintIssue> Run(DialogueProject project, IList<string> entryNodes)
        {
            List<LintIssue> issues = new List<LintIssue>();

            // 중복 제목
            Dictionary<string, DialogueNode> seen = new Dictionary<string, DialogueNode>();
            for (int i = 0; i < project.AllNodes.Count; i++)
            {
                DialogueNode n = project.AllNodes[i];
                DialogueNode first;
                if (seen.TryGetValue(n.Title, out first))
                    Add(issues, LintSeverity.Error, "DUP_NODE", n, n.LineNumber,
                        "노드 제목 '" + n.Title + "' 가 중복됩니다(먼저 정의된 곳: " + first.SourceName + ":" + first.LineNumber + ").");
                else seen[n.Title] = n;
            }

            // 점프 대상 + 도달성용 간선
            Dictionary<string, List<string>> edges = new Dictionary<string, List<string>>();
            for (int i = 0; i < project.AllNodes.Count; i++)
            {
                DialogueNode node = project.AllNodes[i];
                List<string> targets;
                if (!edges.TryGetValue(node.Title, out targets))
                {
                    targets = new List<string>();
                    edges[node.Title] = targets;
                }
                DialogueNode owner = node;
                List<string> tlist = targets;
                StatementWalker.Walk(node.Body, delegate (Statement s)
                {
                    JumpStatement j = s as JumpStatement;
                    if (j == null) return;
                    tlist.Add(j.Target);
                    if (!project.Contains(j.Target))
                        Add(issues, LintSeverity.Error, "UNDEFINED_JUMP", owner, j.LineNumber,
                            "<<jump " + j.Target + ">> 의 대상 노드가 없습니다.");
                }, null);
            }

            // 무음 점프 순환: 대사/선택지/커맨드/stop 이 하나도 없는 노드들만으로 이루어진 점프 순환은
            // 실행기가 한 단계 안에서 영원히 돌 수 있다(DialogueRunner.MaxInternalSteps 가 런타임에 막지만 작성 시 알린다).
            {
                Dictionary<string, bool> silent = new Dictionary<string, bool>();
                for (int i = 0; i < project.AllNodes.Count; i++)
                {
                    DialogueNode node = project.AllNodes[i];
                    if (silent.ContainsKey(node.Title)) continue; // 중복 제목은 먼저 등록된 것이 실행됨
                    bool audible = false;
                    StatementWalker.Walk(node.Body, delegate (Statement st)
                    {
                        if (st is LineStatement || st is OptionsStatement || st is CommandStatement || st is StopStatement) audible = true;
                    }, null);
                    silent[node.Title] = !audible;
                }
                Dictionary<string, int> color = new Dictionary<string, int>(); // 1=방문 중, 2=완료
                HashSet<string> cycleReported = new HashSet<string>();
                foreach (KeyValuePair<string, bool> kv in silent)
                {
                    if (!kv.Value || color.ContainsKey(kv.Key)) continue;
                    // 반복형 DFS(깊은 점프 사슬에서도 스택을 쓰지 않음)
                    Stack<string> path = new Stack<string>();
                    Stack<int> idx = new Stack<int>();
                    path.Push(kv.Key); idx.Push(0); color[kv.Key] = 1;
                    while (path.Count > 0)
                    {
                        string cur = path.Peek();
                        List<string> outs;
                        edges.TryGetValue(cur, out outs);
                        int k = idx.Pop();
                        if (outs == null || k >= outs.Count) { color[cur] = 2; path.Pop(); continue; }
                        idx.Push(k + 1);
                        string nxt = outs[k];
                        bool nxtSilent;
                        if (!silent.TryGetValue(nxt, out nxtSilent) || !nxtSilent) continue;
                        int c;
                        if (!color.TryGetValue(nxt, out c))
                        {
                            color[nxt] = 1; path.Push(nxt); idx.Push(0);
                        }
                        else if (c == 1 && cycleReported.Add(nxt))
                        {
                            DialogueNode at = project.GetNode(nxt);
                            Add(issues, LintSeverity.Warning, "SILENT_JUMP_CYCLE", at, at.LineNumber,
                                "노드 '" + nxt + "' 를 지나는 점프 순환에 대사·선택지·커맨드·stop 이 없어 실행 시 무한 루프가 됩니다.");
                        }
                    }
                }
            }

            // 도달성
            if (entryNodes != null && entryNodes.Count > 0)
            {
                HashSet<string> reached = new HashSet<string>();
                Queue<string> queue = new Queue<string>();
                for (int i = 0; i < entryNodes.Count; i++)
                {
                    if (!project.Contains(entryNodes[i]))
                    {
                        issues.Add(MakeIssue(LintSeverity.Error, "UNDEFINED_ENTRY", null, 0, "시작 노드 '" + entryNodes[i] + "' 가 없습니다."));
                        continue;
                    }
                    if (reached.Add(entryNodes[i])) queue.Enqueue(entryNodes[i]);
                }
                while (queue.Count > 0)
                {
                    string cur = queue.Dequeue();
                    List<string> outs;
                    if (!edges.TryGetValue(cur, out outs)) continue;
                    for (int i = 0; i < outs.Count; i++)
                    {
                        if (project.Contains(outs[i]) && reached.Add(outs[i])) queue.Enqueue(outs[i]);
                    }
                }
                HashSet<string> reported = new HashSet<string>();
                for (int i = 0; i < project.AllNodes.Count; i++)
                {
                    DialogueNode n = project.AllNodes[i];
                    if (!reached.Contains(n.Title) && reported.Add(n.Title))
                        Add(issues, LintSeverity.Warning, "UNREACHABLE", n, n.LineNumber,
                            "노드 '" + n.Title + "' 는 시작 노드에서 점프로 도달할 수 없습니다(게임 코드가 직접 시작하는 노드면 entry 목록에 추가).");
                }
            }

            // 줄 id
            Dictionary<string, string> idOwner = new Dictionary<string, string>();
            for (int i = 0; i < project.AllNodes.Count; i++)
            {
                DialogueNode node = project.AllNodes[i];
                List<LineRef> refs = CollectLines(node);
                for (int k = 0; k < refs.Count; k++)
                {
                    LineRef r = refs[k];
                    if (r.LineId == null)
                    {
                        Add(issues, LintSeverity.Warning, "MISSING_LINE_ID", node, r.LineNumber,
                            (r.IsOption ? "선택지" : "대사") + "에 #line: id 가 없습니다.");
                        continue;
                    }
                    string where;
                    if (idOwner.TryGetValue(r.LineId, out where))
                        Add(issues, LintSeverity.Error, "DUP_LINE_ID", node, r.LineNumber,
                            "#line:" + r.LineId + " 가 중복됩니다(먼저 쓰인 곳: " + where + ").");
                    else idOwner[r.LineId] = node.SourceName + ":" + r.LineNumber;
                }
            }
            return issues;
        }

        /// <summary>해당 코드의 결과 개수.</summary>
        public static int Count(List<LintIssue> issues, string code)
        {
            int n = 0;
            for (int i = 0; i < issues.Count; i++) if (issues[i].Code == code) n++;
            return n;
        }

        private static void Add(List<LintIssue> list, LintSeverity sev, string code, DialogueNode node, int line, string msg)
        {
            LintIssue it = MakeIssue(sev, code, node, line, msg);
            list.Add(it);
        }

        private static LintIssue MakeIssue(LintSeverity sev, string code, DialogueNode node, int line, string msg)
        {
            LintIssue it = new LintIssue();
            it.Severity = sev;
            it.Code = code;
            it.Message = msg;
            it.Node = node == null ? string.Empty : node.Title;
            it.Source = node == null ? string.Empty : node.SourceName;
            it.Line = line;
            return it;
        }

        /// <summary>줄 id 를 가질 수 있는 항목(대사·선택지)의 참조.</summary>
        public sealed class LineRef
        {
            public bool IsOption;
            public int LineNumber;
            public string LineId;
            public int ContentEnd;
        }

        /// <summary>노드의 대사·선택지를 문서 순서로 모은다(LineIdTagger 와 공용).</summary>
        public static List<LineRef> CollectLines(DialogueNode node)
        {
            List<LineRef> list = new List<LineRef>();
            StatementWalker.Walk(node.Body, delegate (Statement s)
            {
                LineStatement ls = s as LineStatement;
                if (ls == null) return;
                LineRef r = new LineRef();
                r.LineNumber = ls.LineNumber;
                r.LineId = ls.LineId;
                r.ContentEnd = ls.ContentEnd;
                list.Add(r);
            }, delegate (OptionItem o)
            {
                LineRef r = new LineRef();
                r.IsOption = true;
                r.LineNumber = o.LineNumber;
                r.LineId = o.LineId;
                r.ContentEnd = o.ContentEnd;
                list.Add(r);
            });
            return list;
        }
    }

    /// <summary>#line: id 가 없는 대사·선택지에 안정적인 id 를 붙인 소스 텍스트를 만든다.</summary>
    public static class LineIdTagger
    {
        /// <summary>
        /// id = FNV-1a32("노드제목" + 0x1F + "노드 안 순번") 의 8자리 소문자 16진수. 순번은 노드 안 모든 대사·선택지의
        /// 문서 순서(0부터)라서 이미 id 가 있는 줄이 있어도 다른 줄의 id 는 바뀌지 않는다.
        /// 같은 id 가 이미 쓰였으면 솔트를 바꿔 다시 계산한다. 이미 모두 붙어 있으면 입력과 동일한 텍스트를 반환(멱등).
        /// </summary>
        public static string AddMissing(string text, string sourceName)
        {
            string src = text ?? string.Empty;
            bool crlf = src.Contains("\r\n");
            string bom = string.Empty;
            string norm = src.Replace("\r\n", "\n");
            if (norm.Length > 0 && norm[0] == '﻿')
            {
                bom = "﻿";
                norm = norm.Substring(1);
            }
            List<DialogueNode> nodes = YarnParser.Parse(norm, sourceName);
            string[] lines = norm.Split('\n');

            HashSet<string> used = new HashSet<string>();
            for (int i = 0; i < nodes.Count; i++)
            {
                List<DialogueLint.LineRef> refs = DialogueLint.CollectLines(nodes[i]);
                for (int k = 0; k < refs.Count; k++) if (refs[k].LineId != null) used.Add(refs[k].LineId);
            }

            bool changed = false;
            for (int i = 0; i < nodes.Count; i++)
            {
                List<DialogueLint.LineRef> refs = DialogueLint.CollectLines(nodes[i]);
                for (int k = 0; k < refs.Count; k++)
                {
                    DialogueLint.LineRef r = refs[k];
                    if (r.LineId != null) continue;
                    string id = null;
                    for (int salt = 0; id == null || used.Contains(id); salt++)
                    {
                        id = Hash(nodes[i].Title + "\u001F" + k + (salt == 0 ? string.Empty : "\u001F" + salt));
                    }
                    used.Add(id);
                    int idx = r.LineNumber - 1;
                    string line = lines[idx];
                    lines[idx] = line.Substring(0, r.ContentEnd) + " #line:" + id + line.Substring(r.ContentEnd);
                    changed = true;
                }
            }
            if (!changed) return src;
            string joined = string.Join("\n", lines);
            if (crlf) joined = joined.Replace("\n", "\r\n");
            return bom + joined;
        }

        /// <summary>FNV-1a 32비트 해시를 8자리 16진수로.</summary>
        public static string Hash(string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            uint h = 2166136261u;
            for (int i = 0; i < bytes.Length; i++)
            {
                h ^= bytes[i];
                h *= 16777619u;
            }
            return h.ToString("x8");
        }
    }
}
