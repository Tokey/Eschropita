using System;
using UnityEngine;

namespace Eschropita.Overhaul.EditorTools
{
    /// <summary>
    /// Tileable procedural noise for baking textures (all functions are periodic over u, v in [0,1) so the
    /// results tile seamlessly). Pair with <see cref="OverhaulAssets.SaveTexture"/>.
    ///
    ///   float h = TexKit.Fbm(u, v, 4, 5, seed);             // 0..1 cloudy noise, base frequency 4, 5 octaves
    ///   TexKit.Worley(u, v, 8, seed, out f1, out f2);       // cellular distances (cells/veins: f2 - f1 small on edges)
    ///   Vector2 w = TexKit.Warp(u, v, 3, seed, 0.08f);      // domain warp for organic shapes
    ///   Texture2D nrm = TexKit.NormalFromHeight(heights, size, strength);
    /// </summary>
    public static class TexKit
    {
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        static int Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = seed * 0x27d4eb2d;
                h ^= x * 0x165667b1;
                h ^= y * 0x2d2816fe + 0x3c6ef372;
                h = (h ^ (h >> 15)) * 0x2c1b3c6d;
                h = (h ^ (h >> 12)) * 0x297a2d39;
                return h ^ (h >> 15);
            }
        }

        static float Rand01(int x, int y, int seed) => (Hash(x, y, seed) & 0xffffff) / 16777215f;

        /// <summary>Periodic gradient noise, roughly -0.7..0.7. x, y in lattice units; px, py = period in cells.</summary>
        public static float Perlin(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            int ix0 = Mod(x0, px), ix1 = Mod(x0 + 1, px), iy0 = Mod(y0, py), iy1 = Mod(y0 + 1, py);
            float n00 = Grad(Hash(ix0, iy0, seed), fx, fy);
            float n10 = Grad(Hash(ix1, iy0, seed), fx - 1f, fy);
            float n01 = Grad(Hash(ix0, iy1, seed), fx, fy - 1f);
            float n11 = Grad(Hash(ix1, iy1, seed), fx - 1f, fy - 1f);
            float u = Fade(fx), v = Fade(fy);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v);
        }

        static float Grad(int h, float x, float y)
        {
            float a = (h & 1023) / 1023f * Mathf.PI * 2f;
            return Mathf.Cos(a) * x + Mathf.Sin(a) * y;
        }

        /// <summary>Tileable fractal noise in 0..1. baseFreq = cells across the tile at octave 0 (integer).</summary>
        public static float Fbm(float u, float v, int baseFreq, int octaves, int seed, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int f = Mathf.Max(1, baseFreq);
            for (int o = 0; o < octaves; o++)
            {
                sum += Perlin(u * f, v * f, f, f, seed + o * 101) * amp;
                norm += amp;
                amp *= gain;
                f *= 2;
            }
            return Mathf.Clamp01(sum / norm * 0.75f + 0.5f);
        }

        /// <summary>Ridged tileable noise 0..1 (sharp crests) — cracks, veins, dunes.</summary>
        public static float Ridged(float u, float v, int baseFreq, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int f = Mathf.Max(1, baseFreq);
            for (int o = 0; o < octaves; o++)
            {
                float n = 1f - Mathf.Abs(Perlin(u * f, v * f, f, f, seed + o * 131) * 1.4f);
                sum += n * n * amp;
                norm += amp;
                amp *= 0.5f;
                f *= 2;
            }
            return Mathf.Clamp01(sum / norm);
        }

        /// <summary>Tileable cellular noise: f1/f2 = distances (in cell units) to the nearest / 2nd nearest feature point.</summary>
        public static void Worley(float u, float v, int cells, int seed, out float f1, out float f2)
        {
            cells = Mathf.Max(1, cells);
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            f1 = f2 = 99f;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = Mod(gx, cells), wy = Mod(gy, cells);
                    float px = gx + Rand01(wx, wy, seed);
                    float py = gy + Rand01(wx, wy, seed + 17);
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
        }

        /// <summary>Tileable domain warp: returns (u, v) displaced by fbm noise of the given amplitude (in UV units).</summary>
        public static Vector2 Warp(float u, float v, int baseFreq, int seed, float amplitude)
        {
            float wx = Fbm(u, v, baseFreq, 3, seed) - 0.5f;
            float wy = Fbm(u, v, baseFreq, 3, seed + 999) - 0.5f;
            return new Vector2(Frac(u + wx * amplitude * 2f), Frac(v + wy * amplitude * 2f));
        }

        public static float Frac(float x) => x - Mathf.Floor(x);
        public static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>Allocates a texture-sized buffer and fills it: fn(u, v) for pixel centres.</summary>
        public static Color[] Fill(int size, Func<float, float, Color> fn)
        {
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++) px[y * size + x] = fn((x + 0.5f) / size, v);
            }
            return px;
        }

        public static float[] FillHeight(int size, Func<float, float, float> fn)
        {
            var h = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++) h[y * size + x] = fn((x + 0.5f) / size, v);
            }
            return h;
        }

        /// <summary>Tangent-space normal map pixels (wrapped Sobel) from a height buffer. strength ≈ 1–8.</summary>
        public static Color[] NormalFromHeight(float[] h, int size, float strength)
        {
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float l = h[y * size + Mod(x - 1, size)], r = h[y * size + Mod(x + 1, size)];
                    float d = h[Mod(y - 1, size) * size + x], u = h[Mod(y + 1, size) * size + x];
                    Vector3 n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    px[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            return px;
        }

        /// <summary>HDRP mask map pixel: R metallic, G ambient occlusion, B detail mask, A smoothness.</summary>
        public static Color Mask(float metallic, float ao, float smoothness, float detail = 0f) => new Color(metallic, ao, detail, smoothness);

        public static Texture2D ToTexture(Color[] px, int size, bool linear, string name)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = TextureWrapMode.Repeat };
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }
    }
}
