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
    ReturnToRoam,
    Scripted        // driven by the game flow (demo / cutscene beats); ignores the player
}

public enum NPCRole
{
    Worker,     // "Fixer"   - prefers repairing sites
    Attacker,   // "Breaker" - prefers dismantling sites
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

    [Header("Movement Feel")]
    [Tooltip("Height of the visual hop while the Mycari is moving.")]
    [Min(0f)] public float hopHeight = 0.22f;
    [Tooltip("Hops per second. The supplied moving sprites are a short crouch-stretch-land cycle.")]
    [Min(0.1f)] public float hopFrequency = 1.3f;
    [Tooltip("Higher values keep the Mycari grounded longer before the quick upward hop.")]
    [Min(0.1f)] public float hopSharpness = 1.8f;
    [Tooltip("How quickly the NavMesh agent reaches the frame-based speed burst. Higher means less ramp-up.")]
    [Min(0.1f)] public float speedResponseAcceleration = 18f;

    [Header("Roaming")]
    public float waypointReachDist = 1.0f;
    public float retargetRoamEvery = 3.0f;

    [Header("Flocking")]
    public float neighborRadius = 6.0f;
    public float separationRadius = 2.6f;
    public float steerDistance = 2.0f;
    public float maxSteer = 4.0f;
    public int maxNeighbors = 8;

    [Header("Follow Rules")]
    public float stopFollowingIfFartherThan = 25f;
    public float minFollowDistance = 3.5f;  // Minimum distance to keep from player (prevents collision)

    [Header("Work (Timing)")]
    [Tooltip("Legacy. Work now ends when the command goal is reached (site fully repaired / destroyed) " +
             "or when TaskSite.workDuration (> 0) elapses; this value is kept for serialization only.")]
    public float defaultWorkDuration = 8f;

    [Header("Work (Orbit)")]
    public bool useKinematicOrbit = true;    // decouple from NavMesh while working
    public bool faceAlongOrbit = true;
    public float orbitMoveSpeed = 3.0f;      // kinematic move speed along ring
    public float workRepathEvery = 0.25f;    // used if not kinematic

    [Header("Work (Stations)")]
    [Tooltip("Seconds spent working one spot before moving round to another.")]
    [Min(0.2f)] public float stationDwellMin = 1.6f;
    [Min(0.2f)] public float stationDwellMax = 3.2f;
    [Tooltip("How far round the site it moves between stations, in degrees.")]
    public float stationHopMinDegrees = 70f;
    public float stationHopMaxDegrees = 150f;
    [Tooltip("Close enough to count as arrived at a station.")]
    [Min(0.05f)] public float stationArriveDistance = 0.35f;
    [Tooltip("Work rate while stood at a station, scaled up to offset the time spent walking.")]
    [Min(1f)] public float stationWorkRateScale = 1.6f;

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

    [Header("Scripted")]
    [Tooltip("A scripted move counts as arrived after this many seconds even if the agent never reaches " +
             "the target (stuck / unreachable), so a cutscene beat can never hang.")]
    public float scriptedStuckSeconds = 20f;

    [Tooltip("If false, Start() will not snap the NPC into the roam area. NPCManager.SpawnAt clears this " +
             "so the flow can spawn Mycari outside the roam box (e.g. next to the windmill).")]
    [HideInInspector] public bool clampSpawnToRoamArea = true;

    public NPCState state { get; private set; } = NPCState.Roam;
    public Transform followTarget { get; private set; }

    /// <summary>What the NPC was asked to do at <see cref="CurrentTask"/> (valid in FollowToTask / Work).</summary>
    public TaskCommand CurrentCommand => _command;
    /// <summary>Site the NPC is heading to / working at, or null.</summary>
    public TaskSite CurrentTask => _task;
    /// <summary>True while the game flow drives this NPC (it ignores the player).</summary>
    public bool IsScripted => state == NPCState.Scripted;

    /// <summary>Stable ground anchor for scan labels; excludes the procedural hop offset.</summary>
    public Vector3 ScanLabelPosition
    {
        get
        {
            if (_agent == null) return transform.position;
            float hopOffset = _agent.baseOffset - _baseAgentOffset;
            return transform.position - Vector3.up * hopOffset;
        }
    }

