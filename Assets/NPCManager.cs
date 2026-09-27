using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NPCManager : MonoBehaviour
{
    public static NPCManager Instance { get; private set; }

    [Header("Prefabs & Spawn Settings")]
    [Tooltip("The Martian (or NPC) prefab to spawn.")]
    public GameObject martianPrefab;

    [Tooltip("Total number of Martians to spawn if custom counts are disabled.")]
    [Min(1)] public int spawnCount = 10;

    [Tooltip("Spawn individual role counts instead of using total spawnCount.")]
    public bool useCustomRoleCounts = true;

    [Header("Role-specific Counts")]
    [Min(0)] public int workerCount = 4;
    [Min(0)] public int attackerCount = 3;
    [Min(0)] public int wandererCount = 3;

    [Tooltip("If true, spawn Martians automatically on Start (skipped when a GameFlowManager drives spawning).")]
    public bool autoSpawnOnStart = true;

    [Header("Roam Area (BoxCollider)")]
    public BoxCollider roamArea;

    [Tooltip("Grows the roam area at startup without editing the scene. The Mycari flock tightly, " +
             "and in a small box they end up shoulder to shoulder and impossible to pick apart. " +
             "1 = leave the box exactly as authored.")]
    [Min(1f)] public float roamAreaScale = 2.5f;

    [Tooltip("Keeps roam targets at least this far apart from each other, so the flock spreads " +
             "out instead of converging on one spot.")]
    [Min(0f)] public float minRoamTargetSpacing = 3f;

    // Roam targets handed out recently, used to keep successive picks apart.
    readonly List<Vector3> _recentRoamTargets = new List<Vector3>();

    [Header("Decision Zone (BoxCollider)")]
    public BoxCollider decisionZone;

    [Header("Global Boids Weights")]
    [Range(0f, 1f)] public float separationWeight = 0.7f;
    [Range(0f, 1f)] public float alignmentWeight = 0.5f;
    [Range(0f, 1f)] public float cohesionWeight = 0.6f;

    [Header("Perf")]
    [Min(1)] public int neighborUpdateStride = 4;

    [Header("State Colors (vibrant)")]
    public Color roamColor = new Color(0.1f, 0.5f, 1f); // Blue
    public Color followColor = new Color(1f, 0.85f, 0.1f); // Yellow
    public Color taskColor = new Color(0.1f, 1f, 0.2f);  // Green
    public Color declineColor = new Color(1f, 0.2f, 0.2f);  // Red

    [Header("Command acceptance (0-1)")]
    [Tooltip("Chance a Fixer accepts a Repair order.")]
    [Range(0f, 1f)] public float workerRepair = 1f;
    [Tooltip("Chance a Fixer accepts a Dismantle order.")]
    [Range(0f, 1f)] public float workerDismantle = 0.25f;
    [Tooltip("Chance a Breaker accepts a Repair order.")]
    [Range(0f, 1f)] public float attackerRepair = 0.25f;
    [Tooltip("Chance a Breaker accepts a Dismantle order.")]
    [Range(0f, 1f)] public float attackerDismantle = 1f;
    [Tooltip("Chance a Wanderer accepts a Repair order.")]
    [Range(0f, 1f)] public float wandererRepair = 0.6f;
    [Tooltip("Chance a Wanderer accepts a Dismantle order.")]
    [Range(0f, 1f)] public float wandererDismantle = 0.6f;

    [Tooltip("How far around a requested SpawnAt position we search for NavMesh.")]
    [Min(0.5f)] public float spawnSampleRadius = 5f;

    private readonly List<NPCFlocker> _all = new List<NPCFlocker>();
    public IReadOnlyList<NPCFlocker> All => _all;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ApplyRoamAreaScale();
    }

    /// <summary>
    /// Widens the roam box at runtime. Done here rather than in the scene so the authored value
    /// stays intact and the spacing can be retuned from the Inspector without scene surgery.
    /// </summary>
    void ApplyRoamAreaScale()
    {
        if (!roamArea || roamAreaScale <= 1.0001f) return;
        Vector3 before = roamArea.size;
        // Only widen the footprint; growing height would let them roam above the terrain.
        roamArea.size = new Vector3(before.x * roamAreaScale, before.y, before.z * roamAreaScale);
        Debug.Log($"[NPCManager] Roam area widened {before} -> {roamArea.size} (x{roamAreaScale}).");
    }

    void Start()
    {
        // The scripted flow spawns Mycari itself (MycariArrives / EnsurePopulation at Sandbox).
        if (GameFlowManager.Instance != null) return;

        if (autoSpawnOnStart && martianPrefab != null)
        {
            if (useCustomRoleCounts)
            {
                SpawnByRoles(workerCount, attackerCount, wandererCount);
            }
            else
            {
                SpawnMartians(spawnCount);
            }
        }
    }

    public void Register(NPCFlocker npc)
    {
        if (npc != null && !_all.Contains(npc))
            _all.Add(npc);
    }

    public void Unregister(NPCFlocker npc)
    {
        _all.Remove(npc);
    }

    /// <summary>Probability (0-1) that a role accepts a command; see the acceptance table.</summary>
    public float GetAcceptChance(NPCRole role, TaskCommand cmd)
    {
        bool repair = cmd == TaskCommand.Repair;
        switch (role)
        {
            case NPCRole.Worker: return repair ? workerRepair : workerDismantle;
            case NPCRole.Attacker: return repair ? attackerRepair : attackerDismantle;
            default: return repair ? wandererRepair : wandererDismantle;
        }
    }

    public Vector3 GetRandomNavPointInside()
    {
        if (!roamArea)
        {
            Debug.LogWarning("No roamArea assigned in NPCManager.");
            return Vector3.zero;
        }

        var center = roamArea.bounds.center;
        var ext = roamArea.bounds.extents;

        // Spread the flock out: prefer a point that is not on top of somewhere we just sent
        // someone else. Falls back to any valid point rather than failing.
        Vector3 fallback = center;
        bool haveFallback = false;

        for (int i = 0; i < 24; i++)
        {
            Vector3 randomOffset = new Vector3(
                Random.Range(-ext.x, ext.x),
                Random.Range(-ext.y, ext.y),
                Random.Range(-ext.z, ext.z)
            );

            Vector3 randomPoint = center + randomOffset;

            if (!NavMesh.SamplePosition(randomPoint, out var hit, 2f, NavMesh.AllAreas)) continue;

            if (!haveFallback) { fallback = hit.position; haveFallback = true; }

            if (minRoamTargetSpacing > 0f && TooCloseToRecent(hit.position)) continue;

            RememberRoamTarget(hit.position);
            return hit.position;
        }

        if (haveFallback) RememberRoamTarget(fallback);
        return fallback;
    }

    bool TooCloseToRecent(Vector3 p)
    {
        float r2 = minRoamTargetSpacing * minRoamTargetSpacing;
        for (int i = 0; i < _recentRoamTargets.Count; i++)
            if ((_recentRoamTargets[i] - p).sqrMagnitude < r2) return true;
        return false;
    }

    void RememberRoamTarget(Vector3 p)
    {
        _recentRoamTargets.Add(p);
        // Only the last handful matter; beyond that the area would saturate and every pick
        // would fall through to the fallback.
        int keep = Mathf.Max(4, _all.Count);
        while (_recentRoamTargets.Count > keep) _recentRoamTargets.RemoveAt(0);
    }

    public bool IsInsideDecisionZone(Vector3 pos)
    {
        return decisionZone && decisionZone.bounds.Contains(pos);
    }

    public bool IsInsideRoamArea(Vector3 pos)
    {
        return roamArea && roamArea.bounds.Contains(pos);
    }

    /// <summary>
    /// Spawns a specified number of Martians inside the roam area (default role: Worker).
    /// </summary>
    public void SpawnMartians(int count)
    {
        if (martianPrefab == null)
        {
            Debug.LogError("No martianPrefab assigned in NPCManager!");
            return;
        }

        if (!roamArea)
        {
            Debug.LogError("No roamArea assigned in NPCManager!");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 spawnPos = GetRandomNavPointInside();
            GameObject npcObj = Instantiate(martianPrefab, spawnPos, Quaternion.identity, transform);

            var flocker = npcObj.GetComponent<NPCFlocker>();
            if (flocker != null)
            {
                flocker.role = NPCRole.Worker;
                Register(flocker);
            }
        }

        Debug.Log($"Spawned {count} Worker Martians in roam area.");
    }

    /// <summary>
    /// Spawns specific counts for each role type.
    /// </summary>
    public void SpawnByRoles(int workers, int attackers, int wanderers)
    {
        if (martianPrefab == null)
        {
            Debug.LogError("No martianPrefab assigned in NPCManager!");
            return;
        }

        if (!roamArea)
        {
            Debug.LogError("No roamArea assigned in NPCManager!");
            return;
        }

        int totalSpawned = 0;

        // Workers
        for (int i = 0; i < workers; i++)
            SpawnSingle(NPCRole.Worker, ref totalSpawned);

        // Attackers
        for (int i = 0; i < attackers; i++)
            SpawnSingle(NPCRole.Attacker, ref totalSpawned);

        // Wanderers
        for (int i = 0; i < wanderers; i++)
            SpawnSingle(NPCRole.Wanderer, ref totalSpawned);

        Debug.Log($"Spawned total {totalSpawned} Martians: {workers} workers, {attackers} attackers, {wanderers} wanderers.");
    }

    /// <summary>
    /// Spawns the regular population (role counts) only if there are no Martians yet.
    /// Used by the flow when the sandbox opens.
    /// </summary>
    public void EnsurePopulation()
    {
        _all.RemoveAll(n => n == null);
        if (_all.Count > 0) return;
        SpawnByRoles(workerCount, attackerCount, wandererCount);
    }

    /// <summary>
    /// Spawns one Martian of the given role at (or near) a world position, snapped to the NavMesh
    /// within <see cref="spawnSampleRadius"/>. The NPC is NOT clamped into the roam area, so the
    /// flow can place it anywhere (e.g. next to the windmill). Returns null if spawning failed.
    /// </summary>
    public NPCFlocker SpawnAt(NPCRole role, Vector3 worldPos)
    {
        if (martianPrefab == null)
        {
            Debug.LogError("No martianPrefab assigned in NPCManager!");
            return null;
        }

        Vector3 pos = worldPos;
        if (NavMesh.SamplePosition(worldPos, out var hit, spawnSampleRadius, NavMesh.AllAreas))
            pos = hit.position;
        else
            Debug.LogWarning($"NPCManager.SpawnAt: no NavMesh within {spawnSampleRadius} m of {worldPos}; spawning at the raw position.");

        GameObject npcObj = Instantiate(martianPrefab, pos, Quaternion.identity, transform);

        var flocker = npcObj.GetComponent<NPCFlocker>();
        if (flocker == null)
        {
            Debug.LogError("NPCManager.SpawnAt: martianPrefab has no NPCFlocker component.");
            return null;
        }

        flocker.role = role;
        flocker.clampSpawnToRoamArea = false;
        Register(flocker);
        return flocker;
    }

    /// <summary>
    /// Nearest registered Martian to <paramref name="from"/>, optionally restricted to a role and
    /// an extra predicate. Returns null when nothing matches.
    /// </summary>
    public NPCFlocker FindNearest(NPCRole? role, Vector3 from, System.Func<NPCFlocker, bool> filter = null)
    {
        NPCFlocker best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < _all.Count; i++)
        {
            var npc = _all[i];
            if (npc == null) continue;
            if (role.HasValue && npc.role != role.Value) continue;
            if (filter != null && !filter(npc)) continue;

            float sqr = (npc.transform.position - from).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = npc;
            }
        }

        return best;
    }

    private void SpawnSingle(NPCRole role, ref int counter)
    {
        Vector3 spawnPos = GetRandomNavPointInside();
        GameObject npcObj = Instantiate(martianPrefab, spawnPos, Quaternion.identity, transform);

        var flocker = npcObj.GetComponent<NPCFlocker>();
        if (flocker != null)
        {
            flocker.role = role;
            Register(flocker);
        }

        counter++;
    }
}
