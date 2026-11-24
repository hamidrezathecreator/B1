Shader "Universal Render Pipeline/Hamidreza/Screen Space Decal/Thermal Only"
{
    Properties
    {
        [Header(Decal Projection)]
        [MainTexture]_MainTex("Heat Pattern Texture", 2D) = "white" {}
        
        [Header(Thermal Heat Settings)]
        [Range(0, 1)] _ThermalHeat("Heat Intensity", Float) = 0.8
        [MainColor]_Color("Heat Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent-450"  // FIXED: Moved earlier in queue to render below other transparents
            "DisableBatching" = "True" 
            "RenderPipeline" = "UniversalPipeline"
        }

        // ═══════════════════════════════════════════════════════════
        // THERMAL VISION ONLY - No Normal Rendering
        // ═══════════════════════════════════════════════════════════
        Pass
        {
            Name "ThermalVision"
            Tags { "LightMode" = "ThermalVision" }

            Cull Back
            ZTest LEqual
            ZWrite Off
            
            // Additive blending for heat accumulation
            Blend One One
            BlendOp Add
            ColorMask R  // Only write to RED channel (heat)

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
                
                // Calculate view ray in object space for projection
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

                // Reconstruct world position from depth
                i.viewRayOS.xyz /= i.viewRayOS.w;
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = tex2D(_CameraDepthTexture, screenUV).r;
                float sceneDepthVS = LinearEyeDepth(sceneDepth, _ZBufferParams);
                float3 objectPos = i.cameraPosOS + i.viewRayOS.xyz * sceneDepthVS;
                
                // Clip pixels outside decal box volume
                clip(0.5 - abs(objectPos));
                
                // Project texture onto surface
                float2 uv = (objectPos.xy + 0.5) * _MainTex_ST.xy + _MainTex_ST.zw;
                half4 decalSample = tex2D(_MainTex, uv);
                
                // Calculate final heat contribution
                // Texture alpha acts as mask, Color alpha as intensity multiplier
                half heatContribution = _ThermalHeat * decalSample.a * _Color.a;
                
                // Discard near-zero heat (optimization)
                if (heatContribution < 0.001)
                    discard;
                
                // Output only red channel (heat)
                return half4(heatContribution, 0, 0, 0);
            }
            ENDHLSL
        }
    }
    
    FallBack Off
}
