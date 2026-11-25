Shader "JangBalad/Effects/Wilson Cloud Rim URP"
{
    Properties
    {
        [MainTexture] _MainTex ("Cloud Texture", 2D) = "white" {}
        _RimTex ("Height / Rim Texture", 2D) = "white" {}
        [HDR] _MainColor ("Cloud Color", Color) = (1,1,1,0.65)
        _DepthContrast ("Depth Contrast", Range(0,1)) = 0.5
        _RimPower ("Rim Power", Range(0.25,15)) = 3
        _RimSoftness ("Rim Texture Softness", Range(0.001,1)) = 0.25
        _CenterFalloff ("Equator Falloff (Fraction of Radius)", Range(0,1)) = 0.2
        _AlphaBoost ("Alpha Boost", Range(0,4)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_RimTex);
            SAMPLER(sampler_RimTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _RimTex_ST;
                half4 _MainColor;
                half _DepthContrast;
                half _RimPower;
                half _RimSoftness;
                half _CenterFalloff;
                half _AlphaBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uvMain      : TEXCOORD0;
                float2 uvRim       : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 viewDirWS   : TEXCOORD3;
                float heightOS     : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);

                output.positionHCS = position.positionCS;
                output.uvMain = TRANSFORM_TEX(input.uv, _MainTex);
                output.uvRim = TRANSFORM_TEX(input.uv, _RimTex);
                output.normalWS = normalize(normal.normalWS);
                output.viewDirWS = GetWorldSpaceViewDir(position.positionWS);
                // The cloud uses a unit-diameter sphere centered at the origin.
                output.heightOS = input.positionOS.y * 2.0;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 cloudTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uvMain);
                half rimTex = SAMPLE_TEXTURE2D(_RimTex, sampler_RimTex, input.uvRim).r;

                half rimTextureMask = 1.0h - smoothstep(0.0h, max(_RimSoftness, 0.001h), rimTex);
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = SafeNormalize(input.viewDirWS);
                half viewRim = 1.0h - saturate(dot(viewDirWS, normalWS));
                half rim = pow(saturate(viewRim), max(_RimPower, 0.25h));

                half3 color = cloudTex.rgb * _MainColor.rgb;
                color += rimTextureMask * _DepthContrast;

                // Keeps the sphere soft at the rim while avoiding the original
                // shader's divide-by-zero path when the mask reaches zero.
                half alpha = cloudTex.a * _MainColor.a;
                alpha *= saturate(1.0h - rimTextureMask * rim * _AlphaBoost);
                // Fade both hemispheres into the equator so a visible cone has
                // a soft base. Object space keeps the fade stable as it expands.
                half centerMask = smoothstep(0.0, max((float)_CenterFalloff, 0.001), abs(input.heightOS));
                alpha *= (_CenterFalloff > 0.0h) ? centerMask : 1.0h;
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
