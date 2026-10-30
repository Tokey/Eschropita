// Spore-trip screen pass for HDRP (fullscreen custom pass, see TripVisuals.cs / TripWarpPass).
// Reads a copy of the camera colour (_TripSource) and redraws the screen with three separate looks:
//   - ambient (_TripIntensity, kept low): a faint liquid sway and a hint of iridescence; the picture stays clear
//   - hit (_TripHit, _TripHitAge): one smooth ring of light rolling out from the pad that was hit, bending the
//     image gently along its front and warming it toward the note's colour
//   - miss (_TripMiss): a cold horizontal shiver, slices of the image slipping sideways with their colours split,
//     the frame drained toward grey-violet
// With all three at zero the image is untouched.
Shader "Hidden/Eschropita/TripWarp"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    TEXTURE2D_X(_TripSource);
    float _TripIntensity;
    float _TripTime;
    float _TripHit;
    float _TripHitAge;
    float _TripMiss;
    float4 _TripPulseCenter;
    float4 _TripTint;

    float3 Src(float2 uv)
    {
        uv = clamp(uv, 0.0, 1.0 - _ScreenSize.zw * 0.5);
        return SAMPLE_TEXTURE2D_X_LOD(_TripSource, s_linear_clamp_sampler, uv * _RTHandleScale.xy, 0).rgb;
    }

    float Hash(float n) { return frac(sin(n) * 43758.5453); }

    float4 Frag(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

        float2 uv = varyings.positionCS.xy * _ScreenSize.zw;
        float A = saturate(_TripIntensity);
        float H = saturate(_TripHit);
        float M = saturate(_TripMiss);
        float t = _TripTime;
        float aspect = _ScreenSize.x * _ScreenSize.w;

        float2 c = uv - 0.5;
        c.x *= aspect;

        // ---- ambient: a slow, faint liquid sway (a few pixels at most) ----
        float2 w = c;
        float2 wave = float2(sin(uv.y * 7.0 + t * 0.9), cos(uv.x * 6.0 - t * 0.8));
        w += wave * 0.0022 * A;

        // ---- hit: one ring of light rolling out from the pad ----
        float2 pc = _TripPulseCenter.xy - 0.5;
        pc.x *= aspect;
        float2 toP = c - pc;
        float pd = length(toP);
        float front = _TripHitAge * 0.75;                       // ring radius, in screen heights
        float ring = exp(-pow((pd - front) / 0.06, 2.0));       // soft band around the front
        float bend = H * ring * 0.012;
        w += (toP / max(pd, 1e-4)) * bend;

        float2 wuv = float2(w.x / aspect, w.y) + 0.5;

        // ---- miss: horizontal slices slip sideways, colours pulled apart ----
        float slip = 0.0;
        if (M > 0.001)
        {
            float band = floor(uv.y * 26.0);
            float tick = floor(_TripTime * 18.0);
            float r = Hash(band * 13.7 + tick * 3.1);
            slip = (r - 0.5) * 0.045 * M * step(0.55, Hash(band * 7.3 + tick));   // only some slices move
            wuv.x += slip;
        }
        float split = 0.0025 * A + 0.0045 * H * ring + 0.012 * M;
        float3 col;
        col.r = Src(wuv + float2(split, 0.0)).r;
        col.g = Src(wuv).g;
        col.b = Src(wuv - float2(split, 0.0)).b;

        // ---- colour ----
        float luma = dot(col, float3(0.2126, 0.7152, 0.0722));
        // ambient: the faintest iridescent sheen
        float3 iri = 0.5 + 0.5 * cos(6.2831853 * (float3(0.0, 0.33, 0.67) + luma * 1.2 + t * 0.05));
        col = lerp(col, col * (0.85 + 0.3 * iri), 0.12 * A);
        // hit: the ring glows in the note's colour, and the whole frame warms a touch
        float3 warm = lerp(float3(1.0, 0.85, 0.6), _TripTint.rgb, 0.5);
        col += warm * ring * H * 0.22 * (0.4 + luma);
        col = lerp(col, col * warm * 1.15, 0.12 * H);
        // miss: drain toward a cold grey-violet
        col = lerp(col, float3(luma * 0.9, luma * 0.85, luma * 1.08), 0.45 * M);

        float3 orig = Src(uv);
        float amount = saturate(A * 1.5 + H + M);
        return float4(lerp(orig, max(col, 0.0), amount), 1.0);
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
