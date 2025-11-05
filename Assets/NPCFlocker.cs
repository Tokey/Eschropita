using UnityEngine;
using UnityEngine.AI;

public enum NPCState
{
    Roam,
    FollowToDecision,
    AwaitDecision,
    FollowApproved,
    FollowToTask,
    Work,
    DeclineCooldown,
    ReturnToRoam
}

public enum NPCRole
{
    Worker,     // repairs sites
    Attacker,   // damages sites
    Wanderer    // just walks around for a fixed time, then leaves
}

[RequireComponent(typeof(NavMeshAgent))]
public class NPCFlocker : MonoBehaviour
{

    [Header("Role Settings")]
    public NPCRole role = NPCRole.Worker;
    [Tooltip("Seconds to wander at a site before leaving when role = Wanderer.")]
    public float wanderDuration = 15f;


    [Header("Speeds")]
    public float roamSpeed = 2.0f;
    public float followSpeed = 3.5f;

    [Header("Roaming")]
    public float waypointReachDist = 1.0f;
    public float retargetRoamEvery = 3.0f;

    [Header("Flocking")]
    public float neighborRadius = 6.0f;
    public float separationRadius = 1.5f;
    public float steerDistance = 2.0f;
    public float maxSteer = 4.0f;
    public int maxNeighbors = 8;

    [Header("Follow Rules")]
    public float stopFollowingIfFartherThan = 25f;
    public float minFollowDistance = 1.2f;

    [Header("Work (Timing)")]
    public float defaultWorkDuration = 8f;   // fallback if TaskSite.workDuration <= 0

    [Header("Work (Orbit)")]
    public bool useKinematicOrbit = true;    // decouple from NavMesh while working
    public bool faceAlongOrbit = true;
    public float orbitMoveSpeed = 3.0f;      // kinematic move speed along ring
    public float workRepathEvery = 0.25f;    // used if not kinematic

    [Header("Work (Randomization)")]
    public float orbitSpeedMinDeg = 60f;
    public float orbitSpeedMaxDeg = 130f;
    public float radialOscAmp = 0.30f;
    public float radialOscFreqMin = 0.4f;
    public float radialOscFreqMax = 1.2f;
    public float angularOscAmpDeg = 8f;
    public float angularOscFreqMin = 0.3f;
    public float angularOscFreqMax = 0.9f;

    [Header("Decline")]
    public float declineCooldownSeconds = 3.0f;

    public NPCState state { get; private set; } = NPCState.Roam;
    public Transform followTarget { get; private set; }

    NavMeshAgent _agent;
    Vector3 _currentRoamTarget;
    float _nextRetargetTime;
    float _retargetJitter;
    int _frame;
    int updateOffset;

    TaskSite _task;
    float _orbitAngle;
    float _nextWorkRepath;
    float _workEndTime;

    int _orbitSign = 1;
    float _orbitSpeedDeg;
    float _radOscFreq;
    float _angOscFreq;
    float _oscPhaseR;
    float _oscPhaseA;

    bool _cachedUpdatePosition, _cachedUpdateRotation, _cachedAutoBraking;
    float _cachedStoppingDistance, _cachedSpeed, _cachedAccel;

    float _cooldownEnd;

    void OnEnable() => NPCManager.Instance?.Register(this);
    void OnDisable()
    {
        if (_task != null) _task.UnregisterWorker(this);
        NPCManager.Instance?.Unregister(this);
    }

    void Start()
    {
        EnsureSpawnInside();

        _agent = GetComponent<NavMeshAgent>();
        _agent.updateRotation = true;
        _agent.stoppingDistance = 0f;

        var mgr = NPCManager.Instance;
        updateOffset = Random.Range(0, Mathf.Max(1, mgr ? mgr.neighborUpdateStride : 4));
        _retargetJitter = Random.Range(-0.6f, 0.6f);

        PickNewRoamTarget(true);
        ApplyColor(mgr ? mgr.roamColor : Color.blue);

        // Add glowing sphere indicator
        CreateRoleIndicator();
    }

