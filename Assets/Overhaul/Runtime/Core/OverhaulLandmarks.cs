using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Where things are, for every overhaul layer (audio emitters, light paths, lore terminals, atmosphere zones).
    /// Positions come from the live scene (player spawn, grove, windmill, obelisk, Mycari home) and from the
    /// runtime level dressing, which places the dome, life-support plant and shuttle and reports them here.
    /// Values are refreshed lazily; everything is on the terrain surface.
    /// </summary>
    public static class OverhaulLandmarks
    {
        public static Vector3 Spawn, GroveCenter, Windmill, Obelisk, Home, TornadoAnchor;
        public static Vector3 DomeCenter, LifeSupportPlant, Shuttle;
        public static Vector3 DoorFacing = Vector3.back;
        public static float DomeRadius = 9f;
        public static bool HasDressing;
        /// <summary>Where the Mycari roam (NPCManager's roam box, after its runtime widening). Nothing solid goes here.</summary>
        public static Bounds RoamArea;
        public static bool HasRoamArea;
        /// <summary>0..1 rotor speed of the Mars turbine (set by its rig; the audio pitches the rotor whoosh with it).</summary>
        public static float TurbineRotorSpeed01 = -1f;

        static bool _resolved;
        static float _nextRefresh;

        public static void Invalidate() { _resolved = false; HasDressing = false; TurbineRotorSpeed01 = -1f; }

        /// <summary>Reads landmark positions from the scene (cheap after the first call; re-reads every few seconds).</summary>
        public static void Resolve(bool force = false)
        {
            if (_resolved && !force && Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 5f;
            var bridge = SignalBridge.Instance;
            var player = bridge != null && bridge.Player != null ? bridge.Player : Object.FindAnyObjectByType<PlayerController>();
            if (!_resolved && player != null) Spawn = OnGround(player.transform.position);
            var grove = bridge != null && bridge.Grove != null ? bridge.Grove : Object.FindAnyObjectByType<MushroomFieldTrigger>();
            if (grove != null) GroveCenter = OnGround(grove.transform.position);
            var windmill = bridge != null ? bridge.Windmill : null;
            if (windmill == null)
                foreach (var s in Object.FindObjectsByType<TaskSite>(FindObjectsSortMode.None))
                    if (s.GetComponent<global::Obelisk>() == null && (s.type == TaskType.PowerPlant || s.name.ToLowerInvariant().Contains("wind"))) { windmill = s; break; }
            if (windmill != null) Windmill = OnGround(windmill.transform.position);
            var ob = bridge != null && bridge.Obelisk != null ? bridge.Obelisk : Object.FindAnyObjectByType<global::Obelisk>();
            if (ob != null) Obelisk = OnGround(ob.transform.position);
            var mgr = NPCManager.Instance != null ? NPCManager.Instance : Object.FindAnyObjectByType<NPCManager>();
            HasRoamArea = mgr != null && mgr.roamArea != null;
            if (HasRoamArea) RoamArea = mgr.roamArea.bounds;
            if (!_resolved)
            {
                var home = GameObject.Find("MycariHome");
                Home = home != null ? OnGround(home.transform.position) : OnGround(new Vector3(13.33f, 0f, 19.94f));
                var t = Object.FindAnyObjectByType<TornadoCalamity>(FindObjectsInactive.Include);
                TornadoAnchor = t != null ? OnGround(t.anchor != null ? t.anchor.position : t.transform.position) : OnGround(new Vector3(0f, 0f, 8.5f));
                if (!HasDressing) DeriveStructures();
            }
            _resolved = true;
        }

        /// <summary>
        /// Default placement of the new structures, relative to the gameplay landmarks. The dome stands behind the
        /// Mycari home but entirely outside the area they roam (its walls would otherwise split their NavMesh and
        /// trap them), clear of the walking lines between landmarks; if no such spot exists it is not built
        /// (DomeCenter stays zero).
        /// </summary>
        public static void DeriveStructures()
        {
            Vector3 away = Home - Spawn; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            away.Normalize();
            DomeCenter = Vector3.zero;
            for (float d = DomeRadius; d <= 80f; d += 1f)
            {
                Vector3 c = Home + away * d;
                if (InRoamArea(c, DomeRadius + 3f) || !OnTerrain(c, DomeRadius) || NearWalkingLines(c, DomeRadius + 2f)) continue;
                DomeCenter = OnGround(c);
                break;
            }
            DoorFacing = -away;
            Vector3 toHome = Home - Windmill; toHome.y = 0f;
            toHome = toHome.sqrMagnitude > 0.01f ? toHome.normalized : Vector3.right;
            LifeSupportPlant = OnGround(Windmill + toHome * 7f + Vector3.Cross(Vector3.up, toHome) * 2.5f);
            if (InRoamArea(LifeSupportPlant, 4f)) LifeSupportPlant = OnGround(Windmill - toHome * 7f + Vector3.Cross(Vector3.up, toHome) * 2.5f);
            Shuttle = OnGround(Spawn + new Vector3(-7f, 0f, -2f));
        }

        /// <summary>True when <paramref name="p"/> is within <paramref name="margin"/> metres (XZ) of the Mycari roam area.</summary>
        public static bool InRoamArea(Vector3 p, float margin)
        {
            if (!HasRoamArea) return false;
            Vector3 mn = RoamArea.min, mx = RoamArea.max;
            float dx = Mathf.Max(mn.x - p.x, 0f, p.x - mx.x);
            float dz = Mathf.Max(mn.z - p.z, 0f, p.z - mx.z);
            return dx * dx + dz * dz < margin * margin;
        }

        /// <summary>True near the straight walks between the gameplay landmarks (home, grove, windmill, obelisk, spawn).</summary>
        public static bool NearWalkingLines(Vector3 p, float margin)
        {
            Vector3[] a = { Home, Home, Home, Spawn, Spawn, Spawn, GroveCenter, GroveCenter, Windmill };
            Vector3[] b = { GroveCenter, Windmill, Spawn, GroveCenter, Windmill, Obelisk, Windmill, Obelisk, Obelisk };
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == Vector3.zero || b[i] == Vector3.zero) continue;
                if (DistToSegmentXZ(p, a[i], b[i]) < margin) return true;
            }
            return false;
        }

        static float DistToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
            Vector2 ab = B - A;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(P, A + ab * t);
        }

        static bool OnTerrain(Vector3 c, float r)
        {
            var t = Terrain.activeTerrain;
            if (t == null || t.terrainData == null) return true;
            Vector3 o = t.transform.position, s = t.terrainData.size;
            return c.x - r > o.x && c.x + r < o.x + s.x && c.z - r > o.z && c.z + r < o.z + s.z;
        }

        public static Vector3 OnGround(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            if (t != null) p.y = t.SampleHeight(p) + t.transform.position.y;
            return p;
        }
    }
}
