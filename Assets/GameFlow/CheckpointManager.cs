using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Keeps one checkpoint snapshot of everything the sandbox can change and restores it after a
/// game over. The phase is stored but never applied here; GameFlowManager reads
/// <see cref="Last"/>.phase and re-enters it itself.
/// </summary>
[DisallowMultipleComponent]
public class CheckpointManager : MonoBehaviour
{
    public static CheckpointManager Instance { get; private set; }

    public struct Snapshot
    {
        public GamePhase phase;
        public Vector3 playerPos;
        public float daffodilEnergy;
        public float lifeSupport;
        public int shards;
        public List<(TaskSite site, float health, float shield)> sites;
        public ObeliskStage obeliskStage;
        public string label;
        public bool valid;
    }

    [Header("Debug")]
    [SerializeField] bool logCheckpoints = true;

    public bool HasCheckpoint { get; private set; }
    public Snapshot Last { get; private set; }

    public event Action OnSaved;
    public event Action OnRestored;

    // Found lazily (never per frame); re-resolved when the reference dies.
    PlayerController _player;
    Obelisk _obelisk;
    TornadoCalamity _tornado;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CheckpointManager] Duplicate instance; keeping the first one.", this);
            enabled = false;
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ save

    public void Save(string label)
    {
        ResolveRefs();

        var snap = new Snapshot
        {
            label = label ?? "",
            valid = true,
            phase = GameFlowManager.Instance != null ? GameFlowManager.Instance.Phase : GamePhase.Sandbox,
            playerPos = _player ? _player.transform.position : Vector3.zero,
            shards = _player ? _player.Shards : 0,
            daffodilEnergy = FollowPlayer.Instance ? FollowPlayer.Instance.Energy : 0f,
            lifeSupport = LifeSupport.Instance ? LifeSupport.Instance.Value : 0f,
            obeliskStage = _obelisk ? _obelisk.Stage : ObeliskStage.Shielded,
            sites = new List<(TaskSite site, float health, float shield)>()
        };

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            var sites = tm.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                if (s == null) continue;
                snap.sites.Add((s, s.health, s.shield));
            }
        }

        Last = snap;
        HasCheckpoint = true;
        if (logCheckpoints) Debug.Log($"[CheckpointManager] Saved '{snap.label}' (phase {snap.phase}, {snap.sites.Count} sites).");
        OnSaved?.Invoke();
    }

    // ------------------------------------------------------------------ restore

    /// <summary>
    /// Puts the world back to <see cref="Last"/>. Does not change the game phase.
    /// Safe to call with no checkpoint (logs and returns).
    /// </summary>
    public void Restore()
    {
        if (!HasCheckpoint || !Last.valid)
        {
            Debug.LogWarning("[CheckpointManager] Restore called without a checkpoint.");
            return;
        }

        ResolveRefs();
        Snapshot snap = Last;

        // Player: CharacterController overrides transform writes unless it is disabled first.
        if (_player)
        {
            var cc = _player.GetComponent<CharacterController>();
            bool hadCc = cc != null && cc.enabled;
            if (hadCc) cc.enabled = false;
            _player.transform.position = snap.playerPos;
            Physics.SyncTransforms();
            if (hadCc) cc.enabled = true;

            _player.SetShards(snap.shards);
            _player.DismissFollower();
        }

        // Daffodil: energy back, awake, and standing beside the player again.
        var daffodil = FollowPlayer.Instance;
        if (daffodil)
        {
            daffodil.SetEnergy(snap.daffodilEnergy);
            daffodil.WakeUp();
            if (_player) WarpAgentNear(daffodil.GetComponent<NavMeshAgent>(), snap.playerPos);
        }

        if (LifeSupport.Instance) LifeSupport.Instance.Set(snap.lifeSupport);

        // Sites first, then the obelisk so its stage wins over raw numbers for its own site.
        if (snap.sites != null)
        {
            for (int i = 0; i < snap.sites.Count; i++)
            {
                var entry = snap.sites[i];
                if (entry.site == null) continue;
                entry.site.SetState(entry.health, entry.shield);
            }
        }
        if (_obelisk) _obelisk.RestoreStage(snap.obeliskStage);

        // Mycari drop whatever they were doing, unless the flow is puppeteering them.
        var all = NPCManager.Instance ? NPCManager.Instance.All : null;
        if (all != null)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var npc = all[i];
                if (npc == null || npc.IsScripted) continue;
                npc.SwitchToRoam();
            }
        }
        else
        {
            var found = FindObjectsByType<NPCFlocker>(FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
                if (!found[i].IsScripted) found[i].SwitchToRoam();
        }

        if (_tornado) _tornado.ForceStop();

        if (logCheckpoints) Debug.Log($"[CheckpointManager] Restored '{snap.label}' (phase {snap.phase}).");
        OnRestored?.Invoke();
    }

    // ------------------------------------------------------------------ helpers

    void ResolveRefs()
    {
        if (_player == null)
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged) _player = tagged.GetComponent<PlayerController>();
            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
        }
        if (_obelisk == null) _obelisk = FindAnyObjectByType<Obelisk>();
        if (_tornado == null) _tornado = FindAnyObjectByType<TornadoCalamity>();
    }

    static void WarpAgentNear(NavMeshAgent agent, Vector3 anchor)
    {
        if (agent == null) return;
        Vector3 target = anchor + new Vector3(1.5f, 0f, -1f);
        if (NavMesh.SamplePosition(target, out var hit, 4f, NavMesh.AllAreas))
            target = hit.position;
        else if (NavMesh.SamplePosition(anchor, out hit, 4f, NavMesh.AllAreas))
            target = hit.position;
        else
            return; // no mesh nearby: leave her where she is
        if (agent.Warp(target) && agent.isOnNavMesh) agent.ResetPath();
    }
}
