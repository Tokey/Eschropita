using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Spore attunement (the "trip"). The Mycari and their grove breathe spores; standing near them is consensual
    /// exposure (design doc: "spores to be consensually taken up"). Two things come out of this module:
    ///   - a low ambient intensity, so the world always stays clearly visible: a faint shimmer near a Mycari, a
    ///     little more inside the mushroom grove;
    ///   - two short, strong event flashes that only the mushroom song triggers, each with its own look and sound:
    ///     <see cref="HitPulse"/> when you hit the right mushroom (warm, harmonious, a ring of light from the pad;
    ///     a solved song blooms longest) and <see cref="MissPulse"/> when you miss (cold, dissonant, a shiver).
    /// It also runs the attunement mechanic: linger patiently (walk slowly) beside a Mycari and you perceive its
    /// true role colour without Daffodil's scanner (patience reveals; the mushroom song persuades).
    /// Focus: while the mushrooms sing and you answer them, the spores settle (the trip eases off, a little more
    /// when you stand still and listen) so the notes are easy to follow; solving the song lets it bloom back.
    /// F7 cycles the effect strength (100 % / 50 % / off) for comfort; the choice is remembered.
    /// </summary>
    [OverhaulModule(15)]
    public class TripDirector : MonoBehaviour
    {
        public static TripDirector Instance { get; private set; }

        /// <summary>Final 0..1 intensity, after smoothing and the player's comfort setting.</summary>
        public static float Intensity => Instance != null ? Instance._intensity : 0f;
        /// <summary>0..1, the stronger of the two event flashes (for anything that just wants "a musical moment").</summary>
        public static float Pulse => Mathf.Max(HitPulse, MissPulse);
        /// <summary>0..1 flash from a right note (or a solved song, which lasts longer); decays in about a second.</summary>
        public static float HitPulse => Instance != null ? Instance._hit * Instance._userScale : 0f;
        /// <summary>0..1 flash from a wrong note; decays in under a second.</summary>
        public static float MissPulse => Instance != null ? Instance._miss * Instance._userScale : 0f;
        /// <summary>Seconds since the last hit (drives the ring of light spreading from the pad).</summary>
        public static float HitAge => Instance != null ? Time.unscaledTime - Instance._hitAt : 99f;
        /// <summary>0..1 the full-blown trip when a song is solved (a few seconds, fading).</summary>
        public static float SolvePeak => Instance != null ? Instance._solvePeak * Instance._userScale : 0f;
        /// <summary>0..1 the violent, sour version when a song fails.</summary>
        public static float FailPeak => Instance != null ? Instance._failPeak * Instance._userScale : 0f;
        /// <summary>0..1 how starved of oxygen the player is (life support under about a third).</summary>
        public static float Hypoxia => Instance != null ? Instance._hypoxia * Instance._userScale : 0f;
        /// <summary>0..1 a passing blur, as if Mars were claiming the eyesight (comes in spells while hypoxic).</summary>
        public static float Blur => Instance != null ? Instance._blur * Instance._userScale : 0f;
        /// <summary>Trip clock: advances faster the deeper the trip (drives every oscillation so they stay in sync).</summary>
        public static float Clock => Instance != null ? Instance._clock : 0f;
        /// <summary>True while the player is inside the grove (intense level).</summary>
        public static bool InGrove => Instance != null && Instance._inGrove;
        /// <summary>True while a mushroom melody is playing or awaiting an answer: colour drift is damped so note colours stay readable.</summary>
        public static bool SongActive => Instance != null && Instance._songActive;
        /// <summary>Colour the trip leans towards (nearest attuned Mycari's role, or the last mushroom note).</summary>
        public static Color Tint => Instance != null ? Instance._tint : Color.white;
        /// <summary>World position of the last musical pulse (a pad that sang, a recruited Mycari, …).</summary>
        public static Vector3 PulseSource => Instance != null ? Instance._pulseSource : Vector3.zero;
        /// <summary>Comfort scale 0..1 chosen with F7.</summary>
        public static float UserScale => Instance != null ? Instance._userScale : 1f;
        /// <summary>0..1 how settled the spores are while the mushroom song is on (already applied to <see cref="Intensity"/>).</summary>
        public static float Focus => Instance != null ? Instance._focus : 0f;

        [Header("Mycari proximity (faint)")]
        public float mycariFullDistance = 1.8f;
        public float mycariStartDistance = 9f;
        public float mycariMaxIntensity = 0.2f;
        public float extraMycariBonus = 0.03f;
        public float mycariCap = 0.26f;

        [Header("Mushroom grove (a little more)")]
        public float groveIntensity = 0.4f;
        public float groveApproachIntensity = 0.2f;
        public float groveApproachDistance = 7f;

        [Header("Song flashes")]
        [Tooltip("Seconds a right note's flash takes to fade.")]
        public float hitSeconds = 1.1f;
        [Tooltip("Seconds a solved song's bloom takes to fade.")]
        public float solvedSeconds = 2.4f;
        [Tooltip("Seconds a wrong note's shiver takes to fade.")]
        public float missSeconds = 0.85f;
        [Tooltip("Seconds the full-blown trip lasts after a solved song.")]
        public float solvePeakSeconds = 4.5f;
        [Tooltip("Seconds the sour, violent trip lasts after a failed song.")]
        public float failPeakSeconds = 2.8f;

        [Header("Low oxygen")]
        [Tooltip("Below this life support (0-1) the air gets thin: the trip rises, sight blurs in spells, the camera sways.")]
        [Range(0f, 1f)] public float hypoxiaBelow = 0.35f;
        [Tooltip("Ambient trip added at empty life support.")]
        public float hypoxiaTrip = 0.45f;

        [Header("Focus while doing the mushroom puzzle")]
        [Tooltip("How much the trip settles while the mushrooms sing and you answer them (0 = unchanged, 1 = gone). " +
                 "It blooms back to full strength when the song is solved.")]
        [Range(0f, 1f)] public float puzzleCalm = 0.4f;
        [Tooltip("Extra settling while you stand still: watching the demo, or at a pad on your turn.")]
        [Range(0f, 1f)] public float stillnessCalm = 0.15f;
        [Tooltip("Seconds for the spores to settle once the song starts (and to deepen when you stop moving).")]
        public float settleSeconds = 1.2f;
        [Tooltip("Seconds for the full trip to bloom back after the song is solved (or when you leave the grove).")]
        public float bloomBackSeconds = 0.8f;

        [Header("Smoothing")]
        public float riseRate = 0.9f;
        public float fallRate = 0.35f;

        [Header("Attunement (perceive a Mycari's role)")]
        public float attuneDistance = 4.5f;
        public float attuneSeconds = 2.4f;
        public float patientSpeed = 2.6f;

        const string PrefKey = "Eschropita.TripStrength";
        static readonly float[] Strengths = { 1f, 0.5f, 0f };

        float _intensity, _raw, _clock, _userScale = 1f, _focus, _groveNear;
        float _hit, _miss, _hitFade = 1f, _hitAt = -99f;
        float _solvePeak, _failPeak, _hypoxia, _blur, _blurTarget, _nextBlur, _blurEnd;
        bool _inGrove, _songActive;
        Color _tint = Color.white;
        Color _noteTint = OverhaulPalette.SporeCyan;
        Vector3 _pulseSource;
        float _groveRadius = 6f;
        Collider _groveCollider;
        Vector3 _lastPlayerPos;
        float _playerSpeed;
        readonly Dictionary<NPCFlocker, float> _attune = new Dictionary<NPCFlocker, float>();
        readonly HashSet<NPCFlocker> _attuned = new HashSet<NPCFlocker>();
        readonly List<NPCFlocker> _scratch = new List<NPCFlocker>();

        /// <summary>True once the player has perceived this Mycari's role through attunement.</summary>
        public static bool IsAttuned(NPCFlocker n) => Instance != null && n != null && Instance._attuned.Contains(n);
        /// <summary>0..1 attunement progress for a Mycari (for UI / aura build-up).</summary>
        public static float AttuneProgress(NPCFlocker n)
        {
            if (Instance == null || n == null) return 0f;
            if (Instance._attuned.Contains(n)) return 1f;
            return Instance._attune.TryGetValue(n, out float p) ? p : 0f;
        }

        void Awake()
        {
            Instance = this;
            int idx = 0;
            try { idx = Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, Strengths.Length - 1); } catch { }
            _userScale = Strengths[idx];
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnEnable()
        {
            GameSignals.PadSang += OnPadSang;
            GameSignals.PadPressed += OnPadPressed;
            GameSignals.PuzzleSolved += OnSolved;
            GameSignals.PuzzleFailed += OnFailed;
            GameSignals.MycariRecruited += OnRecruited;
            // Mycari you have sung with trust you: they accept any command (the role table only applies to strangers).
            OverhaulHooks.AcceptOverride = SungWithTrust;
        }

        void OnDisable()
        {
            GameSignals.PadSang -= OnPadSang;
            GameSignals.PadPressed -= OnPadPressed;
            GameSignals.PuzzleSolved -= OnSolved;
            GameSignals.PuzzleFailed -= OnFailed;
            GameSignals.MycariRecruited -= OnRecruited;
            if (OverhaulHooks.AcceptOverride == SungWithTrust) OverhaulHooks.AcceptOverride = null;
        }

        static bool? SungWithTrust(NPCFlocker npc, TaskCommand cmd)
        {
            // FollowApproved is the state the mushroom song puts a following Mycari in.
            if (npc != null && npc.state == NPCState.FollowApproved) return true;
            return null;
        }

        // The demo's notes only ripple faintly: the big flashes belong to the player's own answers.
        void OnPadSang(int pad, Vector3 pos) { _noteTint = NoteColor(pad); Hit(0.22f, pos, hitSeconds); }
        void OnPadPressed(int pad, Vector3 pos, bool ok)
        {
            if (ok) { _noteTint = NoteColor(pad); Hit(0.7f, pos, hitSeconds); }
            else Miss(pos);
        }
        void OnSolved(Vector3 pos) { Hit(1f, pos, solvedSeconds); _solvePeak = 1f; _failPeak = 0f; }
        void OnFailed(Vector3 pos) { Miss(pos); _failPeak = 1f; _solvePeak = 0f; }
        void OnRecruited(NPCFlocker n) { if (n != null) Hit(0.25f, n.transform.position, hitSeconds); }

        void Hit(float amount, Vector3 at, float seconds)
        {
            if (amount >= _hit * 0.8f) { _hitAt = Time.unscaledTime; _pulseSource = at; }
            if (amount >= _hit) { _hit = amount; _hitFade = Mathf.Max(0.1f, seconds); }   // a faint note never cuts a bloom short
            _miss = 0f;
        }

        void Miss(Vector3 at)
        {
            _miss = 1f;
            _hit = 0f;
            _pulseSource = at;
        }

        Color NoteColor(int pad)
        {
            var puzzle = SignalBridge.Instance != null ? SignalBridge.Instance.Puzzle : null;
            if (puzzle != null && puzzle.pads != null && pad >= 0 && pad < puzzle.pads.Length && puzzle.pads[pad] != null)
            {
                var mp = puzzle.pads[pad].GetComponent<MushroomPad>();
                if (mp != null) return mp.NoteColor;
            }
            return OverhaulPalette.SporeCyan;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7)) CycleStrength();

            float dt = Time.deltaTime;
            var bridge = SignalBridge.Instance;
            var player = bridge != null ? bridge.Player : null;
            var gfm = GameFlowManager.Instance;

            float target = 0f;
            NPCFlocker nearest = null;
            float nearestD = float.MaxValue;
            _inGrove = false;
            _groveNear = 0f;

            if (player != null)
            {
                Vector3 p = player.transform.position;
                Vector3 v = p - _lastPlayerPos; v.y = 0f;
                _lastPlayerPos = p;
                if (dt > 0f) _playerSpeed = Mathf.Lerp(_playerSpeed, v.magnitude / dt, 1f - Mathf.Exp(-dt * 8f));

                // --- Mycari proximity ---
                float best = 0f, extra = 0f;
                var mgr = NPCManager.Instance;
                if (mgr != null)
                {
                    var all = mgr.All;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var n = all[i];
                        if (n == null || !n.isActiveAndEnabled) continue;
                        Vector3 d = n.transform.position - p; d.y = 0f;
                        float dist = d.magnitude;
                        if (dist < nearestD) { nearestD = dist; nearest = n; }
                        float f = 1f - Smooth(mycariFullDistance, mycariStartDistance, dist);
                        if (f <= 0f) continue;
                        if (f > best) { extra += best; best = f; } else extra += f;
                    }
                }
                float mild = Mathf.Min(mycariCap, best * mycariMaxIntensity + Mathf.Min(extra, 3f) * extraMycariBonus);

                // --- Grove ---
                float grove = 0f;
                var trig = bridge.Grove;
                if (trig != null)
                {
                    if (_groveCollider == null)
                    {
                        _groveCollider = trig.GetComponent<Collider>();
                        if (_groveCollider != null)
                        {
                            Vector3 e = _groveCollider.bounds.extents;
                            _groveRadius = Mathf.Clamp(Mathf.Max(e.x, e.z), 3f, 14f);
                        }
                    }
                    Vector3 gd = trig.Center - p; gd.y = 0f;
                    float gdist = gd.magnitude;
                    _inGrove = trig.PlayerInside || gdist < _groveRadius * 0.85f;
                    _groveNear = _inGrove ? 1f : 1f - Smooth(_groveRadius, _groveRadius + groveApproachDistance, gdist);
                    grove = _inGrove ? groveIntensity : _groveNear * groveApproachIntensity;
                }

                target = Mathf.Max(mild, grove);
                UpdateAttunement(nearest, nearestD, dt);
            }

            // Scripted moments that need a clear head (videos, the report card) keep the trip out of the way.
            if (gfm != null)
            {
                var ph = gfm.Phase;
                if (ph == GamePhase.IntroVideo || ph == GamePhase.OutroVideo || ph == GamePhase.End) target = 0f;
                else if (ph == GamePhase.FinalReport) target = Mathf.Min(target, 0.3f);
            }

            var puzzle = bridge != null ? bridge.Puzzle : null;
            _songActive = puzzle != null && (puzzle.IsDemoRunning || puzzle.IsPlayerTurn);

            // Focus: the spores settle while the mushrooms sing and you answer (more if you stand still and listen),
            // so the notes stay easy to follow; once the song is solved the full trip blooms back.
            bool doingPuzzle = puzzle != null && !puzzle.IsPuzzleCompleted() && (puzzle.IsPuzzleActive() || puzzle.IsDemoRunning);
            float focusTarget = 0f;
            if (doingPuzzle)
            {
                float still = 1f - Smooth(0.6f, 2.6f, _playerSpeed);
                focusTarget = Mathf.Clamp01(puzzleCalm + stillnessCalm * still) * _groveNear;
            }
            float focusSeconds = Mathf.Max(0.05f, focusTarget > _focus ? settleSeconds : bloomBackSeconds);
            _focus = Mathf.Lerp(_focus, focusTarget, 1f - Mathf.Exp(-dt * 3f / focusSeconds));

            target = Mathf.Clamp01(target + _hypoxia * hypoxiaTrip);   // thin air (UpdateHypoxia, last frame's value)
            float rate = target > _raw ? riseRate : fallRate;
            _raw = Mathf.MoveTowards(_raw, target, rate * dt);
            float udt = Time.unscaledDeltaTime;   // flashes keep their timing through hit-stops
            _hit = Mathf.MoveTowards(_hit, 0f, udt / _hitFade);
            _miss = Mathf.MoveTowards(_miss, 0f, udt / Mathf.Max(0.1f, missSeconds));
            _solvePeak = Mathf.MoveTowards(_solvePeak, 0f, udt / Mathf.Max(0.1f, solvePeakSeconds));
            _failPeak = Mathf.MoveTowards(_failPeak, 0f, udt / Mathf.Max(0.1f, failPeakSeconds));
            UpdateHypoxia(udt);
            _intensity = Mathf.Clamp01(_raw * (1f - _focus) * _userScale);
            _clock += dt * (0.35f + 1.1f * _intensity);

            // Tint: an attuned nearby Mycari's role colour, else the grove's last note, else iridescent drift.
            Color want;
            if (nearest != null && nearestD < mycariStartDistance && _attuned.Contains(nearest)) want = OverhaulPalette.ForRole(nearest.role);
            else if (_inGrove) want = _noteTint;
            else want = Color.HSVToRGB(Mathf.Repeat(_clock * 0.03f, 1f), 0.55f, 1f);
            _tint = Color.Lerp(_tint, want, 1f - Mathf.Exp(-dt * 2f));
        }

        /// <summary>Thin air: below hypoxiaBelow the trip rises and the eyesight blurs in spells, more often the lower it gets.</summary>
        void UpdateHypoxia(float dt)
        {
            var life = LifeSupport.Instance;
            var gfm = GameFlowManager.Instance;
            bool live = life != null && life.Active && (gfm == null || gfm.PlayerControlEnabled);
            float want = live ? Mathf.Clamp01((hypoxiaBelow - life.Value01) / Mathf.Max(0.01f, hypoxiaBelow)) : 0f;
            _hypoxia = Mathf.MoveTowards(_hypoxia, want, dt * 0.25f);

            float now = Time.unscaledTime;
            if (_hypoxia > 0.05f)
            {
                if (now >= _nextBlur)
                {
                    float gap = Mathf.Lerp(14f, 4.5f, _hypoxia);
                    _blurEnd = now + Random.Range(1.2f, 2.4f) + 1.5f * _hypoxia;
                    _nextBlur = _blurEnd + gap * Random.Range(0.7f, 1.3f);
                    _blurTarget = Mathf.Lerp(0.45f, 1f, _hypoxia);
                }
                if (now > _blurEnd) _blurTarget = 0f;
            }
            else { _blurTarget = 0f; _nextBlur = now + 3f; }
            _blur = Mathf.MoveTowards(_blur, _blurTarget, dt * (_blurTarget > _blur ? 0.9f : 0.6f));
        }

        void UpdateAttunement(NPCFlocker nearest, float dist, float dt)
        {
            // Progress builds only for the nearest Mycari while you are close and unhurried; it fades if you leave.
            _scratch.Clear();
            foreach (var kv in _attune) _scratch.Add(kv.Key);
            foreach (var n in _scratch)
            {
                if (n == null) { _attune.Remove(n); continue; }
                if (n != nearest) _attune[n] = Mathf.Max(0f, _attune[n] - dt / (attuneSeconds * 2f));
            }
            if (nearest == null || _attuned.Contains(nearest)) return;
            if (dist > attuneDistance) return;
            bool patient = _playerSpeed < patientSpeed;
            _attune.TryGetValue(nearest, out float prog);
            prog = patient ? prog + dt / attuneSeconds : Mathf.Max(0f, prog - dt / attuneSeconds);
            _attune[nearest] = prog;
            if (prog >= 1f)
            {
                _attuned.Add(nearest);
                _attune.Remove(nearest);
                if (nearest.GetComponent<MycariAura>() == null) nearest.gameObject.AddComponent<MycariAura>();
                Hit(0.5f, nearest.transform.position, hitSeconds);
                GameSignals.RaiseMycariAttuned(nearest);
            }
        }

        void CycleStrength()
        {
            int idx = 0;
            for (int i = 0; i < Strengths.Length; i++) if (Mathf.Approximately(Strengths[i], _userScale)) idx = i;
            idx = (idx + 1) % Strengths.Length;
            _userScale = Strengths[idx];
            try { PlayerPrefs.SetInt(PrefKey, idx); } catch { }
            var hud = GameHUD.Instance;
            if (hud != null) hud.Toast(_userScale <= 0f ? "Spore visuals: off (F7)" : $"Spore visuals: {_userScale * 100f:0}% (F7)", 2f);
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
