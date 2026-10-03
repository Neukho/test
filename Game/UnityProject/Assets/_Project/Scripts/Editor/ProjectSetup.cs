using UnityEditor;
using UnityEngine;

namespace MorpheHouse.EditorTools
{
    /// <summary>권장 프로젝트 설정 일괄 적용: 새 Input System 전용, 기본 해상도, 가로 화면.</summary>
    public static class ProjectSetup
    {
        [MenuItem("MorpheHouse/Setup/Apply Recommended Project Settings")]
        public static void Apply()
        {
            PlayerSettings.companyName = "MorpheHouse";
            PlayerSettings.productName = "MoonCornerHouse";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Active Input Handling: 0=Old, 1=New(Input System Package), 2=Both
            bool inputChanged = false;
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets != null && assets.Length > 0)
            {
                SerializedObject so = new SerializedObject(assets[0]);
                SerializedProperty prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 1)
                {
                    prop.intValue = 1;
                    so.ApplyModifiedProperties();
                    inputChanged = true;
                }
            }

            AssetDatabase.SaveAssets();
            if (inputChanged)
                EditorUtility.DisplayDialog("MorpheHouse", "Input System 설정을 변경했습니다. 에디터를 재시작해야 적용됩니다.", "확인");
            Debug.Log("[MorpheHouse] 프로젝트 설정 적용 완료.");
        }
    }
}
