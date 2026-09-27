using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Sets up the Mycari's world-projected back swirl, and bakes the region it shows through.
///
/// The art leaves that region empty: the wispy shapes behind the body are open black strokes on
/// transparency, with nothing enclosing them, so there is no shape for a shader to fill. The bake
/// works the region out instead of asking the artist to flat-fill it by hand — close the gaps
/// between the strokes, take everything the outside cannot reach as the creature's interior, and
/// whatever is interior but unpainted is the region. That gets written into the frame as a flat
/// key colour, which the Chowder Fill shader swaps for the pattern.
/// </summary>
public static class MycariPatternSetup
{
    const string Root        = "Assets/Mycari";
    const string PatternsDir = Root + "/Patterns";
    const string MatDir      = Root + "/Materials";
    const string MatPath     = MatDir + "/MycariChowderFill.mat";
    const string PrefabPath  = Root + "/Mycari.prefab";
    const string ControllerPath = Root + "/MycariController.controller";
    const string RepairFolder   = Root + "/Repair";
    const string RepairClipPath = Root + "/MycariRepair.anim";
    const float  FrameRate      = 8f;

    // Must match MycariPattern / the material. Pure magenta appears nowhere in the art.
    static readonly Color32 KeyColor = new Color32(255, 0, 255, 255);

    static readonly string[] FrameFolders =
        { Root + "/Idle", Root + "/Moving", Root + "/Attack", Root + "/Repair" };

    // ------------------------------------------------------------------ setup

