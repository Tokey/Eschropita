using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Trigger volume around the mushroom field (the pad puzzle area).
///
/// Bringing a Mycari here is the whole point of the opening, so the field reacts to the MYCARI
/// walking in, not to the player: lead one into the grove and the melody starts on its own.
/// The player's own entries are still tracked, because the flow wants to know they are present
/// to watch it.
///
/// With a GameFlowManager in the scene the flow owns what actually happens. Standalone it keeps
/// the legacy behaviour so the field still works in a scene with no story running.
/// </summary>
public class MushroomFieldTrigger : MonoBehaviour
{
    [Tooltip("Puzzle started when a Mycari walks in. Required for the standalone path; the flow " +
             "uses its own reference.")]
    public MartianSequencePuzzle puzzle;

    [Tooltip("Start the melody by itself as soon as a Mycari is inside, without a GameFlowManager " +
             "having to ask for it.")]
    public bool autoStartOnMycari = true;

    /// <summary>Fired every time the player enters the field.</summary>
    public event System.Action OnPlayerEntered;

    /// <summary>Fired when a Mycari enters and the field was previously empty of them.</summary>
    public event System.Action<NPCFlocker> OnMycariEntered;

    /// <summary>True while the player is inside the trigger volume.</summary>
    public bool PlayerInside { get; private set; }

    /// <summary>True while at least one Mycari is inside the trigger volume.</summary>
    public bool MycariInside => _mycari.Count > 0;

    /// <summary>The Mycari that first walked in, or null once they have all left.</summary>
    public NPCFlocker FirstMycari { get; private set; }

    /// <summary>World position of the field — where the flow sends the Mycari for the demo.</summary>
    public Vector3 Center => transform.position;

    // Live set, because a Mycari can be destroyed or disabled while standing in the field.
    readonly HashSet<NPCFlocker> _mycari = new HashSet<NPCFlocker>();

    // The melody only starts itself once.
    bool _autoStarted;

    void Update()
    {
        // Prune anything that died or was disabled inside the volume; OnTriggerExit never fires
        // for those, and a stale entry would keep MycariInside true forever.
        if (_mycari.Count > 0)
        {
            _mycari.RemoveWhere(m => m == null || !m.isActiveAndEnabled);
            if (_mycari.Count == 0) FirstMycari = null;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerInside = true;
            OnPlayerEntered?.Invoke();
            GameFlowManager.Instance?.NotifyMushroomFieldEntered();
            return;
        }

        var npc = other.GetComponentInParent<NPCFlocker>();
        if (npc == null) return;

        bool wasEmpty = _mycari.Count == 0;
        if (!_mycari.Add(npc)) return;

        if (wasEmpty)
        {
            FirstMycari = npc;
            OnMycariEntered?.Invoke(npc);
            TryAutoStart();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerInside = false;
            return;
        }

        var npc = other.GetComponentInParent<NPCFlocker>();
        if (npc == null) return;

        _mycari.Remove(npc);
        if (_mycari.Count == 0) FirstMycari = null;
        else if (FirstMycari == npc)
        {
            foreach (var m in _mycari) { FirstMycari = m; break; }
        }
    }

    /// <summary>
    /// Starts the melody the moment a Mycari is in the grove. The flow, when present, drives the
    /// demo itself (it needs the Mycari to walk pad to pad), so this only fires standalone.
    /// </summary>
    void TryAutoStart()
    {
        if (!autoStartOnMycari || _autoStarted) return;
        if (GameFlowManager.Instance != null) return;   // the flow owns the sequencing
        if (puzzle == null) return;

        _autoStarted = true;
        puzzle.StartPuzzle();
        Debug.Log("[MushroomField] A Mycari walked in — starting the melody.");
    }

    void OnDrawGizmosSelected()
    {
        var box = GetComponent<BoxCollider>();
        if (!box) return;
        Gizmos.color = new Color(0.55f, 0.85f, 0.4f, 0.35f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
