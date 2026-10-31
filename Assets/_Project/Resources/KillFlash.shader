Shader "P1/UI/KillFlash"
{
    // 击杀确认屏幕边缘闪光（P6-B 升级版）。
    // 精确对齐 INKWAVE src/fx/screenfx.js 中的 kill 通道（uKill uniform）：
    //   col += uKill.rgb * smoothstep(0.5, 1.15, r) * uKill.a
    // 加法混合（Blend SrcAlpha One）叠亮屏幕边缘，首发瞬起、平滑衰减。

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _KillColor ("Kill Color", Color) = (1.0, 0.83, 0.0, 1)
        _Intensity ("Intensity", Range(0, 1)) = 0
        _Aspect ("Aspect", Float) = 1.7777778
        _InnerRadius ("Inner Radius", Float) = 0.50
        _OuterRadius ("Outer Radius", Float) = 1.15
        _MaxAlpha ("Max Alpha", Range(0, 1)) = 0.95
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha One

        Pass
        {
            Name "KillFlash"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            fixed4 _KillColor;
            float _Intensity;
            float _Aspect;
            float _InnerRadius;
            float _OuterRadius;
            float _MaxAlpha;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Intensity <= 0.0001) return fixed4(0, 0, 0, 0);

                float2 q = (i.uv - 0.5) * float2(_Aspect, 1.0);
                float r = length(q);

                float glow = smoothstep(_InnerRadius, _OuterRadius, r);
                float alpha = glow * _Intensity * _MaxAlpha * i.color.a;
                if (alpha < 0.002) return fixed4(0, 0, 0, 0);

                // 边缘向高光白微偏移，保证加法叠亮冲击力
                float3 col = lerp(_KillColor.rgb, float3(1.0, 1.0, 1.0), glow * 0.35);
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
