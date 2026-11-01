using P1.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace P1.Player
{
    /// <summary>
    /// 程序化接触阴影（P30）：角色脚下贴地暗斑，钉住"脚踩在地面上"的视觉证据。
    /// 平行光 52° 仰角下真实投影整块落在身后约 0.8m，脚正下方没有压暗；短腿蛋仔在此条件下
    /// 即使脚底只离地几毫米也会读作悬浮，因此额外生成一块跟随地面的软边暗斑。
    ///
    /// 实现要点：
    /// - 零美术资产：64×64 径向 alpha 贴图 + 单 quad 网格 + URP Unlit 透明材质全部运行时生成，
    ///   8 个角色共享网格/贴图/材质，逐实例用 MaterialPropertyBlock 写不透明度；
    /// - 贴地：LateUpdate 向下射线找地面（跳过角色自身碰撞体），按地面法线对齐、抬高数毫米防 z-fighting；
    /// - 滞空：阴影留在地面，随离地高度放大并淡出；潜墨/攀墙/隐匿/阵亡时隐藏。
    /// 只读 motor/health 的公共状态，不改变任何物理与命中判定。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterContactShadow : MonoBehaviour
    {
        public const string ObjectName = "ContactShadow";

        private const float GroundProbeUp = 0.55f;
        private const float GroundProbeDistance = 4f;
        private const float AirScaleGrowth = 0.45f;
        private const float MinAlpha = 0.01f;

        private static readonly RaycastHit[] HitBuffer = new RaycastHit[8];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static Mesh _quadMesh;
        private static Texture2D _radialTexture;
        private static Material _sharedMaterial;

        private CharacterVisualController _owner;
        private InkCharacterMotor _motor;
        private CombatantHealth _health;
        private Transform _actorRoot;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _propertyBlock;

        /// <summary>为角色根节点创建（或取回已有的）接触阴影。</summary>
        public static CharacterContactShadow Create(Transform actorRoot)
        {
            if (actorRoot == null) return null;

            Transform existing = actorRoot.Find(ObjectName);
            if (existing != null)
            {
                var found = existing.GetComponent<CharacterContactShadow>();
                return found != null ? found : existing.gameObject.AddComponent<CharacterContactShadow>();
            }

            var go = new GameObject(ObjectName);
            go.transform.SetParent(actorRoot, false);
            return go.AddComponent<CharacterContactShadow>();
        }

        public void Bind(InkCharacterMotor motor, CharacterVisualController owner)
        {
            _motor = motor;
            _owner = owner;
            _actorRoot = transform.parent != null ? transform.parent : transform;
            _health = _actorRoot.GetComponentInParent<CombatantHealth>();
            EnsureVisuals();
        }

        private void Awake()
        {
            _actorRoot = transform.parent != null ? transform.parent : transform;
            if (_health == null) _health = _actorRoot.GetComponentInParent<CombatantHealth>();
            EnsureVisuals();
        }

        private void OnDestroy()
        {
            _renderer = null;
            _propertyBlock = null;
        }

        private void EnsureVisuals()
        {
            // 只要任一运行时资源缺失就重建（域重载/上一次运行残留会让静态引用变 fake-null）
            if (_renderer != null && _propertyBlock != null) return;
            if (_quadMesh == null) _quadMesh = BuildQuadMesh();
            if (_radialTexture == null) _radialTexture = BuildRadialTexture();
            if (_sharedMaterial == null) _sharedMaterial = BuildMaterial(_radialTexture);

            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = _quadMesh;

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _sharedMaterial;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _renderer.enabled = false;

            _propertyBlock = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (_renderer == null) return;
            if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();

            CharacterAnimConfigSO cfg = _owner != null ? _owner.AnimConfig : null;
            if (cfg == null || !cfg.contactShadowEnabled || !IsBodyVisible())
            {
                Hide();
                return;
            }

            if (_motor == null && _owner != null)
            {
                _motor = _owner.GetComponentInParent<InkCharacterMotor>();
            }

            if (_actorRoot == null) _actorRoot = transform.parent != null ? transform.parent : transform;

            // 向下找地面：从角色身体中部往下打，跳过自身碰撞体（射线起点在胶囊内，正常也不会命中自己）
            Vector3 origin = _actorRoot.position + Vector3.up * GroundProbeUp;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, HitBuffer,
                GroundProbeDistance, ~0, QueryTriggerInteraction.Ignore);

            float bestDistance = float.MaxValue;
            RaycastHit hit = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = HitBuffer[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(_actorRoot)) continue;
                if (h.distance <= 0.0001f) continue;
                if (h.distance >= bestDistance) continue;
                bestDistance = h.distance;
                hit = h;
                found = true;
            }

            if (!found)
            {
                Hide();
                return;
            }

            float height = Mathf.Max(0f, _actorRoot.position.y - hit.point.y);
            float maxHeight = Mathf.Max(0.05f, cfg.contactShadowMaxHeight);
            float height01 = Mathf.Clamp01(height / maxHeight);
            if (height01 >= 1f)
            {
                Hide();
                return;
            }

            float alpha = cfg.contactShadowOpacity * (1f - height01) * (1f - height01);
            if (alpha < MinAlpha)
            {
                Hide();
                return;
            }

            Vector3 normal = hit.normal.sqrMagnitude > 0.001f ? hit.normal.normalized : Vector3.up;
            float diameter = Mathf.Max(0.05f, cfg.contactShadowRadius) * 2f * (1f + height01 * AirScaleGrowth);

            transform.SetPositionAndRotation(
                hit.point + normal * Mathf.Max(0f, cfg.contactShadowGroundOffset),
                Quaternion.FromToRotation(Vector3.up, normal));
            transform.localScale = new Vector3(diameter, 1f, diameter);

            Color color = Color.black;
            color.a = alpha;
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            _renderer.SetPropertyBlock(_propertyBlock);
            _renderer.enabled = true;
        }

        private bool IsBodyVisible()
        {
            if (_health != null && !_health.IsAlive) return false;
            if (_motor == null) return true;
            if (_motor.IsWallClimbing) return false;
            // 潜墨形态整体压进墨面，脚下的接触阴影会变成墨面上的一块黑斑
            if (_motor.IsSwimming) return false;
            // 隐匿时模型对敌方完全不可见，阴影必须一起隐藏，否则等于暴露位置
            if (_motor.IsStealthed) return false;
            return true;
        }

        private void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        private static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh { name = "ContactShadowQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, -0.5f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>径向 alpha 贴图：中心不透明、边缘平滑归零（本身透明，颜色由材质 _Color 决定）。</summary>
        private static Texture2D BuildRadialTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "ContactShadowRadial",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    // 中心保留一块实心，边缘 0.3~1.0 半径平滑收软（smoothstep），贴地暗斑不出现硬边
                    float a = 1f - Mathf.SmoothStep(0.30f, 1f, r);
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Material BuildMaterial(Texture2D radial)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var material = new Material(shader) { name = "ContactShadowMat" };

            // URP Unlit 切到透明 alpha 混合（运行时改材质属性，不新增 Shader 资产依赖）
            if (shader != null && shader.name == "Universal Render Pipeline/Unlit")
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", radial);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", radial);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.black);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.black);
            return material;
        }
    }
}