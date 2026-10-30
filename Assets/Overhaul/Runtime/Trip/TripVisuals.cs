using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// What the spore trip looks like. Two layers, both driven by <see cref="TripDirector"/>:
    ///  1. A high-priority global Volume whose weight follows the trip intensity: drifting hue, boosted saturation,
    ///     breathing lens distortion, chromatic fringes, rainbow-tinted bloom and vignette, wavering white balance.
    ///     Robust (stock HDRP overrides) — this alone carries the effect.
    ///  2. A fullscreen custom pass ("Hidden/Eschropita/TripWarp") that liquefies the image: swirling, breathing
    ///     domain warp, ripples that radiate from each mushroom note, colour splitting along the flow and, in the
    ///     grove, a kaleidoscope ghost. Skipped automatically if the shader is unavailable or failed to compile.
    /// Plus a slow camera roll applied after Cinemachine each frame. Mushroom note colours stay readable: while a
    /// melody is playing or waiting for the player the hue drift is damped.
    /// </summary>
    [OverhaulModule(16)]
    public class TripVisuals : MonoBehaviour
    {
        public float volumePriority = 120f;
        public float maxRollDegrees = 3.2f;

        Volume _volume;
        VolumeProfile _profile;
        ColorAdjustments _color;
        ChromaticAberration _chroma;
        LensDistortion _lens;
        Bloom _bloom;
        Vignette _vignette;
        WhiteBalance _white;
        SplitToning _split;
        PaniniProjection _panini;
        ChannelMixer _mixer;

        CustomPassVolume _passVolume;
        TripWarpPass _pass;
        Material _warp;
        bool _cmHooked;

        static readonly int IdIntensity = Shader.PropertyToID("_TripIntensity");
        static readonly int IdTime = Shader.PropertyToID("_TripTime");
        static readonly int IdPulse = Shader.PropertyToID("_TripPulse");
        static readonly int IdPulseCenter = Shader.PropertyToID("_TripPulseCenter");
        static readonly int IdTint = Shader.PropertyToID("_TripTint");
        static readonly int IdHue = Shader.PropertyToID("_TripHueDrift");
        static readonly int IdKaleido = Shader.PropertyToID("_TripKaleido");

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
            _panini = _profile.Add<PaniniProjection>(false);
            _mixer = _profile.Add<ChannelMixer>(false);

            _vignette.mode.Override(VignetteMode.Procedural);
            _vignette.rounded.Override(true);
            _vignette.smoothness.Override(0.62f);
            _bloom.threshold.Override(0.85f);
            _panini.cropToFit.Override(1f);
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
            float I = TripDirector.Intensity;
            float t = TripDirector.Clock;
            float pulse = TripDirector.Pulse;
            bool song = TripDirector.SongActive;
            bool grove = TripDirector.InGrove;
            Color tint = TripDirector.Tint;

            if (_volume != null)
            {
                _volume.weight = I <= 0.001f ? 0f : Mathf.Pow(I, 0.8f);
                if (_volume.weight > 0f) Animate(I, t, pulse, song, grove, tint);
            }

            if (_passVolume != null && _warp != null)
            {
                bool on = I > 0.02f;
                if (_passVolume.enabled != on) _passVolume.enabled = on;
                if (on)
                {
                    _warp.SetFloat(IdIntensity, I);
                    _warp.SetFloat(IdTime, t);
                    _warp.SetFloat(IdPulse, pulse);
                    _warp.SetColor(IdTint, tint);
                    float drift = song ? 0.25f * Mathf.Sin(t * 0.4f) : (grove ? 1.4f : 0.6f) * Mathf.Sin(t * 0.17f);
                    _warp.SetFloat(IdHue, drift * I);
                    _warp.SetFloat(IdKaleido, grove ? Mathf.Clamp01((I - 0.55f) / 0.45f) : 0f);
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

        void Animate(float I, float t, float pulse, bool song, bool grove, Color tint)
        {
            float deep = Mathf.Clamp01((I - 0.5f) / 0.5f);     // 0 for the mild level, rising to 1 in the grove
            float hueAmp = song ? 18f : Mathf.Lerp(55f, 170f, deep);
            _color.hueShift.Override(hueAmp * Mathf.Sin(t * 0.13f) + (song ? 0f : 25f * Mathf.Sin(t * 0.41f)));
            _color.saturation.Override(38f + 22f * deep + 12f * Mathf.Sin(t * 0.7f) + 20f * pulse);
            _color.contrast.Override(10f + 10f * deep);
            _color.postExposure.Override(0.12f * Mathf.Sin(t * 0.9f) + 0.25f * pulse);
            _color.colorFilter.Override(Color.Lerp(Color.white, tint, 0.18f + 0.12f * deep));

            _chroma.intensity.Override(Mathf.Clamp01(0.25f + 0.3f * (0.5f + 0.5f * Mathf.Sin(t * 1.3f)) + 0.35f * deep + 0.35f * pulse));

            _lens.intensity.Override(Mathf.Clamp(0.22f * Mathf.Sin(t * 0.6f) + 0.18f * deep * Mathf.Sin(t * 0.23f) - 0.18f * pulse, -0.6f, 0.6f));
            _lens.xMultiplier.Override(0.65f + 0.35f * Mathf.Sin(t * 0.27f));
            _lens.yMultiplier.Override(0.65f + 0.35f * Mathf.Cos(t * 0.33f));
            _lens.center.Override(new Vector2(0.5f + 0.05f * Mathf.Sin(t * 0.21f), 0.5f + 0.05f * Mathf.Cos(t * 0.17f)));
            _lens.scale.Override(0.96f);

            Color rainbow = Color.HSVToRGB(Mathf.Repeat(t * 0.05f, 1f), 0.6f, 1f);
            _bloom.intensity.Override(Mathf.Clamp01(0.28f + 0.22f * deep + 0.3f * pulse));
            _bloom.scatter.Override(0.78f);
            _bloom.tint.Override(Color.Lerp(rainbow, tint, 0.4f));

            Color vigCol = Color.HSVToRGB(Mathf.Repeat(t * 0.05f + 0.5f, 1f), 0.75f, 0.35f);
            _vignette.color.Override(vigCol);
            _vignette.intensity.Override(Mathf.Clamp01(0.28f + 0.12f * Mathf.Sin(t * 0.9f) + 0.12f * deep + 0.1f * pulse));
            _vignette.center.Override(new Vector2(0.5f + 0.03f * Mathf.Sin(t * 0.3f), 0.5f + 0.03f * Mathf.Cos(t * 0.26f)));

            _white.temperature.Override(28f * Mathf.Sin(t * 0.19f));
            _white.tint.Override(32f * Mathf.Sin(t * 0.23f + 1.3f));

            float h = Mathf.Repeat(t * 0.04f, 1f);
            _split.shadows.Override(Color.HSVToRGB(h, 0.6f, 0.45f));
            _split.highlights.Override(Color.HSVToRGB(Mathf.Repeat(h + 0.5f, 1f), 0.5f, 0.65f));
            _split.balance.Override(25f * Mathf.Sin(t * 0.4f));

            _panini.distance.Override(0.22f + 0.22f * deep * (0.5f + 0.5f * Mathf.Sin(t * 0.15f)));

            // Deep in the grove the colour channels slowly bleed into each other.
            float k = song ? 0f : deep * (0.5f + 0.5f * Mathf.Sin(t * 0.11f)) * 35f;
            _mixer.redOutRedIn.Override(100f - k);
            _mixer.redOutGreenIn.Override(k);
            _mixer.greenOutGreenIn.Override(100f - k);
            _mixer.greenOutBlueIn.Override(k);
            _mixer.blueOutBlueIn.Override(100f - k);
            _mixer.blueOutRedIn.Override(k);
        }

        void OnCameraUpdated(CinemachineBrain brain)
        {
            float I = TripDirector.Intensity;
            if (brain == null || I <= 0.001f) return;
            var cam = brain.OutputCamera;
            if (cam == null) return;
            float t = TripDirector.Clock;
            float roll = maxRollDegrees * I * (Mathf.Sin(t * 0.37f) * 0.7f + Mathf.Sin(t * 0.61f + 1.7f) * 0.3f);
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
