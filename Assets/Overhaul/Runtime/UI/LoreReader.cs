using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Reads a log on screen: the world pauses, the text types itself out (any key finishes it), long logs scroll
    /// (mouse wheel or W/S / arrows), E or Esc closes. Raises LoreOpened / LoreClosed for music and stats.
    /// </summary>
    [OverhaulModule(71)]
    public class LoreReader : MonoBehaviour
    {
        public static LoreReader Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._open;

        public float charsPerSecond = 900f;

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _panel;
        TextMeshProUGUI _title, _author, _body, _hint;
        ScrollRect _scroll;
        bool _open;
        string _id;
        float _openedAt, _shownChars, _savedTimeScale = 1f, _fade;
        int _totalChars;
        PlayerController _disabledPlayer;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Opens a log by id (see <see cref="LoreLibrary"/>).</summary>
        public static void Open(string id)
        {
            if (Instance != null) Instance.Show(id);
        }

        void Build()
        {
            _canvas = OverhaulUI.CreateCanvas("Lore Reader", 250, transform);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            var dim = OverhaulUI.Image("Dim", _canvas.transform, GameHUD.WhiteSprite, new Color(0.02f, 0.02f, 0.03f, 0.72f));
            OverhaulUI.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            _panel = OverhaulUI.Rect("Panel", _canvas.transform);
            OverhaulUI.Place(_panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 720f));
            var plate = OverhaulUI.PanelImage("Plate", _panel, new Color(0.04f, 0.05f, 0.07f, 0.94f), 18);
            OverhaulUI.Stretch(plate.rectTransform);
            var edge = OverhaulUI.Image("Edge", _panel, GameHUD.WhiteSprite, OverhaulUI.Accent);
            OverhaulUI.Place(edge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -28f), new Vector2(64f, 3f));

            _title = OverhaulUI.Label("Title", _panel, "", 34f, OverhaulUI.Text, TextAlignmentOptions.TopLeft, FontWeight.SemiBold, true);
            OverhaulUI.Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(920f, 46f));
            _author = OverhaulUI.Label("Author", _panel, "", 17f, OverhaulUI.Dim, TextAlignmentOptions.TopLeft, FontWeight.Regular, true, 1f);
            OverhaulUI.Place(_author.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -90f), new Vector2(920f, 26f));

            var viewport = OverhaulUI.Rect("Viewport", _panel);
            OverhaulUI.Place(viewport, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(920f, 520f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.001f);   // catches the mouse wheel
            var content = OverhaulUI.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 520f);
            _body = OverhaulUI.Label("Body", content, "", 21f, OverhaulUI.Text, TextAlignmentOptions.TopLeft, FontWeight.Regular, true);
            _body.lineSpacing = 8f;
            _body.paragraphSpacing = 14f;
            var brt = _body.rectTransform;
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(-12f, 520f);
            var fitter = content.gameObject.AddComponent<VerticalLayoutGroup>();
            fitter.childControlHeight = true;
            fitter.childControlWidth = true;
            fitter.childForceExpandHeight = false;
            fitter.padding = new RectOffset(0, 12, 0, 12);
            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;

            string accent = OverhaulUI.Hex(OverhaulUI.Accent);
            _hint = OverhaulUI.Label("Hint", _panel, $"<color=#{accent}><b>E</b></color> / <color=#{accent}><b>Esc</b></color>  Close     <color=#{accent}><b>W</b></color> / <color=#{accent}><b>S</b></color>  Scroll",
                                     15f, OverhaulUI.Dim, TextAlignmentOptions.Center);
            OverhaulUI.Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(900f, 24f));
            _canvas.gameObject.SetActive(false);
        }

        void Show(string id)
        {
            var e = LoreLibrary.Get(id);
            if (e == null || _open) return;
            if (_canvas == null) Build();
            _id = id;
            _title.text = e.title;
            _author.text = e.author;
            _body.text = e.body;
            _body.ForceMeshUpdate();
            _totalChars = _body.textInfo.characterCount;
            _shownChars = 0f;
            _body.maxVisibleCharacters = 0;
            _scroll.verticalNormalizedPosition = 1f;
            _canvas.gameObject.SetActive(true);
            _fade = 0f;
            _open = true;
            _openedAt = Time.unscaledTime;

            // never restore a hit-stop's slow motion (or a pause) when the reader closes
            _savedTimeScale = Time.timeScale >= 0.5f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            var bridge = SignalBridge.Instance;
            var player = bridge != null ? bridge.Player : null;
            if (player != null && player.enabled) { player.enabled = false; _disabledPlayer = player; }
            GameSignals.RaiseLoreOpened(id);
        }

        void Close()
        {
            if (!_open) return;
            _open = false;
            Time.timeScale = _savedTimeScale;
            LoreTerminal.Block(0.5f);
            if (_disabledPlayer != null) StartCoroutine(ReenablePlayer(_disabledPlayer));
            _disabledPlayer = null;
            GameSignals.RaiseLoreClosed(_id);
        }

        System.Collections.IEnumerator ReenablePlayer(PlayerController p)
        {
            yield return null;   // a frame later, so the key that closed the reader is not also an interaction
            if (p != null) p.enabled = true;
        }

        void Update()
        {
            if (_canvas == null) return;
            float dt = Time.unscaledDeltaTime;
            _fade = Mathf.MoveTowards(_fade, _open ? 1f : 0f, dt * 5f);
            _group.alpha = _fade;
            _panel.localScale = Vector3.one * (0.96f + 0.04f * _fade);
            if (!_open)
            {
                if (_fade <= 0f && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
                return;
            }

            bool typing = _shownChars < _totalChars;
            if (typing)
            {
                _shownChars = Mathf.Min(_totalChars, _shownChars + charsPerSecond * dt);
                _body.maxVisibleCharacters = Mathf.FloorToInt(_shownChars);
            }

            float since = Time.unscaledTime - _openedAt;
            bool advance = Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space);
            if (since > 0.25f && (Input.GetKeyDown(KeyCode.Escape) || (advance && !typing))) { Close(); return; }
            if (advance && typing) { _shownChars = _totalChars; _body.maxVisibleCharacters = _totalChars; }

            float scroll = 0f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) scroll += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) scroll -= 1f;
            if (scroll != 0f)
            {
                float h = Mathf.Max(1f, _scroll.content.rect.height - ((RectTransform)_scroll.viewport).rect.height);
                _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + scroll * 600f * dt / h);
            }
        }
    }
}
