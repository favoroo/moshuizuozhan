using P1.Ink;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.Core
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "P1/Game Config")]
    public class GameConfigSO : ScriptableObject
    {
        [Header("子配置引用")]
        public InkGridConfigSO inkGridConfig;
        public PlayerConfigSO playerConfig;
        public WeaponConfigSO weaponConfig;

        [Header("比赛时长与规则")]
        public float matchDurationSeconds = 120f;
        public float finalWarningSeconds = 30f;

        [Header("俯视倾斜相机参数")]
        [Tooltip("是否启用透视模式（true=3D透视带自然纵深与高度落差；false=正交投影）")]
        public bool usePerspective = true;
        [Tooltip("透视模式下的视野广角（度），推荐 38~42，兼顾立体纵深与极低边缘畸变")]
        public float cameraFov = 40f;
        [Tooltip("正交模式下的视野半高")]
        public float cameraOrthoSize = 4.4f;
        [Tooltip("相机相对于玩家的偏移 (Y=高度, Z=后方距离)。(0, 9.2, -8.2) 对应约 48.3° 倾斜俯角，高低差与站姿立体感极强")]
        public Vector3 cameraOffset = new Vector3(0f, 9.2f, -8.2f);
        public float cameraLookAheadMax = 1.5f;
        public float cameraSmoothTime = 0.10f;

        [Header("屏幕震颤 (P6-A)")]
        [Tooltip("Trauma=1 时的最大水平抖幅（米）。0.42 在俯视 9m 高度下强劲但不晕眩")]
        public float shakeMaxOffset = 0.42f;
        [Tooltip("Trauma=1 时的最大镜头 Roll 角（度）。仅 1.3° 用于制造冲击感，不影响瞄准")]
        public float shakeMaxRollDegrees = 1.3f;
        [Tooltip("Trauma 每秒自然衰减量。2.4 = 满额震颤约 0.42s 完全平息")]
        public float shakeDecayPerSecond = 2.4f;
        [Tooltip("高频震颤的角频率 (rad/s)。层叠正弦驱动，避免逐帧随机噪声造成的嗡嗡静电感")]
        public float shakeFrequency = 38f;
        [Tooltip("爆炸/大招震屏的距离衰减半径（米）：半径内按距离平方衰减，半径外不震")]
        public float shakeExplosionRadius = 14f;
    }
}
