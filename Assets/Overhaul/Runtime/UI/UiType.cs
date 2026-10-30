using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The game's UI type: a refined 70s soft serif for titles and names, a Futura-style sans for everything you read.
    ///  - Display (titles, grades, stamps): Fraunces, in the text's own colour, capitals evenly tracked.
    ///  - Heading (speaker names, short names): Fraunces Italic.
    ///  - Body / Reading (everything you read): Jost, clean 60s/70s geometric forms, one size step up so its x-height
    ///    matches or beats the stock font's, with slightly tighter line spacing.
    ///  - Ink: dark letters on light key caps, Jost SemiBold.
    /// Colours, panels and their transparency stay exactly as the game draws them. The only effect is a faint, soft
    /// shadow that keeps letters crisp over bright ground. The fonts are SIL OFL (Resources/Overhaul/Fonts, licences
    /// alongside). Their TextMeshPro assets are built at runtime (dynamic SDF), so the project's TMP settings are never
    /// touched. Everything changed is recorded, and F10 switches between this and the stock type.
    /// </summary>
    public static class UiType
    {
        public enum Role { Display, Heading, Body, Reading, Ink }

        /// <summary>False while the stock type is switched back on with F10.</summary>
        public static bool Enabled { get; private set; } = true;
        public static TMP_FontAsset DisplayFont { get; private set; }
        public static TMP_FontAsset ItalicFont { get; private set; }
        public static TMP_FontAsset BodyFont { get; private set; }

        const string FontRoot = "Overhaul/Fonts/";
        static bool _tried, _ready;
        static Material _displayMat, _italicMat, _bodyMat, _readingMat;

        class Saved
        {
            public Role role;
            public float size, spacing;
            public bool dropCaps, applied;
            public TMP_FontAsset font;
            public Material material;
            public float origSize, origSpacing, lineSpacing;
            public FontStyles style;
            public FontWeight weight;
        }

        static readonly Dictionary<TMP_Text, Saved> _texts = new Dictionary<TMP_Text, Saved>();
        static readonly List<TMP_Text> _dead = new List<TMP_Text>();

        /// <summary>Builds the font assets and materials once. False (and the stock type) if the fonts can't load.</summary>
        public static bool Init()
        {
            if (_ready && (DisplayFont == null || ItalicFont == null || BodyFont == null)) _tried = _ready = false;   // assets unloaded: rebuild
            if (_tried) return _ready;
            _tried = true;
            try
            {
                var display = Resources.Load<Font>(FontRoot + "Fraunces-Display");
                var italic = Resources.Load<Font>(FontRoot + "Fraunces-Italic");
                var body = Resources.Load<Font>(FontRoot + "Jost-Book");
                var bold = Resources.Load<Font>(FontRoot + "Jost-SemiBold");
                if (display == null || body == null)
                {
                    Debug.LogWarning("[Overhaul] UI fonts not found in Resources/" + FontRoot + "; keeping the stock type.");
                    return false;
                }
                DisplayFont = MakeFont(display, "Fraunces Display (runtime SDF)", 72);
                ItalicFont = italic != null ? MakeFont(italic, "Fraunces Italic (runtime SDF)", 64) : DisplayFont;
                BodyFont = MakeFont(body, "Jost Book (runtime SDF)", 56);
                var bodyBold = bold != null ? MakeFont(bold, "Jost SemiBold (runtime SDF)", 56) : null;
                if (DisplayFont == null || ItalicFont == null || BodyFont == null) return false;

                // <b>, SemiBold and Bold all take the real SemiBold cut instead of a smeared faux bold
                if (bodyBold != null)
                    for (int w = 6; w < BodyFont.fontWeightTable.Length; w++) BodyFont.fontWeightTable[w].regularTypeface = bodyBold;
                // symbols these fonts lack (arrows, notes, dots) come from TextMeshPro's default font
                var stock = TMP_Settings.defaultFontAsset;
                foreach (var fa in new[] { BodyFont, bodyBold })
                {
                    if (fa == null) continue;
                    fa.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    if (stock != null) fa.fallbackFontAssetTable.Add(stock);
                }
                DisplayFont.fallbackFontAssetTable = new List<TMP_FontAsset> { BodyFont };
                if (ItalicFont != DisplayFont) ItalicFont.fallbackFontAssetTable = new List<TMP_FontAsset> { BodyFont };

                _displayMat = Preset(DisplayFont, "UI Display", 0.55f, 0.55f, 0.15f);
                _italicMat = Preset(ItalicFont, "UI Heading", 0.5f, 0.5f, 0.1f);
                _bodyMat = Preset(BodyFont, "UI Body", 0.45f, 0.45f, 0.1f);
                _readingMat = Preset(BodyFont, "UI Reading", 0.35f, 0.5f, 0f);
                _ready = true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Overhaul] UI type disabled: " + e.Message);
                _ready = false;
            }
            return _ready;
        }

        static TMP_FontAsset MakeFont(Font font, string name, int pointSize)
        {
            var fa = TMP_FontAsset.CreateFontAsset(font, pointSize, Mathf.RoundToInt(pointSize * 0.125f), GlyphRenderMode.SDFAA,
                                                   1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (fa != null) fa.name = name;
            return fa;
        }

        /// <summary>A faint soft shadow straight under the letters: no outline, nothing that reads as an effect.</summary>
        static Material Preset(TMP_FontAsset fa, string name, float shadowAlpha, float softness, float dilate)
        {
            var m = new Material(fa.material) { name = name };
            // Exactly the keyword pair of TMP's stock "Drop Shadow" preset (in Resources), so builds keep this variant.
            m.EnableKeyword("OUTLINE_ON");
            m.EnableKeyword("UNDERLAY_ON");
            m.SetFloat("_OutlineWidth", 0f);
            m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, shadowAlpha));
            m.SetFloat("_UnderlayOffsetX", 0f);
            m.SetFloat("_UnderlayOffsetY", -0.25f);
            m.SetFloat("_UnderlaySoftness", softness);
            m.SetFloat("_UnderlayDilate", dilate);
            return m;
        }

        // ---------------------------------------------------------------------------------------------------------

        /// <summary>True once a text has been styled (or deliberately left alone) by this system.</summary>
        public static bool IsKnown(TMP_Text t) => t != null && _texts.ContainsKey(t);

        /// <summary>True for texts still on TextMeshPro's default font (the ones the game never gave a font of its own).</summary>
        public static bool UsesStockFont(TMP_Text t)
        {
            if (t == null) return false;
            var f = t.font;
            return f == null || f == TMP_Settings.defaultFontAsset || f.name.StartsWith("LiberationSans");
        }

        /// <summary>
        /// Styles a text for a role. size / spacing &lt; 0 keep a role-appropriate version of the text's own;
        /// dropCaps turns off a forced UPPERCASE style (mixed case reads faster and suits the italic).
        /// </summary>
        public static void Style(TMP_Text t, Role role, float size = -1f, float spacing = -1f, bool dropCaps = false)
        {
            if (t == null || _texts.ContainsKey(t)) return;
            var s = new Saved
            {
                role = role, dropCaps = dropCaps, font = t.font, material = t.fontSharedMaterial,
                origSize = t.fontSize, origSpacing = t.characterSpacing, lineSpacing = t.lineSpacing,
                style = t.fontStyle, weight = t.fontWeight,
            };
            s.size = size > 0f ? size : SizeFor(role, t.fontSize);
            s.spacing = spacing >= 0f ? spacing : Mathf.Min(t.characterSpacing, role == Role.Heading ? 1f : 4f);
            _texts[t] = s;
            if (Enabled && Init()) Apply(t, s);
        }

        /// <summary>A sensible role for a text from its colour, size, weight and contents.</summary>
        public static Role Guess(TMP_Text t, string text)
        {
            if (Luma(t.color) < 0.35f) return Role.Ink;
            if (!string.IsNullOrEmpty(text) && IsNumeric(text)) return Role.Body;   // gauges and counters: plain, steady digits
            if (t.fontSize >= 30f) return Role.Display;
            bool wraps = t.textWrappingMode != TextWrappingModes.NoWrap;
            if (!wraps && t.fontSize >= 20f && t.fontWeight >= FontWeight.SemiBold) return Role.Heading;   // names, short headers
            if (wraps && t.fontSize >= 18f) return Role.Reading;
            return Role.Body;
        }

        static float SizeFor(Role role, float size)
        {
            switch (role)
            {
                case Role.Display: return Mathf.Round(size * 1.1f);
                case Role.Heading: return Mathf.Round(size * 1.12f);
                case Role.Ink: return size;   // key caps are sized around their letter
                default: return size < 18f ? Mathf.Round(size * 1.18f * 2f) / 2f : size + 2f;
            }
        }

        static void Apply(TMP_Text t, Saved s)
        {
            if (s.applied) return;
            s.applied = true;
            switch (s.role)
            {
                case Role.Display:
                case Role.Heading:
                    t.font = s.role == Role.Display ? DisplayFont : ItalicFont;
                    t.fontSharedMaterial = s.role == Role.Display ? _displayMat : _italicMat;
                    t.fontWeight = FontWeight.Regular;   // the cuts are chosen for their roles: no faux bold
                    t.fontStyle = s.style & ~FontStyles.Bold;
                    break;
                case Role.Ink:
                    t.font = BodyFont;
                    t.fontSharedMaterial = BodyFont.material;
                    break;
                default:
                    t.font = BodyFont;
                    t.fontSharedMaterial = s.role == Role.Reading ? _readingMat : _bodyMat;
                    t.lineSpacing = s.lineSpacing - 10f;   // Jost's line box is tall; this lands near 1.35
                    break;
            }
            if (s.dropCaps) t.fontStyle &= ~FontStyles.UpperCase;
            t.fontSize = s.size;
            t.characterSpacing = s.spacing;
        }

        static void Restore(TMP_Text t, Saved s)
        {
            if (!s.applied) return;
            s.applied = false;
            t.font = s.font;
            t.fontSharedMaterial = s.material;
            t.fontSize = s.origSize;
            t.characterSpacing = s.origSpacing;
            t.lineSpacing = s.lineSpacing;
            t.fontStyle = s.style;
            t.fontWeight = s.weight;
        }

        static bool IsNumeric(string s)
        {
            foreach (char ch in s)
                if (!char.IsDigit(ch) && ch != '%' && ch != '.' && ch != '/' && ch != ' ' && ch != '-') return false;
            return true;
        }

        public static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>Switches between this type and the stock type; every text styled so far follows.</summary>
        public static void SetEnabled(bool on)
        {
            if (on && !Init()) return;
            Enabled = on;
            Prune();
            foreach (var kv in _texts)
            {
                if (on) Apply(kv.Key, kv.Value);
                else Restore(kv.Key, kv.Value);
            }
        }

        /// <summary>Forgets destroyed texts (scene changes).</summary>
        public static void Prune()
        {
            _dead.Clear();
            foreach (var kv in _texts) if (kv.Key == null) _dead.Add(kv.Key);
            foreach (var t in _dead) _texts.Remove(t);
        }
    }
}
