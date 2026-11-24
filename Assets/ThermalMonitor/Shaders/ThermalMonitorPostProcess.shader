// Thermal monitor post-process.
// Derived from Hidden/ThermalPostProcess_SuperOptimized_V3 with these changes:
//   * Every uniform is declared in Properties (per-material, so several monitors can have different looks).
//   * Unity 6 / Render Graph: uses Blit.hlsl (Vert, _BlitTexture) and is driven by Blitter.BlitTexture.
//   * Scene detail is read from the blit source (_BlitTexture) instead of _CameraOpaqueTexture,
//     so the camera does NOT need "Opaque Texture" enabled.
//   * Thermal data texture + texel size are set on the material by the render pass.
Shader "Hidden/ThermalMonitor/PostProcess"
{
    Properties
    {
        [NoScaleOffset] _ThermalDataTexture ("Thermal Data (set by pass)", 2D) = "black" {}
        _ThermalDataTexelSize ("Thermal Data Texel Size (set by pass)", Vector) = (0.001,0.001,1000,1000)

        _ColdColor ("Cold", Color) = (0,0,0,1)
        _MidColor  ("Mid",  Color) = (0.35,0.35,0.35,1)
        _HotColor  ("Hot",  Color) = (1,1,1,1)

        _Brightness     ("Brightness", Float) = 1
        _Contrast       ("Contrast",   Float) = 1
        _BloomIntensity ("Hot Blur Intensity", Float) = 0.5
        _BloomThreshold ("Hot Threshold", Float) = 0.7
        _HotBlurRadius  ("Hot Blur Radius (texels)", Float) = 2

        _GrainIntensity ("Grain Intensity", Float) = 0.5
        _GrainBalance   ("Grain Balance",   Float) = 0.8
        _GrainScale     ("Grain Scale",     Float) = 2
        _GrainAnimSpeed ("Grain Anim Speed", Float) = 1
        _ThermalGrainTime ("Grain time (set by pass)", Float) = 0
        _GrainMode      ("Grain Mode (0 Add, 1 Mul, 2 Overlay)", Float) = 0

        _ScanlineIntensity  ("Scanline Intensity", Float) = 0.35
        _ScanlineVertScale  ("Scanline Vertical Scale", Float) = 280
        _ScanlineScrollSpeed ("Scanline Scroll Speed", Float) = 2
        _ScanlineWidth      ("Scanline Band Width", Float) = 0.2

        _InvertBlurRadius ("Halo Radius (texels)", Float) = 2
        _HaloColor        ("Halo Color", Color) = (1,1,1,1)
        _HaloIntensity    ("Halo Intensity", Float) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "ThermalMonitor"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile_local _ _INVERT_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "ThermalMonitorGrainNoise.hlsl"

            // Blit.hlsl provides: Varyings { positionCS, texcoord }, Vert(), _BlitTexture, sampler_LinearClamp

            TEXTURE2D(_ThermalDataTexture); SAMPLER(sampler_ThermalDataTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _ThermalDataTexelSize;
                float4 _ColdColor;
                float4 _MidColor;
                float4 _HotColor;
                float  _Brightness;
                float  _Contrast;
                float  _BloomIntensity;
                float  _BloomThreshold;
                float  _HotBlurRadius;
                float  _GrainIntensity;
                float  _GrainBalance;
                float  _GrainScale;
                float  _GrainAnimSpeed;
                float  _ThermalGrainTime;
                float  _GrainMode;
                float  _ScanlineIntensity;
                float  _ScanlineVertScale;
                float  _ScanlineScrollSpeed;
                float  _ScanlineWidth;
                float  _InvertBlurRadius;
                float4 _HaloColor;
                float  _HaloIntensity;
            CBUFFER_END

            half3 ApplyThermalGradient(half3 values)
            {
                #ifdef _INVERT_ON
                    values = 1.0 - values;
                #endif

                half3 blend     = values * 2.0;
                half3 useMid    = step(0.5, values);
                half3 lowerGrad = lerp(_ColdColor.rgb, _MidColor.rgb, blend);
                half3 upperGrad = lerp(_MidColor.rgb,  _HotColor.rgb, blend - 1.0);
                return lerp(lowerGrad, upperGrad, useMid);
            }

            half3 ProcessThermalValues(half3 values)
            {
                values = saturate(values * _Brightness);
                float contrastFactor = saturate(_Contrast - 1.0);
                values = lerp(values, values * values, contrastFactor);
                return values;
            }

            half HotMask(half processed)
            {
                return saturate((processed - _BloomThreshold) / (1.0 - _BloomThreshold + 0.001));
            }

            half3 ApplyHotFourTapBlur(half3 centerColor, half centerProcessed, float2 uv)
            {
                float2 texel = _ThermalDataTexelSize.xy * _HotBlurRadius;

                half3 acc  = 0;
                half  wSum = 0;

                #define HOT_TAP(dx, dy) \
                { \
                    half v = SAMPLE_TEXTURE2D(_ThermalDataTexture, sampler_ThermalDataTexture, uv + float2(dx, dy)).r; \
                    half3 pv = ProcessThermalValues(half3(v, v, v)); \
                    half  m  = HotMask(pv.r); \
                    acc  += ApplyThermalGradient(pv) * m; \
                    wSum += m; \
                }

                HOT_TAP( texel.x, 0);
                HOT_TAP(-texel.x, 0);
                HOT_TAP(0,  texel.y);
                HOT_TAP(0, -texel.y);

                #undef HOT_TAP

                if (wSum < 0.001)
                    return centerColor;

                half3 blurredHot = acc / wSum;
                half  centerHot  = HotMask(centerProcessed);
                half  spread     = saturate(wSum * 0.25 + centerHot);

                return centerColor + blurredHot * _BloomIntensity * spread;
            }

            half3 ApplyFilmGrain(half3 finalColor, float4 positionCS)
            {
                half grainSample = TMGrainFlickerScreen(positionCS.xy, _GrainScale, _GrainAnimSpeed, _ThermalGrainTime);

                half luminance  = dot(finalColor, half3(0.299h, 0.587h, 0.114h));
                half brightness = saturate(luminance);
                half darkness   = 1.0 - brightness;

                half mask = 1.0;
                if (_GrainBalance >= 0.01)
                {
                    half powerFactor = lerp(1.0, 16.0, _GrainBalance);
                    mask = pow(max(darkness, 0.001), powerFactor);
                }

                half3 result = finalColor;
                if (_GrainMode < 0.5)            // Additive
                {
                    half grain = (grainSample - 0.5) * 2.0;
                    result = finalColor + grain * _GrainIntensity * mask;
                }
                else if (_GrainMode < 1.5)       // Multiply
                {
                    half grainMult = 1.0 - grainSample;
                    result = lerp(finalColor, finalColor * grainMult, _GrainIntensity * mask);
                }
                else                             // Overlay
                {
                    half grain = (grainSample - 0.5) * 4.0;
                    result = finalColor * (1.0 + grain * _GrainIntensity * mask);
                }
                return result;
            }

            half3 ApplyScanlines(half3 color, float2 uv)
            {
                half shade = TMScanlineShade(uv, _ScanlineVertScale, _ScanlineScrollSpeed, _ScanlineWidth,
                    _ScanlineIntensity, _ThermalGrainTime);
                return color * shade;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // 1. Thermal data (skybox never draws into this — stays black)
                half thermalValue = SAMPLE_TEXTURE2D(_ThermalDataTexture, sampler_ThermalDataTexture, uv).r;

                // 2. Scene detail (luminance from camera colour; sky heat is pre-baked in the data pass)
                float3 sceneRGB      = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float sceneLuminance = dot(sceneRGB, float3(0.299, 0.587, 0.114));
                float sceneDetail    = sqrt(sceneLuminance) * 0.4;
                float detailBlend    = 1.0 - saturate(thermalValue * 5.0);
                thermalValue = lerp(thermalValue, (half)max(thermalValue, sceneDetail), detailBlend * 0.5);


                // 3. Process + 4. gradient
                half3 processedValues = ProcessThermalValues(half3(thermalValue, thermalValue, thermalValue));
                half3 thermalColor    = ApplyThermalGradient(processedValues);

                // 5. Edge halo (before bloom so bloom can lift hot edges)
                float2 texel = _ThermalDataTexelSize.xy * _InvertBlurRadius;
                half tR = SAMPLE_TEXTURE2D(_ThermalDataTexture, sampler_ThermalDataTexture, uv + float2( texel.x, 0)).r;
                half tL = SAMPLE_TEXTURE2D(_ThermalDataTexture, sampler_ThermalDataTexture, uv + float2(-texel.x, 0)).r;
                half tU = SAMPLE_TEXTURE2D(_ThermalDataTexture, sampler_ThermalDataTexture, uv + float2(0,  texel.y)).r;
                half tD = SAMPLE_TEXTURE2D(_ThermalDataTexture, sampler_ThermalDataTexture, uv + float2(0, -texel.y)).r;

                half thermalAvg      = (tR + tL + tU + tD) * 0.25;
                half thermalVariance = abs(thermalValue - thermalAvg);
                half edgeMask        = saturate(thermalVariance * 20.0);

                if (edgeMask > 0.01 && thermalValue < thermalAvg)
                {
                    half3 haloColor = _HaloColor.rgb * thermalAvg;
                    thermalColor = lerp(thermalColor, haloColor, edgeMask * _HaloIntensity);
                }

                // 6. Hot four-tap blur (only above-threshold taps contribute)
                thermalColor = ApplyHotFourTapBlur(thermalColor, processedValues.r, uv);

                // Pre-grain colour (palette + halo + bloom); grain is the only step after this.
                half3 finalColor = saturate(thermalColor);

                // 7. Film grain
                finalColor = ApplyFilmGrain(finalColor, input.positionCS);

                // 8. Scanlines — on top of grain (tall vertical field, scrolls down)
                finalColor = ApplyScanlines(finalColor, uv);

                return half4(saturate(finalColor), 1.0);
            }
            ENDHLSL
        }
    }
}
