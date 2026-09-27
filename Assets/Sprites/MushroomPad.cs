using System.Collections;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// One mushroom of the melody puzzle: the painted sprite plus everything that makes poking it feel
/// physical - springy squash and stretch, a lean toward whoever is close, a halo that hugs the
/// silhouette, spores, and light on the ground under it.
///
/// <see cref="MartianSequencePuzzle"/> drives it with cues rather than colours:
///  - a mood that persists (<see cref="Mood"/>), i.e. what it looks like at rest right now;
///  - one-shot reactions (<see cref="Sing"/>, <see cref="Correct"/>, <see cref="Wrong"/>,
///    <see cref="Alarm"/>, <see cref="Celebrate"/>, <see cref="Poke"/>) that fade back into the mood.
/// Each mushroom has its own note colour and cap hue, so the melody can be remembered by colour as
/// well as by ear.
///
/// Hierarchy (built in the scene): this root holds the collider and sits on the ground at the foot of
/// the stem; a SpriteBillboard child turns to the camera; under that, <see cref="body"/> is the
/// squash / lean pivot carrying the body, halo and depth-twin sprites. Spores, the ground light,
/// the contact shadow and the nav obstacle are built at runtime.
/// </summary>
[DisallowMultipleComponent]
public class MushroomPad : MonoBehaviour
{
    public enum Mood
    {
        /// <summary>Puzzle not listening: calm, faint cyan glow.</summary>
        Dormant,
        /// <summary>Player's turn: glows its own note colour softly so the colours can be learned.</summary>
        Ready,
        /// <summary>Player's turn and they can press this one: warm highlight, leans toward them.</summary>
        Near,
        /// <summary>Melody solved: content, glowing its note, drifting spores.</summary>
        Solved,
    }

    [Header("Parts")]
    [Tooltip("Squash, stretch and lean pivot at the foot of the stem. Child of the billboard.")]
    [SerializeField] Transform body;
    [SerializeField] SpriteRenderer bodyRenderer;
    [Tooltip("Halo sprite (MushroomGlow) sitting just behind the body.")]
    [SerializeField] SpriteRenderer glowRenderer;
    [Tooltip("Depth-only twin of the body (MushroomDepth material), drawn first so whatever stands " +
             "behind the cap is hidden per pixel.")]
    [SerializeField] SpriteRenderer depthRenderer;
    [Tooltip("Eschropita/Soft Glow material, used for the spores and the light on the ground.")]
    [SerializeField] Material softGlowMaterial;

    [Header("Shape")]
    [Tooltip("Uniform size. The collider and nav obstacle follow it.")]
    [SerializeField, Min(0.1f)] float size = 1f;
    [Tooltip("Mirror the painting. Unflipped, the cap overhangs to the left of the stem.")]
    [SerializeField] bool flip;
    [Tooltip("Collider and nav obstacle radius at size 1. The painted stem is about 0.33 m; the rest " +
             "is root flare and a little personal space.")]
    [SerializeField, Min(0.05f)] float footRadius = 0.55f;
    [SerializeField, Min(0.1f)] float colliderHeight = 3f;
    [Tooltip("Snap onto the ground below when the scene starts (and when it opens in the editor), so " +
             "a nudge in the scene never leaves it floating or buried.")]
    [SerializeField] bool plantOnGround = true;

    [Header("Voice")]
    [Tooltip("This mushroom's note: the colour it glows when it sings or is pressed.")]
    [SerializeField, ColorUsage(false, true)] Color noteColor = new Color(1f, 0.28f, 0.78f);
    [Tooltip("How far the cap's marbling rotates in hue while it sings, in turns.")]
    [SerializeField, Range(-0.5f, 0.5f)] float noteHue = 0.12f;
    [SerializeField, Range(0f, 2f)] float noteSaturation = 1.15f;

    [Header("Feedback Colours")]
    [SerializeField, ColorUsage(false, true)] Color dormantGlow = new Color(0.3f, 0.75f, 1f);
    [SerializeField, ColorUsage(false, true)] Color nearGlow = new Color(1f, 0.85f, 0.45f);
    [SerializeField, ColorUsage(false, true)] Color correctGlow = new Color(0.25f, 1f, 0.35f);
    [SerializeField, ColorUsage(false, true)] Color wrongGlow = new Color(1f, 0.12f, 0.08f);

    [Header("Feel")]
    [Tooltip("Squash spring. Stiffer snaps back faster.")]
    [SerializeField] float squashStiffness = 240f;
    [Tooltip("Below ~10 it overshoots and wobbles, which is where the bounce comes from.")]
    [SerializeField] float squashDamping = 8f;
    [SerializeField] float leanStiffness = 80f;
    [SerializeField] float leanDamping = 6.5f;
    [Tooltip("Degrees it tilts toward the player while it is the one they can press.")]
    [SerializeField, Range(0f, 15f)] float leanTowardPlayer = 5f;
    [SerializeField, Range(0f, 0.1f)] float breathe = 0.018f;
    [Tooltip("Multiplier on the ambient spores drifting off the gills.")]
    [SerializeField, Range(0f, 4f)] float sporeRate = 1f;

