using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.HighDefinition;

/// <summary>Damage stages of the obelisk. Order matters: stages only advance forward during play.</summary>
public enum ObeliskStage
{
    Shielded,
    Exposed,
    Cracked,
    Destroyed
}

/// <summary>
/// The alien obelisk: a dummy structure built entirely from primitives at runtime.
/// The attached TaskSite is the single source of truth for shield/health; this component
/// only listens to it, derives a stage, and drives visuals, sound, shards and the bunker.
/// </summary>
[RequireComponent(typeof(TaskSite))]
[DisallowMultipleComponent]
public class Obelisk : MonoBehaviour
{
    [Header("Strength")]
    [Tooltip("Shield points the Mycari must dismantle before the core is exposed.")]
    public float shieldHealth = 150f;
    [Tooltip("Core health once the shield is gone.")]
    public float coreHealth = 200f;
    [Tooltip("Core health fraction at or below which the obelisk counts as Cracked.")]
    [Range(0.05f, 0.95f)] public float crackedThreshold01 = 0.5f;

    [Header("Shards")]
    [Tooltip("Shards scattered around the obelisk when the shield breaks.")]
    public int shardCount = 6;
    [Tooltip("Radius (m) around the obelisk in which shards land.")]
    public float shardScatterRadius = 4f;

    [Header("Visuals (auto-built when empty)")]
    [Tooltip("Tall core mesh. Built from a cube in Awake when not assigned.")]
    [SerializeField] Transform core;
    [Tooltip("Translucent shield sphere. Built in Awake when not assigned.")]
    [SerializeField] Transform shieldVisual;
    [Tooltip("Bunker revealed on destruction (must contain a BunkerTrigger). Built in Awake when not assigned.")]
    [SerializeField] GameObject bunker;
    [Tooltip("HDR emissive strength of the dummy materials (exposure-independent).")]
    public float emissiveIntensity = 4f;
    [Tooltip("Position jitter (m) of the core right after taking damage.")]
    public float shakeAmplitude = 0.08f;

    [Header("Sound")]
    [Tooltip("Looping hum. A procedural hum is generated when this is empty.")]
    [SerializeField] AudioClip humClip;
    [SerializeField] AudioSource humSource;
    [Tooltip("Start humming in Start(). The flow can also call StartHum()/StopHum().")]
    [SerializeField] bool humOnStart = true;

    // Palette shared with the HUD / scan colours.
    static readonly Color VioletColor = new Color(0.541f, 0.361f, 1f);   // #8A5CFF
    static readonly Color CyanColor   = new Color(0.361f, 0.910f, 0.910f); // #5CE8E8
    static readonly Color AmberColor  = new Color(0.910f, 0.659f, 0.361f); // #E8A85C

    static readonly Vector3 CoreScale   = new Vector3(1.6f, 6f, 1.6f);
    static readonly Vector3 CoreOffset  = new Vector3(0f, 3f, 0f);
    static readonly Vector3 StumpScale  = new Vector3(1.6f, 0.6f, 1.6f);
    static readonly Vector3 StumpOffset = new Vector3(0f, 0.3f, 0f);
    const float ShieldScale = 7f;

    public ObeliskStage Stage => _stage;
    /// <summary>Set by the flow once the player has found the obelisk (not touched here).</summary>
    public bool Discovered { get; set; }
    public TaskSite Site => _site;

    public event Action OnShieldBroken;
    public event Action OnCracked;
    public event Action OnDestroyed;
    public event Action<ObeliskStage> OnStageChanged;

    TaskSite _site;
    ObeliskStage _stage = ObeliskStage.Shielded;
    BunkerTrigger _bunkerTrigger;
    Material _coreMat;
    Material _shieldMat;
    Color _coreEmissive;
    float _lastHealth;
    float _shake;            // 0..1, spikes on damage and decays
    float _targetVolume;
    bool _humming;
    bool _restoring;         // suppresses stage transitions while a checkpoint rewrites the site
    static AudioClip _generatedHum;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        _site = GetComponent<TaskSite>();
        ConfigureSite();
        EnsureCollider();
        BuildVisuals();
        SetupAudio();

