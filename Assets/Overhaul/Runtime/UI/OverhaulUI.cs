using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Small uGUI toolkit for the overhaul's own screens, styled like <see cref="GameHUD"/> (dark translucent rounded
    /// panels, warm off-white TextMeshPro text, amber accent). Each overhaul screen lives on its own Screen Space
    /// Overlay canvas so it never disturbs the game's HUD.
    /// </summary>
    public static class OverhaulUI
    {
        public static Color Panel => GameHUD.Instance != null ? GameHUD.Instance.panelColor : new Color(10f / 255f, 12f / 255f, 16f / 255f, 0.62f);
        public static Color Text => GameHUD.Instance != null ? GameHUD.Instance.textColor : new Color32(0xF2, 0xEF, 0xE9, 0xFF);
        public static Color Dim => GameHUD.Instance != null ? GameHUD.Instance.dimText : new Color(0.95f, 0.94f, 0.91f, 0.55f);
        public static Color Accent => GameHUD.Instance != null ? GameHUD.Instance.accent : (Color)new Color32(0xE8, 0xA8, 0x5C, 0xFF);
        public static Color Good => GameHUD.Instance != null ? GameHUD.Instance.good : OverhaulPalette.Good;
        public static Color Warn => GameHUD.Instance != null ? GameHUD.Instance.warn : OverhaulPalette.Warn;
        public static Color Bad => GameHUD.Instance != null ? GameHUD.Instance.bad : OverhaulPalette.Bad;

        static Sprite _ring, _softCircle;

        public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sortingOrder;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920f, 1080f);
            s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        public static Image Image(string name, Transform parent, Sprite sprite, Color color, Image.Type type = UnityEngine.UI.Image.Type.Simple)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = type;
            img.raycastTarget = false;
            return img;
        }

        public static Image PanelImage(string name, Transform parent, Color color, int radius = 14)
            => Image(name, parent, GameHUD.RoundedSprite(radius), color, UnityEngine.UI.Image.Type.Sliced);

        public static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color,
                                           TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft,
                                           FontWeight weight = FontWeight.Regular, bool wrap = false, float spacing = 0f)
        {
            var rt = Rect(name, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text ?? "";
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontWeight = weight;
            tmp.characterSpacing = spacing;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>A crisp anti-aliased ring (for radial gauges: Image.Type.Filled, Radial360).</summary>
        public static Sprite RingSprite
        {
            get
            {
                if (_ring != null) return _ring;
                _ring = MakeCircle(256, 0.78f, 0.98f, "OverhaulRing");
                return _ring;
            }
        }

        /// <summary>A soft radial glow (dots, halos, bubbles).</summary>
        public static Sprite SoftCircle
        {
            get
            {
                if (_softCircle != null) return _softCircle;
                const int n = 64;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "OverhaulSoftCircle", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d));
                    }
                t.SetPixels(px);
                t.Apply(false, true);
                _softCircle = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
                return _softCircle;
            }
        }

        static Sprite MakeCircle(int n, float inner, float outer, string name)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float aa = 1.5f / (n / 2f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01((d - inner) / aa) * Mathf.Clamp01((outer - d) / aa);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
