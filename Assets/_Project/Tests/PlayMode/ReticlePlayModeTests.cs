using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using P1.Weapons;
using UnityEngine;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// [P2-C] 运行时 PlayMode 验证：
    /// 1. 副武器 64 点平滑抛物线虚线与 2.7m 爆炸红圈实时渲染与精确落点；
    /// 2. 地面准星在小枪、滚筒、蓄力狙之间平滑跨形态过渡与散布角驱动；
    /// 3. 屏幕专属准星 UI (WeaponReticleUI) 屏幕投影定位与副武器微胶囊状态响应。
    /// </summary>
    [Category("UI")]
    public class ReticlePlayModeTests
    {
        [UnityTest]
        public IEnumerator SubWeaponTrajectory_Runtime_UpdatesAndHidesSmoothly()
        {
            var hostGo = new GameObject("TestTrajectoryHost");
            var trajectory = hostGo.AddComponent<SubWeaponThrowTrajectory>();

            var cfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            cfg.bombMaxThrowDistance = 9f;
            cfg.bombArcHeight = 2.6f;
            cfg.bombSplatRadius = 2.7f;

            // 放在高空：只验证弹道数学与渲染，不依赖当前地图在该位置是否有掩体。
            // （地面射线会贴合 y，水平落点仍应精确等于瞄准点 x=7。）
            Vector3 startPos = new Vector3(0f, 50f, 0f);
            Vector3 aimPos = new Vector3(7f, 49f, 0f);

            // 驱动 3 帧渲染
            for (int i = 0; i < 3; i++)
            {
                trajectory.UpdateTrajectory(startPos, aimPos, cfg, true, Team.TeamA);
                yield return null;
            }

            Assert.IsTrue(trajectory.IsShowing);
            Assert.AreEqual(7f, trajectory.CurrentLandingPosition.x, 0.05f);

            var line = hostGo.GetComponentInChildren<LineRenderer>();
            Assert.IsNotNull(line);
            Assert.AreEqual(64, line.positionCount);
            Assert.IsTrue(line.gameObject.activeInHierarchy);

            trajectory.Hide();
            yield return null;

            Assert.IsFalse(trajectory.IsShowing);
            Assert.IsFalse(line.gameObject.activeInHierarchy);

            Object.Destroy(hostGo);
            Object.Destroy(cfg);
        }

        [UnityTest]
        public IEnumerator GroundAimReticle_Runtime_MorphsVisualsAcrossWeapons()
        {
            var reticleGo = new GameObject("TestGroundReticle");
            var reticle = reticleGo.AddComponent<GroundAimReticle>();

            var playerGo = new GameObject("TestPlayer");
            var motor = playerGo.AddComponent<InkCharacterMotor>();
            var health = playerGo.AddComponent<CombatantHealth>();
            var weapon = playerGo.AddComponent<WeaponController>();

            Vector3 aimPos = new Vector3(5f, 0f, 0f);

            // 1. 小枪形态更新 5 帧，权重平滑上升
            for (int i = 0; i < 5; i++)
            {
                reticle.UpdateReticle(aimPos, playerGo.transform.position, weapon, motor, health);
                yield return null;
            }

            Assert.AreEqual(aimPos.x, reticleGo.transform.position.x, 0.01f);
            Assert.AreEqual(aimPos.z, reticleGo.transform.position.z, 0.01f);

            // 2. 装备滚筒并更新
            var rollerCfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            rollerCfg.weaponKind = WeaponKind.Roller;
            rollerCfg.rollWidth = 1.9f;
            weapon.EquipWeapon(rollerCfg);

            for (int i = 0; i < 10; i++)
            {
                reticle.UpdateReticle(aimPos, playerGo.transform.position, weapon, motor, health);
                yield return null;
            }

            // 3. 装备蓄力狙并更新
            var chargerCfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            chargerCfg.weaponKind = WeaponKind.Charger;
            weapon.EquipWeapon(chargerCfg);

            for (int i = 0; i < 10; i++)
            {
                reticle.UpdateReticle(aimPos, playerGo.transform.position, weapon, motor, health);
                yield return null;
            }

            Object.Destroy(reticleGo);
            Object.Destroy(playerGo);
            Object.Destroy(rollerCfg);
            Object.Destroy(chargerCfg);
        }

        [UnityTest]
        public IEnumerator WeaponReticleUI_Runtime_RendersContainers()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var reticleGo = new GameObject("TestReticleUI", typeof(RectTransform));
            reticleGo.transform.SetParent(canvasGo.transform, false);
            var reticleUI = reticleGo.AddComponent<WeaponReticleUI>();
            reticleUI.EnsureInitialized();

            yield return null;

            Assert.IsNotNull(reticleGo.transform.Find("ShooterReticle"));
            Assert.IsNotNull(reticleGo.transform.Find("RollerReticle"));
            Assert.IsNotNull(reticleGo.transform.Find("ChargerReticle"));
            Assert.IsNotNull(reticleGo.transform.Find("SubAimBadge"));

            Object.Destroy(canvasGo);
        }

        /// <summary>
        /// 回归：7 把主武器每一把都必须真的显示准星。
        /// 旧实现屏幕准星只比较 Shooter/Roller/Charger 三种 kind，
        /// 双枪 / 泼桶 / 爆破枪 / 加特林四把武器的三个容器权重全为 0，屏幕上一个准星都没有。
        /// </summary>
        [UnityTest]
        public IEnumerator WeaponReticleUI_EveryWeaponKind_ShowsAContainer()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var reticleGo = new GameObject("TestReticleUI", typeof(RectTransform));
            reticleGo.transform.SetParent(canvasGo.transform, false);
            var reticleUI = reticleGo.AddComponent<WeaponReticleUI>();
            reticleUI.EnsureInitialized();

            var playerGo = new GameObject("TestLocalPlayer",
                typeof(CharacterController),
                typeof(CombatantHealth),
                typeof(InkCharacterMotor),
                typeof(WeaponController),
                typeof(PlayerController));
            var player = playerGo.GetComponent<PlayerController>();
            player.Health.Configure(Team.TeamA, null);
            var weapon = playerGo.GetComponent<WeaponController>();

            yield return null;

            Transform shooter = reticleGo.transform.Find("ShooterReticle");
            Transform roller = reticleGo.transform.Find("RollerReticle");
            Transform charger = reticleGo.transform.Find("ChargerReticle");
            Assert.IsNotNull(shooter);
            Assert.IsNotNull(roller);
            Assert.IsNotNull(charger);

            WeaponKind[] kinds =
            {
                WeaponKind.Shooter,
                WeaponKind.Roller,
                WeaponKind.Charger,
                WeaponKind.Dualies,
                WeaponKind.Slosher,
                WeaponKind.Blaster,
                WeaponKind.Splatling
            };

            for (int i = 0; i < kinds.Length; i++)
            {
                WeaponKind kind = kinds[i];
                var cfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
                cfg.weaponKind = kind;
                weapon.EquipWeapon(cfg);

                // 形态权重以 dt*10 的速度淡入，给足 20 帧让权重从 0 走到 1
                for (int f = 0; f < 20; f++)
                    yield return null;

                ReticleShape expected = ReticleStateMath.ShapeFor(kind);
                bool anyVisible = shooter.gameObject.activeSelf
                                  || roller.gameObject.activeSelf
                                  || charger.gameObject.activeSelf;

                Assert.IsTrue(anyVisible, $"{kind} 必须至少显示一个准星容器（期望形状 {expected}）");

                bool expectedVisible = expected == ReticleShape.Shooter ? shooter.gameObject.activeSelf
                    : expected == ReticleShape.Roller ? roller.gameObject.activeSelf
                    : charger.gameObject.activeSelf;
                Assert.IsTrue(expectedVisible, $"{kind} 应显示 {expected} 形态的准星");

                Object.Destroy(cfg);
            }

            Object.Destroy(playerGo);
            Object.Destroy(canvasGo);
        }

        /// <summary>
        /// P36 空墨反馈：墨水跌到「下一发门槛」以下时准星进入 IsOutOfInk（染红 + 中央禁止叉）；
        /// 此时扣扳机被拒 → 弹出短 toast「弹药耗尽」，约 0.9s 后自动收起；补满墨后警告立即消失。
        /// </summary>
        [UnityTest]
        public IEnumerator WeaponReticleUI_OutOfInk_ShowsWarningAndEmptyToast()
        {
            // 本用例不加载场景，但准星 LateUpdate 受 MatchManager.InputAllowed 门控：
            // 全量跑时前序用例（如 MobileBombTouchPlayTests）会留下停在 Ready 相位的 MatchManager，
            // 此时 InputAllowed=false → 准星整帧早退、_isOutOfInk 永不更新，断言会误报。
            // 单条跑时 Instance==null 反而是绿的。这里显式把相位推进到 Playing，消除顺序依赖。
            yield return EnsureMatchInputAllowed();

            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var reticleGo = new GameObject("TestReticleUI", typeof(RectTransform));
            reticleGo.transform.SetParent(canvasGo.transform, false);
            var reticleUI = reticleGo.AddComponent<WeaponReticleUI>();
            reticleUI.EnsureInitialized();

            var playerGo = new GameObject("TestLocalPlayer",
                typeof(CharacterController),
                typeof(CombatantHealth),
                typeof(InkCharacterMotor),
                typeof(WeaponController),
                typeof(PlayerController));
            var player = playerGo.GetComponent<PlayerController>();
            player.Health.Configure(Team.TeamA, null);
            var weapon = playerGo.GetComponent<WeaponController>();
            var motor = playerGo.GetComponent<InkCharacterMotor>();

            var cfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            cfg.weaponKind = WeaponKind.Shooter;
            cfg.inkCostPerShot = 1f;
            weapon.EquipWeapon(cfg);

            yield return null;

            Assert.IsNotNull(reticleGo.transform.Find("EmptyInkX"), "空墨红叉层必须随准星一起装配");
            Assert.IsNotNull(reticleGo.transform.Find("EmptyInkToast"), "「弹药耗尽」toast 必须随准星一起装配");

            // 1. 墨量充足：不预警
            motor.RefillInkFull();
            yield return null;
            yield return null;
            Assert.IsFalse(reticleUI.IsOutOfInk, "满墨时准星不该染红");
            Assert.AreEqual("弹药耗尽", reticleUI.EmptyToastText);

            // 2. 墨量掉到门槛以下：进入空墨状态
            motor.TryConsumeInk(motor.CurrentInk - 0.2f);
            yield return null;
            yield return null;
            Assert.IsTrue(reticleUI.IsOutOfInk, "墨水低于下一发门槛时准星必须进入空墨状态");

            // 3. 空墨扣扳机：戳记递增 → 红叉弹跳 + toast 弹出
            int stampBefore = weapon.EmptyFeedbackStamp;
            weapon.TryFireMainWeapon(Vector3.forward);
            Assert.Greater(weapon.EmptyFeedbackStamp, stampBefore,
                "空墨开火必须递增 EmptyFeedbackStamp（准星据此播放空打动效）");

            yield return null;
            Assert.IsTrue(reticleUI.IsEmptyToastVisible, "空墨拒射后 toast 必须立刻可见");
            Assert.Greater(reticleUI.EmptyPunch01, 0f, "空墨拒射后红叉/刻度冲击必须开始播放");

            // 4. toast 很短：1.2s 后自动收起
            float waited = 0f;
            while (waited < 1.2f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            Assert.IsFalse(reticleUI.IsEmptyToastVisible, "toast 必须短促自动收起，不能常驻挡视野");

            // 5. 补满墨：警告与红叉一起退出
            motor.RefillInkFull();
            yield return null;
            yield return null;
            Assert.IsFalse(reticleUI.IsOutOfInk, "补满墨后准星必须立刻恢复正常配色");

            Object.Destroy(cfg);
            Object.Destroy(playerGo);
            Object.Destroy(canvasGo);
        }

        /// <summary>
        /// 保证 <see cref="MatchManager.InputAllowed"/> 为真：没有 MatchManager（纯 UI 用例）时直接返回，
        /// 有残留实例但停在 Ready/Finished/Result 时把它推进到 Playing，避免顺序依赖造成的假红。
        /// </summary>
        private static IEnumerator EnsureMatchInputAllowed()
        {
            if (MatchManager.InputAllowed) yield break;

            MatchManager match = MatchManager.Instance;
            if (match == null) yield break;

            if (match.CurrentPhase == MatchManager.Phase.Finished ||
                match.CurrentPhase == MatchManager.Phase.Result)
            {
                match.ReturnToMenu();
                yield return null;
            }

            PlayModeTestKit.ShortenMatchTimers();
            match.StartMatch();
            yield return PlayModeTestKit.WaitForPhase(match, MatchManager.Phase.Playing, 8f);
            Assert.IsTrue(MatchManager.InputAllowed, "前置：准星用例需要允许输入的比赛相位");
        }
    }
}
