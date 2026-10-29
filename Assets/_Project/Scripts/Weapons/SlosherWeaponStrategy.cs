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

        public SlosherWeaponStrategy(WeaponConfigSO config) : base(config)
        {
        }

        public override WeaponKind Kind => WeaponKind.Slosher;

        public bool IsWindingUp => _windupTimer >= 0f;

        public override void Reset()
        {
            _cooldown = 0f;
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
                    FireSlosherWave(_pendingAim.FlatDirection.sqrMagnitude > 0.001f ? _pendingAim : aim, host);

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

            var volley = new SlosherVolley();
            int drops = Config != null ? Mathf.Max(1, Config.slosherDrops) : 8;
            float speed = Config != null ? Mathf.Max(1f, Config.projectileSpeed) : 15f;
            float gravity = Config != null ? Mathf.Max(0.01f, Config.projectileGravity) : 22f;
            float maxRange = Config != null ? Mathf.Max(1f, Config.maxRange) : 9.5f;
            float pitchDegrees = Config != null ? Config.slosherPitchDegrees : 18f;
            float headDamage = Config != null ? Config.slosherHeadDamage : 70f;
            float tailDamage = Config != null ? Config.slosherTailDamage : 34f;

            Vector3 muzzle = host.MuzzlePosition;
            Vector3 flatAim = aim.FlatDirection;
            float baseYaw = Mathf.Atan2(flatAim.x, flatAim.z);
            float basePitch = pitchDegrees * Mathf.Deg2Rad;

            for (int i = 0; i < drops; i++)
            {
                float k = drops > 1 ? i / (float)(drops - 1) : 0f;
                float sp = speed * (1f - 0.18f * k);
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
