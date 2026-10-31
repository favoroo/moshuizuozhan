Shader "P1/UI/ScreenInkTransition"
{
    // 全屏墨水漫灌与复活虹膜撕裂转场着色器（P1/UI/ScreenInkTransition）。
    // 基于 INKWAVE src/fx/screenfx.js 中的 splatted ink flood 与 respawn iris reveal：
    // - 阵亡：敌墨从四方漫灌（粘稠前缘 + 顶部垂滴 + 表面光泽波动）；
    // - 复活：己墨中心撕裂开出有机虹膜破口，带强发光边缘，向外平滑展开；
    // - 受击形变（Punch）与边缘色差（Chroma）。

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _InkColor ("Ink Color", Color) = (0.15, 0.42, 1.0, 1.0)
        _HoleRimColor ("Hole Rim Color", Color) = (1.5, 1.5, 1.8, 1.0)
        _Flood ("Flood Coverage", Range(0, 1.2)) = 0
        _FloodDrip ("Flood Drip", Range(0, 1)) = 0
        _Hole ("Iris Hole Radius", Range(0, 2.0)) = 0
        _Aspect ("Aspect", Float) = 1.7777778
        _Punch ("Punch Distortion", Range(-0.5, 0.5)) = 0
        _PunchCenter ("Punch Center", Vector) = (0.5, 0.5, 0, 0)
        _Chroma ("Chroma Split", Range(0, 2.0)) = 0
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
            Name "ScreenInkTransitionPass"

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
            fixed4 _HoleRimColor;
            float _Flood;
            float _FloodDrip;
            float _Hole;
            float _Aspect;
            float _Punch;
            float4 _PunchCenter;
            float _Chroma;

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

            float edgeD(float2 uv)
            {
                float2 d = min(uv, 1.0 - uv) * float2(_Aspect, 1.0);
                return min(d.x, d.y);
            }

            float gooBand(float2 uv, float width, float wob, float seed)
            {
                float2 p = uv * float2(_Aspect, 1.0);
                float n = fbm(p * 3.2 + float2(seed, _Time.y * 0.12)) - 0.44;
                float n2 = vnoise(p * 10.0 + float2(_Time.y * 0.25, seed * 1.7)) - 0.5;
                return width * (1.0 + n * wob * 2.2 + n2 * wob * 0.7) - edgeD(uv);
            }

            float drips(float2 uv, float len, float seed, float top)
            {
                const float N = 19.0;
                float x = uv.x * N;
                float id = floor(x);
                float h = hash12(float2(id, seed));
                float h2 = hash12(float2(id, seed + 7.3));
                float fx = (frac(x) - 0.5 - (h2 - 0.5) * 0.5) / N * _Aspect;
                float l = len * (0.12 + 0.88 * h * h) * (0.9 + 0.1 * sin(_Time.y * (0.7 + h) + h * 6.28));
                float y = 1.0 - uv.y - top;
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
                float2 uv = i.uv;

                // Punch 瞬时几何形变抽动
                if (abs(_Punch) > 0.0005)
                {
                    uv = _PunchCenter.xy + (uv - _PunchCenter.xy) * (1.0 - _Punch);
                }

                float2 c0 = uv - 0.5;
                float2 q = c0 * float2(_Aspect, 1.0);
                float r = length(q);

                float width = _Flood * 1.05;
                float f = -1.0;

                if (_Flood > 0.001)
                {
                    float wob = 0.3 * (1.0 - 0.5 * smoothstep(0.7, 1.0, _Flood));
                    f = gooBand(uv, width, wob, 3.7);

                    if (_FloodDrip > 0.001)
                    {
                        float d = drips(uv, _FloodDrip, 1.3, width * 0.8);
                        f = max(f, d);
                    }

                    // 虹膜中心破口：从破口向内剔除
                    if (_Hole > 0.0)
                    {
                        float hn = (fbm(q * 4.5 + float2(_Time.y * 0.4, 0.0)) - 0.44) * 0.16;
                        f = min(f, r + hn - _Hole);
                    }
                }

                float aa = fwidth(f) + 0.0015;
                float m = smoothstep(-aa, aa, f);

                float3 col = float3(0, 0, 0);
                float alpha = 0.0;

                // 漫灌墨水渲染
                if (m > 0.001 && _Flood > 0.001)
                {
                    float und = fbm(q * 2.2 + float2(_Time.y * 0.05, -_Time.y * 0.03)) - 0.44;
                    float hgt = smoothstep(0.0, 0.05, f) + und * 1.6 * smoothstep(0.01, 0.16, f);
                    float2 grad = float2(ddx(hgt), ddy(hgt));
                    float3 n = normalize(float3(-grad * (_ScreenParams.y * 0.04), 1.0));

                    float3 lightDir = normalize(float3(-0.42, 0.62, 0.66));
                    float ndl = saturate(dot(n, lightDir));
                    float3 halfDir = normalize(lightDir + float3(0.0, 0.0, 1.0));
                    float spec = pow(saturate(dot(n, halfDir)), 64.0);
                    float spec2 = pow(saturate(dot(n, normalize(float3(0.5, -0.3, 0.8)))), 18.0);
                    float fres = pow(1.0 - saturate(n.z), 2.0);

                    float grain = vnoise(q * 60.0) * 0.05;
                    float3 ink = _InkColor.rgb * (0.94 + grain);
                    float3 base = ink * (0.42 + 0.62 * ndl);
                    float3 inkShaded = base * (1.0 - fres * 0.45) + spec * 2.4 + spec2 * ink * 0.25;

                    col = inkShaded;
                    alpha = m * 0.98;
                }

                // 破口外圈强发光光晕（Hole Rim）
                if (_Hole > 0.0 && _Flood > 0.001)
                {
                    float hn = (fbm(q * 4.5 + float2(_Time.y * 0.4, 0.0)) - 0.44) * 0.16;
                    float rd = abs(r + hn - _Hole);
                    float rim = exp(-rd * 26.0) * 0.9 * (1.0 - smoothstep(0.9, 1.25, _Hole));
                    col += _HoleRimColor.rgb * rim;
                    alpha = max(alpha, rim);
                }

                // 受击色相分离光晕（Chroma）
                if (_Chroma > 0.001)
                {
                    float edgeW = smoothstep(0.25, 1.05, r);
                    float ca = _Chroma * 0.02 * (0.35 + r) * edgeW;
                    float3 chromaCol = float3(1.0, 0.15, 0.08) * ca * 3.0;
                    col += chromaCol;
                    alpha = max(alpha, ca * 2.2);
                }

                if (alpha < 0.002) return fixed4(0, 0, 0, 0);

                return fixed4(col, saturate(alpha * i.color.a));
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
