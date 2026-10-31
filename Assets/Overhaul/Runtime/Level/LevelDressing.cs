using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Builds the redesigned JoyTech compound at runtime around the existing gameplay landmarks — nothing in the
    /// scene file changes, and every original visual it covers is only hidden (restored when this component goes
    /// away, or with F9, which toggles between the new dressing and the original look):
    ///   Mars wind turbine (replaces the stock turbine visual) · life-support plant + conduits to the dome door ·
    ///   organic mushroom grove (replaces the stone tile visual) · light paths · JoyTech biodome + airlock door ·
    ///   Escropita's lander, ruins, the obelisk's masts/fence/bunker hatch, horizon landmarks.
    /// Each piece builds independently; a failure is logged and the rest still appear.
    /// </summary>
    [OverhaulModule(80)]
    public class LevelDressing : MonoBehaviour
    {
        public static LevelDressing Instance { get; private set; }

        Transform _root;
        readonly List<Renderer> _hidden = new List<Renderer>();
        bool _shown = true;

        void Awake() => Instance = this;

        IEnumerator Start()
        {
            // wait for the scene to settle (the flow may create objects on its first frames)
            for (int i = 0; i < 180; i++)
            {
                var b = SignalBridge.Instance;
                if (b != null && b.Player != null && (b.Windmill != null || i > 60)) break;
                yield return null;
            }
            OverhaulLandmarks.Resolve(true);
            OverhaulLandmarks.DeriveStructures();
            OverhaulLandmarks.HasDressing = true;
            LevelKit.Release();

            _root = new GameObject("== JoyTech Overhaul (runtime) ==").transform;
            var bridge = SignalBridge.Instance;
            var src = new Sources
            {
                windmill = bridge != null ? bridge.Windmill : null,
                grove = bridge != null ? bridge.Grove : null,
                pads = bridge != null && bridge.Puzzle != null ? bridge.Puzzle.pads : null,
            };
            foreach (var _ in BuildSteps(_root, src, _hidden, HideRenderers)) yield return null;
        }

        /// <summary>The scene objects the dressing is built around.</summary>
        public struct Sources
        {
            public TaskSite windmill;
            public MushroomFieldTrigger grove;
            public GameObject[] pads;
        }

        /// <summary>
        /// The dressing, one piece per step: the runtime waits a frame between pieces, the editor's edit-mode preview
        /// runs them straight through. Landmarks must be resolved first (<see cref="OverhaulLandmarks"/>).
        /// <paramref name="hide"/> hides an original visual a piece replaces; the grove reports the stone tile
        /// renderers it switched off in <paramref name="hidden"/>.
        /// </summary>
        public static IEnumerable<string> BuildSteps(Transform root, Sources src, List<Renderer> hidden, System.Action<GameObject> hide)
        {
            yield return Step("Mars wind turbine", () =>
            {
                if (src.windmill == null) return;
                hide(src.windmill.gameObject);
                MarsTurbine.Build(root, OverhaulLandmarks.Windmill, OverhaulLandmarks.Spawn, OverhaulLandmarks.LifeSupportPlant);
            });
            yield return Step("Life support plant", () => LifeSupportPlant.Build(root, OverhaulLandmarks.LifeSupportPlant, OverhaulLandmarks.Windmill, OverhaulLandmarks.Home));
            yield return Step("Organic grove", () =>
            {
                if (src.grove == null) return;
                OrganicGrove.Build(root, src.grove, src.pads, new[] { OverhaulLandmarks.Windmill, OverhaulLandmarks.Obelisk, OverhaulLandmarks.Home }, hidden);
            });
            yield return Step("Light paths", () =>
            {
                float r = 6.5f;
                var col = src.grove != null ? src.grove.GetComponent<Collider>() : null;
                if (col != null) r = Mathf.Clamp(Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) * 1.1f, 5f, 10f);
                LightPaths.Build(root, OverhaulLandmarks.GroveCenter, r, new[]
                {
                    (OverhaulLandmarks.Windmill, 3.6f, OverhaulPalette.RoleFixer, NPCRole.Worker),
                    (OverhaulLandmarks.Obelisk, 4.2f, OverhaulPalette.RoleBreaker, NPCRole.Attacker),
                    (OverhaulLandmarks.Home, 1.8f, OverhaulPalette.RoleWanderer, NPCRole.Wanderer),
                });
            });
            yield return Step("JoyTech biodome", () =>
            {
                var home = GameObject.Find("MycariHome");
                if (home != null) hide(home);
                if (OverhaulLandmarks.DomeCenter != Vector3.zero)   // zero: no spot clear of the Mycari's roam area
                    JoyTechDome.Build(root, OverhaulLandmarks.DomeCenter, OverhaulLandmarks.DomeRadius, OverhaulLandmarks.Home, OverhaulLandmarks.DoorFacing);
            });
            yield return Step("Colony props", () => ColonyProps.Build(root));
        }

        static string Step(string what, System.Action build)
        {
            try { build(); }
            catch (System.Exception e) { Debug.LogWarning("[Overhaul] Level dressing: " + what + " failed: " + e); }
            return what;
        }

        void HideRenderers(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || !r.enabled) continue;
                r.enabled = false;
                _hidden.Add(r);
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9) && _root != null)
            {
                _shown = !_shown;
                _root.gameObject.SetActive(_shown);
                foreach (var r in _hidden) if (r != null) r.enabled = !_shown;
                var hud = GameHUD.Instance;
                if (hud != null) hud.Toast(_shown ? "Overhaul level dressing: on (F9)" : "Original level visuals (F9)", 2f);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();
            if (_root != null) Destroy(_root.gameObject);
            LevelKit.Release();
            OverhaulLandmarks.HasDressing = false;
        }
    }
}
