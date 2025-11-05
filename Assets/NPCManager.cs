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

    [Tooltip("If true, spawn Martians automatically on Start.")]
    public bool autoSpawnOnStart = true;

    [Header("Roam Area (BoxCollider)")]
    public BoxCollider roamArea;

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
    }

    void Start()
    {
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
        if (!_all.Contains(npc))
            _all.Add(npc);
    }

    public void Unregister(NPCFlocker npc)
    {
        _all.Remove(npc);
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

        for (int i = 0; i < 16; i++)
        {
            Vector3 randomOffset = new Vector3(
                Random.Range(-ext.x, ext.x),
                Random.Range(-ext.y, ext.y),
                Random.Range(-ext.z, ext.z)
            );

            Vector3 randomPoint = center + randomOffset;

            if (NavMesh.SamplePosition(randomPoint, out var hit, 2f, NavMesh.AllAreas))
                return hit.position;
        }

        return center; // fallback
    }

    public bool IsInsideDecisionZone(Vector3 pos)
    {
        return decisionZone && decisionZone.bounds.Contains(pos);
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
