Shader "P1/UI/SubmergedInk"
{
    // 潜入墨水屏幕浸染（对齐 INKWAVE src/fx/screenfx.js 的 swim 段）：
    //   Pass1 浸染压暗（乘法）：mix(col, col*(0.55+ink*0.9), v*W*swim) —— 用 Blend DstColor Zero
    //          精确复刻"画面整体向己方墨色沉下去"的乘法混色，保留场景细节（不是糊一层色雾）；
    //   Pass2 焦散光斑（加法）：cst = vnoise(q*16+t)*vnoise(q*21-t)，col += ink * cst^3 * gain * v。
    // 两 Pass 都吃同一个全屏 Image（uGUI 会按材质逐 Pass 绘制同一网格）。
    //
    // 与 INKWAVE 的差异（俯视角刻意压浅，"一点点"就够）：
    //   浸染权重 W 0.45 → 0.32；焦散增益 1.6 → 1.1；不做场景 UV 微扰（0.0011 幅度在俯视角不可读，
    //   且需要 _CameraOpaqueTexture，移动端还得走降级分支，收益不抵复杂度）。
    // 遮罩 v = smoothstep(0.35, 1.05, r)，r = |(uv-0.5)*(aspect,1)|：中心干净，四角浸染。
    // 强度由 C# 侧统一写入 _Intensity（damp λ=7 平滑），Shader 不含任何状态。

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _InkColor ("Own Ink Color", Color) = (1.0, 0.44, 0.06, 1)
        _Intensity ("Intensity", Range(0, 1)) = 0
        _Aspect ("Aspect", Float) = 1.7777778
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

        CGINCLUDE
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

        v2f vert(appdata_t v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            o.color = v.color;
            return o;
        }

        // INKWAVE screenfx.js hash12/vnoise 逐行移植（值噪声，两张不同相位噪声取积得焦散网）
        float hash12(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float vnoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(hash12(i), hash12(i + float2(1.0, 0.0)), u.x),
                        lerp(hash12(i + float2(0.0, 1.0)), hash12(i + float2(1.0, 1.0)), u.x), u.y);
        }

        // 径向遮罩：中心 0，四角 → 1（r 域与 INKWAVE 相同）
        float swimMask(float2 uv)
        {
            float2 q = (uv - 0.5) * float2(_Aspect, 1.0);
            return smoothstep(0.35, 1.05, length(q));
        }
        ENDCG

        Pass
        {
            Name "SubmergedTint"
            Blend DstColor Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Intensity <= 0.0001) return fixed4(1, 1, 1, 1);

                float v = swimMask(i.uv);
                float m = saturate(v * 0.32 * _Intensity);
                float3 mul3 = lerp(float3(1.0, 1.0, 1.0), 0.55 + _InkColor.rgb * 0.9, m);
                return fixed4(mul3, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "SubmergedCaustics"
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Intensity <= 0.0001) return fixed4(0, 0, 0, 0);

                float t = _Time.y;
                float2 q = (i.uv - 0.5) * float2(_Aspect, 1.0);
                float v = swimMask(i.uv);
                float cst = vnoise(q * 16.0 + float2(t * 0.7, -t * 0.4))
                          * vnoise(q * 21.0 - float2(t * 0.5, t * 0.6));
                float3 glow = _InkColor.rgb * pow(cst, 3.0) * 1.1 * _Intensity * v;
                return fixed4(glow, 0);
            }
            ENDCG
        }
    }

    Fallback Off
}
