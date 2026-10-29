using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// Swell Roller（滚筒）：
    /// 1. 按下扳机先进入短挥击前摇，前摇结束向正前方扇形抛出 9 滴墨（近 125 / 远 30）；
    /// 2. 按住扳机且贴地时切换为推地形态，按移动距离消耗墨水并每帧向宿主提交一条 1.9m 宽的
    ///    <see cref="RollSweep"/>（涂地 + 前方矩形范围 140 碾压伤害由宿主执行）。
    /// </summary>
    public sealed class RollerWeaponStrategy : MainWeaponStrategy
    {
        private float _cooldown;
        private float _flickWindup = -1f;
        private float _emptyFeedbackCooldown;
        private bool _isRolling;
        private Vector3 _lastRollPosition;

        public RollerWeaponStrategy(WeaponConfigSO config) : base(config)
        {
        }

        public override WeaponKind Kind => WeaponKind.Roller;

        public override bool IsRolling => _isRolling;

        public override void Reset()
        {
            _cooldown = 0f;
            _flickWindup = -1f;
            _emptyFeedbackCooldown = 0f;
            _isRolling = false;
            _lastRollPosition = Vector3.zero;
        }

        public override bool OnTriggerHeld(WeaponAim aim, float dt, IWeaponRuntimeHost host)
        {
            if (!host.IsAlive)
                return false;

            // 挥击前摇期间不接受新的输入，避免按住时连续触发挥击。
            if (_flickWindup >= 0f)
                return false;

            bool pressedThisFrame = !host.TriggerHeldLastFrame;
            if (pressedThisFrame && _cooldown <= 0f)
            {
                float flickInk = Config != null ? Config.flickInkCost : 9f;
                if (!host.ConsumeInk(flickInk))
                {
                    ReportEmpty(host);
                    _cooldown = 0.2f;
                    return false;
                }

                _flickWindup = Config != null ? Mathf.Max(0f, Config.flickWindup) : 0.22f;
                if (_flickWindup <= 0f)
                {
                    _flickWindup = -1f;
                    FireFlick(aim, host);
                    return true;
                }
            }

            return _isRolling;
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
            if (_emptyFeedbackCooldown > 0f)
                _emptyFeedbackCooldown -= dt;

            // 1. 挥击前摇：走完立刻抛出扇形墨滴（即使中途松开扳机也照常挥出，墨水已付）。
            if (_flickWindup >= 0f)
            {
                _flickWindup -= dt;
                _isRolling = false;
                if (_flickWindup <= 0f)
                {
                    _flickWindup = -1f;
                    FireFlick(aim, host);
                }
                return;
            }

            // 2. 推地形态：需要贴地、按住扳机且墨水未枯竭。
            float minInk = Config != null ? Mathf.Max(0f, Config.rollMinInkToStart) : 0.5f;
            bool canRoll = triggerHeld && host.IsGrounded && host.CurrentInk > minInk;

            if (!canRoll)
            {
                _isRolling = false;
                return;
            }

            Vector3 current = host.Position;
            if (!_isRolling)
            {
                _isRolling = true;
                _lastRollPosition = current;
            }

            Vector3 delta = current - _lastRollPosition;
            delta.y = 0f;
            float moved = delta.magnitude;

            if (moved > 0.0005f)
            {
                float inkPerMeter = Config != null ? Mathf.Max(0f, Config.rollInkPerMeter) : 1.1f;
                if (!host.ConsumeInk(inkPerMeter * moved))
                {
                    _isRolling = false;
                    _lastRollPosition = current;
                    ReportEmpty(host);
                    return;
                }
            }

            host.ApplyRoll(new RollSweep
            {
                From = _lastRollPosition,
                To = current,
                Forward = aim.FlatDirection,
                Width = Config != null ? Mathf.Max(0.2f, Config.rollWidth) : 1.9f,
                ForwardReach = Config != null ? Mathf.Max(0.1f, Config.rollForwardReach) : 1.35f,
                Damage = Config != null ? Config.rollDamage : 140f,
                HitInterval = Config != null ? Mathf.Max(0.05f, Config.rollDamageInterval) : 0.5f,
                PaintRadius = Config != null ? Mathf.Max(0.1f, Config.rollPaintRadius) : 0.34f
            });

            _lastRollPosition = current;
        }

        private void FireFlick(WeaponAim aim, IWeaponRuntimeHost host)
        {
            int drops = Config != null ? Mathf.Max(1, Config.flickDrops) : 9;
            float fanDegrees = Config != null ? Mathf.Max(0f, Config.flickSpreadDegrees) : 34f;
            float speed = Config != null ? Mathf.Max(1f, Config.flickSpeed) : 17f;
            float gravity = Config != null ? Mathf.Max(0.01f, Config.flickGravity) : 26f;
            float range = Config != null ? Mathf.Max(0.5f, Config.flickRange) : 8.5f;
            float pitchDegrees = Config != null ? Config.flickPitchDegrees : 18f;
            float hitRadius = Config != null ? Mathf.Max(0.05f, Config.flickHitRadius) : 0.85f;
            float nearDamage = Config != null ? Config.flickDamageNear : 125f;
            float farDamage = Config != null ? Config.flickDamageFar : 30f;
            float falloffStart = Config != null ? Mathf.Max(0f, Config.flickFalloffStart) : 1.5f;

            Vector3 flatAim = aim.FlatDirection;
            float baseYaw = Mathf.Atan2(flatAim.x, flatAim.z) * Mathf.Rad2Deg;
            Vector3 origin = host.Position + Vector3.up * 1.15f + flatAim * 0.55f;
            float pitch = pitchDegrees * Mathf.Deg2Rad;

            for (int i = 0; i < drops; i++)
            {
                // t = -1..1：扇面两侧速度衰减，中间墨滴飞得最远。
                float t = drops > 1 ? (i / (float)(drops - 1)) * 2f - 1f : 0f;
                float yaw = baseYaw + t * fanDegrees * 0.5f + Random.Range(-1.2f, 1.2f);
                float speedScale = 0.82f + 0.28f * (1f - Mathf.Abs(t)) + Random.Range(0f, 0.08f);
                float yawRad = yaw * Mathf.Deg2Rad;
                float cosPitch = Mathf.Cos(pitch);

                Vector3 dir = new Vector3(
                    Mathf.Sin(yawRad) * cosPitch,
                    Mathf.Sin(pitch),
                    Mathf.Cos(yawRad) * cosPitch).normalized;

                host.SpawnShot(new WeaponShot
                {
                    Origin = origin,
                    Direction = dir,
                    Speed = speed * speedScale,
                    Gravity = gravity,
                    MaxRange = range,
                    Damage = nearDamage,
                    HitRadius = hitRadius,
                    FarDamage = farDamage,
                    FalloffStart = falloffStart,
                    FalloffDistance = range
                });
            }

            host.ReportShotFeedback();
        }

        private void ReportEmpty(IWeaponRuntimeHost host)
        {
            if (_emptyFeedbackCooldown > 0f)
                return;

            _emptyFeedbackCooldown = 0.45f;
            host.ReportEmptyFeedback();
        }
    }
}
