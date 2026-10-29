using P1.Core;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 正交俯视相机控制器：平滑跟随主角 + 向瞄准方向 Look-ahead 偏移 + 爆炸/大招微震屏。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TopDownCameraFollow : MonoBehaviour
    {
        public static TopDownCameraFollow Instance { get; private set; }

        [SerializeField] private Transform target;
        [SerializeField] private GameConfigSO gameConfig;

        private Camera _cam;
        private Vector3 _currentVelocity;
        private Vector3 _aimGroundPoint;
        private float _trauma;
        private float _shakeRoll;
        private bool _isSubAimActive;

        public Camera CameraComponent => _cam;
        public bool IsSubAimActive => _isSubAimActive;

        private void Awake()
        {
            Instance = this;
            _cam = GetComponent<Camera>();
            ApplyCameraSettings();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Configure(Transform followTarget, GameConfigSO config)
        {
            target = followTarget;
            gameConfig = config;
            ApplyCameraSettings();
            SnapImmediately();
        }

        public void SetAimGroundPoint(Vector3 worldAimPoint)
        {
            _aimGroundPoint = worldAimPoint;
        }

        /// <summary>
        /// 副武器抛物线瞄准或远射程瞄准时动态激活：平滑扩展视野 Look-ahead 距离，
        /// 让远端 2.7m 爆炸红圈落点清晰可见。
        /// </summary>
        public void SetSubAimActive(bool active)
        {
            _isSubAimActive = active;
        }

        /// <summary>
        /// 安全将世界坐标转换为屏幕像素坐标，供 HUD/准星等组件使用。
        /// </summary>
        public Vector3 WorldToScreenPoint(Vector3 worldPos)
        {
            return _cam != null ? _cam.WorldToScreenPoint(worldPos) : Vector3.zero;
        }

        /// <summary>
        /// 累加震屏创伤（0~1）。多次伤害叠加而非重置，符合 Inkwave 的 trauma 模型。
        /// </summary>
        public void AddTrauma(float amount)
        {
            _trauma = Mathf.Clamp01(_trauma + amount);
        }

        /// <summary>
        /// 按世界坐标距离精准附加震屏（P6-A）：用于大招砸地、手雷爆炸等瞬时冲击。
        /// 半径内按距离平方衰减，核心处取 <paramref name="maxTrauma"/>，边缘保留 30% 强度；
        /// 距离以本地玩家为参照，保证「离得越近越震」且 Bot 的爆炸不会无故晃玩家的屏幕。
        /// </summary>
        public void AddTraumaFromPoint(Vector3 worldPoint, float maxTrauma, float falloffRadius = -1f)
        {
            float radius = falloffRadius > 0f
                ? falloffRadius
                : (gameConfig != null ? gameConfig.shakeExplosionRadius : 14f);
            if (radius <= 0.01f || maxTrauma <= 0f)
                return;

            Vector3 focus = target != null ? target.position : transform.position;
            Vector3 delta = worldPoint - focus;
            delta.y = 0f;

            float distance01 = Mathf.Clamp01(delta.magnitude / radius);
            float falloff = 1f - distance01;
            falloff *= falloff;
            AddTrauma(Mathf.Clamp01(maxTrauma) * Mathf.Lerp(0.3f, 1f, falloff));
        }

        public void SnapImmediately()
        {
            if (target == null) return;
            Vector3 offset = gameConfig != null ? gameConfig.cameraOffset : new Vector3(0f, 9.2f, -8.2f);
            transform.position = target.position + offset;
            transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
        }

        private void ApplyCameraSettings()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            bool usePerspective = gameConfig != null ? gameConfig.usePerspective : true;
            _cam.orthographic = !usePerspective;
            if (usePerspective)
            {
                _cam.fieldOfView = gameConfig != null ? gameConfig.cameraFov : 40f;
            }
            else
            {
                _cam.orthographicSize = gameConfig != null ? gameConfig.cameraOrthoSize : 4.4f;
            }
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 100f;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            if (_cam != null && gameConfig != null)
            {
                bool wantPerspective = gameConfig.usePerspective;
                if (_cam.orthographic == wantPerspective)
                {
                    _cam.orthographic = !wantPerspective;
                }

                if (_cam.orthographic)
                {
                    if (Mathf.Abs(_cam.orthographicSize - gameConfig.cameraOrthoSize) > 0.01f)
                    {
                        _cam.orthographicSize = gameConfig.cameraOrthoSize;
                    }
                }
                else
                {
                    if (Mathf.Abs(_cam.fieldOfView - gameConfig.cameraFov) > 0.01f)
                    {
                        _cam.fieldOfView = gameConfig.cameraFov;
                    }
                }
            }

            Vector3 offset = gameConfig != null ? gameConfig.cameraOffset : new Vector3(0f, 9.2f, -8.2f);
            float baseLookAhead = gameConfig != null ? gameConfig.cameraLookAheadMax : 1.5f;
            float maxLookAhead = _isSubAimActive ? baseLookAhead * 1.55f : baseLookAhead;
            float smoothTime = gameConfig != null ? gameConfig.cameraSmoothTime : 0.10f;

            Vector3 toAim = _aimGroundPoint - target.position;
            toAim.y = 0f;
            float lookAheadRatio = _isSubAimActive ? 0.38f : 0.25f;
            Vector3 lookAhead = Vector3.ClampMagnitude(toAim * lookAheadRatio, maxLookAhead);

            Vector3 desiredPos = target.position + lookAhead + offset;
            Vector3 smoothed = Vector3.SmoothDamp(transform.position, desiredPos, ref _currentVelocity, smoothTime);

            ApplyShake(ref smoothed);

            transform.position = smoothed;
            transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up) *
                                 Quaternion.Euler(0f, 0f, _shakeRoll);
        }

        /// <summary>
        /// Trauma 驱动的三层高频震颤（水平 X/Z + 镜头 Roll）：
        /// 用 trauma² 做响应曲线（小伤害几乎不动、大爆炸才有劲），
        /// 用层叠正弦代替逐帧随机噪声（不会嗡嗡抖动），并用真实时间推进
        /// 让 Hitstop 定格期间震屏仍在继续，冲击定格与震颤同时被读到。
        /// </summary>
        private void ApplyShake(ref Vector3 position)
        {
            float decay = gameConfig != null ? gameConfig.shakeDecayPerSecond : 2.4f;
            float maxOffset = gameConfig != null ? gameConfig.shakeMaxOffset : 0.42f;
            float maxRoll = gameConfig != null ? gameConfig.shakeMaxRollDegrees : 1.3f;
            float frequency = gameConfig != null ? gameConfig.shakeFrequency : 38f;

            if (_trauma <= 0.001f)
            {
                _shakeRoll = Mathf.MoveTowards(_shakeRoll, 0f, 20f * Time.unscaledDeltaTime);
                return;
            }

            float t = Time.unscaledTime;
            float shake = _trauma * _trauma;

            float noiseX = Mathf.Sin(t * frequency) * 0.62f + Mathf.Sin(t * frequency * 1.63f + 1.7f) * 0.38f;
            float noiseZ = Mathf.Sin(t * frequency * 0.86f + 0.9f) * 0.58f + Mathf.Sin(t * frequency * 1.47f + 2.4f) * 0.42f;
            float noiseR = Mathf.Sin(t * frequency * 0.71f + 2.1f) * 0.6f + Mathf.Sin(t * frequency * 1.31f + 0.4f) * 0.4f;

            position += new Vector3(noiseX * maxOffset, 0f, noiseZ * maxOffset) * shake;
            _shakeRoll = noiseR * maxRoll * shake;

            _trauma = Mathf.Max(0f, _trauma - decay * Time.unscaledDeltaTime);
        }
    }
}
