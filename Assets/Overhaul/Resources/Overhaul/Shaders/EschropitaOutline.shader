// Inverted-hull ink outline for the overhaul's 3D structures, so they sit with the hand-inked sprites
// (Eschropita, Mycari, mushrooms, obelisk all have dark painted outlines).
//
// Draw the same mesh a second time with this material: back faces only, pushed out along the normal by a
// roughly constant screen width. Uses smoothed normals from UV3 when present (OverhaulOutline.BakeSmoothNormals)
// so hard-edged boxes don't crack open at their corners.
//
// Legacy CG with no LightMode tag, like the project's other custom shaders, so HDRP draws it as SRPDefaultUnlit.
Shader "Eschropita/Outline"
{
    Properties
    {
        _OutlineColor ("Outline Colour", Color) = (0.09, 0.06, 0.07, 1)
        _OutlineWidth ("Width (screen-ish px at 1080p)", Range(0, 8)) = 2.2
        _MaxWorldWidth ("Max World Width (m)", Range(0.005, 0.5)) = 0.12
        _FadeStart ("Fade Start Distance", Float) = 60
        _FadeEnd ("Fade End Distance", Float) = 140
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+10" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Front
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _MaxWorldWidth;
            float _FadeStart;
            float _FadeEnd;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float3 smoothN : TEXCOORD3;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float fade : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 n = dot(v.smoothN, v.smoothN) > 0.25 ? v.smoothN : v.normal;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldN = normalize(mul((float3x3)unity_ObjectToWorld, n));
                float dist = distance(_WorldSpaceCameraPos.xyz, worldPos);
                // pixels → metres at this distance (vertical fov from the projection matrix, 1080 px reference)
                float metresPerPixel = 2.0 * dist / max(1e-4, unity_CameraProjection._m11) / 1080.0;
                float w = min(_OutlineWidth * metresPerPixel, _MaxWorldWidth);
                worldPos += worldN * w;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.fade = 1.0 - saturate((dist - _FadeStart) / max(1e-3, _FadeEnd - _FadeStart));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = _OutlineColor;
                c.a *= i.fade;
                clip(c.a - 0.02);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
