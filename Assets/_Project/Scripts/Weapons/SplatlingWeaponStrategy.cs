using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// Gyre Splatling（加特林）：
    /// 1. 按住扳机蓄力旋转枪管（蓄力时间 0.85s，蓄力期间移速减缓）；
    /// 2. 松开扳机释放 0.3~1.7s 高密度暴风雨弹幕（15 发/秒，单发 28 伤害，高初速 40m/s，射程 15m）；
    /// 3. 扫射期间无论扳机状态均自动倾泻弹幕，直至持续时间结束或墨水耗尽。
    /// </summary>
    public sealed class SplatlingWeaponStrategy : MainWeaponStrategy
    {
        private float _cooldown;
        private bool _isCharging;
        private float _chargeElapsed;
        private float _charge;

        private bool _isStreaming;
        private float _burstDuration;
        private float _burstTimer;
        private float _shotTimer;
        private float _bloom;

        public SplatlingWeaponStrategy(WeaponConfigSO config) : base(config)
        {
        }

        public override WeaponKind Kind => WeaponKind.Splatling;

        public override bool IsCharging => _isCharging;

        public bool IsStreaming => _isStreaming;

        public override float Charge01
        {
            get
            {
                if (_isCharging)
                    return _charge;
                if (_isStreaming && _burstDuration > 0.001f)
                    return Mathf.Clamp01(_burstTimer / _burstDuration);
                return 0f;
            }
        }

        public override float MoveSpeedMultiplier
        {
            get
            {
                if (_isCharging)
                {
                    // 蓄力时移速明显降低
                    return Config != null ? Config.splatlingMoveSpeedCharging / 6.0f : 0.4f;
                }
                if (_isStreaming)
                {
                    // 扫射时移速轻度降低
                    return Config != null ? Config.splatlingMoveSpeedFiring / 6.0f : 0.57f;
                }
                return 1f;
            }
        }

        public override float SpreadDegrees
        {
            get
            {
                float min = Config != null ? Mathf.Max(0f, Config.spreadMinDegrees) : 0.6f;
                float max = Config != null ? Mathf.Max(min, Config.spreadDegrees) : 3.2f;
                return Mathf.Lerp(min, max, Mathf.Clamp01(_bloom));
            }
        }

        public override void Reset()
        {
            _cooldown = 0f;
            _isCharging = false;
            _chargeElapsed = 0f;
            _charge = 0f;
            _isStreaming = false;
            _burstDuration = 0f;
            _burstTimer = 0f;
            _shotTimer = 0f;
            _bloom = 0f;
        }

        public override bool OnTriggerHeld(WeaponAim aim, float dt, IWeaponRuntimeHost host)
        {
            if (!host.IsAlive || _cooldown > 0f)
                return false;

            // 正在扫射中，扳机输入不打断既有扫射
            if (_isStreaming)
                return true;

            if (!_isCharging)
            {
                float costPerShot = Config != null ? Config.inkCostPerShot : 0.6f;
                if (host.CurrentInk < costPerShot * 5f)
                {
                    host.ReportEmptyFeedback();
                    _cooldown = 0.25f;
                    return false;
                }

                _isCharging = true;
                _chargeElapsed = 0f;
                _charge = 0f;
            }

            return false;
        }

        public override void Tick(float dt, bool triggerHeld, WeaponAim aim, IWeaponRuntimeHost host)
        {
            if (!host.IsAlive)
            {
                Reset();
                return;
            }

            if (_cooldown > 0f)
                _cooldown -= dt;

            // 1. 扫射推进（最高优先级）
            if (_isStreaming)
            {
                _burstTimer -= dt;
                _shotTimer -= dt;

                while (_shotTimer <= 0f && _burstTimer > 0f)
                {
                    float inkCost = Config != null ? Config.inkCostPerShot : 0.6f;
                    if (!host.ConsumeInk(inkCost))
                    {
                        host.ReportEmptyFeedback();
                        _isStreaming = false;
                        _burstTimer = 0f;
                        _cooldown = 0.22f;
                        break;
                    }

                    FireSplatlingShot(aim, host);
                    float interval = Config != null ? Mathf.Max(0.02f, Config.fireInterval) : 0.066f;
                    _shotTimer += interval;
                }

                if (_burstTimer <= 0f)
                {
                    _isStreaming = false;
                    _cooldown = 0.22f;
                }
                return;
            }

            // 2. 蓄力逻辑
            if (_isCharging)
            {
                if (triggerHeld)
                {
                    _chargeElapsed += dt;
                    float chargeTime = Config != null ? Mathf.Max(0.05f, Config.splatlingChargeTime) : 0.85f;
                    _charge = Mathf.Clamp01(_chargeElapsed / chargeTime);
                    return;
                }

                // 松开扳机触发扫射
                _isCharging = false;
                float minDur = Config != null ? Config.splatlingBurstMinDuration : 0.3f;
                float maxDur = Config != null ? Config.splatlingBurstMaxDuration : 1.7f;
                _burstDuration = Mathf.Lerp(minDur, maxDur, _charge);
                _burstTimer = _burstDuration;
                _isStreaming = true;
                _shotTimer = 0f;
                _bloom = 0f;
                return;
            }

            // 3. 散布回复
            if (!triggerHeld)
            {
                float recover = Config != null ? Mathf.Max(0.05f, Config.bloomRecoverSeconds) : 0.28f;
                _bloom = Mathf.Max(0f, _bloom - dt / recover);
            }
        }

        private void FireSplatlingShot(WeaponAim aim, IWeaponRuntimeHost host)
        {
            float speed = Config != null ? Mathf.Max(1f, Config.projectileSpeed) : 40f;
            float gravity = Config != null ? Config.projectileGravity : 28f;
            float maxRange = Config != null ? Mathf.Max(1f, Config.maxRange) : 15f;
            float damage = Config != null ? Config.damage : 28f;
            float hitRadius = Config != null ? Config.projectileRadius : 0.18f;

            Vector3 muzzle = host.MuzzlePosition;
            Vector3 solveTarget = aim.GetShotTarget(muzzle, maxRange) + Vector3.up * hitRadius;

            Vector3 launchDir = InkBallistics.SolveLaunchDirection(muzzle, solveTarget, speed, gravity, maxRange);
            Vector3 shotDir = InkBallistics.ApplySpread(launchDir, SpreadDegrees);

            host.SpawnShot(new WeaponShot
            {
                Origin = muzzle,
                Direction = shotDir,
                Speed = speed,
                Gravity = gravity,
                MaxRange = maxRange,
                Damage = damage,
                HitRadius = hitRadius,
                FarDamage = 0f,
                FalloffStart = 0f,
                FalloffDistance = 0f
            });

            float bloomPerShot = Config != null ? Mathf.Max(0f, Config.bloomPerShot) : 0.05f;
            _bloom = Mathf.Min(1f, _bloom + bloomPerShot);

            host.ReportShotFeedback();
        }
    }
}
