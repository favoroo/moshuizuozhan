Shader "P1/CharacterToonOutlined"
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
        _ShadowTint       ("Shadow Tint", Color) = (0.40,0.45,0.62,1)
        _ShadeThreshold   ("Shade Threshold", Range(0,1)) = 0.5
        _ShadeSoftness    ("Shade Softness", Range(0.001, 0.2)) = 0.02
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2

        // 运行时潜墨隐匿幽灵态（InkCharacterMotor）克隆材质后动态修改的透明通道属性，不可删除或更名
        [HideInInspector] _Surface  ("__surface", Float) = 0
        [HideInInspector] _Blend    ("__blend", Float) = 0
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite   ("__zw", Float) = 1

        _ShadowFloor      ("Shadow Floor", Range(0.0, 1.0)) = 0.55
        _SpecThreshold    ("Spec Threshold", Range(0.5, 0.999)) = 0.93
        _SpecSoftness     ("Spec Softness", Range(0.001, 0.1)) = 0.02

        // 蛋形身体描边属性（仅 Outlined 材质独有，用于反向法线外扩 Pass）
        _OutlineWidth     ("Outline Width (Object Space)", Float) = 0.012
        _OutlineColor     ("Outline Color", Color) = (0.09,0.10,0.13,1)
    }

    // 全 Pass 注入描边宏，保证所有 Pass 的 UnityPerMaterial CBUFFER 布局严格一致（SRP Batcher 必需）
    HLSLINCLUDE
    #define CHARACTER_TOON_OUTLINE 1
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // ------------------------------------------------------------------
        // ForwardLit: 卡通主光照 Pass（与 CharacterToon 完全相同）
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

            // GPU Resident Drawer (P12-C)：DOTS 实例化变体；GRD 启用时走 BatchRendererGroup 提交，关闭时走原渲染路径
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON

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
        // ShadowCaster: 阴影投射 Pass（与 CharacterToon 完全相同）
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

            // GPU Resident Drawer (P12-C)：DOTS 实例化变体（shadowmap 同样走 GRD 批处理）
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/_Project/Shaders/CharacterToonCommon.hlsl"
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Outline: 反向法线外扩描边 Pass（蛋形身体专用，不受透明变体影响）
        // ------------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex OutlinePassVertex
            #pragma fragment OutlinePassFragment

            // GPU Resident Drawer (P12-C)：DOTS 实例化变体（每角色额外一次 draw，同样走 GRD 批处理）
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/_Project/Shaders/CharacterToonCommon.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
