using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The whole in-game HUD, built 100% from code in Awake (no prefabs, no scene references).
///
/// Layers (children of the canvas, bottom to top):
///   VideoLayer   – black + RawImage for cutscenes / placeholder card
///   ScanLayer    – bars, objective, world labels, scan tint + sweep line (visible only while Daffodil scans)
///   PromptLayer  – bottom-center contextual prompt + key hints, top-center toast
///   DialogueLayer– bottom panel handed to DialogueSystem
///   GameOverLayer/ EndLayer – full-screen cards that wait for Space
///   FadeLayer    – black overlay for FadeToBlack / FadeFromBlack
///
/// Design language: one accent colour, rounded translucent panels, pill bars, tiny uppercase labels.
/// All sprites are generated procedurally and cached statically.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

    // ------------------------------------------------------------------ style

    [Header("Style")]
    [Tooltip("The one accent colour: keycaps, objective dot, Daffodil's energy, scan tint.")]
    public Color accent = new Color32(0xE8, 0xA8, 0x5C, 0xFF);
    [Tooltip("Bar fill when a value is healthy (>= 50%).")]
    public Color good = new Color32(0x7F, 0xD8, 0xA6, 0xFF);
    [Tooltip("Bar fill when a value is getting low (25–50%).")]
    public Color warn = new Color32(0xE8, 0xC1, 0x5C, 0xFF);
    [Tooltip("Bar fill when a value is critical (< 25%).")]
    public Color bad = new Color32(0xE8, 0x6A, 0x5C, 0xFF);
    [Tooltip("Translucent background of every rounded panel.")]
    public Color panelColor = new Color(10f / 255f, 12f / 255f, 16f / 255f, 0.62f);
    [Tooltip("Empty part of a bar.")]
    public Color barTrack = new Color(1f, 1f, 1f, 0.10f);
    public Color textColor = new Color32(0xF2, 0xEF, 0xE9, 0xFF);
    [Tooltip("Secondary text: bar labels, hints, subtitles.")]
    public Color dimText = new Color(242f / 255f, 239f / 255f, 233f / 255f, 0.55f);
    [Tooltip("Seconds for the scan overlay / toast to fade in or out (unscaled time).")]
    public float fadeSeconds = 0.15f;

    [Header("Layout")]
    [Tooltip("Metres above a scanned target's pivot where its world label sits.")]
    public float labelHeight = 1.8f;
    [Tooltip("Seconds for the scan line to sweep from the top of the screen to the bottom.")]
    public float scanSweepSeconds = 1.8f;
    [Tooltip("Exponential lerp rate (per second) for animated bar fills.")]
    public float barLerpRate = 10f;

    // ------------------------------------------------------------------ constants

    const float RefWidth = 1920f;
    const float RefHeight = 1080f;
    const float Margin = 24f;          // distance from the screen edge for corner panels
    const float PanelRadius = 14f;
    const float PanelPadding = 16f;
    const float BarHeight = 10f;
    const float ThinBarHeight = 4f;
    const float ScanPanelWidth = 300f;
    const float DialogueHeight = 150f;
    const float DialogueBottom = 24f;
    const float EnergyPanelHeight = 52f;  // always-on Daffodil readout above the scan panel
    const float PromptLowY = 34f;         // resting height of the prompt above the bottom edge
    const int MaxWorldLabels = 24;
    const int UILayer = 5;

    // ------------------------------------------------------------------ public API (properties)

    /// <summary>Target surface for CutsceneVideoPlayer's RenderTexture.</summary>
    public RawImage VideoSurface { get; private set; }

    /// <summary>When true the OBELISK bars are shown in the scan panel (the flow sets it once discovered).</summary>
    public bool ObeliskVisible { get; set; }

    /// <summary>True while the scan overlay has any visible alpha (including fade in/out).</summary>
    public bool IsScanOverlayVisible => _scanGroup != null && _scanGroup.alpha > 0.001f;

    // ------------------------------------------------------------------ runtime widgets

    Canvas _canvas;
    RectTransform _canvasRect;

    // video
    GameObject _videoLayer;
    AspectRatioFitter _videoAspect;
    GameObject _videoPlaceholderRoot;
    TextMeshProUGUI _videoTitle;
    TextMeshProUGUI _videoSubtitle;
    GameObject _videoSkipHint;

    // scan overlay
    GameObject _scanLayer;
    CanvasGroup _scanGroup;
    RectTransform _scanLine;
    float _scanSweepT;
    float _promptY;
    HudBar _daffodilBar, _lifeBar, _windmillBar, _windmillShieldBar, _obeliskBar, _obeliskShieldBar;
    GameObject _objectiveRoot;
    TextMeshProUGUI _objectiveText;
    string _objectiveOverride = "";
    string _objectiveShown;
    RectTransform _labelRoot;
    readonly List<WorldLabel> _labels = new List<WorldLabel>();

    // prompt layer
    KeyRow _prompt;
    KeyRow _hints;
    GameObject _toastRoot;
    CanvasGroup _toastGroup;
    TextMeshProUGUI _toastText;
    float _toastUntil = -1f;

    // dialogue layer
    GameObject _dialoguePanel;
    TextMeshProUGUI _dialogueBody;
    TextMeshProUGUI _dialogueSpeaker;
    GameObject _dialogueHint;
    bool _dialogueBound;

    // game over / end
    GameObject _gameOverLayer;
    CanvasGroup _gameOverGroup;
    TextMeshProUGUI _gameOverSubtitle;
    GameObject _endLayer;
    CanvasGroup _endGroup;
    TextMeshProUGUI _endSubtitle;
    Coroutine _overlayRoutine;

    // fade
    CanvasGroup _fadeGroup;
    Coroutine _fadeRoutine;

    // cached data sources (no per-frame scene searches)
    TaskSite _windmill;
    TaskSite _obelisk;
    float _nextSiteSearch;
    bool _removedLegacyUI;
    bool _built;   // false on a duplicate that is being destroyed, or before Awake

    // ------------------------------------------------------------------ Unity

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[GameHUD] Duplicate GameHUD on '{name}' — destroying it.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;

        SetupCanvas();
        RemoveLegacyUI();

        // Build order = draw order (later siblings render on top).
        BuildVideoLayer();
        BuildScanLayer();
        BuildPromptLayer();
        BuildDialogueLayer();
        BuildGameOverLayer();
        BuildEndLayer();
        BuildFadeLayer();
        _built = true;

        // Positions the scan labels after CinemachineBrain has moved the camera; see the class docs.
        if (GetComponent<GameHUDLabelDriver>() == null) gameObject.AddComponent<GameHUDLabelDriver>();
    }

    void Start()
    {
        if (_built) TryBindDialogue();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!_built) return;

        float dt = Time.unscaledDeltaTime;
        var daffodil = FollowPlayer.Instance;
        bool scanning = daffodil != null && daffodil.IsScanning;

        // Late-arriving DialogueSystem (spawned after our Start).
        if (!_dialogueBound) TryBindDialogue();

        // Asking a sleeping Daffodil to scan → tell the player why nothing happens (once per press).
        if (daffodil != null && daffodil.IsSleeping && Input.GetKeyDown(KeyCode.V))
            Toast("Daffodil is recharging…");

        // Scan overlay fade. The layer is disabled when fully transparent so it costs nothing.
        float targetAlpha = scanning ? 1f : 0f;
        _scanGroup.alpha = fadeSeconds <= 0f
            ? targetAlpha
            : Mathf.MoveTowards(_scanGroup.alpha, targetAlpha, dt / fadeSeconds);
        bool overlayVisible = _scanGroup.alpha > 0.001f;
        if (_scanLayer.activeSelf != overlayVisible) _scanLayer.SetActive(overlayVisible);

        RefreshSiteReferences();
        UpdateBars(daffodil, dt);

        if (overlayVisible)
        {
            UpdateObjective();
            UpdateScanLine(dt);
        }

        UpdateToast(dt);
        UpdatePromptHeight(dt);
    }

    /// <summary>
    /// Places the world-space scan labels. Called by <see cref="GameHUDLabelDriver"/>, never from
    /// this component's own LateUpdate.
    ///
    /// The labels are projected through the camera, so they have to be positioned AFTER
    /// CinemachineBrain has moved it for this frame. The brain runs at execution order 0 and this
    /// component runs at -100, so doing it here would project through the PREVIOUS frame's camera
    /// transform. The rig adds handheld perlin noise on top of damping, so that one-frame lag shows
    /// up as every label swimming around the screen while the world underneath stays put.
    /// </summary>
    public void PositionWorldLabels()
    {
        if (!_built || !_scanLayer.activeSelf) return;

        var daffodil = FollowPlayer.Instance;
        UpdateWorldLabels(daffodil, daffodil != null && daffodil.IsScanning);
    }

    // ------------------------------------------------------------------ public API (methods)

    /// <summary>Explicit objective text. Empty → falls back to GameFlowManager.CurrentObjective.</summary>
    public void SetObjective(string text)
    {
        _objectiveOverride = text ?? "";
    }

    /// <summary>Bottom-center prompt, e.g. "E  Recruit Mycari". Keys and labels alternate, separated by 2+ spaces. Null/empty hides.</summary>
    public void SetPrompt(string text)
    {
        if (_built) _prompt.Set(text);
    }

    /// <summary>Smaller key-hint row under the prompt, e.g. "V  Scan    Q  Dismiss". Null/empty hides.</summary>
    public void SetHints(string text)
    {
        if (_built) _hints.Set(text);
    }

    /// <summary>Top-center message that fades out after <paramref name="seconds"/> (unscaled).</summary>
    public void Toast(string text, float seconds = 2.5f)
    {
        if (!_built || string.IsNullOrEmpty(text)) return;
        _toastText.text = text;
        _toastUntil = Time.unscaledTime + Mathf.Max(0.1f, seconds);
        if (!_toastRoot.activeSelf)
        {
            _toastGroup.alpha = 0f;
            _toastRoot.SetActive(true);
        }
    }

    /// <summary>Shows the video layer with <paramref name="texture"/> on VideoSurface (placeholder text hidden).</summary>
    public void ShowVideo(Texture texture)
    {
        if (!_built) return;
        VideoSurface.texture = texture;
        VideoSurface.enabled = texture != null;
        if (texture != null && texture.height > 0)
            _videoAspect.aspectRatio = (float)texture.width / texture.height;
        _videoPlaceholderRoot.SetActive(false);
        _videoLayer.SetActive(true);
    }

    /// <summary>Shows the video layer as a black card with centered title/subtitle (no clip assigned).</summary>
    public void ShowVideoPlaceholder(string title, string subtitle)
    {
        if (!_built) return;
        VideoSurface.texture = null;
        VideoSurface.enabled = false;
        _videoTitle.text = title ?? "";
        _videoSubtitle.text = subtitle ?? "";
        _videoSubtitle.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));
        _videoPlaceholderRoot.SetActive(true);
        _videoLayer.SetActive(true);
    }

    public void SetVideoSkipHint(bool visible)
    {
        if (_built) _videoSkipHint.SetActive(visible);
    }

    public void HideVideo()
    {
        if (!_built) return;
        VideoSurface.texture = null;
        VideoSurface.enabled = false;
        _videoLayer.SetActive(false);
    }

    /// <summary>Dark card "LIFE SUPPORT FAILED"; waits for Space (unscaled), hides, then calls <paramref name="onConfirm"/>.</summary>
    public void ShowGameOver(string subtitle, Action onConfirm)
    {
        if (!_built) { onConfirm?.Invoke(); return; }
        _gameOverSubtitle.text = subtitle ?? "";
        ShowOverlay(_gameOverLayer, _gameOverGroup, onConfirm);
    }

    /// <summary>"THE END" card; waits for Space (unscaled), hides, then calls <paramref name="onConfirm"/>.</summary>
    public void ShowEnd(string subtitle, Action onConfirm)
    {
        if (!_built) { onConfirm?.Invoke(); return; }
        _endSubtitle.text = subtitle ?? "";
        ShowOverlay(_endLayer, _endGroup, onConfirm);
    }

    public void FadeToBlack(float seconds, Action onDone = null)
    {
        if (!_built) { onDone?.Invoke(); return; }
        StartFade(1f, seconds, onDone);
    }

    public void FadeFromBlack(float seconds, Action onDone = null)
    {
        if (!_built) { onDone?.Invoke(); return; }
        StartFade(0f, seconds, onDone);
    }

    // ------------------------------------------------------------------ setup

    void SetupCanvas()
    {
        gameObject.layer = UILayer;

        _canvas = GetComponent<Canvas>();
        if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;   // above any leftover scene canvas
        _canvas.pixelPerfect = false;
        _canvasRect = _canvas.GetComponent<RectTransform>();

        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;

        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
    }

    /// <summary>Destroys the old hand-made UI canvas (the one holding the legacy energy/health bars).</summary>
    void RemoveLegacyUI()
    {
        var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            var c = canvases[i];
            if (c == null || c == _canvas) continue;
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            if (!HasDescendantNamed(c.transform, "DaffodilEnergyBar") && !HasDescendantNamed(c.transform, "WindmillHealth")) continue;

            Debug.Log("[GameHUD] Removed legacy UI canvas", c.gameObject);
            Destroy(c.gameObject);
            _removedLegacyUI = true;
        }
    }

    void TryBindDialogue()
    {
        var ds = DialogueSystem.Instance;
        if (ds == null) return;
        // Bind when the dialogue has no panel, or its panel lived on the canvas we just removed.
        if (!ds.HasUI || _removedLegacyUI)
            ds.BindUI(_dialoguePanel, _dialogueBody, _dialogueSpeaker, _dialogueHint);
        _dialogueBound = true;
    }

    // ------------------------------------------------------------------ layer builders

    void BuildVideoLayer()
    {
        _videoLayer = MakeRect("VideoLayer", transform).gameObject;
        Stretch(_videoLayer.transform as RectTransform);

        MakeImage("Black", _videoLayer.transform, WhiteSprite, Color.black, Image.Type.Simple, stretch: true);

        var surface = MakeRect("VideoSurface", _videoLayer.transform);
        Stretch(surface);
        VideoSurface = surface.gameObject.AddComponent<RawImage>();
        VideoSurface.color = Color.white;
        VideoSurface.raycastTarget = false;
        VideoSurface.enabled = false;
        _videoAspect = surface.gameObject.AddComponent<AspectRatioFitter>();
        _videoAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        _videoAspect.aspectRatio = 16f / 9f;

        _videoPlaceholderRoot = MakeRect("Placeholder", _videoLayer.transform).gameObject;
        Stretch(_videoPlaceholderRoot.transform as RectTransform);
        _videoTitle = MakeLabel("Title", _videoPlaceholderRoot.transform, "", 34f, textColor, TextAlignmentOptions.Center, uppercase: true, spacing: 8f, weight: FontWeight.Bold);
        Anchor(_videoTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 18f), new Vector2(1400f, 48f));
        _videoSubtitle = MakeLabel("Subtitle", _videoPlaceholderRoot.transform, "", 14f, dimText, TextAlignmentOptions.Center);
        Anchor(_videoSubtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(1400f, 24f));

        var skip = MakeKeyRow("SkipHint", _videoLayer.transform, this, panel: false, keySize: 11f, labelSize: 12f);
        Anchor(skip.root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 28f), Vector2.zero);
        skip.Set("SPACE / ESC  SKIP");
        _videoSkipHint = skip.root.gameObject;

        _videoLayer.SetActive(false);
    }

    void BuildScanLayer()
    {
        _scanLayer = MakeRect("ScanLayer", transform).gameObject;
        var layerRt = _scanLayer.transform as RectTransform;
        Stretch(layerRt);
        _scanGroup = _scanLayer.AddComponent<CanvasGroup>();
        _scanGroup.alpha = 0f;
        _scanGroup.interactable = false;
        _scanGroup.blocksRaycasts = false;

        // Subtle full-screen tint + one thin sweeping line = "scanner on" feel for almost nothing.
        MakeImage("Tint", _scanLayer.transform, WhiteSprite, WithAlpha(accent, 0.04f), Image.Type.Simple, stretch: true);
        var line = MakeImage("ScanLine", _scanLayer.transform, WhiteSprite, WithAlpha(accent, 0.35f));
        _scanLine = line.rectTransform;
        _scanLine.anchorMin = new Vector2(0f, 1f);
        _scanLine.anchorMax = new Vector2(1f, 1f);
        _scanLine.pivot = new Vector2(0.5f, 1f);
        _scanLine.sizeDelta = new Vector2(0f, 1f);
        _scanLine.anchoredPosition = Vector2.zero;

        // World labels sit under the panels so a label never covers the readouts.
        _labelRoot = MakeRect("WorldLabels", _scanLayer.transform);
        Stretch(_labelRoot);

        // Top-left readout panel, tucked under the always-on energy bar.
        var panel = MakePanel("ScanPanel", _scanLayer.transform, panelColor, PanelRadius);
        Anchor(panel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Margin, -(Margin + EnergyPanelHeight + 8f)), new Vector2(ScanPanelWidth, 100f));
        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)PanelPadding, (int)PanelPadding, (int)PanelPadding, (int)PanelPadding);
        vlg.spacing = 12f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _lifeBar = MakeBar("LifeSupport", panel.transform, this, "LIFE SUPPORT", BarHeight, colourByValue: true);
        _windmillBar = MakeBar("Windmill", panel.transform, this, "WINDMILL", BarHeight, colourByValue: true);
        _windmillShieldBar = MakeBar("WindmillShield", panel.transform, this, "SHIELD", ThinBarHeight, colourByValue: false, thin: true);
        _obeliskBar = MakeBar("Obelisk", panel.transform, this, "OBELISK", BarHeight, colourByValue: true);
        _obeliskShieldBar = MakeBar("ObeliskShield", panel.transform, this, "SHIELD", ThinBarHeight, colourByValue: false, thin: true);
        _windmillShieldBar.SetVisible(false);
        _obeliskBar.SetVisible(false);
        _obeliskShieldBar.SetVisible(false);

        // Top-center objective: small pill with an accent dot before the text.
        var objective = MakePanel("Objective", _scanLayer.transform, panelColor, PanelRadius);
        Anchor(objective.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -Margin), Vector2.zero);
        var hlg = objective.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 18, 9, 9);
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        var objFit = objective.gameObject.AddComponent<ContentSizeFitter>();
        objFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        objFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var dot = MakeImage("Dot", objective.transform, CircleSprite, accent);
        var dotLe = dot.gameObject.AddComponent<LayoutElement>();
        dotLe.preferredWidth = 6f;
        dotLe.preferredHeight = 6f;
        _objectiveText = MakeLabel("Text", objective.transform, "", 16f, textColor, TextAlignmentOptions.MidlineLeft, weight: FontWeight.Medium);
        _objectiveRoot = objective.gameObject;
        _objectiveRoot.SetActive(false);

        _scanLayer.SetActive(false);
    }

    void BuildPromptLayer()
    {
        var layer = MakeRect("PromptLayer", transform);
        Stretch(layer);

        // Daffodil's energy is the one number the player has to ration, and hiding it behind the
        // scan meant you only saw it once you were already spending it. It lives outside the scan
        // overlay so it is readable at all times; the scan panel sits underneath it.
        var energyPanel = MakePanel("EnergyPanel", layer, panelColor, PanelRadius);
        Anchor(energyPanel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
               new Vector2(Margin, -Margin), new Vector2(ScanPanelWidth, EnergyPanelHeight));
        var evlg = energyPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        evlg.padding = new RectOffset((int)PanelPadding, (int)PanelPadding, 10, 10);
        evlg.childControlWidth = true;  evlg.childControlHeight = true;
        evlg.childForceExpandWidth = true; evlg.childForceExpandHeight = false;
        _daffodilBar = MakeBar("Daffodil", energyPanel.transform, this, "DAFFODIL", BarHeight, colourByValue: false);

        // Prompt + hints sit low, near the bottom edge. They only climb clear of the dialogue
        // panel while dialogue is actually on screen, which is the only time they would collide.
        _hints = MakeKeyRow("Hints", layer, this, panel: false, keySize: 10f, labelSize: 12f);
        Anchor(_hints.root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, PromptLowY), Vector2.zero);
        _prompt = MakeKeyRow("Prompt", layer, this, panel: true, keySize: 13f, labelSize: 15f);
        Anchor(_prompt.root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, PromptLowY + 30f), Vector2.zero);
        _hints.Set(null);
        _prompt.Set(null);

        // Toast under the objective slot.
        var toast = MakePanel("Toast", layer, panelColor, PanelRadius);
        Anchor(toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(Margin + 56f)), Vector2.zero);
        var hlg = toast.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(18, 18, 9, 9);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        var fit = toast.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _toastText = MakeLabel("Text", toast.transform, "", 14f, textColor, TextAlignmentOptions.Center, weight: FontWeight.Medium);
        _toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
        _toastGroup.alpha = 0f;
        _toastGroup.blocksRaycasts = false;
        _toastRoot = toast.gameObject;
        _toastRoot.SetActive(false);
    }

    void BuildDialogueLayer()
    {
        var layer = MakeRect("DialogueLayer", transform);
        Stretch(layer);

        // 60% width bottom panel: anchors carry the width so it holds at 16:9 and 16:10.
        var panel = MakePanel("DialoguePanel", layer, panelColor, PanelRadius);
        var rt = panel.rectTransform;
        rt.anchorMin = new Vector2(0.2f, 0f);
        rt.anchorMax = new Vector2(0.8f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, DialogueBottom);
        rt.sizeDelta = new Vector2(0f, DialogueHeight);

        _dialogueSpeaker = MakeLabel("Speaker", panel.transform, "", 12f, accent, TextAlignmentOptions.MidlineLeft, uppercase: true, spacing: 4f, weight: FontWeight.Bold);
        var srt = _dialogueSpeaker.rectTransform;
        srt.anchorMin = new Vector2(0f, 1f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(0.5f, 1f);
        srt.offsetMin = new Vector2(22f, -34f);
        srt.offsetMax = new Vector2(-22f, -16f);

        _dialogueBody = MakeLabel("Body", panel.transform, "", 20f, textColor, TextAlignmentOptions.TopLeft, wrap: true);
        var brt = _dialogueBody.rectTransform;
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = new Vector2(22f, 34f);
        brt.offsetMax = new Vector2(-22f, -42f);

        var hint = MakeKeyRow("ContinueHint", panel.transform, this, panel: false, keySize: 10f, labelSize: 11f);
        Anchor(hint.root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 12f), Vector2.zero);
        hint.Set("SPACE  Continue");
        _dialogueHint = hint.root.gameObject;
        _dialogueHint.SetActive(false);

        _dialoguePanel = panel.gameObject;
        _dialoguePanel.SetActive(false);
    }

    void BuildGameOverLayer()
    {
        _gameOverLayer = BuildCardLayer("GameOverLayer", "LIFE SUPPORT FAILED", bad, "SPACE  Return to last checkpoint",
            out _gameOverGroup, out _gameOverSubtitle);
    }

    void BuildEndLayer()
    {
        _endLayer = BuildCardLayer("EndLayer", "THE END", accent, "SPACE  Play again",
            out _endGroup, out _endSubtitle);
    }

    /// <summary>Full-screen dark card with a big title, a subtitle line and a key hint. Starts hidden.</summary>
    GameObject BuildCardLayer(string name, string title, Color titleColor, string keyHint, out CanvasGroup group, out TextMeshProUGUI subtitle)
    {
        var layer = MakeRect(name, transform);
        Stretch(layer);
        group = layer.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        MakeImage("Overlay", layer, WhiteSprite, new Color(0.02f, 0.02f, 0.03f, 0.88f), Image.Type.Simple, stretch: true);

        var titleText = MakeLabel("Title", layer, title, 44f, titleColor, TextAlignmentOptions.Center, uppercase: true, spacing: 8f, weight: FontWeight.Bold);
        Anchor(titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1600f, 60f));

        subtitle = MakeLabel("Subtitle", layer, "", 16f, dimText, TextAlignmentOptions.Center);
        Anchor(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(1200f, 26f));

        var hint = MakeKeyRow("KeyHint", layer, this, panel: true, keySize: 12f, labelSize: 14f);
        Anchor(hint.root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), Vector2.zero);
        hint.Set(keyHint);

        layer.gameObject.SetActive(false);
        return layer.gameObject;
    }

    void BuildFadeLayer()
    {
        var layer = MakeRect("FadeLayer", transform);
        Stretch(layer);
        MakeImage("Black", layer, WhiteSprite, Color.black, Image.Type.Simple, stretch: true);
        _fadeGroup = layer.gameObject.AddComponent<CanvasGroup>();
        _fadeGroup.alpha = 0f;
        _fadeGroup.blocksRaycasts = false;
        _fadeGroup.interactable = false;
    }

    // ------------------------------------------------------------------ per-frame updates

    /// <summary>Re-finds the windmill / obelisk TaskSites at most once a second while missing (no scene scans).</summary>
    void RefreshSiteReferences()
    {
        bool needWindmill = _windmill == null;
        bool needObelisk = ObeliskVisible && _obelisk == null;
        if (!needWindmill && !needObelisk) return;
        if (Time.unscaledTime < _nextSiteSearch) return;
        _nextSiteSearch = Time.unscaledTime + 1f;

        var tm = TaskManager.Instance;
        if (tm == null) return;
        if (needWindmill) _windmill = tm.FindByType(TaskType.PowerPlant);
        if (needObelisk) _obelisk = tm.FindByType(TaskType.Obelisk);
    }

    void UpdateBars(FollowPlayer daffodil, float dt)
    {
        // Daffodil energy — always shown, accent coloured.
        float energy01 = 0f;
        if (daffodil != null && daffodil.MaxEnergy > 0f)
            energy01 = Mathf.Clamp01(daffodil.Energy / daffodil.MaxEnergy);
        _daffodilBar.SetVisible(true);
        _daffodilBar.SetTarget(energy01);
        _daffodilBar.SetLabel(daffodil != null && daffodil.IsSleeping ? "DAFFODIL · RECHARGING" : "DAFFODIL");

        // Life support — only when the system exists in this scene.
        var life = LifeSupport.Instance;
        _lifeBar.SetVisible(life != null);
        if (life != null) _lifeBar.SetTarget(Mathf.Clamp01(life.Value01));

        // Windmill (+ shield when it can have one).
        bool hasWindmill = _windmill != null;
        _windmillBar.SetVisible(hasWindmill);
        _windmillShieldBar.SetVisible(hasWindmill && _windmill.maxShield > 0f);
        if (hasWindmill)
        {
            _windmillBar.SetLabel(SiteLabel(_windmill, "WINDMILL"));
            _windmillBar.SetTarget(Mathf.Clamp01(_windmill.Health01));
            if (_windmill.maxShield > 0f) _windmillShieldBar.SetTarget(Mathf.Clamp01(_windmill.Shield01));
        }

        // Obelisk — only once the flow reveals it.
        bool hasObelisk = ObeliskVisible && _obelisk != null;
        _obeliskBar.SetVisible(hasObelisk);
        _obeliskShieldBar.SetVisible(hasObelisk && _obelisk.maxShield > 0f);
        if (hasObelisk)
        {
            _obeliskBar.SetLabel(SiteLabel(_obelisk, "OBELISK"));
            _obeliskBar.SetTarget(Mathf.Clamp01(_obelisk.Health01));
            if (_obelisk.maxShield > 0f) _obeliskShieldBar.SetTarget(Mathf.Clamp01(_obelisk.Shield01));
        }

        _daffodilBar.Tick(this, dt);
        _lifeBar.Tick(this, dt);
        _windmillBar.Tick(this, dt);
        _windmillShieldBar.Tick(this, dt);
        _obeliskBar.Tick(this, dt);
        _obeliskShieldBar.Tick(this, dt);
    }

    static string SiteLabel(TaskSite site, string fallback)
    {
        return string.IsNullOrWhiteSpace(site.displayName) ? fallback : site.displayName.ToUpperInvariant();
    }

    void UpdateObjective()
    {
        string text = _objectiveOverride;
        if (string.IsNullOrEmpty(text))
        {
            var flow = GameFlowManager.Instance;
            if (flow != null) text = flow.CurrentObjective ?? "";
        }

        if (text == _objectiveShown) return;
        _objectiveShown = text;
        _objectiveText.text = text;
        _objectiveRoot.SetActive(!string.IsNullOrEmpty(text));
    }

    void UpdateWorldLabels(FollowPlayer daffodil, bool scanning)
    {
        var cam = Camera.main;
        IReadOnlyList<ScanHit> hits = (scanning && daffodil != null) ? daffodil.ScannedTargets : null;
        int count = (hits != null && cam != null) ? Mathf.Min(hits.Count, MaxWorldLabels) : 0;

        while (_labels.Count < count) _labels.Add(MakeWorldLabel(_labelRoot, this));

        for (int i = 0; i < _labels.Count; i++)
        {
            var label = _labels[i];
            if (i >= count) { label.SetActive(false); continue; }

            var hit = hits[i];
            if (hit.target == null) { label.SetActive(false); continue; }

            Vector3 targetPosition = hit.target.position;
            var mycari = hit.target.GetComponent<NPCFlocker>();
            if (mycari != null)
                targetPosition = mycari.ScanLabelPosition;

            Vector3 world = targetPosition + Vector3.up * labelHeight;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f) { label.SetActive(false); continue; }   // behind the camera

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out Vector2 local);
            label.root.anchoredPosition = local;
            label.Apply(hit, this);
            label.SetActive(true);
        }
    }

    void UpdateScanLine(float dt)
    {
        float sweep = Mathf.Max(0.1f, scanSweepSeconds);
        _scanSweepT = (_scanSweepT + dt / sweep) % 1f;
        _scanLine.anchoredPosition = new Vector2(0f, -_scanSweepT * _canvasRect.rect.height);
    }

    /// <summary>
    /// Keeps the prompt low unless the dialogue panel is up, in which case it rides above it.
    /// Eased rather than snapped so the jump is not distracting mid-conversation.
    /// </summary>
    void UpdatePromptHeight(float dt)
    {
        if (_prompt == null || _hints == null) return;
        bool dialogueUp = _dialoguePanel != null && _dialoguePanel.activeInHierarchy;
        float target = dialogueUp ? DialogueBottom + DialogueHeight + 26f : PromptLowY;
        _promptY = Mathf.Lerp(_promptY <= 0f ? target : _promptY, target, 1f - Mathf.Exp(-10f * dt));
        var h = _hints.root.anchoredPosition;  h.y = _promptY;        _hints.root.anchoredPosition = h;
        var pr = _prompt.root.anchoredPosition; pr.y = _promptY + 30f; _prompt.root.anchoredPosition = pr;
    }

    void UpdateToast(float dt)
    {
        if (!_toastRoot.activeSelf) return;
        float step = fadeSeconds <= 0f ? 1f : dt / fadeSeconds;
        if (Time.unscaledTime < _toastUntil)
        {
            _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, 1f, step);
        }
        else
        {
            _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, 0f, step);
            if (_toastGroup.alpha <= 0f) _toastRoot.SetActive(false);
        }
    }

    // ------------------------------------------------------------------ overlays & fades

    void ShowOverlay(GameObject layer, CanvasGroup group, Action onConfirm)
    {
        if (_overlayRoutine != null) StopCoroutine(_overlayRoutine);
        // Only one card at a time.
        if (layer != _gameOverLayer) _gameOverLayer.SetActive(false);
        if (layer != _endLayer) _endLayer.SetActive(false);
        _overlayRoutine = StartCoroutine(OverlayRoutine(layer, group, onConfirm));
    }

    IEnumerator OverlayRoutine(GameObject layer, CanvasGroup group, Action onConfirm)
    {
        const float cardFade = 0.45f;
        group.alpha = 0f;
        group.blocksRaycasts = true;
        layer.SetActive(true);

        // Skip the frame the card was shown so the key press that caused it can't confirm it.
        yield return null;

        bool confirmed = false;
        while (!confirmed)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 1f, Time.unscaledDeltaTime / cardFade);
            if (Input.GetKeyDown(KeyCode.Space)) confirmed = true;
            yield return null;
        }

        while (group.alpha > 0f)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, Time.unscaledDeltaTime / cardFade);
            yield return null;
        }

        group.blocksRaycasts = false;
        layer.SetActive(false);
        _overlayRoutine = null;
        onConfirm?.Invoke();
    }

    void StartFade(float target, float seconds, Action onDone)
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeRoutine(target, seconds, onDone));
    }

    IEnumerator FadeRoutine(float target, float seconds, Action onDone)
    {
        float start = _fadeGroup.alpha;
        _fadeGroup.blocksRaycasts = target > 0f;
        if (seconds > 0f)
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / seconds;
                _fadeGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t));
                yield return null;
            }
        }
        _fadeGroup.alpha = target;
        _fadeRoutine = null;
        onDone?.Invoke();
    }

    // ------------------------------------------------------------------ colours

    /// <summary>Fill colour for a 0–1 value: good / warn / bad.</summary>
    public Color ColorForValue(float v01)
    {
        if (v01 >= 0.5f) return good;
        if (v01 >= 0.25f) return warn;
        return bad;
    }

    static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    /// <summary>Dark text used on accent keycaps.</summary>
    Color KeycapTextColor => new Color(0.06f, 0.07f, 0.09f, 1f);

    // ------------------------------------------------------------------ procedural sprites (static, cached)

    static Sprite _white;
    static Sprite _circle;
    static readonly Dictionary<int, Sprite> _rounded = new Dictionary<int, Sprite>();

    /// <summary>1x1 white sprite for flat fills.</summary>
    public static Sprite WhiteSprite
    {
        get
        {
            if (_white == null)
            {
                var tex = NewTexture(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply(false, true);
                _white = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
                _white.name = "HUD_White";
                _white.hideFlags = HideFlags.HideAndDontSave;
            }
            return _white;
        }
    }

    /// <summary>64x64 anti-aliased rounded rectangle with a 9-slice border of <paramref name="radiusPx"/>. Use with Image.Type.Sliced.</summary>
    public static Sprite RoundedSprite(int radiusPx)
    {
        const int size = 64;
        radiusPx = Mathf.Clamp(radiusPx, 1, size / 2 - 1);
        if (_rounded.TryGetValue(radiusPx, out var cached) && cached != null) return cached;

        var tex = NewTexture(size, size);
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        float inner = half - radiusPx;   // half-extent of the rectangle the corners are rounded from
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Signed distance to the rounded box, sampled at the pixel centre.
                float px = Mathf.Abs(x + 0.5f - half) - inner;
                float py = Mathf.Abs(y + 0.5f - half) - inner;
                float outside = Mathf.Sqrt(Mathf.Max(px, 0f) * Mathf.Max(px, 0f) + Mathf.Max(py, 0f) * Mathf.Max(py, 0f));
                float insideD = Mathf.Min(Mathf.Max(px, py), 0f);
                float d = outside + insideD - radiusPx;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        var border = new Vector4(radiusPx, radiusPx, radiusPx, radiusPx);
        var sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        sprite.name = "HUD_Rounded" + radiusPx;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        _rounded[radiusPx] = sprite;
        return sprite;
    }

    /// <summary>32x32 soft-edged disc for dots.</summary>
    public static Sprite CircleSprite
    {
        get
        {
            if (_circle == null)
            {
                const int size = 32;
                var tex = NewTexture(size, size);
                var pixels = new Color32[size * size];
                float half = size * 0.5f;
                float radius = half - 1.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - half;
                        float dy = y + 0.5f - half;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                        byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.75f - d / 1.5f) * 255f);
                        pixels[y * size + x] = new Color32(255, 255, 255, a);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _circle = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
                _circle.name = "HUD_Circle";
                _circle.hideFlags = HideFlags.HideAndDontSave;
            }
            return _circle;
        }
    }

    static Texture2D NewTexture(int w, int h)
    {
        return new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
            name = "HUD_Tex"
        };
    }

    // ------------------------------------------------------------------ widget builders (static)

    static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = UILayer;
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    /// <summary>Anchors the rect to fill its parent.</summary>
    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Anchors a rect at a single point with an explicit pivot, offset and size.</summary>
    static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    static Image MakeImage(string name, Transform parent, Sprite sprite, Color color, Image.Type type = Image.Type.Simple, bool stretch = false)
    {
        var rt = MakeRect(name, parent);
        if (stretch) Stretch(rt);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.type = type;
        img.raycastTarget = false;
        img.maskable = false;
        return img;
    }

    /// <summary>Rounded translucent panel (sliced sprite so the corners never stretch).</summary>
    static Image MakePanel(string name, Transform parent, Color color, float radius)
    {
        return MakeImage(name, parent, RoundedSprite(Mathf.RoundToInt(radius)), color, Image.Type.Sliced);
    }

    static TextMeshProUGUI MakeLabel(string name, Transform parent, string text, float size, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool uppercase = false, float spacing = 0f,
        FontWeight weight = FontWeight.Regular, bool wrap = false)
    {
        var rt = MakeRect(name, parent);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();   // font = TMP default settings
        tmp.text = text ?? "";
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.characterSpacing = spacing;
        tmp.fontWeight = weight;
        tmp.fontStyle = uppercase ? FontStyles.UpperCase : FontStyles.Normal;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.maskable = false;
        return tmp;
    }

    /// <summary>
    /// Bar block: "LABEL ........ 72%" row on top, pill track + animated fill below.
    /// Thin variant (shield) is a 4px bar with a smaller label row.
    /// </summary>
    static HudBar MakeBar(string name, Transform parent, GameHUD hud, string label, float barHeight, bool colourByValue, bool thin = false)
    {
        float rowHeight = thin ? 12f : 14f;
        float gap = thin ? 3f : 4f;
        float labelSize = thin ? 10f : 11f;

        var root = MakeRect(name, parent);
        var le = root.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = rowHeight + gap + barHeight;
        le.flexibleWidth = 1f;

        var labelText = MakeLabel("Label", root, label, labelSize, hud.dimText, TextAlignmentOptions.MidlineLeft, uppercase: true, spacing: 4f, weight: FontWeight.Medium);
        var lrt = labelText.rectTransform;
        lrt.anchorMin = new Vector2(0f, 1f);
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.offsetMin = new Vector2(0f, -rowHeight);
        lrt.offsetMax = new Vector2(-40f, 0f);

        var valueText = MakeLabel("Value", root, "0%", thin ? 10f : 12f, hud.textColor, TextAlignmentOptions.MidlineRight, weight: FontWeight.Medium);
        var vrt = valueText.rectTransform;
        vrt.anchorMin = new Vector2(1f, 1f);
        vrt.anchorMax = new Vector2(1f, 1f);
        vrt.pivot = new Vector2(1f, 1f);
        vrt.anchoredPosition = Vector2.zero;
        vrt.sizeDelta = new Vector2(60f, rowHeight);

        int radius = Mathf.Max(1, Mathf.RoundToInt(barHeight * 0.5f));
        var track = MakeImage("Track", root, RoundedSprite(radius), hud.barTrack, Image.Type.Sliced);
        var trt = track.rectTransform;
        trt.anchorMin = new Vector2(0f, 0f);
        trt.anchorMax = new Vector2(1f, 0f);
        trt.pivot = new Vector2(0.5f, 0f);
        trt.offsetMin = new Vector2(0f, 0f);
        trt.offsetMax = new Vector2(0f, barHeight);

        var fill = MakeImage("Fill", track.transform, RoundedSprite(radius), hud.accent, Image.Type.Sliced);
        var frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = new Vector2(0f, 1f);
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;

        return new HudBar
        {
            root = root.gameObject,
            label = labelText,
            value = valueText,
            fill = fill,
            colourByValue = colourByValue,
            fixedColor = thin ? WithAlpha(hud.textColor, 0.85f) : hud.accent
        };
    }

    /// <summary>Small rounded accent box with dark key text, e.g. [E].</summary>
    static GameObject MakeKeycap(Transform parent, GameHUD hud, float fontSize, out TextMeshProUGUI keyText)
    {
        var cap = MakeImage("Keycap", parent, RoundedSprite(6), hud.accent, Image.Type.Sliced);
        var hlg = cap.gameObject.AddComponent<HorizontalLayoutGroup>();
        int padX = Mathf.RoundToInt(fontSize * 0.55f);
        int padY = Mathf.RoundToInt(fontSize * 0.25f);
        hlg.padding = new RectOffset(padX, padX, padY, padY);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        var le = cap.gameObject.AddComponent<LayoutElement>();
        le.minWidth = fontSize * 2f;   // single letters still get a square-ish cap
        keyText = MakeLabel("Key", cap.transform, "", fontSize, hud.KeycapTextColor, TextAlignmentOptions.Center, uppercase: true, spacing: 1f, weight: FontWeight.Bold);
        return cap.gameObject;
    }

    /// <summary>
    /// Horizontal row of [KEY] label pairs, sized to content. Optionally on a pill panel.
    /// Text format: tokens separated by two or more spaces alternate key / label ("E  Recruit    Q  Dismiss").
    /// </summary>
    static KeyRow MakeKeyRow(string name, Transform parent, GameHUD hud, bool panel, float keySize, float labelSize)
    {
        RectTransform root;
        if (panel)
        {
            var img = MakePanel(name, parent, hud.panelColor, PanelRadius);
            root = img.rectTransform;
        }
        else
        {
            root = MakeRect(name, parent);
        }

        var hlg = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        int padX = panel ? 12 : 0;
        int padY = panel ? 8 : 0;
        hlg.padding = new RectOffset(padX, padX + (panel ? 4 : 0), padY, padY);
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return new KeyRow(root, hud, keySize, labelSize, panel ? hud.textColor : hud.dimText);
    }

    /// <summary>World label: rounded chip with a coloured dot, name, dim detail and an optional 3px bar underneath.</summary>
    static WorldLabel MakeWorldLabel(Transform parent, GameHUD hud)
    {
        var chip = MakePanel("WorldLabel", parent, hud.panelColor, 10f);
        var root = chip.rectTransform;
        Anchor(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        var vlg = chip.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 12, 6, 7);
        vlg.spacing = 5f;
        vlg.childAlignment = TextAnchor.MiddleLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var fit = chip.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var row = MakeRect("Row", root);
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 7f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var dot = MakeImage("Dot", row, CircleSprite, hud.accent);
        var dotLe = dot.gameObject.AddComponent<LayoutElement>();
        dotLe.preferredWidth = 8f;
        dotLe.preferredHeight = 8f;

        var nameText = MakeLabel("Name", row, "", 12f, hud.textColor, TextAlignmentOptions.MidlineLeft, uppercase: true, spacing: 3f, weight: FontWeight.Bold);
        var detailText = MakeLabel("Detail", row, "", 12f, hud.dimText, TextAlignmentOptions.MidlineLeft, weight: FontWeight.Medium);

        var bar = MakeRect("Bar", root);
        var barLe = bar.gameObject.AddComponent<LayoutElement>();
        barLe.preferredHeight = 3f;
        barLe.minHeight = 3f;
        barLe.flexibleWidth = 1f;
        var track = MakeImage("Track", bar, RoundedSprite(2), hud.barTrack, Image.Type.Sliced, stretch: true);
        var fill = MakeImage("Fill", track.transform, RoundedSprite(2), hud.accent, Image.Type.Sliced);
        var frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = new Vector2(0f, 1f);
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;

        chip.gameObject.SetActive(false);
        return new WorldLabel
        {
            root = root,
            dot = dot,
            name = nameText,
            detail = detailText,
            bar = bar.gameObject,
            barFill = fill
        };
    }

    static bool HasDescendantNamed(Transform root, string childName)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name == childName) return true;
            if (HasDescendantNamed(c, childName)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ widget classes

    /// <summary>One readout bar: label, right-aligned percentage and a pill fill that eases toward its target.</summary>
    class HudBar
    {
        public GameObject root;
        public TextMeshProUGUI label;
        public TextMeshProUGUI value;
        public Image fill;
        public bool colourByValue;
        public Color fixedColor;

        float _target;
        float _shown;
        string _labelShown;
        int _percentShown = -1;
        bool _visible = true;

        public void SetTarget(float v01) => _target = Mathf.Clamp01(v01);

        public void SetLabel(string text)
        {
            if (text == _labelShown) return;
            _labelShown = text;
            label.text = text;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (root.activeSelf != visible) root.SetActive(visible);
        }

        public void Tick(GameHUD hud, float dt)
        {
            if (!_visible) return;

            // Exponential ease (~rate/s) — frame-rate independent, settles crisply.
            _shown = Mathf.Lerp(_shown, _target, 1f - Mathf.Exp(-hud.barLerpRate * dt));
            if (Mathf.Abs(_shown - _target) < 0.0005f) _shown = _target;

            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(_shown, 1f);
            fill.enabled = _shown > 0.002f;
            fill.color = colourByValue ? hud.ColorForValue(_shown) : fixedColor;

            int percent = Mathf.RoundToInt(_shown * 100f);
            if (percent != _percentShown)
            {
                _percentShown = percent;
                value.text = percent + "%";
            }
        }
    }

    /// <summary>Pooled [KEY] label pairs in a horizontal row.</summary>
    class KeyRow
    {
        public readonly RectTransform root;

        readonly GameHUD _hud;
        readonly float _keySize;
        readonly float _labelSize;
        readonly Color _labelColor;
        readonly List<Segment> _segments = new List<Segment>();
        readonly List<string> _tokens = new List<string>();
        string _shown;

        struct Segment
        {
            public GameObject cap;
            public TextMeshProUGUI key;
            public TextMeshProUGUI label;
        }

        public KeyRow(RectTransform root, GameHUD hud, float keySize, float labelSize, Color labelColor)
        {
            this.root = root;
            _hud = hud;
            _keySize = keySize;
            _labelSize = labelSize;
            _labelColor = labelColor;
        }

        public void Set(string text)
        {
            string normalized = string.IsNullOrWhiteSpace(text) ? "" : text;
            if (normalized == _shown) return;
            _shown = normalized;

            if (normalized.Length == 0)
            {
                root.gameObject.SetActive(false);
                return;
            }

            Tokenize(text, _tokens);
            int pairs = (_tokens.Count + 1) / 2;
            bool keyless = _tokens.Count == 1;   // plain sentence, no keycap

            while (_segments.Count < pairs) _segments.Add(MakeSegment());

            for (int i = 0; i < _segments.Count; i++)
            {
                var seg = _segments[i];
                bool used = i < pairs;
                if (!used)
                {
                    seg.cap.SetActive(false);
                    seg.label.gameObject.SetActive(false);
                    continue;
                }

                string key = keyless ? null : _tokens[i * 2];
                string label = keyless ? _tokens[0] : (i * 2 + 1 < _tokens.Count ? _tokens[i * 2 + 1] : null);

                seg.cap.SetActive(!string.IsNullOrEmpty(key));
                if (!string.IsNullOrEmpty(key)) seg.key.text = key;

                seg.label.gameObject.SetActive(!string.IsNullOrEmpty(label));
                if (!string.IsNullOrEmpty(label)) seg.label.text = label;

                // Extra breathing room between pairs, none after the last one.
                seg.label.margin = new Vector4(0f, 0f, i < pairs - 1 ? 14f : 0f, 0f);
            }

            root.gameObject.SetActive(true);
        }

        Segment MakeSegment()
        {
            var cap = MakeKeycap(root, _hud, _keySize, out var keyText);
            var label = MakeLabel("Label", root, "", _labelSize, _labelColor, TextAlignmentOptions.MidlineLeft, weight: FontWeight.Medium);
            return new Segment { cap = cap, key = keyText, label = label };
        }

        /// <summary>Splits on runs of two or more spaces; single spaces stay inside a token.</summary>
        static void Tokenize(string text, List<string> into)
        {
            into.Clear();
            int start = 0;
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == ' ' && i + 1 < text.Length && text[i + 1] == ' ')
                {
                    string tok = text.Substring(start, i - start).Trim();
                    if (tok.Length > 0) into.Add(tok);
                    while (i < text.Length && text[i] == ' ') i++;
                    start = i;
                }
                else i++;
            }
            string last = text.Substring(start).Trim();
            if (last.Length > 0) into.Add(last);
        }
    }

    /// <summary>Pooled world-space label chip for one ScanHit.</summary>
    class WorldLabel
    {
        public RectTransform root;
        public Image dot;
        public TextMeshProUGUI name;
        public TextMeshProUGUI detail;
        public GameObject bar;
        public Image barFill;

        string _name;
        string _detail;

        public void SetActive(bool active)
        {
            if (root.gameObject.activeSelf != active) root.gameObject.SetActive(active);
        }

        public void Apply(ScanHit hit, GameHUD hud)
        {
            string n = hit.label ?? "";
            string d = hit.detail ?? "";
            if (n != _name) { _name = n; name.text = n; }
            if (d != _detail)
            {
                _detail = d;
                detail.text = d;
                detail.gameObject.SetActive(d.Length > 0);
            }

            dot.color = hit.color;

            bool showBar = hit.value01 >= 0f;
            if (bar.activeSelf != showBar) bar.SetActive(showBar);
            if (showBar)
            {
                float v = Mathf.Clamp01(hit.value01);
                barFill.rectTransform.anchorMax = new Vector2(v, 1f);
                barFill.enabled = v > 0.002f;
                barFill.color = hit.color;
            }
        }
    }
}
