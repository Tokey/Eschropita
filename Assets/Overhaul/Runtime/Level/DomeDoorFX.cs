using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>The dome door's status lights: a slow amber/green idle pulse, flaring green when a Mycari goes home.</summary>
    public class DomeDoorFX : MonoBehaviour
    {
        public Light[] lights = new Light[0];
        public Material[] lamps = new Material[0];
        float _flare;

        void OnEnable() => GameSignals.MycariLeftHome += OnLeft;
        void OnDisable() => GameSignals.MycariLeftHome -= OnLeft;

        void OnLeft(NPCFlocker n, Vector3 p)
        {
            Vector3 d = p - transform.position; d.y = 0f;
            if (d.magnitude < 12f) _flare = 1f;
        }

        void Update()
        {
            _flare = Mathf.MoveTowards(_flare, 0f, Time.deltaTime * 0.6f);
            float t = Time.time;
            for (int i = 0; i < lights.Length; i++)
            {
                Color idle = i == 0 ? OverhaulPalette.RoleWanderer : OverhaulPalette.HazardAmber;
                Color c = Color.Lerp(idle, OverhaulPalette.RoleWanderer, _flare);
                float k = 0.45f + 0.35f * Mathf.Sin(t * 1.5f + i * 1.7f) + 2.5f * _flare;
                if (lamps != null && i < lamps.Length && lamps[i] != null) OverhaulMaterials.SetEmissive(lamps[i], c * (3f * k));
                if (lights[i] != null) { lights[i].color = c; OverhaulLights.SetLumens(lights[i], 220f * k); }
            }
        }
    }
}
