using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Difficulty tuning, applied at runtime so the scene's values stay untouched (disable this component to get the
    /// original numbers back). Goals: tense but fair, and a melody game that grows with the player.
    ///  - Life support: slower unpowered drain (≈50 s instead of ≈30 s from full after the first tornado strike),
    ///    an emergency reserve that halves the drain below 25 % so the alarm gives you time to react, and a
    ///    quicker recovery once the windmill turns again.
    ///  - Mushroom songs: after the tutorial, melodies start at 4 notes and grow by one every two songs you answer
    ///    (to 6); two misses in a row shorten the next song by one (rubber-banding, as the design doc suggests).
    ///  - Tornadoes chip the windmill a little more gently.
    /// </summary>
    [OverhaulModule(5)]
    public class GameBalance : MonoBehaviour
    {
        [Header("Life support")]
        public float drainUnpowered = 4f;
        public float regenPowered = 3f;
        [Range(0f, 1f)] public float emergencyBelow = 0.25f;
        [Range(0.1f, 1f)] public float emergencyDrainScale = 0.5f;

        [Header("Mushroom songs (after the tutorial)")]
        public int startLength = 4;
        public int maxLength = 6;
        public int solvesPerStep = 2;

        [Header("Tornado")]
        public float tornadoSiteDamagePerSecond = 4f;

        int _solves, _failStreak;
        bool _emergency, _tornadoTuned;

        void OnEnable()
        {
            GameSignals.PuzzleSolved += OnSolved;
            GameSignals.PuzzleFailed += OnFailed;
        }

        void OnDisable()
        {
            GameSignals.PuzzleSolved -= OnSolved;
            GameSignals.PuzzleFailed -= OnFailed;
        }

        bool PastTutorial
        {
            get
            {
                var gfm = GameFlowManager.Instance;
                return gfm == null || gfm.Phase >= GamePhase.MycariRepairs;
            }
        }

        void OnSolved(Vector3 p)
        {
            if (!PastTutorial) return;
            _solves++;
            _failStreak = 0;
            ApplyMelodyLength();
        }

        void OnFailed(Vector3 p)
        {
            if (!PastTutorial) return;
            _failStreak++;
            if (_failStreak >= 2) ApplyMelodyLength();
        }

        void ApplyMelodyLength()
        {
            var bridge = SignalBridge.Instance;
            var puzzle = bridge != null ? bridge.Puzzle : null;
            if (puzzle == null) return;
            int len = startLength + _solves / Mathf.Max(1, solvesPerStep);
            if (_failStreak >= 2) { len -= 1; _solves = Mathf.Max(0, _solves - solvesPerStep); _failStreak = 0; }
            len = Mathf.Clamp(len, 3, Mathf.Max(3, maxLength));
            puzzle.SetSequenceLength(len, len);
        }

        void Update()
        {
            var life = LifeSupport.Instance;
            if (life != null)
            {
                bool emergency = life.Active && life.Value01 < emergencyBelow;
                life.drainPerSecondUnpowered = drainUnpowered * (emergency ? emergencyDrainScale : 1f);
                life.regenPerSecondPowered = regenPowered;
                if (emergency && !_emergency)
                {
                    var hud = GameHUD.Instance;
                    if (hud != null) hud.Toast("Emergency O<sub>2</sub> reserve: drain slowed", 3f);
                }
                _emergency = emergency;
            }

            if (!_tornadoTuned)
            {
                var bridge = SignalBridge.Instance;
                if (bridge != null && bridge.Tornado != null)
                {
                    bridge.Tornado.siteDamagePerSecond = tornadoSiteDamagePerSecond;
                    _tornadoTuned = true;
                }
            }

            // Once the player is past the tutorial, take over the melody length (the flow set it for the demo).
            if (PastTutorial && _solves == 0 && _failStreak == 0 && !_initialLengthSet)
            {
                ApplyMelodyLength();
                _initialLengthSet = true;
            }
        }

        bool _initialLengthSet;
    }
}
