using System.Collections;
using UnityEngine;

/// <summary>
/// What the obelisk looks and sounds like: the painted statue, the shards breaking off its sides,
/// its shield bubble, and everything that makes it read as dangerous rather than inviting - sparks
/// spitting off the shard tips, arcs jumping between them, a dim underlight that stutters, cracks
/// that open as it is hurt, glitching when it is unstable, its ocarina holes lighting up when it
/// sings or speaks, and a collapse down to the plinth when it dies.
///
/// <see cref="Obelisk"/> owns the rules (stage, shield, health) and tells this component what
/// happened; nothing here changes game state. It is built from the mushroom kit on purpose - the
/// halo, sparks, debris, ground light and shock rings all run on the mushrooms' Soft Glow material
/// - only tuned darker, harsher and twitchier, and with far less glow.
///
/// Hierarchy, built at runtime: this object is the obelisk root. "Billboard" turns to the camera
/// rig's yaw and stretches by 1/cos(pitch), the same as <see cref="SpriteBillboard"/> does for the
/// mushrooms; under it "Body" is the shake / squash pivot carrying the halo, the depth twin, the
/// statue and the shard layer, and "Shield" is the bubble quad. Particles and ground decals hang
/// off the root so the billboard's stretch never distorts them.
/// </summary>
[DisallowMultipleComponent]
public class ObeliskVisuals : MonoBehaviour
{
    [Header("Art")]
    [Tooltip("The painted statue with the flying shards cut out (Obelisk.png).")]
    [SerializeField] Sprite bodySprite;
    [Tooltip("The shards breaking off its sides, on the same canvas and pivot (ObeliskShards.png).")]
    [SerializeField] Sprite shardsSprite;
    [Tooltip("Soft white silhouette for the halo (ObeliskGlow.png).")]
    [SerializeField] Sprite glowSprite;
    [Tooltip("Eschropita/Obelisk Sprite with the mask assigned (ObeliskBody). Draws the statue and the shards.")]
    [SerializeField] Material bodyMaterial;
    [Tooltip("Depth-only twin of the body (ObeliskDepth), drawn first so whatever stands behind the " +
             "statue is hidden per pixel.")]
    [SerializeField] Material depthMaterial;
    [Tooltip("Eschropita/Obelisk Shield (ObeliskShield).")]
    [SerializeField] Material shieldMaterial;
    [Tooltip("The mushrooms' Eschropita/Soft Glow material, reused for the halo, sparks, arcs, debris and " +
             "the light on the ground.")]
    [SerializeField] Material softGlowMaterial;
    [Tooltip("Uniform size. The collider and nav obstacle follow it.")]
    [SerializeField, Min(0.1f)] float size = 1f;

    [Header("Palette")]
    [SerializeField, ColorUsage(false, true)] Color energyColor = new Color(0.45f, 0.85f, 1f);
    [SerializeField, ColorUsage(false, true)] Color violetColor = new Color(0.541f, 0.361f, 1f);   // #8A5CFF, the scan colour
    [Tooltip("Tint it shifts toward once it is cracked and unstable.")]
    [SerializeField, ColorUsage(false, true)] Color warningColor = new Color(1f, 0.25f, 0.35f);

    [Header("Mood")]
    [Tooltip("Halo strength. Kept low on purpose: it should look like something leaking, not something lit.")]
    [SerializeField, Range(0f, 2f)] float haloStrength = 1f;
    [Tooltip("Multiplier on how often it discharges sparks and arcs.")]
    [SerializeField, Range(0f, 4f)] float dischargeRate = 1f;
    [Tooltip("Volume of the electrical crackle and the impacts.")]
    [SerializeField, Range(0f, 1f)] float crackleVolume = 0.6f;
    [Tooltip("Speaker name whose dialogue lines make the obelisk light up and shudder.")]
    [SerializeField] string voiceSpeaker = "OBELISK";

    [Header("Shield Bubble")]
    [Tooltip("Diameter on screen, in metres.")]
    [SerializeField, Min(1f)] float shieldDiameter = 8.8f;
    [Tooltip("Height of the bubble's centre on the painting, in sprite units above the base.")]
    [SerializeField] float shieldCentre = 3.55f;
    [Tooltip("How far the bubble sits in front of the statue, toward the camera. A camera-facing disc " +
             "through the statue's own plane would be half hidden behind it.")]
    [SerializeField, Min(0f)] float shieldForward = 0.7f;

    [Header("Collapse")]
    [Tooltip("Where the break line stops, as a fraction of the painting's height: just above the plinth.")]
    [SerializeField, Range(0.05f, 0.6f)] float stumpHeight = 0.29f;
    [Tooltip("Seconds for the break line to sweep from the top of the head down to the plinth.")]
    [SerializeField, Min(0.2f)] float collapseSeconds = 1.3f;

    // ------------------------------------------------------------------ shader ids

    static readonly int FxTimeID          = Shader.PropertyToID("_FxTime");
    static readonly int SaturationID      = Shader.PropertyToID("_Saturation");
    static readonly int BrightnessID      = Shader.PropertyToID("_Brightness");
    static readonly int TintColorID       = Shader.PropertyToID("_TintColor");
    static readonly int TintAmountID      = Shader.PropertyToID("_TintAmount");
    static readonly int FlashColorID      = Shader.PropertyToID("_FlashColor");
    static readonly int FlashAmountID     = Shader.PropertyToID("_FlashAmount");
    static readonly int EnergyColorID     = Shader.PropertyToID("_EnergyColor");
    static readonly int HoleColorID       = Shader.PropertyToID("_HoleColor");
    static readonly int HoleGlowID        = Shader.PropertyToID("_HoleGlow");
    static readonly int CoreFlowID        = Shader.PropertyToID("_CoreFlow");
    static readonly int CorePhaseID       = Shader.PropertyToID("_CorePhase");
    static readonly int ShardGlowID       = Shader.PropertyToID("_ShardGlow");
    static readonly int CrackAmountID     = Shader.PropertyToID("_CrackAmount");
    static readonly int GlitchID          = Shader.PropertyToID("_Glitch");
    static readonly int CrumbleHeightID   = Shader.PropertyToID("_CrumbleHeight");
    static readonly int CrumbleEdgeGlowID = Shader.PropertyToID("_CrumbleEdgeGlow");
    static readonly int StrengthID        = Shader.PropertyToID("_Strength");
    static readonly int FlickerID         = Shader.PropertyToID("_Flicker");
    static readonly int BurstID           = Shader.PropertyToID("_Burst");
    static readonly int HitUVID           = Shader.PropertyToID("_HitUV");
    static readonly int MainTexID         = Shader.PropertyToID("_MainTex");
    static readonly int ColorID           = Shader.PropertyToID("_Color");
    static readonly int IntensityID       = Shader.PropertyToID("_Intensity");
    static readonly int AdditiveID        = Shader.PropertyToID("_Additive");
    static readonly int ShapeID           = Shader.PropertyToID("_Shape");
    static readonly int SoftnessID        = Shader.PropertyToID("_Softness");
    static readonly int RingRadiusID      = Shader.PropertyToID("_RingRadius");
    static readonly int RingWidthID       = Shader.PropertyToID("_RingWidth");

    // ------------------------------------------------------------------ spots on the painting
    // Normalised on Obelisk.png (1500 x 1900): u from the left edge, v up from the bottom edge.
    // Measured by the import script; the shards layer shares the canvas, so the same numbers apply.

    static readonly Vector2[] Holes =
    {
        new Vector2(0.5077f, 0.9155f), new Vector2(0.6427f, 0.8634f), new Vector2(0.4107f, 0.8579f),
        new Vector2(0.5253f, 0.7687f), new Vector2(0.3787f, 0.7416f), new Vector2(0.2273f, 0.7045f),
    };

    // xy: the pipe's open end, zw: the way it points on the painting.
    static readonly Vector4[] Pipes =
    {
        new Vector4(0.26f, 0.9368f, -0.6f, 0.8f), new Vector4(0.6453f, 0.9642f, 0.05f, 1f),
        new Vector4(0.19f, 0.8042f, -1f, 0.1f),   new Vector4(0.7867f, 0.8042f, 1f, 0.1f),
    };

    // The points of the shards furthest from the body: where sparks spit and arcs start.
    static readonly Vector2[] ShardTips =
    {
        new Vector2(0.9647f, 0.32f), new Vector2(0.904f, 0.2989f), new Vector2(0.876f, 0.5153f),
        new Vector2(0.8167f, 0.6105f), new Vector2(0.8633f, 0.4674f), new Vector2(0.178f, 0.5284f),
        new Vector2(0.208f, 0.4811f), new Vector2(0.228f, 0.4932f), new Vector2(0.2373f, 0.4458f),
        new Vector2(0.024f, 0.4732f), new Vector2(0.084f, 0.4763f),
    };

