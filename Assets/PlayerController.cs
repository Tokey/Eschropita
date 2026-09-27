using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 1f;
    [SerializeField] private float gravity = -9.81f;

    [Tooltip("Caps downward speed. Without this, anything that stops isGrounded reporting true " +
             "lets gravity build until a single Move step is long enough to tunnel through the floor.")]
    [SerializeField] private float terminalVelocity = -30f;

    [Header("Interaction")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private KeyCode dismissKey = KeyCode.Q;
    [SerializeField] private float interactRadius = 4f;
    [SerializeField] private LayerMask npcLayer;
    [SerializeField] private Camera viewCam;

    [Tooltip("How far a site can be and still be given orders.")]
    [SerializeField] private float assignDistance = 6f;

    [Tooltip("Walking this far from where the order menu opened cancels it.")]
    [SerializeField] private float commandCancelDistance = 2f;

    // Kept so the scene's serialized values do not warn. The site a follower is sent to is
    // now chosen by proximity (TaskManager.FindNearestSite), not by a camera ray.
    [SerializeField] private float siteRayRange = 20f;
    [SerializeField] private LayerMask siteLayer;

    [Tooltip("Obsolete: orders are accepted or refused by role now, not by a dice roll.")]
    [HideInInspector] public float agreeChance = 0.6f;

    [Header("Puzzle")]
    public MartianSequencePuzzle puzzle;          // assign in Inspector
    [Tooltip("How close (XZ, to the pad's centre) the player must be to press it. Mushroom pads are solid, " +
             "so this has to reach past their collider plus the player's own radius. Keep in sync with " +
             "MartianSequencePuzzle.padProximityRadius.")]
    [SerializeField] private float padInteractRadius = 2.6f;

    Animator animator;
    CharacterController cc;
    float scaleX;
    Vector3 velocity;

    // keep one active follower handle (you can expand to many later)
    NPCFlocker currentFollower;

    // expose whether the player currently has a follower (used by mushroom field trigger)
    public bool HasFollower => currentFollower != null;
    public NPCFlocker CurrentFollower => currentFollower;

    public int Shards => _shards;
    public event System.Action<int> OnShardsChanged;

    int _shards;

    // Order menu state: which site we are giving orders about, null when closed.
    TaskSite _commandSite;
    Vector3 _commandOpenedAt;

    // Context worked out once a frame and reused by both the E handler and the prompt,
    // so the prompt can never promise an action the key will not perform.
    NPCFlocker _nearNpc;
    TaskSite _nearSite;
    bool _canBuildShield;
    string _lastPrompt, _lastHints;
    GameHUD _lastHud;

    private float speedMultiplier = 1f;
    public void SetSpeedMultiplier(float mult) { speedMultiplier = mult; }

    public void AddShards(int amount)
    {
        if (amount == 0) return;
        _shards = Mathf.Max(0, _shards + amount);
        OnShardsChanged?.Invoke(_shards);
        if (amount > 0)
            GameHUD.Instance?.Toast(amount == 1 ? $"+1 shard ({_shards})" : $"+{amount} shards ({_shards})");
    }

    public void SetShards(int amount)
    {
        amount = Mathf.Max(0, amount);
        if (_shards == amount) return;
        _shards = amount;
        OnShardsChanged?.Invoke(_shards);
    }

    public void DismissFollower()
    {
        if (currentFollower != null)
            currentFollower.SwitchToRoam();
        currentFollower = null;
        _commandSite = null;
    }

    void OnDisable()
    {
        // Do not leave a ring burning on someone we are no longer pointing at.
        if (_nearNpc) _nearNpc.SetTargeted(false);
        _nearNpc = null;
    }

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
        // Videos, scripted beats and dialogue take the controls away, but gravity has to keep
        // running or the player floats off whatever they were standing on.
        var flow = GameFlowManager.Instance;
        bool dialogueOpen = DialogueSystem.Instance != null && DialogueSystem.Instance.IsPlaying();
        if ((flow != null && !flow.PlayerControlEnabled) || dialogueOpen)
        {
            ApplyGravityOnly();
            if (animator) animator.SetFloat("Speed", 0f);
            _commandSite = null;
            PushPrompt("", "");
            return;
        }

        ValidateFollower();
        HandleMove();
        UpdateContext();
        HandleCommandMode();
        HandleDismiss();
        HandleInteract();
        UpdatePrompt();
    }

    void ApplyGravityOnly()
    {
        if (cc.isGrounded && velocity.y < 0f) velocity.y = -2f;
        velocity.y += gravity * Time.deltaTime;
        velocity.y = Mathf.Max(velocity.y, terminalVelocity);
        cc.Move(Vector3.up * velocity.y * Time.deltaTime);
    }

    /// <summary>
    /// Drops the follower handle when the NPC is no longer actually following us - it was sent
    /// to a job, wandered out of range, was taken over by the scripted flow, or was destroyed.
    /// Without this the player keeps "having" a follower that is busy elsewhere and E does nothing.
    /// </summary>
    void ValidateFollower()
    {
        if (currentFollower == null) return;

        bool stillOurs = currentFollower.followTarget == transform &&
                         (currentFollower.state == NPCState.FollowToDecision ||
                          currentFollower.state == NPCState.AwaitDecision ||
                          currentFollower.state == NPCState.FollowApproved);

        if (!stillOurs)
        {
            currentFollower = null;
            _commandSite = null;
        }
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
        velocity.y = Mathf.Max(velocity.y, terminalVelocity);

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

    void UpdateContext()
    {
        var previous = _nearNpc;
        _nearNpc = currentFollower ? null : FindNearestNPC();

        // Ring follows the selection, so it is always obvious which one E will grab.
        if (previous != _nearNpc)
        {
            if (previous) previous.SetTargeted(false);
            if (_nearNpc) _nearNpc.SetTargeted(true);
        }

        _nearSite = TaskManager.Instance
            ? TaskManager.Instance.FindNearestSite(transform.position, assignDistance,
                                                   s => s.allowRepair || s.allowDismantle)
            : null;

        // Shards are only spendable on a site that wants a shield and is not already full.
        _canBuildShield = false;
        if (!currentFollower && _nearSite != null && _nearSite.canBuildShield &&
            _shards >= _nearSite.shardsPerShield)
        {
            bool shieldFull = _nearSite.maxShield > 0f && _nearSite.shield >= _nearSite.maxShield;
            _canBuildShield = !shieldFull;
        }
    }

    void HandleDismiss()
    {
        if (!Input.GetKeyDown(dismissKey)) return;
        if (currentFollower == null) return;
        GameHUD.Instance?.Toast("Mycari dismissed");
        DismissFollower();
    }

    /// <summary>
    /// While an order menu is open, 1 and 2 pick the order and Esc (or walking away) cancels it.
    /// </summary>
    void HandleCommandMode()
    {
        if (_commandSite == null) return;

        if (currentFollower == null)
        {
            _commandSite = null;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape) ||
            Vector3.Distance(transform.position, _commandOpenedAt) > commandCancelDistance)
        {
            _commandSite = null;
            return;
        }

        bool repair = _commandSite.allowRepair &&
                      (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1));
        bool dismantle = _commandSite.allowDismantle &&
                         (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2));

        if (!repair && !dismantle) return;

        var cmd = repair ? TaskCommand.Repair : TaskCommand.Dismantle;
        var site = _commandSite;
        var npc = currentFollower;
        _commandSite = null;

        if (npc.CommandTask(site, cmd))
        {
            GameHUD.Instance?.Toast($"Mycari heads to {SiteName(site)}");
        }
        else
        {
            // It refused and walked off in a huff; the handle is no longer ours either way.
            GameHUD.Instance?.Toast(cmd == TaskCommand.Repair
                ? "The Mycari refuses. Maybe a Fixer?"
                : "The Mycari refuses. Maybe a Breaker?");
        }
        currentFollower = null;
    }

    void HandleInteract()
    {
        if (!Input.GetKeyDown(interactKey)) return;

        // 1) The melody puzzle owns E while it is running - but only when the player is actually
        //    standing at a pad. Falling through otherwise means E still does something sensible
        //    in the mushroom field instead of silently doing nothing.
        if (puzzle != null && puzzle.IsPuzzleActive())
        {
            if (puzzle.TryPlayerInteract(transform, padInteractRadius)) return;
        }

        // 2) Follower + a site in range -> open the order menu, but only once the melody is
        //    learned. Before that it follows you around without understanding a word.
        if (currentFollower && _nearSite != null)
        {
            if (!currentFollower.CanBeCommanded)
            {
                GameHUD.Instance?.Toast("It follows, but it doesn't understand the request yet.");
                return;
            }
            _commandSite = _nearSite;
            _commandOpenedAt = transform.position;
            return;
        }

        // 3) No follower, enough shards, a site that takes a shield -> spend them.
        if (_canBuildShield && _nearSite != null)
        {
            _nearSite.AddShield(_nearSite.shieldPerBuild);
            SetShards(_shards - _nearSite.shardsPerShield);
            GameHUD.Instance?.Toast($"Shield built on {SiteName(_nearSite)} (-{_nearSite.shardsPerShield} shards)");
            return;
        }

        // 4) Otherwise try to recruit whoever is nearest.
        if (!currentFollower && _nearNpc != null)
        {
            if (_nearNpc.TryRecruit(transform))
            {
                currentFollower = _nearNpc;
                GameHUD.Instance?.Toast("Mycari is following you");
            }
            else
            {
                var flow = GameFlowManager.Instance;
                GameHUD.Instance?.Toast(flow != null && !flow.MycariObey
                    ? "It doesn't understand you yet."
                    : "It isn't interested right now.");
            }
        }
    }

    static string SiteName(TaskSite s) =>
        s == null ? "the site" : (string.IsNullOrWhiteSpace(s.displayName) ? s.type.ToString() : s.displayName);

    void UpdatePrompt()
    {
        string prompt = "";

        if (_commandSite != null)
        {
            // Only offer the orders this particular site actually accepts.
            if (_commandSite.allowRepair && _commandSite.allowDismantle)
                prompt = "1  Repair    2  Dismantle    Esc  Cancel";
            else if (_commandSite.allowRepair)
                prompt = "1  Repair    Esc  Cancel";
            else if (_commandSite.allowDismantle)
                prompt = "2  Dismantle    Esc  Cancel";
        }
        else if (puzzle != null && puzzle.IsPuzzleActive() && NearAPad())
        {
            prompt = "E  Tap mushroom";
        }
        else if (currentFollower && _nearSite != null)
        {
            prompt = currentFollower.CanBeCommanded ? "E  Give orders" : "It can't understand you yet";
        }
        else if (_canBuildShield && _nearSite != null)
        {
            prompt = $"E  Build shield ({_nearSite.shardsPerShield} shards)";
        }
        else if (!currentFollower && _nearNpc != null)
        {
            prompt = "E  Recruit Mycari";
        }

        string hints = currentFollower ? "V  Scan    Q  Dismiss" : "V  Scan";
        PushPrompt(prompt, hints);
    }

    bool NearAPad()
    {
        if (puzzle == null || puzzle.pads == null) return false;
        Vector3 p = transform.position;
        foreach (var pad in puzzle.pads)
        {
            if (pad == null) continue;
            Vector3 d = pad.transform.position - p;
            d.y = 0f;                                   // pads sit lower than the player
            if (d.magnitude <= padInteractRadius) return true;
        }
        return false;
    }

    /// <summary>Only pushes when the text actually changed, so the HUD's fade is not restarted every frame.</summary>
    void PushPrompt(string prompt, string hints)
    {
        var hud = GameHUD.Instance;
        if (hud == null) return;

        if (hud != _lastHud) { _lastPrompt = _lastHints = null; _lastHud = hud; }

        if (prompt != _lastPrompt) { hud.SetPrompt(prompt); _lastPrompt = prompt; }
        if (hints != _lastHints) { hud.SetHints(hints); _lastHints = hints; }
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
                if (!n || !n.CanBeRecruited()) continue;
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
                if (!n || !n.CanBeRecruited()) continue;
                float d2 = (n.transform.position - p).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = n; }
            }
        }

        return best;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, interactRadius);
        Gizmos.color = new Color(1f, 0.66f, 0.36f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, assignDistance);
    }
#endif
}
