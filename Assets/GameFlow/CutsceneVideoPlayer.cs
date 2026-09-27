using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Plays a full-screen cutscene (intro / outro) and reports completion exactly once.
///
/// With a <see cref="VideoClip"/> the VideoPlayer decodes into a RenderTexture that is handed to
/// <c>GameHUD.ShowVideo</c>; without a clip a placeholder card is shown for
/// <see cref="placeholderSeconds"/> so the whole flow can be played end-to-end before the real
/// videos exist. Everything runs on unscaled time (works while Time.timeScale == 0) and the
/// player may skip with any of <see cref="skipKeys"/> once a short grace period has elapsed.
///
/// If there is no GameHUD in the scene the component still works: the video is drawn on the
/// main camera's near plane and the placeholder simply waits out its duration.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
[DisallowMultipleComponent]
public class CutsceneVideoPlayer : MonoBehaviour
{
    public static CutsceneVideoPlayer Instance { get; private set; }

    [Header("Skipping")]
    [Tooltip("Any of these keys (GetKeyDown) skips the cutscene once the grace period has elapsed.")]
    [SerializeField] KeyCode[] skipKeys = { KeyCode.Space, KeyCode.Escape, KeyCode.Return, KeyCode.E };
    [Tooltip("When false the cutscene always plays to the end (Stop() still works).")]
    [SerializeField] bool skippable = true;
    [Tooltip("Grace period before skip keys are honoured, so a key still held from the previous screen does not skip instantly. The skip hint appears when this elapses.")]
    [Min(0f)] [SerializeField] float minSecondsBeforeSkip = 0.5f;

    [Header("Placeholder")]
    [Tooltip("How long the dummy placeholder card stays on screen when Play() is called without a clip.")]
    [Min(0f)] [SerializeField] float placeholderSeconds = 4f;

    [Header("Transitions")]
    [Tooltip("Fade-to-black duration used before and after the cutscene (via GameHUD.FadeToBlack / FadeFromBlack). 0 = hard cut.")]
    [Min(0f)] [SerializeField] float fadeSeconds = 0.35f;

    [Header("Audio")]
    [Tooltip("Direct-output volume applied to the clip's audio tracks.")]
    [Range(0f, 1f)] [SerializeField] float volume = 1f;

    const string PlaceholderSubtitle = "video placeholder — assign a clip on CutsceneVideoPlayer";
    const int FallbackWidth = 1280;
    const int FallbackHeight = 720;
    const float PrepareTimeoutSeconds = 15f;   // a clip that never prepares must not hang the flow
    const float EndWatchdogSlack = 3f;         // seconds past the clip length before we assume loopPointReached was lost
    const float FadeWaitSlack = 0.5f;          // extra wait on a HUD fade callback before giving up on it

    VideoPlayer _video;
    RenderTexture _rt;
    Coroutine _routine;
    Action _onDone;
    bool _initialised;
    bool _doneFired;
    bool _stopRequested;   // Stop() asks the running routine to wind down gracefully
    bool _prepared;        // set by prepareCompleted
    bool _videoFinished;   // set by loopPointReached / errorReceived
    bool _fadeDone;        // set by the HUD fade callback that matches _fadeToken
    int _fadeToken;        // guards against a stale callback from an aborted fade
    bool _skipHintShown;
    bool _quitting;
    float _startTime;      // unscaled time Play() was called; drives the skip grace period

    /// <summary>True from Play() until onDone has fired (transitions included).</summary>
    public bool IsPlaying => _routine != null;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("[CutsceneVideoPlayer] Duplicate instance on '" + name + "'; keeping the first one.", this);
        else
            Instance = this;

