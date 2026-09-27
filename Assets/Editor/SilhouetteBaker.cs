using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes a silhouette into the alpha channel of coloring-book style lineart.
///
/// The Chowder Fill shader needs to know which pixels are inside the character,
/// but on white-background art the paper and the character's own white fill are
/// the same colour - no per-pixel test can tell them apart, because "enclosed by
/// lines" is not something a fragment shader can evaluate. So we flood fill from
/// the image border here, once, and store the answer in alpha.
///
/// This also cleans up the pink construction sketch: strays outside the lineart
/// are reached by the flood and dropped, and strays inside are painted over by
/// the pattern anyway.
/// </summary>
public class SilhouetteBaker : EditorWindow
{
    [SerializeField] float floodThreshold = 0.75f;  // luminance the flood can pass through
    [SerializeField] float sketchSatLo    = 0.05f;  // below this, treat as neutral edge AA
    [SerializeField] float sketchSatHi    = 0.20f;  // above this, treat as coloured sketch
    [SerializeField] string outputFolder  = "Baked";
    [SerializeField] bool  overwriteInPlace;

    // Baked frames keep their original filenames so the frame number stays at the
    // end, which is what PlayerCharacterSetup groups on. A suffix would land after
    // the number ("Idle1_fill") and break that.
    [SerializeField] float targetWorldHeight = 2.56f;  // matches the existing player quad
    [SerializeField] SpriteAlignment pivot = SpriteAlignment.BottomCenter;

    [MenuItem("Tools/Eschropita/Bake Silhouette To Alpha")]
    static void Open()
    {
        GetWindow<SilhouetteBaker>(true, "Silhouette Baker").minSize = new Vector2(360, 260);
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Select the lineart PNGs in the Project window, then Bake.\n\n" +
            "Flood fills inward from the image border and writes the result to alpha. " +
            "If a frame comes out mostly transparent, the flood leaked through a gap " +
            "in the outline - close the gap in your art, or lower the threshold.",
            MessageType.Info);

        EditorGUILayout.Space();
        floodThreshold = EditorGUILayout.Slider(
            new GUIContent("Flood Threshold", "The flood passes through pixels brighter than this. Raise it if the flood leaks into the character, lower it if the background is not fully cleared."),
            floodThreshold, 0.4f, 0.99f);
        sketchSatLo = EditorGUILayout.Slider(
            new GUIContent("Sketch Reject Start", "Colour saturation at which a flooded pixel starts being treated as construction sketch rather than antialiasing."),
            sketchSatLo, 0f, 0.5f);
        sketchSatHi = EditorGUILayout.Slider(
            new GUIContent("Sketch Reject Full", "Saturation at which a flooded pixel is fully erased."),
            sketchSatHi, 0f, 0.8f);
        if (sketchSatHi < sketchSatLo) sketchSatHi = sketchSatLo;

        EditorGUILayout.Space();
        targetWorldHeight = EditorGUILayout.FloatField(
            new GUIContent("Target World Height", "Sets pixels-per-unit so the character ends up this tall in world units. The existing player quad is 2.56."),
            targetWorldHeight);
        pivot = (SpriteAlignment)EditorGUILayout.EnumPopup(
            new GUIContent("Pivot", "BottomCenter puts the origin at the feet, so the character stands on the ground rather than sinking half-way in."),
            pivot);

        EditorGUILayout.Space();
        overwriteInPlace = EditorGUILayout.Toggle(
            new GUIContent("Overwrite In Place", "Off (recommended) writes to a subfolder so your originals survive a re-bake with different settings."),
            overwriteInPlace);
        using (new EditorGUI.DisabledScope(overwriteInPlace))
            outputFolder = EditorGUILayout.TextField(
                new GUIContent("Output Subfolder", "Filenames are preserved, so frame numbers stay at the end of the name."),
                outputFolder);

