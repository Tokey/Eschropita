// Chowder-style fill: the character's silhouette acts as a window onto a texture
// that is nailed to WORLD space, so walking drags the art across the pattern.
//
// Screen-space UVs (the usual way to do this) do not work in this project: the
// Cinemachine rig in IsoFollowCamera.cs follows the player, so the player barely
// moves in screen space and the pattern would look frozen. Instead we project
// world position onto the camera's image plane using the rig's FIXED yaw/pitch.
// Using the constant angles rather than the live view matrix means the perlin
// shake and speed-zoom in IsoFollowCamera cannot make the pattern wobble.
//
// Two ways to decide which pixels show the pattern:
//   Ink key (default)  - lineart on white. Light pixels become pattern, dark stay ink.
//   Colour key         - painted art. Pixels of _KeyColor (the flat green suit)
//                        become pattern; skin, hair and outline keep their paint.
//
// Expects sprites whose alpha is the silhouette. Coloring-book art on a white
// background has no such alpha - run Tools > Eschropita > Bake Silhouette To Alpha
// first, or the shader will fill the whole quad. Painted art that already ships
// with transparency must NOT be baked: the flood cannot reach inside the helmet,
// so the glass would come out as an opaque white disc.
Shader "Eschropita/Chowder Fill"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Pattern)]
        [NoScaleOffset] _PatternTex ("Pattern (world projected)", 2D) = "white" {}
        _PatternScale ("World Units Per Tile", Float) = 6
        _PatternOffset ("Pattern Offset (XY)", Vector) = (0,0,0,0)
        _FillTint ("Fill Tint", Color) = (1,1,1,1)

        // Pushes the fill above 1.0 so HDRP's bloom treats it as an emissive surface. At 1 the
        // pattern is just albedo and reads as wallpaper showing through a hole; a little over
        // and it lifts off the background and looks lit from within.
        _FillGlow ("Fill Glow (HDR)", Range(1,8)) = 1
        _FillGlowSaturation ("Fill Glow Saturation", Range(0.5,2)) = 1
        // Off stretches every texture to a square world tile, which the original
        // swirl was tuned against. On keeps the texture's own width:height, so a
        // motif like the navy ogee is not squashed; the scale sets tile width.
        [ToggleUI] _PatternKeepAspect ("Keep Texture Aspect", Float) = 0

        // Keep punctuation out of [Header(...)]. ShaderLab only accepts
        // identifier characters and spaces, and a stray dash here is a parse
        // error that takes the whole shader down.
        [Header(Camera Basis from IsoFollowCamera)]
        _CamYaw ("Camera Yaw (deg)", Float) = 45
        _CamPitch ("Camera Pitch (deg)", Float) = 35

        [Header(Ink Keying)]
        _InkColor ("Ink Color", Color) = (0,0,0,1)
        _InkThreshold ("Ink Threshold", Range(0,1)) = 0.55
        _InkSoftness ("Ink Softness", Range(0.001,0.5)) = 0.12
        _AlphaClip ("Alpha Clip", Range(0,1)) = 0.01

        [Header(Colour Keying)]
        [Toggle(_CHROMA_KEY)] _ChromaKey ("Key On Colour Instead Of Ink", Float) = 0
        _KeyColor ("Key Color", Color) = (0.2118,1,0,1)

        // Flat-key mode, used by the Mycari. Their back region is a solid colour baked in by
        // Tools > Eschropita > Mycari > Bake Fill Region, so there is no greenness to measure
        // and no ink ramp to ride - it is a plain "is this pixel the key" test with a tolerance
        // wide enough to catch the feathered border the bake leaves behind.
        [Header(Flat Key)]
        [Toggle(_FLAT_KEY)] _FlatKey ("Key On Exact Colour", Float) = 0
        _FlatKeyColor ("Flat Key Color", Color) = (1,0,1,1)
        _FlatKeyTolerance ("Flat Key Tolerance", Range(0.01,1)) = 0.35
        _FlatKeySoftness ("Flat Key Softness", Range(0.001,0.5)) = 0.12

        [Header(Sorting)]
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 0

        // _RendererColor, _Flip and _EnableExternalAlpha are intentionally NOT
        // declared here. UnitySprites.cginc puts them in the UnityPerDrawSprite
        // constant buffer as per-renderer data; adding material properties of the
        // same name would shadow that and break SpriteRenderer flipX and tint.
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        // Cull Off is mandatory: PlayerController flips facing by negating
        // transform.localScale.x, which reverses triangle winding.
        Cull Off
        Lighting Off
        ZWrite [_ZWrite]
        Blend One OneMinusSrcAlpha

        Pass
        {
        // No LightMode tag, so HDRP picks this up as SRPDefaultUnlit in its
        // forward transparent pass - the same route Sprites-Default already
        // takes in this project. Unlit means it ignores HDRP exposure, which
        // keeps the character at constant readability as scene light changes.
        CGPROGRAM
            #pragma vertex EschVert
            #pragma fragment EschFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #pragma shader_feature_local _ _CHROMA_KEY
            #pragma shader_feature_local _ _FLAT_KEY
            #include "UnitySprites.cginc"

            sampler2D _PatternTex;
            float4 _PatternTex_TexelSize;
            float  _PatternScale;
            float4 _PatternOffset;
            float  _PatternKeepAspect;
            fixed4 _FillTint;
            float  _FillGlow;
            float  _FillGlowSaturation;
            fixed4 _KeyColor;
            fixed4 _FlatKeyColor;
            float  _FlatKeyTolerance;
            float  _FlatKeySoftness;
            float  _CamYaw;
            float  _CamPitch;
            fixed4 _InkColor;
            float  _InkThreshold;
            float  _InkSoftness;
            float  _AlphaClip;

            struct appdata_esch
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_esch
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f_esch EschVert(appdata_esch IN)
            {
                v2f_esch OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_OUTPUT(v2f_esch, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float4 pos  = UnityFlipSprite(IN.vertex.xyz, _Flip.xy);
                OUT.worldPos = mul(unity_ObjectToWorld, pos).xyz;
                OUT.vertex   = UnityObjectToClipPos(pos);
                OUT.texcoord = IN.texcoord;
                OUT.color    = IN.color * _Color * _RendererColor;

                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif
                return OUT;
            }

            fixed4 EschFrag(v2f_esch IN) : SV_Target
            {
                fixed4 tex = SampleSpriteTexture(IN.texcoord);
                clip(tex.a - _AlphaClip);

                // Camera image-plane basis from the rig's constant angles.
                float sy, cy, sp, cp;
                sincos(radians(_CamYaw),   sy, cy);
                sincos(radians(_CamPitch), sp, cp);
                float3 camRight = float3(cy, 0.0, -sy);
                float3 camUp    = float3(sy * sp, cp, cy * sp);

                // Both axes respond to ground movement in every direction, so
                // walking "into" the screen slides the pattern too - using
                // worldPos.y for V instead would freeze it on that axis.
                float2 puv = float2(dot(IN.worldPos, camRight),
                                    dot(IN.worldPos, camUp))
                             / max(_PatternScale, 1e-4);
                // TexelSize.z * .y is width / height.
                puv.y *= lerp(1.0, _PatternTex_TexelSize.z * _PatternTex_TexelSize.y,
                              _PatternKeepAspect);
                puv += _PatternOffset.xy;

                // float, not fixed: fixed clamps to [-2,2] on some targets, which would cap the
                // HDR headroom the glow depends on.
                float3 fill = tex2D(_PatternTex, puv).rgb * _FillTint.rgb;

                // Saturate first, then lift. Lifting a desaturated swirl just makes it white,
                // so the colour has to be pushed before the exposure is.
                float fillLum = dot(fill, float3(0.299, 0.587, 0.114));
                fill = lerp(float3(fillLum, fillLum, fillLum), fill, _FillGlowSaturation);
                fill *= _FillGlow;

            #if defined(_FLAT_KEY)
                // Straight distance to the baked key colour. The bake feathers the region's
                // border into the strokes, and those blended pixels sit part-way between the
                // key and the ink, so the ramp has to be soft or the fill gets a hard fringe.
                float kd = distance(tex.rgb, _FlatKeyColor.rgb);
                float cover = 1.0 - smoothstep(_FlatKeyTolerance,
                                               _FlatKeyTolerance + _FlatKeySoftness, kd);

                fixed4 c = fixed4(lerp(tex.rgb, fill, cover), tex.a) * IN.color;
            #elif defined(_CHROMA_KEY)
                // Greenness measured against the key's own greenness is the
                // coverage: 1 on the flat suit, falling to 0 across the
                // antialiased edge into the outline. Anything that is not green
                // (skin, hair, the pink collar) scores <= 0 and is untouched.
                float keyGreen = max(_KeyColor.g - max(_KeyColor.r, _KeyColor.b), 1e-3);
                float cover = saturate((tex.g - max(tex.r, tex.b)) / keyGreen);

                // Despill before mixing. Without it the few percent of key left
                // in edge and compression-noise pixels reads as a teal cast on
                // a pattern as dark as the navy.
                fixed3 paint = tex.rgb;
                paint.g = min(paint.g, max(paint.r, paint.b));

                fixed4 c = fixed4(lerp(paint, fill, cover), tex.a) * IN.color;
            #else
                // Dark pixels stay ink, light pixels become pattern. Grey
                // hatching in the hair lands mid-ramp and blends, which is
                // what keeps the drawn shading readable over the swirl.
                float lum = dot(tex.rgb, float3(0.299, 0.587, 0.114));
                float ink = 1.0 - smoothstep(_InkThreshold - _InkSoftness,
                                             _InkThreshold + _InkSoftness, lum);

                fixed4 c = fixed4(lerp(fill, _InkColor.rgb, ink), tex.a) * IN.color;
            #endif
                c.rgb *= c.a;   // premultiplied, to match Blend One OneMinusSrcAlpha
                return c;
            }
        ENDCG
        }
    }

    Fallback "Sprites/Default"
}
