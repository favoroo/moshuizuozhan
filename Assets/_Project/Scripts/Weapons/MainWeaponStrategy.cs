using P1.Core;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>一帧的瞄准上下文：地面点供弹道反解与蓄力狙墨线使用。</summary>
    public struct WeaponAim
    {
        public Vector3 Direction;
        public Vector3 GroundPoint;
        public bool HasGroundPoint;

        public static WeaponAim FromDirection(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 0.0001f)
                flat = Vector3.forward;
            flat.Normalize();
            return new WeaponAim
            {
                Direction = flat,
                GroundPoint = Vector3.zero,
                HasGroundPoint = false
            };
        }

        public static WeaponAim FromDirection(Vector3 direction, Vector3 groundPoint)
        {
            WeaponAim aim = FromDirection(direction);
            aim.GroundPoint = groundPoint;
            aim.HasGroundPoint = true;
            return aim;
        }

        /// <summary>水平瞄准方向；无效时退回世界正前方。</summary>
        public Vector3 FlatDirection
        {
            get
            {
                Vector3 flat = new Vector3(Direction.x, 0f, Direction.z);
                return flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
            }
        }
    }

    /// <summary>
    /// 武器策略与 Unity 世界的唯一接口。WeaponController 负责实现，EditMode 测试用 Fake 实现即可
    /// 完整验证开火节奏、伤害与涂地参数，不依赖场景、物理或对象池。
    /// </summary>
    public interface IWeaponRuntimeHost
    {
        Team Team { get; }
        bool IsAlive { get; }
        bool IsGrounded { get; }
        float CurrentInk { get; }
        Vector3 Position { get; }
        Vector3 Forward { get; }
        Vector3 MuzzlePosition { get; }
        WeaponConfigSO Config { get; }

        /// <summary>上一帧是否收到主武器扳机输入（用于识别“刚按下”的挥击边沿）。</summary>
        bool TriggerHeldLastFrame { get; }

        bool ConsumeInk(float amount);
        void SpawnShot(WeaponShot shot);
        void FireBeam(WeaponBeam beam);
        void ApplyRoll(RollSweep sweep);
        void PaintGround(Vector3 worldPos, float radius);
        void ReportShotFeedback();
        void ReportEmptyFeedback();
    }

    /// <summary>
    /// 主武器策略基类（策略模式）：所有武器共享“扳机按住 + 每帧状态推进”的驱动方式，
    /// 具体开火节奏、蓄力、推地由子类实现。公共接口只增不改，兼容旧 <see cref="WeaponController"/> 调用方。
    /// </summary>
    public abstract class MainWeaponStrategy
    {
        protected MainWeaponStrategy(WeaponConfigSO config)
        {
            Config = config;
        }

        public WeaponConfigSO Config { get; private set; }

        public abstract WeaponKind Kind { get; }

        /// <summary>当前准星散布半角（度）；非连发武器为 0。</summary>
        public virtual float SpreadDegrees => 0f;

        /// <summary>蓄力进度 0~1；非蓄力武器为 0。</summary>
        public virtual float Charge01 => 0f;

        public virtual bool IsCharging => false;

        public virtual bool IsRolling => false;

        /// <summary>双枪战术翻滚中（期间无法正常开火，位移并沿途滴墨）。</summary>
        public virtual bool IsRollingDodge => false;

        /// <summary>双枪翻滚后的原地架枪状态（移速锁定、射速提升 20%、散布清零）。</summary>
        public virtual bool IsTurretLocked => false;

        /// <summary>武器对移动速度的系数（正常 1.0，架枪 0.0，加特林蓄力/扫射减速）。</summary>
        public virtual float MoveSpeedMultiplier => 1f;

        /// <summary>双枪当前剩余可用翻滚次数（最多 2 次）。</summary>
        public virtual int RollsRemaining => 0;

        /// <summary>尝试触发双枪战术翻滚。</summary>
        public virtual bool TryPerformDodgeRoll(Vector3 moveDirection, IWeaponRuntimeHost host) => false;

        /// <summary>重开 / 复活 / 换武器时清空全部内部计时。</summary>
        public virtual void Reset()
        {
        }

        /// <summary>
        /// 本帧收到主武器扳机输入（按住期间每帧调用一次）。返回 true 表示本帧产生了一次有效攻击。
        /// </summary>
        public abstract bool OnTriggerHeld(WeaponAim aim, float dt, IWeaponRuntimeHost host);

        /// <summary>
        /// 每帧末推进状态机：冷却、散布恢复、蓄力、滚筒推地。triggerHeld 为本帧是否收到扳机输入；
        /// 蓄力狙据此在松开的一瞬间发射。
        /// </summary>
        public virtual void Tick(float dt, bool triggerHeld, WeaponAim aim, IWeaponRuntimeHost host)
        {
        }

        protected float FireInterval => Config != null ? Mathf.Max(0.02f, Config.fireInterval) : 0.12f;
    }
}
