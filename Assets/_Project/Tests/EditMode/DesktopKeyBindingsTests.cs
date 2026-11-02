using NUnit.Framework;
using P1.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace P1.Tests
{
    /// <summary>
    /// 桌面端按键自定义绑定纯逻辑测试（EditMode）：
    /// 验证 9 项动作默认值、键位友好文本转换、改键冲突互换、清空备用键、重置默认及 PlayerPrefs 持久化。
    /// </summary>
    public class DesktopKeyBindingsTests
    {
        [SetUp]
        public void SetUp()
        {
            DesktopKeyBindingService.ResetToDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(DesktopKeyBindingService.StorageKey);
            PlayerPrefs.Save();
            DesktopKeyBindingService.ResetToDefaults();
        }

        [Test]
        public void DefaultConfig_HasAllNineActionsWithCorrectDefaults()
        {
            var config = DesktopKeyBindingService.CreateDefaultConfig();
            Assert.AreEqual(9, config.bindings.Count, "默认必须包含 9 项核心操作动作");

            var fwd = config.Find(DesktopAction.MoveForward);
            Assert.AreEqual(Key.W, fwd.primary.key);
            Assert.AreEqual(Key.UpArrow, fwd.secondary.key);

            var back = config.Find(DesktopAction.MoveBackward);
            Assert.AreEqual(Key.S, back.primary.key);
            Assert.AreEqual(Key.DownArrow, back.secondary.key);

            var left = config.Find(DesktopAction.MoveLeft);
            Assert.AreEqual(Key.A, left.primary.key);
            Assert.AreEqual(Key.LeftArrow, left.secondary.key);

            var right = config.Find(DesktopAction.MoveRight);
            Assert.AreEqual(Key.D, right.primary.key);
            Assert.AreEqual(Key.RightArrow, right.secondary.key);

            var jump = config.Find(DesktopAction.Jump);
            Assert.AreEqual(Key.Space, jump.primary.key);
            Assert.IsTrue(jump.secondary.IsEmpty);

            var fire = config.Find(DesktopAction.Fire);
            Assert.AreEqual(DesktopBindingDevice.Mouse, fire.primary.device);
            Assert.AreEqual(0, fire.primary.mouseButton);

            var swim = config.Find(DesktopAction.Swim);
            Assert.AreEqual(DesktopBindingDevice.Mouse, swim.primary.device);
            Assert.AreEqual(1, swim.primary.mouseButton);
            Assert.AreEqual(Key.LeftShift, swim.secondary.key);

            var sub = config.Find(DesktopAction.SubWeapon);
            Assert.AreEqual(Key.E, sub.primary.key);
            Assert.AreEqual(DesktopBindingDevice.Mouse, sub.secondary.device);
            Assert.AreEqual(2, sub.secondary.mouseButton);

            var sp = config.Find(DesktopAction.Special);
            Assert.AreEqual(Key.Q, sp.primary.key);
            Assert.IsTrue(sp.secondary.IsEmpty);
        }

        [Test]
        public void DisplayNames_AreDescriptiveAndFormatted()
        {
            Assert.AreEqual("W", DesktopBindingSlot.FromKey(Key.W).GetDisplayName());
            Assert.AreEqual("空格", DesktopBindingSlot.FromKey(Key.Space).GetDisplayName());
            Assert.AreEqual("左 Shift", DesktopBindingSlot.FromKey(Key.LeftShift).GetDisplayName());
            Assert.AreEqual("鼠标左键", DesktopBindingSlot.FromMouse(0).GetDisplayName());
            Assert.AreEqual("鼠标右键", DesktopBindingSlot.FromMouse(1).GetDisplayName());
            Assert.AreEqual("鼠标中键", DesktopBindingSlot.FromMouse(2).GetDisplayName());
            Assert.AreEqual("-", DesktopBindingSlot.None.GetDisplayName());
        }

        [Test]
        public void TryBind_ChangesKeyAndPersists()
        {
            bool success = DesktopKeyBindingService.TryBind(
                DesktopAction.MoveForward,
                false,
                DesktopBindingSlot.FromKey(Key.I),
                out var swapped);

            Assert.IsTrue(success);
            Assert.IsNull(swapped);

            DesktopKeyBindingService.GetBinding(DesktopAction.MoveForward, out var primary, out _);
            Assert.AreEqual(Key.I, primary.key);

            // 重新从 PlayerPrefs 读取验证持久化
            DesktopKeyBindingService.LoadFromPlayerPrefs();
            DesktopKeyBindingService.GetBinding(DesktopAction.MoveForward, out primary, out _);
            Assert.AreEqual(Key.I, primary.key);
        }

        [Test]
        public void TryBind_ConflictWithOtherPrimary_SwapsKeys()
        {
            // Jump 默认是 Space，将 Jump 改为 W（MoveForward 的主键）
            bool success = DesktopKeyBindingService.TryBind(
                DesktopAction.Jump,
                false,
                DesktopBindingSlot.FromKey(Key.W),
                out var swapped);

            Assert.IsTrue(success);
            Assert.AreEqual(DesktopAction.MoveForward, swapped, "应该报告与 MoveForward 发生了按键互换");

            DesktopKeyBindingService.GetBinding(DesktopAction.Jump, out var jumpPrimary, out _);
            DesktopKeyBindingService.GetBinding(DesktopAction.MoveForward, out var fwdPrimary, out _);

            Assert.AreEqual(Key.W, jumpPrimary.key, "Jump 主键应变为 W");
            Assert.AreEqual(Key.Space, fwdPrimary.key, "MoveForward 主键应被换为原 Jump 的 Space");
        }

        [Test]
        public void TryBind_ConflictWithSecondary_ClearsSecondary()
        {
            // Swim 默认 secondary 是 LeftShift。将 Special（默认 Q）主键改为 LeftShift
            bool success = DesktopKeyBindingService.TryBind(
                DesktopAction.Special,
                false,
                DesktopBindingSlot.FromKey(Key.LeftShift),
                out var swapped);

            Assert.IsTrue(success);
            Assert.IsNull(swapped);

            DesktopKeyBindingService.GetBinding(DesktopAction.Special, out var spPrimary, out _);
            DesktopKeyBindingService.GetBinding(DesktopAction.Swim, out _, out var swimSecondary);

            Assert.AreEqual(Key.LeftShift, spPrimary.key);
            Assert.IsTrue(swimSecondary.IsEmpty, "Swim 的备用键应该被清空以避免冲突");
        }

        [Test]
        public void ClearSecondary_SetsSecondaryToNone()
        {
            DesktopKeyBindingService.ClearSecondary(DesktopAction.Swim);
            DesktopKeyBindingService.GetBinding(DesktopAction.Swim, out _, out var secondary);
            Assert.IsTrue(secondary.IsEmpty);
        }

        [Test]
        public void ResetToDefaults_RestoresInitialKeys()
        {
            DesktopKeyBindingService.TryBind(DesktopAction.Jump, false, DesktopBindingSlot.FromKey(Key.J), out _);
            DesktopKeyBindingService.ResetToDefaults();

            DesktopKeyBindingService.GetBinding(DesktopAction.Jump, out var primary, out _);
            Assert.AreEqual(Key.Space, primary.key);
        }
    }
}
