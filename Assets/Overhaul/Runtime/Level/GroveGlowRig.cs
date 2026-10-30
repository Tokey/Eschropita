using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Makes the grove's mycelium and mushrooms answer the music: a slow breathing glow, a flash of each note's
    /// colour when a mushroom sings or is pressed, a brighter "listening" state on the player's turn, a violet
    /// hush on a miss and a big bloom when the song is solved. The spore trip makes everything glow more.
    /// </summary>
    public class GroveGlowRig : MonoBehaviour
    {
        public Material veins;
        public Material[] caps = new Material[0];
        Color[] _capBase;
        Color _flash = OverhaulPalette.SporeCyan;
        float _flashK, _bloom, _fail;

        void Start()
        {
            _capBase = new Color[caps.Length];
            for (int i = 0; i < caps.Length; i++)
                _capBase[i] = caps[i] != null && caps[i].HasProperty("_EmissiveColor") ? caps[i].GetColor("_EmissiveColor") : Color.white;
        }

        void OnEnable()
        {
            GameSignals.PadSang += OnNote;
            GameSignals.PadPressed += OnPress;
            GameSignals.PuzzleSolved += OnSolved;
            GameSignals.PuzzleFailed += OnFailed;
        }

        void OnDisable()
        {
            GameSignals.PadSang -= OnNote;
            GameSignals.PadPressed -= OnPress;
            GameSignals.PuzzleSolved -= OnSolved;
            GameSignals.PuzzleFailed -= OnFailed;
        }

        static Color Note(int i)
        {
            var b = SignalBridge.Instance;
            var pz = b != null ? b.Puzzle : null;
            if (pz != null && pz.pads != null && i >= 0 && i < pz.pads.Length && pz.pads[i] != null)
            {
                var mp = pz.pads[i].GetComponent<MushroomPad>();
                if (mp != null) return mp.NoteColor;
            }
            return OverhaulPalette.SporeCyan;
        }

        void OnNote(int i, Vector3 p) { _flash = Note(i); _flashK = 1f; }
        void OnPress(int i, Vector3 p, bool ok) { if (ok) { _flash = Note(i); _flashK = 0.8f; } }
        void OnSolved(Vector3 p) => _bloom = 1f;
        void OnFailed(Vector3 p) => _fail = 1f;

        void Update()
        {
            float dt = Time.deltaTime, t = Time.time;
            var b = SignalBridge.Instance;
            var pz = b != null ? b.Puzzle : null;
            bool listening = pz != null && pz.IsPlayerTurn;
            float trip = TripDirector.Intensity;
            _flashK = Mathf.MoveTowards(_flashK, 0f, dt * 1.6f);
            _bloom = Mathf.MoveTowards(_bloom, 0f, dt * 0.35f);
            _fail = Mathf.MoveTowards(_fail, 0f, dt * 1.5f);

            float breath = 0.5f + 0.5f * Mathf.Sin(t * 0.9f);
            float flicker = 1f - 0.55f * _fail;   // a smooth dim on a miss (no strobing)
            float baseI = (0.8f + 0.5f * breath + (listening ? 0.5f : 0f) + 1.2f * trip) * flicker;
            Color col = Color.Lerp(OverhaulPalette.SporeCyan, OverhaulPalette.SporeViolet, 0.5f + 0.5f * Mathf.Sin(t * 0.21f));
            col = Color.Lerp(col, _flash, _flashK);
            col = Color.Lerp(col, new Color(0.55f, 0.3f, 0.8f), _fail * 0.7f);
            if (veins != null)
            {
                veins.SetColor("_Color", col);
                veins.SetFloat("_Intensity", baseI + 3f * _flashK + 5f * _bloom);
            }
            for (int i = 0; i < caps.Length; i++)
            {
                if (caps[i] == null || _capBase == null || i >= _capBase.Length) continue;
                float k = 0.7f + 0.4f * Mathf.Sin(t * 1.3f + i * 2.1f) + 1.2f * _flashK + 2.5f * _bloom + 0.8f * trip;
                OverhaulMaterials.SetEmissive(caps[i], _capBase[i] * k * flicker);
            }
        }
    }
}
