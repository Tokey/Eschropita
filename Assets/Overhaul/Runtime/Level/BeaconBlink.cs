using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>A blinking warning light (antenna masts, distant towers): emissive material + point light pulse.</summary>
    public class BeaconBlink : MonoBehaviour
    {
        public Material lamp;
        public Light glow;
        public Color color = new Color(1f, 0.12f, 0.08f);
        public float period = 1.6f;
        public float phase;
        public float lumens = 260f;

        void Update()
        {
            float k = Mathf.Repeat(Time.time / period + phase, 1f) < 0.18f ? 1f : 0.06f;
            if (lamp != null) OverhaulMaterials.SetEmissive(lamp, color * (6f * k));
            if (glow != null) OverhaulLights.SetLumens(glow, lumens * k);
        }
    }
}
