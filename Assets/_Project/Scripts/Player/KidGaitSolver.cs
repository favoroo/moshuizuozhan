using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// P33 真骨骼（squidkid）单帧步态姿态（纯数据，无 Transform 依赖）。
    /// 与旧蛋形版的本质区别：躯干前倾不再是一整块旋转，而是按 INKWAVE 的脊柱链分配
    /// （character.js runPose：hips 30% / spine 45% / chest 25%），腿部两骨链在骨盆下独立踩地。
    /// </summary>
    public struct KidGaitPose
    {
        /// <summary>骨盆竖向位移（米，= 贴地几何下沉 HipDropY + 步频起伏附加项）。</summary>
        public float BobY;
        /// <summary>骨盆贴地下沉量（米，≤0）：支撑腿大腿摆角按腿长投影求解，支撑脚不抬不扎。</summary>
        public float HipDropY;
        /// <summary>骨盆绕 Z 左右摇滚（度）。</summary>
        public float RockDeg;

        /// <summary>总前倾量（度）：脊柱链分配前的源值（控制器按 share 拆给三层 + 头部稳定补偿）。</summary>
        public float LeanTotalDeg;
        /// <summary>骨盆分担的前倾（度）。</summary>
        public float HipPitchDeg;
        /// <summary>腰胸分担的前倾（度）。</summary>
        public float SpinePitchDeg;
        /// <summary>胸腔分担的前倾（度）。</summary>
        public float ChestPitchDeg;

        /// <summary>双臂反相摆动（度，带符号：正 = 左臂向后）。</summary>
        public float ArmSwingDeg;
        /// <summary>左肘屈曲角（度，正 = 屈肘；前摆臂更弯）。</summary>
        public float LeftElbowBendDeg;
        /// <summary>右肘屈曲角（度）。</summary>
        public float RightElbowBendDeg;

        /// <summary>左大腿 X 摆角（度，正 = 向后摆）。</summary>
        public float LeftThighDeg;
        /// <summary>右大腿 X 摆角（度）。</summary>
        public float RightThighDeg;
        /// <summary>左膝屈角（度，正 = 屈膝；只在摆动相打开）。</summary>
        public float LeftKneeDeg;
        /// <summary>右膝屈角（度）。</summary>
        public float RightKneeDeg;
        /// <summary>左脚掌俯仰（度，正 = 脚尖下垂）。</summary>
        public float LeftFootPitchDeg;
        /// <summary>右脚掌俯仰（度）。</summary>
        public float RightFootPitchDeg;

        /// <summary>竖向挤压系数（触地压扁 &lt; 1，作用于 BodySquash 节点）。</summary>
        public float SquashY;
        /// <summary>横向膨胀系数（触地 &gt; 1）。</summary>
        public float SquashXZ;

        public static readonly KidGaitPose Neutral = new KidGaitPose
        {
            BobY = 0f,
            HipDropY = 0f,
            RockDeg = 0f,
            LeanTotalDeg = 0f,
            HipPitchDeg = 0f,
            SpinePitchDeg = 0f,
            ChestPitchDeg = 0f,
            ArmSwingDeg = 0f,
            LeftElbowBendDeg = 0f,
            RightElbowBendDeg = 0f,
            LeftThighDeg = 0f,
            RightThighDeg = 0f,
            LeftKneeDeg = 0f,
            RightKneeDeg = 0f,
            LeftFootPitchDeg = 0f,
            RightFootPitchDeg = 0f,
            SquashY = 1f,
            SquashXZ = 1f
        };
    }

    /// <summary>滞空单帧姿态权重（四权重分解，继承 P26 验证过的时序，字段改 Kid 命名）。</summary>
    public struct KidAirPose
    {
        public float UpW;
        public float LaunchW;
        public float ApexW;
        public float ReachW;
        /// <summary>跑步起跳前后腿劈开角（度，正 = 左腿向前顶、右腿向后蹬）。</summary>
        public float RunSplitDeg;
        /// <summary>本帧竖向挤压乘算修正（快速下落拉伸 / 顶点微团身，1 = 不修正）。</summary>
        public float SquashYMul;

        public static readonly KidAirPose Neutral = new KidAirPose
        {
            UpW = 0f,
            LaunchW = 0f,
            ApexW = 0f,
            ReachW = 0f,
            RunSplitDeg = 0f,
            SquashYMul = 1f
        };
    }

    /// <summary>步态幅度参数包（由 CharacterAnimConfigSO 填充；纯函数不依赖 SO，便于单测）。</summary>
    public struct KidGaitParams
    {
        public float BobSlow;
        public float BobFast;
        public float SquashDepth;
        public float SquashBulge;
        public float RockSlowDeg;
        public float RockFastDeg;
        public float ArmSwingSlowDeg;
        public float ArmSwingFastDeg;
        public float LegSwingSlowDeg;
        public float LegSwingFastDeg;
        public float KneeBendSlowDeg;
        public float KneeBendFastDeg;
        public float ToeUpDeg;
        public float ToeOffDeg;
        /// <summary>髋→踝腿长（米）= CharacterMetrics.ThighLength + ShinLength。</summary>
        public float LegLength;
        public float ElbowBendBaseDeg;
        public float ElbowBendFastDeg;
        public float ElbowSwingGainDeg;
        public float LeanSlowDeg;
        public float LeanFastDeg;
        public float HipLeanShare;
        public float SpineLeanShare;
        public float ChestLeanShare;

        /// <summary>默认值 = KidGaitSolver 常量（测试锁定与 CharacterAnimConfigSO 默认字段一致）。</summary>
        public static KidGaitParams Default => new KidGaitParams
        {
            BobSlow = KidGaitSolver.BobSlow,
            BobFast = KidGaitSolver.BobFast,
            SquashDepth = KidGaitSolver.SquashDepth,
            SquashBulge = KidGaitSolver.SquashBulge,
            RockSlowDeg = KidGaitSolver.RockSlow,
            RockFastDeg = KidGaitSolver.RockFast,
            ArmSwingSlowDeg = KidGaitSolver.ArmSwingSlow,
            ArmSwingFastDeg = KidGaitSolver.ArmSwingFast,
            LegSwingSlowDeg = KidGaitSolver.LegSwingSlow,
            LegSwingFastDeg = KidGaitSolver.LegSwingFast,
            KneeBendSlowDeg = KidGaitSolver.KneeBendSlow,
            KneeBendFastDeg = KidGaitSolver.KneeBendFast,
            ToeUpDeg = KidGaitSolver.ToeUpMax,
            ToeOffDeg = KidGaitSolver.ToeOffMax,
            LegLength = KidGaitSolver.LegLengthDefault,
            ElbowBendBaseDeg = KidGaitSolver.ElbowBendBase,
            ElbowBendFastDeg = KidGaitSolver.ElbowBendFast,
            ElbowSwingGainDeg = KidGaitSolver.ElbowSwingGain,
            LeanSlowDeg = KidGaitSolver.LeanSlow,
            LeanFastDeg = KidGaitSolver.LeanFast,
            HipLeanShare = KidGaitSolver.HipLeanShare,
            SpineLeanShare = KidGaitSolver.SpineLeanShare,
            ChestLeanShare = KidGaitSolver.ChestLeanShare
        };
    }

    /// <summary>滞空表演参数包。</summary>
    public struct KidAirParams
    {
        public float ApexSoften;
        public float FallStretch;
        public float ReachThighDeg;
        public float RunSplitThighDeg;

        public static KidAirParams Default => new KidAirParams
        {
            ApexSoften = KidGaitSolver.AirApexSoften,
            FallStretch = KidGaitSolver.AirFallStretch,
            ReachThighDeg = KidGaitSolver.AirReachThighDeg,
            RunSplitThighDeg = KidGaitSolver.AirRunSplitThighDeg
        };
    }

    /// <summary>
    /// P33 真骨骼角色步态求解：左右腿独立相位 FK + 骨盆贴地几何（支撑脚世界钉地）+
    /// 脊柱链前倾分配（hips/spine/chest 30/45/25，参考 INKWAVE character.js L1601-1694 的 runPose）+
    /// 摆臂/屈肘反相 + 触地挤压。
    /// 与旧蛋形求解器的关键差异：
    /// 1. 腿长 0.22 → 0.433（真实两骨腿），峰值匹配律下同样摆幅的步频自然降 half，跑动不再"碎步打滑"；
    /// 2. 前倾是分配量而非整体量：骨盆微倾、腰胸主体倾、胸腔少量，头部由控制器反向稳定（HeadStabilize）；
    /// 3. 摆臂幅度按真实手臂重新标定为常规跑姿量级（12°→38°），配合屈肘 22°→62°。
    /// 纯函数、无状态、零分配，可在 EditMode 直接单测；实际位移仍由移动组件负责。
    /// </summary>
    public static class KidGaitSolver
    {
        /// <summary>低于此速度视为静止，调用方不应推进步态相位。</summary>
        public const float MoveSpeedThreshold = 1.0f;

        /// <summary>人形跑速归一化分母（m/s）。</summary>
        public const float HumanRunSpeed = 6.0f;
        /// <summary>步态起始速度（m/s）。</summary>
        public const float GaitMinSpeed = 0.25f;

        // ---- 骨盆竖向：贴地几何之外的步频起伏（米）----
        public const float BobSlow = 0.006f;
        public const float BobFast = 0.012f;
        /// <summary>触地挤压深度 / 横向膨胀比例。</summary>
        public const float SquashDepth = 0.030f;
        public const float SquashBulge = 0.018f;
        /// <summary>骨盆左右摇滚（度）：慢走 / 快跑。</summary>
        public const float RockSlow = 2.0f;
        public const float RockFast = 4.5f;

        // ---- 摆臂（度）：真手臂绕肩前后摆，慢走收着、快跑迈开 ----
        public const float ArmSwingSlow = 12f;
        public const float ArmSwingFast = 38f;

        // ---- 屈肘（度）：基础弯常驻（站立持枪不读成直棍），快跑收拢加深，前摆臂额外弯 ----
        public const float ElbowBendBase = 22f;
        public const float ElbowBendFast = 62f;
        public const float ElbowSwingGain = 16f;

        // ---- 双腿：慢走迈得开（长腿小碎步反而怪异），快跑大步幅高频 ----
        public const float LegSwingSlow = 14f;
        public const float LegSwingFast = 42f;
        public const float KneeBendSlow = 22f;
        public const float KneeBendFast = 65f;

        // ---- 脚掌：摆动相脚尖上翘 / 支撑末段蹬地压尖（度）----
        public const float ToeUpMax = 10f;
        public const float ToeOffMax = 14f;

        /// <summary>髋→踝腿长（米）= CharacterMetrics.ThighLength + ShinLength。</summary>
        public const float LegLengthDefault =
            CharacterMetrics.ThighLength + CharacterMetrics.ShinLength;

        // ---- 步频（峰值匹配律的钳位；长腿自然把同速步频压到正常跑姿区间）----
        public const float CadenceMin = 1.6f;
        public const float CadenceMax = 9.0f;

        // ---- 前倾（度）：行走微前倾 → 冲刺压胸；按脊柱链分配 ----
        public const float LeanSlow = 4f;
        public const float LeanFast = 13f;
        public const float HipLeanShare = 0.25f;
        public const float SpineLeanShare = 0.45f;
        public const float ChestLeanShare = 0.30f;

        // ---- 横移半侧转：角色面向准星，横移时骨盆朝移动方向偏转 ----
        public const float StrafeYawMaxDeg = 28f;
        public const float StrafeSwingFalloff = 0.55f;
        public const float BackwardSwingScale = 0.5f;

        // ---- 滞空 / 攀墙收腿（度）：真腿收腿幅度按两骨链重标（团身更明显）----
        public const float AirTuckThighDeg = 30f;
        public const float AirTuckKneeDeg = 82f;
        public const float AirTuckFootDeg = 16f;
        public const float WallClimbTuckKneeDeg = 34f;

        // ---- 滞空表演 ----
        public const float AirApexSoften = 0.02f;
        public const float AirFallStretch = 0.04f;
        public const float AirReachThighDeg = 20f;
        public const float AirRunSplitThighDeg = 30f;
        /// <summary>起跳水平速度超过该值才做"跑步起跳"前后腿劈开。</summary>
        public const float RunSplitSpeedThreshold = 3.0f;

        /// <summary>把实际移速归一化（11.8 m/s 为潜墨全速上限）。</summary>
        public static float Speed01(float moveSpeed)
            => Mathf.Clamp01(moveSpeed / 11.8f);

        /// <summary>人形步态归一化（分母 = 6.0 m/s 跑速上限）。</summary>
        public static float Speed01Run(float moveSpeed)
            => Mathf.Clamp01(moveSpeed / HumanRunSpeed);

        /// <summary>
        /// 防滑核心（峰值匹配律）：cadence = speed / (π·L·amp)。
        /// 正弦摆腿在支撑中点（θ=π）的相对后扫速度 = L·amp·θ̇ = L·amp·π·cadence，
        /// 令其等于身体速度 → 支撑中段脚世界钉地；摆幅含步向衰减时自动换成
        /// "小摆幅 + 高步频"的自洽碎步。长腿（0.433m）下同速步频天然约为旧蛋腿的一半。
        /// </summary>
        public static float CadenceFromGait(float speed, float ampEffectiveRad, float legLength)
            => Mathf.Clamp(
                speed / (Mathf.PI * Mathf.Max(0.01f, legLength) * Mathf.Max(0.02f, ampEffectiveRad)),
                CadenceMin, CadenceMax);

        /// <summary>按标准摆幅查表推导步频（预览/离线采样用）。</summary>
        public static float CadenceFromSpeed(float speed, float speed01)
        {
            float ampRad = Mathf.Lerp(LegSwingSlow, LegSwingFast, Mathf.Clamp01(speed01)) * Mathf.Deg2Rad;
            return CadenceFromGait(speed, ampRad, LegLengthDefault);
        }

        /// <summary>横移半侧转目标角（度）：侧向速度占比线性映射。</summary>
        public static float StrafeYawTargetDeg(Vector3 localVel, float speed, float gaitWeight)
        {
            if (speed < MoveSpeedThreshold || gaitWeight <= 0.001f) return 0f;
            float side01 = Mathf.Clamp(localVel.x / Mathf.Max(0.1f, speed), -1f, 1f);
            return side01 * StrafeYawMaxDeg * gaitWeight;
        }

        /// <summary>摆腿/摆臂步向衰减：横移收窄前后摆、后退再减半。</summary>
        public static float DirectionalSwingScale(float forwardness)
        {
            float fwd01 = Mathf.Clamp01(Mathf.Abs(forwardness));
            float scale = Mathf.Lerp(StrafeSwingFalloff, 1f, fwd01);
            if (forwardness < 0f)
            {
                scale *= Mathf.Lerp(1f, BackwardSwingScale, Mathf.Clamp01(-forwardness * 2f));
            }
            return scale;
        }

        /// <summary>采样步态（默认参数版）。</summary>
        public static KidGaitPose SampleGait(float speed01, float phaseRadians)
            => SampleGait(speed01, phaseRadians, KidGaitParams.Default);

        /// <summary>
        /// 采样步态（参数包版）。
        /// 相位约定（单腿 θ）：0 = 摆动中点（脚最高），π/2 = 落地，π = 支撑中点（腿垂直），3π/2 = 蹬地；
        /// 左腿 θ=φ、右腿 θ=φ+π。骨盆下沉按支撑腿摆角与腿长几何求解，支撑脚踩在站立高度。
        /// </summary>
        public static KidGaitPose SampleGait(float speed01, float phaseRadians, in KidGaitParams p)
        {
            speed01 = Mathf.Clamp01(speed01);

            float sway = Mathf.Sin(phaseRadians);
            float lift = Mathf.Abs(sway);          // |sinφ|=1 双脚撑地最低点；0 支撑腿垂直最高点
            float effort = 0.5f + 0.5f * speed01;

            SampleLeg(phaseRadians, speed01, p, out float leftThigh, out float leftKnee, out float leftFoot);
            SampleLeg(phaseRadians + Mathf.PI, speed01, p, out float rightThigh, out float rightKnee, out float rightFoot);

            // 骨盆贴地：支撑腿摆角按腿长投影（L(1-cos)），支撑脚恰好落回站立地面
            float legAmp = Mathf.Lerp(p.LegSwingSlowDeg, p.LegSwingFastDeg, speed01);
            float stanceThighAbsDeg = lift * legAmp;
            float hipDrop = p.LegLength * (Mathf.Cos(stanceThighAbsDeg * Mathf.Deg2Rad) - 1f); // ≤ 0
            float extraDip = -lift * Mathf.Lerp(p.BobSlow, p.BobFast, speed01) * 0.25f;

            float lean = Mathf.Lerp(p.LeanSlowDeg, p.LeanFastDeg, speed01) * (0.4f + 0.6f * speed01);

            KidGaitPose pose = KidGaitPose.Neutral;
            pose.BobY = hipDrop + extraDip;
            pose.HipDropY = hipDrop;
            pose.RockDeg = sway * Mathf.Lerp(p.RockSlowDeg, p.RockFastDeg, speed01);
            pose.LeanTotalDeg = lean;
            pose.HipPitchDeg = lean * p.HipLeanShare;
            pose.SpinePitchDeg = lean * p.SpineLeanShare;
            pose.ChestPitchDeg = lean * p.ChestLeanShare;
            pose.ArmSwingDeg = sway * Mathf.Lerp(p.ArmSwingSlowDeg, p.ArmSwingFastDeg, speed01);
            pose.LeftThighDeg = leftThigh;
            pose.RightThighDeg = rightThigh;
            pose.LeftKneeDeg = leftKnee;
            pose.RightKneeDeg = rightKnee;
            pose.LeftFootPitchDeg = leftFoot;
            pose.RightFootPitchDeg = rightFoot;
            pose.SquashY = 1f - p.SquashDepth * lift * effort;
            pose.SquashXZ = 1f + p.SquashBulge * lift * effort;

            float baseBend = Mathf.Lerp(p.ElbowBendBaseDeg, p.ElbowBendFastDeg, speed01);
            pose.LeftElbowBendDeg = baseBend + Mathf.Max(0f, -sway) * p.ElbowSwingGainDeg;
            pose.RightElbowBendDeg = baseBend + Mathf.Max(0f, sway) * p.ElbowSwingGainDeg;
            return pose;
        }

        /// <summary>
        /// 单腿相位姿态：θ=0 摆动中点（抬脚最高）、π/2 落地、π 支撑中点、3π/2 蹬地。
        /// 膝弯只在摆动相（cosθ&gt;0）打开；脚掌摆动相上翘、蹬地段压尖。
        /// </summary>
        private static void SampleLeg(float theta, float speed01, in KidGaitParams p,
            out float thighDeg, out float kneeDeg, out float footDeg)
        {
            float s = Mathf.Sin(theta);
            float c = Mathf.Cos(theta);
            float swingW = Mathf.Max(0f, c);

            float amp = Mathf.Lerp(p.LegSwingSlowDeg, p.LegSwingFastDeg, speed01);
            float kneeAmp = Mathf.Lerp(p.KneeBendSlowDeg, p.KneeBendFastDeg, speed01);
            float toeUp = Mathf.Lerp(p.ToeUpDeg * 0.6f, p.ToeUpDeg, speed01);
            float toeOff = Mathf.Lerp(p.ToeOffDeg * 0.6f, p.ToeOffDeg, speed01);

            thighDeg = -s * amp;
            kneeDeg = swingW * kneeAmp * (0.5f + 0.5f * speed01);
            footDeg = -c * swingW * toeUp
                    + Mathf.Max(0f, -s) * Mathf.Max(0f, -c) * toeOff;
        }

        /// <summary>采样滞空表演权重（默认参数版）。</summary>
        public static KidAirPose SampleAir(float verticalVel, float airTime, float jumpRunSpeed)
            => SampleAir(verticalVel, airTime, jumpRunSpeed, KidAirParams.Default);

        /// <summary>
        /// 采样滞空表演权重（参考 INKWAVE _poseAir）：
        /// 起跳蹬伸（launch）→ 中段团身（控制器乘 (1-launch)(1-reach)）→ 顶点悬停（apex）→ 下落够地（reach）。
        /// </summary>
        public static KidAirPose SampleAir(float verticalVel, float airTime, float jumpRunSpeed, in KidAirParams p)
        {
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((verticalVel + 1.5f) / 5.5f));
            float launch = airTime < 0.3f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((airTime - 0.03f) / 0.17f)) : 0f;
            float apex = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(verticalVel) - 0.6f) / 2.6f));
            float reach = (1f - up) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((airTime - 0.25f) / 0.3f));

            float fallStretch01 = Mathf.Clamp01((-verticalVel - 4f) / 6f);
            float squashYMul = 1f + p.FallStretch * fallStretch01 - p.ApexSoften * apex;

            return new KidAirPose
            {
                UpW = up,
                LaunchW = launch,
                ApexW = apex,
                ReachW = reach,
                RunSplitDeg = jumpRunSpeed > RunSplitSpeedThreshold ? launch * p.RunSplitThighDeg : 0f,
                SquashYMul = squashYMul
            };
        }
    }
}
