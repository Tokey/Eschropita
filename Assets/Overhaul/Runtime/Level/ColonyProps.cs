using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Set dressing for the failed JoyTech compound (design doc: abandoned infrastructure, ecological scars):
    /// Escropita's little solarpunk lander, a toppled "JOY" monument, a JoyTech billboard frame, scattered crates and
    /// barrels, surveillance antenna masts and a broken fence around the obelisk, a hatch frame over the bunker, and
    /// distant mesas, domes and a comms tower on the horizon for depth. Props keep clear of every gameplay corridor.
    /// </summary>
    public static class ColonyProps
    {
        public static void Build(Transform parent)
        {
            var root = new GameObject("JoyTech Colony Props").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(31337);
            Shuttle(root, OverhaulLandmarks.Shuttle, OverhaulLandmarks.Spawn);
            Vector3 mon;
            if (FindSpot(rng, OverhaulLandmarks.Spawn, 11f, 20f, 3f, out mon)) Monument(root, mon, rng);
            Vector3 bb;
            if (FindSpot(rng, OverhaulLandmarks.Spawn, 14f, 24f, 2.5f, out bb)) Billboard(root, bb, OverhaulLandmarks.Spawn);
            Clutter(root, rng);
            ObeliskSurroundings(root, rng);
            Horizon(root, rng);
        }

        static bool FindSpot(System.Random rng, Vector3 around, float rMin, float rMax, float margin, out Vector3 p)
        {
            for (int i = 0; i < 80; i++)
            {
                float a = LevelKit.Rand(rng, 0f, Mathf.PI * 2f);
                float r = LevelKit.Rand(rng, rMin, rMax);
                p = around + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (LevelKit.Clear(p, margin)) { p = LevelKit.OnGround(p); return true; }
            }
            p = Vector3.zero;
            return false;
        }

        // ---- Escropita's lander --------------------------------------------------------------------------
        static void Shuttle(Transform root, Vector3 pos, Vector3 face)
        {
            if (pos == Vector3.zero) return;
            Vector3 f = face - pos; f.y = 0f;
            Quaternion q = Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f.normalized : Vector3.back, Vector3.up);
            Vector3 p = new Vector3(pos.x, LevelKit.MinGround(pos, 2.2f), pos.z);
            var cream = LevelKit.Textured("ShuttleCream", "MetalPlate", new Color(0.93f, 0.88f, 0.76f), 0.55f, 0.0001f, 2f);
            var sage = LevelKit.Plain("ShuttleSage", new Color(0.52f, 0.66f, 0.52f), 0.5f);
            var mb = new MeshBuilder();
            mb.Submesh(0);
            mb.Lathe(new Vector3(0f, 1.0f, 0f), Quaternion.identity, new List<Vector2>
            {
                new Vector2(0.001f, 0f), new Vector2(1.1f, 0.05f), new Vector2(1.45f, 0.5f), new Vector2(1.5f, 1.6f),
                new Vector2(1.3f, 2.6f), new Vector2(0.9f, 3.25f), new Vector2(0.4f, 3.6f), new Vector2(0.001f, 3.7f),
            }, 22, Color.white, false, 0.5f);
            mb.Submesh(1);
            mb.Lathe(new Vector3(0f, 1.0f, 0f), Quaternion.identity, new List<Vector2> { new Vector2(1.52f, 1.05f), new Vector2(1.52f, 1.35f) }, 22, Color.white, false, 0.5f);
            mb.Submesh(2);
            mb.Lathe(new Vector3(0f, 0.35f, 0f), Quaternion.identity, new List<Vector2> { new Vector2(0.75f, 0f), new Vector2(0.55f, 0.4f), new Vector2(0.45f, 0.7f) }, 16, Color.white, false, 0.5f);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                mb.Strut(d * 1.25f + Vector3.up * 1.6f, d * 2.2f + Vector3.up * 0.1f, 0.08f, 8, Color.white, true);
                mb.Cylinder(d * 2.2f + Vector3.up * 0.0f, d * 2.2f + Vector3.up * 0.12f, 0.35f, 0.3f, 12, Color.white);
            }
            mb.Submesh(1);
            mb.Box(new Vector3(1.9f, 3.0f, 0f), new Vector3(1.3f, 0.06f, 0.7f), Quaternion.Euler(0f, 0f, -18f), Color.white);
            mb.Box(new Vector3(-1.9f, 3.0f, 0f), new Vector3(1.3f, 0.06f, 0.7f), Quaternion.Euler(0f, 0f, 18f), Color.white);
            mb.Submesh(2);
            mb.Box(new Vector3(0f, 0.55f, 2.1f), new Vector3(1.0f, 0.08f, 2.2f), Quaternion.Euler(-24f, 0f, 0f), Color.white);
            var go = LevelKit.Mesh(root, "Escropita's Lander", mb, new[] { cream, sage, LevelKit.Trim }, p, q, true);
            var cap = go.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 2.6f, 0f);
            cap.radius = 1.45f;
            cap.height = 4f;
            LevelKit.CarveCapsule(go, new Vector3(0f, 1f, 0f), 2f, 2f);

            // portholes and the warm open hatch light
            var win = LevelKit.Emissive("ShuttleWindows", new Color(0.9f, 0.75f, 0.45f), new Color(3f, 2.1f, 1.1f));
            var wm = new MeshBuilder();
            for (int i = 0; i < 5; i++)
            {
                float a = (i - 2) * 26f * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                wm.Disc(d * 1.47f + Vector3.up * 3.3f, d, 0.2f, 14, Color.white);
            }
            LevelKit.Mesh(go.transform, "Portholes", wm, win, p, q, false, false);
            var lg = new GameObject("Hatch Light");
            lg.transform.SetParent(go.transform, false);
            lg.transform.localPosition = new Vector3(0f, 1.6f, 1.3f);
            OverhaulLights.Point(lg, new Color(1f, 0.78f, 0.5f), 900f, 7f, true, false);
            // a potted plant by the ramp: the En-Joy motto made literal ("plant what grows")
            var pm = new MeshBuilder();
            pm.Submesh(0);
            pm.Cylinder(new Vector3(0.9f, 0f, 3.4f), new Vector3(0.9f, 0.4f, 3.4f), 0.22f, 0.3f, 12, Color.white);
            pm.Submesh(1);
            pm.Sphere(new Vector3(0.9f, 0.62f, 3.4f), 0.3f, 10, 7, Color.white);
            pm.Sphere(new Vector3(0.8f, 0.85f, 3.35f), 0.18f, 8, 6, Color.white);
            LevelKit.Mesh(go.transform, "Potted Plant", pm, new[] { LevelKit.Rust, LevelKit.Plain("Leaves", new Color(0.35f, 0.62f, 0.3f), 0.4f) }, p, q, true, false);
        }

        // ---- toppled JOY monument ---------------------------------------------------------------------------
        static void Monument(Transform root, Vector3 p, System.Random rng)
        {
            var paint = LevelKit.Plain("MonumentPink", new Color(0.86f, 0.42f, 0.62f), 0.55f, 0.1f);
            var mb = new MeshBuilder();
            mb.Submesh(0);
            // J
            mb.Box(new Vector3(-2.4f, 1.6f, 0f), new Vector3(0.6f, 3.2f, 0.6f), Quaternion.identity, Color.white, 0.5f);
            mb.Box(new Vector3(-3.0f, 0.3f, 0f), new Vector3(1.6f, 0.6f, 0.6f), Quaternion.identity, Color.white, 0.5f);
            mb.Box(new Vector3(-3.5f, 0.75f, 0f), new Vector3(0.6f, 1.0f, 0.6f), Quaternion.identity, Color.white, 0.5f);
            // O (a lathe ring stood up)
            var ring = new List<Vector2>();
            for (int i = 0; i <= 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                ring.Add(new Vector2(1.3f + Mathf.Cos(a) * 0.32f, Mathf.Sin(a) * 0.32f));
            }
            mb.Lathe(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(90f, 0f, 0f), ring, 26, Color.white, false, 0.5f);
            // Y
            mb.Box(new Vector3(2.6f, 0.8f, 0f), new Vector3(0.6f, 1.6f, 0.6f), Quaternion.identity, Color.white, 0.5f);
            mb.Box(new Vector3(2.2f, 2.3f, 0f), new Vector3(0.55f, 1.7f, 0.6f), Quaternion.Euler(0f, 0f, 28f), Color.white, 0.5f);
            mb.Box(new Vector3(3.0f, 2.3f, 0f), new Vector3(0.55f, 1.7f, 0.6f), Quaternion.Euler(0f, 0f, -28f), Color.white, 0.5f);
            mb.Submesh(1);
            mb.Box(new Vector3(0f, -0.35f, 0f), new Vector3(8.6f, 0.7f, 1.6f), Quaternion.identity, Color.white, 0.5f);
            // fallen over on its back, half sunk in a drift
            var q = Quaternion.Euler(0f, LevelKit.Rand(rng, 0f, 360f), 0f) * Quaternion.Euler(-78f, 0f, 6f);
            var go = LevelKit.Mesh(root, "Toppled JOY Monument", mb, new[] { paint, LevelKit.Concrete }, p + Vector3.up * 0.25f, q, true);
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.4f, 0f);
            col.size = new Vector3(8.6f, 3.4f, 1.6f);
        }

        static void Billboard(Transform root, Vector3 p, Vector3 face)
        {
            Vector3 f = face - p; f.y = 0f;
            var q = Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f.normalized : Vector3.back, Vector3.up) * Quaternion.Euler(0f, 0f, 4f);
            var mb = new MeshBuilder();
            mb.Submesh(0);
            mb.Strut(new Vector3(-2.4f, 0f, 0f), new Vector3(-2.4f, 4.6f, 0f), 0.12f, 8, Color.white, true);
            mb.Strut(new Vector3(2.4f, 0f, 0f), new Vector3(2.4f, 4.6f, 0f), 0.12f, 8, Color.white, true);
            mb.Box(new Vector3(0f, 3.4f, -0.1f), new Vector3(5.6f, 2.6f, 0.12f), Quaternion.identity, Color.white);
            mb.Submesh(1);
            mb.Box(new Vector3(-1.2f, 3.4f, 0.0f), new Vector3(2.4f, 2.3f, 0.05f), Quaternion.identity, Color.white);
            mb.Submesh(2);
            mb.Box(new Vector3(1.4f, 3.9f, 0.0f), new Vector3(2.3f, 0.5f, 0.05f), Quaternion.identity, Color.white);
            mb.Box(new Vector3(1.4f, 3.1f, 0.0f), new Vector3(2.3f, 0.3f, 0.05f), Quaternion.Euler(0f, 0f, -6f), Color.white);
            LevelKit.Mesh(root, "JoyTech Billboard", mb, new[] { LevelKit.Rust, LevelKit.Teal, LevelKit.Magenta }, p, q, true);
        }

        static void Clutter(Transform root, System.Random rng)
        {
            var crates = new MeshBuilder();
            var barrels = new MeshBuilder();
            var origin = OverhaulLandmarks.Spawn;
            int nc = 0, nb = 0;
            for (int i = 0; i < 200 && (nc < 12 || nb < 10); i++)
            {
                float a = LevelKit.Rand(rng, 0f, Mathf.PI * 2f);
                float r = LevelKit.Rand(rng, 6f, 34f);
                Vector3 p = origin + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r + 8f);
                if (!LevelKit.Clear(p, 1.2f)) continue;
                p = LevelKit.OnGround(p);
                int pile = 1 + rng.Next(3);
                for (int k = 0; k < pile; k++)
                {
                    Vector3 o = p - origin + new Vector3(LevelKit.Rand(rng, -1f, 1f), 0f, LevelKit.Rand(rng, -1f, 1f));
                    o.y = LevelKit.GroundY(origin.x + o.x, origin.z + o.z) - origin.y;
                    if (nc < 12 && rng.NextDouble() < 0.55)
                    {
                        float s = LevelKit.Rand(rng, 0.7f, 1.2f);
                        crates.Box(o + Vector3.up * (s * 0.45f), new Vector3(s, s * 0.9f, s), Quaternion.Euler(0f, LevelKit.Rand(rng, 0f, 90f), LevelKit.Rand(rng, -4f, 4f)), Color.white, 0.6f);
                        nc++;
                    }
                    else if (nb < 10)
                    {
                        bool fallen = rng.NextDouble() < 0.35;
                        Vector3 a0 = o + (fallen ? new Vector3(-0.5f, 0.35f, 0f) : Vector3.zero);
                        Vector3 a1 = o + (fallen ? new Vector3(0.5f, 0.35f, 0f) : Vector3.up * 1f);
                        barrels.Cylinder(a0, a1, 0.34f, 0.34f, 14, Color.white, true, true, 0.6f);
                        nb++;
                    }
                }
            }
            LevelKit.Mesh(root, "Crates", crates, LevelKit.Corrugated, origin, Quaternion.identity, true);
            LevelKit.Mesh(root, "Barrels", barrels, LevelKit.Rust, origin, Quaternion.identity, true);
        }

        // ---- the surveillance obelisk's surroundings --------------------------------------------------------
        static void ObeliskSurroundings(Transform root, System.Random rng)
        {
            Vector3 ob = OverhaulLandmarks.Obelisk;
            if (ob == Vector3.zero) return;
            Vector3 toGrove = OverhaulLandmarks.GroveCenter - ob; toGrove.y = 0f;
            Vector3 toSpawn = OverhaulLandmarks.Spawn - ob; toSpawn.y = 0f;
            float ag = Mathf.Atan2(toGrove.z, toGrove.x), asp = Mathf.Atan2(toSpawn.z, toSpawn.x);
            System.Func<float, bool> openAngle = a =>
                Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, ag * Mathf.Rad2Deg)) > 30f && Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, asp * Mathf.Rad2Deg)) > 30f;

            // antenna masts with dishes and blinking lights
            int masts = 0;
            for (int i = 0; i < 12 && masts < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 12f + 0.2f;
                if (!openAngle(a)) continue;
                if (i % 4 != 0 && masts > 0 && i % 3 != 0) continue;
                Vector3 p = LevelKit.OnGround(ob + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 8.5f);
                var mb = new MeshBuilder();
                float h = LevelKit.Rand(rng, 6.5f, 8.5f);
                mb.Submesh(0);
                for (int k = 0; k < 3; k++)
                {
                    float b = k * Mathf.PI * 2f / 3f;
                    Vector3 foot = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * 0.5f;
                    mb.Strut(foot, Vector3.up * h, 0.05f, 5, Color.white, true);
                }
                for (int k = 1; k < 6; k++)
                {
                    float y = h * k / 6f, r = 0.5f * (1f - k / 6f);
                    for (int s = 0; s < 3; s++)
                    {
                        float b0 = s * Mathf.PI * 2f / 3f, b1 = (s + 1) * Mathf.PI * 2f / 3f;
                        mb.Strut(new Vector3(Mathf.Cos(b0) * r, y, Mathf.Sin(b0) * r), new Vector3(Mathf.Cos(b1) * r, y, Mathf.Sin(b1) * r), 0.03f, 4, Color.white);
                    }
                }
                mb.Submesh(1);
                Vector3 toOb = ob - p; toOb.y = 0f;
                var dq = Quaternion.LookRotation(toOb.normalized) * Quaternion.Euler(-70f, 0f, 0f);
                mb.Lathe(Vector3.up * (h * 0.72f), dq, new List<Vector2> { new Vector2(0.001f, 0f), new Vector2(0.45f, 0.05f), new Vector2(0.8f, 0.22f), new Vector2(0.85f, 0.26f) }, 18, Color.white, false, 1f);
                var go = LevelKit.Mesh(root, "Surveillance Mast", mb, new[] { LevelKit.Rust, LevelKit.JoyPaint }, p, Quaternion.identity, true);
                var lamp = new GameObject("Mast Light");
                lamp.transform.SetParent(go.transform, false);
                lamp.transform.localPosition = Vector3.up * (h + 0.1f);
                var lm = new MeshBuilder();
                lm.Sphere(Vector3.zero, 0.12f, 10, 6, Color.white);
                var lampMat = LevelKit.Emissive("MastLamp", new Color(0.5f, 0.05f, 0.05f), Color.black);
                LevelKit.Mesh(lamp.transform, "Bulb", lm, lampMat, lamp.transform.position, Quaternion.identity, false, false);
                var bl = lamp.AddComponent<BeaconBlink>();
                bl.lamp = lampMat;
                bl.glow = OverhaulLights.Point(lamp, new Color(1f, 0.1f, 0.08f), 0f, 6f, true, false);
                bl.phase = masts * 0.31f;
                masts++;
            }

            // broken security fence ring
            var fence = new MeshBuilder();
            int posts = 26;
            Vector3 origin = LevelKit.OnGround(ob);
            for (int i = 0; i < posts; i++)
            {
                float a0 = i * Mathf.PI * 2f / posts, a1 = (i + 1) * Mathf.PI * 2f / posts;
                if (!openAngle((a0 + a1) * 0.5f) || rng.NextDouble() < 0.22) continue;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 11.5f, p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 11.5f;
                p0.y = LevelKit.GroundY(ob.x + p0.x, ob.z + p0.z) - origin.y;
                p1.y = LevelKit.GroundY(ob.x + p1.x, ob.z + p1.z) - origin.y;
                float lean = rng.NextDouble() < 0.2 ? 0.6f : 0f;
                fence.Strut(p0, p0 + new Vector3(lean * 0.3f, 1.8f, 0f), 0.05f, 5, Color.white, true);
                fence.Strut(p0 + Vector3.up * 1.6f, p1 + Vector3.up * 1.6f, 0.02f, 4, Color.white);
                fence.Strut(p0 + Vector3.up * 1.0f, p1 + Vector3.up * 1.0f, 0.02f, 4, Color.white);
                fence.Strut(p0 + Vector3.up * 0.4f, p1 + Vector3.up * 0.4f, 0.02f, 4, Color.white);
            }
            LevelKit.Mesh(root, "Security Fence", fence, LevelKit.Rust, origin, Quaternion.identity, false);

            // bunker hatch frame + fungal growth
            var bunker = Object.FindAnyObjectByType<BunkerTrigger>(FindObjectsInactive.Include);
            Vector3 bp = bunker != null ? bunker.transform.position : ob - toSpawn.normalized * 3.5f;
            bp = LevelKit.OnGround(bp);
            var hb = new MeshBuilder();
            hb.Submesh(0);
            hb.Box(new Vector3(0f, 0.12f, -1.15f), new Vector3(2.6f, 0.3f, 0.3f), Quaternion.identity, Color.white, 0.5f);
            hb.Box(new Vector3(0f, 0.12f, 1.15f), new Vector3(2.6f, 0.3f, 0.3f), Quaternion.identity, Color.white, 0.5f);
            hb.Box(new Vector3(-1.15f, 0.12f, 0f), new Vector3(0.3f, 0.3f, 2.0f), Quaternion.identity, Color.white, 0.5f);
            hb.Box(new Vector3(1.15f, 0.12f, 0f), new Vector3(0.3f, 0.3f, 2.0f), Quaternion.identity, Color.white, 0.5f);
            hb.Submesh(1);
            hb.Box(new Vector3(0f, 0.28f, -1.15f), new Vector3(2.62f, 0.04f, 0.32f), Quaternion.identity, Color.white);
            hb.Submesh(2);
            for (int k = 0; k < 7; k++)
                hb.Sphere(new Vector3(LevelKit.Rand(rng, -1.4f, 1.4f), 0.1f, LevelKit.Rand(rng, -1.4f, 1.4f)), LevelKit.Rand(rng, 0.12f, 0.28f), 8, 6, Color.white);
            var fungus = LevelKit.Emissive("BunkerFungus", new Color(0.45f, 0.35f, 0.7f), OverhaulPalette.Hdr(OverhaulPalette.SporeViolet, 1.2f));
            LevelKit.Mesh(root, "Bunker Hatch Frame", hb, new[] { LevelKit.Concrete, LevelKit.Hazard, fungus }, bp, Quaternion.LookRotation(toSpawn.sqrMagnitude > 0.01f ? toSpawn.normalized : Vector3.forward), true);
        }

        // ---- horizon ------------------------------------------------------------------------------------
        static void Horizon(Transform root, System.Random rng)
        {
            Vector3 c = (OverhaulLandmarks.Spawn + OverhaulLandmarks.GroveCenter + OverhaulLandmarks.Windmill + OverhaulLandmarks.Obelisk) * 0.25f;
            var mesas = new MeshBuilder();
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f + LevelKit.Rand(rng, -0.2f, 0.2f);
                float r = LevelKit.Rand(rng, 80f, 112f);
                Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.y = LevelKit.GroundY(p.x, p.z) - c.y - 2f;
                float w = LevelKit.Rand(rng, 9f, 18f), h = LevelKit.Rand(rng, 7f, 16f);
                var prof = new List<Vector2>
                {
                    new Vector2(w * 1.25f, 0f), new Vector2(w * 1.05f, h * 0.25f), new Vector2(w, h * 0.55f), new Vector2(w * 0.92f, h * 0.85f),
                    new Vector2(w * 0.86f, h), new Vector2(w * 0.4f, h * 1.02f), new Vector2(0.001f, h * 1.03f),
                };
                mesas.Lathe(p, Quaternion.Euler(0f, LevelKit.Rand(rng, 0f, 360f), 0f), prof, 9, Color.white, true, 0.08f);
            }
            LevelKit.Mesh(root, "Distant Mesas", mesas, LevelKit.MarsRock, c, Quaternion.identity, false, true);

            var domes = new MeshBuilder();
            var towers = new MeshBuilder();
            for (int i = 0; i < 2; i++)
            {
                float a = LevelKit.Rand(rng, 0.6f, 2.4f) + i * 1.5f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * LevelKit.Rand(rng, 58f, 72f);
                p.y = LevelKit.GroundY(p.x, p.z) - c.y - 0.3f;
                var prof = new List<Vector2>();
                float rr = LevelKit.Rand(rng, 6f, 9f);
                for (int k = 0; k <= 8; k++) { float t = k / 8f * Mathf.PI * 0.5f; prof.Add(new Vector2(Mathf.Cos(t) * rr + 0.001f, Mathf.Sin(t) * rr)); }
                domes.Lathe(p, Quaternion.identity, prof, 18, Color.white, false, 0.3f);
            }
            {
                float a = LevelKit.Rand(rng, 3.4f, 5.5f);
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 56f;
                p.y = LevelKit.GroundY(p.x, p.z) - c.y;
                for (int k = 0; k < 4; k++)
                {
                    float b = k * Mathf.PI * 0.5f;
                    towers.Strut(p + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * 1.6f, p + Vector3.up * 22f, 0.12f, 5, Color.white, true);
                }
                towers.Strut(p + Vector3.up * 22f, p + new Vector3(2.5f, 17f, 1f), 0.1f, 5, Color.white, true);
            }
            LevelKit.Mesh(root, "Distant Domes", domes, LevelKit.JoyPaintDark, c, Quaternion.identity, false, false);
            LevelKit.Mesh(root, "Broken Comms Tower", towers, LevelKit.Rust, c, Quaternion.identity, false, false);
        }
    }
}
