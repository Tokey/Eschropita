using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Daffodil's scan on the ground itself: while V is held, waves roll out from Daffodil across the terrain, every
    /// structure and every rock ("Hidden/Eschropita/ScanSweep", a depth-based fullscreen pass), each one leaving a
    /// fading measuring grid and height contours behind; a light beam drops from Daffodil to the ground. It replaces
    /// the HUD's screen-only sweep line (the HUD's tint and labels stay). Switches itself off if the shader is unavailable.
    /// </summary>
    [OverhaulModule(41)]
    public class ScanSweep : MonoBehaviour
    {
        [Tooltip("How far a wave travels (m).")]
        public float range = 34f;
        [Tooltip("Seconds for a wave to reach full range.")]
        public float waveSeconds = 1.25f;
        [Tooltip("Seconds between waves while scanning.")]
        public float interval = 1.6f;
        [ColorUsage(false, true)] public Color color = new Color(0.25f, 1.6f, 1.75f);

        CustomPassVolume _volume;
        ScanSweepPass _pass;
        Material _mat;
        Light _beam;
        float _waveStart = -99f, _strength;
        bool _scanning, _hudLineHidden;

        static readonly int IdOrigin = Shader.PropertyToID("_ScanOrigin");
        static readonly int IdParams = Shader.PropertyToID("_ScanParams");
        static readonly int IdColor = Shader.PropertyToID("_ScanColor");
        static readonly int IdTime = Shader.PropertyToID("_ScanTime");

        void OnEnable()
        {
            GameSignals.ScanStarted += OnScanStart;
            GameSignals.ScanStopped += OnScanStop;
        }

        void OnDisable()
        {
            GameSignals.ScanStarted -= OnScanStart;
            GameSignals.ScanStopped -= OnScanStop;
            if (_volume != null) _volume.enabled = false;
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }

        void Start()
        {
            var shader = Resources.Load<Shader>("Overhaul/Shaders/ScanSweep");
            if (shader == null || !shader.isSupported)
            {
                Debug.Log("[Overhaul] Scan sweep shader unavailable; keeping the HUD's scan line.");
                return;
            }
            _mat = new Material(shader) { name = "ScanSweep (runtime)", hideFlags = HideFlags.DontSave };
            var go = new GameObject("Scan Sweep Pass");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<CustomPassVolume>();
            _volume.isGlobal = true;
            _volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            _pass = _volume.AddPassOfType<ScanSweepPass>();
            if (_pass != null) { _pass.name = "Daffodil Scan"; _pass.material = _mat; }
            _volume.enabled = false;

            var beamGo = new GameObject("Scan Beam");
            beamGo.transform.SetParent(transform, false);
            _beam = OverhaulLights.Spot(beamGo, new Color(0.4f, 0.95f, 1f), 0f, 12f, 70f, true, false);
            _beam.enabled = false;
        }

        void OnScanStart() { _scanning = true; if (FollowPlayer.Instance == null || FollowPlayer.Instance.scanChargePerWave <= 0f) _waveStart = Time.time; }
        void OnScanStop() => _scanning = false;

        void Update()
        {
            if (_mat == null) return;
            var daffodil = FollowPlayer.Instance;
            _strength = Mathf.MoveTowards(_strength, _scanning && daffodil != null ? 1f : 0f, Time.deltaTime * (_scanning ? 6f : 1.5f));
            // Daffodil sends the waves (each one takes its charge); follow her timing so bar and ground agree
            if (daffodil != null && daffodil.scanChargePerWave > 0f) _waveStart = daffodil.LastScanWaveTime;
            else if (_scanning && Time.time - _waveStart >= interval) _waveStart = Time.time;
            float age = Time.time - _waveStart;
            bool on = daffodil != null && _strength > 0.001f;
            if (_volume.enabled != on) _volume.enabled = on;
            HideHudLine();

            if (on)
            {
                Vector3 o = daffodil.transform.position;
                float k = Mathf.Clamp01(age / Mathf.Max(0.1f, waveSeconds));
                float radius = range * (1f - (1f - k) * (1f - k));   // fast out, easing at the edge
                float fade = 1f - Mathf.Clamp01((age - waveSeconds) / 0.8f);
                _mat.SetVector(IdOrigin, new Vector4(o.x, o.y, o.z, radius));
                _mat.SetVector(IdParams, new Vector4(2.2f, range, 1f, _strength * fade));
                _mat.SetColor(IdColor, color);
                _mat.SetFloat(IdTime, Time.time);
            }

            if (_beam != null)
            {
                bool beamOn = daffodil != null && _strength > 0.01f;
                if (_beam.enabled != beamOn) _beam.enabled = beamOn;
                if (beamOn)
                {
                    _beam.transform.position = daffodil.transform.position + Vector3.up * 1.5f;
                    _beam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 9f);
                    OverhaulLights.SetLumens(_beam, 6000f * _strength * pulse);
                }
            }
        }

        /// <summary>The HUD's own scan is a line sweeping down the screen; the ground sweep replaces it.</summary>
        void HideHudLine()
        {
            if (_hudLineHidden) return;
            var hud = GameHUD.Instance;
            if (hud == null) return;
            var line = hud.transform.Find("ScanLayer/ScanLine");
            if (line == null) return;
            var img = line.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.enabled = false;
            _hudLineHidden = true;
        }
    }

    /// <summary>Draws the scan material over the camera colour (additive), after the opaque and transparent passes.</summary>
    [System.Serializable]
    public class ScanSweepPass : CustomPass
    {
        public Material material;

        protected override bool executeInSceneView => false;

        protected override void Execute(CustomPassContext ctx)
        {
            if (material == null) return;
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            CoreUtils.DrawFullScreen(ctx.cmd, material, ctx.propertyBlock, shaderPassId: 0);
        }
    }
}
