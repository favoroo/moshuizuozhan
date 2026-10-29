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

            // 只有本地玩家触发前台射击音；Bot/队友的声音由各自位置的音效通道负责，
            // 避免 4v4 中多名队友同时开火把单发音效池刷爆。
            if (!CombatantHealth.IsLocalPlayer(_health))
                return;

            AudioManager.Instance.PlayWeaponShot(_weapon.CurrentWeaponKind);
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

            // 2. 加特林蓄力旋转循环音
            if (isAlive && kind == WeaponKind.Splatling && _weapon.IsCharging)
            {
                AudioManager.Instance.PlayLoop(LoopSfxKind.SplatlingSpin);
            }
            else
            {
                AudioManager.Instance.StopLoop(LoopSfxKind.SplatlingSpin);
            }

            // 3. 蓄力狙蓄力循环音与蓄满清脆叮音
            if (isAlive && kind == WeaponKind.Charger && _weapon.IsCharging)
            {
                AudioManager.Instance.PlayLoop(LoopSfxKind.ChargerCharge);

                if (_weapon.Charge01 >= 0.999f && !_wasChargerFull)
                {
                    _wasChargerFull = true;
                    if (AudioManager.Instance.chargerFullClip != null)
                    {
                        AudioManager.Instance.PlayOne(AudioManager.Instance.chargerFullClip, 0.65f);
                    }
                }
            }
            else
            {
                AudioManager.Instance.StopLoop(LoopSfxKind.ChargerCharge);
                if (!_weapon.IsCharging)
                {
                    _wasChargerFull = false;
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