        _stage = DeriveStage();
        _lastHealth = _site.health;
    }

    void OnEnable()
    {
        if (_site == null) _site = GetComponent<TaskSite>();
        _site.OnHealthChanged += OnSiteChanged;
        _site.OnShieldBroken += OnSiteChanged;
        _site.OnDepleted += OnSiteChanged;
        _site.OnFullyRepaired += OnSiteChanged;
    }

    void OnDisable()
    {
        if (_site == null) return;
        _site.OnHealthChanged -= OnSiteChanged;
        _site.OnShieldBroken -= OnSiteChanged;
        _site.OnDepleted -= OnSiteChanged;
        _site.OnFullyRepaired -= OnSiteChanged;
    }

    void Start()
    {
        // All Awakes have run (TaskSite included), so the site values are final here.
        _stage = DeriveStage();
        _lastHealth = _site.health;
        ApplyStageState();
        if (humOnStart) StartHum();
    }

    void Update()
    {
        // Poll as well as listen: cheap, and robust to whichever TaskSite path changed the numbers.
        EvaluateStage();

        float dt = Time.deltaTime;
        if (_site.health < _lastHealth - 0.01f) _shake = 1f;
        _lastHealth = _site.health;
        _shake = Mathf.Max(0f, _shake - dt * 3f);

        AnimateCore();
        AnimateShield();
        UpdateHum(dt);
    }

    // ------------------------------------------------------------------ site setup

    void ConfigureSite()
    {
        if (_site.type != TaskType.Obelisk)
        {
            Debug.LogWarning($"[Obelisk] TaskSite on '{name}' was type {_site.type}; forcing Obelisk.", this);
            _site.type = TaskType.Obelisk;
        }
        _site.allowRepair = false;
        _site.allowDismantle = true;
        _site.tornadoCanDamage = false;
        _site.displayName = "Obelisk";

        // Only seed the numbers when the scene left them at their defaults.
        if (_site.maxShield <= 0f)
        {
            _site.maxShield = shieldHealth;
            _site.shield = _site.maxShield;
            _site.maxHealth = coreHealth;
            _site.health = _site.maxHealth;
        }
    }

    void EnsureCollider()
    {
        // Daffodil's scan and the Mycari work radius both need a physics presence on the root.
        if (GetComponent<Collider>() != null) return;
        var cap = gameObject.AddComponent<CapsuleCollider>();
        cap.radius = 1.2f;
        cap.height = 7f;
        cap.center = new Vector3(0f, 3.5f, 0f);
    }

    // ------------------------------------------------------------------ stage logic

    ObeliskStage DeriveStage()
    {
        if (_site.IsDestroyed) return ObeliskStage.Destroyed;
        if (_site.shield > 0f) return ObeliskStage.Shielded;
        if (_site.Health01 <= crackedThreshold01) return ObeliskStage.Cracked;
        return ObeliskStage.Exposed;
    }

    void OnSiteChanged(TaskSite _) => EvaluateStage();

    /// <summary>Advances the stage forward (firing each crossed stage's effects) when the site says so.</summary>
    void EvaluateStage()
    {
        if (_restoring || _site == null) return;
        ObeliskStage derived = DeriveStage();
        if (derived == _stage) return;

        if (derived < _stage)
        {
            // Site was healed/reset from outside; just resync silently.
            _stage = derived;
            ApplyStageState();
            OnStageChanged?.Invoke(_stage);
            return;
        }

        while (_stage < derived)
        {
            _stage = _stage + 1;
            EnterStage(_stage);
        }
    }

    void EnterStage(ObeliskStage stage)
    {
        ApplyStageState();
        switch (stage)
        {
            case ObeliskStage.Exposed:
                ScatterShards();
                OnShieldBroken?.Invoke();
                break;
            case ObeliskStage.Cracked:
                _shake = 1f;
                OnCracked?.Invoke();
                break;
            case ObeliskStage.Destroyed:
                _shake = 1f;
                OnDestroyed?.Invoke();
                break;
        }
        OnStageChanged?.Invoke(stage);
    }

    /// <summary>Rebuilds visuals, bunker and hum volume for the current stage (no events).</summary>
    void ApplyStageState()
    {
        bool destroyed = _stage == ObeliskStage.Destroyed;
        bool shielded = _stage == ObeliskStage.Shielded;

        if (shieldVisual) shieldVisual.gameObject.SetActive(shielded);
        if (core)
        {
            core.localScale = destroyed ? StumpScale : CoreScale;
            core.localPosition = destroyed ? StumpOffset : CoreOffset;
        }
        if (bunker && bunker.activeSelf != destroyed) bunker.SetActive(destroyed);

        switch (_stage)
        {
            case ObeliskStage.Shielded:  _targetVolume = 0.15f; break;
            case ObeliskStage.Exposed:   _targetVolume = 0.35f; break;
            case ObeliskStage.Cracked:   _targetVolume = 0.7f;  break;
            default:                     _targetVolume = 0f;    break;
        }
        if (humSource) humSource.pitch = _stage == ObeliskStage.Cracked ? 1.15f : 1f;
    }

    /// <summary>
    /// Checkpoint restore: forces the TaskSite numbers to be consistent with <paramref name="stage"/>
    /// (keeping them when they already are), clears leftover shards and rebuilds the visual state.
    /// </summary>
    public void RestoreStage(ObeliskStage stage)
    {
        if (_site == null) _site = GetComponent<TaskSite>();

        float maxShield = _site.maxShield > 0f ? _site.maxShield : shieldHealth;
        float maxHealth = _site.maxHealth > 0f ? _site.maxHealth : coreHealth;
        float h = _site.health;
        float s = _site.shield;
        float h01 = maxHealth > 0f ? h / maxHealth : 0f;

        switch (stage)
        {
            case ObeliskStage.Shielded:
                if (s <= 0f) s = maxShield;
                if (h <= 0f) h = maxHealth;
                break;
            case ObeliskStage.Exposed:
                s = 0f;
                if (h <= 0f || h01 <= crackedThreshold01) h = maxHealth;
                break;
            case ObeliskStage.Cracked:
                s = 0f;
                if (h <= 0f || h01 > crackedThreshold01) h = Mathf.Max(1f, maxHealth * crackedThreshold01);
                break;
            case ObeliskStage.Destroyed:
                s = 0f;
                h = 0f;
                break;
        }

        _restoring = true;
        try { _site.SetState(h, s); }
        finally { _restoring = false; }

        ObeliskShard.DestroyAll();
        _stage = stage;
        _lastHealth = _site.health;
        _shake = 0f;
        if (_bunkerTrigger) _bunkerTrigger.ResetEntered();
        ApplyStageState();
        if (humSource && _humming && stage != ObeliskStage.Destroyed && !humSource.isPlaying) humSource.Play();
        OnStageChanged?.Invoke(_stage);
    }

    // ------------------------------------------------------------------ scan

    public ScanHit ToScanHit()
    {
        return new ScanHit
        {
            target = transform,
            kind = ScanKind.Obelisk,
            label = "OBELISK",
            detail = StageText(_stage),
            value01 = _site.shield > 0f ? _site.Shield01 : _site.Health01,
            color = VioletColor
        };
    }

    static string StageText(ObeliskStage s)
    {
        switch (s)
        {
            case ObeliskStage.Shielded:  return "Shielded";
            case ObeliskStage.Exposed:   return "Exposed";
            case ObeliskStage.Cracked:   return "Cracked";
            default:                     return "Destroyed";
        }
    }

    // ------------------------------------------------------------------ shards

    void ScatterShards()
    {
        int count = Mathf.Max(0, shardCount);
        Vector3 origin = transform.position;
        for (int i = 0; i < count; i++)
        {
            // Even angular spread with a little jitter so the ring doesn't look mechanical.
            float angle = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) / Mathf.Max(1, count) * Mathf.PI * 2f;
            float radius = Mathf.Lerp(shardScatterRadius * 0.5f, shardScatterRadius, UnityEngine.Random.value);
            Vector3 p = origin + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas))
                p = hit.position + Vector3.up * 0.5f;
            else
                p = new Vector3(p.x, origin.y + 0.5f, p.z);

            ObeliskShard.Spawn(p);
        }
    }

    // ------------------------------------------------------------------ visuals

    void BuildVisuals()
    {
        if (core == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Core";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = CoreOffset;
            go.transform.localScale = CoreScale;
            Destroy(go.GetComponent<Collider>()); // root capsule is the physics presence
            core = go.transform;
        }
        var coreRenderer = core.GetComponent<Renderer>();
        if (coreRenderer)
        {
            var dark = new Color(0.07f, 0.05f, 0.11f);
            _coreMat = ObeliskMaterials.CreateEmissive(dark, VioletColor, emissiveIntensity, false, 1f, coreRenderer.sharedMaterial);
            coreRenderer.material = _coreMat;
            _coreEmissive = VioletColor * emissiveIntensity;
        }

        if (shieldVisual == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Shield";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = CoreOffset;
            go.transform.localScale = Vector3.one * ShieldScale;
            var col = go.GetComponent<Collider>();
            if (col) col.enabled = false;
            shieldVisual = go.transform;
        }
        var shieldRenderer = shieldVisual.GetComponent<Renderer>();
        if (shieldRenderer)
        {
            _shieldMat = ObeliskMaterials.CreateEmissive(CyanColor, CyanColor, emissiveIntensity * 0.5f, true, 0.28f, shieldRenderer.sharedMaterial);
            shieldRenderer.material = _shieldMat;
            shieldRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        if (bunker == null) bunker = BuildBunker();
        _bunkerTrigger = bunker.GetComponentInChildren<BunkerTrigger>(true);
        if (_bunkerTrigger == null) _bunkerTrigger = bunker.AddComponent<BunkerTrigger>();
        bunker.SetActive(false);
    }

    GameObject BuildBunker()
    {
        // Sits just in front of the obelisk so the player can actually walk into the trigger
        // (the root capsule collider keeps them out of the centre).
        var root = new GameObject("Bunker");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = new Vector3(0f, 0f, 2.6f);

        var baseBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseBox.name = "Base";
        baseBox.transform.SetParent(root.transform, false);
        baseBox.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        baseBox.transform.localScale = new Vector3(3f, 1f, 3f);
        Destroy(baseBox.GetComponent<Collider>()); // trigger below is the only collider; nothing blocks entry
        var baseRenderer = baseBox.GetComponent<Renderer>();
        if (baseRenderer)
            baseRenderer.material = ObeliskMaterials.CreateEmissive(new Color(0.08f, 0.08f, 0.09f), Color.black, 0f, false, 1f, baseRenderer.sharedMaterial);

        var hatch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hatch.name = "Hatch";
        hatch.transform.SetParent(root.transform, false);
        hatch.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        hatch.transform.localScale = new Vector3(0.9f, 0.2f, 0.9f);
        Destroy(hatch.GetComponent<Collider>());
        var hatchRenderer = hatch.GetComponent<Renderer>();
        if (hatchRenderer)
            hatchRenderer.material = ObeliskMaterials.CreateEmissive(AmberColor, AmberColor, emissiveIntensity, false, 1f, hatchRenderer.sharedMaterial);

        var trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 1.5f, 0f);
        trigger.size = new Vector3(3f, 3f, 3f);
        root.AddComponent<BunkerTrigger>();
        return root;
    }

    void AnimateCore()
    {
        if (!core) return;
        bool destroyed = _stage == ObeliskStage.Destroyed;
        float t = Time.time;

        if (destroyed)
        {
            core.localPosition = StumpOffset;
            core.localScale = StumpScale;
            if (_coreMat) ObeliskMaterials.SetEmissive(_coreMat, _coreEmissive * 0.15f);
            return;
        }

        // Shrinks slightly as the core loses health; jitters briefly after each hit.
        float damage = 1f - _site.Health01;
        float scaleMul = 1f - 0.12f * damage;
        Vector3 jitter = _shake > 0f ? UnityEngine.Random.insideUnitSphere * (shakeAmplitude * _shake) : Vector3.zero;
        core.localScale = new Vector3(CoreScale.x * scaleMul, CoreScale.y * scaleMul, CoreScale.z * scaleMul);
        core.localPosition = CoreOffset * scaleMul + jitter;

        if (_coreMat == null) return;
        float glow = 1f;
        if (_stage == ObeliskStage.Cracked)
        {
            // Unstable flicker: fast noise plus occasional near-dropouts.
            float n = Mathf.PerlinNoise(t * 9f, 0.37f);
            float dropout = Mathf.PerlinNoise(t * 2.3f, 7.1f) < 0.25f ? 0.25f : 1f;
            glow = (0.45f + 0.75f * n) * dropout;
        }
        else if (_stage == ObeliskStage.Exposed)
        {
            glow = 0.9f + 0.1f * Mathf.Sin(t * 3f);
        }
        ObeliskMaterials.SetEmissive(_coreMat, _coreEmissive * glow);
    }

    void AnimateShield()
    {
        if (!shieldVisual || !shieldVisual.gameObject.activeSelf) return;
        float pulse = 1f + 0.03f * Mathf.Sin(Time.time * 2.2f);
        shieldVisual.localScale = Vector3.one * (ShieldScale * pulse);
        if (_shieldMat)
        {
            // Thinner as it takes damage so the player can read it without the HUD.
            float alpha = Mathf.Lerp(0.1f, 0.32f, _site.Shield01);
            ObeliskMaterials.SetAlpha(_shieldMat, alpha);
        }
    }

    // ------------------------------------------------------------------ sound

    void SetupAudio()
    {
        if (humSource == null) humSource = GetComponent<AudioSource>();
        if (humSource == null) humSource = gameObject.AddComponent<AudioSource>();

        if (humClip == null)
        {
            if (_generatedHum == null) _generatedHum = GenerateHum();
            humClip = _generatedHum;
        }

        humSource.clip = humClip;
        humSource.loop = true;
        humSource.playOnAwake = false;
        humSource.spatialBlend = 1f;
        humSource.minDistance = 6f;
        humSource.maxDistance = 60f;
        humSource.rolloffMode = AudioRolloffMode.Linear;
        humSource.dopplerLevel = 0f;
        humSource.volume = 0f;
    }

    public void StartHum()
    {
        _humming = true;
        if (humSource && !humSource.isPlaying && _stage != ObeliskStage.Destroyed) humSource.Play();
    }

    public void StopHum()
    {
        _humming = false;
        if (humSource && humSource.isPlaying) humSource.Stop();
        if (humSource) humSource.volume = 0f;
    }

    void UpdateHum(float dt)
    {
        if (!humSource || !_humming) return;
        float target = _targetVolume;
        humSource.volume = Mathf.MoveTowards(humSource.volume, target, dt * 0.5f);
        if (_stage == ObeliskStage.Destroyed && humSource.volume <= 0.001f && humSource.isPlaying)
            humSource.Stop();
    }

    /// <summary>2 s mono loop: 55/110/165 Hz partials with slow amplitude wobble. Every partial and
    /// wobble completes whole cycles in 2 s so the loop point is seamless.</summary>
    static AudioClip GenerateHum()
    {
        const int rate = 44100;
        const float seconds = 2f;
        int samples = (int)(rate * seconds);
        var data = new float[samples];
        const float twoPi = Mathf.PI * 2f;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float wobbleA = 0.75f + 0.25f * Mathf.Sin(twoPi * 0.5f * t);
            float wobbleB = 0.85f + 0.15f * Mathf.Sin(twoPi * 1.0f * t + 1.3f);
            float v = 0.55f * Mathf.Sin(twoPi * 55f * t)
                    + 0.30f * Mathf.Sin(twoPi * 110f * t) * wobbleB
                    + 0.15f * Mathf.Sin(twoPi * 165f * t) * wobbleA;
            data[i] = v * wobbleA * 0.8f;
        }
        var clip = AudioClip.Create("ObeliskHum", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.541f, 0.361f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, shardScatterRadius);
    }
}

