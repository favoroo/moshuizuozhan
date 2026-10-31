using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 角色程序化动画调参入口（人⇄乌贼形态手势、游动姿态、触须行波、表演性点缀）。
    /// 默认值全部来自 INKWAVE 逆向源码（inkwave-aah-research/src/game/character.js L383-386/1342-1358/3550-3611），
    /// 未绑定该资产时 CharacterVisualController 会在运行时回退到等值默认实例。
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterAnimConfig", menuName = "P1/Character Anim Config")]
    public class CharacterAnimConfigSO : ScriptableObject
    {
        [Header("形态手势 (Form Gesture)")]
        [Tooltip("驱动形态标量 f 的阻尼弹簧固有频率 (rad/s)。ω=18 ζ=0.35 时约 0.4s 过冲回稳，对齐 Inkwave 0.42s 手势")]
        public float formSpringOmega = 18f;
        [Tooltip("阻尼比：<1 过冲（浮出拉伸/潜入回稳摆动），≥1 临界无过冲")]
        [Range(0.05f, 2f)]
        public float formSpringZeta = 0.35f;
        [Tooltip("f 下限（负值区 = 浮出时的拉伸过冲区）")]
        public float formAmountMin = -0.35f;
        [Tooltip("f 上限（>1 区 = 潜入到位后的回稳摆动区）")]
        public float formAmountMax = 1.35f;
        [Tooltip("f 超过该值后人形态根节点隐藏（kid 已压扁进墨面）")]
        public float kidHideFormAmount = 0.98f;
        [Tooltip("f 低于该值后乌贼形态根节点显示（从墨里钻出）")]
        public float squidShowFormAmount = 0.02f;
        [Tooltip("潜入时 f 达到该值立即隐藏武器")]
        public float weaponHideFormAmount = 0.35f;
        [Tooltip("浮出时 f 回落到该值才显示武器（与上一项形成迟滞，防边界闪烁）")]
        public float weaponShowFormAmount = 0.30f;

        [Header("形态手势曲线（横轴 f：-0.35 浮出过冲区 / 0 人形 / 1 乌贼 / >1 潜入回稳区）")]
        [Tooltip("人形态纵向缩放：潜入压扁 1→0.85→0.34→0.16，浮出过冲拉伸到 ~1.3")]
        public AnimationCurve kidScaleY = new AnimationCurve(
            new Keyframe(-0.25f, 1.30f), new Keyframe(-0.12f, 0.97f), new Keyframe(0f, 1f),
            new Keyframe(0.12f, 0.85f), new Keyframe(0.45f, 0.34f), new Keyframe(0.75f, 0.16f),
            new Keyframe(1f, 0.15f), new Keyframe(1.2f, 0.14f));

        [Tooltip("人形态横向缩放：压扁时膨胀到 ~1.4，拉伸时收窄到 ~0.72")]
        public AnimationCurve kidScaleXZ = new AnimationCurve(
            new Keyframe(-0.25f, 0.72f), new Keyframe(-0.12f, 1.02f), new Keyframe(0f, 1f),
            new Keyframe(0.12f, 1.08f), new Keyframe(0.45f, 1.40f), new Keyframe(0.75f, 1.22f),
            new Keyframe(1f, 1.20f), new Keyframe(1.2f, 1.20f));

        [Tooltip("乌贼纵向缩放：从墨里钻出 0.3→0.72→过冲 1.28→回稳 1.0；>1 区为回稳摆动")]
        public AnimationCurve squidScaleY = new AnimationCurve(
            new Keyframe(0f, 0.30f), new Keyframe(0.15f, 0.72f), new Keyframe(0.30f, 1.28f),
            new Keyframe(0.45f, 0.95f), new Keyframe(0.70f, 1.06f), new Keyframe(1f, 1f),
            new Keyframe(1.12f, 1.05f), new Keyframe(1.25f, 0.97f), new Keyframe(1.35f, 1f));

        [Tooltip("乌贼横向缩放：钻出时略鼓、回稳摆动")]
        public AnimationCurve squidScaleXZ = new AnimationCurve(
            new Keyframe(0f, 0.90f), new Keyframe(0.30f, 1.05f), new Keyframe(0.45f, 0.92f),
            new Keyframe(1f, 1f), new Keyframe(1.12f, 0.96f), new Keyframe(1.25f, 1.02f),
            new Keyframe(1.35f, 1f));

        [Header("游动姿态 (Swim Posture，对齐 Inkwave character.js L3550-3560)")]
        [Tooltip("动画用全速归一化速度 (m/s)：与 PlayerConfig.swimMoveSpeed 一致")]
        public float swimRefSpeed = 11.8f;
        [Tooltip("身体起伏波频率下限 (rad/s)，慢速蠕动")]
        public float swimUndulationFreqSlow = 5f;
        [Tooltip("身体起伏波频率上限 (rad/s)，全速 14 对齐 Inkwave")]
        public float swimUndulationFreqFast = 14f;
        [Tooltip("起伏俯仰幅度（度，全速时 ±3.4°）")]
        public float swimUndulationPitchDeg = 3.4f;
        [Tooltip("转向侧倾系数：roll(rad) = -yawRate·factor，0.08 为 Inkwave 原值")]
        public float swimBankFactor = 0.08f;
        [Tooltip("转向侧倾上限（度）。Inkwave ±0.5rad≈28.6°，这里取保守 ±20°")]
        public float swimBankMaxDeg = 20f;
        [Tooltip("头部点摆幅度（度，Inkwave 0.1rad≈5.7°）")]
        public float swimSwayDeg = 5f;
        [Tooltip("头部点摆频率下限 (rad/s)")]
        public float swimSwayFreqSlow = 4f;
        [Tooltip("头部点摆频率上限 (rad/s)")]
        public float swimSwayFreqFast = 11f;
        [Tooltip("全速纵向拉伸上限（保体积：横向自动 1/√）。Inkwave 0.2")]
        [Range(0f, 0.5f)]
        public float swimStretchMax = 0.20f;

        [Header("触须行波 (Tentacle Wave)")]
        [Tooltip("摆频下限 (rad/s)，近静止时 12 对齐 Inkwave wigFreq 12+8·sv")]
        public float tentacleFreqSlow = 12f;
        [Tooltip("摆频上限 (rad/s)，全速 20")]
        public float tentacleFreqFast = 20f;
        [Tooltip("近静止摆幅（度）：隐匿时轻微飘动")]
        public float tentacleAmpIdleDeg = 3f;
        [Tooltip("全速摆幅（度）：Inkwave 读数上限 ~0.04 折算骨骼拍水 ±14°")]
        public float tentacleAmpFastDeg = 14f;
        [Tooltip("相邻触须相位差（rad，模拟根→尖行波传播）")]
        public float tentaclePhaseStep = 0.9f;
        [Tooltip("侧向飘摆系数（相对拍水摆幅）")]
        [Range(0f, 1f)]
        public float tentacleSideSwayFactor = 0.5f;

        [Header("贴地下沉与表演点缀")]
        [Tooltip("乌贼展开时身体沉入墨面的深度 (米)，半潜观感")]
        public float squidSubmergeDepth = 0.10f;
        [Tooltip("浮出甩臂幅度（度）：双臂弹开亮相")]
        public float emergeArmFlingDeg = 35f;
        [Tooltip("浮出甩臂时长（秒）")]
        public float emergeArmFlingDuration = 0.28f;
        [Tooltip("浮出果冻拉伸冲量（起跳拉伸同款弹簧）")]
        public float emergeJellyKick = 2.2f;
        [Tooltip("潜入甩臂幅度（度）：自由臂高举如跳水")]
        public float diveArmFlingDeg = 55f;
        [Tooltip("潜入甩臂时长（秒）")]
        public float diveArmFlingDuration = 0.22f;
        [Tooltip("潜入果冻压扁冲量（ anticipation 下蹲）")]
        public float diveJellyKick = -1.5f;
        [Tooltip("形态切换时墨焰/发辫的惯性甩动幅度（度）")]
        public float formHairKickDeg = 14f;

        /// <summary>运行时实际使用的速度归一化分母：优先玩家配置的潜墨速度。</summary>
        public float ResolveSwimRefSpeed(PlayerConfigSO playerConfig)
        {
            return playerConfig != null && playerConfig.swimMoveSpeed > 0.1f
                ? playerConfig.swimMoveSpeed
                : swimRefSpeed;
        }
    }
}
