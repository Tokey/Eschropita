// Spore-trip screen warp for HDRP (fullscreen custom pass, see TripVisuals.cs / TripWarpPass).
// Reads a copy of the camera colour (_TripSource) and redraws the screen liquefied:
//   - slow swirl + breathing zoom around the centre, layered sine "liquid" waves
//   - ripples that radiate from the last musical pulse (a mushroom note, a recruited Mycari)
//   - colour channels split along the flow, a gentle hue drift and iridescent luminance bands
//   - in the grove at full strength, a 6-fold kaleidoscope ghost blended over the image
// Everything scales with _TripIntensity (0 = untouched image).
Shader "Hidden/Eschropita/TripWarp"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    TEXTURE2D_X(_TripSource);
    float _TripIntensity;
    float _TripTime;
    float _TripPulse;
    float4 _TripPulseCenter;
    float4 _TripTint;
    float _TripHueDrift;
    float _TripKaleido;

    float3 Src(float2 uv)
    {
        uv = clamp(uv, 0.0, 1.0 - _ScreenSize.zw * 0.5);
        return SAMPLE_TEXTURE2D_X_LOD(_TripSource, s_linear_clamp_sampler, uv * _RTHandleScale.xy, 0).rgb;
    }

    float3 HueRotate(float3 c, float a)
    {
        const float3 k = float3(0.57735, 0.57735, 0.57735);
        float ca = cos(a);
        return c * ca + cross(k, c) * sin(a) + k * dot(k, c) * (1.0 - ca);
    }

    float4 Frag(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

        float2 uv = varyings.positionCS.xy * _ScreenSize.zw;
        float I = saturate(_TripIntensity);
        float t = _TripTime;
        float aspect = _ScreenSize.x * _ScreenSize.w;

        float2 c = uv - 0.5;
        c.x *= aspect;
        float r = length(c);

        // swirl + breathing zoom, strongest in the middle of the screen
        float swirl = I * (0.22 + 0.18 * I) * sin(t * 0.21) * (1.0 - smoothstep(0.0, 0.95, r));
        float s = sin(swirl), co = cos(swirl);
        float2 w = float2(c.x * co - c.y * s, c.x * s + c.y * co);
        w *= 1.0 + I * 0.022 * sin(t * 0.8 + r * 6.0);

        // liquid waves
        float2 wave = float2(sin(uv.y * 9.0 + t * 1.1) + 0.4 * sin(uv.y * 23.0 - t * 0.7),
                             cos(uv.x * 7.0 - t * 0.9) + 0.4 * cos(uv.x * 19.0 + t * 1.3));
        w += wave * (0.0035 * I + 0.0065 * I * I);

        // ripple from the last pulse
        float2 pc = _TripPulseCenter.xy - 0.5;
        pc.x *= aspect;
        float2 toP = c - pc;
        float pd = length(toP);
        float ripple = _TripPulse * sin(pd * 40.0 - t * 9.0) * exp(-pd * 3.5) * 0.014 * (0.4 + 0.6 * I);
        w += (toP / max(pd, 1e-4)) * ripple;

        float2 wuv = float2(w.x / aspect, w.y) + 0.5;

        // colour split along the flow
        float2 flow = (wuv - uv) * (1.2 + 2.2 * I) + float2(c.x / aspect, c.y) * 0.006 * I;
        float3 col;
        col.r = Src(wuv + flow).r;
        col.g = Src(wuv).g;
        col.b = Src(wuv - flow).b;

        // kaleidoscope ghost (grove only)
        if (_TripKaleido > 0.001)
        {
            float ang = atan2(c.y, c.x) + t * 0.05;
            const float seg = 6.2831853 / 6.0;
            ang = abs(fmod(ang + 100.0 * seg, seg) - seg * 0.5);
            float2 kc = float2(cos(ang), sin(ang)) * r * (1.0 + 0.08 * sin(t * 0.5));
            float2 kuv = float2(kc.x / aspect, kc.y) + 0.5;
            float3 k = HueRotate(Src(kuv), 2.1 + t * 0.1);
            col = lerp(col, max(col, k), _TripKaleido * 0.32);
        }

        col = max(HueRotate(col, _TripHueDrift), 0.0);

        // iridescent luminance bands, tinted towards the trip colour
        float luma = dot(col, float3(0.2126, 0.7152, 0.0722));
        float bands = sin(luma * 16.0 - t * 2.0) * 0.5 + 0.5;
        float3 iri = 0.5 + 0.5 * cos(6.2831853 * (float3(0.0, 0.33, 0.67) + luma * 1.5 + t * 0.07));
        iri = lerp(iri, _TripTint.rgb, 0.35);
        col = lerp(col, col * (0.75 + 0.5 * iri), I * 0.3 * bands);

        float3 orig = Src(uv);
        col = lerp(orig, col, saturate(I * 1.5));
        return float4(col, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "TripWarp"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }
    }
    Fallback Off
}
