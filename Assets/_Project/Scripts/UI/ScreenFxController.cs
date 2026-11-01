using P1.Ink;
using P1.Player;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// P31 镜头反馈控制器（当前实现：受击色差 kick）。
    /// 订阅 CombatantHealth.DamageApplied——本地玩家受击瞬间把色差强度打到峰值，
    /// 随后按指数衰减回 0；强度为 0 时 FullScreenPass 像素直通。
    /// 桌面端启用，移动端跳过（FullScreenPass 由 Installer 只挂到 PC_Renderer）。
    /// 与 UI 层 KillFlashView（击杀边缘闪光）互补：本控制器负责"被击中"的镜头震感。
    /// </summary>
    public class ScreenFxController : MonoBehaviour
    {
        private const float KickPeak = 0.045f;     // 受击瞬间色边峰值（uv 偏移量，约 4.5% 屏宽）
        private const float DecayPerSecond = 7.5f; // 指数衰减速率（越大消退越快）
        private const float Cutoff = 0.0005f;      // 低于该值直接归零，避免长尾微偏移

        private static readonly int ChromaAmountId = Shader.PropertyToID("_ChromaAmount");

        private Material _material;
        private float _chroma;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // 移动端由画质分级关闭（FullScreenPass 未挂 Mobile_Renderer，此处双保险直接不启动）
            if (Application.isMobilePlatform || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return;
            }

            // 仅在墨水地面系统存在的对局/测试场景启动，避免 UIDemo 等纯 UI 场景白挂组件
            if (FindAnyObjectByType<InkSurfaceController>() == null)
            {
                return;
            }

            var go = new GameObject("ScreenFxController");
            go.AddComponent<ScreenFxController>();
        }

        private void Awake()
        {
            // 与 Installer 挂到 PC_Renderer 的 FullScreenPass 共享同一材质资产：
            // 控制器写参数 = 镜头 Pass 读参数。找不到资产时回退运行时新建（仅编辑器可见效果）。
            _material = Resources.Load<Material>("Mat_ScreenFx");
            if (_material == null)
            {
                var shader = Shader.Find("P1/ScreenFx");
                if (shader == null)
                {
                    Debug.LogWarning("[ScreenFx] P1/ScreenFx Shader 未找到，镜头反馈已停用。");
                    Destroy(gameObject);
                    return;
                }
                _material = new Material(shader);
            }
            _material.SetFloat(ChromaAmountId, 0f);
        }

        private void OnEnable()
        {
            CombatantHealth.DamageApplied += HandleDamageApplied;
        }

        private void OnDisable()
        {
            CombatantHealth.DamageApplied -= HandleDamageApplied;
        }

        private void HandleDamageApplied(CombatantHealth victim, CombatantHealth attacker, float damage, bool killed)
        {
            // 只响应"本地玩家被击中"（击杀瞬间已有 KillFlashView 白闪，不叠加色差）
            if (victim == null || killed) return;
            if (!CombatantHealth.IsLocalPlayer(victim)) return;
            if (damage <= 0f) return;

            // 伤害越大色边越强：60 HP 满额一击到峰值，擦伤按比例缩小
            float scale = Mathf.Clamp01(damage / 60f);
            _chroma = Mathf.Max(_chroma, KickPeak * (0.45f + 0.55f * scale));
        }

        private void Update()
        {
            if (_material == null) return;

            if (_chroma > 0f)
            {
                _chroma *= Mathf.Exp(-DecayPerSecond * Time.deltaTime);
                if (_chroma < Cutoff) _chroma = 0f;
                _material.SetFloat(ChromaAmountId, _chroma);
            }
            // 注：强度归零后未禁用 FullScreenPass——一次直通 blit 在桌面端 <0.5ms，
            // 禁用/启用 feature 需要运行时拿到 RendererData 资产引用，成本高于收益，保持常开直通。
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }
    }
}
