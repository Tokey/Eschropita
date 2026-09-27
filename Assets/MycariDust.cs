using UnityEngine;

/// <summary>
/// Kicks a small puff of Martian dust off the ground on the two frames of the moving loop where
/// the Mycari pushes off, and only when it is actually travelling at speed.
///
/// The particle system is built in code so the prefab carries no extra authored asset, and so
/// the puff sits at the creature's feet regardless of how the sprite is scaled.
/// </summary>
[RequireComponent(typeof(MycariAnimator))]
[DisallowMultipleComponent]
public class MycariDust : MonoBehaviour
{
    [Header("When to puff")]
    [Tooltip("Ground speed below which it is walking, not scuffing, and no dust is raised.")]
    [Min(0f)] public float minSpeed = 1.2f;

    [Tooltip("Shortest gap between two puffs, so a fast loop cannot machine-gun them.")]
    [Min(0.02f)] public float minInterval = 0.18f;

    [Header("Look")]
    [Range(1, 12)] public int particlesPerPuff = 5;
    public float puffRadius = 0.22f;
    public float puffSpeed = 0.7f;
    public float puffLifetime = 0.55f;
    public float startSize = 0.28f;
    [Tooltip("Dust colour. Alpha is the peak opacity; it fades out over the lifetime.")]
    public Color dustColor = new Color(0.78f, 0.55f, 0.38f, 0.5f);

    ParticleSystem _ps;
    MycariAnimator _anim;
    NPCFlocker _npc;
    UnityEngine.AI.NavMeshAgent _agent;
    bool _wasOnBurst;
    float _nextAllowed;

    void Awake()
    {
        _anim  = GetComponent<MycariAnimator>();
        _npc   = GetComponentInParent<NPCFlocker>();
        _agent = GetComponentInParent<UnityEngine.AI.NavMeshAgent>();
        Build();
    }

    void Build()
    {
        var go = new GameObject("DustPuff");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;    // feet: the sprite pivots at the base
        go.transform.localRotation = Quaternion.identity;

        _ps = go.AddComponent<ParticleSystem>();
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = puffLifetime;
        main.startSpeed = puffSpeed;
        main.startSize = startSize;
        main.startColor = dustColor;
        main.gravityModifier = -0.03f;                // drifts up a touch, like kicked dust
        main.simulationSpace = ParticleSystemSimulationSpace.World;  // stays put once kicked
        main.maxParticles = 64;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = _ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;                   // bursts only, driven from Update

        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = puffRadius;

        // Balloon out then settle, so it reads as a scuff rather than a spark.
        var sol = _ps.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.5f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.75f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var col = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(dustColor, 0f), new GradientColorKey(dustColor, 1f) },
            new[] { new GradientAlphaKey(dustColor.a, 0f),
                    new GradientAlphaKey(dustColor.a * 0.8f, 0.3f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var vol = _ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.Local;
        // All three axes must share a curve mode or Unity throws
        // "Particle Velocity curves must all be in the same mode", so x and z are given
        // explicit two-constant ranges rather than being left as plain constants.
        vol.x = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
        vol.y = new ParticleSystem.MinMaxCurve(0.12f, 0.34f);
        vol.z = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);

        var limit = _ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.35f;                          // air-drags to a stop instead of flying off

        // Unlit additive-ish sprite material so it reads in HDRP without needing lighting.
        var r = _ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        var sh = Shader.Find("Particles/Standard Unlit")
              ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
              ?? Shader.Find("Sprites/Default");
        r.material = new Material(sh);
        if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", dustColor);
    }

    void LateUpdate()
    {
        if (_ps == null || _anim == null) return;

        bool onBurst = _anim.IsOnBurstFrame;
        bool fast = Speed() >= minSpeed;

        // Edge-triggered: one puff as the frame comes up, not one per frame it is held.
        if (onBurst && !_wasOnBurst && fast && Time.time >= _nextAllowed)
        {
            _ps.Emit(particlesPerPuff);
            _nextAllowed = Time.time + minInterval;
        }
        _wasOnBurst = onBurst;
    }

    float Speed()
    {
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            return _agent.velocity.magnitude;
        return _npc != null ? 0f : 0f;
    }
}
