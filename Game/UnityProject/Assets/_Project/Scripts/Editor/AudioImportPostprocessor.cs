using UnityEditor;
using UnityEngine;

namespace MorpheHouse.EditorTools
{
    /// <summary>
    /// Resources/Audio 아래 오디오 클립에 임포트 규칙을 자동 적용한다(docs/14_Audio_Request_List.md, docs/15_Performance_Guide.md §6).
    /// BGM(…/BGM/): Streaming + Vorbis(메모리에 통째로 올리지 않는다), 백그라운드 로드.
    /// SE(…/SE/): Decompress On Load + Vorbis, 모노(짧은 소리는 즉시 재생돼야 하고 메모리도 작다).
    /// 이 스크립트는 Unity 에서 컴파일 검증을 하지 못했다(UnityEditor 는 이 저장소의 컴파일 검사 범위 밖).
    /// </summary>
    public sealed class AudioImportPostprocessor : AssetPostprocessor
    {
        private const string Root = "Assets/_Project/Resources/Audio/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;

            AudioImporter importer = (AudioImporter)assetImporter;
            bool bgm = assetPath.Contains("/BGM/");

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = bgm ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = bgm ? 0.6f : 0.7f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = !bgm;
            importer.loadInBackground = bgm;
            importer.preloadAudioData = !bgm;
        }
    }
}
