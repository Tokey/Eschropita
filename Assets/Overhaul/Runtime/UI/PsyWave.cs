using TMPro;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// A slow, gentle wave through a display title's letters (a few percent of the font size), a little livelier while
    /// the spores are strong. Only big titles get it, so nothing you have to read ever moves.
    /// </summary>
    [DisallowMultipleComponent]
    public class PsyWave : MonoBehaviour
    {
        [Tooltip("Height of the wave as a fraction of the font size.")]
        public float amplitude = 0.035f;
        [Tooltip("Radians per second.")]
        public float speed = 1.8f;
        [Tooltip("Phase step from one letter to the next (radians).")]
        public float spread = 0.55f;

        TMP_Text _t;
        TMP_MeshInfo[] _rest;   // the letters' resting vertices, re-read whenever the text regenerates
        bool _dirty = true, _regenerating;

        void Awake() => _t = GetComponent<TMP_Text>();

        void OnEnable()
        {
            _dirty = true;
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            if (_t != null && _t.isActiveAndEnabled) _t.ForceMeshUpdate();   // put the letters back on the line
        }

        void OnDestroy()
        {
            if (_t != null && _t.isActiveAndEnabled) _t.ForceMeshUpdate();
        }

        void OnTextChanged(Object o)
        {
            if (!_regenerating && o == _t) _dirty = true;
        }

        void LateUpdate()
        {
            if (_t == null || !_t.isActiveAndEnabled) return;
            if (_dirty || _rest == null)
            {
                _regenerating = true;
                _t.ForceMeshUpdate();
                _regenerating = false;
                _rest = _t.textInfo.CopyMeshInfoVertexData();
                _dirty = false;
            }

            var info = _t.textInfo;
            if (info.meshInfo.Length != _rest.Length) { _dirty = true; return; }
            float amp = amplitude * _t.fontSize * (1f + 0.6f * TripDirector.Intensity);
            float time = Time.unscaledTime * speed;
            for (int i = 0; i < info.characterCount; i++)
            {
                var ch = info.characterInfo[i];
                if (!ch.isVisible) continue;
                int m = ch.materialReferenceIndex, v = ch.vertexIndex;
                var src = _rest[m].vertices;
                var dst = info.meshInfo[m].vertices;
                if (src == null || dst == null || v + 3 >= src.Length || v + 3 >= dst.Length) continue;
                var lift = new Vector3(0f, Mathf.Sin(time + i * spread) * amp, 0f);
                for (int k = 0; k < 4; k++) dst[v + k] = src[v + k] + lift;
            }
            _t.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }
    }
}
