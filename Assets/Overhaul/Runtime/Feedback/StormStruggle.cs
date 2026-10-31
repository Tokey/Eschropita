using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// What it looks like when the tornado's wind has hold of Eschropita (TornadoCalamity.PlayerStruggle): she leans
    /// into the wind and shudders, dust tears off her feet sideways, and the camera trembles. The slow and the shove
    /// themselves live in TornadoCalamity. The lean is applied just before rendering, after the sprite billboard has
    /// set its rotation for the frame.
    /// </summary>
    [OverhaulModule(42)]
    public class StormStruggle : MonoBehaviour
    {
        public float maxLeanDegrees = 16f;
        public float shudderDegrees = 4f;
        public float cameraShakeDegrees = 0.9f;

        float _struggle, _windSide, _nextDust;
        bool _told, _hooked;

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            if (!_hooked) { CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated); _hooked = true; }
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (_hooked) { CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated); _hooked = false; }
        }

        void Update()
        {
            var bridge = SignalBridge.Instance;
            var tornado = bridge != null ? bridge.Tornado : null;
            var player = bridge != null ? bridge.Player : null;
            float want = tornado != null && tornado.IsActive ? tornado.PlayerStruggle : 0f;
            _struggle = Mathf.MoveTowards(_struggle, want, Time.deltaTime * 3f);
            if (player == null || _struggle <= 0.01f) return;

            // which way the wind shoves her on screen (it circles the funnel)
            Vector3 dir = player.transform.position - tornado.Position;
            dir.y = 0f;
            Vector3 push = dir.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, dir.normalized) : Vector3.zero;
            var cam = Camera.main;
            float side = cam != null ? Vector3.Dot(push, cam.transform.right) : 0f;
            _windSide = Mathf.Lerp(_windSide, Mathf.Sign(side) * Mathf.Clamp01(Mathf.Abs(side) * 2f), 1f - Mathf.Exp(-Time.deltaTime * 4f));

            if (Time.time >= _nextDust)
            {
                _nextDust = Time.time + Mathf.Lerp(0.2f, 0.07f, _struggle);
                Vector3 feet = player.transform.position + Vector3.up * 0.1f;
                VfxKit.Dust(feet + push * 0.3f, 2 + Mathf.RoundToInt(_struggle * 4f), new Color(0.55f, 0.36f, 0.25f, 0.75f),
                            0.5f + 0.4f * _struggle, 1.5f + 2.5f * _struggle);
            }

            if (!_told && _struggle > 0.45f)
            {
                _told = true;
                var hud = GameHUD.Instance;
                if (hud != null) hud.Toast("Daffodil: Wind shear. Walk out of it; across the wind is easier than against.", 3.5f);
            }
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_struggle <= 0.01f || cam == null || cam.cameraType != CameraType.Game) return;
            var bridge = SignalBridge.Instance;
            var player = bridge != null ? bridge.Player : null;
            if (player == null) return;
            float t = Time.time;
            float shudder = (Mathf.Sin(t * 31f) * 0.6f + Mathf.Sin(t * 47f + 1.3f) * 0.4f) * shudderDegrees;
            // lean into the wind: the top of the sprite tips against the shove
            float angle = (-_windSide * maxLeanDegrees + shudder) * _struggle;
            player.transform.rotation = Quaternion.AngleAxis(angle, cam.transform.forward) * player.transform.rotation;
        }

        void OnCameraUpdated(CinemachineBrain brain)
        {
            if (_struggle <= 0.01f || brain == null) return;
            var cam = brain.OutputCamera;
            if (cam == null) return;
            float t = Time.time;
            float roll = cameraShakeDegrees * _struggle * (Mathf.Sin(t * 23f) * 0.6f + Mathf.Sin(t * 37f + 0.4f) * 0.4f);
            Vector3 jolt = new Vector3(Mathf.Sin(t * 19f), Mathf.Sin(t * 27f + 1f), 0f) * (0.05f * _struggle);
            cam.transform.position += cam.transform.rotation * jolt;
            cam.transform.rotation = cam.transform.rotation * Quaternion.AngleAxis(roll, Vector3.forward);
        }
    }
}
