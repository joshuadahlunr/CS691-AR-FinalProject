Shader "Custom/MaskShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SmoothAmount ("Smooth Amount", Range(0, 1)) = 0.5
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Pass
        {
            Cull Off
            ZWrite On
            AlphaToMask On // Helps with anti-aliasing on MSAA-supported hardware

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _SmoothAmount;
            float _Cutoff;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Sample the main texture normally (no pixel snapping)
                fixed4 col = tex2D(_MainTex, i.uv);

                // Sample surrounding pixels for smoothing effect
                fixed4 left = tex2D(_MainTex, i.uv + float2(-_MainTex_TexelSize.x, 0));
                fixed4 right = tex2D(_MainTex, i.uv + float2(_MainTex_TexelSize.x, 0));
                fixed4 up = tex2D(_MainTex, i.uv + float2(0, _MainTex_TexelSize.y));
                fixed4 down = tex2D(_MainTex, i.uv + float2(0, -_MainTex_TexelSize.y));

                // Average colors for a smoother effect
                fixed4 blendedColor = (col + left + right + up + down) / 5.0;

                // Interpolate between the original and smoothed version
                fixed4 finalColor = lerp(col, blendedColor, _SmoothAmount);

                // Apply smooth alpha cutoff
                float alpha = smoothstep(_Cutoff - 0.05, _Cutoff + 0.05, finalColor.a);
                clip(alpha - 0.5); // Discard pixels below cutoff

                return finalColor;
            }
            ENDCG
        }
    }
}