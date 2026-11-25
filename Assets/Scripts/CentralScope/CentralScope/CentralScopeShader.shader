Shader "Hidden/CentralScope"
{
    Properties
    {
        _RadialBlurAmount ("Radial Blur Amount", Range(0,1)) = 0.12
        _RadialBlurRadius ("Radial Blur Radius", Range(0,1)) = 0.06
        _VignetteAmount ("Vignette Amount", Range(0,1)) = 0.04
        _FeatherInnerRadius ("Feather Inner Radius", Range(0,0.8)) = 0.3
        _FeatherOuterRadius ("Feather Outer Radius", Range(0,0.8)) = 0.65
        _FeatherCurve ("Feather Curve", Range(0.5,4)) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Blend Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _RadialBlurAmount;
                half _RadialBlurRadius;
                half _VignetteAmount;
                half _FeatherInnerRadius;
                half _FeatherOuterRadius;
                half _FeatherCurve;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 fromCentre = uv - 0.5;
                float aspect = _ScaledScreenParams.x / _ScaledScreenParams.y;
                float distanceFromCentre = length(float2(fromCentre.x * aspect, fromCentre.y));
                half mask = pow(smoothstep(_FeatherInnerRadius, _FeatherOuterRadius, distanceFromCentre), _FeatherCurve);
                half4 sharp = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (mask < 0.001h) return sharp;
                half strength = _RadialBlurAmount * saturate(dot(fromCentre, fromCentre) * 4.0) * mask;
                float2 stepUV = -fromCentre * (_RadialBlurRadius / 6.0);
                half4 accumulation = 0;
                UNITY_UNROLL
                for (int sampleIndex = 0; sampleIndex < 6; sampleIndex++)
                    accumulation += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv + stepUV * sampleIndex));
                half4 result = lerp(sharp, accumulation / 6.0h, strength);
                result.rgb *= 1.0h - saturate(dot(fromCentre, fromCentre) * 8.0h * _VignetteAmount * mask);
                return result;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
