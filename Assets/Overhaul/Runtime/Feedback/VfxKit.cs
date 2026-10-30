using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Shared, pooled visual-effect building blocks for runtime feedback (everything procedural, no prefabs):
    ///  - particles: one world-space ParticleSystem per look (sparks, motes, dust, smoke, shards, spores) that emits
    ///    at any position on demand — no per-effect GameObjects
    ///  - ground rings (expanding soft rings lying on the terrain), light flashes, vertical light beams
    ///  - a soft line material for tethers
    /// Static API (VfxKit.Sparks(...), VfxKit.Ring(...), …) is a no-op when the module isn't installed.
    /// Colours are LDR hues; brightness comes from each look's HDR intensity, so glows bloom.
    /// </summary>
    [OverhaulModule(35)]
    public class VfxKit : MonoBehaviour
    {
        public static VfxKit Instance { get; private set; }

        ParticleSystem _sparks, _motes, _dust, _smoke, _shards, _spores;
        Material _sparksMat, _motesMat, _dustMat, _smokeMat, _shardsMat, _sporesMat, _beamMat0, _lineMat;
        Texture2D _beamTex, _lineTex;

        class RingFx { public Transform t; public Material m; public Vector3 pos; public Color c; public float r0, r1, life, age, inten; public bool live; }
        class FlashFx { public Light l; public float lumens, life, age; public bool live; }
        class BeamFx { public Transform t; public Material m; public float height, width, life, age, inten; public bool live; }

        readonly List<RingFx> _rings = new List<RingFx>();
        readonly List<FlashFx> _flashes = new List<FlashFx>();
        readonly List<BeamFx> _beams = new List<BeamFx>();

        public Material LineMaterial => _lineMat;

        void Awake()
        {
            Instance = this;
            _beamTex = MakeBeamTexture();
            _lineTex = MakeLineTexture();
            _sparksMat = OverhaulMaterials.SoftGlow("VfxSparks", Color.white, 5f, 1f, OverhaulMaterials.GlowShape.Disc, null, 2.5f);
            _motesMat = OverhaulMaterials.SoftGlow("VfxMotes", Color.white, 3f, 1f, OverhaulMaterials.GlowShape.Disc, null, 2f);
            _sporesMat = OverhaulMaterials.SoftGlow("VfxSpores", Color.white, 2.2f, 0.9f, OverhaulMaterials.GlowShape.Disc, null, 1.6f);
            _dustMat = OverhaulMaterials.SoftGlow("VfxDust", Color.white, 0.9f, 0f, OverhaulMaterials.GlowShape.Disc, null, 1.1f);
            _smokeMat = OverhaulMaterials.SoftGlow("VfxSmoke", Color.white, 0.75f, 0f, OverhaulMaterials.GlowShape.Disc, null, 1f);
            _shardsMat = OverhaulMaterials.SoftGlow("VfxShards", Color.white, 3.5f, 0.85f, OverhaulMaterials.GlowShape.Disc, null, 4f);
            _beamMat0 = OverhaulMaterials.SoftGlow("VfxBeam", Color.white, 2.5f, 1f, OverhaulMaterials.GlowShape.Texture, _beamTex);
            _lineMat = OverhaulMaterials.SoftGlow("VfxLine", Color.white, 2.5f, 1f, OverhaulMaterials.GlowShape.Texture, _lineTex);

            _sparks = MakePS("Sparks", _sparksMat, ParticleSystemRenderMode.Stretch, 600, 1.4f, false, FastFade());
            _motes = MakePS("Motes", _motesMat, ParticleSystemRenderMode.Billboard, 800, -0.05f, true, SoftFade());
            _spores = MakePS("Spores", _sporesMat, ParticleSystemRenderMode.Billboard, 600, -0.02f, true, SoftFade());
            _dust = MakePS("Dust", _dustMat, ParticleSystemRenderMode.Billboard, 500, 0.05f, false, SoftFade(), grow: 2.2f);
            _smoke = MakePS("Smoke", _smokeMat, ParticleSystemRenderMode.Billboard, 300, -0.08f, true, SoftFade(), grow: 3f);
            _shards = MakePS("Shards", _shardsMat, ParticleSystemRenderMode.Billboard, 300, 1.8f, false, FastFade());
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var m in new[] { _sparksMat, _motesMat, _sporesMat, _dustMat, _smokeMat, _shardsMat, _beamMat0, _lineMat }) if (m != null) Destroy(m);
            foreach (var r in _rings) if (r.m != null) Destroy(r.m);
            foreach (var b in _beams) if (b.m != null) Destroy(b.m);
            if (_beamTex != null) Destroy(_beamTex);
            if (_lineTex != null) Destroy(_lineTex);
        }

        ParticleSystem MakePS(string name, Material mat, ParticleSystemRenderMode mode, int max, float gravity, bool noise, Gradient fade, float grow = 0f)
        {
            var go = new GameObject("VFX " + name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 0.1f;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(fade);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var curve = grow > 0f ? AnimationCurve.EaseInOut(0f, 0.6f, 1f, grow) : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0.2f));
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
            if (noise)
            {
                var n = ps.noise;
                n.enabled = true;
                n.strength = 0.45f;
                n.frequency = 0.6f;
                n.scrollSpeed = 0.25f;
                n.damping = true;
            }
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = mode == ParticleSystemRenderMode.Stretch ? 0.05f : 0.12f;
            limit.limit = 30f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = mode;
            if (mode == ParticleSystemRenderMode.Stretch) { r.velocityScale = 0.04f; r.lengthScale = 1.2f; }
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            ps.Play();
            return ps;
        }

        static Gradient FastFade()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static Gradient SoftFade()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static Texture2D MakeBeamTexture()
        {
            const int w = 32, h = 128;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "VfxBeam", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f);
                float vert = Mathf.SmoothStep(0f, 1f, v / 0.08f) * Mathf.Pow(1f - v, 1.4f);
                for (int x = 0; x < w; x++)
                {
                    float u = Mathf.Abs(x / (w - 1f) * 2f - 1f);
                    float a = Mathf.Pow(1f - u, 2.2f) * vert;
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        static Texture2D MakeLineTexture()
        {
            const int w = 8, h = 32;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "VfxLine", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = Mathf.Abs(y / (h - 1f) * 2f - 1f);
                float a = Mathf.Pow(1f - v, 1.8f);
                for (int x = 0; x < w; x++) px[y * w + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        // ---------------------------------------------------------------------------------------------------
        // particles

        static void Emit(ParticleSystem ps, Vector3 p, Vector3 v, float size, float life, Color c)
        {
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = p, velocity = v, startSize = size, startLifetime = life, startColor = c, applyShapeToPosition = false,
            };
            ps.Emit(ep, 1);
        }

        /// <summary>Hot fast streaks bursting outwards (impacts, failures, power loss).</summary>
        public static void Sparks(Vector3 p, int count, Color c, float speed = 6f, float up = 0.6f)
        {
            var k = Instance;
            if (k == null) return;
            for (int i = 0; i < count; i++)
            {
                Vector3 d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) * up + 0.15f;
                Emit(k._sparks, p, d.normalized * speed * Random.Range(0.5f, 1.2f), Random.Range(0.03f, 0.07f), Random.Range(0.35f, 0.8f), c);
            }
        }

        /// <summary>Glowing motes drifting upwards (magic, repair, recruitment).</summary>
        public static void Motes(Vector3 p, int count, Color c, float spread = 0.6f, float rise = 1.2f, float size = 0.12f)
        {
            var k = Instance;
            if (k == null) return;
            for (int i = 0; i < count; i++)
            {
                Vector3 o = Random.insideUnitSphere * spread; o.y = Mathf.Abs(o.y) * 0.5f;
                Vector3 v = new Vector3(Random.Range(-0.3f, 0.3f), rise * Random.Range(0.6f, 1.3f), Random.Range(-0.3f, 0.3f));
                Emit(k._motes, p + o, v, size * Random.Range(0.6f, 1.4f), Random.Range(1.0f, 2.2f), c);
            }
        }

        /// <summary>Motes flying from a point towards a target (repair streams, shards flying to the player).</summary>
        public static void Stream(Vector3 from, Vector3 to, int count, Color c, float seconds = 0.8f)
        {
            var k = Instance;
            if (k == null) return;
            for (int i = 0; i < count; i++)
            {
                Vector3 jitter = Random.insideUnitSphere * 0.25f;
                Vector3 v = (to - from + jitter) / seconds + Vector3.up * Random.Range(0.3f, 1.2f);
                Emit(k._motes, from + jitter, v, Random.Range(0.06f, 0.12f), seconds * Random.Range(0.8f, 1.05f), c);
            }
        }

        /// <summary>Fungal spores: slow, wandering, glowing.</summary>
        public static void Spores(Vector3 p, int count, Color c, float spread = 1f)
        {
            var k = Instance;
            if (k == null) return;
            for (int i = 0; i < count; i++)
            {
                Vector3 o = Random.insideUnitSphere * spread; o.y = Mathf.Abs(o.y);
                Emit(k._spores, p + o, new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.15f, 0.6f), Random.Range(-0.2f, 0.2f)),
                     Random.Range(0.05f, 0.11f), Random.Range(2f, 4f), c);
            }
        }

        /// <summary>Soft dust puffs (footsteps, impacts, tornado). Alpha-blended, reads on bright ground.</summary>
        public static void Dust(Vector3 p, int count, Color c, float size = 0.5f, float speed = 0.8f)
        {
            var k = Instance;
            if (k == null) return;
            for (int i = 0; i < count; i++)
            {
                Vector3 d = Random.insideUnitCircle;
                Vector3 v = new Vector3(d.x, Random.Range(0.1f, 0.5f), d.y) * speed;
                Emit(k._dust, p + new Vector3(d.x, 0f, d.y) * 0.2f, v, size * Random.Range(0.7f, 1.3f), Random.Range(0.6f, 1.4f), c);
            }
        }

        /// <summary>Rising dark smoke (power failure, destruction).</summary>
        public static void Smoke(Vector3 p, int count, float size = 1f)
        {
            var k = Instance;
            if (k == null) return;
            for (int i = 0; i < count; i++)
            {
                Vector3 o = Random.insideUnitSphere * 0.4f;
                Emit(k._smoke, p + o, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.8f, 1.6f), Random.Range(-0.3f, 0.3f)),
                     size * Random.Range(0.7f, 1.4f), Random.Range(2f, 3.5f), new Color(0.12f, 0.1f, 0.1f, 0.85f));
            }
        }

        /// <summary>Tumbling crystalline shards with gravity (shields breaking, obelisk shattering).</summary>
        public static void Shards(Vector3 p, int count, Color c, float speed = 5f)
        {
            var k = Instance;
            if (k == null) return;
            for (int i = 0; i < count; i++)
            {
                Vector3 d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) + 0.4f;
                Emit(k._shards, p, d.normalized * speed * Random.Range(0.4f, 1.1f), Random.Range(0.08f, 0.2f), Random.Range(0.8f, 1.6f), c);
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // rings, flashes, beams

        /// <summary>An expanding soft ring lying on the ground.</summary>
        public static void Ring(Vector3 p, Color c, float radius, float seconds = 0.8f, float intensity = 2.5f, float startRadius = 0.2f)
        {
            var k = Instance;
            if (k == null) return;
            RingFx r = null;
            foreach (var x in k._rings) if (!x.live) { r = x; break; }
            if (r == null)
            {
                if (k._rings.Count >= 32) r = k._rings[0];
                else
                {
                    r = new RingFx { m = OverhaulMaterials.SoftGlow("VfxRing", Color.white, 2f, 1f, OverhaulMaterials.GlowShape.Ring, null, 1.5f, 0.8f, 0.08f) };
                    r.t = OverhaulPrimitives.EffectRenderer(k.transform, "VFX Ring", OverhaulPrimitives.GroundQuad, r.m).transform;
                    k._rings.Add(r);
                }
            }
            r.pos = p;
            r.pos.y = OverhaulPrimitives.GroundY(p) + 0.07f;
            r.c = c; r.r0 = startRadius; r.r1 = radius; r.life = Mathf.Max(0.05f, seconds); r.age = 0f; r.inten = intensity; r.live = true;
            r.m.SetColor("_Color", c);
            r.t.gameObject.SetActive(true);
            r.t.position = r.pos;
            r.t.localScale = Vector3.one * (startRadius * 2f);
        }

        /// <summary>A brief burst of coloured light.</summary>
        public static void Flash(Vector3 p, Color c, float lumens = 3000f, float seconds = 0.5f, float range = 10f)
        {
            var k = Instance;
            if (k == null) return;
            FlashFx f = null;
            foreach (var x in k._flashes) if (!x.live) { f = x; break; }
            if (f == null)
            {
                if (k._flashes.Count >= 6) { f = k._flashes[0]; }
                else
                {
                    var go = new GameObject("VFX Flash");
                    go.transform.SetParent(k.transform, false);
                    f = new FlashFx { l = OverhaulLights.Point(go, c, 0f, range, false, false) };
                    k._flashes.Add(f);
                }
            }
            f.l.transform.position = p;
            f.l.color = c;
            f.l.range = range;
            f.lumens = lumens; f.life = Mathf.Max(0.05f, seconds); f.age = 0f; f.live = true;
            f.l.enabled = true;
        }

        /// <summary>A vertical shaft of light (command targets, checkpoints, attunement).</summary>
        public static void Beam(Vector3 p, Color c, float height = 8f, float seconds = 1.5f, float width = 1.2f, float intensity = 2.5f)
        {
            var k = Instance;
            if (k == null) return;
            BeamFx b = null;
            foreach (var x in k._beams) if (!x.live) { b = x; break; }
            if (b == null)
            {
                if (k._beams.Count >= 8) b = k._beams[0];
                else
                {
                    b = new BeamFx { m = new Material(k._beamMat0) };
                    b.t = OverhaulPrimitives.EffectRenderer(k.transform, "VFX Beam", OverhaulPrimitives.UprightQuad, b.m).transform;
                    k._beams.Add(b);
                }
            }
            p.y = OverhaulPrimitives.GroundY(p);
            b.t.position = p;
            b.height = height; b.width = width; b.life = Mathf.Max(0.1f, seconds); b.age = 0f; b.inten = intensity; b.live = true;
            b.m.SetColor("_Color", c);
            b.t.gameObject.SetActive(true);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _rings.Count; i++)
            {
                var r = _rings[i];
                if (!r.live) continue;
                r.age += dt;
                float t = r.age / r.life;
                if (t >= 1f) { r.live = false; r.t.gameObject.SetActive(false); continue; }
                float e = 1f - (1f - t) * (1f - t);
                float rad = Mathf.Lerp(r.r0, r.r1, e);
                r.t.localScale = new Vector3(rad * 2f, 1f, rad * 2f);
                r.m.SetFloat("_Intensity", r.inten * Mathf.Pow(1f - t, 1.5f));
            }
            for (int i = 0; i < _flashes.Count; i++)
            {
                var f = _flashes[i];
                if (!f.live) continue;
                f.age += dt;
                float t = f.age / f.life;
                if (t >= 1f) { f.live = false; f.l.enabled = false; continue; }
                float env = t < 0.08f ? t / 0.08f : Mathf.Pow(1f - (t - 0.08f) / 0.92f, 2f);
                OverhaulLights.SetLumens(f.l, f.lumens * env);
            }
            var cam = Camera.main;
            for (int i = 0; i < _beams.Count; i++)
            {
                var b = _beams[i];
                if (!b.live) continue;
                b.age += dt;
                float t = b.age / b.life;
                if (t >= 1f) { b.live = false; b.t.gameObject.SetActive(false); continue; }
                float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.2f));
                b.t.localScale = new Vector3(b.width * (1.2f - 0.4f * t), b.height * grow, 1f);
                // the quad is centred: lift by half its height so it stands on the ground
                Vector3 pos = b.t.position;
                pos.y = OverhaulPrimitives.GroundY(pos) + b.height * grow * 0.5f;
                b.t.position = pos;
                if (cam != null)
                {
                    Vector3 f = cam.transform.position - pos; f.y = 0f;
                    if (f.sqrMagnitude > 1e-4f) b.t.rotation = Quaternion.LookRotation(-f.normalized, Vector3.up);
                }
                b.m.SetFloat("_Intensity", b.inten * (t < 0.2f ? 1f : Mathf.Pow(1f - (t - 0.2f) / 0.8f, 1.5f)));
            }
        }
    }
}
