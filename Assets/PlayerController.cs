using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 1f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Interaction")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private float interactRadius = 4f;
    [SerializeField] private LayerMask npcLayer;
    [SerializeField] private Camera viewCam;
    [SerializeField] private float siteRayRange = 20f;
    [SerializeField] private LayerMask siteLayer;

    [Header("Decision")]
    [Range(0f, 1f)] public float agreeChance = 0.6f;

    Animator animator;
    CharacterController cc;
    float scaleX;
    Vector3 velocity;

    // keep one active follower handle (you can expand to many later)
    NPCFlocker currentFollower;


    private float speedMultiplier = 1f;
    public void SetSpeedMultiplier(float mult) { speedMultiplier = mult; }

    void Start()
    {
        animator = GetComponent<Animator>();
        cc = GetComponent<CharacterController>();
        scaleX = transform.localScale.x;
        velocity = Vector3.zero;
        if (!viewCam) viewCam = Camera.main;
    }

    void Update()
    {
        HandleMove();
        HandleInteract();
    }

    void HandleMove()
    {
        Vector3 input = new Vector3(
            Input.GetAxis("Horizontal"),
            0f,
            Input.GetAxis("Vertical")
        ).normalized;

        Vector3 horizontalMove = input * moveSpeed * speedMultiplier;

        if (cc.isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        velocity.y += gravity * Time.deltaTime;

        Vector3 move = horizontalMove + Vector3.up * velocity.y;
        cc.Move(move * Time.deltaTime);

        if (animator) animator.SetFloat("Speed", input.sqrMagnitude);

        if (input.x != 0f)
        {
            Vector3 s = transform.localScale;
            s.x = scaleX * (input.x > 0 ? 1 : -1);
            transform.localScale = s;
        }
    }

    void HandleInteract()
    {
        if (!Input.GetKeyDown(interactKey)) return;

        // If we already have a follower, drive the decision / assignment flow
        if (currentFollower)
        {
            // 1) If we're inside the zone but not parked yet, park to AwaitDecision
            if (currentFollower.IsInDecisionZone() && !currentFollower.IsAwaitingDecision())
            {
                currentFollower.SetAwaitDecision();
                return; // next E will make the decision
            }

            // 2) If awaiting a decision, decide now (random) — NEW SIGNATURE: DecideWork(bool)
            if (currentFollower.IsAwaitingDecision())
            {
                bool accept = Random.value <= agreeChance;
                currentFollower.DecideWork(accept);

                // If declined (RED), drop our handle
                if (!accept) currentFollower = null;
                return;
            }

            // 3) If GREEN (FollowApproved), allow assigning a site
            //    (NPCFlocker.AssignTask(site) does nothing unless state == FollowApproved)
            TaskSite site = TaskManager.Instance
                ? TaskManager.Instance.FindBestSiteForPlayer(transform, viewCam, siteRayRange, siteLayer)
                : null;

            if (site)
            {
                currentFollower.AssignTask(site);
                return;
            }

            // Optional: if you want E to release a green follower back to roam, uncomment:
            // currentFollower.SwitchToRoam(); currentFollower = null; return;

            return;
        }

        // No follower yet → try to recruit nearest NPC (NEW: TryRecruit)
        NPCFlocker npc = FindNearestNPC();
        if (npc != null)
        {
            // TryRecruit returns false if NPC is cooling down (red) or otherwise not recruitable
            if (npc.TryRecruit(transform))
                currentFollower = npc;
        }
    }

    NPCFlocker FindNearestNPC()
    {
        var all = NPCManager.Instance ? NPCManager.Instance.All : null;
        Vector3 p = transform.position;
        NPCFlocker best = null;
        float bestD2 = float.MaxValue;

        if (all != null && all.Count > 0)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var n = all[i];
                if (!n) continue;
                float d2 = (n.transform.position - p).sqrMagnitude;
                if (d2 < interactRadius * interactRadius && d2 < bestD2)
                {
                    bestD2 = d2; best = n;
                }
            }
        }
        else
        {
            var hits = Physics.OverlapSphere(p, interactRadius, npcLayer, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                var n = h.GetComponentInParent<NPCFlocker>();
                if (!n) continue;
                float d2 = (n.transform.position - p).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = n; }
            }
        }
        if (best) Debug.DrawLine(p, best.transform.position, Color.green, 0.5f);
        return best;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
#endif
}
