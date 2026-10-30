using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// A short squash-and-stretch on the object's scale: taller and thinner, then a little squat, then back.
    /// Restores the exact original scale when done (and if disabled mid-way). See <see cref="CameraJuice.Punch"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScalePunch : MonoBehaviour
    {
        Vector3 _base;
        float _amount, _seconds, _t = -1f;
        bool _hasBase;

        public void Play(float amount, float seconds)
        {
            if (!_hasBase || _t < 0f) { _base = transform.localScale; _hasBase = true; }
            _amount = amount;
            _seconds = Mathf.Max(0.05f, seconds);
            _t = 0f;
            enabled = true;
        }

        void LateUpdate()
        {
            if (_t < 0f) { enabled = false; return; }
            _t += Time.deltaTime / _seconds;
            if (_t >= 1f)
            {
                transform.localScale = _base;
                _t = -1f;
                enabled = false;
                return;
            }
            // damped oscillation: stretch, squash, settle
            float s = Mathf.Sin(_t * Mathf.PI * 2.5f) * (1f - _t) * _amount;
            transform.localScale = new Vector3(_base.x * (1f - s * 0.6f), _base.y * (1f + s), _base.z * (1f - s * 0.6f));
        }

        void OnDisable()
        {
            if (_t >= 0f && _hasBase) transform.localScale = _base;
            _t = -1f;
        }
    }
}
