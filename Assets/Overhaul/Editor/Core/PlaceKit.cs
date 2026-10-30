using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Eschropita.Overhaul.EditorTools
{
    /// <summary>
    /// Placement helpers for builders: terrain height/normal sampling, creating mesh objects with sensible HDRP
    /// renderer settings, colliders, nav-mesh carving and static flags.
    /// </summary>
    public static class PlaceKit
    {
        static Terrain _terrain;

        public static Terrain Terrain
        {
            get
            {
                if (_terrain == null) _terrain = Terrain.activeTerrain;
                if (_terrain == null) _terrain = Object.FindAnyObjectByType<Terrain>();
                return _terrain;
            }
        }

        public static void ResetCache() { _terrain = null; }

        /// <summary>Terrain height (world Y) at x, z. Ignores rocks/props.</summary>
        public static float TerrainY(float x, float z)
        {
            var t = Terrain;
            if (t == null) return 0f;
            return t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
        }

        public static Vector3 OnTerrain(float x, float z, float yOffset = 0f) => new Vector3(x, TerrainY(x, z) + yOffset, z);
        public static Vector3 OnTerrain(Vector3 p, float yOffset = 0f) => OnTerrain(p.x, p.z, yOffset);

        public static Vector3 TerrainNormal(float x, float z)
        {
            var t = Terrain;
            if (t == null || t.terrainData == null) return Vector3.up;
            Vector3 tp = t.transform.position, size = t.terrainData.size;
            return t.terrainData.GetInterpolatedNormal((x - tp.x) / size.x, (z - tp.z) / size.z);
        }

        /// <summary>Lowest terrain height within a radius (to sink foundations so no edge floats).</summary>
        public static float MinTerrainY(Vector3 center, float radius, int samples = 12)
        {
            float m = TerrainY(center.x, center.z);
            for (int i = 0; i < samples; i++)
            {
                float a = i * Mathf.PI * 2f / samples;
                m = Mathf.Min(m, TerrainY(center.x + Mathf.Cos(a) * radius, center.z + Mathf.Sin(a) * radius));
            }
            return m;
        }

        /// <summary>Highest non-trigger surface (terrain, rocks, props) under x, z; ignores anything under <paramref name="ignore"/>.</summary>
        public static float SurfaceY(float x, float z, Transform ignore = null)
        {
            Physics.SyncTransforms();
            float best = TerrainY(x, z);
            var hits = Physics.RaycastAll(new Vector3(x, 500f, z), Vector3.down, 1000f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (ignore != null && h.transform.IsChildOf(ignore)) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return best;
        }

        public static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>
        /// Creates a GameObject with MeshFilter + MeshRenderer. Mesh/material assets should already be saved.
        /// </summary>
        public static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material[] mats,
                                            Vector3 worldPos, Quaternion worldRot,
                                            ShadowCastingMode shadows = ShadowCastingMode.On, bool isStatic = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(worldPos, worldRot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = shadows;
            mr.receiveShadows = true;
            if (isStatic) SetStatic(go);
            return go;
        }

        public static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material mat, Vector3 worldPos, Quaternion worldRot,
                                            ShadowCastingMode shadows = ShadowCastingMode.On, bool isStatic = true)
            => MeshObject(parent, name, mesh, new[] { mat }, worldPos, worldRot, shadows, isStatic);

        /// <summary>Static for batching/occlusion/lightmaps, minus navigation (we carve with obstacles instead).</summary>
        public static void SetStatic(GameObject go, bool includeChildren = false)
        {
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                        StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            if (includeChildren)
                foreach (Transform c in go.transform) SetStatic(c.gameObject, true);
        }

        public static void ClearStatic(GameObject go, bool includeChildren = true)
        {
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            if (includeChildren)
                foreach (Transform c in go.transform) ClearStatic(c.gameObject, true);
        }

        public static BoxCollider BoxCollider(GameObject go, Vector3 localCenter, Vector3 size)
        {
            var c = go.AddComponent<BoxCollider>();
            c.center = localCenter;
            c.size = size;
            return c;
        }

        public static CapsuleCollider CapsuleCollider(GameObject go, Vector3 localCenter, float radius, float height)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.center = localCenter;
            c.radius = radius;
            c.height = height;
            c.direction = 1;
            return c;
        }

        /// <summary>Carves the baked nav mesh at runtime so Mycari walk around the object.</summary>
        public static NavMeshObstacle Carve(GameObject go, Vector3 localCenter, Vector3 size)
        {
            var o = go.AddComponent<NavMeshObstacle>();
            o.shape = NavMeshObstacleShape.Box;
            o.center = localCenter;
            o.size = size;
            o.carving = true;
            o.carveOnlyStationary = true;
            return o;
        }

        public static NavMeshObstacle CarveCapsule(GameObject go, Vector3 localCenter, float radius, float height)
        {
            var o = go.AddComponent<NavMeshObstacle>();
            o.shape = NavMeshObstacleShape.Capsule;
            o.center = localCenter;
            o.radius = radius;
            o.height = height;
            o.carving = true;
            o.carveOnlyStationary = true;
            return o;
        }

        /// <summary>Deterministic jittered points in a disc (Poisson-ish), for scattering props.</summary>
        public static List<Vector2> Scatter(System.Random rng, Vector2 center, float radius, float minSpacing, int count, int tries = 30)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < count * tries && pts.Count < count; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Sqrt((float)rng.NextDouble()) * radius;
                var p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                bool ok = true;
                foreach (var q in pts) if ((q - p).sqrMagnitude < minSpacing * minSpacing) { ok = false; break; }
                if (ok) pts.Add(p);
            }
            return pts;
        }

        public static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
    }
}
