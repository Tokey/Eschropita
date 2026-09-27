using System;
using UnityEngine;

/// <summary>
/// Trigger volume on the bunker revealed under the destroyed obelisk. Tells the flow once
/// when the player steps in. Needs a trigger collider on the same GameObject; a 3x3x3 box is
/// added when none exists.
/// </summary>
[DisallowMultipleComponent]
public class BunkerTrigger : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("Only colliders with this tag count.")]
    [SerializeField] string playerTag = "Player";

    /// <summary>True once the player has entered (until <see cref="ResetEntered"/>).</summary>
    public bool Entered { get; private set; }

    public event Action OnEntered;

    void Awake()
    {
        if (GetComponent<Collider>() != null) return;
        var box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, 1.5f, 0f);
        box.size = new Vector3(3f, 3f, 3f);
    }

    void OnTriggerEnter(Collider other)
    {
        if (Entered) return;
        if (!other.CompareTag(playerTag)) return;

        Entered = true;
        OnEntered?.Invoke();
        if (GameFlowManager.Instance != null) GameFlowManager.Instance.NotifyBunkerEntered();
    }

    /// <summary>Re-arms the trigger (checkpoint restore to a stage before the bunker).</summary>
    public void ResetEntered() => Entered = false;

    public ScanHit ToScanHit()
    {
        return new ScanHit
        {
            target = transform,
            kind = ScanKind.Bunker,
            label = "BUNKER",
            detail = Entered ? "Explored" : "",
            value01 = -1f,
            color = new Color(0.910f, 0.659f, 0.361f) // amber, same as the hatch
        };
    }
}
