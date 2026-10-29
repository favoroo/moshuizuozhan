Shader "P1/CharacterToon"
{
    Properties
    {
        _BaseColor        ("Base Color", Color) = (1,1,1,1)
        _Color            ("Color (Alias)", Color) = (1,1,1,1)
        _Smoothness       ("Smoothness", Range(0,1)) = 0.5
        _Metallic         ("Metallic", Range(0,1)) = 0
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionStrength ("Emission Strength", Float) = 0
        _RimColor         ("Rim Color", Color) = (1,1,1,1)
        _RimStrength      ("Rim Strength", Range(0,2)) = 0.5
        _RimPower         ("Rim Power", Range(1,8)) = 3
        _ShadowTint       ("Shadow Tint", Color) = (0.55,0.55,0.62,1)
        _ShadeThreshold   ("Shade Threshold", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2

        // 运行时潜墨隐匿幽灵态（InkCharacterMotor）克隆材质后动态修改的透明通道属性，不可删除或更名
        [HideInInspector] _Surface  ("__surface", Float) = 0
        [HideInInspector] _Blend    ("__blend", Float) = 0
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite   ("__zw", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // ------------------------------------------------------------------
        // ForwardLit: 卡通主光照 Pass（支持半兰伯特二分阶梯、边缘光与透明变体）
        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // 混合与深度必须绑定属性，供运行时无缝切换为半透明幽灵态
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // 潜墨隐匿半透明变体：开启时片元输出 albedo.a
            #pragma multi_compile_local_fragment _ _SURFACE_TYPE_TRANSPARENT

            // URP 光照、阴影与雾效宏
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/_Project/Shaders/CharacterToonCommon.hlsl"
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // ShadowCaster: 阴影投射 Pass（与 PropLit 保持完全一致）
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/_Project/Shaders/CharacterToonCommon.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