    /// <summary>Short name of the role for the scan overlay.</summary>
    public string RoleLabel => role switch
    {
        NPCRole.Worker => "Fixer",
        NPCRole.Attacker => "Breaker",
        _ => "Wanderer"
    };

    /// <summary>Short human-readable activity for the scan overlay.</summary>
    public string StateLabel
    {
        get
        {
            switch (state)
            {
                case NPCState.FollowToDecision:
                case NPCState.AwaitDecision:
                case NPCState.FollowApproved: return "Following";
                case NPCState.Work: return _command == TaskCommand.Dismantle ? "Dismantling" : "Repairing";
                case NPCState.DeclineCooldown: return "Refusing";
                case NPCState.Scripted: return "Busy";
                default: return "Roaming";
            }
        }
    }

    /// <summary>Fired when the NPC reaches its task site and starts working (EnterWork).</summary>
    public event System.Action<NPCFlocker> OnArrivedAtTask;

    NavMeshAgent _agent;
    MycariAnimator _animator;
    float _baseAgentSpeed;
    SpriteRenderer _spriteRenderer;
    float _baseAgentOffset;
    float _hopPhase;
    Vector3 _lastPosition;
    Vector3 _currentRoamTarget;
    float _nextRetargetTime;
    float _retargetJitter;
    int _frame;
    int updateOffset;

    TaskSite _task;
    TaskCommand _command = TaskCommand.Repair;
    float _orbitAngle;
    float _nextWorkRepath;
    float _workEndTime;

    float _stationAngle;
    float _stationDwellEnd;
    bool  _atStation;

    int _orbitSign = 1;
    float _orbitSpeedDeg;
    float _radOscFreq;
    float _angOscFreq;
    float _oscPhaseR;
    float _oscPhaseA;

    bool _cachedUpdatePosition, _cachedUpdateRotation, _cachedAutoBraking;
    float _cachedStoppingDistance, _cachedSpeed, _cachedAccel;

    float _cooldownEnd;

    // Scripted sub-mode (only meaningful while state == Scripted)
    enum ScriptedMode { Idle, MoveTo, Follow }
    ScriptedMode _scriptedMode = ScriptedMode.Idle;
    Vector3 _scriptedTarget;
    float _scriptedArriveDist;
    float _scriptedDeadline;
    System.Action _onScriptedArrived;
    Transform _scriptedFollow;

    // Role indicator visibility
    GameObject _roleIndicator;
    FollowPlayer _daffodil;     // cached; the indicator is only visible while Daffodil scans

    // Selection ring, drawn on the ground under whichever Mycari the player is about to talk to.
    LineRenderer _targetRing;
    bool _targeted;

    /// <summary>
    /// Marks this one as the Mycari the player's E would act on. They roam in a flock and end up
    /// shoulder to shoulder, so without a marker you cannot tell which one you are about to grab.
    /// </summary>
    public void SetTargeted(bool on)
    {
        if (_targeted == on) return;
        _targeted = on;
        if (on && _targetRing == null) BuildTargetRing();
        if (_targetRing) _targetRing.enabled = on;
    }

    void BuildTargetRing()
    {
        var go = new GameObject("TargetRing");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        _targetRing = go.AddComponent<LineRenderer>();
        _targetRing.useWorldSpace = false;
        _targetRing.loop = true;
        _targetRing.widthMultiplier = 0.06f;
        _targetRing.numCapVertices = 4;
        _targetRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _targetRing.receiveShadows = false;
        _targetRing.alignment = LineAlignment.View;

        const int seg = 36;
        float r = Mathf.Max(0.45f, _agent ? _agent.radius * 1.6f : 0.8f);
        _targetRing.positionCount = seg;
        for (int i = 0; i < seg; i++)
        {
            float t = (i / (float)seg) * Mathf.PI * 2f;
            // Sits just above the feet so it reads as a ring on the ground, not a halo.
            _targetRing.SetPosition(i, new Vector3(Mathf.Cos(t) * r, 0.04f, Mathf.Sin(t) * r));
        }

        var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        var mat = new Material(sh);
        var amber = new Color(1f, 0.66f, 0.36f, 1f);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", amber);
        _targetRing.material = mat;
        _targetRing.startColor = _targetRing.endColor = amber;
        _targetRing.enabled = false;
    }

