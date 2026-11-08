using UnityEngine;
using UnityEngine.Serialization;

namespace P1.Core
{
    /// <summary>
    /// P5-C 音频混音调参：双段 BGM 音量/交叉淡化、比赛流程提示音音量与 Ducking 包络。
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
        [Tooltip("剩余秒数降到该值以下时切入决战段 BGM，并播放一分钟提示音")]
        [Min(0f)] public float finalStageLeadSeconds = 60f;

        [Header("比赛流程提示音")]
        [Tooltip("开赛/GO/一分钟/终场提示音的单发音量（不再有播报员语音）")]
        [FormerlySerializedAs("voiceVolume")]
        [Range(0f, 1f)] public float matchCueVolume = 0.95f;

        [Header("Ducking（BGM 闪避）")]
        [Tooltip("流程提示音响时 BGM 下压比例：0.55 = 压到 45%")]
        [FormerlySerializedAs("announcementDuckAmount")]
        [Range(0f, 0.9f)] public float matchCueDuckAmount = 0.55f;
        [Tooltip("大招爆发时 BGM 下压比例")]
        [Range(0f, 0.9f)] public float ultDuckAmount = 0.45f;
        [Tooltip("玩家阵亡时 BGM 下压比例")]
        [Range(0f, 0.9f)] public float deathDuckAmount = 0.55f;
        [Tooltip("下压到目标值所需时间（秒），越小越干脆")]
        [Min(0.01f)] public float duckAttackSeconds = 0.08f;
        [Tooltip("提示音/事件结束后恢复原音量所需时间（秒）")]
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
        [Tooltip("双枪战术翻滚（P40）：高于脚步、低于开火，强调位移反馈")]
        [Range(0f, 1f)] public float dodgeRollVolume = 0.42f;
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
        [Range(0f, 1f)] public float chargerChargeLoopVolume = 0.38f;
        [Tooltip("蓄力狙循环音随蓄力进度的最低音量乘法器：轻蓄更安静，满蓄=1")]
        [Range(0f, 1f)] public float chargerChargeRampMinVolume = 0.40f;

        [Header("涂地补充（P-Audio：墨花分档 / 打墙 / 泵 / 死亡）")]
        [Tooltip("墨花单发音量（splat_small / splat_big 共用，按墨斑大小分档）")]
        [Range(0f, 1f)] public float splatVolume = 0.42f;
        [Tooltip("墨水打墙：比落地略轻（墙没有体积）")]
        [Range(0f, 1f)] public float inkHitWallVolume = 0.34f;
        [Tooltip("爆破枪空爆（旧版烘了却从未播放）")]
        [Range(0f, 1f)] public float blasterBoomVolume = 0.62f;
        [Tooltip("爆破枪换气泵（仅本地）")]
        [Range(0f, 1f)] public float blasterPumpVolume = 0.30f;
        [Range(0f, 1f)] public float splatlingReadyVolume = 0.40f;
        [Range(0f, 1f)] public float stormWindVolume = 0.38f;
        [Tooltip("泼桶落墨重音（旧版烘了却从未播放）")]
        [Range(0f, 1f)] public float sloshLandVolume = 0.52f;
        [Tooltip("墨罐回满（旧版烘了却从未播放）")]
        [Range(0f, 1f)] public float refillFullVolume = 0.34f;
        [Range(0f, 1f)] public float deathVolume = 0.75f;

        [Header("战斗状态提示（P-Audio）")]
        [Tooltip("出生保护生效：只给本地玩家")]
        [Range(0f, 1f)] public float spawnProtectVolume = 0.42f;
        [Tooltip("低血心跳：必须压得住，只做存在感")]
        [Range(0f, 1f)] public float lowHealthVolume = 0.34f;
        [Tooltip("低血心跳的重复间隔（秒），约等于一次心跳")]
        [Min(0.1f)] public float lowHealthThrottleSeconds = 1.1f;
        [Range(0f, 1f)] public float respawnTickVolume = 0.30f;

        [Header("战局提示（P-Audio）")]
        [Range(0f, 1f)] public float leadChangeVolume = 0.36f;
        [Tooltip("比分反超提示的最小间隔：避免比分来回拉锯时刷成音墙")]
        [Min(0.5f)] public float leadChangeThrottleSeconds = 6f;
        [Range(0f, 1f)] public float final10sVolume = 0.40f;
        [Tooltip("敌方大招来袭警告：按距离衰减，远处只留方位感")]
        [Range(0f, 1f)] public float enemySpecialWarnVolume = 0.40f;

        [Header("操作反馈（P-Audio）")]
        [Range(0f, 1f)] public float weaponSwitchVolume = 0.38f;
        [Range(0f, 1f)] public float chargeCancelVolume = 0.34f;
        [Range(0f, 1f)] public float bombBounceVolume = 0.26f;
        [Min(0.01f)] public float bombBounceThrottleSeconds = 0.08f;
        [Tooltip("准星锁定 tick：要很轻，否则瞄来瞄去会烦")]
        [Range(0f, 1f)] public float reticleLockVolume = 0.26f;
        [Min(0.02f)] public float reticleLockThrottleSeconds = 0.18f;
        [Range(0f, 1f)] public float pauseVolume = 0.40f;

        [Header("音效节流窗口（秒）")]
        [Min(0.01f)] public float footstepThrottleSeconds = 0.16f;
        [Min(0.01f)] public float swimSplashThrottleSeconds = 0.12f;
        [Min(0.01f)] public float hurtThrottleSeconds = 0.10f;
        [Min(0.01f)] public float hitMarkerThrottleSeconds = 0.035f;
        [Min(0.01f)] public float uiThrottleSeconds = 0.03f;
    }
}