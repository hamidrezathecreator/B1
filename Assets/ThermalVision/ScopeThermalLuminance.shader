Shader "Hidden/ScopeThermalLuminance"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes v) {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target {
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                half l = dot(c, half3(0.299h, 0.587h, 0.114h));
                return half4(l, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}