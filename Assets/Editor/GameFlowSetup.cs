using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// One-click wiring of the scripted game flow into the open scene: creates the
/// "GameFlow" root with every flow singleton, retires the legacy UI canvas,
/// drops in a dummy Obelisk, and tunes the windmill / NPCManager / tornado
/// values the flow expects.
///
/// Written as a tool rather than as a checked-in prefab because the flow needs
/// references to scene objects (player, windmill, tornado...) that a prefab
/// cannot hold. Safe to re-run: everything is looked up before it is created,
/// and hand-made assignments are only overwritten when a scene object is found.
/// </summary>
public static class GameFlowSetup
{
    const string SetupMenu  = "Tools/Eschropita/Set Up Game Flow In Scene";
    const string ToggleMenu = "Tools/Eschropita/Game Flow ▸ Start From Sandbox (toggle)";
    const string UndoLabel  = "Set Up Game Flow";
    const string LogPrefix  = "[GameFlowSetup]";

    const string RootName      = "GameFlow";
    const string HudName       = "HUD";
    const string ObeliskName   = "Obelisk";
    const string ViewpointName = "NewLocationViewpoint";
    const string PlayerTag     = "Player";

    // The legacy canvas is recognised by these children. The UIManager script
    // that drove them has been deleted, so its type cannot serve as the marker.
    static readonly string[] LegacyUiChildren = { "DaffodilEnergyBar", "WindmillHealth" };

    // Obelisk placement relative to the windmill, from the design notes.
    static readonly Vector3 ObeliskOffsetFromWindmill = new(-2f, 0f, -26f);
    const float ViewpointDistance = 12f;   // metres beyond the obelisk, away from the windmill
    const float ViewpointHeight   = 2f;
    const float NavMeshSnapRadius = 30f;
    const float GroundRayHeight   = 50f;

    // Where the windmill sits in Main Mars Scene; only used when no PowerPlant
    // TaskSite exists so the obelisk still lands somewhere sensible.
    static readonly Vector3 FallbackWindmillPos = new(-12.6f, 8.3f, 13.2f);

    // ------------------------------------------------------------------ menu

