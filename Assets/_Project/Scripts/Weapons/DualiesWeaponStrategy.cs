using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// Twinfin Dualies（双枪）：
    /// 1. 双手交替射击：12 发/秒高射速，单发 30 伤害，枪口左右轻微错开；
    /// 2. 战术翻滚：射击或机动中可触发战术翻滚（最多 2 连滚），耗墨 7，位移 2.8m 并沿途留墨；
    /// 3. 架枪强化：翻滚后 0.5s 射速提升 20%（约 15 发/秒）、弹道散布清零。
    ///    期间<strong>不再锁定移速</strong>：旧实现把移速清零「站桩」，翻滚后想立刻脱身却被钉住 0.5s，
    ///    操作反馈极差；现在锁的只有开火参数与表现层姿态（准星锁定框 / 架枪躯干下沉）。
    /// </summary>
    public sealed class DualiesWeaponStrategy : MainWeaponStrategy
    {
        private float _cooldown;
        private float _bloom;
        private int _hand; // 0 = 右手, 1 = 左手
        private float _dodgeTimer;
        private float _turretTimer;
        private int _rollsLeft = 2;
        private Vector3 _dodgeDirection = Vector3.forward;
        private float _dodgeSide;
        private float _dodgeForward = 1f;
        private float _rollPaintTimer;

        public DualiesWeaponStrategy(WeaponConfigSO config) : base(config)
        {
            _rollsLeft = config != null ? config.dualiesMaxRolls : 2;
        }

        public override WeaponKind Kind => WeaponKind.Dualies;

        public override bool IsRollingDodge => _dodgeTimer > 0f;

        public override bool IsTurretLocked => _turretTimer > 0f;

        // 架枪强化不锁移速（P40 修正）：翻滚结束即可继续走位，只有射速与散布吃到增益。
        public override float MoveSpeedMultiplier => 1f;

        public override int RollsRemaining => _rollsLeft;

        public override Vector3 DodgeDirection => _dodgeDirection;

        public override float DodgeSide => _dodgeSide;

        public override float DodgeForward => _dodgeForward;

        public override float SpreadDegrees
        {
            get
            {
                if (IsTurretLocked)
                {
                    return Config != null ? Config.dualiesTurretSpreadDegrees : 0f;
                }

                float min = Config != null ? Mathf.Max(0f, Config.spreadMinDegrees) : 0.5f;
                float max = Config != null ? Mathf.Max(min, Config.spreadDegrees) : 6.5f;
                return Mathf.Lerp(min, max, Mathf.Clamp01(_bloom));
            }
        }

        public override void Reset()
        {
            _cooldown = 0f;
            _bloom = 0f;
            _hand = 0;
            _dodgeTimer = 0f;
            _turretTimer = 0f;
            _rollsLeft = Config != null ? Config.dualiesMaxRolls : 2;
            _rollPaintTimer = 0f;
            _dodgeSide = 0f;
            _dodgeForward = 1f;
        }

        public override bool TryPerformDodgeRoll(Vector3 moveDirection, IWeaponRuntimeHost host)
        {
            if (!host.IsAlive || _dodgeTimer > 0f || _rollsLeft <= 0)
                return false;

            float inkCost = Config != null ? Config.dualiesRollInkCost : 7f;
            if (!host.ConsumeInk(inkCost))
            {
                host.ReportEmptyFeedback();
                return false;
            }

            _rollsLeft--;
            float dur = Config != null ? Mathf.Max(0.05f, Config.dualiesRollDuration) : 0.3f;
            _dodgeTimer = dur;
            _turretTimer = 0f;
            _rollPaintTimer = 0f;

            Vector3 flat = new Vector3(moveDirection.x, 0f, moveDirection.z);
            _dodgeDirection = flat.sqrMagnitude > 0.001f ? flat.normalized : host.Forward;

            // 触发瞬间锁定相对身体右方与前方的投影（翻滚中角色会随位移转向，必须用起滚相对向量）
            Vector3 hostFwd = host.Forward;
            hostFwd.y = 0f;
            if (hostFwd.sqrMagnitude < 0.001f) hostFwd = Vector3.forward;
            hostFwd.Normalize();
            Vector3 hostRight = Vector3.Cross(Vector3.up, hostFwd).normalized;
            _dodgeSide = Vector3.Dot(_dodgeDirection, hostRight);
            _dodgeForward = Vector3.Dot(_dodgeDirection, hostFwd);

            host.ReportShotFeedback();
            return true;
        }

        public override bool OnTriggerHeld(WeaponAim aim, float dt, IWeaponRuntimeHost host)
        {
            if (!host.IsAlive || _dodgeTimer > 0f || _cooldown > 0f)
                return false;

            float inkCost = Config != null ? Config.inkCostPerShot : 0.85f;
            if (!host.ConsumeInk(inkCost))
            {
                host.ReportEmptyFeedback();
                _cooldown = FireInterval;
                return false;
            }

            // 架枪状态射速提升 20%
            float currentInterval = IsTurretLocked
                ? (Config != null ? Mathf.Max(0.02f, Config.dualiesTurretFireInterval) : 0.0664f)
                : FireInterval;

            _cooldown = currentInterval;

            float hitRadius = Config != null ? Config.projectileRadius : 0.18f;
            float speed = Config != null ? Config.projectileSpeed : 32f;
            float gravity = Config != null ? Config.projectileGravity : 28f;
            float maxRange = Config != null ? Config.maxRange : 11f;

            // 双手交替枪口位置横向偏移
            Vector3 baseMuzzle = host.MuzzlePosition;
            Vector3 flatAim = aim.FlatDirection;
            Vector3 right = Vector3.Cross(Vector3.up, flatAim).normalized;
            float offset = Config != null ? Config.dualiesMuzzleOffset : 0.22f;
            Vector3 muzzle = baseMuzzle + right * (_hand == 0 ? offset : -offset);
            _hand ^= 1;

            Vector3 solveTarget = aim.GetRangeClampedTarget(muzzle, maxRange) + Vector3.up * hitRadius;

            Vector3 launchDir = InkBallistics.SolveLaunchDirection(muzzle, solveTarget, speed, gravity, maxRange);
            Vector3 shotDir = InkBallistics.ApplySpread(launchDir, SpreadDegrees);

            host.SpawnShot(new WeaponShot
            {
                Origin = muzzle,
                Direction = shotDir,
                Speed = speed,
                Gravity = gravity,
                MaxRange = maxRange,
                Damage = Config != null ? Config.damage : 30f,
                HitRadius = hitRadius,
                FarDamage = 0f,
                FalloffStart = 0f,
                FalloffDistance = 0f
            });

            if (!IsTurretLocked)
            {
                float bloomPerShot = Config != null ? Mathf.Max(0f, Config.bloomPerShot) : 0.25f;
                _bloom = Mathf.Min(1f, _bloom + bloomPerShot);
            }

            host.ReportShotFeedback();
            return true;
        }

        public override void Tick(float dt, bool triggerHeld, WeaponAim aim, IWeaponRuntimeHost host)
        {
            if (_cooldown > 0f)
                _cooldown -= dt;

            // 1. 翻滚状态推进
            if (_dodgeTimer > 0f)
            {
                _dodgeTimer -= dt;
                _rollPaintTimer -= dt;
                if (_rollPaintTimer <= 0f)
                {
                    _rollPaintTimer = 0.05f;
                    host.PaintGround(host.Position, 0.45f);
                }

                if (_dodgeTimer <= 0f)
                {
                    _dodgeTimer = 0f;
                    // 翻滚结束立刻进入架枪强化（射速 +20% / 散布清零），但不再锁住移动
                    _turretTimer = Config != null ? Mathf.Max(0.05f, Config.dualiesTurretDuration) : 0.5f;
                    host.PaintGround(host.Position, 0.65f);
                }
            }
            // 2. 原地架枪计时
            else if (_turretTimer > 0f)
            {
                _turretTimer -= dt;
                if (_turretTimer < 0f)
                    _turretTimer = 0f;
            }

            // 3. 翻滚次数回复：非翻滚且非架枪，松开扳机即可充能恢复
            if (!triggerHeld && _dodgeTimer <= 0f && _turretTimer <= 0f)
            {
                int maxRolls = Config != null ? Config.dualiesMaxRolls : 2;
                _rollsLeft = maxRolls;

                float recover = Config != null ? Mathf.Max(0.05f, Config.bloomRecoverSeconds) : 0.28f;
                _bloom = Mathf.Max(0f, _bloom - dt / recover);
            }
        }
    }
}
