using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simon-says style pad puzzle in the mushroom field.
///
/// Two ways to drive it:
///  - Scripted flow: <see cref="StartDemo"/> makes a Mycari physically walk the pads (each pad flashes and
///    sounds as it arrives), then <see cref="BeginPlayerAttempt"/> replays the sequence on the pads and opens
///    player input. The demo never enables player input by itself.
///  - Legacy / standalone: <see cref="StartPuzzle"/> generates a sequence and starts the player attempt in one go.
///
/// Coroutine ownership: at most one "main" routine runs at a time (demo, sequence replay, wrong-feedback,
/// success animation) and is tracked in <c>_mainRoutine</c>; the short green "correct" flashes are tracked per
/// pad. Every public entry point stops what it needs to before starting something new, so nothing stale keeps
/// painting pads.
///
/// Pads: a pad with a <see cref="MushroomPad"/> is sent cues (sing, correct, wrong, moods) and animates itself;
/// any other pad is recoloured through its material with the Colors below, as before.
/// </summary>
public class MartianSequencePuzzle : MonoBehaviour
{
    [Header("Four pads (buttons)")]
    public GameObject[] pads = new GameObject[4];

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip[] padClips = new AudioClip[4];
    public AudioClip wrongClip;      // Sound for wrong input
    public AudioClip successClip;    // Sound for puzzle success

    [Header("Sequence length")]
    [Tooltip("Shortest sequence GenerateSequence may produce (inclusive).")]
    [Min(1)] public int minLength = 4;
    [Tooltip("Longest sequence GenerateSequence may produce (inclusive).")]
    [Min(1)] public int maxLength = 8;

    [Header("Timing")]
    public float flashDuration = 0.35f;
    public float stepDelay = 0.15f;
    [Tooltip("How long a pad stays green (correct) or red (wrong) after the player presses it.")]
    public float feedbackHold = 1f;

    [Header("Demo (Mycari walks the pads)")]
    [Tooltip("The demo Mycari counts as at a pad once it is within this distance (metres) of where it was " +
             "sent: the pad's centre, or for a mushroom its VisitPoint at the foot of the stem.")]
    public float demoArriveDistance = 1.2f;
    [Tooltip("If the Mycari has not reached a pad after this many seconds the pad flashes anyway, so a stuck agent never stalls the flow.")]
    public float demoArriveTimeout = 8f;
    [Tooltip("Pause after each pad flash before the Mycari heads to the next pad.")]
    public float demoStepPause = 0.5f;

    [Header("Player proximity")]
    [Tooltip("XZ radius within which a pad highlights as 'near'. Keep in sync with PlayerController.padInteractRadius.")]
    public float padProximityRadius = 2.6f;

    [Header("Colors (plain pads only; MushroomPads carry their own)")]
    public Color idleColor = Color.white;
    public Color nearPlayerColor = Color.yellow;
    public Color sequenceColor = Color.blue;      // Blue when playing sequence
    public Color correctColor = Color.green;      // Green on correct input
    public Color wrongColor = Color.red;          // Red on wrong input
    public Color successColor1 = new Color(0.5f, 0f, 0.5f); // Purple
    public Color successColor2 = Color.white;      // White

    // ---- Events (GameFlowManager subscribes to these) ----

    /// <summary>The player repeated the whole sequence. Fires once the success animation has finished.</summary>
    public event System.Action OnSuccess;
    /// <summary>The player pressed a wrong pad. Fires once per mistake; the sequence then replays.</summary>
    public event System.Action OnFail;
    /// <summary>The pads finished replaying and the player may now press them (also after a failed replay).</summary>
    public event System.Action OnPlayerTurnStarted;
    /// <summary>The demo Mycari has walked every pad of the sequence.</summary>
    public event System.Action OnDemoFinished;

    private readonly List<int> sequence = new List<int>();
    private int inputIndex = 0;
    private bool playerTurn = false;
    private bool sequencePlaying = false;

    // Has the puzzle been started at least once?
    private bool started = false;

    // Track if puzzle is completed
    private bool puzzleCompleted = false;

