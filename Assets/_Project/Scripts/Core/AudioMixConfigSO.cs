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
        [Range(0f, 1f)] public float bgmVolume = 0.34f;
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
    }
}