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
        private float _rollElapsed;
        private float _recoverTimer;
        private Vector3 _lastRollPosition;

        public RollerWeaponStrategy(WeaponConfigSO config) : base(config)
        {
        }

        public override WeaponKind Kind => WeaponKind.Roller;

        /// <summary>起射门槛：低于推地起步墨水时滚筒既不能推地也不能挥击（准星按推地条语义预警）。</summary>
        public override float InkRequiredToFire =>
            Config != null ? Mathf.Max(0.01f, Config.rollMinInkToStart) : 0.5f;

        public override bool IsRolling => _isRolling;

        /// <summary>挥击前摇期间为 true（表现层据此把滚筒抡过肩）；Tick 走完前摇的同一帧回到 false。</summary>
        public override bool IsFlicking => _flickWindup >= 0f;

        /// <summary>
        /// 滚筒移速手感（P35，参考 INKWAVE weapons.js L57-68）：
        /// 挥击前摇 1 → 0.45；推地 0.5 → 1（0.45s）；挥击后收势 0.6 → 1（0.18s）。
        /// 推地时角色按 firingMoveSpeed 计速，因此满速仍是 3.8m/s，不改变平衡上限。
        /// </summary>
        public override float MoveSpeedMultiplier
        {
            get
            {
                float windup = Config != null ? Mathf.Max(0f, Config.flickWindup) : 0.22f;
                if (_flickWindup >= 0f && windup > 0.0001f)
                {
                    float t = Mathf.Clamp01(1f - _flickWindup / windup);
                    float factor = Config != null ? Mathf.Clamp01(Config.flickWindupMoveFactor) : 0.45f;
                    return Mathf.Lerp(1f, factor, Mathf.SmoothStep(0f, 1f, t));
                }

                if (_isRolling)
                {
                    float accel = Config != null ? Mathf.Max(0.05f, Config.rollAccelTime) : 0.45f;
                    float start = Config != null ? Mathf.Clamp01(Config.rollStartSpeedFactor) : 0.5f;
                    float t = Mathf.Clamp01(_rollElapsed / accel);
                    return Mathf.Lerp(start, 1f, Mathf.SmoothStep(0f, 1f, t));
                }

                float recover = Config != null ? Mathf.Max(0f, Config.flickRecoverMoveTime) : 0.18f;
                if (_recoverTimer > 0f && recover > 0.0001f)
                {
                    float start = Config != null ? Mathf.Clamp01(Config.flickRecoverMoveFactor) : 0.6f;
                    float t = Mathf.Clamp01(1f - _recoverTimer / recover);
                    return Mathf.Lerp(start, 1f, t);
                }

                return 1f;
            }
        }

        public override void Reset()
        {
            _cooldown = 0f;
            _flickWindup = -1f;
            _emptyFeedbackCooldown = 0f;
            _isRolling = false;
            _rollElapsed = 0f;
            _recoverTimer = 0f;
            _lastRollPosition = Vector3.zero;
        }

        public override bool IsTriggerEngaged => _isRolling || _flickWindup >= 0f;

        /// <summary>
        /// 输入被抑制（暂停 / 终场 / 死亡 / 投雷占用）：停止推地，并丢弃尚未落地的挥击前摇
        /// （被抑制的帧不得生成墨滴；墨水已在按下时扣除，不回补）。
        /// </summary>
        public override void OnTriggerSuppressed(IWeaponRuntimeHost host)
        {
            _isRolling = false;
            _flickWindup = -1f;
            _rollElapsed = 0f;
            _recoverTimer = 0f;
            if (host != null)
                _lastRollPosition = host.Position;
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

                // 挥击节奏：两次挥击（按下沿）至少间隔 flickInterval，面板显示 1.6 次/秒也就名副其实。
                // 旧实现只在墨水不足时设冷却，连点可以打到 ~4 次/秒。
                // 取 max(flickInterval, windup)：前摇比间隔还长时也不会出现「前摇没走完又能按」的错觉。
                float flickInterval = Config != null ? Mathf.Max(0f, Config.flickInterval) : 0.62f;
                _cooldown = Mathf.Max(flickInterval, _flickWindup);

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
            if (_recoverTimer > 0f)
                _recoverTimer = Mathf.Max(0f, _recoverTimer - dt);

            // 1. 挥击前摇：走完立刻抛出扇形墨滴（即使中途松开扳机也照常挥出，墨水已付）。
            if (_flickWindup >= 0f)
            {
                _flickWindup -= dt;
                _isRolling = false;
                _rollElapsed = 0f;
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
                _rollElapsed = 0f;
                return;
            }

            Vector3 current = host.Position;
            if (!_isRolling)
            {
                _isRolling = true;
                _rollElapsed = 0f;
                _lastRollPosition = current;
            }

            _rollElapsed += dt;

            Vector3 delta = current - _lastRollPosition;
            delta.y = 0f;
            float moved = delta.magnitude;

            // 越界回收 / 复活 / 传送：单帧位移远超正常移动速度时，这一段不是真实滚动。
            // 旧实现会把整段传送距离当成推地长度，一帧画出横贯全图的墨带、扣掉大量墨水并卡顿。
            float maxStep = Config != null ? Mathf.Max(0.2f, Config.rollMaxStepMeters) : 1.5f;
            if (moved > maxStep)
            {
                _lastRollPosition = current;
                _isRolling = false;
                _rollElapsed = 0f;
                return;
            }

            if (moved > 0.0005f)
            {
                float inkPerMeter = Config != null ? Mathf.Max(0f, Config.rollInkPerMeter) : 1.1f;
                if (!host.ConsumeInk(inkPerMeter * moved))
                {
                    _isRolling = false;
                    _rollElapsed = 0f;
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
            // 挥击后移速收势（INKWAVE 0.18s 从 60% 回到 100%）：即使玩家仍按住，
            // 下一帧进入推地分支后由推地起步加速接管，不会卡在收势倍率。
            _recoverTimer = Config != null ? Mathf.Max(0f, Config.flickRecoverMoveTime) : 0.18f;

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

            // 挥击仰角跟随视角（对齐 INKWAVE）：低头最多压低 11.5°，抬头最多抬高 28.6°，
            // 因此抬头挥击能把墨浪甩到高台/掩体上方，低头则贴着地面铺。
            float aimPitch = Mathf.Atan2(aim.Direction.y, new Vector2(aim.Direction.x, aim.Direction.z).magnitude);
            float pitch = pitchDegrees * Mathf.Deg2Rad + Mathf.Clamp(aimPitch, -0.2f, 0.5f);

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
