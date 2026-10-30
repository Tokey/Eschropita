using UnityEditor;
using UnityEngine;

namespace Eschropita.Overhaul.EditorTools
{
    /// <summary>
    /// Import settings for the overhaul's audio (Assets/Overhaul/Resources/Overhaul/Audio/...):
    ///  - music/ and amb/ : long loops → Streaming, Vorbis 0.6 (low memory, seamless with the director's crossfades)
    ///  - phrase/         : musical phrases/stingers → Compressed In Memory, Vorbis 0.75
    ///  - sfx/ and ui/    : short one-shots → Decompress On Load, Vorbis 0.75 (no decode cost when triggered)
    /// </summary>
    public class OverhaulAudioImport : AssetPostprocessor
    {
        const string Root = "Assets/Overhaul/Resources/Overhaul/Audio/";

        void OnPreprocessAudio()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Root)) return;
            var imp = (AudioImporter)assetImporter;
            var s = imp.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            if (p.Contains("/music/") || p.Contains("/amb/"))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.quality = 0.6f;
                s.preloadAudioData = false;
                imp.loadInBackground = true;
            }
            else if (p.Contains("/phrase/"))
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.quality = 0.75f;
                s.preloadAudioData = true;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.quality = 0.75f;
                s.preloadAudioData = true;
            }
            imp.defaultSampleSettings = s;
        }
    }
}
