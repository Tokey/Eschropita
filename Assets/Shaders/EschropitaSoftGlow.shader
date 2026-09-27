// Light for the puzzle mushrooms: the halo sprite behind each body, the spore particles, and the
// glow pool, contact shadow and shock rings on the ground. One material serves all of them;
// MushroomPad picks the shape and colour per renderer through a MaterialPropertyBlock.
//
// Colour is vertex colour (SpriteRenderer / particle colour) times _Color times _Intensity. Vertex
// colour is 8-bit, so HDR brightness has to come through _Color / _Intensity, not the renderer.
//
// _Additive slides from plain alpha blending (0: reads on the bright Mars ground, and black at 0
// makes a shadow) to pure additive (1: reads as light). Output is premultiplied either way.
//
// Legacy CG with no LightMode tag, like Chowder Fill, so HDRP draws it as SRPDefaultUnlit.
Shader "Eschropita/Soft Glow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Colour", Color) = (0.3,0.75,1,1)
        _Intensity ("Intensity", Range(0,16)) = 0.5
        _Additive ("Additive", Range(0,1)) = 0.85

        [Header(Shape)]
        [Enum(Texture,0,Disc,1,Ring,2)] _Shape ("Shape", Float) = 0
        // Disc falloff exponent: 1 is a cone, higher is a tighter hot core.
        _Softness ("Disc Softness", Range(0.1,6)) = 1.5
        _RingRadius ("Ring Radius", Range(0,1)) = 0.72
        _RingWidth ("Ring Width", Range(0.01,0.5)) = 0.1
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
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex GlowVert
            #pragma fragment GlowFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float  _Intensity;
            float  _Additive;
            float  _Shape;
            float  _Softness;
            float  _RingRadius;
            float  _RingWidth;

            struct appdata_glow
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_glow
            {
                float4 vertex : SV_POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f_glow GlowVert(appdata_glow IN)
            {
                v2f_glow OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.uv     = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color  = IN.color;
                return OUT;
            }

            float4 GlowFrag(v2f_glow IN) : SV_Target
            {
                float4 tex = tex2D(_MainTex, IN.uv);
                float shape = tex.a;

                if (_Shape > 0.5)
                {
                    float d = length(IN.uv * 2.0 - 1.0);
                    if (_Shape < 1.5)
                    {
                        shape *= pow(saturate(1.0 - d), _Softness);
                    }
                    else
                    {
                        float x = (d - _RingRadius) / max(_RingWidth, 1e-3);
                        shape *= exp(-x * x) * saturate((1.0 - d) * 12.0);
                    }
                }

                float4 col = IN.color * _Color;
                float a = saturate(shape * col.a);
                float3 rgb = tex.rgb * col.rgb * _Intensity * a;
                return float4(rgb, a * (1.0 - _Additive));
            }
        ENDCG
        }
    }
}