/// <summary>
/// Runtime material factory for the dummy obelisk/shard/bunker visuals. Prefers HDRP/Lit and falls
/// back to URP Lit, Standard, or the primitive's own material when a shader is unavailable.
/// </summary>
public static class ObeliskMaterials
{
    static Shader _shader;
    static bool _shaderSearched;
    static bool _isHdrp;

    static Shader FindShader()
    {
        if (_shaderSearched) return _shader;
        _shaderSearched = true;
        _shader = Shader.Find("HDRP/Lit");
        _isHdrp = _shader != null;
        if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Lit");
        if (_shader == null) _shader = Shader.Find("Standard");
        return _shader;
    }

    /// <summary>
    /// Creates an emissive material. <paramref name="fallback"/> is cloned when no known shader exists
    /// (e.g. stripped from a build) so the object still renders with a tint.
    /// </summary>
    public static Material CreateEmissive(Color baseColor, Color emissive, float intensity, bool transparent, float alpha, Material fallback = null)
    {
        Shader shader = FindShader();
        Material mat;
        if (shader != null) mat = new Material(shader);
        else if (fallback != null) mat = new Material(fallback);
        else mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
        mat.name = "ObeliskDummy";

        Color c = baseColor;
        c.a = transparent ? alpha : 1f;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);