    void UpdateTargetRing()
    {
        if (_targetRing == null || !_targeted) return;
        // Gentle pulse so it reads as live selection rather than a decal.
        float a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 3.2f));
        var c = new Color(1f, 0.66f, 0.36f, a);
        _targetRing.startColor = _targetRing.endColor = c;
    }

    void Awake()
    {
        // Grab the agent here so scripted calls issued right after Instantiate (before Start) are safe.
        _agent = GetComponent<NavMeshAgent>();
        _agent.updateRotation = false;
        _agent.stoppingDistance = 0f;
        _baseAgentSpeed = _agent.speed;
        _agent.acceleration = speedResponseAcceleration;
        _animator = GetComponent<MycariAnimator>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _baseAgentOffset = _agent.baseOffset;
        _lastPosition = transform.position;
    }

    void OnEnable() => NPCManager.Instance?.Register(this);
    void OnDisable()
    {
        if (_task != null) _task.UnregisterWorker(this);
        NPCManager.Instance?.Unregister(this);
    }

    void Start()
    {
        if (clampSpawnToRoamArea && state != NPCState.Scripted)
            EnsureSpawnInside();

        var mgr = NPCManager.Instance;
        updateOffset = Random.Range(0, Mathf.Max(1, mgr ? mgr.neighborUpdateStride : 4));
        _retargetJitter = Random.Range(-0.6f, 0.6f);

        // Only start roaming if nobody has already given this NPC orders (the flow may script it
        // in the same frame it was spawned).
        if (state == NPCState.Roam)
        {
            PickNewRoamTarget(true);
            ApplyColor(mgr ? mgr.roamColor : Color.blue);
        }

        // Add glowing sphere indicator
        CreateRoleIndicator();
    }

    void Update()
    {
        _frame++;

        UpdateRoleIndicatorVisibility();
        UpdateTargetRing();

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
            case NPCState.Scripted: DoScripted(); break;
        }

        UpdateAnimation();
    }

    void UpdateAnimation()
    {
        float speed = _agent != null ? _agent.velocity.magnitude : 0f;
        Vector3 movement = _agent != null ? _agent.velocity : Vector3.zero;

        if (state == NPCState.Work && useKinematicOrbit)
        {
            movement = transform.position - _lastPosition;
            speed = movement.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        }

        bool moving = speed > 0.05f;
        if (_animator != null)
        {
            _animator.SetMovementSpeed(moving ? 1f : 0f);
            // Repairing plays while it is actually on the job, whichever way it was sent there.
            _animator.SetRepairing(state == NPCState.Work && _atStation && _command == TaskCommand.Repair);
        }

        if (_agent != null)
            _agent.speed = _baseAgentSpeed * (_animator != null ? _animator.MovementSpeedMultiplier : 1f);

        UpdateFacing(movement);
        UpdateHop(moving);
        _lastPosition = transform.position;
    }

    void SetAgentSpeed(float speed)
    {
        _baseAgentSpeed = speed;
        _agent.speed = speed;
    }

    void UpdateFacing(Vector3 movement)
    {
        if (_spriteRenderer == null) return;

        movement.y = 0f;
        if (movement.sqrMagnitude < 0.0025f) return;

        Vector3 right = Camera.main ? Camera.main.transform.right : Vector3.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;
        right.Normalize();

        // The source art faces screen-right; flip it when travel is screen-left.
        _spriteRenderer.flipX = Vector3.Dot(movement.normalized, right) < 0f;
    }

    void UpdateHop(bool moving)
    {
        if (_agent == null) return;

        if (!moving || hopHeight <= 0f)
        {
            _hopPhase = 0f;
            _agent.baseOffset = _baseAgentOffset;
            return;
        }

        _hopPhase = Mathf.Repeat(_hopPhase + Time.deltaTime * hopFrequency, 1f);
        float arc = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(_hopPhase * Mathf.PI)), hopSharpness);
        _agent.baseOffset = _baseAgentOffset + arc * hopHeight;
    }

    void UpdateRoleIndicatorVisibility()
    {
        if (_roleIndicator == null) return;

        // Static singleton lookup (no scene search); re-check while null in case Daffodil spawns late.
        if (_daffodil == null) _daffodil = FollowPlayer.Instance;

        // Visible only while Daffodil is actively scanning (she already checks energy / sleep).
        bool shouldShow = _daffodil != null && _daffodil.IsScanning;
        if (_roleIndicator.activeSelf != shouldShow)
            _roleIndicator.SetActive(shouldShow);
    }

    // ------------------------------------------------------------------ Player interaction

    /// <summary>
    /// Anyone can be coaxed into tagging along, melody or no melody — following is curiosity,
    /// not obedience. Getting one to actually DO something is gated on the melody instead;
    /// see <see cref="CanBeCommanded"/>.
    /// </summary>
    public bool TryRecruit(Transform player)
    {
        if (!CanBeRecruited() || player == null) return false;
        followTarget = player;
        state = NPCState.FollowToDecision;
        SetAgentSpeed(followSpeed);
        ResumeAgent();
        ApplyColor(NPCManager.Instance ? NPCManager.Instance.followColor : Color.yellow);
        return true;
    }

    public void SetAwaitDecision()
    {
        if (!IsInDecisionZone()) return;
        state = NPCState.AwaitDecision;
        if (_agent.isOnNavMesh) _agent.ResetPath();
        ApplyColor(NPCManager.Instance ? NPCManager.Instance.followColor : Color.yellow);
    }

    public void DecideWork(bool accept)
    {
        if (!IsInDecisionZone()) return;

        if (accept)
        {
            state = NPCState.FollowApproved;
            SetAgentSpeed(followSpeed);
            ApplyColor(NPCManager.Instance ? NPCManager.Instance.taskColor : Color.green);
        }
        else
        {
            // Immediately roam while RED and ignore player until cooldown ends
            EnterDeclineCooldown();
        }
    }

    /// <summary>
    /// Force approve the NPC for work without requiring decision zone.
    /// Called by puzzle success to bypass normal decision flow.
    /// </summary>
    public void ForceApproveForWork()
    {
        // Only approve if NPC is following the player
        if (state != NPCState.FollowToDecision && state != NPCState.AwaitDecision)
            return;

        state = NPCState.FollowApproved;
        SetAgentSpeed(followSpeed);
        ResumeAgent();
        ApplyColor(NPCManager.Instance ? NPCManager.Instance.taskColor : Color.green);
    }

    /// <summary>
    /// Legacy assignment (role decides the effect): Breakers dismantle, everyone else repairs.
    /// </summary>
    public void AssignTask(TaskSite site)
    {
        if (state != NPCState.FollowApproved || site == null) return;
        BeginTask(site, role == NPCRole.Attacker ? TaskCommand.Dismantle : TaskCommand.Repair);
    }

    /// <summary>Rolls the acceptance chance for a command (role-dependent table on NPCManager).</summary>
    public bool WillAccept(TaskCommand cmd)
    {
        var mgr = NPCManager.Instance;
        float chance = mgr ? mgr.GetAcceptChance(role, cmd) : 1f;
        if (chance >= 1f) return true;
        if (chance <= 0f) return false;
        return Random.value < chance;
    }

    /// <summary>
    /// Player gives an order. Requires the NPC to be following (FollowToDecision / AwaitDecision /
    /// FollowApproved) and the site to allow the command. A refusal puts the NPC into decline cooldown.
    /// Returns true when the NPC accepted and is now heading to the site.
    /// </summary>
    /// <summary>
    /// True once the player has learned the melody. Until then a Mycari will follow you around
    /// quite happily but has no idea what you are asking it to do.
    /// </summary>
    public bool CanBeCommanded
    {
        get
        {
            var flow = GameFlowManager.Instance;
            return flow == null || flow.MycariObey;
        }
    }

    public bool CommandTask(TaskSite site, TaskCommand cmd)
    {
        if (site == null) return false;
        if (state != NPCState.FollowToDecision && state != NPCState.AwaitDecision && state != NPCState.FollowApproved)
            return false;

        // No melody yet: this is incomprehension, not refusal, so it keeps following instead of
        // storming off into the decline cooldown.
        if (!CanBeCommanded) return false;

        if (!SiteAllows(site, cmd)) return false;

        if (!WillAccept(cmd))
        {
            EnterDeclineCooldown();
            return false;
        }

        BeginTask(site, cmd);
        return true;
    }

    /// <summary>
    /// Order without an acceptance roll (scripted flow). Works from any state; an NPC that is
    /// currently working is cleanly pulled off its site first.
    /// </summary>
    public void ForceCommandTask(TaskSite site, TaskCommand cmd)
    {
        if (site == null) return;
        LeaveCurrentActivity();
        BeginTask(site, cmd);
    }

    /// <summary>Shared entry for CommandTask / ForceCommandTask / AssignTask.</summary>
    void BeginTask(TaskSite site, TaskCommand cmd)
    {
        _task = site;
        _command = cmd;
        followTarget = null;
        state = NPCState.FollowToTask;
        SetAgentSpeed(followSpeed);
        _agent.stoppingDistance = 0f;
        ResumeAgent();
        ApplyColor(NPCManager.Instance ? NPCManager.Instance.taskColor : Color.green);

        Vector3 dest = _task.transform.position;
        if (_agent.isOnNavMesh && NavMesh.SamplePosition(dest, out var hit, 3f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
    }

    static bool SiteAllows(TaskSite site, TaskCommand cmd)
    {
        return cmd == TaskCommand.Repair ? site.allowRepair : site.allowDismantle;
    }

    public bool IsAwaitingDecision() => state == NPCState.AwaitDecision;
    public bool IsInDecisionZone() => NPCManager.Instance && NPCManager.Instance.IsInsideDecisionZone(transform.position);

    public bool CanBeRecruited()
    {
        if (state == NPCState.DeclineCooldown || state == NPCState.Work ||
            state == NPCState.FollowToTask || state == NPCState.Scripted)
            return false;
        return true;
    }

    public void SwitchToFollow(Transform newTarget) => TryRecruit(newTarget);

    // ------------------------------------------------------------------ Scripted API (game flow)

    /// <summary>
    /// Walk to a world position (NavMesh, followSpeed). <paramref name="onArrived"/> fires once when
    /// the agent is within <paramref name="arriveDistance"/>, or after <see cref="scriptedStuckSeconds"/>
    /// if it never gets there. The NPC stays Scripted (idle) afterwards until released or re-tasked.
    /// </summary>
    public void ScriptedMoveTo(Vector3 worldPos, float arriveDistance, System.Action onArrived)
    {
        LeaveCurrentActivity();
        EnterScripted();

        _scriptedMode = ScriptedMode.MoveTo;
        _scriptedTarget = worldPos;
        _scriptedArriveDist = Mathf.Max(0.05f, arriveDistance);
        _scriptedDeadline = Time.time + Mathf.Max(1f, scriptedStuckSeconds);
        _onScriptedArrived = onArrived;

        SetAgentSpeed(followSpeed);
        if (EnsureAgentOnNavMesh())
        {
            Vector3 dest = worldPos;
            if (NavMesh.SamplePosition(worldPos, out var hit, 3f, NavMesh.AllAreas)) dest = hit.position;
            _agent.SetDestination(dest);
        }
    }

    /// <summary>Follow any transform, ignoring recruit rules and the "too far" break. Stays Scripted.</summary>
    public void ScriptedFollow(Transform t)
    {
        LeaveCurrentActivity();
        EnterScripted();

        _scriptedMode = t ? ScriptedMode.Follow : ScriptedMode.Idle;
        _scriptedFollow = t;
        SetAgentSpeed(followSpeed);
    }

    /// <summary>Send the NPC to work at a site with no acceptance roll.</summary>
    public void ScriptedAssign(TaskSite site, TaskCommand cmd) => ForceCommandTask(site, cmd);

    /// <summary>Hand the NPC back to its normal behaviour.</summary>
    public void ScriptedRelease() => SwitchToRoam();

    void EnterScripted()
    {
        state = NPCState.Scripted;
        _scriptedMode = ScriptedMode.Idle;
        ResumeAgent();
        ApplyColor(NPCManager.Instance ? NPCManager.Instance.followColor : Color.yellow);
    }

    void DoScripted()
    {
        switch (_scriptedMode)
        {
            case ScriptedMode.Follow:
                if (!_scriptedFollow) { _scriptedMode = ScriptedMode.Idle; StopAgent(); return; }
                if (!EnsureAgentOnNavMesh()) return;   // nothing sensible to do off-mesh; wait
                FollowTransform(_scriptedFollow, false);
                return;

            case ScriptedMode.MoveTo:
                DoScriptedMove();
                return;

            default:
                return; // Idle: hold position, wait for the next instruction
        }
    }

    void DoScriptedMove()
    {
        bool onMesh = EnsureAgentOnNavMesh();
        float dt = Time.deltaTime;

        if (onMesh)
        {
            SetAgentSpeed(followSpeed);
            // A path may have been lost while off-mesh / warping; re-issue it.
            if (!_agent.hasPath && !_agent.pathPending)
            {
                Vector3 dest = _scriptedTarget;
                if (NavMesh.SamplePosition(_scriptedTarget, out var hit, 3f, NavMesh.AllAreas)) dest = hit.position;
                _agent.SetDestination(dest);
            }
        }
        else
        {
            // Could not get back onto the NavMesh: slide toward the target so the beat still progresses.
            Vector3 p = Vector3.MoveTowards(transform.position, _scriptedTarget, followSpeed * dt);
            p.y = transform.position.y;
            transform.position = p;
        }

        // Arrival: flat distance is checked first so a partial path / off-mesh slide still completes.
        Vector3 flat = transform.position - _scriptedTarget; flat.y = 0f;
        bool arrived = flat.magnitude <= _scriptedArriveDist;

        if (!arrived && onMesh && !_agent.pathPending)
        {
            if (_agent.pathStatus == NavMeshPathStatus.PathInvalid) arrived = true;                 // unreachable
            else if (_agent.hasPath && _agent.remainingDistance <= _scriptedArriveDist) arrived = true;
        }

        if (!arrived && Time.time >= _scriptedDeadline) arrived = true;   // stuck fallback

        if (!arrived) return;

        _scriptedMode = ScriptedMode.Idle;
        StopAgent();

        // Clear before invoking: the callback commonly issues the next ScriptedMoveTo.
        var cb = _onScriptedArrived;
        _onScriptedArrived = null;
        cb?.Invoke();
    }

    /// <summary>Warps the agent back onto the NavMesh if it fell off. Returns whether it is on it now.</summary>
    bool EnsureAgentOnNavMesh()
    {
        if (_agent.isOnNavMesh) return true;
        if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas))
            return _agent.Warp(hit.position) && _agent.isOnNavMesh;
        return false;
    }

    void ResumeAgent()
    {
        if (_agent.isOnNavMesh && _agent.isStopped) _agent.isStopped = false;
    }

    void StopAgent()
    {
        if (!_agent.isOnNavMesh) return;
        _agent.isStopped = true;
        _agent.ResetPath();
        _agent.velocity = Vector3.zero;
    }

    /// <summary>
    /// Drops whatever the NPC is doing (work registration, kinematic orbit, follow target, scripted
    /// callbacks) without changing state. Callers set the new state right after.
    /// </summary>
    void LeaveCurrentActivity()
    {
        if (state == NPCState.Work) RestoreAgentFromWork();
        if (_task != null) { _task.UnregisterWorker(this); _task = null; }
        followTarget = null;
        _scriptedFollow = null;
        _onScriptedArrived = null;
        _scriptedMode = ScriptedMode.Idle;
        _workEndTime = -1f;
        ResumeAgent();
    }

    // ------------------------------------------------------------------ State behaviours

    void DoRoam()
    {
        SetAgentSpeed(roamSpeed);

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

        if (mgr && mgr.roamArea && !mgr.roamArea.bounds.Contains(transform.position) && contain.sqrMagnitude > 0.0f)
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
        FollowTransform(followTarget, true);
    }

    void DoFollowApproved()
    {
        if (!followTarget) { SwitchToRoam(); return; }
        FollowTransform(followTarget, true);
    }

    /// <summary>
    /// Shared follow step: keep minFollowDistance from the target, stop when close, resume when it
    /// moves away. When <paramref name="breakWhenFar"/> the NPC gives up beyond stopFollowingIfFartherThan.
    /// </summary>
    void FollowTransform(Transform target, bool breakWhenFar)
    {
        float d = Vector3.Distance(transform.position, target.position);

        // If too close, stop the agent completely
        if (d <= minFollowDistance)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
            _agent.velocity = Vector3.zero;
            return;
        }

        // Resume movement if stopped
        if (_agent.isStopped)
            _agent.isStopped = false;

        // Calculate goal position - move towards target but stop at minFollowDistance
        Vector3 dirToTarget = (target.position - transform.position).normalized;
        Vector3 goal = target.position - dirToTarget * minFollowDistance;

        if (NavMesh.SamplePosition(goal, out var hit, 2f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);

        if (breakWhenFar && d > stopFollowingIfFartherThan)
            SwitchToRoam();
    }

    void DoFollowToTask()
    {
        if (_task == null) { SwitchToRoam(); return; }
        if (!EnsureAgentOnNavMesh()) return;   // wait until we are back on the mesh

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

        // Work happens at stations around the site rather than as one continuous lap: walk to a
        // spot, stand and work it, then move round to another. Circling forever reads as pacing;
        // stopping to work somewhere reads as doing a job.
        float t = Time.time;
        float radialOsc = radialOscAmp * Mathf.Sin(2f * Mathf.PI * _radOscFreq * t + _oscPhaseR);
        float radius = Mathf.Max(0.1f, _task.workRadius + radialOsc);

        Vector3 center = _task.transform.position;
        Vector3 ring = new Vector3(Mathf.Cos(_stationAngle * Mathf.Deg2Rad), 0f,
                                   Mathf.Sin(_stationAngle * Mathf.Deg2Rad)) * radius;
        Vector3 target = center + ring;

        float flatDist = Vector3.ProjectOnPlane(target - transform.position, Vector3.up).magnitude;
        if (!_atStation && flatDist <= stationArriveDistance)
        {
            _atStation = true;
            _stationDwellEnd = t + Random.Range(stationDwellMin, stationDwellMax);
        }
        else if (_atStation && t >= _stationDwellEnd)
        {
            // Hop a good way round so the move is legible, and alternate direction sometimes.
            if (Random.value < 0.25f) _orbitSign = -_orbitSign;
            _stationAngle = Mathf.Repeat(_stationAngle + _orbitSign * Random.Range(stationHopMinDegrees, stationHopMaxDegrees), 360f);
            _atStation = false;
        }

        // === Orbital movement (same for all roles) ===
        if (useKinematicOrbit)
        {
            if (NavMesh.SamplePosition(target, out var hit, 1.5f, NavMesh.AllAreas))
                target.y = hit.position.y;
            else
                target.y = transform.position.y;

            if (!_atStation)
                transform.position = Vector3.MoveTowards(transform.position, target, orbitMoveSpeed * dt);

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

        // === Command effect ===
        // Health is applied here only; the RegisterWorker entry carries rate 0 (FX presence), so
        // there is no double application through TaskSite.Update.
        // Only counts while stood at a station - the walk between them is travel, not work.
        // The rate is scaled up so moving around does not make the job take longer overall.
        if (_atStation)
        {
            float rate = _task.repairPerWorkerPerSecond * stationWorkRateScale * dt;
            if (_command == TaskCommand.Repair)
                _task.AddHealth(rate);
            else
                _task.ApplyDamage(rate);
        }

        // === Exit condition: goal reached, or timed stay elapsed ===
        bool goalReached = _command == TaskCommand.Repair
            ? _task.health >= _task.maxHealth - 0.001f
            : _task.IsDestroyed;

        if (goalReached || (_workEndTime > 0f && Time.time >= _workEndTime))
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
        // Head back to one point inside the roam area (picked on entry); re-issue only if the path was lost.
        if (!_agent.hasPath && !_agent.pathPending && _agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(_currentRoamTarget, out var hit, 2f, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
        }

        if (!_agent.pathPending && _agent.remainingDistance <= waypointReachDist + 0.5f)
            SwitchToRoam();
    }

    public void SwitchToRoam()
    {
        LeaveCurrentActivity();

        state = NPCState.Roam;
        SetAgentSpeed(roamSpeed);
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
            if (_roleIndicator && r.gameObject == _roleIndicator) continue;   // keep the role glow colour
            if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
            if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", c);
        }
    }

    void EnterDeclineCooldown()
    {
        LeaveCurrentActivity();

        state = NPCState.DeclineCooldown;
        SetAgentSpeed(roamSpeed);
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
        _stationAngle = _orbitAngle;   // first station is wherever it arrived
        _atStation = false;
        _stationDwellEnd = 0f;
        _nextWorkRepath = 0f;

        // Timed stay only if the site asks for one; otherwise work until the goal is reached.
        _workEndTime = (_task && _task.workDuration > 0f) ? Time.time + _task.workDuration : -1f;

        // Register with rate 0: the site only uses the entry to show its "repairing" FX.
        // The actual health change is applied per-frame in DoWork.
        if (_task != null)
            _task.RegisterWorker(this, 0f);

        // Colour cue per command (same palette as before)
        var mgr = NPCManager.Instance;
        if (role == NPCRole.Wanderer)
            ApplyColor(new Color(0.6f, 0.6f, 0.6f));                                // gray
        else if (_command == TaskCommand.Dismantle)
            ApplyColor(new Color(1f, 0.4f, 0.2f));                                  // orange-red
        else
            ApplyColor(mgr ? mgr.taskColor : Color.green);

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

            if (_agent.isOnNavMesh) _agent.isStopped = true;
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.autoBraking = true;
        }
        else
        {
            if (_agent.isOnNavMesh) _agent.isStopped = false;
            _agent.updatePosition = true;
            _agent.updateRotation = false;
            _agent.autoBraking = false;
            _agent.stoppingDistance = 0f;
            SetAgentSpeed(followSpeed);
            _agent.acceleration = Mathf.Max(_agent.acceleration, 16f);
        }

        OnArrivedAtTask?.Invoke(this);
    }

    void ExitWorkToRoam()
    {
        if (_task != null) _task.UnregisterWorker(this);
        RestoreAgentFromWork();
        _task = null;
        _workEndTime = -1f;
        state = NPCState.ReturnToRoam;
        SetAgentSpeed(followSpeed);

        // Pick the return point once; DoReturnToRoam re-issues it only if the path is lost.
        _currentRoamTarget = NPCManager.Instance ? NPCManager.Instance.GetRandomNavPointInside() : transform.position;
        if (_agent.isOnNavMesh && NavMesh.SamplePosition(_currentRoamTarget, out var hit, 2f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);

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
            SetAgentSpeed(_cachedSpeed);
            _agent.acceleration = _cachedAccel;
            if (_agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.ResetPath();
            }
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

        if (_agent.isOnNavMesh && NavMesh.SamplePosition(_currentRoamTarget, out var hit, 2.0f, NavMesh.AllAreas))
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
            {
                // Warp keeps the agent and transform in sync (plain transform moves desync the agent).
                if (!_agent.Warp(hit.position)) transform.position = hit.position;
            }
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

        // Store reference
        _roleIndicator = sphere;

        // remove collider to avoid interference
        Destroy(sphere.GetComponent<Collider>());

        // parent it to NPC
        sphere.transform.SetParent(transform);

        // === 2. Position it above the NPC's head ===
        // Local space throughout: the sphere is a child, so the NPC's own scale is applied once by the
        // hierarchy. (World-space bounds or the NPC's scale here would apply it twice.)
        float height = 2f;
        if (_spriteRenderer != null && _spriteRenderer.sprite != null)
            height = _spriteRenderer.sprite.bounds.max.y + 0.25f; // just above the top of the frame

        sphere.transform.localPosition = new Vector3(0f, height, 0f);

        // === 3. Fixed fraction of the NPC's size (the parent scale does the rest) ===
        sphere.transform.localScale = Vector3.one * 0.56f;

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

        // === 6. Hidden by default - only shown while Daffodil is scanning ===
        sphere.SetActive(false);
    }

}
