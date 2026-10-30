using UnityEditor;

namespace Eschropita.Overhaul.EditorTools
{
    /// <summary>
    /// Import settings for the level textures in Assets/Overhaul/Resources/Overhaul/Textures (CC0, Poly Haven):
    /// *_Normal → normal map; *_Mask → linear HDRP mask map (with alpha); *_Albedo → sRGB colour. All repeat,
    /// mip-mapped, capped at 1024 px.
    /// </summary>
    public class OverhaulTextureImport : AssetPostprocessor
    {
        const string Root = "Assets/Overhaul/Resources/Overhaul/Textures/";

        void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Root)) return;
            var imp = (TextureImporter)assetImporter;
            imp.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.maxTextureSize = 1024;
            imp.anisoLevel = 4;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            string name = System.IO.Path.GetFileNameWithoutExtension(p);
            if (name.EndsWith("_Normal"))
            {
                imp.textureType = TextureImporterType.NormalMap;
            }
            else if (name.EndsWith("_Mask"))
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = false;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false;
            }
            else
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.alphaSource = TextureImporterAlphaSource.None;
            }
        }
    }
}
