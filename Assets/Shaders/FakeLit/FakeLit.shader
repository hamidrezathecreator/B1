Shader "Custom/FakeLitOpaque_SelfOccluded"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _BackFaceDarkening ("Back Face Darkness", Range(0, 1)) = 0.2
        
        // SRP Batcher compatibility
        [HideInInspector] _QueueOffset("Queue offset", Float) = 0.0
        [HideInInspector] _QueueControl("Queue control", Float) = -1.0
        
        // NOTE: Global properties (_GlobalFakeLightDir, _GlobalFakeAmbient, _GlobalFakeLightIntensity)
        // are NOT declared here to prevent per-material overrides.
        // They are set globally via Shader.SetGlobal*() and declared only in the shader code.
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "Queue"="Geometry"
            "RenderPipeline"="UniversalPipeline"
        }
        
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            
            ZWrite On
            ColorMask 0
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 2.0
            
            #pragma multi_compile_instancing
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            // SRP Batching: Include UnityPerMaterial cbuffer in all passes
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _BackFaceDarkening;
            CBUFFER_END
            
            struct Attributes
            {
                float4 positionOS : POSITION;
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
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            
            half4 DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            Cull Back
            ZWrite On
            ZTest LEqual
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            
            #pragma multi_compile_instancing
            // ✅ FOG: enables linear/exp/exp2 fog variants
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
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
                float3 normalWS : TEXCOORD1;
                // ✅ FOG: plain float, no built-in RP macro
                float fogCoord : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            // ✅ SRP BATCHING: ONLY per-material properties go in UnityPerMaterial cbuffer
            // This cbuffer is what makes materials batchable with SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _BackFaceDarkening;
            CBUFFER_END
            
            // ✅ GLOBAL properties declared OUTSIDE any cbuffer
            // These are set via Shader.SetGlobal*() and live in global shader constant buffer
            // All materials share these values, so they don't break batching
            // ✅ FIXED: Must be half4 to match Shader.SetGlobalVector() which sends Vector4
            half4 _GlobalFakeLightDir;
            half _GlobalFakeAmbient;
            half _GlobalFakeLightIntensity;
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                // ✅ FOG: URP uses ComputeFogFactor(posCS.z), not UNITY_TRANSFER_FOG
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                // Sample texture
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                
                // Normalize interpolated normal
                half3 normalWS = normalize(input.normalWS);
                
                // ✅ FIXED: Extract xyz from the Vector4 global property
                half3 lightDir = _GlobalFakeLightDir.xyz;
                
                // Calculate N·L for lighting
                half NdotL = dot(normalWS, lightDir);
                
                // SELF-OCCLUSION: Faces pointing away from light get darkened
                half facingLight = saturate(NdotL);
                half selfOcclusion = lerp(_BackFaceDarkening, 1.0, facingLight);
                
                // Standard diffuse lighting
                half diffuse = saturate(NdotL) * _GlobalFakeLightIntensity * (1.0 - _GlobalFakeAmbient);
                half lighting = _GlobalFakeAmbient + diffuse;
                
                // Apply self-occlusion to final lighting
                lighting *= selfOcclusion;
                
                // Apply to albedo
                albedo.rgb *= lighting;
                
                // ✅ FOG: URP uses MixFog(), not UNITY_APPLY_FOG
                albedo.rgb = MixFog(albedo.rgb, input.fogCoord);
                
                return albedo;
            }
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}