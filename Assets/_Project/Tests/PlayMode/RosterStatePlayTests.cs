using System.Collections;
using NUnit.Framework;
using P1.AI;
using P1.Core;
using P1.Ink;
using P1.Player;
using P1.UI;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// [P6-B] 4v4 顶部战况条的运行时状态切换回归：
    /// 1. 场景内 8 名战斗人员全部绑定到左右各 4 个槽位；
    /// 2. 队员阵亡 → 槽位切到 DeadRespawning：死亡图标 / 复活进度环 / 秒数角标同时出现，秒数 1~4；
    /// 3. 4s 后自动复活 → 槽位切回 NormalAlive，死亡视觉全部收起；
    /// 4. 存活且大招就绪 → 金色旋转光环强度上升（SpecialReady）。
    /// </summary>
    public class RosterStatePlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        private IEnumerator LoadPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景里应存在本地玩家 PlayerController");
            Assert.IsNotNull(InkSurfaceController.Instance);

            player.enabled = false;

            var bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
            for (int i = 0; i < bots.Length; i++)
            {
                bots[i].enabled = false;
                var motor = bots[i].GetComponent<InkCharacterMotor>();
                if (motor != null) motor.enabled = false;
                var weapon = bots[i].GetComponent<WeaponController>();
                if (weapon != null) weapon.enabled = false;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator TopRoster_BindsFourVersusFour_AndDeathRespawningToAlive()
        {
            yield return LoadPlaybox();

            var hud = Object.FindAnyObjectByType<HudController>();
            Assert.IsNotNull(hud, "场景里应存在 HudController");
            Assert.IsNotNull(hud.TopRosterBar, "HudController 应已装配 TopRosterBar");

            TopRosterBarView roster = hud.TopRosterBar;
            roster.TryAutoBindCombatants();
            yield return null;

            Assert.AreEqual(4, roster.TeamASlots.Count, "Team A 应有 4 个槽位");
            Assert.AreEqual(4, roster.TeamBSlots.Count, "Team B 应有 4 个槽位");

            RosterAvatarSlot targetSlot = null;
            for (int i = 0; i < roster.TeamBSlots.Count; i++)
            {
                RosterAvatarSlot slot = roster.TeamBSlots[i];
                Assert.IsNotNull(slot, $"Team B 槽位 {i} 不能为空");
                Assert.IsNotNull(slot.BoundCombatant, $"Team B 槽位 {i} 应绑定到实际战斗人员");
                if (targetSlot == null) targetSlot = slot;
            }

            for (int i = 0; i < roster.TeamASlots.Count; i++)
            {
                Assert.IsNotNull(roster.TeamASlots[i], $"Team A 槽位 {i} 不能为空");
                Assert.IsNotNull(roster.TeamASlots[i].BoundCombatant, $"Team A 槽位 {i} 应绑定到实际战斗人员");
            }

            CombatantHealth victim = targetSlot.BoundCombatant;
            Assert.IsNotNull(victim);
            Assert.AreEqual(Team.TeamB, victim.Team, "目标槽位应为敌方队员");

            // 其它战斗人员全部无敌，确保只有目标会阵亡/复活，避免相互击杀造成偶发失败
            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i] != victim)
                    all[i].IsInvulnerable = true;
            }
            victim.IsInvulnerable = false;

            victim.TakeDamage(200f, Team.TeamA, victim.transform.position);
            yield return null;
            yield return null;

            Assert.IsFalse(victim.IsAlive, "200 伤害应击杀目标");

            Transform deathIcon = targetSlot.transform.Find("DeathIcon");
            Transform progressRing = targetSlot.transform.Find("ProgressRing");
            Transform countdownBadge = targetSlot.transform.Find("CountdownBadge");
            Transform countdownText = targetSlot.transform.Find("CountdownBadge/CountdownText");

            Assert.IsNotNull(deathIcon, "槽位应包含 DeathIcon");
            Assert.IsNotNull(progressRing, "槽位应包含 ProgressRing");
            Assert.IsNotNull(countdownBadge, "槽位应包含 CountdownBadge");
            Assert.IsNotNull(countdownText, "槽位应包含倒计时文字");

            Assert.IsTrue(deathIcon.gameObject.activeSelf, "阵亡应显示 X/骷髅图标");
            Assert.IsTrue(progressRing.gameObject.activeSelf, "阵亡应显示复活进度环");
            Assert.IsTrue(countdownBadge.gameObject.activeSelf, "阵亡应显示秒数角标");

            var text = countdownText.GetComponent<TMP_Text>();
            Assert.IsNotNull(text);
            int seconds;
            Assert.IsTrue(int.TryParse(text.text, out seconds), $"倒计时文字应为整数秒，实际 '{text.text}'");
            Assert.That(seconds, Is.InRange(1, 4), "阵亡倒计时应为 1~4 秒");

            Assert.AreEqual(AvatarSlotState.DeadRespawning,
                TopRosterMath.DetermineSlotState(isAlive: false, isSpecialReady: false));

            // 等待 4s 复活
            yield return new WaitForSeconds(4.3f);
            yield return null;
            yield return null;

            Assert.IsTrue(victim.IsAlive, "4s 后目标应自动复活");
            Assert.IsFalse(deathIcon.gameObject.activeSelf, "复活后应隐藏死亡图标");
            Assert.IsFalse(progressRing.gameObject.activeSelf, "复活后应隐藏复活进度环");
            Assert.IsFalse(countdownBadge.gameObject.activeSelf, "复活后应隐藏秒数角标");
            Assert.AreEqual(AvatarSlotState.NormalAlive,
                TopRosterMath.DetermineSlotState(isAlive: true, isSpecialReady: false));

            // 存活 + 大招就绪 → 金色光环强度上升
            var targetWeapon = victim.GetComponent<WeaponController>();
            Assert.IsNotNull(targetWeapon, "队员应挂载 WeaponController");
            targetWeapon.AddSpecialPoints(500);
            Assert.IsTrue(targetWeapon.IsSpecialReady, "充能后大招应就绪");

            yield return new WaitForSeconds(0.6f);

            Transform specialRing = targetSlot.transform.Find("SpecialReadyRing");
            Assert.IsNotNull(specialRing, "槽位应包含 SpecialReadyRing");
            var ringGraphic = specialRing.GetComponent<UIRotatingRingGraphic>();
            Assert.IsNotNull(ringGraphic);
            Assert.Greater(ringGraphic.Intensity, 0.5f, "大招就绪时金色旋转光环应点亮");
            Assert.AreEqual(AvatarSlotState.SpecialReady,
                TopRosterMath.DetermineSlotState(isAlive: true, isSpecialReady: true));
        }
    }
}