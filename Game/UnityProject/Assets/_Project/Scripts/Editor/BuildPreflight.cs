// 빌드 전 점검(에디터 전용). 이 파일은 Unity 없이 컴파일하지 못했다(UnityEditor 어셈블리 부재) — 처음 열 때 콘솔 오류부터 확인할 것.
using System.Collections.Generic;
using System.IO;
using System.Text;
using MorpheHouse.ArtPipeline;
using MorpheHouse.Data;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MorpheHouse.EditorTools
{
    public enum PreflightLevel { Pass, Warn, Fail }

    public sealed class PreflightItem
    {
        public string Name;
        public PreflightLevel Level;
        public string Detail;
    }

    /// <summary>출시 전 점검표. 메뉴 MorpheHouse/Build/Preflight Check. FAIL 이 하나라도 있으면 BuildTools 가 빌드를 중단한다.</summary>
    public static class BuildPreflight
    {
        private const string Root = "Assets/_Project/";
        private static readonly string[] BalanceFiles =
        {
            "params", "difficulty", "spots", "characters", "events", "prefs",
            "actions", "endings", "gifts", "staff_bands", "chapters", "shop_levels"
        };

        [MenuItem("MorpheHouse/Build/Preflight Check")]
        public static void RunFromMenu()
        {
            List<PreflightItem> items = Run();
            Debug.Log(Format(items));
        }

        public static int CountFail(List<PreflightItem> items)
        {
            int n = 0;
            for (int i = 0; i < items.Count; i++) if (items[i].Level == PreflightLevel.Fail) n++;
            return n;
        }

        public static List<PreflightItem> Run()
        {
            List<PreflightItem> r = new List<PreflightItem>();
            CheckBootScene(r);
            CheckBalance(r);
            CheckDialogueIndex(r);
            CheckArt(r);
            CheckMissingScripts(r);
            CheckPlayerSettings(r);
            return r;
        }

        public static string Format(List<PreflightItem> items)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[MorpheHouse] Preflight");
            int fail = 0, warn = 0;
            for (int i = 0; i < items.Count; i++)
            {
                PreflightItem it = items[i];
                string tag = it.Level == PreflightLevel.Pass ? "PASS" : it.Level == PreflightLevel.Warn ? "WARN" : "FAIL";
                if (it.Level == PreflightLevel.Fail) fail++;
                if (it.Level == PreflightLevel.Warn) warn++;
                sb.AppendLine(string.Format("  [{0}] {1} - {2}", tag, it.Name, it.Detail));
            }
            sb.AppendLine(string.Format("  RESULT: {0} (FAIL {1}, WARN {2}, total {3})", fail == 0 ? "PASS" : "FAIL", fail, warn, items.Count));
            return sb.ToString();
        }

        private static void Add(List<PreflightItem> r, string name, PreflightLevel level, string detail)
        {
            r.Add(new PreflightItem { Name = name, Level = level, Detail = detail });
        }

        private static void CheckBootScene(List<PreflightItem> r)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int boot = -1, enabled = 0;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].enabled) enabled++;
                if (scenes[i].path.EndsWith("/Boot.unity") && boot < 0) boot = i;
            }
            if (boot < 0) Add(r, "Boot scene in Build Settings", PreflightLevel.Fail, "Boot.unity 없음 - MorpheHouse/Setup/Create Boot Scene 실행");
            else if (!scenes[boot].enabled) Add(r, "Boot scene in Build Settings", PreflightLevel.Fail, "Boot 씬이 비활성");
            else if (boot != 0) Add(r, "Boot scene in Build Settings", PreflightLevel.Warn, "Boot 가 첫 번째가 아님(index " + boot + ")");
            else Add(r, "Boot scene in Build Settings", PreflightLevel.Pass, "index 0, 활성 씬 " + enabled + "개");
        }

        private static void CheckBalance(List<PreflightItem> r)
        {
            List<string> missing = new List<string>();
            List<string> empty = new List<string>();
            for (int i = 0; i < BalanceFiles.Length; i++)
            {
                string p = Root + "Resources/Balance/" + BalanceFiles[i] + ".csv";
                if (!File.Exists(p)) { missing.Add(BalanceFiles[i]); continue; }
                string[] lines = File.ReadAllLines(p);
                int data = 0;
                for (int k = 1; k < lines.Length; k++) if (lines[k].Trim().Length > 0) data++;
                if (data == 0) empty.Add(BalanceFiles[i]);
            }
            if (missing.Count > 0) Add(r, "Resources/Balance csv", PreflightLevel.Fail, "누락: " + string.Join(", ", missing.ToArray()) + " (MorpheHouse/Data/Sync Balance CSV)");
            else if (empty.Count > 0) Add(r, "Resources/Balance csv", PreflightLevel.Fail, "데이터 행 없음: " + string.Join(", ", empty.ToArray()));
            else Add(r, "Resources/Balance csv", PreflightLevel.Pass, BalanceFiles.Length + "개 모두 존재");
        }

        private static void CheckDialogueIndex(List<PreflightItem> r)
        {
            string p = Root + "Resources/Dialogue/_index.txt";
            if (!File.Exists(p)) { Add(r, "Dialogue _index.txt", PreflightLevel.Fail, "없음 - MorpheHouse/Dialogue/Sync Scripts"); return; }
            int n = 0;
            string[] lines = File.ReadAllLines(p);
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length > 0 && !t.StartsWith("#")) n++;
            }
            if (n == 0) Add(r, "Dialogue _index.txt", PreflightLevel.Fail, "항목 0개(주석뿐) - MorpheHouse/Dialogue/Sync Scripts 실행 후 커밋");
            else Add(r, "Dialogue _index.txt", PreflightLevel.Pass, n + "개 항목");
        }

        private static void CheckArt(List<PreflightItem> r)
        {
            List<ArtSpec> specs = ArtManifest.All(CharIds.All);
            int present = 0;
            for (int i = 0; i < specs.Count; i++)
                if (File.Exists(Root + "Resources/" + specs[i].ResPath + ".png")) present++;
            // 플레이스홀더는 최종 파일명으로 생성되어 파일만으로는 구분할 수 없다.
            // 그래서 docs/legal/art_log.csv 에 기록된(=실제 일러스트) 행 수를 뺀 값을 '추정 플레이스홀더'로 본다.
            int logged = CountArtLogRows();
            int placeholders = present - logged;
            if (placeholders < 0) placeholders = 0;
            string detail = string.Format("명세 {0}장, 파일 {1}장, 누락 {2}장, art_log 기록 {3}행, 추정 플레이스홀더 {4}장", specs.Count, present, specs.Count - present, logged, placeholders);
            // 파일 누락은 런타임 폴백(즉석 플레이스홀더)이 있어 FAIL 이 아니라 WARN. 출시 게이트는 docs/17 참조.
            PreflightLevel lv = (placeholders > 0 || present < specs.Count) ? PreflightLevel.Warn : PreflightLevel.Pass;
            Add(r, "Art placeholders / counts", lv, detail + (lv == PreflightLevel.Warn ? " (데모/개발 빌드는 허용, 정식 출시는 0 이어야 함)" : ""));
        }

        private static int CountArtLogRows()
        {
            string p = "../docs/legal/art_log.csv";
            if (!File.Exists(p)) return 0;
            string[] lines = File.ReadAllLines(p);
            int n = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length > 0 && !t.StartsWith("#")) n++;
            }
            return n;
        }

        private static void CheckMissingScripts(List<PreflightItem> r)
        {
            int missing = 0, scenesChecked = 0;
            List<string> where = new List<string>();
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            bool dirty = false;
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) dirty = true;
            if (dirty)
            {
                Add(r, "No missing scripts (scenes)", PreflightLevel.Warn, "저장되지 않은 씬 변경이 있어 씬 검사를 건너뜀 - 저장 후 다시 실행");
            }
            else
            {
                for (int i = 0; i < scenes.Length; i++)
                {
                    if (!scenes[i].enabled || !File.Exists(scenes[i].path)) continue;
                    Scene s = EditorSceneManager.GetSceneByPath(scenes[i].path);
                    bool wasLoaded = s.IsValid() && s.isLoaded;
                    if (!wasLoaded) s = EditorSceneManager.OpenScene(scenes[i].path, OpenSceneMode.Additive);
                    GameObject[] roots = s.GetRootGameObjects();
                    for (int g = 0; g < roots.Length; g++)
                    {
                        Transform[] all = roots[g].GetComponentsInChildren<Transform>(true);
                        for (int t = 0; t < all.Length; t++)
                        {
                            int c = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(all[t].gameObject);
                            if (c > 0) { missing += c; where.Add(scenes[i].path + ":" + all[t].name); }
                        }
                    }
                    scenesChecked++;
                    if (!wasLoaded) EditorSceneManager.CloseScene(s, true);
                }
                if (missing > 0) Add(r, "No missing scripts (scenes)", PreflightLevel.Fail, missing + "건: " + string.Join(", ", where.GetRange(0, Mathf.Min(5, where.Count)).ToArray()));
                else Add(r, "No missing scripts (scenes)", PreflightLevel.Pass, "씬 " + scenesChecked + "개 검사");
            }

            int pm = 0, prefabs = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" });
            for (int i = 0; i < guids.Length; i++)
            {
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (go == null) continue;
                prefabs++;
                Transform[] all = go.GetComponentsInChildren<Transform>(true);
                for (int t = 0; t < all.Length; t++) pm += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(all[t].gameObject);
            }
            Add(r, "No missing scripts (prefabs)", pm > 0 ? PreflightLevel.Fail : PreflightLevel.Pass, prefabs + "개 프리팹, 누락 " + pm + "건");
        }

        private static void CheckPlayerSettings(List<PreflightItem> r)
        {
            string v = PlayerSettings.bundleVersion;
            bool semver = !string.IsNullOrEmpty(v) && System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d+\.\d+\.\d+$");
            Add(r, "Version (bundleVersion)", semver ? PreflightLevel.Pass : PreflightLevel.Fail, "'" + v + "' " + (semver ? "" : "- MAJOR.MINOR.PATCH 형식이어야 함"));

            bool names = !string.IsNullOrEmpty(PlayerSettings.companyName) && !string.IsNullOrEmpty(PlayerSettings.productName) && PlayerSettings.companyName != "DefaultCompany";
            Add(r, "Company / product name", names ? PreflightLevel.Pass : PreflightLevel.Warn, PlayerSettings.companyName + " / " + PlayerSettings.productName);

            bool res = PlayerSettings.defaultScreenWidth == 1920 && PlayerSettings.defaultScreenHeight == 1080;
            Add(r, "Default resolution 1920x1080", res ? PreflightLevel.Pass : PreflightLevel.Warn, PlayerSettings.defaultScreenWidth + "x" + PlayerSettings.defaultScreenHeight);

            string id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            bool idOk = !string.IsNullOrEmpty(id) && !id.StartsWith("com.DefaultCompany");
            Add(r, "Android application id", idOk ? PreflightLevel.Pass : PreflightLevel.Warn, "'" + id + "' (스토어 등록 후 변경 불가)");

            ScriptingImplementation be = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
            Add(r, "Android IL2CPP", be == ScriptingImplementation.IL2CPP ? PreflightLevel.Pass : PreflightLevel.Warn, be + " (BuildTools 가 빌드 시 IL2CPP 로 설정)");

            bool arm64 = (PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) != 0;
            Add(r, "Android ARM64", arm64 ? PreflightLevel.Pass : PreflightLevel.Warn, PlayerSettings.Android.targetArchitectures.ToString());

            bool sdk = PlayerSettings.Android.minSdkVersion >= AndroidSdkVersions.AndroidApiLevel24;
            Add(r, "Android minSdk >= 24", sdk ? PreflightLevel.Pass : PreflightLevel.Warn, PlayerSettings.Android.minSdkVersion.ToString());

            Add(r, "Android no INTERNET permission", PlayerSettings.Android.forceInternetPermission ? PreflightLevel.Warn : PreflightLevel.Pass,
                "forceInternetPermission=" + PlayerSettings.Android.forceInternetPermission + " (무수집 원칙: 끄기. Development 빌드는 프로파일러 때문에 자동 추가될 수 있음)");

            int handler = -1;
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets != null && assets.Length > 0)
            {
                SerializedObject so = new SerializedObject(assets[0]);
                SerializedProperty prop = so.FindProperty("activeInputHandler");
                if (prop != null) handler = prop.intValue;
            }
            Add(r, "Active Input Handling = Input System", handler == 1 ? PreflightLevel.Pass : PreflightLevel.Warn, "activeInputHandler=" + handler + " (1 이 권장)");
        }
    }
}
