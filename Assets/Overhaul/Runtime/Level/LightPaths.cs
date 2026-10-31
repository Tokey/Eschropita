using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Design doc, Pinch 1: "the mushroom grove lights up after the song is played, revealing a light path to three
    /// locations". Three terrain-hugging trails leave the grove in the Mycari role colours — blue to the windmill
    /// (Fixers repair), red to the obelisk (Breakers dismantle), green to the dome door (Wanderers go home) — so the
    /// colours themselves hint which Mycari goes where. Hidden until the first solved song, then they grow outwards
    /// over a few seconds; light motes flow along them towards their destination, a ring pulses at each end, and the
    /// trail matching your current follower's role glows brighter.
    /// Guiding: while a Mycari you asked is waiting at the grove (and in the tutorial walk there), a path runs from
    /// you to the grove along the NavMesh; once a Mycari listens, its role's trail leads on. Both are faint as you
    /// walk; while Daffodil scans (V) they come alive: fast flow, a hard pulsing glow and a stream of motes.
    /// </summary>
    public class LightPaths : MonoBehaviour
    {
        class Trail
        {
            public Mesh mesh;
            public List<float> dist = new List<float>();
            public Vector2[] uvBase;
            public Color[] colors;
            public float length;
            public Color color;
            public NPCRole role;
            public Material mat, ringMat;
            public Transform ring;
            public Vector3 end;
            public List<Vector3> pts;
        }

        readonly List<Trail> _trails = new List<Trail>();
        float _reveal = -1f, _flow;
        bool _revealed;
        Texture2D _tex;
        float _nextMote;

        // the path from the player to the grove
        Vector3 _grove;
        float _groveRadius;
        Trail _guide;
        Renderer _guideRenderer;
        float _guideAlpha, _nextGuideBuild, _scanBoost;
        NavMeshPath _navPath;
        readonly List<Vector3> _corners = new List<Vector3>();

        public static LightPaths Build(Transform parent, Vector3 grove, float groveRadius, (Vector3 target, float stop, Color color, NPCRole role)[] dests)
        {
            var go = new GameObject("Light Paths");
            go.transform.SetParent(parent, false);
            var lp = go.AddComponent<LightPaths>();
            lp._tex = LevelKit.Own(PathTexture());
            lp._grove = grove;
            lp._groveRadius = groveRadius;
            foreach (var d in dests) lp.AddTrail(grove, groveRadius, d.target, d.stop, d.color, d.role);
            return lp;
        }

        void AddTrail(Vector3 grove, float groveRadius, Vector3 target, float stop, Color color, NPCRole role)
        {
            Vector3 dir = target - grove; dir.y = 0f;
            float total = dir.magnitude;
            if (total < groveRadius + stop + 2f) return;
            dir /= total;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 a = grove + dir * (groveRadius + 0.4f), b = target - dir * stop;
            float len = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            var pts = new List<Vector3>();
            int n = Mathf.Max(4, Mathf.CeilToInt(len / 0.5f));
            float phase = color.r * 3f + color.g * 5f;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                Vector3 p = Vector3.Lerp(a, b, t) + side * Mathf.Sin(t * Mathf.PI * 2f + phase) * 1.2f * Mathf.Sin(t * Mathf.PI);
                p.y = LevelKit.SurfaceY(p.x, p.z, transform.parent) + 0.05f;
                pts.Add(p);
            }
            var mb = new MeshBuilder();
            var ups = new List<Vector3>();
            foreach (var p in pts) ups.Add(Vector3.up);
            mb.Ribbon(pts, ups, 0.8f, new Color(1f, 1f, 1f, 0f), 0.35f);
            var mesh = LevelKit.Own(mb.ToMesh("LightPath", false, false));
            mesh.MarkDynamic();
            var tr = new Trail { mesh = mesh, color = color, role = role, end = b, pts = pts };
            tr.uvBase = mesh.uv;
            tr.colors = mesh.colors;
            float acc = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) acc += Vector3.Distance(pts[i], pts[i - 1]);
                tr.dist.Add(acc);
            }
            tr.length = acc;
            tr.mat = LevelKit.Own(OverhaulMaterials.OverGround(OverhaulMaterials.SoftGlow("LightPath", color, 2.2f, 1f, OverhaulMaterials.GlowShape.Texture, _tex)));
            var r = OverhaulPrimitives.EffectRenderer(transform, "Trail", mesh, tr.mat);
            r.transform.position = Vector3.zero;
            tr.ringMat = LevelKit.Own(OverhaulMaterials.OverGround(OverhaulMaterials.SoftGlow("PathEnd", color, 2.5f, 1f, OverhaulMaterials.GlowShape.Ring, null, 1.5f, 0.75f, 0.08f)));
            tr.ring = OverhaulPrimitives.EffectRenderer(transform, "End Ring", OverhaulPrimitives.GroundQuad, tr.ringMat).transform;
            tr.ring.position = new Vector3(b.x, LevelKit.SurfaceY(b.x, b.z, transform.parent) + 0.06f, b.z);
            tr.ring.gameObject.SetActive(false);
            _trails.Add(tr);
            ApplyReveal(tr, 0f);
        }

        void OnEnable() => GameSignals.PuzzleSolved += OnSolved;
        void OnDisable() => GameSignals.PuzzleSolved -= OnSolved;

        void Start()
        {
            var gfm = GameFlowManager.Instance;
            if (gfm != null && gfm.Phase >= GamePhase.MycariRepairs) { _revealed = true; _reveal = float.MaxValue; }
        }

        void OnSolved(Vector3 p)
        {
            if (_revealed) return;
            _revealed = true;
            _reveal = 0f;
            GameSignals.RaiseLightPathsRevealed();
        }

        void Update()
        {
            float dt = Time.deltaTime, t = Time.time;
            var daffodil = FollowPlayer.Instance;
            bool scanning = daffodil != null && daffodil.IsScanning;
            _scanBoost = Mathf.MoveTowards(_scanBoost, scanning ? 1f : 0f, dt * (scanning ? 5f : 2f));
            float surge = _scanBoost * (0.75f + 0.25f * Mathf.Sin(t * 9f));   // hard, fast pulse while scanning
            _flow += dt * (1.6f + 5.5f * _scanBoost);
            UpdateGuide(dt, t, surge);

            if (!_revealed) return;
            if (_reveal < 1e6f) _reveal += dt * 9f;     // metres per second

            var bridge = SignalBridge.Instance;
            var follower = bridge != null && bridge.Player != null ? bridge.Player.CurrentFollower : null;
            bool roleKnown = follower != null && (TripDirector.IsAttuned(follower) || (GameFlowManager.Instance != null && GameFlowManager.Instance.MycariObey));

            foreach (var tr in _trails)
            {
                ApplyReveal(tr, _reveal);
                bool focus = roleKnown && follower.role == tr.role;
                float k = (1.6f + 0.5f * Mathf.Sin(t * 1.4f + tr.length)) * (focus ? 1.9f : 1f);
                k *= 1f + surge * (focus || follower == null ? 2.6f : 1.2f);   // V: the trails come alive
                tr.mat.SetFloat("_Intensity", k);
                bool ended = _reveal >= tr.length;
                tr.ring.gameObject.SetActive(ended);
                if (ended)
                {
                    float s = 2.2f + 0.6f * Mathf.Repeat(t * 0.6f, 1f);
                    tr.ring.localScale = new Vector3(s, 1f, s);
                    float beat = Mathf.Repeat(t * (0.6f + 1.4f * _scanBoost), 1f);
                    tr.ringMat.SetFloat("_Intensity", (1.2f + 1.5f * (1f - beat)) * (focus ? 1.6f : 1f) * (1f + 1.5f * _scanBoost));
                }
            }

            // a few motes riding the trails towards their destinations
            if (t >= _nextMote && _trails.Count > 0)
            {
                _nextMote = t + Mathf.Lerp(0.25f, 0.035f, _scanBoost);
                var tr = _trails[Random.Range(0, _trails.Count)];
                if (_scanBoost > 0.5f && roleKnown && Random.value < 0.7f)
                    foreach (var x in _trails) if (x.role == follower.role) tr = x;   // V: motes pour down the follower's trail
                float maxD = Mathf.Min(_reveal, tr.length);
                if (maxD > 2f)
                {
                    int i = Random.Range(0, tr.pts.Count - 2);
                    if (tr.dist[i] < maxD - 1f)
                    {
                        int j = Mathf.Min(tr.pts.Count - 1, i + 6);
                        VfxKit.Stream(tr.pts[i] + Vector3.up * 0.15f, tr.pts[j] + Vector3.up * 0.25f, 1, tr.color, 1.2f);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ the path to the grove

        bool GuideWanted()
        {
            var song = MycariPersuasion.Instance;
            if (song != null && song.Busy) return true;
            var gfm = GameFlowManager.Instance;
            return gfm != null && gfm.Phase == GamePhase.MycariArrives;
        }

        void UpdateGuide(float dt, float t, float surge)
        {
            var bridge = SignalBridge.Instance;
            var player = bridge != null ? bridge.Player : null;
            bool want = player != null && GuideWanted();
            if (want)
            {
                Vector3 d = player.transform.position - _grove; d.y = 0f;
                want = d.magnitude > _groveRadius + 1.5f;   // already there: nothing to show
            }
            _guideAlpha = Mathf.MoveTowards(_guideAlpha, want ? 1f : 0f, dt * 2f);
            if (_guideAlpha <= 0.001f)
            {
                if (_guideRenderer != null) _guideRenderer.enabled = false;
                return;
            }
            if (_guide == null) BuildGuide();
            if (want && t >= _nextGuideBuild)
            {
                _nextGuideBuild = t + 0.5f;
                RebuildGuide(player.transform.position);
            }
            if (_guide.mesh == null || _guide.dist.Count < 2) return;
            _guideRenderer.enabled = true;

            int n = _guide.dist.Count;
            var uv = new Vector2[_guide.uvBase.Length];
            for (int i = 0; i < n; i++)
            {
                float dd = _guide.dist[i];
                float a = Mathf.Clamp01((_guide.length - dd) / 1.5f) * Mathf.Clamp01(dd / 1.5f) * _guideAlpha;
                var c = new Color(1f, 1f, 1f, a);
                _guide.colors[i * 2] = c;
                _guide.colors[i * 2 + 1] = c;
                uv[i * 2] = _guide.uvBase[i * 2] - new Vector2(0f, _flow);
                uv[i * 2 + 1] = _guide.uvBase[i * 2 + 1] - new Vector2(0f, _flow);
            }
            _guide.mesh.colors = _guide.colors;
            _guide.mesh.uv = uv;
            // faint as you walk, blazing while Daffodil scans
            _guide.mat.SetFloat("_Intensity", (1.3f + 0.3f * Mathf.Sin(t * 1.6f)) * (1f + 3.2f * surge));
            _guide.ring.gameObject.SetActive(true);
            float beat = Mathf.Repeat(t * (0.7f + 1.5f * _scanBoost), 1f);
            _guide.ring.localScale = new Vector3(2f + 0.8f * beat, 1f, 2f + 0.8f * beat);
            _guide.ringMat.SetFloat("_Intensity", (1.2f + 1.6f * (1f - beat)) * _guideAlpha * (1f + 2f * _scanBoost));
            if (surge > 0.2f && Random.value < dt * 18f && _guide.pts.Count > 8)
            {
                int i = Random.Range(0, _guide.pts.Count - 7);
                VfxKit.Stream(_guide.pts[i] + Vector3.up * 0.15f, _guide.pts[i + 6] + Vector3.up * 0.25f, 1, _guide.color, 1.2f);
            }
        }

        void BuildGuide()
        {
            Color c = OverhaulPalette.SporeGold;
            _guide = new Trail { color = c, pts = new List<Vector3>() };
            _guide.mat = LevelKit.Own(OverhaulMaterials.OverGround(OverhaulMaterials.SoftGlow("GrovePath", c, 1.4f, 1f, OverhaulMaterials.GlowShape.Texture, _tex)));
            _guideRenderer = OverhaulPrimitives.EffectRenderer(transform, "Path To Grove", null, _guide.mat);
            _guideRenderer.transform.position = Vector3.zero;
            _guide.ringMat = LevelKit.Own(OverhaulMaterials.OverGround(OverhaulMaterials.SoftGlow("GrovePathEnd", c, 2f, 1f, OverhaulMaterials.GlowShape.Ring, null, 1.5f, 0.75f, 0.08f)));
            _guide.ring = OverhaulPrimitives.EffectRenderer(transform, "Grove Ring", OverhaulPrimitives.GroundQuad, _guide.ringMat).transform;
            _guide.ring.gameObject.SetActive(false);
            _navPath = new NavMeshPath();
        }

        /// <summary>Lays the path from <paramref name="from"/> to the grove's edge along the NavMesh, hugging the ground.</summary>
        void RebuildGuide(Vector3 from)
        {
            _corners.Clear();
            Vector3 a = from, b = _grove;
            if (NavMesh.SamplePosition(from, out var ha, 3f, NavMesh.AllAreas)) a = ha.position;
            if (NavMesh.SamplePosition(_grove, out var hb, 4f, NavMesh.AllAreas)) b = hb.position;
            if (NavMesh.CalculatePath(a, b, NavMesh.AllAreas, _navPath) && _navPath.status != NavMeshPathStatus.PathInvalid && _navPath.corners.Length >= 2)
                _corners.AddRange(_navPath.corners);
            else { _corners.Add(a); _corners.Add(b); }

            // resample every half metre, starting a metre ahead of the player and stopping at the grove's rim
            var pts = new List<Vector3>();
            float walked = 0f;
            for (int i = 1; i < _corners.Count; i++)
            {
                Vector3 p0 = _corners[i - 1], p1 = _corners[i];
                float seg = Vector3.Distance(new Vector3(p0.x, 0f, p0.z), new Vector3(p1.x, 0f, p1.z));
                int steps = Mathf.Max(1, Mathf.CeilToInt(seg / 0.5f));
                for (int k = (i == 1 ? 0 : 1); k <= steps; k++)
                {
                    Vector3 p = Vector3.Lerp(p0, p1, k / (float)steps);
                    float toGrove = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(_grove.x, 0f, _grove.z));
                    if (k > 0) walked += seg / steps;
                    if (walked < 1f) continue;
                    if (toGrove < _groveRadius) { i = _corners.Count; break; }
                    p.y = LevelKit.SurfaceY(p.x, p.z, transform.parent) + 0.08f;
                    pts.Add(p);
                }
            }
            if (pts.Count < 2) { _guide.dist.Clear(); return; }

            var mb = new MeshBuilder();
            var ups = new List<Vector3>();
            foreach (var p in pts) ups.Add(Vector3.up);
            mb.Ribbon(pts, ups, 0.75f, new Color(1f, 1f, 1f, 0f), 0.35f);
            var old = _guide.mesh;
            _guide.mesh = mb.ToMesh("GrovePath", false, false);
            _guide.mesh.MarkDynamic();
            _guideRenderer.GetComponent<MeshFilter>().sharedMesh = _guide.mesh;
            if (old != null) Destroy(old);
            _guide.uvBase = _guide.mesh.uv;
            _guide.colors = _guide.mesh.colors;
            _guide.pts = pts;
            _guide.dist.Clear();
            float acc = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) acc += Vector3.Distance(pts[i], pts[i - 1]);
                _guide.dist.Add(acc);
            }
            _guide.length = acc;
            Vector3 e = pts[pts.Count - 1];
            _guide.ring.position = new Vector3(e.x, LevelKit.SurfaceY(e.x, e.z, transform.parent) + 0.06f, e.z);
        }

        void OnDestroy()
        {
            if (_guide != null && _guide.mesh != null) Destroy(_guide.mesh);
        }

        void ApplyReveal(Trail tr, float reveal)
        {
            int n = tr.dist.Count;
            var uv = new Vector2[tr.uvBase.Length];
            for (int i = 0; i < n; i++)
            {
                float d = tr.dist[i];
                float a = Mathf.Clamp01((reveal - d) / 1.5f);
                float endFade = Mathf.Clamp01((tr.length - d) / 1.2f) * Mathf.Clamp01(d / 1.2f + 0.3f);
                Color c = new Color(1f, 1f, 1f, a * endFade);
                tr.colors[i * 2] = c;
                tr.colors[i * 2 + 1] = c;
                uv[i * 2] = tr.uvBase[i * 2] - new Vector2(0f, _flow);
                uv[i * 2 + 1] = tr.uvBase[i * 2 + 1] - new Vector2(0f, _flow);
            }
            tr.mesh.colors = tr.colors;
            tr.mesh.uv = uv;
        }

        /// <summary>Soft across the width (alpha), with bright dashes along the length that scroll as the trail flows.</summary>
        static Texture2D PathTexture()
        {
            const int w = 32, h = 64;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "LightPath", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = Mathf.Abs(x / (w - 1f) * 2f - 1f);
                    float across = Mathf.Pow(1f - u, 1.6f);
                    float v = y / (float)h;
                    float dash = 0.55f + 0.45f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(v * Mathf.PI * 2f), 3f);
                    px[y * w + x] = new Color(1f, 1f, 1f, across * dash);
                }
            t.SetPixels(px);
            t.Apply(true, false);
            return t;
        }
    }
}
