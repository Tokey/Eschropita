using UnityEngine;

public class PlayerInteractor : MonoBehaviour
{
    [Header("Interaction")]
    public KeyCode interactKey = KeyCode.E;
    public float interactRadius = 3.0f;
    [Range(-1f, 1f)] public float minDotForward = 0.1f; // require roughly in-front (optional)
    public LayerMask npcLayer;

    private NPCFlocker _currentFollowing; // the one currently following (optional)

    void Update()
    {
        if (Input.GetKeyDown(interactKey))
        {
            // If someone is following already, toggle them back to roam
            if (_currentFollowing != null)
            {
                _currentFollowing.SwitchToRoam();
                _currentFollowing = null;
                return;
            }

            // Otherwise, pick closest NPC in front
            var npc = FindBestNPC();
            if (npc != null)
            {
                npc.SwitchToFollow(transform);
                _currentFollowing = npc;
            }
        }
    }

    NPCFlocker FindBestNPC()
    {
        var all = NPCManager.Instance ? NPCManager.Instance.All : null;
        if (all == null) return null;

        Vector3 p = transform.position;
        Vector3 fwd = transform.forward;

        NPCFlocker best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < all.Count; i++)
        {
            var n = all[i];
            float d = Vector3.Distance(p, n.transform.position);
            if (d > interactRadius) continue;

            Vector3 dir = (n.transform.position - p);
            dir.y = 0f;
            dir.Normalize();

            float dot = Vector3.Dot(fwd, dir);
            if (dot < minDotForward) continue;

            if (d < bestDist)
            {
                bestDist = d;
                best = n;
            }
        }
        return best;
    }
}
