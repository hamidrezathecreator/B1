Shader "Hidden/B1/XR Lens Flare"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "LensFlareAdditive"
            Blend One One
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ FLARE_INVERSE_SDF
            #pragma multi_compile_vertex _ FLARE_OPENGL3_OR_OPENGLCORE
            #pragma multi_compile _ FLARE_HAS_OCCLUSION
            #pragma multi_compile_local _ B1_FLARE_DEPTH_ARRAY
            #include "XrLensFlareDepth.hlsl"
            #define FLARE_ADDITIVE_BLEND
            #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareCommon.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "LensFlareScreen"
            Blend One OneMinusSrcColor
            BlendOp Max
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ FLARE_INVERSE_SDF
            #pragma multi_compile_vertex _ FLARE_OPENGL3_OR_OPENGLCORE
            #pragma multi_compile _ FLARE_HAS_OCCLUSION
            #pragma multi_compile_local _ B1_FLARE_DEPTH_ARRAY
            #include "XrLensFlareDepth.hlsl"
            #define FLARE_SCREEN_BLEND
            #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareCommon.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "LensFlarePremultiply"
            Blend One OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ FLARE_INVERSE_SDF
            #pragma multi_compile_vertex _ FLARE_OPENGL3_OR_OPENGLCORE
            #pragma multi_compile _ FLARE_HAS_OCCLUSION
            #pragma multi_compile_local _ B1_FLARE_DEPTH_ARRAY
            #include "XrLensFlareDepth.hlsl"
            #define FLARE_PREMULTIPLIED_BLEND
            #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareCommon.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "LensFlareLerp"
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ FLARE_INVERSE_SDF
            #pragma multi_compile_vertex _ FLARE_OPENGL3_OR_OPENGLCORE
            #pragma multi_compile _ FLARE_HAS_OCCLUSION
            #pragma multi_compile_local _ B1_FLARE_DEPTH_ARRAY
            #include "XrLensFlareDepth.hlsl"
            #define FLARE_LERP_BLEND
            #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareCommon.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "LensFlareOcclusion"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertOcclusion
            #pragma fragment fragOcclusion
            #pragma multi_compile_local _ B1_FLARE_DEPTH_ARRAY
            #include "XrLensFlareDepth.hlsl"
            #define FLARE_COMPUTE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareCommon.hlsl"
            ENDHLSL
        }
    }
}
