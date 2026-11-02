using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace P1.Core
{
    /// <summary>
    /// 桌面端可自定义绑定的核心游戏操作动作。
    /// </summary>
    public enum DesktopAction
    {
        MoveForward = 0,    // 前进
        MoveBackward = 1,   // 后退
        MoveLeft = 2,       // 向左移动
        MoveRight = 3,      // 向右移动
        Jump = 4,           // 跳跃
        Fire = 5,           // 开火涂地
        Swim = 6,           // 潜墨 · 爬墙
        SubWeapon = 7,      // 投掷副武器 / 手雷
        Special = 8         // 释放大招
    }

    /// <summary>
    /// 输入设备类型（键盘 / 鼠标）。
    /// </summary>
    public enum DesktopBindingDevice
    {
        None = 0,
        Keyboard = 1,
        Mouse = 2
    }

    /// <summary>
    /// 单个键位/按键绑定槽位。
    /// </summary>
    [Serializable]
    public struct DesktopBindingSlot : IEquatable<DesktopBindingSlot>
    {
        public DesktopBindingDevice device;
        public Key key;
        public int mouseButton; // 0=左键, 1=右键, 2=中键, 3=侧键1(Forward), 4=侧键2(Back)

        public static DesktopBindingSlot None => new DesktopBindingSlot
        {
            device = DesktopBindingDevice.None,
            key = Key.None,
            mouseButton = -1
        };

        public static DesktopBindingSlot FromKey(Key key) => new DesktopBindingSlot
        {
            device = DesktopBindingDevice.Keyboard,
            key = key,
            mouseButton = -1
        };

        public static DesktopBindingSlot FromMouse(int button) => new DesktopBindingSlot
        {
            device = DesktopBindingDevice.Mouse,
            key = Key.None,
            mouseButton = button
        };

        public bool IsEmpty => device == DesktopBindingDevice.None || (device == DesktopBindingDevice.Keyboard && key == Key.None);

        public bool Equals(DesktopBindingSlot other)
        {
            if (device != other.device) return false;
            if (device == DesktopBindingDevice.Keyboard) return key == other.key;
            if (device == DesktopBindingDevice.Mouse) return mouseButton == other.mouseButton;
            return true;
        }

        public override bool Equals(object obj) => obj is DesktopBindingSlot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)device;
                hash = (hash * 397) ^ (int)key;
                hash = (hash * 397) ^ mouseButton;
                return hash;
            }
        }

        public static bool operator ==(DesktopBindingSlot left, DesktopBindingSlot right) => left.Equals(right);
        public static bool operator !=(DesktopBindingSlot left, DesktopBindingSlot right) => !left.Equals(right);

        /// <summary>
        /// 获取面向玩家的友好键位显示文本。
        /// </summary>
        public string GetDisplayName()
        {
            if (device == DesktopBindingDevice.Mouse)
            {
                return mouseButton switch
                {
                    0 => "鼠标左键",
                    1 => "鼠标右键",
                    2 => "鼠标中键",
                    3 => "鼠标侧键1",
                    4 => "鼠标侧键2",
                    _ => $"鼠标键{mouseButton}"
                };
            }

            if (device == DesktopBindingDevice.Keyboard)
            {
                return key switch
                {
                    Key.None => "-",
                    Key.Space => "空格",
                    Key.LeftShift => "左 Shift",
                    Key.RightShift => "右 Shift",
                    Key.LeftCtrl => "左 Ctrl",
                    Key.RightCtrl => "右 Ctrl",
                    Key.LeftAlt => "左 Alt",
                    Key.RightAlt => "右 Alt",
                    Key.Tab => "Tab",
                    Key.Enter => "回车",
                    Key.Backspace => "退格",
                    Key.UpArrow => "↑",
                    Key.DownArrow => "↓",
                    Key.LeftArrow => "←",
                    Key.RightArrow => "→",
                    Key.Digit0 => "0",
                    Key.Digit1 => "1",
                    Key.Digit2 => "2",
                    Key.Digit3 => "3",
                    Key.Digit4 => "4",
                    Key.Digit5 => "5",
                    Key.Digit6 => "6",
                    Key.Digit7 => "7",
                    Key.Digit8 => "8",
                    Key.Digit9 => "9",
                    _ => key.ToString()
                };
            }

            return "-";
        }
    }

    /// <summary>
    /// 单个动作的绑定数据（包含主键与备用键）。
    /// </summary>
    [Serializable]
    public class DesktopActionBindingData
    {
        public DesktopAction action;
        public DesktopBindingSlot primary;
        public DesktopBindingSlot secondary;

        public DesktopActionBindingData Clone()
        {
            return new DesktopActionBindingData
            {
                action = this.action,
                primary = this.primary,
                secondary = this.secondary
            };
        }
    }

    /// <summary>
    /// 全套桌面按键持久化配置容器。
    /// </summary>
    [Serializable]
    public class DesktopKeyBindingsConfig
    {
        public List<DesktopActionBindingData> bindings = new List<DesktopActionBindingData>();

        public DesktopActionBindingData Find(DesktopAction action)
        {
            if (bindings == null) return null;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i] != null && bindings[i].action == action)
                    return bindings[i];
            }
            return null;
        }

        public DesktopKeyBindingsConfig Clone()
        {
            var copy = new DesktopKeyBindingsConfig();
            if (bindings != null)
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    if (bindings[i] != null) copy.bindings.Add(bindings[i].Clone());
                }
            }
            return copy;
        }
    }

    /// <summary>
    /// 全局桌面按键绑定服务（持久化、查询、冲突仲裁、改键与默认配置）。
    /// </summary>
    public static class DesktopKeyBindingService
    {
        public const string StorageKey = "P1_DesktopKeyBindings_v1";

        private static bool _initialized;
        private static DesktopKeyBindingsConfig _currentConfig;

        public static event Action OnBindingsChanged;

        /// <summary>
        /// 获取动作的玩家可见名称。
        /// </summary>
        public static string GetActionDisplayName(DesktopAction action)
        {
            return action switch
            {
                DesktopAction.MoveForward => "前进",
                DesktopAction.MoveBackward => "后退",
                DesktopAction.MoveLeft => "向左移动",
                DesktopAction.MoveRight => "向右移动",
                DesktopAction.Jump => "跳跃",
                DesktopAction.Fire => "开火涂地",
                DesktopAction.Swim => "潜墨 · 爬墙",
                DesktopAction.SubWeapon => "投掷副武器",
                DesktopAction.Special => "释放大招",
                _ => action.ToString()
            };
        }

        public static void EnsureInitialized()
        {
            if (_initialized && _currentConfig != null) return;
            LoadFromPlayerPrefs();
            _initialized = true;
        }

        /// <summary>
        /// 创建标准默认键位配置。
        /// </summary>
        public static DesktopKeyBindingsConfig CreateDefaultConfig()
        {
            var config = new DesktopKeyBindingsConfig();
            config.bindings.Add(new DesktopActionBindingData
            {
                action = DesktopAction.MoveForward,
                primary = DesktopBindingSlot.FromKey(Key.W),
                secondary = DesktopBindingSlot.FromKey(Key.UpArrow)
            });
            config.bindings.Add(new DesktopActionBindingData
            {
                action = DesktopAction.MoveBackward,
                primary = DesktopBindingSlot.FromKey(Key.S),
                secondary = DesktopBindingSlot.FromKey(Key.DownArrow)
            });
            config.bindings.Add(new DesktopActionBindingData
            {
                action = DesktopAction.MoveLeft,
                primary = DesktopBindingSlot.FromKey(Key.A),
                secondary = DesktopBindingSlot.FromKey(Key.LeftArrow)
            });
            config.bindings.Add(new DesktopActionBindingData
            {
                action = DesktopAction.MoveRight,
                primary = DesktopBindingSlot.FromKey(Key.D),
                secondary = DesktopBindingSlot.FromKey(Key.RightArrow)
            });
            config.bindings.Add(new DesktopActionBindingData
            {
                action = DesktopAction.Jump,
                primary = DesktopBindingSlot.FromKey(Key.Space),
                secondary = DesktopBindingSlot.None
            });
            config.bindings.Add(new DesktopActionBindingData
            {
                action = DesktopAction.Fire,
                primary = DesktopBindingSlot.FromMouse(0), // 左键
                secondary = DesktopBindingSlot.None
            });
            config.bindings.Add(new DesktopActionBindingData
            {
                action = DesktopAction.Swim,
                primary = DesktopBindingSlot.FromMouse(1), // 右键
                secondary = DesktopBindingSlot.FromKey(Key.LeftShift)
            });
            config.bindings.Add(new DesktopActionBindingData
            {
                action = DesktopAction.SubWeapon,
                primary = DesktopBindingSlot.FromKey(Key.E),
                secondary = DesktopBindingSlot.FromMouse(2) // 中键
            });
            config.bindings.Add(new DesktopActionBindingData
            {
                action = DesktopAction.Special,
                primary = DesktopBindingSlot.FromKey(Key.Q),
                secondary = DesktopBindingSlot.None
            });
            return config;
        }

        public static void GetBinding(DesktopAction action, out DesktopBindingSlot primary, out DesktopBindingSlot secondary)
        {
            EnsureInitialized();
            var item = _currentConfig.Find(action);
            if (item != null)
            {
                primary = item.primary;
                secondary = item.secondary;
            }
            else
            {
                primary = DesktopBindingSlot.None;
                secondary = DesktopBindingSlot.None;
            }
        }

        /// <summary>
        /// 尝试绑定新按键（带智能冲突处理：若与其他动作主键冲突则互换，若与备用键冲突则清除）。
        /// </summary>
        public static bool TryBind(DesktopAction action, bool isSecondary, DesktopBindingSlot newSlot, out DesktopAction? swappedAction)
        {
            EnsureInitialized();
            swappedAction = null;

            // 主键不能为空
            if (!isSecondary && newSlot.IsEmpty)
            {
                return false;
            }

            var target = _currentConfig.Find(action);
            if (target == null) return false;

            DesktopBindingSlot oldSlot = isSecondary ? target.secondary : target.primary;
            if (oldSlot.Equals(newSlot)) return true; // 无变化

            // 若设置为 None（清空备用键）
            if (newSlot.IsEmpty)
            {
                if (isSecondary)
                {
                    target.secondary = DesktopBindingSlot.None;
                    Save();
                    OnBindingsChanged?.Invoke();
                    return true;
                }
                return false;
            }

            // 检查冲突
            for (int i = 0; i < _currentConfig.bindings.Count; i++)
            {
                var entry = _currentConfig.bindings[i];
                if (entry == null) continue;

                if (entry.action == action)
                {
                    // 与同动作的另一个槽位冲突：清空另一个槽位
                    if (isSecondary && entry.primary.Equals(newSlot))
                    {
                        // 备用键不能与自己的主键相同，直接拒绝或清空备用键
                        return false;
                    }
                    if (!isSecondary && entry.secondary.Equals(newSlot))
                    {
                        entry.secondary = DesktopBindingSlot.None;
                    }
                }
                else
                {
                    // 与其他动作冲突
                    if (entry.primary.Equals(newSlot))
                    {
                        // 对方是主键：为了保证对方主键非空，进行互换（Swap）
                        entry.primary = oldSlot;
                        swappedAction = entry.action;
                    }
                    else if (entry.secondary.Equals(newSlot))
                    {
                        // 对方是备用键：直接清空对方备用键
                        entry.secondary = DesktopBindingSlot.None;
                    }
                }
            }

            if (isSecondary)
            {
                target.secondary = newSlot;
            }
            else
            {
                target.primary = newSlot;
            }

            Save();
            OnBindingsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 清空某动作的备用键。
        /// </summary>
        public static void ClearSecondary(DesktopAction action)
        {
            EnsureInitialized();
            var target = _currentConfig.Find(action);
            if (target != null && !target.secondary.IsEmpty)
            {
                target.secondary = DesktopBindingSlot.None;
                Save();
                OnBindingsChanged?.Invoke();
            }
        }

        /// <summary>
        /// 恢复默认键位配置并保存。
        /// </summary>
        public static void ResetToDefaults()
        {
            _currentConfig = CreateDefaultConfig();
            _initialized = true;
            Save();
            OnBindingsChanged?.Invoke();
        }

        public static void Save()
        {
            if (_currentConfig == null) return;
            string json = JsonUtility.ToJson(_currentConfig);
            PlayerPrefs.SetString(StorageKey, json);
            PlayerPrefs.Save();
        }

        public static void LoadFromPlayerPrefs()
        {
            if (!PlayerPrefs.HasKey(StorageKey))
            {
                _currentConfig = CreateDefaultConfig();
                return;
            }

            string json = PlayerPrefs.GetString(StorageKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                _currentConfig = CreateDefaultConfig();
                return;
            }

            try
            {
                _currentConfig = JsonUtility.FromJson<DesktopKeyBindingsConfig>(json);
                // 校验必须涵盖所有 9 个动作
                var defaults = CreateDefaultConfig();
                for (int i = 0; i < defaults.bindings.Count; i++)
                {
                    var def = defaults.bindings[i];
                    var existing = _currentConfig.Find(def.action);
                    if (existing == null)
                    {
                        _currentConfig.bindings.Add(def.Clone());
                    }
                    else if (existing.primary.IsEmpty)
                    {
                        existing.primary = def.primary;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DesktopKeyBindingService] 读取按键配置失败，使用默认值: {ex.Message}");
                _currentConfig = CreateDefaultConfig();
            }
        }

        // ------------------------------------------------------------------
        // 输入状态查询（供 PlayerController 逐帧调用）
        // ------------------------------------------------------------------

        public static bool IsActionPressed(DesktopAction action)
        {
            EnsureInitialized();
            var item = _currentConfig.Find(action);
            if (item == null) return false;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            return IsSlotPressed(item.primary, kb, mouse) || IsSlotPressed(item.secondary, kb, mouse);
        }

        public static bool WasActionPressedThisFrame(DesktopAction action)
        {
            EnsureInitialized();
            var item = _currentConfig.Find(action);
            if (item == null) return false;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            return WasSlotPressedThisFrame(item.primary, kb, mouse) || WasSlotPressedThisFrame(item.secondary, kb, mouse);
        }

        public static bool IsSlotPressed(DesktopBindingSlot slot, Keyboard kb, Mouse mouse)
        {
            if (slot.device == DesktopBindingDevice.Keyboard && kb != null && slot.key != Key.None)
            {
                var control = kb[slot.key];
                return control != null && control.isPressed;
            }

            if (slot.device == DesktopBindingDevice.Mouse && mouse != null)
            {
                return slot.mouseButton switch
                {
                    0 => mouse.leftButton.isPressed,
                    1 => mouse.rightButton.isPressed,
                    2 => mouse.middleButton.isPressed,
                    3 => mouse.forwardButton.isPressed,
                    4 => mouse.backButton.isPressed,
                    _ => false
                };
            }

            return false;
        }

        public static bool WasSlotPressedThisFrame(DesktopBindingSlot slot, Keyboard kb, Mouse mouse)
        {
            if (slot.device == DesktopBindingDevice.Keyboard && kb != null && slot.key != Key.None)
            {
                var control = kb[slot.key];
                return control != null && control.wasPressedThisFrame;
            }

            if (slot.device == DesktopBindingDevice.Mouse && mouse != null)
            {
                return slot.mouseButton switch
                {
                    0 => mouse.leftButton.wasPressedThisFrame,
                    1 => mouse.rightButton.wasPressedThisFrame,
                    2 => mouse.middleButton.wasPressedThisFrame,
                    3 => mouse.forwardButton.wasPressedThisFrame,
                    4 => mouse.backButton.wasPressedThisFrame,
                    _ => false
                };
            }

            return false;
        }
    }
}
