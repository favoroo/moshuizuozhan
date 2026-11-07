using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// Tidebucket Slosher（泼桶）：
    /// 1. 按下扳机经历 0.13s 前摇，随后向前方抛出一道 8 滴墨浪，可越过矮掩体；
    /// 2. Volley 聚合算法：一次泼出的全部墨滴共享同一个 <see cref="SlosherVolley"/>，
    ///    对同一目标最多造成 1 次直击（70 伤害），确保 2 桶稳定击杀，防止贴身瞬秒。
    /// </summary>
    public sealed class SlosherWeaponStrategy : MainWeaponStrategy
    {
        private float _cooldown;
        private float _windupTimer = -1f;
        private WeaponAim _pendingAim;

        /// <summary>每波泼洒独立租用 Volley 的环形对象池（在飞墨滴的命中记录不会被下一波清空）。</summary>
        private readonly SlosherVolleyPool _volleyPool = new SlosherVolleyPool();

        /// <summary>
        /// 一波墨滴的最长飞行寿命（秒）：泼桶初速 15m/s、重力 22m/s²，最远 9.5m 约 2.4s 落地；
        /// 4s 留足缓冲，保证 Volley 只在整波墨滴确实消失后才被复用。
        /// </summary>
        private const float VolleyFlightLifetime = 4f;

        public SlosherWeaponStrategy(WeaponConfigSO config) : base(config)
        {
        }

        public override WeaponKind Kind => WeaponKind.Slosher;

        public override bool IsWindingUp => _windupTimer >= 0f;

        /// <summary>甩桶前摇期间移速 ×0.7（INKWAVE weapons.js：slosh ≥ 0 → moveSpeedFiring × 0.7，重心随挥桶下沉）。</summary>
        public override float MoveSpeedMultiplier => IsWindingUp ? 0.7f : 1f;

        public override float Windup01
        {
            get
            {
                if (_windupTimer < 0f)
                    return 0f;

                float windup = Config != null ? Mathf.Max(0.0001f, Config.slosherWindup) : 0.13f;
                return Mathf.Clamp01(1f - _windupTimer / windup);
            }
        }

        public override void Reset()
        {
            _cooldown = 0f;
            _windupTimer = -1f;
            _volleyPool.Clear();
        }

        public override bool IsTriggerEngaged => _windupTimer >= 0f;

        /// <summary>
        /// 输入被抑制（暂停 / 终场 / 死亡 / 投雷占用）：丢弃前摇中的这一桶，不生成墨滴
        /// （墨水已在按下时扣除，不回补）。
        /// </summary>
        public override void OnTriggerSuppressed(IWeaponRuntimeHost host)
        {
            _windupTimer = -1f;
        }

        public override bool OnTriggerHeld(WeaponAim aim, float dt, IWeaponRuntimeHost host)
        {
            if (!host.IsAlive || _windupTimer >= 0f || _cooldown > 0f)
                return false;

            float inkCost = Config != null ? Config.inkCostPerShot : 7.5f;
            if (!host.ConsumeInk(inkCost))
            {
                host.ReportEmptyFeedback();
                _cooldown = 0.2f;
                return false;
            }

            _pendingAim = aim;
            float windup = Config != null ? Mathf.Max(0f, Config.slosherWindup) : 0.13f;
            if (windup <= 0.001f)
            {
                _windupTimer = -1f;
                FireSlosherWave(aim, host);
                _cooldown = FireInterval;
                return true;
            }

            _windupTimer = windup;
            return true;
        }

        public override void Tick(float dt, bool triggerHeld, WeaponAim aim, IWeaponRuntimeHost host)
        {
            if (_cooldown > 0f)
                _cooldown -= dt;

            if (_windupTimer >= 0f)
            {
                _windupTimer -= dt;
                if (_windupTimer <= 0f)
                {
                    _windupTimer = -1f;
                    // 甩出瞬间取「当前准星」（对齐 INKWAVE fireSlosh：出手时才读 a.aimPoint）：
                    // 前摇仅 0.13s，但按「按下瞬间」锁方向会让甩枪出现「枪口朝新目标、墨浪飞旧方向」。
                    // 当前准星退化（零向量）时才回落到按下瞬间记录的瞄准。
                    WeaponAim fireAim = aim.FlatDirection.sqrMagnitude > 0.001f ? aim : _pendingAim;
                    FireSlosherWave(fireAim, host);

                    float totalInterval = FireInterval;
                    float windup = Config != null ? Mathf.Max(0f, Config.slosherWindup) : 0.13f;
                    _cooldown = Mathf.Max(0.05f, totalInterval - windup);
                }
            }
        }

        public void FireSlosherWave(WeaponAim aim, IWeaponRuntimeHost host)
        {
            if (!host.IsAlive)
                return;

            // 每波泼洒独立租用一个 Volley：上一波还在飞的墨滴仍持有自己的命中记录，
            // 既不会被 Clear 掉（旧实现的跨波复用会让同一目标吃 70 + 34 多段直击），也不需要每波 new。
            SlosherVolley volley = _volleyPool.Rent(Time.time, VolleyFlightLifetime);
            int drops = Config != null ? Mathf.Max(1, Config.slosherDrops) : 8;
            float speed = Config != null ? Mathf.Max(1f, Config.projectileSpeed) : 15f;
            float gravity = Config != null ? Mathf.Max(0.01f, Config.projectileGravity) : 22f;
            float maxRange = Config != null ? Mathf.Max(1f, Config.maxRange) : 9.5f;
            float pitchDegrees = Config != null ? Config.slosherPitchDegrees : 18f;
            float headDamage = Config != null ? Config.slosherHeadDamage : 70f;
            float tailDamage = Config != null ? Config.slosherTailDamage : 34f;

            Vector3 muzzle = host.MuzzlePosition;
            Vector3 target = aim.GetRangeClampedTarget(muzzle, maxRange);
            Vector3 toTarget = target - muzzle;
            float horizontal = new Vector2(toTarget.x, toTarget.z).magnitude;
            float baseYaw = horizontal > 0.3f
                ? Mathf.Atan2(toTarget.x, toTarget.z)
                : Mathf.Atan2(aim.FlatDirection.x, aim.FlatDirection.z);

            // 抛桶解算：近距离固定 18° 抬升、由初速控制落点；远目标 / 高台 / 朝天改用低伸弹道解，
            // 仰角钳制到 [18°, 69°]，头滴始终砸在准星上，抬头还能越过掩体泼到高处。
            InkBallistics.SolveLobLaunch(
                muzzle,
                target,
                speed,
                gravity,
                maxRange,
                pitchDegrees * Mathf.Deg2Rad,
                out float basePitch,
                out float launchSpeed);

            for (int i = 0; i < drops; i++)
            {
                float k = drops > 1 ? i / (float)(drops - 1) : 0f;
                float sp = launchSpeed * (1f - 0.18f * k);
                float pt = basePitch - 0.04f * k;
                float yawOffset = i == 0 ? 0f : (i % 2 == 1 ? 1f : -1f) * 0.025f * Mathf.Min(1f, i / 3f);
                float yw = baseYaw + yawOffset;

                Vector3 dir = new Vector3(
                    Mathf.Sin(yw) * Mathf.Cos(pt),
                    Mathf.Sin(pt),
                    Mathf.Cos(yw) * Mathf.Cos(pt)).normalized;

                float damage = i == 0 ? headDamage : tailDamage;
                float hitRadius = i == 0 ? 0.35f : 0.25f;

                host.SpawnShot(new WeaponShot
                {
                    Origin = muzzle,
                    Direction = dir,
                    Speed = sp,
                    Gravity = gravity,
                    MaxRange = maxRange,
                    Damage = damage,
                    HitRadius = hitRadius,
                    FarDamage = 0f,
                    FalloffStart = 0f,
                    FalloffDistance = 0f,
                    Volley = volley
                });
            }

            host.ReportShotFeedback();
        }
    }
}
