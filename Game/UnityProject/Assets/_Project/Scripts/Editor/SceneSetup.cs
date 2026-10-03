using System.Collections.Generic;
using System.IO;
using MorpheHouse.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MorpheHouse.EditorTools
{
    /// <summary>Boot 씬을 코드로 만들고 Build Settings 에 등록한다(클릭 한 번).</summary>
    public static class SceneSetup
    {
        private const string ScenePath = "Assets/_Project/Scenes/Boot.unity";

        [MenuItem("MorpheHouse/Setup/Create Boot Scene")]
        public static void CreateBootScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            Camera cam = camObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x14, 0x20, 0x2E, 255);
            cam.orthographic = true;
            camObj.AddComponent<AudioListener>();

            GameObject boot = new GameObject("GameBootstrap");
            boot.AddComponent<GameBootstrap>();

            Directory.CreateDirectory("Assets/_Project/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath)) list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            Debug.Log("[MorpheHouse] Boot 씬 생성 완료: " + ScenePath + " (Build Settings 등록됨). ▶ Play 를 눌러 확인하세요.");
        }
    }
}
