Shader "Custom/HeatmapObject_Particles_Thermal"
{
    Properties
    {
        [MainTexture] _BaseMap ("Particle Texture (Alpha for Shape)", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)
        
        [Header(Particle Settings)]
        [Toggle(USE_SOFT_PARTICLES)] _UseSoftParticles("Soft Particles", Float) = 0
        _InvFade ("Soft Particle Factor", Range(0.01,3.0)) = 1.0

        [Header(Thermal Vision)]
        [Toggle(_THERMAL_ENABLED)] _ThermalEnabled("Enable Thermal Rendering", Float) = 1
        _HeatMap ("Heat Map (R=Heat, A=Mask)", 2D) = "white" {}
        [Toggle(_USE_HEAT_ALPHA)] _UseHeatAlpha("Use Heat Map Alpha Channel", Float) = 1
        _HeatIntensity ("Heat Intensity", Range(0,10)) = 1.0
        
        [Header(Heat Behavior)]
        [Range(0.5, 3.0)] _HeatFalloff("Heat Falloff Curve", Float) = 1.0
        [Range(0, 1.0)] _MinHeatAtDeath("Min Heat Floor", Float) = 0.0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            // ✅ NO "RenderPipeline" tag here — required so Pass 1 (ThermalVision)
            //    is visible to the render feature's DrawRenderers call.
            //    URP still picks up Pass 0 via its LightMode tag.
            "IgnoreProjector" = "True"
        }

        // ═══════════════════════════════════════════════════════════
        // PASS 0: Standard Visuals (URP / HLSL)
        // ═══════════════════════════════════════════════════════════
        Pass
        {
            Name "ParticleForward"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local USE_SOFT_PARTICLES

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                #ifdef USE_SOFT_PARTICLES
                float4 projPos    : TEXCOORD1;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _InvFade;
                float  _ThermalEnabled;
                float  _HeatIntensity;
                float  _HeatFalloff;
                float  _MinHeatAtDeath;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color      = input.color;
                #ifdef USE_SOFT_PARTICLES
                output.projPos    = ComputeScreenPos(output.positionCS);
                #endif
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 texColor   = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 finalColor = texColor * input.color * _BaseColor;
                
                #ifdef USE_SOFT_PARTICLES
                float sceneZ  = LinearEyeDepth(SampleSceneDepth(input.projPos.xy / input.projPos.w), _ZBufferParams);
                float partZ   = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float fade    = saturate(_InvFade * (sceneZ - partZ));
                finalColor.a *= fade;
                #endif
                
                return finalColor;
            }
            ENDHLSL
        }

        // ═══════════════════════════════════════════════════════════
        // PASS 1: Thermal Vision (HLSL — matches working decal pattern)
        //
        // WHY HLSL (changed from CG):
        //   The working decal shader uses HLSL for its ThermalVision pass and
        //   is confirmed visible to the render feature. The original CG pass
        //   was architecturally justified (custom ShaderTagId + DrawRenderers),
        //   but the render feature's ThermalTransparentPass uses HLSL-compiled
        //   URP internals that route correctly when the pass is also HLSL.
        //   CG passes are silently skipped on some URP versions during
        //   DrawRenderers with a custom ShaderTagId at AfterRenderingTransparents.
        //
        // WHY ZTest Always (kept):
        //   Particles use ZWrite Off, so scene depth sits in the depth buffer.
        //   LEqual would fail for every particle fragment. Always bypasses this.
        //   (Same reasoning as the original — this was correct.)
        //
        // WHY shader_feature_local _THERMAL_ENABLED (changed from float check):
        //   The working decal uses #pragma shader_feature_local _THERMAL_ENABLED
        //   and #ifndef _THERMAL_ENABLED / discard. This is a compile-time branch
        //   driven by the material keyword, NOT a runtime float uniform. The
        //   original CG runtime check (if _ThermalEnabled < 0.5) fails when
        //   Shader.SetGlobalFloat hasn't been called yet on the first frame,
        //   causing all fragments to discard until the controller initialises.
        //   The keyword approach is also faster — zero ALU on the disabled path.
        //
        // WHY _HeatIntensity / _HeatFalloff / _MinHeatAtDeath moved into CBUFFER:
        //   Plain globals (outside CBUFFER) are not visible to the HLSL SRP
        //   batcher path. Moving them into CBUFFER_START(UnityPerMaterial) makes
        //   them reachable from both material.SetFloat() and Shader.SetGlobalFloat().
        //
        // WHY ColorMask R (kept):
        //   The thermal RT is RHalf (single channel). Writing RGBA wastes
        //   bandwidth and can corrupt channels the compositor reads as zero.
        // ═══════════════════════════════════════════════════════════
        Pass
        {
            Name "ThermalVision"
            Tags { "LightMode" = "ThermalVision" }

            ZWrite Off
            ZTest Always        // ✅ CRITICAL for particles — particles don't write depth
            Cull Off
            Blend One One
            BlendOp Add
            ColorMask R

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _THERMAL_ENABLED
            #pragma shader_feature_local _USE_HEAT_ALPHA

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ── Textures ─────────────────────────────────────────────
            TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
            TEXTURE2D(_HeatMap);    SAMPLER(sampler_HeatMap);

            // ── Per-material uniforms (CBUFFER — required for HLSL/SRP) ─
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _HeatMap_ST;
                float4 _BaseColor;
                half   _HeatIntensity;
                half   _HeatFalloff;
                half   _MinHeatAtDeath;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                // ✅ COLOR.a carries particle lifetime ratio from the particle
                //    system vertex stream (1 = just spawned, 0 = about to die).
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color      = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // Compile-time early-out — zero cost when thermal is disabled.
                // Matches the pattern used by the working decal shader exactly.
                #ifndef _THERMAL_ENABLED
                    discard;
                    return half4(0, 0, 0, 0);
                #endif

                // ── Shape alpha from base texture ──────────────────────
                half shapeAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;

                // ── Heat map sample ────────────────────────────────────
                half4 heatSample = SAMPLE_TEXTURE2D(_HeatMap, sampler_HeatMap, input.uv);
                half  heatValue  = heatSample.r;

                #ifdef _USE_HEAT_ALPHA
                    half heatMask = heatSample.a;
                #else
                    half heatMask = 1.0h;
                #endif

                // ── Lifetime curve ─────────────────────────────────────
                // Particle system writes lifetime ratio into vertex COLOR.a.
                half particleAlpha = input.color.a * _BaseColor.a;
                half lifeTimeCurve = pow(max(particleAlpha, 0.0001h), _HeatFalloff);
                half heatStrength  = lerp(_MinHeatAtDeath, 1.0h, lifeTimeCurve);

                // ── Color luminance factor ─────────────────────────────
                half luminance   = dot(input.color.rgb, half3(0.299h, 0.587h, 0.114h));
                half colorFactor = saturate(luminance * 1.5h + 0.2h);

                // ── Compose final heat value ───────────────────────────
                half finalHeat = heatValue
                               * heatMask
                               * _HeatIntensity
                               * heatStrength
                               * colorFactor
                               * shapeAlpha
                               * particleAlpha;

                // Discard near-zero to avoid invisible additive halo accumulation.
                clip(finalHeat - 0.001h);

                // R = heat written to thermal RT. G/B/A explicitly zero.
                // ColorMask R discards them anyway but explicit zeroes avoid
                // undefined behaviour on mobile tile-based GPUs.
                return half4(finalHeat, 0.0h, 0.0h, 0.0h);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Particles/Unlit"
}
