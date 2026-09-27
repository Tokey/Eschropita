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

    [Header("Scan Zoom")]
    [Tooltip("Field of view while Daffodil is scanning (perspective), so holding V pulls the view back. " +
             "The spring settles on exactly this whatever the speed zoom is doing.")]
    [Range(1f, 179f)] public float scanFov = 80f;

    [Tooltip("Orthographic lenses only: added to the size (x0.35) while scanning.")]
    public float scanFovBoost = 9f;

    [Tooltip("Spring stiffness for the scan zoom. Higher snaps harder.")]
    [Min(1f)] public float scanZoomStiffness = 42f;

    [Tooltip("Spring damping. Below 1 overshoots slightly and settles, which is what gives the " +
             "zoom its tactile feel; 1 is a dead-stop with no overshoot.")]
    [Range(0.2f, 1.5f)] public float scanZoomDamping = 0.62f;

    [Header("Handheld Noise")]
    [Tooltip("Handheld_normal_mild is the one tuned for this: slow drift, medium sway AND fast " +
             "jitter. The _extreme preset is 15 degrees of very slow lurch with almost no fast " +
             "content, so scaling it down to a sane size leaves nothing you can actually see.")]
    public NoiseSettings noiseProfile;     // <-- assign an asset here
    public float noiseAmpIdle = 0.9f;
    public float noiseAmpMove = 1.3f;
    public float noiseFreqIdle = 1f;
    public float noiseFreqMove = 1.35f;

    [Header("Speed Smoothing")]
    public float speedSmoothing = 0.16f;

    // internals
    CinemachineCamera _cm;
    CinemachinePositionComposer _composer;
    CinemachineBasicMultiChannelPerlin _perlin;
    Vector3 _lastPos;
    float _speedSmoothed;
    bool _warnedNoProfile;

    // Scan zoom spring: current offset and its velocity, integrated each frame.
    float _scanFov, _scanFovVel, _lastScanFov;

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

    /// <summary>
    /// Retargets the rig at runtime (used by the story flow to linger on a location).
    /// Setting <see cref="target"/> alone is not enough because Cinemachine reads its own
    /// tracking target, which Start() only copies once.
    /// </summary>
    public void SetTarget(Transform t)
    {
        target = t;
        if (!_cm) _cm = GetComponent<CinemachineCamera>();
        if (_cm) _cm.Target.TrackingTarget = t;
        if (t) _lastPos = t.position;
        _speedSmoothed = 0f;
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

        var lens = _cm.Lens;

        // Speed zoom, eased. The scan offset is added after it so the spring below is not smoothed
        // away by the lerp that the speed zoom needs.
        float speedZoomed = 0f;
        if (!lens.Orthographic)
            speedZoomed = Mathf.Lerp(lens.FieldOfView - _lastScanFov, baseFOV + zoomOutAmount * t,
                                     1f - Mathf.Exp(-zoomLerp * dt));

        // Scan pull-back, integrated as a damped spring rather than an exponential lerp. A lerp
        // eases in and dies at the target; a spring under-damped a little overshoots and settles,
        // which is what makes the zoom feel like it has weight instead of sliding. In perspective the
        // target is whatever offset lands the lens on scanFov, so running while scanning does not
        // stack the two zooms.
        var daffodil = FollowPlayer.Instance;
        bool scanning = daffodil != null && daffodil.IsScanning;
        float scanTarget = !scanning ? 0f
                         : lens.Orthographic ? scanFovBoost
                         : Mathf.Max(0f, scanFov - speedZoomed);
        float k = scanZoomStiffness;
        float c = 2f * scanZoomDamping * Mathf.Sqrt(Mathf.Max(k, 0.0001f));
        _scanFovVel += ((scanTarget - _scanFov) * k - _scanFovVel * c) * dt;
        _scanFov += _scanFovVel * dt;

        // zoom
        if (lens.Orthographic)
        {
            float targetSize = baseOrthoSize + zoomOutAmount * t + _scanFov * 0.35f;
            lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, targetSize, 1f - Mathf.Exp(-zoomLerp * dt));
        }
        else
        {
            lens.FieldOfView = speedZoomed + _scanFov;
            _lastScanFov = _scanFov;
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
