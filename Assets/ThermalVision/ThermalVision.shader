Shader "Hidden/ThermalPostProcess_SuperOptimized_V3"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _GrainTex ("Grain Texture", 2D) = "gray" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Cull Off 
        ZWrite Off 
        ZTest Always

        Pass
        {
            Name "ThermalVision"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ _INVERT_ON
            #pragma multi_compile_local _ QUALITY_LOW QUALITY_MEDIUM QUALITY_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);
            TEXTURE2D(_ThermalDataTexture);
            SAMPLER(sampler_ThermalDataTexture);
            TEXTURE2D(_GrainTex);
            SAMPLER(sampler_GrainTex);

            float4 _ColdColor;
            float4 _MidColor;
            float4 _HotColor;
            float _BloomIntensity;
            float _BloomThreshold;
            float _Contrast;
            float _Brightness;
            float _GrainIntensity;
            float _GrainBalance;
            float _GrainScale;
            float _GrainSpeed;
            int _GrainMode; // 0=Additive, 1=Multiply, 2=Overlay
            float _InvertBlurRadius;
            float4 _ThermalDataTexture_TexelSize;
            float4 _HaloColor;
            float _HaloIntensity;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // ═══════════════════════════════════════════════════════════
            // GRADIENT & PROCESSING FUNCTIONS
            // ═══════════════════════════════════════════════════════════
            half3 ApplyThermalGradient(half3 values)
            {
                #ifdef _INVERT_ON
                    values = 1.0 - values;
                #endif
                
                half3 blend = values * 2.0;
                half3 useMid = step(0.5, values);
                
                half3 lowerGrad = lerp(_ColdColor.rgb, _MidColor.rgb, blend);
                half3 upperGrad = lerp(_MidColor.rgb, _HotColor.rgb, blend - 1.0);
                
                return lerp(lowerGrad, upperGrad, useMid);
            }

            half3 ProcessThermalValues(half3 values)
            {
                values = saturate(values * _Brightness);
                float contrastFactor = saturate(_Contrast - 1.0);
                values = lerp(values, values * values, contrastFactor);
                return values;
            }

            // ═══════════════════════════════════════════════════════════
            // GRAIN TEXTURE SAMPLING
            // ═══════════════════════════════════════════════════════════
            half3 ApplyGrainTexture(half3 color, half3 processedValues, float2 uv)
            {
                #if !defined(QUALITY_LOW)
                    // Branchless bloom
                    half3 bloomMask = step(_BloomThreshold, processedValues);
                    half3 bloomAmount = (processedValues - _BloomThreshold) / (1.0 - _BloomThreshold + 0.001);
                    bloomAmount = bloomAmount * sqrt(bloomAmount) * bloomMask;
                    color += _HotColor.rgb * bloomAmount * _BloomIntensity * 0.5;
                #endif
                
                // Calculate animation offset ONCE
                float2 offset = float2(
                    sin(_Time.y * _GrainSpeed * 1.3) * 0.5,
                    cos(_Time.y * _GrainSpeed * 0.7) * 0.5
                );
                
                // Sample grain texture
                float2 grainUV = uv * _GrainScale + offset;
                half grainSample = SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, grainUV).r;
                
                // Calculate darkness (1.0 = dark, 0.0 = bright)
                half brightness = saturate(processedValues.r);
                half darkness = 1.0 - brightness;
                
                // EXTREME balance curve
                // balance=0: mask=1 (uniform everywhere)
                // balance=1: mask=darkness^16 (ONLY pitch black gets grain)
                half mask;
                if (_GrainBalance < 0.01)
                {
                    mask = 1.0; // Uniform
                }
                else
                {
                    half powerFactor = lerp(1.0, 16.0, _GrainBalance);
                    mask = pow(max(darkness, 0.001), powerFactor);
                }
                
                // Apply grain based on mode
                half3 result = color;
                
                if (_GrainMode == 0) // ADDITIVE (default - adds sparkles)
                {
                    // Use grain directly, amplified
                    half grain = (grainSample - 0.5) * 2.0; // -1 to +1 range
                    result = color + grain * _GrainIntensity * mask;
                }
                else if (_GrainMode == 1) // MULTIPLY (darkens with grain)
                {
                    // Invert for multiply (white speckles = dark)
                    half grainMult = 1.0 - grainSample;
                    half lerpAmount = _GrainIntensity * mask;
                    result = lerp(color, color * grainMult, lerpAmount);
                }
                else // OVERLAY (mix of both)
                {
                    half grain = (grainSample - 0.5) * 4.0; // -2 to +2 range (amplified)
                    result = color * (1.0 + grain * _GrainIntensity * mask);
                }
                
                return result;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                
                // STEP 1: Sample thermal data
                half thermalValue = SAMPLE_TEXTURE2D(_ThermalDataTexture, sampler_ThermalDataTexture, uv).r;
                
                // STEP 2: Scene detail blending
                #if !defined(QUALITY_LOW)
                    float4 sceneColor = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv);
                    float sceneLuminance = dot(sceneColor.rgb, float3(0.299, 0.587, 0.114));
                    
                    #if defined(QUALITY_HIGH)
                        float sceneDetail = sqrt(sceneLuminance) * 0.4;
                    #else 
                        float sceneDetail = sqrt(sceneLuminance) * 0.25;
                    #endif
                    
                    float detailBlend = 1.0 - saturate(thermalValue * 5.0);
                    thermalValue = lerp(thermalValue, max(thermalValue, sceneDetail), detailBlend * 0.5);
                #endif
                
                // STEP 3: Process thermal value
                half3 processedValues = ProcessThermalValues(half3(thermalValue, thermalValue, thermalValue));
                
                // STEP 4: Apply thermal gradient
                half3 thermalColor = ApplyThermalGradient(processedValues);
                
                // STEP 5: Apply Inverted Blur (Edge Halo)
                float2 texelSize = _ThermalDataTexture_TexelSize.xy * _InvertBlurRadius;
                half thermalRight = SAMPLE_TEXTURE2D(_ThermalDataTexture, sampler_ThermalDataTexture, uv + float2(texelSize.x, 0)).r;
                half thermalLeft = SAMPLE_TEXTURE2D(_ThermalDataTexture, sampler_ThermalDataTexture, uv + float2(-texelSize.x, 0)).r;
                half thermalUp = SAMPLE_TEXTURE2D(_ThermalDataTexture, sampler_ThermalDataTexture, uv + float2(0, texelSize.y)).r;
                half thermalDown = SAMPLE_TEXTURE2D(_ThermalDataTexture, sampler_ThermalDataTexture, uv + float2(0, -texelSize.y)).r;
                
                half thermalAvg = (thermalRight + thermalLeft + thermalUp + thermalDown) * 0.25;
                half thermalVariance = abs(thermalValue - thermalAvg);
                half edgeMask = saturate(thermalVariance * 20.0); 
                
                if (edgeMask > 0.01 && thermalValue < thermalAvg)
                {
                    half3 haloColor = _HaloColor.rgb * thermalAvg;
                    thermalColor = lerp(thermalColor, haloColor, edgeMask * _HaloIntensity);
                }
                
                // STEP 6: Apply grain texture and bloom
                thermalColor = ApplyGrainTexture(thermalColor, processedValues, uv);
                
                return half4(saturate(thermalColor), 1.0);
            }
            ENDHLSL
        }
    }
}
