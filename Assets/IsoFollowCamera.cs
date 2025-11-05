using UnityEngine;
using Unity.Cinemachine;

[ExecuteAlways]
[RequireComponent(typeof(CinemachineCamera))]
public class IsoFollowCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public bool autoFindPlayerByTag = true;

    [Header("Isometric")]
    [Range(0, 360)] public float yaw = 45f;
    [Range(0, 89)] public float pitch = 35f;
    public Vector3 targetOffset = new(0f, 1f, 0f);
    public float baseDistance = 12f;

    [Header("Damping (PositionComposer)")]
    public Vector3 damping = new(0.2f, 0.2f, 0.2f);

    [Header("Zoom vs Speed")]
    public float baseFOV = 50f;          // perspective
    public float baseOrthoSize = 7.5f;   // orthographic
    public float zoomOutAmount = 8f;     // +FOV / +Size at max speed
    public float speedForMaxZoom = 5f;   // world units / sec
    public float zoomLerp = 3f;

    [Header("Distance Ease (optional)")]
    public bool easeDistanceOnMove = true;
    public float distanceZoomOut = 2f;
    public float distanceLerp = 3f;

    [Header("Handheld Noise")]
    public NoiseSettings noiseProfile;     // <-- assign an asset here
    public float noiseAmpIdle = 0.25f;
    public float noiseAmpMove = 0.5f;
    public float noiseFreqIdle = 0.7f;
    public float noiseFreqMove = 1.2f;

    [Header("Speed Smoothing")]
    public float speedSmoothing = 0.16f;

    // internals
    CinemachineCamera _cm;
    CinemachinePositionComposer _composer;
    CinemachineBasicMultiChannelPerlin _perlin;
    Vector3 _lastPos;
    float _speedSmoothed;
    bool _warnedNoProfile;

    void OnEnable()
    {
        _cm = GetComponent<CinemachineCamera>();

        _composer = GetComponent<CinemachinePositionComposer>();
        if (!_composer) _composer = gameObject.AddComponent<CinemachinePositionComposer>();

        _perlin = GetComponent<CinemachineBasicMultiChannelPerlin>();
        if (!_perlin) _perlin = gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();

        // assign profile if provided
        if (noiseProfile) _perlin.NoiseProfile = noiseProfile;

        ApplyStatic();
    }

    void Start()
    {
        if (!target && autoFindPlayerByTag)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go) target = go.transform;
        }
        _cm.Target.TrackingTarget = target;
        if (target) _lastPos = target.position;

        ApplyStatic();
    }

    void ApplyStatic()
    {
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        var lens = _cm.Lens;
        if (lens.Orthographic) lens.OrthographicSize = baseOrthoSize;
        else lens.FieldOfView = baseFOV;
        _cm.Lens = lens;

        _composer.TargetOffset = targetOffset;
        _composer.CameraDistance = Mathf.Max(0.01f, baseDistance);
        _composer.Damping = damping;
    }

    void Update()
    {
        // keep edits live in editor
        if (!Application.isPlaying)
        {
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            if (_composer)
            {
                _composer.TargetOffset = targetOffset;
                _composer.CameraDistance = Mathf.Max(0.01f, baseDistance);
                _composer.Damping = damping;
            }
            return;
        }

        if (!_cm || !_composer || !target) return;

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // speed estimate + smoothing
        float instSpeed = (target.position - _lastPos).magnitude / dt;
        _lastPos = target.position;
        _speedSmoothed = Mathf.Lerp(_speedSmoothed, instSpeed, 1f - Mathf.Exp(-6f * dt)); // τ≈0.16s
        float t = Mathf.Clamp01(_speedSmoothed / Mathf.Max(0.01f, speedForMaxZoom));

        // zoom
        var lens = _cm.Lens;
        if (lens.Orthographic)
        {
            lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, baseOrthoSize + zoomOutAmount * t, 1f - Mathf.Exp(-zoomLerp * dt));
        }
        else
        {
            lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, baseFOV + zoomOutAmount * t, 1f - Mathf.Exp(-zoomLerp * dt));
        }
        _cm.Lens = lens;

        // distance ease
        if (easeDistanceOnMove)
        {
            float targetDist = Mathf.Max(0.01f, baseDistance + distanceZoomOut * t);
            _composer.CameraDistance = Mathf.Lerp(_composer.CameraDistance, targetDist, 1f - Mathf.Exp(-distanceLerp * dt));
        }

        // perlin gains (requires a NoiseSettings)
        if (_perlin)
        {
            if (_perlin.NoiseProfile == null)
            {
                if (!_warnedNoProfile)
                {
                    _warnedNoProfile = true;
                    Debug.LogWarning("[IsoFollowCamera] Assign a NoiseSettings asset to enable handheld noise (Right-click → Create → Cinemachine → Noise Settings).");
                }
            }
            else
            {
                _perlin.AmplitudeGain = Mathf.Lerp(noiseAmpIdle, noiseAmpMove, t);
                _perlin.FrequencyGain = Mathf.Lerp(noiseFreqIdle, noiseFreqMove, t);
            }
        }
    }
}
