using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Daffodil as she is drawn (Assets/Sprites/Daffodil.png) instead of the placeholder sphere and capsule: the
    /// painting is cut into layers (Resources/Overhaul/Daffodil: DaffodilRig atlas + DaffodilRigLayout, made by
    /// Tools/Daffodil/cut_rig.py) and animated as a puppet that floats with Daffodil's NavMesh agent.
    ///
    /// Idle she hovers on her ring jets, bobbing, her glass head a beat behind her body; she blinks, glances at
    /// Eschropita, twitches an ear, paddles her paws, and the daffodil in her dome sways. Moving, she leans into it,
    /// ears and flower trailing and the rings streaming behind. Scanning, her ears perk up and quiver, her paws lift
    /// and a cyan halo wakes behind her head. Out of charge she sinks to the ground, eyes shut and ears drooping,
    /// the jets out; rebooting, she pops back up. A soft shadow on the ground keeps her height readable.
    /// </summary>
    [OverhaulModule(45)]
    public class DaffodilPuppet : MonoBehaviour
    {
        [Tooltip("Overall size (1 = 1.42 m from the ear tips to the last jet ring).")]
        public float scale = 1f;
        [Tooltip("Height of the body's base above the ground while awake (m); the jet rings hang below it.")]
        public float hoverHeight = 0.42f;
        public float sleepHeight = 0.1f;

        [System.Serializable] class PartData { public string name; public int[] rect; public float[] pivot; public float[] pos; public int order; }
        [System.Serializable] class Layout { public float pixelsPerUnit = 440f; public PartData[] parts; }

        class Part
        {
            public Transform t;
            public SpriteRenderer r;
            public Vector3 rest;     // local position at rest
        }

        FollowPlayer _daffodil;
        IsoFollowCamera _rig;
        Transform _root, _bodyG, _headG, _ringsG, _shadow;
        readonly Dictionary<string, Part> _parts = new Dictionary<string, Part>();
        readonly List<Renderer> _hidden = new List<Renderer>();
        readonly List<Object> _owned = new List<Object>();
        SpriteRenderer _halo, _shadowR;
        float _ppu = 440f, _headBaseY;
        bool _built, _failed;

        // motion state
        Vector3 _lastPos, _vel;
        float _height, _lean, _leanVel, _flowerSwing, _flowerVel, _headLag, _headLagVel;
        float _sleep, _scan, _pop = 1f, _lowCharge;
        bool _wasSleeping, _wasScanning;
        float _nextBlink, _blinkStart = -9f, _nextTwitch, _twitchStart = -9f, _nextGlance, _glance, _glanceTarget;
        int _twitchSide, _blinksLeft;
        float _seed;

        void Start()
        {
            _seed = Random.value * 100f;
            _nextBlink = Time.time + 1.5f;
            _nextTwitch = Time.time + 3f;
        }

        void OnDestroy()
        {
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            if (_root != null) Destroy(_root.gameObject);
            if (_shadow != null) Destroy(_shadow.gameObject);
            foreach (var o in _owned) if (o != null) Destroy(o);
        }

        void LateUpdate()
        {
            if (_failed) return;
            if (!_built)
            {
                _daffodil = FollowPlayer.Instance;
                if (_daffodil == null) return;
                if (!Build()) { _failed = true; return; }
            }
            if (_daffodil == null) { _root.gameObject.SetActive(false); _shadow.gameObject.SetActive(false); return; }

            bool show = _daffodil.isActiveAndEnabled;
            if (_root.gameObject.activeSelf != show) { _root.gameObject.SetActive(show); _shadow.gameObject.SetActive(show); }
            if (!show) return;
            // keep the placeholder hidden (the flow or a checkpoint may switch renderers back on)
            foreach (var r in _hidden) if (r != null && r.enabled) r.enabled = false;

            Animate(Mathf.Min(Time.deltaTime, 0.05f));
        }

        // ------------------------------------------------------------------ building

        bool Build()
        {
            var tex = Resources.Load<Texture2D>("Overhaul/Daffodil/DaffodilRig");
            var json = Resources.Load<TextAsset>("Overhaul/Daffodil/DaffodilRigLayout");
            if (tex == null || json == null)
            {
                Debug.LogWarning("[Overhaul] Daffodil rig not found in Resources/Overhaul/Daffodil; keeping the placeholder.");
                return false;
            }
            Layout layout;
            try { layout = JsonUtility.FromJson<Layout>(json.text); }
            catch (System.Exception e) { Debug.LogWarning("[Overhaul] Daffodil rig layout unreadable: " + e.Message); return false; }
            if (layout == null || layout.parts == null || layout.parts.Length == 0) return false;
            _ppu = layout.pixelsPerUnit > 0f ? layout.pixelsPerUnit : 440f;

            var shader = Shader.Find("Sprites/Default");
            var mat = shader != null ? new Material(shader) { name = "Daffodil puppet" } : null;
            if (mat != null) _owned.Add(mat);

            _root = new GameObject("Daffodil Puppet").transform;
            _root.gameObject.AddComponent<SortingGroup>();
            _ringsG = Group("Jets", Vector2.zero);
            _bodyG = Group("Body", Pos(layout, "body"));
            _headG = Group("Head", Pos(layout, "head"));
            _headBaseY = _headG.localPosition.y;

            foreach (var p in layout.parts)
            {
                if (p.name == "glow") continue;
                Transform parent = p.name.StartsWith("ring") || p.name == "sparkles" ? _ringsG
                                 : p.name == "body" || p.name.StartsWith("arm") ? _bodyG : _headG;
                var go = new GameObject(p.name);
                go.transform.SetParent(parent, false);
                var r = go.AddComponent<SpriteRenderer>();
                r.sprite = MakeSprite(tex, p);
                if (mat != null) r.sharedMaterial = mat;
                r.sortingOrder = p.order;
                Vector3 rest = new Vector3(p.pos[0], p.pos[1], 0f) / _ppu - parent.localPosition;
                rest.z = -p.order * 0.0004f;   // a hair toward the camera per layer, so depth sorting agrees with the order
                go.transform.localPosition = rest;
                _parts[p.name] = new Part { t = go.transform, r = r, rest = rest };
            }

            // halo behind the head (scanning) and a soft shadow on the ground
            var glowData = System.Array.Find(layout.parts, q => q.name == "glow");
            var haloMat = OverhaulMaterials.SoftGlow("Daffodil halo", new Color(0.35f, 1f, 0.95f), 1.6f, 0.9f);
            _owned.Add(haloMat);
            var haloGo = new GameObject("Halo");
            haloGo.transform.SetParent(_headG, false);
            haloGo.transform.localPosition = new Vector3(0f, 80f / _ppu, 0.002f);
            _halo = haloGo.AddComponent<SpriteRenderer>();
            _halo.sharedMaterial = haloMat;
            _halo.sortingOrder = 15;
            if (glowData != null) _halo.sprite = MakeSprite(tex, glowData);
            _halo.color = new Color(1f, 1f, 1f, 0f);

            _shadow = new GameObject("Daffodil Shadow").transform;
            var shadowMat = OverhaulMaterials.SoftGlow("Daffodil shadow", Color.black, 1f, 0f);
            _owned.Add(shadowMat);
            _shadowR = _shadow.gameObject.AddComponent<SpriteRenderer>();
            _shadowR.sharedMaterial = shadowMat;
            if (glowData != null) _shadowR.sprite = MakeSprite(tex, glowData);
            _shadowR.sortingOrder = -1;
            _shadow.rotation = Quaternion.Euler(90f, 0f, 0f);

            foreach (var r in _daffodil.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled) continue;
                r.enabled = false;
                _hidden.Add(r);
            }
            _rig = FindAnyObjectByType<IsoFollowCamera>();
            _lastPos = _daffodil.transform.position;
            _height = hoverHeight;
            _built = true;
            return true;
        }

        Transform Group(string name, Vector2 artPos)
        {
            var g = new GameObject(name).transform;
            g.SetParent(_root, false);
            g.localPosition = new Vector3(artPos.x, artPos.y, 0f) / _ppu;
            return g;
        }

        static Vector2 Pos(Layout l, string name)
        {
            var p = System.Array.Find(l.parts, q => q.name == name);
            return p != null ? Vec(p.pos) : Vector2.zero;
        }

        static Vector2 Vec(float[] a) => a != null && a.Length >= 2 ? new Vector2(a[0], a[1]) : Vector2.zero;

        Sprite MakeSprite(Texture2D tex, PartData p)
        {
            var rect = new Rect(p.rect[0], p.rect[1], p.rect[2], p.rect[3]);
            var s = Sprite.Create(tex, rect, new Vector2(p.pivot[0], p.pivot[1]), _ppu, 0, SpriteMeshType.FullRect);
            s.name = "Daffodil " + p.name;
            _owned.Add(s);
            return s;
        }

        // ------------------------------------------------------------------ animation

        void Animate(float dt)
        {
            float t = Time.time + _seed;
            Vector3 at = _daffodil.transform.position;

            // velocity (smoothed), and what it means on screen
            Vector3 v = dt > 0f ? (at - _lastPos) / dt : Vector3.zero;
            _lastPos = at;
            if (v.sqrMagnitude > 400f) v = Vector3.zero;   // a warp (checkpoint), not a dash
            _vel = Vector3.Lerp(_vel, v, 1f - Mathf.Exp(-dt * 8f));
            float yaw = _rig != null ? _rig.yaw : 45f;
            Quaternion face = Quaternion.Euler(0f, yaw, 0f);
            Vector3 right = face * Vector3.right;
            float side = Vector3.Dot(_vel, right);                 // + moving right on screen
            float speed01 = Mathf.Clamp01(new Vector2(_vel.x, _vel.z).magnitude / 3.5f);

            // states
            bool sleeping = _daffodil.IsSleeping, scanning = _daffodil.IsScanning;
            if (_wasSleeping && !sleeping) _pop = 0f;                                // reboot: pop up
            if (!_wasScanning && scanning) _pop = Mathf.Min(_pop, 0.55f);           // a little hop as the scan starts
            _wasSleeping = sleeping; _wasScanning = scanning;
            _sleep = Mathf.MoveTowards(_sleep, sleeping ? 1f : 0f, dt * (sleeping ? 1.2f : 2.5f));
            _scan = Mathf.MoveTowards(_scan, scanning ? 1f : 0f, dt * 5f);
            _pop = Mathf.MoveTowards(_pop, 1f, dt * 2.2f);
            float charge = _daffodil.MaxEnergy > 0f ? _daffodil.Energy / _daffodil.MaxEnergy : 1f;
            _lowCharge = Mathf.MoveTowards(_lowCharge, !sleeping && charge < 0.2f ? 1f : 0f, dt * 1.5f);
            float awake = 1f - _sleep;

            // height: hover with a bob, sink when asleep, a springy pop on boot
            float bob = Mathf.Sin(t * 2.1f) * 0.05f + Mathf.Sin(t * 3.7f + 1f) * 0.012f;
            float popK = 1f - _pop;
            float pop = Mathf.Sin(popK * Mathf.PI) * 0.18f * (popK > 0f ? 1f : 0f);
            float wantH = Mathf.Lerp(sleepHeight, hoverHeight + _scan * 0.06f, awake) + bob * awake + pop;
            _height = Mathf.Lerp(_height, wantH, 1f - Mathf.Exp(-dt * 6f));

            // lean into sideways movement (spring), the flower and head trailing behind
            float wantLean = Mathf.Clamp(-side * 4.5f, -13f, 13f) * awake;
            _lean = Spring(_lean, ref _leanVel, wantLean, 60f, 11f, dt);
            _flowerSwing = Spring(_flowerSwing, ref _flowerVel, -_lean * 0.8f, 40f, 4f, dt);
            float headWant = -bob * 0.35f;
            _headLag = Spring(_headLag, ref _headLagVel, headWant, 90f, 9f, dt);

            _root.position = at + Vector3.up * _height;
            _root.rotation = face * Quaternion.Euler(0f, 0f, _lean - _sleep * 4f);
            float s = scale * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(_pop * 1.6f));
            float squash = 1f + Mathf.Sin(t * 4.2f) * 0.018f * awake + pop * 0.25f;
            _root.localScale = new Vector3(s / Mathf.Sqrt(squash), s * squash, s);

            // body: a gentle breath
            float breathe = Mathf.Sin(t * (sleeping ? 1.3f : 2.6f)) * (sleeping ? 0.02f : 0.012f);
            _bodyG.localScale = new Vector3(1f - breathe * 0.5f, 1f + breathe, 1f);

            // head: a beat behind, tilts toward what it looks at, nods off when asleep
            var player = SignalBridge.Instance != null ? SignalBridge.Instance.Player : null;
            if (Time.time >= _nextGlance)
            {
                _nextGlance = Time.time + Random.Range(1.2f, 3.5f);
                float toPlayer = 0f;
                if (player != null) toPlayer = Mathf.Clamp(Vector3.Dot(player.transform.position - at, right), -1f, 1f);
                _glanceTarget = Random.value < 0.7f ? toPlayer : Random.Range(-1f, 1f);
            }
            float look = Mathf.Abs(side) > 0.4f ? Mathf.Sign(side) : _glanceTarget;
            _glance = Mathf.Lerp(_glance, look, 1f - Mathf.Exp(-dt * 6f));
            float headTilt = -_glance * 4f * awake + Mathf.Sin(t * 0.9f) * 1.5f * awake - _sleep * 9f;
            _headG.localPosition = new Vector3(_headG.localPosition.x, _headBaseY + _headLag - _sleep * 0.03f, 0f);
            _headG.localRotation = Quaternion.Euler(0f, 0f, headTilt);

            // ears: idle sway, a twitch now and then, trailing when moving, perked and quivering when scanning,
            // drooping asleep, wilting when the charge is low
            if (Time.time >= _nextTwitch && awake > 0.9f)
            {
                _nextTwitch = Time.time + Random.Range(2.5f, 6.5f);
                _twitchStart = Time.time;
                _twitchSide = Random.value < 0.5f ? -1 : 1;
            }
            float tw = Time.time - _twitchStart;
            float twitch = tw < 0.35f ? Mathf.Sin(tw / 0.35f * Mathf.PI) * 16f : 0f;
            float sway = Mathf.Sin(t * 1.7f) * 3f;
            float droop = _sleep * 28f + _lowCharge * 12f + speed01 * 7f - _scan * 9f;
            float quiver = _scan * Mathf.Sin(t * 26f) * 2.5f;
            Rot("earL", droop + sway + quiver + (_twitchSide < 0 ? twitch : 0f) + _lean * 0.6f);
            Rot("earR", -droop + sway - quiver - (_twitchSide > 0 ? twitch : 0f) + _lean * 0.6f);
            float perk = 1f + _scan * 0.06f;
            Scale("earL", perk, perk);
            Scale("earR", perk, perk);

            // eyes: blink (sometimes twice), glance, widen when scanning; shut asleep, heavy when the charge is low
            if (Time.time >= _nextBlink && awake > 0.5f)
            {
                _blinkStart = Time.time;
                if (_blinksLeft > 0) { _blinksLeft--; _nextBlink = Time.time + 0.22f; }
                else
                {
                    _blinksLeft = Random.value < 0.2f ? 1 : 0;
                    _nextBlink = Time.time + (_blinksLeft > 0 ? 0.22f : Random.Range(2.2f, 5.5f));
                }
            }
            float bt = Time.time - _blinkStart;
            float lid = bt < 0.06f ? bt / 0.06f : bt < 0.11f ? 1f : bt < 0.2f ? 1f - (bt - 0.11f) / 0.09f : 0f;
            float heavy = _lowCharge * (0.35f + 0.15f * Mathf.Sin(t * 1.1f));
            float open = Mathf.Clamp01(1f - Mathf.Max(lid, heavy));
            bool shut = _sleep > 0.5f;
            float eyeScale = 1f + _scan * 0.08f;
            Vector3 gaze = new Vector3(_glance * 3f, 0f, 0f) / _ppu;
            foreach (var n in new[] { "eyeL", "eyeR" })
            {
                if (!_parts.TryGetValue(n, out var e)) continue;
                e.r.enabled = !shut && open > 0.08f;
                e.t.localScale = new Vector3(eyeScale, eyeScale * Mathf.Max(0.08f, open), 1f);
                e.t.localPosition = e.rest + gaze;
            }
            foreach (var n in new[] { "eyeClosedL", "eyeClosedR" })
                if (_parts.TryGetValue(n, out var c)) c.r.enabled = shut || open <= 0.08f;

            // nose: a sniff now and then
            float sniff = Mathf.Max(0f, Mathf.Sin(t * 0.7f) - 0.93f) * 2f;
            Scale("nose", 1f + sniff, 1f + sniff * 0.6f);

            // the daffodil in the dome: sways, trails, and lifts its head while scanning
            Rot("flower", Mathf.Sin(t * 1.3f) * 4f + _flowerSwing - _sleep * 10f);
            Scale("flower", 1f + _scan * 0.06f * (0.6f + 0.4f * Mathf.Sin(t * 9f)), 1f + _scan * 0.08f);

            // arms: paddle at rest, sweep back moving, lift scanning, hang asleep
            float paddle = Mathf.Sin(t * 2.4f) * 6f * awake;
            float lift = _scan * 20f;
            float sweep = speed01 * 10f;
            Rot("armL", paddle - lift + sweep + _sleep * 12f);
            Rot("armR", -Mathf.Sin(t * 2.4f + 0.6f) * 6f * awake + lift - sweep - _sleep * 12f);

            // jets: rings pulse in a wave running down, stream behind when moving, sputter on low charge, out asleep
            float jets = awake * Mathf.Clamp01(_pop * 2f);
            for (int i = 1; i <= 5; i++)
            {
                if (!_parts.TryGetValue("ring" + i, out var ring)) continue;
                float ph = t * 6.5f - i * 0.9f;
                float sputter = _lowCharge > 0f && Mathf.PerlinNoise(t * 7f, i * 3.3f) < 0.35f * _lowCharge ? 0.15f : 1f;
                float flick = 0.78f + 0.22f * Mathf.PerlinNoise(t * 9f, i * 1.7f);
                ring.r.color = new Color(1f, 1f, 1f, jets * flick * sputter);
                float trail = -side * 0.022f * i;
                ring.t.localPosition = ring.rest + new Vector3(trail, Mathf.Sin(ph) * 4f / _ppu - speed01 * 0.01f * i, 0f);
                float sx = (1f + Mathf.Sin(ph + 1f) * 0.08f) * (1f + _scan * 0.12f);
                ring.t.localScale = new Vector3(sx, 1f + speed01 * 0.25f, 1f);
            }
            if (_parts.TryGetValue("sparkles", out var sp))
            {
                sp.r.color = new Color(1f, 1f, 1f, jets * (0.45f + 0.55f * Mathf.Abs(Mathf.Sin(t * 1.9f))));
                sp.t.localPosition = sp.rest + new Vector3(0f, Mathf.Sin(t * 1.1f) * 0.02f, 0f);
                sp.t.localScale = Vector3.one * (1f + Mathf.Sin(t * 1.4f) * 0.04f);
            }

            // scan halo behind the head
            if (_halo != null)
            {
                float a = _scan * (0.55f + 0.25f * Mathf.Sin(t * 8f));
                _halo.color = new Color(1f, 1f, 1f, a);
                float hs = 3.1f + _scan * 0.4f + Mathf.Sin(t * 8f) * 0.1f;   // the glow sprite is 96 px: about 0.7 m across
                _halo.transform.localScale = new Vector3(hs, hs, 1f);
            }

            // shadow: smaller and fainter the higher she floats
            if (_shadow != null)
            {
                Vector3 ground = at + Vector3.up * 0.03f;
                _shadow.position = ground;
                _shadow.rotation = Quaternion.Euler(90f, yaw, 0f);
                float k = Mathf.InverseLerp(hoverHeight + 0.3f, 0f, _height);
                float w = scale * Mathf.Lerp(2.4f, 3.8f, k);
                _shadow.localScale = new Vector3(w, w * 0.55f, 1f);
                _shadowR.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.18f, 0.42f, k));
            }
        }

        static float Spring(float x, ref float v, float target, float stiffness, float damping, float dt)
        {
            v += ((target - x) * stiffness - v * damping) * dt;
            return x + v * dt;
        }


        void Rot(string name, float deg)
        {
            if (_parts.TryGetValue(name, out var p)) p.t.localRotation = Quaternion.Euler(0f, 0f, deg);
        }

        void Scale(string name, float sx, float sy)
        {
            if (_parts.TryGetValue(name, out var p)) p.t.localScale = new Vector3(sx, sy, 1f);
        }
    }
}
