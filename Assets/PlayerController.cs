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

    [Header("Puzzle")]
    public MartianSequencePuzzle puzzle;          // assign in Inspector
    [SerializeField] private float padInteractRadius = 2f; // how close to a pad to interact

    Animator animator;
    CharacterController cc;
    float scaleX;
    Vector3 velocity;

    // keep one active follower handle (you can expand to many later)
    NPCFlocker currentFollower;

    // expose whether the player currently has a follower (used by mushroom field trigger)
    public bool HasFollower => currentFollower != null;

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

        // PUZZLE INTERACTION ONLY - No NPC interaction during active puzzle
        if (puzzle != null && puzzle.IsPuzzleActive())
        {
            // Try to interact with a pad
            if (puzzle.TryPlayerInteract(transform, padInteractRadius))
            {
                Debug.Log("Interacted with puzzle pad");
            }
            else
            {
                Debug.Log("Puzzle active - move closer to a pad to interact");
            }
            // Block ALL other interactions during puzzle
            return;
        }

        // AFTER PUZZLE IS COMPLETE (or not started yet):
        
        // If we have a follower, handle follower interactions
        if (currentFollower)
        {
            // If we're inside the decision zone (windmill) but not parked yet, park to AwaitDecision
            if (currentFollower.IsInDecisionZone() && !currentFollower.IsAwaitingDecision())
            {
                currentFollower.SetAwaitDecision();
                Debug.Log("Follower set to await decision at windmill");
                return;
            }

            // If awaiting a decision, decide now (random)
            if (currentFollower.IsAwaitingDecision())
            {
                bool accept = Random.value <= agreeChance;
                currentFollower.DecideWork(accept);

                if (!accept) currentFollower = null;
                Debug.Log($"Decision made: {(accept ? "Accepted" : "Rejected")}");
                return;
            }

            // If GREEN (FollowApproved), allow assigning a site
            TaskSite site = TaskManager.Instance
                ? TaskManager.Instance.FindBestSiteForPlayer(transform, viewCam, siteRayRange, siteLayer)
                : null;

            if (site)
            {
                currentFollower.AssignTask(site);
                Debug.Log("Assigned task site to follower");
                return;
            }

            // Has follower but no valid action
            Debug.Log("Follower exists but no valid interaction available");
            return;
        }

        // No follower → try to recruit nearest NPC (only when puzzle not active)
        NPCFlocker npc = FindNearestNPC();
        if (npc != null)
        {
            if (npc.TryRecruit(transform))
            {
                currentFollower = npc;
                Debug.Log("Recruited new NPC follower");
            }
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
