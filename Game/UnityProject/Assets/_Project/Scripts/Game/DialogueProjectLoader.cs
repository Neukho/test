using System;
using System.Collections.Generic;
using System.Text;
using MorpheHouse.Dialogue;

namespace MorpheHouse.GameFlow
{
    /// <summary>스크립트 로드 결과. 파싱에 실패한 파일은 Errors 에만 남고 나머지는 정상 로드된다.</summary>
    public sealed class DialogueLoadResult
    {
        public DialogueProject Project = new DialogueProject();
        public List<string> Errors = new List<string>();
        public int FileCount;
        public int NodeCount;
    }

    /// <summary>
    /// .yarn 텍스트 모음 → DialogueProject, Resources 용 _index.txt 형식, 폴더 구분을 평탄화한 이름 규칙.
    /// Unity Resources 는 중첩 폴더 열거가 불안정하므로 파일 이름 목록을 _index.txt 에 적어 둔다.
    /// </summary>
    public static class DialogueProjectLoader
    {
        /// <summary>Resources/Dialogue 안의 인덱스 파일 이름(확장자 없음).</summary>
        public const string IndexName = "_index";

        /// <summary>폴더 구분자를 대체하는 문자열(routes/howl/x.yarn → routes__howl__x).</summary>
        public const string FolderSeparator = "__";

        /// <summary>(소스 이름, 텍스트) 쌍들을 합쳐 프로젝트를 만든다. 한 파일의 구문 오류가 다른 파일을 막지 않는다.</summary>
        public static DialogueLoadResult Build(IList<KeyValuePair<string, string>> sources)
        {
            DialogueLoadResult result = new DialogueLoadResult();
            if (sources == null) return result;
            Dictionary<string, string> firstSource = new Dictionary<string, string>();
            for (int i = 0; i < sources.Count; i++)
            {
                string name = sources[i].Key;
                int before = result.Project.AllNodes.Count;
                try
                {
                    result.Project.AddSource(sources[i].Value ?? string.Empty, name);
                    result.FileCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add("스크립트 로드 실패 " + name + ": " + ex.Message);
                    continue;
                }
                for (int n = before; n < result.Project.AllNodes.Count; n++)
                {
                    DialogueNode node = result.Project.AllNodes[n];
                    string prev;
                    if (firstSource.TryGetValue(node.Title, out prev))
                        result.Errors.Add("중복 노드 '" + node.Title + "' (" + prev + " / " + name + ") — 먼저 로드된 것이 사용됩니다.");
                    else
                        firstSource[node.Title] = name;
                }
            }
            result.NodeCount = result.Project.AllNodes.Count;
            return result;
        }

        /// <summary>_index.txt 파싱: 한 줄에 이름 하나, 빈 줄과 '#' 로 시작하는 줄은 무시. 중복은 한 번만.</summary>
        public static List<string> ParseIndex(string text)
        {
            List<string> names = new List<string>();
            if (string.IsNullOrEmpty(text)) return names;
            HashSet<string> seen = new HashSet<string>();
            string[] lines = text.Replace("\r", string.Empty).Replace("﻿", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) line = line.Substring(0, line.Length - 4);
                if (line == IndexName) continue;
                if (seen.Add(line)) names.Add(line);
            }
            return names;
        }

        /// <summary>_index.txt 내용 생성(이름 정렬, 머리 주석 포함). 에디터 메뉴가 쓴다.</summary>
        public static string BuildIndex(IEnumerable<string> names)
        {
            List<string> sorted = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            foreach (string n in names)
            {
                if (string.IsNullOrEmpty(n) || n == IndexName) continue;
                if (seen.Add(n)) sorted.Add(n);
            }
            sorted.Sort(StringComparer.Ordinal);
            StringBuilder sb = new StringBuilder();
            sb.Append("# MorpheHouse/Dialogue/Rebuild Index 가 생성한 목록입니다. 한 줄에 Resources/Dialogue 기준 에셋 이름 하나.\n");
            for (int i = 0; i < sorted.Count; i++) sb.Append(sorted[i]).Append('\n');
            return sb.ToString();
        }

        /// <summary>상대 경로 → 평탄화한 에셋 이름. "routes/howl/howl_E1.yarn" → "routes__howl__howl_E1".</summary>
        public static string FlattenName(string relativePath)
        {
            string p = (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
            if (p.EndsWith(".yarn", StringComparison.OrdinalIgnoreCase)) p = p.Substring(0, p.Length - 5);
            return p.Replace("/", FolderSeparator);
        }
    }
}
