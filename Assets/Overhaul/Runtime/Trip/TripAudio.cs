using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Wires the spore trip into the sound: puts <see cref="TripAudioFilter"/>, a reverb and a low-pass filter on the
    /// AudioListener (they process the whole mix, in that order) and drives them from <see cref="TripDirector"/>,
    /// and plays a shimmering pad (the library's "amb.trip" cue, or a synthesized one) that rises with the trip.
    /// Filters are bypassed while the trip is off, so the normal mix is untouched.
    /// </summary>
    [OverhaulModule(17)]
    public class TripAudio : MonoBehaviour
    {
        public float padVolume = 0.34f;

        AudioListener _listener;
        TripAudioFilter _dsp;
        AudioReverbFilter _reverb;
        AudioLowPassFilter _lowpass;
        AudioSource _pad;
        float _nextScan;

        void Start()
        {
            var go = new GameObject("Trip Pad");
            go.transform.SetParent(transform, false);
            _pad = go.AddComponent<AudioSource>();
            _pad.playOnAwake = false;
            _pad.loop = true;
            _pad.spatialBlend = 0f;
            _pad.volume = 0f;
            _pad.priority = 40;
            _pad.clip = AudioCatalog.TryClip("amb.trip", out var clip) ? clip : SynthPad();
        }

        void Update()
        {
            if (_listener == null || !_listener.isActiveAndEnabled)
            {
                if (Time.unscaledTime >= _nextScan)
                {
                    _nextScan = Time.unscaledTime + 1f;
                    Attach(FindAnyObjectByType<AudioListener>());
                }
            }

            float I = TripDirector.Intensity;
            float deep = TripDirector.InGrove ? Mathf.Clamp01((I - 0.45f) / 0.55f) : 0f;
            float t = TripDirector.Clock;
            bool song = TripDirector.SongActive;

            if (_dsp != null) _dsp.SetIntensity(song ? I * 0.55f : I, song ? deep * 0.4f : deep);

            if (_reverb != null)
            {
                bool on = I > 0.02f;
                if (_reverb.enabled != on) _reverb.enabled = on;
                if (on)
                {
                    float e = Mathf.SmoothStep(0f, 1f, I);
                    _reverb.dryLevel = 0f;
                    _reverb.room = Mathf.Lerp(-10000f, -650f, e);
                    _reverb.roomHF = -900f;
                    _reverb.decayTime = 1.4f + 4.2f * I;
                    _reverb.decayHFRatio = 0.55f;
                    _reverb.reflectionsLevel = Mathf.Lerp(-10000f, -1400f, e);
                    _reverb.reverbLevel = Mathf.Lerp(-10000f, song ? -900f : -250f, e);
                    _reverb.diffusion = 100f;
                    _reverb.density = 100f;
                }
            }

            if (_lowpass != null)
            {
                bool on = deep > 0.01f;
                if (_lowpass.enabled != on) _lowpass.enabled = on;
                if (on)
                {
                    float sweep = 2800f + 7500f * (0.5f + 0.5f * Mathf.Sin(t * 0.21f));
                    if (song) sweep = Mathf.Max(sweep, 7000f);   // the melody must stay clear
                    _lowpass.cutoffFrequency = Mathf.Lerp(22000f, sweep, deep * 0.85f);
                    _lowpass.lowpassResonanceQ = 1f + 0.7f * deep;
                }
            }

            if (_pad != null && _pad.clip != null)
            {
                float v = padVolume * Mathf.Pow(I, 1.4f) * (song ? 0.45f : 1f);
                _pad.volume = Mathf.MoveTowards(_pad.volume, v, Time.deltaTime * 0.6f);
                _pad.pitch = 1f + 0.012f * Mathf.Sin(t * 0.3f);
                if (_pad.volume > 0.001f && !_pad.isPlaying) _pad.Play();
                else if (_pad.volume <= 0.001f && _pad.isPlaying) _pad.Stop();
            }
        }

        void Attach(AudioListener l)
        {
            if (l == null || l == _listener) return;
            _listener = l;
            var go = l.gameObject;
            _dsp = go.GetComponent<TripAudioFilter>() ?? go.AddComponent<TripAudioFilter>();
            _reverb = go.GetComponent<AudioReverbFilter>() ?? go.AddComponent<AudioReverbFilter>();
            _reverb.reverbPreset = AudioReverbPreset.User;
            _reverb.enabled = false;
            _lowpass = go.GetComponent<AudioLowPassFilter>() ?? go.AddComponent<AudioLowPassFilter>();
            _lowpass.cutoffFrequency = 22000f;
            _lowpass.enabled = false;
        }

        /// <summary>
        /// A seamless 12 s shimmer in F major add9: slowly beating sine partials whose amplitudes swell in turn.
        /// Every frequency is rounded to whole cycles per loop so the loop point is inaudible.
        /// </summary>
        static AudioClip SynthPad()
        {
            const int rate = 44100;
            const float len = 12f;
            int n = (int)(rate * len);
            var data = new float[n * 2];
            float[] notes = { 174.61f, 220f, 261.63f, 392f, 523.25f, 659.25f, 783.99f, 1046.5f };
            for (int k = 0; k < notes.Length; k++)
            {
                double f = System.Math.Round(notes[k] * len) / len;
                double beat = (k % 3 + 1) / (double)len;           // detuned twin → slow beating
                double swell = (k % 4 + 1) / (double)len;          // amplitude cycle
                float amp = 0.11f / (1f + k * 0.35f);
                double ph = k * 1.7;
                for (int i = 0; i < n; i++)
                {
                    double tt = i / (double)rate;
                    float a = amp * (0.55f + 0.45f * (float)System.Math.Sin(2 * System.Math.PI * swell * tt + ph));
                    float l = (float)System.Math.Sin(2 * System.Math.PI * f * tt + ph);
                    float r = (float)System.Math.Sin(2 * System.Math.PI * (f + beat) * tt + ph * 1.3);
                    data[i * 2] += a * l;
                    data[i * 2 + 1] += a * r;
                }
            }
            var clip = AudioClip.Create("TripShimmer (synth)", n, 2, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
