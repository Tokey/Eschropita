using UnityEngine;

/// <summary>Drives the Mycari sprite animator from NPC movement.</summary>
[RequireComponent(typeof(Animator))]
public class MycariAnimator : MonoBehaviour
{
    [SerializeField] float movingThreshold = 0.05f;
    [Tooltip("Movement multiplier used only while moving animation frames 1 and 2 are displayed.")]
    [Min(1f)] [SerializeField] float burstMultiplier = 1.8f;
    [Tooltip("Overall movement scale applied to normal travel and the frame burst.")]
    [Range(0.1f, 1f)] [SerializeField] float overallSpeedMultiplier = 0.9f;

    Animator _animator;
    float _lastSpeed;
    bool _repairing;

    void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    public void SetMovementSpeed(float speed)
    {
        _lastSpeed = speed;
        _animator.SetFloat("Speed", speed);
    }

    public void SetMoving(bool moving)
    {
        SetMovementSpeed(moving ? 1f : 0f);
    }

    public void PlayAttack()
    {
        _animator.SetTrigger("Attack");
    }

    /// <summary>Holds the repair loop while the Mycari is working a site.</summary>
    public void SetRepairing(bool repairing)
    {
        if (_repairing == repairing) return;
        _repairing = repairing;
        _animator.SetBool("Repair", repairing);
    }

    public bool IsRepairing => _repairing;

    /// <summary>
    /// True on the two frames of the moving loop where the creature pushes off — the same pair
    /// that <see cref="MovementSpeedMultiplier"/> speeds up. The dust puff hangs off this so the
    /// kick and the scuff land together.
    /// </summary>
    public bool IsOnBurstFrame
    {
        get
        {
            if (_animator == null) return false;
            var state = _animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName("Moving")) return false;
            int frame = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(state.normalizedTime, 1f) * 6f), 0, 5);
            return frame == 1 || frame == 2;
        }
    }

    public bool IsMoving => _lastSpeed > movingThreshold;

    public float MovementSpeedMultiplier
    {
        get
        {
            if (_animator == null) return overallSpeedMultiplier;

            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName("Moving")) return overallSpeedMultiplier;

            int frame = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(state.normalizedTime, 1f) * 6f), 0, 5);
            return overallSpeedMultiplier * (frame == 1 || frame == 2 ? burstMultiplier : 1f);
        }
    }
}