    void Update()
    {
        _frame++;

        switch (state)
        {
            case NPCState.Roam: DoRoam(); break;
            case NPCState.FollowToDecision: DoFollowToDecision(); break;
            case NPCState.AwaitDecision: break;
            case NPCState.FollowApproved: DoFollowApproved(); break;
            case NPCState.FollowToTask: DoFollowToTask(); break;
            case NPCState.Work: DoWork(); break;
            case NPCState.DeclineCooldown: DoDeclineCooldown(); break;
            case NPCState.ReturnToRoam: DoReturnToRoam(); break;
        }
    }

    public bool TryRecruit(Transform player)
    {
        if (!CanBeRecruited()) return false;
        followTarget = player;
        state = NPCState.FollowToDecision;
        _agent.speed = followSpeed;
        ApplyColor(NPCManager.Instance ? NPCManager.Instance.followColor : Color.yellow);
        return true;
    }

    public void SetAwaitDecision()
    {
        if (!IsInDecisionZone()) return;
        state = NPCState.AwaitDecision;
        _agent.ResetPath();
        ApplyColor(NPCManager.Instance ? NPCManager.Instance.followColor : Color.yellow);
    }

    public void DecideWork(bool accept)
    {
        if (!IsInDecisionZone()) return;

        if (accept)
        {
            state = NPCState.FollowApproved;
            _agent.speed = followSpeed;
            ApplyColor(NPCManager.Instance ? NPCManager.Instance.taskColor : Color.green);
        }
        else
        {
            // Immediately roam while RED and ignore player until cooldown ends
            EnterDeclineCooldown();
        }
    }

