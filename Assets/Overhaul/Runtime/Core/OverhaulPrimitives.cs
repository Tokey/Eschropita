using UnityEngine;
using UnityEngine.Rendering;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Tiny shared meshes and renderer helpers for runtime effects (glow discs on the ground, billboards, …).
    /// Meshes are created once and shared; never destroy them from a caller.
    /// </summary>
    public static class OverhaulPrimitives
    {
        static Mesh _groundQuad, _uprightQuad;

        /// <summary>1×1 quad lying on the XZ plane, centred, facing up. White vertex colours (for Soft Glow).</summary>
        public static Mesh GroundQuad
        {
            get
            {
                if (_groundQuad != null) return _groundQuad;
                _groundQuad = BuildQuad("OverhaulGroundQuad", true);
                return _groundQuad;
            }
        }

        /// <summary>1×1 quad on the XY plane, centred, facing -Z (towards a default camera).</summary>
        public static Mesh UprightQuad
        {
            get
            {
                if (_uprightQuad != null) return _uprightQuad;
                _uprightQuad = BuildQuad("OverhaulUprightQuad", false);
                return _uprightQuad;
            }
        }

        static Mesh BuildQuad(string name, bool ground)
        {
            var m = new Mesh { name = name };
            if (ground)
            {
                m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
                m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            }
            else
            {
                m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
                m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            }
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Creates a child object drawing <paramref name="mesh"/> with <paramref name="mat"/>, with effect-friendly renderer
        /// settings (no shadows, no probes, no motion vectors).
        /// </summary>
        public static MeshRenderer EffectRenderer(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return mr;
        }

        /// <summary>Ground height under <paramref name="p"/> (terrain, else a downward raycast, else p.y).</summary>
        public static float GroundY(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            if (t != null) return t.SampleHeight(p) + t.transform.position.y;
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
            return p.y;
        }
    }
}
