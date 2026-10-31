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
    /// device's screen with scanlines, static and a warbling signal, closed captions (Resources/Overhaul/Captions,
    /// SRT text named after the clip; C toggles them, remembered) and nothing else of the HUD or Daffodil on screen.
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
        RectTransform _device, _screen, _captionBox;
        RawImage _picture, _noise, _lines;
        Image _flash, _led, _backdrop;
        TextMeshProUGUI _caption, _hint;
        RenderTexture _copy;
        readonly List<Texture> _owned = new List<Texture>();

        struct Cue { public float a, b; public string text; }
        readonly List<Cue> _cues = new List<Cue>();
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
                Hint();
            }
            UpdateCaption();
        }

        void UpdateCaption()
        {
            string line = null;
            var cvp = CutsceneVideoPlayer.Instance;
            var vp = cvp != null ? cvp.GetComponent<VideoPlayer>() : null;
            if (_captionsOn && vp != null && vp.clip != null && vp.isPrepared)
            {
                LoadCues(vp.clip.name);
                float now = (float)vp.time;
                foreach (var c in _cues) if (now >= c.a && now < c.b) { line = c.text; break; }
            }
            bool show = !string.IsNullOrEmpty(line);
            if (_captionBox.gameObject.activeSelf != show) _captionBox.gameObject.SetActive(show);
            if (show && _caption.text != line) _caption.text = line;
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
            _captionBox.gameObject.SetActive(false);
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
            OverhaulUI.Place(_device, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1240f, 860f));
            var antenna = OverhaulUI.Image("Antenna", _device, GameHUD.WhiteSprite, new Color(0.62f, 0.6f, 0.55f));
            OverhaulUI.Place(antenna.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0f), new Vector2(-170f, -10f), new Vector2(7f, 300f));
            antenna.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -22f);
            var rim = OverhaulUI.Image("Rim", _device, GameHUD.RoundedSprite(40), new Color(0.42f, 0.29f, 0.19f), Image.Type.Sliced);
            OverhaulUI.Stretch(rim.rectTransform);
            var face = OverhaulUI.Image("Face", _device, GameHUD.RoundedSprite(32), new Color(0.15f, 0.12f, 0.1f), Image.Type.Sliced);
            OverhaulUI.Stretch(face.rectTransform, 16f);

            // the screen (16:9) in its bezel
            var bezel = OverhaulUI.Image("Bezel", _device, GameHUD.RoundedSprite(26), new Color(0.05f, 0.045f, 0.04f), Image.Type.Sliced);
            OverhaulUI.Place(bezel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(1104f, 628f));
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

            // closed captions on the bottom of the screen: white on a dark box
            _captionBox = OverhaulUI.Rect("Captions", _screen);
            OverhaulUI.Place(_captionBox, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(1010f, 100f));
            var back = OverhaulUI.Image("Back", _captionBox, GameHUD.RoundedSprite(10), new Color(0f, 0f, 0f, 0.72f), Image.Type.Sliced);
            OverhaulUI.Stretch(back.rectTransform);
            _caption = OverhaulUI.Label("Text", _captionBox, "", 30f, Color.white, TextAlignmentOptions.Center, FontWeight.Medium, true);
            OverhaulUI.Stretch(_caption.rectTransform, 10f);
            _captionBox.gameObject.SetActive(false);

            // speaker grille, dial, signal lamp and maker's plate under the screen
            var grille = Raw("Grille", _device, Own(GrilleTexture()), new Color(0.75f, 0.66f, 0.52f, 0.9f));
            OverhaulUI.Place(grille.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(60f, 46f), new Vector2(640f, 112f));
            grille.uvRect = new Rect(0f, 0f, 40f, 7f);
            var dial = OverhaulUI.Image("Dial", _device, GameHUD.RoundedSprite(44), new Color(0.72f, 0.62f, 0.46f), Image.Type.Sliced);
            OverhaulUI.Place(dial.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-70f, 52f), new Vector2(92f, 92f));
            var dialMark = OverhaulUI.Image("Mark", dial.rectTransform, GameHUD.WhiteSprite, new Color(0.2f, 0.14f, 0.1f));
            OverhaulUI.Place(dialMark.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(5f, 40f));
            dialMark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -35f);
            _led = OverhaulUI.Image("Lamp", _device, GameHUD.RoundedSprite(12), new Color(1f, 0.6f, 0.2f), Image.Type.Sliced);
            OverhaulUI.Place(_led.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-200f, 88f), new Vector2(22f, 22f));
            var plate = OverhaulUI.Label("Plate", _device, "EN-JOY RELAY  ·  XANTHE TERRA", 17f, new Color(0.86f, 0.78f, 0.64f, 0.9f),
                                         TextAlignmentOptions.MidlineRight, FontWeight.SemiBold, false, 4f);
            OverhaulUI.Place(plate.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-180f, 30f), new Vector2(420f, 30f));

            _hint = OverhaulUI.Label("Hint", _canvas.transform, "", 17f, OverhaulUI.Dim, TextAlignmentOptions.Center);
            OverhaulUI.Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(900f, 28f));
            Hint();
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
