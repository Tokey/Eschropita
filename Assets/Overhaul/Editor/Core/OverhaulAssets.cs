using System.IO;
using UnityEditor;
using UnityEngine;

namespace Eschropita.Overhaul.EditorTools
{
    public enum TexKind
    {
        /// <summary>sRGB colour (albedo, emissive colour).</summary>
        Color,
        /// <summary>Tangent-space normal map.</summary>
        Normal,
        /// <summary>Linear data (HDRP mask map, height, flow, masks).</summary>
        Linear,
    }

    /// <summary>
    /// Saves generated meshes / materials / textures under Assets/Overhaul/Generated/... (overwriting on rebuild).
    /// Paths passed in are relative to the Generated folder, e.g. "Turbine/TowerMesh.asset".
    /// </summary>
    public static class OverhaulAssets
    {
        public const string GeneratedRoot = "Assets/Overhaul/Generated";

        public static string PathFor(string relative) => GeneratedRoot + "/" + relative.TrimStart('/');

        public static void EnsureFolder(string assetFolder)
        {
            assetFolder = assetFolder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>
        /// The shared ink-outline material (saved once at Generated/Shared/Outline_Ink.mat). Pass a different width
        /// only for special cases; every structure should normally use this one so line weight is consistent.
        /// </summary>
        public static Material SharedOutline(float widthPx = 2.2f)
        {
            string rel = widthPx == 2.2f ? "Shared/Outline_Ink.mat" : $"Shared/Outline_Ink_{widthPx:0.0}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(PathFor(rel));
            if (existing != null) return existing;
            var m = OverhaulOutline.CreateMaterial(OverhaulOutline.Ink, widthPx);
            return m != null ? SaveMaterial(m, rel) : null;
        }

        public static Mesh SaveMesh(Mesh mesh, string relative)
        {
            string path = PathFor(relative.EndsWith(".asset") ? relative : relative + ".asset");
            EnsureFolder(Path.GetDirectoryName(path));
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        public static Material SaveMaterial(Material mat, string relative)
        {
            string path = PathFor(relative.EndsWith(".mat") ? relative : relative + ".mat");
            EnsureFolder(Path.GetDirectoryName(path));
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = mat.shader;
                existing.CopyPropertiesFromMaterial(mat);
                existing.shaderKeywords = mat.shaderKeywords;
                existing.renderQueue = mat.renderQueue;
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>
        /// Writes pixels as a PNG and imports it with settings matching <paramref name="kind"/>.
        /// Returns the imported texture asset.
        /// </summary>
        public static Texture2D SaveTexture(Color[] pixels, int size, string relative, TexKind kind,
                                            bool repeat = true, bool alpha = false, int maxSize = 2048)
        {
            string path = PathFor(relative.EndsWith(".png") ? relative : relative + ".png");
            EnsureFolder(Path.GetDirectoryName(path));
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, kind != TexKind.Color);
            tex.SetPixels(pixels);
            tex.Apply(false);
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp != null)
            {
                imp.textureType = kind == TexKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = kind == TexKind.Color;
                imp.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                imp.alphaIsTransparency = alpha && kind == TexKind.Color;
                imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.maxTextureSize = maxSize;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.anisoLevel = 4;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
