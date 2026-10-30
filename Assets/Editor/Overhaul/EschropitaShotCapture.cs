using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Headless screenshot tool for checking the look of the level without opening the editor UI.
///
///   Unity.exe -batchmode -projectPath . -executeMethod EschropitaShotCapture.CaptureEditMode
///             -shotOut &lt;dir&gt; [-shotSpec &lt;json&gt;] [-shotScene &lt;scene path&gt;] [-quit]
///
/// Without a spec it captures automatic shots: the scene's main camera, a top-down orthographic map of the level,
/// an oblique overview, and a close-up of every gameplay landmark it can find. A JSON spec adds explicit shots:
///   { "width":1600, "height":900, "warmupFrames":48, "auto":true,
///     "shots":[ { "name":"grove", "pos":[0,12,-14], "lookAt":[0,0,0], "fov":35 } ] }
/// The scene is opened in memory only and never saved.
/// Also available from the menu: Tools ▸ Eschropita ▸ Capture Review Screenshots.
/// </summary>
public static class EschropitaShotCapture
{
    [Serializable]
    public class Shot
    {
        public string name;
        public float[] pos;
        public float[] lookAt;
        public float fov = 35f;
        public bool ortho;
        public float orthoSize = 50f;
    }

    [Serializable]
    public class Spec
    {
        public int width = 1600;
        public int height = 900;
        public int warmupFrames = 48;
        public bool auto = true;
        public Shot[] shots = new Shot[0];
    }

    static readonly string[] LandmarkTypes =
    {
        "PlayerController", "MartianSequencePuzzle", "MushroomFieldTrigger", "TaskSite", "Obelisk", "ObeliskShard",
        "BunkerTrigger", "TornadoCalamity", "CheckpointManager", "NPCManager", "LifeSupport", "GameFlowManager",
        "JoyTechLevel", "WindTurbine", "LifeSupportPlant"
    };

    static readonly string[] LandmarkNameHints =
    {
        "wind", "mill", "turbine", "obelisk", "bunker", "dome", "door", "grove", "mushroom", "floor", "ground",
        "shuttle", "ship", "hangar", "lab", "tank", "solar", "spawn", "site", "terrain", "joytech", "life"
    };

    [MenuItem("Tools/Eschropita/Capture Review Screenshots")]
    static void CaptureFromMenu()
    {
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Temp", "ReviewShots");
        RunCapture(dir, new Spec(), keepSceneOpen: true);
        EditorUtility.RevealInFinder(dir);
    }