        EditorGUILayout.Space();
        var targets = Selection.GetFiltered<Texture2D>(SelectionMode.Assets);
        using (new EditorGUI.DisabledScope(targets.Length == 0))
        {
            if (GUILayout.Button($"Bake {targets.Length} texture(s)", GUILayout.Height(30)))
                BakeAll(targets);
        }
        if (targets.Length == 0)
            EditorGUILayout.LabelField("No textures selected.", EditorStyles.miniLabel);
    }

    void BakeAll(Texture2D[] targets)
    {
        try
        {
            for (int i = 0; i < targets.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Baking silhouettes",
                    targets[i].name, (float)i / targets.Length);
                Bake(targets[i]);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }
    }

    void Bake(Texture2D source)
    {
        string path = AssetDatabase.GetAssetPath(source);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[SilhouetteBaker] {path} has no TextureImporter, skipped.");
            return;
        }

        // Reading pixels needs an uncompressed, readable source at its true size.
        // Default-type textures default to nPOTScale ToNearest, which silently
        // resamples 750x1000 art to 1024x1024 and distorts it - we would then
        // flood fill and bake the distorted version. Snapshot everything we
        // override so the user's settings survive.
        bool prevReadable = importer.isReadable;
        var  prevCompress = importer.textureCompression;
        var  prevNpot     = importer.npotScale;
        importer.isReadable = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();

        Color32[] pixels;
        int w, h;
        try
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            w = tex.width;
            h = tex.height;
            pixels = tex.GetPixels32();
        }
        finally
        {
            importer.isReadable = prevReadable;
            importer.textureCompression = prevCompress;
            importer.npotScale = prevNpot;
            importer.SaveAndReimport();
        }

        var outside = FloodFromBorder(pixels, w, h, floodThreshold);

        int flooded = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!outside[i]) { pixels[i].a = 255; continue; }

            flooded++;
            Color32 p = pixels[i];

            // Neutral pixels the flood reached are the outline's own antialiasing
            // ramp, so fade them by brightness instead of cutting them flat.
            float lum = Luminance(p);
            float edge = Mathf.Clamp01((1f - lum) / Mathf.Max(1f - floodThreshold, 1e-4f));

            // Coloured pixels out here are construction sketch. Erase them.
            float sat  = Chroma(p);
            float keep = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(sketchSatLo, Mathf.Max(sketchSatHi, sketchSatLo + 1e-4f), sat));

            pixels[i].a = (byte)Mathf.RoundToInt(Mathf.Clamp01(edge * keep) * 255f);
        }

        float floodedPct = 100f * flooded / pixels.Length;
        if (floodedPct > 92f)
            Debug.LogWarning($"[SilhouetteBaker] {source.name}: flood covered {floodedPct:F1}% of the image. " +
                             "That usually means it leaked through a gap in the outline.", source);

        // Derive PPU from the desired on-screen size rather than copying the
        // source's, which is 100 by default and would make the character 10
        // world units tall.
        float ppu = h / Mathf.Max(targetWorldHeight, 1e-3f);

        WriteResult(source, path, pixels, w, h, ppu, floodedPct);
    }

    /// <summary>Marks every pixel reachable from the border through bright pixels.</summary>
    static bool[] FloodFromBorder(Color32[] pixels, int w, int h, float threshold)
    {
        var outside = new bool[pixels.Length];
        var stack = new Stack<int>(w * 2 + h * 2);

        void Push(int x, int y)
        {
            int i = y * w + x;
            if (outside[i] || Luminance(pixels[i]) < threshold) return;
            outside[i] = true;
            stack.Push(i);
        }

        for (int x = 0; x < w; x++) { Push(x, 0); Push(x, h - 1); }
        for (int y = 0; y < h; y++) { Push(0, y); Push(w - 1, y); }

        while (stack.Count > 0)
        {
            int i = stack.Pop();
            int x = i % w, y = i / w;
            if (x > 0)     Push(x - 1, y);
            if (x < w - 1) Push(x + 1, y);
            if (y > 0)     Push(x, y - 1);
            if (y < h - 1) Push(x, y + 1);
        }
        return outside;
    }

    void WriteResult(Texture2D source, string sourcePath, Color32[] pixels,
                     int w, int h, float ppu, float floodedPct)
    {
        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        outTex.SetPixels32(pixels);
        outTex.Apply();
        byte[] png = outTex.EncodeToPNG();
        DestroyImmediate(outTex);

        string dir  = Path.GetDirectoryName(sourcePath).Replace('\\', '/');
        string name = Path.GetFileName(sourcePath);
        string outPath;

        if (overwriteInPlace)
        {
            outPath = sourcePath;
        }
        else
        {
            string outDir = $"{dir}/{outputFolder}";
            if (!AssetDatabase.IsValidFolder(outDir))
                AssetDatabase.CreateFolder(dir, outputFolder);
            outPath = $"{outDir}/{name}";
        }

        File.WriteAllBytes(outPath, png);
        AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);

        if (AssetImporter.GetAtPath(outPath) is TextureImporter outImporter)
        {
            outImporter.textureType         = TextureImporterType.Sprite;
            outImporter.spriteImportMode    = SpriteImportMode.Single;
            outImporter.alphaSource         = TextureImporterAlphaSource.FromInput;
            outImporter.alphaIsTransparency = true;
            outImporter.mipmapEnabled       = false;
            outImporter.spritePixelsPerUnit = ppu;

            // spriteAlignment is only reachable through TextureImporterSettings.
            var settings = new TextureImporterSettings();
            outImporter.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)pivot;
            outImporter.SetTextureSettings(settings);

            outImporter.SaveAndReimport();
        }

        Debug.Log($"[SilhouetteBaker] {source.name} -> {outPath} " +
                  $"({100f - floodedPct:F1}% of the image kept as silhouette)", source);
    }

    static float Luminance(Color32 c) =>
        (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

    static float Chroma(Color32 c) =>
        (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b))) / 255f;
}
