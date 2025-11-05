using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum TaskType { PowerPlant, Farm, Dome }

public class TaskSite : MonoBehaviour
{
    [Header("Task")]
    public TaskType type;
    public float workRadius = 2.5f;
    public float workDuration = 8f;

    [Header("Health")]
    public float maxHealth = 100f;
    public float health = 0f;
    public float repairPerWorkerPerSecond = 6f;

    [Header("Proximity Effect (GameObject toggle)")]
    [Tooltip("This GameObject will be enabled when PLAYER is in range.")]
    public GameObject proximityObject;

    [Header("Repairing Effect (GameObject toggle)")]
    [Tooltip("Shown while there is at least one worker.")]
    public GameObject repairingObject;

    [Header("Optional world-space health UI")]
    public Canvas worldCanvasPrefab;
    public Vector3 uiOffset = new Vector3(0f, 2f, 0f);

    Transform _player;
    Canvas _ui;
    Slider _slider;

    // === New unified contributor model ===
    // Each entry is (worker → rate), where rate can be positive (repair) or negative (damage)
    readonly Dictionary<object, float> _contributors = new();

    void Awake()
    {
        if (health <= 0f)
            health = maxHealth * 0.5f;
    }

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p) _player = p.transform;

        if (worldCanvasPrefab)
        {
            _ui = Instantiate(worldCanvasPrefab, transform);
            _ui.renderMode = RenderMode.WorldSpace;
            _ui.transform.localPosition = uiOffset;
            _ui.transform.localRotation = Quaternion.identity;

            _slider = _ui.GetComponentInChildren<Slider>();
            if (_slider)
            {
                _slider.minValue = 0f;
                _slider.maxValue = maxHealth;
                _slider.value = health;
            }
        }

        if (proximityObject) proximityObject.SetActive(false);
        if (repairingObject) repairingObject.SetActive(false);
    }

    void Update()
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // Sum all contributor rates (workers and attackers)
        float totalRate = 0f;
        foreach (var kv in _contributors)
            totalRate += kv.Value;

        if (totalRate != 0f)
        {
            health = Mathf.Clamp(health + totalRate * dt, 0f, maxHealth);
        }

        // Toggle proximity object
        if (_player && proximityObject)
        {
            bool inRange = Vector3.Distance(_player.position, transform.position) <= workRadius;
            if (proximityObject.activeSelf != inRange)
                proximityObject.SetActive(inRange);
        }

        // Toggle repairing/damaging object (active if any contributors exist)
        if (repairingObject)
        {
            bool active = _contributors.Count > 0;
            if (repairingObject.activeSelf != active)
                repairingObject.SetActive(active);
        }

        // Update UI
        if (_ui)
        {
            _ui.transform.position = transform.position + uiOffset;
            var cam = Camera.main;
            if (cam)
                _ui.transform.rotation = Quaternion.LookRotation(_ui.transform.position - cam.transform.position);
            if (_slider)
                _slider.value = health;
        }
    }

    void OnDisable()
    {
        if (proximityObject) proximityObject.SetActive(false);
        if (repairingObject) repairingObject.SetActive(false);
        _contributors.Clear();
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

    // Utility method for external scripts (damage/heal directly)
    public void AddHealth(float amount)
    {
        health = Mathf.Clamp(health + amount, 0f, maxHealth);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, workRadius);
    }
}
