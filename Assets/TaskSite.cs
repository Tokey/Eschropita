using System;
using System.Collections.Generic;
using UnityEngine;

public enum TaskType { PowerPlant, Farm, Dome, Obelisk }

/// <summary>
/// A repairable / dismantlable structure. Health is changed by Mycari (NPCFlocker.DoWork), the tornado
/// (ApplyDamage) and the player (AddShield). Its state is drawn by the scan overlay (GameHUD); the site
/// itself no longer owns any world-space UI.
/// </summary>
public class TaskSite : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Shown by the scan overlay and in toasts. Leave empty to derive it from the type (PowerPlant → \"Windmill\").")]
    public string displayName;

    [Header("Task")]
    public TaskType type;
    public float workRadius = 2.5f;
    [Tooltip("How long a Mycari works here before leaving. <= 0 = until the job is done (fully repaired / destroyed).")]
    public float workDuration = 8f;

    [Header("Health")]
    public float maxHealth = 100f;
    public float health = 0f;
    public float repairPerWorkerPerSecond = 6f;
    [Tooltip("Start at 0 health instead of the 50% fallback that is applied when health is left at 0.")]
    public bool startDepleted = false;

    [Header("Shield")]
    [Tooltip("0 = this site cannot hold a shield (unless canBuildShield is set, which defaults it to 100 in Awake).")]
    public float maxShield = 0f;
    public float shield = 0f;

    [Header("Rules")]
    [Tooltip("Mycari may be ordered to repair this site.")]
    public bool allowRepair = true;
    [Tooltip("Mycari may be ordered to dismantle this site.")]
    public bool allowDismantle = true;
    [Tooltip("The tornado damages this site while overlapping it.")]
    public bool tornadoCanDamage = true;
    [Tooltip("The player can spend shards here to add shield.")]
    public bool canBuildShield = false;
    public int shardsPerShield = 3;
    public float shieldPerBuild = 60f;

    [Header("Turbine (optional)")]
    [Tooltip("Spins proportionally to health so a dead windmill stops. Auto-found in children when empty.")]
    [SerializeField] TurbineRotator turbine;

    [Header("Proximity Effect (GameObject toggle)")]
    [Tooltip("This GameObject will be enabled when PLAYER is in range.")]
    public GameObject proximityObject;

    [Header("Repairing Effect (GameObject toggle)")]
    [Tooltip("Shown while there is at least one worker.")]
    public GameObject repairingObject;

    // === Events (edge-triggered; a checkpoint restore via SetState only raises OnHealthChanged) ===
    /// <summary>Raised whenever health or shield changes.</summary>
    public event Action<TaskSite> OnHealthChanged;
    /// <summary>Raised once when health crosses down to 0.</summary>
    public event Action<TaskSite> OnDepleted;
    /// <summary>Raised once when health crosses up to maxHealth.</summary>
    public event Action<TaskSite> OnFullyRepaired;
    /// <summary>Raised once when shield crosses down to 0.</summary>
    public event Action<TaskSite> OnShieldBroken;

    public float Health01 => maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;
    public float Shield01 => maxShield > 0f ? Mathf.Clamp01(shield / maxShield) : 0f;
    public bool IsDestroyed => health <= 0f;

    /// <summary>displayName, or the type-derived fallback when the field is empty.</summary>
    public string DisplayName => string.IsNullOrEmpty(displayName) ? DefaultNameFor(type) : displayName;

    const float DefaultShieldCapacity = 100f; // used when canBuildShield is set but maxShield was left at 0

    Transform _player;
    float _turbineBaseSpeed;

    // === Contributor model (FX presence only) ===
    // Each entry is (worker → rate). Rates are kept for backwards compatibility but are NOT applied to
    // health any more: NPCFlocker.DoWork calls AddHealth / ApplyDamage directly, so applying the rates
    // here as well would double-count the work. The dictionary only drives the repairing FX toggle.
    readonly Dictionary<object, float> _contributors = new();

    void Awake()
    {
        if (string.IsNullOrEmpty(displayName))
            displayName = DefaultNameFor(type);

        // Legacy fallback: an unset health starts half repaired, unless the site is meant to start dead.
        if (health <= 0f && !startDepleted)
            health = maxHealth * 0.5f;
        health = Mathf.Clamp(health, 0f, maxHealth);

        if (canBuildShield && maxShield <= 0f)
            maxShield = DefaultShieldCapacity;
        shield = maxShield > 0f ? Mathf.Clamp(shield, 0f, maxShield) : 0f;

        if (turbine == null)
            turbine = GetComponentInChildren<TurbineRotator>(true);
        if (turbine != null)
            _turbineBaseSpeed = turbine.rotationSpeed;
    }

    void OnEnable()
    {
        // Self-register so sites created at runtime (e.g. the obelisk) are found; the manager dedupes.
        if (TaskManager.Instance != null)
            TaskManager.Instance.Register(this);
    }

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p) _player = p.transform;

        if (proximityObject) proximityObject.SetActive(false);
        if (repairingObject) repairingObject.SetActive(false);
    }

    void Update()
    {
        // Toggle proximity object
        if (_player && proximityObject)
        {
            bool inRange = Vector3.Distance(_player.position, transform.position) <= workRadius;
            if (proximityObject.activeSelf != inRange)
                proximityObject.SetActive(inRange);
        }

        // Toggle repairing/damaging object (active if any contributors exist). Rates are ignored, see _contributors.
        if (repairingObject)
        {
            bool active = _contributors.Count > 0;
            if (repairingObject.activeSelf != active)
                repairingObject.SetActive(active);
        }

        // Turbine spins with health so a broken windmill visibly winds down.
        if (turbine != null)
            turbine.rotationSpeed = _turbineBaseSpeed * Health01;
    }

    void OnDisable()
    {
        if (proximityObject) proximityObject.SetActive(false);
        if (repairingObject) repairingObject.SetActive(false);
        _contributors.Clear();

        if (TaskManager.Instance != null)
            TaskManager.Instance.Unregister(this);
    }

    // === NPC API ===
    public void RegisterWorker(object worker, float ratePerSecond)
    {
        if (worker == null) return;
        _contributors[worker] = ratePerSecond;
    }

    public void UpdateWorkerRate(object worker, float ratePerSecond)
    {
        if (worker == null) return;
        if (_contributors.ContainsKey(worker))
            _contributors[worker] = ratePerSecond;
    }

    public void UnregisterWorker(object worker)
    {
        if (worker == null) return;
        _contributors.Remove(worker);
    }

    // === Health / shield API ===

    /// <summary>Positive = repair (goes straight to health). Negative delegates to ApplyDamage (shield first).</summary>
    public void AddHealth(float amount)
    {
        if (amount > 0f) SetHealthInternal(health + amount);
        else if (amount < 0f) ApplyDamage(-amount);
    }

    /// <summary>Damage is absorbed by the shield first; whatever is left comes off health.</summary>
    public void ApplyDamage(float amount)
    {
        if (amount <= 0f) return;

        if (shield > 0f)
        {
            float absorbed = Mathf.Min(shield, amount);
            shield -= absorbed;
            amount -= absorbed;
            OnHealthChanged?.Invoke(this);
            if (shield <= 0f)
            {
                shield = 0f;
                OnShieldBroken?.Invoke(this);
            }
        }

        if (amount > 0f)
            SetHealthInternal(health - amount);
    }

    /// <summary>Adds shield, clamped to maxShield. Sites that cannot hold a shield ignore the call.</summary>
    public void AddShield(float amount)
    {
        if (amount <= 0f) return;
        if (maxShield <= 0f && canBuildShield)
            maxShield = DefaultShieldCapacity;
        if (maxShield <= 0f) return;

        float next = Mathf.Clamp(shield + amount, 0f, maxShield);
        if (Mathf.Approximately(next, shield)) return;
        shield = next;
        OnHealthChanged?.Invoke(this);
    }

    /// <summary>Checkpoint restore: sets both values directly. Only OnHealthChanged fires, so no flow beat is re-triggered.</summary>
    public void SetState(float newHealth, float newShield)
    {
        health = Mathf.Clamp(newHealth, 0f, maxHealth);
        shield = maxShield > 0f ? Mathf.Clamp(newShield, 0f, maxShield) : 0f;
        OnHealthChanged?.Invoke(this);
    }

    // Central health write: clamps and raises the edge events exactly once per crossing.
    void SetHealthInternal(float next)
    {
        next = Mathf.Clamp(next, 0f, maxHealth);
        if (Mathf.Approximately(next, health)) return;

        float prev = health;
        health = next;
        OnHealthChanged?.Invoke(this);

        if (prev > 0f && health <= 0f)
            OnDepleted?.Invoke(this);
        else if (prev < maxHealth && health >= maxHealth)
            OnFullyRepaired?.Invoke(this);
    }

    static string DefaultNameFor(TaskType t) => t == TaskType.PowerPlant ? "Windmill" : t.ToString();

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, workRadius);
    }
}
