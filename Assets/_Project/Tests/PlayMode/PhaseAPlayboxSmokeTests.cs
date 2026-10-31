using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Ink;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 第一阶段试玩场景的运行时冒烟测试：真实加载 PhaseA_Playbox，验证涂地、移动/潜行、
    /// 主武器开火、延时手雷与大招砸地在运行时的实际行为（批处理可跑，用于替代人眼截图）。
    /// </summary>
    public class PhaseAPlayboxSmokeTests
    {
        const string SceneName = "PhaseA_Playbox";

        private IEnumerator LoadPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            Assert.IsNotNull(PlayerController.LocalPlayer, "场景里应存在本地玩家 PlayerController");
            Assert.IsNotNull(InkSurfaceController.Instance, "场景里应存在 InkSurfaceController");
        }

        [UnityTest]
        public IEnumerator Scene_LoadsWithManagersPlayerAndInkGrid()
        {
            yield return LoadPlaybox();

            var surface = InkSurfaceController.Instance;
            Assert.AreEqual(400, surface.Grid.Width, "桌面端墨水网格应为 400x400");
            Assert.Greater(surface.Grid.PaintableCount, 0);
            Assert.Less(surface.Grid.PaintableCount, 400 * 400, "掩体与出生台应从可涂面积中排除");
            Assert.IsNotNull(surface.InkTexture);

            var player = PlayerController.LocalPlayer;
            Assert.IsTrue(player.Health.IsAlive);
            Assert.AreEqual(Team.TeamA, player.Health.Team);
        }

        [UnityTest]
        public IEnumerator PaintingCircle_IncreasesCoverageAndMarksCells()
        {
            yield return LoadPlaybox();

            var surface = InkSurfaceController.Instance;
            float before = surface.GetCoveragePercent(Team.TeamA);

            int changed = surface.PaintCircle(new Vector3(0f, 0f, 0f), 3.0f, Team.TeamA);
            yield return null;

            Assert.Greater(changed, 0, "在空地涂色应产生归属变化");
            Assert.Greater(surface.GetCoveragePercent(Team.TeamA), before);
            Assert.AreEqual(Team.TeamA, surface.InkAt(new Vector3(0f, 0f, 0f)));
        }

        [UnityTest]
        public IEnumerator Player_FiresMainWeapon_ConsumesInkAndPaintsGround()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            player.enabled = false; // 关闭输入驱动，避免真实输入干扰
            yield return null;

            var motor = player.Motor;
            float inkBefore = motor.CurrentInk;
            float coverageBefore = InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA);

            bool fired = player.Weapon.TryFireMainWeapon(Vector3.forward);
            Assert.IsTrue(fired, "墨水充足时应能开火");
            Assert.Less(motor.CurrentInk, inkBefore, "开火应消耗墨水");

            // 等待弹丸飞行并沿途滴落/落点爆斑
            yield return new WaitForSeconds(0.8f);

            Assert.Greater(InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA), coverageBefore,
                "弹丸应在沿途与落点涂出墨水");
        }

        [UnityTest]
        public IEnumerator Player_SwimsOnOwnInk_AndGetsSpeedAndRefill()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            player.enabled = false;
            yield return null;

            var motor = player.Motor;

            // 出生台按设计不可涂，先把角色移到旁边可涂地面并铺一片己方墨水
            var paintSpot = new Vector3(0f, 0.1f, -11f);
            InkSurfaceController.Instance.PaintCircle(paintSpot, 2.5f, Team.TeamA);

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = paintSpot;
            if (cc != null) cc.enabled = true;
            yield return null;

            Assert.AreEqual(Team.TeamA, motor.FootInkTeam, "应站在己方墨水上");
            Assert.IsFalse(motor.IsSwimming);

            motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
            yield return null;

            Assert.IsTrue(motor.IsSwimming, "踩在己方墨水并按住潜行键时应进入潜行形态");
            Assert.AreEqual(11.8f, motor.CurrentMoveSpeed, 0.01f, "潜行移速应为 11.8 m/s（P2-A 对齐 Inkwave swimSpeed）");

            // 松开潜行键应立即恢复人形
            motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
            yield return null;
            Assert.IsFalse(motor.IsSwimming);
        }

        [UnityTest]
        public IEnumerator Player_SwimAndFireSwapWhileHoldingBoth_WorksBothWays()
        {
            yield return LoadPlaybox();

            var match = MatchManager.Instance;
            // 压缩 3s 倒计时：本用例只验证 Playing 下的操作互换
            PlayModeTestKit.ShortenMatchTimers();
            match.StartMatch();
            yield return PlayModeTestKit.WaitForPhase(match, MatchManager.Phase.Playing, 6f);
            Assert.IsTrue(MatchManager.InputAllowed, "比赛开始后应允许玩家输入");

            var player = PlayerController.LocalPlayer;
            player.Health.IsInvulnerable = true; // 避免测试期间被 Bot 击杀打断

            // 把角色放到己方墨水可涂地面上
            var motor = player.Motor;
            var paintSpot = new Vector3(0f, 0.1f, -11f);
            InkSurfaceController.Instance.PaintCircle(paintSpot, 2.5f, Team.TeamA);
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = paintSpot;
            if (cc != null) cc.enabled = true;
            yield return null;
            yield return null;
            Assert.AreEqual(Team.TeamA, motor.FootInkTeam, "应站在己方墨水上");

            // 1. 按住射击 → 应处于人形开火状态
            player.TouchFireHeld = true;
            yield return null;
            yield return null;
            Assert.IsFalse(motor.IsSwimming, "只按射击时应保持人形");

            // 2. 射击中按潜行 → 立即停火潜入
            player.TouchSwimHeld = true;
            yield return null;
            yield return null;
            Assert.IsTrue(motor.IsSwimming, "射击中按潜行键应立即进入潜行");

            // 3. 松开潜行、仍按住射击 → 回到人形并继续开火
            player.TouchSwimHeld = false;
            yield return null;
            Assert.IsFalse(motor.IsSwimming, "松开潜行键应立即恢复人形");

            // 4. 重新按住潜行 → 再次潜入
            player.TouchSwimHeld = true;
            yield return null;
            yield return null;
            Assert.IsTrue(motor.IsSwimming, "按住潜行键应再次进入潜行");

            // 5. 松开射击后仍按住潜行 → 保持潜行；再按射击 → 立即出枪并扣墨
            player.TouchFireHeld = false;
            yield return null;
            yield return null;
            Assert.IsTrue(motor.IsSwimming, "松开射击后按住潜行键应保持潜行");

            // P2-A 丝滑进出水：按开火当帧立刻恢复人形；第一发经 0.07s 出水过渡后被缓冲补射（绝不吞键）
            float inkBefore = motor.CurrentInk;
            player.TouchFireHeld = true;
            yield return null;
            Assert.IsFalse(motor.IsSwimming, "潜行中按射击应立刻恢复人形");

            yield return new WaitForSeconds(0.15f);
            Assert.Less(motor.CurrentInk, inkBefore, "出水过渡结束后按键应被补射并消耗墨水（Fire Buffer 不吞键）");

            player.TouchFireHeld = false;
            player.TouchSwimHeld = false;
        }

        [UnityTest]
        public IEnumerator Player_ThrowBomb_ConsumesInkAndLeavesBobmInFlight()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            player.enabled = false;
            yield return null;

            var motor = player.Motor;
            motor.RefillInkFull();
            float inkBefore = motor.CurrentInk;

            bool thrown = player.Weapon.TryThrowSubBomb(player.transform.position + Vector3.forward * 5f);
            Assert.IsTrue(thrown, "墨水充足时应能投掷手雷");
            Assert.AreEqual(inkBefore - 70f, motor.CurrentInk, 0.5f, "P3-B：手雷应消耗 70 点墨水（70%）");

            yield return new WaitForSeconds(0.4f);
        }

        [UnityTest]
        public IEnumerator Player_TouchBombHoldRelease_ThrowsViaArbiter()
        {
            yield return LoadPlaybox();

            // 本用例驱动 PlayerController.Update 内的仲裁链路，必须处于 Playing 相位
            // （Ready 下 InputAllowed=false，Update 整帧早退，仲裁不会 tick）
            var match = MatchManager.Instance;
            PlayModeTestKit.ShortenMatchTimers();
            match.StartMatch();
            yield return PlayModeTestKit.WaitForPhase(match, MatchManager.Phase.Playing, 6f);

            // 编辑器里触屏 UI 可能被序列化显示：它会每帧覆写 TouchBombHeld，先关掉
            var touchUi = Object.FindFirstObjectByType<P1.UI.MobileTouchUI>();
            if (touchUi != null) touchUi.enabled = false;

            var player = PlayerController.LocalPlayer;
            player.Health.IsInvulnerable = true; // 避免测试期间被 Bot 击杀打断
            var motor = player.Motor;
            motor.RefillInkFull();
            float inkBefore = motor.CurrentInk;

            // 1. 长按（TouchBombHeld）→ 进入瞄准态，期间不得扣墨投掷（旧「按下即投」bug 回归）
            player.TouchBombHeld = true;
            yield return null;
            yield return null;
            Assert.IsTrue(player.IsAimingBomb, "长按副武器键应进入瞄准态");
            Assert.AreEqual(inkBefore, motor.CurrentInk, 0.01f, "按住瞄准期间不得扣墨投掷");

            // 2. 触屏松手注入（TouchBombTriggered，pointer-up 语义）→ 投掷一次并扣 70 墨
            player.TouchBombTriggered = true;
            yield return null;
            Assert.IsFalse(player.IsAimingBomb, "松手后应退出瞄准态");
            Assert.AreEqual(inkBefore - 70f, motor.CurrentInk, 0.5f, "松手投掷应恰好消耗 70 点墨水（只投一颗）");

            player.TouchBombHeld = false;
        }

        [UnityTest]
        public IEnumerator Player_BombBallisticThrow_ArmsFuseAndPaintsOnDetonation()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            player.enabled = false; // 关闭输入驱动，避免真实输入干扰
            yield return null;

            var motor = player.Motor;
            motor.RefillInkFull();
            float inkBefore = motor.CurrentInk;
            float coverageBefore = InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA);

            // inkwave 弹道模型：平抛初速（相机俯仰 0°）从出手点真实抛出，远近由俯仰物理决定
            var cfg = player.Weapon.Config;
            Vector3 muzzle = player.Weapon.GetSubBombMuzzlePosition();
            Vector3 velocity = InkBombTrajectoryUtility.ComputeThrowVelocity(
                player.transform.forward, 0f, Vector3.zero, cfg);

            // 预览仿真与实飞同参数：应能在窗口内落到前方地面
            var preview = new Vector3[InkBombTrajectoryUtility.TrajectoryPointCount];
            bool landed = InkBombTrajectoryUtility.SimulateThrow(
                muzzle, velocity, cfg, player.transform, preview, out Vector3 hitPoint);
            Assert.IsTrue(landed, "平抛初速应能在预览窗口内落到地面");
            Assert.Less(hitPoint.y, 1.5f, "预测落点应接近地面");
            Assert.Greater(hitPoint.z, muzzle.z + 3f, "平抛落点应在出手点前方数米，而不是贴脚");

            bool thrown = player.Weapon.TryThrowSubBombBallistic(velocity);
            Assert.IsTrue(thrown, "墨水充足时应能弹道投掷");
            Assert.AreEqual(inkBefore - 70f, motor.CurrentInk, 0.5f, "弹道投掷应恰好消耗 70 点墨水");

            // 飞行（约 0.6s）+ 引信 0.95s + 余量 → 爆炸涂地
            yield return new WaitForSeconds(2.6f);
            Assert.Greater(InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA), coverageBefore,
                "手雷弹道飞行落地引信后应爆炸涂出墨水");
        }

        [UnityTest]
        public IEnumerator Player_Special_ChargesAndSplatsLargeArea()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            player.enabled = false;
            yield return null;

            var weapon = player.Weapon;
            Assert.IsFalse(weapon.IsSpecialReady, "开局大招不应就绪");

            weapon.AddSpecialPoints(500);
            Assert.IsTrue(weapon.IsSpecialReady, "充能后大招应就绪");

            float coverageBefore = InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA);
            bool activated = weapon.TryActivateSpecial();
            Assert.IsTrue(activated, "大招就绪时应能释放");

            // P3-B：升空 0.55s + 滞空 0.25s + 下砸 0.1s 后落地砸出大范围墨水
            yield return new WaitForSeconds(1.3f);

            Assert.Greater(InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA), coverageBefore + 0.5f,
                "大招落地应在身边涂出大范围墨水");
            Assert.IsFalse(weapon.IsPerformingSpecial, "落地后应结束大招状态");
        }

        [UnityTest]
        public IEnumerator Player_Jump_LeavesGroundAndLandsSafely()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            player.enabled = false;
            yield return null;

            var motor = player.Motor;
            float initialY = player.transform.position.y;
            Assert.IsTrue(motor.IsGrounded, "起跳前应在地面上");

            // 触发跳跃
            motor.SetFrameIntent(Vector3.forward, Vector3.forward, false, true, false);
            yield return null;

            // 等待 0.15 秒，应跃升至空中较高位置
            yield return new WaitForSeconds(0.15f);
            Assert.Greater(player.transform.position.y, initialY + 0.35f, "起跳后角色应明显腾空上升");

            // 等待下落着地（单次跳跃周期约 0.65s）；低帧率下多给一点收敛时间
            yield return new WaitForSeconds(0.65f);
            float landDeadline = Time.time + 1.5f;
            while (!motor.IsGrounded && Time.time < landDeadline)
            {
                yield return null;
            }

            Assert.Less(player.transform.position.y, initialY + 0.15f, "跳跃结束应在重力作用下重新平稳着地");
            Assert.IsTrue(motor.IsGrounded, "着地后应恢复接地判定");
        }
    }
}
