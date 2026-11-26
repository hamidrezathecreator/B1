#ifndef THERMAL_MONITOR_GRAIN_NOISE_INCLUDED
#define THERMAL_MONITOR_GRAIN_NOISE_INCLUDED

// Heat variation (replacement pass): stable procedural noise — baked into thermal data first.
// Film grain (post pass): temporal screen flicker — applied last on the final image.

float TMHash21(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float TMHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float TMValueNoise(float2 uv)
{
    float2 i = floor(uv);
    float2 f = frac(uv);
    f = f * f * (3.0 - 2.0 * f);

    float a = TMHash21(i);
    float b = TMHash21(i + float2(1.0, 0.0));
    float c = TMHash21(i + float2(0.0, 1.0));
    float d = TMHash21(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Replacement / ambient heat — no time; goes through palette → halo → bloom before film grain.
float TMHeatVariationNoise(float2 uv)
{
    float n = TMValueNoise(uv);
    n += TMValueNoise(uv * 2.71 + 17.31) * 0.35;
    return saturate(n / 1.35);
}

// Post film grain — temporal flicker on top of everything (including heat noise from data pass).
float TMGrainFlickerScreen(float2 screenPx, float scale, float speed, float grainTime)
{
    float hz = max(speed, 0.01) * 30.0;
    float timeStep = floor(grainTime * hz);

    float cellPx = max(1.5, 4.0 / max(scale, 0.1));
    float2 cell = floor(screenPx / cellPx);

    float n0 = TMHash31(float3(cell,         timeStep));
    float n1 = TMHash31(float3(cell + 17.0,  timeStep + 1.3));
    float n2 = TMHash31(float3(cell + 113.0, timeStep * 0.71));

    return saturate(n0 * 0.5 + n1 * 0.3 + n2 * 0.2);
}

// Tall vertical stripe field (like a high-resolution scanline texture), scrolled downward.
half TMScanlineShade(float2 uv, float vertScale, float scrollSpeed, float lineWidth, float intensity, float time)
{
    if (intensity < 0.001)
        return 1.0;

    float v = uv.y * max(vertScale, 1.0) - time * scrollSpeed;
    float stripe = frac(v);

    float dist = abs(stripe - 0.5) * 2.0;
    half scanlineMask = smoothstep(1.0, saturate(1.0 - lineWidth), dist);

    return lerp(1.0 - intensity, 1.0, scanlineMask);
}

#endif
