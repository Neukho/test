using UnityEditor;
using UnityEngine;

namespace MorpheHouse.EditorTools
{
    /// <summary>
    /// Resources/Art 아래 텍스처에 임포트 규칙을 자동 적용한다(플레이스홀더와 최종 일러스트 모두).
    /// 02_Art_Plan.md §6 의 표와 동일: Sprite/Single, PPU 100, 밉맵 없음, 최대 2048(아이콘 512),
    /// PC=BC7, Android=ASTC(6x6, 아이콘 4x4), 스탠딩 피벗=하단 중앙.
    /// </summary>
    public sealed class ArtImportPostprocessor : AssetPostprocessor
    {
        private const string Root = "Assets/_Project/Resources/Art/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;

            TextureImporter ti = (TextureImporter)assetImporter;
            bool isIcon = assetPath.Contains("/Icons/");
            bool isStanding = assetPath.Contains("/Standing/");
            int maxSize = isIcon ? 512 : 2048;

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100f;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.maxTextureSize = maxSize;
            ti.textureCompression = TextureImporterCompression.Compressed;

            TextureImporterSettings settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = isStanding ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
            ti.SetTextureSettings(settings);

            TextureImporterPlatformSettings pc = ti.GetPlatformTextureSettings("Standalone");
            pc.overridden = true;
            pc.maxTextureSize = maxSize;
            pc.format = TextureImporterFormat.BC7;
            ti.SetPlatformTextureSettings(pc);

            TextureImporterPlatformSettings android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = maxSize;
            android.format = isIcon ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
        }
    }
}
