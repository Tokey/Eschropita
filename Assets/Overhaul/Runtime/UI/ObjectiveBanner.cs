using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// "NEW OBJECTIVE" banner: when the objective changes, a short card slides down under the HUD's objective slot,
    /// holds for a few seconds and fades out (queued if several arrive together). Complements GameHUD's own
    /// objective text, which stays as the persistent reminder.
    /// </summary>
    [OverhaulModule(62)]
    public class ObjectiveBanner : MonoBehaviour
    {
        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _card;
        TextMeshProUGUI _header, _text;
        readonly Queue<string> _queue = new Queue<string>();
        float _t = -1f;
        const float In = 0.35f, Hold = 2.8f, Out = 0.5f;

        void Start()
        {
            _canvas = OverhaulUI.CreateCanvas("Objective Banner", 46, transform);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _card = OverhaulUI.Rect("Card", _canvas.transform);
            OverhaulUI.Place(_card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(640f, 84f));
            var plate = OverhaulUI.PanelImage("Plate", _card, OverhaulUI.Panel, 16);
            OverhaulUI.Stretch(plate.rectTransform);
            var stripe = OverhaulUI.Image("Stripe", _card, GameHUD.WhiteSprite, OverhaulUI.Accent);
            OverhaulUI.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(120f, 3f));
            _header = OverhaulUI.Label("Header", _card, "NEW OBJECTIVE", 13f, OverhaulUI.Accent, TextAlignmentOptions.Center, FontWeight.SemiBold, false, 4f);
            OverhaulUI.Place(_header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(600f, 20f));
            _text = OverhaulUI.Label("Text", _card, "", 26f, OverhaulUI.Text, TextAlignmentOptions.Center, FontWeight.Medium, true);
            OverhaulUI.Place(_text.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(600f, 40f));
        }

        void OnEnable() => GameSignals.ObjectiveChanged += OnObjective;
        void OnDisable() => GameSignals.ObjectiveChanged -= OnObjective;

        void OnObjective(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_queue.Count > 2) _queue.Dequeue();
            _queue.Enqueue(text);
        }

        void Update()
        {
            if (_group == null) return;
            float dt = Time.unscaledDeltaTime;
            if (_t < 0f)
            {
                if (_queue.Count == 0) { _group.alpha = 0f; return; }
                _text.text = _queue.Dequeue();
                _t = 0f;
            }
            _t += dt;
            float a, y;
            if (_t < In) { float k = _t / In; a = k; y = Mathf.Lerp(30f, 0f, 1f - (1f - k) * (1f - k)); }
            else if (_t < In + Hold) { a = 1f; y = 0f; }
            else if (_t < In + Hold + Out) { float k = (_t - In - Hold) / Out; a = 1f - k; y = -8f * k; }
            else { _t = -1f; a = 0f; y = 0f; }
            _group.alpha = a;
            _card.anchoredPosition = new Vector2(0f, -150f + y);
            _card.localScale = Vector3.one * (1f + (_t >= 0f && _t < In ? 0.06f * (1f - _t / In) : 0f));
        }
    }
}
