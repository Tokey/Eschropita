using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds the animation clips, animator controller and Chowder Fill material for
/// the player, then wires them onto the selected GameObject.
///
/// Written as a tool rather than as checked-in .anim/.controller assets because
/// those files reference sprites by GUID, and the GUIDs only exist once the art
/// is imported. Re-run it whenever frames are added.
/// </summary>
public class PlayerCharacterSetup : EditorWindow
{
    const string ClipDir = "Assets/Animation/Eschropita";
    const string MatDir  = "Assets/Sprites/Materials";
    const string MatPath = MatDir + "/EschropitaChowderFill.mat";

    [SerializeField] GameObject player;
    [SerializeField] Texture2D  patternTexture;
    [SerializeField] int   frameRate = 12;
    [SerializeField] float patternScale = 6f;
    [SerializeField] bool  colourKey = true;
    [SerializeField] float runThreshold = 0.1f;

    // Group name -> frames, discovered from the selection. Case-insensitive
    // because the art is named inconsistently ("Idle1" but "idle2"), and a
    // case-sensitive key would split one animation into two clips.
    readonly SortedDictionary<string, List<Sprite>> _groups =
        new(System.StringComparer.OrdinalIgnoreCase);
    string _idleGroup, _runGroup;
    Vector2 _scroll;

    [MenuItem("Tools/Eschropita/Set Up Player Character")]
    static void Open()
    {
        var w = GetWindow<PlayerCharacterSetup>(true, "Player Character Setup");
        w.minSize = new Vector2(420, 420);
        w.SeedFromMaterial();
        w.Rescan();
    }

