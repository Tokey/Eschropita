using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class FollowPlayer : MonoBehaviour
{
    [Header("Target")]
    public Transform player;
    public float stopDistance = 2f;

    [Header("Energy")]
    public float maxEnergy = 100f;
    [SerializeField] public float energy = 100f;

    [Tooltip("Drain per second while scanning (HOLD V)")]
    public float scanDrainPerSecond = 35f;

    [Tooltip("Drain per second while following/moving")]
    public float followDrainPerSecond = 6f;

    [Tooltip("Drain per second while awake but stationary")]
    public float idleDrainPerSecond = 2f;

    [Tooltip("Regen per second when energy is 0 (and sleeping)")]
    public float sleepRegenPerSecond = 12f;

    [Tooltip("Regen per second when awake (optional small regen, set to 0 if you want none)")]
    public float awakeRegenPerSecond = 0f;

    [Header("Scan")]
    public float scanRadius = 8f;
    [Tooltip("How often (seconds) the scan 'pulse' happens while holding V")]
    public float scanPulseInterval = 0.2f;
    public LayerMask scanMask = ~0;

    private NavMeshAgent agent;

    private bool isSleeping;
    private bool isScanning;
    private float nextScanPulseTime;

    // UIManager reads these
    public float Energy => energy;
    public float MaxEnergy => maxEnergy;
    public bool IsSleeping => isSleeping;
    public bool IsScanning => isScanning;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        energy = Mathf.Clamp(energy, 0f, maxEnergy);
    }

    void Update()
    {
        if (player == null || agent == null) return;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // --- If sleeping (energy was 0) ---
        if (isSleeping)
        {
            agent.isStopped = true;

            // regen until full
            energy = Mathf.Clamp(energy + sleepRegenPerSecond * dt, 0f, maxEnergy);

            if (energy >= maxEnergy - 0.001f)
                isSleeping = false;

            return;
        }

        // HOLD V to scan
        bool vHeld = Input.GetKey(KeyCode.V);

        // Start/stop scanning based on hold
        if (vHeld && energy > 0.001f)
        {
            isScanning = true;

            // scan pulses while held
            if (Time.time >= nextScanPulseTime)
            {
                DoScanOnce();
                nextScanPulseTime = Time.time + Mathf.Max(0.01f, scanPulseInterval);
            }
        }
        else
        {
            isScanning = false;
        }

        // --- Movement / follow logic (disabled while scanning) ---
        if (!isScanning)
        {
            float dist = Vector3.Distance(transform.position, player.position);

            if (energy <= 0.001f)
            {
                GoToSleep();
                return;
            }

            if (dist > stopDistance)
            {
                agent.isStopped = false;
                agent.SetDestination(player.position);
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
        {
            GoToSleep();
            return;
        }
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

    private void DoScanOnce()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, scanRadius, scanMask);

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].transform == transform) continue;
            Debug.Log($"[Daffodil Scan] Found: {hits[i].name}");
        }
    }

    private void GoToSleep()
    {
        energy = 0f;
        isSleeping = true;
        isScanning = false;

        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, scanRadius);
    }
}
