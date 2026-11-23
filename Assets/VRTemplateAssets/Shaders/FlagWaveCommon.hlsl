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
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

#define FLAG_TWO_PI 6.28318530718

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

void ApplyFlagWave(float2 uv, inout float3 positionOS, inout float3 normalOS)
{
    float time = _TimeParameters.x * _WindSpeed;

    float3 windDir = normalize(_WindDir.xyz + float3(1e-5, 0.0, 0.0));
    float3 rippleDir = normalize(_RippleDir.xyz + float3(0.0, 1e-5, 0.0));

    float windDisp;
    float rippleDisp;
    FlagWaveComponents(uv, time, windDisp, rippleDisp);

    float3 offset = windDir * windDisp + rippleDir * rippleDisp;
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
    normalOS = normalize(lerp(normalOS, wavedNormal, FlagTetherMask(uv)));
}

#endif