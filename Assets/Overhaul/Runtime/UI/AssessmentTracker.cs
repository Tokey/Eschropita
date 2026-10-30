using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The Mycari students' "Field Assessment" (design doc win condition: do each task four times), shown under
    /// the life-support gauge once the player can direct Mycari:
    ///   ● Repair  (blue Fixers)   ● Dismantle (red Breakers)   ● Freedom (green Wanderers escorted to the door)
    /// Each row has four pips that pop and glow as they fill; a completed row gets a check.
    /// </summary>
    [OverhaulModule(61)]
    public class AssessmentTracker : MonoBehaviour
    {
        class Row { public string id; public TextMeshProUGUI label, count; public Image[] pips; public Image dot; public float pop; public int shown = -1; public Color color; }

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _root;
        readonly Row[] _rows = new Row[3];
        float _shown, _headerPop;

        void Start()
        {
            _canvas = OverhaulUI.CreateCanvas("Field Assessment", 44, transform);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _root = OverhaulUI.Rect("Assessment", _canvas.transform);
            OverhaulUI.Place(_root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -168f), new Vector2(252f, 128f));
            var plate = OverhaulUI.PanelImage("Plate", _root, OverhaulUI.Panel, 14);
            OverhaulUI.Stretch(plate.rectTransform);
            var title = OverhaulUI.Label("Title", _root, "FIELD ASSESSMENT", 12f, OverhaulUI.Accent, TextAlignmentOptions.MidlineLeft, FontWeight.SemiBold, false, 2.5f);
            OverhaulUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -10f), new Vector2(220f, 20f));

            string[] ids = { "repair", "dismantle", "freedom" };
            string[] names = { "Repair", "Dismantle", "Freedom" };
            Color[] cols = { OverhaulPalette.RoleFixer, OverhaulPalette.RoleBreaker, OverhaulPalette.RoleWanderer };
            for (int i = 0; i < 3; i++)
            {
                var r = new Row { id = ids[i], color = cols[i], pips = new Image[StoryStats.TaskGoal] };
                float y = -40f - i * 28f;
                r.dot = OverhaulUI.Image("Dot", _root, OverhaulUI.SoftCircle, cols[i]);
                OverhaulUI.Place(r.dot.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(24f, y), new Vector2(16f, 16f));
                r.label = OverhaulUI.Label("Label", _root, names[i], 15f, OverhaulUI.Text, TextAlignmentOptions.MidlineLeft, FontWeight.Medium);
                OverhaulUI.Place(r.label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(38f, y), new Vector2(100f, 22f));
                for (int k = 0; k < r.pips.Length; k++)
                {
                    r.pips[k] = OverhaulUI.Image("Pip", _root, GameHUD.CircleSprite, new Color(1f, 1f, 1f, 0.15f));
                    OverhaulUI.Place(r.pips[k].rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(146f + k * 18f, y), new Vector2(11f, 11f));
                }
                r.count = OverhaulUI.Label("Count", _root, "0/4", 13f, OverhaulUI.Dim, TextAlignmentOptions.MidlineRight, FontWeight.Regular);
                OverhaulUI.Place(r.count.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-14f, y), new Vector2(44f, 22f));
                _rows[i] = r;
            }
        }

        void OnEnable() => GameSignals.AssessmentProgress += OnProgress;
        void OnDisable() => GameSignals.AssessmentProgress -= OnProgress;

        void OnProgress(string task, int count)
        {
            foreach (var r in _rows) if (r != null && r.id == task) r.pop = 1f;
            _headerPop = 1f;
        }

        void Update()
        {
            if (_group == null) return;
            float dt = Time.unscaledDeltaTime;
            var gfm = GameFlowManager.Instance;
            var stats = StoryStats.Instance;
            bool show = stats != null && gfm != null && gfm.MycariObey &&
                        gfm.Phase != GamePhase.OutroVideo && gfm.Phase != GamePhase.End && gfm.Phase != GamePhase.FinalReport;
            _shown = Mathf.MoveTowards(_shown, show ? 1f : 0f, dt * 2.5f);
            _group.alpha = _shown;
            if (stats == null || _shown <= 0f) return;

            int[] counts = { stats.Repairs, stats.Dismantles, stats.Freedom };
            for (int i = 0; i < 3; i++)
            {
                var r = _rows[i];
                int c = counts[i];
                if (c != r.shown) { r.shown = c; r.count.text = c >= StoryStats.TaskGoal ? "done" : $"{c}/{StoryStats.TaskGoal}"; }
                r.count.color = c >= StoryStats.TaskGoal ? r.color : OverhaulUI.Dim;
                for (int k = 0; k < r.pips.Length; k++)
                {
                    bool on = k < c;
                    bool newest = on && k == Mathf.Min(c, StoryStats.TaskGoal) - 1;
                    float s = 11f * (1f + (newest ? 0.9f * r.pop : 0f));
                    r.pips[k].rectTransform.sizeDelta = new Vector2(s, s);
                    r.pips[k].color = on ? Color.Lerp(r.color, Color.white, newest ? r.pop * 0.6f : 0f) : new Color(1f, 1f, 1f, 0.15f);
                }
                r.dot.rectTransform.localScale = Vector3.one * (1f + 0.6f * r.pop);
                r.pop = Mathf.MoveTowards(r.pop, 0f, dt * 1.8f);
            }
            _root.localScale = Vector3.one * (1f + 0.04f * _headerPop);
            _headerPop = Mathf.MoveTowards(_headerPop, 0f, dt * 2f);
        }
    }
}
