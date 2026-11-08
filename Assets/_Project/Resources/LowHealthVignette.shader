Shader "P1/UI/LowHealthVignette"
{
    // 低血屏幕边缘暗角（P42-B）。
    // 参考 INKWAVE src/ui/hud.js 的 _updHp 危急暗角：血量低于阈值时屏幕四周压暗并偏向敌方色，
    // 与踩敌墨的有机墨边（InkEdgeWarning）区分——这里是"视野收窄"的压暗，不是墨水形状。
    //
    // 设计约束（Docs/UI-GeometricBold.md）：
    // - 纯色径向衰减，无渐变纹理、无模糊；
    // - 强度由 C# 侧统一写入 _Intensity，Shader 本身不播任何循环动画
    //   （有限次告警闪烁在 LowHealthVignetteMath 里按次数生成，不常驻）。

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _VignetteColor ("Vignette Color", Color) = (0.55, 0.02, 0.06, 1)
        _Intensity ("Intensity", Range(0, 1)) = 0
        _Aspect ("Aspect", Float) = 1.7777778
        _InnerRadius ("Inner Radius", Float) = 0.45
        _OuterRadius ("Outer Radius", Float) = 1.02
        _MaxAlpha ("Max Alpha", Range(0, 1)) = 0.88
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
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "LowHealthVignette"

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

            fixed4 _VignetteColor;
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

                float edge = smoothstep(_InnerRadius, max(_OuterRadius, _InnerRadius + 0.001), r);
                float alpha = edge * _Intensity * _MaxAlpha * i.color.a;
                if (alpha < 0.002) return fixed4(0, 0, 0, 0);

                return fixed4(_VignetteColor.rgb, alpha);
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
