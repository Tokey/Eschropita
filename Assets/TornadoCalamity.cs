using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
[DisallowMultipleComponent]
public class TornadoCalamity : MonoBehaviour
{
    [Header("Anchor (stay around here)")]
    public Transform anchor;             // set this in Inspector (e.g., a dummy at map center)
    public Vector3 fallbackAnchor = Vector3.zero; // used if anchor is null
    [Min(0f)] public float orbitMinRadius = 8f;   // min distance from anchor to wander
    [Min(0.01f)] public float orbitMaxRadius = 35f; // max distance from anchor to wander
    [Min(0.01f)] public float leashRadius = 40f;  // hard cap from anchor

    [Header("Tornado Lifecycle")]
    public ParticleSystem tornadoEffect; // assign your PE here
    [Min(0.01f)] public float maxScale = 20f;
    [Min(0.01f)] public float growTime = 5f;
    [Min(0.01f)] public float lifeTime = 10f;
    [Min(0.01f)] public float shrinkTime = 5f;

    [Header("Movement")]
    [Min(0.01f)] public float moveSpeed = 5f;
    [Min(0.1f)] public float retargetEvery = 2.5f; // seconds before picking a new orbit point

    [Header("Damage/Slow")]
    [Range(0.05f, 1f)] public float playerSlowMultiplier = 0.4f;
    [Min(0f)] public float siteDamagePerSecond = 5f;
    [Tooltip("Damage radius scales with size via: radius = scale * damageRadiusFactor")]
    [Min(0.01f)] public float damageRadiusFactor = 0.5f;

    [Header("Audio")]
    public AudioSource tornadoAudioSource;  // Assign in Inspector or will be created
    public AudioClip tornadoSound;          // Looping tornado sound
    [Range(0f, 1f)] public float maxVolume = 1f;

    [Header("Respawn")]
    [Min(0f)] public float minRespawnDelay = 15f;
    [Min(0f)] public float maxRespawnDelay = 30f;

    SphereCollider _col;
    Rigidbody _rb;
    bool _active;
    enum State { Idle, Growing, Peak, Shrinking }
    State _state;
    float _stateT;
    float _nextRetargetTime;
    Vector3 _wanderTarget;
    PlayerController _slowedPlayer; // track current slowed player to restore speed on disable

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
        CleanupPlayerSlow();
        if (tornadoEffect) tornadoEffect.gameObject.SetActive(false);
        _active = false;
    }

    IEnumerator Loop()
    {
        while (true)
        {
            float delay = Random.Range(minRespawnDelay, Mathf.Max(minRespawnDelay, maxRespawnDelay));
            if (delay > 0f) yield return new WaitForSeconds(delay);

            Activate();
            yield return RunLifecycle();
            Deactivate();
        }
    }

    void Activate()
    {
        _active = true;
        _state = State.Growing;
        _stateT = 0f;
        _nextRetargetTime = 0f;
        if (tornadoEffect) tornadoEffect.gameObject.SetActive(true);

        // start near a valid orbit position
        transform.position = GetClampedAnchor() + RandomOnRing(orbitMinRadius, orbitMaxRadius);
        ApplyScale(0f);
        PickNewWanderTarget(true);
        
        // Start playing tornado sound
        StartTornadoSound();
    }

    void Deactivate()
    {
        _active = false;
        CleanupPlayerSlow();
        if (tornadoEffect) tornadoEffect.gameObject.SetActive(false);
        ApplyScale(0f);
        
        // Stop tornado sound
        StopTornadoSound();
    }

    IEnumerator RunLifecycle()
    {
        // Grow
        while (_state == State.Growing)
        {
            _stateT += Time.deltaTime;
            float t = Mathf.Clamp01(_stateT / growTime);
            ApplyScale(Mathf.Lerp(0f, maxScale, t));
            MoveWander();
            if (t >= 1f) { _state = State.Peak; _stateT = 0f; }
            yield return null;
        }

        // Peak
        while (_state == State.Peak)
        {
            _stateT += Time.deltaTime;
            ApplyScale(maxScale);
            MoveWander();
            if (_stateT >= lifeTime) { _state = State.Shrinking; _stateT = 0f; }
            yield return null;
        }

        // Shrink
        while (_state == State.Shrinking)
        {
            _stateT += Time.deltaTime;
            float t = Mathf.Clamp01(_stateT / shrinkTime);
            ApplyScale(Mathf.Lerp(maxScale, 0f, t));
            MoveWander();
            if (t >= 1f) break;
            yield return null;
        }
    }

    void MoveWander()
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
        transform.position = Vector3.MoveTowards(p, target, moveSpeed * Time.deltaTime);

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

    void ApplyScale(float s)
    {
        // damage/trigger radius
        _col.radius = Mathf.Max(0.1f, s * damageRadiusFactor);

        // particle visuals
        if (tornadoEffect)
        {
            if (!tornadoEffect.isPlaying) tornadoEffect.Play(true);
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
            // Volume scales from 0 to maxVolume based on current scale relative to maxScale
            float volumeRatio = Mathf.Clamp01(currentScale / maxScale);
            tornadoAudioSource.volume = volumeRatio * maxVolume;
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (!_active) return;

        if (other.CompareTag("Player"))
        {
            var pc = other.GetComponent<PlayerController>();
            if (pc)
            {
                _slowedPlayer = pc;
                pc.SetSpeedMultiplier(playerSlowMultiplier);
            }
        }

        var site = other.GetComponent<TaskSite>();
        if (site) site.AddHealth(-siteDamagePerSecond * Time.deltaTime);
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            var pc = other.GetComponent<PlayerController>();
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
