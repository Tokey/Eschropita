using Unity.Cinemachine;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Physical feedback for the camera: Cinemachine impulses (shakes that fall off with distance from the player)
    /// and a tiny hit-stop (the world freezes for a few hundredths of a second) on the biggest beats.
    /// Impulses add on top of the IsoFollowCamera's own noise; field of view is never touched.
    ///   CameraJuice.Impulse(worldPos, strength)   strength ~0.2 (tap) … 1.5 (obelisk falls)
    ///   CameraJuice.HitStop(seconds)
    /// </summary>
    [OverhaulModule(50)]
    public class CameraJuice : MonoBehaviour
    {
        public static CameraJuice Instance { get; private set; }

        public float globalStrength = 1f;
        public float falloffDistance = 40f;

        CinemachineImpulseSource _source;
        CinemachineCamera _hookedCamera;
        float _nextCameraCheck;
        float _hitStopUntil = -1f;
        float _savedTimeScale = 1f;

        void Awake()
        {
            Instance = this;
            _source = gameObject.AddComponent<CinemachineImpulseSource>();
            var d = _source.ImpulseDefinition;
            d.ImpulseChannel = 1;
            d.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            d.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            d.ImpulseDuration = 0.25f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_hitStopUntil > 0f) Time.timeScale = _savedTimeScale;
        }

        void Update()
        {
            if (_hookedCamera == null && Time.unscaledTime >= _nextCameraCheck)
            {
                _nextCameraCheck = Time.unscaledTime + 1f;
                var cam = FindAnyObjectByType<CinemachineCamera>();
                if (cam != null)
                {
                    var l = cam.GetComponent<CinemachineImpulseListener>();
                    if (l == null)
                    {
                        l = cam.gameObject.AddComponent<CinemachineImpulseListener>();
                        // AddComponent does not run the editor Reset(): use its defaults explicitly.
                        l.ApplyAfter = CinemachineCore.Stage.Noise;
                        l.ChannelMask = 1;
                        l.Gain = 1f;
                        l.Use2DDistance = false;
                        l.UseCameraSpace = true;
                        l.SignalCombinationMode = CinemachineImpulseListener.SignalCombinationModes.Additive;
                        l.ReactionSettings = new CinemachineImpulseListener.ImpulseReaction { AmplitudeGain = 1f, FrequencyGain = 1f, Duration = 1f };
                    }
                    _hookedCamera = cam;
                }
            }

            if (_hitStopUntil > 0f && Time.unscaledTime >= _hitStopUntil)
            {
                if (Time.timeScale < 0.5f && Time.timeScale > 0f) Time.timeScale = _savedTimeScale;
                _hitStopUntil = -1f;
            }
        }

        /// <summary>Shake the camera from an event at <paramref name="worldPos"/>; weaker the farther it is from the player.</summary>
        public static void Impulse(Vector3 worldPos, float strength)
        {
            var k = Instance;
            if (k == null || k._source == null || strength <= 0f) return;
            var bridge = SignalBridge.Instance;
            Vector3 pp = bridge != null && bridge.Player != null ? bridge.Player.transform.position : worldPos;
            float d = Vector3.Distance(pp, worldPos);
            float s = strength * k.globalStrength * Mathf.Clamp01(1f - d / k.falloffDistance);
            if (s < 0.02f) return;
            Vector3 v = Random.insideUnitSphere;
            v.y = Mathf.Abs(v.y) + 0.5f;
            k._source.GenerateImpulseWithVelocity(v.normalized * s * 0.35f);
        }

        /// <summary>Freezes the world for a moment (unscaled). Ignored while the game is paused.</summary>
        public static void HitStop(float seconds = 0.05f)
        {
            var k = Instance;
            if (k == null || Time.timeScale <= 0f || k._hitStopUntil > 0f) return;
            k._savedTimeScale = Time.timeScale;
            Time.timeScale = 0.05f;
            k._hitStopUntil = Time.unscaledTime + seconds;
        }

        /// <summary>Squash-and-stretch on a transform (a Mycari answering, a pad being pressed).</summary>
        public static void Punch(Transform t, float amount = 0.18f, float seconds = 0.35f)
        {
            if (t == null) return;
            var p = t.GetComponent<ScalePunch>();
            if (p == null) p = t.gameObject.AddComponent<ScalePunch>();
            p.Play(amount, seconds);
        }
    }
}
