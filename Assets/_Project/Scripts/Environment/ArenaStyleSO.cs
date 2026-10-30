using UnityEngine;

namespace P1.Environment
{
    /// <summary>后处理色调映射模式：ACES 更电影、Neutral 更保留高饱和卡通色。</summary>
    public enum ArenaTonemapMode
    {
        ACES = 0,
        Neutral = 1
    }

    /// <summary>
    /// 竞技场环境视觉风格档案（P7-B）：集中管理「集装箱码头 · 白天清爽」的配色、光照、
    /// 雾、天空与后处理参数。所有材质由 ProjectScaffolder 按此资产生成，场景里不散落硬编码色值。
    /// P10-A：新增三渲二色阶、冷暖阴影、卡通描边与高饱和日光参数。
    /// </summary>
    [CreateAssetMenu(fileName = "ArenaStyle", menuName = "P1/Arena Style")]
    public class ArenaStyleSO : ScriptableObject
    {
        [Header("地面与标线")]
        public Color groundBase = new Color(0.76f, 0.74f, 0.68f, 1f);
        public Color groundSeam = new Color(0.52f, 0.50f, 0.45f, 1f);
        public Color groundMarking = new Color(0.88f, 0.86f, 0.78f, 1f);
        public Color groundMarkingAccent = new Color(0.82f, 0.65f, 0.26f, 1f);
        public Color groundCliff = new Color(0.34f, 0.33f, 0.31f, 1f);
        public Color hazardYellow = new Color(0.86f, 0.68f, 0.20f, 1f);
        public Color hazardDark = new Color(0.16f, 0.16f, 0.16f, 1f);

        [Header("混凝土与钢构")]
        public Color concreteLight = new Color(0.80f, 0.78f, 0.72f, 1f);
        public Color concreteMid = new Color(0.64f, 0.62f, 0.57f, 1f);
        public Color concreteDark = new Color(0.42f, 0.41f, 0.38f, 1f);
        public Color steel = new Color(0.42f, 0.47f, 0.56f, 1f);
        public Color steelDark = new Color(0.24f, 0.27f, 0.32f, 1f);

        [Header("道具")]
        public Color wood = new Color(0.72f, 0.58f, 0.42f, 1f);
        public Color woodDark = new Color(0.48f, 0.36f, 0.24f, 1f);
        public Color foliage = new Color(0.24f, 0.45f, 0.28f, 1f);
        public Color lampGlass = new Color(1.00f, 0.85f, 0.60f, 1f);

        [Header("集装箱与边界")]
        public Color containerTeal = new Color(0.22f, 0.56f, 0.54f, 1f);
        public Color containerRust = new Color(0.68f, 0.32f, 0.22f, 1f);
        public Color containerSlate = new Color(0.36f, 0.47f, 0.62f, 1f);
        public Color containerMustard = new Color(0.85f, 0.64f, 0.22f, 1f);
        public Color boundaryConcrete = new Color(0.80f, 0.78f, 0.72f, 1f);
        public Color boundaryTrim = new Color(0.55f, 0.53f, 0.48f, 1f);
        public Color teamAPad = new Color(1.00f, 0.48f, 0.08f, 1f);
        public Color teamBPad = new Color(0.15f, 0.35f, 1.00f, 1f);

        [Header("日光与环境光")]
        public Color sunColor = new Color(1.00f, 0.97f, 0.91f, 1f);
        [Range(0.2f, 4f)] public float sunIntensity = 1.60f;
        public Vector3 sunEuler = new Vector3(52f, -38f, 0f);
        public Color ambientSky = new Color(0.36f, 0.50f, 0.76f, 1f);
        public Color ambientEquator = new Color(0.56f, 0.54f, 0.50f, 1f);
        public Color ambientGround = new Color(0.18f, 0.17f, 0.17f, 1f);
        [Range(0f, 2f)] public float ambientIntensity = 0.75f;
        [Range(0f, 1f)] public float reflectionIntensity = 0.35f;
        [Range(0f, 1f)] public float shadowStrength = 0.88f;

