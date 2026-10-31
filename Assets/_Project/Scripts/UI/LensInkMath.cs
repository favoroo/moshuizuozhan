using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 镜头墨滴（LensInk）核心模拟与表面张力距离场的纯数学工具类。
    /// 无 GameObject 依赖，EditMode 可独立测试。
    /// 包含：Metaball 势能评估、重力下滑速度、粘滞减速、拖尾产生与边缘坐标计算。
    /// </summary>
    public static class LensInkMath
    {
        public const float DefaultAspect = 16f / 9f;
        public const float GravityConstant = 1.05f;
        public const float ReferenceRadius = 0.05f;
        public const float SlideRadiusThreshold = 0.012f;

        /// <summary>
        /// 评估单个椭球形液滴在指定屏幕 UV 点处的 Metaball 势能贡献 (0~1)。
        /// 采用竞品同款势函数：k = 1 - d^2, field = k * k * intensity。
        /// </summary>
        public static float EvaluateInfluence(Vector2 uv, Vector2 pos, float rx, float ry, float aspect)
        {
            if (rx <= 0.0001f || ry <= 0.0001f) return 0f;

            float dx = (uv.x - pos.x) * (aspect > 0.01f ? aspect : DefaultAspect);
            float dy = uv.y - pos.y;
            float d2 = (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry);
            if (d2 >= 1f) return 0f;

            float k = 1f - d2;
            return k * k;
        }

        /// <summary>
        /// 计算液滴下滑重力加速度。
        /// 大液滴质量大、下滑快；卡滞期 (hold > 0) 加速度为 0。
        /// </summary>
        public static float CalculateSlideGravity(float radius, float mass, float hold)
        {
            if (hold > 0f) return 0f;
            float normalizedMass = Mathf.Max(0.1f, mass);
            return GravityConstant * (radius / ReferenceRadius) * (0.55f + 0.45f * normalizedMass);
        }

        /// <summary>
        /// 粘性流体阻尼系数：卡滞时阻尼极大 (12.0)，正常流动时温和 (2.1)。
        /// </summary>
        public static float CalculateDamping(float hold)
        {
            return hold > 0f ? 12f : 2.1f;
        }

        /// <summary>
        /// 判断液滴是否应该启动下滑：
        /// 小于阈值 (0.012) 的毛细微滴表面张力占优，卡在镜头上不动；大滴超过停留期后下滑。
        /// </summary>
        public static bool ShouldSlide(bool currentlySliding, float age, float stickTime, float radius)
        {
            if (currentlySliding) return true;
            return age > stickTime && radius > SlideRadiusThreshold;
        }

        /// <summary>
        /// 计算液滴沿重力下滑时的横向蛇形摇摆位移。
        /// </summary>
        public static float CalculateSlideWobble(float age, float seed)
        {
            return Mathf.Sin(age * 2.3f + seed) * 0.006f + Mathf.Sin(age * 6.1f + seed * 2f) * 0.003f;
        }

        /// <summary>
        /// 推进单个液滴的寿命衰减。末段强度收缩模拟液体挥发或渗透。
        /// </summary>
        public static float StepIntensity(float age, float life, float initialIntensity, bool isTrail)
        {
            if (age <= life) return initialIntensity;
            float fadeDuration = isTrail ? 0.9f : 0.55f;
            float t = (age - life) / Mathf.Max(0.01f, fadeDuration);
            return Mathf.Max(0f, initialIntensity * Mathf.Clamp01(1f - t));
        }

        /// <summary>
        /// 依据受击/入射方向角计算在屏幕边缘的着墨点坐标（归一化 UV，y 向上）。
        /// 避开准星中心与底部主角模型区域。
        /// </summary>
        public static Vector2 EdgePoint(float angleRad, float inset, float aspect)
        {
            float a = aspect > 0.01f ? aspect : DefaultAspect;
            float cos = Mathf.Cos(angleRad);
            float sin = Mathf.Sin(angleRad);

            float tx = (a * 0.5f) / Mathf.Max(0.001f, Mathf.Abs(cos));
            float ty = 0.5f / Mathf.Max(0.001f, Mathf.Abs(sin));
            float t = Mathf.Min(tx, ty);

            float x = a * 0.5f + cos * t;
            float y = 0.5f + sin * t;

            float signX = Mathf.Sign(cos);
            float signY = Mathf.Sign(sin);
            x -= signX * inset * (Mathf.Abs(cos) > 0.25f ? 1f : 0.3f);
            y -= signY * inset * (Mathf.Abs(sin) > 0.25f ? 1f : 0.3f);

            float u = x / a;
            float v = y;

            // 避开底部中央的主角位置
            if (v < 0.5f && Mathf.Abs(u - 0.5f) < 0.2f)
            {
                float side = u >= 0.5f ? 1f : -1f;
                u = 0.5f + side * 0.26f;
            }

            return new Vector2(Mathf.Clamp(u, 0.02f, 0.98f), Mathf.Clamp(v, 0.03f, 0.97f));
        }
    }
}
