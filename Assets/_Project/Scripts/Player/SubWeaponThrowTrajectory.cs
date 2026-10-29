using P1.Core;
using P1.Ink;
using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 副武器「抛物线手雷」弹道预测线与地面爆炸预警圈：
    /// 实时渲染 64 点平滑抛物线虚线与末端 2.7m 爆炸范围预警红圈。
    /// 算法严格与 <see cref="InkBombTrajectoryUtility"/> 保持一致，保证预测落点与实际爆炸 100% 重合。
    /// </summary>
    public class SubWeaponThrowTrajectory : MonoBehaviour
    {
        private const int PointCount = InkBombTrajectoryUtility.TrajectoryPointCount; // 64 点
        private const float DefaultExplosionRadius = 2.7f; // 2.7m 杀伤与涂地范围

        private LineRenderer _lineRenderer;
        private Transform _landingRingRoot;
        private MeshRenderer _outerRingRenderer;
        private MeshRenderer _innerFillRenderer;

        private Material _lineMaterial;
        private Material _ringMaterial;
        private Texture2D _dashedTexture;
        private MaterialPropertyBlock _ringBlock;

        private readonly Vector3[] _trajectoryBuffer = new Vector3[PointCount];
        private Vector3 _currentLandingPos;
        private bool _isShowing;

        public bool IsShowing => _isShowing;
        public Vector3 CurrentLandingPosition => _currentLandingPos;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private void Awake()
        {
            EnsureInitialized();
            Hide();
        }

        public void EnsureInitialized()
        {
            if (_lineRenderer != null) return;
            _ringBlock = new MaterialPropertyBlock();
            InitializeMaterials();
            BuildLineRenderer();
            BuildLandingRing();
        }

        private void OnDestroy()
        {
            if (_dashedTexture != null) Destroy(_dashedTexture);
            if (_lineMaterial != null) Destroy(_lineMaterial);
            if (_ringMaterial != null) Destroy(_ringMaterial);
            if (_landingRingRoot != null) Destroy(_landingRingRoot.gameObject);
        }

        private void InitializeMaterials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Hidden/InternalErrorShader");

            // 1. 生成 32x2 黑白间隔虚线贴图
            _dashedTexture = new Texture2D(32, 2, TextureFormat.RGBA32, false)
            {
                name = "Procedural_DashedLineTex",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };
            Color[] colors = new Color[32 * 2];
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    // 前 18 像素为白色（实线），后 14 像素为透明（虚线间隙）
                    colors[y * 32 + x] = x < 18 ? Color.white : new Color(1f, 1f, 1f, 0f);
                }
            }
            _dashedTexture.SetPixels(colors);
            _dashedTexture.Apply();

            if (shader != null)
            {
                _lineMaterial = new Material(shader)
                {
                    name = "SubAimTrajectoryLineMat",
                    mainTexture = _dashedTexture
                };

                _ringMaterial = new Material(shader)
                {
                    name = "SubAimExplosionRingMat"
                };
            }
        }

        private void BuildLineRenderer()
        {
            GameObject lineObj = new GameObject("SubAim_LineRenderer");
            lineObj.transform.SetParent(transform, false);

            _lineRenderer = lineObj.AddComponent<LineRenderer>();
            _lineRenderer.positionCount = PointCount;
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.startWidth = 0.085f;
            _lineRenderer.endWidth = 0.085f;
            _lineRenderer.sharedMaterial = _lineMaterial;
            _lineRenderer.textureMode = LineTextureMode.Tile;
            _lineRenderer.alignment = LineAlignment.View;
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;
        }

        private void BuildLandingRing()
        {
            GameObject root = new GameObject("SubAim_ExplosionWarningRing");
            _landingRingRoot = root.transform;

            // 1. 外围高光警戒环 (外半径 1.0f，内径 0.94f)
            GameObject outer = new GameObject("OuterRing");
            outer.transform.SetParent(_landingRingRoot, false);
            var mfOuter = outer.AddComponent<MeshFilter>();
            mfOuter.sharedMesh = VfxPool.BuildRingMesh(48, 0.94f, 1.0f);
            _outerRingRenderer = outer.AddComponent<MeshRenderer>();
            _outerRingRenderer.sharedMaterial = _ringMaterial;
            _outerRingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _outerRingRenderer.receiveShadows = false;

            // 2. 内部微光填充圆盘 (外半径 0.94f，内径 0.0f)
            GameObject inner = new GameObject("InnerDisc");
            inner.transform.SetParent(_landingRingRoot, false);
            var mfInner = inner.AddComponent<MeshFilter>();
            mfInner.sharedMesh = VfxPool.BuildRingMesh(40, 0.02f, 0.94f);
            _innerFillRenderer = inner.AddComponent<MeshRenderer>();
            _innerFillRenderer.sharedMaterial = _ringMaterial;
            _innerFillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _innerFillRenderer.receiveShadows = false;
        }

        /// <summary>
        /// 实时更新并展示 64 点平滑抛物线与 2.7m 爆炸红圈。
        /// </summary>
        public void UpdateTrajectory(
            Vector3 startPos,
            Vector3 targetGroundPos,
            WeaponConfigSO config,
            bool hasEnoughInk,
            Team team)
        {
            EnsureInitialized();

            if (!_isShowing)
            {
                _isShowing = true;
                _lineRenderer.gameObject.SetActive(true);
                _landingRingRoot.gameObject.SetActive(true);
            }

            // 1. 严格解析实际落点
            _currentLandingPos = InkBombTrajectoryUtility.ResolveLandingPosition(startPos, targetGroundPos, config);
            float arcHeight = config != null ? config.bombArcHeight : 2.6f;
            float splatRadius = config != null ? config.bombSplatRadius : DefaultExplosionRadius;

            // 2. 采样 64 个点平滑曲线
            InkBombTrajectoryUtility.GenerateTrajectoryPoints(startPos, _currentLandingPos, arcHeight, _trajectoryBuffer);
            _lineRenderer.SetPositions(_trajectoryBuffer);

            // 虚线沿抛物线流动微动效果
            float flowOffset = -Time.time * 2.8f;
            float totalApproxLength = Vector3.Distance(startPos, _currentLandingPos) + arcHeight * 1.5f;
            _lineMaterial.mainTextureScale = new Vector2(totalApproxLength * 3.2f, 1f);
            _lineMaterial.mainTextureOffset = new Vector2(flowOffset, 0f);

            // 3. 颜色与充足墨水反馈
            Color teamColor = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(team)
                : Color.white;

            Color lineColor;
            Color ringOuterColor;
            Color ringInnerColor;

            if (hasEnoughInk)
            {
                lineColor = Color.Lerp(teamColor, Color.white, 0.45f);
                lineColor.a = 0.92f;

                // 2.7m 爆炸预警红圈：鲜明警示红橙色
                ringOuterColor = new Color(1.0f, 0.22f, 0.24f, 0.95f);
                ringInnerColor = new Color(1.0f, 0.18f, 0.20f, 0.16f);
            }
            else
            {
                // 缺墨：暗灰红闪烁
                float blink = Mathf.PingPong(Time.time * 6f, 1f);
                lineColor = Color.Lerp(new Color(0.7f, 0.2f, 0.2f, 0.45f), new Color(0.4f, 0.4f, 0.4f, 0.35f), blink);
                ringOuterColor = new Color(0.85f, 0.2f, 0.2f, 0.5f);
                ringInnerColor = new Color(0.85f, 0.1f, 0.1f, 0.08f);
            }

            _lineRenderer.startColor = lineColor;
            _lineRenderer.endColor = lineColor;

            // 4. 更新爆炸预警红圈：贴地放置与呼吸动效
            float pulse = 1.0f + Mathf.Sin(Time.time * 8f) * 0.035f;
            float dynamicRadius = splatRadius * pulse;

            _landingRingRoot.position = new Vector3(_currentLandingPos.x, _currentLandingPos.y + 0.038f, _currentLandingPos.z);
            _landingRingRoot.localScale = new Vector3(dynamicRadius, 1f, dynamicRadius);

            _ringBlock.SetColor(BaseColorProp, ringOuterColor);
            _ringBlock.SetColor(ColorProp, ringOuterColor);
            _outerRingRenderer.SetPropertyBlock(_ringBlock);

            _ringBlock.SetColor(BaseColorProp, ringInnerColor);
            _ringBlock.SetColor(ColorProp, ringInnerColor);
            _innerFillRenderer.SetPropertyBlock(_ringBlock);
        }

        public void Hide()
        {
            _isShowing = false;
            if (_lineRenderer != null && _lineRenderer.gameObject.activeSelf)
                _lineRenderer.gameObject.SetActive(false);
            if (_landingRingRoot != null && _landingRingRoot.gameObject.activeSelf)
                _landingRingRoot.gameObject.SetActive(false);
        }
    }
}
