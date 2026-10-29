using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 一发墨弹的完整运行参数。策略层只负责产出该结构，由 <see cref="InkProjectilePool"/> 落地成
    /// 真实弹丸；这样武器手感与弹丸物理、对象池彻底解耦，也便于 EditMode 纯逻辑测试。
    /// </summary>
    public struct WeaponShot
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public float Speed;
        public float Gravity;
        public float MaxRange;
        public float Damage;
        public float HitRadius;

        /// <summary>远距离伤害（滚筒挥击等）。&lt;= 0 表示不做距离衰减。</summary>
        public float FarDamage;

        /// <summary>开始向 <see cref="FarDamage"/> 衰减的飞行距离。</summary>
        public float FalloffStart;

        /// <summary>衰减到 <see cref="FarDamage"/> 的飞行距离。</summary>
        public float FalloffDistance;

        /// <summary>泼桶同一次泼洒共享的 Volley 聚合上下文（同一个目标单次只判 1 次直击）。</summary>
        public SlosherVolley Volley;

        /// <summary>是否为爆破枪重弹（到达最大射程或撞击时触发空爆范围溅射）。</summary>
        public bool IsBlaster;

        /// <summary>空爆溅射半径（米）。</summary>
        public float SplashRadius;

        /// <summary>空爆中心最高溅射伤害。</summary>
        public float SplashDamageMax;

        /// <summary>空爆边缘最低溅射伤害。</summary>
        public float SplashDamageMin;

        /// <summary>按 <see cref="WeaponConfigSO"/> 的默认弹道拼一发标准墨弹（保持旧调用方语义）。</summary>
        public static WeaponShot FromConfig(WeaponConfigSO config, Vector3 origin, Vector3 direction)
        {
            return new WeaponShot
            {
                Origin = origin,
                Direction = direction,
                Speed = config != null ? config.projectileSpeed : 25f,
                Gravity = config != null ? config.projectileGravity : 28f,
                MaxRange = config != null ? config.maxRange : 12f,
                Damage = config != null ? config.damage : 34f,
                HitRadius = config != null ? config.projectileRadius : 0.18f,
                FarDamage = 0f,
                FalloffStart = 0f,
                FalloffDistance = 0f,
                Volley = null,
                IsBlaster = false,
                SplashRadius = 0f,
                SplashDamageMax = 0f,
                SplashDamageMin = 0f
            };
        }

        /// <summary>
        /// 按飞行距离结算命中伤害：FarDamage &lt;= 0 或 Damage &lt;= FarDamage 时恒为 Damage；
        /// 否则在 [FalloffStart, FalloffDistance] 区间内线性衰减到 FarDamage（滚筒挥击 125→30）。
        /// 与 <see cref="InkProjectilePool"/> 的运行时判定共用同一份公式，避免逻辑漂移。
        /// </summary>
        public float DamageAtDistance(float distanceTraveled)
        {
            return ComputeDamage(Damage, FarDamage, FalloffStart, FalloffDistance, distanceTraveled);
        }

        /// <summary>按飞行距离结算命中伤害的纯函数版本，供弹丸池直接复用。</summary>
        public static float ComputeDamage(
            float damage,
            float farDamage,
            float falloffStartDistance,
            float falloffEndDistance,
            float distanceTraveled)
        {
            if (farDamage <= 0f || damage <= farDamage)
                return damage;

            float falloffStart = Mathf.Max(0f, falloffStartDistance);
            float falloffEnd = Mathf.Max(falloffStart + 0.01f, falloffEndDistance);
            float t = Mathf.Clamp01((distanceTraveled - falloffStart) / (falloffEnd - falloffStart));
            return Mathf.Lerp(damage, farDamage, t);
        }
    }

    /// <summary>蓄力狙的瞬时贯穿光束参数。</summary>
    public struct WeaponBeam
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public float Range;
        public float Damage;

        /// <summary>沿地面墨线的单点涂抹半径。</summary>
        public float PaintRadius;

        /// <summary>沿光束每隔多少米补一个地面墨点。</summary>
        public float PaintStep;

        /// <summary>终点撞击墨斑半径。</summary>
        public float ImpactRadius;

        /// <summary>是否贯穿命中路径上的所有敌人。</summary>
        public bool Pierce;

        /// <summary>蓄力强度 0~1，仅用于表现层宽度/亮度。</summary>
        public float Intensity01;
    }

    /// <summary>滚筒每帧推地产生的一次清扫：从上一位置扫到当前位置。</summary>
    public struct RollSweep
    {
        public Vector3 From;
        public Vector3 To;
        public Vector3 Forward;
        public float Width;
        public float ForwardReach;
        public float Damage;
        public float HitInterval;

        /// <summary>横向每一枚墨印的半径（越小边缘越平整，但涂抹调用越多）。</summary>
        public float PaintRadius;
    }
}
