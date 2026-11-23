Shader "VRTemplate/Flag Wave Unlit"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)

        [Header(Cloth Lighting Variants)]
        [Toggle] _UseClothLightMaps("Use Cloth Light Maps", Float) = 0
        _ClothMap2("Cloth Light Map 2", 2D) = "white" {}
        _ClothMap3("Cloth Light Map 3", 2D) = "white" {}
        _MotionThresholdLow("Motion Threshold Low", Range(0, 1)) = 0.12
        _MotionThresholdHigh("Motion Threshold High", Range(0, 1)) = 0.38
        _MotionBlendSoftness("Threshold Blend Softness", Range(0.01, 0.35)) = 0.06
        _MotionIntensityScale("Motion Intensity Scale", Float) = 1

        [Header(Wave)]
        _WindSpeed("Wind Speed", Float) = 1.25
        _WaveAmplitude("Wave Amplitude", Float) = 0.08
        _WaveFrequency("Wave Frequency", Float) = 4.0
        _SecondHarmonic("Second Harmonic", Range(0, 1)) = 0.35
        _RippleAmplitude("Ripple Amplitude", Float) = 0.02
        _RippleFrequency("Ripple Frequency", Float) = 6.0

        [Header(Noise)]
        _NoiseScale("Noise Scale", Float) = 3.5
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.45

        [Header(Attachment)]
        [Toggle] _DualSidePoles("Dual Side Poles (Left + Right)", Float) = 0
        [Enum(UV_U_0, 0, UV_U_1, 1)] _PoleAtMaxU("Side Pole Edge", Float) = 0
        [Toggle] _UseTopPole("Use Top Pole", Float) = 1
        [Enum(UV_V_0, 0, UV_V_1, 1)] _TopAtMaxV("Top Edge Reference", Float) = 1
        _TetherPower("Tether Falloff Power", Range(1, 4)) = 2
        _TopExcludeDistance("Rigid Band From Top (UV)", Range(0, 1)) = 0
        _TopExcludeSoftness("Rigid Band Blend (UV)", Range(0.001, 0.35)) = 0.04

        [Header(Wind Direction Object Space)]
        _WindDir("Wind Direction", Vector) = (0, 0, 1, 0)
        _RippleDir("Vertical Ripple Direction", Vector) = (0, 1, 0, 0)

        [Header(Distance Simplify)]
        [Toggle] _EnableDistanceCull("Enable Distance Simplify", Float) = 1
        _CullDistance("Simplify Beyond Distance", Float) = 60
        _CullFadeRange("Simplify Fade Range", Float) = 5

        [HideInInspector] _Smoothness("Smoothness", Range(0, 1)) = 0.0
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex UnlitVert
            #pragma fragment UnlitFrag
            #pragma target 3.0

            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FlagWaveCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogCoord : TEXCOORD1;
                half motion : TEXCOORD2;
                half detail : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings UnlitVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = input.positionOS.xyz;
                float3 normalOS = input.normalOS;
                ApplyFlagWave(input.uv, positionOS, normalOS);

                VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);
                output.positionCS = posInputs.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.detail = FlagGetDetailFactor();
                output.motion = FlagMotionIntensity(input.uv);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 UnlitFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 albedo = FlagSampleClothAlbedo(input.uv, input.motion, input.detail) * _BaseColor;
                half3 color = MixFog(albedo.rgb, input.fogCoord);
                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "FlagWaveCommon.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionOS = input.positionOS.xyz;
                float3 normalOS = input.normalOS;
                ApplyFlagWave(input.uv, positionOS, normalOS);

                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FlagWaveCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionOS = input.positionOS.xyz;
                float3 normalOS = input.normalOS;
                ApplyFlagWave(input.uv, positionOS, normalOS);
                output.positionCS = TransformObjectToHClip(positionOS);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}