    private Renderer[] padRenderers;
    private Material[] padMaterials; // Use material instances to avoid shared material issues
    private Color[] _appliedColors;  // last colour written per pad, so Update does not rewrite materials every frame
    private MushroomPad[] _mushrooms; // per pad; null for a plain pad driven through its material
    private bool _padsReady = false; // Awake validated the pad array and built the material instances

    // Track which pad player is currently near
    private int nearPadIndex = -1;

    // Track if we're currently showing a feedback animation (prevents Update from overwriting)
    private bool showingFeedback = false;

    // Coroutine ownership (see class summary)
    private Coroutine _mainRoutine;          // demo / replay / wrong-feedback / success — only one at a time
    private Coroutine[] _padFlashRoutines;   // per-pad green "correct" flash, may overlap with the player's turn

    // Cached player reference for proximity highlighting (no per-frame scene searches)
    private PlayerController _player;
    private float _nextPlayerLookup;

    // ---- Read-only state for other systems (HUD prompts, flow) ----

    /// <summary>True while the pads accept player presses.</summary>
    public bool IsPlayerTurn => started && playerTurn && !sequencePlaying;
    /// <summary>True while a Mycari demo walk is in progress.</summary>
    public bool IsDemoRunning { get; private set; }
    /// <summary>Index of the pad the player is currently standing next to, or -1.</summary>
    public int NearPadIndex => nearPadIndex;
    /// <summary>Length of the current sequence (0 until generated).</summary>
    public int SequenceLength => sequence.Count;
    /// <summary>How many pads of the sequence the player has repeated correctly so far.</summary>
    public int InputIndex => inputIndex;

    void Awake()
    {
        // Safety: make sure we actually have 4 pads
        if (pads == null || pads.Length != 4)
        {
            Debug.LogError("MartianSequencePuzzle: Please assign exactly 4 pads in the inspector.");
            return;
        }

        padRenderers = new Renderer[pads.Length];
        padMaterials = new Material[pads.Length];
        _appliedColors = new Color[pads.Length];
        _padFlashRoutines = new Coroutine[pads.Length];
        _mushrooms = new MushroomPad[pads.Length];

        for (int i = 0; i < pads.Length; i++)
        {
            // Sentinel so the first SetPadColor always writes the material
            _appliedColors[i] = new Color(-1f, -1f, -1f, -1f);

            if (pads[i] == null)
            {
                Debug.LogError($"MartianSequencePuzzle: Pad at index {i} is not assigned.");
                continue;
            }

            // A mushroom animates itself from cues. Its sprites must not get a material colour, so it
            // gets no material entry and SetPadColor skips it.
            _mushrooms[i] = pads[i].GetComponent<MushroomPad>();
            if (_mushrooms[i] != null) continue;

            // Try to find renderer - check self first, then children
            padRenderers[i] = pads[i].GetComponent<Renderer>();
            if (padRenderers[i] == null)
                padRenderers[i] = pads[i].GetComponentInChildren<Renderer>();

            if (padRenderers[i])
            {
                // Create material instance to avoid shared material issues
                padMaterials[i] = padRenderers[i].material;
            }
            else
            {
                Debug.LogError($"MartianSequencePuzzle: Pad {i} ('{pads[i].name}') has no Renderer component!");
            }
        }

        _padsReady = true;

        // One warning up front instead of one per pad press
        if (audioSource == null || padClips == null || padClips.Length < pads.Length)
            Debug.LogWarning("MartianSequencePuzzle: audioSource / padClips not fully assigned — pads will be silent.");
    }

    void Start()
    {
        _player = FindAnyObjectByType<PlayerController>();
    }

    void Update()
    {
        if (!_padsReady) return;

        // A main feedback routine (wrong / success) owns every pad colour while it runs
        if (showingFeedback) return;

        if (started && playerTurn && !sequencePlaying)
        {
            UpdatePadProximityColors();
        }
        else if (started && !playerTurn && !sequencePlaying && !puzzleCompleted)
        {
            // Not player's turn yet (e.g. after the demo) - all pads should be idle
            nearPadIndex = -1;
            SetAllPadsIdle();
        }
    }

