using System;
using P1.Core;
using P1.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 衣柜的隔离 3D 预览台（非 MonoBehaviour，纯句柄对象，由 <see cref="LockerOverlay"/> 持有与释放）。
    ///
    /// 为什么不在原地拍真实角色：三张地图的出生点周围都有 Bot 与掩体，直接对着本地玩家架机会把杂物
    /// 一起拍进取景框。这里改为把 KidRoot / SquidRoot / WeaponRoot **克隆**到场地外
    /// <c>(0, -500, 0)</c> 的一棵新根下，再放一台只拍这块小舞台的专用相机——
    /// 不改真实角色的任何节点、也不依赖出生点环境，衣柜在地图任意位置打开都是同一个干净构图。
    ///
    /// 取景：按克隆体的 <see cref="Renderer.bounds"/> 自动算相机距离，让角色头顶到脚底占约 82% 画高，
    /// 相机带约 6° 俯角（完全平视会像证件照，略俯才有「展示台」的观感）。
    ///
    /// 唯一允许的动态是 <see cref="Rotate"/>（拖拽），**不做自动转圈**：
    /// 循环旋转既违反几何风「无循环动画」的红线，也会让玩家想细看某个角度时抓不住。
    /// </summary>
    public sealed class LockerPreviewStage : IDisposable
    {
        /// <summary>预览贴图尺寸（3:4 竖幅，与 UI 里的取景框同比例）。</summary>
        private const int TextureWidth = 768;
        private const int TextureHeight = 1024;

        /// <summary>取景参数：角色占画高比例 / 横向安全比例 / 相机垂直视场角 / 俯角。</summary>
        private const float HeightFill = 0.82f;
        private const float WidthFill = 0.88f;
        private const float FieldOfView = 26f;
        private const float CameraPitchDegrees = 6f;

        /// <summary>拖拽灵敏度：水平 1px = 0.4°，拖满一个手机屏宽约转 300°，手感接近转台。</summary>
        private const float DegreesPerDragPixel = 0.4f;

        /// <summary>远离任何地图的舞台原点（40×56m 场地最远只到 ±28m，-500 绝无遮挡物）。</summary>
        private static readonly Vector3 StageOrigin = new Vector3(0f, -500f, 0f);

        private static Mesh _sharedQuadMesh;

        private readonly Team _team;
        private readonly RectTransform _viewport;
        private readonly RenderTexture _renderTexture;

        private GameObject _stageRoot;
        private GameObject _cameraObject;
        private GameObject _rawImageObject;

        private Transform _pivot;
        private Transform _kidClone;
        private Transform _squidClone;
        private Transform _weaponClone;
        private Camera _camera;
        private RawImage _rawImage;
        private Light _stageLight;
        private MeshRenderer _contactShadow;
        private Material _contactShadowMaterial;

        private float _height = 1.3f;
        private float _width = 0.6f;
        private bool _centered;
        private bool _disposed;

        private LockerPreviewStage(RectTransform viewport, Team team, RenderTexture rt)
        {
            _viewport = viewport;
            _team = team;
            _renderTexture = rt;
        }

        /// <summary>
        /// 建台：克隆本地玩家三根视觉根 + 专用相机 + RT + RawImage。
        /// 找不到本地玩家时仍然给一张干净的墨色底图，衣柜不会因此打不开。
        /// </summary>
        public static LockerPreviewStage Create(RectTransform viewport, Team team)
        {
            if (viewport == null) return null;

            var rt = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "LockerPreviewRT",
                antiAliasing = 2, // UI 里是等比例缩小显示，2×MSAA 足够抹掉轮廓台阶
            };
            rt.Create();

            var stage = new LockerPreviewStage(viewport, team, rt);
            try
            {
                stage.Build();
            }
            catch (Exception ex)
            {
                // 预览是装饰层：建台失败只降级成空底图，绝不让整个衣柜打不开。
                Debug.LogWarning("[LockerPreviewStage] 预览台构建失败：" + ex.Message);
            }

            return stage;
        }

        // ==================== 构建 ====================

        private void Build()
        {
            _stageRoot = new GameObject("LockerPreviewStage");
            _stageRoot.transform.position = StageOrigin;
            _stageRoot.transform.rotation = Quaternion.identity;

            // 舞台是纯 3D 层级（不进 Canvas），用普通 Transform 而不是 RectTransform。
            var pivotGo = new GameObject("Pivot");
            pivotGo.transform.SetParent(_stageRoot.transform, false);
            _pivot = pivotGo.transform;

            CloneLocalPlayer();
            BuildStageLight();
            BuildCamera();
            BuildContactShadow();
            FrameCamera();
            BuildRawImage();
            // 相机保持 enabled：交给 URP 的常规渲染循环出图最稳（手动 Camera.Render 在部分
            // URP 版本上会漏掉 AdditionalCameraData，预览变纯黑）。只有衣柜打开期间多渲一张 768×1024，
            // 面板入场从 alpha 0 淡入，首帧空白不可见。
        }

        /// <summary>
        /// 克隆本地玩家的三个视觉根。保留各自的 localPosition / localRotation / localScale：
        /// KidRoot 的局部下沉（贴地补偿）、WeaponRoot 的链条抵消位移都靠这些值，重置成零会让角色错位。
        /// </summary>
        private void CloneLocalPlayer()
        {
            PlayerController player = PlayerController.LocalPlayer;
            if (player == null)
            {
                return; // 没有本地玩家（例如从首页直接打开）：只留墨色底图
            }

            // 注意：CharacterVisualController 挂在 Player 的**子节点** VisualRoot 上（
            // Player_TeamA/VisualRoot），不是 Player 本体。这里必须用 GetComponentInChildren，
            // 否则取不到控制器 → 克隆整段跳过 → 衣柜预览只剩一张墨色空底图。
            CharacterVisualController visual = player.GetComponentInChildren<CharacterVisualController>(true);
            if (visual == null || visual.KidRoot == null)
            {
                return;
            }

            _kidClone = CloneRoot(visual.KidRoot.transform);
            if (_kidClone != null) _kidClone.gameObject.SetActive(true); // 只显示 Kid 形态

            // 乌贼形态原状态通常是关的；这里强制关掉，衣柜只讲「穿什么」。
            _squidClone = CloneRoot(visual.SquidRoot != null ? visual.SquidRoot.transform : null);
            if (_squidClone != null) _squidClone.gameObject.SetActive(false);

            // 武器根在装配期被挂到 Chest 下（抵消骨骼链偏移），克隆 KidRoot 时它已经一起被克隆了；
            // 再克隆一次会得到两把枪，因此这种情况复用 Kid 克隆里的那一份。
            Transform weaponSource = visual.WeaponRoot != null ? visual.WeaponRoot.transform : null;
            if (weaponSource != null && visual.KidRoot != null && weaponSource.IsChildOf(visual.KidRoot.transform))
            {
                string relativePath = BuildRelativePath(visual.KidRoot.transform, weaponSource);
                _weaponClone = string.IsNullOrEmpty(relativePath) ? null : _kidClone.Find(relativePath);
            }
            else
            {
                _weaponClone = CloneRoot(weaponSource);
            }
        }

        private Transform CloneRoot(Transform source)
        {
            if (source == null || _pivot == null) return null;

            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, _pivot, false);
            clone.name = source.name + " (LockerPreview)";

            // Instantiate(..., parent, false) 会保留 localScale，但 localPosition/rotation 要按世界值反算；
            // 这里显式还原源节点的局部变换，克隆体在舞台里的相对结构才与真实角色完全一致。
            clone.transform.localPosition = source.localPosition;
            clone.transform.localRotation = source.localRotation;
            clone.transform.localScale = source.localScale;
            return clone.transform;
        }

        private static string BuildRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || !target.IsChildOf(root)) return string.Empty;

            string path = target.name;
            Transform cursor = target.parent;
            while (cursor != null && cursor != root)
            {
                path = cursor.name + "/" + path;
                cursor = cursor.parent;
            }
            return cursor == root ? path : string.Empty;
        }

        /// <summary>
        /// 场景没有平行光时补一盏**点光**（不用平行光：平行光会照亮整张真实地图）。
        /// 点光射程只有几米、舞台又在 y=-500，绝不会影响到对局场地。
        /// </summary>
        private void BuildStageLight()
        {
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional) return;
            }

            var lightGo = new GameObject("LockerStageLight");
            lightGo.transform.SetParent(_stageRoot.transform, false);

            _stageLight = lightGo.AddComponent<Light>();
            _stageLight.type = LightType.Point;
            _stageLight.color = Color.white;
            _stageLight.shadows = LightShadows.None; // 预览台没有地面，投影只会变成脸上的噪点
            _stageLight.cullingMask = ~(1 << 5);     // 不照 UI 层
        }

        private void BuildCamera()
        {
            _cameraObject = new GameObject("LockerPreviewCamera");
            _cameraObject.transform.SetParent(_stageRoot.transform, false);

            _camera = _cameraObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = ComicStyle.Ink; // 深色舞台底：角色轮廓才跳出来
            _camera.targetTexture = _renderTexture;
            _camera.fieldOfView = FieldOfView;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 24f;   // 只拍这块舞台：500m 外的地图自然被裁掉
            _camera.allowHDR = false;
            _camera.allowMSAA = true;
            _camera.useOcclusionCulling = false;
            // 不拍 UI 层：ScreenSpaceCamera 画布若存在会被这台相机重复渲染一次。
            _camera.cullingMask = ~(1 << 5);
        }

        /// <summary>脚下椭圆接触阴影（黑 15%）：没有它角色会像浮在墨色空气里。</summary>
        private void BuildContactShadow()
        {
            Shader shader = Shader.Find("P1/ContactShadow");
            if (shader == null) return; // 取不到着色器就不画，绝不退化成黑方块

            var go = new GameObject("LockerContactShadow");
            go.transform.SetParent(_stageRoot.transform, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = SharedQuadMesh;

            _contactShadowMaterial = new Material(shader) { name = "LockerContactShadowMat" };
            _contactShadowMaterial.SetColor("_Color", new Color(0f, 0f, 0f, 0.15f));
            _contactShadowMaterial.SetFloat("_CoreRadius", 0.16f);
            _contactShadowMaterial.SetFloat("_EdgeRadius", 1f);

            _contactShadow = go.AddComponent<MeshRenderer>();
            _contactShadow.sharedMaterial = _contactShadowMaterial;
            _contactShadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _contactShadow.receiveShadows = false;
            _contactShadow.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _contactShadow.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private static Mesh SharedQuadMesh
        {
            get
            {
                if (_sharedQuadMesh != null) return _sharedQuadMesh;

                var mesh = new Mesh { name = "LockerShadowQuad" };
                mesh.vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(-0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, -0.5f),
                };
                mesh.uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(1f, 0f),
                };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                _sharedQuadMesh = mesh;
                return _sharedQuadMesh;
            }
        }

        /// <summary>
        /// 按克隆体的包围盒摆相机：让包围盒中心对准舞台原点、脚底贴地，再按「画高 82%」反算距离。
        /// 横向也要校验（3:4 竖幅的水平视角只有垂直的 0.75 倍），否则张手姿势会被切边。
        ///
        /// 量包围盒前先把 pivot 转到 0°：<see cref="Renderer.bounds"/> 是世界 AABB，
        /// 角色转 45° 后 AABB 会胀到 √2 倍，直接拿它算距离会让角色在换装时突然缩小一圈。
        /// </summary>
        private void FrameCamera()
        {
            if (_camera == null || _pivot == null) return;

            Quaternion rotation = _pivot.rotation;
            _pivot.rotation = Quaternion.identity;

            bool hasBounds = TryGetBounds(out Bounds bounds);
            if (!hasBounds)
            {
                // 没有克隆体（无本地玩家）：用角色度量兜底，相机仍然对着一块空舞台。
                bounds = new Bounds(StageOrigin + new Vector3(0f, CharacterMetrics.BodyHeight * 0.5f, 0f),
                    new Vector3(0.6f, CharacterMetrics.BodyHeight, 0.5f));
            }

            // 1) 水平居中 + 脚底贴地。只在首次取景做一次：之后 pivot 已在正确位置，
            //    换装时重新居中会让角色横向「跳」一下。
            if (!_centered && hasBounds)
            {
                var offset = new Vector3(
                    bounds.center.x - StageOrigin.x,
                    bounds.min.y - StageOrigin.y,
                    bounds.center.z - StageOrigin.z);
                _pivot.position -= offset;
                _centered = true;
                TryGetBounds(out bounds);
            }

            _pivot.rotation = rotation;

            if (hasBounds)
            {
                _height = Mathf.Max(0.2f, bounds.size.y);
                _width = Mathf.Max(0.15f, Mathf.Max(bounds.size.x, bounds.size.z));
            }

            // 2) 距离：垂直与水平各算一次，取更远的那个（近的那个方向会切边）
            float halfTan = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = TextureWidth / (float)TextureHeight;
            float distanceVertical = _height * 0.5f / Mathf.Max(0.05f, HeightFill * halfTan);
            float distanceHorizontal = _width * 0.5f / Mathf.Max(0.05f, WidthFill * halfTan * aspect);
            float distance = Mathf.Max(distanceVertical, distanceHorizontal) + 0.12f;

            // 3) 6° 俯角：相机略高于目标点再 LookAt，避免「证件照」式纯平视
            Vector3 target = StageOrigin + new Vector3(0f, _height * 0.5f, 0f);
            float pitch = CameraPitchDegrees * Mathf.Deg2Rad;
            var camPosition = target + new Vector3(0f, Mathf.Sin(pitch) * distance, Mathf.Cos(pitch) * distance);

            _camera.transform.position = camPosition;
            _camera.transform.rotation = Quaternion.LookRotation(target - camPosition, Vector3.up);

            if (_contactShadow != null)
            {
                // 6° 俯角几乎平视，水平的地面阴影会被压成一条看不见的线。
                // 因此把 quad 绕 X 立到 ~75°、贴在脚后：屏幕上看是脚下的一枚扁椭圆，
                // 既保住了「落地」的体积提示，又不会被透视压没。
                _contactShadow.transform.position = StageOrigin + new Vector3(0f, _height * 0.045f, -0.03f);
                _contactShadow.transform.rotation = Quaternion.Euler(75f, 0f, 0f);
                _contactShadow.transform.localScale = new Vector3(
                    Mathf.Max(0.34f, _width * 1.25f), 1f, Mathf.Max(0.16f, _width * 0.34f));
            }

            // 点光跟着取景高度走：射程刚好罩住角色，照不到舞台之外。
            if (_stageLight != null)
            {
                _stageLight.range = Mathf.Max(4f, _height * 5f);
                _stageLight.intensity = 3.2f;
                _stageLight.transform.position = StageOrigin + new Vector3(0.7f, _height * 1.1f, 1.1f);
            }
        }

        private bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            if (_pivot == null) return false;

            bool any = false;
            Renderer[] renderers = _pivot.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled) continue;

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return any;
        }

        private void BuildRawImage()
        {
            if (_viewport == null) return;

            // 视口裁切（圆角卡片里的预览不该溢出）。注意：RawImage 自身不加任何硬影——
            // 硬影要画到 Rect 之外，放进 RectMask2D 会被裁掉一半。
            if (_viewport.GetComponent<RectMask2D>() == null)
            {
                _viewport.gameObject.AddComponent<RectMask2D>();
            }

            _rawImageObject = new GameObject("PreviewRawImage",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            _rawImageObject.transform.SetParent(_viewport, false);

            var rect = (RectTransform)_rawImageObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _rawImage = _rawImageObject.GetComponent<RawImage>();
            _rawImage.texture = _renderTexture;
            _rawImage.color = Color.white;
            _rawImage.raycastTarget = true; // 拖拽热区

            // 拖拽旋转走 EventTrigger：uGUI 事件与新版 Input System 无关，鼠标 / 触屏同一套。
            var trigger = _rawImageObject.AddComponent<EventTrigger>();
            var dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            dragEntry.callback.AddListener(data =>
            {
                if (data is PointerEventData pointer) Rotate(pointer.delta.x * DegreesPerDragPixel);
            });
            trigger.triggers.Add(dragEntry);
        }

        // ==================== 对外 API ====================

        /// <summary>换装即时生效：重设外观后重新取景（发型会改变包围盒高度）。</summary>
        public void SetCosmetics(in CosmeticSet set)
        {
            if (_disposed) return;
            if (_kidClone == null) return;

            try
            {
                CharacterCosmetics.Apply(_kidClone, _squidClone, _weaponClone, set, _team);
            }
            catch (Exception ex)
            {
                // 换装是装饰性链路：某个部件缺失不该让衣柜崩掉。
                Debug.LogWarning("[LockerPreviewStage] 应用外观失败：" + ex.Message);
            }

            FrameCamera();
        }

        /// <summary>拖拽旋转（唯一的动态；水平位移 → 绕克隆根世界 Y 轴转）。</summary>
        public void Rotate(float deltaDegrees)
        {
            if (_disposed || _pivot == null) return;
            if (Mathf.Abs(deltaDegrees) < 0.0001f) return;

            // 绕世界 Y 转：相机不在 pivot 下，所以只有角色转、机位不动，观感就是「转台」。
            _pivot.Rotate(0f, deltaDegrees, 0f, Space.World);
        }

        /// <summary>销毁克隆体 / 相机 / RT / RawImage：顺序上先断引用再 Destroy，避免同帧内被别处读到。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _kidClone = null;
            _squidClone = null;
            _weaponClone = null;
            _pivot = null;

            if (_rawImage != null)
            {
                _rawImage.texture = null;
                _rawImage = null;
            }

            if (_rawImageObject != null)
            {
                UnityEngine.Object.Destroy(_rawImageObject);
                _rawImageObject = null;
            }

            if (_camera != null)
            {
                _camera.targetTexture = null;
                _camera = null;
            }

            if (_cameraObject != null)
            {
                UnityEngine.Object.Destroy(_cameraObject);
                _cameraObject = null;
            }

            _contactShadow = null;
            _stageLight = null;

            if (_stageRoot != null)
            {
                UnityEngine.Object.Destroy(_stageRoot);
                _stageRoot = null;
            }

            if (_contactShadowMaterial != null)
            {
                UnityEngine.Object.Destroy(_contactShadowMaterial);
                _contactShadowMaterial = null;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                UnityEngine.Object.Destroy(_renderTexture);
            }
        }
    }
}
