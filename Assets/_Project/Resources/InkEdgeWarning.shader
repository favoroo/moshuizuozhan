Shader "P1/UI/InkEdgeWarning"
{
    // 踩敌墨全屏边缘墨迹（P18-A 升级版）。
    // 参考 INKWAVE src/fx/screenfx.js 中的 edgeInk 原理：
    // 底边最厚的粘稠墨水带 + FBM 有机边界抖动 + 顶部悬垂长丝墨滴 + 立体法线 + 双高光与菲涅尔外缘。
    // 队伍色高纯度保护，保证踩墨警告具有鲜明的辨识度。

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _InkColor ("Ink Color", Color) = (0.15, 0.42, 1.0, 1)
        _Intensity ("Intensity", Range(0, 1)) = 0
        _Aspect ("Aspect", Float) = 1.7777778
        _BottomBand ("Bottom Band Height", Range(0, 0.2)) = 0.078
        _EdgeBand ("Edge Band Height", Range(0, 0.05)) = 0.0025
        _Wobble ("Wobble", Range(0, 2)) = 0.85
        _DripStrength ("Drip Strength", Range(0, 1)) = 0.55
        _DripScale ("Drip Length Scale", Range(0.2, 2.0)) = 1.0
        _SpecularIntensity ("Specular Intensity", Range(0.5, 5.0)) = 2.4
        _MaxAlpha ("Max Alpha", Range(0, 1)) = 0.94
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
            Name "InkEdgeWarning"

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

            fixed4 _InkColor;
            float _Intensity;
            float _Aspect;
            float _BottomBand;
            float _EdgeBand;
            float _Wobble;
            float _DripStrength;
            float _DripScale;
            float _SpecularIntensity;
            float _MaxAlpha;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash12(i);
                float b = hash12(i + float2(1.0, 0.0));
                float c = hash12(i + float2(0.0, 1.0));
                float d = hash12(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float a = 0.5;
                float s = 0.0;
#if defined(SHADER_API_MOBILE)
                [unroll] for (int i = 0; i < 2; i++)
#else
                [unroll] for (int i = 0; i < 3; i++)
#endif
                {
                    s += a * vnoise(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return s;
            }

            float softEdgeD(float2 p)
            {
                float dx = min(p.x, _Aspect - p.x);
                float dy = min(p.y, 1.0 - p.y);
                const float k = 16.0;
                return -log(max(1e-6, exp(-k * dx) + exp(-k * dy))) / k;
            }

            // 顶部下垂丝状墨滴：随强度拉长并形成球泡尖端
            float drips(float2 uv, float len, float seed)
            {
                const float N = 19.0;
                float x = uv.x * N;
                float id = floor(x);
                float h = hash12(float2(id, seed));
                float h2 = hash12(float2(id, seed + 7.3));
                float fx = (frac(x) - 0.5 - (h2 - 0.5) * 0.5) / N * _Aspect;
                float l = len * (0.12 + 0.88 * h * h) * (0.9 + 0.1 * sin(_Time.y * (0.7 + h) + h * 6.28));
                float y = 1.0 - uv.y;
                if (y < -0.02) return -1.0;
                float w = (0.005 + 0.011 * h2) * (0.55 + 0.45 * step(0.35, h));
                float bulb = w * 1.55;
                float inTail = saturate(y / max(l, 1e-4));
                float rad = lerp(w * 1.2, w, smoothstep(0.0, 0.3, inTail));
                rad = lerp(rad, bulb, smoothstep(0.8, 1.0, inTail));
                float d = length(float2(fx, max(y - l, 0.0))) - rad;
                return -d;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Intensity <= 0.0001) return fixed4(0, 0, 0, 0);

                float2 uv = i.uv;
                float2 p = float2(uv.x * _Aspect, uv.y);

                // 底部最厚，左右细线，边缘有机贴合
                float bottom = 1.0 - smoothstep(0.0, 0.60, uv.y);
                float band = _Intensity * (_EdgeBand + _BottomBand * (bottom * bottom + 0.15 * bottom));
                float maxBand = _Intensity * (_EdgeBand + _BottomBand) * 3.0;

                float2 d2 = float2(min(p.x, _Aspect - p.x), min(p.y, 1.0 - p.y));
                float dMin = min(d2.x, d2.y);
                bool dripZone = uv.y > 0.70;

                // 边缘 25% 以外区域快速跳过
                if (dMin > maxBand && !dripZone) return fixed4(0, 0, 0, 0);

                float edge = softEdgeD(p);

                float n = fbm(p * 3.2 + float2(11.0, _Time.y * 0.12)) - 0.44;
                float n2 = vnoise(p * 10.0 + float2(_Time.y * 0.25, 18.7)) - 0.5;
                float field = band * (1.0 + n * _Wobble * 2.2 + n2 * _Wobble * 0.7) - edge;

                float dripField = -1.0;
                if (dripZone && _DripStrength > 0.001)
                {
                    float dLen = 0.12 * _DripStrength * _Intensity * max(0.1, _DripScale);
                    dripField = drips(uv, dLen, 3.7);
                }

                float aa = fwidth(field) + 0.0015;
                float mask = smoothstep(-aa, aa, field);

                float dripMask = 0.0;
                if (dripField > -0.5)
                {
                    float daa = fwidth(dripField) + 0.0015;
                    dripMask = smoothstep(-daa, daa, dripField);
                }

                float cover = max(mask, dripMask);
                if (cover < 0.002) return fixed4(0, 0, 0, 0);

                // 立体法线计算
                float hgt = smoothstep(0.0, 0.032, max(field, dripField));
                float2 grad = float2(ddx(hgt), ddy(hgt));
                float3 nrm = normalize(float3(-grad * (_ScreenParams.y * 0.038), 1.0));

                // 双高光光照模型 + 菲涅尔边缘
                float3 lightDir = normalize(float3(-0.42, 0.62, 0.66));
                float ndl = saturate(dot(nrm, lightDir));
                float3 halfDir = normalize(lightDir + float3(0.0, 0.0, 1.0));
                float spec1 = pow(saturate(dot(nrm, halfDir)), 64.0) * _SpecularIntensity;
                float3 halfDir2 = normalize(float3(0.5, -0.3, 0.8) + float3(0.0, 0.0, 1.0));
                float spec2 = pow(saturate(dot(nrm, halfDir2)), 18.0) * 0.35;
                float fres = pow(1.0 - saturate(nrm.z), 2.0);

                // 队伍色高纯度保护：保持色彩饱满不发灰
                float3 ink = _InkColor.rgb;
                float3 base = ink * (0.52 + 0.58 * ndl);
                float3 col = base * (1.0 - fres * 0.42) + spec1 * float3(1.0, 1.0, 1.0) + spec2 * ink;

                float inner = smoothstep(0.0, 0.025, max(field, 0.0));
                col = lerp(col * 0.68, col, inner);

                float alpha = cover * _MaxAlpha * saturate(_Intensity * 3.5) * i.color.a;
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
