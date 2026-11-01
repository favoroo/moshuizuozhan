Shader "P1/ContactShadow"
{
    // 角色接触阴影专用着色器：软边径向暗斑，固定 SrcAlpha/OneMinusSrcAlpha 混合。
    //
    // 为什么不用 URP Unlit 运行时切透明（旧实现）：
    // URP Unlit 的透明路径靠材质关键字 _SURFACE_TYPE_TRANSPARENT 选变体，
    // 而运行时才 new 出来的材质不参与打包期的变体收集，独立包里该变体被剥离后
    // OutputAlpha 会把 alpha 强制写 1 —— 1.1m 的 quad 直接变成不透明黑方块。
    // 这里把混合状态写死在 SubShader 上，并按 UV 程序化算软边：无关键字、无贴图、
    // 无材质属性依赖，桌面 / Android 表现一致；放在 Resources 下保证进包不被剔除。
    Properties
    {
        _Color("Shadow Color (A = Opacity)", Color) = (0, 0, 0, 0.5)
        _CoreRadius("Solid Core Radius", Range(0, 0.9)) = 0.12
        _EdgeRadius("Fade Out Radius", Range(0.1, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "ContactShadow"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _CoreRadius;
                float _EdgeRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 四边形内切圆：中心密实、边缘平滑归零；圆外（quad 四角）直接裁掉，
                // 无论什么视角/缩放都不会再露出方角。
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float a = (1.0 - smoothstep(_CoreRadius, _EdgeRadius, r)) * _Color.a;
                if (a <= 0.002) discard;
                return half4(_Color.rgb, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
