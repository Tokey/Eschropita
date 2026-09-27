using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class FollowPlayer : MonoBehaviour
{
    public static FollowPlayer Instance { get; private set; }

    [Header("Target")]
    public Transform player;
    public float stopDistance = 2f;

    [Header("Energy")]
    public float maxEnergy = 100f;
    [SerializeField] public float energy = 100f;

    [Tooltip("Drain per second while scanning (HOLD V)")]
    public float scanDrainPerSecond = 35f;

    [Tooltip("Drain per second while following/moving")]
    public float followDrainPerSecond = 0.4f;

    [Tooltip("Drain per second while awake but stationary")]
    public float idleDrainPerSecond = 0.2f;

    [Tooltip("Regen per second when energy is 0 (and sleeping). Scaled by base power: " +
             "35% of this with a dead windmill, 100% with a healthy one.")]
    public float sleepRegenPerSecond = 12f;

    [Tooltip("Regen per second when awake (optional small regen, set to 0 if you want none)")]
    public float awakeRegenPerSecond = 0f;

    [Header("Scan")]
    public float scanRadius = 8f;
    [Tooltip("How often (seconds) the scan 'pulse' happens while holding V")]
    public float scanPulseInterval = 0.2f;
    public LayerMask scanMask = ~0;

    [Tooltip("The scripted flow clears this during cutscenes so V does nothing.")]
    public bool scanEnabled = true;

    [Header("Follow")]
    [Tooltip("Only re-path when the player has moved at least this far from the last destination.")]
    public float repathDistance = 0.5f;

    // Scan label colours (match the HUD palette).
    static readonly Color FixerColor = new Color32(0x7F, 0xD8, 0xA6, 0xFF);
    static readonly Color BreakerColor = new Color32(0xE8, 0x6A, 0x5C, 0xFF);
    static readonly Color WandererColor = new Color32(0x8F, 0xB4, 0xE8, 0xFF);
    static readonly Color GoodColor = new Color32(0x7F, 0xD8, 0xA6, 0xFF);
    static readonly Color WarnColor = new Color32(0xE8, 0xC1, 0x5C, 0xFF);
    static readonly Color BadColor = new Color32(0xE8, 0x6A, 0x5C, 0xFF);
    static readonly Color AmberColor = new Color32(0xE8, 0xA8, 0x5C, 0xFF);
    static readonly Color VioletColor = new Color32(0x8A, 0x5C, 0xFF, 0xFF);

    private NavMeshAgent agent;

    private bool isSleeping;
    private bool isScanning;
    private float nextScanPulseTime;

    // Follow throttling: last point we actually handed to the agent.
    private Vector3 _lastDestination;
    private bool _hasDestination;

    // Scan results, rebuilt every pulse. The HUD draws one world label per entry.
    private readonly List<ScanHit> _hits = new List<ScanHit>();
    private readonly HashSet<Component> _seen = new HashSet<Component>(); // dedupe per object per pulse
    public IReadOnlyList<ScanHit> ScannedTargets => _hits;

    // HUD / flow read these
    public float Energy => energy;
    public float MaxEnergy => maxEnergy;
    public bool IsSleeping => isSleeping;
    public bool IsScanning => isScanning;

    /// <summary>Seconds spent scanning since the scene started (tutorial gating).</summary>
    public float TotalScanSeconds { get; private set; }
    public bool HasEverScanned { get; private set; }

    public event System.Action OnScanStarted;
    public event System.Action OnScanStopped;
    public event System.Action OnFellAsleep;
    public event System.Action OnWokeUp;
    public event System.Action<float> OnEnergyChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("[FollowPlayer] More than one Daffodil in the scene; using the newest.");
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        energy = Mathf.Clamp(energy, 0f, maxEnergy);

        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }
    }

    void Update()
    {
        if (player == null || agent == null) return;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float energyBefore = energy;

        // --- If sleeping (energy was 0) ---
        if (isSleeping)
        {
            agent.isStopped = true;

            // regen until full; a weak base power slows the recharge
            float power = LifeSupport.Instance != null ? LifeSupport.Instance.PowerLevel01 : 1f;
            float regen = sleepRegenPerSecond * (0.35f + 0.65f * Mathf.Clamp01(power));
            energy = Mathf.Clamp(energy + regen * dt, 0f, maxEnergy);

            if (energy >= maxEnergy - 0.001f)
                WakeUp();

            NotifyEnergy(energyBefore);
            return;
        }

        // HOLD V to scan (blocked while the flow owns the player, e.g. cutscenes)
        var flow = GameFlowManager.Instance;
        bool scanAllowed = scanEnabled && (flow == null || flow.PlayerControlEnabled);
        bool vHeld = scanAllowed && Input.GetKey(KeyCode.V);

        // Start/stop scanning based on hold
        if (vHeld && energy > 0.001f)
        {
            SetScanning(true);
            TotalScanSeconds += dt;

            // scan pulses while held
            if (Time.time >= nextScanPulseTime)
            {
                DoScanOnce();
                nextScanPulseTime = Time.time + Mathf.Max(0.01f, scanPulseInterval);
            }
        }
        else
        {
            SetScanning(false);
        }

        // --- Movement / follow logic (disabled while scanning) ---
        if (!isScanning)
        {
            float dist = Vector3.Distance(transform.position, player.position);

            if (energy <= 0.001f)
            {
                GoToSleep();
                NotifyEnergy(energyBefore);
                return;
            }

            if (dist > stopDistance)
            {
                agent.isStopped = false;
                UpdateDestination(player.position);
            }
            else
            {
                agent.isStopped = true;
            }
        }
        else
        {
            // while scanning, stay put
            agent.isStopped = true;
        }

        // --- Energy drain / regen while awake ---
        ApplyEnergyLogic(dt);

        // --- If we hit 0 during this frame -> sleep ---
        if (energy <= 0.001f)
            GoToSleep();

        NotifyEnergy(energyBefore);
    }

    /// <summary>Only re-paths when the target moved noticeably; SetDestination every frame is wasteful.</summary>
    private void UpdateDestination(Vector3 target)
    {
        if (_hasDestination && agent.hasPath
            && (target - _lastDestination).sqrMagnitude < repathDistance * repathDistance)
            return;

        _lastDestination = target;
        _hasDestination = true;
        agent.SetDestination(target);
    }

    private void ApplyEnergyLogic(float dt)
    {
        bool isMoving = agent.velocity.sqrMagnitude > 0.05f * 0.05f;

        if (isScanning)
        {
            // rapid drain while holding V
            energy = Mathf.Clamp(energy - scanDrainPerSecond * dt, 0f, maxEnergy);
            return;
        }

        if (isMoving)
        {
            energy = Mathf.Clamp(energy - followDrainPerSecond * dt, 0f, maxEnergy);
        }
        else
        {
            energy = Mathf.Clamp(energy - idleDrainPerSecond * dt, 0f, maxEnergy);
        }

        if (awakeRegenPerSecond > 0f)
            energy = Mathf.Clamp(energy + awakeRegenPerSecond * dt, 0f, maxEnergy);
    }

    private void NotifyEnergy(float before)
    {
        if (!Mathf.Approximately(before, energy))
            OnEnergyChanged?.Invoke(energy);
    }

    // ------------------------------------------------------------------ scanning

    private void SetScanning(bool on)
    {
        if (isScanning == on) return;
        isScanning = on;

        if (on)
        {
            HasEverScanned = true;
            nextScanPulseTime = 0f; // pulse immediately so labels appear on the first frame
            OnScanStarted?.Invoke();
        }
        else
        {
            _hits.Clear();
            OnScanStopped?.Invoke();
        }
    }

    private void DoScanOnce()
    {
        _hits.Clear();
        _seen.Clear();
        bool sawObelisk = false;

        // Triggers included on purpose: shards and the bunker hatch are trigger volumes.
        Collider[] hits = Physics.OverlapSphere(transform.position, scanRadius, scanMask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;
            if (t == transform || t.IsChildOf(transform)) continue;                      // Daffodil herself
            if (player != null && (t == player || t.IsChildOf(player))) continue;         // the player
            if (hits[i].CompareTag("Player")) continue;

            // Mycari
            var npc = hits[i].GetComponentInParent<NPCFlocker>();
            if (npc != null)
            {
                if (_seen.Add(npc)) _hits.Add(MakeMycariHit(npc));
                continue;
            }

            // Obelisk shard (child trigger of nothing else, so check before sites)
            var shard = hits[i].GetComponentInParent<ObeliskShard>();
            if (shard != null)
            {
                if (_seen.Add(shard)) _hits.Add(shard.ToScanHit());
                continue;
            }

            // Bunker hatch (child of the obelisk, so it must win over the TaskSite lookup)
            var bunker = hits[i].GetComponentInParent<BunkerTrigger>();
            if (bunker != null)
            {
                if (_seen.Add(bunker)) _hits.Add(MakeBunkerHit(bunker));
                continue;
            }

            // Task sites (windmill, obelisk...)
            var site = hits[i].GetComponentInParent<TaskSite>();
            if (site != null)
            {
                if (!_seen.Add(site)) continue;

                if (site.type == TaskType.Obelisk)
                {
                    sawObelisk = true;
                    var obelisk = site.GetComponent<Obelisk>();
                    _hits.Add(obelisk != null ? obelisk.ToScanHit() : MakeObeliskFallbackHit(site));
                }
                else
                {
                    _hits.Add(MakeSiteHit(site));
                }
            }
        }

        if (sawObelisk && GameFlowManager.Instance != null)
            GameFlowManager.Instance.NotifyObeliskScanned();
    }

    private static ScanHit MakeMycariHit(NPCFlocker npc)
    {
        Color c;
        switch (npc.role)
        {
            case NPCRole.Worker: c = FixerColor; break;
            case NPCRole.Attacker: c = BreakerColor; break;
            default: c = WandererColor; break;
        }

        return new ScanHit
        {
            target = npc.transform,
            kind = ScanKind.Mycari,
            label = (npc.RoleLabel ?? "MYCARI").ToUpperInvariant(),
            detail = npc.StateLabel ?? "",
            value01 = -1f,
            color = c
        };
    }

    private static ScanHit MakeSiteHit(TaskSite site)
    {
        float h = site.Health01;
        Color c = h >= 0.66f ? GoodColor : (h >= 0.33f ? WarnColor : BadColor);

        string name = string.IsNullOrWhiteSpace(site.displayName)
            ? (site.type == TaskType.PowerPlant ? "Windmill" : site.type.ToString())
            : site.displayName;

        return new ScanHit
        {
            target = site.transform,
            kind = ScanKind.Site,
            label = name.ToUpperInvariant(),
            detail = $"{Mathf.RoundToInt(h * 100f)}%",
            value01 = h,
            color = c
        };
    }

    // Used only if an Obelisk-type TaskSite has no Obelisk component attached.
    private static ScanHit MakeObeliskFallbackHit(TaskSite site)
    {
        bool shielded = site.shield > 0f;
        return new ScanHit
        {
            target = site.transform,
            kind = ScanKind.Obelisk,
            label = "OBELISK",
            detail = shielded ? "Shielded" : (site.IsDestroyed ? "Destroyed" : "Exposed"),
            value01 = shielded ? site.Shield01 : site.Health01,
            color = VioletColor
        };
    }

    private static ScanHit MakeBunkerHit(BunkerTrigger bunker)
    {
        return new ScanHit
        {
            target = bunker.transform,
            kind = ScanKind.Bunker,
            label = "BUNKER",
            detail = "",
            value01 = -1f,
            color = AmberColor
        };
    }

    // ------------------------------------------------------------------ sleep / energy API

    private void GoToSleep()
    {
        bool wasAwake = !isSleeping;

        energy = 0f;
        isSleeping = true;
        SetScanning(false);

        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
        _hasDestination = false;

        if (wasAwake) OnFellAsleep?.Invoke();
    }

    /// <summary>Ends the nap early (checkpoint restore / refill). No-op with zero energy.</summary>
    public void WakeUp()
    {
        if (!isSleeping || energy <= 0.001f) return;

        isSleeping = false;
        if (agent != null) agent.isStopped = false;
        OnWokeUp?.Invoke();
    }

    public void SetEnergy(float value)
    {
        float before = energy;
        energy = Mathf.Clamp(value, 0f, maxEnergy);
        NotifyEnergy(before);
    }

    public void Refill()
    {
        SetEnergy(maxEnergy);
        WakeUp();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, scanRadius);
    }
}
