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
    }
}
