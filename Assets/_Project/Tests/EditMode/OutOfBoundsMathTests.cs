using NUnit.Framework;
using P1.Core;
using P1.Environment;
using UnityEditor;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P16-B 越界判定与出生锚点纯函数 EditMode 单元测试。
    /// </summary>
    public class OutOfBoundsMathTests
    {
        [Test]
        public void IsOutOfBounds_WithinBoundary_ReturnsFalse()
        {
            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(Vector3.zero));
            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(new Vector3(10f, 0f, 10f)));
            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(new Vector3(-10f, 0f, -10f)));

            // 恰好在边界上（margin 0.3 内）
            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(new Vector3(ArenaLayoutV2.BoundaryX, 0f, 0f)));
            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(new Vector3(0f, 0f, ArenaLayoutV2.BoundaryZ)));
        }

        [Test]
        public void IsOutOfBounds_ExceedsBoundaryX_ReturnsTrue()
        {
            float limitX = ArenaLayoutV2.BoundaryX + 0.3f;
            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(new Vector3(limitX + 0.05f, 0f, 0f)));
            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(new Vector3(-(limitX + 0.05f), 0f, 0f)));
        }

        [Test]
        public void IsOutOfBounds_ExceedsBoundaryZ_ReturnsTrue()
        {
            float limitZ = ArenaLayoutV2.BoundaryZ + 0.3f;
            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(new Vector3(0f, 0f, limitZ + 0.05f)));
            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(new Vector3(0f, 0f, -(limitZ + 0.05f))));
        }

        [Test]
        public void IsOutOfBounds_RespectsCustomMargins()
        {
            float marginX = 1.5f;
            float marginZ = 2.5f;

            // 超出默认 0.3 但在自定义 margin 内
            Vector3 testPos = new Vector3(ArenaLayoutV2.BoundaryX + 0.8f, 0f, 0f);
            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(testPos, 0.3f, 0.3f, -1f));
            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(testPos, marginX, marginZ, -1f));

            Vector3 testPosZ = new Vector3(0f, 0f, ArenaLayoutV2.BoundaryZ + 1.8f);
            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(testPosZ, 0.3f, 0.3f, -1f));
            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(testPosZ, marginX, marginZ, -1f));
        }

        [Test]
        public void IsOutOfBounds_BelowMinY_ReturnsTrue()
        {
            // y >= minY 正常
            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(new Vector3(0f, -0.5f, 0f), 0.3f, 0.3f, -1f));

            // y < minY 判定为坠落越界
            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(new Vector3(0f, -1.05f, 0f), 0.3f, 0.3f, -1f));

            // 自定义较高 minY
            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(new Vector3(0f, 0.2f, 0f), 0.3f, 0.3f, 0.5f));
            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(new Vector3(0f, 0.6f, 0f), 0.3f, 0.3f, 0.5f));
        }

        [Test]
        public void ShouldRecover_SkateparkBowlFloor_IsNotAFall()
        {
            // 霓虹滑板场碗池底 y=-2.1m 是合法玩法区；阈值若停在旧值 -1m，
            // 角色下到半坡就会被当成坠落拉回出生点。
            Vector3 bowlFloor = new Vector3(0f, ArenaLayoutSkatepark.BowlFloorY, 0f);
            Assert.Less(GameConfigSO.DefaultOutOfBoundsMinY, ArenaLayoutSkatepark.BowlFloorY - 1f,
                "默认坠落阈值必须低于碗池底并留出余量");

            Assert.IsFalse(OutOfBoundsGuard.ShouldRecover(
                bowlFloor, allowHorizontalRecovery: true, minY: GameConfigSO.DefaultOutOfBoundsMinY));
            Assert.IsFalse(OutOfBoundsGuard.ShouldRecover(
                bowlFloor, allowHorizontalRecovery: false, minY: GameConfigSO.DefaultOutOfBoundsMinY));

            // 真正掉出世界时仍要回收
            Assert.IsTrue(OutOfBoundsGuard.ShouldRecover(
                new Vector3(0f, GameConfigSO.DefaultOutOfBoundsMinY - 0.1f, 0f),
                allowHorizontalRecovery: false, minY: GameConfigSO.DefaultOutOfBoundsMinY));
        }

        [Test]
        public void GameConfigAsset_OutOfBoundsMinY_LeavesSkateparkBowlPlayable()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfigSO>(
                "Assets/_Project/ScriptableObjects/GameConfig.asset");
            Assert.IsNotNull(config, "GameConfig.asset 必须存在（菜单 P1/构建/1 生成）");
            Assert.Less(config.outOfBoundsMinY, ArenaLayoutSkatepark.BowlFloorY - 1f,
                "GameConfig.asset 的 outOfBoundsMinY 必须低于滑板场碗池底，否则碗池不可玩");
        }

        [Test]
        public void GetFallbackSpawnPosition_TeamA_HasNegativeSpawnZ()
        {
            Vector3 pos = OutOfBoundsGuard.GetFallbackSpawnPosition(Team.TeamA);
            Assert.AreEqual(-ArenaLayoutV2.SpawnZ, pos.z, 0.001f);
            Assert.AreEqual(ArenaLayoutV2.SpawnPointY, pos.y, 0.001f);
            Assert.AreEqual(0f, pos.x, 0.001f);
        }

        [Test]
        public void GetFallbackSpawnPosition_TeamB_HasPositiveSpawnZ()
        {
            Vector3 pos = OutOfBoundsGuard.GetFallbackSpawnPosition(Team.TeamB);
            Assert.AreEqual(ArenaLayoutV2.SpawnZ, pos.z, 0.001f);
            Assert.AreEqual(ArenaLayoutV2.SpawnPointY, pos.y, 0.001f);
            Assert.AreEqual(0f, pos.x, 0.001f);
        }

        [Test]
        public void GetFallbackSpawnPosition_ClampsPreferredXToSpawnLineWidth()
        {
            Vector3 posNormal = OutOfBoundsGuard.GetFallbackSpawnPosition(Team.TeamA, 1.5f);
            Assert.AreEqual(1.5f, posNormal.x, 0.001f);

            Vector3 posRightClamp = OutOfBoundsGuard.GetFallbackSpawnPosition(Team.TeamA, 4.0f);
            Assert.AreEqual(2.4f, posRightClamp.x, 0.001f);

            Vector3 posLeftClamp = OutOfBoundsGuard.GetFallbackSpawnPosition(Team.TeamA, -6.0f);
            Assert.AreEqual(-2.4f, posLeftClamp.x, 0.001f);
        }

        [Test]
        public void GetSpawnRotation_FacesOpponentSide()
        {
            Quaternion rotA = OutOfBoundsGuard.GetSpawnRotation(Team.TeamA);
            Assert.AreEqual(0f, rotA.eulerAngles.y, 0.01f, "TeamA 出生应面向 +Z (0度)");

            Quaternion rotB = OutOfBoundsGuard.GetSpawnRotation(Team.TeamB);
            Assert.AreEqual(180f, rotB.eulerAngles.y, 0.01f, "TeamB 出生应面向 -Z (180度)");
        }
    }
}
