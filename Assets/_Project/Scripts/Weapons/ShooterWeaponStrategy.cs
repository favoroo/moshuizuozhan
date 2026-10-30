using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// Spritzer（连发步枪）：按住持续射击，首发射击精度最高，连续开火准星逐渐扩散（Bloom），
    /// 松开扳机后在 <c>bloomRecoverSeconds</c> 内回落。弹道沿用 <see cref="InkBallistics"/> 低伸反解，
    /// 落点始终贴合地面准星。
    /// </summary>
    public sealed class ShooterWeaponStrategy : MainWeaponStrategy
    {
        private float _cooldown;
        private float _bloom;

        public ShooterWeaponStrategy(WeaponConfigSO config) : base(config)
        {
        }

        public override WeaponKind Kind => WeaponKind.Shooter;

        public override float SpreadDegrees
        {
            get
            {
                float min = Config != null ? Mathf.Max(0f, Config.spreadMinDegrees) : 1.2f;
                float max = Config != null ? Mathf.Max(min, Config.spreadDegrees) : 5.5f;
                return Mathf.Lerp(min, max, Mathf.Clamp01(_bloom));
            }
        }

        public override void Reset()
        {
            _cooldown = 0f;
            _bloom = 0f;
        }

        public override bool OnTriggerHeld(WeaponAim aim, float dt, IWeaponRuntimeHost host)
        {
            if (!host.IsAlive || _cooldown > 0f)
                return false;

            float inkCost = Config != null ? Config.inkCostPerShot : 1.2f;
            if (!host.ConsumeInk(inkCost))
            {
                host.ReportEmptyFeedback();
                _cooldown = FireInterval;
                return false;
            }

            _cooldown = FireInterval;

            float hitRadius = Config != null ? Config.projectileRadius : 0.18f;
            float speed = Config != null ? Config.projectileSpeed : 21f;
            float gravity = Config != null ? Config.projectileGravity : 28f;
            float maxRange = Config != null ? Config.maxRange : 7.2f;

            Vector3 muzzle = host.MuzzlePosition;
            Vector3 solveTarget = aim.GetShotTarget(muzzle, maxRange) + Vector3.up * hitRadius;

            // 先反解仰角（射程内落点与准星重合），再做绕弹道轴的锥形散布：
            // 抬头朝天时弹丸会真的向上飞，而不是被压平水平射出。
            Vector3 launchDir = InkBallistics.SolveLaunchDirection(muzzle, solveTarget, speed, gravity, maxRange);
            Vector3 shotDir = InkBallistics.ApplySpread(launchDir, SpreadDegrees);

            host.SpawnShot(new WeaponShot
            {
                Origin = muzzle,
                Direction = shotDir,
                Speed = speed,
                Gravity = gravity,
                MaxRange = maxRange,
                Damage = Config != null ? Config.damage : 34f,
                HitRadius = hitRadius,
                FarDamage = 0f,
                FalloffStart = 0f,
                FalloffDistance = 0f
            });

            float bloomPerShot = Config != null ? Mathf.Max(0f, Config.bloomPerShot) : 0.3f;
            _bloom = Mathf.Min(1f, _bloom + bloomPerShot);
            host.ReportShotFeedback();
            return true;
        }

        public override void Tick(float dt, bool triggerHeld, WeaponAim aim, IWeaponRuntimeHost host)
        {
            if (_cooldown > 0f)
                _cooldown -= dt;

            if (!triggerHeld)
            {
                float recover = Config != null ? Mathf.Max(0.05f, Config.bloomRecoverSeconds) : 0.3f;
                _bloom = Mathf.Max(0f, _bloom - dt / recover);
            }
        }
    }
}
