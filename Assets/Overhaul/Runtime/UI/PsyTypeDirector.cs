using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Gives every text the game draws the psychedelic type (<see cref="PsyType"/>): the HUD's, the dialogue box, title
    /// cards, prompts and the overhaul's own screens. Texts are picked up as soon as they first render (TextMeshPro's
    /// text-changed event) plus a slow safety scan; only texts still on TextMeshPro's default font are touched, so a
    /// font someone chose on purpose stays. The dark panel right behind a text gets the matching tint and rim.
    /// F10 switches the look off and on (before / after).
    /// </summary>
    [OverhaulModule(3)]
    public class PsyTypeDirector : MonoBehaviour
    {
        readonly HashSet<TMP_Text> _pending = new HashSet<TMP_Text>();
        readonly HashSet<TMP_Text> _ignored = new HashSet<TMP_Text>();
        readonly List<TMP_Text> _work = new List<TMP_Text>();
        float _nextScan;

        void OnEnable() => TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        void OnDisable() => TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);

        void OnDestroy()
        {
            if (!PsyType.Enabled) PsyType.SetEnabled(true);   // the next play session starts with the look on
        }

        void Start()
        {
            if (PsyType.Init()) Scan();
        }

        // Raised from inside TextMeshPro's mesh generation (often during the canvas rebuild), so only queue here.
        void OnTextChanged(Object o)
        {
            if (o is TMP_Text t && !_ignored.Contains(t) && !PsyType.IsKnown(t)) _pending.Add(t);
        }

        void LateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F10)) Toggle();
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                PsyType.Prune();
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
            PsyType.SetEnabled(!PsyType.Enabled);
            var hud = GameHUD.Instance;
            if (hud != null) hud.Toast(PsyType.Enabled ? "Psychedelic type: on (F10)" : "Psychedelic type: off (F10)", 2f);
        }

        void Scan()
        {
            foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!_ignored.Contains(t) && !PsyType.IsKnown(t)) Adopt(t);
        }

        void Adopt(TMP_Text t)
        {
            if (t == null || PsyType.IsKnown(t)) return;
            if (!PsyType.UsesStockFont(t)) { _ignored.Add(t); return; }
            Plan(t, out var role, out float size, out float spacing, out bool dropCaps);
            PsyType.Style(t, role, size, spacing, dropCaps);
            // the rounded backdrop right behind it (toast, dialogue box, objective, prompt pill)
            var p = t.transform.parent;
            for (int i = 0; i < 2 && p != null; i++, p = p.parent)
            {
                var img = p.GetComponent<Image>();
                if (img != null && img.type == Image.Type.Sliced) { PsyType.StylePanel(img); break; }
            }
        }

        /// <summary>
        /// Role and size per HUD text. The dialogue and the big cards get hand-picked sizes (checked against their
        /// layout in GameHUD); everything else follows the general rule in <see cref="PsyType"/>.
        /// </summary>
        static void Plan(TMP_Text t, out PsyType.Role role, out float size, out float spacing, out bool dropCaps)
        {
            string name = t.name;
            string parent = t.transform.parent != null ? t.transform.parent.name : "";
            size = -1f;
            spacing = -1f;
            dropCaps = false;

            if (PsyType.Luma(t.color) < 0.35f) { role = PsyType.Role.Ink; return; }   // dark letter on a light key cap

            if (parent == "DialoguePanel")
            {
                if (name == "Speaker") { role = PsyType.Role.Heading; size = 22f; spacing = 0.5f; dropCaps = true; return; }
                if (name == "Body") { role = PsyType.Role.Reading; size = 22f; return; }
            }
            if (name == "Title" && t.fontSize >= 30f)
            {
                // video placeholder card (34) and the game-over / end cards (44): clear of their subtitles at these sizes
                role = PsyType.Role.Display;
                size = t.fontSize < 40f ? 42f : 54f;
                spacing = 2f;
                return;
            }
            if (name == "Subtitle") { role = PsyType.Role.Body; size = Mathf.Max(t.fontSize + 3f, 17f); return; }
            if (parent == "Toast" && name == "Text") { role = PsyType.Role.Body; size = 17f; return; }

            role = PsyType.Guess(t, t.text);
        }
    }
}
