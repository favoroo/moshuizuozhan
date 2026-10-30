using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// Popper Blaster（爆破枪）：
    /// 1. 低射速慢速重弹（23 m/s，0.78s 射击间隔）；
    /// 2. 直击 125 伤害秒杀 100 HP 角色；
    /// 3. 飞行达到 10.5m 最大射程在空中爆裂，或撞击掩体/敌人发生爆裂；
    /// 4. 产生 2.6m 范围溅射（30~70 伤害按距离线性衰减），直击目标不重复受溅射。
    /// </summary>
    public sealed class BlasterWeaponStrategy : MainWeaponStrategy
    {
        private float _cooldown;

        public BlasterWeaponStrategy(WeaponConfigSO config) : base(config)
        {
        }

        public override WeaponKind Kind => WeaponKind.Blaster;

        public override float SpreadDegrees => Config != null ? Config.spreadDegrees : 1.2f;

        public override void Reset()
        {
            _cooldown = 0f;
        }

        public override bool OnTriggerHeld(WeaponAim aim, float dt, IWeaponRuntimeHost host)
        {
            if (!host.IsAlive || _cooldown > 0f)
                return false;

            float inkCost = Config != null ? Config.inkCostPerShot : 9f;
            if (!host.ConsumeInk(inkCost))
            {
                host.ReportEmptyFeedback();
                _cooldown = 0.25f;
                return false;
            }

            _cooldown = FireInterval;

            float speed = Config != null ? Mathf.Max(1f, Config.projectileSpeed) : 23f;
            float maxRange = Config != null ? Mathf.Max(1f, Config.maxRange) : 10.5f;
            float damage = Config != null ? Config.damage : 125f;
            float splashRadius = Config != null ? Mathf.Max(0.1f, Config.blasterSplashRadius) : 2.6f;
            float splashMax = Config != null ? Config.blasterSplashDamageMax : 70f;
            float splashMin = Config != null ? Config.blasterSplashDamageMin : 30f;

            Vector3 muzzle = host.MuzzlePosition;
            Vector3 aimDir = aim.GetShotDirection(muzzle);

            // 沿「枪口 → 准星命中点」平直飞行（重力为 0）：打地、打墙、打高处都由准星决定，
            // 到最大射程或撞到东西时爆裂。散布是以弹道轴为中心的锥形。
            Vector3 shotDir = InkBallistics.ApplySpread(aimDir, SpreadDegrees);

            host.SpawnShot(new WeaponShot
            {
                Origin = muzzle,
                Direction = shotDir,
                Speed = speed,
                Gravity = 0f, // Blaster 在空中平直飞行并在尽头爆裂
                MaxRange = maxRange,
                Damage = damage,
                HitRadius = 0.26f,
                FarDamage = 0f,
                FalloffStart = 0f,
                FalloffDistance = 0f,
                IsBlaster = true,
                SplashRadius = splashRadius,
                SplashDamageMax = splashMax,
                SplashDamageMin = splashMin
            });

            host.ReportShotFeedback();
            return true;
        }

        public override void Tick(float dt, bool triggerHeld, WeaponAim aim, IWeaponRuntimeHost host)
        {
            if (_cooldown > 0f)
                _cooldown -= dt;
        }
    }
}
