#if P1_BOOT_PROBE
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 真机启动自检浮层（**只在定义了 <c>P1_BOOT_PROBE</c> 的构建里编译**，正式包不含此文件）。
    ///
    /// 用途：定位「启动 logo 之后一直黑屏」这类**没有 adb 就拿不到任何信息**的故障。
    /// 它在屏幕最上层（sortingOrder 32760，高于 LaunchIntro 的 10000）实时写出：
    /// 引擎是否在跑/在画（帧号与 unscaledDeltaTime 心跳）、图形 API、画布与安全区、
    /// 首页是否构建成功（Hero 卡 / 2×3 卡阵 / 几何是否 settle）、启动黑遮罩是否还在、
    /// 当前比赛阶段，以及最后一次 error/exception 文本（抓异常根因）。
    ///
    /// 因此一张照片就能判定问题层次：
    /// - 浮层可见 → uGUI 与渲染管线正常，问题在首页装配（异常文本会直接写出来）；
    /// - 只有深色底、没有卡片 → 首页装配中断（同上，看异常文本）；
    /// - 浮层不可见（纯黑）→ 渲染层/引擎级故障（再往图形 API 方向查）。
    ///
    /// 触摸屏幕任意处可隐藏/显示浮层（隐藏时不拦截任何输入，可以继续试玩）。
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public class BootSelfCheckOverlay : MonoBehaviour
    {
        private const int SortingOrder = 32760;
        private const float RefreshInterval = 0.25f;

        private static readonly List<string> Errors = new List<string>();
        private static BootSelfCheckOverlay s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            Errors.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!Application.isPlaying || s_instance != null) return;

            var go = new GameObject("[BootSelfCheckOverlay]");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<BootSelfCheckOverlay>();
        }

        private RectTransform _panel;
        private TextMeshProUGUI _text;
        private float _nextRefresh;
        private float _startedAt;
        private int _touchGuardFrames;

        private void Awake()
        {
            _startedAt = Time.realtimeSinceStartup;
            Application.logMessageReceived += OnLogMessage;

            try
            {
                Build();
            }
            catch (Exception e)
            {
                Debug.LogError("[BootProbe] 浮层构建失败：" + e);
            }

            // 同时打进 logcat：即使浮层没画出来，有 adb 时也能看到这一行。
            Debug.Log(string.Format(
                "[BootProbe] alive unity={0} os={1} gfx={2} gpu={3} screen={4}x{5} safe={6} quality={7} rp={8}",
                Application.unityVersion,
                SystemInfo.operatingSystem,
                SystemInfo.graphicsDeviceType,
                SystemInfo.graphicsDeviceName,
                Screen.width,
                Screen.height,
                Screen.safeArea,
                QualitySettings.names.Length > QualitySettings.GetQualityLevel()
                    ? QualitySettings.names[QualitySettings.GetQualityLevel()]
                    : "?",
                UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                    ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name
                    : "null"));
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessage;
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;

            // 别把自检浮层自己的日志算进去，否则会自吞自己的告警。
            if (condition != null && condition.StartsWith("[BootProbe]", StringComparison.Ordinal)) return;

            string line = condition ?? "(null)";
            if (line.Length > 220) line = line.Substring(0, 220) + "…";
            Errors.Add(line);
            while (Errors.Count > 2) Errors.RemoveAt(0);
        }

        private void Update()
        {
            // 触摸/点击切换显隐：不依赖 EventSystem，Input System 直接读。
            if (_touchGuardFrames < int.MaxValue) _touchGuardFrames++;
            if (_touchGuardFrames > 10 && AnyTapThisFrame() && _panel != null)
            {
                _panel.gameObject.SetActive(!_panel.gameObject.activeSelf);
            }

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;

            if (_text != null)
            {
                try { _text.text = Compose(); }
                catch (Exception) { /* 自检文本绝不能反过来拖垮游戏 */ }
            }
        }

        private static bool AnyTapThisFrame()
        {
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        // ---------------------------------------------------------------- 文本

        private string Compose()
        {
            var sb = new StringBuilder(700);

            sb.Append("【P1 启动自检】点屏幕可隐藏/显示\n");
            sb.AppendFormat("帧号 {0}  运行 {1:F1}s  delta {2:F3}s  dt={3}\n",
                Time.frameCount, Time.realtimeSinceStartup - _startedAt, Time.unscaledDeltaTime, Time.deltaTime);

            sb.AppendFormat("Unity {0} | {1} | {2}\n", Application.unityVersion, SystemInfo.deviceModel,
                SystemInfo.operatingSystem);
            sb.AppendFormat("图形 {0} / {1}\n", SystemInfo.graphicsDeviceType, SystemInfo.graphicsDeviceName);
            sb.AppendFormat("质量 {0} | RP {1}\n", QualityName(), PipelineName());

            sb.AppendFormat("屏幕 {0}x{1} @{2}dpi | 安全区 {3}\n", Screen.width, Screen.height, Screen.dpi,
                Screen.safeArea);
            sb.AppendFormat("画布 {0} | mobile={1} | 触屏UI={2}\n", CanvasSize(),
                Application.isMobilePlatform ? "Y" : "N", MobileTouchUI.IsTouchSimulationActive ? "Y" : "N");

            sb.AppendFormat("场景 {0} | 场景数 {1} | 选中图 {2}\n",
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings,
                MapCatalogService.SelectedId);

            var camera = Camera.main;
            sb.AppendFormat("相机 {0}\n", camera != null
                ? camera.name + " on=" + (camera.enabled ? "Y" : "N") + " " + camera.clearFlags
                : "无");

            var flow = UnityEngine.Object.FindAnyObjectByType<MatchFlowUI>();
            sb.AppendFormat("首页 装配={0} 稳定={1} Hero卡={2} 卡阵={3}\n",
                flow != null ? "Y" : "N",
                MatchFlowUI.HasStableHomeGeometry ? "Y" : "N",
                GameObject.Find("HomeHeroCard") != null ? "Y" : "N",
                CountHomeTiles());

            bool cover = UnityEngine.Object.FindAnyObjectByType<LaunchIntroView>() != null;
            var match = P1.Core.MatchManager.Instance;
            sb.AppendFormat("黑遮罩={0} | 阶段 {1} | 墨水加载={2}\n",
                cover ? "还在(异常!)" : "已撤",
                match != null ? match.CurrentPhase.ToString() : "无 MatchManager",
                MapCatalogService.IsLoading ? "Y" : "N");

            if (Errors.Count == 0)
            {
                sb.Append("异常：无");
            }
            else
            {
                for (int i = 0; i < Errors.Count; i++)
                {
                    sb.Append(i == 0 ? "异常：" : "      ");
                    sb.AppendLine(Errors[i]);
                }
            }

            return sb.ToString();
        }

        private static string QualityName()
        {
            int level = QualitySettings.GetQualityLevel();
            return QualitySettings.names != null && level >= 0 && level < QualitySettings.names.Length
                ? QualitySettings.names[level]
                : "?";
        }

        private static string PipelineName()
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            return rp != null ? rp.name : "null(内置管线!)";
        }

        private static string CanvasSize()
        {
            var scaler = UnityEngine.Object.FindAnyObjectByType<CanvasScaler>();
            if (scaler == null || !(scaler.transform is RectTransform rt)) return "无 CanvasScaler";
            return string.Format("{0:F0}x{1:F0} scale={2:F2}", rt.rect.width, rt.rect.height, scaler.scaleFactor);
        }

        private static string CountHomeTiles()
        {
            GameObject safe = GameObject.Find("MenuSafeArea");
            if (safe == null) return "无安全区容器";

            int tiles = 0;
            Transform tr = safe.transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                if (tr.GetChild(i).name.StartsWith("HomeTile_", StringComparison.Ordinal)) tiles++;
            }
            return tiles + "张";
        }

        // ---------------------------------------------------------------- 构建

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelGo.transform.SetParent(transform, false);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = new Vector2(0f, 1f);
            _panel.anchorMax = new Vector2(1f, 1f);
            _panel.pivot = new Vector2(0.5f, 1f);
            _panel.anchoredPosition = Vector2.zero;
            _panel.sizeDelta = new Vector2(0f, 430f);

            var bg = panelGo.GetComponent<Image>();
            bg.color = new Color(0.02f, 0.02f, 0.03f, 0.92f);
            bg.raycastTarget = false;

            var barGo = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            barGo.transform.SetParent(_panel, false);
            var bar = (RectTransform)barGo.transform;
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, 10f);
            var barImg = barGo.GetComponent<Image>();
            barImg.color = new Color(1f, 0.44f, 0.06f, 1f); // 品牌橙，用来确认「真的画出东西了」
            barImg.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
            textGo.transform.SetParent(_panel, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(22f, 18f);
            textRt.offsetMax = new Vector2(-22f, -18f);

            _text = textGo.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 24f;
            _text.color = Color.white;
            _text.alignment = TextAlignmentOptions.TopLeft;
            // enableWordWrapping 在本工程是 Obsolete(error:true)，只能用 textWrappingMode。
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.raycastTarget = false;

            TMP_FontAsset font = UIThemeSO.ResolveGlobalFont(display: false, cjk: true);
            if (font != null) _text.font = font;

            _text.text = Compose();
        }
    }
}
#endif
