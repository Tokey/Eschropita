using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eschropita.Overhaul.EditorTools
{
    /// <summary>
    /// Marks a static method <c>static void Build(OverhaulBuildContext ctx)</c> as one step of the level-dressing
    /// bake. Steps run in ascending Order; a step that throws is logged and skipped (the rest still build).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class OverhaulBuildStepAttribute : Attribute
    {
        public int Order { get; }
        public string Name { get; }
        public OverhaulBuildStepAttribute(int order, string name) { Order = order; Name = name; }
    }

    /// <summary>
    /// Everything a build step needs: the generated root, landmark positions read from the live scene (so nothing is
    /// hard-coded and designers can move landmarks and rebuild), and helpers to hide original visuals reversibly.
    /// </summary>
    public sealed class OverhaulBuildContext
    {
        public Transform Root;
        public OverhaulGeneratedRoot Record;
        public Scene Scene;
        public readonly StringBuilder Log = new StringBuilder();

        // Gameplay landmarks (world, Y on the terrain)
        public Vector3 Spawn, GroveCenter, Windmill, Obelisk, Home, TornadoAnchor;
        // New structures (decided once here so every builder agrees)
        public Vector3 DomeCenter, LifeSupportPlant, Shuttle;
        public float DomeRadius = 10f;
        /// <summary>Unit XZ direction from the dome centre towards its door (the door is at <see cref="Home"/>).</summary>
        public Vector3 DoorFacing;

        public PlayerController Player;
        public MushroomFieldTrigger Grove;
        public TaskSite WindmillSite;
        public global::Obelisk ObeliskComp;
        public GameObject HomeObject;
        public MartianSequencePuzzle Puzzle;

        public Transform Group(string name) => PlaceKit.Group(Root, name);

        public System.Random Rng(int salt) => new System.Random(1234567 + salt * 7919);

        /// <summary>Disables renderers under <paramref name="go"/> (recorded for Remove). filter = null → all.</summary>
        public int HideRenderers(GameObject go, Func<Renderer, bool> filter = null)
        {
            if (go == null) return 0;
            int n = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                if (filter != null && !filter(r)) continue;
                r.enabled = false;
                Record.hiddenRenderers.Add(r);
                n++;
            }
            return n;
        }

        /// <summary>Disables colliders under <paramref name="go"/> (recorded). nonTriggerOnly keeps gameplay triggers.</summary>
        public int DisableColliders(GameObject go, bool nonTriggerOnly = true, bool includeChildren = true)
        {
            if (go == null) return 0;
            int n = 0;
            var cols = includeChildren ? go.GetComponentsInChildren<Collider>(true) : go.GetComponents<Collider>();
            foreach (var c in cols)
            {
                if (!c.enabled) continue;
                if (nonTriggerOnly && c.isTrigger) continue;
                c.enabled = false;
                Record.disabledColliders.Add(c);
                n++;
            }
            return n;
        }

        public void Deactivate(GameObject go)
        {
            if (go == null || !go.activeSelf) return;
            go.SetActive(false);
            Record.deactivatedObjects.Add(go);
        }

        public void Info(string msg) { Log.AppendLine(msg); }
    }

    /// <summary>
    /// Bakes the overhaul's level dressing into the open scene under "== JoyTech Overhaul (generated) ==".
    /// Tools ▸ Eschropita ▸ Overhaul ▸ Build Level Dressing (re-runnable: it removes the previous bake first) and
    /// ▸ Remove Level Dressing (restores everything the bake hid). Batch:
    ///   Unity.exe -batchmode -projectPath . -executeMethod Eschropita.Overhaul.EditorTools.OverhaulLevelBuilder.BuildFromCommandLine
    /// </summary>
    public static class OverhaulLevelBuilder
    {
        public const string RootName = "== JoyTech Overhaul (generated) ==";
        const string DefaultScene = "Assets/Main Mars Scene.unity";

        [MenuItem("Tools/Eschropita/Overhaul/Build Level Dressing")]
        static void MenuBuild()
        {
            var scene = SceneManager.GetActiveScene();
            string report = Build(scene, save: false);
            EditorUtility.DisplayDialog("JoyTech Overhaul", report.Length > 1500 ? report.Substring(0, 1500) + "…" : report, "OK");
        }

        [MenuItem("Tools/Eschropita/Overhaul/Remove Level Dressing")]
        static void MenuRemove()
        {
            Remove(SceneManager.GetActiveScene());
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        public static void BuildFromCommandLine()
        {
            string scenePath = Arg("-overhaulScene") ?? DefaultScene;
            int code = 0;
            try
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                string report = Build(scene, save: true);
                Debug.Log("[Overhaul] build report:\n" + report);
                if (report.Contains("FAILED")) code = 2;
            }
            catch (Exception e)
            {
                Debug.LogError("[Overhaul] build failed: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        public static void RemoveFromCommandLine()
        {
            string scenePath = Arg("-overhaulScene") ?? DefaultScene;
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Remove(scene);
            EditorSceneManager.SaveScene(scene);
            EditorApplication.Exit(0);
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        public static void Remove(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var rec = root.GetComponent<OverhaulGeneratedRoot>();
                if (rec == null && root.name != RootName) continue;
                if (rec != null) rec.RestoreOriginals();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static string Build(Scene scene, bool save)
        {
            Remove(scene);
            PlaceKit.ResetCache();

            var rootGo = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(rootGo, scene);
            var ctx = new OverhaulBuildContext
            {
                Root = rootGo.transform,
                Record = rootGo.AddComponent<OverhaulGeneratedRoot>(),
                Scene = scene,
            };
            ResolveLandmarks(ctx);

            var steps = TypeCache.GetMethodsWithAttribute<OverhaulBuildStepAttribute>()
                .Where(m => m.IsStatic && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(OverhaulBuildContext))
                .Select(m => (m, a: m.GetCustomAttribute<OverhaulBuildStepAttribute>()))
                .OrderBy(x => x.a.Order).ThenBy(x => x.a.Name, StringComparer.Ordinal)
                .ToList();

            ctx.Info($"Landmarks: spawn {ctx.Spawn}, grove {ctx.GroveCenter}, windmill {ctx.Windmill}, obelisk {ctx.Obelisk}, home {ctx.Home}");
            ctx.Info($"Derived: dome {ctx.DomeCenter} r{ctx.DomeRadius}, plant {ctx.LifeSupportPlant}, shuttle {ctx.Shuttle}");
            // No StartAssetEditing here: steps import textures and must load them back immediately.
            try
            {
                foreach (var (m, a) in steps)
                {
                    var t0 = DateTime.Now;
                    try
                    {
                        m.Invoke(null, new object[] { ctx });
                        ctx.Info($"step {a.Order} {a.Name}: ok ({(DateTime.Now - t0).TotalSeconds:0.0}s)");
                    }
                    catch (Exception e)
                    {
                        var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                        ctx.Info($"step {a.Order} {a.Name}: FAILED {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}");
                        Debug.LogException(inner);
                    }
                }
            }
            finally
            {
                AssetDatabase.SaveAssets();
            }

            ctx.Record.buildReport = ctx.Log.ToString();
            EditorSceneManager.MarkSceneDirty(scene);
            if (save) EditorSceneManager.SaveScene(scene);
            try
            {
                string logPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Temp", "OverhaulBuild.log");
                File.WriteAllText(logPath, ctx.Log.ToString());
            }
            catch { /* Temp may not exist in odd setups */ }
            return ctx.Log.ToString();
        }

        static void ResolveLandmarks(OverhaulBuildContext ctx)
        {
            ctx.Player = UnityEngine.Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
            ctx.Grove = UnityEngine.Object.FindAnyObjectByType<MushroomFieldTrigger>(FindObjectsInactive.Include);
            ctx.ObeliskComp = UnityEngine.Object.FindAnyObjectByType<global::Obelisk>(FindObjectsInactive.Include);
            ctx.Puzzle = UnityEngine.Object.FindAnyObjectByType<MartianSequencePuzzle>(FindObjectsInactive.Include);
            foreach (var s in UnityEngine.Object.FindObjectsByType<TaskSite>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (s.GetComponent<global::Obelisk>() != null) continue;
                if (s.type == TaskType.PowerPlant || s.name.ToLowerInvariant().Contains("wind")) { ctx.WindmillSite = s; break; }
            }
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == "MycariHome") { ctx.HomeObject = t.gameObject; break; }
            var tornado = UnityEngine.Object.FindAnyObjectByType<TornadoCalamity>(FindObjectsInactive.Include);

            ctx.Spawn = PlaceKit.OnTerrain(ctx.Player ? ctx.Player.transform.position : Vector3.zero);
            ctx.GroveCenter = PlaceKit.OnTerrain(ctx.Grove ? ctx.Grove.transform.position : new Vector3(14.3f, 0f, 4.07f));
            ctx.Windmill = PlaceKit.OnTerrain(ctx.WindmillSite ? ctx.WindmillSite.transform.position : new Vector3(-12.62f, 0f, 13.22f));
            ctx.Obelisk = PlaceKit.OnTerrain(ctx.ObeliskComp ? ctx.ObeliskComp.transform.position : new Vector3(-14.62f, 0f, -12.78f));
            ctx.Home = PlaceKit.OnTerrain(ctx.HomeObject ? ctx.HomeObject.transform.position : new Vector3(13.33f, 0f, 19.94f));
            ctx.TornadoAnchor = PlaceKit.OnTerrain(tornado ? (tornado.anchor ? tornado.anchor.position : tornado.transform.position) : new Vector3(0f, 0f, 8.47f));

            // The JoyTech biodome sits behind the Mycari home so its door opens exactly where Mycari leave.
            Vector3 away = ctx.Home - ctx.Spawn; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            away.Normalize();
            ctx.DomeCenter = PlaceKit.OnTerrain(ctx.Home + away * ctx.DomeRadius);
            ctx.DoorFacing = -away;

            // Life-support plant beside the turbine, on the side facing the dome (pipes run turbine → plant → dome).
            Vector3 toDome = ctx.Home - ctx.Windmill; toDome.y = 0f;
            toDome = toDome.sqrMagnitude > 0.01f ? toDome.normalized : Vector3.right;
            ctx.LifeSupportPlant = PlaceKit.OnTerrain(ctx.Windmill + toDome * 6.5f);

            // Eschropita's shuttle: just left of where she starts, inside the opening camera view.
            ctx.Shuttle = PlaceKit.OnTerrain(ctx.Spawn + new Vector3(-6.5f, 0f, -1f));

            var r = ctx.Record;
            r.spawn = ctx.Spawn; r.groveCenter = ctx.GroveCenter; r.windmill = ctx.Windmill; r.obelisk = ctx.Obelisk;
            r.home = ctx.Home; r.domeCenter = ctx.DomeCenter; r.lifeSupportPlant = ctx.LifeSupportPlant; r.shuttle = ctx.Shuttle;
        }
    }
}
