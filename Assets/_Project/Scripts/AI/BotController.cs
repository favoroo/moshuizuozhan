using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.AI
{
    /// <summary>
    /// Bot 控制器：巡涂 / 交战 / 补给 / 残血撤退 / 死亡 五状态有限状态机。
    /// 复用与玩家完全相同的 InkCharacterMotor + WeaponController + CombatantHealth，
    /// 寻路只负责给出移动方向，位移与潜行规则和玩家一致。
    ///
    /// 拟人化三件套（对齐 Inkwave bots.js）：
    /// 1. 瞄准 = 二阶临界阻尼弹簧 + 转速上限 + 缓慢漂移/拉枪过冲误差，且「枪口真的指哪打哪」；
    /// 2. 走位 = 移动朝向按角速度上限平滑（路点切换/横向拉枪不再抽搐），速度按指数逼近；
    /// 3. 决策 = 墨水加权 A*（己方 0.5 / 中性 1.0 / 敌方 3.0）+ 残血钻己方墨潜行回血。
    /// </summary>
    [RequireComponent(typeof(InkCharacterMotor))]
    [RequireComponent(typeof(WeaponController))]
    [RequireComponent(typeof(CombatantHealth))]
    public class BotController : MonoBehaviour
    {
        public enum BotState { Paint, Engage, Refill, Dead, Retreat }

        [SerializeField] private BotConfigSO config;

        private InkCharacterMotor _motor;
        private WeaponController _weapon;
        private CombatantHealth _health;

        private readonly List<Vector3> _path = new List<Vector3>();
        private int _pathIndex;
        private float _repathTimer;
        private Vector3 _moveTarget;
        private bool _hasMoveTarget;
        private float _paintGoalTimer;

        private float _thinkTimer;
        private float _reactionTimer;
        private CombatantHealth _targetEnemy;
        private Vector3 _targetVelocity;
        private Vector3 _prevTargetPos;
        private const float PerceptionInterval = 0.15f;

        private readonly HumanAimSpring _aim = new HumanAimSpring();
        private float _aimPhaseA;
        private float _aimPhaseB;
        private float _acquireAge = 1f;
        private float _acquireSign = 1f;
        private bool _firingLatch;

        private float _moveHeadingDegrees;
        private float _moveMagnitude;
        private float _strafeDir = 1f;
        private float _strafeAmp = 1f;
        private float _strafeTimer;
        private float _strafeBlend;

        private float _paintSweepPhase;
        private float _paintAheadTimer;
        private bool _aheadNeedsPaint = true;
        private float _chargerPaintPause;

        private float _bombTimer;
        private float _specialRetryTimer;

        private Vector3 _threatPosition;
        private float _threatMemoryTimer;
        private float _retreatTimer;
        private float _retreatRepathTimer;
        private float _retreatCooldown;

        private float _stuckTimer;
        private Vector3 _stuckAnchor;

        private float _refillSearchTimer;
        private float _elapsed;

        private InkPathWeights _pathWeights = InkPathWeights.Default;
        private Vector3 _aimPoint;

        public BotState State { get; private set; } = BotState.Paint;
        public string DebugTargetName => _targetEnemy != null ? _targetEnemy.name : "-";
        public bool HasPath => _path.Count > 0;
        public float DebugAimYaw => _aim.AngleDegrees;

        /// <summary>当前武器推导出的理想交火距离（调试面板/测试读取）。</summary>
        public float DebugPreferredDistance => BotTactics.PreferredEngageDistance(
            _weapon != null ? _weapon.CurrentWeaponKind : WeaponKind.Shooter,
            _weapon != null ? _weapon.Config : null,
            config);

        private void Awake()
        {
            _motor = GetComponent<InkCharacterMotor>();
            _weapon = GetComponent<WeaponController>();
            _health = GetComponent<CombatantHealth>();
            _stuckAnchor = transform.position;
            _paintSweepPhase = Random.Range(0f, 10f);
            _aimPhaseA = Random.Range(0f, 20f);
            _aimPhaseB = Random.Range(0f, 20f);
            _aim.Reset(transform.eulerAngles.y);
            RefreshPathWeights();
        }

        public void Configure(BotConfigSO botConfig)
        {
            config = botConfig;
            RefreshPathWeights();
        }

        private void RefreshPathWeights()
        {
            _pathWeights = new InkPathWeights(
                config != null ? config.ownInkPathCost : 0.5f,
                config != null ? config.neutralPathCost : 1f,
                config != null ? config.enemyInkPathCost : 3f);
        }

        private void Update()
        {
            if (!_health.IsAlive)
            {
                State = BotState.Dead;
                _firingLatch = false;
                _moveMagnitude = 0f;
                _hasMoveTarget = false;
                _path.Clear();
                _motor.SetFrameIntent(Vector3.zero, AimDirection(), false, false);
                return;
            }

            if (State == BotState.Dead)
            {
                RespawnReset();
            }

            if (!MatchManager.CombatAllowed)
            {
                _motor.SetFrameIntent(Vector3.zero, AimDirection(), false, false);
                return;
            }

            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.05f);
            _elapsed += dt;
            if (_bombTimer > 0f) _bombTimer -= dt;
            if (_specialRetryTimer > 0f) _specialRetryTimer -= dt;
            if (_retreatCooldown > 0f) _retreatCooldown -= dt;
            if (_threatMemoryTimer > 0f) _threatMemoryTimer -= dt;
            if (_reactionTimer > 0f) _reactionTimer -= dt;
            if (_chargerPaintPause > 0f) _chargerPaintPause -= dt;
            _acquireAge += dt;
            _paintSweepPhase += dt;

            UpdateStuckDetection(dt);

            _thinkTimer -= dt;
            if (_thinkTimer <= 0f)
            {
                _thinkTimer = PerceptionInterval;
                UpdateState();
                if (!_health.IsAlive) return;
            }

            switch (State)
            {
                case BotState.Engage: TickEngage(dt); break;
                case BotState.Retreat: TickRetreat(dt); break;
                case BotState.Refill: TickRefill(dt); break;
                default: TickPaint(dt); break;
            }
        }

        private void RespawnReset()
        {
            State = BotState.Paint;
            ResetMovement();
            _aim.Reset(transform.eulerAngles.y);
            _aimPhaseA = Random.Range(0f, 20f);
            _aimPhaseB = Random.Range(0f, 20f);
            _acquireAge = 1f;
            _reactionTimer = 0f;
            _targetEnemy = null;
            _targetVelocity = Vector3.zero;
            _threatMemoryTimer = 0f;
            _retreatTimer = 0f;
            _retreatCooldown = 0f;
            _firingLatch = false;
            _moveMagnitude = 0f;
            _strafeBlend = 0f;
            _aheadNeedsPaint = true;
        }

        // ---------- 状态决策 ----------

        private void UpdateState()
        {
            CombatantHealth enemy = FindNearestEnemy(out Vector3 enemyPos);
            TrackTarget(enemy, enemyPos);

            float hp = _health.CurrentHealth;
            float threatDistance = _threatMemoryTimer > 0f ? FlatDistance(_threatPosition) : float.MaxValue;

            if (State != BotState.Retreat && BotTactics.ShouldRetreat(hp, threatDistance, config, _retreatCooldown))
            {
                EnterRetreat();
                return;
            }

            // 撤退何时结束由 TickRetreat 判定（回满血 / 脱离威胁）
            if (State == BotState.Retreat) return;

            bool lowInk = _motor.CurrentInk <= (config != null ? config.refillInkThreshold : 18f);
            if (lowInk && State != BotState.Refill)
            {
                _refillSearchTimer = 0f;
                State = BotState.Refill;
                ResetMovement();
                return;
            }

            if (State == BotState.Refill &&
                _motor.CurrentInk < (config != null ? config.refillExitInk : 75f))
            {
                return;
            }

            State = enemy != null ? BotState.Engage : BotState.Paint;
        }

        private void TrackTarget(CombatantHealth enemy, Vector3 enemyPos)
        {
            if (enemy != _targetEnemy)
            {
                _targetEnemy = enemy;
                _firingLatch = false;

                if (enemy != null)
                {
                    _reactionTimer = (config != null ? config.reactionTime : 0.2f) * Random.Range(0.7f, 1.3f);
                    _acquireAge = 0f;
                    _acquireSign = (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.5f, 1f);
                    _prevTargetPos = enemyPos;
                    _targetVelocity = Vector3.zero;
                    _threatPosition = enemyPos;
                    _threatMemoryTimer = 2.5f;
                }
                return;
            }

            if (enemy == null) return;

            Vector3 velocity = (enemyPos - _prevTargetPos) / PerceptionInterval;
            velocity.y = 0f;
            _targetVelocity = Vector3.Lerp(_targetVelocity, velocity, 0.5f);
            _prevTargetPos = enemyPos;
            _threatPosition = enemyPos;
            _threatMemoryTimer = 2.5f;
        }

        private CombatantHealth FindNearestEnemy(out Vector3 enemyPosition)
        {
            enemyPosition = default;
            if (_weapon == null || _health == null) return null;

            float sight = BotTactics.SightRange(_weapon.CurrentWeaponKind, _weapon.Config, config);
            float sightSq = sight * sight;
            float stealthDetect = config != null ? Mathf.Max(0.5f, config.stealthDetectRange) : 2.5f;

            CombatantHealth best = null;
            float bestScore = float.MaxValue;

            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (candidate == null || candidate == _health || !candidate.IsAlive) continue;
                if (!candidate.Team.IsOpponentOf(_health.Team)) continue;

                Vector3 targetPos = candidate.transform.position;
                Vector3 to = targetPos - transform.position;
                to.y = 0f;
                float sq = to.sqrMagnitude;
                if (sq > sightSq) continue;

                // 潜行敌人：与 P2-A 的隐身规则一致，除非贴身否则看不见
                if (candidate.TryGetComponent(out InkCharacterMotor enemyMotor) &&
                    enemyMotor.IsStealthed && sq > stealthDetect * stealthDetect)
                {
                    continue;
                }

                if (!HasLineOfSight(candidate)) continue;

                // 粘性目标：当前目标享受 4m 的优先权，避免两名敌人之间反复横跳
                float score = Mathf.Sqrt(sq) - (candidate == _targetEnemy ? 4f : 0f);
                if (score >= bestScore) continue;

                bestScore = score;
                best = candidate;
                enemyPosition = targetPos;
            }

            return best;
        }

        private bool HasLineOfSight(CombatantHealth target)
        {
            Vector3 eye = transform.position + Vector3.up * 0.9f;
            Vector3 targetEye = target.transform.position + Vector3.up * 0.9f;
            if (!Physics.Linecast(eye, targetEye, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
                return true;

            return hit.collider.GetComponentInParent<CombatantHealth>() == target;
        }

        // ---------- 交战 ----------

        private void TickEngage(float dt)
        {
            if (_targetEnemy == null || !_targetEnemy.IsAlive)
            {
                State = BotState.Paint;
                _firingLatch = false;
                ResetMovement();
                return;
            }

            WeaponKind kind = _weapon.CurrentWeaponKind;
            WeaponConfigSO weaponConfig = _weapon.Config;

            Vector3 enemyPos = _targetEnemy.transform.position;
            Vector3 toEnemy = enemyPos - transform.position;
            toEnemy.y = 0f;
            float dist = toEnemy.magnitude;
            Vector3 dir = dist > 0.001f ? toEnemy / dist : transform.forward;

            float preferred = BotTactics.PreferredEngageDistance(kind, weaponConfig, config);
            float fireRange = BotTactics.MaxFireDistance(kind, weaponConfig, config);
            float engageRange = _firingLatch ? fireRange * 1.35f : fireRange;

            // ---- 瞄准：带提前量的理想角 + 漂移/拉枪误差 → 弹簧
            float lead = BotTactics.AimLeadSeconds(kind, weaponConfig, dist);
            Vector3 predicted = enemyPos + _targetVelocity * lead;
            Vector3 toPredicted = predicted - transform.position;
            toPredicted.y = 0f;
            float idealYaw = toPredicted.sqrMagnitude > 0.0001f
                ? HumanAimSpring.YawFromDirection(toPredicted)
                : HumanAimSpring.YawFromDirection(dir);

            float wanderSpeed = config != null ? config.aimWanderSpeed : 1.7f;
            float wander = Mathf.Sin(_elapsed * wanderSpeed + _aimPhaseA) * 0.6f
                           + Mathf.Sin(_elapsed * wanderSpeed * 1.34f + _aimPhaseB) * 0.4f;

            float reaction = config != null ? config.reactionTime : 0.2f;
            float acquireTau = Mathf.Max(0.12f, reaction * 0.9f);
            float acquire = Mathf.Exp(-_acquireAge / acquireTau);
            float aimError = config != null ? config.aimErrorDegrees : 5.5f;
            float wanderDegrees = config != null ? config.aimWanderDegrees : 1.2f;
            float overshoot = config != null ? config.aimAcquireOvershoot : 2.4f;

            // 慢漂移（长期不呆板）+ 发现目标时的大偏角按 reaction 指数收敛（拉枪过冲后修正）
            float wantYaw = idealYaw + wanderDegrees * wander + aimError * overshoot * acquire * _acquireSign;

            _aim.Step(wantYaw,
                config != null ? config.aimSpringOmega : 13f,
                config != null ? config.aimMaxTurnRateDegrees : 573f,
                1f, dt);
            Vector3 aimDir = AimDirection();

            float offAngle = Mathf.Abs(HumanAimSpring.ShortestDelta(_aim.AngleDegrees, idealYaw));
            float tolerance = BotTactics.AimToleranceDegrees(dist, config, _firingLatch);
            bool aimed = offAngle <= tolerance;
            bool reactionReady = _reactionTimer <= 0f;
            bool hasInk = _motor.CurrentInk > 1f;

            // ---- 开火决策（多武器适配）
            bool wantFire = false;
            switch (kind)
            {
                case WeaponKind.Charger:
                    if (_weapon.IsCharging)
                    {
                        // 蓄满即松手：本帧不注入扳机，LateUpdate 会走“松开 → 发射”分支
                        float release = config != null ? Mathf.Clamp01(config.chargerReleaseCharge) : 0.95f;
                        wantFire = _weapon.Charge01 < release;
                    }
                    else
                    {
                        wantFire = reactionReady && aimed && dist <= fireRange && hasInk;
                    }
                    break;

                case WeaponKind.Splatling:
                    if (_weapon.IsCharging)
                    {
                        // 加特林同样是“蓄满松手才扫射”，否则会一直端着不发射
                        float release = config != null ? Mathf.Clamp01(config.splatlingReleaseCharge) : 0.9f;
                        wantFire = _weapon.Charge01 < release;
                    }
                    else
                    {
                        // 扫射/冷却期间保持扳机：策略内部会忽略，冷却结束后自动重新起转
                        wantFire = reactionReady && aimed && dist <= fireRange && hasInk;
                    }
                    break;

                case WeaponKind.Roller:
                {
                    // 滚筒：贴近才推地碾压，转身阶段不浪费扳机
                    float rollerRange = config != null ? Mathf.Max(1.5f, config.rollerFireRange) : 5.5f;
                    wantFire = reactionReady && hasInk && dist <= rollerRange && (aimed || dist <= preferred + 1f);
                    break;
                }

                default:
                    wantFire = reactionReady && aimed && dist <= engageRange && hasInk;
                    break;
            }

            // ---- 移动：远则寻路切入（墨水加权），近则拉开，保持距离时平滑横向拉枪
            Vector3 move;
            if (kind == WeaponKind.Roller)
            {
                if (dist > preferred + 0.4f)
                    move = FollowPath(enemyPos) ? CurrentSteer() : dir;
                else
                    move = dir;
            }
            else if (dist > preferred + 1.2f)
            {
                move = FollowPath(enemyPos) ? CurrentSteer() : dir;
            }
            else if (dist < preferred - 1.5f)
            {
                move = -dir;
                _hasMoveTarget = false;
            }
            else
            {
                _hasMoveTarget = false;
                _strafeTimer -= dt;
                if (_strafeTimer <= 0f)
                {
                    _strafeTimer = Random.Range(0.6f, 1.6f);
                    _strafeDir = Random.value > 0.5f ? 1f : -1f;
                    _strafeAmp = Random.Range(0.55f, 1f);
                }

                _strafeBlend = Mathf.Lerp(_strafeBlend, _strafeDir * _strafeAmp, 1f - Mathf.Exp(-5f * dt));
                move = Vector3.Cross(Vector3.up, dir) * (_strafeBlend * 0.9f);
            }

            if (kind == WeaponKind.Charger && _weapon.IsCharging)
            {
                move *= 0.3f;   // 蓄力时脚步放慢，符合蓄力狙手感
            }

            TryUseBombOrSpecial(dist);

            // 脱离射程但脚下是自家墨：切乌贼形态高速贴近（又快又难被打中）
            bool wantsSwim = !wantFire
                             && kind != WeaponKind.Roller
                             && !_weapon.IsCharging
                             && dist > fireRange * 1.15f
                             && _motor.IsInOwnInk;

            _firingLatch = wantFire;

            Vector3 smoothedMove = SmoothMove(move, dt);
            _motor.SetFrameIntent(smoothedMove, aimDir, wantsSwim, false, wantFire);

            if (wantFire)
            {
                float aimDistance = Mathf.Clamp(dist, 2f, Mathf.Max(2f, fireRange));
                _aimPoint = BotTactics.AimGroundPoint(transform.position, aimDir, aimDistance, predicted.y);
                _weapon.TryFireMainWeapon(aimDir, _aimPoint);
            }
        }

        private void TryUseBombOrSpecial(float distToEnemy)
        {
            if (_specialRetryTimer <= 0f && _weapon.IsSpecialReady)
            {
                int near = CountEnemiesWithin(config != null ? config.specialSearchRange : 7.5f);
                if (near >= (config != null ? config.specialMinEnemies : 1))
                {
                    if (_weapon.TryActivateSpecial())
                    {
                        _specialRetryTimer = 1f;
                        return;
                    }
                    _specialRetryTimer = 0.8f;
                }
            }

            if (_bombTimer > 0f) return;
            if (_targetEnemy == null) return;

            float minR = config != null ? config.bombRangeMin : 4.5f;
            float maxR = config != null ? config.bombRangeMax : 11f;
            float minInk = config != null ? config.bombMinInk : 60f;

            if (distToEnemy < minR || distToEnemy > maxR) return;
            if (_motor.CurrentInk < minInk) return;
            if (Random.value > (config != null ? config.bombUseChance : 0.55f)) return;

            if (_weapon.TryThrowSubBomb(_targetEnemy.transform.position))
            {
                _bombTimer = config != null ? config.bombCooldown : 5f;
            }
        }

        private int CountEnemiesWithin(float radius)
        {
            var all = CombatantHealthRegistry.All;
            int count = 0;
            float sq = radius * radius;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !c.IsAlive || !c.Team.IsOpponentOf(_health.Team)) continue;
                if ((c.transform.position - transform.position).sqrMagnitude <= sq) count++;
            }
            return count;
        }

        // ---------- 残血战术撤退 ----------

        private void EnterRetreat()
        {
            State = BotState.Retreat;
            float min = config != null ? config.retreatDurationMin : 2.2f;
            float max = config != null ? config.retreatDurationMax : 3.6f;
            _retreatTimer = Random.Range(min, Mathf.Max(min, max));
            _retreatRepathTimer = 0f;
            _firingLatch = false;
            _strafeBlend = 0f;
            ResetMovement();
            PickRetreatPoint();
        }

        private void ExitRetreat()
        {
            ExitRetreat(true);
        }

        private void ExitRetreat(bool allowReentryCooldown)
        {
            State = BotState.Paint;
            if (allowReentryCooldown)
            {
                _retreatCooldown = config != null ? config.retreatReentryCooldown : 1.5f;
            }
            else
            {
                // 没回满血就超时脱战：不惩罚再入冷却，否则残血 Bot 下次遇敌无法立即撤退
                _retreatCooldown = 0f;
            }
            _retreatTimer = 0f;
            _firingLatch = false;
            _chargerPaintPause = 0f;
            ResetMovement();
        }

        private void TickRetreat(float dt)
        {
            _retreatTimer -= dt;

            float exitHealth = config != null ? config.retreatExitHealth : 75f;
            if (_health.CurrentHealth >= exitHealth)
            {
                ExitRetreat();
                return;
            }

            float triggerRange = config != null ? config.retreatTriggerRange : 14f;
            bool threatNearby = _threatMemoryTimer > 0f && FlatDistance(_threatPosition) <= triggerRange * 1.2f;

            if (_retreatTimer <= 0f)
            {
                if (!threatNearby)
                {
                    ExitRetreat(_health.CurrentHealth >= exitHealth);
                    return;
                }
                _retreatTimer = 1.2f;   // 还没脱离危险：继续边退边打
            }

            _retreatRepathTimer -= dt;
            if (_retreatRepathTimer <= 0f || !_hasMoveTarget || ReachedTarget())
            {
                _retreatRepathTimer = config != null ? Mathf.Max(0.2f, config.retreatRepathInterval) : 0.9f;
                PickRetreatPoint();
            }

            Vector3 steer;
            if (_hasMoveTarget)
            {
                steer = transform.forward;
                FollowPath(_moveTarget);
                Vector3 pathSteer = CurrentSteer();
                if (pathSteer.sqrMagnitude > 0.001f) steer = pathSteer;
            }
            else
            {
                // 没有可用撤退点：绝不能沿用 transform.forward 盲目前冲（可能正对威胁），
                // 有可信威胁方位时朝其反方向撤离；记忆已过期则没有可信方位，保持当前朝向。
                if (_threatMemoryTimer > 0f)
                {
                    Vector3 away = transform.position - _threatPosition;
                    away.y = 0f;
                    steer = away.sqrMagnitude > 0.001f ? away.normalized : transform.forward;
                }
                else
                {
                    steer = transform.forward;
                }
            }

            // 站在己方墨里就潜下去回血（60 HP/s），否则边跑边铺一条逃生墨道
            bool inOwnInk = _motor.IsInOwnInk;
            bool canPaintEscape = !inOwnInk
                                  && _weapon.CurrentWeaponKind != WeaponKind.Charger
                                  && _motor.CurrentInk > 3f;

            float idleOmega = config != null ? config.aimIdleSpringOmega : 8f;
            float idleRate = config != null ? config.aimIdleTurnRateDegrees : 344f;
            _aim.Step(HumanAimSpring.YawFromDirection(steer), idleOmega, idleRate, 1f, dt);
            Vector3 aimDir = AimDirection();

            Vector3 move = SmoothMove(steer, dt);
            _motor.SetFrameIntent(move, aimDir, inOwnInk, false, canPaintEscape);
            if (canPaintEscape)
            {
                _weapon.TryFireMainWeapon(aimDir);
            }
        }

        private void PickRetreatPoint()
        {
            Vector3 self = transform.position;
            InkGrid ink = ResolveInkGrid();
            Team myTeam = _health.Team;
            float radius = config != null ? Mathf.Max(4f, config.retreatSearchRadius) : 9f;
            bool hasThreat = _threatMemoryTimer > 0f;

            bool found = false;
            float bestScore = float.MinValue;
            Vector3 best = self;

            // 第一轮：只在己方墨水点里选（安全屋：潜行 + 高速回血）
            for (int i = 0; i < 16; i++)
            {
                Vector3 p = RandomRingPoint(self, 3f, radius);
                if (ink != null)
                {
                    if (ink.IsBlockedAt(p)) continue;
                    if (ink.InkAt(p) != myTeam) continue;
                }

                // 只接受「背离威胁」的候选点：远征到地图对角虽然离威胁更远，
                // 但路径会先穿过威胁，残血撤退反而送命。
                if (!IsRetreatDirectionAway(p, self)) continue;

                float score = 6f - Vector3.Distance(p, self) * 0.15f;
                if (hasThreat)
                {
                    float away = FlatDistanceTo(_threatPosition, p) - FlatDistanceTo(_threatPosition, self);
                    score += away * 0.8f;
                    if (!HasLineOfSightFrom(p)) score += 4f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                    found = true;
                }
            }

            // 第二轮：附近没有己方墨，就退向离威胁最远的点，边退边铺墨
            if (!found)
            {
                float bestAway = float.MinValue;
                for (int i = 0; i < 12; i++)
                {
                    Vector3 p = RandomRingPoint(self, 3f, radius);
                    if (ink != null && ink.IsBlockedAt(p)) continue;
                    if (!IsRetreatDirectionAway(p, self)) continue;

                    float away = hasThreat ? FlatDistanceTo(_threatPosition, p) : Random.value;
                    if (away <= bestAway) continue;
                    bestAway = away;
                    best = p;
                    found = true;
                }
            }

            var nav = ArenaNavigator.Instance != null ? ArenaNavigator.Instance.Grid : null;
            if (found && nav != null && !nav.TryGetNearestWalkable(best, out best))
            {
                found = false;
            }

            // 吸附到可行走格后再次确认方向，避免越界点被夹到侧后方/对向。
            if (found && !IsRetreatDirectionAway(best, self))
            {
                found = false;
            }

            if (!found)
            {
                _hasMoveTarget = false;
                return;
            }

            _moveTarget = new Vector3(best.x, self.y, best.z);
            _hasMoveTarget = true;
            _path.Clear();
            _pathIndex = 0;
            _repathTimer = 0f;
        }

        /// <summary>候选撤退点是否位于威胁的反方向（夹角 &lt;= 约 69°）。无威胁记忆时一律接受。</summary>
        private bool IsRetreatDirectionAway(Vector3 point, Vector3 self)
        {
            if (_threatMemoryTimer <= 0f)
                return true;

            Vector3 toPoint = new Vector3(point.x - self.x, 0f, point.z - self.z);
            Vector3 toThreat = new Vector3(_threatPosition.x - self.x, 0f, _threatPosition.z - self.z);
            if (toPoint.sqrMagnitude < 0.0001f || toThreat.sqrMagnitude < 0.0001f)
                return true;

            return Vector3.Dot(toPoint.normalized, toThreat.normalized) <= 0.35f;
        }

        private bool HasLineOfSightFrom(Vector3 from)
        {
            if (_threatMemoryTimer <= 0f) return false;

            Vector3 eye = from + Vector3.up * 0.9f;
            Vector3 threatEye = _threatPosition + Vector3.up * 0.9f;
            return !Physics.Linecast(eye, threatEye, out _, ~0, QueryTriggerInteraction.Ignore);
        }

        private static Vector3 RandomRingPoint(Vector3 center, float minRadius, float maxRadius)
        {
            return center + RandomRingOffset(minRadius, maxRadius);
        }

        private static Vector3 RandomRingOffset(float minRadius, float maxRadius)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(minRadius, maxRadius);
            return new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
        }

        // ---------- 巡涂 ----------

        private void TickPaint(float dt)
        {
            _paintGoalTimer -= dt;
            if (!_hasMoveTarget || ReachedTarget() || _paintGoalTimer <= 0f)
            {
                if (!PickPaintTarget())
                {
                    _hasMoveTarget = false;
                    _paintGoalTimer = 1.5f;
                }
            }

            Vector3 steer;
            if (_hasMoveTarget)
            {
                FollowPath(_moveTarget);
                steer = CurrentSteer();
                if (steer.sqrMagnitude < 0.001f) steer = transform.forward;
            }
            else
            {
                steer = new Vector3(Mathf.Sin(_elapsed * 0.7f + _paintSweepPhase), 0f,
                                    Mathf.Cos(_elapsed * 0.7f + _paintSweepPhase));
            }

            _paintAheadTimer -= dt;
            if (_paintAheadTimer <= 0f)
            {
                _paintAheadTimer = config != null ? Mathf.Max(0.05f, config.paintAheadCheckInterval) : 0.3f;
                _aheadNeedsPaint = NeedsPaintAhead(steer);
            }

            // 前方已经涂好 + 站在自家墨上：收起武器潜行转场（沿己方墨道高速切入）
            bool travelFast = !_aheadNeedsPaint && PathRemainingDistance() > 4f && _motor.IsInOwnInk;

            WeaponKind kind = _weapon.CurrentWeaponKind;
            float sweep = Mathf.Sin(_paintSweepPhase * (config != null ? config.paintSweepSpeed : 1.6f))
                          * (config != null ? config.paintSweepDegrees : 26f);
            float wantYaw = HumanAimSpring.YawFromDirection(steer) + sweep;

            _aim.Step(wantYaw,
                config != null ? config.aimIdleSpringOmega : 8f,
                config != null ? config.aimIdleTurnRateDegrees : 344f,
                1f, dt);
            Vector3 aimDir = AimDirection();

            bool wantFire;
            bool wantsSwim;

            switch (kind)
            {
                case WeaponKind.Charger:
                {
                    // 蓄力狙巡涂：蓄到 70% 松手画一条墨线，短暂停顿后继续
                    float paintRelease = config != null ? Mathf.Clamp01(config.chargerPaintReleaseCharge) : 0.7f;
                    if (_weapon.IsCharging)
                    {
                        wantFire = _weapon.Charge01 < paintRelease;
                        if (!wantFire)
                        {
                            _chargerPaintPause = config != null ? Mathf.Max(0f, config.chargerPaintPause) : 0.35f;
                        }
                    }
                    else
                    {
                        wantFire = !travelFast && _aheadNeedsPaint && _chargerPaintPause <= 0f
                                   && _motor.CurrentInk > 25f;
                    }
                    wantsSwim = !wantFire && travelFast;
                    break;
                }

                case WeaponKind.Roller:
                    wantFire = !travelFast && _motor.CurrentInk > 6f;
                    wantsSwim = false;
                    break;

                case WeaponKind.Splatling:
                {
                    // 加特林巡涂同样是“蓄到阈值松手才喷墨带”，缺了这支会永远端着蓄力不发一发
                    float paintRelease = config != null ? Mathf.Clamp01(config.splatlingPaintReleaseCharge) : 0.6f;
                    if (_weapon.IsCharging)
                    {
                        wantFire = _weapon.Charge01 < paintRelease;
                        if (!wantFire)
                        {
                            _chargerPaintPause = config != null ? Mathf.Max(0f, config.chargerPaintPause) : 0.35f;
                        }
                    }
                    else
                    {
                        wantFire = !travelFast && _aheadNeedsPaint && _chargerPaintPause <= 0f
                                   && _motor.CurrentInk > 12f;
                    }
                    wantsSwim = !wantFire && travelFast;
                    break;
                }

                default:
                    wantFire = !travelFast && _aheadNeedsPaint && _motor.CurrentInk > 12f;
                    wantsSwim = travelFast;
                    break;
            }

            Vector3 move = SmoothMove(steer, dt);
            _motor.SetFrameIntent(move, aimDir, wantsSwim, false, wantFire);
            if (wantFire)
            {
                _weapon.TryFireMainWeapon(aimDir);
            }
        }

        private bool NeedsPaintAhead(Vector3 direction)
        {
            var surface = InkSurfaceController.Instance;
            if (surface == null || surface.Grid == null) return true;

            InkGrid grid = surface.Grid;
            Team myTeam = _health.Team;
            float range = config != null ? Mathf.Max(2f, config.paintAimRange) : 6f;

            int own = 0;
            for (int i = 1; i <= 3; i++)
            {
                Vector3 p = transform.position + direction * (range * i / 3f);
                if (grid.IsBlockedAt(p)) continue;
                if (grid.InkAt(p) == myTeam) own++;
            }

            return own < 2;
        }

        private bool ReachedTarget()
        {
            Vector3 d = _moveTarget - transform.position;
            d.y = 0f;
            float arrive = config != null ? config.arriveDistance : 1.3f;
            return d.sqrMagnitude <= arrive * arrive;
        }

        private bool PickPaintTarget()
        {
            var surface = InkSurfaceController.Instance;
            if (surface == null || surface.Grid == null) return false;

            InkGrid ink = surface.Grid;
            var nav = ArenaNavigator.Instance != null ? ArenaNavigator.Instance.Grid : null;

            Vector3 myPos = transform.position;
            Team myTeam = _health.Team;
            float enemySideSign = myTeam == Team.TeamA ? 1f : -1f;
            int samples = config != null ? Mathf.Max(6, config.paintGoalSamples) : 20;

            bool found = false;
            float bestScore = float.MinValue;
            Vector3 best = myPos;

            for (int attempt = 0; attempt < samples; attempt++)
            {
                Vector3 p = myPos + RandomRingOffset(6f, 24f);
                if (ink.IsBlockedAt(p)) continue;

                Team cellTeam = ink.InkAt(p);
                if (cellTeam == myTeam) continue;   // 已经是自家墨，没必要再去

                Vector3 target = p;
                if (nav != null && !nav.TryGetNearestWalkable(target, out target)) continue;

                float dist = FlatDistance(target);
                if (dist < 4f || dist > 28f) continue;

                // 越靠近敌方半场越值得压上；敌方墨水比中性地面更值得抢回。
                // 半场深度直接取当前涂地网格的 Z 尺寸，地图加长时不需要再改魔法数字。
                float halfDepth = Mathf.Max(1f, ink.MapSize.y * 0.5f);
                float progress = Mathf.Clamp01((target.z * enemySideSign) / halfDepth);
                float value = cellTeam == Team.Neutral ? 1f : 1.6f;
                float score = value * 10f - dist * 0.25f + progress * 3f + Random.Range(0f, 2.5f);

                if (score <= bestScore) continue;
                bestScore = score;
                best = target;
                found = true;
            }

            _paintGoalTimer = config != null ? Mathf.Max(1f, config.paintGoalRefresh) : 7f;
            if (!found) return false;

            _moveTarget = new Vector3(best.x, myPos.y, best.z);
            _hasMoveTarget = true;
            _path.Clear();
            _pathIndex = 0;
            _repathTimer = 0f;
            return true;
        }

        // ---------- 补给 ----------

        private void TickRefill(float dt)
        {
            if (_motor.IsInOwnInk)
            {
                _hasMoveTarget = false;
                // 潜进己方墨原地补给：回墨 42/s、回血 60/s
                _motor.SetFrameIntent(Vector3.zero, AimDirection(), true, false);
                return;
            }

            _refillSearchTimer -= dt;
            if (_refillSearchTimer <= 0f || !_hasMoveTarget)
            {
                _refillSearchTimer = 0.6f;
                if (TryFindNearbyOwnInk(out Vector3 inkPos))
                {
                    _moveTarget = inkPos;
                    _hasMoveTarget = true;
                    _path.Clear();
                    _pathIndex = 0;
                    _repathTimer = 0f;
                }
                else
                {
                    // 附近没有己方墨水：切回涂地，先铺一条退路
                    State = BotState.Paint;
                    return;
                }
            }

            FollowPath(_moveTarget);
            Vector3 steer = CurrentSteer();
            Vector3 move = SmoothMove(steer, dt);
            _aim.Step(HumanAimSpring.YawFromDirection(steer),
                config != null ? config.aimIdleSpringOmega : 8f,
                config != null ? config.aimIdleTurnRateDegrees : 344f,
                1f, dt);
            _motor.SetFrameIntent(move, AimDirection(), false, false);
        }

        private bool TryFindNearbyOwnInk(out Vector3 result)
        {
            result = transform.position;
            InkGrid ink = ResolveInkGrid();
            if (ink == null) return false;

            Team myTeam = _health.Team;

            for (int attempt = 0; attempt < 80; attempt++)
            {
                Vector3 p = RandomRingPoint(transform.position, 2f, 14f);

                if (ink.InkAt(p) != myTeam) continue;
                if (ink.IsBlockedAt(p)) continue;

                result = p;
                return true;
            }

            return false;
        }

        // ---------- 移动朝向平滑 ----------

        /// <summary>
        /// 把移动指令当作「带惯性的人体」：朝向按角速度上限转向，速度指数逼近。
        /// 这样路点切换、横向拉枪换向时不会出现瞬时 180° 抽搐。
        /// </summary>
        private Vector3 SmoothMove(Vector3 desired, float dt)
        {
            float magnitude = Mathf.Clamp01(desired.magnitude);

            if (magnitude > 0.01f)
            {
                float desiredYaw = HumanAimSpring.YawFromDirection(desired);
                float delta = HumanAimSpring.ShortestDelta(_moveHeadingDegrees, desiredYaw);

                if (_moveMagnitude < 0.05f)
                {
                    _moveHeadingDegrees = desiredYaw;   // 起步直接对齐，避免第一帧原地画圈
                }
                else if (Mathf.Abs(delta) > 120f)
                {
                    _moveHeadingDegrees = desiredYaw;   // 急掉头：先刹住再反向
                    _moveMagnitude *= 0.35f;
                }
                else
                {
                    float rate = config != null ? Mathf.Max(60f, config.moveHeadingTurnRateDegrees) : 630f;
                    _moveHeadingDegrees += Mathf.Clamp(delta, -rate * dt, rate * dt);
                }
            }

            _moveMagnitude = Mathf.Lerp(_moveMagnitude, magnitude, 1f - Mathf.Exp(-14f * dt));
            return HumanAimSpring.DirectionFromYaw(_moveHeadingDegrees) * _moveMagnitude;
        }

        // ---------- 路径跟随 ----------

        private bool FollowPath(Vector3 goal)
        {
            _repathTimer -= Time.deltaTime;
            if (_repathTimer <= 0f || _path.Count == 0)
            {
                _repathTimer = config != null ? config.repathInterval : 0.55f;
                var arena = ArenaNavigator.Instance;
                bool ok = false;

                if (arena != null)
                {
                    // 墨水加权：己方墨道 0.5 / 中性 1.0 / 敌方 3.0
                    ok = arena.TryFindPath(transform.position, goal, _path, _health.Team, _pathWeights);
                }

                if (!ok)
                {
                    _path.Clear();
                    _path.Add(goal);   // 兜底：没有导航网格时朝目标直线推进
                }

                _pathIndex = 0;
            }

            if (_path.Count == 0)
            {
                _moveTarget = goal;
                return false;
            }

            while (_pathIndex < _path.Count)
            {
                Vector3 d = _path[_pathIndex] - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.36f) break;
                _pathIndex++;
            }

            return _pathIndex < _path.Count;
        }

        private Vector3 CurrentSteer()
        {
            if (_path.Count == 0 || _pathIndex >= _path.Count)
                return Vector3.zero;

            Vector3 d = _path[_pathIndex] - transform.position;
            d.y = 0f;
            return d.sqrMagnitude > 1e-5f ? d.normalized : Vector3.zero;
        }

        private float PathRemainingDistance()
        {
            if (_path.Count == 0 || _pathIndex >= _path.Count) return 0f;

            Vector3 d = _path[_path.Count - 1] - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        private void ResetMovement()
        {
            _path.Clear();
            _pathIndex = 0;
            _hasMoveTarget = false;
            _repathTimer = 0f;
            _stuckAnchor = transform.position;
            _stuckTimer = 0f;
        }

        private void UpdateStuckDetection(float dt)
        {
            _stuckTimer += dt;
            float interval = config != null ? config.stuckCheckInterval : 1f;
            if (_stuckTimer < interval) return;

            float threshold = config != null ? config.stuckMoveThreshold : 0.35f;
            if (Vector3.Distance(transform.position, _stuckAnchor) < threshold)
            {
                _path.Clear();
                _hasMoveTarget = false;
                _repathTimer = 0f;
            }

            _stuckTimer = 0f;
            _stuckAnchor = transform.position;
        }

        // ---------- 工具 ----------

        private Vector3 AimDirection()
        {
            return HumanAimSpring.DirectionFromYaw(_aim.AngleDegrees);
        }

        private InkGrid ResolveInkGrid()
        {
            var surface = InkSurfaceController.Instance;
            return surface != null ? surface.Grid : null;
        }

        private float FlatDistance(Vector3 world)
        {
            return FlatDistanceTo(world, transform.position);
        }

        private static float FlatDistanceTo(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void OnDrawGizmosSelected()
        {
            if (_path != null && _path.Count > 0)
            {
                Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
                for (int i = 0; i < _path.Count - 1; i++)
                {
                    Gizmos.DrawLine(_path[i] + Vector3.up * 0.1f, _path[i + 1] + Vector3.up * 0.1f);
                }
            }

            Gizmos.color = new Color(0.2f, 1f, 1f, 0.9f);
            Gizmos.DrawLine(transform.position + Vector3.up * 1.1f,
                transform.position + Vector3.up * 1.1f + AimDirection() * 3f);

            if (_aimPoint.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = new Color(1f, 0.35f, 0.35f, 0.9f);
                Gizmos.DrawWireSphere(_aimPoint, 0.25f);
            }
        }
    }
}
