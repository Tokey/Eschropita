// Body of a puzzle mushroom: the painted sprite with live colour controls, so MushroomPad can make
// it react - the marbled cap rotates hue when it sings, drains and reddens when you get it wrong,
// and a travelling hue wave turns it into a rainbow when the melody is solved.
//
// Every control fades out on the black ink, so the outline always stays an outline.
//
// Same route as Chowder Fill: legacy CG and no LightMode tag, so HDRP draws it as SRPDefaultUnlit
// in the forward transparent pass and exposure leaves it alone. Output is float, not fixed, so
// brightness and flash can go over 1 and feed bloom.
//
// Depth twin: a second material on this shader (Color Mask None, ZWrite On, Alpha Clip 0.5) is
// drawn just before the body. Sprites in this project do not write depth, and sorting by distance
// alone cannot order a three metre billboard against a character standing right behind it - the
// twin hides whatever is behind the cap per pixel instead.
Shader "Eschropita/Mushroom Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Colour)]
        _HueShift ("Hue Shift (turns)", Range(-1,1)) = 0
        _HueWave ("Hue Wave Amount (turns)", Range(0,1)) = 0
        _HueWaveFreq ("Hue Wave Frequency", Float) = 1.5
        _HueWavePhase ("Hue Wave Phase", Float) = 0
        _Saturation ("Saturation", Range(0,2)) = 1
        _Brightness ("Brightness", Range(0,4)) = 1

        // Tint is scaled by the painted luminance, so the marbling still reads through it.
        [HDR] _TintColor ("Mood Tint", Color) = (1,0,0,1)
        _TintAmount ("Mood Tint Amount", Range(0,1)) = 0

        [HDR] _FlashColor ("Flash Colour", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,4)) = 0

        [Header(Ink)]
        // Pixels whose brightest channel is under this count as outline and keep their colour.
        _InkThreshold ("Ink Threshold", Range(0.01,1)) = 0.14

        [Header(Depth Twin)]
        _AlphaClip ("Alpha Clip", Range(0,1)) = 0.002
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 0
        [Enum(None,0,All,15)] _ColorMask ("Color Mask", Float) = 15

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

        // Cull Off: MushroomPad mirrors the painting with a negative X scale.
        Cull Off
        Lighting Off
        ZWrite [_ZWrite]
        ColorMask [_ColorMask]
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment MushFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float  _HueShift;
            float  _HueWave;
            float  _HueWaveFreq;
            float  _HueWavePhase;
            float  _Saturation;
            float  _Brightness;
            float4 _TintColor;
            float  _TintAmount;
            float4 _FlashColor;
            float  _FlashAmount;
            float  _InkThreshold;
            float  _AlphaClip;

            // Rodrigues rotation about the grey axis. Cheaper than a round trip through HSV and it
            // cannot divide by zero on the near-black ink.
            float3 RotateHue(float3 c, float turns)
            {
                const float3 k = float3(0.57735, 0.57735, 0.57735);
                float s, co;
                sincos(turns * 6.2831853, s, co);
                return c * co + cross(k, c) * s + k * dot(k, c) * (1.0 - co);
            }

            float4 MushFrag(v2f IN) : SV_Target
            {
                float4 tex = SampleSpriteTexture(IN.texcoord);
                clip(tex.a - _AlphaClip);

                float3 c = tex.rgb;
                float paint = smoothstep(_InkThreshold * 0.5, _InkThreshold * 1.5, max(c.r, max(c.g, c.b)));

                // Hue wave runs up the sprite, so a rainbow climbs from stem to cap as the phase moves.
                float hue = _HueShift + _HueWave * sin(IN.texcoord.y * _HueWaveFreq * 6.2831853 + _HueWavePhase);
                c = saturate(RotateHue(c, hue));

                float lum = dot(c, float3(0.299, 0.587, 0.114));
                c = max(lerp(lum.xxx, c, _Saturation), 0.0);

                float3 tinted = _TintColor.rgb * (0.3 + 1.2 * lum);
                c = lerp(c, tinted, saturate(_TintAmount) * paint);

                c *= lerp(1.0, _Brightness, paint);
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