        [Header("三渲二光照")]
        [Range(0.2f, 0.8f)] public float shadeThreshold = 0.50f;
        [Range(0.005f, 0.2f)] public float shadeSoftness = 0.03f;
        public Color shadowTint = new Color(0.40f, 0.45f, 0.62f, 1f);
        [Range(0f, 1f)] public float shadowFloor = 0.42f;
        public Color rimColor = new Color(0.75f, 0.86f, 1f, 1f);
        [Range(0f, 1f)] public float rimStrength = 0.18f;
        [Range(0.5f, 8f)] public float rimPower = 3f;
        [Range(0f, 2f)] public float specStrength = 0.5f;
        [Range(0.5f, 0.999f)] public float specThreshold = 0.95f;
        [Range(0.001f, 0.1f)] public float specSoftness = 0.02f;

        [Header("墨水三渲二")]
        [Range(0.2f, 0.8f)] public float inkShadeThreshold = 0.48f;
        [Range(0.005f, 0.2f)] public float inkShadeSoftness = 0.08f;
        [Range(0f, 1f)] public float inkShadowFloor = 0.72f;
        [Range(0f, 1f)] public float characterShadowFloor = 0.55f;

        [Header("雾")]
        public bool fogEnabled = true;
        public Color fogColor = new Color(0.62f, 0.74f, 0.86f, 1f);
        public float fogStartDistance = 45f;
        public float fogEndDistance = 135f;

        [Header("程序化天空")]
        public Color skyTop = new Color(0.16f, 0.46f, 0.86f, 1f);
        public Color skyHorizon = new Color(0.80f, 0.89f, 0.97f, 1f);
        public Color skyGround = new Color(0.52f, 0.60f, 0.66f, 1f);
        public Color sunDisc = new Color(1.00f, 0.95f, 0.82f, 1f);
        public Color cloudColor = new Color(1f, 1f, 1f, 1f);
        [Range(0f, 1f)] public float cloudStrength = 0.28f;

        [Header("远景水面")]
        public Color waterShallow = new Color(0.20f, 0.55f, 0.62f, 1f);
        public Color waterDeep = new Color(0.05f, 0.24f, 0.36f, 1f);
        public Color waterHorizon = new Color(0.66f, 0.78f, 0.84f, 1f);

        [Header("后处理")]
        public ArenaTonemapMode tonemapMode = ArenaTonemapMode.Neutral;
        [Range(0f, 2f)] public float bloomIntensity = 0.35f;
        [Range(0.5f, 2f)] public float bloomThreshold = 1.05f;
        [Range(-100f, 100f)] public float colorSaturation = 18f;
        [Range(-100f, 100f)] public float colorContrast = 10f;
        [Range(0f, 1f)] public float vignetteIntensity = 0.12f;
        [Range(-2f, 2f)] public float postExposure = 0.08f;
        [Range(0f, 1f)] public float ssaoIntensity = 0.28f;

        [Header("卡通描边（PC 全屏描边）")]
        public bool sceneOutlineEnabled = true;
        public Color outlineColor = new Color(0.09f, 0.10f, 0.13f, 1f);
        [Range(0.5f, 4f)] public float outlineThickness = 1.6f;
        [Range(0.001f, 0.2f)] public float outlineDepthThreshold = 0.02f;
        [Range(0.02f, 1f)] public float outlineNormalThreshold = 0.35f;
        public float outlineFadeStart = 25f;
        public float outlineFadeEnd = 65f;

        [Header("装饰密度")]
        [Tooltip("1 = 完整道具；0.5 = 只保留主要掩体与出生区道具（移动端可降）")]
        [Range(0f, 1f)] public float decorationDensity = 1f;
        public bool buildBackdrop = true;
    }
}