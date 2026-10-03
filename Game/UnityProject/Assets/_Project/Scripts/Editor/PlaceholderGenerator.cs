using System.Collections.Generic;
using System.IO;
using MorpheHouse.ArtPipeline;
using MorpheHouse.Data;
using UnityEditor;
using UnityEngine;

namespace MorpheHouse.EditorTools
{
    /// <summary>
    /// 플레이스홀더 PNG 를 최종 파일명 그대로 생성한다. 이미 있는 파일(=실제 일러스트)은 덮어쓰지 않는다.
    /// 임포트 설정은 ArtImportPostprocessor 가 경로 규칙에 따라 자동 적용한다.
    /// </summary>
    public static class PlaceholderGenerator
    {
        private const string ResourcesRoot = "Assets/_Project/Resources/";

        [MenuItem("MorpheHouse/Art/Generate Placeholders")]
        public static void Generate()
        {
            List<ArtSpec> specs = ArtManifest.All(CharIds.All);
            int created = 0;
            int skipped = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < specs.Count; i++)
                {
                    ArtSpec spec = specs[i];
                    string assetPath = ResourcesRoot + spec.ResPath + ".png";
                    if (File.Exists(assetPath)) { skipped++; continue; }

                    if (i % 20 == 0) EditorUtility.DisplayProgressBar("플레이스홀더 생성", spec.ResPath, i / (float)specs.Count);
                    Directory.CreateDirectory(Path.GetDirectoryName(assetPath));

                    Texture2D tex = new Texture2D(spec.Width, spec.Height, TextureFormat.RGBA32, false);
                    tex.SetPixels32(PlaceholderPainter.Paint(spec));
                    tex.Apply();
                    File.WriteAllBytes(assetPath, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    created++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.Refresh();
            Debug.Log("[MorpheHouse] 플레이스홀더 생성: " + created + "장 (기존 파일 유지 " + skipped + "장). 같은 파일명으로 일러스트를 덮어쓰면 자동 반영됩니다.");
        }
    }
}
