using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Mood lighting for each part of the game, blended smoothly on one runtime global Volume (above the scene's
    /// Sky and Fog volume) plus the sun:
    ///   Day (golden Mars afternoon, violet shadows, butterscotch haze) · Grove (cooler, bloomier — bioluminescence
    ///   pops) · Storm (dense ochre dust, dim sun) · Obelisk (violet cast, tense contrast, faint flicker) ·
    ///   Bunker (dim, cold) · Hope (warm and bright for the report and ending).
    /// Also fixes the white flash at start (exposure adapts instantly for the first moments, then smoothly within
    /// a sane range) and adds a breathing red overlay when life support is critical.
    /// </summary>
    [OverhaulModule(20)]
    public class AtmosphereDirector : MonoBehaviour
    {
        public float priority = 60f;
        public float transitionRate = 0.45f;

        struct Look
        {
            public float ev, saturation, contrast, temperature, tint, bloom, bloomThreshold, vignette, fogDistance, anisotropy, sun, sunKelvin, grain;
            public Color filter, shadows, highlights, vignetteColor, fogAlbedo, fogTint;

            public static Look Lerp(Look a, Look b, float t)
            {
                return new Look
                {
                    ev = Mathf.Lerp(a.ev, b.ev, t), saturation = Mathf.Lerp(a.saturation, b.saturation, t),
                    contrast = Mathf.Lerp(a.contrast, b.contrast, t), temperature = Mathf.Lerp(a.temperature, b.temperature, t),
                    tint = Mathf.Lerp(a.tint, b.tint, t), bloom = Mathf.Lerp(a.bloom, b.bloom, t),
                    bloomThreshold = Mathf.Lerp(a.bloomThreshold, b.bloomThreshold, t), vignette = Mathf.Lerp(a.vignette, b.vignette, t),
                    fogDistance = Mathf.Lerp(a.fogDistance, b.fogDistance, t), anisotropy = Mathf.Lerp(a.anisotropy, b.anisotropy, t),
                    sun = Mathf.Lerp(a.sun, b.sun, t), sunKelvin = Mathf.Lerp(a.sunKelvin, b.sunKelvin, t), grain = Mathf.Lerp(a.grain, b.grain, t),
                    filter = Color.Lerp(a.filter, b.filter, t), shadows = Color.Lerp(a.shadows, b.shadows, t),
                    highlights = Color.Lerp(a.highlights, b.highlights, t), vignetteColor = Color.Lerp(a.vignetteColor, b.vignetteColor, t),
                    fogAlbedo = Color.Lerp(a.fogAlbedo, b.fogAlbedo, t), fogTint = Color.Lerp(a.fogTint, b.fogTint, t),
                };
            }
        }

        static readonly Look Day = new Look
        {
            ev = 0f, saturation = 8f, contrast = 8f, temperature = 6f, tint = 0f, bloom = 0.22f, bloomThreshold = 0.95f,
            vignette = 0.2f, fogDistance = 190f, anisotropy = 0.6f, sun = 1f, sunKelvin = 5600f, grain = 0.08f,
            filter = new Color(1f, 0.975f, 0.93f), shadows = new Color(0.42f, 0.44f, 0.62f), highlights = new Color(0.64f, 0.53f, 0.42f),
            vignetteColor = new Color(0.12f, 0.06f, 0.04f), fogAlbedo = new Color(0.86f, 0.62f, 0.44f), fogTint = new Color(1f, 0.9f, 0.8f),
        };
        static readonly Look Grove = new Look
        {
            ev = 0.1f, saturation = 20f, contrast = 6f, temperature = -6f, tint = 4f, bloom = 0.42f, bloomThreshold = 0.82f,
            vignette = 0.26f, fogDistance = 120f, anisotropy = 0.55f, sun = 0.9f, sunKelvin = 6200f, grain = 0.08f,
            filter = new Color(0.95f, 0.96f, 1f), shadows = new Color(0.3f, 0.5f, 0.6f), highlights = new Color(0.62f, 0.5f, 0.7f),
            vignetteColor = new Color(0.08f, 0.04f, 0.12f), fogAlbedo = new Color(0.62f, 0.55f, 0.78f), fogTint = new Color(0.85f, 0.85f, 1f),
        };
        static readonly Look Storm = new Look
        {
            ev = -0.7f, saturation = -18f, contrast = 12f, temperature = 18f, tint = 0f, bloom = 0.15f, bloomThreshold = 1f,
            vignette = 0.38f, fogDistance = 24f, anisotropy = 0.75f, sun = 0.35f, sunKelvin = 4200f, grain = 0.25f,
            filter = new Color(1f, 0.86f, 0.66f), shadows = new Color(0.5f, 0.4f, 0.35f), highlights = new Color(0.7f, 0.5f, 0.3f),
            vignetteColor = new Color(0.2f, 0.1f, 0.03f), fogAlbedo = new Color(0.8f, 0.55f, 0.32f), fogTint = new Color(1f, 0.75f, 0.5f),
        };
        static readonly Look ObeliskLook = new Look
        {
            ev = -0.25f, saturation = -5f, contrast = 14f, temperature = -4f, tint = 12f, bloom = 0.35f, bloomThreshold = 0.9f,
            vignette = 0.32f, fogDistance = 75f, anisotropy = 0.6f, sun = 0.8f, sunKelvin = 6500f, grain = 0.15f,
            filter = new Color(0.93f, 0.86f, 1f), shadows = new Color(0.45f, 0.3f, 0.55f), highlights = new Color(0.6f, 0.5f, 0.7f),
            vignetteColor = new Color(0.12f, 0.03f, 0.14f), fogAlbedo = new Color(0.6f, 0.45f, 0.72f), fogTint = new Color(0.9f, 0.8f, 1f),
        };
        static readonly Look BunkerLook = new Look
        {
            ev = -0.6f, saturation = -25f, contrast = 10f, temperature = -15f, tint = 0f, bloom = 0.3f, bloomThreshold = 0.9f,
            vignette = 0.45f, fogDistance = 50f, anisotropy = 0.5f, sun = 0.55f, sunKelvin = 7000f, grain = 0.2f,
            filter = new Color(0.86f, 0.93f, 1f), shadows = new Color(0.35f, 0.45f, 0.5f), highlights = new Color(0.55f, 0.6f, 0.65f),
            vignetteColor = new Color(0.02f, 0.05f, 0.07f), fogAlbedo = new Color(0.5f, 0.6f, 0.62f), fogTint = new Color(0.8f, 0.9f, 1f),
        };
        static readonly Look Hope = new Look
        {
            ev = 0.25f, saturation = 15f, contrast = 4f, temperature = 10f, tint = 2f, bloom = 0.4f, bloomThreshold = 0.88f,
            vignette = 0.15f, fogDistance = 230f, anisotropy = 0.6f, sun = 1.1f, sunKelvin = 5200f, grain = 0.05f,
            filter = new Color(1f, 0.96f, 0.88f), shadows = new Color(0.45f, 0.42f, 0.6f), highlights = new Color(0.7f, 0.58f, 0.42f),
            vignetteColor = new Color(0.12f, 0.07f, 0.03f), fogAlbedo = new Color(0.95f, 0.75f, 0.55f), fogTint = new Color(1f, 0.92f, 0.82f),
        };

        Volume _volume, _critical;
        VolumeProfile _profile, _criticalProfile;
        Exposure _exposure;
        ColorAdjustments _color, _critColor;
        WhiteBalance _white;
        SplitToning _split;
        Bloom _bloom;
        Vignette _vignette, _critVignette;
        Fog _fog;
        FilmGrain _grain;
        ChromaticAberration _critChroma;

        Light _sun;
        float _sunIntensity0, _sunKelvin0;
        bool _sunKelvinUsed;

        float _wGrove, _wStorm, _wObelisk, _wBunker, _wHope, _wCritical;
        float _started;
        bool _reportCard;

        void OnEnable() => GameSignals.ReportCardShown += OnReport;
        void OnDisable() => GameSignals.ReportCardShown -= OnReport;
        void OnReport(string g) => _reportCard = true;

        void Start()
        {
            _started = Time.unscaledTime;
            _volume = NewVolume("Atmosphere Volume", priority, out _profile);
            _volume.weight = 1f;
            _exposure = _profile.Add<Exposure>(false);
            _color = _profile.Add<ColorAdjustments>(false);
            _white = _profile.Add<WhiteBalance>(false);
            _split = _profile.Add<SplitToning>(false);
            _bloom = _profile.Add<Bloom>(false);
            _vignette = _profile.Add<Vignette>(false);
            _fog = _profile.Add<Fog>(false);
            _grain = _profile.Add<FilmGrain>(false);

            // Instant adaptation at first (no white flash), then smooth adaptation inside a sane range.
            _exposure.mode.Override(ExposureMode.AutomaticHistogram);
            _exposure.limitMin.Override(9f);
            _exposure.limitMax.Override(14f);
            _exposure.adaptationMode.Override(AdaptationMode.Fixed);
            _exposure.adaptationSpeedDarkToLight.Override(2.5f);
            _exposure.adaptationSpeedLightToDark.Override(1.5f);

            _vignette.mode.Override(VignetteMode.Procedural);
            _vignette.smoothness.Override(0.45f);
            _vignette.rounded.Override(false);
            _bloom.scatter.Override(0.7f);
            _fog.enabled.Override(true);
            _fog.enableVolumetricFog.Override(true);
            _grain.type.Override(FilmGrainLookup.Thin2);
            _grain.response.Override(0.8f);

            _critical = NewVolume("Critical Life Support Volume", priority + 10f, out _criticalProfile);
            _critVignette = _criticalProfile.Add<Vignette>(false);
            _critColor = _criticalProfile.Add<ColorAdjustments>(false);
            _critChroma = _criticalProfile.Add<ChromaticAberration>(false);
            _critVignette.mode.Override(VignetteMode.Procedural);
            _critVignette.color.Override(new Color(0.55f, 0.02f, 0.02f));
            _critVignette.smoothness.Override(0.75f);
            _critColor.saturation.Override(-35f);

            FindSun();
        }

        Volume NewVolume(string name, float prio, out VolumeProfile profile)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = prio;
            v.weight = 0f;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = name + " (runtime)";
            v.sharedProfile = profile;
            return v;
        }

        void FindSun()
        {
            _sun = RenderSettings.sun;
            if (_sun == null)
            {
                float best = -1f;
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && l.intensity > best) { best = l.intensity; _sun = l; }
            }
            if (_sun == null) return;
            _sunIntensity0 = _sun.intensity;
            _sunKelvin0 = _sun.colorTemperature;
            _sunKelvinUsed = _sun.useColorTemperature;
        }

        void OnDestroy()
        {
            if (_sun != null) { _sun.intensity = _sunIntensity0; _sun.colorTemperature = _sunKelvin0; }
            if (_profile != null) Destroy(_profile);
            if (_criticalProfile != null) Destroy(_criticalProfile);
        }

        void Update()
        {
            if (_profile == null) return;
            float dt = Time.unscaledDeltaTime;
            float age = Time.unscaledTime - _started;
            if (age > 1.2f && _exposure.adaptationMode.value != AdaptationMode.Progressive)
                _exposure.adaptationMode.Override(AdaptationMode.Progressive);

            // ---- mood weights ----
            var gfm = GameFlowManager.Instance;
            var ph = gfm != null ? gfm.Phase : GamePhase.Sandbox;
            var bridge = SignalBridge.Instance;
            var player = bridge != null ? bridge.Player : null;
            Vector3 pp = player != null ? player.transform.position : Vector3.zero;

            float grove = TripDirector.InGrove ? 1f : Near(pp, OverhaulLandmarks.GroveCenter, 8f, 18f);
            bool storm = (bridge != null && bridge.Tornado != null && bridge.Tornado.IsActive) || ph == GamePhase.TornadoStrike;
            float obelisk = 0f;
            var ob = bridge != null ? bridge.Obelisk : null;
            if (ph == GamePhase.ObeliskShield || ph == GamePhase.ObeliskCore) obelisk = 0.85f;
            else if (ob != null && ob.Stage != ObeliskStage.Destroyed) obelisk = 0.6f * Near(pp, OverhaulLandmarks.Obelisk, 6f, 16f);
            bool bunker = ph == GamePhase.Bunker;
            bool hope = _reportCard || ph == GamePhase.FinalReport || ph == GamePhase.OutroVideo || ph == GamePhase.End;

            float k = transitionRate * dt;
            _wGrove = Mathf.MoveTowards(_wGrove, grove, k);
            _wStorm = Mathf.MoveTowards(_wStorm, storm ? 1f : 0f, k * 0.8f);
            _wObelisk = Mathf.MoveTowards(_wObelisk, obelisk, k);
            _wBunker = Mathf.MoveTowards(_wBunker, bunker ? 1f : 0f, k);
            _wHope = Mathf.MoveTowards(_wHope, hope ? 1f : 0f, k * 0.6f);

            Look look = Day;
            look = Look.Lerp(look, Grove, _wGrove);
            look = Look.Lerp(look, ObeliskLook, _wObelisk);
            look = Look.Lerp(look, Storm, _wStorm);
            look = Look.Lerp(look, BunkerLook, _wBunker);
            look = Look.Lerp(look, Hope, _wHope);
            if (_wObelisk > 0.01f) look.ev += 0.05f * _wObelisk * Mathf.Sin(Time.time * 13f) * Mathf.Sin(Time.time * 3.1f);

            Apply(look);
            UpdateCritical(dt);
        }

        static float Near(Vector3 p, Vector3 target, float full, float none)
        {
            if (target == Vector3.zero) return 0f;
            Vector3 d = target - p; d.y = 0f;
            return 1f - Mathf.Clamp01((d.magnitude - full) / Mathf.Max(0.01f, none - full));
        }

        void Apply(Look l)
        {
            _volume.weight = 1f;
            _exposure.compensation.Override(l.ev);
            _color.saturation.Override(l.saturation);
            _color.contrast.Override(l.contrast);
            _color.colorFilter.Override(l.filter);
            _white.temperature.Override(l.temperature);
            _white.tint.Override(l.tint);
            _split.shadows.Override(l.shadows);
            _split.highlights.Override(l.highlights);
            _split.balance.Override(10f);
            _bloom.intensity.Override(l.bloom);
            _bloom.threshold.Override(l.bloomThreshold);
            _vignette.intensity.Override(l.vignette);
            _vignette.color.Override(l.vignetteColor);
            _fog.meanFreePath.Override(l.fogDistance);
            _fog.albedo.Override(l.fogAlbedo);
            _fog.tint.Override(l.fogTint);
            _fog.anisotropy.Override(l.anisotropy);
            _grain.intensity.Override(l.grain);

            if (_sun != null && _sunIntensity0 > 0f)
            {
                _sun.intensity = _sunIntensity0 * l.sun;
                if (_sunKelvinUsed) _sun.colorTemperature = l.sunKelvin;
            }
        }

        void UpdateCritical(float dt)
        {
            var life = LifeSupport.Instance;
            float target = 0f;
            if (life != null && life.Active)
            {
                float v = life.Value01;
                target = 1f - Mathf.Clamp01((v - 0.08f) / 0.2f);   // starts below ~28 %, full near 8 %
            }
            _wCritical = Mathf.MoveTowards(_wCritical, target, dt * 0.8f);
            _critical.weight = _wCritical;
            if (_wCritical <= 0f) return;
            // a slow breathing pulse (~0.9 Hz) — urgent but not strobing
            float beat = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.6f);
            _critVignette.intensity.Override(0.32f + 0.18f * beat);
            _critChroma.intensity.Override(0.15f + 0.2f * beat);
        }
    }
}
