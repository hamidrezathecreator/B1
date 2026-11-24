Shader "Custom/HeatmapObject_Lit"
{
    Properties
    {
        _BaseMap        ("Albedo",          2D)           = "white" {}
        _BaseColor      ("Color",           Color)        = (1,1,1,1)
        _BumpMap        ("Normal Map",      2D)           = "bump" {}
        _BumpScale      ("Normal Scale",    Range(0, 2))  = 1
        _Metallic       ("Metallic",        Range(0, 1))  = 0
        _Smoothness     ("Smoothness",      Range(0, 1))  = 0.5
        _HeatMap        ("Heat Map",        2D)           = "white" {}
        _HeatIntensity  ("Heat Intensity",  Range(0, 2))  = 0.75
        [Toggle] _UseAlbedoHeat ("Mix Albedo Into Thermal", Float) = 1
        _AlbedoHeatMix  ("Albedo Heat Mix", Range(0, 1))  = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            float4 _BumpMap_ST;
            half   _BumpScale;
            half   _Metallic;
            half   _Smoothness;
            float4 _HeatMap_ST;
            half   _HeatIntensity;
            half   _UseAlbedoHeat;
            half   _AlbedoHeatMix;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 posWS      : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = posInputs.positionCS;
                o.posWS      = posInputs.positionWS;
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                o.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                InputData lightingInput = (InputData)0;
                lightingInput.positionWS = input.posWS;
                lightingInput.normalWS   = normalize(input.normalWS);
                lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.posWS);
                lightingInput.shadowCoord  = TransformWorldToShadowCoord(input.posWS);
                lightingInput.fogCoord     = 0;
                lightingInput.vertexLighting = half3(0, 0, 0);
                lightingInput.bakedGI      = SampleSH(lightingInput.normalWS);
                lightingInput.normalizedScreenSpaceUV = 0;
                lightingInput.shadowMask   = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo     = albedo.rgb;
                surface.alpha      = albedo.a;
                surface.metallic   = _Metallic;
                surface.smoothness = _Smoothness;
                surface.emission   = half3(0, 0, 0);
                surface.occlusion  = 1;
                surface.normalTS   = half3(0, 0, 1);
                surface.clearCoatMask = 0;
                surface.clearCoatSmoothness = 0;

                return UniversalFragmentPBR(lightingInput, surface);
            }
            ENDHLSL
        }

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
                half2  uvHeat     : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
            TEXTURE2D(_HeatMap);  SAMPLER(sampler_HeatMap);

            Varyings vert_thermal(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                o.uvHeat     = TRANSFORM_TEX(input.uv, _HeatMap);
                return o;
            }

            half4 frag_thermal(Varyings input) : SV_Target
            {
                half heatMap = SAMPLE_TEXTURE2D(_HeatMap, sampler_HeatMap, input.uvHeat).r;
                half heat    = saturate(heatMap * _HeatIntensity);

                if (_UseAlbedoHeat > 0.5h)
                {
                    half3 rgb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                    half lum  = dot(rgb, half3(0.2126h, 0.7152h, 0.0722h));
                    heat      = saturate(lerp(heat, heat * (0.25h + lum * 0.75h), _AlbedoHeatMix));
                }

                return half4(heat, 0, 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   vert_shadow
            #pragma fragment frag_shadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;
            float  _ShadowBias;
            float  _ShadowNormalBias;

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
                    float3 lightDirWS = normalize(_LightPosition - posWS);
                #else
                    float3 lightDirWS = _LightDirection;
                #endif

                posWS             = ApplyShadowBiasInline(posWS, normalWS, lightDirWS);
                output.positionCS = TransformWorldToHClip(posWS);
                return output;
            }

            void frag_shadow(Varyings input) { }
            ENDHLSL
        }

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

    FallBack "Universal Render Pipeline/Lit"
}