#ifndef FLAG_WAVE_COMMON_INCLUDED
#define FLAG_WAVE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _WindSpeed;
    half _WaveAmplitude;
    half _WaveFrequency;
    half _SecondHarmonic;
    half _RippleAmplitude;
    half _RippleFrequency;
    half _NoiseScale;
    half _NoiseStrength;
    half _DualSidePoles;
    half _PoleAtMaxU;
    half _UseTopPole;
    half _TopAtMaxV;
    half _TetherPower;
    half _TopExcludeDistance;
    half _TopExcludeSoftness;
    half4 _WindDir;
    half4 _RippleDir;
    half _Smoothness;
    half _Cutoff;
    half _EnableDistanceCull;
    half _CullDistance;
    half _CullFadeRange;
    half _UseClothLightMaps;
    half _MotionThresholdLow;
    half _MotionThresholdHigh;
    half _MotionBlendSoftness;
    half _MotionIntensityScale;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_ClothMap2);
SAMPLER(sampler_ClothMap2);
TEXTURE2D(_ClothMap3);
SAMPLER(sampler_ClothMap3);

#define FLAG_TWO_PI 6.28318530718

float FlagDistanceToCamera(float3 positionWS)
{
    return distance(GetCameraPositionWS(), positionWS);
}

float FlagGetDetailFactor()
{
    if (_EnableDistanceCull < 0.5)
        return 1.0;

    float3 pivotWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float dist = FlagDistanceToCamera(pivotWS);

    if (dist >= _CullDistance)
        return 0.0;

    float fadeStart = max(_CullDistance - _CullFadeRange, 0.0);
    if (dist <= fadeStart || _CullFadeRange <= 1e-5)
        return 1.0;

    return saturate((_CullDistance - dist) / _CullFadeRange);
}

float FlagHash21(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float FlagValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = FlagHash21(i);
    float b = FlagHash21(i + float2(1.0, 0.0));
    float c = FlagHash21(i + float2(0.0, 1.0));
    float d = FlagHash21(i + float2(1.0, 1.0));

    float x1 = lerp(a, b, f.x);
    float x2 = lerp(c, d, f.x);
    return lerp(x1, x2, f.y);
}

float FlagTopExclusionMask(float2 uv)
{
    if (_TopExcludeDistance <= 1e-5)
        return 1.0;

    float distFromTop = (_TopAtMaxV > 0.5) ? (1.0 - uv.y) : uv.y;
    return saturate((distFromTop - _TopExcludeDistance) / max(_TopExcludeSoftness, 1e-4));
}

float FlagTetherMask(float2 uv)
{
    float tetherU;
    if (_DualSidePoles > 0.5)
    {
        float fromCenter = min(uv.x, 1.0 - uv.x) * 2.0;
        tetherU = pow(saturate(fromCenter), _TetherPower);
    }
    else
    {
        float fromSidePole = (_PoleAtMaxU > 0.5) ? (1.0 - uv.x) : uv.x;
        tetherU = pow(saturate(fromSidePole), _TetherPower);
    }

    float tetherV = 1.0;
    if (_UseTopPole > 0.5)
    {
        float fromTopPole = (_TopAtMaxV > 0.5) ? (1.0 - uv.y) : uv.y;
        tetherV = pow(saturate(fromTopPole), _TetherPower);
    }

    return tetherU * tetherV * FlagTopExclusionMask(uv);
}

void FlagWaveComponents(float2 uv, float time, out float windDisp, out float rippleDisp)
{
    float tether = FlagTetherMask(uv);

    float2 noiseUv = uv * _NoiseScale + float2(time * 0.09, time * 0.05);
    float n0 = FlagValueNoise(noiseUv);
    float n1 = FlagValueNoise(noiseUv * 1.97 + 13.7);
    float turbulence = lerp(n0, n1, 0.4) * 2.0 - 1.0;

    float phase = uv.y * _WaveFrequency * FLAG_TWO_PI + time + turbulence * _NoiseStrength;
    float wave = sin(phase);
    wave += sin(phase * 1.93 + 0.75) * _SecondHarmonic;

    windDisp = tether * (wave * _WaveAmplitude + turbulence * _NoiseStrength * _WaveAmplitude * 0.35);
    rippleDisp = tether * sin(uv.x * _RippleFrequency * FLAG_TWO_PI + time * 1.17) * _RippleAmplitude;
}

