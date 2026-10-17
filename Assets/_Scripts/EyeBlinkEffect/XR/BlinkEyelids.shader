Shader "UI/XR Blink Eyelids"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Eyelid Color", Color) = (0,0,0,1)
        _Progress ("Blink Progress", Range(0,1)) = 0
        _Curvature ("Curvature", Range(0,5)) = 1
        _Smoothness ("Edge Softness", Range(0.001,0.25)) = 0.04
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPosition : TEXCOORD1; };

            fixed4 _Color;
            float4 _ClipRect;
            float _Progress;
            float _Curvature;
            float _Smoothness;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // _Progress is the amount of closure: 0 is fully open, 1 is
                // fully closed. Build the visible opening directly from that
                // value so it cannot invert during the transition.
                float closure = saturate(_Progress);
                if (closure <= 0.0001) return fixed4(0, 0, 0, 0);
                float softness = max(_Smoothness, 0.001);
                // Include the feather width in the travel so the soft edges
                // start outside the view and meet fully opaque at closure.
                // Shape the lids as a symmetric arch: the eye opening is
                // tallest in the middle and tighter toward the corners. The
                // sine envelope keeps the curve smooth at both endpoints.
                // Let the lids overlap past the center so their feathered
                // edges become fully opaque without a snap at full closure.
                float normalizedX = abs(i.uv.x * 2.0 - 1.0);
                float curveEnvelope = sin(closure * UNITY_PI);
                float curve = normalizedX * normalizedX * _Curvature * 0.12 * curveEnvelope;
                float baseBoundary = lerp(-softness, 0.5 + softness, closure);
                float bottomBoundary = baseBoundary + curve;
                float topBoundary = 1.0 - bottomBoundary;
                float bottom = 1.0 - smoothstep(bottomBoundary - softness, bottomBoundary + softness, i.uv.y);
                float top = smoothstep(topBoundary - softness, topBoundary + softness, i.uv.y);
                float alpha = saturate(max(bottom, top));
                alpha *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                return fixed4(i.color.rgb, i.color.a * alpha);
            }
            ENDCG
        }
    }
}





