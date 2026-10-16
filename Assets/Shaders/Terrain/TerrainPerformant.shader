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

    }

    SubShader
    {
        Tags 
        { 
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "UniversalMaterialType" = "SimpleLit"
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
            #pragma multi_compile_instancing

            // NOTE: HLSLINCLUDE above already included Core.hlsl and the CBuffer.
            // We re-declare the minimal structs needed for this pass only.

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        // ─────────────────────────────────────────────────────────────────────
        // Main Lit Pass
        // ─────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            
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
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
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

                half4 fogAndVertexLight : TEXCOORD5;
                float3 positionWS : TEXCOORD7;
                half3 normalWS    : TEXCOORD8;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 9);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Vertex ────────────────────────────────────────────────────────
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexPos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexPos.positionCS;
                output.positionWS = vertexPos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
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
                
                output.fogAndVertexLight.x = ComputeFogFactor(vertexPos.positionCS.z);
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                output.fogAndVertexLight.yzw = VertexLighting(output.positionWS, output.normalWS);
                #endif
                return output;
            }

            // ── Fragment ──────────────────────────────────────────────────────
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

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
                
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                inputData.shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                inputData.fogCoord = input.fogAndVertexLight.x;
                inputData.vertexLighting = input.fogAndVertexLight.yzw;
                inputData.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = result;
                surfaceData.alpha = 1.0h;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1.0h;

                half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
