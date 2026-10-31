// Daffodil's scan, drawn on the world instead of the screen (fullscreen custom pass, see ScanSweep.cs).
// From the scene depth each pixel's world position is rebuilt; a bright wavefront rolls out over the actual ground
// from Daffodil, trailing a glow, a measuring grid and height contour lines that fade behind it. Added on top of
// the picture (Blend One One), before post-processing so bloom catches the front.
Shader "Hidden/Eschropita/ScanSweep"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    float4 _ScanOrigin;   // xyz: Daffodil (absolute world position), w: radius of the front (m)
    float4 _ScanParams;   // x: width of the front (m), y: range (m), z: trail strength, w: overall strength
    float4 _ScanColor;    // HDR
    float _ScanTime;

    float LineMask(float v, float width)
    {
        float f = abs(frac(v) - 0.5);           // 0.5 on the line, 0 halfway between lines
        return smoothstep(0.5 - width, 0.5, f);
    }

    float4 Frag(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

        float depth = LoadCameraDepth(varyings.positionCS.xy);
    #if UNITY_REVERSED_Z
        if (depth <= 1e-6) return float4(0, 0, 0, 0);           // sky
    #else
        if (depth >= 1.0 - 1e-6) return float4(0, 0, 0, 0);
    #endif
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float3 ws = GetAbsolutePositionWS(posInput.positionWS);

        float dist = length(ws.xz - _ScanOrigin.xz);
        float R = _ScanOrigin.w;
        float width = max(0.05, _ScanParams.x);
        float range = max(1.0, _ScanParams.y);
        if (dist > range) return float4(0, 0, 0, 0);

        float behind = R - dist;                                   // > 0 where the front has already passed
        float front = exp(-pow(behind / (width * 0.22), 2.0));     // the bright leading edge
        float glow = behind > 0.0 ? exp(-behind / (width * 2.0)) : 0.0;
        float trail = behind > 0.0 ? exp(-behind / (range * 0.4)) * _ScanParams.z : 0.0;

        float grid = max(LineMask(ws.x / 1.5, 0.035), LineMask(ws.z / 1.5, 0.035));
        float contour = LineMask(ws.y / 0.6, 0.06);
        float rings = LineMask(dist / 2.0 - _ScanTime * 0.5, 0.04);   // faint rings drifting outward

        float fadeFar = 1.0 - smoothstep(range * 0.75, range, dist);
        float fadeNear = smoothstep(0.0, 1.2, dist);                   // keep Daffodil's own spot clean
        float a = (front * 1.6 + glow * 0.45 + trail * (grid * 0.7 + contour * 0.45 + rings * 0.25)) * fadeFar * fadeNear * _ScanParams.w;
        return float4(_ScanColor.rgb * a, 0.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "ScanSweep"
            ZWrite Off
            ZTest Always
            Blend One One
            Cull Off

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }
    }
    Fallback Off
}
