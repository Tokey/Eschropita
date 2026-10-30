using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// A JoyTech data-pad on a post holding one log. Dark until its story moment arrives; then its screen glows,
    /// a glyph bobs above it and soft rings pulse on the ground so the player notices it. Press E nearby to read
    /// (opens <see cref="LoreReader"/>). Once read it settles to a calm green.
    /// </summary>
    public class LoreTerminal : MonoBehaviour, IOverhaulInteractable
    {
        public string loreId;
        public float radius = 2.2f;
        public System.Func<bool> isAvailable;

        Material _screen, _glyph;
        Transform _glyphT;
        Light _light;
        bool _read;
        float _nextPulse;
        static float _blockedUntil;

        public Vector3 InteractPosition => transform.position;
        public float InteractRadius => radius;
        public bool Available => isAvailable == null || isAvailable();
        public bool Read => _read;
        public bool CanInteract => Available && !LoreReader.IsOpen && Time.unscaledTime >= _blockedUntil;
        public string Title { get { var e = LoreLibrary.Get(loreId); return e != null ? e.title : loreId; } }

        /// <summary>Stops any terminal from opening for a moment (the key that closed the reader must not reopen it).</summary>
        public static void Block(float seconds) => _blockedUntil = Time.unscaledTime + seconds;

        public static LoreTerminal Create(Transform parent, string id, Vector3 groundPos, Vector3 faceTowards, System.Func<bool> available)
        {
            var go = new GameObject("Lore Terminal (" + id + ")");
            go.transform.SetParent(parent, false);
            Vector3 f = faceTowards - groundPos; f.y = 0f;
            go.transform.SetPositionAndRotation(groundPos, Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward, Vector3.up));
            var t = go.AddComponent<LoreTerminal>();
            t.loreId = id;
            t.isAvailable = available;
            t.Build();
            return t;
        }

        void Build()
        {
            var body = OverhaulMaterials.Lit(new LitSpec { name = "TerminalBody", color = OverhaulPalette.JoyWhite, smoothness = 0.45f, metallic = 0.1f });
            var dark = OverhaulMaterials.Lit(new LitSpec { name = "TerminalTrim", color = OverhaulPalette.JoyDark, smoothness = 0.3f });
            _screen = OverhaulMaterials.Lit(new LitSpec { name = "TerminalScreen", color = new Color(0.05f, 0.08f, 0.1f), smoothness = 0.9f, emissive = Color.black });

            var mb = new MeshBuilder();
            mb.Submesh(0);
            mb.Cylinder(new Vector3(0f, 0f, 0f), new Vector3(0f, 0.12f, 0f), 0.42f, 0.36f, 18, Color.white);
            mb.Strut(new Vector3(0f, 0.12f, 0f), new Vector3(0f, 0.95f, 0f), 0.06f, 10, Color.white, true);
            Quaternion tilt = Quaternion.Euler(-38f, 0f, 0f);
            Vector3 head = new Vector3(0f, 1.05f, 0f);
            mb.Box(head, new Vector3(0.74f, 0.5f, 0.07f), tilt, Color.white);
            mb.Submesh(1);
            mb.Box(head + tilt * new Vector3(0f, -0.29f, 0.0f), new Vector3(0.6f, 0.06f, 0.1f), tilt, Color.white);
            mb.Submesh(2);
            mb.Box(head + tilt * new Vector3(0f, 0.02f, 0.04f), new Vector3(0.62f, 0.38f, 0.012f), tilt, Color.white);
            var mesh = mb.ToMesh("LoreTerminal");
            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { body, dark, _screen };
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.6f, 0f);
            col.radius = 0.3f;
            col.height = 1.2f;

            _glyph = OverhaulMaterials.SoftGlow("TerminalGlyph", OverhaulPalette.SporeCyan, 2f, 1f, OverhaulMaterials.GlowShape.Ring, null, 1.5f, 0.6f, 0.16f);
            _glyphT = OverhaulPrimitives.EffectRenderer(transform, "Glyph", OverhaulPrimitives.UprightQuad, _glyph).transform;
            _glyphT.localScale = Vector3.one * 0.5f;

            var lg = new GameObject("Screen Light");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = head + tilt * new Vector3(0f, 0f, 0.4f);
            _light = OverhaulLights.Point(lg, OverhaulPalette.SporeCyan, 0f, 4f, false, false);
        }

        void OnEnable() => OverhaulInteract.Register(this);
        void OnDisable() => OverhaulInteract.Unregister(this);

        public void Interact()
        {
            if (!CanInteract) return;
            _read = true;
            LoreReader.Open(loreId);
        }

        void Update()
        {
            bool avail = Available;
            Color c = !avail ? new Color(0.25f, 0.3f, 0.35f) : _read ? OverhaulPalette.Good : OverhaulPalette.SporeCyan;
            float t = Time.time;
            float flicker = avail && !_read ? 0.85f + 0.15f * Mathf.Sin(t * 7f) * Mathf.Sin(t * 2.3f) : 1f;
            OverhaulMaterials.SetEmissive(_screen, c * (avail ? (_read ? 1.2f : 3.5f) * flicker : 0.15f));
            if (_light != null) OverhaulLights.SetLumens(_light, avail ? (_read ? 60f : 260f) * flicker : 0f);

            bool beacon = avail && !_read;
            _glyphT.gameObject.SetActive(beacon);
            if (beacon)
            {
                _glyphT.localPosition = new Vector3(0f, 1.9f + 0.12f * Mathf.Sin(t * 2f), 0f);
                var cam = Camera.main;
                if (cam != null) _glyphT.rotation = Quaternion.LookRotation(_glyphT.position - cam.transform.position, Vector3.up);
                _glyph.SetFloat("_Intensity", 1.6f + 0.8f * Mathf.Sin(t * 3f));
                if (t >= _nextPulse)
                {
                    _nextPulse = t + 2.6f;
                    VfxKit.Ring(transform.position, OverhaulPalette.SporeCyan, 2.2f, 1.4f, 1.8f, 0.4f);
                }
            }
        }

        void OnDestroy()
        {
            if (_screen != null) Destroy(_screen);
            if (_glyph != null) Destroy(_glyph);
        }
    }
}
