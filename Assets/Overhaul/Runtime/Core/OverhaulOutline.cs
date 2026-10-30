using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Ink outlines for 3D structures (inverted hull, shader "Eschropita/Outline") so they match the painted,
    /// outlined sprites. Usage (builders): bake smooth normals into the mesh BEFORE saving it, then add the hull:
    ///   OverhaulOutline.BakeSmoothNormals(mesh);
    ///   OverhaulOutline.AddHull(go, outlineMaterial);
    /// Keep one shared outline material per look (editor builders save it once as an asset).
    /// </summary>
    public static class OverhaulOutline
    {
        public const string ShaderName = "Eschropita/Outline";
        public static readonly Color Ink = new Color(0.09f, 0.06f, 0.07f, 1f);

        public static Material CreateMaterial(Color color, float widthPx = 2.2f, float maxWorldWidth = 0.12f)
        {
            var sh = Resources.Load<Shader>("Overhaul/Shaders/EschropitaOutline");
            if (sh == null) sh = Shader.Find(ShaderName);
            if (sh == null || !sh.isSupported) return null;
            var m = new Material(sh) { name = "Outline" };
            m.SetColor("_OutlineColor", color);
            m.SetFloat("_OutlineWidth", widthPx);
            m.SetFloat("_MaxWorldWidth", maxWorldWidth);
            return m;
        }

        /// <summary>
        /// Writes position-averaged normals into UV channel 3, so the hull stays closed at hard edges.
        /// Call before saving the mesh asset.
        /// </summary>
        public static void BakeSmoothNormals(Mesh mesh)
        {
            if (mesh == null) return;
            var v = mesh.vertices;
            var n = mesh.normals;
            if (n == null || n.Length != v.Length) { mesh.RecalculateNormals(); n = mesh.normals; }
            var sum = new Dictionary<Vector3Int, Vector3>(v.Length);
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f), Mathf.RoundToInt(p.z * 1000f));
            for (int i = 0; i < v.Length; i++)
            {
                var k = Key(v[i]);
                sum.TryGetValue(k, out Vector3 s);
                sum[k] = s + n[i];
            }
            var smooth = new List<Vector3>(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 s = sum[Key(v[i])];
                smooth.Add(s.sqrMagnitude > 1e-8f ? s.normalized : n[i]);
            }
            mesh.SetUVs(3, smooth);
        }

        /// <summary>Adds a child renderer drawing the same mesh with the outline material. Returns the child.</summary>
        public static GameObject AddHull(GameObject source, Material outline)
        {
            if (source == null || outline == null) return null;
            var mf = source.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return null;
            var go = new GameObject(source.name + " (outline)");
            go.transform.SetParent(source.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = outline;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            go.isStatic = source.isStatic;
            return go;
        }
    }
}
