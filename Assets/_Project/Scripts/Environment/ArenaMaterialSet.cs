using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 竞技场构建所需的材质集合（P7-B）。由 ProjectScaffolder 统一加载/生成后传入，
    /// 让 ArenaGeometryBuilder 与装饰构建器都不必自己摸 AssetDatabase。
    /// 所有字段允许为 null：EditMode 测试会以空集合调用几何构建器。
    /// </summary>
    public sealed class ArenaMaterialSet
    {
        public Material ground;
        public Material wall;
        public Material inkWall;
        public Material boundary;
        public Material spawnPadA;
        public Material spawnPadB;

        public Material containerTeal;
        public Material containerRust;
        public Material containerSlate;
        public Material containerMustard;

        public Material concrete;
        public Material concreteDark;
        public Material steel;
        public Material steelDark;
        public Material wood;
        public Material woodDark;
        public Material foliage;

        public Material propMatte;
        public Material propMetal;
        public Material propEmissive;
        public Material propCloth;
        public Material bannerAtlas;
        public Material water;
        public Material skybox;

        // ---- 多地图扩展（海鸥港湾；允许为 null，构建器有兜底）----
        /// <summary>船壳深蓝（渡轮 / 拖船 / 游艇）。</summary>
        public Material hull;
        /// <summary>客舱 / 船屋奶油白。</summary>
        public Material cabin;
        /// <summary>甲板与木栈道（比 wood 更亮的柚木色）。</summary>
        public Material deck;
        /// <summary>珊瑚橙强调色（船身带 / 警示条 / 救生圈）。</summary>
        public Material accent;
        /// <summary>舷窗 / 门窗深色玻璃。</summary>
        public Material glass;
        /// <summary>场内浅湾水面：与远景水面同色，但关闭岸沫（12m 岸距会让整条水线糊成白带）。</summary>
        public Material waterInner;
    }
}
