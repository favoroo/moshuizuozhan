Shader "P1/ScreenFx"
{
    // P31 镜头反馈全屏 Pass（当前实现：受击色差 kick）。
    // 强度由材质参数驱动；全 0 时像素直通。作为 URP FullScreenPassRendererFeature 的 passMaterial。
    // 与 UI 层 KillFlash（击杀边缘闪光）互补：本 Pass 负责"镜头级"受击反馈。
    Properties
    {
        _ChromaAmount ("Chromatic Aberration Amount", Range(0, 0.1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ScreenFx"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            SAMPLER(sampler_BlitTexture);

            CBUFFER_START(UnityPerMaterial)
                float _ChromaAmount;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // 色差 kick：R/B 沿"远离屏幕中心"方向反向分离采样，中心处无偏移，
                // 画面边缘的色边最明显——受击瞬间的"镜头被震了一下"感。
                float2 dir = uv - 0.5;
                half r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv + dir * _ChromaAmount).r;
                half g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv).g;
                half b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv - dir * _ChromaAmount).b;

                return half4(r, g, b, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
