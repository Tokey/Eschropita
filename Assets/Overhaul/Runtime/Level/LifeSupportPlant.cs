using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The visible life-support system: a compact plant beside the turbine — three O₂/water tanks with glowing gauge
    /// windows, a pump with a spinning fan, a slanted control panel, vent stacks, an alarm-light mast and a railing —
    /// and conduits that run from the turbine to the plant and on to the dome door, studded with light bands that
    /// carry pulses along the pipe. <see cref="LifeSupportPlantRig"/> animates it from LifeSupport.
    /// </summary>
    public static class LifeSupportPlant
    {
        public static LifeSupportPlantRig Build(Transform parent, Vector3 site, Vector3 turbine, Vector3 door)
        {
            Vector3 f = turbine - site; f.y = 0f;
            // The plant faces sideways to the turbine so its tanks and panel read from the gameplay camera.
            Quaternion rot = Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, f.normalized) : Vector3.back, Vector3.up);
            float ground = LevelKit.MinGround(site, 3.2f);
            Vector3 origin = new Vector3(site.x, ground, site.z);
            var root = new GameObject("Life Support Plant").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(origin, rot);

            var pad = new MeshBuilder();
            pad.Box(new Vector3(0f, -0.15f, 0f), new Vector3(6.4f, 0.6f, 4.2f), Quaternion.identity, Color.white, 0.5f);
            LevelKit.Mesh(root, "Pad", pad, LevelKit.Concrete, origin, rot, true);

            var rig = root.gameObject.AddComponent<LifeSupportPlantRig>();
            var gauges = new List<Transform>();

            // tanks
            var tanks = new MeshBuilder();
            for (int i = 0; i < 3; i++)
            {
                float x = -2.2f + i * 1.55f;
                tanks.Submesh(0);
                tanks.Lathe(new Vector3(x, 0.15f, -0.6f), Quaternion.identity, new List<Vector2>
                {
                    new Vector2(0.001f, 0f), new Vector2(0.62f, 0f), new Vector2(0.66f, 0.1f), new Vector2(0.66f, 2.4f),
                    new Vector2(0.58f, 2.75f), new Vector2(0.35f, 2.95f), new Vector2(0.001f, 3.02f),
                }, 20, Color.white, false, 0.5f);
                tanks.Submesh(1);
                tanks.Lathe(new Vector3(x, 0.15f, -0.6f), Quaternion.identity, new List<Vector2> { new Vector2(0.675f, 1.9f), new Vector2(0.675f, 2.1f) }, 20, Color.white, false, 0.5f);
                tanks.Submesh(2);
                tanks.Box(new Vector3(x, 1.35f, -0.6f + 0.66f), new Vector3(0.2f, 2.0f, 0.06f), Quaternion.identity, Color.white);
            }
            LevelKit.Mesh(root, "Tanks", tanks, new[] { LevelKit.JoyPaint, LevelKit.Teal, LevelKit.Trim }, origin, rot, true);
            for (int i = 0; i < 3; i++)
            {
                float x = -2.2f + i * 1.55f;
                var g = new GameObject("Gauge " + i).transform;
                g.SetParent(root, false);
                g.localPosition = new Vector3(x, 0.4f, -0.6f + 0.7f);
                var gm = new MeshBuilder();
                gm.Box(new Vector3(0f, 0.5f, 0f), new Vector3(0.13f, 1f, 0.03f), Quaternion.identity, Color.white);
                var mat = LevelKit.Emissive("TankGauge", new Color(0.2f, 0.4f, 0.3f), OverhaulPalette.Hdr(OverhaulPalette.Good, 3f));
                LevelKit.Mesh(g, "Fill", gm, mat, g.position, g.rotation, false, false);
                g.localScale = new Vector3(1f, 1.9f, 1f);
                gauges.Add(g);
                rig.gaugeMaterials.Add(mat);
            }
            rig.gauges = gauges.ToArray();

            // pump + fan, control panel, vents, railing
            var mach = new MeshBuilder();
            mach.Submesh(0);
            mach.Box(new Vector3(2.15f, 0.75f, -0.4f), new Vector3(1.5f, 1.3f, 1.3f), Quaternion.identity, Color.white, 0.5f);
            mach.Cylinder(new Vector3(2.15f, 0.8f, 0.25f), new Vector3(2.15f, 0.8f, 0.4f), 0.55f, 0.55f, 18, Color.white, false, false);
            mach.Box(new Vector3(1.0f, 0.55f, 1.25f), new Vector3(1.1f, 0.9f, 0.5f), Quaternion.identity, Color.white);
            mach.Submesh(1);
            mach.Strut(new Vector3(-2.9f, 0.1f, 1.2f), new Vector3(-2.9f, 3.4f, 1.2f), 0.14f, 10, Color.white, true);
            mach.Strut(new Vector3(-2.5f, 0.1f, 1.35f), new Vector3(-2.5f, 2.7f, 1.35f), 0.11f, 10, Color.white, true);
            mach.Submesh(2);
            for (int i = 0; i < 9; i++)
            {
                float x = -3.0f + i * 0.75f;
                mach.Strut(new Vector3(x, 0.15f, 1.9f), new Vector3(x, 1.1f, 1.9f), 0.03f, 5, Color.white, true);
            }
            mach.Strut(new Vector3(-3.0f, 1.1f, 1.9f), new Vector3(3.0f, 1.1f, 1.9f), 0.035f, 6, Color.white, true);
            mach.Strut(new Vector3(-3.0f, 0.65f, 1.9f), new Vector3(3.0f, 0.65f, 1.9f), 0.025f, 6, Color.white, true);
            LevelKit.Mesh(root, "Machinery", mach, new[] { LevelKit.JoyPaintDark, LevelKit.Rust, LevelKit.Trim }, origin, rot, true);

            var fan = new GameObject("Fan").transform;
            fan.SetParent(root, false);
            fan.localPosition = new Vector3(2.15f, 0.8f, 0.43f);
            var fb = new MeshBuilder();
            for (int i = 0; i < 4; i++) fb.Box(Vector3.zero, new Vector3(0.95f, 0.14f, 0.03f), Quaternion.Euler(0f, 0f, i * 45f) * Quaternion.Euler(18f, 0f, 0f), Color.white);
            LevelKit.Mesh(fan, "Fan Blades", fb, LevelKit.Trim, fan.position, fan.rotation, false, false);
            rig.fan = fan;

            var panel = new GameObject("Panel Screen").transform;
            panel.SetParent(root, false);
            panel.localPosition = new Vector3(1.0f, 1.12f, 1.42f);
            panel.localRotation = Quaternion.Euler(-35f, 180f, 0f);
            var pm = new MeshBuilder();
            pm.Box(Vector3.zero, new Vector3(0.9f, 0.5f, 0.03f), Quaternion.identity, Color.white);
            rig.screen = LevelKit.Emissive("PlantScreen", new Color(0.05f, 0.1f, 0.1f), OverhaulPalette.Hdr(OverhaulPalette.Good, 2.5f));
            LevelKit.Mesh(panel, "Screen", pm, rig.screen, panel.position, panel.rotation, false, false);

            // alarm mast
            var mast = new MeshBuilder();
            mast.Strut(new Vector3(2.9f, 0.1f, 1.6f), new Vector3(2.9f, 3.1f, 1.6f), 0.06f, 8, Color.white, true);
            LevelKit.Mesh(root, "Alarm Mast", mast, LevelKit.Trim, origin, rot, false);
            var lamp = new GameObject("Alarm Lamp").transform;
            lamp.SetParent(root, false);
            lamp.localPosition = new Vector3(2.9f, 3.25f, 1.6f);
            var lm = new MeshBuilder();
            lm.Lathe(Vector3.zero, Quaternion.identity, new List<Vector2> { new Vector2(0.18f, -0.1f), new Vector2(0.2f, 0.05f), new Vector2(0.12f, 0.2f), new Vector2(0.001f, 0.24f) }, 12, Color.white, false, 1f);
            rig.lampMaterial = LevelKit.Emissive("AlarmLamp", new Color(0.4f, 0.25f, 0.05f), Color.black);
            LevelKit.Mesh(lamp, "Lamp", lm, rig.lampMaterial, lamp.position, lamp.rotation, false, false);
            var spotGo = new GameObject("Alarm Spot");
            spotGo.transform.SetParent(lamp, false);
            spotGo.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
            rig.alarmSpot = OverhaulLights.Spot(spotGo, new Color(1f, 0.15f, 0.05f), 0f, 14f, 55f, true, false);
            rig.lamp = lamp;
            rig.vents = new[] { root.TransformPoint(new Vector3(-2.9f, 3.45f, 1.2f)), root.TransformPoint(new Vector3(-2.5f, 2.75f, 1.35f)) };

            var col = root.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(-0.2f, 1.4f, -0.3f);
            col.size = new Vector3(5.6f, 2.8f, 2.2f);
            LevelKit.CarveBox(root.gameObject, new Vector3(0f, 1f, 0f), new Vector3(6.6f, 2f, 4.4f));

            // conduits: turbine → plant → dome door, with light bands
            var bands = new List<Renderer>();
            Vector3 inlet = root.TransformPoint(new Vector3(-3.4f, 0.55f, -0.6f));
            Vector3 outlet = root.TransformPoint(new Vector3(3.4f, 0.55f, -0.6f));
            Vector3 fromTurbine = turbine + (site - turbine).normalized * 1.6f;
            Pipe(root, new[] { fromTurbine, inlet }, bands, 0f);
            Pipe(root, new[] { outlet, door }, bands, 1f);
            rig.bands = bands.ToArray();
            return rig;
        }

        /// <summary>A terrain-following conduit between points (raised on saddles every ~3 m) with light bands every 1.2 m.</summary>
        static void Pipe(Transform parent, Vector3[] ends, List<Renderer> bands, float phaseOffset)
        {
            var pts = new List<Vector3>();
            for (int s = 0; s < ends.Length - 1; s++)
            {
                Vector3 a = ends[s], b = ends[s + 1];
                float len = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
                int n = Mathf.Max(2, Mathf.CeilToInt(len / 1.5f));
                for (int i = 0; i <= n; i++)
                {
                    if (s > 0 && i == 0) continue;
                    Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                    p.y = LevelKit.GroundY(p.x, p.z) + 0.45f;
                    pts.Add(p);
                }
            }
            if (pts.Count < 2) return;
            var path = MeshBuilder.Smooth(pts, 0.5f);
            var go = new GameObject("Conduit");
            go.transform.SetParent(parent, true);
            var mb = new MeshBuilder();
            mb.Submesh(0);
            mb.Tube(path, 0.16f, 12, Color.white, true, 0.5f);
            float dist = 0f, next = 3f;
            mb.Submesh(1);
            for (int i = 1; i < path.Count; i++)
            {
                dist += Vector3.Distance(path[i], path[i - 1]);
                if (dist >= next)
                {
                    next += 3f;
                    Vector3 p = path[i];
                    float gy = LevelKit.GroundY(p.x, p.z);
                    mb.Box(new Vector3(p.x, (gy + p.y) * 0.5f - 0.05f, p.z), new Vector3(0.35f, p.y - gy + 0.1f, 0.25f), Quaternion.LookRotation(path[i] - path[i - 1]), Color.white);
                }
            }
            LevelKit.Mesh(go.transform, "Pipe", mb, new[] { LevelKit.JoyPaintDark, LevelKit.Trim }, Vector3.zero, Quaternion.identity, true);

            // light bands, each its own renderer so the rig can run a pulse along the line
            var bandMat = LevelKit.Emissive("ConduitBand", new Color(0.1f, 0.2f, 0.2f), OverhaulPalette.Hdr(OverhaulPalette.Good, 2f));
            dist = 0f; next = 0.6f;
            for (int i = 1; i < path.Count; i++)
            {
                dist += Vector3.Distance(path[i], path[i - 1]);
                if (dist < next) continue;
                next += 1.2f;
                Vector3 dir = (path[i] - path[i - 1]).normalized;
                var bm = new MeshBuilder();
                bm.Cylinder(-dir * 0.06f, dir * 0.06f, 0.19f, 0.19f, 12, Color.white, false, false);
                var bgo = LevelKit.Mesh(go.transform, "Band", bm, bandMat, path[i], Quaternion.identity, false, false);
                var r = bgo.GetComponent<Renderer>();
                bands.Add(r);
            }
        }
    }
}