    public void AssignTask(TaskSite site)
    {
        if (state != NPCState.FollowApproved || site == null) return;

        _task = site;
        state = NPCState.FollowToTask;
        _agent.speed = followSpeed;
        _agent.stoppingDistance = 0f;

        Vector3 dest = _task.transform.position;
        if (NavMesh.SamplePosition(dest, out var hit, 3f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
    }

    public bool IsAwaitingDecision() => state == NPCState.AwaitDecision;
    public bool IsInDecisionZone() => NPCManager.Instance && NPCManager.Instance.IsInsideDecisionZone(transform.position);

    public bool CanBeRecruited()
    {
        if (state == NPCState.DeclineCooldown || state == NPCState.Work || state == NPCState.FollowToTask)
            return false;
        return true;
    }

    public void SwitchToFollow(Transform newTarget) => TryRecruit(newTarget);

    void DoRoam()
    {
        _agent.speed = roamSpeed;

        if (Vector3.Distance(transform.position, _currentRoamTarget) <= waypointReachDist ||
            Time.time >= _nextRetargetTime)
        {
            PickNewRoamTarget(false);
        }

        Vector3 desired = (_currentRoamTarget - transform.position);
        desired.y = 0f;
        if (desired.sqrMagnitude > 0.001f) desired = desired.normalized * maxSteer;

        Vector3 contain = ComputeContainmentVelocity(1.5f, 0.75f);

        Vector3 flock = Vector3.zero;
        var mgr = NPCManager.Instance;
        if (mgr && ((_frame + updateOffset) % mgr.neighborUpdateStride == 0))
            flock = ComputeFlockingVelocity();

        Vector3 steer = desired * 0.6f + flock * 0.3f + contain * 0.1f;

        if (!mgr.roamArea.bounds.Contains(transform.position) && contain.sqrMagnitude > 0.0f)
            steer = contain + desired * 0.25f;

        if (steer.sqrMagnitude < 0.0005f)
        {
            if (!_agent.hasPath || _agent.remainingDistance <= waypointReachDist * 0.5f)
                _agent.SetDestination(_currentRoamTarget);
            return;
        }

        Vector3 ahead = transform.position + steer.normalized * steerDistance;
        if (NavMesh.SamplePosition(ahead, out var hit, 2.0f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);

        if (mgr && mgr.roamArea && !_agent.pathPending && _agent.hasPath)
        {
            Vector3 final = _agent.pathEndPosition;
            if (!mgr.roamArea.bounds.Contains(final))
            {
                Vector3 clamped = BoxClosestPointInside(mgr.roamArea, final);
                if (NavMesh.SamplePosition(clamped, out var hit2, 2f, NavMesh.AllAreas))
                    _agent.SetDestination(hit2.position);
            }
        }
    }

    void DoFollowToDecision()
    {
        if (!followTarget) { SwitchToRoam(); return; }

        float d = Vector3.Distance(transform.position, followTarget.position);
        Vector3 goal = followTarget.position;

        if (d < minFollowDistance)
        {
            Vector3 dir = (transform.position - followTarget.position).normalized;
            goal = followTarget.position + dir * minFollowDistance;
        }

        if (NavMesh.SamplePosition(goal, out var hit, 2f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);

        if (d > stopFollowingIfFartherThan)
            SwitchToRoam();
    }

    void DoFollowApproved()
    {
        if (!followTarget) { SwitchToRoam(); return; }

        float d = Vector3.Distance(transform.position, followTarget.position);
        Vector3 goal = followTarget.position;

        if (d < minFollowDistance)
        {
            Vector3 dir = (transform.position - followTarget.position).normalized;
            goal = followTarget.position + dir * minFollowDistance;
        }

        if (NavMesh.SamplePosition(goal, out var hit, 2f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);

        if (d > stopFollowingIfFartherThan)
            SwitchToRoam();
    }

    void DoFollowToTask()
    {
        if (_task == null) { SwitchToRoam(); return; }

        Vector3 dest = _task.transform.position;
        if (NavMesh.SamplePosition(dest, out var hit, 3f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);

        if (!_agent.pathPending && _agent.remainingDistance <= (_task.workRadius + 0.6f))
            EnterWork();
    }

    void DoWork()
    {
        if (_task == null)
        {
            ExitWorkToRoam();
            return;
        }

        float dt = Time.deltaTime;
        _orbitAngle += _orbitSign * _orbitSpeedDeg * dt;
        if (_orbitAngle > 360f) _orbitAngle -= 360f;
        if (_orbitAngle < -360f) _orbitAngle += 360f;

        float t = Time.time;
        float radialOsc = radialOscAmp * Mathf.Sin(2f * Mathf.PI * _radOscFreq * t + _oscPhaseR);
        float angularOsc = angularOscAmpDeg * Mathf.Sin(2f * Mathf.PI * _angOscFreq * t + _oscPhaseA);

        float baseRadius = _task.workRadius;
        float radius = Mathf.Max(0.1f, baseRadius + radialOsc);
        float angleDeg = _orbitAngle + angularOsc;

        Vector3 center = _task.transform.position;
        Vector3 ring = new Vector3(Mathf.Cos(angleDeg * Mathf.Deg2Rad), 0f,
                                   Mathf.Sin(angleDeg * Mathf.Deg2Rad)) * radius;
        Vector3 target = center + ring;

        // === Orbital movement (same for all roles) ===
        if (useKinematicOrbit)
        {
            if (NavMesh.SamplePosition(target, out var hit, 1.5f, NavMesh.AllAreas))
                target.y = hit.position.y;
            else
                target.y = transform.position.y;

            transform.position = Vector3.MoveTowards(transform.position, target, orbitMoveSpeed * dt);

            if (faceAlongOrbit)
            {
                float aheadDeg = angleDeg + _orbitSign * 5f;
                Vector3 tangent = new Vector3(Mathf.Cos(aheadDeg * Mathf.Deg2Rad), 0f,
                                              Mathf.Sin(aheadDeg * Mathf.Deg2Rad));
                Vector3 lookDir = (center + tangent * radius) - transform.position;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(lookDir),
                        10f * dt
                    );
            }
        }
        else
        {
            if (Time.time >= _nextWorkRepath)
            {
                if (NavMesh.SamplePosition(target, out var hit2, 2f, NavMesh.AllAreas))
                    _agent.SetDestination(hit2.position);
                _nextWorkRepath = Time.time + Mathf.Max(0.1f, workRepathEvery);
            }
        }

        // === Role-specific effect ===
        if (_task != null)
        {
            switch (role)
            {
                case NPCRole.Worker:
                    // Worker contributes positively to site repair
                    _task.AddHealth(_task.repairPerWorkerPerSecond * dt);
                    break;

                case NPCRole.Attacker:
                    // Attacker contributes negatively (damages instead of repairing)
                    _task.AddHealth(-_task.repairPerWorkerPerSecond * dt);
                    break;

                case NPCRole.Wanderer:
                    // Wanderer only stays for a fixed time, no health effect
                    if (_workEndTime <= 0f)
                        _workEndTime = Time.time + Mathf.Max(1f, wanderDuration);
                    break;
            }
        }

        // === Exit condition ===
        if (_workEndTime > 0f && Time.time >= _workEndTime)
            ExitWorkToRoam();
    }


    void DoDeclineCooldown()
    {
        // Roam movement but keep color RED and ignore player
        DoRoam();
        if (Time.time >= _cooldownEnd)
            SwitchToRoam(); // will turn blue again
    }

    void DoReturnToRoam()
    {
        if (NPCManager.Instance)
        {
            Vector3 dest = NPCManager.Instance.GetRandomNavPointInside();
            if (NavMesh.SamplePosition(dest, out var hit, 2f, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
        }

        if (!_agent.pathPending && _agent.remainingDistance <= waypointReachDist + 0.5f)
            SwitchToRoam();
    }

    public void SwitchToRoam()
    {
        if (state == NPCState.Work) RestoreAgentFromWork();

        followTarget = null;
        state = NPCState.Roam;
        _agent.speed = roamSpeed;
        PickNewRoamTarget(true);
        ApplyColor(NPCManager.Instance ? NPCManager.Instance.roamColor : Color.blue);
    }

    public void SetFollowTarget(Transform player) => followTarget = player;

    public void ApplyColor(Color c)
    {
        var mrs = GetComponentsInChildren<MeshRenderer>();
        foreach (var r in mrs)
        {
            if (!r) continue;
            if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
            if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", c);
        }
    }

    void EnterDeclineCooldown()
    {
        if (state == NPCState.Work) RestoreAgentFromWork();
        if (_task != null) { _task.UnregisterWorker(this); _task = null; }

        followTarget = null;
        state = NPCState.DeclineCooldown;
        _agent.speed = roamSpeed;
        _cooldownEnd = Time.time + declineCooldownSeconds;
        ApplyColor(NPCManager.Instance ? NPCManager.Instance.declineColor : Color.red);

        // start roaming right away so it "goes back" immediately
        if (!_agent.hasPath) PickNewRoamTarget(true);
    }

    void EnterWork()
    {
        state = NPCState.Work;

        _orbitSign = Random.value < 0.5f ? -1 : 1;
        _orbitSpeedDeg = Random.Range(orbitSpeedMinDeg, orbitSpeedMaxDeg);
        _radOscFreq = Random.Range(radialOscFreqMin, radialOscFreqMax);
        _angOscFreq = Random.Range(angularOscFreqMin, angularOscFreqMax);
        _oscPhaseR = Random.value * Mathf.PI * 2f;
        _oscPhaseA = Random.value * Mathf.PI * 2f;

        _orbitAngle = Random.Range(0f, 360f);
        _nextWorkRepath = 0f;

        float dur = (_task && _task.workDuration > 0f) ? _task.workDuration : defaultWorkDuration;
        _workEndTime = dur > 0f ? Time.time + dur : -1f;

        if (_task != null)
        {
            if (role == NPCRole.Worker)
                _task.RegisterWorker(this, _task.repairPerWorkerPerSecond);
            else if (role == NPCRole.Attacker)
                _task.RegisterWorker(this, -_task.repairPerWorkerPerSecond);
            // Wanderer: do not register (no effect on health)
        }

        // Optional color cue per role (keep your existing colors if you like)
        var mgr = NPCManager.Instance;
        switch (role)
        {
            case NPCRole.Worker: ApplyColor(mgr ? mgr.taskColor : Color.green); break;
            case NPCRole.Attacker: ApplyColor(new Color(1f, 0.4f, 0.2f)); break; // orange-red
            case NPCRole.Wanderer: ApplyColor(new Color(0.6f, 0.6f, 0.6f)); break; // gray
        }

        // Ensure Wanderer gets a finite stay
        if (role == NPCRole.Wanderer)
            _workEndTime = Time.time + Mathf.Max(1f, wanderDuration);

        if (useKinematicOrbit)
        {
            _cachedUpdatePosition = _agent.updatePosition;
            _cachedUpdateRotation = _agent.updateRotation;
            _cachedAutoBraking = _agent.autoBraking;
            _cachedStoppingDistance = _agent.stoppingDistance;
            _cachedSpeed = _agent.speed;
            _cachedAccel = _agent.acceleration;

            _agent.isStopped = true;
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.autoBraking = true;
        }
        else
        {
            _agent.isStopped = false;
            _agent.updatePosition = true;
            _agent.updateRotation = true;
            _agent.autoBraking = false;
            _agent.stoppingDistance = 0f;
            _agent.speed = followSpeed;
            _agent.acceleration = Mathf.Max(_agent.acceleration, 16f);
        }
    }

    void ExitWorkToRoam()
    {
        if (_task != null) _task.UnregisterWorker(this);
        RestoreAgentFromWork();
        _task = null;
        state = NPCState.ReturnToRoam;
        _agent.speed = followSpeed;
        ApplyColor(NPCManager.Instance ? NPCManager.Instance.followColor : Color.yellow);
    }

    void RestoreAgentFromWork()
    {
        if (useKinematicOrbit)
        {
            // snap agent to our visible position on the NavMesh to avoid pops
            SyncAgentToTransformOnNavMesh();

            _agent.updatePosition = _cachedUpdatePosition;
            _agent.updateRotation = _cachedUpdateRotation;
            _agent.autoBraking = _cachedAutoBraking;
            _agent.stoppingDistance = _cachedStoppingDistance;
            _agent.speed = _cachedSpeed;
            _agent.acceleration = _cachedAccel;
            _agent.isStopped = false;
            _agent.ResetPath();
        }
    }

    void SyncAgentToTransformOnNavMesh()
    {
        Vector3 pos = transform.position;
        if (NavMesh.SamplePosition(pos, out var hit, 1.5f, NavMesh.AllAreas))
            pos = hit.position;
        _agent.Warp(pos);          // ensures agent and transform are in sync
        _agent.nextPosition = pos; // extra safety
        _agent.velocity = Vector3.zero;
    }

    void PickNewRoamTarget(bool immediate)
    {
        _currentRoamTarget = NPCManager.Instance
            ? NPCManager.Instance.GetRandomNavPointInside()
            : transform.position;

        float period = Mathf.Max(0.8f, retargetRoamEvery + _retargetJitter);
        _nextRetargetTime = Time.time + (immediate ? 0.5f : period);

        if (NavMesh.SamplePosition(_currentRoamTarget, out var hit, 2.0f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
    }

    Vector3 ComputeFlockingVelocity()
    {
        var all = NPCManager.Instance ? NPCManager.Instance.All : null;
        if (all == null) return Vector3.zero;

        Vector3 pos = transform.position;
        Vector3 separation = Vector3.zero, alignment = Vector3.zero, cohesion = Vector3.zero;
        int count = 0;

        for (int i = 0; i < all.Count; i++)
        {
            var other = all[i];
            if (other == this || !other) continue;
            float dist = Vector3.Distance(pos, other.transform.position);
            if (dist > neighborRadius) continue;

            count++; if (count > maxNeighbors) break;
            if (dist < separationRadius && dist > 0.0001f) separation += (pos - other.transform.position) / dist;
            alignment += other.transform.forward;
            cohesion += other.transform.position;
        }

        if (count == 0) return Vector3.zero;

        alignment /= count;
        cohesion = (cohesion / count) - pos;

        var mgr = NPCManager.Instance;
        float wSep = mgr ? mgr.separationWeight : 0.7f;
        float wAli = mgr ? mgr.alignmentWeight : 0.5f;
        float wCoh = mgr ? mgr.cohesionWeight : 0.6f;

        Vector3 steer = separation * wSep + alignment * wAli + cohesion * wCoh;
        steer.y = 0f;
        if (steer.magnitude > maxSteer) steer = steer.normalized * maxSteer;
        return steer;
    }

    Vector3 BoxClosestPointInside(BoxCollider box, Vector3 worldPos)
    {
        var b = box.bounds;
        float x = Mathf.Clamp(worldPos.x, b.min.x, b.max.x);
        float y = Mathf.Clamp(worldPos.y, b.min.y, b.max.y);
        float z = Mathf.Clamp(worldPos.z, b.min.z, b.max.z);
        return new Vector3(x, y, z);
    }

    void EnsureSpawnInside()
    {
        var mgr = NPCManager.Instance;
        if (!mgr || !mgr.roamArea) return;

        if (!mgr.roamArea.bounds.Contains(transform.position))
        {
            Vector3 p = BoxClosestPointInside(mgr.roamArea, transform.position);
            if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas))
                transform.position = hit.position;
        }
    }

    Vector3 ComputeContainmentVelocity(float innerMargin = 1.5f, float outerBoost = 1.0f)
    {
        var mgr = NPCManager.Instance;
        if (!mgr || !mgr.roamArea) return Vector3.zero;

        var box = mgr.roamArea;
        var b = box.bounds;
        Vector3 pos = transform.position;

        if (!b.Contains(pos))
        {
            Vector3 target = BoxClosestPointInside(box, pos);
            Vector3 v = (target - pos); v.y = 0;
            return v.normalized * (maxSteer * (1.0f + outerBoost));
        }

        Vector3 center = b.center;
        Vector3 toCenter = (center - pos); toCenter.y = 0;

        float dx = Mathf.Min(pos.x - b.min.x, b.max.x - pos.x);
        float dz = Mathf.Min(pos.z - b.min.z, b.max.z - pos.z);
        float edgeProximity = Mathf.Min(dx, dz);

        if (edgeProximity < innerMargin)
            return toCenter.normalized * (maxSteer * Mathf.InverseLerp(innerMargin, 0f, edgeProximity));

        return Vector3.zero;
    }

    void CreateRoleIndicator()
    {
        // === 1. Create a small sphere object ===
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "RoleIndicator";

        // remove collider to avoid interference
        Destroy(sphere.GetComponent<Collider>());

        // parent it to NPC
        sphere.transform.SetParent(transform);

        // === 2. Position it above the NPC's head ===
        // Try to guess height based on current bounds or scale
        float height = 2f;
        var renderer = GetComponentInChildren<Renderer>();
        if (renderer != null)
            height = renderer.bounds.size.y * 1.2f; // just above head

        sphere.transform.localPosition = new Vector3(0f, height, 0f);

        // === 3. Scale down to 1/5th of NPC size ===
        float scale = 0.5f * transform.localScale.magnitude;
        sphere.transform.localScale = Vector3.one * scale;

        // === 4. Choose color based on role ===
        Color col = Color.white;
        switch (role)
        {
            case NPCRole.Attacker: col = Color.red; break;
            case NPCRole.Worker: col = Color.green; break;
            case NPCRole.Wanderer: col = Color.blue; break;
        }

        // === 5. Create glowing HDRP material ===
#if UNITY_HDRP
    var mat = new UnityEngine.Material(UnityEngine.Rendering.HighDefinition.HDRenderPipeline.defaultMaterial);
    mat.EnableKeyword("_EMISSION");
    mat.SetColor("_BaseColor", col);
    mat.SetColor("_EmissiveColor", col * 3000f); // strong glow
    mat.SetFloat("_EmissiveIntensity", 20f);   // HDR emission intensity
#else
        var mat = new Material(Shader.Find("HDRP/Lit"));
        mat.SetColor("_BaseColor", col);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", col * 3000f);
#endif

        sphere.GetComponent<Renderer>().material = mat;
    }

}
