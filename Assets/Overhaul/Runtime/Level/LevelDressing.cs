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

            Try("Mars wind turbine", () =>
            {
                var site = bridge != null ? bridge.Windmill : null;
                if (site == null) return;
                HideRenderers(site.gameObject);
                MarsTurbine.Build(_root, OverhaulLandmarks.Windmill, OverhaulLandmarks.Spawn, OverhaulLandmarks.LifeSupportPlant);
            });
            yield return null;
            Try("Life support plant", () => LifeSupportPlant.Build(_root, OverhaulLandmarks.LifeSupportPlant, OverhaulLandmarks.Windmill, OverhaulLandmarks.Home));
            yield return null;
            Try("Organic grove", () =>
            {
                var grove = bridge != null ? bridge.Grove : null;
                if (grove == null) return;
                var pads = bridge.Puzzle != null ? bridge.Puzzle.pads : null;
                OrganicGrove.Build(_root, grove, pads, new[] { OverhaulLandmarks.Windmill, OverhaulLandmarks.Obelisk, OverhaulLandmarks.Home }, _hidden);
            });
            yield return null;
            Try("Light paths", () =>
            {
                var grove = bridge != null ? bridge.Grove : null;
                float r = 6.5f;
                var col = grove != null ? grove.GetComponent<Collider>() : null;
                if (col != null) r = Mathf.Clamp(Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) * 1.1f, 5f, 10f);
                LightPaths.Build(_root, OverhaulLandmarks.GroveCenter, r, new[]
                {
                    (OverhaulLandmarks.Windmill, 3.6f, OverhaulPalette.RoleFixer, NPCRole.Worker),
                    (OverhaulLandmarks.Obelisk, 4.2f, OverhaulPalette.RoleBreaker, NPCRole.Attacker),
                    (OverhaulLandmarks.Home, 1.8f, OverhaulPalette.RoleWanderer, NPCRole.Wanderer),
                });
            });
            yield return null;
            Try("JoyTech biodome", () =>
            {
                var home = GameObject.Find("MycariHome");
                if (home != null) HideRenderers(home);
                if (OverhaulLandmarks.DomeCenter != Vector3.zero)   // zero: no spot clear of the Mycari's roam area
                    JoyTechDome.Build(_root, OverhaulLandmarks.DomeCenter, OverhaulLandmarks.DomeRadius, OverhaulLandmarks.Home, OverhaulLandmarks.DoorFacing);
            });
            yield return null;
            Try("Colony props", () => ColonyProps.Build(_root));
        }

        void Try(string what, System.Action build)
        {
            try { build(); }
            catch (System.Exception e) { Debug.LogWarning("[Overhaul] Level dressing: " + what + " failed: " + e); }
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
