using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The sound library index. Cues (ids like "music.explore", "sfx.shard.pickup") are listed in
    /// Resources/Overhaul/Audio/manifest.json together with their files (random variations), bus, mix volume and
    /// playback hints. Clips load lazily from Resources/Overhaul/Audio/&lt;file&gt; and are cached.
    /// Missing cues are tolerated: callers get null and a single warning per cue.
    /// The library itself is cut from the team's sound designer's tracks — see Tools/SoundLibrary.
    /// </summary>
    public static class AudioCatalog
    {
        public const string Root = "Overhaul/Audio/";

        [Serializable]
        public class Cue
        {
            public string id;
            public string[] files = new string[0];
            /// <summary>music | amb | phrase | stinger | sfx | ui | voice</summary>
            public string bus = "sfx";
            public float volume = 1f;
            public bool loop;
            public float pitchJitter;
            public int maxVoices = 4;
            public float minInterval = 0.04f;
            public float minDistance = 3f;
            public float maxDistance = 38f;
            public string key;
            public float bpm;
            public string source;
            public string notes;
        }

        [Serializable]
        class Manifest
        {
            public int version;
            public Cue[] cues = new Cue[0];
        }

        static Dictionary<string, Cue> _cues;
        static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        static readonly HashSet<string> _warned = new HashSet<string>();
        static readonly Dictionary<string, int> _lastPick = new Dictionary<string, int>();
        static readonly System.Random _rng = new System.Random();

        public static int Count { get { Ensure(); return _cues.Count; } }

        public static IEnumerable<string> Ids { get { Ensure(); return _cues.Keys; } }

        public static void Reload()
        {
            _cues = null;
            _clips.Clear();
            _warned.Clear();
            _lastPick.Clear();
        }

        static void Ensure()
        {
            if (_cues != null) return;
            _cues = new Dictionary<string, Cue>(StringComparer.Ordinal);
            var ta = Resources.Load<TextAsset>(Root + "manifest");
            if (ta == null)
            {
                Debug.LogWarning("[Overhaul] Sound library manifest not found (Resources/" + Root + "manifest.json).");
                return;
            }
            try
            {
                var m = JsonUtility.FromJson<Manifest>(ta.text);
                if (m != null && m.cues != null)
                    foreach (var c in m.cues)
                        if (c != null && !string.IsNullOrEmpty(c.id)) _cues[c.id] = c;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Overhaul] Could not read the sound library manifest: " + e.Message);
            }
        }

        public static Cue Get(string id)
        {
            Ensure();
            return id != null && _cues.TryGetValue(id, out var c) ? c : null;
        }

        public static bool Has(string id) => Get(id) != null;

        /// <summary>Number of variations a cue has (0 if missing).</summary>
        public static int Variants(string id)
        {
            var c = Get(id);
            return c != null && c.files != null ? c.files.Length : 0;
        }

        /// <summary>Clip for a cue; variant -1 picks a random variation (never the one it picked last time, so
        /// footsteps and blips don't stutter on a repeat). Null if the cue or file is missing.</summary>
        public static AudioClip Clip(string id, int variant = -1)
        {
            var c = Get(id);
            if (c == null || c.files == null || c.files.Length == 0)
            {
                WarnOnce(id, "cue not in the sound library");
                return null;
            }
            int n = c.files.Length, i;
            if (variant >= 0) i = variant % n;
            else
            {
                i = _rng.Next(n);
                if (n > 1 && _lastPick.TryGetValue(id, out int last) && i == last) i = (i + 1 + _rng.Next(n - 1)) % n;
                _lastPick[id] = i;
            }
            string path = Root + c.files[i];
            if (_clips.TryGetValue(path, out var clip) && clip != null) return clip;
            clip = Resources.Load<AudioClip>(path);
            if (clip == null) { WarnOnce(id + "#" + i, "missing file Resources/" + path); return null; }
            _clips[path] = clip;
            return clip;
        }

        public static bool TryClip(string id, out AudioClip clip)
        {
            clip = Has(id) ? Clip(id) : null;
            return clip != null;
        }

        public static float Volume(string id)
        {
            var c = Get(id);
            return c != null ? c.volume : 1f;
        }

        static void WarnOnce(string key, string what)
        {
            if (key == null || !_warned.Add(key)) return;
            Debug.Log("[Overhaul] Sound '" + key + "': " + what + ".");
        }
    }
}
