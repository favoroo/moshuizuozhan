using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 触屏手雷真实链路测试：不关 MobileTouchUI，用 ExecuteEvents 驱动手雷按钮的
    /// pointer-down / pointer-up，回归「按住瞄准（显示弹道预览）、松手投出」。
    /// 同时验证手雷 55 墨门槛（空墨拒绝、准星弧线变灰）与被拒时的显性反馈。
    /// </summary>
    public class MobileBombTouchPlayTests
    {
        const string SceneName = "PhaseA_Playbox";

        private IEnumerator LoadPlayingMatch()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var match = MatchManager.Instance;
            PlayModeTestKit.ShortenMatchTimers();
            match.StartMatch();
            yield return PlayModeTestKit.WaitForPhase(match, MatchManager.Phase.Playing, 6f);
            Assert.IsTrue(MatchManager.InputAllowed, "比赛开始后应允许玩家输入");

            var touchUi = Object.FindFirstObjectByType<P1.UI.MobileTouchUI>();
            Assert.IsNotNull(touchUi, "场景里应存在 MobileTouchUI");
            touchUi.ShowTouchUiInEditor = true; // 编辑器里模拟触屏（真机默认显示）
            touchUi.EnsureBuilt();
            touchUi.SetVisible(true);
            yield return null;
            Assert.IsTrue(touchUi.IsShown, "触屏层必须处于显示状态，按钮事件才会注入");

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景里应存在本地玩家");
            player.Health.IsInvulnerable = true; // 避免测试期间被 Bot 击杀打断
        }

        private static Transform FindChildRecursive(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static GameObject GetBombButton(P1.UI.MobileTouchUI touchUi)
        {
            var bombGo = FindChildRecursive(touchUi.transform, "BombButton")?.gameObject;
            Assert.IsNotNull(bombGo, "触屏层里必须存在 BombButton");
            return bombGo;
        }

        /// <summary>真实 pointer down/up 序列（可选按住时长），结束后等两帧让注入事件被消费。</summary>
        private static IEnumerator PressAndRelease(GameObject bombGo, float holdSeconds)
        {
            var es = EventSystem.current;
            Assert.IsNotNull(es, "场景里应存在 EventSystem");

            var ped = new PointerEventData(es);
            ExecuteEvents.Execute(bombGo, ped, ExecuteEvents.pointerDownHandler);
            if (holdSeconds > 0f)
            {
                yield return new WaitForSeconds(holdSeconds);
            }
            ExecuteEvents.Execute(bombGo, ped, ExecuteEvents.pointerUpHandler);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TouchBombButton_Press_Spends55Ink_AndDeniesEmptyInkWithFeedback()
        {
            yield return LoadPlayingMatch();

            var touchUi = Object.FindFirstObjectByType<P1.UI.MobileTouchUI>();
            GameObject bombGo = GetBombButton(touchUi);
            var player = PlayerController.LocalPlayer;
            var motor = player.Motor;

            // 空墨：55 墨门槛在「按下」就拒绝——不进入瞄准（不出弹道预览）、不投弹，
            // 但必须复用主武器空墨反馈（EmptyFeedbackStamp +1 → 准星红叉/「弹药耗尽」toast），不做静默失败。
            motor.TryConsumeInk(motor.CurrentInk);
            Assert.Less(motor.CurrentInk, 1f, "前置：墨水应已被清空");
            Assert.IsFalse(player.Weapon.CanThrowSubBombNow, "空墨必须拒绝投掷（55 墨门槛）");

            int bombsAtEmpty = InkBombPool.Instance.ActiveBombCount;
            int emptyStampBefore = player.Weapon.EmptyFeedbackStamp;
            yield return PressAndRelease(bombGo, 0f);

            Assert.AreEqual(bombsAtEmpty, InkBombPool.Instance.ActiveBombCount, "空墨按下不得投出");
            Assert.IsFalse(player.IsAimingBomb, "空墨按下不得进入手雷瞄准态（不显示抛物线）");
            Assert.Greater(player.Weapon.EmptyFeedbackStamp, emptyStampBefore,
                "空墨按手雷键必须递增 EmptyFeedbackStamp（准星弹出「弹药耗尽」提示）");
            Assert.AreEqual(PlayerController.SubBombDenyReason.NotEnoughInk, player.LastSubBombDenyReason,
                "拒绝原因应记录为墨水不足");

            // 满墨：按下即真实投出，并扣 55 墨
            motor.RefillInkFull();
            int before = InkBombPool.Instance.ActiveBombCount;
            yield return PressAndRelease(bombGo, 0f);

            Assert.AreEqual(before + 1, InkBombPool.Instance.ActiveBombCount, "按下必须真实投出一颗手雷");
            Assert.AreEqual(45f, motor.CurrentInk, 0.5f, "投掷手雷应扣 55 墨（100 → 45）");

            // 桌面/移动端共用的准星冷却徽标：投出后 6s 内必须可见，给玩家明确的冷却反馈
            var reticle = Object.FindFirstObjectByType<P1.UI.WeaponReticleUI>();
            Assert.IsNotNull(reticle, "场景里应存在武器准星");
            Assert.Greater(player.Weapon.BombCooldownRemaining, 0f, "投出后应进入冷却");
            Assert.IsTrue(reticle.IsBombCooldownBadgeVisible, "冷却期间准星下方必须显示手雷冷却倒计时");
        }

        [UnityTest]
        public IEnumerator TouchBombButton_HoldAims_ReleaseThrowsOnce()
        {
            yield return LoadPlayingMatch();

            var touchUi = Object.FindFirstObjectByType<P1.UI.MobileTouchUI>();
            GameObject bombGo = GetBombButton(touchUi);
            var player = PlayerController.LocalPlayer;
            int before = InkBombPool.Instance.ActiveBombCount;

            var es = EventSystem.current;
            var ped = new PointerEventData(es);
            ExecuteEvents.Execute(bombGo, ped, ExecuteEvents.pointerDownHandler);
            yield return null;
            yield return null;

            Assert.AreEqual(before, InkBombPool.Instance.ActiveBombCount, "按下只进入瞄准，不得立刻投出");
            Assert.IsTrue(player.IsAimingBomb, "按住手雷键应处于瞄准态");
            Assert.IsTrue(player.TouchBombHeld, "触屏按住状态应传递给玩家");
            Assert.IsTrue(player.SubTrajectory.IsShowing, "按住期间应显示弹道预览");

            // 继续按住 0.3s：不得投出
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(before, InkBombPool.Instance.ActiveBombCount, "按住期间不得投出");
            Assert.IsTrue(player.IsAimingBomb, "按住期间保持瞄准态");

            ExecuteEvents.Execute(bombGo, ped, ExecuteEvents.pointerUpHandler);
            yield return null;
            yield return null;

            Assert.AreEqual(before + 1, InkBombPool.Instance.ActiveBombCount, "松手必须投出一颗");
            Assert.IsFalse(player.IsAimingBomb, "松开后退出瞄准态");
            Assert.IsFalse(player.SubTrajectory.IsShowing, "投出后收起弹道预览");
        }

        [UnityTest]
        public IEnumerator TouchBombButton_SecondTapDuringCooldown_IsDeniedWithFeedback()
        {
            yield return LoadPlayingMatch();

            var touchUi = Object.FindFirstObjectByType<P1.UI.MobileTouchUI>();
            GameObject bombGo = GetBombButton(touchUi);
            var player = PlayerController.LocalPlayer;

            int before = InkBombPool.Instance.ActiveBombCount;
            int stampBefore = player.SubBombDenyStamp;

            yield return PressAndRelease(bombGo, 0f);
            Assert.AreEqual(before + 1, InkBombPool.Instance.ActiveBombCount, "第一发必须成功投出");

            // 补满墨，把失败原因隔离到「冷却」上（墨水不足会先被 55 墨门槛拦下）
            player.Motor.RefillInkFull();

            // 冷却期内立刻再投：必须被拒且给显性反馈（拒绝戳递增驱动按钮闪红），不再静默
            yield return PressAndRelease(bombGo, 0f);
            Assert.AreEqual(before + 1, InkBombPool.Instance.ActiveBombCount, "冷却期内不得投出第二颗");
            Assert.Greater(player.SubBombDenyStamp, stampBefore, "冷却拒绝必须递增 SubBombDenyStamp");
            Assert.AreEqual(PlayerController.SubBombDenyReason.Cooldown, player.LastSubBombDenyReason);
        }
    }
}
