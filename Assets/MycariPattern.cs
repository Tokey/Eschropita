using UnityEngine;

/// <summary>
/// Drives the swirl that shows through a Mycari's back.
///
/// The region is baked into the sprites as a flat key colour (Tools ▸ Eschropita ▸ Mycari ▸
/// Bake Fill Region); the Chowder Fill shader swaps that colour for whichever pattern texture
/// this component hands it, projected in world space so it slides as the creature walks —
/// the same treatment Eschropita gets.
///
/// What the colour MEANS: nothing, until the player has learned the melody. Every Mycari shows
/// the default swirl while it is going about its own business. Recruit one, or send it to a job,
/// and it reveals its kind — green Fixer, red Breaker, blue Wanderer. So the colour always reads
/// as "this one is working with you", never as passive decoration.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[DisallowMultipleComponent]
public class MycariPattern : MonoBehaviour
{
    [Header("Patterns (world-projected swirls)")]
    [Tooltip("Shown by every Mycari until it is actually working with the player.")]
    public Texture2D defaultPattern;
    public Texture2D fixerPattern;
    public Texture2D breakerPattern;
    public Texture2D wandererPattern;

    [Header("Projection")]
    [Tooltip("World units per tile. Smaller churns the swirl faster as the Mycari moves.")]
    public float patternScale = 4f;

    [Tooltip("Brightness applied to the swirl. Below 1 sits it back behind the painted body.")]
    [Range(0f, 2f)] public float fillBrightness = 1f;

    [Tooltip("Pushes the swirl above 1.0 so HDRP's bloom picks it up and it reads as lit from " +
             "within rather than as wallpaper behind a hole. 1 = no glow.")]
    [Range(1f, 8f)] public float fillGlow = 3.6f;

    [Tooltip("Saturates the swirl before the glow lifts it. Lifting a washed-out colour only " +
             "makes it white, so the colour has to be pushed first.")]
    [Range(0.5f, 2f)] public float fillGlowSaturation = 1.45f;

    [Tooltip("Extra glow while the Mycari is actually working, so a busy one stands out.")]
    [Range(1f, 3f)] public float workingGlowMultiplier = 1.25f;

    [Header("Behaviour")]
    [Tooltip("Reveal the kind only once the melody puzzle is done. Off = always reveal.")]
    public bool requirePuzzleDone = true;

    [Tooltip("Seconds to cross-fade when the pattern changes.")]
    [Range(0f, 2f)] public float blendSeconds = 0.35f;

    static readonly int PatternTexID   = Shader.PropertyToID("_PatternTex");
    static readonly int PatternScaleID = Shader.PropertyToID("_PatternScale");
    static readonly int FillTintID     = Shader.PropertyToID("_FillTint");
    static readonly int FillGlowID     = Shader.PropertyToID("_FillGlow");
    static readonly int FillGlowSatID  = Shader.PropertyToID("_FillGlowSaturation");
    static readonly int CamYawID       = Shader.PropertyToID("_CamYaw");
    static readonly int CamPitchID     = Shader.PropertyToID("_CamPitch");

    SpriteRenderer _sr;
    MaterialPropertyBlock _mpb;
    NPCFlocker _npc;
    IsoFollowCamera _rig;

    Texture2D _current;
    float _blend = 1f;          // 1 = settled on _current
    Color _tint = Color.white;
    float _phase;               // per-Mycari offset so a crowd does not pulse in unison

    void Awake()
    {
        _sr  = GetComponent<SpriteRenderer>();
        _npc = GetComponentInParent<NPCFlocker>();
        _mpb = new MaterialPropertyBlock();
        _rig = FindAnyObjectByType<IsoFollowCamera>();
        _current = defaultPattern;
        _phase = Random.Range(0f, Mathf.PI * 2f);
    }

    void LateUpdate()
    {
        var wanted = Choose();
        if (wanted != _current)
        {
            _current = wanted;
            _blend = blendSeconds > 0f ? 0f : 1f;
        }
        if (_blend < 1f)
            _blend = Mathf.Min(1f, _blend + Time.deltaTime / Mathf.Max(0.01f, blendSeconds));

        Apply();
    }

    /// <summary>
    /// Yellow unless this one is actually engaged with the player. "Engaged" covers being
    /// recruited, walking to a job and doing it - anything where the player has a stake in
    /// which kind it is.
    /// </summary>
    Texture2D Choose()
    {
        if (_npc == null) return defaultPattern;

        if (requirePuzzleDone)
        {
            var flow = GameFlowManager.Instance;
            if (flow != null && !flow.MycariObey) return defaultPattern;
        }

        bool engaged = _npc.state == NPCState.FollowToDecision ||
                       _npc.state == NPCState.AwaitDecision   ||
                       _npc.state == NPCState.FollowApproved  ||
                       _npc.state == NPCState.FollowToTask    ||
                       _npc.state == NPCState.Work;

        if (!engaged) return defaultPattern;

        switch (_npc.role)
        {
            case NPCRole.Worker:   return fixerPattern    ? fixerPattern    : defaultPattern;
            case NPCRole.Attacker: return breakerPattern  ? breakerPattern  : defaultPattern;
            default:               return wandererPattern ? wandererPattern : defaultPattern;
        }
    }

    void Apply()
    {
        if (_sr == null) return;

        // Read first: the SpriteRenderer writes its own per-frame data into the block, and
        // replacing it wholesale loses the sprite's tint and flip.
        _sr.GetPropertyBlock(_mpb);

        if (_current != null) _mpb.SetTexture(PatternTexID, _current);
        _mpb.SetFloat(PatternScaleID, Mathf.Max(0.01f, patternScale));

        // Fade the new swirl up rather than cutting, so a Mycari accepting an order reads as a
        // colour washing through it instead of a hard swap.
        float b = Mathf.SmoothStep(0f, 1f, _blend);
        _tint = new Color(fillBrightness, fillBrightness, fillBrightness,
                          Mathf.Lerp(0.55f, 1f, b));
        _mpb.SetColor(FillTintID, _tint);

        // A working Mycari burns a little brighter, so you can pick out the ones on a job
        // across the field. Breathes slowly rather than sitting at a fixed value - a static
        // glow reads as a flat decal, a moving one reads as alive.
        bool working = _npc != null &&
                       (_npc.state == NPCState.Work || _npc.state == NPCState.FollowToTask);
        float breathe = 1f + 0.10f * Mathf.Sin(Time.time * 1.6f + _phase);
        float glow = fillGlow * breathe * (working ? workingGlowMultiplier : 1f);
        _mpb.SetFloat(FillGlowID, Mathf.Lerp(1f, glow, b));
        _mpb.SetFloat(FillGlowSatID, fillGlowSaturation);

        // The shader projects through the rig's fixed angles, not the live view matrix, so the
        // perlin shake cannot make the swirl wobble. Keep them in step if the rig is retuned.
        if (_rig != null)
        {
            _mpb.SetFloat(CamYawID, _rig.yaw);
            _mpb.SetFloat(CamPitchID, _rig.pitch);
        }

        _sr.SetPropertyBlock(_mpb);
    }
}
