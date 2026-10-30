using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The collectible logs (design doc: "lore object collectibles … scattered throughout the dome"). Texts are
    /// editable TextAssets in Resources/Overhaul/Lore/&lt;id&gt;.txt: lines starting with '#' are notes, then the title
    /// line, the author line, a blank line and the body (TextMeshPro rich text allowed).
    /// </summary>
    public static class LoreLibrary
    {
        public class Entry
        {
            public string id, title, author, body;
        }

        /// <summary>All logs in the order the player is most likely to meet them.</summary>
        public static readonly string[] Ids =
        {
            "danu_radio", "joytech_brochure", "aniya_log_240", "vera_sterling", "hargrave_logs", "surveillance_dossier",
        };

        static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();

        public static Entry Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_cache.TryGetValue(id, out var e)) return e;
            var ta = Resources.Load<TextAsset>("Overhaul/Lore/" + id);
            if (ta == null) { _cache[id] = null; return null; }
            e = Parse(id, ta.text);
            _cache[id] = e;
            return e;
        }

        static Entry Parse(string id, string text)
        {
            var e = new Entry { id = id, title = id, author = "", body = "" };
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int i = 0;
            while (i < lines.Length && lines[i].TrimStart().StartsWith("#")) i++;
            if (i < lines.Length) e.title = lines[i++].Trim();
            if (i < lines.Length && lines[i].Trim().Length > 0) e.author = lines[i++].Trim();
            while (i < lines.Length && lines[i].Trim().Length == 0) i++;
            e.body = string.Join("\n", lines, i, lines.Length - i).Trim();
            return e;
        }
    }
}
