using System.Collections.Generic;
using P1.Core;
using P1.Environment;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace P1.Player
{
    /// <summary>
    /// 开赛扫场镜头导演（P41，对标 INKWAVE `CameraRig.cinematic`）：
    /// Countdown 阶段接管主相机 —— 先在高空斜角俯瞰整块场地并横向扫过，
    /// 再沿二次贝塞尔俯冲到玩家身后；t=1 的姿态精确等于跟随机位，
    /// Playing 首帧把相机交还给 <see cref="TopDownCameraFollow"/>，无 pop。
    ///
    /// 接管方式：<see cref="DefaultExecutionOrder"/> = 200，晚于跟随相机（默认 0）的 LateUpdate，
    /// 每帧「先让跟随相机算好合法位姿，再覆盖成电影机位」。这样不需要改跟随相机内部状态，
    /// 也不用和它抢初始化顺序。
    ///
    /// 为什么终点能精确对上：接管第一帧的跟随位姿直接作为路径终点（<see cref="MatchIntroCameraSetup.FinalCameraPosition"/>），
    /// Countdown 期间输入被 MatchManager 锁死、玩家不会移动，所以放手那一帧两端严格相等。
    ///
    /// 关闭方式：MatchManager.introCameraSeconds = 0（Inspector 可调，发版默认 3.4s）。
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class MatchIntroCameraDirector : MonoBehaviour
    {
        public static MatchIntroCameraDirector Instance { get; private set; }

        /// <summary>扫场镜头是否正在接管相机（调试/测试可读）。</summary>
        public bool IsSweepActive => _sweepActive;

        private MatchManager _match;
        private bool _subscribed;

        private Camera _camera;
        private TopDownCameraFollow _follow;

        /// <summary>Countdown 已进入、等下一帧跟随相机写完位姿后开拍（终点要用那一帧的真实位姿）。</summary>
        private bool _armed;

        private bool _sweepActive;
        private float _elapsed;
        private float _duration;
        private MatchIntroCameraSetup _setup;

        private Vector3 _finalPosition;
        private Quaternion _finalRotation;
        private float _baseFov;

        // 扫场期间抑制遮挡淡出：高空机位到玩家的视线会扫过大半个场地，
        // TopDownOcclusionManager 会把沿途掩体全判成「挡住角色」→ 整片场地网点闪烁。
        // 只关闭管理器并先把所有 fader 复位成不透明（用公开 API，不改 Environment 域内部状态）。
        private TopDownOcclusionManager _occlusion;
        private bool _occlusionSuppressed;

        private float _retryTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeAtRuntime()
        {
            if (!Application.isPlaying) return;
            if (Instance != null) return;

            // 常驻宿主：地图切换是 LoadScene(Single)，组件必须活过换图、在新场景重新认领相机与 MatchManager。
            // 不挂在相机上，避免改场景结构（.unity 禁止手改）。
            var hostGo = new GameObject("[MatchIntroCameraDirector]");
            DontDestroyOnLoad(hostGo);
            hostGo.AddComponent<MatchIntroCameraDirector>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            // 首个场景在 RuntimeInitializeOnLoadMethod(AfterSceneLoad) 时已经 awake 完毕，
            // 这里立刻认领一次，避免「从首页自动开赛」抢在 Update 重试之前。
            BindMatch();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                SceneManager.sceneLoaded -= HandleSceneLoaded;
            }

            StopSweep();
            UnbindMatch();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 换图：相机与 MatchManager 都是场景对象，上一份引用已失效，重新认领。
            _armed = false;
            StopSweep();
            _sweepActive = false;
            _camera = null;
            _follow = null;
            // 换图会销毁旧场景的遮挡管理器（新建的那个默认 enabled），抑制标记必须清零，
            // 否则下一局的扫场会因为「以为还在抑制」而不再关闭遮挡淡出。
            _occlusion = null;
            _occlusionSuppressed = false;
            UnbindMatch();
            BindMatch();
        }

        private void Update()
        {
            if (_match != null) return;

            _retryTimer += Time.unscaledDeltaTime;
            if (_retryTimer < 0.5f) return;
            _retryTimer = 0f;
            BindMatch();
        }

        private void BindMatch()
        {
            MatchManager match = MatchManager.Instance != null
                ? MatchManager.Instance
                : FindAnyObjectByType<MatchManager>();
            if (match == _match) return;

            UnbindMatch();
            _match = match;
            if (_match != null)
            {
                _match.PhaseChanged += HandlePhaseChanged;
                _subscribed = true;
            }
        }

        private void UnbindMatch()
        {
            if (_subscribed && _match != null)
            {
                _match.PhaseChanged -= HandlePhaseChanged;
            }

            _subscribed = false;
            _match = null;
        }

        private void HandlePhaseChanged(MatchManager.Phase phase)
        {
            if (phase == MatchManager.Phase.Countdown)
            {
                // 等下一帧：本帧 MatchManager.ResetWorld 刚把玩家瞬移回出生点，
                // 跟随相机要到它自己的 LateUpdate 才 SnapImmediately，届时才拿到真正的终点位姿。
                _armed = _match != null && _match.IntroCameraSeconds > 0.01f;
                _sweepActive = false;
                _elapsed = 0f;
                return;
            }

            // 离开 Countdown（开打 / 回主菜单 / 终场）：立即停止接管。
            // 不在这里写位姿：跟随相机本帧已经写过合法跟随位姿，直接放手即可。
            _armed = false;
            StopSweep();
        }

        private void LateUpdate()
        {
            if (!_armed && !_sweepActive) return;

            if (_follow == null)
            {
                _follow = TopDownCameraFollow.Instance != null
                    ? TopDownCameraFollow.Instance
                    : FindAnyObjectByType<TopDownCameraFollow>();
            }

            _camera = _follow != null ? _follow.CameraComponent : null;
            if (_camera == null || _follow.FollowTarget == null)
            {
                _armed = false;
                StopSweep();
                return;
            }

            if (_armed)
            {
                BeginSweep();
                return;
            }

            // 每帧刷新终点：跟随相机本帧的 LateUpdate（执行顺序 0）已经把「当前真实跟随位姿」写进
            // transform，此刻读出来就是这一帧的终点。用它覆盖路径末端，出生点哪怕有厘米级物理沉降，
            // t=1 放手那一帧也不会出现任何跳变。
            RefreshFinalPose();

            _elapsed += Time.deltaTime;
            float t = _duration > 0.0001f ? Mathf.Clamp01(_elapsed / _duration) : 1f;
            ApplyPose(MatchIntroCameraMath.Evaluate(t, _setup));

            if (t >= 1f)
            {
                // 姿态 == 跟随机位：放手，下一帧由 TopDownCameraFollow 继续写同一姿态。
                StopSweep();
            }
        }

        private void RefreshFinalPose()
        {
            Transform target = _follow.FollowTarget;
            if (target == null) return;

            _finalPosition = _camera.transform.position;
            _finalRotation = _camera.transform.rotation;
            _baseFov = _camera.fieldOfView;

            _setup.PlayerFeet = target.position;
            _setup.PlayerYawDegrees = target.eulerAngles.y;
            _setup.FinalCameraPosition = _finalPosition;
            _setup.FinalLookTarget = _finalPosition + _finalRotation * Vector3.forward * 10f;
            _setup.BaseFov = _baseFov;
        }

        /// <summary>结束接管并恢复遮挡淡出（幂等，所有退出路径都走这里）。</summary>
        private void StopSweep()
        {
            _sweepActive = false;

            if (!_occlusionSuppressed)
                return;

            _occlusionSuppressed = false;
            if (_occlusion != null)
            {
                _occlusion.enabled = true;
            }
        }

        /// <summary>
        /// 开拍：以「本帧跟随相机写出的位姿」为终点，反推扫场起点与俯冲控制点。
        /// 必须在跟随相机 LateUpdate 之后调用（执行顺序 200 保证这一点）。
        /// </summary>
        private void BeginSweep()
        {
            Transform target = _follow.FollowTarget;

            _finalPosition = _camera.transform.position;
            _finalRotation = _camera.transform.rotation;
            _baseFov = _camera.fieldOfView;

            GameConfigSO config = _match != null ? _match.Config : null;
            _setup = BuildSetup(target, config);
            _duration = Mathf.Max(0.15f, _match != null ? _match.IntroCameraSeconds : 0f);
            _elapsed = 0f;
            _armed = false;
            _sweepActive = true;

            SuppressOcclusionFading();

            // 首帧就写扫场起点：本帧屏幕仍被墨幕压满（ScreenInkTransition 在 Countdown 首帧 flood=1），
            // 这里即使有切换也看不见；写出来是为了避免下一帧出现一帧跟随视角闪烁。
            ApplyPose(MatchIntroCameraMath.Evaluate(0f, _setup));
        }

        private void SuppressOcclusionFading()
        {
            if (_occlusionSuppressed)
                return;

            if (_occlusion == null)
            {
                _occlusion = TopDownOcclusionManager.Instance != null
                    ? TopDownOcclusionManager.Instance
                    : FindAnyObjectByType<TopDownOcclusionManager>();
            }

            if (_occlusion == null)
                return;

            _occlusionSuppressed = true;
            _occlusion.enabled = false;

            // 让所有掩体先恢复不透明：管理器停摆后 EvaluateStep 不再推进，
            // 上一局残留的半透明会一直冻在扫场画面里。
            IReadOnlyList<OcclusionFader> faders = OcclusionFader.All;
            if (faders == null)
                return;

            for (int i = 0; i < faders.Count; i++)
            {
                faders[i]?.ResetToOpaque();
            }
        }

        private MatchIntroCameraSetup BuildSetup(Transform target, GameConfigSO config)
        {
            float overhead = config != null
                ? config.matchIntroOverviewHeight
                : MatchIntroCameraMath.DefaultOverviewHeight;
            float back = config != null
                ? config.matchIntroOverviewBackDistance
                : MatchIntroCameraMath.DefaultOverviewBackDistance;
            float side = config != null
                ? config.matchIntroOverviewSideOffset
                : MatchIntroCameraMath.DefaultOverviewSideOffset;
            float sweep = config != null
                ? config.matchIntroSweepDegrees
                : MatchIntroCameraMath.DefaultSweepDegrees;
            float fovBonus = config != null
                ? config.matchIntroFovBonus
                : MatchIntroCameraMath.DefaultOverviewFovBonus;
            float diveStart = config != null
                ? config.matchIntroDiveStart01
                : MatchIntroCameraMath.DefaultDiveStart01;
            float arcLift = config != null
                ? config.matchIntroArcLift
                : MatchIntroCameraMath.DefaultArcLift;

            return new MatchIntroCameraSetup
            {
                // 三张地图（货柜码头 / 海鸥港湾 / 霓虹滑板场）都以原点为中心、40×56m 共享 ArenaLayoutV2 常量。
                ArenaCenter = Vector3.zero,
                PlayerFeet = target.position,
                PlayerYawDegrees = target.eulerAngles.y,
                FinalCameraPosition = _finalPosition,
                FinalLookTarget = _finalPosition + _finalRotation * Vector3.forward * 10f,
                BaseFov = _baseFov,
                OverviewFov = Mathf.Clamp(_baseFov + Mathf.Max(0f, fovBonus), 25f, 100f),
                OverviewHeight = Mathf.Clamp(overhead, 5f, 60f),
                OverviewBackDistance = Mathf.Clamp(back, 2f, 40f),
                OverviewSideOffset = Mathf.Clamp(side, -30f, 30f),
                SweepDegrees = Mathf.Clamp(sweep, -180f, 180f),
                DiveStart01 = Mathf.Clamp(diveStart, 0.05f, 0.95f),
                ArcLift = Mathf.Clamp(arcLift, 0f, 30f)
            };
        }

        private void ApplyPose(MatchIntroCameraPose pose)
        {
            Vector3 direction = pose.LookTarget - pose.Position;
            if (direction.sqrMagnitude > 1e-6f)
            {
                _camera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }

            _camera.transform.position = pose.Position;
            _camera.fieldOfView = Mathf.Clamp(pose.Fov, 25f, 100f);
        }
    }
}
