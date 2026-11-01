using NUnit.Framework;
using P1.Core;
using P1.Environment;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P16-A 出生禁入圈纯数学与规则判定 EditMode 单元测试。
    /// </summary>
    public class SpawnZoneMathTests
    {
        [Test]
        public void IsInside_PointInsideCircle_ReturnsTrue()
        {
            Vector3 center = new Vector3(0f, 1.2f, 23.5f);
            float radius = 6.0f;

            // 圆心
            Assert.IsTrue(SpawnZone.IsInside(center, center, radius));
            // 圈内任意点
            Assert.IsTrue(SpawnZone.IsInside(center + new Vector3(3f, 0f, 4f), center, radius));
            // 忽略 Y 轴高度差异（例如跳在空中或站在台面高处）
            Assert.IsTrue(SpawnZone.IsInside(center + new Vector3(0f, 8f, 5.5f), center, radius));
        }

        [Test]
        public void IsInside_PointOutsideCircle_ReturnsFalse()
        {
            Vector3 center = new Vector3(0f, 1.2f, 23.5f);
            float radius = 6.0f;

            // X 轴超界
            Assert.IsFalse(SpawnZone.IsInside(center + new Vector3(6.1f, 0f, 0f), center, radius));
            // 对角线超界 (5^2 + 5^2 = 50 > 36)
            Assert.IsFalse(SpawnZone.IsInside(center + new Vector3(5f, 0f, 5f), center, radius));
        }

        [Test]
        public void CalculatePushDirection_RadialDirection_NormalizedAndFlat()
        {
            Vector3 center = Vector3.zero;
            Vector3 pos = new Vector3(3f, 10f, 4f); // 带 Y 偏移

            Vector3 dir = SpawnZone.CalculatePushDirection(pos, center, Vector3.forward);

            Assert.AreEqual(0.6f, dir.x, 0.001f);
            Assert.AreEqual(0f, dir.y, 0.0001f, "推出方向垂直分量必须严格为 0");
            Assert.AreEqual(0.8f, dir.z, 0.001f);
            Assert.AreEqual(1.0f, dir.magnitude, 0.001f, "推出向量必须归一化");
        }

        [Test]
        public void CalculatePushDirection_AtCenter_UsesFallbackDirectionWithoutNaN()
        {
            Vector3 center = new Vector3(2f, 0f, 2f);
            Vector3 pos = center;

            Vector3 dir = SpawnZone.CalculatePushDirection(pos, center, Vector3.back);

            Assert.IsFalse(float.IsNaN(dir.x));
            Assert.IsFalse(float.IsNaN(dir.y));
            Assert.IsFalse(float.IsNaN(dir.z));
            Assert.AreEqual(0f, dir.y, 0.0001f);
            Assert.AreEqual(new Vector3(0f, 0f, -1f), dir);
        }

        [Test]
        public void CalculatePushDelta_ScalesByPushSpeedAndDt()
        {
            Vector3 center = Vector3.zero;
            Vector3 pos = new Vector3(10f, 0f, 0f); // 沿 +X
            float speed = 8.0f;
            float dt = 0.05f;

            Vector3 delta = SpawnZone.CalculatePushDelta(pos, center, speed, dt, Vector3.forward);

            Assert.AreEqual(0.4f, delta.x, 0.001f);
            Assert.AreEqual(0f, delta.y, 0.0001f);
            Assert.AreEqual(0f, delta.z, 0.001f);
        }

        [Test]
        public void ShouldPush_OnlyPushesOpponent()
        {
            // Team A 的禁入圈
            Assert.IsFalse(SpawnZone.ShouldPush(Team.TeamA, Team.TeamA), "本队友军不可被推出");
            Assert.IsTrue(SpawnZone.ShouldPush(Team.TeamB, Team.TeamA), "敌队单位必须被推出");
            Assert.IsFalse(SpawnZone.ShouldPush(Team.Neutral, Team.TeamA), "中立单位不可被推出");

            // Team B 的禁入圈
            Assert.IsTrue(SpawnZone.ShouldPush(Team.TeamA, Team.TeamB), "敌队单位必须被推出");
            Assert.IsFalse(SpawnZone.ShouldPush(Team.TeamB, Team.TeamB), "本队友军不可被推出");
            Assert.IsFalse(SpawnZone.ShouldPush(Team.Neutral, Team.TeamB), "中立单位不可被推出");
        }

        [Test]
        public void IsInEnemySpawnZone_AccountsForRadiusAndAvoidBuffer()
        {
            // Team A 视角的敌方（Team B）禁入圈位于 +SpawnZ (23.5)
            Vector3 enemyCenter = SpawnZone.GetDefaultZoneCenter(Team.TeamB);

            // 圈内
            Assert.IsTrue(SpawnZone.IsInEnemySpawnZone(enemyCenter + new Vector3(2f, 0f, 0f), Team.TeamA, 2.0f));

            // 在 6m 外但在 6m + 2m buffer 内
            Vector3 bufferPos = enemyCenter + new Vector3(7.0f, 0f, 0f);
            Assert.IsTrue(SpawnZone.IsInEnemySpawnZone(bufferPos, Team.TeamA, 2.0f));

            // 超出 6m + 2m = 8m
            Vector3 outsidePos = enemyCenter + new Vector3(8.5f, 0f, 0f);
            Assert.IsFalse(SpawnZone.IsInEnemySpawnZone(outsidePos, Team.TeamA, 2.0f));

            // 中立体判定为 false
            Assert.IsFalse(SpawnZone.IsInEnemySpawnZone(enemyCenter, Team.Neutral, 2.0f));
        }
    }
}