float FlagMotionIntensity(float2 uv)
{
    float detail = FlagGetDetailFactor();
    if (detail <= 1e-4)
        return 0.0;

    float time = _TimeParameters.x * _WindSpeed;
    float tether = FlagTetherMask(uv);

    float windDisp;
    float rippleDisp;
    FlagWaveComponents(uv, time, windDisp, rippleDisp);

    const float epsilon = 0.0025;
    float windDU;
    float rippleDU;
    float windDV;
    float rippleDV;
    FlagWaveComponents(uv + float2(epsilon, 0.0), time, windDU, rippleDU);
    FlagWaveComponents(uv + float2(0.0, epsilon), time, windDV, rippleDV);

    float3 windDir = normalize(_WindDir.xyz + float3(1e-5, 0.0, 0.0));
    float3 rippleDir = normalize(_RippleDir.xyz + float3(0.0, 1e-5, 0.0));

    float3 offset0 = windDir * windDisp + rippleDir * rippleDisp;
    float3 offsetU = windDir * windDU + rippleDir * rippleDU;
    float3 offsetV = windDir * windDV + rippleDir * rippleDV;

    float displacement = length(offset0);
    float fold = (length(offsetU - offset0) + length(offsetV - offset0)) / epsilon;
    float waveAmp = max(_WaveAmplitude + _RippleAmplitude, 1e-4);
    float foldNorm = fold / max(waveAmp * 6.0, 1e-4);

    return saturate((displacement / waveAmp + foldNorm * 0.65) * tether * _MotionIntensityScale) * detail;
}

half4 FlagSampleClothAlbedo(float2 uv, half motion, half detail)
{
    half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
    if (_UseClothLightMaps < 0.5 || detail <= 1e-4)
        return base;

    half4 lit2 = SAMPLE_TEXTURE2D(_ClothMap2, sampler_ClothMap2, uv);
    half4 lit3 = SAMPLE_TEXTURE2D(_ClothMap3, sampler_ClothMap3, uv);

    half soft = max(_MotionBlendSoftness, 1e-3);
    half blend2 = smoothstep(_MotionThresholdLow - soft, _MotionThresholdLow + soft, motion);
    half blend3 = smoothstep(_MotionThresholdHigh - soft, _MotionThresholdHigh + soft, motion);

    half4 mixed = lerp(base, lit2, blend2);
    mixed = lerp(mixed, lit3, blend3);
    return mixed;
}

void ApplyFlagWave(float2 uv, inout float3 positionOS, inout float3 normalOS)
{
    float detail = FlagGetDetailFactor();
    if (detail <= 1e-4)
        return;

    float3 normalIn = normalOS;
    float time = _TimeParameters.x * _WindSpeed;

    float3 windDir = normalize(_WindDir.xyz + float3(1e-5, 0.0, 0.0));
    float3 rippleDir = normalize(_RippleDir.xyz + float3(0.0, 1e-5, 0.0));

    float windDisp;
    float rippleDisp;
    FlagWaveComponents(uv, time, windDisp, rippleDisp);

    float3 offset = (windDir * windDisp + rippleDir * rippleDisp) * detail;
    positionOS += offset;

    const float epsilon = 0.0025;
    float windDU;
    float rippleDU;
    float windDV;
    float rippleDV;
    FlagWaveComponents(uv + float2(epsilon, 0.0), time, windDU, rippleDU);
    FlagWaveComponents(uv + float2(0.0, epsilon), time, windDV, rippleDV);

    float3 offsetDU = windDir * (windDU - windDisp) + rippleDir * (rippleDU - rippleDisp);
    float3 offsetDV = windDir * (windDV - windDisp) + rippleDir * (rippleDV - rippleDisp);

    float3 tangentU = normalize(float3(1.0, 0.0, 0.0) + offsetDU / epsilon);
    float3 tangentV = normalize(float3(0.0, 1.0, 0.0) + offsetDV / epsilon);
    float3 wavedNormal = normalize(cross(tangentU, tangentV));
    normalOS = normalize(lerp(normalIn, wavedNormal, FlagTetherMask(uv) * detail));
}

#endif