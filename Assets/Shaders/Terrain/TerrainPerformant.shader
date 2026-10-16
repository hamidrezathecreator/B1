Shader "Universal Render Pipeline/Custom/UnlitToggleQuadMasks_Fog_Mobile"
{
    Properties
    {
        // --- Mask Texture (Optimized: R=M1, G=M2, B=M3, A=M4) ---
        
        [Toggle(BASE_LAYER_ON)] _BaseLayerToggle ("Base Layer", Float) = 1.0
        _BaseTexture ("Base Texture", 2D) = "gray" {}
        _BaseRotation ("Base Rotation (Degrees)", Range(0, 360)) = 0.0
        _BaseRandomRotation ("Base Random Rotation Factor", Range(0, 1)) = 0.0
        [Range(0f,2f)]
        _BaseStrength ("Base Strength", Range(0, 3)) = 1.0
        
        [Space]
        [Space]
        
        [Space]
        [Space]
        
        [Toggle(DETAIL_LAYER_1_ON)] _DetailLayer1Toggle ("1. Detail Layer 1 (R Mask)", Float) = 1.0
        _DetailTex1 ("   Overlay Texture 1 (RGB)", 2D) = "white" {}
        _Mask1Tiling ("Mask 1 (R) Tiling Override", Range(0.1, 10)) = 1.0
        _Detail1Rotation ("Detail 1 Rotation (Degrees)", Range(0, 360)) = 0.0
        _Detail1RandomRotation ("Detail 1 Random Rotation Factor", Range(0, 1)) = 0.0
        _Detail1Strength ("Detail 1 Strength", Range(0, 3)) = 1.0
        [Enum(Normal,0,Multiply,1,Screen,2,Overlay,3,Add,4)] _Detail1BlendMode ("Detail 1 Blend Mode", Float) = 0
        
        [Space]
        [Space]
        
        [Space]
        [Space]
        
        [Toggle(DETAIL_LAYER_2_ON)] _DetailLayer2Toggle ("2. Detail Layer 2 (G Mask)", Float) = 0.0
        _DetailTex2 ("   Overlay Texture 2 (RGB)", 2D) = "white" {}
        _Mask2Tiling ("Mask 2 (G) Tiling Override", Range(0.1, 10)) = 1.0
        _Detail2Rotation ("Detail 2 Rotation (Degrees)", Range(0, 360)) = 0.0
        _Detail2RandomRotation ("Detail 2 Random Rotation Factor", Range(0, 1)) = 0.0
        _Detail2Strength ("Detail 2 Strength", Range(0, 3)) = 1.0
        [Enum(Normal,0,Multiply,1,Screen,2,Overlay,3,Add,4)] _Detail2BlendMode ("Detail 2 Blend Mode", Float) = 0
        
        [Space]
        [Space]
        
        [Space]
        [Space]
        
        [Toggle(DETAIL_LAYER_3_ON)] _DetailLayer3Toggle ("3. Detail Layer 3 (B Mask)", Float) = 0.0
        _DetailTex3 ("   Overlay Texture 3 (RGB)", 2D) = "white" {}
        _Mask3Tiling ("Mask 3 (B) Tiling Override", Range(0.1, 10)) = 1.0
        _Detail3Rotation ("Detail 3 Rotation (Degrees)", Range(0, 360)) = 0.0
        _Detail3RandomRotation ("Detail 3 Random Rotation Factor", Range(0, 1)) = 0.0
        _Detail3Strength ("Detail 3 Strength", Range(0, 3)) = 1.0
        [Enum(Normal,0,Multiply,1,Screen,2,Overlay,3,Add,4)] _Detail3BlendMode ("Detail 3 Blend Mode", Float) = 0
        
        [Space]
        [Space]
        
        [Space]
        [Space]
        
        [Toggle(DETAIL_LAYER_4_ON)] _DetailLayer4Toggle ("4. Detail Layer 4 (A Mask)", Float) = 0.0
        _DetailTex4 ("   Overlay Texture 4 (RGB)", 2D) = "white" {}
        _Mask4Tiling ("Mask 4 (A) Tiling Override", Range(0.1, 10)) = 1.0
        _Detail4Rotation ("Detail 4 Rotation (Degrees)", Range(0, 360)) = 0.0
        _Detail4RandomRotation ("Detail 4 Random Rotation Factor", Range(0, 1)) = 0.0
        _Detail4Strength ("Detail 4 Strength", Range(0, 3)) = 1.0
        [Enum(Normal,0,Multiply,1,Screen,2,Overlay,3,Add,4)] _Detail4BlendMode ("Detail 4 Blend Mode", Float) = 0
        
        [Space]
        [Space]
        
        
        [Space]
        [Space]
        [NoScaleOffset] _MasksTexture ("Packed Masks (R/G/B/A)", 2D) = "gray" {}
        _MaskContrast ("Mask Contrast", Range(0.1, 5)) = 1.0
        _MaskClampMin ("Mask Clamp Min (Blacks)", Range(0, 1)) = 0.0
        _MaskClampMax ("Mask Clamp Max (Whites)", Range(0, 1)) = 1.0
        [Space]
        [Space]
        
      
        [Toggle(USE_GLOBAL_TINT)] _UseGlobalTint ("Enable Global Tint", Float) = 0.0
        _Color ("Global Tint", Color) = (1,1,1,1)

        // ── Thermal Vision ────────────────────────────────────────────────────
        [Header(Thermal Vision)]
        [Space]
        [Tooltip(Base heat emitted by this surface in thermal view.)]
        _ThermalBaseHeat     ("Thermal Base Heat",      Range(0, 1))    = 0.3
        [Tooltip(Perlayer heat deltas. Each channel maps to Detail Layer)]
        _ThermalLayerHeats   ("Thermal Layer Heats (RGBA -> Layer 1-4)", Vector) = (0.6, 0.4, 0.5, 0.35)
        [Tooltip(How strongly the mask drives the thermal layer heat contribution.)]
        _ThermalMaskInfluence("Thermal Mask Influence", Range(0, 1))    = 1.0
    }

    SubShader
    {
        Tags 
        { 
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "UniversalMaterialType" = "Unlit"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        // ─────────────────────────────────────────────────────────────────────
        // Shared CBuffer include — MUST be identical across all passes for SRP
        // Batcher compatibility. All properties are declared unconditionally;
        // #if guards are only used in the actual shader logic, never inside
        // CBUFFER_START / CBUFFER_END blocks.
        // ─────────────────────────────────────────────────────────────────────
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            // Masks
            float4 _MasksTexture_ST;
            float  _Mask1Tiling;
            float  _Mask2Tiling;
            float  _Mask3Tiling;
            float  _Mask4Tiling;
            float  _MaskContrast;
            float  _MaskClampMin;
            float  _MaskClampMax;

            // Global tint
            float4 _Color;

            // Base layer
            float4 _BaseTexture_ST;
            float  _BaseRotation;
            float  _BaseRandomRotation;
            float  _BaseStrength;

            // Detail layer 1
            float4 _DetailTex1_ST;
            float  _Detail1Rotation;
            float  _Detail1RandomRotation;
            float  _Detail1Strength;
            float  _Detail1BlendMode;

            // Detail layer 2
            float4 _DetailTex2_ST;
            float  _Detail2Rotation;
            float  _Detail2RandomRotation;
            float  _Detail2Strength;
            float  _Detail2BlendMode;

            // Detail layer 3
            float4 _DetailTex3_ST;
            float  _Detail3Rotation;
            float  _Detail3RandomRotation;
            float  _Detail3Strength;
            float  _Detail3BlendMode;

            // Detail layer 4
            float4 _DetailTex4_ST;
            float  _Detail4Rotation;
            float  _Detail4RandomRotation;
            float  _Detail4Strength;
            float  _Detail4BlendMode;

            // Thermal
            float  _ThermalBaseHeat;
            float4 _ThermalLayerHeats;
            float  _ThermalMaskInfluence;
        CBUFFER_END
        ENDHLSL

        // ─────────────────────────────────────────────────────────────────────
        // Depth Only
        // ─────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // NOTE: HLSLINCLUDE above already included Core.hlsl and the CBuffer.
            // We re-declare the minimal structs needed for this pass only.

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        // ─────────────────────────────────────────────────────────────────────
        // Main Unlit Pass
        // ─────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            
            #pragma shader_feature_local BASE_LAYER_ON
            #pragma shader_feature_local DETAIL_LAYER_1_ON
            #pragma shader_feature_local DETAIL_LAYER_2_ON
            #pragma shader_feature_local DETAIL_LAYER_3_ON
            #pragma shader_feature_local DETAIL_LAYER_4_ON
            #pragma shader_feature_local USE_GLOBAL_TINT

            // Core.hlsl and the CBuffer are already included via HLSLINCLUDE above.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MasksTexture);   SAMPLER(sampler_MasksTexture);

            #if defined(BASE_LAYER_ON)
            TEXTURE2D(_BaseTexture);    SAMPLER(sampler_BaseTexture);
            #endif
            
            #if defined(DETAIL_LAYER_1_ON)
            TEXTURE2D(_DetailTex1);     SAMPLER(sampler_DetailTex1);
            #endif
            #if defined(DETAIL_LAYER_2_ON)
            TEXTURE2D(_DetailTex2);     SAMPLER(sampler_DetailTex2);
            #endif
            #if defined(DETAIL_LAYER_3_ON)
            TEXTURE2D(_DetailTex3);     SAMPLER(sampler_DetailTex3);
            #endif
            #if defined(DETAIL_LAYER_4_ON)
            TEXTURE2D(_DetailTex4);     SAMPLER(sampler_DetailTex4);
            #endif

            // ── Blend helpers ─────────────────────────────────────────────────
            half3 BlendMultiply(half3 base, half3 blend) { return base * blend; }
            half3 BlendScreen  (half3 base, half3 blend) { return 1.0 - (1.0 - base) * (1.0 - blend); }
            half3 BlendOverlay (half3 base, half3 blend)
            {
                half3 r;
                r.r = base.r < 0.5 ? (2.0 * base.r * blend.r) : (1.0 - 2.0 * (1.0 - base.r) * (1.0 - blend.r));
                r.g = base.g < 0.5 ? (2.0 * base.g * blend.g) : (1.0 - 2.0 * (1.0 - base.g) * (1.0 - blend.g));
                r.b = base.b < 0.5 ? (2.0 * base.b * blend.b) : (1.0 - 2.0 * (1.0 - base.b) * (1.0 - blend.b));
                return r;
            }
            half3 BlendAdd(half3 base, half3 blend) { return saturate(base + blend); }

            half3 ApplyBlendMode(half3 base, half3 detail, float blendMode)
            {
                if      (blendMode < 0.5) return detail;
                else if (blendMode < 1.5) return BlendMultiply(base, detail);
                else if (blendMode < 2.5) return BlendScreen  (base, detail);
                else if (blendMode < 3.5) return BlendOverlay (base, detail);
                else                      return BlendAdd      (base, detail);
            }

            // ── UV helpers ────────────────────────────────────────────────────
            float hash(float2 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            float2 applyRotation(float2 uv, float baseAngle, float randomFactor)
            {
                float2 tileID  = floor(uv);
                float2 tileUV  = frac(uv);
                float  rndAng  = hash(tileID) * 6.28318530718;
                float  angle   = baseAngle + (rndAng - baseAngle) * randomFactor;
                float2 c       = tileUV - 0.5;
                float  cosA = cos(angle), sinA = sin(angle);
                return float2(c.x * cosA - c.y * sinA, c.x * sinA + c.y * cosA) + tileID + 0.5;
            }

            // ── Structs ───────────────────────────────────────────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uvMain     : TEXCOORD0;

                #if defined(BASE_LAYER_ON)
                float2 base       : TEXCOORD6;
                #endif
                
                #if defined(DETAIL_LAYER_1_ON)
                float2 uvDetail1  : TEXCOORD1;
                #endif
                #if defined(DETAIL_LAYER_2_ON)
                float2 uvDetail2  : TEXCOORD2;
                #endif
                #if defined(DETAIL_LAYER_3_ON)
                float2 uvDetail3  : TEXCOORD3;
                #endif
                #if defined(DETAIL_LAYER_4_ON)
                float2 uvDetail4  : TEXCOORD4;
                #endif

                float fogFactor   : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Vertex ────────────────────────────────────────────────────────
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexPos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexPos.positionCS;
                output.uvMain     = input.uv;

                #if defined(BASE_LAYER_ON)
                output.base = TRANSFORM_TEX(input.uv, _BaseTexture);
                #endif
                
                #if defined(DETAIL_LAYER_1_ON)
                output.uvDetail1 = TRANSFORM_TEX(input.uv, _DetailTex1);
                #endif
                #if defined(DETAIL_LAYER_2_ON)
                output.uvDetail2 = TRANSFORM_TEX(input.uv, _DetailTex2);
                #endif
                #if defined(DETAIL_LAYER_3_ON)
                output.uvDetail3 = TRANSFORM_TEX(input.uv, _DetailTex3);
                #endif
                #if defined(DETAIL_LAYER_4_ON)
                output.uvDetail4 = TRANSFORM_TEX(input.uv, _DetailTex4);
                #endif
                
                output.fogFactor = ComputeFogFactor(vertexPos.positionCS.z);
                return output;
            }

            // ── Fragment ──────────────────────────────────────────────────────
            half4 frag(Varyings input) : SV_Target
            {
                // Sample + process masks
                half mask1 = SAMPLE_TEXTURE2D(_MasksTexture, sampler_MasksTexture, input.uvMain * _Mask1Tiling).r;
                half mask2 = SAMPLE_TEXTURE2D(_MasksTexture, sampler_MasksTexture, input.uvMain * _Mask2Tiling).g;
                half mask3 = SAMPLE_TEXTURE2D(_MasksTexture, sampler_MasksTexture, input.uvMain * _Mask3Tiling).b;
                half mask4 = SAMPLE_TEXTURE2D(_MasksTexture, sampler_MasksTexture, input.uvMain * _Mask4Tiling).a;
                
                mask1 = saturate(((mask1 - 0.5) * _MaskContrast) + 0.5);
                mask2 = saturate(((mask2 - 0.5) * _MaskContrast) + 0.5);
                mask3 = saturate(((mask3 - 0.5) * _MaskContrast) + 0.5);
                mask4 = saturate(((mask4 - 0.5) * _MaskContrast) + 0.5);
                
                mask1 = saturate((mask1 - _MaskClampMin) / (_MaskClampMax - _MaskClampMin));
                mask2 = saturate((mask2 - _MaskClampMin) / (_MaskClampMax - _MaskClampMin));
                mask3 = saturate((mask3 - _MaskClampMin) / (_MaskClampMax - _MaskClampMin));
                mask4 = saturate((mask4 - _MaskClampMin) / (_MaskClampMax - _MaskClampMin));
                
                const float DEG_TO_RAD = 0.01745329251;
                
                half3 result;
                #if defined(BASE_LAYER_ON)
                    float2 finalBaseUV = applyRotation(input.base, _BaseRotation * DEG_TO_RAD, _BaseRandomRotation);
                    result = SAMPLE_TEXTURE2D(_BaseTexture, sampler_BaseTexture, finalBaseUV).rgb * _BaseStrength;
                #else
                    result = half3(0.5, 0.5, 0.5);
                #endif
                
                #if defined(DETAIL_LAYER_1_ON)
                    float2 finalDetail1UV = applyRotation(input.uvDetail1, _Detail1Rotation * DEG_TO_RAD, _Detail1RandomRotation);
                    half3 detail1  = SAMPLE_TEXTURE2D(_DetailTex1, sampler_DetailTex1, finalDetail1UV).rgb;
                    half3 blended1 = ApplyBlendMode(result, detail1, _Detail1BlendMode);
                    result = lerp(result, blended1, mask1 * _Detail1Strength);
                #endif

                #if defined(DETAIL_LAYER_2_ON)
                    float2 finalDetail2UV = applyRotation(input.uvDetail2, _Detail2Rotation * DEG_TO_RAD, _Detail2RandomRotation);
                    half3 detail2  = SAMPLE_TEXTURE2D(_DetailTex2, sampler_DetailTex2, finalDetail2UV).rgb;
                    half3 blended2 = ApplyBlendMode(result, detail2, _Detail2BlendMode);
                    result = lerp(result, blended2, mask2 * _Detail2Strength);
                #endif
                
                #if defined(DETAIL_LAYER_3_ON)
                    float2 finalDetail3UV = applyRotation(input.uvDetail3, _Detail3Rotation * DEG_TO_RAD, _Detail3RandomRotation);
                    half3 detail3  = SAMPLE_TEXTURE2D(_DetailTex3, sampler_DetailTex3, finalDetail3UV).rgb;
                    half3 blended3 = ApplyBlendMode(result, detail3, _Detail3BlendMode);
                    result = lerp(result, blended3, mask3 * _Detail3Strength);
                #endif
                
                #if defined(DETAIL_LAYER_4_ON)
                    float2 finalDetail4UV = applyRotation(input.uvDetail4, _Detail4Rotation * DEG_TO_RAD, _Detail4RandomRotation);
                    half3 detail4  = SAMPLE_TEXTURE2D(_DetailTex4, sampler_DetailTex4, finalDetail4UV).rgb;
                    half3 blended4 = ApplyBlendMode(result, detail4, _Detail4BlendMode);
                    result = lerp(result, blended4, mask4 * _Detail4Strength);
                #endif
                
                #if defined(USE_GLOBAL_TINT)
                result *= _Color.rgb;
                #endif
                
                result = MixFog(result, input.fogFactor);
                return half4(result, 1.0);
            }
            ENDHLSL
        }

        // ─────────────────────────────────────────────────────────────────────
        // Thermal Vision Pass
        // ─────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ThermalVision"
            Tags { "LightMode" = "ThermalVision" }

            ZWrite On
            ZTest  LEqual
            Cull   Back
            ColorMask R

            HLSLPROGRAM
            #pragma vertex   vert_thermal
            #pragma fragment frag_thermal
            #pragma target   2.0
            #pragma multi_compile_instancing

            #pragma shader_feature_local DETAIL_LAYER_1_ON
            #pragma shader_feature_local DETAIL_LAYER_2_ON
            #pragma shader_feature_local DETAIL_LAYER_3_ON
            #pragma shader_feature_local DETAIL_LAYER_4_ON

            // Core.hlsl and the CBuffer are already included via HLSLINCLUDE above.

            TEXTURE2D(_MasksTexture); SAMPLER(sampler_MasksTexture);

            // ── Structs ───────────────────────────────────────────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uvMain     : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Vertex ────────────────────────────────────────────────────────
            Varyings vert_thermal(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetVertexPositionInputs(input.positionOS.xyz).positionCS;
                output.uvMain     = input.uv;
                return output;
            }

            // ── Fragment ──────────────────────────────────────────────────────
            half4 frag_thermal(Varyings input) : SV_Target
            {
                // ── 1. Sample & process masks (same pipeline as main pass) ────
                half mask1 = SAMPLE_TEXTURE2D(_MasksTexture, sampler_MasksTexture, input.uvMain * _Mask1Tiling).r;
                half mask2 = SAMPLE_TEXTURE2D(_MasksTexture, sampler_MasksTexture, input.uvMain * _Mask2Tiling).g;
                half mask3 = SAMPLE_TEXTURE2D(_MasksTexture, sampler_MasksTexture, input.uvMain * _Mask3Tiling).b;
                half mask4 = SAMPLE_TEXTURE2D(_MasksTexture, sampler_MasksTexture, input.uvMain * _Mask4Tiling).a;

                mask1 = saturate(((mask1 - 0.5h) * _MaskContrast) + 0.5h);
                mask2 = saturate(((mask2 - 0.5h) * _MaskContrast) + 0.5h);
                mask3 = saturate(((mask3 - 0.5h) * _MaskContrast) + 0.5h);
                mask4 = saturate(((mask4 - 0.5h) * _MaskContrast) + 0.5h);

                half range = max(0.001h, (half)_MaskClampMax - (half)_MaskClampMin);
                mask1 = saturate((mask1 - (half)_MaskClampMin) / range);
                mask2 = saturate((mask2 - (half)_MaskClampMin) / range);
                mask3 = saturate((mask3 - (half)_MaskClampMin) / range);
                mask4 = saturate((mask4 - (half)_MaskClampMin) / range);

                // ── 2. Accumulate heat ────────────────────────────────────────
                half heat      = (half)_ThermalBaseHeat;
                half influence = (half)_ThermalMaskInfluence;

                #if defined(DETAIL_LAYER_1_ON)
                    heat = lerp(heat, (half)_ThermalLayerHeats.x, mask1 * influence);
                #endif
                #if defined(DETAIL_LAYER_2_ON)
                    heat = lerp(heat, (half)_ThermalLayerHeats.y, mask2 * influence);
                #endif
                #if defined(DETAIL_LAYER_3_ON)
                    heat = lerp(heat, (half)_ThermalLayerHeats.z, mask3 * influence);
                #endif
                #if defined(DETAIL_LAYER_4_ON)
                    heat = lerp(heat, (half)_ThermalLayerHeats.w, mask4 * influence);
                #endif

                return half4(saturate(heat), 0, 0, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