    // ------------------------------------------------------------------ shader ids

    static readonly int HueShiftID     = Shader.PropertyToID("_HueShift");
    static readonly int HueWaveID      = Shader.PropertyToID("_HueWave");
    static readonly int HueWavePhaseID = Shader.PropertyToID("_HueWavePhase");
    static readonly int SaturationID   = Shader.PropertyToID("_Saturation");
    static readonly int BrightnessID   = Shader.PropertyToID("_Brightness");
    static readonly int TintColorID    = Shader.PropertyToID("_TintColor");
    static readonly int TintAmountID   = Shader.PropertyToID("_TintAmount");
    static readonly int FlashColorID   = Shader.PropertyToID("_FlashColor");
    static readonly int FlashAmountID  = Shader.PropertyToID("_FlashAmount");
    static readonly int ColorID        = Shader.PropertyToID("_Color");
    static readonly int IntensityID    = Shader.PropertyToID("_Intensity");
    static readonly int AdditiveID     = Shader.PropertyToID("_Additive");
    static readonly int ShapeID        = Shader.PropertyToID("_Shape");
    static readonly int SoftnessID     = Shader.PropertyToID("_Softness");
    static readonly int RingRadiusID   = Shader.PropertyToID("_RingRadius");
    static readonly int RingWidthID    = Shader.PropertyToID("_RingWidth");

    // ------------------------------------------------------------------ spots on the painting
    // Normalised on Mushroom.png: u from the left edge, v up from the bottom edge.

    const float GillU0 = 0.2f, GillU1 = 0.85f, GillV0 = 0.5f, GillV1 = 0.58f;
    const float CapV0 = 0.62f, CapV1 = 0.88f;
    static readonly Vector2 CapCentre = new Vector2(0.5f, 0.72f);
    static readonly Vector2[] DripTips =
    {
        new Vector2(0.226f, 0.489f), new Vector2(0.333f, 0.480f), new Vector2(0.444f, 0.446f),
        new Vector2(0.555f, 0.446f), new Vector2(0.678f, 0.465f),
    };

    // ------------------------------------------------------------------ look

    /// <summary>Everything the shaders show, so a mood and a reaction can be blended as one.</summary>
    struct Look
    {
        public Color glow;
        public float glowIntensity;
        public float hue;
        public float saturation;
        public float brightness;
        public Color tint;
        public float tintAmount;
        public float hueWave;

        public static Look Calm(Color glow, float intensity) => new Look
        {
            glow = glow, glowIntensity = intensity, saturation = 1f, brightness = 1f, tint = Color.red,
        };

        public static Look Lerp(in Look a, in Look b, float t) => new Look
        {
            glow          = Color.LerpUnclamped(a.glow, b.glow, t),
            glowIntensity = Mathf.LerpUnclamped(a.glowIntensity, b.glowIntensity, t),
            hue           = Mathf.LerpUnclamped(a.hue, b.hue, t),
            saturation    = Mathf.LerpUnclamped(a.saturation, b.saturation, t),
            brightness    = Mathf.LerpUnclamped(a.brightness, b.brightness, t),
            tint          = Color.LerpUnclamped(a.tint, b.tint, t),
            tintAmount    = Mathf.LerpUnclamped(a.tintAmount, b.tintAmount, t),
            hueWave       = Mathf.LerpUnclamped(a.hueWave, b.hueWave, t),
        };
    }

    Mood _mood = Mood.Dormant;
    Look _moodLook, _react, _reactTarget;
    float _reactWeight;        // 0 = pure mood, 1 = pure reaction
    Coroutine _reaction;

    float _flash;
    Color _flashColor = Color.white;
    float _flickerUntil;
    float _hueSpin;            // extra hue turns, used by the celebration's full colour cycle

    // Springs. Squash is a fraction (+ taller, - flatter); lean is degrees in the screen plane.
    float _squash, _squashVel, _droop;
    float _lean, _leanVel, _leanTarget;
    float _shake;

    float _phase;
    float _driftAcc;
    MaterialPropertyBlock _mpb;

    // Runtime-built effects
    ParticleSystem _spores, _drops;
    Transform _ground;
    MeshRenderer _pool, _shadow;
    MeshRenderer[] _rings;
    float[] _ringAge;
    Color[] _ringColor;
    int _nextRing;
    const float RingLife = 0.75f;

    // Player contact
    PlayerController _player;
    CharacterController _playerCc;
    float _nextPlayerLookup;
    Vector3 _lastPlayerPos;
    bool _havePlayerPos;
    float _bumpCooldown;

    static readonly RaycastHit[] s_hits = new RaycastHit[16];
    static Mesh s_groundQuad;

    // ------------------------------------------------------------------ public API

    public Mood CurrentMood => _mood;
    public Color NoteColor => noteColor;

    /// <summary>
    /// Where a Mycari should stand to play this mushroom: at the foot of the stem on the camera's side,
    /// just outside the carved NavMesh hole, so it is never hidden behind the stem.
    /// </summary>
    public Vector3 VisitPoint
    {
        get
        {
            Vector3 fwd = body && body.parent ? body.parent.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            return transform.position - fwd.normalized * (footRadius * size + 0.6f);
        }
    }