    [MenuItem(SetupMenu)]
    static void SetUpGameFlow()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning($"{LogPrefix} Exit play mode before running the setup.");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogWarning($"{LogPrefix} No loaded scene is active.");
            return;
        }

        // One undo step for the whole operation.
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        var report = new Report();

        // 1. Root object and the flow singletons.
        GameObject root = EnsureRoot(scene, report);
        var flow        = Ensure<GameFlowManager>(root, report);
        var lifeSupport = EnsureSingleton<LifeSupport>(root, report);
        var checkpoints = EnsureSingleton<CheckpointManager>(root, report);
        var dialogue    = EnsureSingleton<DialogueSystem>(root, report);
        var video       = EnsureVideo(root, report);
        report.Done($"Root '{root.name}' ready");

        // The HUD drives dialogue now; the old start sequence must not auto-play.
        SetSerializedBool(dialogue, "playOnStart", false, report);

        var hud = EnsureHud(root, report);
        report.Done($"HUD '{hud.gameObject.name}' ready");

        // 2. Retire the old canvas before the HUD ever builds next to it.
        RemoveLegacyUi(scene, hud, report);

        // 3. Scene actors the flow scripts against.
        var player     = FindPlayer(report);
        var daffodil   = FindSingle<FollowPlayer>("Daffodil", report);
        var windmill   = FindWindmill(report);
        var puzzle     = FindSingle<MartianSequencePuzzle>("the sequence puzzle", report);
        var mushroom   = FindSingle<MushroomFieldTrigger>("the mushroom field", report);
        var tornado    = FindSingle<TornadoCalamity>("the tornado", report);
        var npcManager = FindSingle<NPCManager>("NPCManager", report);
        var cameraRig  = FindSingle<IsoFollowCamera>("the camera rig", report);

        // 4. Dummy obelisk plus the viewpoint the flow pans to.
        var obelisk   = EnsureObelisk(scene, windmill, report);
        var viewpoint = EnsureViewpoint(obelisk, windmill, report);
        var bunker    = FindBunker(obelisk);
        report.Done($"Obelisk '{obelisk.gameObject.name}' ready");

        WireFlowManager(flow, report, new (string field, Object value, bool optional)[]
        {
            ("player",               player,      false),
            ("daffodil",             daffodil,    false),
            ("windmill",             windmill,    false),
            ("puzzle",               puzzle,      false),
            ("mushroomField",        mushroom,    false),
            ("tornado",              tornado,     false),
            ("npcManager",           npcManager,  false),
            ("obelisk",              obelisk,     false),
            // The Obelisk builds its bunker at runtime, so an empty slot here is normal.
            ("bunker",               bunker,      true),
            ("newLocationViewpoint", viewpoint,   false),
            ("cameraRig",            cameraRig,   false),
            ("hud",                  hud,         false),
            ("dialogue",             dialogue,    false),
            ("video",                video,       false),
            ("lifeSupport",          lifeSupport, false),
            ("checkpoints",          checkpoints, false),
        });

        // 5-8. Gameplay values the flow relies on.
        TuneWindmill(windmill, report);
        TuneNpcManager(npcManager, report);
        TuneTornado(tornado, report);
        EnsurePlayerTag(player, report);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;

        string warnings = report.Warnings > 0
            ? $" {report.Warnings} warning(s) logged above need a manual fix."
            : " No warnings.";
        Debug.Log($"{LogPrefix} {report}{warnings}", root);
    }

    /// <summary>
    /// Flips GameFlowManager.debugStartPhase between IntroVideo and Sandbox so
    /// the sandbox can be tested without sitting through the scripted intro.
    /// </summary>
    [MenuItem(ToggleMenu)]
    static void ToggleStartFromSandbox()
    {
        var flow = Object.FindAnyObjectByType<GameFlowManager>(FindObjectsInactive.Include);
        if (flow == null)
        {
            Debug.LogWarning($"{LogPrefix} No GameFlowManager in the open scene. Run '{SetupMenu}' first.");
            return;
        }

        var so = new SerializedObject(flow);
        var prop = so.FindProperty("debugStartPhase");
        if (prop == null)
        {
            Debug.LogWarning($"{LogPrefix} GameFlowManager has no serialized field 'debugStartPhase'; nothing toggled.", flow);
            return;
        }

        bool fromSandbox = prop.intValue == (int)GamePhase.Sandbox;
        prop.intValue = (int)(fromSandbox ? GamePhase.IntroVideo : GamePhase.Sandbox);
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);

        Debug.Log($"{LogPrefix} debugStartPhase = {(GamePhase)prop.intValue}. " +
                  (fromSandbox ? "Play mode runs the full flow from the intro."
                               : "Play mode skips straight to the sandbox."), flow);
    }

    // Greys the item out without a manager and shows a tick while Sandbox is set.
    [MenuItem(ToggleMenu, true)]
    static bool ValidateToggleStartFromSandbox()
    {
        var flow = Object.FindAnyObjectByType<GameFlowManager>(FindObjectsInactive.Include);
        bool sandbox = false;
        if (flow != null)
        {
            var prop = new SerializedObject(flow).FindProperty("debugStartPhase");
            sandbox = prop != null && prop.intValue == (int)GamePhase.Sandbox;
        }
        Menu.SetChecked(ToggleMenu, sandbox);
        return flow != null;
    }

    // ------------------------------------------------------------ step 1: root

    /// <summary>
    /// Reuses an existing manager wherever it lives (a renamed root must not be
    /// duplicated), then a root object called "GameFlow", and only then creates one.
    /// </summary>
    static GameObject EnsureRoot(Scene scene, Report report)
    {
        var existing = Object.FindAnyObjectByType<GameFlowManager>(FindObjectsInactive.Include);
        if (existing != null) return existing.gameObject;

        var named = FindSceneRoot(scene, RootName);
        if (named != null) return named;

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, UndoLabel);
        root.transform.position = Vector3.zero;
        report.Note($"created '{RootName}' at the origin");
        return root;
    }

    /// <summary>
    /// The video player needs its VideoPlayer on the same object, so both are
    /// ensured on whichever object already hosts the cutscene player.
    /// </summary>
    static CutsceneVideoPlayer EnsureVideo(GameObject root, Report report)
    {
        var cutscene = Object.FindAnyObjectByType<CutsceneVideoPlayer>(FindObjectsInactive.Include);
        GameObject host = cutscene != null ? cutscene.gameObject : root;

        var videoPlayer = Ensure<VideoPlayer>(host, report);
        if (videoPlayer.playOnAwake)
            Modify(videoPlayer, vp => vp.playOnAwake = false);

        return cutscene != null ? cutscene : Ensure<CutsceneVideoPlayer>(host, report);
    }

    /// <summary>
    /// GameHUD builds its widgets at runtime but reuses a Canvas that is already
    /// on its object, so the canvas is pre-configured here to the HUD's spec.
    /// </summary>
    static GameHUD EnsureHud(GameObject root, Report report)
    {
        var existing = Object.FindAnyObjectByType<GameHUD>(FindObjectsInactive.Include);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            var child = root.transform.Find(HudName);
            if (child != null)
            {
                go = child.gameObject;
            }
            else
            {
                go = new GameObject(HudName);
                Undo.RegisterCreatedObjectUndo(go, UndoLabel);
                Undo.SetTransformParent(go.transform, root.transform, UndoLabel);
                report.Note($"created '{HudName}' under '{root.name}'");
            }
        }

        int uiLayer = LayerMask.NameToLayer("UI");
        if (go.layer != uiLayer)
            Modify(go, g => g.layer = uiLayer);

        var canvas = Ensure<Canvas>(go, report);
        Modify(canvas, c => c.renderMode = RenderMode.ScreenSpaceOverlay);

        var scaler = Ensure<CanvasScaler>(go, report);
        Modify(scaler, s =>
        {
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920f, 1080f);
            s.matchWidthOrHeight = 0.5f;
        });

        Ensure<GraphicRaycaster>(go, report);
        return existing != null ? existing : Ensure<GameHUD>(go, report);
    }

    // ------------------------------------------------------- step 2: legacy UI

    /// <summary>
    /// Destroys every canvas that still carries the old bars. Outermost canvases
    /// go first so a nested canvas is removed with its parent rather than
    /// leaving the parent behind.
    /// </summary>
    static void RemoveLegacyUi(Scene scene, GameHUD hud, Report report)
    {
        var canvases = new List<Canvas>(
            Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
        canvases.Sort((a, b) => Depth(a.transform).CompareTo(Depth(b.transform)));

        var removed = new List<string>();
        foreach (var canvas in canvases)
        {
            // Already destroyed along with an ancestor in this loop.
            if (canvas == null) continue;
            if (canvas.gameObject.scene != scene) continue;
            if (hud != null && canvas.transform.IsChildOf(hud.transform)) continue;
            if (!HasLegacyChild(canvas.transform)) continue;

            removed.Add(canvas.gameObject.name);
            Debug.Log($"{LogPrefix} Removed legacy UI canvas '{canvas.gameObject.name}'.");
            Undo.DestroyObjectImmediate(canvas.gameObject);
        }

        report.Note(removed.Count > 0
            ? $"removed legacy UI canvas '{string.Join("', '", removed)}'"
            : "no legacy UI canvas present");
    }

    static bool HasLegacyChild(Transform canvasRoot)
    {
        foreach (var t in canvasRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t == canvasRoot) continue;
            foreach (var marker in LegacyUiChildren)
                if (t.name == marker) return true;
        }
        return false;
    }

    static int Depth(Transform t)
    {
        int depth = 0;
        for (var p = t.parent; p != null; p = p.parent) depth++;
        return depth;
    }

    // --------------------------------------------------------- step 3: wiring

    /// <summary>
    /// Assigns the manager's private [SerializeField] references by name. A field
    /// the manager does not (yet) declare or a reference of the wrong type is
    /// reported, never thrown, so a partially integrated project still sets up.
    /// </summary>
    static void WireFlowManager(GameFlowManager flow, Report report,
                                (string field, Object value, bool optional)[] refs)
    {
        var so = new SerializedObject(flow);
        var assigned = new List<(string field, Object value)>();
        var missing  = new List<string>();

        foreach (var (field, value, optional) in refs)
        {
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                report.Warn($"GameFlowManager has no serialized field '{field}'; skipped.", flow);
                continue;
            }

            if (value == null)
            {
                // Keep whatever was assigned by hand; only complain when the slot is empty.
                if (prop.objectReferenceValue == null && !optional) missing.Add(field);
                continue;
            }

            prop.objectReferenceValue = value;
            assigned.Add((field, value));
        }
        so.ApplyModifiedProperties();

        // Unity silently drops a reference whose type does not match the field,
        // so read back what actually landed.
        so.Update();
        int wired = 0;
        foreach (var (field, value) in assigned)
        {
            if (so.FindProperty(field).objectReferenceValue == value) wired++;
            else report.Warn($"GameFlowManager.{field} rejected a {value.GetType().Name}: field type mismatch.", flow);
        }

        foreach (var field in missing)
            report.Warn($"GameFlowManager.{field}: nothing found in the scene to assign; set it by hand.", flow);

        report.Note($"wired {wired}/{refs.Length} GameFlowManager references" +
                    (missing.Count > 0 ? $" (unassigned: {string.Join(", ", missing)})" : ""));
    }

    static PlayerController FindPlayer(Report report)
    {
        var tagged = GameObject.FindGameObjectWithTag(PlayerTag);
        var player = tagged != null ? tagged.GetComponent<PlayerController>() : null;
        // Tag missing or object inactive: fall back to the component itself.
        if (player == null)
            player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (player == null)
            report.Warn("No PlayerController in the scene; the player cannot be wired.");
        return player;
    }

    static TaskSite FindWindmill(Report report)
    {
        foreach (var site in Object.FindObjectsByType<TaskSite>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            if (site.type == TaskType.PowerPlant) return site;

        report.Warn("No TaskSite of type PowerPlant (the windmill) in the scene; " +
                    $"the obelisk is placed relative to {FallbackWindmillPos} instead.");
        return null;
    }

    static BunkerTrigger FindBunker(Obelisk obelisk)
    {
        if (obelisk != null)
        {
            var own = obelisk.GetComponentInChildren<BunkerTrigger>(true);
            if (own != null) return own;
        }
        return Object.FindAnyObjectByType<BunkerTrigger>(FindObjectsInactive.Include);
    }

    static T FindSingle<T>(string description, Report report) where T : Component
    {
        var found = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
        if (found == null)
            report.Warn($"No {typeof(T).Name} ({description}) in the scene.");
        return found;
    }

    // -------------------------------------------------------- step 4: obelisk

    static Obelisk EnsureObelisk(Scene scene, TaskSite windmill, Report report)
    {
        var existing = Object.FindAnyObjectByType<Obelisk>(FindObjectsInactive.Include);
        GameObject go = existing != null ? existing.gameObject : FindSceneRoot(scene, ObeliskName);

        if (go == null)
        {
            Vector3 windmillPos = windmill != null ? windmill.transform.position : FallbackWindmillPos;
            Vector3 pos = SnapToGround(windmillPos + ObeliskOffsetFromWindmill, out string how, report);

            go = new GameObject(ObeliskName);
            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
            go.transform.position = pos;
            report.Note($"created '{ObeliskName}' at {Fmt(pos)} ({how})");
        }

        // The Obelisk component enforces its own rules in Awake; these are the
        // same values so the inspector tells the truth before play mode too.
        var site = Ensure<TaskSite>(go, report);
        Modify(site, s =>
        {
            s.type            = TaskType.Obelisk;
            s.displayName     = "Obelisk";
            s.allowRepair     = false;
            s.allowDismantle  = true;
            s.tornadoCanDamage = false;
            s.canBuildShield  = false;
            s.workRadius      = 4.5f;
        });

        // Tall capsule so scans and Mycari path-finding treat the whole pillar as the site.
        var collider = Ensure<CapsuleCollider>(go, report);
        Modify(collider, c =>
        {
            c.direction = 1; // Y axis
            c.radius    = 1.2f;
            c.height    = 7f;
            c.center    = new Vector3(0f, 3.5f, 0f);
        });

        // The Obelisk drives volume itself; this just makes it a looping 3D source.
        var audio = Ensure<AudioSource>(go, report);
        Modify(audio, a =>
        {
            a.playOnAwake  = false;
            a.loop         = true;
            a.spatialBlend = 1f;
            a.minDistance  = 6f;
            a.maxDistance  = 60f;
        });

        return existing != null ? existing : Ensure<Obelisk>(go, report);
    }

    /// <summary>
    /// Snaps onto the NavMesh so Mycari can reach the site, or onto whatever the
    /// ground collider is when the mesh has not been baked yet.
    /// </summary>
    static Vector3 SnapToGround(Vector3 pos, out string how, Report report)
    {
        if (NavMesh.SamplePosition(pos, out var navHit, NavMeshSnapRadius, NavMesh.AllAreas))
        {
            how = "snapped to NavMesh";
            return navHit.position;
        }

        if (Physics.Raycast(pos + Vector3.up * GroundRayHeight, Vector3.down, out var rayHit,
                            GroundRayHeight * 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            how = $"snapped to '{rayHit.collider.name}'";
            return rayHit.point;
        }

        report.Warn($"Could not snap the obelisk to the NavMesh or any ground collider; left at {Fmt(pos)}.");
        how = "unsnapped";
        return pos;
    }

    /// <summary>
    /// Empty child the flow pans to when the obelisk is revealed: twelve metres
    /// past the obelisk on the line from the windmill, raised a little.
    /// </summary>
    static Transform EnsureViewpoint(Obelisk obelisk, TaskSite windmill, Report report)
    {
        if (obelisk == null) return null;

        var parent = obelisk.transform;
        var existing = parent.Find(ViewpointName);
        if (existing != null) return existing;

        Vector3 windmillPos = windmill != null ? windmill.transform.position : FallbackWindmillPos;
        Vector3 away = parent.position - windmillPos;
        away.y = 0f;
        // Only degenerate if the obelisk sits on top of the windmill; keep the design's "behind" direction.
        away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.back;

        var go = new GameObject(ViewpointName);
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        Undo.SetTransformParent(go.transform, parent, UndoLabel);
        go.transform.position = parent.position + away * ViewpointDistance + Vector3.up * ViewpointHeight;
        // Face the obelisk so anything parked here looks at it.
        go.transform.rotation = Quaternion.LookRotation(-away, Vector3.up);

        report.Note($"created '{ViewpointName}' at {Fmt(go.transform.position)}");
        return go.transform;
    }

    // ------------------------------------------------------- steps 5-8: tuning

    static void TuneWindmill(TaskSite windmill, Report report)
    {
        if (windmill == null) return;
        Modify(windmill, w =>
        {
            w.workDuration   = 0f;   // Mycari stay until the repair is actually done
            w.canBuildShield = true;
            w.maxShield      = 100f;
            w.displayName    = "Windmill";
        });
        report.Note($"tuned windmill '{windmill.gameObject.name}' (work until done, shield 100)");
    }

    static void TuneNpcManager(NPCManager npcManager, Report report)
    {
        if (npcManager == null) return;
        Modify(npcManager, m =>
        {
            m.autoSpawnOnStart = false;  // the flow spawns Mycari when the story calls for them
            m.workerCount      = 3;
            m.attackerCount    = 3;
            m.wandererCount    = 2;
        });
        report.Note("tuned NPCManager (auto-spawn off, 3 fixers / 3 breakers / 2 wanderers)");
    }

    static void TuneTornado(TornadoCalamity tornado, Report report)
    {
        if (tornado == null) return;
        Modify(tornado, t =>
        {
            t.suppressed         = true;   // released by the flow at TornadoIntro
            t.minDamageRadius    = 3.5f;
            t.minRespawnDelay    = 20f;
            t.maxRespawnDelay    = 45f;
            t.centerEffectOnRoot = true;
        });
        report.Note("tuned tornado (suppressed, respawn 20-45 s)");
    }

    static void EnsurePlayerTag(PlayerController player, Report report)
    {
        if (player == null) return;
        if (player.CompareTag(PlayerTag))
        {
            report.Note("player tag OK");
            return;
        }
        Modify(player.gameObject, go => go.tag = PlayerTag);
        report.Note($"tagged '{player.gameObject.name}' as {PlayerTag}");
    }

    // ---------------------------------------------------------------- helpers

    static T Ensure<T>(GameObject go, Report report) where T : Component
    {
        var component = go.GetComponent<T>();
        if (component != null) return component;
        component = Undo.AddComponent<T>(go);
        report.Added(typeof(T).Name);
        return component;
    }

    /// <summary>Uses a singleton that already lives anywhere in the scene before adding one to the root.</summary>
    static T EnsureSingleton<T>(GameObject root, Report report) where T : Component
    {
        var existing = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
        if (existing == null) return Ensure<T>(root, report);
        if (existing.gameObject != root)
            report.Note($"using the existing {typeof(T).Name} on '{existing.gameObject.name}'");
        return existing;
    }

    /// <summary>
    /// Records undo, applies the edit and keeps the change as an override on
    /// prefab instances (the windmill and tornado are ones), which RecordObject
    /// alone does not guarantee for direct field writes.
    /// </summary>
    static void Modify<T>(T target, System.Action<T> edit) where T : Object
    {
        Undo.RecordObject(target, UndoLabel);
        edit(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target))
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        EditorUtility.SetDirty(target);
    }

    static void SetSerializedBool(Object target, string field, bool value, Report report)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            report.Warn($"{target.GetType().Name} has no serialized field '{field}'; skipped.", target);
            return;
        }
        if (prop.boolValue == value) return;
        prop.boolValue = value;
        so.ApplyModifiedProperties();
        report.Note($"{target.GetType().Name}.{field} = {value.ToString().ToLowerInvariant()}");
    }

    static GameObject FindSceneRoot(Scene scene, string name)
    {
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }

    static string Fmt(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";

    /// <summary>
    /// Collects what happened into one paragraph for the final log line, and
    /// counts warnings (which are logged immediately, with their context object).
    /// </summary>
    sealed class Report
    {
        readonly StringBuilder _text = new();
        readonly List<string> _added = new();

        public int Warnings { get; private set; }

        public void Added(string componentName) => _added.Add(componentName);

        /// <summary>Closes a section, folding in the components added since the previous one.</summary>
        public void Done(string subject)
        {
            _text.Append(subject);
            if (_added.Count > 0)
            {
                _text.Append(" (added ").Append(string.Join(", ", _added)).Append(')');
                _added.Clear();
            }
            _text.Append(". ");
        }

        public void Note(string sentence)
        {
            _text.Append(char.ToUpperInvariant(sentence[0])).Append(sentence, 1, sentence.Length - 1).Append(". ");
        }

        public void Warn(string message, Object context = null)
        {
            Warnings++;
            Debug.LogWarning($"{LogPrefix} {message}", context);
        }

        public override string ToString() => _text.ToString().TrimEnd();
    }
}
