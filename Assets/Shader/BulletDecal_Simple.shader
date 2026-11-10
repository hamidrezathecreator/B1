Shader "Custom/BulletDecal"
{
    Properties
    {
        _MainTex    ("Decal Texture",    2D)           = "white" {}
        _Color      ("Tint",             Color)         = (1,1,1,1)
        _Brightness ("Brightness",       Range(0, 4))   = 1.0
        _Alpha      ("Opacity",          Range(0, 1))   = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "AlphaTest+100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha

            ZWrite Off
            ZTest  LEqual
            Cull   Back

            Lighting Off
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0

            // GPU instancing
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(fixed,  _Brightness)
                UNITY_DEFINE_INSTANCED_PROP(fixed,  _Alpha)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                fixed4 tex        = tex2D(_MainTex, i.uv);
                fixed  alpha      = UNITY_ACCESS_INSTANCED_PROP(Props, _Alpha);
                fixed4 tint       = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                fixed  brightness = UNITY_ACCESS_INSTANCED_PROP(Props, _Brightness);

                fixed finalAlpha = tex.a * alpha * tint.a;
                clip(finalAlpha - 0.001);

                return fixed4(tex.rgb * tint.rgb * brightness, finalAlpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
