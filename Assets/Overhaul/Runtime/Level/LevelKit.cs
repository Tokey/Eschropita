using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Shared helpers for the runtime level dressing: a cached palette of HDRP materials built from the CC0 texture
    /// sets in Resources/Overhaul/Textures (painted JoyTech metal, rust, concrete, lichen rock, grove soil, Mars soil),
    /// procedural hazard stripes, ink outlines, mesh objects with sensible renderer settings, colliders and nav-mesh
    /// carving, terrain sampling and simple scattering. Everything it creates is destroyed with the dressing root.
    /// </summary>
    public static class LevelKit
    {
        static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        static readonly List<Object> _owned = new List<Object>();
        static Material _outline;

        public static void Release()
        {
            foreach (var o in _owned) if (o != null) Object.Destroy(o);
            _owned.Clear();
            _mats.Clear();
            _outline = null;
        }

        public static T Own<T>(T o) where T : Object { if (o != null) _owned.Add(o); return o; }

        static Texture2D Tex(string name) => Resources.Load<Texture2D>("Overhaul/Textures/" + name);

        /// <summary>HDRP/Lit with a texture set (albedo/normal/mask) tinted by <paramref name="tint"/>; tiles every <paramref name="metresPerTile"/>.</summary>
        public static Material Textured(string key, string set, Color tint, float smoothness, float metallic, float metresPerTile, Color? emissive = null)
        {
            string id = key + "|" + set;
            if (_mats.TryGetValue(id, out var m) && m != null) return m;
            float t = 1f / Mathf.Max(0.05f, metresPerTile);
            m = OverhaulMaterials.Lit(new LitSpec
            {
                name = key, color = tint, smoothness = smoothness, metallic = metallic,
                baseMap = Tex(set + "_Albedo"), normalMap = Tex(set + "_Normal"), maskMap = Tex(set + "_Mask"),
                tiling = new Vector2(t, t), emissive = emissive ?? Color.black,
            });
            _mats[id] = Own(m);
            return m;
        }

        public static Material Plain(string key, Color color, float smoothness = 0.4f, float metallic = 0f, Color? emissive = null)
        {
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            m = OverhaulMaterials.Lit(new LitSpec { name = key, color = color, smoothness = smoothness, metallic = metallic, emissive = emissive ?? Color.black });
            _mats[key] = Own(m);
            return m;
        }

        /// <summary>A fresh (unshared) emissive material the caller animates.</summary>
        public static Material Emissive(string key, Color baseColor, Color hdr)
            => Own(OverhaulMaterials.Lit(new LitSpec { name = key, color = baseColor, smoothness = 0.6f, emissive = hdr }));

        public static Material Glass(string key, Color tint, float alpha)
        {
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            m = OverhaulMaterials.Lit(new LitSpec { name = key, color = tint, smoothness = 0.92f, transparent = true, alpha = alpha, doubleSided = true });
            _mats[key] = Own(m);
            return m;
        }

        // ---- common looks ---------------------------------------------------------------------------------
        public static Material JoyPaint => Textured("JoyPaint", "MetalPlate", OverhaulPalette.JoyWhite, 0.55f, 0.0001f, 2f);
        public static Material JoyPaintDark => Textured("JoyPaintDark", "MetalPlate", OverhaulPalette.JoyGrey, 0.5f, 0.0001f, 2f);
        public static Material Rust => Textured("Rust", "RustyMetal", new Color(1f, 0.92f, 0.85f), 0.6f, 0.6f, 2f);
        public static Material Concrete => Textured("Concrete", "Concrete", new Color(0.85f, 0.78f, 0.72f), 0.5f, 0.0001f, 3f);
        public static Material Lichen => Textured("Lichen", "LichenRock", new Color(0.82f, 0.74f, 0.8f), 0.45f, 0.0001f, 2.5f);
        public static Material GroveSoil => Textured("GroveSoil", "GroveSoil", new Color(0.42f, 0.3f, 0.42f), 0.35f, 0.0001f, 3f);
        public static Material MarsRock => Textured("MarsRock", "MarsSoil", new Color(0.95f, 0.7f, 0.58f), 0.3f, 0.0001f, 6f);
        public static Material Corrugated => Textured("Corrugated", "CorrugatedIron", new Color(0.9f, 0.85f, 0.8f), 0.45f, 0.4f, 2f);
        public static Material Trim => Plain("Trim", OverhaulPalette.JoyDark, 0.35f, 0.3f);
        public static Material Teal => Plain("JoyTeal", OverhaulPalette.JoyTeal, 0.55f);
        public static Material Magenta => Plain("JoyMagenta", OverhaulPalette.JoyMagenta, 0.5f);
        public static Material Cable => Plain("Cable", new Color(0.08f, 0.08f, 0.09f), 0.3f);

        /// <summary>Yellow/black hazard stripes (generated, tiles along U).</summary>
        public static Material Hazard
        {
            get
            {
                if (_mats.TryGetValue("Hazard", out var m) && m != null) return m;
                const int n = 64;
                var t = Own(new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "HazardStripes", wrapMode = TextureWrapMode.Repeat });
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        bool yellow = ((x + y) / (n / 4)) % 2 == 0;
                        px[y * n + x] = yellow ? new Color(0.93f, 0.66f, 0.12f) : new Color(0.08f, 0.07f, 0.06f);
                    }
                t.SetPixels(px);
                t.Apply(true, false);
                m = OverhaulMaterials.Lit(new LitSpec { name = "Hazard", color = Color.white, smoothness = 0.45f, baseMap = t, tiling = new Vector2(1.5f, 1.5f) });
                _mats["Hazard"] = Own(m);
                return m;
            }
        }

        public static Material Outline
        {
            get
            {
                if (_outline != null) return _outline;
                _outline = Own(OverhaulOutline.CreateMaterial(OverhaulOutline.Ink, 2f, 0.1f));
                return _outline;
            }
        }

        // ---- objects ---------------------------------------------------------------------------------------
        /// <summary>Builds a mesh object from a MeshBuilder (one material per submesh), optionally ink-outlined.</summary>
        public static GameObject Mesh(Transform parent, string name, MeshBuilder mb, Material[] mats, Vector3 pos, Quaternion rot,
                                      bool outline = true, bool shadows = true)
        {
            var mesh = Own(mb.ToMesh(name));
            if (outline) OverhaulOutline.BakeSmoothNormals(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var use = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < use.Length; i++) use[i] = mats[Mathf.Min(i, mats.Length - 1)];
            mr.sharedMaterials = use;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (outline && Outline != null) OverhaulOutline.AddHull(go, Outline);
            return go;
        }

        public static GameObject Mesh(Transform parent, string name, MeshBuilder mb, Material mat, Vector3 pos, Quaternion rot, bool outline = true, bool shadows = true)
            => Mesh(parent, name, mb, new[] { mat }, pos, rot, outline, shadows);

        public static BoxCollider Box(GameObject go, Vector3 center, Vector3 size)
        {
            var c = go.AddComponent<BoxCollider>();
            c.center = center;
            c.size = size;
            return c;
        }

        public static void CarveBox(GameObject go, Vector3 center, Vector3 size)
        {
            var o = go.AddComponent<NavMeshObstacle>();
            o.shape = NavMeshObstacleShape.Box;
            o.center = center;
            o.size = size;
            o.carving = true;
            o.carveOnlyStationary = true;
        }

        public static void CarveCapsule(GameObject go, Vector3 center, float radius, float height)
        {
            var o = go.AddComponent<NavMeshObstacle>();
            o.shape = NavMeshObstacleShape.Capsule;
            o.center = center;
            o.radius = radius;
            o.height = height;
            o.carving = true;
            o.carveOnlyStationary = true;
        }

        // ---- terrain ---------------------------------------------------------------------------------------
        public static float GroundY(float x, float z)
        {
            var t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y : 0f;
        }

        public static Vector3 OnGround(Vector3 p, float lift = 0f) { p.y = GroundY(p.x, p.z) + lift; return p; }

        /// <summary>Highest walkable (non-trigger) surface under x,z — terrain or any collider — ignoring <paramref name="ignore"/>.</summary>
        public static float SurfaceY(float x, float z, Transform ignore = null)
        {
            float best = GroundY(x, z);
            var hits = Physics.RaycastAll(new Vector3(x, best + 40f, z), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (ignore != null && h.transform.IsChildOf(ignore)) continue;
                var col = h.collider;
                if (col is TerrainCollider || col is CapsuleCollider || col is SphereCollider || col is CharacterController) continue;
                if (col.attachedRigidbody != null || col.GetComponentInParent<MushroomPad>() != null || col.GetComponentInParent<NPCFlocker>() != null ||
                    col.GetComponentInParent<PlayerController>() != null || col.GetComponentInParent<PadButton>() != null) continue;
                if (h.point.y > best && h.point.y < best + 1.5f) best = h.point.y;
            }
            return best;
        }

        /// <summary>Lowest terrain height around a footprint (so foundations never float).</summary>
        public static float MinGround(Vector3 c, float r)
        {
            float m = GroundY(c.x, c.z);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                m = Mathf.Min(m, GroundY(c.x + Mathf.Cos(a) * r, c.z + Mathf.Sin(a) * r));
            }
            return m;
        }

        /// <summary>Distance in XZ from p to the segment a–b.</summary>
        public static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
            Vector2 ab = B - A;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(P, A + ab * t);
        }

        /// <summary>True if p keeps clear of every gameplay corridor and landmark (for scattering props).</summary>
        public static bool Clear(Vector3 p, float margin)
        {
            Vector3 g = OverhaulLandmarks.GroveCenter, w = OverhaulLandmarks.Windmill, o = OverhaulLandmarks.Obelisk, h = OverhaulLandmarks.Home, s = OverhaulLandmarks.Spawn;
            if (DistToSegment(p, g, w) < margin + 1.5f || DistToSegment(p, g, o) < margin + 1.5f || DistToSegment(p, g, h) < margin + 1.5f) return false;
            if (DistToSegment(p, s, g) < margin + 1f || DistToSegment(p, s, w) < margin + 1f || DistToSegment(p, s, o) < margin + 1f) return false;
            foreach (var q in new[] { g, w, o, h, s, OverhaulLandmarks.LifeSupportPlant, OverhaulLandmarks.Shuttle, OverhaulLandmarks.DomeCenter })
            {
                float r = q == OverhaulLandmarks.DomeCenter ? OverhaulLandmarks.DomeRadius + 2f : (q == g ? 9f : 6f);
                Vector3 d = p - q; d.y = 0f;
                if (q != Vector3.zero && d.magnitude < r + margin) return false;
            }
            return true;
        }

        public static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
    }
}
