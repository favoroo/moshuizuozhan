Shader "P1/UI/LensInk"
{
    // 镜头墨滴着色器（P1/UI/LensInk）。
    // 基于 INKWAVE src/fx/screenfx.js 中的 lensInk 原理：
    // 屏幕空间表面张力融合（Metaball SDF）、液体法线推导、双高光亮斑、菲涅尔外缘与折射。
    //
    // PC 端开启 _RefractionOn 时利用 _CameraOpaqueTexture 采样折射；
    // 移动端无该纹理时走轻量高光半透明墨膜分支，保证高帧率与兼容性。
    // 采用固定 96 长度 uniform 数组，完全避开 GLES 3.0 uniform 向量数量上限 (224 vec4)。

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Aspect ("Aspect Ratio", Float) = 1.7777778
        _RefractionOn ("Refraction Enabled", Float) = 1.0
        _LensColA ("Team A Ink Color", Color) = (1.0, 0.44, 0.06, 1.0)
        _LensColB ("Team B Ink Color", Color) = (0.15, 0.42, 1.0, 1.0)
        _WaterCol ("Water Color", Color) = (0.93, 0.98, 1.04, 1.0)
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
            Name "LensInkPass"

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

            #define MAX_LENS_DROPS 96

            uniform float4 _LensDrops[MAX_LENS_DROPS];
            uniform float4 _LensDropParams[MAX_LENS_DROPS];
            uniform int _LensDropCount;

            uniform float _Aspect;
            uniform float _RefractionOn;
            uniform fixed4 _LensColA;
            uniform fixed4 _LensColB;
            uniform fixed4 _WaterCol;

            sampler2D _CameraOpaqueTexture;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            // 液体表面光照：主方向光 + 辅助光 + 双高光 + 菲涅尔亮边
            float3 inkShade(float3 inkCol, float3 behind, float3 n, float thick)
            {
                float3 L = normalize(float3(-0.42, 0.62, 0.66));
                float ndl = saturate(dot(n, L));
                float3 H = normalize(L + float3(0.0, 0.0, 1.0));
                float spec = pow(saturate(dot(n, H)), 64.0);
                float spec2 = pow(saturate(dot(n, normalize(float3(0.5, -0.3, 0.8)))), 18.0);
                float fres = pow(1.0 - saturate(n.z), 2.0);

                float3 base = inkCol * (0.42 + 0.62 * ndl);
                base = lerp(base, behind * (0.35 + inkCol * 1.1), (1.0 - thick) * 0.28);
                return base * (1.0 - fres * 0.45) + spec * 2.4 + spec2 * inkCol * 0.25;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_LensDropCount <= 0) return fixed4(0, 0, 0, 0);

                float2 uv = i.uv;
                float fieldA = 0.0;
                float fieldB = 0.0;
                float fieldW = 0.0;

                int count = clamp(_LensDropCount, 0, MAX_LENS_DROPS);

                [loop]
                for (int k = 0; k < count; k++)
                {
                    float4 g = _LensDrops[k];
                    float4 p = _LensDropParams[k];

                    float2 diff = float2((uv.x - g.x) * _Aspect, uv.y - g.y);
                    float rx = max(g.z, 0.001);
                    float ry = max(g.w, 0.001);

                    float d2 = (diff.x * diff.x) / (rx * rx) + (diff.y * diff.y) / (ry * ry);
                    if (d2 < 1.0)
                    {
                        float influence = 1.0 - d2;
                        float infVal = influence * influence * p.x;
                        int ch = (int)(p.y + 0.1);

                        if (ch == 0)
                            fieldA += infVal;
                        else if (ch == 1)
                            fieldB += infVal;
                        else
                            fieldW += infVal;
                    }
                }

                float fs = fieldA + fieldB;
                float fw = fieldW;

                if (fs < 0.03 && fw < 0.03)
                    return fixed4(0, 0, 0, 0);

                float3 col = float3(0, 0, 0);
                float alpha = 0.0;

                // ------------------------------------------------------------- 墨水液滴渲染
                if (fs >= 0.03)
                {
                    float2 gradI = float2(ddx(fs), ddy(fs)) * (_ScreenParams.y * 0.045);
                    float3 nI = normalize(float3(-gradI * 1.35, 1.0));

                    float aa = fwidth(fs) * 1.1 + 0.012;
                    float cov = smoothstep(0.5 - aa, 0.5 + aa, fs);
                    float film = smoothstep(0.08, 0.5, fs) * (1.0 - cov);
                    float thick = saturate((fs - 0.5) * 1.3);

                    float3 inkCol = (fieldA * _LensColA.rgb + fieldB * _LensColB.rgb) / max(fs, 1e-4);

                    float3 behind = inkCol * 0.22;
                    if (_RefractionOn > 0.5)
                    {
                        float2 refrUv = uv + nI.xy * 0.03;
                        refrUv = clamp(refrUv, 0.001, 0.999);
                        behind = tex2D(_CameraOpaqueTexture, refrUv).rgb;
                    }

                    float3 body = inkShade(inkCol, behind, nI, thick);
                    float rim = smoothstep(0.5, 0.56, fs) * (1.0 - smoothstep(0.56, 0.75, fs));
                    body *= (1.0 - rim * 0.3);

                    float3 inkResult = lerp(behind * lerp(float3(1, 1, 1), inkCol * 1.5 + 0.1, 0.55), body, cov);
                    col = inkResult;
                    alpha = max(cov * 0.96, film * 0.42);
                }

                // ------------------------------------------------------------- 清亮水珠渲染
                if (fw >= 0.03)
                {
                    float2 gradW = float2(ddx(fw), ddy(fw)) * (_ScreenParams.y * 0.045);
                    float3 nW = normalize(float3(-gradW * 1.6, 1.0));

                    float aaw = fwidth(fw) * 1.1 + 0.012;
                    float covW = smoothstep(0.5 - aaw, 0.5 + aaw, fw);

                    float3 refrW = float3(0.1, 0.1, 0.1);
                    if (_RefractionOn > 0.5)
                    {
                        float2 refrW_Uv = uv - nW.xy * 0.07;
                        refrW_Uv = clamp(refrW_Uv, 0.001, 0.999);
                        refrW = tex2D(_CameraOpaqueTexture, refrW_Uv).rgb;
                    }

                    float3 L = normalize(float3(-0.42, 0.62, 0.66));
                    float specW = pow(saturate(dot(nW, normalize(L + float3(0.0, 0.0, 1.0)))), 80.0);
                    float fresW = pow(1.0 - saturate(nW.z), 1.6);
                    float3 wcol = refrW * (1.02 - fresW * 0.55) * _WaterCol.rgb + specW * 2.6;

                    col = lerp(col, wcol, covW);
                    alpha = max(alpha, covW * 0.9);
                }

                return fixed4(col, saturate(alpha * i.color.a));
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
