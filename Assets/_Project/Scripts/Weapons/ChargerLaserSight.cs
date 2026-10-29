using P1.Core;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 蓄力狙的激光指示线（纯程序化几何，零外部美术资源）：
    /// 一条从枪口指向准星的细光柱 + 地面落点圆环，颜色随队伍染色、亮度与粗细随蓄力增强。
    /// 由 <see cref="WeaponController"/> 持有，仅在蓄力时显示。
    /// </summary>
    public sealed class ChargerLaserSight
    {
        private readonly Transform _root;
        private readonly Transform _line;
        private readonly Transform _dot;
        private readonly MeshRenderer _lineRenderer;
        private readonly MeshRenderer _dotRenderer;
        private readonly Material _material;
        private readonly MaterialPropertyBlock _block;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        public ChargerLaserSight(Transform parent, string name)
        {
            var rootGo = new GameObject($"{name}_ChargerLaser");
            rootGo.transform.SetParent(parent, false);
            _root = rootGo.transform;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            _material = new Material(shader) { name = "ChargerLaserMat" };
            _block = new MaterialPropertyBlock();

            GameObject lineGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lineGo.name = "LaserLine";
            DestroySafe(lineGo.GetComponent<Collider>());
            lineGo.transform.SetParent(_root, false);
            _line = lineGo.transform;
            _lineRenderer = lineGo.GetComponent<MeshRenderer>();
            _lineRenderer.sharedMaterial = _material;
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;

            var dotGo = new GameObject("AimDot");
            dotGo.transform.SetParent(_root, false);
            var dotFilter = dotGo.AddComponent<MeshFilter>();
            dotFilter.sharedMesh = VfxPool.BuildRingMesh(24, 0.62f, 1f);
            _dotRenderer = dotGo.AddComponent<MeshRenderer>();
            _dotRenderer.sharedMaterial = _material;
            _dotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _dotRenderer.receiveShadows = false;
            _dot = dotGo.transform;

            Hide();
        }

        public void Show(Vector3 origin, Vector3 end, float charge01, Color teamColor)
        {
            if (_root == null)
                return;

            Vector3 delta = end - origin;
            float length = delta.magnitude;
            if (length < 0.05f)
            {
                Hide();
                return;
            }

            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);

            charge01 = Mathf.Clamp01(charge01);

            _line.position = origin + delta * 0.5f;
            _line.rotation = Quaternion.LookRotation(delta / length, Vector3.up);
            float width = 0.045f + 0.045f * charge01;
            _line.localScale = new Vector3(width, width, length);

            _dot.position = end + Vector3.up * 0.03f;
            _dot.rotation = Quaternion.identity;
            float dotScale = 0.42f + 0.30f * charge01;
            _dot.localScale = new Vector3(dotScale, 1f, dotScale);

            Color lineColor = Color.Lerp(teamColor, Color.white, 0.30f + 0.40f * charge01);
            SetColor(_lineRenderer, lineColor);
            SetColor(_dotRenderer, teamColor);
        }

        public void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf)
                _root.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (_root != null)
            {
                DestroySafe(_root.gameObject);
            }

            if (_material != null)
            {
                DestroySafe(_material);
            }
        }

        private static void DestroySafe(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }

        private void SetColor(MeshRenderer renderer, Color color)
        {
            if (renderer == null)
                return;

            _block.SetColor(BaseColorProp, color);
            _block.SetColor(ColorProp, color);
            renderer.SetPropertyBlock(_block);
        }
    }
}
