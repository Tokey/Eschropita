using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// HDRP-correct light creation for builders and runtime effects (the HDAdditionalLightData is added and the
    /// intensity unit set explicitly). Shadows default to off: these are accent lights, and HDRP shadow maps are
    /// the expensive part.
    /// Rough guide for a dusty Mars afternoon: small beacon 200–600 lm, lamp 1500–4000 lm, flash 8000–30000 lm.
    /// </summary>
    public static class OverhaulLights
    {
        public static Light Point(GameObject go, Color color, float lumens, float range, bool volumetric = true, bool shadows = false)
        {
            var l = Setup(go, LightType.Point, color, lumens, range, volumetric, shadows);
            return l;
        }

        public static Light Spot(GameObject go, Color color, float lumens, float range, float angle, bool volumetric = true, bool shadows = false)
        {
            var l = Setup(go, LightType.Spot, color, lumens, range, volumetric, shadows);
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.6f;
            return l;
        }

        /// <summary>Sets intensity in lumens for punctual lights (keeps the unit explicit).</summary>
        public static void SetLumens(Light l, float lumens)
        {
            if (l == null) return;
            if (l.lightUnit != LightUnit.Lumen) l.lightUnit = LightUnit.Lumen;
            l.intensity = lumens;
        }

        static Light Setup(GameObject go, LightType type, Color color, float lumens, float range, bool volumetric, bool shadows)
        {
            var l = go.GetComponent<Light>();
            if (l == null) l = go.AddComponent<Light>();
            var hd = go.GetComponent<HDAdditionalLightData>();
            if (hd == null) hd = go.AddComponent<HDAdditionalLightData>();
            l.type = type;
            l.useColorTemperature = false;
            l.color = color;
            l.lightUnit = LightUnit.Lumen;
            l.intensity = lumens;
            l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            hd.affectsVolumetric = volumetric;
            hd.volumetricDimmer = volumetric ? 1f : 0f;
            return l;
        }
    }
}
