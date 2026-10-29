using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 竞技场环境视觉风格档案（P7-B）：集中管理「集装箱码头 · 白天清爽」的配色、光照、
    /// 雾、天空与后处理参数。所有材质由 ProjectScaffolder 按此资产生成，场景里不散落硬编码色值。
    /// </summary>
    [CreateAssetMenu(fileName = "ArenaStyle", menuName = "P1/Arena Style")]
    public class ArenaStyleSO : ScriptableObject
    {
        [Header("地面与标线")]
        public Color groundBase = new Color(0.72f, 0.69f, 0.62f, 1f);
        public Color groundSeam = new Color(0.52f, 0.50f, 0.45f, 1f);
        public Color groundMarking = new Color(0.88f, 0.86f, 0.78f, 1f);
        public Color groundMarkingAccent = new Color(0.82f, 0.65f, 0.26f, 1f);
        public Color groundCliff = new Color(0.34f, 0.33f, 0.31f, 1f);
        public Color hazardYellow = new Color(0.86f, 0.68f, 0.20f, 1f);
        public Color hazardDark = new Color(0.16f, 0.16f, 0.16f, 1f);

        [Header("混凝土与钢构")]
        public Color concreteLight = new Color(0.65f, 0.63f, 0.58f, 1f);
        public Color concreteMid = new Color(0.48f, 0.46f, 0.42f, 1f);
        public Color concreteDark = new Color(0.32f, 0.31f, 0.29f, 1f);
        public Color steel = new Color(0.38f, 0.42f, 0.48f, 1f);
        public Color steelDark = new Color(0.20f, 0.22f, 0.25f, 1f);

        [Header("道具")]
        public Color wood = new Color(0.72f, 0.58f, 0.42f, 1f);
        public Color woodDark = new Color(0.48f, 0.36f, 0.24f, 1f);
        public Color foliage = new Color(0.24f, 0.45f, 0.28f, 1f);
        public Color lampGlass = new Color(1.00f, 0.85f, 0.60f, 1f);

        [Header("集装箱与边界")]
        public Color containerTeal = new Color(0.25f, 0.52f, 0.50f, 1f);
        public Color containerRust = new Color(0.62f, 0.33f, 0.24f, 1f);
        public Color containerSlate = new Color(0.37f, 0.46f, 0.57f, 1f);
        public Color containerMustard = new Color(0.79f, 0.60f, 0.24f, 1f);
        public Color boundaryConcrete = new Color(0.76f, 0.74f, 0.68f, 1f);
        public Color boundaryTrim = new Color(0.55f, 0.53f, 0.48f, 1f);
        public Color teamAPad = new Color(1.00f, 0.48f, 0.08f, 1f);
        public Color teamBPad = new Color(0.15f, 0.35f, 1.00f, 1f);

        [Header("日光与环境光")]
        public Color sunColor = new Color(1.00f, 0.96f, 0.88f, 1f);
        [Range(0.2f, 4f)] public float sunIntensity = 1.15f;
        public Vector3 sunEuler = new Vector3(50f, -40f, 0f);
        public Color ambientSky = new Color(0.42f, 0.52f, 0.65f, 1f);
        public Color ambientEquator = new Color(0.48f, 0.47f, 0.44f, 1f);
        public Color ambientGround = new Color(0.24f, 0.23f, 0.22f, 1f);
        [Range(0f, 2f)] public float ambientIntensity = 0.85f;

        [Header("雾")]
        public bool fogEnabled = true;
        public Color fogColor = new Color(0.68f, 0.76f, 0.82f, 1f);
        public float fogStartDistance = 30f;
        public float fogEndDistance = 95f;

        [Header("程序化天空")]
        public Color skyTop = new Color(0.16f, 0.42f, 0.75f, 1f);
        public Color skyHorizon = new Color(0.78f, 0.86f, 0.92f, 1f);
        public Color skyGround = new Color(0.55f, 0.62f, 0.66f, 1f);
        public Color sunDisc = new Color(1.00f, 0.95f, 0.82f, 1f);
        public Color cloudColor = new Color(1f, 1f, 1f, 1f);
        [Range(0f, 1f)] public float cloudStrength = 0.35f;

        [Header("远景水面")]
        public Color waterShallow = new Color(0.20f, 0.55f, 0.62f, 1f);
        public Color waterDeep = new Color(0.05f, 0.24f, 0.36f, 1f);
        public Color waterHorizon = new Color(0.66f, 0.78f, 0.84f, 1f);

        [Header("后处理")]
        [Range(0f, 2f)] public float bloomIntensity = 0.25f;
        [Range(0.5f, 2f)] public float bloomThreshold = 1.25f;
        [Range(-100f, 100f)] public float colorSaturation = 6f;
        [Range(-100f, 100f)] public float colorContrast = 6f;
        [Range(0f, 1f)] public float vignetteIntensity = 0.16f;
        [Range(-2f, 2f)] public float postExposure = 0.0f;

        [Header("装饰密度")]
        [Tooltip("1 = 完整道具；0.5 = 只保留主要掩体与出生区道具（移动端可降）")]
        [Range(0f, 1f)] public float decorationDensity = 1f;
        public bool buildBackdrop = true;
    }
}
