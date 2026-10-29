using System.Collections.Generic;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 俯视角半透明遮挡剔除组件：
    /// 当角色（玩家或敌人）被该建筑物/掩体遮挡时，通过 Screen-Door Dither 网点半透明平滑淡化（默认 0.35），
    /// 并在角色周围形成清晰视野轮廓，离开后平滑恢复不透明。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class OcclusionFader : MonoBehaviour
    {
        private static readonly List<OcclusionFader> _allFaders = new List<OcclusionFader>();
        public static IReadOnlyList<OcclusionFader> All => _allFaders;

        [Header("遮挡透明度")]
        [Tooltip("被遮挡时的目标透明度 (Dither 0.35)")]
        [Range(0.05f, 1.0f)]
        [SerializeField] private float minAlpha = 0.35f;

        [Tooltip("淡入淡出速度 (越大过渡越快)")]
        [SerializeField] private float fadeSpeed = 7.0f;

        [Header("角色周围视野轮廓")]
        [Tooltip("角色周围额外镂空轮廓半径")]
        [SerializeField] private float focusRadius = 2.2f;

        [Tooltip("角色周围中心最小透明度")]
        [Range(0.05f, 1.0f)]
        [SerializeField] private float focusMinAlpha = 0.25f;

        private Renderer _renderer;
        private List<Renderer> _childRenderers;
        private MaterialPropertyBlock _propBlock;

        private float _currentAlpha = 1.0f;
        private float _targetAlpha = 1.0f;
        private float _currentFocusWeight = 0.0f;
        private float _targetFocusWeight = 0.0f;
        private Vector3 _focusWorldPos;
        private bool _isOccludingThisFrame;

        private static readonly int DitherAlphaProp = Shader.PropertyToID("_DitherAlpha");
        private static readonly int FocusPosProp = Shader.PropertyToID("_FocusPos");
        private static readonly int FocusRadiusProp = Shader.PropertyToID("_FocusRadius");
        private static readonly int FocusMinAlphaProp = Shader.PropertyToID("_FocusMinAlpha");
        private static readonly int FocusWeightProp = Shader.PropertyToID("_FocusWeight");

        public float CurrentAlpha => _currentAlpha;
        public bool IsOccluding => _isOccludingThisFrame || _currentAlpha < 0.99f;
        public float MinAlpha { get => minAlpha; set => minAlpha = value; }
        public float FadeSpeed { get => fadeSpeed; set => fadeSpeed = value; }

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _propBlock = new MaterialPropertyBlock();
            CacheRenderers();
        }

        private void OnEnable()
        {
            if (!_allFaders.Contains(this))
            {
                _allFaders.Add(this);
            }
        }

        private void OnDisable()
        {
            _allFaders.Remove(this);
            ResetToOpaque();
        }

        public void CacheRenderers()
        {
            if (_childRenderers == null)
            {
                _childRenderers = new List<Renderer>(4);
            }
            _childRenderers.Clear();

            var rends = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] != null && rends[i] != _renderer)
                {
                    _childRenderers.Add(rends[i]);
                }
            }
        }

        /// <summary>
        /// 标记当前帧该遮挡物遮挡了角色
        /// </summary>
        public void MarkOccluded(Vector3 characterWorldPos)
        {
            _isOccludingThisFrame = true;
            _focusWorldPos = characterWorldPos;
        }

        /// <summary>
        /// 每帧由 TopDownOcclusionManager 统一驱动推演
        /// </summary>
        public void EvaluateStep(float deltaTime)
        {
            _targetAlpha = _isOccludingThisFrame ? minAlpha : 1.0f;
            _targetFocusWeight = _isOccludingThisFrame ? 1.0f : 0.0f;

            float prevAlpha = _currentAlpha;
            _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, fadeSpeed * deltaTime);
            _currentFocusWeight = Mathf.MoveTowards(_currentFocusWeight, _targetFocusWeight, fadeSpeed * deltaTime);

            // 当透明度发生变化，或处于半透明状态时，刷新 MaterialPropertyBlock
            if (Mathf.Abs(prevAlpha - _currentAlpha) > 0.001f || _currentAlpha < 0.999f)
            {
                ApplyProperties();
            }

            _isOccludingThisFrame = false;
        }

        private void ApplyProperties()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_renderer == null) return;

            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(DitherAlphaProp, _currentAlpha);
            _propBlock.SetVector(FocusPosProp, _focusWorldPos);
            _propBlock.SetFloat(FocusRadiusProp, focusRadius);
            _propBlock.SetFloat(FocusMinAlphaProp, focusMinAlpha);
            _propBlock.SetFloat(FocusWeightProp, _currentFocusWeight);
            _renderer.SetPropertyBlock(_propBlock);

            // 同步给子节点渲染器（如立面涂墨 Quad）
            if (_childRenderers != null && _childRenderers.Count > 0)
            {
                for (int i = 0; i < _childRenderers.Count; i++)
                {
                    var r = _childRenderers[i];
                    if (r != null)
                    {
                        r.GetPropertyBlock(_propBlock);
                        _propBlock.SetFloat(DitherAlphaProp, _currentAlpha);
                        r.SetPropertyBlock(_propBlock);
                    }
                }
            }
        }

        public void ResetToOpaque()
        {
            _currentAlpha = 1.0f;
            _targetAlpha = 1.0f;
            _currentFocusWeight = 0.0f;
            _targetFocusWeight = 0.0f;
            _isOccludingThisFrame = false;
            ApplyProperties();
        }
    }
}