    // Left and right edge of the body every 2 % of the height (index = v * 50), shards excluded.
    static readonly Vector2[] Rows =
    {
        new Vector2(0.5f, 0.5f), new Vector2(0.08f, 0.923f), new Vector2(0.069f, 0.921f), new Vector2(0.063f, 0.915f),
        new Vector2(0.062f, 0.905f), new Vector2(0.063f, 0.889f), new Vector2(0.066f, 0.855f), new Vector2(0.073f, 0.805f),
        new Vector2(0.081f, 0.77f), new Vector2(0.089f, 0.771f), new Vector2(0.097f, 0.762f), new Vector2(0.132f, 0.749f),
        new Vector2(0.15f, 0.753f), new Vector2(0.163f, 0.756f), new Vector2(0.187f, 0.757f), new Vector2(0.24f, 0.757f),
        new Vector2(0.292f, 0.757f), new Vector2(0.337f, 0.757f), new Vector2(0.316f, 0.773f), new Vector2(0.305f, 0.755f),
        new Vector2(0.302f, 0.753f), new Vector2(0.275f, 0.75f), new Vector2(0.365f, 0.743f), new Vector2(0.351f, 0.754f),
        new Vector2(0.333f, 0.729f), new Vector2(0.315f, 0.737f), new Vector2(0.354f, 0.736f), new Vector2(0.393f, 0.733f),
        new Vector2(0.417f, 0.724f), new Vector2(0.443f, 0.693f), new Vector2(0.421f, 0.661f), new Vector2(0.411f, 0.649f),
        new Vector2(0.09f, 0.648f), new Vector2(0.079f, 0.665f), new Vector2(0.106f, 0.675f), new Vector2(0.134f, 0.64f),
        new Vector2(0.159f, 0.657f), new Vector2(0.181f, 0.684f), new Vector2(0.193f, 0.711f), new Vector2(0.194f, 0.737f),
        new Vector2(0.173f, 0.799f), new Vector2(0.188f, 0.794f), new Vector2(0.233f, 0.795f), new Vector2(0.243f, 0.809f),
        new Vector2(0.254f, 0.82f), new Vector2(0.267f, 0.822f), new Vector2(0.245f, 0.807f), new Vector2(0.241f, 0.682f),
        new Vector2(0.359f, 0.67f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
    };

    // The broken shell's exposed core (the grey fibres), and the point the shard layer strains away from.
    static readonly Rect CoreRect = new Rect(0.454f, 0.3184f, 0.2927f, 0.2637f);
    static readonly Vector2 ShardsCentre = new Vector2(0.55f, 0.45f);

    const float Intact = 1.2f;          // _CrumbleHeight that cuts nothing
    const float RingLife = 0.6f;
    const int ArcSegments = 12;
    const float HitReactThreshold = 1.5f;   // damage gathered before the next visible reaction

    // ------------------------------------------------------------------ mood

    /// <summary>The resting look per stage; the live look eases toward it.</summary>
    struct Mood
    {
        public float activity, halo, holes, core, shards, saturation, brightness, tint, glitch, jitter, pool;

        public static Mood Lerp(in Mood a, in Mood b, float t) => new Mood
        {
            activity   = Mathf.LerpUnclamped(a.activity, b.activity, t),
            halo       = Mathf.LerpUnclamped(a.halo, b.halo, t),
            holes      = Mathf.LerpUnclamped(a.holes, b.holes, t),
            core       = Mathf.LerpUnclamped(a.core, b.core, t),
            shards     = Mathf.LerpUnclamped(a.shards, b.shards, t),
            saturation = Mathf.LerpUnclamped(a.saturation, b.saturation, t),
            brightness = Mathf.LerpUnclamped(a.brightness, b.brightness, t),
            tint       = Mathf.LerpUnclamped(a.tint, b.tint, t),
            glitch     = Mathf.LerpUnclamped(a.glitch, b.glitch, t),
            jitter     = Mathf.LerpUnclamped(a.jitter, b.jitter, t),
            pool       = Mathf.LerpUnclamped(a.pool, b.pool, t),
        };
    }

    static Mood MoodFor(ObeliskStage stage)
    {
        switch (stage)
        {
            // Asleep behind its shield: drained, dark, the odd spark. Still not a nice place to stand.
            case ObeliskStage.Shielded:
                return new Mood { activity = 0.35f, halo = 0.16f, holes = 0.22f, core = 0.22f, shards = 0.2f,
                                  saturation = 0.8f, brightness = 0.86f, tint = 0f, glitch = 0f, jitter = 0.008f, pool = 0.1f };
            // Exposed and awake: the core runs, the shards strain.
            case ObeliskStage.Exposed:
                return new Mood { activity = 0.6f, halo = 0.24f, holes = 0.4f, core = 0.5f, shards = 0.38f,
                                  saturation = 0.95f, brightness = 0.93f, tint = 0.04f, glitch = 0.02f, jitter = 0.016f, pool = 0.17f };
            // Cracked: unstable, flickering toward the warning colour, discharging constantly.
            case ObeliskStage.Cracked:
                return new Mood { activity = 1f, halo = 0.3f, holes = 0.75f, core = 0.95f, shards = 0.65f,
                                  saturation = 1.05f, brightness = 1f, tint = 0.12f, glitch = 0.07f, jitter = 0.03f, pool = 0.26f };
            default:
                return new Mood { saturation = 0.7f, brightness = 0.72f };
        }
    }

    // ------------------------------------------------------------------ state

    ObeliskStage _stage = ObeliskStage.Shielded;
    Mood _mood;
    TaskSite _site;
    IsoFollowCamera _rig;
    float _nextRigLookup;
    bool _built;

    float _fxTime;
    float _corePhase;                 // integrated pulse phase for the core, see Update
    float _flicker = 1f;
    float _hum, _speak;
    bool _speaking;
    float _overload;                  // 0..1 during the build-up before the collapse

    // Transients, each decaying back to rest.
    float _flash;
    Color _flashColor = Color.white;
    float _glitchKick, _shake, _shardKick, _poolKick;

    // Springs: squash is a fraction (+ taller), lean is degrees in the screen plane.
    float _squash, _squashVel, _lean, _leanVel, _leanTarget;
    Vector2 _vib, _shardJitter;
    float _nextVibStep, _nextShardStep;

    // Collapse
    Coroutine _collapse, _shieldBreak;
    bool _collapsing;
    float _crumble = Intact;
    float _shardsAlpha = 1f, _shardsFling, _haloFade = 1f;
    float _smokeUntil, _embersUntil, _smokeAcc, _breakAcc, _moteAcc;

    // Shield
    bool _shieldVisible = true;
    float _shieldBurst, _shieldScale = 1f, _hitAge = 9f;
    Vector2 _hitPoint;

    // Damage feedback
    float _shieldDamage, _coreDamage, _nextShieldReact, _nextCoreReact, _nextDischarge;

    // Built parts
    Transform _billboard, _body, _shardsPivot, _shield, _ground;
    SpriteRenderer _bodyRenderer, _depthRenderer, _shardsRenderer, _glowRenderer;
    MeshRenderer _shieldRenderer, _pool, _shadow, _blight;
    MeshRenderer[] _rings;
    float[] _ringAge;
    Color[] _ringColor;
    float[] _ringSize;
    int _nextRing;
    ParticleSystem _sparks, _debris, _dust, _motes;
    AudioSource _fxAudio;
    MaterialPropertyBlock _mpb;
    Vector3 _shardsCentreLocal;

    class Arc
    {
        public LineRenderer line;
        public Vector3 a, b;
        public float age, life, width, nextJag;
        public Color color;
        public bool alive;
    }
    Arc[] _arcs;

    DialogueSystem _dialogue;

    static Mesh s_quad, s_groundQuad;
    static Texture2D s_arcProfile;
    static AudioClip[] s_crackles;
    static AudioClip s_thump, s_rumble;
    static readonly RaycastHit[] s_hits = new RaycastHit[16];

    // ------------------------------------------------------------------ public API

    /// <summary>True when the painted art is assigned; otherwise <see cref="Obelisk"/> keeps its primitive dummy.</summary>
    public bool HasArt => bodySprite != null && bodyMaterial != null;

    /// <summary>Horizontal radius the statue occupies, for the collider and the nav obstacle.</summary>
    public float FootprintRadius => 2f * size;

    /// <summary>World height of the intact statue (the painting is stretched by the camera pitch).</summary>
    public float StatueHeight => 7.04f * size * PitchStretch();

    /// <summary>World height of what is left once it has collapsed.</summary>
    public float StumpWorldHeight => 7.04f * stumpHeight * size * PitchStretch();

    /// <summary>Jumps straight to how a stage looks at rest, with no transition (checkpoint restore, startup).</summary>
    public void Snap(ObeliskStage stage)
    {
        _stage = stage;
        if (!_built) return;

        StopTransitions();
        _mood = MoodFor(stage);

        bool destroyed = stage == ObeliskStage.Destroyed;
        _crumble = destroyed ? stumpHeight : Intact;
        _shardsAlpha = destroyed ? 0f : 1f;
        _shardsFling = 0f;
        _haloFade = destroyed ? 0f : 1f;
        _shieldVisible = stage == ObeliskStage.Shielded;
        _shieldBurst = 0f;
        _shieldScale = 1f;
        _hitAge = 9f;
        _flash = _glitchKick = _shake = _shardKick = _poolKick = 0f;
        _squash = _squashVel = _lean = _leanVel = _leanTarget = 0f;
        _shieldDamage = _coreDamage = 0f;
        _smokeUntil = _embersUntil = 0f;
        _nextDischarge = Time.time + Random.Range(0.5f, 2f);
        if (_debris) _debris.Clear();
        if (_dust) _dust.Clear();
        if (_sparks) _sparks.Clear();
        KillArcs();
    }

    /// <summary>Plays the transition into a stage the obelisk has just reached.</summary>
    public void PlayStage(ObeliskStage stage)
    {
        if (!_built) { _stage = stage; return; }

        var previous = _stage;
        _stage = stage;
        switch (stage)
        {
            case ObeliskStage.Exposed:
                if (previous == ObeliskStage.Shielded) _shieldBreak = StartCoroutine(ShieldBreakRoutine());
                break;
            case ObeliskStage.Cracked:
                CrackedBurst();
                break;
            case ObeliskStage.Destroyed:
                if (_shieldBreak != null) StopCoroutine(_shieldBreak);
                _shieldBreak = null;
                _shieldVisible = false;
                _shieldBurst = 0f;
                if (_collapse == null) _collapse = StartCoroutine(CollapseRoutine());
                break;
            default:
                Snap(stage);
                break;
        }
    }

    /// <summary>The shield just lost <paramref name="amount"/> points. Reactions are paced, not per frame.</summary>
    public void ShieldHit(float amount)
    {
        if (!_built || !_shieldVisible) return;
        _shieldDamage += amount;
        if (Time.time < _nextShieldReact || _shieldDamage < HitReactThreshold) return;

        float power = Mathf.Clamp01(_shieldDamage / 8f);
        _shieldDamage = 0f;
        _nextShieldReact = Time.time + Random.Range(0.18f, 0.35f);

        // The Breakers work it from the ground, so most impacts land on the lower half of the bubble.
        float ang = Random.value < 0.75f ? Random.Range(200f, 340f) : Random.Range(0f, 360f);
        _hitPoint = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad)) * Random.Range(0.78f, 0.96f);
        _hitAge = 0f;