    public static void CaptureEditMode()
    {
        string outDir = Arg("-shotOut") ?? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Temp", "ReviewShots");
        string specPath = Arg("-shotSpec");
        string scenePath = Arg("-shotScene") ?? "Assets/Main Mars Scene.unity";

        var spec = new Spec();
        if (!string.IsNullOrEmpty(specPath) && File.Exists(specPath))
            spec = JsonUtility.FromJson<Spec>(File.ReadAllText(specPath)) ?? new Spec();

        try
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            RunCapture(outDir, spec, keepSceneOpen: false);
        }
        catch (Exception e)
        {
            Debug.LogError("[ShotCapture] failed: " + e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    static void RunCapture(string outDir, Spec spec, bool keepSceneOpen)
    {
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder();
        var shots = new List<Shot>();

        Camera main = Camera.main;
        if (main == null) main = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (main == null)
        {
            var go = new GameObject("ShotCaptureCamera");
            main = go.AddComponent<Camera>();
        }
        report.AppendLine($"main camera: {HierPath(main.transform)} pos={main.transform.position} rot={main.transform.eulerAngles} fov={main.fieldOfView} ortho={main.orthographic} size={main.orthographicSize}");

        Bounds level = LevelBounds(report);
        report.AppendLine($"level bounds (non-terrain renderers): center={level.center} size={level.size}");

        var landmarks = FindLandmarks(report);

        if (spec.auto)
        {
            shots.Add(new Shot { name = "00_main_camera", pos = V(main.transform.position), lookAt = V(main.transform.position + main.transform.forward * 10f), fov = main.fieldOfView, ortho = main.orthographic, orthoSize = main.orthographicSize });
            float half = Mathf.Max(level.extents.x, level.extents.z) * 1.08f + 4f;
            shots.Add(new Shot { name = "01_topdown_map", pos = V(level.center + Vector3.up * (level.extents.y + 200f)), lookAt = V(level.center), ortho = true, orthoSize = half });
            Vector3 iso = main.transform.forward;
            iso.y = Mathf.Min(iso.y, -0.45f);
            iso.Normalize();
            float dist = Mathf.Max(level.extents.x, level.extents.z) * 2.4f + 20f;
            shots.Add(new Shot { name = "02_overview_iso", pos = V(level.center - iso * dist), lookAt = V(level.center), fov = 40f });
            Vector3 low = new Vector3(iso.x, -0.18f, iso.z).normalized;
            shots.Add(new Shot { name = "03_overview_low", pos = V(level.center - low * dist * 0.75f + Vector3.up * 6f), lookAt = V(level.center), fov = 45f });

            int idx = 10;
            foreach (var kv in landmarks)
            {
                Vector3 p = kv.Value;
                shots.Add(new Shot { name = $"{idx++:00}_{Sanitize(kv.Key)}", pos = V(p - iso * 26f), lookAt = V(p), fov = 35f });
                if (idx > 60) break;
            }
        }
        if (spec.shots != null) shots.AddRange(spec.shots);

        var rt = new RenderTexture(spec.width, spec.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
        rt.Create();
        var tex = new Texture2D(spec.width, spec.height, TextureFormat.RGB24, false);
        var prevTarget = main.targetTexture;
        var prevPos = main.transform.position;
        var prevRot = main.transform.rotation;
        float prevFov = main.fieldOfView, prevSize = main.orthographicSize, prevFar = main.farClipPlane;
        bool prevOrtho = main.orthographic;

        try
        {
            foreach (var s in shots)
            {
                if (s.pos == null || s.pos.Length < 3 || s.lookAt == null || s.lookAt.Length < 3) continue;
                Vector3 pos = new Vector3(s.pos[0], s.pos[1], s.pos[2]);
                Vector3 look = new Vector3(s.lookAt[0], s.lookAt[1], s.lookAt[2]);
                Vector3 dir = look - pos;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                Vector3 up = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
                main.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, up));
                main.orthographic = s.ortho;
                main.orthographicSize = s.orthoSize;
                main.fieldOfView = s.fov;
                main.farClipPlane = Mathf.Max(prevFar, dir.magnitude + 600f);
                main.targetTexture = rt;
                for (int i = 0; i < Mathf.Max(1, spec.warmupFrames); i++) main.Render();
                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, spec.width, spec.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                string file = System.IO.Path.Combine(outDir, Sanitize(s.name) + ".png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                report.AppendLine($"shot {s.name}: pos={pos} look={look} ortho={s.ortho} -> {file}");
            }
        }
        finally
        {
            main.targetTexture = prevTarget;
            main.transform.SetPositionAndRotation(prevPos, prevRot);
            main.fieldOfView = prevFov;
            main.orthographic = prevOrtho;
            main.orthographicSize = prevSize;
            main.farClipPlane = prevFar;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        File.WriteAllText(System.IO.Path.Combine(outDir, "capture_report.txt"), report.ToString());
        Debug.Log("[ShotCapture] wrote " + shots.Count + " shots to " + outDir);
    }

    static Bounds LevelBounds(StringBuilder report)
    {
        bool any = false;
        Bounds b = new Bounds();
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            var rb = r.bounds;
            if (rb.size.x > 300f || rb.size.z > 300f) continue;
            if (!any) { b = rb; any = true; }
            else b.Encapsulate(rb);
        }
        foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            report.AppendLine($"terrain {HierPath(t.transform)} pos={t.transform.position} size={(t.terrainData ? t.terrainData.size : Vector3.zero)}");
        if (!any) b = new Bounds(Vector3.zero, new Vector3(100, 10, 100));
        return b;
    }

    static List<KeyValuePair<string, Vector3>> FindLandmarks(StringBuilder report)
    {
        var found = new List<KeyValuePair<string, Vector3>>();
        var seen = new HashSet<GameObject>();
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (!LandmarkTypes.Contains(tn)) continue;
            if (!seen.Add(mb.gameObject)) continue;
            report.AppendLine($"landmark[{tn}] {HierPath(mb.transform)} pos={mb.transform.position} active={mb.gameObject.activeInHierarchy}");
            if (mb.gameObject.activeInHierarchy && found.Count < 40)
                found.Add(new KeyValuePair<string, Vector3>(tn + "_" + mb.gameObject.name, mb.transform.position));
        }
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string n = t.name.ToLowerInvariant();
            if (!LandmarkNameHints.Any(h => n.Contains(h))) continue;
            report.AppendLine($"named {HierPath(t)} pos={t.position} scale={t.lossyScale} active={t.gameObject.activeInHierarchy} renderers={t.GetComponentsInChildren<Renderer>(true).Length}");
        }
        return found;
    }

    static string HierPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
        return sb.ToString();
    }

    static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };

    static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }
}
