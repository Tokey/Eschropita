using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Gives every text the game draws the UI type (<see cref="UiType"/>): the HUD's, the dialogue box, title cards,
    /// prompts and the overhaul's own screens. Texts are picked up as soon as they first render (TextMeshPro's
    /// text-changed event) plus a slow safety scan. Only texts still on TextMeshPro's default font are touched, so a
    /// font someone chose on purpose stays. Panels and colours are never changed. F10 switches to the stock type and back.
    /// </summary>
    [OverhaulModule(3)]
    public class UiTypeDirector : MonoBehaviour
    {
        readonly HashSet<TMP_Text> _pending = new HashSet<TMP_Text>();
        readonly HashSet<TMP_Text> _ignored = new HashSet<TMP_Text>();
        readonly List<TMP_Text> _work = new List<TMP_Text>();
        float _nextScan;

        void OnEnable() => TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        void OnDisable() => TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);

        void OnDestroy()
        {
            if (!UiType.Enabled) UiType.SetEnabled(true);   // the next play session starts with the type on
        }

        void Start()
        {
            if (UiType.Init()) Scan();
        }

        // Raised from inside TextMeshPro's mesh generation (often during the canvas rebuild), so only queue here.
        void OnTextChanged(Object o)
        {
            if (o is TMP_Text t && !_ignored.Contains(t) && !UiType.IsKnown(t)) _pending.Add(t);
        }

        void LateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F10)) Toggle();
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                UiType.Prune();
                _ignored.RemoveWhere(t => t == null);
                Scan();
            }
            if (_pending.Count == 0) return;
            _work.Clear();
            _work.AddRange(_pending);
            _pending.Clear();
            foreach (var t in _work) Adopt(t);
        }

        void Toggle()
        {
            UiType.SetEnabled(!UiType.Enabled);
            var hud = GameHUD.Instance;
            if (hud != null) hud.Toast(UiType.Enabled ? "UI type: new (F10)" : "UI type: original (F10)", 2f);
        }

        void Scan()
        {
            foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!_ignored.Contains(t) && !UiType.IsKnown(t)) Adopt(t);
        }

        void Adopt(TMP_Text t)
        {
            if (t == null || UiType.IsKnown(t)) return;
            if (!UiType.UsesStockFont(t)) { _ignored.Add(t); return; }
            Plan(t, out var role, out float size, out float spacing, out bool dropCaps);
            UiType.Style(t, role, size, spacing, dropCaps);
        }

        /// <summary>
        /// Role and size per HUD text. The dialogue and the big cards get hand-picked sizes (checked against their
        /// layout in GameHUD); everything else follows the general rule in <see cref="UiType"/>.
        /// </summary>
        static void Plan(TMP_Text t, out UiType.Role role, out float size, out float spacing, out bool dropCaps)
        {
            string name = t.name;
            string parent = t.transform.parent != null ? t.transform.parent.name : "";
            size = -1f;
            spacing = -1f;
            dropCaps = false;

            if (UiType.Luma(t.color) < 0.35f) { role = UiType.Role.Ink; return; }   // dark letter on a light key cap

            if (parent == "DialoguePanel")
            {
                if (name == "Speaker") { role = UiType.Role.Heading; size = 22f; spacing = 0.5f; dropCaps = true; return; }
                if (name == "Body") { role = UiType.Role.Reading; size = 23f; return; }
            }
            if (name == "Title" && t.fontSize >= 30f)
            {
                // video placeholder card (34) and the game-over / end cards (44): clear of their subtitles at these sizes
                role = UiType.Role.Display;
                size = t.fontSize < 40f ? 40f : 50f;
                spacing = 4f;
                return;
            }
            if (name == "Subtitle") { role = UiType.Role.Body; size = Mathf.Max(t.fontSize + 3f, 17f); return; }
            if (parent == "Toast" && name == "Text") { role = UiType.Role.Body; size = 17f; return; }

            role = UiType.Guess(t, t.text);
        }
    }
}
