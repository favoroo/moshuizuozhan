using P1.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// P5-C 音频绑定工具：把播报员语音、双段 BGM 与 AudioMixConfig 写进当前场景的 AudioManager。
    /// 遵守 AGENTS.md 规则 1：不改 .unity/.asset 的 YAML，用 Editor 脚本改并在保存前 MarkSceneDirty。
    /// </summary>
    public static class AudioSetupTools
    {
        private const string VoiceDir = "Assets/_Project/Audio/Voice";
        private const string MusicDir = "Assets/_Project/Audio/Music";
        private const string SoDir = "Assets/_Project/ScriptableObjects";
        private const string ConfigPath = SoDir + "/AudioMixConfig.asset";

        [MenuItem("P1/音频/1. 绑定播报语音与双段 BGM", false, 20)]
        public static void WireAnnouncerAndMusic()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[P5-C] 请先退出 PlayMode 再绑定音频（绑定需要保存场景）。");
                return;
            }

            AudioManager manager = FindManager();
            if (manager == null)
            {
                Debug.LogError("[P5-C] 当前场景找不到 AudioManager（预期挂在 _Managers 上）。");
                return;
            }

            AudioMixConfigSO config = LoadOrCreateConfig();
            int missing = 0;

            manager.voiceCount3Clip = LoadClip($"{VoiceDir}/vo_announcer_count_3_01.wav", ref missing);
            manager.voiceCount2Clip = LoadClip($"{VoiceDir}/vo_announcer_count_2_01.wav", ref missing);
            manager.voiceCount1Clip = LoadClip($"{VoiceDir}/vo_announcer_count_1_01.wav", ref missing);
            manager.voiceGoClip = LoadClip($"{VoiceDir}/vo_announcer_go_01.wav", ref missing);
            manager.voiceOneMinuteClip = LoadClip($"{VoiceDir}/vo_announcer_one_minute_01.wav", ref missing);
            manager.voiceFinishClip = LoadClip($"{VoiceDir}/vo_announcer_finish_01.wav", ref missing);

            manager.bgmMainClip = LoadClip($"{MusicDir}/bgm_match_main.ogg", ref missing);
            manager.bgmFinalClip = LoadClip($"{MusicDir}/bgm_match_final.ogg", ref missing);
            manager.mixConfig = config;

            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            EditorSceneManager.SaveScene(manager.gameObject.scene);

            Debug.Log($"[P5-C] 音频绑定完成：6 条播报 + 2 段 BGM + AudioMixConfig；缺失 {missing} 项。" +
                      $"场景：{manager.gameObject.scene.path}");
        }

        [MenuItem("P1/音频/2. 体检音频导入设置", false, 21)]
        public static void ReportAudio()
        {
            MediaTools.ReportMedia();
        }

        private static AudioManager FindManager()
        {
            GameObject managers = GameObject.Find("_Managers");
            if (managers != null)
            {
                AudioManager manager = managers.GetComponent<AudioManager>();
                if (manager != null)
                    return manager;
            }

            return Object.FindFirstObjectByType<AudioManager>();
        }

        private static AudioMixConfigSO LoadOrCreateConfig()
        {
            AudioMixConfigSO config = AssetDatabase.LoadAssetAtPath<AudioMixConfigSO>(ConfigPath);
            if (config != null)
                return config;

            if (!AssetDatabase.IsValidFolder(SoDir))
                AssetDatabase.CreateFolder("Assets/_Project", "ScriptableObjects");

            config = ScriptableObject.CreateInstance<AudioMixConfigSO>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[P5-C] 已创建 {ConfigPath}");
            return config;
        }

        private static AudioClip LoadClip(string path, ref int missing)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                missing++;
                Debug.LogError($"[P5-C] 缺少音频素材：{path}（先确认已拷入并 Refresh）");
            }
            return clip;
        }
    }
}