    /// <summary>
    /// Cached player lookup. Found once in Start; if the player did not exist yet, retry at most once a second.
    /// </summary>
    private PlayerController GetPlayer()
    {
        if (_player != null) return _player;
        if (Time.time < _nextPlayerLookup) return null;
        _nextPlayerLookup = Time.time + 1f;
        _player = FindAnyObjectByType<PlayerController>();
        return _player;
    }

    private void UpdatePadProximityColors()
    {
        PlayerController player = GetPlayer();
        if (!player) return;

        Vector3 playerPos = player.transform.position;

        int closestPad = -1;
        float closestDist = padProximityRadius;

        // Find closest pad within range using XZ distance only
        for (int i = 0; i < pads.Length; i++)
        {
            if (pads[i] == null) continue;

            Vector3 padPos = pads[i].transform.position;
            // XZ distance only (ignore Y)
            float dx = padPos.x - playerPos.x;
            float dz = padPos.z - playerPos.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);

            if (dist <= closestDist)
            {
                closestDist = dist;
                closestPad = i;
            }
        }

        // Update colors: yellow for closest, idle for others.
        // A pad that is still flashing green from a correct press keeps its flash colour.
        // A mushroom takes a mood instead; its own reactions already layer over the mood.
        for (int i = 0; i < pads.Length; i++)
        {
            if (_mushrooms[i] != null)
            {
                _mushrooms[i].SetMood(i == closestPad ? MushroomPad.Mood.Near : MushroomPad.Mood.Ready);
                continue;
            }
            if (_padFlashRoutines[i] != null) continue;
            SetPadColor(i, i == closestPad ? nearPlayerColor : idleColor);
        }

