using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The spore trip's sound: a DSP effect on the AudioListener, so it bends the whole mix.
    ///  - "wow": a slowly swaying delay that makes every pitch float a couple of percent sharp and flat
    ///  - chorus / flanger: short swept delays per ear with feedback that deepens in the grove
    ///  - slow auto-pan
    /// Wet amount and depth come from the main thread (<see cref="SetIntensity"/>) and are smoothed per sample on
    /// the audio thread. At zero intensity the audio passes through untouched.
    /// </summary>
    public class TripAudioFilter : MonoBehaviour
    {
        volatile float _targetWet;
        volatile float _targetDeep;

        float _wet, _deep;
        float[] _bufL, _bufR;
        int _mask, _w;
        double _phWow, _phCh, _phPan, _phWow2;
        float _sr = 48000f;
        float _fbL, _fbR;

        /// <summary>wet: overall trip (0..1); deep: grove level (0..1).</summary>
        public void SetIntensity(float wet, float deep)
        {
            _targetWet = Mathf.Clamp01(wet);
            _targetDeep = Mathf.Clamp01(deep);
        }

        void Awake()
        {
            _sr = Mathf.Max(8000, AudioSettings.outputSampleRate);
            int need = Mathf.CeilToInt(_sr * 0.06f);
            int size = 1;
            while (size < need) size <<= 1;
            _bufL = new float[size];
            _bufR = new float[size];
            _mask = size - 1;
        }

        float Read(float[] buf, float delaySamples)
        {
            float pos = _w - delaySamples;
            int i0 = Mathf.FloorToInt(pos);
            float f = pos - i0;
            float a = buf[i0 & _mask];
            float b = buf[(i0 + 1) & _mask];
            return a + (b - a) * f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_bufL == null || channels <= 0) return;
            float targetWet = _targetWet, targetDeep = _targetDeep;
            if (_wet < 1e-4f && targetWet < 1e-4f)
            {
                // Keep the delay lines fed so the effect fades in without a burst of stale audio.
                int frames0 = data.Length / channels;
                for (int i = 0; i < frames0; i++)
                {
                    _bufL[_w] = data[i * channels];
                    _bufR[_w] = channels > 1 ? data[i * channels + 1] : data[i * channels];
                    _w = (_w + 1) & _mask;
                }
                _wet = 0f;
                return;
            }

            float smooth = 1f / (0.08f * _sr);
            double twoPi = 2.0 * System.Math.PI;
            double incWow = twoPi * 0.47 / _sr, incWow2 = twoPi * 0.131 / _sr, incCh = twoPi * 0.23 / _sr, incPan = twoPi * 0.11 / _sr;
            int frames = data.Length / channels;
            for (int i = 0; i < frames; i++)
            {
                _wet += (targetWet - _wet) * smooth;
                _deep += (targetDeep - _deep) * smooth;
                int idx = i * channels;
                float inL = data[idx];
                float inR = channels > 1 ? data[idx + 1] : inL;

                float fb = 0.38f * _deep;
                _bufL[_w] = inL + _fbL * fb;
                _bufR[_w] = inR + _fbR * fb;

                float wowA = (float)System.Math.Sin(_phWow), wowB = (float)System.Math.Sin(_phWow2);
                float d1 = (0.013f + (0.006f + 0.004f * _deep) * (0.75f * wowA + 0.25f * wowB)) * _sr;
                float chA = (float)System.Math.Sin(_phCh), chB = (float)System.Math.Sin(_phCh + 1.5707963);
                float d2L = (0.0045f + 0.0032f * chA) * _sr;
                float d2R = (0.0045f + 0.0032f * chB) * _sr;

                float vL = Read(_bufL, d1);
                float vR = Read(_bufR, d1 + 0.0009f * _sr);
                float cL = Read(_bufL, d2L);
                float cR = Read(_bufR, d2R);
                _fbL = cL;
                _fbR = cR;

                float wet = _wet;
                float dry = 1f - 0.72f * wet;
                float pan = (float)System.Math.Sin(_phPan) * 0.28f * wet;
                float outL = (inL * dry + vL * 0.58f * wet + cL * (0.22f + 0.2f * _deep) * wet) * (1f - pan);
                float outR = (inR * dry + vR * 0.58f * wet + cR * (0.22f + 0.2f * _deep) * wet) * (1f + pan);

                data[idx] = outL;
                if (channels > 1) data[idx + 1] = outR;

                _w = (_w + 1) & _mask;
                _phWow += incWow; _phWow2 += incWow2; _phCh += incCh; _phPan += incPan;
            }
            if (_phWow > twoPi) _phWow -= twoPi;
            if (_phWow2 > twoPi) _phWow2 -= twoPi;
            if (_phCh > twoPi) _phCh -= twoPi;
            if (_phPan > twoPi) _phPan -= twoPi;
        }
    }
}
