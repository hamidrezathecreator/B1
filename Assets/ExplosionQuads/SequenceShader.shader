Shader "Custom/QuadBottomFade Mobile Thermal HQ"
{
    Properties
    {
        _MainTex ("Texture Sheet", 2D) = "white" {}
        _EmissionColor ("Emission Color", Color) = (1,1,1,1)
        _EmissionIntensity ("Emission Intensity", Range(0, 10)) = 1
        _TilesX ("Tiles X", Float) = 4
        _TilesY ("Tiles Y", Float) = 4
        _AnimationSpeed ("Animation Speed (FPS)", Float) = 24
        _StartTime ("Start Time", Float) = 0
        _Loop ("Loop", Float) = 0
        // 0 = legacy behavior: play every tile in the _TilesX * _TilesY grid.
        // >0 = play only the first N tiles (row-major) as a single one-shot sequence,
        // e.g. a 4x4 sheet with 3 unused/padding tiles can set this to 13.
        _FrameCount ("One-Shot Frame Count (0 = full grid)", Float) = 0
        _FadeStart ("Fade Start (from bottom)", Range(0, 1)) = 0.0
        _FadeEnd ("Fade End (from bottom)", Range(0, 1)) = 0.3
        
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1 
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1 
        
        [Header(Thermal Vision)]
        [Toggle] _ThermalEnabled("Enable Thermal Rendering", Float) = 1
        _HeatIntensity ("Heat Intensity", Range(0,10)) = 1.0
        
        [Header(Heat Behavior)]
        [Range(0.5, 3.0)] _HeatFalloff("Heat Falloff Curve", Float) = 1.0
        [Range(0, 1.0)] _MinHeatAtDeath("Min Heat Floor", Float) = 0.0
    }

    SubShader
    {
        Tags 
        { 
            "Queue"="Transparent" 
            "IgnoreProjector"="True" 
            "RenderType"="Transparent"
        }

        // ═══════════════════════════════════════════════════════════
        // PASS 0: Standard Visuals (HIGH QUALITY)
        // ═══════════════════════════════════════════════════════════
        Pass
        {
            Name "StandardForward"
            
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0  // Upgraded from 2.0
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _EmissionColor;      // half instead of fixed
            half _EmissionIntensity;   // half instead of float
            float _TilesX;
            float _TilesY;
            float _AnimationSpeed;
            float _StartTime;
            float _Loop;
            float _FrameCount;
            float _FadeStart;
            float _FadeEnd;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 localUV : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                
                float elapsedTime = _Time.y - _StartTime;
                float gridFrames = _TilesX * _TilesY;
                // 0 = legacy/full-grid behavior; >0 = clamp the sheet to a shorter one-shot sequence.
                float totalFrames = _FrameCount > 0.5 ? min(_FrameCount, gridFrames) : gridFrames;
                float currentFrame = floor(elapsedTime * _AnimationSpeed + 0.001);
                
                if (_Loop > 0.5)
                {
                    currentFrame = fmod(currentFrame, totalFrames);
                }
                else
                {
                    currentFrame = clamp(currentFrame, 0.0, totalFrames - 1.0);
                }
                
                float frameX = floor(fmod(currentFrame, _TilesX));
                float frameY = floor(currentFrame / _TilesX);
                
                float tileWidth = 1.0 / _TilesX;
                float tileHeight = 1.0 / _TilesY;
                
                float epsilon = 0.001;
                float2 tileSize = float2(tileWidth - epsilon * 2.0, tileHeight - epsilon * 2.0);
                
                float2 tileOffset = float2(
                    frameX * tileWidth + epsilon, 
                    (_TilesY - 1.0 - frameY) * tileHeight + epsilon
                );
                
                o.uv = v.uv * tileSize + tileOffset;
                o.localUV = v.uv;
                
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag (v2f i) : SV_Target  // half4 instead of fixed4
            {
                // Sample texture at remapped UV
                half4 col = tex2D(_MainTex, i.uv);  // half4 for better precision
                
                // Apply emission
                col.rgb *= _EmissionColor.rgb * _EmissionIntensity;
                
                float tileY = i.localUV.y;
                float fadeRange = max(_FadeEnd - _FadeStart, 0.001);
                float bottomFadeMask = saturate((tileY - _FadeStart) / fadeRange);
                float invertedTileY = 1.0 - tileY;
                float topFadeMask = saturate((invertedTileY - _FadeStart) / fadeRange);
                float combinedFadeMask = min(bottomFadeMask, topFadeMask);
                
                col.a *= combinedFadeMask * _EmissionColor.a;
                
                UNITY_APPLY_FOG(i.fogCoord, col);
                
                return col;
            }
            ENDCG
        }

        // ═══════════════════════════════════════════════════════════
        // PASS 1: Thermal Vision (HIGH QUALITY)
        // ═══════════════════════════════════════════════════════════
        Pass
        {
            Name "ThermalVision"
            Tags { "LightMode" = "ThermalVision" }
            
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One
            BlendOp Add
            ColorMask R

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0  // Upgraded from 2.0

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _EmissionColor;
            half _EmissionIntensity;
            float _TilesX;
            float _TilesY;
            float _AnimationSpeed;
            float _StartTime;
            float _Loop;
            float _FrameCount;
            float _FadeStart;
            float _FadeEnd;
            float _ThermalEnabled;
            half _HeatIntensity;
            half _HeatFalloff;
            half _MinHeatAtDeath;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 localUV : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                
                float elapsedTime = _Time.y - _StartTime;
                float gridFrames = _TilesX * _TilesY;
                // 0 = legacy/full-grid behavior; >0 = clamp the sheet to a shorter one-shot sequence.
                float totalFrames = _FrameCount > 0.5 ? min(_FrameCount, gridFrames) : gridFrames;
                float currentFrame = floor(elapsedTime * _AnimationSpeed + 0.001);
                
                if (_Loop > 0.5)
                {
                    currentFrame = fmod(currentFrame, totalFrames);
                }
                else
                {
                    currentFrame = clamp(currentFrame, 0.0, totalFrames - 1.0);
                }
                
                float frameX = floor(fmod(currentFrame, _TilesX));
                float frameY = floor(currentFrame / _TilesX);
                
                float tileWidth = 1.0 / _TilesX;
                float tileHeight = 1.0 / _TilesY;
                
                float epsilon = 0.001;
                float2 tileSize = float2(tileWidth - epsilon * 2.0, tileHeight - epsilon * 2.0);
                
                float2 tileOffset = float2(
                    frameX * tileWidth + epsilon, 
                    (_TilesY - 1.0 - frameY) * tileHeight + epsilon
                );
                
                o.uv = v.uv * tileSize + tileOffset;
                o.localUV = v.uv;
                
                return o;
            }

            half4 frag (v2f i) : SV_Target  // half4 instead of fixed4
            {
                if (_ThermalEnabled < 0.5)
                {
                    discard;
                }

                half4 texSample = tex2D(_MainTex, i.uv);  // half4
                half shapeAlpha = texSample.a;
                half textureBrightness = dot(texSample.rgb, half3(0.299, 0.587, 0.114));

                float tileY = i.localUV.y;
                float fadeRange = max(_FadeEnd - _FadeStart, 0.001);
                float bottomFadeMask = saturate((tileY - _FadeStart) / fadeRange);
                float invertedTileY = 1.0 - tileY;
                float topFadeMask = saturate((invertedTileY - _FadeStart) / fadeRange);
                float combinedFadeMask = min(bottomFadeMask, topFadeMask);
                
                half finalAlpha = shapeAlpha * combinedFadeMask * _EmissionColor.a;
                half heatStrength = pow(_EmissionIntensity / 10.0, _HeatFalloff);
                heatStrength = lerp(_MinHeatAtDeath, 1.0, heatStrength);
                half luminance = dot(_EmissionColor.rgb, half3(0.299, 0.587, 0.114));
                half colorFactor = saturate(luminance * 1.5 + 0.2);
                half finalHeat = textureBrightness * _HeatIntensity * heatStrength * colorFactor;
                finalHeat *= finalAlpha;

                if (finalHeat < 0.001)
                {
                    discard;
                }

                return half4(finalHeat, 0, 0, 0);
            }
            ENDCG
        }
    }

    FallBack "Mobile/Unlit (Supports Lightmap)"
}