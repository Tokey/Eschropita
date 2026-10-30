// The obelisk's shield: a procedural energy bubble drawn on a camera-facing quad, so it needs no
// texture. A hard thin rim, almost no fill, and patches of thin filaments that crawl across it,
// thickest toward the edge. As the shield weakens, holes eat into it and their edges
// sizzle; a hit sends a ripple out from the impact point; _Burst flashes it apart when it breaks.
//
// Kept thin and twitchy on purpose (filaments and a rim, almost no fill) so it reads as live
// current rather than a soft glow. _FxTime and _Flicker are driven per renderer by ObeliskVisuals.
//
// _HitUV: xy is the impact point in the quad's -1..1 space, z the seconds since it landed.
//
// Legacy CG with no LightMode tag, like the other Eschropita effect shaders, so HDRP draws it as
// SRPDefaultUnlit. Premultiplied output; _Additive slides from alpha blended to pure light.
Shader "Eschropita/Obelisk Shield"
{
    Properties
    {
        [HDR] _Color ("Rim Colour", Color) = (0.36,0.91,0.91,1)
        [HDR] _WebColor ("Web Colour", Color) = (0.54,0.36,1,1)
        _Intensity ("Intensity", Range(0,8)) = 0.9
        _Additive ("Additive", Range(0,1)) = 0.9
        _Strength ("Shield Strength", Range(0,1)) = 1
        _Flicker ("Flicker", Range(0,1)) = 1
        _Burst ("Burst", Range(0,1)) = 0
        _FxTime ("Effect Clock (set by script)", Float) = 0
        _HitUV ("Hit (xy point, z age)", Vector) = (0,0,9,0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex ShieldVert
            #pragma fragment ShieldFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _Color;
            float4 _WebColor;
            float  _Intensity;
            float  _Additive;
            float  _Strength;
            float  _Flicker;
            float  _Burst;
            float  _FxTime;
            float4 _HitUV;

            struct appdata_shield
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_shield
            {
                float4 vertex : SV_POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f_shield ShieldVert(appdata_shield IN)
            {
                v2f_shield OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.uv     = IN.texcoord;
                OUT.color  = IN.color;
                return OUT;
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
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

            // 1 on the crest of a noise field, falling off sharply: thin wandering filaments.
            float Ridge(float n, float sharpness)
            {
                return pow(saturate(1.0 - abs(n * 2.0 - 1.0)), sharpness);
            }

            float4 ShieldFrag(v2f_shield IN) : SV_Target
            {
                float2 p = IN.uv * 2.0 - 1.0;
                float d = length(p);
                clip(1.0 - d);

                float t = _FxTime;

                // Rim and shell: a hard thin edge and next to no fill, so the statue stays readable
                // through it and it never reads as a soft, friendly glow.
                float rim = smoothstep(0.93, 0.99, d) * (1.0 - smoothstep(0.99, 1.0, d));
                float d2 = d * d;
                float shell = 0.015 + 0.07 * d2 * d2 * d2;

                // Web: thin crests of domain-warped noise, only switched on in patches that drift
                // about, and mostly toward the edge where you look through more of the sphere.
                float2 q = p * 3.4;
                float2 warp = float2(Noise2(q + float2(t * 0.4, 0.0)), Noise2(q + float2(5.2, -t * 0.35)));
                float web = Ridge(Noise2(q * 1.9 + warp * 2.0 + t * 0.2), 40.0);
                web *= smoothstep(0.45, 0.75, Noise2(p * 1.6 + float2(-t * 0.25, t * 0.18) + 3.0));
                web *= 0.25 + 0.75 * d2;

                // Weakening: holes open where the noise is lowest, more of them as strength drops.
                float holeN = Noise2(p * 3.3 + 11.0);
                float thr = (1.0 - _Strength) * 0.9 - 0.06;
                float present = smoothstep(thr - 0.001, thr + 0.05, holeN);
                float holeEdge = (1.0 - smoothstep(0.0, 0.015, abs(holeN - thr - 0.025))) * step(0.0, thr);

                // Hit: a ring running out from the impact point plus a brief hot spot.
                float hd = length(p - _HitUV.xy);
                float age = _HitUV.z;
                float ring = (hd - age * 1.8) / 0.07;
                float ripple = exp(-ring * ring) * saturate(1.0 - age * 1.6);
                float hot = exp(-hd * hd * 12.0) * saturate(1.0 - age * 3.0);

                float shape = (rim + shell + web * 1.4) * present
                            + holeEdge * 0.7 + ripple * 1.4 + hot * 0.6;
                shape *= _Flicker;
                shape += _Burst * (0.6 * rim + 1.5 * web + 0.25);

                float3 col = lerp(_Color.rgb, _WebColor.rgb, saturate(web * 0.8 + holeEdge))
                           * _Intensity * shape;
                float a = saturate(shape) * (1.0 - _Additive);

                float k = IN.color.a;
                return float4(col * IN.color.rgb * k, a * k);
            }
        ENDCG
        }
    }
}
