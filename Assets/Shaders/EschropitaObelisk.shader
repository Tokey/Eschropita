// The obelisk: the painted statue plus the energy trapped inside it, driven by ObeliskVisuals.
// The same material renders the body and the shards breaking off it (they share the painting's
// canvas, so they share UVs and the mask).
//
// Mask texture (half resolution, linear, aligned with the painting):
//   R  the fibrous core behind the broken shell - pulses run up its dark grain
//   G  the six ocarina holes - a falloff from each hole's centre, so they burn from deep inside
//   B  the outer shell - where cracks are allowed to open as it takes damage
//   A  the blue shards - their fill crackles
//
// Deliberately dim: the paint stays dark and the energy reads as thin, twitching light rather
// than a warm glow, so the thing looks dangerous rather than inviting. Everything that moves is
// clocked by _FxTime, which the script sets per renderer, instead of the built-in _Time.
//
// _CrumbleHeight cuts away everything above a jagged line (in UV v, so 1.2 is intact) - the
// collapse animates it down to the stump. _Glitch slides whole bands of the painting sideways.
//
// Same route as Chowder Fill and the mushrooms: legacy CG with no LightMode tag, so HDRP draws it
// as SRPDefaultUnlit in the forward transparent pass and exposure leaves it alone. A depth twin
// (_DepthOnly 1, Color Mask None, ZWrite On, Alpha Clip 0.5, queue 2990) hides whatever stands
// behind the statue per pixel; it crumbles and glitches with the body because ObeliskVisuals
// feeds it the same values.
Shader "Eschropita/Obelisk Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [NoScaleOffset] _MaskTex ("Mask (R core, G holes, B shell, A shards)", 2D) = "black" {}

        [Header(Colour)]
        _Saturation ("Saturation", Range(0,2)) = 1
        _Brightness ("Brightness", Range(0,4)) = 1
        // Scaled by the painted luminance, so the shading still reads through it.
        [HDR] _TintColor ("Mood Tint", Color) = (1,0.25,0.35,1)
        _TintAmount ("Mood Tint Amount", Range(0,1)) = 0
        [HDR] _FlashColor ("Flash Colour", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,4)) = 0

        [Header(Energy)]
        [HDR] _EnergyColor ("Energy Colour", Color) = (0.45,0.85,1,1)
        [HDR] _HoleColor ("Hole Colour", Color) = (0.54,0.36,1,1)
        _HoleGlow ("Hole Glow", Range(0,8)) = 0.3
        _CoreFlow ("Core Flow", Range(0,8)) = 0.3
        // Integrated by the script (phase += speed * dt), so changing the speed never makes the pulses jump.
        _CorePhase ("Core Flow Phase (set by script)", Float) = 0
        _ShardGlow ("Shard Glow", Range(0,8)) = 0.25
        _FxTime ("Effect Clock (set by script)", Float) = 0

        [Header(Damage)]
        _CrackAmount ("Crack Amount", Range(0,1)) = 0
        _CrackScale ("Crack Cells Per Height", Float) = 6
        [HDR] _CrackColor ("Crack Seep Colour", Color) = (0.6,0.3,1,1)
        _CrackGlow ("Crack Seep", Range(0,4)) = 0.35
        _Glitch ("Glitch", Range(0,1)) = 0

        [Header(Crumble)]
        _CrumbleHeight ("Crumble Height (v)", Range(0,1.2)) = 1.2
        _CrumbleJag ("Crumble Jaggedness", Range(0,0.2)) = 0.06
        [HDR] _CrumbleEdgeColor ("Crumble Edge Colour", Color) = (1,0.45,0.2,1)
        _CrumbleEdgeGlow ("Crumble Edge Glow", Range(0,4)) = 0

        [Header(Ink)]
        // Pixels whose brightest channel is under this count as outline and keep their colour.
        _InkThreshold ("Ink Threshold", Range(0.01,1)) = 0.14

        [Header(Depth Twin)]
        _AlphaClip ("Alpha Clip", Range(0,1)) = 0.002
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 0
        [Enum(None,0,All,15)] _ColorMask ("Color Mask", Float) = 15
        // Skips the colour work on the depth twin; it only needs the clip.
        [ToggleUI] _DepthOnly ("Depth Only", Float) = 0

        // _RendererColor, _Flip and _EnableExternalAlpha are intentionally NOT declared here -
        // UnitySprites.cginc owns them as per-renderer data (see Chowder Fill for the details).
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

        Cull Off
        Lighting Off
        ZWrite [_ZWrite]
        ColorMask [_ColorMask]
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment ObeliskFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            sampler2D _MaskTex;
            float  _Saturation;
            float  _Brightness;
            float4 _TintColor;
            float  _TintAmount;
            float4 _FlashColor;
            float  _FlashAmount;
            float4 _EnergyColor;
            float4 _HoleColor;
            float  _HoleGlow;
            float  _CoreFlow;
            float  _CorePhase;
            float  _ShardGlow;
            float  _FxTime;
            float  _CrackAmount;
            float  _CrackScale;
            float4 _CrackColor;
            float  _CrackGlow;
            float  _Glitch;
            float  _CrumbleHeight;
            float  _CrumbleJag;
            float4 _CrumbleEdgeColor;
            float  _CrumbleEdgeGlow;
            float  _InkThreshold;
            float  _AlphaClip;
            float  _DepthOnly;

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 Hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float Noise1(float x)
            {
                float i = floor(x);
                float f = frac(x);
                return lerp(Hash11(i), Hash11(i + 1.0), f * f * (3.0 - 2.0 * f));
            }

            float Noise2(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Distance to the nearest Voronoi cell border (x) and a random value for the cell (y).
            // The two-pass border distance (rather than F2 - F1) keeps the crack lines an even width.
            float2 VoronoiBorder(float2 p)
            {
                float2 n = floor(p);
                float2 f = frac(p);

                float2 mg = 0.0;
                float2 mr = 0.0;
                float md = 8.0;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    float2 g = float2(i, j);
                    float2 r = g + Hash22(n + g) - f;
                    float d = dot(r, r);
                    if (d < md) { md = d; mr = r; mg = g; }
                }

                md = 8.0;
                for (int j2 = -2; j2 <= 2; j2++)
                for (int i2 = -2; i2 <= 2; i2++)
                {
                    float2 g = mg + float2(i2, j2);
                    float2 r = g + Hash22(n + g) - f;
                    float2 diff = r - mr;
                    if (dot(diff, diff) > 1e-5)
                        md = min(md, dot(0.5 * (mr + r), normalize(diff)));
                }
                return float2(md, Hash21(n + mg + 0.37));
            }

            float4 ObeliskFrag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // Glitch: a share of horizontal bands jump sideways, re-rolled 18 times a second.
                float band = floor(uv.y * 46.0);
                float tick = floor(_FxTime * 18.0);
                float jump = step(1.0 - 0.35 * _Glitch, Hash11(band * 1.37 + tick * 11.1));
                uv.x += (Hash11(band + tick * 3.7) - 0.5) * 0.07 * _Glitch * jump;

                float4 tex = SampleSpriteTexture(uv);

                // Crumble: everything above a jagged line is gone. Two octaves of 1D noise across the
                // width make the break read as rubble rather than a saw cut.
                float jag = (Noise1(uv.x * 23.0) - 0.5) * _CrumbleJag
                          + (Noise1(uv.x * 6.0 + 4.1) - 0.5) * _CrumbleJag * 1.6;
                float below = (_CrumbleHeight + jag) - uv.y;   // > 0 under the line
                clip(min(tex.a - _AlphaClip, below));

                if (_DepthOnly > 0.5)
                    return 0;

                float4 m = tex2D(_MaskTex, uv);
                float3 c = tex.rgb;
                float hi = max(c.r, max(c.g, c.b));
                float paint = smoothstep(_InkThreshold * 0.5, _InkThreshold * 1.5, hi);
                float lum = dot(c, float3(0.299, 0.587, 0.114));

                // Mood: drained and darkened while it sleeps, tinted toward warning when unstable.
                // Every control fades out on the ink, so the outline always stays an outline.
                c = max(lerp(lum.xxx, c, _Saturation), 0.0);
                float3 tinted = _TintColor.rgb * (0.3 + 1.2 * lum);
                c = lerp(c, tinted, saturate(_TintAmount) * paint);
                c *= lerp(1.0, _Brightness, paint);

                float flick = 0.7 + 0.3 * Noise1(_FxTime * 13.0 + uv.y * 3.0);
                float3 glow = 0.0;

                // Cracks open across the shell as the core is hurt: dark fissures with a thin seep of
                // light down the middle. They climb from the plinth, where the Breakers hit, as the
                // damage grows.
                if (_CrackAmount > 0.001)
                {
                    float2 cp = float2(uv.x * 0.7895, uv.y) * _CrackScale;   // 1500 / 1900 keeps cells round
                    // Warped cells, so the borders wander like fractures instead of running straight.
                    cp += (float2(Noise2(cp * 2.3), Noise2(cp * 2.3 + 17.0)) - 0.5) * 0.55;
                    float2 vor = VoronoiBorder(cp);
                    float order = uv.y * 0.75 + Noise2(cp * 0.45 + 3.7) * 0.45;
                    float reveal = smoothstep(0.0, 0.06, _CrackAmount * 1.25 - order);
                    // Only some stretches of border open, so it reads as cracks rather than tiles.
                    float run = smoothstep(0.38, 0.6, Noise2(cp * 1.15 + 9.1));
                    float width = lerp(0.012, 0.04, Noise2(cp * 3.0 + 1.3));
                    float fissure = (1.0 - smoothstep(0.0, width, vor.x)) * run;
                    float seam = (1.0 - smoothstep(0.0, width * 0.35, vor.x)) * run;
                    float shell = m.b * paint * reveal;
                    c = lerp(c, c * 0.18, fissure * shell);
                    glow += _CrackColor.rgb * _CrackGlow * seam * shell * flick;
                }

                // Ocarina holes: lit from deep inside. The mask is a falloff from each hole's centre, so
                // they read as sockets with something burning at the bottom, not as painted dots.
                glow += _HoleColor.rgb * _HoleGlow * m.g * m.g * flick;

                // Core: thin pulses running up the dark fibres, each strand slightly out of step.
                float fibre = 1.0 - smoothstep(0.22, 0.42, lum);
                float pulse = frac(uv.y * 5.0 - _CorePhase + Noise1(uv.x * 38.0) * 0.35);
                pulse = pulse * pulse * pulse * pulse * pulse * pulse;
                glow += _EnergyColor.rgb * _CoreFlow * m.r * (0.12 + fibre * (0.3 + 1.6 * pulse));

                // Shards: the blue fill crawls with static.
                float sn = Noise2(uv * float2(30.0, 38.0) + _FxTime * float2(1.7, -2.3));
                glow += _EnergyColor.rgb * _ShardGlow * m.a * (0.35 + 0.9 * sn * sn) * flick;

                // The broken top of the stump: charred, with embers along the line.
                float edge = (1.0 - saturate(below / 0.015)) * step(_CrumbleHeight, 1.05);
                c = lerp(c, c * 0.3, edge * 0.85);
                glow += _CrumbleEdgeColor.rgb * _CrumbleEdgeGlow * edge
                      * (0.4 + 0.6 * Noise1(uv.x * 40.0 + _FxTime * 3.0));

                c += glow;
                c += _FlashColor.rgb * _FlashAmount * (0.15 + 0.85 * paint);

                float4 o = float4(c, tex.a) * IN.color;
                o.rgb *= o.a;   // premultiplied, to match Blend One OneMinusSrcAlpha
                return o;
            }
        ENDCG
        }
    }

    Fallback "Sprites/Default"
}
