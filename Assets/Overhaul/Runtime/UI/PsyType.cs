using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Psychedelic UI type: 60s/70s poster lettering that reads more easily than the stock look, not less.
    ///  - Display (titles, grades, stamps): Shrikhand, a chunky groovy face, with a sunset gradient on every letter
    ///    (yellow → pink), a heavy ink outline and a hard retro drop shadow; big titles sway very gently (<see cref="PsyWave"/>).
    ///  - Heading (speaker names, small headers): the same face and colours, smaller, never moving.
    ///  - Body / Reading (everything you read): Fredoka Medium (SemiBold for bold), round and open with a big x-height,
    ///    in warm cream with a thin ink outline and a soft shadow, so it holds up over a bright (or tripping) world.
    ///    Sizes go up a step and faint "dim" text gets more opaque. Reading text (logs, dialogue) skips the outline.
    ///  - Ink: dark text on light key caps keeps its colour and only takes the rounded face.
    /// Dark rounded panels behind text turn deep aubergine with a thin neon-pink rim.
    /// The fonts are SIL OFL (Resources/Overhaul/Fonts, licences alongside). Their TextMeshPro assets are built at runtime
    /// (dynamic SDF), so the project's TMP settings are never touched. Everything changed is recorded and F10 undoes it.
    /// </summary>
    public static class PsyType
    {
        public enum Role { Display, Heading, Body, Reading, Ink }

        public static readonly Color Cream = new Color32(0xFF, 0xF4, 0xE0, 0xFF);
        public static readonly Color InkColor = new Color32(0x1A, 0x0B, 0x26, 0xFF);
        public static readonly Color Panel = new Color32(0x1D, 0x10, 0x2E, 0xE0);
        public static readonly Color Rim = new Color32(0xFF, 0x6F, 0xB5, 0x99);
        /// <summary>Per letter: sun yellow on top, hot pink to violet below.</summary>
        public static readonly VertexGradient Sunset = new VertexGradient(
            new Color32(0xFF, 0xEC, 0x8A, 0xFF), new Color32(0xFF, 0xC9, 0x6B, 0xFF),
            new Color32(0xFF, 0x7A, 0xB8, 0xFF), new Color32(0xD0, 0x86, 0xFF, 0xFF));

        /// <summary>False while the look is switched off with F10.</summary>
        public static bool Enabled { get; private set; } = true;
        public static TMP_FontAsset DisplayFont { get; private set; }
        public static TMP_FontAsset BodyFont { get; private set; }

        const string FontRoot = "Overhaul/Fonts/";
        static bool _tried, _ready;
        static Material _displayMat, _headingMat, _bodyMat, _readingMat;

        class Saved
        {
            public Role role;
            public float size, spacing;
            public bool dropCaps, applied, addedWave;
            public TMP_FontAsset font;
            public Material material;
            public Color color;
            public bool gradientOn;
            public VertexGradient gradient;
            public float origSize, origSpacing, lineSpacing;
            public FontStyles style;
            public FontWeight weight;
        }

        class SavedPanel
        {
            public Color color;
            public bool applied;
            public Outline addedRim;
        }

        static readonly Dictionary<TMP_Text, Saved> _texts = new Dictionary<TMP_Text, Saved>();
        static readonly Dictionary<Image, SavedPanel> _panels = new Dictionary<Image, SavedPanel>();
        static readonly List<TMP_Text> _deadTexts = new List<TMP_Text>();
        static readonly List<Image> _deadPanels = new List<Image>();

        /// <summary>Builds the font assets and materials once. False (and the stock look) if the fonts can't load.</summary>
        public static bool Init()
        {
            if (_ready && (DisplayFont == null || BodyFont == null)) _tried = _ready = false;   // assets unloaded: rebuild
            if (_tried) return _ready;
            _tried = true;
            try
            {
                var display = Resources.Load<Font>(FontRoot + "Shrikhand-Regular");
                var body = Resources.Load<Font>(FontRoot + "Fredoka-Medium");
                var bold = Resources.Load<Font>(FontRoot + "Fredoka-SemiBold");
                if (display == null || body == null)
                {
                    Debug.LogWarning("[Overhaul] Psychedelic UI fonts not found in Resources/" + FontRoot + "; keeping the stock type.");
                    return false;
                }
                // generous padding on the display face leaves room for its heavy outline and offset shadow
                DisplayFont = MakeFont(display, "Shrikhand (runtime SDF)", 72, 12);
                BodyFont = MakeFont(body, "Fredoka Medium (runtime SDF)", 56, 8);
                var bodyBold = bold != null ? MakeFont(bold, "Fredoka SemiBold (runtime SDF)", 56, 8) : null;
                if (DisplayFont == null || BodyFont == null) return false;

                // <b>, SemiBold and Bold all take the real SemiBold cut instead of a smeared faux bold
                if (bodyBold != null)
                    for (int w = 6; w < BodyFont.fontWeightTable.Length; w++) BodyFont.fontWeightTable[w].regularTypeface = bodyBold;
                var stock = TMP_Settings.defaultFontAsset;
                BodyFont.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (stock != null) BodyFont.fallbackFontAssetTable.Add(stock);   // arrows and symbols Fredoka lacks
                if (bodyBold != null)
                {
                    bodyBold.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    if (stock != null) bodyBold.fallbackFontAssetTable.Add(stock);
                }
                DisplayFont.fallbackFontAssetTable = new List<TMP_FontAsset> { BodyFont };

                _displayMat = Preset(DisplayFont, "Psy Display", 0.24f, 0.18f, new Vector2(0.75f, -0.85f), 0.05f, 0.25f, new Color32(0x2B, 0x0A, 0x3D, 0xF0));
                _headingMat = Preset(DisplayFont, "Psy Heading", 0.2f, 0.1f, new Vector2(0.5f, -0.6f), 0.1f, 0.15f, new Color32(0x2B, 0x0A, 0x3D, 0xE0));
                _bodyMat = Preset(BodyFont, "Psy Body", 0.12f, 0.02f, new Vector2(0.2f, -0.35f), 0.4f, 0.1f, new Color(0.07f, 0.03f, 0.1f, 0.75f));
                _readingMat = Preset(BodyFont, "Psy Reading", 0f, 0f, new Vector2(0.15f, -0.25f), 0.5f, 0f, new Color(0.07f, 0.03f, 0.1f, 0.6f));
                _ready = true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Overhaul] Psychedelic UI type disabled: " + e.Message);
                _ready = false;
            }
            return _ready;
        }

        static TMP_FontAsset MakeFont(Font font, string name, int pointSize, int padding)
        {
            var fa = TMP_FontAsset.CreateFontAsset(font, pointSize, padding, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (fa != null) fa.name = name;
            return fa;
        }

        static Material Preset(TMP_FontAsset fa, string name, float outline, float faceDilate, Vector2 shadowOffset,
                               float shadowSoftness, float shadowDilate, Color shadowColor)
        {
            var m = new Material(fa.material) { name = name };
            // Exactly the keyword pair of TMP's stock "Drop Shadow" preset (in Resources), so builds keep this variant.
            m.EnableKeyword("OUTLINE_ON");
            m.EnableKeyword("UNDERLAY_ON");
            m.SetFloat("_FaceDilate", faceDilate);
            m.SetFloat("_OutlineWidth", outline);
            m.SetColor("_OutlineColor", InkColor);
            m.SetColor("_UnderlayColor", shadowColor);
            m.SetFloat("_UnderlayOffsetX", shadowOffset.x);
            m.SetFloat("_UnderlayOffsetY", shadowOffset.y);
            m.SetFloat("_UnderlaySoftness", shadowSoftness);
            m.SetFloat("_UnderlayDilate", shadowDilate);
            return m;
        }

        // ---------------------------------------------------------------------------------------------------------
        // texts

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
        /// dropCaps turns off a forced UPPERCASE style (mixed case reads faster and suits the display face).
        /// </summary>
        public static void Style(TMP_Text t, Role role, float size = -1f, float spacing = -1f, bool dropCaps = false)
        {
            if (t == null || _texts.ContainsKey(t)) return;
            var s = new Saved
            {
                role = role, dropCaps = dropCaps,
                font = t.font, material = t.fontSharedMaterial, color = t.color, gradientOn = t.enableVertexGradient,
                gradient = t.colorGradient, origSize = t.fontSize, origSpacing = t.characterSpacing, lineSpacing = t.lineSpacing,
                style = t.fontStyle, weight = t.fontWeight,
            };
            s.size = size > 0f ? size : SizeFor(role, t.fontSize);
            s.spacing = spacing >= 0f ? spacing : Mathf.Min(t.characterSpacing, role == Role.Display || role == Role.Heading ? 2f : 2.5f);
            _texts[t] = s;
            if (Enabled && Init()) Apply(t, s);
        }

        /// <summary>A sensible role for a text from its size, weight and contents.</summary>
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
                case Role.Heading: return Mathf.Round(size * 1.15f);
                case Role.Ink: return size;   // key caps are sized around their letter
                default: return size < 18f ? Mathf.Round(size * 1.18f * 2f) / 2f : size + 1f;
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
                    t.font = DisplayFont;
                    t.fontSharedMaterial = s.role == Role.Display ? _displayMat : _headingMat;
                    t.fontWeight = FontWeight.Regular;                 // the face is already heavy: no faux bold
                    t.fontStyle = s.style & ~FontStyles.Bold;
                    t.colorGradient = GradientFor(s.color);
                    t.enableVertexGradient = true;
                    t.color = new Color(1f, 1f, 1f, s.color.a);
                    if (s.role == Role.Display && s.size >= 30f && t.GetComponent<PsyWave>() == null)
                    {
                        t.gameObject.AddComponent<PsyWave>();
                        s.addedWave = true;
                    }
                    break;
                case Role.Ink:
                    t.font = BodyFont;
                    t.fontSharedMaterial = BodyFont.material;
                    break;
                default:
                    t.font = BodyFont;
                    t.fontSharedMaterial = s.role == Role.Reading ? _readingMat : _bodyMat;
                    t.color = Readable(s.color);
                    if (s.role == Role.Reading) t.lineSpacing = s.lineSpacing + 4f;
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
            if (s.addedWave)
            {
                var w = t.GetComponent<PsyWave>();
                if (w != null) Object.Destroy(w);
                s.addedWave = false;
            }
            t.font = s.font;
            t.fontSharedMaterial = s.material;
            t.enableVertexGradient = s.gradientOn;
            t.colorGradient = s.gradient;
            t.color = s.color;
            t.fontSize = s.origSize;
            t.characterSpacing = s.origSpacing;
            t.lineSpacing = s.lineSpacing;
            t.fontStyle = s.style;
            t.fontWeight = s.weight;
        }

        /// <summary>Light text stays light but never faint: "dim" text gets at least 86 % opacity and the warm cream.</summary>
        static Color Readable(Color c)
        {
            Color.RGBToHSV(c, out _, out float sat, out float val);
            if (sat < 0.15f && val > 0.8f) c = new Color(Cream.r, Cream.g, Cream.b, c.a);
            if (Luma(c) > 0.55f && c.a < 0.86f) c.a = 0.86f;
            return c;
        }

        static VertexGradient GradientFor(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.25f || IsAccent(c)) return Sunset;
            // a semantic colour (good / bad / a grade's colour) keeps its meaning: lighter above, deeper and shifted below
            Color top = Color.HSVToRGB(Mathf.Repeat(h + 0.035f, 1f), s * 0.55f, Mathf.Min(1f, v * 1.1f + 0.1f));
            Color bottom = Color.HSVToRGB(Mathf.Repeat(h - 0.05f, 1f), Mathf.Min(1f, s * 1.1f), v * 0.95f);
            return new VertexGradient(top, top, bottom, bottom);
        }

        static bool IsAccent(Color c)
        {
            Color a = GameHUD.Instance != null ? GameHUD.Instance.accent : (Color)new Color32(0xE8, 0xA8, 0x5C, 0xFF);
            return Mathf.Abs(c.r - a.r) + Mathf.Abs(c.g - a.g) + Mathf.Abs(c.b - a.b) < 0.12f;
        }

        static bool IsNumeric(string s)
        {
            foreach (char ch in s)
                if (!char.IsDigit(ch) && ch != '%' && ch != '.' && ch != '/' && ch != ' ' && ch != '-') return false;
            return true;
        }

        public static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // ---------------------------------------------------------------------------------------------------------
        // panels

        /// <summary>Tints a dark backdrop behind text deep aubergine with a thin neon rim. Light images are left alone.</summary>
        public static void StylePanel(Image img)
        {
            if (img == null || _panels.ContainsKey(img) || Luma(img.color) > 0.2f) return;
            var s = new SavedPanel { color = img.color };
            _panels[img] = s;
            if (Enabled && Init()) Apply(img, s);
        }

        static void Apply(Image img, SavedPanel s)
        {
            if (s.applied) return;
            s.applied = true;
            img.color = new Color(Panel.r, Panel.g, Panel.b, Mathf.Max(s.color.a, Panel.a));
            if (img.GetComponent<Outline>() == null)
            {
                var rim = img.gameObject.AddComponent<Outline>();
                rim.effectColor = Rim;
                rim.effectDistance = new Vector2(1.5f, -1.5f);
                rim.useGraphicAlpha = true;
                s.addedRim = rim;
            }
        }

        static void Restore(Image img, SavedPanel s)
        {
            if (!s.applied) return;
            s.applied = false;
            img.color = s.color;
            if (s.addedRim != null) Object.Destroy(s.addedRim);
            s.addedRim = null;
        }

        // ---------------------------------------------------------------------------------------------------------

        /// <summary>Switches the whole look on or off; every text and panel styled so far follows.</summary>
        public static void SetEnabled(bool on)
        {
            if (on && !Init()) return;
            Enabled = on;
            _deadTexts.Clear();
            foreach (var kv in _texts)
            {
                if (kv.Key == null) { _deadTexts.Add(kv.Key); continue; }
                if (on) Apply(kv.Key, kv.Value); else Restore(kv.Key, kv.Value);
            }
            foreach (var t in _deadTexts) _texts.Remove(t);
            _deadPanels.Clear();
            foreach (var kv in _panels)
            {
                if (kv.Key == null) { _deadPanels.Add(kv.Key); continue; }
                if (on) Apply(kv.Key, kv.Value); else Restore(kv.Key, kv.Value);
            }
            foreach (var p in _deadPanels) _panels.Remove(p);
        }

        /// <summary>Forgets destroyed texts and panels (scene changes).</summary>
        public static void Prune()
        {
            _deadTexts.Clear();
            foreach (var kv in _texts) if (kv.Key == null) _deadTexts.Add(kv.Key);
            foreach (var t in _deadTexts) _texts.Remove(t);
            _deadPanels.Clear();
            foreach (var kv in _panels) if (kv.Key == null) _deadPanels.Add(kv.Key);
            foreach (var p in _deadPanels) _panels.Remove(p);
        }
    }
}
