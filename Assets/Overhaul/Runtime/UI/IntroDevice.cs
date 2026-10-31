using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The intro message as it was meant to arrive: on a battered 70s radio-tablet ("a woman's voice warbles through
    /// unstable sound frequencies emitting from the vintage radio"). While the intro plays, the video sits on the
    /// device's screen with scanlines, static and a warbling signal, and nothing else of the HUD or Daffodil on screen.
    /// Closed captions (Resources/Overhaul/Captions, SRT text named after the clip; C toggles them, remembered) run on
    /// the device's own read-out under the screen: a glowing dot-matrix strip like an old calculator's, each line
    /// entering on the right as it is spoken and travelling left, faster when the speech runs ahead of it.
    /// When it ends or is skipped the screen powers off like an old tube (the picture collapses to a line, then a
    /// dot), the device drops away toward Eschropita, and the camera pulls back from close on her into the scene.
    /// </summary>
    [OverhaulModule(73)]
    public class IntroDevice : MonoBehaviour
    {
        public float powerOffSeconds = 0.9f;
        public float revealSeconds = 3.4f;
        [Tooltip("Camera distance / field of view the reveal starts from (it eases back to the rig's own values).")]
        public float closeDistance = 3.2f;
        public float closeFov = 24f;

        enum State { Waiting, Playing, PowerOff, Reveal, Done }
        State _state;
        float _t;

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _device, _screen, _tape;
        RawImage _picture, _noise, _lines;
        Image _flash, _led, _backdrop, _ccLamp;
        TextMeshProUGUI _hint, _ccLabel, _idle;
        Material _dotMat;
        RenderTexture _copy;
        readonly List<Texture> _owned = new List<Texture>();

        struct Cue { public float a, b; public string text; }
        readonly List<Cue> _cues = new List<Cue>();

        // the read-out: one label per caption on a tape that slides left
        class Segment { public TextMeshProUGUI tmp; public float x, w; }
        readonly List<Segment> _segments = new List<Segment>();
        float _tapeX, _tapeEnd;
        int _lastCue = -1;
        [Tooltip("Read-out scroll speed (px/s) with nothing waiting; it speeds up while text is still off to the right.")]
        public float tickerSpeed = 95f;
        public float tickerMaxSpeed = 720f;
        static readonly Color DotLit = new Color(0.52f, 1f, 0.84f);
        static TMP_FontAsset _dotFont;
        static bool _dotTried;
        string _cuesFor;
        bool _captionsOn = true;
        const string CaptionPref = "Eschropita.Captions";

        IsoFollowCamera _rig;
        float _origDistance, _origFov, _origOrtho;
        Vector2 _deviceFrom;

        void Start()
        {
            try { _captionsOn = PlayerPrefs.GetInt(CaptionPref, 1) == 1; } catch { }
        }

        void OnDestroy()
        {
            RestoreCamera();
            Release();
        }

        void Update()
        {
            var gfm = GameFlowManager.Instance;
            switch (_state)
            {
                case State.Waiting:
                    if (gfm == null) return;
                    if (gfm.Phase > GamePhase.IntroVideo) { _state = State.Done; return; }   // started past the intro
                    var cvp = CutsceneVideoPlayer.Instance;
                    if (gfm.Phase == GamePhase.IntroVideo && cvp != null && cvp.IsPlaying) Begin();
                    break;
                case State.Playing:
                    Pump();
                    if (gfm == null || gfm.Phase != GamePhase.IntroVideo) BeginPowerOff();
                    break;
                case State.PowerOff:
                    PowerOff();
                    break;
                case State.Reveal:
                    Reveal();
                    break;
            }
        }

        // ------------------------------------------------------------------ playing

        void Begin()
        {
            Build();
            _state = State.Playing;
        }

        void Pump()
        {
            float t = Time.unscaledTime;
            var hud = GameHUD.Instance;
            var tex = hud != null && hud.VideoSurface != null && hud.VideoSurface.enabled ? hud.VideoSurface.texture : null;
            if (tex != null)
            {
                if (_copy == null || _copy.width != tex.width || _copy.height != tex.height)
                {
                    if (_copy != null) _copy.Release();
                    _copy = new RenderTexture(tex.width, tex.height, 0) { name = "IntroDeviceScreen" };
                    _copy.Create();
                }
                Graphics.Blit(tex, _copy);   // our own copy, so the last frame survives the player's teardown
                _picture.texture = _copy;
                _picture.color = Color.white;
            }

            // the warbling signal: bursts of static and a sideways slip of the picture now and then
            float warble = Mathf.Clamp01(Mathf.PerlinNoise(t * 0.6f, 3.1f) * 2.4f - 1.5f);
            float tuning = tex == null ? 1f : 0f;
            _noise.uvRect = new Rect(Random.value, Random.value, 2.2f, 1.25f);
            _noise.color = new Color(1f, 1f, 1f, 0.05f + 0.55f * tuning + 0.22f * warble);
            _picture.rectTransform.anchoredPosition = new Vector2(warble * 9f * Mathf.Sin(t * 53f), 0f);
            _lines.uvRect = new Rect(0f, t * 0.04f, 1f, 198f);
            float glow = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 6.3f)) * Random.Range(0.7f, 1f);
            _led.color = Color.Lerp(new Color(0.35f, 0.16f, 0.04f), new Color(1f, 0.62f, 0.18f), glow);

            if (Input.GetKeyDown(KeyCode.C))
            {
                _captionsOn = !_captionsOn;
                try { PlayerPrefs.SetInt(CaptionPref, _captionsOn ? 1 : 0); } catch { }
                if (!_captionsOn) ClearTape();
                else if (_lastCue >= 0 && _lastCue < _cues.Count) Enqueue(_cues[_lastCue].text);   // pick up the line being spoken
                Hint();
            }
            UpdateCaption();
            Ticker(Time.unscaledDeltaTime);
        }

        /// <summary>Feeds the read-out: a caption goes on the tape the moment its cue starts.</summary>
        void UpdateCaption()
        {
            var cvp = CutsceneVideoPlayer.Instance;
            var vp = cvp != null ? cvp.GetComponent<VideoPlayer>() : null;
            if (vp == null || vp.clip == null || !vp.isPrepared) return;
            LoadCues(vp.clip.name);
            float now = (float)vp.time;
            int cue = -1;
            for (int i = 0; i < _cues.Count; i++) if (now >= _cues[i].a && now < _cues[i].b) { cue = i; break; }
            if (cue < 0 || cue == _lastCue) return;
            _lastCue = cue;
            if (_captionsOn) Enqueue(_cues[cue].text);
        }

        void Enqueue(string text)
        {
            string s = "\u25C6  " + text.Replace('\n', ' ').Trim().ToUpperInvariant();
            float window = _tape.rect.width;
            var tmp = DotLabel("Caption", _tape, s, 36f);
            float w = tmp.GetPreferredValues(s).x;
            float start = Mathf.Max(_tapeEnd + 70f, _tapeX + window);   // after what is queued, or just off the right edge
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(w + 8f, _tape.rect.height);
            _segments.Add(new Segment { tmp = tmp, x = start, w = w });
            _tapeEnd = start + w;
        }

        /// <summary>Slides the tape left: a calm crawl, quicker the more text is still waiting off to the right.</summary>
        void Ticker(float dt)
        {
            float window = _tape.rect.width;
            float backlog = _tapeEnd - (_tapeX + window);
            float speed = Mathf.Clamp(tickerSpeed + Mathf.Max(0f, backlog) * 0.85f, tickerSpeed, tickerMaxSpeed);
            _tapeX += speed * dt;
            for (int i = _segments.Count - 1; i >= 0; i--)
            {
                var seg = _segments[i];
                if (seg.x + seg.w < _tapeX - 4f || seg.tmp == null)
                {
                    if (seg.tmp != null) Destroy(seg.tmp.gameObject);
                    _segments.RemoveAt(i);
                    continue;
                }
                seg.tmp.rectTransform.anchoredPosition = new Vector2(Mathf.Round(seg.x - _tapeX), 0f);
            }

            // the CC lamp is lit while captions are on; with them off (or nothing yet) the strip idles
            float t = Time.unscaledTime;
            _ccLamp.color = _captionsOn ? new Color(DotLit.r, DotLit.g, DotLit.b, 0.9f) : new Color(DotLit.r, DotLit.g, DotLit.b, 0.12f);
            _ccLabel.color = _captionsOn ? new Color(0.02f, 0.08f, 0.06f, 1f) : new Color(DotLit.r, DotLit.g, DotLit.b, 0.35f);
            bool idle = _segments.Count == 0;
            if (_idle.gameObject.activeSelf != idle) _idle.gameObject.SetActive(idle);
            if (idle)
            {
                string want = _captionsOn ? (Mathf.Repeat(t, 1.1f) < 0.6f ? "\u258C" : " ") : "CAPTIONS OFF  \u00B7  PRESS C";
                if (_idle.text != want) _idle.text = want;
                _idle.color = new Color(DotLit.r, DotLit.g, DotLit.b, _captionsOn ? 0.85f : 0.4f);
            }
        }

        void ClearTape()
        {
            foreach (var seg in _segments) if (seg.tmp != null) Destroy(seg.tmp.gameObject);
            _segments.Clear();
            _tapeEnd = _tapeX;
        }

        /// <summary>SRT text: blocks of an index line, "hh:mm:ss,mmm --> hh:mm:ss,mmm", then the caption lines.</summary>
        void LoadCues(string clip)
        {
            if (_cuesFor == clip) return;
            _cuesFor = clip;
            _cues.Clear();
            var asset = Resources.Load<TextAsset>("Overhaul/Captions/" + clip);
            if (asset == null) return;
            var blocks = asset.text.Replace("\r", "").Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                var lines = block.Trim().Split('\n');
                int timing = System.Array.FindIndex(lines, l => l.Contains("-->"));
                if (timing < 0 || timing >= lines.Length - 1) continue;
                var parts = lines[timing].Split(new[] { "-->" }, System.StringSplitOptions.None);
                if (parts.Length != 2 || !TryTime(parts[0], out float a) || !TryTime(parts[1], out float b)) continue;
                _cues.Add(new Cue { a = a, b = b, text = string.Join("\n", lines, timing + 1, lines.Length - timing - 1) });
            }
        }

        static bool TryTime(string s, out float seconds)
        {
            seconds = 0f;
            var p = s.Trim().Replace(',', '.').Split(':');
            if (p.Length != 3) return false;
            if (!int.TryParse(p[0], out int h) || !int.TryParse(p[1], out int m)) return false;
            if (!float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sec)) return false;
            seconds = h * 3600f + m * 60f + sec;
            return true;
        }

        // ------------------------------------------------------------------ the end

        void BeginPowerOff()
        {
            _state = State.PowerOff;
            _t = 0f;
            ClearTape();
            _idle.gameObject.SetActive(false);
            _ccLamp.color = new Color(0f, 0f, 0f, 0f);
            _ccLabel.color = new Color(DotLit.r, DotLit.g, DotLit.b, 0.12f);
            _hint.gameObject.SetActive(false);
            Sfx.Play("sfx.daffodil.shutdown", null, 0.7f);
            // behind the closed device, bring the camera in close on Eschropita; the reveal pulls it back out
            _rig = FindAnyObjectByType<IsoFollowCamera>();
            if (_rig != null)
            {
                _origDistance = _rig.baseDistance;
                _origFov = _rig.baseFOV;
                _origOrtho = _rig.baseOrthoSize;
                _rig.baseDistance = closeDistance;
                _rig.baseFOV = closeFov;
                _rig.baseOrthoSize = _origOrtho * 0.4f;
            }
        }

        /// <summary>An old tube switching off: the picture squeezes to a bright line, the line to a dot, the dot fades.</summary>
        void PowerOff()
        {
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / Mathf.Max(0.1f, powerOffSeconds));
            float sy = k < 0.4f ? Mathf.Lerp(1f, 0.012f, Ease(k / 0.4f)) : 0.012f;
            float sx = k < 0.4f ? 1f : k < 0.68f ? Mathf.Lerp(1f, 0.015f, Ease((k - 0.4f) / 0.28f)) : 0.015f;
            _screen.localScale = new Vector3(sx, sy, 1f);
            float flash = k < 0.4f ? k / 0.4f : 1f - Mathf.Clamp01((k - 0.68f) / 0.32f);
            _flash.color = new Color(1f, 0.97f, 0.9f, flash);
            if (_t >= powerOffSeconds)
            {
                _screen.gameObject.SetActive(false);
                _state = State.Reveal;
                _t = 0f;
                _deviceFrom = _device.anchoredPosition;
            }
        }

        /// <summary>The device drops away toward Eschropita while the world fades in and the camera pulls back.</summary>
        void Reveal()
        {
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / Mathf.Max(0.1f, revealSeconds));
            float fly = Ease(Mathf.Clamp01(k / 0.45f));
            Vector2 to = _deviceFrom + new Vector2(0f, -420f);
            var cam = Camera.main;
            var bridge = SignalBridge.Instance;
            if (cam != null && bridge != null && bridge.Player != null)
            {
                Vector3 sp = cam.WorldToViewportPoint(bridge.Player.transform.position + Vector3.up * 0.9f);
                if (sp.z > 0f) to = new Vector2((sp.x - 0.5f) * 1920f, (sp.y - 0.5f) * 1080f);
            }
            _device.anchoredPosition = Vector2.Lerp(_deviceFrom, to, fly);
            _device.localScale = Vector3.one * Mathf.Lerp(1f, 0.04f, fly);
            _device.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, -12f, fly));
            _backdrop.color = new Color(_backdrop.color.r, _backdrop.color.g, _backdrop.color.b, 1f - Ease(Mathf.Clamp01(k / 0.5f)));
            _group.alpha = 1f - Mathf.Clamp01((k - 0.35f) / 0.15f);

            if (_rig != null)
            {
                float e = Ease(k);
                _rig.baseDistance = Mathf.Lerp(closeDistance, _origDistance, e);
                _rig.baseFOV = Mathf.Lerp(closeFov, _origFov, e);
                _rig.baseOrthoSize = Mathf.Lerp(_origOrtho * 0.4f, _origOrtho, e);
            }
            if (k >= 1f)
            {
                RestoreCamera();
                Release();
                _state = State.Done;
            }
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        void RestoreCamera()
        {
            if (_rig == null) return;
            _rig.baseDistance = _origDistance;
            _rig.baseFOV = _origFov;
            _rig.baseOrthoSize = _origOrtho;
            _rig = null;
        }

        void Release()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            _segments.Clear();
            if (_dotMat != null) { Destroy(_dotMat); _dotMat = null; }
            if (_copy != null) { _copy.Release(); Destroy(_copy); _copy = null; }
            foreach (var t in _owned) if (t != null) Destroy(t);
            _owned.Clear();
        }

        // ------------------------------------------------------------------ the device

        void Hint()
        {
            string accent = OverhaulUI.Hex(OverhaulUI.Accent);
            _hint.text = $"<color=#{accent}><b>Space</b></color> / <color=#{accent}><b>Esc</b></color>  Skip        " +
                         $"<color=#{accent}><b>C</b></color>  Captions {(_captionsOn ? "on" : "off")}";
        }

        void Build()
        {
            _canvas = OverhaulUI.CreateCanvas("Intro Device", 400, transform);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _backdrop = OverhaulUI.Image("Backdrop", _canvas.transform, GameHUD.WhiteSprite, new Color(0.035f, 0.025f, 0.02f, 1f));
            OverhaulUI.Stretch(_backdrop.rectTransform);

            // walnut body with a dark face
            _device = OverhaulUI.Rect("Device", _canvas.transform);
            OverhaulUI.Place(_device, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(1240f, 944f));
            var antenna = OverhaulUI.Image("Antenna", _device, GameHUD.WhiteSprite, new Color(0.62f, 0.6f, 0.55f));
            OverhaulUI.Place(antenna.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0f), new Vector2(-170f, -10f), new Vector2(7f, 300f));
            antenna.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -22f);
            var rim = OverhaulUI.Image("Rim", _device, GameHUD.RoundedSprite(40), new Color(0.42f, 0.29f, 0.19f), Image.Type.Sliced);
            OverhaulUI.Stretch(rim.rectTransform);
            var face = OverhaulUI.Image("Face", _device, GameHUD.RoundedSprite(32), new Color(0.15f, 0.12f, 0.1f), Image.Type.Sliced);
            OverhaulUI.Stretch(face.rectTransform, 16f);

            // the screen (16:9) in its bezel
            var bezel = OverhaulUI.Image("Bezel", _device, GameHUD.RoundedSprite(26), new Color(0.05f, 0.045f, 0.04f), Image.Type.Sliced);
            OverhaulUI.Place(bezel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(1104f, 628f));
            _screen = OverhaulUI.Rect("Screen", bezel.rectTransform);
            OverhaulUI.Place(_screen, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1072f, 603f));
            _screen.gameObject.AddComponent<RectMask2D>();
            var black = OverhaulUI.Image("Black", _screen, GameHUD.WhiteSprite, Color.black);
            OverhaulUI.Stretch(black.rectTransform);
            _picture = Raw("Picture", _screen, null, new Color(1f, 1f, 1f, 0f));
            _lines = Raw("Scanlines", _screen, Own(ScanlineTexture()), new Color(1f, 1f, 1f, 0.55f));
            _noise = Raw("Static", _screen, Own(NoiseTexture()), new Color(1f, 1f, 1f, 0.5f));
            var glare = Raw("Glare", _screen, Own(GlareTexture()), new Color(1f, 1f, 1f, 0.22f));
            glare.raycastTarget = false;
            _flash = OverhaulUI.Image("Flash", _screen, GameHUD.WhiteSprite, new Color(1f, 1f, 1f, 0f));
            OverhaulUI.Stretch(_flash.rectTransform);

            // the caption read-out: a recessed dot-matrix strip under the screen, CC lamp on the left
            var well = OverhaulUI.Image("Readout", _device, GameHUD.RoundedSprite(16), new Color(0.06f, 0.045f, 0.035f), Image.Type.Sliced);
            OverhaulUI.Place(well.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -680f), new Vector2(1104f, 94f));
            var glass = OverhaulUI.Image("Glass", well.rectTransform, GameHUD.RoundedSprite(10), new Color(0.015f, 0.04f, 0.035f), Image.Type.Sliced);
            OverhaulUI.Stretch(glass.rectTransform, 9f);
            _ccLamp = OverhaulUI.Image("CC", glass.rectTransform, GameHUD.RoundedSprite(6), DotLit, Image.Type.Sliced);
            OverhaulUI.Place(_ccLamp.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(54f, 34f));
            _ccLabel = DotLabel("Label", _ccLamp.rectTransform, "CC", 24f);
            _ccLabel.alignment = TextAlignmentOptions.Center;
            OverhaulUI.Stretch(_ccLabel.rectTransform);
            _tape = OverhaulUI.Rect("Tape", glass.rectTransform);
            _tape.anchorMin = Vector2.zero; _tape.anchorMax = Vector2.one;
            _tape.offsetMin = new Vector2(86f, 4f); _tape.offsetMax = new Vector2(-16f, -4f);
            _tape.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(28, 0);
            _idle = DotLabel("Idle", _tape, "", 30f);
            _idle.alignment = TextAlignmentOptions.MidlineRight;
            OverhaulUI.Stretch(_idle.rectTransform);
            var dots = Raw("Dots", glass.rectTransform, Own(DotGridTexture()), Color.white);
            dots.uvRect = new Rect(0f, 0f, (1104f - 18f) / 4f, (94f - 18f) / 4f);   // a 4 px pixel grid
            var sheen = OverhaulUI.Image("Sheen", glass.rectTransform, GameHUD.WhiteSprite, new Color(1f, 1f, 1f, 0.035f));
            OverhaulUI.Place(sheen.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -3f), new Vector2(1070f, 26f));

            // speaker grille, dial, signal lamp and maker's plate under the screen
            var grille = Raw("Grille", _device, Own(GrilleTexture()), new Color(0.75f, 0.66f, 0.52f, 0.9f));
            OverhaulUI.Place(grille.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(60f, 34f), new Vector2(640f, 104f));
            grille.uvRect = new Rect(0f, 0f, 40f, 6.5f);
            var dial = OverhaulUI.Image("Dial", _device, GameHUD.RoundedSprite(44), new Color(0.72f, 0.62f, 0.46f), Image.Type.Sliced);
            OverhaulUI.Place(dial.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-70f, 40f), new Vector2(92f, 92f));
            var dialMark = OverhaulUI.Image("Mark", dial.rectTransform, GameHUD.WhiteSprite, new Color(0.2f, 0.14f, 0.1f));
            OverhaulUI.Place(dialMark.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(5f, 40f));
            dialMark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -35f);
            _led = OverhaulUI.Image("Lamp", _device, GameHUD.RoundedSprite(12), new Color(1f, 0.6f, 0.2f), Image.Type.Sliced);
            OverhaulUI.Place(_led.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-200f, 76f), new Vector2(22f, 22f));
            var plate = OverhaulUI.Label("Plate", _device, "EN-JOY RELAY  ·  XANTHE TERRA", 17f, new Color(0.86f, 0.78f, 0.64f, 0.9f),
                                         TextAlignmentOptions.MidlineRight, FontWeight.SemiBold, false, 4f);
            OverhaulUI.Place(plate.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-180f, 22f), new Vector2(420f, 30f));

            _hint = OverhaulUI.Label("Hint", _canvas.transform, "", 17f, OverhaulUI.Dim, TextAlignmentOptions.Center);
            OverhaulUI.Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(900f, 28f));
            Hint();
        }

        /// <summary>A glowing read-out label in the dot-matrix face. Not a stock-font text, so the UI type pass leaves it alone.</summary>
        TextMeshProUGUI DotLabel(string name, Transform parent, string text, float size)
        {
            var rt = OverhaulUI.Rect(name, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var font = DotFont();
            if (font != null)
            {
                tmp.font = font;
                if (_dotMat == null)
                {
                    _dotMat = new Material(font.material) { name = "Readout glow" };
                    _dotMat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                    _dotMat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0.2f, 1f, 0.75f, 0.55f));
                    _dotMat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.85f);
                    _dotMat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.55f);
                }
                tmp.fontSharedMaterial = _dotMat;
            }
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = DotLit;
            tmp.characterSpacing = 6f;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            return tmp;
        }

        static TMP_FontAsset DotFont()
        {
            if (_dotFont != null || _dotTried) return _dotFont;
            _dotTried = true;
            try
            {
                var font = Resources.Load<Font>("Overhaul/Fonts/DotGothic16-Latin");
                if (font == null) return null;
                _dotFont = TMP_FontAsset.CreateFontAsset(font, 48, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 512, 512,
                                                         AtlasPopulationMode.Dynamic, true);
                if (_dotFont != null)
                {
                    _dotFont.name = "DotGothic16 (runtime SDF)";
                    if (TMP_Settings.defaultFontAsset != null)
                        _dotFont.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Overhaul] Read-out font unavailable: " + e.Message); }
            return _dotFont;
        }

        RawImage Raw(string name, Transform parent, Texture tex, Color c)
        {
            var rt = OverhaulUI.Rect(name, parent);
            OverhaulUI.Stretch(rt);
            var r = rt.gameObject.AddComponent<RawImage>();
            r.texture = tex;
            r.color = c;
            r.raycastTarget = false;
            return r;
        }

        Texture2D Own(Texture2D t) { _owned.Add(t); return t; }

        static Texture2D ScanlineTexture()
        {
            var t = new Texture2D(1, 4, TextureFormat.RGBA32, false) { name = "Scanlines", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            t.SetPixels(new[] { new Color(0f, 0f, 0f, 0.5f), new Color(0f, 0f, 0f, 0.15f), new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.15f) });
            t.Apply(false, true);
            return t;
        }

        /// <summary>The read-out's pixel grid: thin dark lines between faintly lit cells, laid over the text.</summary>
        static Texture2D DotGridTexture()
        {
            const int n = 4;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Dots", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = x == n - 1 || y == n - 1 ? new Color(0.01f, 0.03f, 0.025f, 0.5f) : new Color(DotLit.r, DotLit.g, DotLit.b, 0.04f);
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        static Texture2D NoiseTexture()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Static", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color[n * n];
            var rng = new System.Random(1971);
            for (int i = 0; i < px.Length; i++) { float v = (float)rng.NextDouble(); px[i] = new Color(v, v, v, v * 0.9f); }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        static Texture2D GlareTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Glare", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = (x + y) / (2f * (n - 1));                       // diagonal 0..1
                    float band = Mathf.Exp(-Mathf.Pow((d - 0.32f) / 0.12f, 2f)) + 0.35f * Mathf.Exp(-Mathf.Pow((d - 0.5f) / 0.04f, 2f));
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(band * 0.6f));
                }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        static Texture2D GrilleTexture()
        {
            const int n = 16;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Grille", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    px[y * n + x] = new Color(0.08f, 0.06f, 0.05f, Mathf.Clamp01((0.62f - d) * 8f));
                }
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }
    }
}
