using System;
using UnityEngine;

/// <summary>
/// Colony life support. Rises while the windmill is healthy and drains while it is broken;
/// hitting zero is the game-over condition. Only ticks while <see cref="active"/> is set,
/// so the scripted intro can leave it frozen.
/// </summary>
[DisallowMultipleComponent]
public class LifeSupport : MonoBehaviour
{
    public static LifeSupport Instance { get; private set; }

    [Header("Values")]
    public float max = 100f;
    public float value = 100f;
    [Tooltip("Ticks only while true. The flow enables this once the sandbox opens.")]
    public bool active = false;

    [Header("Rates (per second)")]
    [Tooltip("Gain at full power (windmill health 100%).")]
    public float regenPerSecondPowered = 2f;
    [Tooltip("Loss at zero power (windmill health 0%).")]
    public float drainPerSecondUnpowered = 6f;
    [Tooltip("OnLow fires when the value crosses below this fraction.")]
    [Range(0f, 1f)] public float lowThreshold01 = 0.25f;

    [Header("Power")]
    [Tooltip("Site whose health drives the rate. Auto: the first PowerPlant TaskSite.")]
    [SerializeField] TaskSite powerSource;

    /// <summary>Fired once when the value reaches 0; re-armed when it rises above 0 again.</summary>
    public event Action OnDepleted;
    /// <summary>Fired when the value crosses below <see cref="lowThreshold01"/>; re-armed on recovery.</summary>
    public event Action OnLow;
    /// <summary>Fired whenever the value changes, with the new 0..1 fraction.</summary>
    public event Action<float> OnChanged;

    public float Value => value;
    public float Max => max;
    public bool Active => active;
    public float Value01 => max > 0f ? Mathf.Clamp01(value / max) : 0f;
    /// <summary>Power available, 0..1 (windmill health, or 1 when there is no power source).</summary>
    public float PowerLevel01 => powerSource ? Mathf.Clamp01(powerSource.Health01) : 1f;
    public TaskSite PowerSource => powerSource;
    public void SetPowerSource(TaskSite site) => powerSource = site;

    /// <summary>Current change per second given the power level (positive = regen).</summary>
    public float RatePerSecond
    {
        get
        {
            float p = PowerLevel01;
            // p = 0 → full drain, p = 0.5 → neutral, p = 1 → full regen.
            if (p >= 0.5f) return regenPerSecondPowered * ((p - 0.5f) / 0.5f);
            return -drainPerSecondUnpowered * (1f - p / 0.5f);
        }
    }

    bool _depletedArmed = true;
    bool _lowArmed = true;
    float _nextSourceSearch;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[LifeSupport] Duplicate instance; keeping the first one.", this);
            enabled = false;
            return;
        }
        Instance = this;
        value = Mathf.Clamp(value, 0f, max);
        _depletedArmed = value > 0f;
        _lowArmed = Value01 >= lowThreshold01;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        TryFindPowerSource();
    }

    void Update()
    {
        if (powerSource == null && Time.time >= _nextSourceSearch) TryFindPowerSource();
        if (!active) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float next = Mathf.Clamp(value + RatePerSecond * dt, 0f, max);
        if (Mathf.Approximately(next, value)) return;
        value = next;
        AfterValueChanged();
    }

    /// <summary>Sets the value directly (checkpoint restore, debugging). Fires the usual events.</summary>
    public void Set(float v)
    {
        float next = Mathf.Clamp(v, 0f, max);
        if (Mathf.Approximately(next, value)) return;
        value = next;
        AfterValueChanged();
    }

    public void Refill() => Set(max);

    public void SetActive(bool v) => active = v;

    void TryFindPowerSource()
    {
        // Cheap list scan on TaskManager; never a scene-wide FindObjectOfType per frame.
        _nextSourceSearch = Time.time + 1f;
        var tm = TaskManager.Instance;
        if (tm == null) return;
        powerSource = tm.FindByType(TaskType.PowerPlant);
    }

    void AfterValueChanged()
    {
        float v01 = Value01;
        OnChanged?.Invoke(v01);

        // Low: edge-triggered on the way down, re-armed once back at or above the threshold.
        if (v01 < lowThreshold01)
        {
            if (_lowArmed)
            {
                _lowArmed = false;
                OnLow?.Invoke();
            }
        }
        else
        {
            _lowArmed = true;
        }

        // Depleted: once per trip to zero.
        if (value <= 0f)
        {
            if (_depletedArmed)
            {
                _depletedArmed = false;
                OnDepleted?.Invoke();
            }
        }
        else
        {
            _depletedArmed = true;
        }
    }
}