    /// <summary>Sets the resting look. Cheap and idempotent, so it can be called every frame.</summary>
    public void SetMood(Mood mood)
    {
        if (_mood == mood) return;

        // Becoming the pressable one gets a little hop of attention.
        if (mood == Mood.Near && isActiveAndEnabled)
        {
            _squashVel += 1.4f;
            Flash(nearGlow, 0.35f);
        }
        _mood = mood;
    }

    /// <summary>Sequence playback (the Mycari demo and the replay): glows its note, pops, puffs spores.</summary>
    public void Sing(float hold)
    {
        if (!isActiveAndEnabled) return;

        _squash = Mathf.Min(_squash, -0.05f);
        _squashVel += 2.1f;
        Flash(noteColor, 0.9f);
        _reactTarget = NoteLook(3.2f, 1.3f);
        PuffSpores(12, noteColor, 0.9f, fromCap: false);
        Ring(noteColor, 0.8f);
        Play(Envelope(0.05f, hold, 0.4f));
    }

    /// <summary>The player pressed the right one: squash, springy double bounce, green glow, spore burst.</summary>
    public void Correct(float hold)
    {
        if (!isActiveAndEnabled) return;

        // Pressed flat first, then released - the overshoot on the way back up is the bounce.
        _squash = -0.17f;
        _squashVel = 3.9f;
        _leanVel += Random.Range(-35f, 35f);
        Flash(Color.Lerp(correctGlow, Color.white, 0.35f), 1.6f);

        _reactTarget = NoteLook(3.4f, 1.45f);
        _reactTarget.glow = Color.Lerp(correctGlow, noteColor, 0.2f);

        BurstSpores(36, correctGlow, noteColor, 1f);
        PuffSpores(14, noteColor, 1.2f, fromCap: false);
        Ring(correctGlow, 1f);
        Play(CorrectRoutine(hold));
    }

    /// <summary>The player pressed the wrong one: shudders, sags, drains to red, drips.</summary>
    public void Wrong(float hold)
    {
        if (!isActiveAndEnabled) return;

        _squash = -0.2f;
        _squashVel = -0.5f;
        _shake = 7f;
        _flickerUntil = Time.time + 0.45f;
        Flash(wrongGlow, 0.9f);

        _reactTarget = Look.Calm(wrongGlow, 3f);
        _reactTarget.saturation = 0.4f;
        _reactTarget.brightness = 0.78f;
        _reactTarget.tint = wrongGlow;
        _reactTarget.tintAmount = 0.45f;

        Drip(9);
        PuffSpores(5, new Color(0.35f, 0.08f, 0.12f), 0.5f, fromCap: false);
        Ring(wrongGlow, 0.6f);
        Play(WrongRoutine(hold));
    }

    /// <summary>One beat of the all-pads red strobe after a mistake.</summary>
    public void Alarm(float duration)
    {
        if (!isActiveAndEnabled) return;

        _shake = Mathf.Max(_shake, 2.5f);
        _squashVel -= 0.6f;
        Flash(wrongGlow, 0.5f);
        _reactTarget = Look.Calm(wrongGlow, 2.6f);
        _reactTarget.tint = wrongGlow;
        _reactTarget.tintAmount = 0.3f;
        _reactTarget.saturation = 0.6f;
        Play(Envelope(0.01f, Mathf.Max(0f, duration - 0.01f), 0.08f));
    }

    /// <summary>Melody solved: rainbow cycle, big bounces, a fountain of spores. Starts after <paramref name="delay"/>.</summary>
    public void Celebrate(float delay)
    {
        if (!isActiveAndEnabled) return;
        Play(CelebrateRoutine(delay));
    }

    /// <summary>Pressed when it isn't listening: a wobble and a couple of spores, nothing more.</summary>
    public void Poke()
    {
        if (!isActiveAndEnabled) return;

        _squash = Mathf.Min(_squash, -0.08f);
        _squashVel += 1.3f;
        _leanVel += Random.value < 0.5f ? -45f : 45f;
        Flash(nearGlow, 0.3f);
        PuffSpores(5, dormantGlow, 0.6f, fromCap: false);
    }

    /// <summary>Back to calm, dormant, nothing playing. Used by puzzle resets.</summary>
    public void ResetVisuals()
    {
        if (_reaction != null) StopCoroutine(_reaction);
        _reaction = null;
        _reactWeight = 0f;
        _flash = 0f;
        _droop = 0f;
        _hueSpin = 0f;
        _shake = 0f;
        _flickerUntil = 0f;
        _mood = Mood.Dormant;
    }

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        ResolveParts();
        _mpb = new MaterialPropertyBlock();

        // Stable per-mushroom phase so the four never breathe in lockstep.
        Vector3 p = transform.position;
        _phase = Mathf.Repeat(p.x * 1.73f + p.z * 3.11f, 100f);

        ApplyLayout();
        AddNavObstacle();

