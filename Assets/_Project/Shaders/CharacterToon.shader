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

        // P31 可选 albedo 细节层：默认 white，不贴图时行为与旧版完全一致（向后兼容）
        [Header(Albedo Detail P31)]
        _BaseMap          ("Albedo Map (optional)", 2D) = "white" {}
        _DetailNormalMap  ("Detail Normal (optional)", 2D) = "bump" {}
        _DetailNormalStrength ("Detail Normal Strength", Range(0, 1)) = 0.0

        // P32 敌方墨覆盖 + 受击闪白：运行时由 CombatantHealth 经 MaterialPropertyBlock 逐单位推送
        // （8 名角色共用同一份材质资产，覆盖量必须逐实例，故不能写进材质）。
        // 默认全 0 → 与旧观感逐像素一致。
        [Header(Ink Coat P32)]
        _InkColor         ("Ink Color (enemy ink)", Color) = (0,0,0,1)
        _InkAmount        ("Ink Coverage Amount", Range(0,1)) = 0
        _InkSeed          ("Ink Splotch Seed", Float) = 0
        // 覆盖曲线按本噪声实测分布标定（见 CharacterToonCommon.hlsl）：满覆盖约 52% 表面积，
        // 保留本队色可辨认；三个值都是美术可调档位，不要随手改回 0.47/1.0。
        _InkThresholdMax  ("Ink Coverage Threshold Max (clean)", Range(0.2,1)) = 0.77
        _InkThresholdMin  ("Ink Coverage Threshold Min (full ink)", Range(0.2,1)) = 0.505
        _InkCurvePower    ("Ink Coverage Curve Power", Range(0.1,2)) = 0.55
        _FlashAmount      ("Hit Flash Amount", Range(0,1)) = 0
        // 逐 renderer 的静止位姿坐标系（运行时 MPB 推送，不需要在材质上编辑）
        [HideInInspector] _InkPartOffset ("__inkPartOffset", Vector) = (0,0,0,0)
        [HideInInspector] _InkPartScale  ("__inkPartScale", Vector) = (1,1,1,0)
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

            // GPU Resident Drawer (P12-C)：DOTS 实例化变体；GRD 启用时走 BatchRendererGroup 提交，关闭时走原渲染路径
            #pragma target 3.0
                        #if !UNITY_WEBGL
            #pragma multi_compile_instancing
            #endif

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

            // GPU Resident Drawer (P12-C)：DOTS 实例化变体（shadowmap 同样走 GRD 批处理）
            #pragma target 3.0
                        #if !UNITY_WEBGL
            #pragma multi_compile_instancing
            #endif

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/_Project/Shaders/CharacterToonCommon.hlsl"
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // DepthNormals: SSAO 的邻域法线来源。
        // 角色原先不写 DepthNormals，SSAO（Source=DepthNormals）在角色像素上读到的是
        // 身后地面/掩体的法线；AfterOpaque 全屏乘算后会在角色身上留下错误的暗斑，
        // 角色脚下也不会产生接触遮蔽。补上与 PropLit 同构的 Pass。
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma target 3.0
            #if !UNITY_WEBGL
            #pragma multi_compile_instancing
            #endif

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/_Project/Shaders/CharacterToonCommon.hlsl"

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            DepthNormalsVaryings DepthNormalsVertex(Attributes input)
            {
                DepthNormalsVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                return half4(packedNormalWS, 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
