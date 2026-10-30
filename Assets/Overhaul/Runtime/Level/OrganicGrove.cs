using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Replaces the mushroom grove's grey stone tile with something organic (the old tile's renderers are hidden;
    /// its collider stays, so pads and player keep their footing): a lobed patch of violet-umber mycelium soil that
    /// hugs the walkable surface, a glowing vein network over it (<see cref="GroveGlowRig"/> makes it breathe and
    /// ripple with each note), clusters of glowing mushrooms and lichen standing stones around the rim with root
    /// ridges between them — leaving gaps where the three light paths leave, and space around every pad.
    /// </summary>
    public static class OrganicGrove
    {
        public static GroveGlowRig Build(Transform parent, MushroomFieldTrigger grove, GameObject[] pads, Vector3[] exits, List<Renderer> hidden)
        {
            Vector3 c = grove.transform.position;
            float R = 6.5f;
            var trig = grove.GetComponent<Collider>();
            if (trig != null) { Vector3 e = trig.bounds.extents; R = Mathf.Clamp(Mathf.Max(e.x, e.z) * 1.1f, 5f, 10f); }

            // hide the stone tile visuals (keep particles and the pads, which are separate objects)
            foreach (var r in grove.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                bool isPad = false;
                if (pads != null) foreach (var p in pads) if (p != null && r.transform.IsChildOf(p.transform)) isPad = true;
                if (isPad) continue;
                r.enabled = false;
                hidden.Add(r);
            }

            var root = new GameObject("Organic Grove").transform;
            root.SetParent(parent, false);
            var padPos = new List<Vector3>();
            if (pads != null) foreach (var p in pads) if (p != null) padPos.Add(p.transform.position);
            var exitAngles = new List<float>();
            foreach (var x in exits) { Vector3 d = x - c; exitAngles.Add(Mathf.Atan2(d.z, d.x)); }

            System.Func<float, float> rim = a => R * (1f + 0.13f * Mathf.Sin(3f * a + 1f) + 0.07f * Mathf.Sin(7f * a + 2.3f));
            float size = R * 1.35f * 2f;
            System.Func<float, float, Vector3> P = (u, v) =>
            {
                float x = c.x + (u - 0.5f) * size, z = c.z + (v - 0.5f) * size;
                return new Vector3(x, LevelKit.SurfaceY(x, z, parent) + 0.025f, z);
            };
            System.Func<float, float, float> radial = (u, v) =>
            {
                float x = (u - 0.5f) * size, z = (v - 0.5f) * size;
                float a = Mathf.Atan2(z, x);
                return Mathf.Sqrt(x * x + z * z) / rim(a);
            };
            int cells = Mathf.Clamp(Mathf.RoundToInt(size / 0.35f), 24, 80);
            float tiles = size / 3f;

            var soil = new MeshBuilder();
            soil.Grid(P, cells, cells, Color.white, new Vector2(tiles, tiles), (u, v) => radial(u, v) <= 1f);
            LevelKit.Mesh(root, "Mycelium Soil", soil, LevelKit.GroveSoil, Vector3.zero, Quaternion.identity, false, false);

            // glowing veins, fading towards the rim (vertex alpha)
            var glow = new MeshBuilder();
            System.Func<float, float, Vector3> PG = (u, v) => P(u, v) + Vector3.up * 0.02f;
            glow.Grid(PG, cells, cells, Color.white, new Vector2(tiles * 0.9f, tiles * 0.9f), (u, v) => radial(u, v) <= 0.98f,
                      (u, v) => new Color(1f, 1f, 1f, Mathf.Clamp01((1f - radial(u, v)) * 4f)));
            var veinTex = LevelKit.Own(VeinTexture(256, 6, 1337));
            var veinMat = LevelKit.Own(OverhaulMaterials.SoftGlow("MyceliumGlow", OverhaulPalette.SporeCyan, 1.2f, 1f, OverhaulMaterials.GlowShape.Texture, veinTex));
            LevelKit.Mesh(root, "Mycelium Glow", glow, veinMat, Vector3.zero, Quaternion.identity, false, false);

            // mushrooms around the rim
            var rng = new System.Random(77);
            var capMats = new[]
            {
                LevelKit.Emissive("CapCyan", new Color(0.3f, 0.75f, 0.85f), OverhaulPalette.Hdr(OverhaulPalette.SporeCyan, 1.1f)),
                LevelKit.Emissive("CapViolet", new Color(0.55f, 0.35f, 0.85f), OverhaulPalette.Hdr(OverhaulPalette.SporeViolet, 1.1f)),
                LevelKit.Emissive("CapGold", new Color(0.95f, 0.75f, 0.4f), OverhaulPalette.Hdr(OverhaulPalette.SporeGold, 0.9f)),
            };
            var stemMat = LevelKit.Plain("MushroomStem", OverhaulPalette.MyceliumCream, 0.35f);
            int clusters = 0;
            for (int attempt = 0; attempt < 60 && clusters < 9; attempt++)
            {
                float a = LevelKit.Rand(rng, 0f, Mathf.PI * 2f);
                if (NearExit(a, exitAngles, 0.38f)) continue;
                float rr = rim(a) * LevelKit.Rand(rng, 0.78f, 1.08f);
                Vector3 cp = new Vector3(c.x + Mathf.Cos(a) * rr, 0f, c.z + Mathf.Sin(a) * rr);
                if (NearAny(cp, padPos, 2.6f)) continue;
                clusters++;
                bool back = Mathf.Sin(a) > 0.3f;    // north side (far from the camera) gets the tall ones
                int n = 2 + rng.Next(3);
                for (int k = 0; k < n; k++)
                {
                    Vector3 mp = cp + new Vector3(LevelKit.Rand(rng, -0.8f, 0.8f), 0f, LevelKit.Rand(rng, -0.8f, 0.8f));
                    if (NearAny(mp, padPos, 2.2f)) continue;
                    mp.y = LevelKit.SurfaceY(mp.x, mp.z, parent);
                    float h = (back ? LevelKit.Rand(rng, 0.9f, 2.4f) : LevelKit.Rand(rng, 0.35f, 1.1f)) * (k == 0 ? 1f : 0.65f);
                    Mushroom(root, mp, h, rng, stemMat, capMats[rng.Next(capMats.Length)]);
                }
            }
            // tiny glowing caps scattered inside the rim
            for (int i = 0; i < 26; i++)
            {
                float a = LevelKit.Rand(rng, 0f, Mathf.PI * 2f);
                float rr = rim(a) * LevelKit.Rand(rng, 0.55f, 0.95f);
                Vector3 mp = new Vector3(c.x + Mathf.Cos(a) * rr, 0f, c.z + Mathf.Sin(a) * rr);
                if (NearAny(mp, padPos, 1.8f)) continue;
                mp.y = LevelKit.SurfaceY(mp.x, mp.z, parent);
                Mushroom(root, mp, LevelKit.Rand(rng, 0.12f, 0.3f), rng, stemMat, capMats[rng.Next(capMats.Length)]);
            }

            // lichen standing stones outside the rim, and root ridges between them
            var stones = new List<Vector3>();
            int placed = 0;
            for (int attempt = 0; attempt < 40 && placed < 7; attempt++)
            {
                float a = placed * (Mathf.PI * 2f / 7f) + LevelKit.Rand(rng, -0.25f, 0.25f);
                if (NearExit(a, exitAngles, 0.32f)) continue;
                float rr = rim(a) * 1.2f + 0.8f;
                Vector3 sp = new Vector3(c.x + Mathf.Cos(a) * rr, 0f, c.z + Mathf.Sin(a) * rr);
                sp.y = LevelKit.GroundY(sp.x, sp.z) - 0.2f;
                Stone(root, sp, LevelKit.Rand(rng, 1.4f, 2.6f), rng);
                stones.Add(sp);
                placed++;
            }
            var roots = new MeshBuilder();
            for (int i = 0; i < stones.Count; i++)
            {
                Vector3 a = stones[i], b = stones[(i + 1) % stones.Count];
                if (Vector3.Distance(a, b) > R * 1.6f) continue;
                var pts = new List<Vector3>();
                for (int s = 0; s <= 8; s++)
                {
                    Vector3 p = Vector3.Lerp(a, b, s / 8f);
                    Vector3 toC = (c - p); toC.y = 0f;
                    p += toC.normalized * Mathf.Sin(s / 8f * Mathf.PI) * 0.6f;
                    p.y = LevelKit.SurfaceY(p.x, p.z, parent) + 0.05f;
                    pts.Add(p);
                }
                roots.Tube(MeshBuilder.Smooth(pts, 0.4f), 0.09f, 7, Color.white, true, 0.5f);
            }
            LevelKit.Mesh(root, "Root Ridges", roots, LevelKit.Plain("Roots", new Color(0.24f, 0.16f, 0.22f), 0.3f), Vector3.zero, Quaternion.identity, true, false);

            var rig = root.gameObject.AddComponent<GroveGlowRig>();
            rig.veins = veinMat;
            rig.caps = capMats;
            return rig;
        }

        static bool NearExit(float a, List<float> exits, float halfWidth)
        {
            foreach (var e in exits) if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, e * Mathf.Rad2Deg)) < halfWidth * Mathf.Rad2Deg) return true;
            return false;
        }

        static bool NearAny(Vector3 p, List<Vector3> pts, float r)
        {
            foreach (var q in pts) { Vector3 d = p - q; d.y = 0f; if (d.magnitude < r) return true; }
            return false;
        }

        static void Mushroom(Transform root, Vector3 p, float h, System.Random rng, Material stem, Material cap)
        {
            var mb = new MeshBuilder();
            float lean = LevelKit.Rand(rng, -9f, 9f);
            var q = Quaternion.Euler(lean, LevelKit.Rand(rng, 0f, 360f), LevelKit.Rand(rng, -6f, 6f));
            float sr = h * 0.09f + 0.03f;
            mb.Submesh(0);
            mb.Lathe(Vector3.zero, q, new List<Vector2> { new Vector2(sr * 1.4f, -0.05f), new Vector2(sr, h * 0.25f), new Vector2(sr * 0.85f, h * 0.9f), new Vector2(sr * 0.001f, h * 0.95f) }, 10, Color.white, false, 1f);
            mb.Submesh(1);
            float cr = h * LevelKit.Rand(rng, 0.38f, 0.55f) + 0.08f;
            mb.Lathe(q * new Vector3(0f, h * 0.88f, 0f), q, new List<Vector2>
            {
                new Vector2(0.001f, -cr * 0.05f), new Vector2(cr * 0.8f, -cr * 0.08f), new Vector2(cr, cr * 0.05f),
                new Vector2(cr * 0.85f, cr * 0.35f), new Vector2(cr * 0.5f, cr * 0.55f), new Vector2(0.001f, cr * 0.62f),
            }, 16, Color.white, false, 1f);
            LevelKit.Mesh(root, "Mushroom", mb, new[] { stem, cap }, p, Quaternion.identity, true, h > 0.8f);
        }

        static void Stone(Transform root, Vector3 p, float h, System.Random rng)
        {
            var sections = new List<Vector3[]>();
            float w = h * LevelKit.Rand(rng, 0.28f, 0.4f);
            float lean = LevelKit.Rand(rng, -8f, 8f);
            for (int s = 0; s <= 5; s++)
            {
                float y = h * s / 5f;
                float k = Mathf.Lerp(1f, 0.55f, s / 5f) * (1f + LevelKit.Rand(rng, -0.08f, 0.08f));
                var ring = new Vector3[8];
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f;
                    float rr = w * k * (1f + LevelKit.Rand(rng, -0.15f, 0.15f));
                    ring[i] = new Vector3(Mathf.Cos(a) * rr * 1.25f, y, Mathf.Sin(a) * rr * 0.8f);
                }
                sections.Add(ring);
            }
            var mb = new MeshBuilder();
            mb.Loft(sections, Color.white, false, true, 0.6f);
            var go = LevelKit.Mesh(root, "Standing Stone", mb, LevelKit.Lichen, p, Quaternion.Euler(lean, LevelKit.Rand(rng, 0f, 360f), LevelKit.Rand(rng, -6f, 6f)), true);
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, h * 0.5f, 0f);
            col.radius = w;
            col.height = h;
        }

        /// <summary>Tileable glowing vein network (alpha = vein), from cellular noise edges.</summary>
        public static Texture2D VeinTexture(int n, int cells, int seed)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "MyceliumVeins", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[n * n];
            var rng = new System.Random(seed);
            var fx = new float[cells * cells];
            var fy = new float[cells * cells];
            for (int i = 0; i < fx.Length; i++) { fx[i] = (float)rng.NextDouble(); fy[i] = (float)rng.NextDouble(); }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * cells, v = (y + 0.5f) / n * cells;
                    int cx = Mathf.FloorToInt(u), cy = Mathf.FloorToInt(v);
                    float f1 = 9f, f2 = 9f;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int gx = cx + i, gy = cy + j;
                            int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                            float px0 = gx + fx[wy * cells + wx], py0 = gy + fy[wy * cells + wx];
                            float d = Mathf.Sqrt((px0 - u) * (px0 - u) + (py0 - v) * (py0 - v));
                            if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
                        }
                    float edge = f2 - f1;
                    float vein = Mathf.Clamp01(1f - edge / 0.09f);
                    vein = vein * vein * (3f - 2f * vein);
                    float node = Mathf.Clamp01(1f - f1 / 0.12f) * 0.35f;
                    float a = Mathf.Clamp01(vein * 0.9f + node);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px);
            t.Apply(true, false);
            return t;
        }
    }
}
