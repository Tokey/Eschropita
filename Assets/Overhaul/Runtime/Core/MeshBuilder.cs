using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Small procedural mesh toolkit shared by every overhaul builder (editor bakes and runtime effects).
    ///
    /// Conventions:
    ///  - Every primitive takes WORLD-SCALE metres; UVs are in metres × <c>uvScale</c> so a texture tiles at a
    ///    consistent density across objects (uvScale 0.5 = one tile per 2 m).
    ///  - Triangles are wound automatically from the intended normals (<see cref="TriAuto"/>), so faces are never
    ///    inside-out regardless of how a primitive orders its vertices.
    ///  - <see cref="Submesh"/> selects which material slot following triangles go to.
    ///  - An optional transform (<see cref="SetTransform"/>) is applied to positions/normals as they are added.
    ///  - Vertex colour is stored (HDRP/Lit ignores it; custom shaders may use it, e.g. for glow masks).
    /// </summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();
        readonly List<List<int>> _submeshes = new List<List<int>> { new List<int>() };
        int _sub;
        Matrix4x4 _m = Matrix4x4.identity;
        Matrix4x4 _nm = Matrix4x4.identity;
        bool _hasM;

        public int VertexCount => Vertices.Count;
        public int SubmeshCount => _submeshes.Count;

        /// <summary>Selects the material slot for following triangles (creates slots as needed).</summary>
        public MeshBuilder Submesh(int index)
        {
            while (_submeshes.Count <= index) _submeshes.Add(new List<int>());
            _sub = index;
            return this;
        }

        public MeshBuilder SetTransform(Matrix4x4 m)
        {
            _m = m;
            _nm = m.inverse.transpose;
            _hasM = m != Matrix4x4.identity;
            return this;
        }

        public MeshBuilder ResetTransform() => SetTransform(Matrix4x4.identity);

        public int AddVertex(Vector3 p, Vector3 n, Vector2 uv, Color c)
        {
            if (_hasM)
            {
                p = _m.MultiplyPoint3x4(p);
                n = _nm.MultiplyVector(n).normalized;
            }
            Vertices.Add(p);
            Normals.Add(n);
            UVs.Add(uv);
            Colors.Add(c);
            return Vertices.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            var l = _submeshes[_sub];
            l.Add(a); l.Add(b); l.Add(c);
        }

        /// <summary>Adds a triangle wound so its face normal agrees with the vertices' normals.</summary>
        public void TriAuto(int a, int b, int c)
        {
            Vector3 pa = Vertices[a], pb = Vertices[b], pc = Vertices[c];
            Vector3 fn = Vector3.Cross(pb - pa, pc - pa);
            Vector3 vn = Normals[a] + Normals[b] + Normals[c];
            if (Vector3.Dot(fn, vn) < 0f) Tri(a, c, b);
            else Tri(a, b, c);
        }

        /// <summary>Quad from four corners in order around the edge (either direction).</summary>
        public void QuadAuto(int a, int b, int c, int d)
        {
            TriAuto(a, b, c);
            TriAuto(a, c, d);
        }

        // ---------------------------------------------------------------------------------------------------
        // Primitives
        // ---------------------------------------------------------------------------------------------------

        /// <summary>Flat-shaded box. UVs per face in metres × uvScale.</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rot, Color c, float uvScale = 1f)
        {
            Vector3 h = size * 0.5f;
            Vector3[] ns = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] vs = { Vector3.up, Vector3.up, Vector3.forward, Vector3.forward, Vector3.up, Vector3.up };
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = ns[f], v = vs[f];
                Vector3 u = Vector3.Cross(n, v);
                float hn = Mathf.Abs(Vector3.Dot(n, h)), hu = Mathf.Abs(Vector3.Dot(u, h)), hv = Mathf.Abs(Vector3.Dot(v, h));
                Vector3 fc = n * hn;
                Vector3 wn = rot * n;
                int i0 = AddVertex(center + rot * (fc - u * hu - v * hv), wn, new Vector2(-hu, -hv) * uvScale, c);
                int i1 = AddVertex(center + rot * (fc - u * hu + v * hv), wn, new Vector2(-hu, hv) * uvScale, c);
                int i2 = AddVertex(center + rot * (fc + u * hu + v * hv), wn, new Vector2(hu, hv) * uvScale, c);
                int i3 = AddVertex(center + rot * (fc + u * hu - v * hv), wn, new Vector2(hu, -hv) * uvScale, c);
                QuadAuto(i0, i1, i2, i3);
            }
        }

        /// <summary>Smooth-sided (optionally tapered) cylinder from a to b with optional flat caps.</summary>
        public void Cylinder(Vector3 a, Vector3 b, float radiusA, float radiusB, int segments, Color c,
                             bool capA = true, bool capB = true, float uvScale = 1f)
        {
            segments = Mathf.Max(3, segments);
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            Vector3 dir = axis / len;
            Frame(dir, out Vector3 p1, out Vector3 p2);
            float slope = (radiusA - radiusB) / len;
            int baseA = VertexCount;
            for (int s = 0; s <= segments; s++)
            {
                float t = (float)s / segments;
                float ang = t * Mathf.PI * 2f;
                Vector3 radial = Mathf.Cos(ang) * p1 + Mathf.Sin(ang) * p2;
                Vector3 n = (radial + dir * slope).normalized;
                float circ = Mathf.PI * (radiusA + radiusB);
                AddVertex(a + radial * radiusA, n, new Vector2(t * circ, 0f) * uvScale, c);
                AddVertex(b + radial * radiusB, n, new Vector2(t * circ, len) * uvScale, c);
            }
            for (int s = 0; s < segments; s++)
            {
                int i = baseA + s * 2;
                QuadAuto(i, i + 1, i + 3, i + 2);
            }
            if (capA && radiusA > 1e-5f) Disc(a, -dir, radiusA, segments, c, uvScale);
            if (capB && radiusB > 1e-5f) Disc(b, dir, radiusB, segments, c, uvScale);
        }

        /// <summary>Thin cylinder between two points (struts, cables, rods). No caps by default.</summary>
        public void Strut(Vector3 a, Vector3 b, float radius, int segments, Color c, bool caps = false, float uvScale = 1f)
            => Cylinder(a, b, radius, radius, segments, c, caps, caps, uvScale);

        /// <summary>Flat disc facing <paramref name="normal"/>.</summary>
        public void Disc(Vector3 center, Vector3 normal, float radius, int segments, Color c, float uvScale = 1f)
        {
            segments = Mathf.Max(3, segments);
            normal = normal.normalized;
            Frame(normal, out Vector3 p1, out Vector3 p2);
            int ci = AddVertex(center, normal, Vector2.zero, c);
            int first = VertexCount;
            for (int s = 0; s < segments; s++)
            {
                float ang = (float)s / segments * Mathf.PI * 2f;
                Vector3 o = (Mathf.Cos(ang) * p1 + Mathf.Sin(ang) * p2) * radius;
                AddVertex(center + o, normal, new Vector2(Vector3.Dot(o, p1), Vector3.Dot(o, p2)) * uvScale, c);
            }
            for (int s = 0; s < segments; s++)
                TriAuto(ci, first + s, first + (s + 1) % segments);
        }

        /// <summary>
        /// Surface of revolution around the local up axis at <paramref name="origin"/>.
        /// profile[i] = (radius, height). Smooth normals along the profile unless <paramref name="hard"/>.
        /// U runs around (metres of circumference at the profile's mean radius), V along the profile length.
        /// </summary>
        public void Lathe(Vector3 origin, Quaternion rot, IList<Vector2> profile, int segments, Color c,
                          bool hard = false, float uvScale = 1f, float arcDegrees = 360f)
        {
            if (profile == null || profile.Count < 2) return;
            segments = Mathf.Max(3, segments);
            bool full = arcDegrees >= 359.99f;
            float arc = arcDegrees * Mathf.Deg2Rad;
            float meanR = 0f;
            for (int i = 0; i < profile.Count; i++) meanR += profile[i].x;
            meanR /= profile.Count;
            float circ = Mathf.Max(0.01f, meanR * arc);

            // Profile tangents → 2D normals (pointing away from the axis for an outward-facing surface).
            int n = profile.Count;
            var len = new float[n];
            for (int i = 1; i < n; i++) len[i] = len[i - 1] + Vector2.Distance(profile[i], profile[i - 1]);

            if (!hard)
            {
                var pn = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    Vector2 t = profile[Mathf.Min(i + 1, n - 1)] - profile[Mathf.Max(i - 1, 0)];
                    pn[i] = new Vector2(t.y, -t.x).normalized; // rotate tangent -90°: outward when profile goes upward
                }
                int baseIdx = VertexCount;
                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / segments;
                    float ang = t * arc;
                    Vector3 radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p = origin + rot * (radial * profile[i].x + Vector3.up * profile[i].y);
                        Vector3 nn = rot * (radial * pn[i].x + Vector3.up * pn[i].y);
                        AddVertex(p, nn, new Vector2(t * circ, len[i]) * uvScale, c);
                    }
                }
                for (int s = 0; s < segments; s++)
                    for (int i = 0; i < n - 1; i++)
                    {
                        int a = baseIdx + s * n + i, b = a + 1, d = a + n, e = d + 1;
                        if ((profile[i].x < 1e-5f && profile[i + 1].x < 1e-5f)) continue;
                        QuadAuto(a, b, e, d);
                    }
            }
            else
            {
                for (int i = 0; i < n - 1; i++)
                {
                    Vector2 t = profile[i + 1] - profile[i];
                    Vector2 bn = new Vector2(t.y, -t.x).normalized;
                    int baseIdx = VertexCount;
                    for (int s = 0; s <= segments; s++)
                    {
                        float tt = (float)s / segments;
                        float ang = tt * arc;
                        Vector3 radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                        Vector3 nn = rot * (radial * bn.x + Vector3.up * bn.y);
                        AddVertex(origin + rot * (radial * profile[i].x + Vector3.up * profile[i].y), nn, new Vector2(tt * circ, len[i]) * uvScale, c);
                        AddVertex(origin + rot * (radial * profile[i + 1].x + Vector3.up * profile[i + 1].y), nn, new Vector2(tt * circ, len[i + 1]) * uvScale, c);
                    }
                    for (int s = 0; s < segments; s++)
                    {
                        int a = baseIdx + s * 2;
                        QuadAuto(a, a + 1, a + 3, a + 2);
                    }
                }
            }
            if (!full) { /* open arc: caller adds end caps if needed */ }
        }

        /// <summary>UV sphere (smooth).</summary>
        public void Sphere(Vector3 center, float radius, int segments, int rings, Color c, float uvScale = 1f)
        {
            rings = Mathf.Max(2, rings);
            var prof = new List<Vector2>(rings + 1);
            for (int i = 0; i <= rings; i++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * i / rings;
                prof.Add(new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius));
            }
            Lathe(center, Quaternion.identity, prof, segments, c, false, uvScale);
        }

        /// <summary>
        /// Pipe along a polyline with rotation-minimising frames. V = distance along the path (metres × uvScale),
        /// so a scrolling texture on V flows from the first point to the last.
        /// </summary>
        public void Tube(IList<Vector3> path, float radius, int segments, Color c, bool caps = true, float uvScale = 1f)
        {
            if (path == null || path.Count < 2) return;
            segments = Mathf.Max(3, segments);
            int n = path.Count;
            var tangents = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 t = path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)];
                tangents[i] = t.sqrMagnitude > 1e-10f ? t.normalized : Vector3.forward;
            }
            Frame(tangents[0], out Vector3 nrm, out _);
            float dist = 0f;
            int baseIdx = VertexCount;
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    dist += Vector3.Distance(path[i], path[i - 1]);
                    // parallel transport of the normal
                    Quaternion q = Quaternion.FromToRotation(tangents[i - 1], tangents[i]);
                    nrm = (q * nrm).normalized;
                }
                Vector3 bin = Vector3.Cross(tangents[i], nrm).normalized;
                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / segments;
                    float ang = t * Mathf.PI * 2f;
                    Vector3 radial = Mathf.Cos(ang) * nrm + Mathf.Sin(ang) * bin;
                    AddVertex(path[i] + radial * radius, radial, new Vector2(t * Mathf.PI * 2f * radius, dist) * uvScale, c);
                }
            }
            int ring = segments + 1;
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < segments; s++)
                {
                    int a = baseIdx + i * ring + s;
                    QuadAuto(a, a + 1, a + 1 + ring, a + ring);
                }
            if (caps)
            {
                Disc(path[0], -tangents[0], radius, segments, c, uvScale);
                Disc(path[n - 1], tangents[n - 1], radius, segments, c, uvScale);
            }
        }

        /// <summary>
        /// Flat strip along a path. <paramref name="ups"/> gives the strip's facing normal per point (e.g. ground
        /// normals); null = world up. U goes 0..1 across the width, V = distance (metres × uvScale).
        /// widthAt (optional) scales the width per point (0..1 along the path → multiplier).
        /// </summary>
        public void Ribbon(IList<Vector3> path, IList<Vector3> ups, float width, Color c, float uvScale = 1f, Func<float, float> widthAt = null)
        {
            if (path == null || path.Count < 2) return;
            int n = path.Count;
            float total = 0f;
            for (int i = 1; i < n; i++) total += Vector3.Distance(path[i], path[i - 1]);
            float dist = 0f;
            int baseIdx = VertexCount;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) dist += Vector3.Distance(path[i], path[i - 1]);
                Vector3 t = path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)];
                Vector3 up = ups != null && i < ups.Count ? ups[i] : Vector3.up;
                Vector3 side = Vector3.Cross(up, t).normalized;
                float w = width * 0.5f * (widthAt != null ? widthAt(total > 0f ? dist / total : 0f) : 1f);
                AddVertex(path[i] - side * w, up, new Vector2(0f, dist * uvScale), c);
                AddVertex(path[i] + side * w, up, new Vector2(1f, dist * uvScale), c);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = baseIdx + i * 2;
                QuadAuto(a, a + 1, a + 3, a + 2);
            }
        }

        /// <summary>
        /// Connects consecutive cross-sections (each the same vertex count, ordered around the section) into a
        /// closed skin. Normals are taken from each vertex's offset to its section centroid (good for convex-ish
        /// sections like blades, roots, stalks). Optionally caps both ends.
        /// </summary>
        public void Loft(IList<Vector3[]> sections, Color c, bool capStart = true, bool capEnd = true, float uvScale = 1f)
        {
            if (sections == null || sections.Count < 2) return;
            int k = sections[0].Length;
            if (k < 3) return;
            int baseIdx = VertexCount;
            float v = 0f;
            Vector3 prevCentroid = Centroid(sections[0]);
            for (int si = 0; si < sections.Count; si++)
            {
                var sec = sections[si];
                Vector3 cen = Centroid(sec);
                if (si > 0) v += Vector3.Distance(cen, prevCentroid);
                prevCentroid = cen;
                float perim = 0f;
                for (int j = 0; j <= k; j++)
                {
                    Vector3 p = sec[j % k];
                    if (j > 0) perim += Vector3.Distance(p, sec[(j - 1) % k]);
                    Vector3 nrm = (p - cen).sqrMagnitude > 1e-10f ? (p - cen).normalized : Vector3.up;
                    AddVertex(p, nrm, new Vector2(perim, v) * uvScale, c);
                }
            }
            int ring = k + 1;
            for (int si = 0; si < sections.Count - 1; si++)
                for (int j = 0; j < k; j++)
                {
                    int a = baseIdx + si * ring + j;
                    QuadAuto(a, a + 1, a + 1 + ring, a + ring);
                }
            if (capStart) CapSection(sections[0], -SectionAxis(sections, 0), c);
            if (capEnd) CapSection(sections[sections.Count - 1], SectionAxis(sections, sections.Count - 1), c);
        }

        /// <summary>
        /// Height-field grid: <paramref name="pos"/>(u, v) returns a world position for u, v in 0..1.
        /// Normals are computed from neighbouring samples. UV = (u, v) × uvTiles. keep(u, v) can drop cells
        /// (e.g. outside an organic outline).
        /// </summary>
        public void Grid(Func<float, float, Vector3> pos, int cellsU, int cellsV, Color c, Vector2 uvTiles,
                         Func<float, float, bool> keep = null, Func<float, float, Color> colorAt = null)
        {
            cellsU = Mathf.Max(1, cellsU);
            cellsV = Mathf.Max(1, cellsV);
            int baseIdx = VertexCount;
            float du = 1f / cellsU, dv = 1f / cellsV;
            for (int j = 0; j <= cellsV; j++)
                for (int i = 0; i <= cellsU; i++)
                {
                    float u = i * du, v = j * dv;
                    Vector3 p = pos(u, v);
                    Vector3 pu = pos(Mathf.Min(u + du, 1f), v) - pos(Mathf.Max(u - du, 0f), v);
                    Vector3 pv = pos(u, Mathf.Min(v + dv, 1f)) - pos(u, Mathf.Max(v - dv, 0f));
                    Vector3 n = Vector3.Cross(pv, pu).normalized;
                    if (n.y < 0f) n = -n;
                    AddVertex(p, n, new Vector2(u * uvTiles.x, v * uvTiles.y), colorAt != null ? colorAt(u, v) : c);
                }
            int row = cellsU + 1;
            for (int j = 0; j < cellsV; j++)
                for (int i = 0; i < cellsU; i++)
                {
                    if (keep != null && !keep((i + 0.5f) * du, (j + 0.5f) * dv)) continue;
                    int a = baseIdx + j * row + i;
                    QuadAuto(a, a + 1, a + 1 + row, a + row);
                }
        }

        /// <summary>Copies another mesh in (all its submeshes go to the current slot), transformed.</summary>
        public void Append(Mesh mesh, Matrix4x4 m, Color c)
        {
            if (mesh == null) return;
            var v = mesh.vertices;
            var nr = mesh.normals;
            var uv = mesh.uv;
            var nm = m.inverse.transpose;
            int baseIdx = VertexCount;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 n = nr != null && nr.Length == v.Length ? nm.MultiplyVector(nr[i]).normalized : Vector3.up;
                AddVertex(m.MultiplyPoint3x4(v[i]), n, uv != null && uv.Length == v.Length ? uv[i] : Vector2.zero, c);
            }
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3) Tri(baseIdx + tris[i], baseIdx + tris[i + 1], baseIdx + tris[i + 2]);
            }
        }

        /// <summary>Builds the Unity mesh. 32-bit indices are used automatically above 65k vertices.</summary>
        public Mesh ToMesh(string name, bool recalcNormals = false, bool tangents = true)
        {
            var mesh = new Mesh { name = name };
            if (Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, UVs);
            mesh.SetColors(Colors);
            int used = _submeshes.Count;
            while (used > 1 && _submeshes[used - 1].Count == 0) used--;
            mesh.subMeshCount = used;
            for (int s = 0; s < used; s++) mesh.SetTriangles(_submeshes[s], s, false);
            if (recalcNormals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (tangents) mesh.RecalculateTangents();
            return mesh;
        }

        // ---------------------------------------------------------------------------------------------------
        // helpers
        // ---------------------------------------------------------------------------------------------------

        /// <summary>Two unit vectors perpendicular to dir and to each other.</summary>
        public static void Frame(Vector3 dir, out Vector3 p1, out Vector3 p2)
        {
            dir = dir.normalized;
            Vector3 r = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            p1 = Vector3.Cross(dir, r).normalized;
            p2 = Vector3.Cross(dir, p1).normalized;
        }

        static Vector3 Centroid(Vector3[] s)
        {
            Vector3 c = Vector3.zero;
            for (int i = 0; i < s.Length; i++) c += s[i];
            return c / s.Length;
        }

        static Vector3 SectionAxis(IList<Vector3[]> sections, int i)
        {
            int a = Mathf.Max(0, i - 1), b = Mathf.Min(sections.Count - 1, i + 1);
            Vector3 d = Centroid(sections[b]) - Centroid(sections[a]);
            return d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.up;
        }

        void CapSection(Vector3[] sec, Vector3 normal, Color c)
        {
            Vector3 cen = Centroid(sec);
            int ci = AddVertex(cen, normal, Vector2.zero, c);
            int first = VertexCount;
            for (int j = 0; j < sec.Length; j++) AddVertex(sec[j], normal, Vector2.zero, c);
            for (int j = 0; j < sec.Length; j++) TriAuto(ci, first + j, first + (j + 1) % sec.Length);
        }

        /// <summary>Catmull-Rom resample of a polyline into roughly evenly spaced points.</summary>
        public static List<Vector3> Smooth(IList<Vector3> pts, float spacing)
        {
            var outPts = new List<Vector3>();
            if (pts == null || pts.Count == 0) return outPts;
            if (pts.Count == 1) { outPts.Add(pts[0]); return outPts; }
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, pts.Count - 1)];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(p1, p2) / Mathf.Max(0.05f, spacing)));
                for (int s = 0; s < steps; s++)
                {
                    float t = (float)s / steps;
                    float t2 = t * t, t3 = t2 * t;
                    outPts.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            outPts.Add(pts[pts.Count - 1]);
            return outPts;
        }
    }
}
