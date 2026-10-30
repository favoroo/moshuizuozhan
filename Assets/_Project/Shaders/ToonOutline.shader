Shader "P1/ToonOutline"
{
    // 全屏卡通描边：基于相机深度 + 法线做十字邻域边缘检测。
    // 作为 URP FullScreenPassRendererFeature 的 passMaterial 使用（Blitter 兼容）。
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.09, 0.10, 0.13, 1)
        _OutlineThickness ("Thickness (px)", Range(0.5, 4.0)) = 1.6
        _DepthThreshold ("Depth Edge Threshold", Range(0.001, 0.2)) = 0.02
        _NormalThreshold ("Normal Edge Threshold", Range(0.02, 1.0)) = 0.35
        _FadeStart ("Fade Start (m)", Float) = 25.0
        _FadeEnd ("Fade End (m)", Float) = 65.0
        _SkyCutoff ("Sky Depth Cutoff", Range(0.5, 0.999)) = 0.97
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
            Name "ToonOutline"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            SAMPLER(sampler_BlitTexture);

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineThickness;
                float _DepthThreshold;
                float _NormalThreshold;
                float _FadeStart;
                float _FadeEnd;
                float _SkyCutoff;
            CBUFFER_END

            // 线性深度：天空/远裁剪面直接跳过，避免地平线上出现一条横线。
            float LinearDepthAt(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv).rgb;

                float centerDepth = LinearDepthAt(uv);
                float farPlane = _ProjectionParams.z;
                if (centerDepth >= farPlane * _SkyCutoff)
                {
                    return half4(sceneColor, 1.0);
                }

                float2 texel = _BlitTexture_TexelSize.xy * _OutlineThickness;
                float2 uvL = uv - float2(texel.x, 0.0);
                float2 uvR = uv + float2(texel.x, 0.0);
                float2 uvD = uv - float2(0.0, texel.y);
                float2 uvU = uv + float2(0.0, texel.y);

                float depthL = LinearDepthAt(uvL);
                float depthR = LinearDepthAt(uvR);
                float depthD = LinearDepthAt(uvD);
                float depthU = LinearDepthAt(uvU);

                float depthEdge = max(max(abs(depthL - centerDepth), abs(depthR - centerDepth)),
                                      max(abs(depthD - centerDepth), abs(depthU - centerDepth)));
                float relativeDepthEdge = depthEdge / max(centerDepth, 0.5);
                float edge = step(_DepthThreshold, relativeDepthEdge);

                float3 normalC = SampleSceneNormals(uv);
                float3 normalL = SampleSceneNormals(uvL);
                float3 normalR = SampleSceneNormals(uvR);
                float3 normalD = SampleSceneNormals(uvD);
                float3 normalU = SampleSceneNormals(uvU);

                float normalDiff = 1.0 - min(min(dot(normalC, normalL), dot(normalC, normalR)),
                                             min(dot(normalC, normalD), dot(normalC, normalU)));
                edge = max(edge, step(_NormalThreshold, normalDiff));

                // 远处淡化，避免整张地图都是黑边；近景线宽由厚度参数控制。
                float distanceFade = 1.0 - smoothstep(_FadeStart, _FadeEnd, centerDepth);
                edge *= distanceFade;

                half3 finalColor = lerp(sceneColor, _OutlineColor.rgb, edge);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}