        bool hdrp = _isHdrp && shader != null;
        if (hdrp)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetFloat("_UseEmissiveIntensity", 0f);
            // Exposure weight 0 = emission ignores scene exposure, so a small HDR value glows on a bright Mars day.
            if (mat.HasProperty("_EmissiveExposureWeight")) mat.SetFloat("_EmissiveExposureWeight", 0f);
            mat.SetColor("_EmissiveColor", emissive * intensity);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", transparent ? 0.9f : 0.55f);

            if (transparent)
            {
                mat.SetFloat("_SurfaceType", 1f);
                mat.SetFloat("_BlendMode", 0f);
                if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 0f);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                if (mat.HasProperty("_TransparentZWrite")) mat.SetFloat("_TransparentZWrite", 0f);
                if (mat.HasProperty("_EnableFogOnTransparent")) mat.SetFloat("_EnableFogOnTransparent", 1f);
                if (mat.HasProperty("_TransparentBackfaceEnable")) mat.SetFloat("_TransparentBackfaceEnable", 0f);
            }
            // Lets HDRP derive blend states, passes and keywords from the properties above.
            HDMaterial.ValidateMaterial(mat);
            if (transparent) mat.renderQueue = 3000;
        }
        else
        {
            // URP Lit / Standard share the _EmissionColor convention. Intensities are not exposure-based there.
            float k = Mathf.Min(intensity, 4f);
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissive * k);
            if (transparent)
            {
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // URP transparent
                if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);         // Standard: Transparent
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
            }
        }
        return mat;
    }

    /// <summary>Updates the HDR emissive colour on a material created by <see cref="CreateEmissive"/>.</summary>
    public static void SetEmissive(Material mat, Color hdr)
    {
        if (mat == null) return;
        if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", hdr);
        else if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", hdr);
    }

    public static void SetAlpha(Material mat, float alpha)
    {
        if (mat == null) return;
        if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor"); c.a = alpha; mat.SetColor("_BaseColor", c);
        }
        else if (mat.HasProperty("_Color"))
        {
            Color c = mat.GetColor("_Color"); c.a = alpha; mat.SetColor("_Color", c);
        }
    }
}
