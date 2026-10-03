using System.Collections.Generic;
using System.IO;
using System.Text;
using MorpheHouse.Dialogue;
using MorpheHouse.GameFlow;
using UnityEditor;
using UnityEngine;

namespace MorpheHouse.EditorTools
{
    /// <summary>
    /// 대화 스크립트 도구. .yarn 원본(Assets/_Project/Dialogue)을 Resources/Dialogue 의 .txt TextAsset 으로 복사하고
    /// 목록 파일(_index.txt)을 만든다. Resources 는 중첩 폴더 열거가 불안정해서 목록 파일을 쓴다.
    /// </summary>
    public static class DialogueTools
    {
        private const string SourceDir = "Assets/_Project/Dialogue";
        private const string TargetDir = "Assets/_Project/Resources/Dialogue";

        /// <summary>Dialogue/**/*.yarn 을 평탄화한 이름(폴더 구분 "__")의 .txt 로 복사하고 인덱스를 다시 만든다. 사라진 원본의 .txt 는 지운다.</summary>
        [MenuItem("MorpheHouse/Dialogue/Sync Scripts")]
        public static void SyncScripts()
        {
            if (!Directory.Exists(SourceDir))
            {
                Debug.LogError("[MorpheHouse] 소스 폴더가 없습니다: " + SourceDir);
                return;
            }
            Directory.CreateDirectory(TargetDir);

            HashSet<string> wanted = new HashSet<string>();
            int copied = 0;
            string[] files = Directory.GetFiles(SourceDir, "*.yarn", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string rel = files[i].Substring(SourceDir.Length).TrimStart('/', '\\');
                string flat = DialogueProjectLoader.FlattenName(rel);
                wanted.Add(flat);
                File.WriteAllText(Path.Combine(TargetDir, flat + ".txt"), File.ReadAllText(files[i], Encoding.UTF8), new UTF8Encoding(false));
                copied++;
            }

            int removed = 0;
            foreach (string txt in Directory.GetFiles(TargetDir, "*.txt"))
            {
                string name = Path.GetFileNameWithoutExtension(txt);
                if (name == DialogueProjectLoader.IndexName || wanted.Contains(name)) continue;
                AssetDatabase.DeleteAsset(txt.Replace('\\', '/'));
                removed++;
            }

            WriteIndex();
            AssetDatabase.Refresh();
            Debug.Log("[MorpheHouse] 스크립트 " + copied + "개 동기화, 오래된 파일 " + removed + "개 삭제.");
            ReportProblems();
        }

        /// <summary>Resources/Dialogue 의 .txt 목록으로 _index.txt 만 다시 만든다.</summary>
        [MenuItem("MorpheHouse/Dialogue/Rebuild Index")]
        public static void RebuildIndex()
        {
            int n = WriteIndex();
            AssetDatabase.Refresh();
            Debug.Log("[MorpheHouse] _index.txt 갱신: " + n + "개 항목.");
        }

        private static int WriteIndex()
        {
            Directory.CreateDirectory(TargetDir);
            List<string> names = new List<string>();
            foreach (string txt in Directory.GetFiles(TargetDir, "*.txt"))
            {
                string name = Path.GetFileNameWithoutExtension(txt);
                if (name != DialogueProjectLoader.IndexName) names.Add(name);
            }
            File.WriteAllText(Path.Combine(TargetDir, DialogueProjectLoader.IndexName + ".txt"),
                DialogueProjectLoader.BuildIndex(names), new UTF8Encoding(false));
            return names.Count;
        }

        /// <summary>동기화 직후 전체를 파싱해 구문 오류·중복 노드·린트 오류를 콘솔에 보여 준다.</summary>
        private static void ReportProblems()
        {
            List<KeyValuePair<string, string>> sources = new List<KeyValuePair<string, string>>();
            foreach (string txt in Directory.GetFiles(TargetDir, "*.txt"))
            {
                string name = Path.GetFileNameWithoutExtension(txt);
                if (name == DialogueProjectLoader.IndexName) continue;
                sources.Add(new KeyValuePair<string, string>(name, File.ReadAllText(txt, Encoding.UTF8)));
            }
            DialogueLoadResult r = DialogueProjectLoader.Build(sources);
            for (int i = 0; i < r.Errors.Count; i++) Debug.LogError("[MorpheHouse] " + r.Errors[i]);
            List<LintIssue> issues = DialogueLint.Run(r.Project, new List<string>());
            int errors = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity != LintSeverity.Error) continue;
                if (issues[i].Code == "UNDEFINED_ENTRY") continue;
                errors++;
                Debug.LogError("[MorpheHouse] 린트: " + issues[i]);
            }
            Debug.Log("[MorpheHouse] 노드 " + r.NodeCount + "개 · 로드 오류 " + r.Errors.Count + "건 · 린트 오류 " + errors + "건");
        }
    }
}
