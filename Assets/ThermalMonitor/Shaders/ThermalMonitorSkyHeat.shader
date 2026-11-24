// Writes skybox luminance into the R channel of the thermal data target (ColorMask R).
Shader "Hidden/ThermalMonitor/SkyHeat"
{
    Properties {}

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SkyHeat"
            ZWrite Off
            ZTest Always
            Cull Off
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURECUBE(_TM_SkyboxCube);
            SAMPLER(sampler_TM_SkyboxCube);
            TEXTURE2D(_TM_Skybox2D);
            SAMPLER(sampler_TM_Skybox2D);

            float4x4 _TM_InvViewProj;
            float    _TM_SkyHeatMul;
            float    _TM_SkyHeatBias;
            float    _TM_SkyPanoramic;
            float    _TM_SkyRotation;

            float3 ViewDirectionWS(float2 uv)
            {
                float4 ndc = float4(uv * 2.0 - 1.0, 1.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                ndc.y = -ndc.y;
                #endif
                float4 worldH = mul(_TM_InvViewProj, ndc);
                float3 worldPos = worldH.xyz / worldH.w;
                return normalize(worldPos - _WorldSpaceCameraPos.xyz);
            }

            float2 DirectionToLatLong(float3 dir)
            {
                float2 latLong;
                latLong.x = atan2(dir.z, dir.x) * 0.159154943f + 0.5;
                latLong.y = asin(saturate(dir.y)) * 0.318309886f + 0.5;
                latLong.x += _TM_SkyRotation;
                return latLong;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = ViewDirectionWS(input.texcoord);
                half3 skyRgb;

                if (_TM_SkyPanoramic > 0.5)
                {
                    float2 skyUV = DirectionToLatLong(dir);
                    skyRgb = SAMPLE_TEXTURE2D(_TM_Skybox2D, sampler_TM_Skybox2D, skyUV).rgb;
                }
                else
                {
                    skyRgb = SAMPLE_TEXTURECUBE(_TM_SkyboxCube, sampler_TM_SkyboxCube, dir).rgb;
                }

                half lum = dot(skyRgb, half3(0.299h, 0.587h, 0.114h));
                half heat = saturate(pow(max(lum, 0.001h), 0.55h) * (half)_TM_SkyHeatMul + (half)_TM_SkyHeatBias);
                return half4(heat, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}