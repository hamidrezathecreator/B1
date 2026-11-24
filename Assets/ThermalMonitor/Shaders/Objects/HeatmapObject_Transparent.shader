Shader "Custom/HeatmapObject_Transparent"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor]   _BaseColor ("Color Tint", Color) = (1,1,1,1)
        _HeatMap      ("Heat Map (optional)", 2D) = "white" {}
        _HeatIntensity("Heat Map Intensity", Range(0,2)) = 1.0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        LOD 100

        // ─── Fixed settings for typical particle/fire/explosion use ───
                  // Additive – bright & glowy
        ZWrite Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 vertexColor  : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 vertexColor  : COLOR;
                float fogFactor     : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_HeatMap);
            SAMPLER(sampler_HeatMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _HeatIntensity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.vertexColor = input.vertexColor;
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 col = tex * _BaseColor * input.vertexColor;

                // ─── Heat calculation ────────────────────────────────────────
                half brightness = max(max(col.r, col.g), col.b);

                half heat = brightness * 10.0;                      // base strength
                heat += saturate(brightness - 0.35) * 30.0;         // strong boost for bright cores

                // Particles often live mostly in alpha → keep heat alive
                heat *= saturate(col.a * 1.4 + brightness * 0.6);

                // Let it go above 1 so bloom can be strong
                heat = min(heat, 15.0);

                // Optional: mix in dedicated heat map if you assign one
                // half extra = SAMPLE_TEXTURE2D(_HeatMap, sampler_HeatMap, input.uv).r * _HeatIntensity;
                // heat = max(heat, extra);

                // ─── For thermal replacement pass we return heat in red ───
                // In normal rendering URP will use the fallback instead
                return half4(heat, 0, 0, col.a);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Particles/Unlit"
}