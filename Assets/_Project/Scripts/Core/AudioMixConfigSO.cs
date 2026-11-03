using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// P5-C 音频混音调参：双段 BGM 音量/交叉淡化、播报员语音音量与 Ducking 包络。
    /// 所有数值集中在这里，AudioManager 不硬编码（AGENTS.md 规则 4）。
    /// </summary>
    [CreateAssetMenu(fileName = "AudioMixConfig", menuName = "P1/Audio Mix Config")]
    public class AudioMixConfigSO : ScriptableObject
    {
        [Header("BGM 音量与切换")]
        [Tooltip("P30：0.34 → 0.26。BGM 原曲 -10.5 LUFS 且 crest factor 仅 ~9.6dB（高压缩高能量），" +
                 "0.26 使对局音乐床约 -22 LUFS，武器瞬态（峰值 -12~-15 dBFS）得以浮出")]
        [Range(0f, 1f)] public float bgmVolume = 0.26f;
        [Min(0.01f)] public float bgmCrossfadeSeconds = 1.2f;
        [Min(0.01f)] public float bgmFadeOutSeconds = 1.4f;
        [Tooltip("剩余秒数降到该值以下时切入决战段 BGM，并播报 1 Minute Remaining")]
        [Min(0f)] public float finalStageLeadSeconds = 60f;

        [Header("播报员语音")]
        [Range(0f, 1f)] public float voiceVolume = 0.95f;

        [Header("Ducking（BGM 闪避）")]
        [Tooltip("播报员说话时 BGM 下压比例：0.55 = 压到 45%")]
        [Range(0f, 0.9f)] public float announcementDuckAmount = 0.55f;
        [Tooltip("大招爆发时 BGM 下压比例")]
        [Range(0f, 0.9f)] public float ultDuckAmount = 0.45f;
        [Tooltip("玩家阵亡时 BGM 下压比例")]
        [Range(0f, 0.9f)] public float deathDuckAmount = 0.55f;
        [Tooltip("下压到目标值所需时间（秒），越小越干脆")]
        [Min(0.01f)] public float duckAttackSeconds = 0.08f;
        [Tooltip("播报/事件结束后恢复原音量所需时间（秒）")]
        [Min(0.01f)] public float duckReleaseSeconds = 0.55f;
        [Tooltip("事件结束后额外保持下压的时间（秒），避免尾音被抬起")]
        [Min(0f)] public float duckTailSeconds = 0.25f;

        [Header("战斗 Ducking（P30：持续开火期间压低 BGM，让武器音浮出）")]
        [Tooltip("本地开火时 BGM 下压比例：0.18 = 压到 82%（约 -1.7dB），持续开火时稳定保持")]
        [Range(0f, 0.5f)] public float combatDuckAmount = 0.18f;
        [Tooltip("最后一次开火后保持下压的时长（秒），停火后经 duckReleaseSeconds 恢复")]
        [Min(0.05f)] public float combatDuckHoldSeconds = 0.45f;

        [Header("单发音量阶梯（P30，对齐 Inkwave sfx-catalog 增益比例）")]
        [Tooltip("Shooter/Dualies/Splatling 开火（Inkwave shoot_shooter 0.72）")]
        [Range(0f, 1f)] public float shotVolume = 0.72f;
        [Range(0f, 1f)] public float rollerFlickVolume = 0.60f;
        [Tooltip("蓄力狙开火下限（无蓄力）")]
        [Range(0f, 1f)] public float chargerShotVolumeMin = 0.50f;
        [Tooltip("蓄力狙开火上限（满蓄）")]
        [Range(0f, 1f)] public float chargerShotVolumeMax = 0.66f;
        [Range(0f, 1f)] public float blasterShotVolume = 0.58f;
        [Range(0f, 1f)] public float sloshThrowVolume = 0.58f;
        [Tooltip("墨拍身体（Inkwave ink_hit_body 0.50）")]
        [Range(0f, 1f)] public float hitBodyVolume = 0.50f;
        [Tooltip("准星命中 tick，关键反馈层，保持高于开火")]
        [Range(0f, 1f)] public float hitMarkerVolume = 0.55f;
        [Range(0f, 1f)] public float jumpVolume = 0.50f;
        [Range(0f, 1f)] public float landVolume = 0.55f;
        [Range(0f, 1f)] public float squidTransformVolume = 0.60f;
        [Tooltip("干地脚步，刻意压低避免与武器争抢（Inkwave step_dry 0.16）")]
        [Range(0f, 1f)] public float stepDryVolume = 0.26f;
        [Range(0f, 1f)] public float stepInkVolume = 0.30f;
        [Tooltip("踩敌墨脚步：危险提示 cue，高于普通脚步")]
        [Range(0f, 1f)] public float stepEnemyInkVolume = 0.36f;
        [Range(0f, 1f)] public float swimSplashVolume = 0.45f;

        [Header("Bot 远场开火（P30：4v4 战场氛围，严格压低于本地开火）")]
        [Tooltip("贴脸（0m）时的最大音量，随距离平方衰减")]
        [Range(0f, 1f)] public float botShotVolume = 0.30f;
        [Tooltip("超过该距离（米）直接不播")]
        [Min(1f)] public float botShotMaxDistance = 24f;
        [Tooltip("全局节流窗口（秒）：7 个 Bot 齐射也不会叠成音墙")]
        [Min(0.02f)] public float botShotThrottleSeconds = 0.05f;

        [Header("循环音效（P1-Loop，P30 按 Inkwave 循环增益比例整体下压）")]
        [Range(0f, 1f)] public float loopMasterVolume = 0.85f;
        [Tooltip("循环音效淡入淡出时长（秒，0.08~0.15s）")]
        [Range(0.05f, 0.30f)] public float loopFadeSeconds = 0.10f;
        [Range(0f, 1f)] public float rollerLoopVolume = 0.55f;
        [Range(0f, 1f)] public float splatlingSpinLoopVolume = 0.50f;
        [Range(0f, 1f)] public float swimLoopVolume = 0.45f;
        [Range(0f, 1f)] public float climbLoopVolume = 0.45f;
        [Range(0f, 1f)] public float enemyInkSizzleLoopVolume = 0.52f;
        [Range(0f, 1f)] public float stormRainLoopVolume = 0.60f;
        [Range(0f, 1f)] public float chargerChargeLoopVolume = 0.50f;

        [Header("音效节流窗口（秒）")]
        [Min(0.01f)] public float footstepThrottleSeconds = 0.16f;
        [Min(0.01f)] public float swimSplashThrottleSeconds = 0.12f;
        [Min(0.01f)] public float hurtThrottleSeconds = 0.10f;
        [Min(0.01f)] public float hitMarkerThrottleSeconds = 0.035f;
        [Min(0.01f)] public float uiThrottleSeconds = 0.03f;
    }
}