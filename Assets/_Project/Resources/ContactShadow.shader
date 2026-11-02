Shader "P1/ContactShadow"
{
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
            #pragma target 3.0

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
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float a = (1.0 - smoothstep(_CoreRadius, _EdgeRadius, r)) * _Color.a;
                if (a <= 0.002) discard;
                return half4(_Color.rgb, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
