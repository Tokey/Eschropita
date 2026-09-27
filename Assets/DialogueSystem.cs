using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class DialogueSystem : MonoBehaviour
{
    /// <summary>First DialogueSystem that woke up. Duplicates are logged, not destroyed.</summary>
    public static DialogueSystem Instance { get; private set; }

    /// <summary>Fired whenever a sequence finishes, is stopped or is interrupted by another one.</summary>
    public event System.Action OnDialogueEnded;

    [Header("UI (Canvas)")]
    [SerializeField] private GameObject dialoguePanel;   // panel root
    [SerializeField] private TMP_Text dialogueText;      // main text
    [SerializeField] private TMP_Text speakerText;       // optional
    [SerializeField] private GameObject continueHint;    // optional (arrow / "..." / icon)

    [Header("Typing (Pokemon style)")]
    [SerializeField] private float charsPerSecond = 35f;
    [SerializeField] private bool skipToFullLineOnAdvance = true;
    [SerializeField] private float punctuationPause = 0.06f; // extra pause on .,!?
    [SerializeField] private bool useUnscaledTime = false;

    [Header("Input")]
    [SerializeField] private KeyCode advanceKey = KeyCode.Space;

    [Header("Start Dialogue")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private DialogueSequence startSequence;

    [Header("Trigger Sets (configure in Inspector)")]
    [SerializeField] private List<DialogueTriggerConfig> triggers = new();

    // runtime
    DialogueSequence _active;
    int _lineIndex;
    bool _isTyping;
    bool _isPlaying;
    Coroutine _typingRoutine;

    // Frame on which the current sequence started; the advance key is ignored on that frame so the
    // key press that triggered the dialogue (e.g. Space to confirm something) can't skip its first line.
    int _startFrame = -1;

    // Play(DialogueLine[], onComplete) support: the throw-away sequence asset and its callback.
    DialogueSequence _runtimeSequence;
    System.Action _onComplete;

    // trigger state
    readonly Dictionary<string, bool> _playedOnce = new();
    readonly Dictionary<Collider, bool> _inZone = new();
    float _lastTriggerTime;

    /// <summary>True when a panel has been assigned (in the Inspector or via BindUI).</summary>
    public bool HasUI => dialoguePanel != null;

    // ---------------- Unity ----------------

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning($"[DialogueSystem] Duplicate DialogueSystem on '{name}' — '{Instance.name}' stays the Instance.", this);
        else
            Instance = this;

        SetUIVisible(false);
        if (continueHint) continueHint.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_runtimeSequence) Destroy(_runtimeSequence);
    }

    void Start()
    {
        if (playOnStart && startSequence) Play(startSequence);

        // mark oneshots that are pre-set as already played (optional)
        foreach (var t in triggers)
        {
            if (!string.IsNullOrWhiteSpace(t.triggerId) && !_playedOnce.ContainsKey(t.triggerId))
                _playedOnce[t.triggerId] = false;
        }
    }

    void Update()
    {
        // Input for advancing while dialogue is active (Space by default, Return/Enter always;
        // never on the frame the sequence started).
        bool advance = Input.GetKeyDown(advanceKey) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (_isPlaying && advance && Time.frameCount != _startFrame)
        {
            if (_isTyping && skipToFullLineOnAdvance) FinishTypingInstant();
            else NextLine();
        }

        // Key trigger (works even while not playing)
        HandleKeyTriggers();
    }

    void OnTriggerEnter(Collider other)
    {
        if (triggers == null || triggers.Count == 0) return;

        for (int i = 0; i < triggers.Count; i++)
        {
            var cfg = triggers[i];
            if (cfg == null || cfg.method != DialogueTriggerMethod.OnTriggerEnter) continue;
            if (cfg.triggerCollider == null) continue;

            // this DialogueSystem should sit on the collider object, OR you can assign triggerCollider explicitly
            if (other == null) continue;

            // If you assigned a specific trigger collider, only react when that collider is involved.
            // Common setup: put this DialogueSystem on an object that also has the trigger collider,
            // then leave triggerCollider empty and just rely on Unity's trigger callbacks.
            if (cfg.triggerCollider != null && other != cfg.requiredOtherCollider && cfg.requiredOtherCollider != null)
                continue;

            // Optional tag filter
            if (!string.IsNullOrWhiteSpace(cfg.requiredOtherTag) && !other.CompareTag(cfg.requiredOtherTag))
                continue;

            TryFire(cfg, other, isEnterEvent: true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (triggers == null || triggers.Count == 0) return;

        // Track "inside" for OnTriggerStay + PressWhileInZone
        if (other != null && _inZone.ContainsKey(other))
            _inZone[other] = false;
    }

    void OnTriggerStay(Collider other)
    {
        if (triggers == null || triggers.Count == 0) return;
        if (other == null) return;

        // mark inside
        _inZone[other] = true;

        for (int i = 0; i < triggers.Count; i++)
        {
            var cfg = triggers[i];
            if (cfg == null) continue;

            // Tag filter
            if (!string.IsNullOrWhiteSpace(cfg.requiredOtherTag) && !other.CompareTag(cfg.requiredOtherTag))
                continue;

            if (cfg.method == DialogueTriggerMethod.OnTriggerStayAfterSeconds)
            {
                // Stay timer per collider
                if (!_stayTime.ContainsKey((cfg, other))) _stayTime[(cfg, other)] = 0f;
                _stayTime[(cfg, other)] += Time.deltaTime;

                if (_stayTime[(cfg, other)] >= Mathf.Max(0f, cfg.staySeconds))
                {
                    TryFire(cfg, other, isEnterEvent: false);
                    _stayTime[(cfg, other)] = 0f; // reset so it can fire again if repeatable
                }
            }
            else if (cfg.method == DialogueTriggerMethod.PressKeyWhileInZone)
            {
                if (Input.GetKeyDown(cfg.pressKey))
                {
                    TryFire(cfg, other, isEnterEvent: false);
                }
            }
        }
    }

    // per-trigger per-collider stay timers
    readonly Dictionary<(DialogueTriggerConfig, Collider), float> _stayTime = new();

    // ---------------- Public API (call from buttons, puzzles, scripts) ----------------

    /// <summary>
    /// Hands the dialogue its UI at runtime (GameHUD builds the panel in code and calls this in Start).
    /// The panel is hidden immediately; if a sequence is already running it is re-shown on the new widgets.
    /// </summary>
    public void BindUI(GameObject panel, TMP_Text text, TMP_Text speaker, GameObject hint)
    {
        dialoguePanel = panel;
        dialogueText = text;
        speakerText = speaker;
        continueHint = hint;

        if (continueHint) continueHint.SetActive(false);
        SetUIVisible(false);

        // Mid-sequence rebind: show the current line fully typed on the new widgets.
        if (_isPlaying && _active != null && _lineIndex >= 0 && _lineIndex < _active.lines.Length)
        {
            SetUIVisible(true);
            var line = _active.lines[_lineIndex];
            if (speakerText != null)
            {
                speakerText.text = string.IsNullOrWhiteSpace(line.speaker) ? "" : line.speaker;
                speakerText.gameObject.SetActive(!string.IsNullOrWhiteSpace(line.speaker));
            }
            FinishTypingInstant();
        }
    }

    public void Play(DialogueSequence sequence)
    {
        if (sequence == null || sequence.lines == null || sequence.lines.Length == 0) return;

        // Hard interrupt current dialogue (configurable by per-trigger "queue" too)
        StopDialogue();

        _active = sequence;
        _lineIndex = 0;
        _isPlaying = true;
        _startFrame = Time.frameCount;

        SetUIVisible(true);
        ShowLine(_active.lines[_lineIndex]);
    }

    /// <summary>
    /// Plays ad-hoc lines from code. The lines are wrapped in a throw-away DialogueSequence;
    /// onComplete fires exactly once when that sequence ends, is stopped or is interrupted.
    /// </summary>
    public void Play(DialogueLine[] lines, System.Action onComplete = null)
    {
        if (lines == null || lines.Length == 0)
        {
            onComplete?.Invoke();
            return;
        }

        var seq = ScriptableObject.CreateInstance<DialogueSequence>();
        seq.name = "RuntimeDialogue";
        seq.hideFlags = HideFlags.HideAndDontSave;
        seq.lines = lines;

        // Play() stops (and completes) whatever was running first, so the new callback is
        // registered only after that, otherwise it would fire for the interrupted sequence.
        Play(seq);
        if (_active != seq) { Destroy(seq); onComplete?.Invoke(); return; }

        _runtimeSequence = seq;
        _onComplete = onComplete;
    }

    public void PlayById(string triggerId)
    {
        if (string.IsNullOrWhiteSpace(triggerId)) return;
        var cfg = FindById(triggerId);
        if (cfg != null && cfg.sequence != null)
            TryFire(cfg, null, isEnterEvent: false, force: true);
    }

    public void StopDialogue()
    {
        bool wasPlaying = _isPlaying;

        if (_typingRoutine != null)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }

        _isTyping = false;
        _isPlaying = false;
        _active = null;
        _lineIndex = 0;

        if (continueHint) continueHint.SetActive(false);
        SetUIVisible(false);

        // Release the throw-away sequence from Play(DialogueLine[]) and take its callback
        // before invoking anything, so a callback that starts a new dialogue can't re-enter.
        if (_runtimeSequence)
        {
            Destroy(_runtimeSequence);
            _runtimeSequence = null;
        }
        var done = _onComplete;
        _onComplete = null;

        if (wasPlaying)
        {
            done?.Invoke();
            OnDialogueEnded?.Invoke();
        }
    }

    public bool IsPlaying() => _isPlaying;

    // ---------------- Triggers ----------------

    void HandleKeyTriggers()
    {
        if (triggers == null || triggers.Count == 0) return;

        for (int i = 0; i < triggers.Count; i++)
        {
            var cfg = triggers[i];
            if (cfg == null) continue;
            if (cfg.method != DialogueTriggerMethod.PressGlobalKey) continue;

            if (Input.GetKeyDown(cfg.pressKey))
                TryFire(cfg, null, isEnterEvent: false);
        }
    }

    DialogueTriggerConfig FindById(string id)
    {
        if (triggers == null) return null;
        for (int i = 0; i < triggers.Count; i++)
        {
            var t = triggers[i];
            if (t != null && t.triggerId == id) return t;
        }
        return null;
    }

    void TryFire(DialogueTriggerConfig cfg, Collider other, bool isEnterEvent, bool force = false)
    {
        if (cfg == null || cfg.sequence == null) return;

        // Cooldown
        float cd = Mathf.Max(0f, cfg.cooldownSeconds);
        if (!force && cd > 0f && Time.time < _lastTriggerTime + cd) return;

        // One-shot gating
        if (!string.IsNullOrWhiteSpace(cfg.triggerId))
        {
            if (!_playedOnce.ContainsKey(cfg.triggerId)) _playedOnce[cfg.triggerId] = false;
            if (cfg.playOnce && _playedOnce[cfg.triggerId]) return;
        }

        // If dialogue already playing
        if (_isPlaying)
        {
            if (cfg.ifPlaying == DialogueIfPlayingBehavior.Ignore) return;

            if (cfg.ifPlaying == DialogueIfPlayingBehavior.Queue)
            {
                _queue.Enqueue(cfg.sequence);
                return;
            }

            // Interrupt
            StopDialogue();
        }

        _lastTriggerTime = Time.time;

        // Mark played
        if (!string.IsNullOrWhiteSpace(cfg.triggerId) && cfg.playOnce)
            _playedOnce[cfg.triggerId] = true;

        // Optional delay
        if (cfg.startDelaySeconds > 0f)
            StartCoroutine(DelayedPlay(cfg.sequence, cfg.startDelaySeconds));
        else
            Play(cfg.sequence);
    }

    readonly Queue<DialogueSequence> _queue = new();

    IEnumerator DelayedPlay(DialogueSequence seq, float delay)
    {
        if (useUnscaledTime) yield return new WaitForSecondsRealtime(delay);
        else yield return new WaitForSeconds(delay);
        Play(seq);
    }

    // When a dialogue ends, start queued one (if any)
    void TryPlayNextFromQueue()
    {
        if (_queue.Count == 0) return;
        var next = _queue.Dequeue();
        if (next) Play(next);
    }

    // ---------------- Rendering ----------------

    void ShowLine(DialogueLine line)
    {
        if (speakerText != null)
        {
            speakerText.text = string.IsNullOrWhiteSpace(line.speaker) ? "" : line.speaker;
            speakerText.gameObject.SetActive(!string.IsNullOrWhiteSpace(line.speaker));
        }

        if (continueHint) continueHint.SetActive(false);

        if (_typingRoutine != null)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }

        _typingRoutine = StartCoroutine(TypeLine(line.text));
    }

    IEnumerator TypeLine(string fullText)
    {
        _isTyping = true;

        // No text widget bound (HUD not present yet): nothing to type, just wait for the advance key.
        if (dialogueText == null)
        {
            _isTyping = false;
            _typingRoutine = null;
            if (continueHint) continueHint.SetActive(true);
            yield break;
        }

        dialogueText.text = "";

        if (string.IsNullOrEmpty(fullText))
        {
            _isTyping = false;
            if (continueHint) continueHint.SetActive(true);
            yield break;
        }

        float baseDelay = (charsPerSecond <= 0.01f) ? 0f : (1f / charsPerSecond);

        for (int i = 0; i < fullText.Length; i++)
        {
            char c = fullText[i];
            dialogueText.text += c;

            float extra = 0f;
            if (c == '.' || c == '!' || c == '?' || c == ',')
                extra = punctuationPause;

            float d = baseDelay + extra;

            if (d > 0f)
            {
                if (useUnscaledTime) yield return new WaitForSecondsRealtime(d);
                else yield return new WaitForSeconds(d);
            }
            else
            {
                yield return null;
            }
        }

        _isTyping = false;
        _typingRoutine = null;
        if (continueHint) continueHint.SetActive(true);
    }

    void FinishTypingInstant()
    {
        if (_active == null) return;
        if (_lineIndex < 0 || _lineIndex >= _active.lines.Length) return;

        if (_typingRoutine != null)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }

        if (dialogueText != null) dialogueText.text = _active.lines[_lineIndex].text;
        _isTyping = false;
        if (continueHint) continueHint.SetActive(true);
    }

    void NextLine()
    {
        if (_active == null) { StopDialogue(); return; }

        _lineIndex++;

        if (_lineIndex >= _active.lines.Length)
        {
            // end
            StopDialogue();
            TryPlayNextFromQueue();
            return;
        }

        ShowLine(_active.lines[_lineIndex]);
    }

    void SetUIVisible(bool visible)
    {
        if (dialoguePanel) dialoguePanel.SetActive(visible);
    }
}