        nearPadIndex = closestPad;
    }

    // =====================================================================
    //  Public control
    // =====================================================================

    /// <summary>
    /// Legacy entry point (standalone scene without GameFlowManager): generate a sequence and start the
    /// player attempt straight away. Runs only once.
    /// </summary>
    public void StartPuzzle()
    {
        if (started) return;

        if (!_padsReady)
        {
            Debug.LogError("StartPuzzle: Pads not properly configured!");
            return;
        }

        Debug.Log("[Puzzle] Starting puzzle");
        GenerateSequence();
        BeginPlayerAttempt();
    }

    /// <summary>
    /// Overrides the sequence length range used by the next GenerateSequence (both inclusive, clamped to >= 1).
    /// </summary>
    public void SetSequenceLength(int min, int max)
    {
        minLength = Mathf.Max(1, min);
        maxLength = Mathf.Max(minLength, max);
    }

    /// <summary>
    /// Scripted demo: <paramref name="demoNpc"/> walks from pad to pad in sequence order; each pad flashes blue
    /// and plays its note when the Mycari arrives. Generates the sequence if there is none yet. Marks the puzzle
    /// as started but does NOT open player input. Fires <see cref="OnDemoFinished"/> then <paramref name="onDone"/>.
    /// The Mycari is left standing on the last pad (the flow decides what it does next).
    /// If <paramref name="demoNpc"/> is null the pads still flash in order so the flow can continue.
    /// </summary>
    public void StartDemo(NPCFlocker demoNpc, System.Action onDone)
    {
        if (!_padsReady)
        {
            Debug.LogError("StartDemo: Pads not properly configured!");
            OnDemoFinished?.Invoke();
            onDone?.Invoke();
            return;
        }

        if (demoNpc == null)
            Debug.LogWarning("[Puzzle] StartDemo called without a Mycari — running a flash-only demo.");

        if (sequence.Count == 0) GenerateSequence();

        StopMainRoutine();
        StopPadFlashes();

        started = true;
        puzzleCompleted = false;
        playerTurn = false;
        inputIndex = 0;
        nearPadIndex = -1;

        _mainRoutine = StartCoroutine(DemoRoutine(demoNpc, onDone));
    }

    /// <summary>
    /// Replays the sequence on the pads, then opens player input (fires <see cref="OnPlayerTurnStarted"/>).
    /// Generates a sequence if there is none yet. Safe to call while a demo or previous attempt is running —
    /// whatever was running is stopped first.
    /// </summary>
    public void BeginPlayerAttempt()
    {
        if (!_padsReady)
        {
            Debug.LogError("BeginPlayerAttempt: Pads not properly configured!");
            return;
        }

        if (sequence.Count == 0) GenerateSequence();

        StopMainRoutine();
        StopPadFlashes();

        started = true;
        puzzleCompleted = false;
        inputIndex = 0;
        nearPadIndex = -1;

        _mainRoutine = StartCoroutine(PlaySequenceRoutine());
    }

    /// <summary>
    /// Stops everything and returns the puzzle to its never-started state (sequence cleared, pads idle).
    /// Used by checkpoint restores and by the flow to run the puzzle again.
    /// </summary>
    public void ResetPuzzle()
    {
        StopMainRoutine();
        StopPadFlashes();

        sequence.Clear();
        inputIndex = 0;
        playerTurn = false;
        sequencePlaying = false;
        showingFeedback = false;
        started = false;
        puzzleCompleted = false;
        nearPadIndex = -1;

        if (_padsReady)
        {
            SetAllPadsColor(idleColor);
            for (int i = 0; i < _mushrooms.Length; i++)
                if (_mushrooms[i] != null) _mushrooms[i].ResetVisuals();
        }
    }

    // =====================================================================
    //  Sequence generation / playback
    // =====================================================================

    private void GenerateSequence()
    {
        sequence.Clear();

        int min = Mathf.Max(1, Mathf.Min(minLength, maxLength));
        int max = Mathf.Max(min, maxLength);
        int len = Random.Range(min, max + 1); // inclusive range

        for (int i = 0; i < len; i++)
            sequence.Add(Random.Range(0, pads.Length));

        Debug.Log("[Puzzle] Generated sequence: " + string.Join(",", sequence));
    }

    /// <summary>
    /// The Mycari walks pad to pad; each pad flashes + sounds on arrival (or after the arrival timeout).
    /// </summary>
    private IEnumerator DemoRoutine(NPCFlocker npc, System.Action onDone)
    {
        IsDemoRunning = true;
        sequencePlaying = true;   // blocks player presses and Update's idle repaint while we paint pads
        playerTurn = false;

        SetAllPadsIdle();
        yield return new WaitForSeconds(0.4f);

        for (int i = 0; i < sequence.Count; i++)
        {
            int id = sequence[i];

            // Walk to the pad. 'npc != null' also covers a Mycari destroyed mid-demo (Unity null check).
            // A mushroom is solid, so the Mycari goes to its camera-side foot instead of its centre.
            if (npc != null && pads[id] != null)
            {
                bool arrived = false;
                var mushroom = MushroomAt(id);
                Vector3 target = mushroom != null ? mushroom.VisitPoint : pads[id].transform.position;
                npc.ScriptedMoveTo(target, demoArriveDistance, () => arrived = true);

                float deadline = Time.time + Mathf.Max(0.1f, demoArriveTimeout);
                while (!arrived && Time.time < deadline)
                    yield return null;
            }

            // Flash the pad blue + play its note (a mushroom sings in its own colour)
            SetPadColor(id, sequenceColor);
            CueSing(id);
            PlayPadSound(id);
            yield return new WaitForSeconds(flashDuration);
            SetPadColor(id, idleColor);

            yield return new WaitForSeconds(demoStepPause);
        }

        IsDemoRunning = false;
        sequencePlaying = false;
        _mainRoutine = null;      // cleared before the callbacks so a listener may start the next routine

        Debug.Log("[Puzzle] Demo finished");
        OnDemoFinished?.Invoke();
        onDone?.Invoke();
    }

    /// <summary>
    /// Replays the sequence on the pads, then hands control to the player.
    /// </summary>
    private IEnumerator PlaySequenceRoutine()
    {
        sequencePlaying = true;
        playerTurn = false;
        nearPadIndex = -1;

        // Reset all pads to idle before starting
        SetAllPadsIdle();
        yield return new WaitForSeconds(0.4f);

        for (int i = 0; i < sequence.Count; i++)
        {
            int id = sequence[i];

            // Flash the pad blue + sound (a mushroom sings in its own colour)
            SetPadColor(id, sequenceColor);
            CueSing(id);
            PlayPadSound(id);
            yield return new WaitForSeconds(flashDuration);
            SetPadColor(id, idleColor);

            yield return new WaitForSeconds(stepDelay);
        }

        sequencePlaying = false;
        playerTurn = true;
        inputIndex = 0;
        _mainRoutine = null;      // cleared before the event so a listener may start the next routine

        OnPlayerTurnStarted?.Invoke();
    }

    // =====================================================================
    //  Player input
    // =====================================================================

    // === Original direct call (still used internally) ===
    public void OnPadPress(int id)
    {
        if (!started) return;                 // puzzle hasn't begun
        if (!playerTurn || sequencePlaying) return;
        if (id < 0 || id >= pads.Length) return;
        if (inputIndex < 0 || inputIndex >= sequence.Count) return;

        bool isCorrect = (sequence[inputIndex] == id);

        if (isCorrect)
        {
            // Flash green for correct input
            StartPadFlash(id);
            inputIndex++;

            if (inputIndex >= sequence.Count)
            {
                playerTurn = false;
                OnPuzzleSuccess();
            }
        }
        else
        {
            // Flash red, replay the sequence, then let the player try again.
            // The routine is started before the event so a listener that resets the puzzle stops it cleanly.
            StopPadFlashes();
            StopMainRoutine();
            _mainRoutine = StartCoroutine(WrongFeedbackRoutine(id));
            OnFail?.Invoke();
        }
    }

    /// <summary>Starts (or restarts) the green flash on one pad.</summary>
    private void StartPadFlash(int id)
    {
        if (_padFlashRoutines[id] != null) StopCoroutine(_padFlashRoutines[id]);
        _padFlashRoutines[id] = StartCoroutine(FlashCorrectPad(id));
    }

    private IEnumerator FlashCorrectPad(int id)
    {
        // Play pad sound immediately
        PlayPadSound(id);

        // Hold GREEN, then back to idle. Update leaves this pad alone while the routine is registered.
        SetPadColor(id, correctColor);
        CueCorrect(id);
        yield return new WaitForSeconds(feedbackHold);
        SetPadColor(id, idleColor);

        _padFlashRoutines[id] = null;
    }

    private IEnumerator WrongFeedbackRoutine(int id)
    {
        // Disable player input during wrong animation
        playerTurn = false;
        sequencePlaying = true; // Block all input
        showingFeedback = true; // Block Update from overwriting colors
        nearPadIndex = -1;

        // Play BOTH pad sound AND wrong sound
        PlayPadSound(id);
        if (audioSource != null && wrongClip != null)
            audioSource.PlayOneShot(wrongClip);

        // Step 1: Show the pressed pad as RED (the rest go quiet so it stands out)
        CueMoods(MushroomPad.Mood.Dormant);
        SetPadColor(id, wrongColor);
        CueWrong(id);
        yield return new WaitForSeconds(feedbackHold);

        // Step 2: Rapidly flash ALL pads red for 0.5 seconds (5 rapid flashes)
        int rapidFlashCount = 5;
        float rapidFlashDuration = 0.5f / (rapidFlashCount * 2);

        for (int flash = 0; flash < rapidFlashCount; flash++)
        {
            SetAllPadsColor(wrongColor);
            CueAlarm(rapidFlashDuration);
            yield return new WaitForSeconds(rapidFlashDuration);

            SetAllPadsColor(idleColor);
            yield return new WaitForSeconds(rapidFlashDuration);
        }

        // Step 3: Reset all pads to idle
        SetAllPadsIdle();

        // Step 4: Small pause before replaying sequence
        yield return new WaitForSeconds(0.3f);

        // Step 5: Replay the same sequence. This routine ends right after, so the replay takes over the handle.
        inputIndex = 0;
        sequencePlaying = false;
        showingFeedback = false; // Allow Update to control colors again
        _mainRoutine = StartCoroutine(PlaySequenceRoutine());
    }

    // =====================================================================
    //  Pad helpers
    // =====================================================================

    // Helper: Set a single pad's color (skips the material write when the colour is unchanged)
    private void SetPadColor(int id, Color color)
    {
        if (padMaterials == null || id < 0 || id >= padMaterials.Length) return;

        var mat = padMaterials[id];
        if (mat == null) return;

        if (_appliedColors[id] == color) return;
        _appliedColors[id] = color;

        mat.color = color;

        // HDRP/URP Lit expose _BaseColor, older shaders only _Color — set whichever exists
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);
    }

    // Helper: Set all pads to a color
    private void SetAllPadsColor(Color color)
    {
        for (int i = 0; i < pads.Length; i++)
            SetPadColor(i, color);
    }

    // Helper: every pad at rest - plain pads idle-coloured, mushrooms dormant
    private void SetAllPadsIdle()
    {
        SetAllPadsColor(idleColor);
        CueMoods(MushroomPad.Mood.Dormant);
    }

    // ---- Mushroom cues (no-ops for plain pads) ----

    private MushroomPad MushroomAt(int id) =>
        _mushrooms != null && id >= 0 && id < _mushrooms.Length ? _mushrooms[id] : null;

    private void CueSing(int id)
    {
        var m = MushroomAt(id);
        if (m != null) m.Sing(flashDuration);
    }

    private void CueCorrect(int id)
    {
        var m = MushroomAt(id);
        if (m != null) m.Correct(feedbackHold);
    }

    private void CueWrong(int id)
    {
        var m = MushroomAt(id);
        if (m != null) m.Wrong(feedbackHold);
    }

    private void CueAlarm(float duration)
    {
        if (_mushrooms == null) return;
        for (int i = 0; i < _mushrooms.Length; i++)
            if (_mushrooms[i] != null) _mushrooms[i].Alarm(duration);
    }

    private void CueMoods(MushroomPad.Mood mood)
    {
        if (_mushrooms == null) return;
        for (int i = 0; i < _mushrooms.Length; i++)
            if (_mushrooms[i] != null) _mushrooms[i].SetMood(mood);
    }

    /// <summary>Wobbles the closest mushroom within <paramref name="radius"/> (XZ) without counting a press.</summary>
    private void PokeNearestMushroom(Vector3 from, float radius)
    {
        if (_mushrooms == null) return;

        MushroomPad best = null;
        float bestDist = radius;
        for (int i = 0; i < _mushrooms.Length; i++)
        {
            if (_mushrooms[i] == null) continue;
            Vector3 d = _mushrooms[i].transform.position - from;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist <= bestDist) { bestDist = dist; best = _mushrooms[i]; }
        }
        if (best != null) best.Poke();
    }

    // Helper: Play pad sound (silent if not configured — warned once in Awake)
    private void PlayPadSound(int id)
    {
        if (audioSource != null && padClips != null && id >= 0 && id < padClips.Length && padClips[id] != null)
            audioSource.PlayOneShot(padClips[id]);
    }

    // =====================================================================
    //  Coroutine ownership
    // =====================================================================

    /// <summary>Stops the current main routine (demo / replay / feedback) and releases the flags it owned.</summary>
    private void StopMainRoutine()
    {
        if (_mainRoutine != null)
        {
            StopCoroutine(_mainRoutine);
            _mainRoutine = null;
        }

        // A stopped routine never reaches its tail, so release what it would have released
        IsDemoRunning = false;
        sequencePlaying = false;
        showingFeedback = false;
    }

    /// <summary>Stops every per-pad green flash.</summary>
    private void StopPadFlashes()
    {
        if (_padFlashRoutines == null) return;

        for (int i = 0; i < _padFlashRoutines.Length; i++)
        {
            if (_padFlashRoutines[i] == null) continue;
            StopCoroutine(_padFlashRoutines[i]);
            _padFlashRoutines[i] = null;
        }
    }

    // =====================================================================
    //  Queries used by PlayerController
    // =====================================================================

    /// <summary>
    /// Called by PlayerController when interact key is pressed.
    /// If the puzzle is ready and the player is near a pad,
    /// it triggers that pad. Returns true if it consumed the interaction.
    /// </summary>
    public bool TryPlayerInteract(Transform player, float padInteractRadius)
    {
        if (pads == null || pads.Length == 0) return false;

        // Not ready for player input yet. A mushroom still gets poked - it just doesn't count.
        if (!started || sequencePlaying || !playerTurn)
        {
            PokeNearestMushroom(player.position, padInteractRadius);
            return false;
        }

        // Use the cached nearPadIndex from Update - it's already calculated
        if (nearPadIndex >= 0 && nearPadIndex < pads.Length)
        {
            OnPadPress(nearPadIndex);
            return true;
        }

        // Fallback: manually find closest pad using XZ distance only (ignore Y)
        Vector3 playerPos = player.position;
        int bestIndex = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < pads.Length; i++)
        {
            if (pads[i] == null) continue;

            Vector3 padPos = pads[i].transform.position;
            float dx = padPos.x - playerPos.x;
            float dz = padPos.z - playerPos.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);

            if (dist <= padInteractRadius && dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        if (bestIndex == -1) return false;

        OnPadPress(bestIndex);
        return true;
    }

    /// <summary>
    /// Check if puzzle is complete - used by PlayerController to prevent martian interaction
    /// </summary>
    public bool IsPuzzleCompleted()
    {
        return puzzleCompleted;
    }

    /// <summary>
    /// Check if puzzle is active (started but not completed). True during the demo as well.
    /// </summary>
    public bool IsPuzzleActive()
    {
        return started && !puzzleCompleted;
    }

    // =====================================================================
    //  Success
    // =====================================================================

    private void OnPuzzleSuccess()
    {
        Debug.Log("[Puzzle] Sequence matched!");

        // Flash all pads on success (OnSuccess fires when the animation ends)
        StopMainRoutine();
        _mainRoutine = StartCoroutine(SuccessAnimation());

        // Legacy standalone behaviour: approve ALL NPCs that are following the player.
        // They will accept the player's request based on their role:
        // - Worker will repair/work
        // - Attacker will destroy
        // - Wanderer will wander
        if (NPCManager.Instance != null)
        {
            foreach (var npc in NPCManager.Instance.All)
            {
                if (npc == null) continue;

                // Force approve any NPC that is following or awaiting decision
                if (npc.state == NPCState.FollowToDecision ||
                    npc.state == NPCState.AwaitDecision)
                {
                    npc.ForceApproveForWork();
                }
            }
        }
    }

    private IEnumerator SuccessAnimation()
    {
        // Stop the in-flight green flash of the last press so it cannot repaint a pad mid-animation
        StopPadFlashes();
        showingFeedback = true;

        // Play success sound
        if (audioSource && successClip)
            audioSource.PlayOneShot(successClip);

        // Mushrooms celebrate in a wave round the ring, each on its own clock
        for (int i = 0; i < _mushrooms.Length; i++)
            if (_mushrooms[i] != null) _mushrooms[i].Celebrate(i * 0.14f);

        // Flash all pads purple and white 5 times in 2 seconds
        float totalTime = 2f;
        int flashCount = 5;
        float singleFlashDuration = totalTime / (flashCount * 2);

        for (int flash = 0; flash < flashCount; flash++)
        {
            SetAllPadsColor(successColor1);
            yield return new WaitForSeconds(singleFlashDuration);

            SetAllPadsColor(successColor2);
            yield return new WaitForSeconds(singleFlashDuration);
        }

        // Set to idle at the end
        SetAllPadsColor(idleColor);
        CueMoods(MushroomPad.Mood.Solved);

        showingFeedback = false;
        puzzleCompleted = true;
        _mainRoutine = null;      // cleared before the event so a listener may reset / restart the puzzle

        Debug.Log("[Puzzle] Completed");
        OnSuccess?.Invoke();
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (pads == null) return;

        // Draw interaction spheres for each pad
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        foreach (var pad in pads)
        {
            if (pad != null)
                Gizmos.DrawWireSphere(pad.transform.position, padProximityRadius);
        }
    }
#endif
}