        Init();
    }

    /// <summary>Idempotent setup so Play() also works if another script calls it before our Awake ran.</summary>
    void Init()
    {
        if (_initialised) return;
        _initialised = true;

        _video = GetComponent<VideoPlayer>();
        _video.Stop();                      // in case playOnAwake was ticked in the scene
        _video.playOnAwake = false;
        _video.isLooping = false;
        _video.waitForFirstFrame = true;
        _video.skipOnDrop = true;
        _video.timeUpdateMode = VideoTimeUpdateMode.DSPTime; // audio clock: keeps running at Time.timeScale == 0

        _video.prepareCompleted += OnPrepareCompleted;
        _video.loopPointReached += OnLoopPointReached;
        _video.errorReceived += OnErrorReceived;
    }

    void OnDisable()
    {
        // Losing the component mid-cutscene must not leave the flow waiting forever.
        if (IsPlaying) Abort();
    }

    void OnApplicationQuit()
    {
        _quitting = true;
    }

    void OnDestroy()
    {
        if (_video != null)
        {
            _video.prepareCompleted -= OnPrepareCompleted;
            _video.loopPointReached -= OnLoopPointReached;
            _video.errorReceived -= OnErrorReceived;
        }
        ReleaseRenderTexture();
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ public API

    /// <summary>
    /// Plays <paramref name="clip"/> (or the placeholder card when null) and calls
    /// <paramref name="onDone"/> exactly once when it ends, is skipped, stopped or fails.
    /// </summary>
    public void Play(VideoClip clip, string placeholderTitle, Action onDone)
    {
        if (!isActiveAndEnabled)
        {
            Debug.LogWarning("[CutsceneVideoPlayer] Play() called while disabled; completing immediately.", this);
            SafeInvoke(onDone);
            return;
        }

        Init();

        if (IsPlaying)
        {
            Debug.LogWarning("[CutsceneVideoPlayer] Play() called while a cutscene is running; the previous one is aborted (its onDone still fires).", this);
            Abort();
        }

        _onDone = onDone;
        _doneFired = false;
        _stopRequested = false;
        _prepared = false;
        _videoFinished = false;
        _skipHintShown = false;
        _startTime = Time.unscaledTime;

        _routine = StartCoroutine(Run(clip, placeholderTitle ?? ""));
    }

    /// <summary>
    /// Ends the current cutscene early. The routine winds down like a skip (fade out, hide,
    /// release) and onDone fires once at the end of that; no-op when nothing is playing.
    /// </summary>
    public void Stop()
    {
        if (!IsPlaying) return;
        _stopRequested = true;
    }

    // ------------------------------------------------------------------ main routine

    IEnumerator Run(VideoClip clip, string title)
    {
        bool useHud = GameHUD.Instance != null;
        if (!useHud)
            Debug.LogWarning("[CutsceneVideoPlayer] No GameHUD in the scene; falling back to CameraNearPlane on Camera.main.", this);

        SetSkipHint(false);
        yield return Fade(true);

        if (!_stopRequested)
        {
            if (clip == null) yield return RunPlaceholder(title, useHud);
            else yield return RunClip(clip, useHud);
        }

        // Wind down: back to black, drop the video layer, reveal the game, then report.
        SetSkipHint(false);
        yield return Fade(true);
        Teardown();
        yield return Fade(false);
        yield return null;   // even with instant fades, the skip key's frame must not leak into what the flow starts next

        _routine = null;   // cleared before the callback so onDone may chain straight into another Play()
        FireDone();
    }

    IEnumerator RunPlaceholder(string title, bool useHud)
    {
        if (useHud)
        {
            var hud = GameHUD.Instance;
            if (hud != null) hud.ShowVideoPlaceholder(title, PlaceholderSubtitle);
        }
        else
        {
            Debug.Log($"[CutsceneVideoPlayer] Placeholder '{title}' (no GameHUD to draw it) for {placeholderSeconds:0.#}s.", this);
        }

        yield return Fade(false);

        float end = Time.unscaledTime + placeholderSeconds;
        while (!_stopRequested && Time.unscaledTime < end)
        {
            if (PollSkip()) yield break;
            yield return null;
        }
    }

    IEnumerator RunClip(VideoClip clip, bool useHud)
    {
        ConfigurePlayer(clip, useHud);
        _video.Prepare();

        // Wait for the decoder (bounded).
        float deadline = Time.unscaledTime + PrepareTimeoutSeconds;
        while (!_prepared && !_videoFinished && !_stopRequested)
        {
            if (Time.unscaledTime > deadline)
            {
                Debug.LogError($"[CutsceneVideoPlayer] '{clip.name}' did not prepare within {PrepareTimeoutSeconds:0}s; skipping it.", this);
                yield break;
            }
            if (PollSkip()) yield break;
            yield return null;
        }
        if (_stopRequested || _videoFinished) yield break;   // stopped, or errorReceived during Prepare

        ApplyVolume();
        if (useHud)
        {
            var hud = GameHUD.Instance;
            if (hud != null) hud.ShowVideo(_rt);
        }
        _video.Play();
        float playStarted = Time.unscaledTime;
        double watchdog = (_video.length > 0 ? _video.length : clip.length) + EndWatchdogSlack;

        yield return Fade(false);

        while (!_videoFinished && !_stopRequested)
        {
            if (PollSkip()) yield break;
            if (Time.unscaledTime - playStarted > watchdog)
            {
                Debug.LogWarning($"[CutsceneVideoPlayer] '{clip.name}' ran past its length without loopPointReached; ending it.", this);
                yield break;
            }
            yield return null;
        }
    }

    // ------------------------------------------------------------------ video player setup / teardown

    void ConfigurePlayer(VideoClip clip, bool useHud)
    {
        _video.Stop();   // also drops any previous prepared state so the new clip is re-prepared
        _video.source = VideoSource.VideoClip;
        _video.clip = clip;
        _video.playOnAwake = false;
        _video.isLooping = false;
        _video.audioOutputMode = VideoAudioOutputMode.Direct;

        if (useHud)
        {
            int w = (int)clip.width;
            int h = (int)clip.height;
            if (w <= 0 || h <= 0) { w = FallbackWidth; h = FallbackHeight; }

            ReleaseRenderTexture();
            _rt = new RenderTexture(w, h, 0)
            {
                name = "CutsceneVideoRT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _rt.Create();
            ClearToBlack(_rt);   // no garbage frame before the first decoded one

            _video.renderMode = VideoRenderMode.RenderTexture;
            _video.targetTexture = _rt;
        }
        else
        {
            var cam = Camera.main;
            if (cam != null)
            {
                _video.renderMode = VideoRenderMode.CameraNearPlane;
                _video.targetCamera = cam;
                _video.targetCameraAlpha = 1f;
                _video.aspectRatio = VideoAspectRatio.FitInside;
            }
            else
            {
                Debug.LogWarning("[CutsceneVideoPlayer] No GameHUD and no main camera; the clip will play audio only.", this);
                _video.renderMode = VideoRenderMode.APIOnly;
            }
        }
    }

    /// <summary>Applies <see cref="volume"/> to every controlled track. Valid only once the clip is prepared.</summary>
    void ApplyVolume()
    {
        ushort tracks = (ushort)Mathf.Min(_video.audioTrackCount, _video.controlledAudioTrackCount);
        for (ushort i = 0; i < tracks; i++)
            _video.SetDirectAudioVolume(i, volume);
    }

    static void ClearToBlack(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prev;
    }

    /// <summary>Stops playback, hides the HUD video layer and frees the RenderTexture.</summary>
    void Teardown()
    {
        if (_video != null)
        {
            _video.Stop();
            _video.targetTexture = null;
        }

        var hud = GameHUD.Instance;
        if (hud != null)
        {
            hud.SetVideoSkipHint(false);
            hud.HideVideo();
        }

        ReleaseRenderTexture();
    }

    void ReleaseRenderTexture()
    {
        if (_rt == null) return;
        _rt.Release();
        if (Application.isPlaying) Destroy(_rt); else DestroyImmediate(_rt);
        _rt = null;
    }

    /// <summary>Immediate end without transitions (Play() while playing, component disabled). Fires onDone once.</summary>
    void Abort()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
        _stopRequested = false;
        Teardown();
        FireDone();
    }

    void FireDone()
    {
        if (_doneFired) return;
        _doneFired = true;

        var cb = _onDone;
        _onDone = null;
        if (_quitting) return;   // nobody is listening any more; avoid errors during shutdown
        SafeInvoke(cb);
    }

    void SafeInvoke(Action cb)
    {
        if (cb == null) return;
        try { cb(); }
        catch (Exception e) { Debug.LogException(e, this); }   // a throwing listener must not corrupt our state
    }

    // ------------------------------------------------------------------ input / HUD helpers

    /// <summary>
    /// True on the frame a skip key is pressed (after the grace period). Also reveals the
    /// HUD skip hint the first time the skip becomes available.
    /// </summary>
    bool PollSkip()
    {
        if (!skippable || skipKeys == null) return false;
        if (Time.unscaledTime - _startTime < minSecondsBeforeSkip) return false;

        if (!_skipHintShown)
        {
            _skipHintShown = true;
            SetSkipHint(true);
        }

        for (int i = 0; i < skipKeys.Length; i++)
            if (Input.GetKeyDown(skipKeys[i])) return true;
        return false;
    }

    static void SetSkipHint(bool visible)
    {
        var hud = GameHUD.Instance;
        if (hud != null) hud.SetVideoSkipHint(visible);
    }

    /// <summary>Runs a HUD fade and waits for it (bounded), or yields one frame when there is no HUD / no fade.</summary>
    IEnumerator Fade(bool toBlack)
    {
        var hud = GameHUD.Instance;
        if (hud == null || fadeSeconds <= 0f)
        {
            yield return null;   // still separate the stages by a frame so one key press cannot be read twice
            yield break;
        }

        int token = ++_fadeToken;
        _fadeDone = false;
        Action done = () => { if (token == _fadeToken) _fadeDone = true; };
        if (toBlack) hud.FadeToBlack(fadeSeconds, done);
        else hud.FadeFromBlack(fadeSeconds, done);

        // Bounded wait: a HUD destroyed mid-fade must not stall the cutscene.
        float deadline = Time.unscaledTime + fadeSeconds + FadeWaitSlack;
        while (!_fadeDone && Time.unscaledTime < deadline)
            yield return null;
    }

    // ------------------------------------------------------------------ VideoPlayer events

    void OnPrepareCompleted(VideoPlayer vp)
    {
        _prepared = true;
    }

    void OnLoopPointReached(VideoPlayer vp)
    {
        _videoFinished = true;
    }

    void OnErrorReceived(VideoPlayer vp, string message)
    {
        Debug.LogError("[CutsceneVideoPlayer] Video error: " + message + " — treating the cutscene as finished.", this);
        _videoFinished = true;
    }

    // ------------------------------------------------------------------ designer helpers (Inspector gear menu)

    [ContextMenu("Test Play Placeholder")]
    void TestPlayPlaceholder()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[CutsceneVideoPlayer] Enter Play Mode to test the placeholder card.", this);
            return;
        }
        Play(null, "TEST CUTSCENE", () => Debug.Log("[CutsceneVideoPlayer] Test placeholder finished.", this));
    }

    [ContextMenu("Test Play Assigned Clip")]
    void TestPlayAssignedClip()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[CutsceneVideoPlayer] Enter Play Mode to test a clip.", this);
            return;
        }
        var clip = GetComponent<VideoPlayer>().clip;
        if (clip == null)
        {
            Debug.LogWarning("[CutsceneVideoPlayer] Assign a Video Clip on the VideoPlayer component first.", this);
            return;
        }
        Play(clip, clip.name, () => Debug.Log("[CutsceneVideoPlayer] Test clip finished.", this));
    }

    [ContextMenu("Test Stop")]
    void TestStop()
    {
        if (Application.isPlaying) Stop();
    }
}