        Vector3 p = _shield.TransformPoint(_hitPoint * 0.5f);
        Vector3 outward = (p - _shield.position).normalized;
        Sparks(p, outward, 6 + Mathf.RoundToInt(10 * power), 4f, 0.8f);
        if (Random.value < 0.4f) Zap(p, ShieldRimPoint(ang + Random.Range(-70f, 70f)), 0.05f, 0.14f, ArcColor(0.6f));
        _poolKick = Mathf.Max(_poolKick, 0.4f + 0.4f * power);
        Crackle(0.3f + 0.5f * power);
    }

    /// <summary>The core just lost <paramref name="amount"/> health: it flinches, chips and spits sparks.</summary>
    public void CoreHit(float amount)
    {
        if (!_built || _stage == ObeliskStage.Destroyed) return;
        _coreDamage += amount;
        if (Time.time < _nextCoreReact || _coreDamage < HitReactThreshold) return;

        float power = Mathf.Clamp01(_coreDamage / 8f);
        _coreDamage = 0f;
        _nextCoreReact = Time.time + Random.Range(0.22f, 0.4f);

        Flash(Color.Lerp(violetColor, Color.white, 0.5f), 0.25f + 0.35f * power);
        _squashVel -= 0.5f + 0.7f * power;
        _leanVel += Random.Range(-40f, 40f) * (0.4f + power);
        _shake = Mathf.Max(_shake, 0.35f + 0.4f * power);
        _glitchKick = Mathf.Max(_glitchKick, 0.12f + 0.25f * power);

        // Hits land low, where the Breakers can reach: a random spot on the left or right edge.
        float v = Random.Range(0.08f, 0.45f);
        Vector2 row = Row(v);
        bool left = Random.value < 0.5f;
        Vector3 p = BodyPoint(new Vector2(left ? row.x + 0.02f : row.y - 0.02f, v));
        Vector3 outward = (left ? -_billboard.right : _billboard.right) + Vector3.up * 0.3f;
        Chips(p, outward, 3 + Mathf.RoundToInt(6 * power), 2.5f);
        Sparks(p, outward, 6 + Mathf.RoundToInt(12 * power), 4.5f, 0.8f);
        if (power > 0.5f) Ring(violetColor, 0.7f);
        Crackle(0.35f + 0.5f * power);
    }

    /// <summary>How loud the hum is right now (0..1). The holes pulse and the statue buzzes with it.</summary>
    public void SetHum(float level01) => _hum = Mathf.Clamp01(level01);

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        _site = GetComponent<TaskSite>();
        _mpb = new MaterialPropertyBlock();
        if (!HasArt) return;

        BuildStatue();
        BuildShield();
        BuildGround();
        _sparks = BuildParticles("Sparks", 600, gravity: 1.3f, drag: 0.4f, noisy: false, collide: true,
                                 mode: ParticleSystemRenderMode.Stretch, softness: 2.4f, additive: 1f, intensity: 3f);
        _debris = BuildParticles("Debris", 400, gravity: 2.2f, drag: 0.2f, noisy: false, collide: true,
                                 mode: ParticleSystemRenderMode.Billboard, softness: 0.5f, additive: 0f, intensity: 1f);
        _dust   = BuildParticles("Dust", 300, gravity: -0.03f, drag: 1.2f, noisy: true, collide: false,
                                 mode: ParticleSystemRenderMode.Billboard, softness: 1.1f, additive: 0f, intensity: 1f);
        _motes  = BuildParticles("Motes", 300, gravity: -0.02f, drag: 1.6f, noisy: true, collide: false,
                                 mode: ParticleSystemRenderMode.Billboard, softness: 1.8f, additive: 0.8f, intensity: 2f);
        BuildArcs();
        BuildAudio();

        _built = true;
        _mood = MoodFor(_stage);
        FaceCamera();
    }

    void OnDisable()
    {
        // Coroutines die with the component; leave the look in a state Snap can pick up from.
        _collapse = null;
        _shieldBreak = null;
        _collapsing = false;
        UnhookDialogue();
    }

    void Update()
    {
        if (!_built) return;

        float dt = Time.deltaTime;
        float t = Time.time;
        _fxTime += dt;

        HookDialogue();

        _mood = Mood.Lerp(_mood, MoodFor(_stage), 1f - Mathf.Exp(-dt * 2.5f));
        _flash *= Mathf.Exp(-dt * 6f);
        _glitchKick *= Mathf.Exp(-dt * 5f);
        _shake *= Mathf.Exp(-dt * 4f);
        _shardKick *= Mathf.Exp(-dt * 7f);
        _poolKick *= Mathf.Exp(-dt * 8f);
        _hitAge += dt;
        _speak = Mathf.MoveTowards(_speak, _speaking ? 1f : 0f, dt * (_speaking ? 4f : 1.2f));

        // Integrated rather than time * speed, so the pulses speed up smoothly when the mood changes
        // instead of lurching. Wrapped by a whole number, which frac() in the shader cannot see.
        _corePhase = Mathf.Repeat(_corePhase + dt * (0.5f + 0.9f * _mood.activity + 1.5f * _overload), 64f);

        UpdateFlicker(t);
        StepSprings(dt);
        ApplyTransforms(t);
        ApplyShaders(t);
        UpdateShield(t);
        UpdateArcs(dt);
        UpdateGround(dt);
        UpdateAmbient(dt, t);
        UpdateDischarges(t);
    }

    void LateUpdate()
    {
        if (_built) FaceCamera();
    }

    // ------------------------------------------------------------------ transitions

    void StopTransitions()
    {
        if (_collapse != null) StopCoroutine(_collapse);
        if (_shieldBreak != null) StopCoroutine(_shieldBreak);
        _collapse = null;
        _shieldBreak = null;
        _collapsing = false;
        _overload = 0f;
    }

    /// <summary>The bubble flares, frays outward and is gone, throwing sparks off its whole rim.</summary>
    IEnumerator ShieldBreakRoutine()
    {
        _shieldVisible = false;   // the burst keeps it on screen while it flares
        Flash(energyColor, 0.6f);
        _glitchKick = Mathf.Max(_glitchKick, 0.5f);
        _shake = Mathf.Max(_shake, 0.6f);
        _poolKick = 1f;
        Crackle(1f);
        Thump(0.8f);
        Ring(energyColor, 1.3f);

        for (int i = 0; i < 24; i++)
        {
            float ang = i * 15f + Random.Range(-6f, 6f);
            Vector3 p = ShieldRimPoint(ang);
            Sparks(p, (p - _shield.position).normalized, 3, 6f, 0.5f);
        }

        const float dur = 0.5f;
        bool second = false;
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            float k = e / dur;
            _shieldBurst = k < 0.15f ? k / 0.15f : 1f - (k - 0.15f) / 0.85f;
            _shieldScale = 1f + 0.18f * Mathf.SmoothStep(0f, 1f, k);
            if (!second && k > 0.3f)
            {
                second = true;
                Ring(violetColor, 0.9f);
                Zap(ShieldRimPoint(Random.Range(0f, 360f)), BodyPoint(Holes[Random.Range(0, Holes.Length)]), 0.1f, 0.25f, ArcColor(1f));
            }
            yield return null;
        }
        _shieldBurst = 0f;
        _shieldScale = 1f;
        _shieldBreak = null;
    }

    /// <summary>Half its health gone: a violent stutter, a burst of rubble and arcs.</summary>
    void CrackedBurst()
    {
        Flash(warningColor, 0.6f);
        _glitchKick = Mathf.Max(_glitchKick, 0.6f);
        _shake = Mathf.Max(_shake, 0.8f);
        _squashVel -= 1.4f;
        _poolKick = 1f;
        Ring(warningColor, 1.1f);
        Crackle(1f);
        Thump(0.6f);

        Vector3 centre = BodyPoint(ShardsCentre);
        for (int i = 0; i < 3; i++)
            Discharge(0.9f);
        for (int i = 0; i < 4; i++)
        {
            Vector2 tip = ShardTips[Random.Range(0, ShardTips.Length)];
            Vector3 p = ShardPoint(tip);
            Sparks(p, (p - centre).normalized, 8, 6f, 0.6f);
        }
        for (int i = 0; i < 4; i++)
        {
            float v = Random.Range(0.1f, 0.7f);
            Vector2 row = Row(v);
            Vector3 p = BodyPoint(new Vector2(Random.Range(row.x, row.y), v));
            Chips(p, (p - centre).normalized + Vector3.up * 0.4f, 6, 3f);
        }
    }

    /// <summary>
    /// Overload, a bang, then the break line sweeps down to the plinth while the shards fly off and
    /// rubble pours from the line; the stump settles with a dust ring and smokes for a while.
    /// </summary>
    IEnumerator CollapseRoutine()
    {
        _collapsing = true;
        _shieldVisible = false;

        // 1. Overload: the holes blaze, it shakes harder and harder, arcs everywhere.
        const float overload = 0.6f;
        for (float e = 0f; e < overload; e += Time.deltaTime)
        {
            float k = e / overload;
            _overload = k;
            _shake = Mathf.Max(_shake, 0.4f + 0.9f * k);
            _glitchKick = Mathf.Max(_glitchKick, 0.2f + 0.5f * k);
            if (Random.value < 0.3f) Discharge(0.5f + 0.5f * k);
            yield return null;
        }
        _overload = 0f;

        // 2. Bang.
        Flash(Color.white, 2f);
        _poolKick = 1f;
        Ring(Color.white, 1.6f);
        Ring(violetColor, 1.2f);
        Thump(1f);
        Rumble();
        Crackle(1f);

        Vector3 centre = BodyPoint(ShardsCentre);
        foreach (var tip in ShardTips)
        {
            Vector3 p = ShardPoint(tip);
            Sparks(p, (p - centre).normalized, 8, 8f, 0.6f);
        }
        for (int i = 0; i < 10; i++)
        {
            float v = Random.Range(0.3f, 0.95f);
            Vector2 row = Row(v);
            Vector3 p = BodyPoint(new Vector2(Random.Range(row.x, row.y), v));
            Chips(p, (p - centre).normalized + Vector3.up * 0.5f, 4, 4f);
        }
        Dust(BodyPoint(new Vector2(0.5f, 0.5f)), 10, 2.5f, 0.6f);

        // 3. Crumble from the top of the head down to the plinth. Squared, so it gathers speed like
        //    falling rubble rather than wiping down at an even rate.
        float top = 1.02f;
        for (float e = 0f; e < collapseSeconds; e += Time.deltaTime)
        {
            float k = Mathf.Clamp01(e / collapseSeconds);
            _crumble = Mathf.Lerp(top, stumpHeight, k * k);
            float fly = Mathf.Clamp01(e / 0.6f);
            _shardsFling = 0.9f * Mathf.SmoothStep(0f, 1f, fly);
            _shardsAlpha = 1f - fly;
            _haloFade = Mathf.Clamp01(1f - k * 2f);
            _leanTarget = -2.5f * k;
            _shake = Mathf.Max(_shake, 0.35f);
            EmitAlongBreak(Time.deltaTime);
            yield return null;
        }

        // 4. Settle.
        _crumble = stumpHeight;
        _shardsAlpha = 0f;
        _shardsFling = 0f;
        _haloFade = 0f;
        _leanTarget = 0f;
        _squashVel -= 2.2f;
        Ring(new Color(0.55f, 0.36f, 0.26f), 1f);
        Vector3 foot = transform.position + Vector3.up * 0.3f;
        for (int i = 0; i < 16; i++)
        {
            float a = i / 16f * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Dust(foot + dir * 2f * size, 1, 1.5f, 0.2f);
        }
        _smokeUntil = Time.time + 9f;
        _embersUntil = Time.time + 5f;
        _collapsing = false;
        _collapse = null;
    }

    /// <summary>Rubble, dust and the odd spark pouring off the break line while it sweeps down.</summary>
    void EmitAlongBreak(float dt)
    {
        _breakAcc += dt * 70f;
        float v = Mathf.Clamp(_crumble, 0.02f, 0.98f);
        Vector2 row = Row(v);
        while (_breakAcc >= 1f)
        {
            _breakAcc -= 1f;
            Vector3 p = BodyPoint(new Vector2(Random.Range(row.x, row.y), v));
            Vector3 dir = new Vector3(Random.Range(-1f, 1f), Random.Range(0f, 0.6f), Random.Range(-0.5f, 0.5f));
            float pick = Random.value;
            if (pick < 0.7f) Chips(p, dir, 1, 1.6f);
            else if (pick < 0.9f) Dust(p, 1, 0.8f, 0.3f);
            else Sparks(p, dir + Vector3.up, 2, 3f, 0.8f);
        }
    }

    // ------------------------------------------------------------------ per frame

    void UpdateFlicker(float t)
    {
        // Mostly steady, with stutters and dropouts that get more frequent the less stable it is.
        float n = Mathf.PerlinNoise(t * 11f, 0.31f);
        float act = _mood.activity;
        bool drop = Mathf.PerlinNoise(t * 3.1f, 5.3f) < 0.16f + 0.18f * act;
        _flicker = Mathf.Lerp(0.9f, 0.5f + 0.5f * n, act) * (drop ? 0.35f : 1f);
    }

    void StepSprings(float dt)
    {
        if (dt <= 0f) return;

        // Stone: stiff and well damped, so a hit is a thud, not a wobble.
        int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 120f), 1, 8);
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            _squashVel += (-420f * _squash - 18f * _squashVel) * h;
            _squash += _squashVel * h;
            _leanVel += (-120f * (_lean - _leanTarget) - 14f * _leanVel) * h;
            _lean += _leanVel * h;
        }
        _squash = Mathf.Clamp(_squash, -0.08f, 0.08f);
        _lean = Mathf.Clamp(_lean, -6f, 6f);
    }

    void ApplyTransforms(float t)
    {
        // The body buzzes with the hum: stepped noise rather than a smooth sway, so it reads as
        // vibration in something heavy.
        float amp = (0.004f * _mood.activity + 0.012f * _hum + 0.06f * _shake + 0.02f * _speak) * size;
        if (t >= _nextVibStep)
        {
            _nextVibStep = t + 1f / 30f;
            _vib = Random.insideUnitCircle;
        }
        _body.localPosition = new Vector3(_vib.x * amp, _vib.y * amp * 0.4f, 0f);
        _body.localScale = new Vector3(size * (1f - _squash * 0.5f), size * (1f + _squash), size);
        _body.localRotation = Quaternion.Euler(0f, 0f, _lean);

        // The shards twitch at 24 Hz, strain outward and back, and kick on every discharge.
        if (t >= _nextShardStep)
        {
            _nextShardStep = t + 1f / 24f;
            _shardJitter = Random.insideUnitCircle;
        }
        float jitter = _mood.jitter + 0.03f * _shardKick + 0.02f * _hum + 0.03f * _speak;
        float strain = 1f + (0.006f + 0.01f * _mood.activity) * Mathf.Sin(t * 1.9f)
                          + 0.05f * _shardKick + 0.02f * _speak + _shardsFling;
        _shardsPivot.localPosition = _shardsCentreLocal + (Vector3)(_shardJitter * jitter);
        _shardsPivot.localScale = new Vector3(strain, strain, 1f);
        // No shard art means the renderer only holds the body as a placeholder: keep it off.
        _shardsRenderer.enabled = shardsSprite && _shardsAlpha > 0.001f;
        _shardsRenderer.color = new Color(1f, 1f, 1f, _shardsAlpha);
    }

    void ApplyShaders(float t)
    {
        float health = _site ? _site.Health01 : 1f;
        float crack = _stage >= ObeliskStage.Exposed ? Mathf.Clamp01((1f - health) * 1.15f) : 0f;
        if (_stage == ObeliskStage.Destroyed) crack = 1f;

        float singing = _hum * (0.5f + 0.5f * Mathf.Sin(t * 2.6f));
        float holes = _mood.holes * _flicker + 0.9f * singing + 2.2f * _speak + 3f * _overload;
        float core = _mood.core + 0.5f * _hum + 2f * _overload;
        float shards = _mood.shards * _flicker + 0.4f * _shardKick + 0.3f * _speak;
        float tint = _mood.tint * (_stage == ObeliskStage.Cracked ? 0.6f + 0.4f * Mathf.Sin(t * 4.2f) : 1f);
        float glitch = Mathf.Clamp01(_mood.glitch + _glitchKick + 0.25f * _speak * _flicker);
        float edgeGlow = _crumble < 1f ? (_collapsing ? 1.4f : 0.5f * Mathf.Clamp01((_embersUntil - Time.time) / 5f)) : 0f;

        ApplySprite(_bodyRenderer, crack, holes, core, shards, tint, glitch, _crumble, edgeGlow);
        ApplySprite(_depthRenderer, crack, holes, core, shards, tint, glitch, _crumble, edgeGlow);
        ApplySprite(_shardsRenderer, 0f, 0f, 0f, shards, tint, glitch, Intact, 0f);

        if (_glowRenderer)
        {
            _glowRenderer.GetPropertyBlock(_mpb);
            Color glowColor = Color.Lerp(violetColor, energyColor, 0.3f);
            if (_stage == ObeliskStage.Cracked) glowColor = Color.Lerp(glowColor, warningColor, 0.35f * (0.5f + 0.5f * Mathf.Sin(t * 4.2f)));
            _mpb.SetColor(ColorID, glowColor + _flashColor * (_flash * 0.4f));
            _mpb.SetFloat(IntensityID, haloStrength * _haloFade * (_mood.halo * _flicker + 0.5f * _flash + 0.25f * _speak + 0.4f * _overload));
            _mpb.SetFloat(AdditiveID, 0.85f);
            _mpb.SetFloat(ShapeID, 0f);
            _glowRenderer.SetPropertyBlock(_mpb);
            _glowRenderer.enabled = _haloFade > 0.001f;
        }
    }

    void ApplySprite(SpriteRenderer r, float crack, float holes, float core, float shards, float tint,
                     float glitch, float crumble, float edgeGlow)
    {
        if (!r) return;
        // Read first so whatever the SpriteRenderer keeps in the block survives.
        r.GetPropertyBlock(_mpb);
        _mpb.SetFloat(FxTimeID, _fxTime);
        _mpb.SetFloat(SaturationID, _mood.saturation);
        _mpb.SetFloat(BrightnessID, _mood.brightness);
        _mpb.SetColor(TintColorID, warningColor);
        _mpb.SetFloat(TintAmountID, tint);
        _mpb.SetColor(FlashColorID, _flashColor);
        _mpb.SetFloat(FlashAmountID, _flash);
        _mpb.SetColor(EnergyColorID, energyColor);
        _mpb.SetColor(HoleColorID, violetColor);
        _mpb.SetFloat(HoleGlowID, holes);
        _mpb.SetFloat(CoreFlowID, core);
        _mpb.SetFloat(CorePhaseID, _corePhase);
        _mpb.SetFloat(ShardGlowID, shards);
        _mpb.SetFloat(CrackAmountID, crack);
        _mpb.SetFloat(GlitchID, glitch);
        _mpb.SetFloat(CrumbleHeightID, crumble);
        _mpb.SetFloat(CrumbleEdgeGlowID, edgeGlow);
        r.SetPropertyBlock(_mpb);
    }

    void UpdateShield(float t)
    {
        bool show = shieldMaterial && (_shieldVisible || _shieldBurst > 0.001f);
        _shieldRenderer.enabled = show;
        if (!show) return;

        float strength = _site && _site.maxShield > 0f ? _site.Shield01 : 1f;
        if (!_shieldVisible) strength = 0f;   // breaking: the burst carries it

        // It stutters harder the weaker it gets.
        float weak = 1f - strength;
        float n = Mathf.PerlinNoise(t * 17f, 2.2f);
        bool drop = Mathf.PerlinNoise(t * 4.3f, 9.1f) < 0.1f + 0.35f * weak;
        float flick = (0.72f + 0.28f * n) * (drop ? 0.25f : 1f);

        _shieldRenderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(FxTimeID, _fxTime);
        _mpb.SetFloat(StrengthID, Mathf.Max(0.04f, strength));
        _mpb.SetFloat(FlickerID, flick);
        _mpb.SetFloat(BurstID, _shieldBurst);
        _mpb.SetVector(HitUVID, new Vector4(_hitPoint.x, _hitPoint.y, _hitAge, 0f));
        _mpb.SetFloat(IntensityID, 0.85f * (1f + 1.5f * _shieldBurst) + 0.3f * _speak);
        _shieldRenderer.SetPropertyBlock(_mpb);

        float d = shieldDiameter * size * _shieldScale;
        _shield.localScale = new Vector3(d, d, 1f);
    }

    void UpdateDischarges(float t)
    {
        if (_stage == ObeliskStage.Destroyed || _collapsing || dischargeRate <= 0f) return;
        if (t < _nextDischarge) return;

        float act = Mathf.Clamp01(_mood.activity + 0.5f * _hum + 0.6f * _speak);
        _nextDischarge = t + Random.Range(0.5f, 1.5f) * Mathf.Lerp(4.2f, 0.45f, act) / dischargeRate;
        Discharge(Mathf.Clamp01(Random.Range(0.25f, 0.6f) + 0.5f * act * Random.value));
    }

    /// <summary>
    /// One discharge: an arc from a shard tip to another tip, back into a hole, or (while shielded) out
    /// to the bubble, with sparks spat from both ends, a crackle, and a twitch through the shards.
    /// </summary>
    void Discharge(float power)
    {
        // Once the shell is open, some of it comes straight out of the exposed core.
        Vector3 a = _stage >= ObeliskStage.Exposed && Random.value < 0.25f
            ? BodyPoint(new Vector2(Random.Range(CoreRect.xMin, CoreRect.xMax), Random.Range(CoreRect.yMin, CoreRect.yMax)))
            : ShardPoint(ShardTips[Random.Range(0, ShardTips.Length)]);
        Vector3 b;
        float r = Random.value;
        if (_shieldVisible && r < 0.45f) b = ShieldRimPoint(Random.Range(0f, 360f));
        else if (r < 0.75f) b = ShardPoint(ShardTips[Random.Range(0, ShardTips.Length)]);
        else b = BodyPoint(Holes[Random.Range(0, Holes.Length)]);
        if ((b - a).sqrMagnitude < 0.3f * size * size)
            b = a + (Random.onUnitSphere + Vector3.up) * size;

        Zap(a, b, Mathf.Lerp(0.05f, 0.12f, power), Mathf.Lerp(0.09f, 0.26f, power), ArcColor(power));

        Vector3 centre = BodyPoint(ShardsCentre);
        Vector3 outward = (a - centre).normalized;
        int count = Mathf.RoundToInt(Mathf.Lerp(5f, 22f, power));
        Sparks(a, outward, count, Mathf.Lerp(2.5f, 7f, power), 0.7f);
        Sparks(b, Random.onUnitSphere + Vector3.up * 0.5f, count / 2, Mathf.Lerp(2f, 5f, power), 0.9f);

        Crackle(power);
        _shardKick = Mathf.Max(_shardKick, power);
        _poolKick = Mathf.Max(_poolKick, power);
        _glitchKick = Mathf.Max(_glitchKick, 0.15f * power * _mood.activity);
        if (power > 0.75f) Ring(violetColor, 0.6f);
    }

    /// <summary>Ambient emission: motes out of the holes while it sings or speaks, smoke and embers after it falls.</summary>
    void UpdateAmbient(float dt, float t)
    {
        if (_stage != ObeliskStage.Destroyed && !_collapsing)
        {
            float rate = 0.6f * _mood.activity + 6f * _hum + 14f * _speak;
            _moteAcc += rate * dt;
            while (_moteAcc >= 1f)
            {
                _moteAcc -= 1f;
                bool pipe = Random.value < 0.35f;
                Vector2 uv;
                Vector3 dir;
                if (pipe)
                {
                    Vector4 p = Pipes[Random.Range(0, Pipes.Length)];
                    uv = new Vector2(p.x, p.y);
                    dir = _billboard.right * p.z + Vector3.up * p.w;
                }
                else
                {
                    uv = Holes[Random.Range(0, Holes.Length)];
                    dir = Vector3.up * 0.6f - _billboard.forward * 0.8f + _billboard.right * Random.Range(-0.4f, 0.4f);
                }
                Color c = Clamp01(Color.Lerp(violetColor, energyColor, Random.value * 0.4f));
                c.a = 0.85f;
                Emit(_motes, BodyPoint(uv), dir.normalized * Random.Range(0.4f, 1.4f) * size,
                     Random.Range(0.05f, 0.12f) * size, Random.Range(1.2f, 2.4f), c);
            }
            return;
        }

        // After the fall: the stump smokes and spits the odd ember for a while.
        float top = Mathf.Clamp(_crumble, 0.02f, 0.98f);
        Vector2 row = Row(top);
        if (t < _smokeUntil)
        {
            float k = (_smokeUntil - t) / 9f;
            _smokeAcc += dt * 7f * k;
            while (_smokeAcc >= 1f)
            {
                _smokeAcc -= 1f;
                Vector3 p = BodyPoint(new Vector2(Random.Range(row.x, row.y), top));
                Color c = new Color(0.2f, 0.18f, 0.2f, 0.4f * k + 0.1f);
                Emit(_dust, p, new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.4f, 0.9f), Random.Range(-0.2f, 0.2f)) * size,
                     Random.Range(0.5f, 1.2f) * size, Random.Range(2.5f, 4.5f), c);
            }
        }
        if (t < _embersUntil && Random.value < dt * 3f)
        {
            Vector3 p = BodyPoint(new Vector2(Random.Range(row.x, row.y), top));
            Sparks(p, Vector3.up, 2, 2f, 0.9f);
        }
    }

    // ------------------------------------------------------------------ the voice

    void HookDialogue()
    {
        var d = DialogueSystem.Instance;
        if (d == _dialogue) return;
        UnhookDialogue();
        _dialogue = d;
        if (_dialogue == null) return;
        _dialogue.OnLineStarted += OnLineStarted;
        _dialogue.OnDialogueEnded += OnDialogueEnded;
    }

    void UnhookDialogue()
    {
        if (_dialogue != null)
        {
            _dialogue.OnLineStarted -= OnLineStarted;
            _dialogue.OnDialogueEnded -= OnDialogueEnded;
        }
        _dialogue = null;
        _speaking = false;
    }

    void OnLineStarted(DialogueLine line)
    {
        bool mine = line != null && !string.IsNullOrEmpty(voiceSpeaker) &&
                    string.Equals(line.speaker?.Trim(), voiceSpeaker, System.StringComparison.OrdinalIgnoreCase);
        _speaking = mine && _stage != ObeliskStage.Destroyed;
        if (_speaking) Speak();
    }

    void OnDialogueEnded() => _speaking = false;

    /// <summary>Each line it says: the holes flare, sound pours out of them, everything shudders.</summary>
    void Speak()
    {
        Flash(violetColor, 0.5f);
        _glitchKick = Mathf.Max(_glitchKick, 0.35f);
        _shake = Mathf.Max(_shake, 0.5f);
        _shardKick = 1f;
        _poolKick = 1f;
        Ring(violetColor, 1f);
        Thump(0.5f);
        Crackle(0.8f);
        foreach (var h in Holes)
        {
            Vector3 p = BodyPoint(h);
            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = Vector3.up * 0.4f - _billboard.forward + Random.insideUnitSphere * 0.6f;
                Color c = Clamp01(Color.Lerp(violetColor, Color.white, Random.value * 0.3f));
                Emit(_motes, p, dir.normalized * Random.Range(1f, 2.5f) * size, Random.Range(0.06f, 0.14f) * size,
                     Random.Range(1f, 2f), c);
            }
        }
    }

    // ------------------------------------------------------------------ effects primitives

    void Flash(Color c, float amount)
    {
        if (amount < _flash) return;
        _flash = amount;
        _flashColor = c;
    }

    Color ArcColor(float power)
    {
        Color c = Color.Lerp(energyColor, violetColor, Random.value * 0.6f);
        if (_stage == ObeliskStage.Cracked && Random.value < 0.35f) c = warningColor;
        return Clamp01(Color.Lerp(c, Color.white, 0.25f + 0.35f * power));
    }

    void Sparks(Vector3 p, Vector3 dir, int count, float speed, float spread)
    {
        if (!_sparks || count <= 0) return;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;
        Color hot = Clamp01(energyColor);
        Color cold = Clamp01(violetColor);
        for (int i = 0; i < count; i++)
        {
            Vector3 v = (dir + Random.insideUnitSphere * spread).normalized * speed * Random.Range(0.45f, 1.2f)
                      + Vector3.up * Random.Range(0f, 1.5f);
            Color c = Random.value < 0.6f ? Color.Lerp(Color.white, hot, Random.value * 0.6f)
                                          : Color.Lerp(cold, Color.white, Random.value * 0.4f);
            Emit(_sparks, p, v * size, Random.Range(0.03f, 0.07f) * size, Random.Range(0.25f, 0.7f), c);
        }
    }

    void Chips(Vector3 p, Vector3 dir, int count, float speed)
    {
        if (!_debris || count <= 0) return;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;
        for (int i = 0; i < count; i++)
        {
            Vector3 v = (dir + Random.insideUnitSphere * 0.6f) * speed * Random.Range(0.4f, 1.1f) + Vector3.up * Random.Range(0.5f, 2.5f);
            Color c = Color.Lerp(new Color(0.16f, 0.2f, 0.27f), new Color(0.34f, 0.4f, 0.5f), Random.value);
            Emit(_debris, p, v * size, Random.Range(0.05f, 0.16f) * size, Random.Range(1.2f, 2.4f), c);
        }
    }

    void Dust(Vector3 p, int count, float spread, float rise)
    {
        if (!_dust || count <= 0) return;
        for (int i = 0; i < count; i++)
        {
            Vector2 h = Random.insideUnitCircle * spread;
            Vector3 v = new Vector3(h.x, Random.Range(0.2f, 1f) * rise, h.y);
            Color c = new Color(0.55f, 0.37f, 0.27f, Random.Range(0.3f, 0.5f));
            Emit(_dust, p + Random.insideUnitSphere * 0.3f * size, v * size, Random.Range(0.8f, 2.2f) * size, Random.Range(2.5f, 5f), c);
        }
    }

    static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float sizeM, float life, Color c)
    {
        if (!ps) return;
        var ep = new ParticleSystem.EmitParams
        {
            position = pos,
            velocity = vel,
            startSize = sizeM,
            startLifetime = life,
            startColor = c,
            applyShapeToPosition = false,
        };
        ps.Emit(ep, 1);
    }

    static Color Clamp01(Color c)
    {
        // Particle and line colours are 8-bit: fold HDR brightness back into range keeping the hue.
        float m = Mathf.Max(1f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
        return new Color(c.r / m, c.g / m, c.b / m, Mathf.Clamp01(c.a));
    }

    // ------------------------------------------------------------------ arcs

    void BuildArcs()
    {
        _arcs = new Arc[8];
        var root = new GameObject("Arcs").transform;
        root.SetParent(transform, false);

        var mpb = new MaterialPropertyBlock();
        mpb.SetTexture(MainTexID, ArcProfile());
        mpb.SetFloat(ShapeID, 0f);
        mpb.SetFloat(AdditiveID, 1f);
        mpb.SetFloat(IntensityID, 2.4f);
        mpb.SetColor(ColorID, Color.white);

        for (int i = 0; i < _arcs.Length; i++)
        {
            var go = new GameObject("Arc");
            go.transform.SetParent(root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = ArcSegments;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;
            lr.sharedMaterial = softGlowMaterial;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            lr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            lr.sortingOrder = 2;
            lr.SetPropertyBlock(mpb);
            lr.enabled = false;
            _arcs[i] = new Arc { line = lr };
        }
    }

    void Zap(Vector3 a, Vector3 b, float width, float life, Color c)
    {
        if (_arcs == null || !softGlowMaterial) return;

        // A free arc, or the oldest one if they are all busy.
        Arc pick = null;
        foreach (var arc in _arcs)
        {
            if (!arc.alive) { pick = arc; break; }
            if (pick == null || arc.age / arc.life > pick.age / pick.life) pick = arc;
        }

        pick.a = a;
        pick.b = b;
        pick.age = 0f;
        pick.life = life;
        pick.width = width * size;
        pick.color = c;
        pick.alive = true;
        pick.nextJag = 0f;
        pick.line.enabled = true;
        Jag(pick);
    }

    /// <summary>Re-rolls the arc's path: a bowed, jagged line between its ends, re-drawn every few frames.</summary>
    void Jag(Arc arc)
    {
        Vector3 d = arc.b - arc.a;
        float len = d.magnitude;
        Vector3 view = Camera.main ? Camera.main.transform.forward : _billboard.forward;
        Vector3 side = Vector3.Cross(d, view);
        side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.up;
        float bow = Random.Range(-0.25f, 0.25f) * len;

        for (int i = 0; i < ArcSegments; i++)
        {
            float f = i / (float)(ArcSegments - 1);
            float envelope = Mathf.Sin(f * Mathf.PI);
            Vector3 p = arc.a + d * f + side * (bow * envelope + Random.Range(-0.12f, 0.12f) * len * envelope)
                      + Random.insideUnitSphere * 0.04f * len * envelope;
            arc.line.SetPosition(i, p);
        }
    }

    void UpdateArcs(float dt)
    {
        if (_arcs == null) return;
        float now = Time.time;
        foreach (var arc in _arcs)
        {
            if (!arc.alive) continue;
            arc.age += dt;
            if (arc.age >= arc.life)
            {
                arc.alive = false;
                arc.line.enabled = false;
                continue;
            }
            if (now >= arc.nextJag)
            {
                arc.nextJag = now + 0.035f;
                Jag(arc);
            }

            // Stutters on and off rather than fading smoothly: that is what makes it read as current.
            arc.line.enabled = Random.value > 0.2f;
            float k = 1f - arc.age / arc.life;
            Color c = arc.color;
            c.a = k * k;
            arc.line.startColor = c;
            arc.line.endColor = c;
            arc.line.widthMultiplier = arc.width * Random.Range(0.6f, 1f);
        }
    }

    void KillArcs()
    {
        if (_arcs == null) return;
        foreach (var arc in _arcs)
        {
            arc.alive = false;
            arc.line.enabled = false;
        }
    }

    static Texture2D ArcProfile()
    {
        if (s_arcProfile) return s_arcProfile;

        // Across the line: a hot thin core and a little falloff. Along it: flat (Stretch mode).
        const int h = 32;
        s_arcProfile = new Texture2D(4, h, TextureFormat.RGBA32, false, true)
        {
            name = "ObeliskArcProfile",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[4 * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h * 2f - 1f;
            float a = Mathf.Clamp01(Mathf.Exp(-v * v * 18f) + 0.35f * Mathf.Exp(-v * v * 3.5f));
            byte b = (byte)Mathf.RoundToInt(a * 255f);
            for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(255, 255, 255, b);
        }
        s_arcProfile.SetPixels32(px);
        s_arcProfile.Apply(false, true);
        return s_arcProfile;
    }

    // ------------------------------------------------------------------ building

    void BuildStatue()
    {
        _billboard = new GameObject("Billboard").transform;
        _billboard.SetParent(transform, false);

        _body = new GameObject("Body").transform;
        _body.SetParent(_billboard, false);
        _body.localScale = Vector3.one * size;

        if (glowSprite && softGlowMaterial)
        {
            _glowRenderer = MakeSprite("Halo", _body, glowSprite, softGlowMaterial, -1);
            _glowRenderer.transform.localPosition = new Vector3(0f, 0f, 0.02f);   // just behind (the sprite faces -Z)
        }
        if (depthMaterial)
            _depthRenderer = MakeSprite("Depth", _body, bodySprite, depthMaterial, 0);
        _bodyRenderer = MakeSprite("Statue", _body, bodySprite, bodyMaterial, 0);

        // The shards scale about a point in the middle of the body, so straining pushes the left
        // pieces left and the right pieces right.
        _shardsCentreLocal = SpriteLocal(bodySprite, ShardsCentre);
        _shardsPivot = new GameObject("ShardsPivot").transform;
        _shardsPivot.SetParent(_body, false);
        _shardsPivot.localPosition = _shardsCentreLocal;
        _shardsRenderer = MakeSprite("Shards", _shardsPivot, shardsSprite ? shardsSprite : bodySprite, bodyMaterial, 1);
        _shardsRenderer.transform.localPosition = -_shardsCentreLocal;
        if (!shardsSprite) _shardsRenderer.enabled = false;
    }

    SpriteRenderer MakeSprite(string name, Transform parent, Sprite sprite, Material mat, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sharedMaterial = mat;
        sr.sortingOrder = order;
        sr.spriteSortPoint = SpriteSortPoint.Pivot;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        return sr;
    }

    void BuildShield()
    {
        var go = new GameObject("Shield");
        go.transform.SetParent(_billboard, false);
        go.AddComponent<MeshFilter>().sharedMesh = Quad();
        _shieldRenderer = go.AddComponent<MeshRenderer>();
        _shieldRenderer.sharedMaterial = shieldMaterial;
        _shieldRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _shieldRenderer.receiveShadows = false;
        _shieldRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _shieldRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        _shieldRenderer.enabled = shieldMaterial != null;
        _shield = go.transform;
        if (!shieldMaterial) _shieldVisible = false;
    }

    void BuildGround()
    {
        Vector3 normal = Vector3.up;
        Vector3 point = transform.position;
        int n = Physics.RaycastNonAlloc(transform.position + Vector3.up * 3f, Vector3.down, s_hits, 8f, ~0,
                                        QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var c = s_hits[i].collider;
            if (!c || c.transform.IsChildOf(transform) || c.attachedRigidbody || c is CharacterController) continue;
            if (s_hits[i].distance < best) { best = s_hits[i].distance; normal = s_hits[i].normal; point = s_hits[i].point; }
        }

        _ground = new GameObject("Ground").transform;
        _ground.SetParent(transform, false);
        float yaw = _rig ? _rig.yaw : FindRigYaw();
        _ground.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f);
        _ground.position = point + normal * 0.05f;

        if (!softGlowMaterial) return;

        // Blighted ground around it and a hard contact shadow under the plinth: nothing about the spot
        // should look like a place to rest.
        _blight = MakeGroundQuad("Blight", -4);
        _blight.transform.localScale = new Vector3(11f * size, 1f, 11f * size);
        _shadow = MakeGroundQuad("ContactShadow", -3);
        _shadow.transform.localScale = new Vector3(6.4f * size, 1f, 3.4f * size);
        _pool = MakeGroundQuad("Underlight", -2);
        _pool.transform.localScale = new Vector3(7f * size, 1f, 7f * size);

        _rings = new MeshRenderer[4];
        _ringAge = new float[_rings.Length];
        _ringColor = new Color[_rings.Length];
        _ringSize = new float[_rings.Length];
        for (int i = 0; i < _rings.Length; i++)
        {
            _rings[i] = MakeGroundQuad("Ring", -2);
            _rings[i].enabled = false;
            _ringAge[i] = RingLife;
        }

        _mpb.Clear();
        _mpb.SetFloat(ShapeID, 1f);
        _mpb.SetFloat(SoftnessID, 0.7f);
        _mpb.SetFloat(AdditiveID, 0f);
        _mpb.SetColor(ColorID, new Color(0.06f, 0.02f, 0.08f, 0.35f));
        _mpb.SetFloat(IntensityID, 1f);
        _blight.SetPropertyBlock(_mpb);

        _mpb.Clear();
        _mpb.SetFloat(ShapeID, 1f);
        _mpb.SetFloat(SoftnessID, 1.2f);
        _mpb.SetFloat(AdditiveID, 0f);
        _mpb.SetColor(ColorID, new Color(0.02f, 0f, 0.04f, 0.65f));
        _mpb.SetFloat(IntensityID, 1f);
        _shadow.SetPropertyBlock(_mpb);
    }

    MeshRenderer MakeGroundQuad(string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_ground, false);
        go.AddComponent<MeshFilter>().sharedMesh = GroundQuad();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = softGlowMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        mr.sortingOrder = order;   // ground decals draw before anything standing on them
        return mr;
    }

    void Ring(Color c, float strength)
    {
        if (_rings == null) return;
        int i = _nextRing;
        _nextRing = (_nextRing + 1) % _rings.Length;
        _ringAge[i] = 0f;
        _ringColor[i] = c * strength;
        _ringSize[i] = Mathf.Lerp(5f, 9f, Mathf.Clamp01(strength - 0.5f)) * size;
        _rings[i].enabled = true;
    }

    void UpdateGround(float dt)
    {
        if (_pool)
        {
            // A cold, stuttering underlight - dim, and gone when it dies.
            float glow = _mood.pool * _flicker + 0.5f * _poolKick + 0.4f * _speak + 0.6f * _flash + 0.8f * _overload;
            _mpb.Clear();
            _mpb.SetFloat(ShapeID, 1f);
            _mpb.SetFloat(SoftnessID, 1.6f);
            _mpb.SetFloat(AdditiveID, 1f);
            _mpb.SetColor(ColorID, Color.Lerp(violetColor, _flashColor, Mathf.Clamp01(_flash * 0.5f)));
            _mpb.SetFloat(IntensityID, glow);
            _pool.SetPropertyBlock(_mpb);
            _pool.enabled = glow > 0.002f;
        }

        if (_rings == null) return;
        for (int i = 0; i < _rings.Length; i++)
        {
            if (!_rings[i].enabled) continue;

            _ringAge[i] += dt;
            float k = _ringAge[i] / RingLife;
            if (k >= 1f) { _rings[i].enabled = false; continue; }

            // Fast and thin: a shock, not a ripple.
            float ease = 1f - (1f - k) * (1f - k) * (1f - k);
            float d = Mathf.Lerp(1.2f * size, _ringSize[i], ease);
            _rings[i].transform.localScale = new Vector3(d, 1f, d);

            _mpb.Clear();
            _mpb.SetFloat(ShapeID, 2f);
            _mpb.SetFloat(RingRadiusID, 0.85f);
            _mpb.SetFloat(RingWidthID, Mathf.Lerp(0.03f, 0.08f, k));
            _mpb.SetFloat(AdditiveID, 1f);
            _mpb.SetColor(ColorID, _ringColor[i]);
            _mpb.SetFloat(IntensityID, 2f * (1f - k) * (1f - k));
            _rings[i].SetPropertyBlock(_mpb);
        }
    }

    ParticleSystem BuildParticles(string name, int max, float gravity, float drag, bool noisy, bool collide,
                                  ParticleSystemRenderMode mode, float softness, float additive, float intensity)
    {
        if (!softGlowMaterial) return null;

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.gravityModifier = gravity;
        main.startSpeed = 0f;

        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;

        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.04f),
                    new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(fade);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = noisy
            ? new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.3f)))
            : new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));

        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 100f;
        limit.drag = drag;

        if (noisy)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.3f;
            noise.damping = true;
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        if (collide && _ground)
        {
            // Sparks and rubble skitter on the ground instead of falling through it.
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.Planes;
            collision.AddPlane(_ground);
            collision.bounce = 0.35f;
            collision.dampen = 0.45f;
            collision.lifetimeLoss = 0.15f;
            collision.radiusScale = 0.5f;
        }

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        if (mode == ParticleSystemRenderMode.Stretch)
        {
            r.velocityScale = 0.05f;
            r.lengthScale = 1.5f;
        }
        r.sharedMaterial = softGlowMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        var mpb = new MaterialPropertyBlock();
        mpb.SetFloat(ShapeID, 1f);
        mpb.SetFloat(SoftnessID, softness);
        mpb.SetFloat(AdditiveID, additive);
        mpb.SetColor(ColorID, Color.white);
        mpb.SetFloat(IntensityID, intensity);
        r.SetPropertyBlock(mpb);

        ps.Play();
        return ps;
    }

    // ------------------------------------------------------------------ sound

    void BuildAudio()
    {
        // On a child: Obelisk takes the root's AudioSource for its hum.
        var go = new GameObject("FxAudio");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 3f * size;
        _fxAudio = go.AddComponent<AudioSource>();
        _fxAudio.playOnAwake = false;
        _fxAudio.loop = false;
        _fxAudio.spatialBlend = 1f;
        // Shorter reach than the hum: from the windmill (~26 m) the crackle is a faint warning, not a nag.
        _fxAudio.minDistance = 4f;
        _fxAudio.maxDistance = 40f;
        _fxAudio.rolloffMode = AudioRolloffMode.Linear;
        _fxAudio.dopplerLevel = 0f;

        // Runtime clips do not survive leaving play mode, so a static left over from the last session
        // (domain reload off) compares equal to null here and is rebuilt.
        if (s_crackles == null || !s_crackles[0])
        {
            s_crackles = new AudioClip[4];
            for (int i = 0; i < s_crackles.Length; i++)
                s_crackles[i] = MakeCrackle(1234 + i * 97, 0.14f + 0.08f * i);
        }
        if (!s_thump) s_thump = MakeThump();
        if (!s_rumble) s_rumble = MakeRumble();
    }

    void Crackle(float power)
    {
        if (!_fxAudio || crackleVolume <= 0f || s_crackles == null) return;
        _fxAudio.pitch = Random.Range(0.85f, 1.3f);
        _fxAudio.PlayOneShot(s_crackles[Random.Range(0, s_crackles.Length)], crackleVolume * Mathf.Lerp(0.3f, 1f, power));
    }

    void Thump(float power)
    {
        if (!_fxAudio || crackleVolume <= 0f || !s_thump) return;
        _fxAudio.PlayOneShot(s_thump, crackleVolume * power);
    }

    void Rumble()
    {
        if (!_fxAudio || crackleVolume <= 0f || !s_rumble) return;
        _fxAudio.PlayOneShot(s_rumble, crackleVolume);
    }

    /// <summary>A short electrical crackle: sparse sharp bursts of bright noise over a quick falling whine.</summary>
    static AudioClip MakeCrackle(int seed, float seconds)
    {
        const int rate = 44100;
        int n = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
        var noise = new float[n];
        var rng = new System.Random(seed);

        // Sparse bursts with a sharp attack: what makes it sound like arcing rather than hiss.
        for (int i = 0; i < n;)
        {
            int len = rng.Next(60, 700);
            float amp = 0.25f + 0.75f * (float)rng.NextDouble();
            for (int k = 0; k < len && i + k < n; k++)
                noise[i + k] += ((float)rng.NextDouble() * 2f - 1f) * amp * Mathf.Exp(-k / (len * 0.22f));
            i += rng.Next(90, 1600);
        }

        var data = new float[n];
        float prev = 0f, phase = 0f;
        for (int k = 0; k < n; k++)
        {
            // Differencing tilts the noise toward the top end, so it crackles instead of rumbling.
            float bright = noise[k] - prev * 0.55f;
            prev = noise[k];

            float t = k / (float)rate;
            phase += Mathf.Lerp(2400f, 380f, Mathf.Clamp01(t / 0.08f)) / rate;
            float whine = Mathf.Sin(phase * Mathf.PI * 2f) * 0.3f * Mathf.Exp(-t * 28f);
            float env = Mathf.Exp(-t / seconds * 2.6f) * Mathf.Min(1f, k / 30f);
            data[k] = Mathf.Clamp((bright * 0.5f + whine) * env, -1f, 1f);
        }

        var clip = AudioClip.Create("ObeliskCrackle" + seed, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>A heavy low thud for the shield breaking, the cracks and its voice.</summary>
    static AudioClip MakeThump()
    {
        const int rate = 44100;
        const float seconds = 0.9f;
        int n = Mathf.RoundToInt(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(77);
        float phase = 0f, lp = 0f;
        for (int k = 0; k < n; k++)
        {
            float t = k / (float)rate;
            phase += Mathf.Lerp(70f, 32f, Mathf.Clamp01(t / 0.5f)) / rate;
            float body = Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Exp(-t * 4.5f);
            lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.08f;
            float hit = lp * Mathf.Exp(-t * 30f) * 1.5f;
            data[k] = Mathf.Clamp((body * 0.8f + hit) * Mathf.Min(1f, k / 60f), -1f, 1f);
        }
        var clip = AudioClip.Create("ObeliskThump", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>The collapse: a long low rumble of falling stone with crackles through it.</summary>
    static AudioClip MakeRumble()
    {
        const int rate = 44100;
        const float seconds = 2.6f;
        int n = Mathf.RoundToInt(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(4242);
        float lp1 = 0f, lp2 = 0f;
        for (int k = 0; k < n; k++)
        {
            float t = k / (float)rate;
            float white = (float)rng.NextDouble() * 2f - 1f;
            lp1 += (white - lp1) * 0.03f;
            lp2 += (lp1 - lp2) * 0.03f;
            float wobble = 0.6f + 0.4f * Mathf.Sin(t * 7.3f) * Mathf.Sin(t * 2.1f + 1f);
            float env = Mathf.Min(1f, t / 0.05f) * Mathf.Exp(-t * 1.1f);
            float crack = rng.NextDouble() < 0.0009 ? 0.8f : 0f;
            data[k] = Mathf.Clamp((lp2 * 9f * wobble + crack * white) * env, -1f, 1f);
        }
        var clip = AudioClip.Create("ObeliskRumble", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ------------------------------------------------------------------ geometry helpers

    /// <summary>Turns to the rig's yaw and stretches by 1/cos(pitch) (see SpriteBillboard); places the bubble.</summary>
    void FaceCamera()
    {
        if (!_rig && Time.time >= _nextRigLookup)
        {
            _nextRigLookup = Time.time + 1f;
            _rig = FindAnyObjectByType<IsoFollowCamera>();
        }
        float yaw = _rig ? _rig.yaw : 0f;
        float pitch = _rig ? _rig.pitch : 35f;

        _billboard.rotation = Quaternion.Euler(0f, yaw, 0f);
        _billboard.localScale = new Vector3(1f, 1f / Mathf.Max(Mathf.Cos(pitch * Mathf.Deg2Rad), 0.05f), 1f);

        // In front of the statue (local -Z faces the camera) and lifted by the amount that move drops
        // it on screen, so the bubble stays centred on the statue.
        float lift = shieldForward * Mathf.Sin(pitch * Mathf.Deg2Rad);
        _shield.localPosition = new Vector3(0f, (shieldCentre + lift) * size, -shieldForward * size);
    }

    float FindRigYaw()
    {
        _rig = FindAnyObjectByType<IsoFollowCamera>();
        return _rig ? _rig.yaw : 0f;
    }

    float PitchStretch()
    {
        float pitch = _rig ? _rig.pitch : 35f;
        return 1f / Mathf.Max(Mathf.Cos(pitch * Mathf.Deg2Rad), 0.05f);
    }

    /// <summary>Painting coordinates to the sprite's local space (pivot at the base, in units).</summary>
    static Vector3 SpriteLocal(Sprite s, Vector2 uv)
    {
        Rect r = s.rect;
        Vector2 pivot = s.pivot;
        float ppu = s.pixelsPerUnit;
        return new Vector3((uv.x * r.width - pivot.x) / ppu, (uv.y * r.height - pivot.y) / ppu, 0f);
    }

    /// <summary>A point on the statue in world space, through the shake and squash, a hair in front of it.</summary>
    Vector3 BodyPoint(Vector2 uv) =>
        _bodyRenderer.transform.TransformPoint(SpriteLocal(bodySprite, uv)) - _billboard.forward * 0.15f * size;

    /// <summary>A point on the shard layer in world space, so it follows the shards' twitching.</summary>
    Vector3 ShardPoint(Vector2 uv) =>
        _shardsRenderer.transform.TransformPoint(SpriteLocal(bodySprite, uv)) - _billboard.forward * 0.15f * size;

    Vector3 ShieldRimPoint(float degrees)
    {
        float a = degrees * Mathf.Deg2Rad;
        return _shield.TransformPoint(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.48f);
    }

    static Vector2 Row(float v)
    {
        int i = Mathf.Clamp(Mathf.RoundToInt(v * 50f), 1, Rows.Length - 3);
        return Rows[i];
    }

    static Mesh Quad()
    {
        if (s_quad) return s_quad;
        s_quad = new Mesh { name = "ObeliskQuad", hideFlags = HideFlags.HideAndDontSave };
        s_quad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),  new Vector3(0.5f, 0.5f, 0f),
        };
        s_quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        s_quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        s_quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        s_quad.RecalculateNormals();
        s_quad.RecalculateBounds();
        return s_quad;
    }

    static Mesh GroundQuad()
    {
        if (s_groundQuad) return s_groundQuad;

        // Lies flat in XZ and carries white vertex colours, which the glow shader multiplies by.
        s_groundQuad = new Mesh { name = "ObeliskGroundQuad", hideFlags = HideFlags.HideAndDontSave };
        s_groundQuad.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f, 0.5f),  new Vector3(0.5f, 0f, 0.5f),
        };
        s_groundQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        s_groundQuad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        s_groundQuad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        s_groundQuad.RecalculateNormals();
        s_groundQuad.RecalculateBounds();
        return s_groundQuad;
    }
}
