using System;
using System.Collections.Generic;
using UnityEngine;

namespace P1.UI
{
    /// <summary>单个操作按键的自定义布局配置数据。</summary>
    [Serializable]
    public class ButtonLayoutData
    {
        public string id;
        public Vector2 anchoredPosition;
        public float scale = 1.0f;
        public float alpha = 1.0f;

        public ButtonLayoutData Clone()
        {
            return new ButtonLayoutData
            {
                id = this.id,
                anchoredPosition = this.anchoredPosition,
                scale = this.scale,
                alpha = this.alpha
            };
        }
    }

    /// <summary>移动端全套按键布局配置容器。</summary>
    [Serializable]
    public class MobileLayoutConfig
    {
        public List<ButtonLayoutData> buttons = new List<ButtonLayoutData>();

        public ButtonLayoutData Find(string id)
        {
            if (buttons == null) return null;
            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] != null && buttons[i].id == id)
                    return buttons[i];
            }
            return null;
        }

        public void Set(string id, Vector2 pos, float scale, float alpha)
        {
            if (buttons == null) buttons = new List<ButtonLayoutData>();
            var item = Find(id);
            if (item == null)
            {
                item = new ButtonLayoutData { id = id };
                buttons.Add(item);
            }
            item.anchoredPosition = pos;
            item.scale = Mathf.Clamp(scale, 0.60f, 1.80f);
            item.alpha = Mathf.Clamp(alpha, 0.25f, 1.0f);
        }

        public MobileLayoutConfig Clone()
        {
            var copy = new MobileLayoutConfig();
            if (buttons != null)
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    if (buttons[i] != null) copy.buttons.Add(buttons[i].Clone());
                }
            }
            return copy;
        }
    }

    /// <summary>按键布局的持久化读写与重置工具。</summary>
    public static class MobileLayoutStorage
    {
        public const string StorageKey = "P1_MobileLayoutConfig_v1";

        public static bool HasCustomLayout => PlayerPrefs.HasKey(StorageKey);

        public static void Save(MobileLayoutConfig config)
        {
            if (config == null) return;
            string json = JsonUtility.ToJson(config);
            PlayerPrefs.SetString(StorageKey, json);
            PlayerPrefs.Save();
        }

        public static MobileLayoutConfig Load()
        {
            if (!PlayerPrefs.HasKey(StorageKey)) return null;
            string json = PlayerPrefs.GetString(StorageKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                return JsonUtility.FromJson<MobileLayoutConfig>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MobileLayoutStorage] 读取按键自定义布局失败: {ex.Message}");
                return null;
            }
        }

        public static void Delete()
        {
            if (PlayerPrefs.HasKey(StorageKey))
            {
                PlayerPrefs.DeleteKey(StorageKey);
                PlayerPrefs.Save();
            }
        }
    }
}
