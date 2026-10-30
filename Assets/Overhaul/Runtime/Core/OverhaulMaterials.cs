using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Eschropita.Overhaul
{
    /// <summary>Settings for an HDRP/Lit material (object-initializer friendly).</summary>
    public class LitSpec
    {
        public string name = "OverhaulLit";
        public Color color = Color.white;
        public float metallic = 0f;
        public float smoothness = 0.35f;
        public Texture baseMap;
        public Texture normalMap;
        public float normalScale = 1f;
        /// <summary>HDRP mask map: R metallic, G AO, B detail mask, A smoothness.</summary>
        public Texture maskMap;
        /// <summary>Linear HDR emissive colour (already multiplied by intensity). Black = no emission.</summary>
        public Color emissive = Color.black;
        public Texture emissiveMap;
        /// <summary>0 = emission ignores camera exposure (reads the same day or night). 1 = physically exposed.</summary>
        public float emissiveExposureWeight = 0f;
        public bool transparent;
        public float alpha = 1f;
        /// <summary>Additive transparency (glass glints, holograms). Only used when transparent.</summary>
        public bool additive;
        public bool doubleSided;
        public bool alphaClip;
        public float cutoff = 0.5f;
        public Vector2 tiling = Vector2.one;
        public Vector2 offset = Vector2.zero;
        public bool receiveDecals = true;
        /// <summary>Rough "coat" for wet / glazed looks (0..1). 0 disables.</summary>
        public float coatMask = 0f;
    }

    /// <summary>
    /// Creates materials that render correctly in this HDRP project, at runtime or from editor builders
    /// (editor code saves them with OverhaulAssets.SaveMaterial).
    ///
    /// - <see cref="Lit"/>: HDRP/Lit (opaque, cut-out or transparent, with maps and emission).
    /// - <see cref="Unlit"/>: HDRP/Unlit (flat colour, optional additive glow).
    /// - <see cref="SoftGlow"/>: the project's "Eschropita/Soft Glow" shader (premultiplied/additive sprite-style
    ///   glow; shapes: texture, disc, ring). Use it for particles, halos, ground glow pools and rings.
    /// - <see cref="Decal"/>: HDRP/Decal for DecalProjector (projects albedo/normal/mask/emission onto terrain).
    /// Falls back gracefully (URP/Standard) so nothing turns pink if a shader is missing.
    /// </summary>
    public static class OverhaulMaterials
    {
        public const string LitShader = "HDRP/Lit";
        public const string UnlitShader = "HDRP/Unlit";
        public const string DecalShader = "HDRP/Decal";
        public const string SoftGlowShader = "Eschropita/Soft Glow";

        public static Material Lit(LitSpec s)
        {
            Shader sh = Shader.Find(LitShader);
            bool hdrp = sh != null;
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh) { name = s.name };

            Color c = s.color;
            c.a = s.transparent ? s.alpha : 1f;
            Set(m, "_BaseColor", c);
            Set(m, "_Color", c);
            if (s.baseMap) { SetTex(m, "_BaseColorMap", s.baseMap); SetTex(m, "_BaseMap", s.baseMap); SetTex(m, "_MainTex", s.baseMap); }
            m.SetTextureScale(hdrp ? "_BaseColorMap" : "_MainTex", s.tiling);
            m.SetTextureOffset(hdrp ? "_BaseColorMap" : "_MainTex", s.offset);
            Set(m, "_Metallic", s.metallic);
            Set(m, "_Smoothness", s.smoothness);
            if (s.normalMap) { SetTex(m, "_NormalMap", s.normalMap); SetTex(m, "_BumpMap", s.normalMap); Set(m, "_NormalScale", s.normalScale); }
            if (s.maskMap && hdrp)
            {
                SetTex(m, "_MaskMap", s.maskMap);
                // With a mask map HDRP remaps metallic/smoothness from the map.
                Set(m, "_MetallicRemapMax", Mathf.Max(0.0001f, s.metallic > 0f ? s.metallic : 1f));
                Set(m, "_SmoothnessRemapMin", 0f);
                Set(m, "_SmoothnessRemapMax", Mathf.Max(0.0001f, s.smoothness));
            }
            if (s.coatMask > 0f) Set(m, "_CoatMask", s.coatMask);

            bool emits = s.emissive.maxColorComponent > 0.0001f || s.emissiveMap != null;
            if (emits)
            {
                m.EnableKeyword("_EMISSION");
                Set(m, "_UseEmissiveIntensity", 0f);
                Set(m, "_EmissiveExposureWeight", s.emissiveExposureWeight);
                Color e = s.emissive.maxColorComponent > 0.0001f ? s.emissive : Color.white;
                Set(m, "_EmissiveColor", e);
                Set(m, "_EmissionColor", e);
                if (s.emissiveMap) { SetTex(m, "_EmissiveColorMap", s.emissiveMap); SetTex(m, "_EmissionMap", s.emissiveMap); }
            }

            if (s.doubleSided) Set(m, "_DoubleSidedEnable", 1f);
            if (s.alphaClip)
            {
                Set(m, "_AlphaCutoffEnable", 1f);
                Set(m, "_AlphaCutoff", s.cutoff);
                Set(m, "_Cutoff", s.cutoff);
            }
            if (!s.receiveDecals) Set(m, "_SupportDecals", 0f);

            if (s.transparent)
            {
                Set(m, "_SurfaceType", 1f);
                Set(m, "_BlendMode", s.additive ? 1f : 0f);
                Set(m, "_ZWrite", 0f);
                Set(m, "_TransparentZWrite", 0f);
                Set(m, "_EnableFogOnTransparent", 1f);
                Set(m, "_TransparentBackfaceEnable", s.doubleSided ? 1f : 0f);
                Set(m, "_Surface", 1f); // URP
            }

            if (hdrp)
            {
                HDMaterial.ValidateMaterial(m);
                if (s.transparent) m.renderQueue = 3000;
            }
            return m;
        }

        /// <summary>Flat HDRP/Unlit colour. <paramref name="hdrColor"/> may exceed 1 for glow; additive optional.</summary>
        public static Material Unlit(string name, Color hdrColor, bool transparent = false, bool additive = false, float alpha = 1f, bool doubleSided = false)
        {
            Shader sh = Shader.Find(UnlitShader);
            bool hdrp = sh != null;
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var m = new Material(sh) { name = name };
            Color c = hdrColor; c.a = transparent ? alpha : 1f;
            Set(m, "_UnlitColor", c);
            Set(m, "_BaseColor", c);
            Set(m, "_Color", c);
            if (doubleSided) Set(m, "_DoubleSidedEnable", 1f);
            if (transparent)
            {
                Set(m, "_SurfaceType", 1f);
                Set(m, "_BlendMode", additive ? 1f : 0f);
                Set(m, "_ZWrite", 0f);
                Set(m, "_TransparentZWrite", 0f);
                Set(m, "_EnableFogOnTransparent", 1f);
            }
            if (hdrp)
            {
                HDMaterial.ValidateMaterial(m);
                if (transparent) m.renderQueue = 3000;
            }
            return m;
        }

        public enum GlowShape { Texture = 0, Disc = 1, Ring = 2 }

        /// <summary>
        /// The project's Soft Glow shader: colour = vertex colour × _Color × _Intensity. Additive 0 = alpha blend
        /// (reads on bright ground, black makes shadows), 1 = pure light. Shape Disc/Ring needs no texture.
        /// </summary>
        public static Material SoftGlow(string name, Color color, float intensity, float additive = 0.85f,
                                        GlowShape shape = GlowShape.Disc, Texture texture = null, float softness = 1.5f,
                                        float ringRadius = 0.72f, float ringWidth = 0.1f)
        {
            Shader sh = Shader.Find(SoftGlowShader);
            if (sh == null) return Unlit(name, color * intensity, true, true, 1f, true);
            var m = new Material(sh) { name = name };
            Set(m, "_Color", color);
            Set(m, "_Intensity", intensity);
            Set(m, "_Additive", additive);
            Set(m, "_Shape", (float)(int)shape);
            Set(m, "_Softness", softness);
            Set(m, "_RingRadius", ringRadius);
            Set(m, "_RingWidth", ringWidth);
            if (texture) SetTex(m, "_MainTex", texture);
            return m;
        }

        /// <summary>HDRP/Decal material for a DecalProjector.</summary>
        public static Material Decal(string name, Texture baseMap, Texture normalMap = null, Texture maskMap = null,
                                     Color? tint = null, float opacity = 1f, Color? emissive = null, Texture emissiveMap = null,
                                     bool affectNormal = true, bool affectMask = true)
        {
            Shader sh = Shader.Find(DecalShader);
            if (sh == null) return null;
            var m = new Material(sh) { name = name };
            Set(m, "_BaseColor", tint ?? Color.white);
            if (baseMap) SetTex(m, "_BaseColorMap", baseMap);
            if (normalMap) SetTex(m, "_NormalMap", normalMap);
            if (maskMap) SetTex(m, "_MaskMap", maskMap);
            Set(m, "_DecalBlend", opacity);
            Set(m, "_AffectAlbedo", 1f);
            Set(m, "_AffectNormal", affectNormal && normalMap ? 1f : 0f);
            Set(m, "_AffectAO", affectMask && maskMap ? 1f : 0f);
            Set(m, "_AffectMetal", 0f);
            Set(m, "_AffectSmoothness", affectMask && maskMap ? 1f : 0f);
            Set(m, "_NormalBlendSrc", 0f);
            Set(m, "_MaskBlendSrc", 1f);
            if (emissive.HasValue || emissiveMap)
            {
                Set(m, "_AffectEmission", 1f);
                Set(m, "_UseEmissiveIntensity", 0f);
                Set(m, "_EmissiveExposureWeight", 0f);
                Set(m, "_EmissiveColor", emissive ?? Color.white);
                if (emissiveMap) SetTex(m, "_EmissiveColorMap", emissiveMap);
            }
            HDMaterial.ValidateMaterial(m);
            return m;
        }

        /// <summary>Updates emission on any material made here (HDRP Lit/Decal, URP, Standard, Soft Glow, Unlit).</summary>
        public static void SetEmissive(Material m, Color hdr)
        {
            if (m == null) return;
            if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", hdr);
            else if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", hdr);
            else if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", hdr);
        }

        public static void SetEmissive(MaterialPropertyBlock b, Color hdr) => b.SetColor("_EmissiveColor", hdr);

        static void Set(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
        static void Set(Material m, string p, Color v) { if (m.HasProperty(p)) m.SetColor(p, v); }
        static void SetTex(Material m, string p, Texture t) { if (m.HasProperty(p)) m.SetTexture(p, t); }
    }
}
