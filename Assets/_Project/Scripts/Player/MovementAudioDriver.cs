using P1.Core;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 角色机动音频驱动器（P1-Audio）：
    /// 挂载于本地玩家（PlayerController.LocalPlayer），
    /// 根据 InkCharacterMotor 的机动状态与位移，播放脚步、水花、鱿鱼进出水以及潜水/爬墙/敌墨灼烧循环音。
    /// 不修改 InkCharacterMotor 内部逻辑。
    /// </summary>
    [RequireComponent(typeof(InkCharacterMotor))]
    [RequireComponent(typeof(CombatantHealth))]
    public class MovementAudioDriver : MonoBehaviour
    {
        private InkCharacterMotor _motor;
        private CombatantHealth _health;

        private bool _wasSwimming;
        private bool _wasClimbing;
        private bool _wasInEnemyInk;
        private bool _wasGrounded = true;
        private Vector3 _lastPosition;
        private float _stepDistance;
        private bool _isLocalPlayer;

        private void Awake()
        {
            _motor = GetComponent<InkCharacterMotor>();
            _health = GetComponent<CombatantHealth>();
            _lastPosition = transform.position;
            _isLocalPlayer = CombatantHealth.IsLocalPlayer(_health);
        }

        private void Start()
        {
            _isLocalPlayer = CombatantHealth.IsLocalPlayer(_health);
            _lastPosition = transform.position;
        }

        private void OnDisable()
        {
            StopMovementLoops();
        }

        private void OnDestroy()
        {
            StopMovementLoops();
        }

        private void Update()
        {
            if (AudioManager.Instance == null || _motor == null || _health == null)
                return;

            bool isLocal = _isLocalPlayer || CombatantHealth.IsLocalPlayer(_health);
            if (!isLocal)
                return;

            bool isAlive = _health.IsAlive;
            if (!isAlive)
            {
                StopMovementLoops();
                _wasSwimming = false;
                _wasClimbing = false;
                _wasInEnemyInk = false;
                _wasGrounded = true;
                _stepDistance = 0f;
                _lastPosition = transform.position;
                return;
            }

            // 1. 潜水进出与游泳循环音
            bool isSwimming = _motor.IsSwimming;
            if (isSwimming != _wasSwimming)
            {
                if (isSwimming)
                {
                    AudioManager.Instance.PlaySquidIn();
                    AudioManager.Instance.PlayLoop(LoopSfxKind.Swim);
                }
                else
                {
                    AudioManager.Instance.PlaySquidOut();
                    AudioManager.Instance.StopLoop(LoopSfxKind.Swim);
                }
                _wasSwimming = isSwimming;
            }

            // 2. 爬墙循环音
            bool isClimbing = _motor.IsWallClimbing;
            if (isClimbing != _wasClimbing)
            {
                if (isClimbing)
                {
                    AudioManager.Instance.PlayLoop(LoopSfxKind.Climb);
                }
                else
                {
                    AudioManager.Instance.StopLoop(LoopSfxKind.Climb);
                }
                _wasClimbing = isClimbing;
            }

            // 3. 踩敌方墨水灼烧循环音
            bool inEnemyInk = _motor.IsInEnemyInk && _motor.IsGrounded && !isSwimming;
            if (inEnemyInk != _wasInEnemyInk)
            {
                if (inEnemyInk)
                {
                    AudioManager.Instance.PlayLoop(LoopSfxKind.EnemyInkSizzle);
                }
                else
                {
                    AudioManager.Instance.StopLoop(LoopSfxKind.EnemyInkSizzle);
                }
                _wasInEnemyInk = inEnemyInk;
            }

            // 4. 着地 / 落地声
            bool isGrounded = _motor.IsGrounded;
            if (isGrounded && !_wasGrounded)
            {
                AudioManager.Instance.PlayLand();
            }
            _wasGrounded = isGrounded;

            // 5. 步频与水花位移采样
            Vector3 curPos = transform.position;
            float dist = Vector3.Distance(curPos, _lastPosition);
            _lastPosition = curPos;

            // 过滤传送、复活或极微小抖动
            if (dist > 0.001f && dist < 6.0f)
            {
                _stepDistance += dist;
            }

            if (isSwimming)
            {
                // 高速潜墨水花声（> 5m/s 每移动约 2.2m 触发）
                if (_stepDistance >= 2.2f)
                {
                    _stepDistance = 0f;
                    if (_motor.CurrentMoveSpeed > 5.0f)
                    {
                        AudioManager.Instance.PlaySwimSplash();
                    }
                }
            }
            else if (isGrounded)
            {
                // 陆地跑步声（敌墨黏滞步幅短 0.9m，正常 1.35m）
                float stride = _motor.IsInEnemyInk ? 0.9f : 1.35f;
                if (_stepDistance >= stride)
                {
                    _stepDistance = 0f;
                    if (_motor.CurrentMoveSpeed > 0.4f)
                    {
                        AudioManager.Instance.PlayFootstep(_motor.IsInOwnInk, _motor.IsInEnemyInk);
                    }
                }
            }
        }

        private void StopMovementLoops()
        {
            if (AudioManager.Instance == null)
                return;

            AudioManager.Instance.StopLoop(LoopSfxKind.Swim);
            AudioManager.Instance.StopLoop(LoopSfxKind.Climb);
            AudioManager.Instance.StopLoop(LoopSfxKind.EnemyInkSizzle);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void AutoHookLocalPlayer()
        {
            PlayerController localPlayer = PlayerController.LocalPlayer;
            if (localPlayer != null)
            {
                EnsureDriver(localPlayer.gameObject);
            }
        }

        public static MovementAudioDriver EnsureDriver(GameObject target)
        {
            if (target == null) return null;
            var driver = target.GetComponent<MovementAudioDriver>();
            if (driver == null)
            {
                driver = target.AddComponent<MovementAudioDriver>();
            }
            return driver;
        }
    }
}
