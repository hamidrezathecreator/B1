Shader "Custom/HeatmapObject_Mobile"
{
    Properties
    {
        _BaseMap       ("Base Texture",    2D)          = "white" {}
        _Color         ("Base Color",      Color)       = (1,1,1,1)
        _HeatMap       ("Heat Map",        2D)          = "white" {}
        _HeatIntensity ("Heat Intensity",  Range(0, 2)) = 1.0
        _OutlineColor  ("Outline Color",   Color)       = (0,0,0,1)
        _OutlineWidth  ("Outline Width",   Range(0, 0.1)) = 0.02
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        // ── Outline pass (inverted hull) ──────────────────────────────────────
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZWrite On
            ZTest  LEqual
            Cull   Front

            HLSLPROGRAM
            #pragma vertex   vert_outline
            #pragma fragment frag_outline
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Color;
                float4 _HeatMap_ST;
                float  _HeatIntensity;
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            Varyings vert_outline(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Expand in clip space to keep outline screen-space consistent
                float4 posCS    = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalCS = TransformWorldToHClipDir(normalWS).xy;

                // Normalize and scale by clip w to stay pixel-consistent at depth
                normalCS = normalize(normalCS);
                normalCS.x *= _ScreenParams.y / _ScreenParams.x;
                posCS.xy   += normalCS * (_OutlineWidth * posCS.w);

                output.positionCS = posCS;
                return output;
            }

            half4 frag_outline(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ── Unlit (visual) pass ───────────────────────────────────────────────
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Color;
                float4 _HeatMap_ST;
                float  _HeatIntensity;
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

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

        // ── Thermal Vision pass ───────────────────────────────────────────────
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Color;
                float4 _HeatMap_ST;
                float  _HeatIntensity;
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

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
                return half4(saturate(heat * _HeatIntensity), 0, 0, 1);
            }
            ENDHLSL
        }

        // ── Depth Only ────────────────────────────────────────────────────────
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            half4 frag_depth(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}