// ---------------- Data ----------------

public enum DialogueTriggerMethod
{
    None,
    PressGlobalKey,             // press a key anywhere
    OnTriggerEnter,             // Unity trigger enter
    PressKeyWhileInZone,        // press key while staying inside a trigger zone
    OnTriggerStayAfterSeconds   // stay inside zone for N seconds
}

public enum DialogueIfPlayingBehavior
{
    Ignore,     // do nothing if dialogue currently playing
    Interrupt,  // stop current dialogue and start this one
    Queue       // queue it to play after current ends
}

[System.Serializable]
public class DialogueLine
{
    public string speaker;
    [TextArea(2, 6)]
    public string text;
}

[CreateAssetMenu(menuName = "Dialogue/Dialogue Sequence")]
public class DialogueSequence : ScriptableObject
{
    public DialogueLine[] lines;
}

[System.Serializable]
public class DialogueTriggerConfig
{
    [Tooltip("Unique id so you can also trigger it from code using PlayById(id).")]
    public string triggerId = "intro_01";

    public DialogueSequence sequence;

    [Header("Trigger Method")]
    public DialogueTriggerMethod method = DialogueTriggerMethod.None;

    [Tooltip("Used by PressGlobalKey / PressKeyWhileInZone")]
    public KeyCode pressKey = KeyCode.E;

    [Tooltip("Optional: require the other collider to have this tag (e.g., Player). Leave empty to ignore.")]
    public string requiredOtherTag = "Player";

    [Tooltip("If you want the zone to require a specific other collider, assign it here (optional).")]
    public Collider requiredOtherCollider;

    [Header("Timing")]
    public float startDelaySeconds = 0f;
    public float cooldownSeconds = 0f;
    public bool playOnce = true;

    [Header("Zone Only (OnTriggerStayAfterSeconds)")]
    public float staySeconds = 1.0f;

    [Header("If Dialogue Already Playing")]
    public DialogueIfPlayingBehavior ifPlaying = DialogueIfPlayingBehavior.Interrupt;

    [Header("Optional: explicit trigger collider reference (usually not needed)")]
    public Collider triggerCollider;
}
