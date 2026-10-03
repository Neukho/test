#if UNITY_EDITOR
using System.IO;
using System.Text;
using MorpheHouse.GameFlow;
using UnityEditor;
using UnityEngine;

namespace MorpheHouse.EditorTools
{
    /// <summary>
    /// 데모 빌드 전환 메뉴: Resources/Config/build_config.csv 의 demo 행을 0↔1 로 뒤집는다(순수 로직은 DemoConfig.ToggleDemo).
    /// 데모 빌드를 만들기 전에 켜고, 정식 빌드 전에 반드시 끈다(docs/18 §2.5 의 체크리스트).
    /// Unity 에디터에서만 동작한다 — 이 파일은 Unity 없이는 컴파일 검증하지 못했다.
    /// </summary>
    public static class DemoConfigTool
    {
        private const string ConfigPath = "Assets/_Project/Resources/Config/build_config.csv";

        [MenuItem("MorpheHouse/Build/Toggle Demo Config")]
        public static void Toggle()
        {
            string text = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, Encoding.UTF8) : null;
            bool now;
            string updated = DemoConfig.ToggleDemo(text, out now);
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, updated, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log("[DemoConfig] demo = " + (now ? "1 (데모 빌드)" : "0 (정식 빌드)") + " → " + ConfigPath);
            EditorUtility.DisplayDialog("Demo Config", now ? "데모 빌드: ON (demo,1)" : "정식 빌드: demo,0", "OK");
        }

        [MenuItem("MorpheHouse/Build/Toggle Demo Config", true)]
        private static bool ToggleValidate()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }
    }
}
#endif
