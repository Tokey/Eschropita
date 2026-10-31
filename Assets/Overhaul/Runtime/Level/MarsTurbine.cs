using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Builds the Mars wind turbine at the windmill site (replacing the stock white turbine visually): an oversized
    /// slender three-blade rotor for thin Martian air (Ø ≈ 13 m) on a tapered, dust-weathered tower — rusted base,
    /// hazard band, tripod flare, access hatch, guy wires to concrete anchors, foundation pad with a sand drift — a
    /// nacelle with a solar panel, anemometer and red aviation beacon, hidden amber shard-shield plates and fungal
    /// repair patches for <see cref="MarsTurbineRig"/> to reveal. The rotor faces the player's usual view.
    /// </summary>
    public static class MarsTurbine
    {
        const float H = 11f;            // tower height
        const float BladeLength = 6.4f;

        public static MarsTurbineRig Build(Transform parent, Vector3 site, Vector3 faceTowards, Vector3? keepClear = null)
        {
            Vector3 f = faceTowards - site; f.y = 0f;
            Quaternion rot = Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f.normalized : Vector3.back, Vector3.up);
            float ground = LevelKit.MinGround(site, 2.4f);
            Vector3 origin = new Vector3(site.x, ground, site.z);

            var root = new GameObject("Mars Wind Turbine").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(origin, rot);

            // --- foundation, sand drift, tripod feet ---
            var mb = new MeshBuilder();
            mb.Submesh(0);
            mb.Cylinder(new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.18f, 0f), 2.5f, 2.3f, 8, Color.white, true, true, 0.5f);
            mb.Submesh(1);
            mb.Lathe(new Vector3(0f, -0.25f, 0f), Quaternion.identity, new List<Vector2> { new Vector2(4.2f, 0f), new Vector2(3.1f, 0.18f), new Vector2(2.45f, 0.3f), new Vector2(2.2f, 0.28f) }, 28, Color.white, false, 0.3f);
            LevelKit.Mesh(root, "Foundation", mb, new[] { LevelKit.Concrete, LevelKit.MarsRock }, origin, rot, true);

            // --- tower: rust base / hazard band / painted shaft, tripod feet, access hatch ---
            var tb = new MeshBuilder();
            System.Func<float, float> radius = y => Mathf.Lerp(0.95f, 0.42f, Mathf.Pow(Mathf.Clamp01(y / H), 0.85f));
            tb.Submesh(0);
            tb.Lathe(Vector3.zero, Quaternion.identity, Profile(2.2f, H, radius), 22, Color.white, false, 0.5f);
            tb.Submesh(1);
            tb.Lathe(Vector3.zero, Quaternion.identity, new List<Vector2> { new Vector2(1.25f, 0.1f), new Vector2(1.02f, 0.45f), new Vector2(radius(0.9f), 0.9f), new Vector2(radius(1.6f), 1.6f) }, 22, Color.white, false, 0.5f);
            tb.Submesh(2);
            tb.Lathe(Vector3.zero, Quaternion.identity, new List<Vector2> { new Vector2(radius(1.6f) + 0.01f, 1.6f), new Vector2(radius(2.2f) + 0.01f, 2.2f) }, 22, Color.white, false, 0.6f);
            tb.Submesh(3);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad + Mathf.PI / 6f;
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                tb.Strut(d * 0.85f + Vector3.up * 1.5f, d * 2.05f + Vector3.up * 0.15f, 0.11f, 8, Color.white, true);
                tb.Box(d * 2.05f + Vector3.up * 0.12f, new Vector3(0.45f, 0.18f, 0.45f), Quaternion.LookRotation(d), Color.white);
            }
            tb.Box(new Vector3(0f, 1.25f, radius(1.25f) - 0.02f), new Vector3(0.7f, 1.5f, 0.12f), Quaternion.identity, Color.white);
            LevelKit.Mesh(root, "Tower", tb, new[] { LevelKit.JoyPaint, LevelKit.Rust, LevelKit.Hazard, LevelKit.Trim }, origin, rot, true);

            // --- guy wires to anchors on the terrain ---
            var wires = new MeshBuilder();
            var anchors = new MeshBuilder();
            // anchors sit 60° either side of anything that must stay clear (the life-support plant), else behind
            float clearDeg = 0f;
            if (keepClear.HasValue)
            {
                Vector3 kc = Quaternion.Inverse(rot) * (keepClear.Value - origin);
                clearDeg = Mathf.Atan2(kc.x, kc.z) * Mathf.Rad2Deg;
            }
            for (int i = 0; i < 3; i++)
            {
                float a = (clearDeg + i * 120f + 60f) * Mathf.Deg2Rad;
                Vector3 dirW = rot * new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 anchorW = LevelKit.OnGround(origin + dirW * 7.2f, 0.1f);
                Vector3 anchorL = Quaternion.Inverse(rot) * (anchorW - origin);
                Vector3 topL = Quaternion.Inverse(rot) * (dirW * (radius(7.6f) + 0.02f)) + Vector3.up * 7.6f;
                wires.Strut(topL, anchorL + Vector3.up * 0.25f, 0.025f, 5, Color.white);
                anchors.Box(anchorL, new Vector3(0.7f, 0.5f, 0.7f), Quaternion.LookRotation(Quaternion.Inverse(rot) * dirW), Color.white);
            }
            LevelKit.Mesh(root, "Guy Wires", wires, LevelKit.Cable, origin, rot, false, false);
            LevelKit.Mesh(root, "Wire Anchors", anchors, LevelKit.Concrete, origin, rot, true);

            // --- nacelle (yaws), rotor (spins), beacon, anemometer ---
            var nacelle = new GameObject("Nacelle").transform;
            nacelle.SetParent(root, false);
            nacelle.localPosition = new Vector3(0f, H + 0.45f, 0f);
            var nb = new MeshBuilder();
            nb.Submesh(0);
            nb.Lathe(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new List<Vector2>
            {
                new Vector2(0.001f, -1.75f), new Vector2(0.32f, -1.68f), new Vector2(0.52f, -1.45f), new Vector2(0.6f, -1.05f),
                new Vector2(0.62f, 0.75f), new Vector2(0.56f, 1.05f), new Vector2(0.46f, 1.2f),
            }, 18, Color.white, false, 0.5f);
            nb.Submesh(1);
            nb.Box(new Vector3(0f, 0.72f, -0.35f), new Vector3(1.5f, 0.05f, 1.0f), Quaternion.Euler(-12f, 0f, 0f), Color.white);
            nb.Submesh(2);
            nb.Box(new Vector3(0f, 0.66f, -0.35f), new Vector3(1.6f, 0.08f, 1.1f), Quaternion.Euler(-12f, 0f, 0f), Color.white);
            nb.Strut(new Vector3(0f, 0.4f, -1.3f), new Vector3(0f, 1.15f, -1.3f), 0.03f, 6, Color.white);
            nb.Strut(new Vector3(0f, -0.55f, 0f), new Vector3(0f, -0.9f, 0f), 0.3f, 12, Color.white, false);
            var solar = LevelKit.Plain("SolarCells", new Color(0.08f, 0.12f, 0.22f), 0.9f, 0.3f);
            LevelKit.Mesh(nacelle, "Nacelle Body", nb, new[] { LevelKit.JoyPaint, solar, LevelKit.Trim }, nacelle.position, nacelle.rotation, true);

            var beaconGo = new GameObject("Beacon");
            beaconGo.transform.SetParent(nacelle, false);
            beaconGo.transform.localPosition = new Vector3(0f, 0.7f, -1.55f);
            var bm = new MeshBuilder();
            bm.Sphere(Vector3.zero, 0.13f, 12, 8, Color.white);
            var beaconMat = LevelKit.Emissive("TurbineBeacon", new Color(0.6f, 0.05f, 0.05f), new Color(4f, 0.2f, 0.15f));
            LevelKit.Mesh(beaconGo.transform, "Beacon Bulb", bm, beaconMat, beaconGo.transform.position, beaconGo.transform.rotation, false, false);
            var beaconLight = OverhaulLights.Point(beaconGo, new Color(1f, 0.15f, 0.1f), 300f, 7f, true, false);

            var anemo = new GameObject("Anemometer").transform;
            anemo.SetParent(nacelle, false);
            anemo.localPosition = new Vector3(0f, 1.18f, -1.3f);
            var am = new MeshBuilder();
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                am.Strut(Vector3.zero, d * 0.3f, 0.012f, 4, Color.white);
                am.Sphere(d * 0.3f, 0.06f, 8, 5, Color.white);
            }
            LevelKit.Mesh(anemo, "Cups", am, LevelKit.Trim, anemo.position, anemo.rotation, false, false);

            var rotor = new GameObject("Rotor").transform;
            rotor.SetParent(nacelle, false);
            rotor.localPosition = new Vector3(0f, 0f, 1.3f);
            var hub = new MeshBuilder();
            hub.Lathe(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new List<Vector2>
            {
                new Vector2(0.5f, -0.05f), new Vector2(0.48f, 0.25f), new Vector2(0.38f, 0.52f), new Vector2(0.2f, 0.74f), new Vector2(0.001f, 0.82f),
            }, 18, Color.white, false, 0.5f);
            LevelKit.Mesh(rotor, "Spinner", hub, LevelKit.JoyPaint, rotor.position, rotor.rotation, true);
            var blades = new MeshBuilder();
            for (int i = 0; i < 3; i++)
            {
                var r = Quaternion.AngleAxis(i * 120f, Vector3.forward);
                blades.Submesh(0);
                Blade(blades, r, 0.3f, BladeLength * 0.86f, true, false);
                blades.Submesh(1);
                Blade(blades, r, BladeLength * 0.86f, BladeLength, false, true);
            }
            var tip = LevelKit.Plain("BladeTip", new Color(0.78f, 0.2f, 0.12f), 0.5f);
            LevelKit.Mesh(rotor, "Blades", blades, new[] { LevelKit.JoyPaint, tip }, rotor.position, rotor.rotation, true);

            // --- shard-shield plates (hidden) ---
            var plates = new List<Transform>();
            var plateMesh = new MeshBuilder();
            plateMesh.Lathe(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new List<Vector2> { new Vector2(0.001f, -0.05f), new Vector2(0.58f, -0.05f), new Vector2(0.58f, 0.05f), new Vector2(0.001f, 0.1f) }, 6, Color.white, true, 1f);
            var amber = LevelKit.Emissive("ShieldPlate", OverhaulPalette.ShardAmber, OverhaulPalette.Hdr(OverhaulPalette.ShardAmber, 2.2f));
            for (int i = 0; i < 6; i++)
            {
                float y = 2.8f + (i / 2) * 1.6f;
                float a = (i * 60f + (i / 2) * 30f) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 lp = d * (radius(y) + 0.22f) + Vector3.up * y;
                var pgo = LevelKit.Mesh(root, "Shield Plate " + i, Clone(plateMesh), amber, root.TransformPoint(lp), rot * Quaternion.LookRotation(d), true, false);
                pgo.SetActive(false);
                plates.Add(pgo.transform);
            }

            // --- fungal repair patches (hidden, grow with repairs) ---
            var fungi = new List<Transform>();
            var fungus = LevelKit.Emissive("TurbineFungus", new Color(0.35f, 0.75f, 0.8f), OverhaulPalette.Hdr(OverhaulPalette.SporeTeal, 1.6f));
            var rng = new System.Random(4242);
            for (int i = 0; i < 9; i++)
            {
                float y = i < 3 ? 0.35f : LevelKit.Rand(rng, 1.4f, 7.5f);
                float a = LevelKit.Rand(rng, 0f, Mathf.PI * 2f);
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float r = i < 3 ? 1.6f + LevelKit.Rand(rng, 0f, 0.6f) : radius(y);
                var fm = new MeshBuilder();
                for (int k = 0; k < 4; k++)
                {
                    Vector3 o = new Vector3(LevelKit.Rand(rng, -0.25f, 0.25f), LevelKit.Rand(rng, -0.3f, 0.3f), LevelKit.Rand(rng, -0.08f, 0.08f));
                    fm.Sphere(o, LevelKit.Rand(rng, 0.12f, 0.24f), 10, 7, Color.white);
                }
                Vector3 lp = d * r + Vector3.up * y;
                var fg = LevelKit.Mesh(root, "Fungal Patch " + i, fm, fungus, root.TransformPoint(lp), rot * Quaternion.LookRotation(d), true, false);
                fg.transform.localScale = new Vector3(1f, 1f, 0.6f) * 0.001f;
                fungi.Add(fg.transform);
            }

            // collider + nav-mesh carving for the tower (the TaskSite keeps its own interaction collider)
            var colGo = new GameObject("Turbine Collider");
            colGo.transform.SetParent(root, false);
            var cap = colGo.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, H * 0.5f, 0f);
            cap.radius = 0.9f;
            cap.height = H;
            LevelKit.CarveCapsule(colGo, new Vector3(0f, 1.5f, 0f), 1.4f, 3f);

            var rig = root.gameObject.AddComponent<MarsTurbineRig>();
            rig.nacelle = nacelle;
            rig.rotor = rotor;
            rig.anemometer = anemo;
            rig.beaconLight = beaconLight;
            rig.beaconMaterial = beaconMat;
            rig.plates = plates.ToArray();
            rig.fungi = fungi.ToArray();
            rig.towerHeight = H;
            return rig;
        }

        static List<Vector2> Profile(float y0, float y1, System.Func<float, float> radius)
        {
            var p = new List<Vector2>();
            for (int i = 0; i <= 10; i++)
            {
                float y = Mathf.Lerp(y0, y1, i / 10f);
                p.Add(new Vector2(radius(y), y));
            }
            p.Add(new Vector2(radius(y1) * 0.85f, y1 + 0.08f));
            return p;
        }

        /// <summary>One blade from r0 to r1 along local +Y (rotated by <paramref name="r"/>): tapered, twisted thin airfoil.</summary>
        static void Blade(MeshBuilder mb, Quaternion r, float r0, float r1, bool capStart, bool capEnd)
        {
            var sections = new List<Vector3[]>();
            const int k = 10;
            for (int s = 0; s <= 8; s++)
            {
                float y = Mathf.Lerp(r0, r1, s / 8f);
                float u = Mathf.Clamp01((y - 0.3f) / (BladeLength - 0.3f));
                float chord = Mathf.Lerp(0.62f, 0.13f, Mathf.Pow(u, 0.8f)) * (u < 0.08f ? Mathf.Lerp(0.55f, 1f, u / 0.08f) : 1f);
                float thick = chord * Mathf.Lerp(0.26f, 0.12f, u);
                float twist = Mathf.Lerp(20f, 3f, u);
                var q = r * Quaternion.AngleAxis(twist, Vector3.up);
                var ring = new Vector3[k];
                for (int i = 0; i < k; i++)
                {
                    float a = i / (float)k * Mathf.PI * 2f;
                    float x = Mathf.Cos(a) * chord * 0.5f;
                    float z = Mathf.Sin(a) * thick * 0.5f * (Mathf.Cos(a) > 0f ? 0.7f : 1f);    // blunter leading edge
                    ring[i] = q * new Vector3(x - chord * 0.12f, 0f, z) + r * new Vector3(0f, y, 0f);
                }
                sections.Add(ring);
            }
            mb.Loft(sections, Color.white, capStart, capEnd, 0.5f);
        }

        static MeshBuilder Clone(MeshBuilder src)
        {
            var mb = new MeshBuilder();
            for (int i = 0; i < src.VertexCount; i++) mb.AddVertex(src.Vertices[i], src.Normals[i], src.UVs[i], src.Colors[i]);
            var tmp = src.ToMesh("tmp", false, false);
            var tris = tmp.triangles;
            for (int i = 0; i < tris.Length; i += 3) mb.Tri(tris[i], tris[i + 1], tris[i + 2]);
            LevelKit.Dispose(tmp);
            return mb;
        }
    }
}
