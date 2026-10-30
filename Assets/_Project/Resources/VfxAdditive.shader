Shader "P1/VfxAdditive"
{
    // 程序化 VFX 专用的极简加法混合着色器：枪口闪光 / 撞击高光 / 冲击波环。
    // 放在 Resources 下，保证运行时 Shader.Find 在独立包里也能拿到（不会被打包剔除）。
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
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

        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        ZTest LEqual

        Pass
        {
            Name "VfxAdditive"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 tint = _Color * input.color;
                // 加法混合 (SrcAlpha, One)：输出 RGB 亮度由 alpha 与贴图衰减共同控制，
                // 因此软边圆盘在深色地面上会自然「发光」而不是糊成一块不透明色片。
                half alpha = saturate(tex.a * tint.a);
                return half4(tex.rgb * tint.rgb * _Intensity, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
