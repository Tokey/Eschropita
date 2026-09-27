using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A glowing crystal shard dropped when the obelisk shield breaks. Bobs and spins in place;
/// walking into it adds one shard to the player's pocket and pops the pickup away.
/// The mesh and colliders are built in code so a bare GameObject with this component is enough.
/// </summary>
[DisallowMultipleComponent]
public class ObeliskShard : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Edge length of the cube gem.")]
    public float size = 0.4f;
    [Tooltip("Pickup trigger radius.")]
    public float pickupRadius = 0.9f;
    public Color color = new Color(0.910f, 0.659f, 0.361f); // #E8A85C amber
    public float emissiveIntensity = 4f;

    [Header("Motion")]
    public float bobAmplitude = 0.15f;
    public float bobSpeed = 2f;
    public float spinDegreesPerSecond = 90f;

    [Header("Pickup")]
    [Tooltip("Scale-pop duration before the shard is destroyed.")]
    public float popSeconds = 0.15f;

    static readonly List<ObeliskShard> _active = new List<ObeliskShard>();
    static Material _sharedMaterial;

    /// <summary>Shards currently sitting in the world (collected ones leave immediately).</summary>
    public static int ActiveCount => _active.Count;
    public static IReadOnlyList<ObeliskShard> Active => _active;
    public bool Collected => _collected;

    Transform _gem;
    SphereCollider _trigger;
    float _phase;
    bool _collected;

    /// <summary>Creates a shard GameObject at a world position.</summary>
    public static ObeliskShard Spawn(Vector3 position)
    {
        var go = new GameObject("ObeliskShard");
        go.transform.position = position;
        return go.AddComponent<ObeliskShard>();
    }

    /// <summary>Removes every shard from the world (checkpoint restore).</summary>
    public static void DestroyAll()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var s = _active[i];
            if (s) Destroy(s.gameObject);
        }
        _active.Clear();
    }

    void Awake()
    {
        BuildGem();

        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        _trigger = GetComponent<SphereCollider>();
        if (_trigger == null) _trigger = gameObject.AddComponent<SphereCollider>();
        _trigger.isTrigger = true;
        _trigger.radius = pickupRadius;

        _phase = Random.value * Mathf.PI * 2f;
        transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
    }

    void OnEnable()
    {
        if (!_collected && !_active.Contains(this)) _active.Add(this);
    }

    void OnDisable()
    {
        _active.Remove(this);
    }

    void Update()
    {
        if (_collected || _gem == null) return;
        float t = Time.time * bobSpeed + _phase;
        _gem.localPosition = new Vector3(0f, bobAmplitude * Mathf.Sin(t), 0f);
        // Tilted spin reads better than a flat one for a cube.
        _gem.localRotation = Quaternion.Euler(35f, Time.time * spinDegreesPerSecond + _phase * Mathf.Rad2Deg, 45f);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_collected) return;
        if (!other.CompareTag("Player")) return;

        _collected = true;
        _active.Remove(this);
        if (_trigger) _trigger.enabled = false;

        var player = other.GetComponentInParent<PlayerController>();
        if (player != null) player.AddShards(1);

        StartCoroutine(PopAndDestroy());
    }

    IEnumerator PopAndDestroy()
    {
        Vector3 start = _gem ? _gem.localScale : Vector3.one * size;
        float dur = Mathf.Max(0.01f, popSeconds);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            // Quick swell then collapse to nothing.
            float s = k < 0.4f ? Mathf.Lerp(1f, 1.5f, k / 0.4f) : Mathf.Lerp(1.5f, 0f, (k - 0.4f) / 0.6f);
            if (_gem) _gem.localScale = start * s;
            yield return null;
        }
        Destroy(gameObject);
    }

    public ScanHit ToScanHit()
    {
        return new ScanHit
        {
            target = transform,
            kind = ScanKind.Shard,
            label = "SHARD",
            detail = "",
            value01 = -1f,
            color = color
        };
    }

    void BuildGem()
    {
        // Reuse an existing child gem (e.g. a prefab) when present.
        var existing = transform.Find("Gem");
        if (existing != null) { _gem = existing; return; }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Gem";
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * size;
        Destroy(go.GetComponent<Collider>()); // the root sphere trigger handles pickup
        _gem = go.transform;

        var r = go.GetComponent<Renderer>();
        if (r)
        {
            if (_sharedMaterial == null)
                _sharedMaterial = ObeliskMaterials.CreateEmissive(color, color, emissiveIntensity, false, 1f, r.sharedMaterial);
            r.sharedMaterial = _sharedMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
