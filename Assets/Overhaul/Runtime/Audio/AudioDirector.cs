using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The adaptive score and the mix. Music is built from the sound designer's layers (see Tools/SoundLibrary):
    /// each layer is a pair of AudioSources that crossfade-loop the clip, and every frame the director decides a
    /// target volume per layer from the game state, then glides towards it, so the music changes seamlessly:
    ///   exploration theme (+ slowly evolving calm-drone variations) · a 6-loop grove playlist · storm bed ·
    ///   obelisk space/wobble · bunker / lore introspection · report-card song · near-silence while the mushrooms
    ///   sing (so the melody can be heard), with a tension bass that rises as life support falls.
    /// Also: stingers that briefly duck the beds, ducking under dialogue, 3D ambience emitters at landmarks, a
    /// listener that rides with the player (the camera is ~25 m away), pooled one-shots for <see cref="Sfx"/>,
    /// and a gentle pitch wobble of the score during the spore trip.
    /// </summary>
    [OverhaulModule(10)]
    public class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }
        /// <summary>The listener the overhaul uses (rides with the player). Null until set up.</summary>
        public static AudioListener Listener { get; private set; }

        [Header("Bus levels")]
        [Range(0, 1)] public float master = 1f;
        [Range(0, 1)] public float musicBus = 0.7f;
        [Range(0, 1)] public float ambBus = 0.8f;
        [Range(0, 1)] public float sfxBus = 1f;
        [Range(0, 1)] public float uiBus = 0.8f;
        [Range(0, 1)] public float stingerBus = 0.85f;

        [Header("Score")]
        public float fadeInSeconds = 4f;
        public float fadeOutSeconds = 3f;
        public float loopCrossfade = 4f;
        public float duckUnderDialogue = 0.6f;

        // ---- layers ----------------------------------------------------------------------------------------
        class Layer
        {
            public string cue;
            public AudioClip clip;
            public AudioSource a, b;
            public bool aIsMain = true;
            public float vol, target, cueVolume = 1f;
            public float xf = -1f;      // 0..1 progress of the loop crossfade, -1 = none
            public bool tried, paused;
        }

        static readonly string[] LayerIds =
        {
            "music.explore", "music.layer.calm", "music.layer.tension", "music.layer.wonder", "music.layer.space",
            "music.layer.wobble", "music.storm", "music.thought", "music.report", "music.bunker",
            "music.grove.1", "music.grove.2", "music.grove.3", "music.grove.4", "music.grove.5", "music.grove.6",
        };

        readonly Dictionary<string, Layer> _layers = new Dictionary<string, Layer>();
        readonly Dictionary<string, float> _targets = new Dictionary<string, float>();
        Transform _musicRoot;

        // ---- state -------------------------------------------------------------------------------------------
        float _duck = 1f, _stingerDuck = 1f, _stingerDuckUntil;
        bool _lore, _reportCard, _bunkerSong;
        int _variation;
        float _nextVariationAt;
        int _groveTrack;
        float _groveTime;
        bool _wasInGrove;

        // ---- one-shots ---------------------------------------------------------------------------------------
        AudioSource _stinger;
        AudioSource[] _pool2D, _pool3D;
        int _next2D, _next3D;
        readonly Dictionary<string, float> _lastPlay = new Dictionary<string, float>();
        readonly Dictionary<string, List<AudioSource>> _voices = new Dictionary<string, List<AudioSource>>();
        readonly Dictionary<AudioSource, float> _baseVol = new Dictionary<AudioSource, float>();
        readonly Dictionary<AudioSource, string> _busOf = new Dictionary<AudioSource, string>();

        // ---- ambience & listener -----------------------------------------------------------------------------
        AudioSource _wind, _storm, _groveAmb, _plantAmb, _turbineAmb, _obeliskAmb, _bunkerAmb;
        AudioListener _cameraListener;
        Transform _listenerRig;
        float _nextListenerCheck;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_cameraListener != null) _cameraListener.enabled = true;
            if (_listenerRig != null) Destroy(_listenerRig.gameObject);
            Listener = null;
        }

        void OnEnable()
        {
            GameSignals.LoreOpened += OnLoreOpened;
            GameSignals.LoreClosed += OnLoreClosed;
            GameSignals.ReportCardShown += OnReportCard;
            OverhaulHooks.PlayBunkerSong = ClaimBunkerSong;
        }

        void OnDisable()
        {
            GameSignals.LoreOpened -= OnLoreOpened;
            GameSignals.LoreClosed -= OnLoreClosed;
            GameSignals.ReportCardShown -= OnReportCard;
            if (OverhaulHooks.PlayBunkerSong == ClaimBunkerSong) OverhaulHooks.PlayBunkerSong = null;
        }

        void OnLoreOpened(string id) => _lore = true;
        void OnLoreClosed(string id) => _lore = false;
        void OnReportCard(string grade) => _reportCard = true;

        bool ClaimBunkerSong()
        {
            if (!AudioCatalog.Has("music.bunker")) return false;
            _bunkerSong = true;
            return true;
        }

        void Start()
        {
            _musicRoot = new GameObject("Score").transform;
            _musicRoot.SetParent(transform, false);
            foreach (var id in LayerIds) _layers[id] = new Layer { cue = id };

            var sfxRoot = new GameObject("One-shots").transform;
            sfxRoot.SetParent(transform, false);
            _stinger = NewSource(sfxRoot, "Stinger", false);
            _pool2D = new AudioSource[12];
            for (int i = 0; i < _pool2D.Length; i++) _pool2D[i] = NewSource(sfxRoot, "2D " + i, false);
            _pool3D = new AudioSource[24];
            for (int i = 0; i < _pool3D.Length; i++) _pool3D[i] = NewSource(sfxRoot, "3D " + i, true);

            var ambRoot = new GameObject("Ambience").transform;
            ambRoot.SetParent(transform, false);
            _wind = Ambience(ambRoot, "amb.wind", false);
            _storm = Ambience(ambRoot, "amb.storm", false);
            _groveAmb = Ambience(ambRoot, "amb.grove", true);
            _plantAmb = Ambience(ambRoot, "amb.lifesupport", true);
            _turbineAmb = Ambience(ambRoot, "amb.turbine", true);
            _obeliskAmb = Ambience(ambRoot, "amb.obelisk", true);
            _bunkerAmb = Ambience(ambRoot, "amb.bunker", false);
            _nextVariationAt = Time.time + Random.Range(50f, 80f);
        }

        AudioSource NewSource(Transform parent, string name, bool spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.spread = 40f;
            return s;
        }

        AudioSource Ambience(Transform parent, string cue, bool spatial)
        {
            var c = AudioCatalog.Get(cue);
            var clip = c != null ? AudioCatalog.Clip(cue, 0) : null;
            if (clip == null) return null;
            var s = NewSource(parent, cue, spatial);
            s.clip = clip;
            s.loop = true;
            s.volume = 0f;
            s.priority = 64;
            if (spatial)
            {
                s.minDistance = c.minDistance;
                s.maxDistance = c.maxDistance;
            }
            s.time = Random.Range(0f, clip.length * 0.9f);
            s.Play();
            return s;
        }

        // ======================================================================================================
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            OverhaulLandmarks.Resolve();
            UpdateListener();
            ComputeTargets();

            // ducking: dialogue + stingers (unscaled time: the lore reader pauses the game)
            var dlg = DialogueSystem.Instance;
            float duckTarget = dlg != null && dlg.IsPlaying() ? duckUnderDialogue : 1f;
            _duck = Mathf.MoveTowards(_duck, duckTarget, dt * 1.5f);
            float sd = Time.unscaledTime < _stingerDuckUntil ? 0.45f : 1f;
            _stingerDuck = Mathf.MoveTowards(_stingerDuck, sd, dt * (sd < _stingerDuck ? 4f : 0.8f));

            float trip = TripDirector.Intensity;
            float tc = TripDirector.Clock;
            float pitch = 1f + trip * (0.007f + 0.008f * (TripDirector.InGrove ? 1f : 0f)) * Mathf.Sin(tc * 0.5f);

            foreach (var kv in _layers) UpdateLayer(kv.Value, dt, pitch);
            UpdateAmbience(dt);
        }

        void ComputeTargets()
        {
            _targets.Clear();
            foreach (var id in LayerIds) _targets[id] = 0f;

            var gfm = GameFlowManager.Instance;
            var ph = gfm != null ? gfm.Phase : GamePhase.Sandbox;
            var bridge = SignalBridge.Instance;
            var puzzle = bridge != null ? bridge.Puzzle : null;
            var tornado = bridge != null ? bridge.Tornado : null;
            var player = bridge != null ? bridge.Player : null;

            if (ph == GamePhase.IntroVideo || ph == GamePhase.OutroVideo || ph == GamePhase.End) return;
            if (ph != GamePhase.Bunker) _bunkerSong = false;

            bool inGrove = TripDirector.InGrove;
            if (_reportCard || ph == GamePhase.FinalReport) { _targets["music.report"] = 1f; return; }
            if (_bunkerSong) { _targets["music.bunker"] = 1f; _targets["music.layer.wonder"] = 0.25f; return; }
            if (_lore) { _targets["music.thought"] = 0.9f; return; }

            // The mushrooms are singing / waiting for an answer: the score steps aside.
            if (puzzle != null && (puzzle.IsDemoRunning || puzzle.IsPlayerTurn))
            {
                _targets[GroveId(_groveTrack)] = 0.08f;
                return;
            }

            var life = LifeSupport.Instance;
            float ls = life != null && life.Active ? life.Value01 : 1f;
            float tension = ls < 0.4f ? (0.4f - ls) / 0.4f : 0f;
            bool storm = (tornado != null && tornado.IsActive) || ph == GamePhase.TornadoStrike;

            float obeliskNear = 0f;
            var ob = bridge != null ? bridge.Obelisk : null;
            if (ob != null && ob.Stage != ObeliskStage.Destroyed && player != null)
            {
                Vector3 d = ob.transform.position - player.transform.position; d.y = 0f;
                obeliskNear = 1f - Mathf.Clamp01((d.magnitude - 9f) / 13f);
            }
            bool obeliskPhase = ph == GamePhase.ObeliskShield || ph == GamePhase.ObeliskCore;

            if (inGrove && !_wasInGrove) _groveTime = 0f;
            _wasInGrove = inGrove;

            if (ph == GamePhase.Bunker)
            {
                _targets["music.thought"] = 0.75f;
                _targets["music.layer.wonder"] = 0.45f;
            }
            else if (storm)
            {
                _targets["music.storm"] = 0.85f;
                _targets["music.explore"] = 0.25f;
                tension = Mathf.Max(tension, 0.65f);
            }
            else if (inGrove)
            {
                _groveTime += Time.deltaTime;
                var l = _layers[GroveId(_groveTrack)];
                float len = l.clip != null ? l.clip.length : 100f;
                if (_groveTime > Mathf.Clamp(len - 6f, 40f, 110f)) { _groveTrack = (_groveTrack + 1) % 6; _groveTime = 0f; }
                _targets[GroveId(_groveTrack)] = 1f;
            }
            else if (obeliskPhase || obeliskNear > 0.01f)
            {
                float o = obeliskPhase ? 1f : obeliskNear;
                _targets["music.layer.space"] = 0.8f * o;
                _targets["music.layer.wobble"] = (ph == GamePhase.ObeliskCore ? 0.75f : 0.25f) * o;
                _targets["music.explore"] = 0.7f * (1f - o);
            }
            else
            {
                // Exploration evolves slowly so it never feels static.
                if (Time.time >= _nextVariationAt)
                {
                    _variation = (_variation + 1 + Random.Range(0, 2)) % 4;
                    _nextVariationAt = Time.time + (_variation == 3 ? Random.Range(22f, 32f) : Random.Range(55f, 90f));
                }
                switch (_variation)
                {
                    case 0: _targets["music.explore"] = 1f; break;
                    case 1: _targets["music.explore"] = 0.9f; _targets["music.layer.calm"] = 0.35f; break;
                    case 2: _targets["music.explore"] = 0.6f; _targets["music.layer.calm"] = 0.55f; break;
                    default: _targets["music.layer.calm"] = 0.7f; break;     // a breather: just the drone
                }
            }
            _targets["music.layer.tension"] = Mathf.Max(_targets["music.layer.tension"], 0.75f * tension);
        }

        static string GroveId(int i) => "music.grove." + (i + 1);

        void UpdateLayer(Layer l, float dt, float pitch)
        {
            float target = _targets.TryGetValue(l.cue, out float t) ? t : 0f;
            l.target = target;
            if (target > 0f && l.a == null)
            {
                if (l.tried) return;
                l.tried = true;
                var c = AudioCatalog.Get(l.cue);
                l.clip = c != null ? AudioCatalog.Clip(l.cue, 0) : null;
                if (l.clip == null) return;
                l.cueVolume = c.volume;
                l.a = NewSource(_musicRoot, l.cue + " A", false);
                l.b = NewSource(_musicRoot, l.cue + " B", false);
                l.a.clip = l.b.clip = l.clip;
                l.a.priority = l.b.priority = 8;
            }
            if (l.a == null) return;

            float rate = target > l.vol ? 1f / fadeInSeconds : 1f / fadeOutSeconds;
            l.vol = Mathf.MoveTowards(l.vol, target, dt * rate);

            var main = l.aIsMain ? l.a : l.b;
            var other = l.aIsMain ? l.b : l.a;
            if (l.vol <= 0.0005f)
            {
                if (main.isPlaying) { main.Pause(); l.paused = true; }
                if (other.isPlaying) other.Stop();
                l.xf = -1f;
                return;
            }
            if (!main.isPlaying)
            {
                if (l.paused) { main.UnPause(); l.paused = false; }
                else { main.time = 0f; main.Play(); }
            }

            // seamless loop: start the twin before the end and crossfade (equal power)
            float len = l.clip.length;
            float xfLen = Mathf.Min(loopCrossfade, len * 0.25f);
            if (l.xf < 0f && main.time >= len - xfLen - 0.05f)
            {
                other.time = 0f;
                other.Play();
                l.xf = 0f;
            }
            float gMain = 1f, gOther = 0f;
            if (l.xf >= 0f)
            {
                l.xf += dt * Mathf.Max(0.01f, pitch) / xfLen;
                float p = Mathf.Clamp01(l.xf);
                gMain = Mathf.Cos(p * Mathf.PI * 0.5f);
                gOther = Mathf.Sin(p * Mathf.PI * 0.5f);
                if (l.xf >= 1f)
                {
                    main.Stop();
                    l.aIsMain = !l.aIsMain;
                    l.xf = -1f;
                    gMain = 1f; gOther = 0f;
                    var tmp = main; main = other; other = tmp;
                }
            }
            float v = l.vol * l.cueVolume * musicBus * master * _duck * _stingerDuck;
            main.volume = v * gMain;
            other.volume = v * gOther;
            main.pitch = other.pitch = pitch;
        }

        void UpdateAmbience(float dt)
        {
            var gfm = GameFlowManager.Instance;
            var ph = gfm != null ? gfm.Phase : GamePhase.Sandbox;
            bool silent = ph == GamePhase.IntroVideo || ph == GamePhase.OutroVideo || ph == GamePhase.End;
            var bridge = SignalBridge.Instance;
            bool storm = bridge != null && bridge.Tornado != null && bridge.Tornado.IsActive;
            float k = silent ? 0f : ambBus * master * (_lore ? 0.4f : 1f);

            Fade(_wind, (storm ? 0.3f : 1f) * k, dt, 0.8f);
            Fade(_storm, (storm ? 1f : 0f) * k, dt, 0.5f);
            Fade(_bunkerAmb, (ph == GamePhase.Bunker ? 1f : 0f) * k, dt, 0.5f);

            Place(_groveAmb, OverhaulLandmarks.GroveCenter, 1f * k, dt);
            Place(_plantAmb, OverhaulLandmarks.LifeSupportPlant, (OverhaulLandmarks.HasDressing ? 1f : 0f) * k, dt);
            var ob = bridge != null ? bridge.Obelisk : null;
            Place(_obeliskAmb, OverhaulLandmarks.Obelisk, (ob != null && ob.Stage != ObeliskStage.Destroyed ? 1f : 0f) * k, dt);

            float rotor = OverhaulLandmarks.TurbineRotorSpeed01;
            if (rotor < 0f) rotor = bridge != null && bridge.Windmill != null ? bridge.Windmill.Health01 : 1f;
            Place(_turbineAmb, OverhaulLandmarks.Windmill + Vector3.up * 8f, Mathf.Lerp(0.1f, 1f, rotor) * k, dt);
            if (_turbineAmb != null) _turbineAmb.pitch = Mathf.Lerp(0.55f, 1.15f, rotor);
        }

        void Fade(AudioSource s, float target, float dt, float rate)
        {
            if (s == null) return;
            var c = AudioCatalog.Get(s.clip != null ? s.gameObject.name : null);
            float cv = c != null ? c.volume : 1f;
            s.volume = Mathf.MoveTowards(s.volume, target * cv, dt * rate);
        }

        void Place(AudioSource s, Vector3 pos, float target, float dt)
        {
            if (s == null) return;
            if (pos != Vector3.zero) s.transform.position = pos + Vector3.up * 1.5f;
            Fade(s, pos == Vector3.zero ? 0f : target, dt, 0.5f);
        }

        // ======================================================================================================
        // Listener: ride with the player, face like the camera (left/right panning matches the screen).
        void UpdateListener()
        {
            var bridge = SignalBridge.Instance;
            var player = bridge != null ? bridge.Player : null;
            var cam = Camera.main;
            if (_listenerRig == null)
            {
                if (Time.unscaledTime < _nextListenerCheck || player == null || cam == null) return;
                _nextListenerCheck = Time.unscaledTime + 1f;
                _cameraListener = cam.GetComponent<AudioListener>();
                var go = new GameObject("Overhaul Listener (follows player)");
                _listenerRig = go.transform;
                Listener = go.AddComponent<AudioListener>();
                if (_cameraListener != null) _cameraListener.enabled = false;
                foreach (var other in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    if (other != Listener && other.enabled) other.enabled = false;
            }
            if (player != null) _listenerRig.position = player.transform.position + Vector3.up * 1.6f;
            if (cam != null)
            {
                Vector3 f = cam.transform.forward; f.y = 0f;
                if (f.sqrMagnitude > 1e-4f) _listenerRig.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
            }
        }

        // ======================================================================================================
        // One-shots (used by Sfx)

        public AudioSource PlayCue(string cue, Vector3? pos, float volume, float pitch, int variant = -1)
        {
            var c = AudioCatalog.Get(cue);
            if (c == null) return null;
            float now = Time.unscaledTime;
            if (_lastPlay.TryGetValue(cue, out float last) && now - last < c.minInterval) return null;
            if (!_voices.TryGetValue(cue, out var list)) _voices[cue] = list = new List<AudioSource>();
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] == null || !list[i].isPlaying) list.RemoveAt(i);
            if (list.Count >= Mathf.Max(1, c.maxVoices)) return null;
            var clip = AudioCatalog.Clip(cue, variant);
            if (clip == null) return null;

            bool spatial = pos.HasValue && c.bus != "ui" && c.bus != "stinger" && c.bus != "music";
            var s = spatial ? _pool3D[_next3D++ % _pool3D.Length] : _pool2D[_next2D++ % _pool2D.Length];
            if (s.isPlaying) s.Stop();
            if (spatial)
            {
                s.transform.position = pos.Value;
                s.minDistance = c.minDistance;
                s.maxDistance = c.maxDistance;
            }
            s.clip = clip;
            s.loop = false;
            float jitter = c.pitchJitter > 0f ? Random.Range(-c.pitchJitter, c.pitchJitter) : 0f;
            s.pitch = Mathf.Clamp(pitch * (1f + jitter), 0.3f, 3f);
            float baseVol = c.volume * volume;
            _baseVol[s] = baseVol;
            _busOf[s] = c.bus;
            s.volume = baseVol * BusLevel(c.bus) * master;
            s.Play();
            list.Add(s);
            _lastPlay[cue] = now;
            return s;
        }

        /// <summary>A looping cue on its own source (caller keeps the handle and stops it).</summary>
        public AudioSource StartLoop(string cue, Vector3? pos, float volume)
        {
            var c = AudioCatalog.Get(cue);
            var clip = c != null ? AudioCatalog.Clip(cue, 0) : null;
            if (clip == null) return null;
            bool spatial = pos.HasValue;
            var s = NewSource(transform, "Loop " + cue, spatial);
            if (spatial) { s.transform.position = pos.Value; s.minDistance = c.minDistance; s.maxDistance = c.maxDistance; }
            s.clip = clip;
            s.loop = true;
            _baseVol[s] = c.volume * volume;
            _busOf[s] = c.bus;
            s.volume = 0f;
            s.Play();
            StartCoroutine(FadeTo(s, c.volume * volume * BusLevel(c.bus) * master, 0.25f, false));
            return s;
        }

        public void StopLoop(AudioSource s, float fade = 0.3f)
        {
            if (s == null) return;
            StartCoroutine(FadeTo(s, 0f, fade, true));
        }

        System.Collections.IEnumerator FadeTo(AudioSource s, float target, float seconds, bool destroy)
        {
            float start = s != null ? s.volume : 0f;
            for (float t = 0f; t < seconds && s != null; t += Time.unscaledDeltaTime)
            {
                s.volume = Mathf.Lerp(start, target, t / seconds);
                yield return null;
            }
            if (s == null) yield break;
            s.volume = target;
            if (destroy)
            {
                _baseVol.Remove(s);
                _busOf.Remove(s);
                Destroy(s.gameObject);
            }
        }

        public void PlayStinger(string cue, float volume = 1f)
        {
            var c = AudioCatalog.Get(cue);
            var clip = c != null ? AudioCatalog.Clip(cue) : null;
            if (clip == null) return;
            float now = Time.unscaledTime;
            if (_lastPlay.TryGetValue(cue, out float last) && now - last < 2f) return;
            _lastPlay[cue] = now;
            _stinger.PlayOneShot(clip, Mathf.Clamp01(c.volume * volume * stingerBus * master));
            _stingerDuckUntil = now + Mathf.Max(1.5f, clip.length * 0.75f);
        }

        float BusLevel(string bus)
        {
            switch (bus)
            {
                case "ui": return uiBus;
                case "amb": return ambBus;
                case "music": return musicBus;
                case "stinger": return stingerBus;
                default: return sfxBus;
            }
        }

        /// <summary>Debug summary of what is audible (layer volumes), for logs and the inspector.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _layers)
                if (kv.Value.vol > 0.01f) sb.Append(kv.Key).Append('=').Append(kv.Value.vol.ToString("0.00")).Append(' ');
            return sb.ToString();
        }
    }

    /// <summary>Fire-and-forget sound effects by cue id (see the sound library manifest).</summary>
    public static class Sfx
    {
        /// <summary>Plays a cue; at a world position for 3D sfx/phrases, 2D otherwise. Returns null if limited or missing.</summary>
        public static AudioSource Play(string cue, Vector3? pos = null, float volume = 1f, float pitch = 1f, int variant = -1)
        {
            var d = AudioDirector.Instance;
            return d != null ? d.PlayCue(cue, pos, volume, pitch, variant) : null;
        }

        public static void Stinger(string cue, float volume = 1f)
        {
            var d = AudioDirector.Instance;
            if (d != null) d.PlayStinger(cue, volume);
        }

        public static AudioSource StartLoop(string cue, Vector3? pos = null, float volume = 1f)
        {
            var d = AudioDirector.Instance;
            return d != null ? d.StartLoop(cue, pos, volume) : null;
        }

        public static void StopLoop(AudioSource s, float fade = 0.3f)
        {
            var d = AudioDirector.Instance;
            if (d != null) d.StopLoop(s, fade);
            else if (s != null) Object.Destroy(s.gameObject);
        }
    }
}
