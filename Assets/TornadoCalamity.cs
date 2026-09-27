using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
[DisallowMultipleComponent]
public class TornadoCalamity : MonoBehaviour
{
    [Header("Control")]
    [Tooltip("While suppressed the loop idles and never spawns a random tornado; an active one shrinks away at once. " +
             "An explicit StrikeAt() is still honoured.")]
    public bool suppressed = false;
    [Tooltip("Scales size, damage and damage radius up, and respawn delays down.")]
    [Range(0.25f, 3f)] public float intensity = 1f;

    [Header("Anchor (stay around here)")]
    public Transform anchor;             // set this in Inspector (e.g., a dummy at map center)
    public Vector3 fallbackAnchor = Vector3.zero; // used if anchor is null
    [Min(0f)] public float orbitMinRadius = 8f;   // min distance from anchor to wander
    [Min(0.01f)] public float orbitMaxRadius = 35f; // max distance from anchor to wander
    [Min(0.01f)] public float leashRadius = 40f;  // hard cap from anchor

    [Header("Tornado Lifecycle")]
    public ParticleSystem tornadoEffect; // assign your PE here
    [Tooltip("Snap the effect child onto this transform on spawn (the scene's effect is offset from the trigger).")]
    public bool centerEffectOnRoot = true;
    [Min(0.01f)] public float maxScale = 20f;
    [Min(0.01f)] public float growTime = 5f;
    [Min(0.01f)] public float lifeTime = 10f;
    [Min(0.01f)] public float shrinkTime = 5f;

    [Header("Movement")]
    [Min(0.01f)] public float moveSpeed = 5f;
    [Min(0.1f)] public float retargetEvery = 2.5f; // seconds before picking a new orbit point

    [Header("Strike (scripted)")]
    [Tooltip("A StrikeAt() tornado spawns this far from its target and bears down on it.")]
    [Min(0f)] public float strikeSpawnDistance = 20f;

    [Header("Damage/Slow")]
    [Range(0.05f, 1f)] public float playerSlowMultiplier = 0.4f;
    [Min(0f)] public float siteDamagePerSecond = 5f;
    [Tooltip("Damage radius scales with size via: radius = scale * damageRadiusFactor")]
    [Min(0.01f)] public float damageRadiusFactor = 0.5f;

    [Header("Damage radius")]
    [Tooltip("Floor for the trigger radius while active, so a visually small tornado (maxScale 0.2) still reaches a site.")]
    [Min(0f)] public float minDamageRadius = 3.5f;

    [Header("Audio")]
    public AudioSource tornadoAudioSource;  // Assign in Inspector or will be created
    public AudioClip tornadoSound;          // Looping tornado sound
    [Range(0f, 1f)] public float maxVolume = 1f;

    [Header("Respawn")]
    [Min(0f)] public float minRespawnDelay = 15f;
    [Min(0f)] public float maxRespawnDelay = 30f;

    /// <summary>A tornado is currently up (growing, at peak or shrinking).</summary>
    public bool IsActive => _active;
    public Vector3 Position => transform.position;
    public event System.Action OnSpawned, OnEnded;

    const float IdlePollSeconds = 0.25f;          // idle wait granularity (strike / suppression latency)
    const float StrikeArriveDistance = 1.5f;      // "on target" threshold for a strike
    const float StrikeOrbitRadius = 1.5f;         // weave radius while holding over the target
    const float StrikeOrbitDegreesPerSecond = 70f;

    SphereCollider _col;
    Rigidbody _rb;
    bool _active;
    enum State { Idle, Growing, Peak, Shrinking }
    State _state;
    float _stateT;
    float _scale;             // last applied scale (so an interrupted tornado shrinks from where it is)
    float _shrinkFromScale;
    float _nextRetargetTime;
    Vector3 _wanderTarget;
    PlayerController _slowedPlayer; // track current slowed player to restore speed on disable

    // Strike request (written by StrikeAt, consumed by the loop) and the live strike state.
    bool _strikeRequested;
    Vector3 _requestedStrikePos;
    float _requestedStrikeHold;
    bool _striking;
    Vector3 _strikeTarget;
    float _strikeHoldSeconds;
    bool _strikeArrived;
    float _strikeHoldT;
    float _strikeOrbitAngle;

    // Sites already damaged this physics step (a site with several colliders must not be hit per collider).
    readonly HashSet<TaskSite> _damagedThisStep = new HashSet<TaskSite>();

    float ScaledMaxScale => maxScale * intensity;

    void Reset()
    {
        var sc = GetComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = 0.5f;
    }

