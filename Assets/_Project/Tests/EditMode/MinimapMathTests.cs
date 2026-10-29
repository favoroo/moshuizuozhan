using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    [TestFixture]
    public class MinimapMathTests
    {
        [Test]
        public void WorldToMapAnchoredPosition_Center_MapsToZeroZero()
        {
            Vector2 origin = new Vector2(-20f, -20f);
            Vector2 mapSizeWorld = new Vector2(40f, 40f);
            Vector2 mapPixelSize = new Vector2(200f, 200f);

            Vector3 worldPos = new Vector3(0f, 0f, 0f); // 刚好在地图正中心
            Vector2 result = MinimapMath.WorldToMapAnchoredPosition(worldPos, origin, mapSizeWorld, mapPixelSize);

            Assert.AreEqual(0f, result.x, 0.001f);
            Assert.AreEqual(0f, result.y, 0.001f);
        }

        [Test]
        public void WorldToMapAnchoredPosition_Corners_MapsToHalfBounds()
        {
            Vector2 origin = new Vector2(-20f, -20f);
            Vector2 mapSizeWorld = new Vector2(40f, 40f);
            Vector2 mapPixelSize = new Vector2(200f, 200f);

            // 左下角 (-20, -20)
            Vector3 minPos = new Vector3(-20f, 0f, -20f);
            Vector2 minResult = MinimapMath.WorldToMapAnchoredPosition(minPos, origin, mapSizeWorld, mapPixelSize);
            Assert.AreEqual(-100f, minResult.x, 0.001f);
            Assert.AreEqual(-100f, minResult.y, 0.001f);

            // 右上角 (20, 20)
            Vector3 maxPos = new Vector3(20f, 0f, 20f);
            Vector2 maxResult = MinimapMath.WorldToMapAnchoredPosition(maxPos, origin, mapSizeWorld, mapPixelSize);
            Assert.AreEqual(100f, maxResult.x, 0.001f);
            Assert.AreEqual(100f, maxResult.y, 0.001f);
        }

        [Test]
        public void ShouldShowEnemy_StealthedEnemy_ReturnsFalse()
        {
            var go = new GameObject("TestEnemy");
            var health = go.AddComponent<CombatantHealth>();
            var motor = go.AddComponent<InkCharacterMotor>();

            // 存活但未潜行
            Assert.IsTrue(MinimapMath.ShouldShowEnemy(health, motor));

            // 死后应隐藏
            health.TakeDamage(9999f, Team.Neutral, Vector3.zero);
            Assert.IsFalse(MinimapMath.ShouldShowEnemy(health, motor));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void WorldRadiusToMapRadius_CalculatesProportionalScale()
        {
            Vector2 mapSizeWorld = new Vector2(40f, 40f);
            Vector2 mapPixelSize = new Vector2(200f, 200f); // 比例为 5 px/m

            float mapR = MinimapMath.WorldRadiusToMapRadius(3.4f, mapSizeWorld, mapPixelSize);
            Assert.AreEqual(17f, mapR, 0.001f);
        }
    }
}
