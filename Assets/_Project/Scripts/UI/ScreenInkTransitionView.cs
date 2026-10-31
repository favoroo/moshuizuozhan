using System;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 全屏墨水漫灌与复活虹膜撕裂转场视图（P1/UI/ScreenInkTransitionView）。
    /// 
    /// 参考 INKWAVE `src/fx/screenfx.js` 的 splatted flood 与 iris reveal 状态机：
    /// - 阵亡：敌墨由四周快速漫灌（~0.55s），具有粘稠前缘与顶部垂滴，压住画面；
    /// - 复活：己方墨水从中心撕开有机虹膜破口（~0.86s），外沿强发光，平滑放行底层 HUD 与场景；
    /// - 受击反馈：冲击形变（Punch 临界阻尼弹簧）与边缘色差（Chroma 径向分离）。
    /// 
    /// 全部采用 unscaledDeltaTime 推进，不受 Hitstop 与暂停 (timeScale=0) 干扰；
    /// 转场结束与空闲时自动关闭 Graphic，零开销。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(Image))]
    public class ScreenInkTransitionView : MonoBehaviour
    {
        public enum TransitionMode
        {
            None,
            FloodIn,
            Reveal,
            FadeOut
        }

        private const string ShaderName = "P1/UI/ScreenInkTransition";

        private static readonly int InkColorProp = Shader.PropertyToID("_InkColor");
        private static readonly int HoleRimColorProp = Shader.PropertyToID("_HoleRimColor");
        private static readonly int FloodProp = Shader.PropertyToID("_Flood");
        private static readonly int FloodDripProp = Shader.PropertyToID("_FloodDrip");
        private static readonly int HoleProp = Shader.PropertyToID("_Hole");
        private static readonly int AspectProp = Shader.PropertyToID("_Aspect");
        private static readonly int PunchProp = Shader.PropertyToID("_Punch");
        private static readonly int PunchCenterProp = Shader.PropertyToID("_PunchCenter");
        private static readonly int ChromaProp = Shader.PropertyToID("_Chroma");

        [SerializeField] private Image overlayImage;
        [SerializeField] private Canvas parentCanvas;

        private Material _material;
        private bool _built;
        private bool _shaderMissingLogged;
        private float _lastAspect = -1f;

        private TransitionMode _mode = TransitionMode.None;
        private float _modeTimer;
        private float _floodValue;
        private float _floodDripValue;
        private float _holeValue;

        private float _punch;
        private float _punchV;
        private Vector2 _punchCenter = new Vector2(0.5f, 0.5f);
        private float _chroma;

        private Color _currentInkColor;
        private Color _currentRimColor;
        private MatchManager _phaseMatch;
        private bool _phaseSubscribed;
        private CombatantHealth _subscribedLocalHealth;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private float _debugPreviewUntil;
#endif

        public TransitionMode CurrentMode => _mode;
        public float FloodCoverage => _floodValue;
        public float IrisHoleRadius => _holeValue;
        public float PunchAmount => _punch;
        public float ChromaAmount => _chroma;
        public bool IsShowing => overlayImage != null && overlayImage.enabled;

        private void Awake()
        {
            parentCanvas = GetComponentInParent<Canvas>();
            EnsureBuilt();
        }

        private void OnEnable()
        {
            CombatantHealth.DamageApplied += HandleDamageApplied;
            SubscribePhaseChanged();
        }

        private void OnDisable()
        {
            CombatantHealth.DamageApplied -= HandleDamageApplied;
            UnsubscribePhaseChanged();
            UnsubscribeLocalHealth();
            ResetImmediately();
            if (overlayImage != null) overlayImage.enabled = false;
        }

        private void OnDestroy()
        {
            UnsubscribePhaseChanged();
            UnsubscribeLocalHealth();

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        private void SubscribePhaseChanged()
        {
            if (_phaseSubscribed) return;

            _phaseMatch = MatchManager.Instance != null
                ? MatchManager.Instance
                : FindAnyObjectByType<MatchManager>();
            if (_phaseMatch != null)
            {
                _phaseMatch.PhaseChanged += HandlePhaseChanged;
                _phaseSubscribed = true;
            }
        }

        private void UnsubscribePhaseChanged()
        {
            if (_phaseSubscribed && _phaseMatch != null)
            {
                _phaseMatch.PhaseChanged -= HandlePhaseChanged;
            }

            _phaseSubscribed = false;
            _phaseMatch = null;
        }

        private void HandlePhaseChanged(MatchManager.Phase phase)
        {
            if (phase == MatchManager.Phase.Ready)
            {
                ResetImmediately();
            }
            else if (phase == MatchManager.Phase.Finished)
            {
                FadeOut();
            }
        }

        private void CheckLocalHealthSubscription()
        {
            PlayerController player = PlayerController.LocalPlayer;
            CombatantHealth health = player != null ? player.Health : null;
            if (health != _subscribedLocalHealth)
            {
                UnsubscribeLocalHealth();
                if (health != null)
                {
                    _subscribedLocalHealth = health;
                    _subscribedLocalHealth.OnKilled += HandleLocalPlayerKilled;
                    _subscribedLocalHealth.OnRespawned += HandleLocalPlayerRespawned;
                }
            }
        }

        private void UnsubscribeLocalHealth()
        {
            if (_subscribedLocalHealth != null)
            {
                _subscribedLocalHealth.OnKilled -= HandleLocalPlayerKilled;
                _subscribedLocalHealth.OnRespawned -= HandleLocalPlayerRespawned;
                _subscribedLocalHealth = null;
            }
        }

        private void HandleLocalPlayerKilled(CombatantHealth victim, Team killerTeam)
        {
            Color enemyCol = ResolveEnemyColor();
            TriggerDeathFlood(enemyCol);
        }

        private void HandleLocalPlayerRespawned(CombatantHealth victim)
        {
            Color ownCol = ResolveOwnColor();
            TriggerRespawnReveal(ownCol);
        }

        public void EnsureBuilt()
        {
            if (_built) return;

            try
            {
                var rt = transform as RectTransform;
                if (rt == null) rt = gameObject.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                if (overlayImage == null) overlayImage = GetComponent<Image>();
                if (overlayImage == null) overlayImage = gameObject.AddComponent<Image>();

                overlayImage.sprite = UIFlatSprite.White;
                overlayImage.type = Image.Type.Simple;
                overlayImage.preserveAspect = false;
                overlayImage.color = Color.white;
                overlayImage.raycastTarget = false;
                overlayImage.maskable = false;

                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    if (!_shaderMissingLogged)
                    {
                        _shaderMissingLogged = true;
                        Debug.LogWarning($"[ScreenInkTransition] 找不到 Shader {ShaderName}，转场特效已停用。");
                    }

                    overlayImage.enabled = false;
                    return;
                }

                _material = new Material(shader)
                {
                    name = "ScreenInkTransition (Runtime)",
                    hideFlags = HideFlags.DontSave
                };

                overlayImage.material = _material;
                overlayImage.enabled = false;

                if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
                _built = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ScreenInkTransition] 构建屏幕转场视图失败：" + e.Message);
                _built = false;
            }
        }

        /// <summary>
        /// 触发阵亡敌墨漫灌全屏。
        /// </summary>
        public void TriggerDeathFlood(Color enemyColor)
        {
            if (!_built) EnsureBuilt();
            if (!_built || overlayImage == null || _material == null) return;

            transform.SetAsLastSibling(); // 压住 HUD 与场景
            _mode = TransitionMode.FloodIn;
            _modeTimer = 0f;
            _holeValue = 0f;
            _currentInkColor = ToLinear(enemyColor);

            _material.SetColor(InkColorProp, _currentInkColor);
            _material.SetFloat(HoleProp, 0f);

            if (!overlayImage.enabled) overlayImage.enabled = true;
        }

        /// <summary>
        /// 触发复活己方墨水虹膜撕裂开屏。
        /// </summary>
        public void TriggerRespawnReveal(Color ownColor)
        {
            if (!_built) EnsureBuilt();
            if (!_built || overlayImage == null || _material == null) return;

            _mode = TransitionMode.Reveal;
            _modeTimer = 0f;
            _floodValue = 1f;
            _floodDripValue = 0.05f;
            _holeValue = 0.0001f;

            _currentInkColor = ToLinear(ownColor);
            Color rim = Color.Lerp(ownColor, Color.white, 0.55f) * 1.6f;
            _currentRimColor = ToLinear(rim);

            _material.SetColor(InkColorProp, _currentInkColor);
            _material.SetColor(HoleRimColorProp, _currentRimColor);
            _material.SetFloat(FloodProp, 1f);
            _material.SetFloat(HoleProp, _holeValue);

            if (!overlayImage.enabled) overlayImage.enabled = true;
        }

        public void FadeOut()
        {
            if (_mode == TransitionMode.None) return;
            _mode = TransitionMode.FadeOut;
        }

        public void ResetImmediately()
        {
            _mode = TransitionMode.None;
            _modeTimer = 0f;
            _floodValue = 0f;
            _floodDripValue = 0f;
            _holeValue = 0f;
            _punch = 0f;
            _punchV = 0f;
            _chroma = 0f;
            if (overlayImage != null && overlayImage.enabled)
            {
                overlayImage.enabled = false;
            }
        }

        /// <summary>
        /// 受击冲击：画面形变抽动与边缘色相分离。
        /// </summary>
        public void RegisterHit(Vector3 fromWorldDir, float amount)
        {
            float normAmount = Mathf.Clamp(amount, 0.1f, 2.0f);
            _punchV += -0.035f * normAmount;
            _chroma = Mathf.Min(1.4f, _chroma + 0.8f * normAmount);

            Camera cam = Camera.main;
            if (cam != null && fromWorldDir.sqrMagnitude > 0.01f)
            {
                Vector3 screenDir = cam.transform.InverseTransformDirection(fromWorldDir);
                _punchCenter = new Vector2(
                    Mathf.Clamp01(0.5f - screenDir.x * 0.15f),
                    Mathf.Clamp01(0.5f - screenDir.y * 0.15f));
            }
            else
            {
                _punchCenter = new Vector2(0.5f, 0.5f);
            }

            if (!_built) EnsureBuilt();
            if (overlayImage != null && !overlayImage.enabled)
            {
                overlayImage.enabled = true;
            }
        }

        private void HandleDamageApplied(CombatantHealth victim, CombatantHealth attacker, float damage, bool killed)
        {
            if (!CombatantHealth.IsLocalPlayer(victim)) return;

            Vector3 fromDir = Vector3.forward;
            if (attacker != null)
            {
                fromDir = attacker.transform.position - victim.transform.position;
            }

            RegisterHit(fromDir, damage / 50f);
        }

        private void Update()
        {
            CheckLocalHealthSubscription();
            // 采用 unscaledDeltaTime，确保 Hitstop 与暂停时依然平滑推进转场
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// 推进模拟一帧（支持外部驱动与 EditMode 测试）。
        /// </summary>
        public void Tick(float dt)
        {
            if (!_built) EnsureBuilt();
            if (!_built || overlayImage == null || _material == null) return;

            if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null && !parentCanvas.enabled)
            {
                ResetImmediately();
                return;
            }

            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : LensInkMath.DefaultAspect;
            if (!Mathf.Approximately(aspect, _lastAspect))
            {
                _lastAspect = aspect;
                _material.SetFloat(AspectProp, aspect);
            }

            // 1. 推进受击弹簧与色差
            ScreenInkTransitionMath.StepPunchSpring(ref _punch, ref _punchV, dt);
            _chroma = ScreenInkTransitionMath.StepChroma(_chroma, dt);

            // 2. 推进漫灌与破口转场状态机
            if (_mode != TransitionMode.None)
            {
                _modeTimer += dt;

                if (_mode == TransitionMode.FloodIn)
                {
                    _floodValue = ScreenInkTransitionMath.CalculateFloodCoverage(_modeTimer);
                    _floodDripValue = ScreenInkTransitionMath.CalculateFloodDrip(_modeTimer);
                }
                else if (_mode == TransitionMode.Reveal)
                {
                    _floodValue = 1f;
                    _floodDripValue = 0.05f;
                    _holeValue = ScreenInkTransitionMath.CalculateIrisHole(_modeTimer);

                    if (_modeTimer > 0.86f)
                    {
                        _mode = TransitionMode.None;
                        _floodValue = 0f;
                        _holeValue = 0f;
                    }
                }
                else if (_mode == TransitionMode.FadeOut)
                {
                    _floodValue = Mathf.Max(0f, _floodValue - dt * 2.5f);
                    if (_floodValue <= 0.001f)
                    {
                        _mode = TransitionMode.None;
                    }
                }
            }

            // 3. 判断是否需要绘制
            bool active = _mode != TransitionMode.None || _chroma > 0.002f || Mathf.Abs(_punch) > 0.001f;

            if (!active)
            {
                if (overlayImage.enabled) overlayImage.enabled = false;
                return;
            }

            if (!overlayImage.enabled) overlayImage.enabled = true;

            // 4. 提交材质 Uniform
            _material.SetFloat(FloodProp, _floodValue);
            _material.SetFloat(FloodDripProp, _floodDripValue);
            _material.SetFloat(HoleProp, _holeValue);
            _material.SetFloat(PunchProp, _punch);
            _material.SetVector(PunchCenterProp, new Vector4(_punchCenter.x, _punchCenter.y, 0f, 0f));
            _material.SetFloat(ChromaProp, _chroma);
        }

        private static Color ResolveEnemyColor()
        {
            Team enemyTeam = Team.TeamB;
            PlayerController player = PlayerController.LocalPlayer;
            if (player != null && player.Health != null)
            {
                enemyTeam = player.Health.Team.GetOpponent();
            }

            InkSurfaceController surface = InkSurfaceController.Instance;
            Color col = surface != null
                ? surface.GetTeamColor(enemyTeam)
                : new Color(0.15f, 0.42f, 1.00f, 1f);

            col.a = 1f;
            return col;
        }

        private static Color ResolveOwnColor()
        {
            Team ownTeam = Team.TeamA;
            PlayerController player = PlayerController.LocalPlayer;
            if (player != null && player.Health != null)
            {
                ownTeam = player.Health.Team;
            }

            InkSurfaceController surface = InkSurfaceController.Instance;
            Color col = surface != null
                ? surface.GetTeamColor(ownTeam)
                : new Color(1.00f, 0.44f, 0.06f, 1f);

            col.a = 1f;
            return col;
        }

        private static Color ToLinear(Color color)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                color = color.linear;
            return color;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DebugPreviewDeath()
        {
            TriggerDeathFlood(ResolveEnemyColor());
        }

        public void DebugPreviewRespawn()
        {
            TriggerRespawnReveal(ResolveOwnColor());
        }
#endif
    }
}