        _moodLook = MoodLook(_mood, 0f);
        _react = _reactTarget = _moodLook;
    }

    void Start()
    {
        // Planted here rather than in Awake: by Start every collider in the scene is registered.
        Vector3 normal = Vector3.up;
        if (plantOnGround) TryPlant(out normal);

        BuildGroundFx(normal);
        _spores = BuildParticles("Spores", -0.04f, noisy: true, 400);
        _drops = BuildParticles("Drips", 1.1f, noisy: false, 60);
    }

    void OnDisable()
    {
        // Coroutines die with the component; release what they would have released.
        _reaction = null;
        _reactWeight = 0f;
        _droop = 0f;
        _hueSpin = 0f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float t = Time.time;

        UpdatePlayerContact(dt);

        _moodLook = Look.Lerp(_moodLook, MoodLook(_mood, t), 1f - Mathf.Exp(-dt * 6f));
        _react = Look.Lerp(_react, _reactTarget, 1f - Mathf.Exp(-dt * 30f));
        Look shown = Look.Lerp(_moodLook, _react, _reactWeight);

        if (t < _flickerUntil)
            shown.glowIntensity *= 0.45f + 0.55f * Mathf.PerlinNoise(t * 32f, _phase);

        _flash *= Mathf.Exp(-dt * 7f);
        _shake *= Mathf.Exp(-dt * 5f);

        StepSprings(dt, t);
        ApplyTransform(t);
        ApplyLook(shown, t);
        UpdateGroundFx(shown, dt);
        EmitDrift(shown, dt);
    }

    // ------------------------------------------------------------------ reactions

    void Play(IEnumerator routine)
    {
        if (_reaction != null) StopCoroutine(_reaction);
        _droop = 0f;
        _reaction = StartCoroutine(routine);
    }

    /// <summary>Ramps the reaction in from wherever it is, holds, then hands back to the mood.</summary>
    IEnumerator Envelope(float attack, float hold, float release)
    {
        float from = _reactWeight;
        for (float e = 0f; e < attack; e += Time.deltaTime)
        {
            _reactWeight = Mathf.Lerp(from, 1f, e / attack);
            yield return null;
        }
        _reactWeight = 1f;

        // Frame by frame rather than WaitForSeconds, so routines that wrap this one keep ticking.
        for (float e = 0f; e < hold; e += Time.deltaTime)
            yield return null;

        for (float e = 0f; e < release; e += Time.deltaTime)
        {
            _reactWeight = 1f - Mathf.SmoothStep(0f, 1f, e / release);
            yield return null;
        }
        _reactWeight = 0f;
        _reaction = null;
    }

    IEnumerator CorrectRoutine(float hold)
    {
        var env = Envelope(0.04f, Mathf.Max(0.1f, hold - 0.45f), 0.45f);

        // Second, smaller boing a beat after the first.
        float second = Time.time + 0.3f;
        bool kicked = false;
        while (env.MoveNext())
        {
            if (!kicked && Time.time >= second)
            {
                kicked = true;
                _squashVel += 1.8f;
                PuffSpores(8, correctGlow, 0.8f, fromCap: true);
            }
            yield return env.Current;
        }
    }

    IEnumerator WrongRoutine(float hold)
    {
        // Sags for the hold, then straightens as the look drains back to the mood.
        _droop = -0.09f;
        var env = Envelope(0.03f, Mathf.Max(0.1f, hold - 0.3f), 0.3f);
        float straighten = Time.time + Mathf.Max(0.1f, hold - 0.3f);
        while (env.MoveNext())
        {
            if (_droop < 0f && Time.time >= straighten) _droop = 0f;
            yield return env.Current;
        }
        _droop = 0f;
    }

    IEnumerator CelebrateRoutine(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        _squash = -0.22f;
        _squashVel = 5.2f;
        _leanVel += Random.Range(-60f, 60f);
        Flash(Color.white, 2f);

        _reactTarget = NoteLook(4f, 1.5f);
        _reactTarget.glow = Color.Lerp(correctGlow, noteColor, 0.35f);
        _reactTarget.hueWave = 0.3f;

        BurstSpores(56, correctGlow, noteColor, 1.35f, rainbow: true);
        Ring(correctGlow, 1.3f);

        const float cycle = 1.7f;
        float from = _reactWeight;
        bool second = false, third = false;
        for (float e = 0f; e < cycle; e += Time.deltaTime)
        {
            float k = e / cycle;
            _reactWeight = Mathf.Lerp(from, 1f, Mathf.Clamp01(e / 0.06f));
            _hueSpin = Mathf.SmoothStep(0f, 1f, k);   // one full trip round the colour wheel

            if (!second && e > 0.45f)
            {
                second = true;
                _squashVel += 3f;
                BurstSpores(22, noteColor, Color.white, 1f, rainbow: true);
                Ring(noteColor, 1f);
            }
            if (!third && e > 0.9f)
            {
                third = true;
                _squashVel += 1.8f;
                PuffSpores(12, correctGlow, 1f, fromCap: true);
            }
            yield return null;
        }

        _hueSpin = 0f;   // a full turn is the identity, so this does not pop
        for (float e = 0f; e < 0.6f; e += Time.deltaTime)
        {
            _reactWeight = 1f - Mathf.SmoothStep(0f, 1f, e / 0.6f);
            yield return null;
        }
        _reactWeight = 0f;
        _reaction = null;
    }

    Look NoteLook(float intensity, float brightness)
    {
        var l = Look.Calm(noteColor, intensity);
        l.hue = noteHue;
        l.saturation = noteSaturation;
        l.brightness = brightness;
        return l;
    }

    Look MoodLook(Mood mood, float t)
    {
        switch (mood)
        {
            case Mood.Ready:
            {
                // Glows its own note so the player can learn which colour is which.
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 2.2f + _phase);
                var l = Look.Calm(noteColor, 0.55f + 0.5f * pulse);
                l.brightness = 1.03f;
                l.saturation = 1.05f;
                return l;
            }
            case Mood.Near:
            {
                var l = Look.Calm(nearGlow, 1.9f + 0.3f * Mathf.Sin(t * 6f + _phase));
                l.hue = noteHue * 0.3f + 0.025f * Mathf.Sin(t * 2.5f + _phase);
                l.saturation = 1.12f;
                l.brightness = 1.16f;
                return l;
            }
            case Mood.Solved:
            {
                var l = Look.Calm(noteColor, 0.8f + 0.25f * Mathf.Sin(t * 1.4f + _phase));
                l.hue = noteHue * 0.5f + 0.06f * Mathf.Sin(t * 0.45f + _phase);
                l.saturation = 1.1f;
                l.brightness = 1.05f;
                return l;
            }
            default:
                return Look.Calm(dormantGlow, 0.45f + 0.1f * Mathf.Sin(t * 1.3f + _phase));
        }
    }

    void Flash(Color c, float amount)
    {
        if (amount < _flash) return;
        _flash = amount;
        _flashColor = c;
    }

    // ------------------------------------------------------------------ motion

    void StepSprings(float dt, float t)
    {
        if (dt <= 0f) return;

        float squashTarget = breathe * Mathf.Sin(t * 1.6f + _phase) + _droop;

        // Substeps keep the stiff spring stable at low frame rates.
        int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 120f), 1, 8);
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            _squashVel += (-squashStiffness * (_squash - squashTarget) - squashDamping * _squashVel) * h;
            _squash += _squashVel * h;

            _leanVel += (-leanStiffness * (_lean - _leanTarget) - leanDamping * _leanVel) * h;
            _lean += _leanVel * h;
        }

        _squash = Mathf.Clamp(_squash, -0.32f, 0.42f);
        _lean = Mathf.Clamp(_lean, -18f, 18f);
    }

    void ApplyTransform(float t)
    {
        if (!body) return;

        float sway = 1.1f * Mathf.Sin(t * 0.7f + _phase) + 0.45f * Mathf.Sin(t * 1.9f + _phase * 2f);
        float shake = _shake * Mathf.Sin(t * 69f);

        body.localScale = BaseScale(_squash);
        body.localRotation = Quaternion.Euler(0f, 0f, _lean + sway + shake);
    }

    Vector3 BaseScale(float squash)
    {
        // Rough volume preservation: taller gets thinner, flatter gets wider.
        return new Vector3((flip ? -size : size) * (1f - squash * 0.5f), size * (1f + squash), size);
    }

    void UpdatePlayerContact(float dt)
    {
        _leanTarget = 0f;
        if (_bumpCooldown > 0f) _bumpCooldown -= dt;

        var player = GetPlayer();
        if (!player) { _havePlayerPos = false; return; }

        Vector3 pp = player.transform.position;
        Vector3 vel = _havePlayerPos && dt > 0f ? (pp - _lastPlayerPos) / dt : Vector3.zero;
        _lastPlayerPos = pp;
        _havePlayerPos = true;

        Vector3 toMe = transform.position - pp;
        toMe.y = 0f;
        float dist = toMe.magnitude;
        if (dist < 1e-3f) return;
        toMe /= dist;

        Vector3 right = body ? body.parent.right : Vector3.right;
        right.y = 0f;
        right.Normalize();

        // Lean toward the player while this is the one they can press.
        if (_mood == Mood.Near)
            _leanTarget = Mathf.Clamp(Vector3.Dot(-toMe, right) * dist / 1.5f, -1f, 1f) * -leanTowardPlayer;

        // Walking into it: it gets knocked, so it clearly has a body.
        float playerRadius = _playerCc ? _playerCc.radius + _playerCc.skinWidth : 1f;
        float contact = footRadius * size + playerRadius + 0.15f;
        vel.y = 0f;
        float into = Vector3.Dot(vel, toMe);
        if (dist < contact && into > 1.2f && _bumpCooldown <= 0f)
        {
            _bumpCooldown = 0.45f;
            float k = Mathf.Clamp01(into / 8f);
            float lateral = Vector3.Dot(toMe, right);
            _leanVel += -lateral * 70f * (0.4f + k);
            _squashVel -= (1f - Mathf.Abs(lateral)) * 1.5f * (0.4f + k);
            PuffSpores(3 + Mathf.RoundToInt(4 * k), dormantGlow, 0.5f, fromCap: true);
        }
    }

    PlayerController GetPlayer()
    {
        if (_player) return _player;
        if (Time.time < _nextPlayerLookup) return null;
        _nextPlayerLookup = Time.time + 1f;
        _player = FindAnyObjectByType<PlayerController>();
        if (_player) _playerCc = _player.GetComponent<CharacterController>();
        return _player;
    }

    // ------------------------------------------------------------------ shading

    void ApplyLook(in Look look, float t)
    {
        if (bodyRenderer)
        {
            // Read first so whatever the SpriteRenderer keeps in the block survives.
            bodyRenderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(HueShiftID, look.hue + _hueSpin);
            _mpb.SetFloat(HueWaveID, look.hueWave);
            _mpb.SetFloat(HueWavePhaseID, t * 6f + _phase);
            _mpb.SetFloat(SaturationID, look.saturation);
            _mpb.SetFloat(BrightnessID, look.brightness);
            _mpb.SetColor(TintColorID, look.tint);
            _mpb.SetFloat(TintAmountID, look.tintAmount);
            _mpb.SetColor(FlashColorID, _flashColor);
            _mpb.SetFloat(FlashAmountID, _flash);
            bodyRenderer.SetPropertyBlock(_mpb);
        }

        if (glowRenderer)
        {
            glowRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorID, look.glow + _flashColor * (_flash * 0.5f));
            _mpb.SetFloat(IntensityID, look.glowIntensity);
            _mpb.SetFloat(AdditiveID, 0.85f);
            _mpb.SetFloat(ShapeID, 0f);
            glowRenderer.SetPropertyBlock(_mpb);

            // A strong glow reaches a little further out.
            float spread = 1f + 0.035f * Mathf.Max(0f, look.glowIntensity - 0.5f);
            glowRenderer.transform.localScale = new Vector3(spread, spread, 1f);
        }
    }

    // ------------------------------------------------------------------ spores

    ParticleSystem BuildParticles(string name, float gravity, bool noisy, int max)
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
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.07f),
                    new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(fade);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.3f), new Keyframe(0.1f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));

        // Drag: bursts fly out fast and then hang in the air, which is what makes them read as spores.
        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = 100f;
        drag.drag = noisy ? 1.6f : 0.3f;

        if (noisy)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.45f;
            noise.frequency = 0.7f;
            noise.scrollSpeed = 0.35f;
            noise.damping = true;
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.sizeAmount = 0.6f;   // twinkle
        }

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = softGlowMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        var mpb = new MaterialPropertyBlock();
        mpb.SetFloat(ShapeID, 1f);
        mpb.SetFloat(SoftnessID, noisy ? 1.4f : 2.2f);
        mpb.SetFloat(AdditiveID, noisy ? 0.55f : 0.1f);
        mpb.SetColor(ColorID, Color.white);
        mpb.SetFloat(IntensityID, noisy ? 1.8f : 1.2f);
        r.SetPropertyBlock(mpb);

        ps.Play();
        return ps;
    }

    /// <summary>A fountain off the cap: outward from its centre and up, the way a puffball bursts.</summary>
    void BurstSpores(int count, Color a, Color b, float power, bool rainbow = false)
    {
        if (!_spores || !CanMapSprite()) return;

        Vector3 centre = SpritePoint(CapCentre.x, CapCentre.y);
        Vector3 fwd = body.parent.forward;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = RandomCapPoint();
            Vector3 dir = p - centre;
            dir.y = Mathf.Max(dir.y, 0f) + 0.6f;
            dir += fwd * Random.Range(-0.7f, 0.7f);
            dir.Normalize();

            Color c = rainbow && Random.value < 0.6f
                ? Color.HSVToRGB(Random.value, 0.75f, 1f)
                : Color.Lerp(Clamp01(a), Clamp01(b), Random.value);
            if (Random.value < 0.15f) c = Color.Lerp(c, Color.white, 0.7f);

            Emit(_spores, p, dir * Random.Range(1.4f, 3.6f) * power * size,
                 Random.Range(0.06f, 0.16f) * size, Random.Range(1.4f, 2.8f), c);
        }
    }

    /// <summary>A soft puff from the gills (or the cap), drifting out and up.</summary>
    void PuffSpores(int count, Color c, float power, bool fromCap)
    {
        if (!_spores || !CanMapSprite()) return;

        Vector3 fwd = body.parent.forward;
        Vector3 right = body.parent.right;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = fromCap ? RandomCapPoint() : RandomGillPoint();
            Vector3 v = Vector3.up * Random.Range(0.3f, 1.1f)
                      + right * Random.Range(-0.8f, 0.8f)
                      + fwd * Random.Range(-0.5f, 0.5f);
            Color cc = Color.Lerp(Clamp01(c), Color.white, Random.Range(0f, 0.4f));
            Emit(_spores, p, v * power * size, Random.Range(0.05f, 0.12f) * size, Random.Range(1.2f, 2.4f), cc);
        }
    }

    /// <summary>Heavy drops off the drip tips under the cap - it wilts a little when you get it wrong.</summary>
    void Drip(int count)
    {
        if (!_drops || !CanMapSprite()) return;

        for (int i = 0; i < count; i++)
        {
            Vector2 tip = DripTips[Random.Range(0, DripTips.Length)];
            Vector3 p = SpritePoint(tip.x + Random.Range(-0.01f, 0.01f), tip.y);
            Vector3 v = new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(-0.2f, 0.4f), Random.Range(-0.25f, 0.25f));
            Color c = Color.Lerp(new Color(0.8f, 0.1f, 0.35f), new Color(0.45f, 0.1f, 0.6f), Random.value);
            Emit(_drops, p, v, Random.Range(0.06f, 0.11f) * size, Random.Range(0.7f, 1.1f), c);
        }
    }

    /// <summary>Ambient spores drifting off the gills; how many depends on the mood.</summary>
    void EmitDrift(in Look look, float dt)
    {
        if (!_spores || sporeRate <= 0f || !CanMapSprite()) return;

        float rate;
        switch (_mood)
        {
            case Mood.Ready:  rate = 1f; break;
            case Mood.Near:   rate = 2.6f; break;
            case Mood.Solved: rate = 1.8f; break;
            default:          rate = 0.5f; break;
        }

        _driftAcc += rate * sporeRate * dt;
        Color glow = Clamp01(look.glow);
        Vector3 fwd = body.parent.forward;
        while (_driftAcc >= 1f)
        {
            _driftAcc -= 1f;
            Vector3 p = Random.value < 0.75f ? RandomGillPoint() : RandomCapPoint();
            Vector3 v = new Vector3(Random.Range(-0.12f, 0.12f), Random.Range(0.12f, 0.35f), 0f)
                      + fwd * Random.Range(-0.1f, 0.1f);
            Color c = Color.Lerp(glow, Color.white, Random.Range(0.1f, 0.5f));
            c.a = 0.75f;
            Emit(_spores, p, v * size, Random.Range(0.03f, 0.07f) * size, Random.Range(2.5f, 4.5f), c);
        }
    }

    static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float sizeM, float life, Color c)
    {
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
        // Particle colour is 8-bit: fold HDR brightness back into range keeping the hue.
        float m = Mathf.Max(1f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
        return new Color(c.r / m, c.g / m, c.b / m, Mathf.Clamp01(c.a));
    }

    bool CanMapSprite() => body && body.parent && bodyRenderer && bodyRenderer.sprite;

    /// <summary>Painting coordinates (see the constants above) to a world position, through the squash and lean.</summary>
    Vector3 SpritePoint(float u, float v)
    {
        var s = bodyRenderer.sprite;
        Rect r = s.rect;
        Vector2 pivot = s.pivot;
        float ppu = s.pixelsPerUnit;
        var local = new Vector3((u * r.width - pivot.x) / ppu, (v * r.height - pivot.y) / ppu, 0f);
        return body.TransformPoint(local) + body.parent.forward * Random.Range(-0.2f, 0.2f) * size;
    }

    Vector3 RandomGillPoint() => SpritePoint(Random.Range(GillU0, GillU1), Random.Range(GillV0, GillV1));

    Vector3 RandomCapPoint()
    {
        // The cap is roughly a triangle: wide at the rim, narrow at the tip, leaning slightly right.
        float v = Mathf.Lerp(CapV0, CapV1, Mathf.Pow(Random.value, 1.4f));
        float k = Mathf.InverseLerp(0.64f, 0.88f, v);
        float centre = Mathf.Lerp(0.49f, 0.535f, k);
        float half = Mathf.Lerp(0.4f, 0.12f, k);
        return SpritePoint(centre + Random.Range(-half, half) * 0.9f, v);
    }

    // ------------------------------------------------------------------ ground light

    void BuildGroundFx(Vector3 normal)
    {
        if (!softGlowMaterial) return;

        // Lies on the ground, turned with the billboard so "across the cap" is the decal's X axis.
        float yaw = body && body.parent ? body.parent.eulerAngles.y : 0f;
        _ground = new GameObject("Ground").transform;
        _ground.SetParent(transform, false);
        _ground.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f);
        _ground.position = transform.position + normal * 0.05f;

        // The shadow sits a little toward the cap, which overhangs one side of the stem.
        _shadow = MakeGroundQuad("ContactShadow", -3);
        _shadow.transform.localPosition = new Vector3((flip ? 1f : -1f) * 0.25f * size, 0f, 0f);
        _shadow.transform.localScale = new Vector3(2.3f * size, 1f, 1.3f * size);

        _pool = MakeGroundQuad("GlowPool", -2);
        _pool.transform.localScale = new Vector3(3.6f * size, 1f, 3.6f * size);

        _rings = new MeshRenderer[3];
        _ringAge = new float[_rings.Length];
        _ringColor = new Color[_rings.Length];
        for (int i = 0; i < _rings.Length; i++)
        {
            _rings[i] = MakeGroundQuad("Ring", -2);
            _rings[i].enabled = false;
            _ringAge[i] = RingLife;
        }

        _mpb.Clear();
        _mpb.SetFloat(ShapeID, 1f);
        _mpb.SetFloat(SoftnessID, 1.3f);
        _mpb.SetFloat(AdditiveID, 0f);
        _mpb.SetColor(ColorID, new Color(0.05f, 0f, 0.08f, 0.55f));
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
        _rings[i].enabled = true;
    }

    void UpdateGroundFx(in Look look, float dt)
    {
        if (_pool)
        {
            _mpb.Clear();
            _mpb.SetFloat(ShapeID, 1f);
            _mpb.SetFloat(SoftnessID, 1.8f);
            _mpb.SetFloat(AdditiveID, 1f);
            _mpb.SetColor(ColorID, look.glow + _flashColor * (_flash * 0.4f));
            _mpb.SetFloat(IntensityID, 0.42f * look.glowIntensity);
            _pool.SetPropertyBlock(_mpb);
        }

        if (_rings == null) return;
        for (int i = 0; i < _rings.Length; i++)
        {
            if (!_rings[i].enabled) continue;

            _ringAge[i] += dt;
            float k = _ringAge[i] / RingLife;
            if (k >= 1f) { _rings[i].enabled = false; continue; }

            float ease = 1f - (1f - k) * (1f - k) * (1f - k);
            float d = Mathf.Lerp(0.9f, 4.2f, ease) * size;
            _rings[i].transform.localScale = new Vector3(d, 1f, d);

            _mpb.Clear();
            _mpb.SetFloat(ShapeID, 2f);
            _mpb.SetFloat(RingRadiusID, 0.8f);
            _mpb.SetFloat(RingWidthID, Mathf.Lerp(0.05f, 0.11f, k));
            _mpb.SetFloat(AdditiveID, 1f);
            _mpb.SetColor(ColorID, _ringColor[i]);
            _mpb.SetFloat(IntensityID, 2.4f * (1f - k) * (1f - k));
            _rings[i].SetPropertyBlock(_mpb);
        }
    }

    static Mesh GroundQuad()
    {
        if (s_groundQuad) return s_groundQuad;

        // Built rather than the builtin quad: it lies flat in XZ and carries white vertex colours,
        // which the glow shader multiplies by.
        s_groundQuad = new Mesh { name = "MushroomGroundQuad", hideFlags = HideFlags.HideAndDontSave };
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

    // ------------------------------------------------------------------ layout, ground, navigation

    void ResolveParts()
    {
        if (!body)
        {
            var sr = GetComponentInChildren<SpriteRenderer>(true);
            if (sr) body = sr.transform;
        }
        if (body && !bodyRenderer) bodyRenderer = body.GetComponent<SpriteRenderer>();
    }

    /// <summary>Body scale and flip at rest, and a collider that matches the size.</summary>
    void ApplyLayout()
    {
        if (body) body.localScale = BaseScale(0f);

        if (TryGetComponent(out CapsuleCollider cap))
        {
            cap.direction = 1;
            cap.radius = footRadius * size;
            cap.height = Mathf.Max(colliderHeight * size, cap.radius * 2f);
            cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
        }
    }

    void AddNavObstacle()
    {
        // Carves the NavMesh so the Mycari walk up to the stem instead of through it. Added at runtime:
        // the baked mesh ignores obstacles anyway, and carving only exists while playing.
        if (!TryGetComponent(out NavMeshObstacle obstacle))
            obstacle = gameObject.AddComponent<NavMeshObstacle>();

        float h = Mathf.Max(colliderHeight * size, footRadius * size * 2f);
        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.center = new Vector3(0f, h * 0.5f, 0f);
        obstacle.radius = footRadius * size;
        obstacle.height = h;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
    }

    /// <summary>
    /// Drops the root onto the first solid, static surface under it. Characters, rigidbodies, triggers
    /// and other mushrooms are skipped, and anything more than 3 m away is ignored.
    /// </summary>
    bool TryPlant(out Vector3 normal)
    {
        normal = Vector3.up;
        Vector3 origin = transform.position + Vector3.up * 4f;
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, 10f, ~0, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        RaycastHit ground = default;
        for (int i = 0; i < n; i++)
        {
            var c = s_hits[i].collider;
            if (!c || c.transform.IsChildOf(transform)) continue;
            if (c.attachedRigidbody || c is CharacterController) continue;
            if (c.GetComponentInParent<MushroomPad>() || c.GetComponentInParent<NavMeshAgent>()) continue;
            if (s_hits[i].distance < best) { best = s_hits[i].distance; ground = s_hits[i]; }
        }

        if (best == float.MaxValue || Mathf.Abs(ground.point.y - transform.position.y) > 3f) return false;

        normal = ground.normal;
        if (Vector3.Distance(transform.position, ground.point) > 0.001f)
            transform.position = ground.point;
        return true;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying) return;
        EditorApplication.delayCall -= EditorRefresh;
        EditorApplication.delayCall += EditorRefresh;
    }

    void EditorRefresh()
    {
        if (this == null || Application.isPlaying || !gameObject.scene.IsValid()) return;
        ApplyLayout();
        if (plantOnGround) TryPlant(out _);
    }

    [ContextMenu("Plant On Ground")]
    void PlantFromMenu()
    {
        Undo.RecordObject(transform, "Plant Mushroom");
        if (!TryPlant(out _)) Debug.LogWarning($"[MushroomPad] No ground found under '{name}'.", this);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.05f, footRadius * size);
    }
#endif
}
