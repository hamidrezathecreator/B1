Shader "Hidden/ThermalObjectReplacement"
{
    Properties
    {
        // ── Heat baseline ────────────────────────────────────────────────────
        _BaseHeat       ("Base Heat",             Range(0,1))     = 0.3
        _HeatVariance   ("Heat Variance",         Range(0,0.4))   = 0.15

        // ── Normal influence ─────────────────────────────────────────────────
        [Header(Normal Based Heat)]
        _NormalHeatBias ("Normal Heat Bias",      Range(0,1))     = 0.35
        [Enum(World Up,0,Camera,1)] _NormalMode   ("Hot-face Direction", Float) = 0

        // ── World-space vertical gradient ────────────────────────────────────
        [Header(Vertical Gradient)]
        _GradientBottom ("Gradient Bottom Heat",  Range(0,1))     = 0.25
        _GradientTop    ("Gradient Top Heat",     Range(0,1))     = 0.4
        _GradientRange  ("Gradient World Range",  Range(0.1,20))  = 4.0
        _GradientCenter ("Gradient Center Y",     Float)          = 0.0

        // ── Noise ────────────────────────────────────────────────────────────
        [Header(Noise)]
        _NoiseStrength  ("Noise Strength",        Range(0,0.5))   = 0.12
        _NoiseScale     ("Noise Scale",           Range(0.1,10))  = 2.0
        _NoiseScale2    ("Noise Scale 2 (detail)",Range(0.1,20))  = 7.0
        _NoiseBlend     ("Noise Octave Blend",    Range(0,1))     = 0.4
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ThermalAmbient"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest  LEqual
            Cull   Back
            ColorMask R

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ── Structs ──────────────────────────────────────────────────────

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;   // world-space normal
                float3 positionWS : TEXCOORD2;   // world-space position
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // ── Textures & samplers ──────────────────────────────────────────

            TEXTURE2D(_GrainTex);
            SAMPLER(sampler_GrainTex);

            // ── Global properties set by ThermalVisionController ─────────────
            // These MUST be globals (outside CBUFFER) so Shader.SetGlobalFloat
            // actually reaches them. Per-material CBuffers are NOT filled by
            // Shader.SetGlobal* calls — that's why the old shader was flat.

            float _BaseHeat;
            float _HeatVariance;
            float _NormalHeatBias;
            float _NormalMode;
            float _GradientBottom;
            float _GradientTop;
            float _GradientRange;
            float _GradientCenter;
            float _NoiseStrength;
            float _NoiseScale;
            float _NoiseScale2;
            float _NoiseBlend;

            // Grain animation (also set globally by controller)
            float _GrainScale;
            float _GrainSpeed;

            // ── Vertex shader ────────────────────────────────────────────────

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS  = vpi.positionCS;
                OUT.positionWS  = vpi.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = IN.uv;
                return OUT;
            }

            // ── Fragment shader ──────────────────────────────────────────────

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // ── 1. Grain animation offset (matches post-process grain) ───
                float2 animOffset = float2(
                    sin(_Time.y * _GrainSpeed * 1.3h) * 0.5h,
                    cos(_Time.y * _GrainSpeed * 0.7h) * 0.5h
                );

                // ── 2. Two-octave noise ──────────────────────────────────────
                //    Octave A: large, slow blobs — gives material clumping
                float2 uvA    = IN.uv * _NoiseScale  * _GrainScale + animOffset;
                half   noiseA = SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, uvA).r;

                //    Octave B: fine detail — breaks up the uniform look
                float2 uvB    = IN.uv * _NoiseScale2 * _GrainScale + animOffset * 0.5h;
                half   noiseB = SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, uvB).r;

                //    Blend: mostly A, some B
                half noise = lerp(noiseA, noiseB, (half)_NoiseBlend);

                // Centre noise at 0 and scale it
                half noiseDelta = (noise - 0.5h) * 2.0h * (half)_NoiseStrength;

                // ── 3. Normal-based heat bias ────────────────────────────────
                //    Surfaces facing the "hot" direction emit more heat.
                half3 nWS = normalize(IN.normalWS);

                half3 hotDir;
                if (_NormalMode < 0.5)
                    hotDir = half3(0, 1, 0);          // world up  (ground absorbs heat)
                else
                    hotDir = -normalize(GetWorldSpaceViewDir(IN.positionWS)); // toward camera

                half NdotHot    = saturate(dot(nWS, hotDir));
                half normalHeat = NdotHot * (half)_NormalHeatBias;

                // ── 4. Vertical world-space gradient ─────────────────────────
                half yNorm = saturate((IN.positionWS.y - (half)_GradientCenter + (half)_GradientRange * 0.5h)
                                      / max(0.001h, (half)_GradientRange));
                half gradientHeat = lerp((half)_GradientBottom, (half)_GradientTop, yNorm);

                // ── 5. Combine ───────────────────────────────────────────────
                //    Base + gradient establishes the "material temperature".
                //    Normal bias adds surface directionality.
                //    Noise breaks up uniformity.
                half heat = (half)_BaseHeat
                          + gradientHeat * (half)_HeatVariance  // gradient modulates variance range
                          + normalHeat                           // directional bias
                          + noiseDelta;                          // per-texel noise

                heat = saturate(heat);

                return half4(heat, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