    [MenuItem("Eschropita/Mycari/Set Up Patterns And Repair Clip")]
    public static void SetUp()
    {
        var mat = BuildMaterial();
        if (mat == null) return;

        var repair = BuildRepairClip();
        WireController(repair);
        WirePrefab(mat);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MycariPattern] Material, Repair clip and prefab wiring are up to date.");
    }

    static Material BuildMaterial()
    {
        var shader = Shader.Find("Eschropita/Chowder Fill");
        if (shader == null)
        {
            Debug.LogError("[MycariPattern] Shader 'Eschropita/Chowder Fill' not found — did it fail to compile?");
            return null;
        }

        EnsureDir(MatDir);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        mat.shader = shader;

        // Flat key: the baked region is one exact colour, so neither the ink ramp nor the
        // greenness test applies.
        mat.EnableKeyword("_FLAT_KEY");
        mat.DisableKeyword("_CHROMA_KEY");
        mat.SetFloat("_FlatKey", 1f);
        mat.SetFloat("_ChromaKey", 0f);
        mat.SetColor("_FlatKeyColor", KeyColor);
        mat.SetFloat("_FlatKeyTolerance", 0.35f);
        mat.SetFloat("_FlatKeySoftness", 0.12f);
        mat.SetFloat("_PatternScale", 4f);
        mat.SetFloat("_PatternKeepAspect", 1f);

        var rig = Object.FindAnyObjectByType<IsoFollowCamera>();
        if (rig != null)
        {
            mat.SetFloat("_CamYaw", rig.yaw);
            mat.SetFloat("_CamPitch", rig.pitch);
        }

        var fallback = LoadPattern("YELLOW");
        if (fallback != null) mat.SetTexture("_PatternTex", fallback);

        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D LoadPattern(string name) =>
        AssetDatabase.LoadAssetAtPath<Texture2D>($"{PatternsDir}/Mycari{name}.png");

    static AnimationClip BuildRepairClip()
    {
        var sprites = LoadSprites(RepairFolder);
        if (sprites.Count == 0)
        {
            Debug.LogWarning("[MycariPattern] No frames in " + RepairFolder + " — skipping the Repair clip.");
            return null;
        }

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RepairClipPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, RepairClipPath);
        }
        clip.frameRate = FrameRate;

        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        var keys = new ObjectReferenceKeyframe[sprites.Count];
        for (int i = 0; i < sprites.Count; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / FrameRate, value = sprites[i] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;           // it repairs until the job is done
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);
        Debug.Log($"[MycariPattern] Repair clip built from {sprites.Count} frames.");
        return clip;
    }

    static List<Sprite> LoadSprites(string folder)
    {
        var list = new List<Sprite>();
        if (!AssetDatabase.IsValidFolder(folder)) return list;
        foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            list.AddRange(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>());
        }
        list.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
        return list;
    }

    /// <summary>Adds a Repair state driven by a "Repair" bool, alongside the existing states.</summary>
    static void WireController(AnimationClip repair)
    {
        if (repair == null) return;
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogWarning("[MycariPattern] No animator controller at " + ControllerPath);
            return;
        }

        if (!controller.parameters.Any(p => p.name == "Repair"))
            controller.AddParameter("Repair", AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        var state = sm.states.FirstOrDefault(s => s.state.name == "Repair").state;
        if (state == null) state = sm.AddState("Repair");
        state.motion = repair;

        var idle = sm.states.FirstOrDefault(s => s.state.name == "Idle").state;

        if (!state.transitions.Any(t => t.destinationState == idle) && idle != null)
        {
            var back = state.AddTransition(idle);
            back.hasExitTime = false;
            back.duration = 0.1f;
            back.AddCondition(AnimatorConditionMode.IfNot, 0f, "Repair");
        }
        if (idle != null && !idle.transitions.Any(t => t.destinationState == state))
        {
            var into = idle.AddTransition(state);
            into.hasExitTime = false;
            into.duration = 0.1f;
            into.AddCondition(AnimatorConditionMode.If, 0f, "Repair");
        }

        EditorUtility.SetDirty(controller);
    }

    static void WirePrefab(Material mat)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("[MycariPattern] No prefab at " + PrefabPath);
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var sr = root.GetComponentInChildren<SpriteRenderer>();
            if (sr == null)
            {
                Debug.LogWarning("[MycariPattern] The Mycari prefab has no SpriteRenderer.");
                return;
            }
            sr.sharedMaterial = mat;

            var pat = sr.GetComponent<MycariPattern>();
            if (pat == null) pat = sr.gameObject.AddComponent<MycariPattern>();
            pat.defaultPattern   = LoadPattern("YELLOW");
            pat.fixerPattern     = LoadPattern("GREEN");
            pat.breakerPattern   = LoadPattern("RED");
            pat.wandererPattern  = LoadPattern("BLUE");

            // The prefab serialises these, so raising the defaults in code would never reach it.
            // They flock tightly enough to end up overlapping, which makes picking one impossible.
            var flocker = root.GetComponentInChildren<NPCFlocker>();
            if (flocker != null)
            {
                flocker.separationRadius = Mathf.Max(flocker.separationRadius, 2.6f);
                flocker.neighborRadius   = Mathf.Max(flocker.neighborRadius, 6f);
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------ bake

    [MenuItem("Eschropita/Mycari/Bake Fill Region")]
    public static void BakeFillRegion()
    {
        int baked = 0, skipped = 0;
        var paths = new List<string>();
        foreach (var folder in FrameFolders)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            paths.AddRange(Directory.GetFiles(folder, "*.png").Select(p => p.Replace('\\', '/')));
        }

        for (int i = 0; i < paths.Count; i++)
        {
            string p = paths[i];
            EditorUtility.DisplayProgressBar("Baking Mycari fill region",
                Path.GetFileName(p), i / (float)paths.Count);
            if (BakeOne(p)) baked++; else skipped++;
        }
        EditorUtility.ClearProgressBar();
        AssetDatabase.Refresh();
        Debug.Log($"[MycariPattern] Fill region baked into {baked} frame(s); {skipped} already done or had no region.");
    }

    static bool BakeOne(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return false;

        bool wasReadable = importer.isReadable;
        if (!wasReadable) { importer.isReadable = true; importer.SaveAndReimport(); }

        try
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) return false;

            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;

            // Already carries the key colour? Leave it be, so the bake is safe to re-run.
            int existing = px.Count(c => c.a > 128 && c.r > 240 && c.g < 15 && c.b > 240);
            if (existing > 500) return false;

            var region = ComputeRegion(px, w, h, closeRadius: 14, solid: 128);
            int count = region.Count(v => v);
            if (count < 400) return false;      // poses with no back region (e.g. the landing frame)

            for (int i = 0; i < px.Length; i++)
            {
                if (!region[i] || px[i].a > 128) continue;
                px[i] = KeyColor;
            }

            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            return true;
        }
        finally
        {
            if (!wasReadable)
            {
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null) { imp.isReadable = false; imp.SaveAndReimport(); }
            }
        }
    }

    /// <summary>
    /// close(drawn) → flood from the border → interior = whatever the flood never reached →
    /// region = interior that carries no paint of its own.
    /// </summary>
    static bool[] ComputeRegion(Color32[] px, int w, int h, int closeRadius, byte solid)
    {
        int n = w * h;
        var drawn = new bool[n];
        for (int i = 0; i < n; i++) drawn[i] = px[i].a > solid;

        var closed = Erode(Dilate(drawn, w, h, closeRadius), w, h, closeRadius);

        // Flood the empty space inwards from every border pixel.
        var outside = new bool[n];
        var stack = new Stack<int>();
        void Push(int idx) { if (!closed[idx] && !outside[idx]) { outside[idx] = true; stack.Push(idx); } }
        for (int x = 0; x < w; x++) { Push(x); Push((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { Push(y * w); Push(y * w + w - 1); }
        while (stack.Count > 0)
        {
            int idx = stack.Pop();
            int x = idx % w, y = idx / w;
            if (x > 0)     Push(idx - 1);
            if (x < w - 1) Push(idx + 1);
            if (y > 0)     Push(idx - w);
            if (y < h - 1) Push(idx + w);
        }

        var region = new bool[n];
        for (int i = 0; i < n; i++) region[i] = !outside[i] && px[i].a <= solid;
        return region;
    }

    // Separable box morphology: plenty for closing stroke gaps and far cheaper than a disc.
    static bool[] Dilate(bool[] src, int w, int h, int r) => Sweep(src, w, h, r, true);
    static bool[] Erode (bool[] src, int w, int h, int r) => Sweep(src, w, h, r, false);

    static bool[] Sweep(bool[] src, int w, int h, int r, bool dilate)
    {
        var tmp = new bool[src.Length];
        var dst = new bool[src.Length];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool acc = !dilate;
                for (int d = -r; d <= r; d++)
                {
                    int xx = Mathf.Clamp(x + d, 0, w - 1);
                    bool v = src[y * w + xx];
                    acc = dilate ? (acc || v) : (acc && v);
                    if (dilate == acc) break;      // short-circuit once the result is decided
                }
                tmp[y * w + x] = acc;
            }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool acc = !dilate;
                for (int d = -r; d <= r; d++)
                {
                    int yy = Mathf.Clamp(y + d, 0, h - 1);
                    bool v = tmp[yy * w + x];
                    acc = dilate ? (acc || v) : (acc && v);
                    if (dilate == acc) break;
                }
                dst[y * w + x] = acc;
            }
        return dst;
    }

    static void EnsureDir(string dir)
    {
        if (AssetDatabase.IsValidFolder(dir)) return;
        var parts = dir.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{cur}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
