using NUnit.Framework;
using P1.Core;
using P1.Environment;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// [玩家/机动修复 2026-11-01] 两个纯逻辑判定 + 出生位姿 API 的 EditMode 回归：
    /// 1. <see cref="GroundInkSampleMath.ShouldSampleGroundInk"/>：空中（含下落段与跳顶附近）不得采样脚下墨；
    /// 2. <see cref="OutOfBoundsGuard.ShouldRecover"/>：y 坠落任何阶段都回收，XZ 越界只在比赛 Playing 阶段回收；
    /// 3. <see cref="CombatantHealth.TryGetSpawnPose"/> / <see cref="OutOfBoundsGuard.GetSpawnPose"/>：
    ///    优先各自的出生锚点，无锚点才回落到队伍出生线（保留旧兜底）。
    /// </summary>
    public class PlayerMobilityMathTests
    {
        // ---------- 脚下墨水采样 ----------

        [Test]
        public void ShouldSampleGroundInk_WhenGrounded_AlwaysSamples()
        {
            Assert.IsTrue(GroundInkSampleMath.ShouldSampleGroundInk(
                isGrounded: true, verticalVelocity: -20f, groundDistance: 0f, isWallClimbing: false));
            Assert.IsTrue(GroundInkSampleMath.ShouldSampleGroundInk(
                isGrounded: true, verticalVelocity: 8f, groundDistance: 0f, isWallClimbing: false),
                "贴地时即使刚起跳（本帧 isGrounded 仍为真）也应采样");
        }

        [Test]
        public void ShouldSampleGroundInk_InAir_NeverSamples()
        {
            // 旧实现只排除上升段，下落时 groundDistance 很大也会采样
            Assert.IsFalse(GroundInkSampleMath.ShouldSampleGroundInk(
                isGrounded: false, verticalVelocity: -8f, groundDistance: 1.4f, isWallClimbing: false),
                "下落腾空不得采样脚下墨（跳过敌墨不掉血/不减速）");
            Assert.IsFalse(GroundInkSampleMath.ShouldSampleGroundInk(
                isGrounded: false, verticalVelocity: 6f, groundDistance: 0.1f, isWallClimbing: false),
                "上升段不得采样脚下墨");
            Assert.IsFalse(GroundInkSampleMath.ShouldSampleGroundInk(
                isGrounded: false, verticalVelocity: 0.6f, groundDistance: -1f, isWallClimbing: false),
                "跳顶附近（垂直速度仍向上）不得采样");
        }

        [Test]
        public void ShouldSampleGroundInk_JustAboveSlope_StillSamples()
        {
            // 下坡 / 台阶 / 斜坡：isGrounded 会抖一帧，但向下短探地命中（间隙 ≤ 0.18m）
            Assert.IsTrue(GroundInkSampleMath.ShouldSampleGroundInk(
                isGrounded: false, verticalVelocity: -3f, groundDistance: 0.06f, isWallClimbing: false),
                "贴地前一帧仍应视为贴地（下坡与台阶宽容）");
            Assert.IsTrue(GroundInkSampleMath.ShouldSampleGroundInk(
                isGrounded: false, verticalVelocity: -3f,
                groundDistance: GroundInkSampleMath.MaxGroundSampleDistance, isWallClimbing: false));
            Assert.IsFalse(GroundInkSampleMath.ShouldSampleGroundInk(
                isGrounded: false, verticalVelocity: -3f,
                groundDistance: GroundInkSampleMath.MaxGroundSampleDistance + 0.01f, isWallClimbing: false),
                "超出宽容距离即视为腾空");
        }

        [Test]
        public void ShouldSampleGroundInk_WhileWallClimbing_HandsOverToWall()
        {
            Assert.IsFalse(GroundInkSampleMath.ShouldSampleGroundInk(
                isGrounded: false, verticalVelocity: 0f, groundDistance: 0f, isWallClimbing: true),
                "攀墙时脚下不是地面，墨水归属由墙面逻辑接管");
            Assert.IsFalse(GroundInkSampleMath.ShouldSampleGroundInk(
                isGrounded: true, verticalVelocity: 0f, groundDistance: 0f, isWallClimbing: true),
                "攀墙状态一律不采样脚下墨（墙面 InkAt 才是唯一来源）");
        }

        [Test]
        public void GroundProbeConstants_AreCoherent()
        {
            Assert.Greater(GroundInkSampleMath.MaxGroundSampleDistance, 0f);
            Assert.Greater(GroundInkSampleMath.GroundProbeMaxDistance,
                GroundInkSampleMath.GroundProbeOriginLift + GroundInkSampleMath.MaxGroundSampleDistance);
            Assert.Greater(GroundInkSampleMath.MinWalkableNormalY, 0f);
        }

        // ---------- 越界回收 ----------

        [Test]
        public void ShouldRecover_InAirPhase_StillRecoversVerticalFalls()
        {
            // 显式传 -1f：本用例验证的是「阶段门禁不拦坠落」，不是阈值数值本身
            // （默认阈值已随滑板场碗池降到 -6m）。
            const float legacyMinY = -1f;
            Vector3 falling = new Vector3(0f, -3f, 0f);
            Assert.IsTrue(OutOfBoundsGuard.ShouldRecover(falling, allowHorizontalRecovery: false, minY: legacyMinY),
                "y 低于下限时任何阶段都要回收（否则哨响瞬间越界会无限坠落）");
            Assert.IsTrue(OutOfBoundsGuard.ShouldRecover(falling, allowHorizontalRecovery: true, minY: legacyMinY));
        }

        [Test]
        public void ShouldRecover_HorizontalViolation_RespectsPhaseGate()
        {
            float beyondX = ArenaLayoutV2.BoundaryX + 0.3f + 0.1f;
            float beyondZ = ArenaLayoutV2.BoundaryZ + 0.3f + 0.1f;
            Vector3 outsideX = new Vector3(beyondX, 1.2f, 0f);
            Vector3 outsideZ = new Vector3(0f, 1.2f, beyondZ);

            Assert.IsTrue(OutOfBoundsGuard.ShouldRecover(outsideX, allowHorizontalRecovery: true));
            Assert.IsTrue(OutOfBoundsGuard.ShouldRecover(outsideZ, allowHorizontalRecovery: true));
            Assert.IsFalse(OutOfBoundsGuard.ShouldRecover(outsideX, allowHorizontalRecovery: false),
                "非 Playing 阶段不得把越界角色拉回（会打断结算 / 倒计时站位）");
            Assert.IsFalse(OutOfBoundsGuard.ShouldRecover(outsideZ, allowHorizontalRecovery: false));
        }

        [Test]
        public void ShouldRecover_InsideArena_NeverRecovers()
        {
            Vector3 inside = new Vector3(10f, 0f, 10f);
            Assert.IsFalse(OutOfBoundsGuard.ShouldRecover(inside, allowHorizontalRecovery: true));
            Assert.IsFalse(OutOfBoundsGuard.ShouldRecover(inside, allowHorizontalRecovery: false));
        }

        [Test]
        public void IsOutOfBounds_KeepsLegacySemantics()
        {
            // 旧 API 语义不变：XZ 越界与 y 坠落都算越界。
            // y 阈值不再走默认值 —— 默认值已随滑板场碗池降到 -6m，这里显式传 -1f 才能保住「-1.5m 算坠落」的旧口径。
            const float legacyMinY = -1f;
            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(new Vector3(ArenaLayoutV2.BoundaryX + 0.4f, 0f, 0f)));
            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(new Vector3(0f, -1.5f, 0f), 0.3f, 0.3f, legacyMinY));
            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(Vector3.zero));
        }

        // ---------- 出生位姿 ----------

        [Test]
        public void GetSpawnPose_WithoutAnchor_FallsBackToTeamSpawnLine()
        {
            var go = new GameObject("SpawnPoseTest_NoAnchor");
            try
            {
                var health = go.AddComponent<CombatantHealth>();
                health.Configure(Team.TeamA, null, null);
                go.transform.position = new Vector3(1.0f, 0.5f, 20f);

                Assert.IsFalse(health.TryGetSpawnPose(out _, out _),
                    "没有显式出生锚点的运行时单位必须返回 false（避免把创建位置当出生点）");

                OutOfBoundsGuard.GetSpawnPose(health, out Vector3 pos, out Quaternion rot);
                Vector3 expected = OutOfBoundsGuard.GetFallbackSpawnPosition(Team.TeamA, 1.0f);
                Assert.AreEqual(expected.x, pos.x, 0.001f);
                Assert.AreEqual(expected.z, pos.z, 0.001f);
                Assert.AreEqual(0f, rot.eulerAngles.y, 0.01f, "TeamA 兜底出生朝向 +Z");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void GetSpawnPose_WithAnchor_UsesOwnAnchorPerCombatant()
        {
            var anchorGo = new GameObject("SpawnAnchor_B");
            var actorGo = new GameObject("SpawnPoseTest_WithAnchor");
            try
            {
                anchorGo.transform.SetPositionAndRotation(
                    new Vector3(-2.1f, 1.05f, -ArenaLayoutV2.SpawnZ), Quaternion.Euler(0f, 180f, 0f));

                var health = actorGo.AddComponent<CombatantHealth>();
                health.Configure(Team.TeamB, null, anchorGo.transform);

                Assert.IsTrue(health.TryGetSpawnPose(out Vector3 pos, out Quaternion rot),
                    "配置了 respawnAnchor 的单位必须能读出自己的出生位姿");
                Assert.AreEqual(-2.1f, pos.x, 0.001f, "两人同时越界应各自回到自己的出生点，而不是叠在同一点");
                Assert.AreEqual(180f, rot.eulerAngles.y, 0.01f);

                OutOfBoundsGuard.GetSpawnPose(health, out Vector3 guardPos, out Quaternion guardRot);
                Assert.AreEqual(pos.x, guardPos.x, 0.001f, "越界回收必须优先用单位自己的出生锚点");
                Assert.AreEqual(pos.z, guardPos.z, 0.001f);
                Assert.AreEqual(180f, guardRot.eulerAngles.y, 0.01f);
                Assert.AreEqual(anchorGo.transform, health.RespawnAnchor);
            }
            finally
            {
                Object.DestroyImmediate(actorGo);
                Object.DestroyImmediate(anchorGo);
            }
        }
    }
}