    /// <summary>
    /// Starts from what the material already holds, so rebuilding the clips when
    /// new frames arrive does not reset a pattern tuned in the inspector.
    /// </summary>
    void SeedFromMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null) return;
        if (mat.HasProperty("_PatternTex") && mat.GetTexture("_PatternTex") is Texture2D tex)
            patternTexture = tex;
        if (mat.HasProperty("_PatternScale"))
            patternScale = mat.GetFloat("_PatternScale");
        colourKey = mat.IsKeywordEnabled("_CHROMA_KEY");
    }

    void OnSelectionChange() { Rescan(); Repaint(); }

    /// <summary>
    /// Collects sprites from the selection and buckets them by name with the
    /// trailing frame number stripped, so "Esch_run0..7" becomes one "Esch_run"
    /// group. Handles both loose PNGs and a sliced multi-sprite sheet.
    /// </summary>
    void Rescan()
    {
        _groups.Clear();
        var sprites = new List<Sprite>();

        foreach (var obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;

            if (Directory.Exists(path))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { path }))
                    sprites.AddRange(AssetDatabase.LoadAllAssetsAtPath(
                        AssetDatabase.GUIDToAssetPath(guid)).OfType<Sprite>());
            }
            else
            {
                sprites.AddRange(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>());
            }
        }

        foreach (var s in sprites.Distinct())
        {
            string key = Regex.Replace(s.name, @"[_\s-]*\d+$", "");
            if (key.Length == 0) key = s.name;
            if (!_groups.TryGetValue(key, out var list))
                _groups[key] = list = new List<Sprite>();
            list.Add(s);
        }

        foreach (var list in _groups.Values)
            list.Sort((a, b) => NaturalCompare(a.name, b.name));

        _idleGroup = Guess(_idleGroup, "idle");
        _runGroup  = Guess(_runGroup,  "run", "walk");
    }

    string Guess(string current, params string[] hints)
    {
        if (current != null && _groups.ContainsKey(current)) return current;
        foreach (var h in hints)
        {
            var hit = _groups.Keys.FirstOrDefault(k => k.ToLowerInvariant().Contains(h));
            if (hit != null) return hit;
        }
        return null;
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "1. Lineart on white: run Tools > Eschropita > Bake Silhouette To Alpha on the frames.\n" +
            "   Painted frames with transparency skip this (baking makes the helmet glass opaque); " +
            "set Pixels Per Unit 390.625 and pivot Bottom Center on them instead.\n" +
            "2. Select the frames or their folder here (a sliced sheet also works).\n" +
            "3. Point at the player GameObject and press Build.",
            MessageType.Info);

        player = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Player Object", "The GameObject holding the SpriteRenderer and PlayerController."),
            player, typeof(GameObject), true);

        if (player == null && GUILayout.Button("Find player by tag"))
        {
            player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) Debug.LogWarning("[PlayerSetup] No GameObject tagged 'Player' in the open scene.");
        }

        EditorGUILayout.Space();
        patternTexture = (Texture2D)EditorGUILayout.ObjectField(
            new GUIContent("Pattern Texture", "The swirl the silhouette reveals. Set its wrap mode to Repeat."),
            patternTexture, typeof(Texture2D), false);
        patternScale = EditorGUILayout.FloatField(
            new GUIContent("World Units Per Tile", "Smaller values make the pattern churn faster as you walk."),
            patternScale);
        colourKey = EditorGUILayout.Toggle(
            new GUIContent("Key On Green Suit", "On for painted frames: the green suit shows the pattern. Off for lineart on white: the white fill shows it."),
            colourKey);
        frameRate = EditorGUILayout.IntSlider("Frame Rate", frameRate, 1, 30);
        runThreshold = EditorGUILayout.Slider(
            new GUIContent("Run Threshold", "PlayerController writes normalised input.sqrMagnitude into Speed, so this sits between 0 and 1."),
            runThreshold, 0.01f, 0.9f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Discovered groups ({_groups.Count})", EditorStyles.boldLabel);

        if (_groups.Count == 0)
        {
            EditorGUILayout.HelpBox("Nothing selected, or the selection holds no sprites. " +
                "If your PNGs import as plain textures, set Texture Type to Sprite (2D and UI).",
                MessageType.Warning);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(120));
        foreach (var kv in _groups)
            EditorGUILayout.LabelField($"   {kv.Key}", $"{kv.Value.Count} frame(s)");
        EditorGUILayout.EndScrollView();

        var keys = _groups.Keys.ToArray();
        _idleGroup = Popup("Idle Clip", _idleGroup, keys);
        _runGroup  = Popup("Run Clip",  _runGroup,  keys);

        EditorGUILayout.Space();
        bool ready = player != null && _idleGroup != null && _runGroup != null;
        using (new EditorGUI.DisabledScope(!ready))
            if (GUILayout.Button("Build and wire up player", GUILayout.Height(32)))
                Build();

        if (!ready)
            EditorGUILayout.LabelField("Assign a player object and pick both clips.", EditorStyles.miniLabel);
    }

    static string Popup(string label, string current, string[] keys)
    {
        int i = Mathf.Max(0, System.Array.IndexOf(keys, current));
        return keys[EditorGUILayout.Popup(label, i, keys)];
    }

    void Build()
    {
        Undo.RegisterFullObjectHierarchyUndo(player, "Set Up Player Character");

        var idleClip = BuildClip(_idleGroup, _groups[_idleGroup]);
        var runClip  = BuildClip(_runGroup,  _groups[_runGroup]);
        var controller = BuildController(idleClip, runClip);
        var material = BuildMaterial();

        var sr = player.GetComponent<SpriteRenderer>();
        if (sr == null) sr = Undo.AddComponent<SpriteRenderer>(player);
        // Leave the existing material alone if the shader failed to compile,
        // rather than blanking the renderer.
        if (material != null) sr.sharedMaterial = material;
        sr.sprite = _groups[_idleGroup][0];

        var animator = player.GetComponent<Animator>();
        if (animator == null) animator = Undo.AddComponent<Animator>(player);
        animator.runtimeAnimatorController = controller;

        if (player.GetComponent<ChowderFillCameraSync>() == null)
            Undo.AddComponent<ChowderFillCameraSync>(player);

        // Without this the quad stays at identity rotation facing world +Z while
        // the rig looks from yaw 45, and the character renders ~29% too narrow.
        if (player.GetComponent<SpriteBillboard>() == null)
            Undo.AddComponent<SpriteBillboard>(player);

        EditorUtility.SetDirty(player);
        AssetDatabase.SaveAssets();

        Debug.Log($"[PlayerSetup] Built '{idleClip.name}' ({_groups[_idleGroup].Count}f) and " +
                  $"'{runClip.name}' ({_groups[_runGroup].Count}f), wired onto {player.name}.", player);

        if (patternTexture == null)
            Debug.LogWarning("[PlayerSetup] No pattern texture assigned - the fill will be flat white " +
                             "until you set _PatternTex on the material.", material);
        else
            EnsureRepeatWrap(patternTexture);
    }

    /// <summary>
    /// The pattern is sampled well outside 0-1 as the character walks, so a
    /// clamped texture would smear its edge pixels across the whole silhouette.
    /// </summary>
    static void EnsureRepeatWrap(Texture2D tex)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
        if (importer.wrapMode == TextureWrapMode.Repeat) return;

        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();
        Debug.Log($"[PlayerSetup] Set '{tex.name}' wrap mode to Repeat.", tex);
    }

    AnimationClip BuildClip(string name, List<Sprite> frames)
    {
        EnsureDir(ClipDir);
        string path = $"{ClipDir}/{name}.anim";

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.frameRate = frameRate;

        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        var keys = new ObjectReferenceKeyframe[frames.Count];
        for (int i = 0; i < frames.Count; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / (float)frameRate, value = frames[i] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);
        return clip;
    }

    AnimatorController BuildController(AnimationClip idle, AnimationClip run)
    {
        EnsureDir(ClipDir);
        string path = $"{ClipDir}/Eschropita.controller";

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)
                         ?? AnimatorController.CreateAnimatorControllerAtPath(path);

        // Rebuild the state machine so re-running does not stack duplicates.
        var sm = controller.layers[0].stateMachine;
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
        foreach (var p in controller.parameters.ToArray()) controller.RemoveParameter(p);

        // PlayerController.cs writes this every frame; the name must match.
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

        var idleState = sm.AddState("Idle");
        idleState.motion = idle;
        var runState = sm.AddState("Run");
        runState.motion = run;
        sm.defaultState = idleState;

        var toRun = idleState.AddTransition(runState);
        toRun.hasExitTime = false;
        toRun.duration = 0f;
        toRun.AddCondition(AnimatorConditionMode.Greater, runThreshold, "Speed");

        var toIdle = runState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0f;
        toIdle.AddCondition(AnimatorConditionMode.Less, runThreshold, "Speed");

        EditorUtility.SetDirty(controller);
        return controller;
    }

    Material BuildMaterial()
    {
        EnsureDir(MatDir);
        string path = MatPath;

        var shader = Shader.Find("Eschropita/Chowder Fill");
        if (shader == null)
        {
            Debug.LogError("[PlayerSetup] Shader 'Eschropita/Chowder Fill' not found. " +
                           "Make sure EschropitaChowderFill.shader compiled without errors.");
            return null;
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        if (patternTexture != null) mat.SetTexture("_PatternTex", patternTexture);
        mat.SetFloat("_PatternScale", patternScale);
        // The [Toggle] drawer only syncs the keyword when edited in the
        // inspector, so set both or the float and the variant disagree.
        mat.SetFloat("_ChromaKey", colourKey ? 1f : 0f);
        if (colourKey) mat.EnableKeyword("_CHROMA_KEY");
        else           mat.DisableKeyword("_CHROMA_KEY");

        // Seed the basis from the rig so it looks right before ChowderFillCameraSync runs.
        var rig = Object.FindAnyObjectByType<IsoFollowCamera>();
        if (rig != null)
        {
            mat.SetFloat("_CamYaw", rig.yaw);
            mat.SetFloat("_CamPitch", rig.pitch);
        }

        EditorUtility.SetDirty(mat);
        return mat;
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

    /// <summary>Orders run2 before run10, which a plain string sort gets wrong.</summary>
    static int NaturalCompare(string a, string b)
    {
        var na = Regex.Match(a, @"\d+$");
        var nb = Regex.Match(b, @"\d+$");
        if (na.Success && nb.Success)
        {
            string pa = a.Substring(0, na.Index), pb = b.Substring(0, nb.Index);
            // Ignore case here too, so "Idle1" and "idle2" compare as one series.
            int prefix = string.Compare(pa, pb, System.StringComparison.OrdinalIgnoreCase);
            if (prefix != 0) return prefix;
            return int.Parse(na.Value).CompareTo(int.Parse(nb.Value));
        }
        return string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
    }
}
