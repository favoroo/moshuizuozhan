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

        [Header("第三人称相机 (TPS, P9)")]
        [Tooltip("人形状态下相机枢轴相对脚底的高度（米）。1.85m 略高于头顶，准星不会被自己挡住")]
        public float tpsPivotHeight = 1.85f;
        [Tooltip("潜墨状态下相机枢轴高度（米）。角色压扁贴地，镜头随之降低")]
        public float tpsPivotHeightSwimming = 1.30f;
        [Tooltip("枢轴跟随的平滑时间（秒）。越小越跟手，越大越稳")]
        public float tpsPivotSmoothTime = 0.06f;
        [Tooltip("人形状态相机吊臂长度（米）")]
        public float tpsDistance = 4.5f;
        [Tooltip("潜墨状态相机吊臂长度（米）")]
        public float tpsDistanceSwimming = 4.1f;
        [Tooltip("贴墙时允许收进的最小吊臂长度（米），不得为 0 以免镜头钻进角色身体")]
        public float tpsMinDistance = 0.55f;
        [Tooltip("第三人称垂直视野（度）。52° 在 16:9 下约等于 82° 水平视野，对齐 Inkwave")]
        public float tpsFov = 52f;
        [Tooltip("默认俯仰角（度）：正数=低头俯视，负数=抬头仰视。6° 对应 Inkwave 默认 -0.1rad")]
        public float tpsPitchDefault = 6f;
        [Tooltip("最大抬头角（度，负值）")]
        public float tpsPitchMin = -60f;
        [Tooltip("最大低头角（度，正值）")]
        public float tpsPitchMax = 66f;
        [Tooltip("相机防穿墙球体探针半径（米）")]
        public float tpsCollisionRadius = 0.32f;
        [Tooltip("吊臂被墙强行压到很近时向右肩偏移的最大距离（米），避免角色背影挡住准星")]
        public float tpsShoulderOffset = 0.55f;
        [Tooltip("吊臂短于该距离（米）时开始启用右肩偏移")]
        public float tpsShoulderCloseDistance = 2.8f;
        [Tooltip("镜头离脚下真实地面的最小高度（米）")]
        public float tpsGroundClearance = 0.25f;
        [Tooltip("屏幕中心瞄准射线的最大探测距离（米）")]
        public float tpsAimRayDistance = 70f;
        [Tooltip("第三人称下震屏位移的额外缩放（0~1）。4.5m 吊臂下 0.42m 原始震幅过强，乘 0.45 更舒适")]
        [Range(0f, 1f)]
        public float tpsShakeScale = 0.45f;
        [Tooltip("蓄力狙满蓄力时的 FOV 收窄量（度）")]
        public float tpsChargerZoomFov = 14f;
        [Tooltip("蓄力狙满蓄力时的吊臂拉近量（米）")]
        public float tpsChargerZoomDistance = 0.6f;
        [Tooltip("手雷瞄准时 FOV 收窄量（度）")]
        public float tpsSubAimFovKick = 4f;
        [Tooltip("手雷瞄准时吊臂拉远量（米），便于看清远端落点")]
        public float tpsSubAimDistanceKick = 0.35f;

        [Header("俯视倾斜相机参数（旧版，保留序列化兼容，P9 起不再使用）")]
        [Tooltip("旧版俯视参数：是否启用透视模式")]
        public bool usePerspective = true;
        [Tooltip("旧版俯视参数：透视视野")]
        public float cameraFov = 40f;
        [Tooltip("旧版俯视参数：正交半高")]
        public float cameraOrthoSize = 4.4f;
        [Tooltip("旧版俯视参数：相机相对玩家的偏移")]
        public Vector3 cameraOffset = new Vector3(0f, 9.2f, -8.2f);
        [Tooltip("旧版俯视参数：瞄准前移上限")]
        public float cameraLookAheadMax = 1.5f;
        [Tooltip("旧版俯视参数：跟随平滑时间")]
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
