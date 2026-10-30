using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The life-support system as a living gauge (top right), replacing the HUD's plain bar while visible:
    /// an O₂ ring that fills and breathes, rising bubbles whose pace follows the level, an inner ring showing the
    /// windmill's power, a numeric read-out, an alarm pulse below 25 % and a bright flourish when it recovers.
    /// Appears when life support switches on (the first tornado strike, as in the design doc).
    /// </summary>
    [OverhaulModule(60)]
    public class LifeSupportGauge : MonoBehaviour
    {
        const float Size = 132f;

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _root, _bubbleArea;
        Image _ringBack, _ring, _powerBack, _power, _glow;
        TextMeshProUGUI _pct, _label;
        readonly Image[] _bubbles = new Image[7];
        readonly float[] _bubbleT = new float[7];
        float _shown, _value = -1f, _flourish, _lastValue = 1f;

        void Start()
        {
            _canvas = OverhaulUI.CreateCanvas("Life Support Gauge", 45, transform);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _root = OverhaulUI.Rect("Gauge", _canvas.transform);
            OverhaulUI.Place(_root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f), new Vector2(Size + 120f, Size));

            var plate = OverhaulUI.PanelImage("Plate", _root, OverhaulUI.Panel, 16);
            OverhaulUI.Stretch(plate.rectTransform);

            var dial = OverhaulUI.Rect("Dial", _root);
            OverhaulUI.Place(dial, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(Size - 16f, Size - 16f));
            _glow = OverhaulUI.Image("Glow", dial, OverhaulUI.SoftCircle, new Color(1f, 1f, 1f, 0f));
            OverhaulUI.Stretch(_glow.rectTransform, -18f);
            _ringBack = OverhaulUI.Image("RingBack", dial, OverhaulUI.RingSprite, new Color(1f, 1f, 1f, 0.12f));
            OverhaulUI.Stretch(_ringBack.rectTransform);
            _ring = OverhaulUI.Image("Ring", dial, OverhaulUI.RingSprite, OverhaulUI.Good, Image.Type.Filled);
            OverhaulUI.Stretch(_ring.rectTransform);
            _ring.fillMethod = Image.FillMethod.Radial360;
            _ring.fillOrigin = (int)Image.Origin360.Top;
            _ring.fillClockwise = false;

            _powerBack = OverhaulUI.Image("PowerBack", dial, OverhaulUI.RingSprite, new Color(1f, 1f, 1f, 0.08f));
            OverhaulUI.Stretch(_powerBack.rectTransform, 17f);
            _power = OverhaulUI.Image("Power", dial, OverhaulUI.RingSprite, OverhaulUI.Accent, Image.Type.Filled);
            OverhaulUI.Stretch(_power.rectTransform, 17f);
            _power.fillMethod = Image.FillMethod.Radial360;
            _power.fillOrigin = (int)Image.Origin360.Top;
            _power.fillClockwise = false;

            _bubbleArea = OverhaulUI.Rect("Bubbles", dial);
            OverhaulUI.Stretch(_bubbleArea, 32f);
            for (int i = 0; i < _bubbles.Length; i++)
            {
                _bubbles[i] = OverhaulUI.Image("Bubble", _bubbleArea, OverhaulUI.SoftCircle, new Color(1f, 1f, 1f, 0f));
                _bubbleT[i] = i / (float)_bubbles.Length;
            }

            _pct = OverhaulUI.Label("Pct", dial, "100", 30f, OverhaulUI.Text, TextAlignmentOptions.Center, FontWeight.Bold);
            OverhaulUI.Stretch(_pct.rectTransform);
            _pct.rectTransform.anchoredPosition = new Vector2(0f, 4f);
            var unit = OverhaulUI.Label("Unit", dial, "O₂ %", 11f, OverhaulUI.Dim, TextAlignmentOptions.Center, FontWeight.Medium, false, 2f);
            OverhaulUI.Place(unit.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(80f, 16f));

            _label = OverhaulUI.Label("Label", _root, "LIFE SUPPORT", 13f, OverhaulUI.Dim, TextAlignmentOptions.TopLeft, FontWeight.SemiBold, true, 2f);
            OverhaulUI.Place(_label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(110f, 80f));
        }

        void OnEnable()
        {
            GameSignals.SiteRepaired += OnRepaired;
        }

        void OnDisable()
        {
            GameSignals.SiteRepaired -= OnRepaired;
            OverhaulHooks.HideHudLifeBar = false;
        }

        void OnRepaired(TaskSite s) => _flourish = 1f;

        void Update()
        {
            if (_group == null) return;
            float dt = Time.unscaledDeltaTime;
            var life = LifeSupport.Instance;
            var gfm = GameFlowManager.Instance;
            bool hiddenPhase = gfm != null && (gfm.Phase == GamePhase.IntroVideo || gfm.Phase == GamePhase.OutroVideo || gfm.Phase == GamePhase.End);
            bool show = life != null && life.Active && !hiddenPhase;
            _shown = Mathf.MoveTowards(_shown, show ? 1f : 0f, dt * 3f);
            _group.alpha = _shown;
            OverhaulHooks.HideHudLifeBar = show;
            if (life == null || _shown <= 0f) return;

            float v = life.Value01;
            if (_value < 0f) _value = v;
            _value = Mathf.Lerp(_value, v, 1f - Mathf.Exp(-dt * 6f));
            if (v - _lastValue > 0.15f) _flourish = 1f;
            _lastValue = Mathf.Lerp(_lastValue, v, dt * 0.5f);

            Color c = OverhaulPalette.Status(_value);
            bool alarm = _value < 0.25f;
            float t = Time.unscaledTime;
            float breath = 0.5f + 0.5f * Mathf.Sin(t * (alarm ? 5.6f : 1.6f));

            _ring.fillAmount = _value;
            _ring.color = Color.Lerp(c, Color.white, _flourish * 0.6f);
            _power.fillAmount = Mathf.Lerp(_power.fillAmount, life.PowerLevel01, 1f - Mathf.Exp(-dt * 4f));
            _power.color = life.PowerLevel01 >= 0.5f ? OverhaulUI.Accent : OverhaulUI.Bad;
            _pct.text = Mathf.RoundToInt(_value * 100f).ToString();
            _pct.color = alarm ? Color.Lerp(OverhaulUI.Text, OverhaulUI.Bad, breath) : OverhaulUI.Text;
            _label.text = alarm ? "LIFE SUPPORT\n<color=#" + OverhaulUI.Hex(OverhaulUI.Bad) + ">CRITICAL</color>"
                       : life.RatePerSecond < -0.01f ? "LIFE SUPPORT\n<color=#" + OverhaulUI.Hex(OverhaulUI.Warn) + ">DRAINING</color>"
                       : life.RatePerSecond > 0.01f ? "LIFE SUPPORT\n<color=#" + OverhaulUI.Hex(OverhaulUI.Good) + ">STABLE</color>"
                       : "LIFE SUPPORT";

            float glowA = (alarm ? 0.35f * breath : 0.08f * breath) + 0.5f * _flourish;
            _glow.color = new Color(c.r, c.g, c.b, glowA);
            float scale = 1f + (alarm ? 0.04f * breath : 0f) + 0.12f * _flourish;
            _root.localScale = Vector3.one * scale;
            _flourish = Mathf.MoveTowards(_flourish, 0f, dt * 1.5f);

            // Bubbles rise faster when there is more oxygen; they stall and turn red when it runs out.
            float h = _bubbleArea.rect.height, w = _bubbleArea.rect.width;
            for (int i = 0; i < _bubbles.Length; i++)
            {
                _bubbleT[i] += dt * (0.12f + 0.35f * _value) * (0.7f + 0.15f * i % 3);
                if (_bubbleT[i] > 1f) _bubbleT[i] -= 1f;
                float y = Mathf.Lerp(-h * 0.45f, h * 0.45f, _bubbleT[i]);
                float x = Mathf.Sin(_bubbleT[i] * 9f + i * 1.7f) * w * 0.18f + (i - 3) * w * 0.06f;
                var rt = _bubbles[i].rectTransform;
                rt.anchoredPosition = new Vector2(x, y);
                float s = (4f + 3f * ((i * 7) % 3)) * (0.6f + 0.6f * _value);
                rt.sizeDelta = new Vector2(s, s);
                float a = Mathf.Sin(_bubbleT[i] * Mathf.PI) * 0.55f * Mathf.Clamp01(_value * 2f);
                _bubbles[i].color = new Color(c.r, c.g, c.b, a);
            }
        }
    }
}
