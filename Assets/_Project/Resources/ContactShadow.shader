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
            #pragma target 4.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 阴影颜色/不透明度逐角色不同（接触高度决定 alpha），必须进 instancing buffer，
            // 否则 MaterialPropertyBlock 会让每个角色阴影各占一个 draw。
            UNITY_INSTANCING_BUFFER_START(PerInstance)
                UNITY_DEFINE_INSTANCED_PROP(half4, _Color)
            UNITY_INSTANCING_BUFFER_END(PerInstance)

            CBUFFER_START(UnityPerMaterial)
                float _CoreRadius;
                float _EdgeRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
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
                half4 shadowColor = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color);
                float a = (1.0 - smoothstep(_CoreRadius, _EdgeRadius, r)) * shadowColor.a;
                if (a <= 0.002) discard;
                return half4(shadowColor.rgb, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
