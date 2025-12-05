using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MartianSequencePuzzle : MonoBehaviour
{
    [Header("Four pads (buttons)")]
    public GameObject[] pads = new GameObject[4];

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip[] padClips = new AudioClip[4];
    public AudioClip wrongClip;      // Sound for wrong input
    public AudioClip successClip;    // Sound for puzzle success

    [Header("Timing")]
    public float flashDuration = 0.35f;
    public float stepDelay = 0.15f;

    [Header("Colors")]
    public Color idleColor = Color.white;
    public Color nearPlayerColor = Color.yellow;
    public Color sequenceColor = Color.blue;      // Blue when playing sequence
    public Color correctColor = Color.green;      // Green on correct input
    public Color wrongColor = Color.red;          // Red on wrong input
    public Color successColor1 = new Color(0.5f, 0f, 0.5f); // Purple
    public Color successColor2 = Color.white;      // White

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
    private Color[] originalColors;

    // Track which pad player is currently near
    private int nearPadIndex = -1;

    // Track if we're currently showing a feedback animation (prevents Update from overwriting)
    private bool showingFeedback = false;

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
        originalColors = new Color[pads.Length];

        for (int i = 0; i < pads.Length; i++)
        {
            if (pads[i] == null)
            {
                Debug.LogError($"MartianSequencePuzzle: Pad at index {i} is not assigned.");
                continue;
            }

            // Try to find renderer - check self first, then children
            padRenderers[i] = pads[i].GetComponent<Renderer>();
            if (padRenderers[i] == null)
            {
                padRenderers[i] = pads[i].GetComponentInChildren<Renderer>();
            }

            if (padRenderers[i])
            {
                // Create material instance to avoid shared material issues
                padMaterials[i] = padRenderers[i].material;
                originalColors[i] = padMaterials[i].color;
                Debug.Log($"Pad {i}: Found renderer on '{padRenderers[i].gameObject.name}', material: {padMaterials[i].name}");
            }
            else
            {
                Debug.LogError($"MartianSequencePuzzle: Pad {i} ('{pads[i].name}') has no Renderer component!");
                originalColors[i] = idleColor;
            }
        }
    }

    void Update()
    {
        // Don't update colors if we're showing feedback animation
        if (showingFeedback) return;
        
        // Update pad colors based on player proximity
        if (started && playerTurn && !sequencePlaying)
        {
            UpdatePadProximityColors();
        }
        else if (started && !playerTurn && !sequencePlaying && !puzzleCompleted)
        {
            // Not player's turn yet - all pads should be idle
            for (int i = 0; i < pads.Length; i++)
            {
                SetPadColor(i, idleColor);
            }
        }
    }

    private void UpdatePadProximityColors()
    {
        // Find player
        PlayerController player = FindObjectOfType<PlayerController>();
        if (!player) return;

        Vector3 playerPos = player.transform.position;
        float maxDist = 2f; // Use same radius as padInteractRadius from PlayerController

        int closestPad = -1;
        float closestDist = maxDist;

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

        // Update colors: yellow for closest, idle for others
        for (int i = 0; i < pads.Length; i++)
        {
            if (i == closestPad)
            {
                SetPadColor(i, nearPlayerColor); // Yellow when near
            }
            else
            {
                SetPadColor(i, idleColor); // White when idle
            }
        }

        nearPadIndex = closestPad;
    }

    /// <summary>
    /// Called from MushroomFieldTrigger when the player enters
    /// the mushroom field WITH at least one Martian follower.
    /// Runs only once.
    /// </summary>
    public void StartPuzzle()
    {
        if (started)
        {
            Debug.Log("StartPuzzle called but already started - ignoring");
            return;
        }
        
        if (pads == null || pads.Length != 4)
        {
            Debug.LogError("StartPuzzle: Pads not properly configured!");
            return;
        }

        Debug.Log("=== STARTING PUZZLE ===");
        started = true;

        GenerateSequence();
        StartCoroutine(PlaySequenceRoutine());
    }

    private void GenerateSequence()
    {
        sequence.Clear();
        int len = Random.Range(4, 9); // 4–8 turns

        for (int i = 0; i < len; i++)
            sequence.Add(Random.Range(0, 4));

        Debug.Log("Generated Sequence: " + string.Join(",", sequence));
    }

    private IEnumerator PlaySequenceRoutine()
    {
        Debug.Log("PlaySequenceRoutine started");
        sequencePlaying = true;
        playerTurn = false;

        // Reset all pads to idle before starting
        for (int i = 0; i < padMaterials.Length; i++)
        {
            if (padMaterials[i])
            {
                padMaterials[i].color = idleColor;
                Debug.Log($"Pad {i} set to idle (white)");
            }
        }

        yield return new WaitForSeconds(0.4f);

        for (int i = 0; i < sequence.Count; i++)
        {
            int id = sequence[i];
            Debug.Log($"Flashing pad {id} BLUE (sequence step {i + 1}/{sequence.Count})");
            
            // Flash the pad blue
            if (id >= 0 && id < padMaterials.Length && padMaterials[id])
            {
                Color beforeColor = padMaterials[id].color;
                padMaterials[id].color = sequenceColor; // Blue!
                Debug.Log($"Pad {id}: Changed from {beforeColor} to {padMaterials[id].color} (should be BLUE)");
                
                // Sound
                if (audioSource && padClips != null && id < padClips.Length && padClips[id])
                    audioSource.PlayOneShot(padClips[id]);
                
                yield return new WaitForSeconds(flashDuration);
                
                // Back to idle
                padMaterials[id].color = idleColor;
                Debug.Log($"Pad {id}: Back to idle (white)");
            }
            
            yield return new WaitForSeconds(stepDelay);
        }

        sequencePlaying = false;
        playerTurn = true;
        inputIndex = 0;

        Debug.Log("=== Player turn started. Puzzle is now interactable. ===");
    }

    // === Original direct call (still used internally) ===
    public void OnPadPress(int id)
    {
        Debug.Log($"OnPadPress called: id={id}, started={started}, playerTurn={playerTurn}, sequencePlaying={sequencePlaying}");
        
        if (!started) return;                 // puzzle hasn't begun
        if (!playerTurn || sequencePlaying) return;

        if (id < 0 || id >= pads.Length) return;

        // Check if correct
        bool isCorrect = (sequence[inputIndex] == id);

        Debug.Log($"Pad {id} pressed. Expected: {sequence[inputIndex]}. Correct: {isCorrect}");

        if (isCorrect)
        {
            // Flash green for correct input
            StartCoroutine(FlashCorrectPad(id));
            inputIndex++;

            if (inputIndex >= sequence.Count)
            {
                playerTurn = false;
                OnPuzzleSuccess();
            }
        }
        else
        {
            // Flash red for wrong input
            StartCoroutine(FlashWrongPad(id));
        }
    }

    private IEnumerator FlashCorrectPad(int id)
    {
        if (id < 0 || id >= pads.Length)
            yield break;

        // Block Update from overwriting colors
        showingFeedback = true;

        // Play pad sound immediately
        PlayPadSound(id);

        // Set color to GREEN for 1 second
        SetPadColor(id, correctColor);
        Debug.Log($"Pad {id} flashed GREEN (correct) - showingFeedback={showingFeedback}");

        yield return new WaitForSeconds(1f);

        // Back to idle
        SetPadColor(id, idleColor);
        
        // Allow Update to control colors again
        showingFeedback = false;
    }

    private IEnumerator FlashWrongPad(int id)
    {
        // Disable player input during wrong animation
        playerTurn = false;
        sequencePlaying = true; // Block all input
        showingFeedback = true; // Block Update from overwriting colors
        
        // Play BOTH pad sound AND wrong sound
        PlayPadSound(id);
        if (audioSource != null && wrongClip != null)
        {
            audioSource.PlayOneShot(wrongClip);
            Debug.Log("Playing wrong sound");
        }

        // Step 1: Show the pressed pad as RED for 1 second
        SetPadColor(id, wrongColor);
        Debug.Log($"Pad {id} flashed RED (wrong!) - showingFeedback={showingFeedback}");
        
        yield return new WaitForSeconds(1f);

        // Step 2: Rapidly flash ALL pads red for 0.5 seconds (5 rapid flashes)
        int rapidFlashCount = 5;
        float rapidFlashDuration = 0.5f / (rapidFlashCount * 2);
        
        for (int flash = 0; flash < rapidFlashCount; flash++)
        {
            // All pads RED
            SetAllPadsColor(wrongColor);
            yield return new WaitForSeconds(rapidFlashDuration);
            
            // All pads idle (white)
            SetAllPadsColor(idleColor);
            yield return new WaitForSeconds(rapidFlashDuration);
        }

        // Step 3: Reset all pads to idle
        SetAllPadsColor(idleColor);

        // Step 4: Small pause before replaying sequence
        yield return new WaitForSeconds(0.3f);

        // Step 5: Restart puzzle - replay the same sequence
        inputIndex = 0;
        sequencePlaying = false;
        showingFeedback = false; // Allow Update to control colors again
        StartCoroutine(PlaySequenceRoutine());
    }
    
    // Helper: Set a single pad's color
    private void SetPadColor(int id, Color color)
    {
        if (id < 0 || id >= pads.Length) return;
        
        // Update material color
        if (padMaterials[id] != null)
        {
            padMaterials[id].color = color;
        }
        
        // Also try to update renderer directly (for some shader types)
        if (padRenderers[id] != null)
        {
            var mat = padRenderers[id].material;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
        }
    }
    
    // Helper: Set all pads to a color
    private void SetAllPadsColor(Color color)
    {
        for (int i = 0; i < pads.Length; i++)
        {
            SetPadColor(i, color);
        }
    }
    
    // Helper: Play pad sound
    private void PlayPadSound(int id)
    {
        if (audioSource != null && padClips != null && id >= 0 && id < padClips.Length && padClips[id] != null)
        {
            audioSource.PlayOneShot(padClips[id]);
            Debug.Log($"Playing pad {id} sound");
        }
        else
        {
            Debug.LogWarning($"Cannot play pad {id} sound - audioSource: {audioSource != null}, padClips: {padClips != null}, clip exists: {(padClips != null && id < padClips.Length ? (padClips[id] != null).ToString() : "N/A")}");
        }
    }

    /// <summary>
    /// Called by PlayerController when interact key is pressed.
    /// If the puzzle is ready and the player is near a pad,
    /// it triggers that pad. Returns true if it consumed the interaction.
    /// </summary>
    public bool TryPlayerInteract(Transform player, float padInteractRadius)
    {
        Debug.Log($"TryPlayerInteract: started={started}, sequencePlaying={sequencePlaying}, playerTurn={playerTurn}, nearPadIndex={nearPadIndex}");
        
        // Not ready for player input yet
        if (!started || sequencePlaying || !playerTurn)
        {
            Debug.Log("Puzzle not ready for interaction");
            return false;
        }

        if (pads == null || pads.Length == 0)
        {
            Debug.Log("No pads assigned");
            return false;
        }

        // Use the cached nearPadIndex from Update - it's already calculated
        if (nearPadIndex >= 0 && nearPadIndex < pads.Length)
        {
            Debug.Log($"Using cached nearPadIndex: {nearPadIndex}");
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
            // Calculate XZ distance only (ignore height difference)
            float dx = padPos.x - playerPos.x;
            float dz = padPos.z - playerPos.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            
            Debug.Log($"Pad {i}: XZ distance = {dist}");
            
            if (dist <= padInteractRadius && dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        if (bestIndex == -1)
        {
            Debug.Log($"Player not close enough to any pad. Best XZ distance: {bestDist}, required: {padInteractRadius}");
            return false;
        }

        Debug.Log($"Player interacting with pad {bestIndex} at XZ distance {bestDist}");
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
    /// Check if puzzle is active (started but not completed)
    /// </summary>
    public bool IsPuzzleActive()
    {
        return started && !puzzleCompleted;
    }

    private void OnPuzzleSuccess()
    {
        Debug.Log("Sequence matched! Martians obey now.");

        // Flash all pads green on success
        StartCoroutine(SuccessAnimation());

        // === APPLY DECISION TO ALL NPCs ===
        if (NPCManager.Instance != null)
        {
            foreach (var npc in NPCManager.Instance.All)
            {
                if (npc != null && npc.IsAwaitingDecision())
                {
                    // Automatically approve task
                    npc.DecideWork(true);
                }
            }
        }
    }

    private IEnumerator SuccessAnimation()
    {
        showingFeedback = true;
        
        // Play success sound
        if (audioSource && successClip)
            audioSource.PlayOneShot(successClip);
        
        // Flash all pads purple and white 5 times in 2 seconds
        float totalTime = 2f;
        int flashCount = 5;
        float singleFlashDuration = totalTime / (flashCount * 2);
        
        for (int flash = 0; flash < flashCount; flash++)
        {
            // Purple
            SetAllPadsColor(successColor1);

            yield return new WaitForSeconds(singleFlashDuration);

            // White
            SetAllPadsColor(successColor2);

            yield return new WaitForSeconds(singleFlashDuration);
        }
        
        // Set to idle at the end
        SetAllPadsColor(idleColor);
        
        showingFeedback = false;
        puzzleCompleted = true;
        Debug.Log("Puzzle COMPLETED!");
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
                Gizmos.DrawWireSphere(pad.transform.position, 2f);
        }
    }
#endif
}
