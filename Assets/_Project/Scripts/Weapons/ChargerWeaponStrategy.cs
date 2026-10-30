using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// Glint Charger（蓄力狙）：按住扳机蓄力（默认 1.0s 满），松开瞬间沿瞄准方向发射贯穿光束。
    /// 蓄力越久射程越远、伤害越高：满蓄力直击 160，一枪秒杀；光束沿途在地面拉出瞬时笔直墨线。
    /// 蓄力进度通过 <see cref="Charge01"/> 暴露给 HUD / 激光指示线。
    /// </summary>
    public sealed class ChargerWeaponStrategy : MainWeaponStrategy
    {
        private float _cooldown;
        private bool _charging;
        private float _chargeElapsed;
        private float _charge;

        public ChargerWeaponStrategy(WeaponConfigSO config) : base(config)
        {
        }

        public override WeaponKind Kind => WeaponKind.Charger;

        public override bool IsCharging => _charging;

        public override float Charge01 => _charge;

        public bool IsFullyCharged => _charging && _charge >= 0.995f;

        public float ChargeTime => Config != null ? Mathf.Max(0.05f, Config.chargeTime) : 1f;

        public override void Reset()
        {
            _cooldown = 0f;
            _charging = false;
            _chargeElapsed = 0f;
            _charge = 0f;
        }

        public override bool OnTriggerHeld(WeaponAim aim, float dt, IWeaponRuntimeHost host)
        {
            if (!host.IsAlive || _cooldown > 0f)
                return false;

            if (_charging)
                return false;

            float costFull = Config != null ? Mathf.Max(0f, Config.chargeInkCostFull) : 18f;
            float minFraction = Config != null ? Mathf.Clamp01(Config.chargeMinInkFraction) : 0.2f;
            if (host.CurrentInk < costFull * minFraction)
            {
                host.ReportEmptyFeedback();
                _cooldown = 0.25f;
                return false;
            }

            _charging = true;
            _chargeElapsed = 0f;
            _charge = 0f;
            return false;
        }

        public override void Tick(float dt, bool triggerHeld, WeaponAim aim, IWeaponRuntimeHost host)
        {
            if (_cooldown > 0f)
                _cooldown -= dt;

            if (!_charging)
                return;

            if (!host.IsAlive)
            {
                Reset();
                return;
            }

            if (triggerHeld)
            {
                _chargeElapsed += dt;
                float time01 = Mathf.Clamp01(_chargeElapsed / ChargeTime);

                // 平滑 S 曲线：起手 20% 就能看到明显蓄力，1.0s 仍精确到达满蓄力。
                float curve = Mathf.SmoothStep(0f, 1f, time01);

                // 墨水不足时蓄不满：把上限钳制在当前墨水能支撑的蓄力比例。
                float costFull = Config != null ? Mathf.Max(0.0001f, Config.chargeInkCostFull) : 18f;
                float inkCap = Mathf.Clamp01(host.CurrentInk / costFull);
                _charge = Mathf.Min(curve, inkCap);
                return;
            }

            // 松开扳机（或输入被禁用）→ 发射。
            FireChargedShot(aim, host);
            _charging = false;
            _chargeElapsed = 0f;
            _charge = 0f;
        }

        private bool FireChargedShot(WeaponAim aim, IWeaponRuntimeHost host)
        {
            if (!host.IsAlive)
                return false;

            // 点按也会打出一发弱化墨线（至少 12% 蓄力），避免“按了没反应”。
            float charge = Mathf.Clamp(_charge, 0.12f, 1f);
            float costFull = Config != null ? Mathf.Max(0f, Config.chargeInkCostFull) : 18f;
            if (!host.ConsumeInk(costFull * charge))
            {
                host.ReportEmptyFeedback();
                return false;
            }

            float rangeMin = Config != null ? Config.chargeRangeMin : 11f;
            float rangeMax = Config != null ? Config.chargeRangeMax : 27f;
            float damageMin = Config != null ? Config.chargeDamageMin : 40f;
            float damageMax = Config != null ? Config.chargeDamageMax : 160f;

            float range = Mathf.Lerp(rangeMin, rangeMax, charge);
            float damage = charge >= 0.995f
                ? damageMax
                : Mathf.Lerp(damageMin, damageMax * 0.62f, charge);

            Vector3 muzzle = host.MuzzlePosition;

            // 沿「枪口 → 准星命中点」发射：抬头就是一条朝天的光束，对墙就在墙面截断并铺墨线。
            Vector3 direction = aim.GetShotDirection(muzzle);

            float lineRadius = Config != null ? Mathf.Max(0.05f, Config.chargeLineRadius) : 0.55f;
            float impactRadius = Config != null ? Mathf.Max(0.05f, Config.chargeImpactRadius) : 1.2f;

            host.FireBeam(new WeaponBeam
            {
                Origin = muzzle,
                Direction = direction,
                Range = range,
                Damage = damage,
                PaintRadius = lineRadius * (0.8f + 0.4f * charge),
                PaintStep = Config != null ? Mathf.Max(0.2f, Config.chargeLineSplatEvery) : 1.2f,
                ImpactRadius = impactRadius * (0.6f + 0.4f * charge),
                Pierce = true,
                Intensity01 = charge
            });

            host.ReportShotFeedback();
            _cooldown = Config != null ? Mathf.Max(0f, Config.chargeReleaseCooldown) : 0.28f;
            return true;
        }
    }
}
