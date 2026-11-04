using System.Collections;
using NUnit.Framework;
using P1.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// 播报层视觉验收辅助用例：把 MatchCalloutView 的四种真实形态铺到一块全屏墨底画布上并截图，
    /// 供人工核对「紧凑尺寸」的实际观感（旧版 620px 长条 vs 现版 320px 紧凑条）。
    /// 该用例只做截图与几何打点，不做像素断言；尺寸断言在 <see cref="MatchCalloutViewLayoutTests"/>。
    ///
    /// 两个踩过的坑（改这套 harness 前先看）：
    /// 1) <c>ScreenCapture.CaptureScreenshot</c> 是异步落盘，连续几帧连着截会把上一张的帧写进下一个文件
    ///    （曾出现两张 PNG 字节完全相同）；改用 <c>WaitForEndOfFrame</c> + <c>CaptureScreenshotAsTexture</c>
    ///    同步读回再自己写文件。
    /// 2) 用例跑在主菜单场景，<see cref="P1.Core.MatchManager"/> 的阶段切换会触发
    ///    <c>MatchCalloutView.HandlePhaseChanged</c> 里对非 Playing 阶段的 <c>DismissImmediately</c>，
    ///    把正在展示的播报直接关掉；因此截图前必须确认仍处于 <c>Holding</c>，否则重播。
    /// </summary>
    [Category("UI")]
    public class MatchCalloutVisualCapturePlayTests
    {
        private GameObject _canvasGo;
        private MatchCalloutView _callout;

        private void BuildStage()
        {
            _canvasGo = new GameObject("CalloutCapture_Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // 全屏深墨底：一是让播报的白字/黄块有对比，二是证明截图链路本身正常。
            var bgGo = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(_canvasGo.transform, false);
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImage = bgGo.GetComponent<Image>();
            bgImage.color = new Color(0.10f, 0.11f, 0.14f, 1f);
            bgImage.raycastTarget = false;

            var calloutGo = new GameObject("Callout",
                typeof(RectTransform), typeof(CanvasGroup), typeof(MatchCalloutView));
            calloutGo.transform.SetParent(_canvasGo.transform, false);

            _callout = calloutGo.GetComponent<MatchCalloutView>();
            _callout.EnsureBuilt();
        }

        /// <summary>重播直到稳定停在 Holding（最多 3 次），返回是否成功。</summary>
        private IEnumerator PlayUntilHolding(CalloutResult result)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                _callout.DismissImmediately();
                yield return null;

                _callout.PlayCallout(result);

                float elapsed = 0f;
                while (elapsed < 0.45f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                    if (_callout.State == MatchCalloutView.DisplayState.Holding) break;
                }

                if (_callout.State == MatchCalloutView.DisplayState.Holding) yield break;
            }
        }

        private IEnumerator PlayAndCapture(string fileName, CalloutResult result)
        {
            yield return PlayUntilHolding(result);

            var rt = (RectTransform)_callout.transform;
            var cg = _callout.GetComponent<CanvasGroup>();
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            Debug.Log($"[CalloutCapture] {fileName} | state={_callout.State} alpha={(cg != null ? cg.alpha : -1f):F2} " +
                      $"size={rt.sizeDelta} corners=({corners[0].x:F0},{corners[0].y:F0})-({corners[2].x:F0},{corners[2].y:F0}) " +
                      $"screen={Screen.width}x{Screen.height}");

            // 同步读回（CaptureScreenshotAsTexture 必须在 end of frame 调用），避免异步落盘的串帧。
            yield return new WaitForEndOfFrame();

            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            Assert.IsNotNull(shot, fileName + " 截图读回失败");
            System.IO.File.WriteAllBytes("Logs/" + fileName, shot.EncodeToPNG());
            Object.Destroy(shot);

            yield return null;
        }

        [UnityTest]
        public IEnumerator Callouts_CaptureAllShapes()
        {
            BuildStage();
            yield return null;

            yield return PlayAndCapture("callout_capture_1_multisplat.png", new CalloutResult
            {
                Kind = CalloutKind.MultiSplat,
                MainText = "双杀",
                Count = 2,
                Priority = 5,
                IconKey = "mark_splat"
            });

            yield return PlayAndCapture("callout_capture_2_streak.png", new CalloutResult
            {
                Kind = CalloutKind.Streak,
                MainText = "连杀",
                Count = 5,
                Priority = 1,
                IconKey = "glyph_star"
            });

            yield return PlayAndCapture("callout_capture_3_firstsplat.png", new CalloutResult
            {
                Kind = CalloutKind.FirstSplat,
                MainText = "首杀",
                Count = 0,
                Priority = 4,
                IconKey = "mark_splat"
            });

            yield return PlayAndCapture("callout_capture_4_shutdown.png", new CalloutResult
            {
                Kind = CalloutKind.Shutdown,
                MainText = MatchCalloutMath.TextShutdown,
                SubText = MatchCalloutMath.SubShutdown,
                Count = 0,
                Priority = 2,
                IconKey = "glyph_bolt"
            });

            Object.Destroy(_canvasGo);
            yield return null;
        }
    }
}
