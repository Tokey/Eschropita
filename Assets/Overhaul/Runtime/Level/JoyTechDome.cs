using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The ruined JoyTech biodome behind the Mycari home: a geodesic hemisphere of chunky off-white struts with dirty
    /// teal glass (a third of the panels missing, shards on the ground), a concrete base ring, glowing fungal
    /// overgrowth creeping up from the ground (the Mycari reclaiming it), dead planters inside, and an airlock door
    /// exactly where Mycari leave — jammed half-open slabs, hazard stripes, a JoyTech panel and status lights that
    /// flare green whenever a Mycari goes home (<see cref="DomeDoorFX"/>). Only the base ring has colliders, and the
    /// door gap stays open.
    /// </summary>
    public static class JoyTechDome
    {
        public static void Build(Transform parent, Vector3 center, float radius, Vector3 door, Vector3 doorFacing)
        {
            var root = new GameObject("JoyTech Biodome").transform;
            root.SetParent(parent, false);
            float ground = LevelKit.MinGround(center, radius);
            Vector3 c = new Vector3(center.x, ground - 0.3f, center.z);
            Vector3 toDoor = door - center; toDoor.y = 0f;
            float doorAngle = Mathf.Atan2(toDoor.z, toDoor.x);
            var rng = new System.Random(9001);

            // ---- geodesic shell ----
            var verts = new List<Vector3>();
            var faces = new List<int[]>();
            Icosphere(3, verts, faces);
            var struts = new MeshBuilder();
            var glass = new MeshBuilder();
            var panels = new MeshBuilder();
            var shards = new MeshBuilder();
            var edges = new HashSet<long>();
            System.Func<Vector3, bool> inDoor = v =>
            {
                float a = Mathf.Atan2(v.z, v.x);
                return Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, doorAngle * Mathf.Rad2Deg)) < 17f && v.y < 3.6f / radius;
            };
            foreach (var f in faces)
            {
                Vector3 a = verts[f[0]], b = verts[f[1]], d = verts[f[2]];
                Vector3 cen = (a + b + d) / 3f;
                if (cen.y < 0.02f) continue;
                bool door1 = inDoor(cen);
                for (int e = 0; e < 3; e++)
                {
                    int i0 = f[e], i1 = f[(e + 1) % 3];
                    long key = Mathf.Min(i0, i1) * 100000L + Mathf.Max(i0, i1);
                    if (!edges.Add(key)) continue;
                    Vector3 p0 = verts[i0], p1 = verts[i1];
                    if (inDoor((p0 + p1) * 0.5f)) continue;
                    struts.Strut(p0 * radius, p1 * radius, 0.085f, 6, Color.white, false, 0.5f);
                }
                if (door1) continue;
                double roll = rng.NextDouble();
                Vector3 n = cen.normalized;
                Vector3 inset = n * -0.02f;
                if (roll < 0.58)
                {
                    int i0 = glass.AddVertex(a * radius + inset, n, new Vector2(0, 0), Color.white);
                    int i1 = glass.AddVertex(b * radius + inset, n, new Vector2(1, 0), Color.white);
                    int i2 = glass.AddVertex(d * radius + inset, n, new Vector2(0, 1), Color.white);
                    glass.TriAuto(i0, i1, i2);
                }
                else if (roll < 0.7)
                {
                    int i0 = panels.AddVertex(a * radius + inset, n, new Vector2(0, 0), Color.white);
                    int i1 = panels.AddVertex(b * radius + inset, n, new Vector2(1.5f, 0), Color.white);
                    int i2 = panels.AddVertex(d * radius + inset, n, new Vector2(0, 1.5f), Color.white);
                    panels.TriAuto(i0, i1, i2);
                }
                else if (shards.VertexCount < 400)
                {
                    // missing panel: its pieces lie on the ground below
                    Vector3 g = new Vector3(cen.x, 0f, cen.z) * radius * LevelKit.Rand(rng, 0.85f, 1.15f);
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 o = g + new Vector3(LevelKit.Rand(rng, -0.6f, 0.6f), 0f, LevelKit.Rand(rng, -0.6f, 0.6f));
                        float gy = LevelKit.GroundY(c.x + o.x, c.z + o.z) - c.y + 0.03f;
                        o.y = gy;
                        var q = Quaternion.Euler(LevelKit.Rand(rng, -10f, 10f), LevelKit.Rand(rng, 0f, 360f), LevelKit.Rand(rng, -10f, 10f));
                        int s0 = shards.AddVertex(o + q * new Vector3(-0.3f, 0f, -0.2f), Vector3.up, Vector2.zero, Color.white);
                        int s1 = shards.AddVertex(o + q * new Vector3(0.35f, 0f, -0.1f), Vector3.up, Vector2.right, Color.white);
                        int s2 = shards.AddVertex(o + q * new Vector3(0f, 0.02f, 0.35f), Vector3.up, Vector2.up, Color.white);
                        shards.TriAuto(s0, s1, s2);
                    }
                }
            }
            LevelKit.Mesh(root, "Dome Struts", struts, LevelKit.JoyPaint, c, Quaternion.identity, true);
            LevelKit.Mesh(root, "Dome Glass", glass, LevelKit.Glass("DomeGlass", new Color(0.55f, 0.82f, 0.8f), 0.28f), c, Quaternion.identity, false, false);
            LevelKit.Mesh(root, "Dome Panels", panels, LevelKit.JoyPaintDark, c, Quaternion.identity, false);
            LevelKit.Mesh(root, "Glass Shards", shards, LevelKit.Glass("ShardGlass", new Color(0.6f, 0.85f, 0.85f), 0.5f), c, Quaternion.identity, false, false);

            // ---- base ring (colliders + carving), skipping the door gap ----
            var ring = new MeshBuilder();
            int segs = 28;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                float am = (a0 + a1) * 0.5f;
                if (Mathf.Abs(Mathf.DeltaAngle(am * Mathf.Rad2Deg, doorAngle * Mathf.Rad2Deg)) < 12f) continue;
                Vector3 mid = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am)) * radius;
                float len = 2f * radius * Mathf.Sin(Mathf.PI / segs) + 0.05f;
                var q = Quaternion.LookRotation(new Vector3(-Mathf.Sin(am), 0f, Mathf.Cos(am)));
                var seg = new MeshBuilder();
                seg.Box(Vector3.zero, new Vector3(0.55f, 1.1f, len), Quaternion.identity, Color.white, 0.5f);
                Vector3 wp = c + mid;
                wp.y = LevelKit.GroundY(wp.x, wp.z) + 0.3f;
                var go = LevelKit.Mesh(root, "Base Ring", seg, LevelKit.Concrete, wp, q, true);
                LevelKit.Box(go, Vector3.zero, new Vector3(0.55f, 1.1f, len));
                LevelKit.CarveBox(go, Vector3.zero, new Vector3(0.7f, 1.4f, len));
            }

            // ---- fungal overgrowth (the Mycari reclaiming the dome) ----
            var fungus = LevelKit.Emissive("DomeFungus", new Color(0.35f, 0.6f, 0.75f), OverhaulPalette.Hdr(OverhaulPalette.SporeTeal, 1.3f));
            var fb = new MeshBuilder();
            for (int k = 0; k < 14; k++)
            {
                float a = LevelKit.Rand(rng, 0f, Mathf.PI * 2f);
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, doorAngle * Mathf.Rad2Deg)) < 20f) continue;
                Vector3 bp = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * LevelKit.Rand(rng, 0.97f, 1.05f);
                float up = LevelKit.Rand(rng, 0.6f, 3.2f);
                for (int s = 0; s < 6; s++)
                {
                    float y = LevelKit.Rand(rng, 0.5f, up);
                    float rr = Mathf.Sqrt(Mathf.Max(0f, radius * radius - y * y)) / radius;
                    Vector3 o = new Vector3(bp.x * rr, y, bp.z * rr) + new Vector3(LevelKit.Rand(rng, -0.4f, 0.4f), 0f, LevelKit.Rand(rng, -0.4f, 0.4f));
                    fb.Sphere(o, LevelKit.Rand(rng, 0.12f, 0.32f), 8, 6, Color.white);
                }
            }
            LevelKit.Mesh(root, "Fungal Overgrowth", fb, fungus, c, Quaternion.identity, true, false);

            // ---- dead planters inside ----
            var plant = new MeshBuilder();
            for (int k = 0; k < 5; k++)
            {
                float a = doorAngle + Mathf.PI + LevelKit.Rand(rng, -1.2f, 1.2f);
                float rr = radius * LevelKit.Rand(rng, 0.25f, 0.6f);
                Vector3 p = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                p.y = LevelKit.GroundY(c.x + p.x, c.z + p.z) - c.y + 0.3f;
                plant.Box(p, new Vector3(2.2f, 0.6f, 0.9f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), Color.white, 0.5f);
            }
            LevelKit.Mesh(root, "Planters", plant, LevelKit.Rust, c, Quaternion.identity, true);

            BuildDoor(root, door, doorFacing);
        }

        static void BuildDoor(Transform root, Vector3 door, Vector3 facing)
        {
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) facing = Vector3.back;
            Quaternion q = Quaternion.LookRotation(facing.normalized, Vector3.up);
            Vector3 p = new Vector3(door.x, LevelKit.MinGround(door, 2.5f), door.z);
            var mb = new MeshBuilder();
            mb.Submesh(0);
            mb.Box(new Vector3(-2.1f, 1.9f, 0f), new Vector3(0.7f, 3.8f, 0.9f), Quaternion.identity, Color.white, 0.5f);
            mb.Box(new Vector3(2.1f, 1.9f, 0f), new Vector3(0.7f, 3.8f, 0.9f), Quaternion.identity, Color.white, 0.5f);
            mb.Box(new Vector3(0f, 4.05f, 0f), new Vector3(4.9f, 0.6f, 1f), Quaternion.identity, Color.white, 0.5f);
            mb.Submesh(1);
            mb.Box(new Vector3(0f, 3.62f, 0f), new Vector3(4.92f, 0.26f, 1.02f), Quaternion.identity, Color.white);
            mb.Box(new Vector3(-2.1f, 0.25f, 0f), new Vector3(0.72f, 0.5f, 0.92f), Quaternion.identity, Color.white);
            mb.Box(new Vector3(2.1f, 0.25f, 0f), new Vector3(0.72f, 0.5f, 0.92f), Quaternion.identity, Color.white);
            mb.Submesh(2);
            mb.Box(new Vector3(-1.25f, 1.6f, -0.05f), new Vector3(1.3f, 3.2f, 0.16f), Quaternion.Euler(0f, 0f, 1.5f), Color.white, 0.5f);
            mb.Box(new Vector3(1.4f, 1.6f, -0.05f), new Vector3(1.1f, 3.2f, 0.16f), Quaternion.Euler(0f, 0f, -2.5f), Color.white, 0.5f);
            mb.Submesh(3);
            mb.Box(new Vector3(0f, 4.75f, 0.1f), new Vector3(2.6f, 0.8f, 0.12f), Quaternion.identity, Color.white);
            mb.Submesh(4);
            mb.Box(new Vector3(-0.6f, 4.75f, 0.17f), new Vector3(0.6f, 0.5f, 0.05f), Quaternion.identity, Color.white);
            mb.Box(new Vector3(0.4f, 4.75f, 0.17f), new Vector3(1.2f, 0.18f, 0.05f), Quaternion.identity, Color.white);
            var go = LevelKit.Mesh(root, "Airlock Door", mb, new[] { LevelKit.JoyPaint, LevelKit.Hazard, LevelKit.Corrugated, LevelKit.Teal, LevelKit.Magenta }, p, q, true);
            LevelKit.Box(go, new Vector3(-2.1f, 1.9f, 0f), new Vector3(0.7f, 3.8f, 0.9f));
            LevelKit.Box(go, new Vector3(2.1f, 1.9f, 0f), new Vector3(0.7f, 3.8f, 0.9f));

            var fx = go.AddComponent<DomeDoorFX>();
            fx.lights = new Light[2];
            fx.lamps = new Material[2];
            for (int i = 0; i < 2; i++)
            {
                var lamp = new GameObject(i == 0 ? "Status Light L" : "Status Light R");
                lamp.transform.SetParent(go.transform, false);
                lamp.transform.localPosition = new Vector3(i == 0 ? -2.1f : 2.1f, 3.4f, 0.5f);
                var lm = new MeshBuilder();
                lm.Box(Vector3.zero, new Vector3(0.3f, 0.18f, 0.1f), Quaternion.identity, Color.white);
                fx.lamps[i] = LevelKit.Emissive("DoorLamp", new Color(0.2f, 0.3f, 0.2f), Color.black);
                LevelKit.Mesh(lamp.transform, "Lamp", lm, fx.lamps[i], lamp.transform.position, lamp.transform.rotation, false, false);
                fx.lights[i] = OverhaulLights.Point(lamp, OverhaulPalette.RoleWanderer, 0f, 6f, true, false);
            }
        }

        /// <summary>Unit icosphere with each face subdivided <paramref name="freq"/> times along its edges.</summary>
        static void Icosphere(int freq, List<Vector3> verts, List<int[]> faces)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[][] f =
            {
                new[] {0, 11, 5}, new[] {0, 5, 1}, new[] {0, 1, 7}, new[] {0, 7, 10}, new[] {0, 10, 11},
                new[] {1, 5, 9}, new[] {5, 11, 4}, new[] {11, 10, 2}, new[] {10, 7, 6}, new[] {7, 1, 8},
                new[] {3, 9, 4}, new[] {3, 4, 2}, new[] {3, 2, 6}, new[] {3, 6, 8}, new[] {3, 8, 9},
                new[] {4, 9, 5}, new[] {2, 4, 11}, new[] {6, 2, 10}, new[] {8, 6, 7}, new[] {9, 8, 1},
            };
            var index = new Dictionary<long, int>();
            System.Func<Vector3, int> add = p =>
            {
                p = p.normalized;
                long key = ((long)Mathf.RoundToInt(p.x * 10000f) * 73856093L) ^ ((long)Mathf.RoundToInt(p.y * 10000f) * 19349663L) ^ ((long)Mathf.RoundToInt(p.z * 10000f) * 83492791L);
                if (index.TryGetValue(key, out int id)) return id;
                verts.Add(p);
                index[key] = verts.Count - 1;
                return verts.Count - 1;
            };
            foreach (var tri in f)
            {
                Vector3 a = v[tri[0]], b = v[tri[1]], c = v[tri[2]];
                var grid = new int[freq + 1][];
                for (int i = 0; i <= freq; i++)
                {
                    grid[i] = new int[freq + 1 - i];
                    for (int j = 0; j <= freq - i; j++)
                    {
                        Vector3 p = a + (b - a) * (i / (float)freq) + (c - a) * (j / (float)freq);
                        grid[i][j] = add(p);
                    }
                }
                for (int i = 0; i < freq; i++)
                    for (int j = 0; j < freq - i; j++)
                    {
                        faces.Add(new[] { grid[i][j], grid[i + 1][j], grid[i][j + 1] });
                        if (j < freq - i - 1) faces.Add(new[] { grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1] });
                    }
            }
        }
    }
}
