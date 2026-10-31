using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// What the spore trip looks like. Three looks, mixed by <see cref="TripDirector"/>'s ambient intensity and its
    /// two song flashes, each clearly different:
    ///  - Ambient (faint, always readable): a little extra saturation and a soft hue sway near the Mycari and in
    ///    the grove. No distortion, no camera motion.
    ///  - Hit (right mushroom, solved song): warm and harmonious, with golden bloom, a lift in exposure and colour
    ///    toward the note, a gentle outward "breath", and a ring of light rolling out from the pad (warp pass).
    ///  - Miss (wrong mushroom): cold and dissonant, with draining colour, magenta/cyan split toning, hard colour
    ///    fringes, a pinch, a dark violet vignette, a horizontal shiver (warp pass) and a short camera shake.
    ///  - Solved song: the full trip for a few seconds, with spinning rainbow hues, a flood of bloom, breathing
    ///    lens, swirl and kaleidoscope (warp pass), and a slow camera roll.
    ///  - Failed song: the sour version, with colour drained to near grey, violent fringes and slices tearing
    ///    sideways, flickers of negative, a crushing vignette and a hard shake.
    ///  - Thin air (low life support): more trip, a dark tunnel at the edges, sight blurring in spells, and a
    ///    slow, heavy camera sway (unlike the quick shakes above).
    /// Stock HDRP overrides on a high-priority volume, plus a fullscreen pass ("Hidden/Eschropita/TripWarp") that
    /// switches itself off if the shader is unavailable.
    /// </summary>
    [OverhaulModule(16)]
    public class TripVisuals : MonoBehaviour
    {
        public float volumePriority = 120f;
        [Tooltip("Peak roll of the miss shake, in degrees.")]
        public float missShakeDegrees = 1.6f;

        Volume _volume;
        VolumeProfile _profile;
        ColorAdjustments _color;
        ChromaticAberration _chroma;
        LensDistortion _lens;
        Bloom _bloom;
        Vignette _vignette;
        WhiteBalance _white;
        SplitToning _split;

        CustomPassVolume _passVolume;
        TripWarpPass _pass;
        Material _warp;
        bool _cmHooked;

        static readonly int IdIntensity = Shader.PropertyToID("_TripIntensity");
        static readonly int IdTime = Shader.PropertyToID("_TripTime");
        static readonly int IdHit = Shader.PropertyToID("_TripHit");
        static readonly int IdHitAge = Shader.PropertyToID("_TripHitAge");
        static readonly int IdMiss = Shader.PropertyToID("_TripMiss");
        static readonly int IdPulseCenter = Shader.PropertyToID("_TripPulseCenter");
        static readonly int IdTint = Shader.PropertyToID("_TripTint");
        static readonly int IdSolve = Shader.PropertyToID("_TripSolve");
        static readonly int IdFail = Shader.PropertyToID("_TripFail");
        static readonly int IdBlur = Shader.PropertyToID("_TripBlur");

        void Start()
        {
            BuildVolume();
            BuildWarpPass();
        }

        void OnEnable()
        {
            if (!_cmHooked) { CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated); _cmHooked = true; }
        }

        void OnDisable()
        {
            if (_cmHooked) { CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated); _cmHooked = false; }
            if (_volume != null) _volume.weight = 0f;
            if (_passVolume != null) _passVolume.enabled = false;
        }

        void OnDestroy()
        {
            if (_profile != null) Destroy(_profile);
            if (_warp != null) Destroy(_warp);
        }

        void BuildVolume()
        {
            var go = new GameObject("Trip Volume");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = volumePriority;
            _volume.weight = 0f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Trip (runtime)";
            _volume.sharedProfile = _profile;

            _color = _profile.Add<ColorAdjustments>(false);
            _chroma = _profile.Add<ChromaticAberration>(false);
            _lens = _profile.Add<LensDistortion>(false);
            _bloom = _profile.Add<Bloom>(false);
            _vignette = _profile.Add<Vignette>(false);
            _white = _profile.Add<WhiteBalance>(false);
            _split = _profile.Add<SplitToning>(false);

            _vignette.mode.Override(VignetteMode.Procedural);
            _vignette.rounded.Override(true);
            _vignette.smoothness.Override(0.55f);
            _bloom.threshold.Override(0.9f);
            _bloom.scatter.Override(0.7f);
        }

        void BuildWarpPass()
        {
            var shader = Resources.Load<Shader>("Overhaul/Shaders/TripWarp");
            if (shader == null) shader = Shader.Find("Hidden/Eschropita/TripWarp");
            if (shader == null || !shader.isSupported)
            {
                Debug.Log("[Overhaul] Trip warp shader unavailable; using colour grading only.");
                return;
            }
            _warp = new Material(shader) { name = "TripWarp (runtime)", hideFlags = HideFlags.DontSave };
            var go = new GameObject("Trip Warp Pass");
            go.transform.SetParent(transform, false);
            _passVolume = go.AddComponent<CustomPassVolume>();
            _passVolume.isGlobal = true;
            _passVolume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            _pass = _passVolume.AddPassOfType<TripWarpPass>();
            if (_pass != null)
            {
                _pass.name = "Spore Trip Warp";
                _pass.material = _warp;
            }
            _passVolume.enabled = false;
        }

        void LateUpdate()
        {
            float amb = TripDirector.Intensity;          // already low by design
            float hit = TripDirector.HitPulse;
            float miss = TripDirector.MissPulse;
            float t = TripDirector.Clock;
            Color tint = TripDirector.Tint;
            // ease the flashes so they rise sharply and settle softly
            float h = hit * hit * (3f - 2f * hit);
            float m = miss * miss * (3f - 2f * miss);
            float a = Mathf.Clamp01(amb * 0.8f);          // the ambient look stays a wash
            float sp = TripDirector.SolvePeak, fp = TripDirector.FailPeak;
            float S = Mathf.Sqrt(sp), F = Mathf.Sqrt(fp);  // stay near full strength, then fall away
            float x = TripDirector.Hypoxia, blur = TripDirector.Blur;

            if (_volume != null)
            {
                _volume.weight = Mathf.Clamp01(Mathf.Max(Mathf.Max(a, Mathf.Max(h, m)), Mathf.Max(Mathf.Max(S, F), x * 0.8f)));
                if (_volume.weight > 0.001f) Animate(a, h, m, S, F, x, blur, t, tint);
            }

            if (_passVolume != null && _warp != null)
            {
                bool on = amb > 0.02f || hit > 0.01f || miss > 0.01f || sp > 0.01f || fp > 0.01f || blur > 0.01f;
                if (_passVolume.enabled != on) _passVolume.enabled = on;
                if (on)
                {
                    _warp.SetFloat(IdIntensity, amb);
                    _warp.SetFloat(IdTime, t);
                    _warp.SetFloat(IdHit, hit);
                    _warp.SetFloat(IdHitAge, TripDirector.HitAge);
                    _warp.SetFloat(IdMiss, miss);
                    _warp.SetColor(IdTint, tint);
                    _warp.SetFloat(IdSolve, S);
                    _warp.SetFloat(IdFail, F);
                    _warp.SetFloat(IdBlur, blur);
                    var cam = Camera.main;
                    Vector2 c = new Vector2(0.5f, 0.5f);
                    if (cam != null)
                    {
                        Vector3 vp = cam.WorldToViewportPoint(TripDirector.PulseSource);
                        if (vp.z > 0f) c = new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
                    }
                    _warp.SetVector(IdPulseCenter, new Vector4(c.x, c.y, 0f, 0f));
                }
            }
        }

        /// <summary>
        /// Every override is a weighted mix of the looks (ambient a, hit h, miss m, solved S, failed F, thin air x);
        /// the volume weight sets how far the frame moves from the scene's own grade toward that mix.
        /// </summary>
        void Animate(float a, float h, float m, float S, float F, float x, float blur, float t, Color tint)
        {
            float sum = Mathf.Max(1e-4f, a + h + m + S + F + x);
            float A = a / sum, H = h / sum, M = m / sum, Sw = S / sum, Fw = F / sum, X = x / sum;
            float jitter = Mathf.Sin(t * 37f) * 0.6f + Mathf.Sin(t * 53f + 1.3f) * 0.4f;   // fast, irregular (miss / fail)
            float ut = Time.unscaledTime;
            Color warm = Color.Lerp(new Color(1f, 0.86f, 0.62f), tint, 0.45f);
            Color rainbow = Color.HSVToRGB(Mathf.Repeat(ut * 0.35f, 1f), 0.7f, 1f);

            _color.hueShift.Override(A * 14f * Mathf.Sin(t * 0.13f) + M * 14f * jitter + Sw * 180f * Mathf.Sin(ut * 1.3f)
                                     + Fw * 30f * jitter + X * 25f * Mathf.Sin(ut * 0.31f));
            _color.saturation.Override(A * 22f + H * 32f - M * 45f + Sw * 75f - Fw * 85f - X * 15f);
            _color.contrast.Override(A * 4f + H * 6f + M * 26f + Sw * 15f + Fw * 45f + X * 10f);
            _color.postExposure.Override(H * 0.32f - M * 0.22f + Sw * 0.35f * (0.6f + 0.4f * Mathf.Sin(ut * 4f)) - Fw * 0.45f - X * 0.25f);
            _color.colorFilter.Override(Color.white * A + Color.Lerp(Color.white, warm, 0.35f) * H + new Color(0.82f, 0.86f, 1f) * M
                                        + Color.Lerp(Color.white, rainbow, 0.35f) * Sw + new Color(0.9f, 0.62f, 0.7f) * Fw
                                        + new Color(1f, 0.8f, 0.68f) * X);

            _chroma.intensity.Override(Mathf.Clamp01(A * 0.12f + H * 0.12f + M * 0.9f + Sw * 0.7f + Fw + X * 0.35f));

            _lens.intensity.Override(H * 0.16f - M * (0.24f + 0.06f * jitter) + Sw * 0.35f * Mathf.Sin(ut * 2.6f)
                                     - Fw * (0.45f + 0.12f * jitter) + X * 0.12f * Mathf.Sin(ut * 0.5f));
            _lens.xMultiplier.Override(1f);
            _lens.yMultiplier.Override(1f);
            _lens.center.Override(new Vector2(0.5f + (M + Fw) * 0.025f * jitter, 0.5f));
            _lens.scale.Override(1f - H * 0.04f - Sw * 0.06f);

            _bloom.intensity.Override(Mathf.Clamp01(A * 0.22f + H * 0.65f + M * 0.12f + Sw + Fw * 0.15f + X * 0.3f));
            _bloom.tint.Override(Color.white * A + warm * H + new Color(0.7f, 0.8f, 1f) * M + rainbow * Sw
                                 + new Color(1f, 0.4f, 0.45f) * Fw + new Color(1f, 0.75f, 0.6f) * X);

            _vignette.color.Override(Color.black * A + new Color(0.35f, 0.22f, 0.05f) * H + new Color(0.22f, 0.02f, 0.3f) * M
                                     + Color.HSVToRGB(Mathf.Repeat(ut * 0.35f + 0.5f, 1f), 0.8f, 0.45f) * Sw
                                     + new Color(0.3f, 0f, 0.05f) * Fw + new Color(0.12f, 0.03f, 0.02f) * X);
            _vignette.intensity.Override(Mathf.Clamp01(A * 0.14f + H * 0.1f + M * 0.45f + Sw * 0.3f + Fw * 0.7f
                                                       + X * (0.38f + 0.25f * blur)));
            _vignette.center.Override(new Vector2(0.5f, 0.5f));

            _white.temperature.Override(A * 8f * Mathf.Sin(t * 0.19f) + H * 14f - M * 32f + Sw * 40f * Mathf.Sin(ut * 0.9f) - Fw * 40f + X * 18f);
            _white.tint.Override(A * 8f * Mathf.Sin(t * 0.23f + 1.3f) + M * 22f + Sw * 40f * Mathf.Cos(ut * 1.1f) + Fw * 30f);

            Color neutral = new Color(0.5f, 0.5f, 0.5f);   // grey = no toning
            Color hs = Color.HSVToRGB(Mathf.Repeat(ut * 0.2f, 1f), 0.6f, 0.45f);
            _split.shadows.Override(neutral * (A + H + X) + new Color(0.62f, 0.25f, 0.7f) * M + hs * Sw + new Color(0.55f, 0.1f, 0.2f) * Fw);
            _split.highlights.Override(neutral * (A + X) + Color.Lerp(neutral, new Color(0.75f, 0.62f, 0.4f), 0.6f) * H
                                       + new Color(0.3f, 0.75f, 0.78f) * M + Color.HSVToRGB(Mathf.Repeat(ut * 0.2f + 0.5f, 1f), 0.6f, 0.7f) * Sw
                                       + new Color(0.35f, 0.8f, 0.75f) * Fw);
            _split.balance.Override(0f);
        }

        // Camera: a wrong note shakes it a little and a failed song shakes it hard (quick and irregular); a solved
        // song rolls it slowly like the old full trip; thin air makes it sway heavily, like losing balance.
        void OnCameraUpdated(CinemachineBrain brain)
        {
            float miss = TripDirector.MissPulse, fail = TripDirector.FailPeak, solve = TripDirector.SolvePeak, x = TripDirector.Hypoxia;
            if (brain == null || (miss <= 0.001f && fail <= 0.001f && solve <= 0.001f && x <= 0.001f)) return;
            var cam = brain.OutputCamera;
            if (cam == null) return;
            float t = Time.unscaledTime;
            float shake = Mathf.Sin(t * 41f) * 0.7f + Mathf.Sin(t * 67f + 2f) * 0.3f;
            float roll = missShakeDegrees * miss * miss * shake
                       + 3.5f * fail * fail * (Mathf.Sin(t * 29f + 0.7f) * 0.6f + Mathf.Sin(t * 47f) * 0.4f)
                       + 4f * Mathf.Sqrt(solve) * Mathf.Sin(t * 0.9f)
                       + 2.6f * x * (Mathf.Sin(t * 0.27f) * 0.7f + Mathf.Sin(t * 0.61f + 1.1f) * 0.3f);
            // thin air also lets the view droop and drift a little, slowly
            Vector3 drift = new Vector3(Mathf.Sin(t * 0.19f), -0.6f - 0.4f * Mathf.Sin(t * 0.33f), 0f) * (0.25f * x);
            cam.transform.position += cam.transform.rotation * drift;
            cam.transform.rotation = cam.transform.rotation * Quaternion.AngleAxis(roll, Vector3.forward);
        }
    }

    /// <summary>
    /// Copies the camera colour into a private HDR buffer, then redraws the screen through the warp material
    /// (reading the copy, so there is no read/write hazard). Injected before post-processing so bloom, the trip
    /// colour grading and tonemapping apply on top.
    /// </summary>
    [System.Serializable]
    public class TripWarpPass : CustomPass
    {
        public Material material;
        RTHandle _source;
        static readonly int IdSource = Shader.PropertyToID("_TripSource");

        protected override bool executeInSceneView => false;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            _source = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                                      colorFormat: GraphicsFormat.R16G16B16A16_SFloat, useDynamicScale: true,
                                      name: "TripWarpSource");
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (material == null || _source == null) return;
            CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, _source);
            ctx.propertyBlock.SetTexture(IdSource, _source);
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            CoreUtils.DrawFullScreen(ctx.cmd, material, ctx.propertyBlock, shaderPassId: 0);
        }

        protected override void Cleanup()
        {
            if (_source != null) _source.Release();
            _source = null;
        }
    }
}
