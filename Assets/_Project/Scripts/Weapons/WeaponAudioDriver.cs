using P1.Core;
using P1.Player;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 武器音频驱动器（P1-Audio）：
    /// 监听 WeaponController 开火事件与武器状态（滚筒推地、加特林旋转、蓄力狙蓄力），
    /// 自动驱动开火音效与循环音效通道。
    /// </summary>
    [RequireComponent(typeof(WeaponController))]
    public class WeaponAudioDriver : MonoBehaviour
    {
        private WeaponController _weapon;
        private CombatantHealth _health;
        private bool _wasChargerFull;
        private bool _wasSplatlingReady;
        private float _chargerChargeAtRelease;
        private float _lastBlasterPumpTime;
        private int _lastShotFrame = -1;

        private void Awake()
        {
            _weapon = GetComponent<WeaponController>();
            _health = GetComponent<CombatantHealth>();
        }

        private void OnEnable()
        {
            if (_weapon != null)
            {
                _weapon.OnFired += HandleFired;
            }
        }

        private void OnDisable()
        {
            if (_weapon != null)
            {
                _weapon.OnFired -= HandleFired;
            }

            StopWeaponLoops();
        }

        private void OnDestroy()
        {
            if (_weapon != null)
            {
                _weapon.OnFired -= HandleFired;
            }

            StopWeaponLoops();
        }

        private void HandleFired()
        {
            if (AudioManager.Instance == null || _weapon == null)
                return;

            // P28：滚筒推地由推地循环音承担，不能再每帧叠一发 flick 音
            if (_weapon.IsRolling)
                return;

            // P28 齐射去重：泼桶一次 8 滴 / 滚筒一次 9 滴在同一帧只发一声
            if (Time.frameCount == _lastShotFrame)
                return;
            _lastShotFrame = Time.frameCount;

            // P30：本地玩家走前台全量射击音；Bot/队友走远场衰减音（音量上限远低于本地、
            // 全局节流防音墙），4v4 不再是「全场只有自己一把枪在响」。
            if (!CombatantHealth.IsLocalPlayer(_health))
            {
                AudioManager.Instance.PlayWeaponShotAt(
                    _weapon.transform.position, _weapon.CurrentWeaponKind, _weapon.LastShotStrength01);
                return;
            }

            AudioManager.Instance.PlayWeaponShot(_weapon.CurrentWeaponKind, _weapon.LastShotStrength01);

            // 蓄力狙射出去的那一刻：这一发不算「取消」，清掉待判定量
            if (_weapon.CurrentWeaponKind == WeaponKind.Charger)
                _chargerChargeAtRelease = 0f;

            // 爆破枪换气泵（旧版烘了 blaster_pump 却从没接线）：
            // 每发之后的机械循环声给「这把枪有机构」的重量感。
            // 0.30s 下限避免高频泼洒时叠成噪声墙。
            if (_weapon.CurrentWeaponKind == WeaponKind.Blaster
                && Time.unscaledTime - _lastBlasterPumpTime >= 0.30f)
            {
                _lastBlasterPumpTime = Time.unscaledTime;
                AudioManager.Instance.PlayBlasterPump();
            }
        }

        private void Update()
        {
            if (AudioManager.Instance == null || _weapon == null || _health == null)
                return;

            if (!CombatantHealth.IsLocalPlayer(_health))
                return;

            bool isAlive = _health.IsAlive && !_weapon.IsPerformingSpecial;
            WeaponKind kind = _weapon.CurrentWeaponKind;

            // 1. 滚筒推地碾压循环音
            if (isAlive && kind == WeaponKind.Roller && _weapon.IsRolling)
            {
                AudioManager.Instance.PlayLoop(LoopSfxKind.Roller);
            }
            else
            {
                AudioManager.Instance.StopLoop(LoopSfxKind.Roller);
            }

            // 2. 加特林蓄力旋转循环音 + 蓄力到位的一声机械「咔哒」
            if (isAlive && kind == WeaponKind.Splatling && _weapon.IsCharging)
            {
                AudioManager.Instance.PlayLoop(LoopSfxKind.SplatlingSpin);

                if (_weapon.Charge01 >= 0.999f && !_wasSplatlingReady)
                {
                    _wasSplatlingReady = true;
                    AudioManager.Instance.PlaySplatlingReady();
                }
            }
            else
            {
                AudioManager.Instance.StopLoop(LoopSfxKind.SplatlingSpin);
                if (!_weapon.IsCharging)
                {
                    _wasSplatlingReady = false;
                }
            }

            // 3. 蓄力狙蓄力循环音与蓄满清脆叮音
            //    循环音随蓄力进度渐强（玩家反馈全程满量太吵）：轻蓄只给底噪，满蓄才到全量
            if (isAlive && kind == WeaponKind.Charger && _weapon.IsCharging)
            {
                float chargeRamp = Mathf.Lerp(
                    AudioManager.Instance.ChargerChargeRampMinVolume, 1f, _weapon.Charge01);
                AudioManager.Instance.PlayLoop(LoopSfxKind.ChargerCharge, chargeRamp);

                if (_weapon.Charge01 >= 0.999f && !_wasChargerFull)
                {
                    _wasChargerFull = true;
                    if (AudioManager.Instance.chargerFullClip != null)
                    {
                        AudioManager.Instance.PlayOne(AudioManager.Instance.chargerFullClip, 0.65f);
                    }
                }

                _chargerChargeAtRelease = _weapon.Charge01;
            }
            else
            {
                AudioManager.Instance.StopLoop(LoopSfxKind.ChargerCharge);
                if (!_weapon.IsCharging)
                {
                    _wasChargerFull = false;

                    // 蓄力取消（旧版完全静音）：蓄了但没射就松手/切枪，给一声「泄气」，
                    // 而不是让蓄力循环音凭空消失。射出去的由 HandleFired 负责，这里只处理「没射」。
                    if (_chargerChargeAtRelease > 0.15f)
                        AudioManager.Instance.PlayChargeCancel();

                    _chargerChargeAtRelease = 0f;
                }
            }
        }

        private void StopWeaponLoops()
        {
            if (AudioManager.Instance == null)
                return;

            AudioManager.Instance.StopLoop(LoopSfxKind.Roller);
            AudioManager.Instance.StopLoop(LoopSfxKind.SplatlingSpin);
            AudioManager.Instance.StopLoop(LoopSfxKind.ChargerCharge);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void AutoHookScene()
        {
            var weapons = Resources.FindObjectsOfTypeAll<WeaponController>();
            for (int i = 0; i < weapons.Length; i++)
            {
                WeaponController w = weapons[i];
                if (w == null || !w.gameObject.scene.isLoaded)
                    continue;

                EnsureDriver(w);
            }
        }

        public static WeaponAudioDriver EnsureDriver(WeaponController weapon)
        {
            if (weapon == null) return null;
            var driver = weapon.GetComponent<WeaponAudioDriver>();
            if (driver == null)
            {
                driver = weapon.gameObject.AddComponent<WeaponAudioDriver>();
            }
            return driver;
        }
    }
}
