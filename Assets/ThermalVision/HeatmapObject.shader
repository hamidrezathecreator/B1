Shader "Custom/HeatmapObject_Mobile"
{
    Properties
    {
        _BaseMap       ("Base Texture",    2D)           = "white" {}
        _Color         ("Base Color",      Color)        = (1,1,1,1)
        _HeatMap       ("Heat Map",        2D)           = "white" {}
        _HeatIntensity ("Heat Intensity",  Range(0, 2))  = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        // ─────────────────────────────────────────────────────────────────────
        // Shared CBuffer — declared once here so every pass gets an identical
        // UnityPerMaterial layout, which is required for SRP Batcher support.
        // ─────────────────────────────────────────────────────────────────────
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _Color;
            float4 _HeatMap_ST;
            half   _HeatIntensity;
        CBUFFER_END
        ENDHLSL

        // ─────────────────────────────────────────────────────────────────────
        // Unlit (visual) pass
        // ─────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest  LEqual
            Cull   Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half2  uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _Color;
            }
            ENDHLSL
        }

        // ─────────────────────────────────────────────────────────────────────
        // Thermal Vision pass
        // ─────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ThermalVision"
            Tags { "LightMode" = "ThermalVision" }

            ZWrite On
            ZTest  LEqual
            Cull   Back
            Blend  Off
            ColorMask R

            HLSLPROGRAM
            #pragma vertex   vert_thermal
            #pragma fragment frag_thermal
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half2  uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_HeatMap); SAMPLER(sampler_HeatMap);

            Varyings vert_thermal(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = TRANSFORM_TEX(input.uv, _HeatMap);
                return output;
            }

            half4 frag_thermal(Varyings input) : SV_Target
            {
                half heat = SAMPLE_TEXTURE2D(_HeatMap, sampler_HeatMap, input.uv).r;
                heat      = saturate(heat * _HeatIntensity);
                return half4(heat, 0, 0, 1);
            }
            ENDHLSL
        }

        // ─────────────────────────────────────────────────────────────────────
        // Shadow Caster
        // ─────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest  LEqual
            Cull   Back
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   vert_shadow
            #pragma fragment frag_shadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            float _ShadowBias;
            float _ShadowNormalBias;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 ApplyShadowBiasInline(float3 posWS, float3 normalWS, float3 lightDir)
            {
                float invNdotL = 1.0 - saturate(dot(lightDir, normalWS));
                float scale    = invNdotL * _ShadowNormalBias;
                posWS          = normalWS * scale.xxx + posWS;
                posWS          = lightDir * _ShadowBias.xxx + posWS;
                return posWS;
            }

            Varyings vert_shadow(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS    = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                posWS             = ApplyShadowBiasInline(posWS, normalWS, lightDir);
                output.positionCS = TransformWorldToHClip(posWS);
                return output;
            }

            void frag_shadow(Varyings input) { }
            ENDHLSL
        }

        // ─────────────────────────────────────────────────────────────────────
        // Depth Only
        // ─────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert_depth
            #pragma fragment frag_depth
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert_depth(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            void frag_depth(Varyings input) { }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
