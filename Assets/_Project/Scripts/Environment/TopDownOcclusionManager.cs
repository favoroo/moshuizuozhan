using System.Collections.Generic;
using P1.Core;
using P1.Player;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 第三人称遮挡剔除管理器（P9）：
    /// 每帧从相机向角色投射视线球体检测，精准识别阻挡视线的掩体、集装箱与高台，
    /// 驱动 OcclusionFader 执行 Screen-Door Dither 半透明淡化。
    /// 默认只检测本地玩家：第三人称下相机低、掩体多，若对所有 Bot 检测会导致远处墙体全图闪烁；
    /// 相机自身已有球探针防穿墙，二者配合使用。无本地玩家（编辑模式）时回退为全员检测。
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-10)]
    public class TopDownOcclusionManager : MonoBehaviour
    {
        public static TopDownOcclusionManager Instance { get; private set; }

        [Header("检测配置")]
        [Tooltip("射线检测图层掩码")]
        [SerializeField] private LayerMask obstacleLayerMask = ~0;

        [Tooltip("球体投射检测半径")]
        [SerializeField] private float sphereCastRadius = 0.32f;

        [Tooltip("第三人称下只检测本地玩家，避免远处 Bot 背后的掩体全图闪烁；无本地玩家时自动回退全员检测")]
        [SerializeField] private bool localPlayerOnly = true;

        [Tooltip("是否在场景中自动为所有掩体添加/刷新 OcclusionFader")]
        [SerializeField] private bool autoBindFaders = true;

        private Camera _cachedCamera;
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[32];

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            EnsureCamera();
            if (autoBindFaders)
            {
                AutoBindSceneFaders();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SetCamera(Camera cam)
        {
            _cachedCamera = cam;
        }

        private void EnsureCamera()
        {
            if (_cachedCamera == null)
            {
                if (TopDownCameraFollow.Instance != null && TopDownCameraFollow.Instance.CameraComponent != null)
                {
                    _cachedCamera = TopDownCameraFollow.Instance.CameraComponent;
                }
                else
                {
                    _cachedCamera = Camera.main;
                }
            }
        }

        /// <summary>
        /// 自动为场景内带有碰撞体与渲染器的掩体挂载 OcclusionFader
        /// </summary>
        public void AutoBindSceneFaders()
        {
            var faders = FindObjectsByType<OcclusionFader>(FindObjectsSortMode.None);
            for (int i = 0; i < faders.Length; i++)
            {
                faders[i].CacheRenderers();
            }
        }

        private void LateUpdate()
        {
            EnsureCamera();
            if (_cachedCamera == null) return;

            Vector3 camPos = _cachedCamera.transform.position;

            PlayerController localPlayer = localPlayerOnly ? PlayerController.LocalPlayer : null;
            if (localPlayer != null && localPlayer.Health != null && localPlayer.Health.IsAlive)
            {
                // 第三人称：只保证镜头能看见本地玩家
                CheckCombatant(camPos, localPlayer.Health);
            }
            else
            {
                IReadOnlyList<CombatantHealth> allCombatants = CombatantHealthRegistry.All;

                if (allCombatants != null && allCombatants.Count > 0)
                {
                    for (int i = 0; i < allCombatants.Count; i++)
                    {
                        CheckCombatant(camPos, allCombatants[i]);
                    }
                }
                else
                {
                    // 编辑模式 / 初始阶段兜底
                    var sceneCombatants = FindObjectsByType<CombatantHealth>(FindObjectsSortMode.None);
                    for (int i = 0; i < sceneCombatants.Length; i++)
                    {
                        CheckCombatant(camPos, sceneCombatants[i]);
                    }
                }
            }

            // 驱动所有注册的 OcclusionFader 推进透明度插值
            IReadOnlyList<OcclusionFader> allFaders = OcclusionFader.All;
            if (allFaders == null || allFaders.Count == 0)
            {
                allFaders = FindObjectsByType<OcclusionFader>(FindObjectsSortMode.None);
            }
            float dt = Application.isPlaying ? Time.deltaTime : 0.05f;
            for (int i = 0; i < allFaders.Count; i++)
            {
                allFaders[i]?.EvaluateStep(dt);
            }
        }

        private void CheckCombatant(Vector3 camPos, CombatantHealth combatant)
        {
            if (combatant == null || !combatant.gameObject.activeInHierarchy || !combatant.IsAlive)
                return;

            Vector3 charRoot = combatant.transform.position;

            // 检测头部与胸腹两点，确保从全身任意高度被挡均能触发透明
            CheckLineOfSight(camPos, charRoot + Vector3.up * 1.35f, charRoot);
            CheckLineOfSight(camPos, charRoot + Vector3.up * 0.45f, charRoot);
        }

        private void CheckLineOfSight(Vector3 camPos, Vector3 targetPos, Vector3 characterRoot)
        {
            Vector3 dir = targetPos - camPos;
            float dist = dir.magnitude;
            if (dist < 0.2f) return;

            Vector3 normalizedDir = dir / dist;
            float castDist = Mathf.Max(0.01f, dist - 0.25f);

            int hitCount = Physics.SphereCastNonAlloc(
                camPos,
                sphereCastRadius,
                normalizedDir,
                _hitBuffer,
                castDist,
                obstacleLayerMask,
                QueryTriggerInteraction.Ignore);

            for (int h = 0; h < hitCount; h++)
            {
                Collider col = _hitBuffer[h].collider;
                if (col == null) continue;

                // 排除角色自身的碰撞体
                if (col.GetComponentInParent<CombatantHealth>() != null) continue;

                var fader = col.GetComponentInParent<OcclusionFader>();
                if (fader != null)
                {
                    fader.MarkOccluded(characterRoot);
                }
            }
        }
    }
}
