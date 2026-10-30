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
        public static float DomeRadius = 10f;
        public static bool HasDressing;
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

        /// <summary>Default placement of the new structures, relative to the gameplay landmarks.</summary>
        public static void DeriveStructures()
        {
            Vector3 away = Home - Spawn; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            away.Normalize();
            DomeCenter = OnGround(Home + away * DomeRadius);
            DoorFacing = -away;
            Vector3 toHome = Home - Windmill; toHome.y = 0f;
            toHome = toHome.sqrMagnitude > 0.01f ? toHome.normalized : Vector3.right;
            LifeSupportPlant = OnGround(Windmill + toHome * 7f + Vector3.Cross(Vector3.up, toHome) * 2.5f);
            Shuttle = OnGround(Spawn + new Vector3(-7f, 0f, -2f));
        }

        public static Vector3 OnGround(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            if (t != null) p.y = t.SampleHeight(p) + t.transform.position.y;
            return p;
        }
    }
}
