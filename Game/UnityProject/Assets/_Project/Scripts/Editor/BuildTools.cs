// 빌드 메뉴(에디터 전용). 이 파일은 Unity 없이 컴파일하지 못했다(UnityEditor 어셈블리 부재) — 처음 열 때 콘솔 오류부터 확인할 것.
// 표준 API 만 사용: BuildPipeline.BuildPlayer, BuildPlayerOptions, PlayerSettings, EditorBuildSettings.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace MorpheHouse.EditorTools
{
    /// <summary>
    /// 플랫폼별 빌드. 출력: Builds/&lt;Platform&gt;/&lt;version&gt;/ , 버전의 단일 출처는 PlayerSettings.bundleVersion.
    /// 빌드 후 같은 폴더에 BUILD_INFO.txt(버전·UTC 일시·git 커밋·SHA-256)를 쓴다.
    /// 서명 정보는 코드/저장소에 두지 않고 환경 변수로만 받는다(docs/18 참조).
    /// </summary>
    public static class BuildTools
    {
        private const string ExeName = "MoonCornerHouse";

        [MenuItem("MorpheHouse/Build/Windows (Release)")]
        public static void BuildWindowsRelease() { Build("Windows", BuildTarget.StandaloneWindows64, false, false); }

        [MenuItem("MorpheHouse/Build/Android APK (Debug)")]
        public static void BuildAndroidApkDebug() { Build("Android", BuildTarget.Android, true, false); }

        [MenuItem("MorpheHouse/Build/Android AAB (Release)")]
        public static void BuildAndroidAabRelease() { Build("Android", BuildTarget.Android, false, true); }

        private static void Build(string platform, BuildTarget target, bool development, bool aab)
        {
            int code = 1;
            try { code = DoBuild(platform, target, development, aab) ? 0 : 1; }
            catch (Exception e) { Debug.LogError("[MorpheHouse] 빌드 예외: " + e); }
            // 배치 모드(CI)에서는 종료 코드로 성패를 알린다.
            if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static bool DoBuild(string platform, BuildTarget target, bool development, bool aab)
        {
            List<PreflightItem> pre = BuildPreflight.Run();
            Debug.Log(BuildPreflight.Format(pre));
            if (BuildPreflight.CountFail(pre) > 0)
            {
                Debug.LogError("[MorpheHouse] Preflight FAIL - 빌드를 중단합니다.");
                return false;
            }

            List<string> scenes = new List<string>();
            EditorBuildSettingsScene[] all = EditorBuildSettings.scenes;
            for (int i = 0; i < all.Length; i++) if (all[i].enabled) scenes.Add(all[i].path);
            if (scenes.Count == 0) { Debug.LogError("[MorpheHouse] Build Settings 에 활성 씬이 없습니다."); return false; }

            string version = PlayerSettings.bundleVersion;
            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string outDir = Path.Combine(Path.Combine(Path.Combine(projectRoot, "Builds"), platform), Sanitize(version));
            Directory.CreateDirectory(outDir);

            string outPath;
            BuildOptions options = BuildOptions.None;
            if (target == BuildTarget.StandaloneWindows64)
            {
                outPath = Path.Combine(outDir, ExeName + ".exe");
            }
            else
            {
                if (!ConfigureAndroid(aab)) return false;
                string suffix = development ? "-debug" : "";
                outPath = Path.Combine(outDir, ExeName + "-" + Sanitize(version) + suffix + (aab ? ".aab" : ".apk"));
                if (development) options |= BuildOptions.Development;
            }

            BuildPlayerOptions bpo = new BuildPlayerOptions();
            bpo.scenes = scenes.ToArray();
            bpo.locationPathName = outPath;
            bpo.target = target;
            bpo.options = options;

            BuildReport report = BuildPipeline.BuildPlayer(bpo);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[MorpheHouse] 빌드 실패: " + summary.result + " (오류 " + summary.totalErrors + "건)");
                return false;
            }
            Debug.Log("[MorpheHouse] 빌드 성공: " + outPath + " (" + summary.totalSize + " bytes)");
            WriteBuildInfo(outDir, platform, version, target, development, aab);
            return true;
        }

        /// <summary>IL2CPP + ARM64. 릴리스 AAB 는 환경 변수의 키스토어 정보가 없으면 중단한다(서명 없는 AAB 는 업로드 불가).</summary>
        private static bool ConfigureAndroid(bool aab)
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = aab;

            if (aab)
            {
                string ks = Environment.GetEnvironmentVariable("MORPHE_KEYSTORE_PATH");
                string ksPass = Environment.GetEnvironmentVariable("MORPHE_KEYSTORE_PASS");
                string alias = Environment.GetEnvironmentVariable("MORPHE_KEY_ALIAS");
                string keyPass = Environment.GetEnvironmentVariable("MORPHE_KEY_PASS");
                if (string.IsNullOrEmpty(ks) || string.IsNullOrEmpty(ksPass) || string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(keyPass) || !File.Exists(ks))
                {
                    Debug.LogError("[MorpheHouse] 릴리스 AAB 에는 환경 변수 MORPHE_KEYSTORE_PATH / MORPHE_KEYSTORE_PASS / MORPHE_KEY_ALIAS / MORPHE_KEY_PASS 가 필요합니다(키스토어 파일이 존재해야 함).");
                    return false;
                }
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = ks;
                PlayerSettings.Android.keystorePass = ksPass;
                PlayerSettings.Android.keyaliasName = alias;
                PlayerSettings.Android.keyaliasPass = keyPass;
                Debug.Log("[MorpheHouse] bundleVersionCode=" + PlayerSettings.Android.bundleVersionCode + " (스토어 업로드마다 증가해야 함 - 수동 관리)");
            }
            return true;
        }

        private static void WriteBuildInfo(string outDir, string platform, string version, BuildTarget target, bool development, bool aab)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MoonCornerHouse BUILD_INFO");
            sb.AppendLine("version: " + version);
            sb.AppendLine("platform: " + platform + " (" + target + ")" + (development ? " development" : " release") + (aab ? " aab" : ""));
            sb.AppendLine("built_utc: " + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
            sb.AppendLine("unity: " + UnityEngine.Application.unityVersion);
            sb.AppendLine("git_commit: " + GitCommit());
            sb.AppendLine();
            sb.AppendLine("# SHA-256 (sha256sum 형식: <hash>  <상대경로>)");

            List<string> files = new List<string>(Directory.GetFiles(outDir, "*", SearchOption.AllDirectories));
            files.Sort(StringComparer.Ordinal);
            using (SHA256 sha = SHA256.Create())
            {
                for (int i = 0; i < files.Count; i++)
                {
                    string rel = files[i].Substring(outDir.Length).TrimStart('/', '\\').Replace('\\', '/');
                    if (rel == "BUILD_INFO.txt") continue;
                    if (rel.Contains("_BurstDebugInformation_DoNotShip/") || rel.Contains("_BackUpThisFolder_ButDontShipItWithYourGame/")) continue;
                    try
                    {
                        using (FileStream fs = File.OpenRead(files[i]))
                        {
                            byte[] h = sha.ComputeHash(fs);
                            sb.AppendLine(ToHex(h) + "  " + rel);
                        }
                    }
                    catch (Exception e) { sb.AppendLine("ERROR  " + rel + "  " + e.Message); }
                }
            }
            File.WriteAllText(Path.Combine(outDir, "BUILD_INFO.txt"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[MorpheHouse] BUILD_INFO.txt 작성: " + outDir);
        }

        private static string GitCommit()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("git", "rev-parse HEAD");
                psi.WorkingDirectory = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return "unavailable";
                    string o = p.StandardOutput.ReadToEnd().Trim();
                    if (!p.WaitForExit(5000)) return "unavailable";
                    return (p.ExitCode == 0 && o.Length > 0) ? o : "unavailable";
                }
            }
            catch (Exception) { return "unavailable"; }
        }

        private static string ToHex(byte[] b)
        {
            StringBuilder sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2"));
            return sb.ToString();
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "0.0.0";
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < bad.Length; i++) s = s.Replace(bad[i], '_');
            return s;
        }
    }
}
