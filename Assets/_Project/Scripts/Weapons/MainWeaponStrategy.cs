using P1.Core;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 一帧的瞄准上下文。<see cref="Direction"/> 是**含俯仰**的 3D 瞄准方向（相机前向），
    /// <see cref="GroundPoint"/> 是准星射线命中的世界点（地面 / 墙面 / 敌人，朝天时是 70m 处的空中点）。
    /// 武器按「枪口 → 命中点」求实际弹道，因此抬头能打天上、低头能打脚下、对墙能涂墙。
    /// </summary>
    public struct WeaponAim
    {
        public Vector3 Direction;
        public Vector3 GroundPoint;
        public bool HasGroundPoint;

        /// <summary>枪口到命中点小于该距离时不做“指向命中点”修正，直接用瞄准方向，避免近距离病态角度。</summary>
        public const float MinAimPointDistance = 0.6f;

        public static WeaponAim FromDirection(Vector3 direction)
        {
            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            return new WeaponAim
            {
                Direction = dir,
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

        /// <summary>水平瞄准方向；无效时退回世界正前方。用于角色朝向、滚筒滚地与偏航扇面。</summary>
        public Vector3 FlatDirection
        {
            get
            {
                Vector3 flat = new Vector3(Direction.x, 0f, Direction.z);
                return flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
            }
        }

        /// <summary>
        /// 枪口 → 准星命中点的 3D 单位方向（对齐 INKWAVE 的 <c>_aimFrom</c>）：
        /// 命中点过近或在枪口后方（贴墙/贴脸）时回退到相机瞄准方向。
        /// </summary>
        /// <param name="minDistance">
        /// 低于该距离不做指向修正，默认 <see cref="MinAimPointDistance"/>。
        /// 蓄力狙等长枪管武器可传更小值，让近距离光束仍从实际起点收敛到准星。
        /// </param>
        public Vector3 GetShotDirection(Vector3 muzzle, float minDistance = MinAimPointDistance)
        {
            if (!HasGroundPoint)
                return Direction;

            Vector3 toTarget = GroundPoint - muzzle;
            float distance = toTarget.magnitude;
            if (distance < Mathf.Max(0.01f, minDistance))
                return Direction;

            toTarget /= distance;
            return Vector3.Dot(toTarget, Direction) < 0f ? Direction : toTarget;
        }

        /// <summary>
        /// 枪口到瞄准点过近、或瞄准点已在枪口后方时（长枪管近距离贴墙/打地面），
        /// 改用 <paramref name="fallbackChest"/> 作为射击起点。
        /// 否则从枪口沿相机方向发射会与屏幕中心射线平行错开一个枪口横向偏移。
        /// </summary>
        public Vector3 GetShotOrigin(Vector3 muzzle, Vector3 fallbackChest, float minDistance = 0.35f)
        {
            if (!HasGroundPoint)
                return muzzle;

            Vector3 toTarget = GroundPoint - muzzle;
            float min = Mathf.Max(0.01f, minDistance);
            if (toTarget.sqrMagnitude >= min * min && Vector3.Dot(toTarget, Direction) >= 0f)
                return muzzle;

            return fallbackChest;
        }

        /// <summary>
        /// 本次射击应瞄准的目标点：射程内原样返回准星命中点（落点严格贴合准星）；
        /// 超出水平射程时沿「起点 → 命中点」方向截断到 maxRange，让弹丸朝准星方向飞到射程尽头，
        /// 而不是按远处目标高度反解出一个更陡的下压角。
        /// </summary>
        public Vector3 GetRangeClampedTarget(Vector3 muzzle, float maxRange)
        {
            float range = Mathf.Max(0.1f, maxRange);
            if (!HasGroundPoint)
                return muzzle + Direction * range;

            Vector3 toTarget = GroundPoint - muzzle;
            float horizontal = new Vector2(toTarget.x, toTarget.z).magnitude;
            if (horizontal <= range)
                return GroundPoint;

            Vector3 clamped = muzzle + toTarget * (range / Mathf.Max(0.0001f, horizontal));
            return Vector3.Dot(clamped - muzzle, Direction) >= 0f ? clamped : GroundPoint;
        }

        /// <summary>
        /// 本次射击的 3D 目标点：命中点可用时直接取它（含墙面与空中点），
        /// 否则沿瞄准方向取最大射程处，保证永远有一个合法目标。
        /// </summary>
        public Vector3 GetShotTarget(Vector3 muzzle, float maxRange)
        {
            if (HasGroundPoint)
            {
                Vector3 toTarget = GroundPoint - muzzle;
                float distance = toTarget.magnitude;
                if (distance >= MinAimPointDistance && Vector3.Dot(toTarget, Direction) >= 0f)
                    return GroundPoint;
            }

            return muzzle + Direction * Mathf.Max(0.1f, maxRange);
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

        /// <summary>当前有效射程（米）：HUD 准星据此判断是否超出射程。默认取配置 maxRange。</summary>
        public virtual float EffectiveRange => Config != null ? Mathf.Max(0.1f, Config.maxRange) : 0f;

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
