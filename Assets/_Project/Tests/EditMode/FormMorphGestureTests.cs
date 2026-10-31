using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 形态手势（AnimSpring1D 弹簧 + CharacterAnimConfigSO 曲线）纯逻辑测试。
    /// 数值对标 INKWAVE 逆向源码 character.js L383-386（关键帧表）与 L1342-1358（0.42s 弹性手势）。
    /// </summary>
    public class FormMorphGestureTests
    {
        private const float Omega = 18f;
        private const float Zeta = 0.35f;
        private const float Dt = 1f / 60f;

        private static CharacterAnimConfigSO CreateConfig()
        {
            return ScriptableObject.CreateInstance<CharacterAnimConfigSO>();
        }

        [Test]
        public void FormSpring_SettlesWithinHalfSecondWithOvershoot()
        {
            var spring = new AnimSpring1D(0f);
            float maxValue = 0f;
            float settleTime = -1f;
            float t = 0f;
            for (int i = 0; i < 90; i++)
            {
                spring.Step(1f, Omega, Zeta, Dt);
                t += Dt;
                maxValue = Mathf.Max(maxValue, spring.Value);
                if (settleTime < 0f && Mathf.Abs(spring.Value - 1f) < 0.08f)
                {
                    settleTime = t;
                }
            }

            Assert.LessOrEqual(settleTime, 0.7f, "0.42s 手势应在 0.7s 内基本回稳");
            Assert.Greater(maxValue, 1.05f, "欠阻尼弹簧必须过冲（浮出拉伸/潜入回稳摆动的来源）");
            Assert.LessOrEqual(maxValue, 1.35f, "过冲不应超出曲线定义域");
            Assert.AreEqual(1f, spring.Value, 0.08f, "最终应停在目标 1");
        }

        [Test]
        public void FormSpring_FlipMidGesture_NoTeleportScaleJump()
        {
            var spring = new AnimSpring1D(0f);
            for (int i = 0; i < 12; i++) spring.Step(1f, Omega, Zeta, Dt); // 0.2s 潜入中途翻转
            spring.Step(0f, Omega, Zeta, Dt);

            float maxStep = 0f;
            for (int i = 0; i < 90; i++)
            {
                float before = spring.Value;
                spring.Step(0f, Omega, Zeta, Dt);
                maxStep = Mathf.Max(maxStep, Mathf.Abs(spring.Value - before));
            }

            Assert.LessOrEqual(maxStep, 0.30f, "翻转目标后单帧 f 变化应与正常弹簧运动同量级（弹簧速度×dt ≈0.25），不得瞬移（pop）");
            Assert.AreEqual(0f, spring.Value, 0.08f, "最终应回稳到人形 0");
        }

        [Test]
        public void FormSpring_LargeDt_StaysStable()
        {
            var spring = new AnimSpring1D(0f);
            for (int i = 0; i < 10; i++) spring.Step(1f, Omega, Zeta, 0.05f); // 编辑器卡顿级 dt
            Assert.IsFalse(float.IsNaN(spring.Value) || float.IsInfinity(spring.Value));
            Assert.Greater(spring.Value, 0.5f);
            Assert.LessOrEqual(spring.Value, 1.35f);
        }

        [Test]
        public void KidDiveCurve_FlattensLikeInkwave()
        {
            CharacterAnimConfigSO cfg = CreateConfig();
            Assert.AreEqual(1f, cfg.kidScaleY.Evaluate(0f), 0.01f);
            Assert.AreEqual(0.85f, cfg.kidScaleY.Evaluate(0.12f), 0.03f, "潜入初期下压 0.85（Inkwave K_DV_KY 第二键）");
            Assert.LessOrEqual(cfg.kidScaleY.Evaluate(1f), 0.2f, "潜入到位压扁到 Y≤0.2（Inkwave 0.16）");
            Assert.GreaterOrEqual(cfg.kidScaleXZ.Evaluate(0.45f), 1.3f, "压扁时横向膨胀 ~1.4（Inkwave K_DV_KX）");
        }

        [Test]
        public void KidEmergeOvershoot_StretchesTallAndThin()
        {
            CharacterAnimConfigSO cfg = CreateConfig();
            Assert.GreaterOrEqual(cfg.kidScaleY.Evaluate(-0.25f), 1.2f, "浮出过冲区拉伸 Y≥1.2（Inkwave K_EM_KY 首键 1.36）");
            Assert.LessOrEqual(cfg.kidScaleXZ.Evaluate(-0.25f), 0.8f, "拉伸时横向收窄（Inkwave K_EM_KX 首键 0.68）");
        }

        [Test]
        public void SquidCurve_PopsOutOfPuddleThenSettles()
        {
            CharacterAnimConfigSO cfg = CreateConfig();
            Assert.AreEqual(0.30f, cfg.squidScaleY.Evaluate(0f), 0.02f, "乌贼从墨团 30% 钻出（Inkwave K_DV_SY 首键 0.3）");
            Assert.GreaterOrEqual(cfg.squidScaleY.Evaluate(0.30f), 1.2f, "钻出过冲 1.28（Inkwave K_DV_SY 第三键）");
            Assert.AreEqual(1f, cfg.squidScaleY.Evaluate(1f), 0.02f);
            Assert.Greater(Mathf.Abs(cfg.squidScaleY.Evaluate(1.12f) - 1f), 0.02f, "f>1 回稳区应有可见摆动（wobble）");
        }

        [Test]
        public void SwimStretch_PreservesVolume()
        {
            CharacterAnimConfigSO cfg = CreateConfig();
            float stretchZ = 1f + cfg.swimStretchMax; // sv = 1
            float stretchXZ = 1f / Mathf.Sqrt(stretchZ);
            float volume = stretchZ * stretchXZ * stretchXZ;
            Assert.AreEqual(1f, volume, 0.001f, "保体积拉伸：Z·XZ² 应恒等于 1（Inkwave sxz = 1/√sy）");
        }
    }
}
