Shader "Universal Render Pipeline/Hamidreza/Screen Space Decal/Unlit Mobile Thermal"
{
    Properties
    {
        [Header(Basic)]
        [MainTexture]_MainTex("Texture", 2D) = "white" {}
        [MainColor]_Color("Color", Color) = (1,1,1,1)

        [Header(Thermal Vision)]
        [Toggle(_THERMAL_ENABLED)] _ThermalEnabled("Enable Thermal Rendering", Float) = 1
        [Range(0, 1)] _ThermalHeat("Thermal Heat", Float) = 0.5
    }

    SubShader
    {
        Tags { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent-499" 
            "DisableBatching" = "True" 
            "RenderPipeline" = "UniversalPipeline"
        }

        // ═══════════════════════════════════════════════════════════
        // PASS 0: Standard Unlit Rendering
        // ═══════════════════════════════════════════════════════════
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZTest LEqual
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float4 viewRayOS : TEXCOORD1;
                float3 cameraPosOS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;

            CBUFFER_START(UnityPerMaterial)               
                float4 _MainTex_ST;
                half4 _Color;
                half _ThermalHeat;
            CBUFFER_END

            v2f vert(appdata input) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS);
                o.positionCS = vertexInput.positionCS;
                o.screenPos = ComputeScreenPos(o.positionCS);
                float3 viewRay = vertexInput.positionVS;
                o.viewRayOS.w = viewRay.z;
                viewRay *= -1;
                float4x4 ViewToObjectMatrix = mul(UNITY_MATRIX_I_M, UNITY_MATRIX_I_V);
                o.viewRayOS.xyz = mul((float3x3)ViewToObjectMatrix, viewRay);
                o.cameraPosOS = mul(ViewToObjectMatrix, float4(0,0,0,1)).xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                i.viewRayOS.xyz /= i.viewRayOS.w;
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = tex2D(_CameraDepthTexture, screenUV).r;
                float sceneDepthVS = LinearEyeDepth(sceneDepth, _ZBufferParams);
                float3 objectPos = i.cameraPosOS + i.viewRayOS.xyz * sceneDepthVS;
                clip(0.5 - abs(objectPos));
                float2 uv = (objectPos.xy + 0.5) * _MainTex_ST.xy + _MainTex_ST.zw;
                return tex2D(_MainTex, uv) * _Color;
            }
            ENDHLSL
        }

        // ═══════════════════════════════════════════════════════════
        // PASS 1: Thermal Vision Rendering - FIXED
        // ═══════════════════════════════════════════════════════════
        Pass
        {
            Name "ThermalVision"
            Tags { "LightMode" = "ThermalVision" }

            Cull Back
            ZTest LEqual
            ZWrite Off
            
            // FIXED: Changed to additive blending (same as Universal Thermal FIXED)
            Blend One One
            BlendOp Add
            ColorMask R  // FIXED: Only write to RED channel

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _THERMAL_ENABLED

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float4 viewRayOS : TEXCOORD1;
                float3 cameraPosOS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;

            CBUFFER_START(UnityPerMaterial)               
                float4 _MainTex_ST;
                half4 _Color;
                half _ThermalHeat;
            CBUFFER_END

            v2f vert(appdata input) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS);
                o.positionCS = vertexInput.positionCS;
                o.screenPos = ComputeScreenPos(o.positionCS);
                float3 viewRay = vertexInput.positionVS;
                o.viewRayOS.w = viewRay.z;
                viewRay *= -1;
                float4x4 ViewToObjectMatrix = mul(UNITY_MATRIX_I_M, UNITY_MATRIX_I_V);
                o.viewRayOS.xyz = mul((float3x3)ViewToObjectMatrix, viewRay);
                o.cameraPosOS = mul(ViewToObjectMatrix, float4(0,0,0,1)).xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);

                #ifndef _THERMAL_ENABLED
                    discard;
                #endif

                i.viewRayOS.xyz /= i.viewRayOS.w;
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = tex2D(_CameraDepthTexture, screenUV).r;
                float sceneDepthVS = LinearEyeDepth(sceneDepth, _ZBufferParams);
                float3 objectPos = i.cameraPosOS + i.viewRayOS.xyz * sceneDepthVS;
                
                clip(0.5 - abs(objectPos));
                
                float2 uv = (objectPos.xy + 0.5) * _MainTex_ST.xy + _MainTex_ST.zw;
                half4 decalSample = tex2D(_MainTex, uv);
                
                // FIXED: Match the Universal Thermal FIXED output format
                half heatContribution = _ThermalHeat * decalSample.a * _Color.a;
                
                // With ColorMask R, only red channel matters
                return half4(heatContribution, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
