using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Places the six log terminals around the JoyTech compound at runtime, each unlocking at its story moment:
    ///   Danu's radio message — beside Escropita's shuttle (from the start)
    ///   JoyTech welcome brochure — at the dome door (from the start)
    ///   Aniya Freeman, Log 240 — near the windmill, once the tornado has struck it (doc: "a journal log near the windmill")
    ///   Vera Sterling's message — at the obelisk, once it is destroyed (doc: found when the killswitch is turned off)
    ///   Hargrave's logs + the surveillance dossier — at the bunker under the obelisk (doc: the billionaire's logs)
    /// Shows an "E  Read" prompt over the nearest available terminal, and after the player first reads about the
    /// surveillance, Escropita and Daffodil talk about being observed (doc: her discomfort with tech mediation).
    /// </summary>
    [OverhaulModule(70)]
    public class LoreTerminals : MonoBehaviour
    {
        readonly List<LoreTerminal> _terminals = new List<LoreTerminal>();
        bool _placed, _talkedAboutSurveillance, _pendingTalk;
        Canvas _canvas;
        RectTransform _prompt;
        TextMeshProUGUI _promptText;
        CanvasGroup _promptGroup;

        void OnEnable() => GameSignals.LoreClosed += OnClosed;
        void OnDisable() => GameSignals.LoreClosed -= OnClosed;

        void OnClosed(string id)
        {
            if (!_talkedAboutSurveillance && (id == "vera_sterling" || id == "surveillance_dossier")) _pendingTalk = true;
        }

        void Update()
        {
            if (!_placed) TryPlace();
            UpdatePrompt();
            if (_pendingTalk && !LoreReader.IsOpen)
            {
                var dlg = DialogueSystem.Instance;
                if (dlg != null && !dlg.IsPlaying())
                {
                    _pendingTalk = false;
                    _talkedAboutSurveillance = true;
                    dlg.Play(SurveillanceTalk());
                }
            }
        }

        static GamePhase Phase => GameFlowManager.Instance != null ? GameFlowManager.Instance.Phase : GamePhase.Sandbox;

        void TryPlace()
        {
            OverhaulLandmarks.Resolve();
            if (OverhaulLandmarks.Spawn == Vector3.zero && OverhaulLandmarks.Windmill == Vector3.zero) return;
            // Wait a moment for the level dressing to report the dome / shuttle / plant positions.
            if (!OverhaulLandmarks.HasDressing && Time.timeSinceLevelLoad < 3f) return;
            _placed = true;

            var root = new GameObject("Lore Terminals").transform;
            root.SetParent(transform, false);
            _terminals.AddRange(Place(root));
        }

        /// <summary>
        /// Puts the six terminals around the landmarks (resolved first, see <see cref="OverhaulLandmarks"/>). Also used
        /// by the editor's edit-mode preview.
        /// </summary>
        public static List<LoreTerminal> Place(Transform root)
        {
            var list = new List<LoreTerminal>();
            void Add(string id, Vector3 pos, Vector3 face, System.Func<bool> available)
            {
                if (LoreLibrary.Get(id) == null) return;
                list.Add(LoreTerminal.Create(root, id, OverhaulLandmarks.OnGround(pos), face, available));
            }

            Vector3 spawn = OverhaulLandmarks.Spawn, wind = OverhaulLandmarks.Windmill, ob = OverhaulLandmarks.Obelisk;
            Vector3 home = OverhaulLandmarks.Home, shuttle = OverhaulLandmarks.Shuttle, plant = OverhaulLandmarks.LifeSupportPlant;

            Vector3 toSpawn = Flat(spawn - wind);
            Vector3 aniyaPos = wind + toSpawn * 5.5f + Vector3.Cross(Vector3.up, toSpawn) * 2.5f;
            if (plant != Vector3.zero && (aniyaPos - plant).sqrMagnitude < 16f) aniyaPos -= Vector3.Cross(Vector3.up, toSpawn) * 5f;
            Add("aniya_log_240", aniyaPos, wind, () => Phase >= GamePhase.TornadoStrike);

            Vector3 obToSpawn = Flat(spawn - ob);
            Vector3 side = Vector3.Cross(Vector3.up, obToSpawn);
            Add("vera_sterling", ob + obToSpawn * 4.5f + side * 1.5f, ob, () =>
            {
                var o = SignalBridge.Instance != null ? SignalBridge.Instance.Obelisk : null;
                return Phase >= GamePhase.Bunker || (o != null && o.Stage == ObeliskStage.Destroyed);
            });
            Add("hargrave_logs", ob - obToSpawn * 3.8f + side * 3.2f, ob, () => Phase >= GamePhase.Bunker);
            Add("surveillance_dossier", ob - obToSpawn * 3.8f - side * 3.2f, ob, () => Phase >= GamePhase.Bunker);

            Vector3 doorOut = OverhaulLandmarks.DoorFacing;
            Vector3 doorSide = Vector3.Cross(Vector3.up, doorOut);
            Add("joytech_brochure", home + doorOut * 2.5f + doorSide * 3.2f, home + doorOut * 6f, null);

            Vector3 sh = shuttle != Vector3.zero ? shuttle : spawn + new Vector3(-7f, 0f, -2f);
            Vector3 shToSpawn = Flat(spawn - sh);
            Add("danu_radio", sh + shToSpawn * 3.6f + Vector3.Cross(Vector3.up, shToSpawn) * 1.6f, spawn, null);
            return list;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward; }

        void UpdatePrompt()
        {
            var bridge = SignalBridge.Instance;
            var player = bridge != null ? bridge.Player : null;
            LoreTerminal best = null;
            float bestD = float.MaxValue;
            if (player != null && !LoreReader.IsOpen)
            {
                foreach (var t in _terminals)
                {
                    if (t == null || !t.CanInteract) continue;
                    Vector3 d = t.transform.position - player.transform.position; d.y = 0f;
                    float dist = d.magnitude;
                    if (dist <= t.radius && dist < bestD) { bestD = dist; best = t; }
                }
            }
            if (_canvas == null)
            {
                if (best == null) return;
                _canvas = OverhaulUI.CreateCanvas("Lore Prompt", 47, transform);
                _prompt = OverhaulUI.Rect("Prompt", _canvas.transform);
                _prompt.sizeDelta = new Vector2(360f, 44f);
                _promptGroup = _prompt.gameObject.AddComponent<CanvasGroup>();
                var plate = OverhaulUI.PanelImage("Plate", _prompt, OverhaulUI.Panel, 12);
                OverhaulUI.Stretch(plate.rectTransform);
                _promptText = OverhaulUI.Label("Text", _prompt, "", 17f, OverhaulUI.Text, TextAlignmentOptions.Center, FontWeight.Medium);
                OverhaulUI.Stretch(_promptText.rectTransform, 6f);
            }
            float target = best != null ? 1f : 0f;
            _promptGroup.alpha = Mathf.MoveTowards(_promptGroup.alpha, target, Time.unscaledDeltaTime * 6f);
            if (best == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(best.transform.position + Vector3.up * 2.4f);
            if (sp.z < 0f) { _promptGroup.alpha = 0f; return; }
            var canvasRt = (RectTransform)_canvas.transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out Vector2 local))
                _prompt.anchoredPosition = local;
            _prompt.anchorMin = _prompt.anchorMax = new Vector2(0.5f, 0.5f);
            string accent = OverhaulUI.Hex(OverhaulUI.Accent);
            _promptText.text = $"<color=#{accent}><b>E</b></color>  Read — {best.Title}";
        }

        // Draft dialogue (new writing): Escropita is uneasy about observation; Daffodil is a cheerful funhouse mirror.
        static DialogueLine[] SurveillanceTalk()
        {
            return new[]
            {
                L("Eschropita", "They were watching the Mycari. Recording everything. Nobody asked."),
                L("Eschropita", "…Daffodil, are you recording me right now?"),
                L("Daffodil", "Continuously. It is my primary function. Would you like a transcript of you asking?"),
                L("Eschropita", "I'd like you to ask first. Next time."),
                L("Daffodil", "Noted. Asking: may I keep listening?"),
                L("Eschropita", "…Yes. But we do this differently than they did."),
            };
        }

        static DialogueLine L(string speaker, string text) => new DialogueLine { speaker = speaker, text = text };
    }
}
