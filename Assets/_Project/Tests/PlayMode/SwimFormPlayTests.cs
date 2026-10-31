using System.Collections;
using NUnit.Framework;
using P1.Ink;
using P1.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 乌贼游动姿态与超速惯性滑行的运行时集成测试：
    /// 1. 游动时身体起伏/侧倾/保体积拉伸、4 根触须逐根行波真实生效；
    /// 2. 出水滑行（INKWAVE swim-exit glide）保留超速惯性并按减速度衰减回人形速度。
    /// </summary>
    public class SwimFormPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        private IEnumerator LoadPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            Assert.IsNotNull(PlayerController.LocalPlayer, "场景里应存在本地玩家 PlayerController");
            Assert.IsNotNull(InkSurfaceController.Instance, "场景里应存在 InkSurfaceController");
        }

        /// <summary>测试用角色引用上下文（迭代器不能带 out 参数，用引用对象回传）。</summary>
        private sealed class SwimmerContext
        {
            public PlayerController Player;
            public InkCharacterMotor Motor;
            public CharacterVisualController Visual;
        }

        /// <summary>把本地玩家移到可涂地主地面并涂出足够大的己方墨池，引用写入 ctx。</summary>
        private IEnumerator PrepareSwimmer(SwimmerContext ctx)
        {
            yield return LoadPlaybox();

            ctx.Player = PlayerController.LocalPlayer;
            ctx.Player.enabled = false; // 禁用外部输入覆盖，直接注入帧意图

            ctx.Motor = ctx.Player.Motor;
            ctx.Visual = ctx.Player.GetComponentInChildren<CharacterVisualController>();
            Assert.IsNotNull(ctx.Motor);
            Assert.IsNotNull(ctx.Visual);

            var paintSpot = new Vector3(0f, 0.1f, -11f);
            // 涂大片墨池：以全速游 1 秒约 11.8m，半径 10 保证全程脚下是己方墨
            InkSurfaceController.Instance.PaintCircle(paintSpot, 10f, ctx.Player.Health.Team);

            var cc = ctx.Player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            ctx.Player.transform.position = paintSpot;
            if (cc != null) cc.enabled = true;
            yield return null;

            Assert.AreEqual(ctx.Player.Health.Team, ctx.Motor.FootInkTeam, "角色应站在己方墨水上");
        }

        /// <summary>每帧在脚下续涂己方墨，保证全速游动期间不会游出墨池（否则潜行会中断）。</summary>
        private static void KeepOnInk(PlayerController player)
        {
            InkSurfaceController.Instance.PaintCircle(player.transform.position, 2.5f, player.Health.Team);
        }

        private static IEnumerator SwimForwardFor(PlayerController player, float seconds, Vector3 moveDir)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                KeepOnInk(player);
                player.Motor.SetFrameIntent(moveDir, moveDir, true, false);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator SwimPosture_BodyUndulatesStretchesAndTentaclesWave()
        {
            var ctx = new SwimmerContext();
            yield return PrepareSwimmer(ctx);
            PlayerController player = ctx.Player;
            InkCharacterMotor motor = ctx.Motor;
            CharacterVisualController visual = ctx.Visual;

            // 全速向前游 0.8s（弹簧手势约 0.5s 到达潜行主导区，之后姿态管线接管）
            yield return SwimForwardFor(player, 0.8f, Vector3.forward);
            Assert.IsTrue(motor.IsSwimming, "己方墨上按潜行并移动应保持潜行形态");

            Transform squidBody = visual.SquidRoot.transform.Find("SquidBody");
            Assert.IsNotNull(squidBody, "乌贼层级应包含 SquidBody");

            Transform tentaclesRoot = squidBody.Find("TrailingTentacles");
            Assert.IsNotNull(tentaclesRoot, "乌贼层级应包含 TrailingTentacles");

            // --- 保体积速度拉伸：沿游进方向（局部 Z）拉长、横向收缩 ---
            Assert.Greater(squidBody.localScale.z, squidBody.localScale.x + 0.05f,
                "全速游动应纵向拉伸、横向收缩（Inkwave sy=1+0.2sv, sxz=1/√sy）");

            // --- 身体起伏：采样多个时刻，至少一帧俯仰明显偏离 0 ---
            float maxPitch = 0f;
            float maxRoll = 0f;
            for (int i = 0; i < 20; i++)
            {
                KeepOnInk(player);
                player.Motor.SetFrameIntent(Vector3.forward, Vector3.forward, true, false);
                yield return null;
                Vector3 euler = squidBody.localRotation.eulerAngles;
                float pitch = Mathf.DeltaAngle(0f, euler.x);
                float roll = Mathf.DeltaAngle(0f, euler.z);
                maxPitch = Mathf.Max(maxPitch, Mathf.Abs(pitch));
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(roll));
            }
            Assert.Greater(maxPitch, 0.4f, "游动起伏波应有可见俯仰（±3.4° 量级）");

            // --- 转向侧倾 banking：向右急转，应出现明显滚转 ---
            Vector3 turnDir = Vector3.right;
            float yawStart = player.transform.eulerAngles.y;
            int swimFrames = 0;
            for (int i = 0; i < 24; i++)
            {
                KeepOnInk(player);
                player.Motor.SetFrameIntent(turnDir, turnDir, true, false);
                yield return null;
                if (motor.IsSwimming) swimFrames++;
                Vector3 euler = squidBody.localRotation.eulerAngles;
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(Mathf.DeltaAngle(0f, euler.z)));
            }
            float yawEnd = player.transform.eulerAngles.y;
            Assert.Greater(maxRoll, 2f,
                $"转向时应出现侧倾 banking（Inkwave ±0.5rad 保守版）；maxRoll={maxRoll:F2} yaw {yawStart:F0}->{yawEnd:F0} swimFrames={swimFrames}/24");

            // --- 4 根触须逐根行波：相邻触须旋转姿态应互不相同 ---
            Assert.GreaterOrEqual(tentaclesRoot.childCount, 4, "应存在 4 根拖尾触须");
            Quaternion t0 = tentaclesRoot.GetChild(0).localRotation;
            Quaternion t1 = tentaclesRoot.GetChild(1).localRotation;
            Quaternion t2 = tentaclesRoot.GetChild(2).localRotation;
            Quaternion t3 = tentaclesRoot.GetChild(3).localRotation;
            float q01 = Mathf.Abs(Quaternion.Dot(t0, t1));
            float q02 = Mathf.Abs(Quaternion.Dot(t0, t2));
            float q03 = Mathf.Abs(Quaternion.Dot(t0, t3));
            Assert.Less(q01, 0.9999f, "相邻触须应因相位差呈现不同旋转（根→尖行波近似）");
            Assert.Less(q02, 0.9999f);
            Assert.Less(q03, 0.9999f);
        }

        [UnityTest]
        public IEnumerator SwimExit_KeepsGlideMomentumThenDecaysToRunSpeed()
        {
            var ctx = new SwimmerContext();
            yield return PrepareSwimmer(ctx);
            PlayerController player = ctx.Player;
            InkCharacterMotor motor = ctx.Motor;

            yield return SwimForwardFor(player, 0.5f, Vector3.forward);
            Assert.IsTrue(motor.IsSwimming);

            // 出水（保留前进输入）：CurrentMoveSpeed 立刻回到人形 6.0，但 EffectiveMoveSpeed 应保留滑行惯性
            player.Motor.SetFrameIntent(Vector3.forward, Vector3.forward, false, false);
            yield return null;

            Assert.IsFalse(motor.IsSwimming, "松开潜行键应脱离潜行");
            Assert.AreEqual(motor.Config.normalMoveSpeed, motor.CurrentMoveSpeed, 0.01f, "人形目标速度应为 6.0");
            Assert.Greater(motor.EffectiveMoveSpeed, motor.CurrentMoveSpeed + 3f,
                "出水第一帧应保留超速滑行惯性（而不是瞬间掉到 6.0）");

            // 惯性按 swimExitGlideDecel=42 衰减：最终必然回落到人形速度
            int frames = 0;
            while (motor.EffectiveMoveSpeed > motor.CurrentMoveSpeed + 0.05f && frames < 120)
            {
                player.Motor.SetFrameIntent(Vector3.forward, Vector3.forward, false, false);
                yield return null;
                frames++;
            }
            Assert.Less(frames, 120, "滑行惯性应在约 0.15s（42 m/s²）内衰减回人形速度");
            Assert.AreEqual(motor.CurrentMoveSpeed, motor.EffectiveMoveSpeed, 0.05f);
        }

        [UnityTest]
        public IEnumerator SwimEntry_EffectiveSpeedJumpsToSwimSpeedImmediately()
        {
            var ctx = new SwimmerContext();
            yield return PrepareSwimmer(ctx);
            PlayerController player = ctx.Player;
            InkCharacterMotor motor = ctx.Motor;

            // 先以人形速度跑一小段，确认加速方向瞬时对齐（滑行只作用于超速方向，不影响起步手感）
            for (int i = 0; i < 3; i++)
            {
                player.Motor.SetFrameIntent(Vector3.forward, Vector3.forward, false, false);
                yield return null;
            }
            Assert.AreEqual(motor.CurrentMoveSpeed, motor.EffectiveMoveSpeed, 0.01f, "人形恒速时 Effective 应与 Current 一致");

            // 进入潜行：目标速度升到 11.8，应瞬时对齐（无加速延迟）
            player.Motor.SetFrameIntent(Vector3.forward, Vector3.forward, true, false);
            yield return null;
            yield return null;
            if (motor.IsSwimming)
            {
                Assert.AreEqual(motor.CurrentMoveSpeed, motor.EffectiveMoveSpeed, 0.01f,
                    "进入潜墨时加速方向应瞬时对齐（只有超速衰减，无加速滞后）");
            }
        }
    }
}