    void Awake()
    {
        _col = GetComponent<SphereCollider>();
        _col.isTrigger = true;

        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        if (tornadoEffect == null)
            tornadoEffect = GetComponentInChildren<ParticleSystem>(true);

        if (tornadoEffect)
        {
            var main = tornadoEffect.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy; // transform scale drives visuals
            tornadoEffect.gameObject.SetActive(false);
            tornadoEffect.transform.localScale = Vector3.zero;
        }

        // Setup audio source
        SetupAudioSource();
    }

    void SetupAudioSource()
    {
        // Create audio source if not assigned
        if (tornadoAudioSource == null)
        {
            tornadoAudioSource = GetComponent<AudioSource>();
            if (tornadoAudioSource == null)
            {
                tornadoAudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        // Configure for looping tornado sound
        tornadoAudioSource.loop = true;
        tornadoAudioSource.playOnAwake = false;
        tornadoAudioSource.spatialBlend = 1f; // 3D sound
        tornadoAudioSource.minDistance = 5f;
        tornadoAudioSource.maxDistance = 50f;
        tornadoAudioSource.rolloffMode = AudioRolloffMode.Linear;
        tornadoAudioSource.volume = 0f;

        if (tornadoSound != null)
        {
            tornadoAudioSource.clip = tornadoSound;
        }
    }

    void OnEnable()
    {
        StopAllCoroutines();
        StartCoroutine(Loop());
    }

    void OnDisable()
    {
        Deactivate();
    }

    void FixedUpdate()
    {
        // Runs before this step's trigger callbacks, so the set is fresh for each physics step.
        if (_damagedThisStep.Count > 0) _damagedThisStep.Clear();
    }

    // === Public control ===

    public void SetSuppressed(bool v)
    {
        suppressed = v;
        // An active tornado winds down right away; the loop then idles until unsuppressed.
        if (v && _active && _state != State.Shrinking)
            BeginShrink();
    }

    public void SetIntensity(float v)
    {
        intensity = Mathf.Clamp(v, 0.25f, 3f);
    }

    /// <summary>
    /// Scripted strike: spawns at once (interrupting the idle wait, or hijacking a tornado that is already up),
    /// heads straight for worldPos ignoring the anchor leash, weaves over it for holdSeconds, then shrinks and
    /// the normal loop resumes. Only XZ of worldPos is used; the tornado keeps the anchor's height like a normal
    /// spawn. Honoured even while suppressed, since it is an explicit beat.
    /// </summary>
    public void StrikeAt(Vector3 worldPos, float holdSeconds)
    {
        _requestedStrikePos = worldPos;
        _requestedStrikeHold = Mathf.Max(0f, holdSeconds);
        _strikeRequested = true;
    }

    /// <summary>Checkpoint restore: drop whatever is happening now and start the loop over.</summary>
    public void ForceStop()
    {
        StopAllCoroutines();
        _strikeRequested = false;
        Deactivate();
        if (isActiveAndEnabled)
            StartCoroutine(Loop());
    }

    // === Loop ===

    IEnumerator Loop()
    {
        var poll = new WaitForSeconds(IdlePollSeconds);
        while (true)
        {
            // Idle. Waited out in short polls so a strike request or a suppression change is picked up
            // within a quarter second; the countdown pauses while suppressed and resumes afterwards.
            float delay = Random.Range(minRespawnDelay, Mathf.Max(minRespawnDelay, maxRespawnDelay)) / Mathf.Max(0.25f, intensity);
            float waited = 0f;
            float last = Time.time;
            while (!_strikeRequested && (suppressed || waited < delay))
            {
                yield return poll;
                float now = Time.time;
                if (!suppressed) waited += now - last;
                last = now;
            }

            if (_strikeRequested) ConsumeStrikeRequest();
            else _striking = false;

            Activate();
            yield return RunLifecycle();
            Deactivate();
        }
    }

    void ConsumeStrikeRequest()
    {
        _strikeRequested = false;
        _striking = true;
        _strikeTarget = _requestedStrikePos;
        _strikeHoldSeconds = _requestedStrikeHold;
        _strikeArrived = false;
        _strikeHoldT = 0f;
    }

    void Activate()
    {
        _active = true;
        _state = State.Growing;
        _stateT = 0f;
        _nextRetargetTime = 0f;
        _scale = 0f;

        if (tornadoEffect)
        {
            if (centerEffectOnRoot) tornadoEffect.transform.localPosition = Vector3.zero;
            tornadoEffect.gameObject.SetActive(true);
        }

        if (_striking)
        {
            // Spawn a little way out from the target so the funnel visibly bears down on it.
            Vector3 a = GetClampedAnchor();
            transform.position = new Vector3(_strikeTarget.x, a.y, _strikeTarget.z) + RandomOnRing(strikeSpawnDistance, strikeSpawnDistance);
        }
        else
        {
            // start near a valid orbit position
            transform.position = GetClampedAnchor() + RandomOnRing(orbitMinRadius, orbitMaxRadius);
            PickNewWanderTarget(true);
        }

        ApplyScale(0f);

        // Start playing tornado sound
        StartTornadoSound();

        OnSpawned?.Invoke();
    }

    void Deactivate()
    {
        bool wasActive = _active;
        _active = false;
        _state = State.Idle;
        _striking = false;
        CleanupPlayerSlow();
        if (tornadoEffect) tornadoEffect.gameObject.SetActive(false);
        ApplyScale(0f);

        // Stop tornado sound
        StopTornadoSound();

        if (wasActive) OnEnded?.Invoke();
    }

    IEnumerator RunLifecycle()
    {
        while (_active && _state != State.Idle)
        {
            float dt = Time.deltaTime;
            float peak = ScaledMaxScale;

            // A strike requested while a tornado is already up hijacks it instead of waiting for the next spawn.
            if (_strikeRequested)
            {
                ConsumeStrikeRequest();
                if (_state == State.Shrinking)
                {
                    // Regrow from the current size rather than popping back to full.
                    _state = State.Growing;
                    _stateT = growTime * Mathf.Clamp01(_scale / Mathf.Max(0.001f, peak));
                }
            }

            switch (_state)
            {
                case State.Growing:
                {
                    _stateT += dt;
                    float t = Mathf.Clamp01(_stateT / growTime);
                    ApplyScale(Mathf.Lerp(0f, peak, t));
                    if (t >= 1f) { _state = State.Peak; _stateT = 0f; }
                    break;
                }
                case State.Peak:
                {
                    _stateT += dt;
                    ApplyScale(peak);
                    // A strike stays up until it has sat over its target at full size for holdSeconds;
                    // a normal tornado lives for lifeTime.
                    bool done;
                    if (_striking)
                    {
                        if (_strikeArrived) _strikeHoldT += dt;
                        done = _strikeArrived && _strikeHoldT >= _strikeHoldSeconds;
                    }
                    else done = _stateT >= lifeTime;
                    if (done) BeginShrink();
                    break;
                }
                case State.Shrinking:
                {
                    _stateT += dt;
                    float t = Mathf.Clamp01(_stateT / shrinkTime);
                    ApplyScale(Mathf.Lerp(_shrinkFromScale, 0f, t));
                    if (t >= 1f) yield break;
                    break;
                }
            }

            if (_striking) MoveToStrike(dt);
            else MoveWander(dt);

            yield return null;
        }
    }

    void BeginShrink()
    {
        _state = State.Shrinking;
        _stateT = 0f;
        _shrinkFromScale = _scale;
    }

    // === Movement ===

    void MoveWander(float dt)
    {
        // leash: if too far from anchor, re-target toward a point on the ring closer to anchor
        Vector3 anchorPos = GetClampedAnchor();
        float distFromAnchor = Vector3.Distance(transform.position, anchorPos);
        if (distFromAnchor > leashRadius * 0.98f)
        {
            _wanderTarget = anchorPos + (transform.position - anchorPos).normalized * Mathf.Clamp(leashRadius * 0.8f, orbitMinRadius, orbitMaxRadius);
            _nextRetargetTime = Time.time + retargetEvery * 0.5f;
        }

        // time-based retarget
        if (Time.time >= _nextRetargetTime || Vector3.Distance(transform.position, _wanderTarget) < 1.25f)
            PickNewWanderTarget(false);

        // move on XZ only
        Vector3 p = transform.position;
        Vector3 target = new Vector3(_wanderTarget.x, p.y, _wanderTarget.z);
        transform.position = Vector3.MoveTowards(p, target, moveSpeed * dt);

        // final leash clamp
        Vector3 flatFromAnchor = transform.position - anchorPos;
        flatFromAnchor.y = 0f;
        float flatDist = flatFromAnchor.magnitude;
        if (flatDist > leashRadius)
        {
            flatFromAnchor = flatFromAnchor.normalized * leashRadius;
            transform.position = new Vector3(anchorPos.x, p.y, anchorPos.z) + flatFromAnchor;
        }
    }

    void MoveToStrike(float dt)
    {
        // No leash here: the strike goes wherever it was told to.
        Vector3 p = transform.position;
        Vector3 target = new Vector3(_strikeTarget.x, p.y, _strikeTarget.z);

        if (!_strikeArrived)
        {
            transform.position = Vector3.MoveTowards(p, target, moveSpeed * dt);
            if (Vector3.Distance(transform.position, target) <= StrikeArriveDistance)
            {
                _strikeArrived = true;
                _strikeHoldT = 0f;
                _strikeOrbitAngle = Random.Range(0f, 360f);
            }
            return;
        }

        // Holding: weave a small, slightly irregular orbit around the target so the funnel keeps crossing the site.
        // (The hold timer itself is advanced by the Peak state, so only time at full size counts.)
        _strikeOrbitAngle += StrikeOrbitDegreesPerSecond * dt;
        float wobble = Mathf.PerlinNoise(Time.time * 0.6f, 0.37f) * 2f - 1f; // -1..1, slow
        float radius = StrikeOrbitRadius * (1f + 0.35f * wobble);
        float rad = _strikeOrbitAngle * Mathf.Deg2Rad;
        Vector3 desired = target + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
        transform.position = Vector3.MoveTowards(p, desired, moveSpeed * dt);
    }

    void PickNewWanderTarget(bool first)
    {
        Vector3 a = GetClampedAnchor();
        // random point on annulus [orbitMinRadius, orbitMaxRadius]
        _wanderTarget = a + RandomOnRing(orbitMinRadius, orbitMaxRadius);
        _wanderTarget.y = transform.position.y;

        float jitter = first ? 0.6f : 1f;
        _nextRetargetTime = Time.time + retargetEvery * Random.Range(0.7f, 1.3f) * jitter;
    }

    Vector3 GetClampedAnchor() => anchor ? anchor.position : fallbackAnchor;

    static Vector3 RandomOnRing(float minR, float maxR)
    {
        float r = Random.Range(minR, maxR);
        float theta = Random.Range(0f, Mathf.PI * 2f);
        return new Vector3(Mathf.Cos(theta) * r, 0f, Mathf.Sin(theta) * r);
    }

    // === Scale / audio ===

    void ApplyScale(float s)
    {
        _scale = s;

        // damage/trigger radius: never below the floor, so a tiny visual still reaches the site it sits on
        if (_col != null)
            _col.radius = Mathf.Max(0.1f, Mathf.Max(minDamageRadius, s * damageRadiusFactor) * intensity);

        // particle visuals
        if (tornadoEffect)
        {
            if (tornadoEffect.gameObject.activeInHierarchy && !tornadoEffect.isPlaying) tornadoEffect.Play(true);
            tornadoEffect.transform.localScale = Vector3.one * s;

            var shape = tornadoEffect.shape;
            if (shape.enabled) shape.radius = s * damageRadiusFactor;
        }

        // Update tornado sound volume based on scale
        UpdateTornadoVolume(s);
    }

    void StartTornadoSound()
    {
        if (tornadoAudioSource != null && tornadoSound != null)
        {
            tornadoAudioSource.clip = tornadoSound;
            tornadoAudioSource.volume = 0f;
            tornadoAudioSource.Play();
        }
    }

    void StopTornadoSound()
    {
        if (tornadoAudioSource != null)
        {
            tornadoAudioSource.Stop();
            tornadoAudioSource.volume = 0f;
        }
    }

    void UpdateTornadoVolume(float currentScale)
    {
        if (tornadoAudioSource != null && tornadoAudioSource.isPlaying)
        {
            // Volume scales from 0 to maxVolume based on current scale relative to the (intensity-scaled) peak
            float volumeRatio = Mathf.Clamp01(currentScale / Mathf.Max(0.001f, ScaledMaxScale));
            tornadoAudioSource.volume = volumeRatio * maxVolume;
        }
    }

    // === Damage / slow ===

    void OnTriggerStay(Collider other)
    {
        if (!_active) return;

        if (other.CompareTag("Player"))
        {
            var pc = other.GetComponentInParent<PlayerController>();
            if (pc)
            {
                _slowedPlayer = pc;
                pc.SetSpeedMultiplier(playerSlowMultiplier);
            }
        }

        // Site colliders may sit on children of the TaskSite; damage the site once per physics step.
        var site = other.GetComponentInParent<TaskSite>();
        if (site && site.tornadoCanDamage && _damagedThisStep.Add(site))
            site.ApplyDamage(siteDamagePerSecond * intensity * Time.deltaTime);
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            var pc = other.GetComponentInParent<PlayerController>();
            if (pc) pc.SetSpeedMultiplier(1f);
            if (_slowedPlayer == pc) _slowedPlayer = null;
        }
    }

    void OnDestroy() => CleanupPlayerSlow();

    void CleanupPlayerSlow()
    {
        if (_slowedPlayer) _slowedPlayer.SetSpeedMultiplier(1f);
        _slowedPlayer = null;
    }
}
