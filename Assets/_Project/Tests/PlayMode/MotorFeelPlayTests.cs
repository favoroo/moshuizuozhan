using System.Collections;
using NUnit.Framework;
using P1.AI;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// [P29] 移动手感回归（参考 INKWAVE actor.js 的 S 曲线加减速 + 跳跃缓冲/coyote + 贴地吸附）：
    /// 1. 起步不是一帧满速，松手不是一帧静止（保留惯性滑行）；
    /// 2. 反向输入先踩地制动再反向；
    /// 3. 落地前一瞬按下的跳跃在触地帧触发（jump buffer）；
    /// 4. 运行时收紧 CharacterController.skinWidth（贴地，不再整体悬浮）。
    /// 只读 motor 公共状态（PlanarVelocity / VerticalVelocity），不依赖具体位移路径，低帧率批处理下稳定。
    /// </summary>
    public class MotorFeelPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        private IEnumerator LoadPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景里应存在本地玩家 PlayerController");

            player.enabled = false; // 输入交给测试注入

            var bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
            for (int i = 0; i < bots.Length; i++)
            {
                bots[i].enabled = false;
                var botMotor = bots[i].GetComponent<InkCharacterMotor>();
                if (botMotor != null) botMotor.enabled = false;
                var botWeapon = bots[i].GetComponent<WeaponController>();
                if (botWeapon != null) botWeapon.enabled = false;
            }

            yield return null;
        }

        /// <summary>传送到中立地面并等待落地稳定（沿用 MotorLifecyclePlayTests 验证过的坐标）。</summary>
        private static IEnumerator SettleOnGround(PlayerController player, Vector3 spot)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = spot;
            if (cc != null) cc.enabled = true;

            var motor = player.Motor;
            for (int i = 0; i < 8; i++)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Motor_AcceleratesAndGlides_InsteadOfInstantStartStop()
        {
            yield return LoadPlaybox();
            var player = PlayerController.LocalPlayer;
            var motor = player.Motor;

            yield return SettleOnGround(player, new Vector3(10f, 0.4f, 10f));
            Assert.IsTrue(motor.IsGrounded, "测试起点必须站在地面上");

            // P30 贴地：skinWidth 收紧 + 视觉根按同量下移（控制器静止时根节点悬空 skinWidth）
            var cc = player.GetComponent<CharacterController>();
            Assert.AreEqual(motor.Config.controllerSkinWidth, cc.skinWidth, 0.001f,
                "Awake 应把场景烘焙的 skinWidth 收紧为配置值");
            Transform visualRoot = player.transform.Find("VisualRoot");
            Assert.IsNotNull(visualRoot, "角色根下应有 VisualRoot");
            Transform kidRoot = visualRoot.Find("KidRoot");
            Assert.IsNotNull(kidRoot, "VisualRoot 下应有 KidRoot");
            Assert.AreEqual(-cc.skinWidth, kidRoot.localPosition.y, 0.002f,
                "KidRoot 应下移 skinWidth，抵消控制器接触间隙（脚底不再悬空）");

            // P30 接触阴影：脚正下方的贴地暗斑必须存在且落在脚边地面
            Transform contactShadow = player.transform.Find(CharacterContactShadow.ObjectName);
            Assert.IsNotNull(contactShadow, "应生成接触阴影（平行光投影落在身后，脚下需要贴地暗斑）");
            var shadowRenderer = contactShadow.GetComponent<Renderer>();
            Assert.IsNotNull(shadowRenderer, "接触阴影必须有 Renderer");
            Assert.IsTrue(shadowRenderer.enabled, "站在地面时接触阴影必须可见");
            Assert.Less(Mathf.Abs(contactShadow.position.y), 0.05f,
                $"接触阴影必须贴在脚下地面（实际 y={contactShadow.position.y:F3}）");

            // 起步：第一帧速度远小于满速 6m/s（旧版一帧到位）
            motor.SetFrameIntent(Vector3.forward, Vector3.forward, false, false);
            yield return null;
            float firstFrameSpeed = motor.PlanarVelocity.magnitude;
            Assert.Less(firstFrameSpeed, 4.5f,
                $"起步不应一帧满速（首帧 {firstFrameSpeed:F2} m/s，S 曲线加速应明显低于 6）");

            // 持续加速 0.35s 后应接近满速
            float accelStart = Time.time;
            while (Time.time - accelStart < 0.35f)
            {
                motor.SetFrameIntent(Vector3.forward, Vector3.forward, false, false);
                yield return null;
            }
            float cruiseSpeed = motor.PlanarVelocity.magnitude;
            Assert.That(cruiseSpeed, Is.InRange(5.0f, 6.2f), $"持续输入应达到约 6m/s（实际 {cruiseSpeed:F2}）");

            // 松手：不应一帧静止（保留惯性滑停）
            motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
            yield return null;
            float releaseSpeed = motor.PlanarVelocity.magnitude;
            Assert.Greater(releaseSpeed, 3f, $"松手后应保留惯性（首帧仍有 {releaseSpeed:F2} m/s）");

            float glideStart = Time.time;
            while (Time.time - glideStart < 1.0f && motor.PlanarVelocity.magnitude > 0.05f)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
                yield return null;
            }
            Assert.Less(motor.PlanarVelocity.magnitude, 0.2f, "刹车减速度应在 1s 内完全滑停");

            // 反向输入：先制动到零再反向（不应瞬间满速反向）
            motor.SetFrameIntent(Vector3.forward, Vector3.forward, false, false);
            for (int i = 0; i < 20; i++)
            {
                motor.SetFrameIntent(Vector3.forward, Vector3.forward, false, false);
                yield return null;
            }
            Assert.Greater(motor.PlanarVelocity.z, 3f, "前置条件：应已向前满速");
            motor.SetFrameIntent(Vector3.back, Vector3.forward, false, false);
            yield return null;
            Assert.Greater(motor.PlanarVelocity.z, 0f, "反向首帧不应瞬间反向（先踩地制动）");
        }

        [UnityTest]
        public IEnumerator Motor_JumpBuffer_JumpsWhenPressedJustBeforeLanding()
        {
            yield return LoadPlaybox();
            var player = PlayerController.LocalPlayer;
            var motor = player.Motor;

            yield return SettleOnGround(player, new Vector3(10f, 0.4f, 10f));
            Assert.IsTrue(motor.IsGrounded, "测试起点必须站在地面上");

            // 运行时贴地：skinWidth 收紧到配置值
            var cc = player.GetComponent<CharacterController>();
            Assert.AreEqual(motor.Config.controllerSkinWidth, cc.skinWidth, 0.001f,
                "Awake 应把场景烘焙的 skinWidth 0.08 收紧为配置值（贴地、减少悬浮间隙）");

            float groundY = player.transform.position.y;

            // 起跳：连续几帧发送按下，抵消测试协程与 motor.Update 的执行顺序差异（SingleFrame 输入易被覆盖）
            bool airborne = false;
            for (int i = 0; i < 15 && !airborne; i++)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, true, false);
                yield return null;
                airborne = !motor.IsGrounded || motor.VerticalVelocity > 3f;
            }
            Assert.IsTrue(airborne, "按下跳跃后应离地");
            Assert.Greater(motor.VerticalVelocity, 3f, "起跳应获得向上初速");

            // 下落末段：预测触地时间 < 0.1s 时连续按 2 帧跳跃（落在 0.12s 缓冲窗口内）
            bool pressed = false;
            float deadline = Time.time + 4f;
            while (!pressed && Time.time < deadline)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
                yield return null;

                float height = player.transform.position.y - groundY;
                float vy = motor.VerticalVelocity;
                if (!motor.IsGrounded && vy < -1f && height > 0f && height / -vy < 0.10f)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, true, false);
                        yield return null;
                    }
                    pressed = true;
                }
            }
            Assert.IsTrue(pressed, "应在落地前 0.10s 内按下跳跃（测试时序）");

            // 落地缓冲：触地帧应立刻再次起跳（不是被吞键）
            bool buffered = false;
            deadline = Time.time + 1.5f;
            while (!buffered && Time.time < deadline)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
                yield return null;
                if (motor.VerticalVelocity > 3f)
                {
                    buffered = true;
                }
            }
            Assert.IsTrue(buffered, "落地前缓冲的跳跃必须在触地帧触发（jump buffer）");
        }

        /// <summary>
        /// 加特林扫射阶段（扳机已松开、弹幕仍在持续倾泻）身体必须继续面向准星：
        /// 对齐 INKWAVE weapons.js firingPose() + actor.js _face（蓄力/扫射期间 yaw 目标 = aimYaw，
        /// 且扫射每帧刷新 fireFacing）。旧实现只在按住扳机的帧刷新保持计时，
        /// 松手 0.4s 后身体转回移动方向，出现「枪口朝后、子弹朝前」的错位。
        /// </summary>
        [UnityTest]
        public IEnumerator SplatlingStream_KeepsBodyFacingAim_WhileMovingBackward()
        {
            yield return LoadPlaybox();
            var player = PlayerController.LocalPlayer;
            var motor = player.Motor;
            var weapon = player.Weapon;
            Assert.IsNotNull(weapon, "本地玩家应有 WeaponController");

            WeaponConfigSO originalConfig = weapon.Config;
            var splatlingConfig = ScriptableObject.CreateInstance<WeaponConfigSO>();
            WeaponConfigSO.ApplySplatlingSpec(splatlingConfig);
            weapon.EquipWeapon(splatlingConfig);
            yield return null;
            Assert.AreEqual(WeaponKind.Splatling, weapon.CurrentWeaponKind, "测试前置：应已装备加特林");

            yield return SettleOnGround(player, new Vector3(10f, 0.4f, 10f));
            Assert.IsTrue(motor.IsGrounded, "测试起点必须站在地面上");

            Vector3 aim = Vector3.forward;
            Vector3 moveBack = Vector3.back;

            // 1. 按住扳机蓄力 0.95s（> 0.85s 满蓄力），期间身体朝准星
            float start = Time.time;
            while (Time.time - start < 0.95f)
            {
                weapon.TryFireMainWeapon(aim);
                motor.SetFrameIntent(Vector3.zero, aim, false, false, true);
                yield return null;
            }
            Assert.IsTrue(weapon.IsCharging, "按住扳机 0.95s 应处于蓄力中");
            Assert.IsTrue(weapon.WantsAimFacing, "蓄力中武器应要求身体朝准星");

            // 2. 松开扳机进入扫射，同时持续朝准星反方向后退：身体必须保持朝准星
            start = Time.time;
            while (Time.time - start < 0.8f)
            {
                motor.SetFrameIntent(moveBack, aim, false, false, false);
                yield return null;
            }

            Assert.IsTrue(weapon.IsStreaming, "满蓄力扫射持续 1.7s，0.8s 时应仍在倾泻弹幕");
            Assert.IsTrue(weapon.WantsAimFacing, "扫射中（扳机已松开）仍应要求身体朝准星");
            float angle = Vector3.Angle(player.transform.forward, aim);
            Assert.Less(angle, 25f, $"扫射中后退移动时身体仍应面向准星（实际偏离 {angle:F1}°）");

            // 收尾：先把武器换回原配置再销毁临时配置，避免场景里的 WeaponController 继续引用已销毁的 SO
            weapon.EquipWeapon(originalConfig);
            Object.Destroy(splatlingConfig);
        }

        /// <summary>
        /// 双枪「翻滚 → 架枪强化」阶段不得锁住移动（P40 修正）：
        /// 旧实现架枪 0.5s 内 <c>MoveSpeedMultiplier = 0</c>（照搬 INKWAVE <c>lockT</c> 的原地站桩），
        /// 翻滚完想立刻脱身却被钉在原点，操作反馈极差；现在只有射速 +20% / 散布清零与
        /// 表现层姿态（准星锁定框、躯干下沉）保留，移动立刻恢复可控。
        /// </summary>
        [UnityTest]
        public IEnumerator DualiesTurretStance_DoesNotFreezeMovement_AfterRoll()
        {
            yield return LoadPlaybox();
            var player = PlayerController.LocalPlayer;
            var motor = player.Motor;
            var weapon = player.Weapon;
            Assert.IsNotNull(weapon, "本地玩家应有 WeaponController");

            WeaponConfigSO originalConfig = weapon.Config;
            var dualiesConfig = ScriptableObject.CreateInstance<WeaponConfigSO>();
            WeaponConfigSO.ApplyDualiesSpec(dualiesConfig);
            weapon.EquipWeapon(dualiesConfig);
            yield return null;
            Assert.AreEqual(WeaponKind.Dualies, weapon.CurrentWeaponKind, "测试前置：应已装备双枪");

            yield return SettleOnGround(player, new Vector3(10f, 0.4f, 10f));
            Vector3 aim = Vector3.forward;

            // 先朝准星站定，建立基准朝向（翻滚前的普通朝向）
            for (int i = 0; i < 10; i++)
            {
                motor.SetFrameIntent(Vector3.zero, aim, false, false, false);
                yield return null;
            }

            Assert.IsTrue(weapon.TryDodgeRoll(Vector3.forward), "应能触发双枪战术翻滚");

            // 推进到架枪强化窗口（翻滚 0.3s 内持续朝准星反方向输入移动）
            float start = Time.time;
            while (!weapon.IsTurretLocked && Time.time - start < 2f)
            {
                motor.SetFrameIntent(Vector3.back, aim, false, false, false);
                yield return null;
            }
            Assert.IsTrue(weapon.IsTurretLocked, "翻滚结束应进入架枪强化状态");

            // 架枪窗口内持续反向移动：移速不得归零，位移必须真实发生
            float maxSpeed = 0f;
            Vector3 from = player.transform.position;
            while (weapon.IsTurretLocked && Time.time - start < 3f)
            {
                motor.SetFrameIntent(Vector3.back, aim, false, false, false);
                yield return null;
                maxSpeed = Mathf.Max(maxSpeed, motor.CurrentMoveSpeed);
            }

            Assert.Greater(maxSpeed, 2f, "架枪强化期间移速不得被锁零（旧实现恒为 0，翻滚后被钉在原地）");
            float moved = Vector3.Distance(from, player.transform.position);
            Assert.Greater(moved, 0.2f, $"架枪强化期间移动输入应真实带走角色（实际位移 {moved:F2}m）");
            weapon.EquipWeapon(originalConfig);
            Object.Destroy(dualiesConfig);
        }

        /// <summary>
        /// 外部瞄准姿态请求（副武器手雷按住瞄准：预览弹道与落点按准星解算）：
        /// 对齐 INKWAVE weapons.js <c>aimingSub → a.fireFacing = 0.3</c>，瞄准期间身体朝准星，
        /// 即使同时有反向移动输入也不该把身体转走。
        /// </summary>
        [UnityTest]
        public IEnumerator AimFacingRequest_KeepsBodyFacingAim_WhileMovingBackward()
        {
            yield return LoadPlaybox();
            var player = PlayerController.LocalPlayer;
            var motor = player.Motor;

            yield return SettleOnGround(player, new Vector3(10f, 0.4f, 10f));
            Vector3 aim = Vector3.forward;

            for (int i = 0; i < 10; i++)
            {
                motor.SetFrameIntent(Vector3.zero, aim, false, false, false);
                yield return null;
            }

            for (int i = 0; i < 25; i++)
            {
                motor.SetFrameIntent(Vector3.back, aim, false, false, false, false, true);
                yield return null;
            }

            Assert.IsTrue(motor.IsAimFacingRequested, "外部瞄准请求应被记录（供调试/测试读取）");
            float angle = Vector3.Angle(player.transform.forward, aim);
            Assert.Less(angle, 25f, $"手雷瞄准中后退移动时身体仍应面向准星（实际偏离 {angle:F1}°）");
        }
    }
}