using P1.Core;
using P1.Ink;
using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 俯视角地面动态投射准星：
    /// 在右摇杆/鼠标瞄准的地面交点处，投射微光落点光斑，真实反馈当前装备武器形态、射程与散布角度 (Spread)。
    /// </summary>
    public class GroundAimReticle : MonoBehaviour
    {
        private Transform _root;

        // 三种武器形态子节点
        private Transform _shooterNode;
        private Transform _rollerNode;
        private Transform _chargerNode;

        // 小枪节点组件
        private MeshRenderer _shooterCenterRenderer;
        private MeshRenderer _shooterSpreadRingRenderer;
        private Transform _shooterSpreadRing;
        private Transform[] _shooterTicks = new Transform[4];
        private MeshRenderer[] _shooterTickRenderers = new MeshRenderer[4];

        // 滚筒节点组件
        private Transform _rollerBarTransform;
        private MeshRenderer _rollerBarRenderer;

        // 蓄力狙节点组件
        private Transform _chargerOuterRing;
        private Transform _chargerInnerDot;
        private MeshRenderer _chargerOuterRenderer;
        private MeshRenderer _chargerInnerRenderer;

        private Material _reticleMaterial;
        // 注意：MaterialPropertyBlock 是 Unity 原生对象，不能在 MonoBehaviour 字段初始化器里 new，
        // 必须延迟到 Awake/EnsureInitialized（ApplyColor 内也有空引用兜底）。
        private MaterialPropertyBlock _block;

        private WeaponKind _activeKind = WeaponKind.Shooter;
        private float _shooterWeight = 1f;
        private float _rollerWeight = 0f;
        private float _chargerWeight = 0f;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            // 防御式初始化：即使上一次构建中途抛异常/被销毁，也能把缺失的部分补齐，
            // 杜绝 UpdateReticle → ApplyColor 拿到空 MaterialPropertyBlock 的偶发空引用。
            if (_root == null) _root = transform;
            if (_block == null) _block = new MaterialPropertyBlock();

            if (_shooterNode != null && _rollerNode != null && _chargerNode != null)
                return;

            if (_reticleMaterial == null)
                _reticleMaterial = CreateSafeMaterial("GroundReticleMat");

            if (_shooterNode == null) BuildShooterVisual();
            if (_rollerNode == null) BuildRollerVisual();
            if (_chargerNode == null) BuildChargerVisual();
        }

        private static Material CreateSafeMaterial(string matName)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Hidden/InternalErrorShader");
            return shader != null ? new Material(shader) { name = matName } : null;
        }

        private void OnDestroy()
        {
            if (_reticleMaterial != null) Destroy(_reticleMaterial);
        }

        private void BuildShooterVisual()
        {
            var go = new GameObject("Visual_Shooter");
            go.transform.SetParent(_root, false);
            _shooterNode = go.transform;

            // 1. 中心瞄准微环 (半径 0.22m)
            var centerGo = new GameObject("CenterRing");
            centerGo.transform.SetParent(_shooterNode, false);
            var mf = centerGo.AddComponent<MeshFilter>();
            mf.sharedMesh = VfxPool.BuildRingMesh(24, 0.65f, 0.22f);
            _shooterCenterRenderer = centerGo.AddComponent<MeshRenderer>();
            SetupRenderer(_shooterCenterRenderer);

            // 2. 扩散指示环 (基准半径 0.45m，动态按 Spread 缩放)
            var ringGo = new GameObject("SpreadRing");
            ringGo.transform.SetParent(_shooterNode, false);
            var mfRing = ringGo.AddComponent<MeshFilter>();
            mfRing.sharedMesh = VfxPool.BuildRingMesh(32, 0.90f, 0.45f);
            _shooterSpreadRingRenderer = ringGo.AddComponent<MeshRenderer>();
            SetupRenderer(_shooterSpreadRingRenderer);
            _shooterSpreadRing = ringGo.transform;

            // 3. 四个发散指示刻度角 (上下左右 Tick Marks)
            Mesh tickMesh = BuildQuadMesh(0.04f, 0.14f);
            for (int i = 0; i < 4; i++)
            {
                var tickGo = new GameObject($"Tick_{i}");
                tickGo.transform.SetParent(_shooterNode, false);
                var mfTick = tickGo.AddComponent<MeshFilter>();
                mfTick.sharedMesh = tickMesh;
                var mr = tickGo.AddComponent<MeshRenderer>();
                SetupRenderer(mr);
                _shooterTicks[i] = tickGo.transform;
                _shooterTickRenderers[i] = mr;
                tickGo.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
            }
        }

        private void BuildRollerVisual()
        {
            var go = new GameObject("Visual_Roller");
            go.transform.SetParent(_root, false);
            _rollerNode = go.transform;

            // 宽屏横向指示条 (宽 1.9m, 厚度 0.25m, 对应滚筒 1.9m 推地墨带)
            var barGo = new GameObject("RollerBar");
            barGo.transform.SetParent(_rollerNode, false);
            var mf = barGo.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildRoundedBarMesh(1.9f, 0.22f, 8);
            _rollerBarRenderer = barGo.AddComponent<MeshRenderer>();
            SetupRenderer(_rollerBarRenderer);
            _rollerBarTransform = barGo.transform;
        }

        private void BuildChargerVisual()
        {
            var go = new GameObject("Visual_Charger");
            go.transform.SetParent(_root, false);
            _chargerNode = go.transform;

            // 外层同心圆环 (半径 0.42m)
            var outerGo = new GameObject("OuterRing");
            outerGo.transform.SetParent(_chargerNode, false);
            var mfOuter = outerGo.AddComponent<MeshFilter>();
            mfOuter.sharedMesh = VfxPool.BuildRingMesh(32, 0.88f, 0.42f);
            _chargerOuterRenderer = outerGo.AddComponent<MeshRenderer>();
            SetupRenderer(_chargerOuterRenderer);
            _chargerOuterRing = outerGo.transform;

            // 内层精准准星圆点
            var dotGo = new GameObject("InnerDot");
            dotGo.transform.SetParent(_chargerNode, false);
            var mfDot = dotGo.AddComponent<MeshFilter>();
            mfDot.sharedMesh = VfxPool.BuildRingMesh(16, 0.05f, 0.12f);
            _chargerInnerRenderer = dotGo.AddComponent<MeshRenderer>();
            SetupRenderer(_chargerInnerRenderer);
            _chargerInnerDot = dotGo.transform;
        }

        private void SetupRenderer(MeshRenderer mr)
        {
            mr.sharedMaterial = _reticleMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>
        /// 每帧更新地面准星：位置、形态切换、散布缩放与颜色反馈。
        /// </summary>
        public void UpdateReticle(
            Vector3 aimGroundPos,
            Vector3 playerPos,
            WeaponController weapon,
            InkCharacterMotor motor,
            CombatantHealth health)
        {
            EnsureInitialized();
            if (weapon == null) return;

            // 1. 贴地放置
            _root.position = new Vector3(aimGroundPos.x, aimGroundPos.y + 0.035f, aimGroundPos.z);

            Vector3 aimDir = aimGroundPos - playerPos;
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude > 0.001f)
            {
                _root.rotation = Quaternion.LookRotation(aimDir.normalized, Vector3.up);
            }

            // 2. 武器形态平滑淡入淡出 (Cross-fade weights)
            WeaponKind targetKind = weapon.CurrentWeaponKind;
            _activeKind = targetKind;
            float dt = Time.deltaTime;

            float targetShooter = targetKind == WeaponKind.Shooter ? 1f : 0f;
            float targetRoller = targetKind == WeaponKind.Roller ? 1f : 0f;
            float targetCharger = targetKind == WeaponKind.Charger ? 1f : 0f;

            _shooterWeight = Mathf.MoveTowards(_shooterWeight, targetShooter, dt * 8f);
            _rollerWeight = Mathf.MoveTowards(_rollerWeight, targetRoller, dt * 8f);
            _chargerWeight = Mathf.MoveTowards(_chargerWeight, targetCharger, dt * 8f);

            // 3. 计算颜色状态反馈
            Color baseColor;
            if (health != null && !health.IsAlive)
            {
                baseColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
            }
            else if (motor != null && motor.CurrentInk < 8f)
            {
                // 严重缺墨闪红
                float pulse = 0.55f + Mathf.PingPong(Time.time * 8f, 0.45f);
                baseColor = new Color(1f, 0.22f, 0.22f, pulse);
            }
            else if (weapon.IsSpecialReady)
            {
                // 大招就绪金光
                float pulse = 0.8f + Mathf.Sin(Time.time * 9f) * 0.2f;
                baseColor = new Color(1f, 0.92f, 0.2f, pulse);
            }
            else
            {
                // 队伍色微光
                Color teamCol = InkSurfaceController.Instance != null && health != null
                    ? InkSurfaceController.Instance.GetTeamColor(health.Team)
                    : Color.white;
                baseColor = Color.Lerp(teamCol, Color.white, 0.28f);
                baseColor.a = 0.88f;
            }

            // 潜行时地面光斑微弱半透明
            if (motor != null && motor.IsSwimming)
            {
                baseColor.a *= 0.35f;
            }

            // 4. 更新小枪形态：散布角与 Bloom 扩散
            if (_shooterWeight > 0.01f)
            {
                if (!_shooterNode.gameObject.activeSelf) _shooterNode.gameObject.SetActive(true);
                float spreadDeg = weapon.CurrentSpreadDegrees;
                // 距离与散布半角放大系数：1.2° ~ 5.5° -> scale 0.95 ~ 1.75
                float spreadScale = 1.0f + (spreadDeg - 1.2f) * 0.16f;
                _shooterSpreadRing.localScale = new Vector3(spreadScale, 1f, spreadScale);

                // 4 个刻度角向外扩散
                float tickDist = 0.48f * spreadScale;
                for (int i = 0; i < 4; i++)
                {
                    _shooterTicks[i].localPosition = _shooterTicks[i].localRotation * Vector3.forward * tickDist;
                }

                Color col = baseColor;
                col.a *= _shooterWeight;
                ApplyColor(_shooterCenterRenderer, col);
                ApplyColor(_shooterSpreadRingRenderer, col);
                for (int i = 0; i < 4; i++) ApplyColor(_shooterTickRenderers[i], col);

                _shooterNode.localScale = Vector3.one * _shooterWeight;
            }
            else if (_shooterNode.gameObject.activeSelf)
            {
                _shooterNode.gameObject.SetActive(false);
            }

            // 5. 更新滚筒形态：1.9m 宽幅指示
            if (_rollerWeight > 0.01f)
            {
                if (!_rollerNode.gameObject.activeSelf) _rollerNode.gameObject.SetActive(true);
                float rollWidth = weapon.Config != null ? weapon.Config.rollWidth : 1.9f;
                _rollerBarTransform.localScale = new Vector3(rollWidth / 1.9f, 1f, 1f);

                Color col = baseColor;
                col.a *= _rollerWeight;
                ApplyColor(_rollerBarRenderer, col);
                _rollerNode.localScale = Vector3.one * _rollerWeight;
            }
            else if (_rollerNode.gameObject.activeSelf)
            {
                _rollerNode.gameObject.SetActive(false);
            }

            // 6. 更新蓄力狙形态：同心圆与蓄力白炽光
            if (_chargerWeight > 0.01f)
            {
                if (!_chargerNode.gameObject.activeSelf) _chargerNode.gameObject.SetActive(true);
                float ch01 = weapon.Charge01;

                // 蓄力时光斑收缩聚能并变亮
                float scale = Mathf.Lerp(1.2f, 0.82f, ch01);
                _chargerOuterRing.localScale = new Vector3(scale, 1f, scale);

                Color outerCol = Color.Lerp(baseColor, Color.white, ch01 * 0.7f);
                outerCol.a *= _chargerWeight;
                Color innerCol = ch01 >= 0.99f ? Color.white : outerCol;

                ApplyColor(_chargerOuterRenderer, outerCol);
                ApplyColor(_chargerInnerRenderer, innerCol);
                _chargerNode.localScale = Vector3.one * _chargerWeight;
            }
            else if (_chargerNode.gameObject.activeSelf)
            {
                _chargerNode.gameObject.SetActive(false);
            }
        }

        private void ApplyColor(MeshRenderer mr, Color color)
        {
            if (mr == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            _block.SetColor(BaseColorProp, color);
            _block.SetColor(ColorProp, color);
            mr.SetPropertyBlock(_block);
        }

        private static Mesh BuildQuadMesh(float width, float length)
        {
            Mesh m = new Mesh { name = "QuadTickMesh" };
            float hw = width * 0.5f;
            m.vertices = new Vector3[]
            {
                new Vector3(-hw, 0f, 0f),
                new Vector3(hw, 0f, 0f),
                new Vector3(hw, 0f, length),
                new Vector3(-hw, 0f, length)
            };
            m.normals = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            return m;
        }

        private static Mesh BuildRoundedBarMesh(float width, float depth, int endSegments)
        {
            Mesh m = new Mesh { name = "RoundedBarMesh" };
            float hw = width * 0.5f;
            float hd = depth * 0.5f;

            // 中心矩形面 (4 顶点, 2 三角形)
            Vector3[] verts = new Vector3[]
            {
                new Vector3(-hw, 0f, -hd),
                new Vector3(hw, 0f, -hd),
                new Vector3(hw, 0f, hd),
                new Vector3(-hw, 0f, hd)
            };
            int[] tris = new int[] { 0, 2, 1, 0, 3, 2 };

            m.vertices = verts;
            m.normals = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = tris;
            return m;
        }
    }
}
