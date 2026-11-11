using System.Collections.Generic;
using NUnit.Framework;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 大招预警范围投影（<see cref="SpecialRangeShadow"/>）的贴地断言：
    /// 场地有高差时（台阶/集装箱），网格必须按射线采样到的顶面高度铺开，而不是悬空平面圆；
    /// 纯平地时保持平盘。测试摆在 (500,0,500) 远处，避免命中编辑器里打开的场景碰撞体。
    /// </summary>
    public class SpecialRangeShadowTests
    {
        private static readonly Vector3 TestOrigin = new Vector3(500f, 0f, 500f);

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        private GameObject CreateBox(Vector3 center, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.position = TestOrigin + center;
            box.transform.localScale = size;
            _created.Add(box);
            return box;
        }

        [Test]
        public void DrapedMesh_FollowsRaisedBoxTop_InsteadOfFlatPlane()
        {
            CreateBox(new Vector3(0f, -0.5f, 0f), new Vector3(24f, 1f, 24f));      // 顶面 y=0
            CreateBox(new Vector3(2.4f, 0.75f, 0f), new Vector3(2.4f, 1.5f, 2.4f)); // 顶面 y=1.5
            Physics.SyncTransforms();

            var shadow = new SpecialRangeShadow(null, "TestDrape");
            try
            {
                shadow.Show(TestOrigin, 5.2f, 3.2f / 5.2f, Color.red);

                Assert.IsTrue(shadow.IsVisible, "Show 之后投影应可见");
                Assert.Greater(shadow.LastHeightSpread, 1.2f,
                    $"5.2m 范围里立着 1.5m 高的箱子，贴地网格应有明显高差，实际 {shadow.LastHeightSpread:F2}m" +
                    "（≈0 说明还是平面圆盘）");
            }
            finally
            {
                shadow.Dispose();
            }
        }

        [Test]
        public void DrapedMesh_OnFlatGround_StaysFlat()
        {
            CreateBox(new Vector3(0f, -0.5f, 0f), new Vector3(24f, 1f, 24f));
            Physics.SyncTransforms();

            var shadow = new SpecialRangeShadow(null, "TestFlat");
            try
            {
                shadow.Show(TestOrigin, 5.2f, 3.2f / 5.2f, Color.red);

                Assert.Less(shadow.LastHeightSpread, 0.02f,
                    $"纯平地面不应采样出高差，实际 {shadow.LastHeightSpread:F2}m");
            }
            finally
            {
                shadow.Dispose();
            }
        }

        [Test]
        public void SetRadius_BigChange_RebuildsDrape_ToReachNewCrate()
        {
            CreateBox(new Vector3(0f, -0.5f, 0f), new Vector3(24f, 1f, 24f));
            CreateBox(new Vector3(4.4f, 0.75f, 0f), new Vector3(1.6f, 1.5f, 1.6f));
            Physics.SyncTransforms();

            var shadow = new SpecialRangeShadow(null, "TestGrow");
            try
            {
                // 起跳 35% 半径（2m）：箱子在 3.6m 外，网格还是平的
                shadow.Show(TestOrigin, 2f, 3.2f / 5.2f, Color.red);
                Assert.Less(shadow.LastHeightSpread, 0.05f,
                    "2m 半径够不到 3.6m 外的箱子，此时应为平盘");

                // 长满 5.2m：半径变化远超阈值，必须重建并爬到箱顶
                shadow.SetRadius(5.2f);
                Assert.Greater(shadow.LastHeightSpread, 1.2f,
                    $"半径扩到 5.2m 后应重新采样并覆盖 1.5m 箱顶，实际高差 {shadow.LastHeightSpread:F2}m");
            }
            finally
            {
                shadow.Dispose();
            }
        }

        [Test]
        public void SetRadius_WhenHidden_DoesNothing_AndHideClearsVisibility()
        {
            var shadow = new SpecialRangeShadow(null, "TestHidden");
            try
            {
                shadow.SetRadius(3f);
                Assert.IsFalse(shadow.IsVisible, "Hide 状态下 SetRadius 不应把投影点亮");

                shadow.Show(TestOrigin, 2f, 0.6f, Color.red);
                Assert.IsTrue(shadow.IsVisible);

                shadow.Hide();
                Assert.IsFalse(shadow.IsVisible, "Hide 后 IsVisible 必须为 false");
            }
            finally
            {
                shadow.Dispose();
            }
        }
    }
}