using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eschropita.Overhaul.EditorTools
{
    /// <summary>
    /// Shows the overhaul's runtime-built world in the editor without pressing Play: the Mars turbine, life-support
    /// plant, organic grove, light paths, JoyTech biodome, colony props and lore terminals (the same build steps
    /// LevelDressing and LoreTerminals run in play, around the same landmarks, with the Mycari roam box widened the
    /// way NPCManager widens it), and Daffodil as she is drawn instead of her sphere-and-capsule stand-in.
    ///
    /// Nothing is written into the scene: the preview objects are DontSave, and the originals they replace are only
    /// hidden in the Scene view (scene visibility, the eye icons in the Hierarchy), never switched off. The preview is
    /// cleared before Play, closing the scene and script reloads, and rebuilt after; builds and saves skip it.
    /// Tools ▸ Eschropita ▸ Overhaul ▸ Edit-Mode Preview toggles it; ▸ Rebuild Edit-Mode Preview refreshes it after
    /// moving a landmark.
    /// </summary>
    [InitializeOnLoad]
    static class OverhaulEditPreview
    {
        const string RootName = "== JoyTech Overhaul (edit preview) ==";
        const string Pref = "Eschropita.Overhaul.EditPreview";
        const string MenuToggle = "Tools/Eschropita/Overhaul/Edit-Mode Preview";

        static GameObject _root;
        static readonly List<(GameObject go, bool children)> _hiddenInView = new List<(GameObject, bool)>();
        static readonly HashSet<Object> _owned = new HashSet<Object>();
        static bool _queued;

        static bool Enabled
        {
            get => EditorPrefs.GetBool(Pref, true);
            set => EditorPrefs.SetBool(Pref, value);
        }

        static OverhaulEditPreview()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorSceneManager.sceneOpened += (s, m) => Queue();
            EditorSceneManager.sceneClosing += (s, removing) => Clear();
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
            Queue();
        }

        [MenuItem(MenuToggle, false, 40)]
        static void Toggle()
        {
            Enabled = !Enabled;
            if (Enabled) Queue(); else Clear();
        }

        [MenuItem(MenuToggle, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuToggle, Enabled);
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem("Tools/Eschropita/Overhaul/Rebuild Edit-Mode Preview", false, 41)]
        static void MenuRebuild()
        {
            if (!Enabled) Enabled = true;
            Rebuild();
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) Clear();
            else if (change == PlayModeStateChange.EnteredEditMode) Queue();
        }

        static void Queue()
        {
            if (_queued) return;
            _queued = true;
            EditorApplication.delayCall += () =>
            {
                _queued = false;
                Rebuild();
            };
        }

        // ------------------------------------------------------------------ build

        static void Rebuild()
        {
            Clear();
            if (!Enabled || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            if (Object.FindAnyObjectByType<PlayerController>() == null) return;      // not a game scene
            if (Object.FindAnyObjectByType<OverhaulGeneratedRoot>() != null) return;  // the dressing is baked into it

            // a leftover from before a script reload
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == RootName && go.scene == scene) Object.DestroyImmediate(go);

            try
            {
                ResolveLandmarks();
                LevelKit.Release();

                _root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(_root, scene);
                // keep the preview out of a NavMesh bake made while it is showing (the surface collects everything)
                IgnoreForNavMesh(_root);

                var src = new LevelDressing.Sources
                {
                    windmill = FindWindmill(),
                    grove = Object.FindAnyObjectByType<MushroomFieldTrigger>(),
                };
                var puzzle = Object.FindAnyObjectByType<MartianSequencePuzzle>();
                if (puzzle != null) src.pads = puzzle.pads;

                var switchedOff = new List<Renderer>();
                foreach (var _ in LevelDressing.BuildSteps(_root.transform, src, switchedOff, HideInView)) { }
                // the grove builder switches its stone tile off; here that stays a Scene-view-only hide
                foreach (var r in switchedOff)
                {
                    if (r == null) continue;
                    r.enabled = true;
                    HideInView(r.gameObject, false);
                }

                var lore = new GameObject("Lore Terminals").transform;
                lore.SetParent(_root.transform, false);
                LoreTerminals.Place(lore);

                DaffodilStandIn();
                MarkPreview(_root);
                CollectOwned(_root);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Overhaul] Edit-mode preview failed (Play is unaffected): " + e);
                if (_root != null) MarkPreview(_root);   // whatever was built must never be saved into the scene
                Clear();
            }
            finally
            {
                OverhaulLandmarks.Invalidate();   // play resolves its own landmarks from scratch
            }
        }

        /// <summary>The same landmarks as in play, with the roam box widened by NPCManager.roamAreaScale (done in its Awake).</summary>
        static void ResolveLandmarks()
        {
            OverhaulLandmarks.Invalidate();
            OverhaulLandmarks.Resolve(true);
            var mgr = Object.FindAnyObjectByType<NPCManager>();
            if (mgr != null && mgr.roamArea != null && mgr.roamAreaScale > 1.0001f)
            {
                var b = mgr.roamArea.bounds;
                OverhaulLandmarks.RoamArea = new Bounds(b.center, new Vector3(b.size.x * mgr.roamAreaScale, b.size.y, b.size.z * mgr.roamAreaScale));
                OverhaulLandmarks.HasRoamArea = true;
            }
            OverhaulLandmarks.DeriveStructures();
            OverhaulLandmarks.HasDressing = true;
        }

        /// <summary>NavMeshModifier (AI Navigation package, looked up by name so this compiles without it): ignore from build, children too.</summary>
        static void IgnoreForNavMesh(GameObject go)
        {
            var type = System.Type.GetType("Unity.AI.Navigation.NavMeshModifier, Unity.AI.Navigation");
            if (type == null) return;
            var mod = go.AddComponent(type);
            type.GetProperty("ignoreFromBuild")?.SetValue(mod, true);
            type.GetProperty("applyToChildren")?.SetValue(mod, true);
        }

        static TaskSite FindWindmill()
        {
            foreach (var s in Object.FindObjectsByType<TaskSite>(FindObjectsSortMode.None))
                if (s.GetComponent<global::Obelisk>() == null && (s.type == TaskType.PowerPlant || s.name.ToLowerInvariant().Contains("wind")))
                    return s;
            return null;
        }

        /// <summary>Daffodil's painting where the puppet floats in play; her placeholder meshes hidden in the Scene view.</summary>
        static void DaffodilStandIn()
        {
            var daffodil = Object.FindAnyObjectByType<FollowPlayer>();
            if (daffodil == null) return;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Daffodil.png");
            if (sprite == null) return;
            foreach (var r in daffodil.GetComponentsInChildren<MeshRenderer>(true)) HideInView(r.gameObject, false);

            var go = new GameObject("Daffodil (as in play)");
            go.transform.SetParent(_root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            var rig = Object.FindAnyObjectByType<IsoFollowCamera>();
            float yaw = rig != null ? rig.yaw : 45f;
            // DaffodilPuppet: 440 px per metre, the body's base 0.42 m up; the painting's centre is 250 px above that base
            float k = sprite.pixelsPerUnit / 440f;
            go.transform.position = daffodil.transform.position + Vector3.up * (0.42f + 250f / 440f);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * k;
        }

        static void HideInView(GameObject go) => HideInView(go, true);

        static void HideInView(GameObject go, bool includeChildren)
        {
            if (go == null) return;
            SceneVisibilityManager.instance.Hide(go, includeChildren);
            _hiddenInView.Add((go, includeChildren));
        }

        static void MarkPreview(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        }

        /// <summary>Meshes and materials the builders made (not assets), so clearing does not leak them.</summary>
        static void CollectOwned(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && !AssetDatabase.Contains(mf.sharedMesh)) _owned.Add(mf.sharedMesh);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || AssetDatabase.Contains(m)) continue;
                    _owned.Add(m);
                    foreach (int id in m.GetTexturePropertyNameIDs())
                    {
                        var tex = m.GetTexture(id);
                        if (tex != null && !AssetDatabase.Contains(tex)) _owned.Add(tex);
                    }
                }
        }

        // ------------------------------------------------------------------ clear

        static void Clear()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
            foreach (var o in _owned) if (o != null) Object.DestroyImmediate(o);
            _owned.Clear();
            LevelKit.Release();
            var svm = SceneVisibilityManager.instance;
            foreach (var (go, children) in _hiddenInView) if (go != null) svm.Show(go, children);
            _hiddenInView.Clear();
        }
    }
}
