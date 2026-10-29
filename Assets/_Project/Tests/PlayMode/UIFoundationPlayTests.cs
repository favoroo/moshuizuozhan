using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// P4-A 运行时验证：卡片自动装配 + 弹簧落地、队伍染色在真实 MonoBehaviour 生命周期下生效。
    /// </summary>
    public class UIFoundationPlayTests
    {
        private static GameObject CreateCanvas()
        {
            GameObject root = new GameObject("P4A_TestCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            return root;
        }

        [UnityTest]
        public IEnumerator InkCard_AutoBuildsAndSpringsToRest()
        {
            GameObject root = CreateCanvas();

            GameObject cardGo = new GameObject("Card", typeof(RectTransform), typeof(UIInkCard));
            cardGo.transform.SetParent(root.transform, false);
            ((RectTransform)cardGo.transform).sizeDelta = new Vector2(420f, 280f);

            UIInkCard card = cardGo.GetComponent<UIInkCard>();
            card.Configure(null, UIInkCardVariant.Team, Team.TeamA, true);
            yield return null;

            Assert.IsNotNull(card.Background, "Awake 应自动装配墨滴背景");
            Assert.IsNotNull(card.Content, "Awake 应自动装配 Content 内容区");

            card.PlayIn();

            float elapsed = 0f;
            while (elapsed < 1.5f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1f, card.transform.localScale.x, 0.03f, "弹入后缩放应回到 1");
            Assert.AreEqual(1f, card.transform.localScale.y, 0.03f);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InkTeamColorFilter_TintsWhiteIconToTeamInk()
        {
            GameObject root = CreateCanvas();

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image),
                typeof(InkTeamColorFilter));
            iconGo.transform.SetParent(root.transform, false);
            ((RectTransform)iconGo.transform).sizeDelta = new Vector2(64f, 64f);

            Image image = iconGo.GetComponent<Image>();
            image.color = Color.white;

            InkTeamColorFilter filter = iconGo.GetComponent<InkTeamColorFilter>();
            filter.Configure(Team.TeamB, false);

            Color expected = InkTeamColorFilter.ResolveTeamColor(Team.TeamB, null);
            Assert.AreEqual(expected.r, image.color.r, 0.02f, "B 队图标应染成蓝墨");
            Assert.AreEqual(expected.g, image.color.g, 0.02f);
            Assert.AreEqual(expected.b, image.color.b, 0.02f);
            Assert.AreEqual(1f, image.color.a, 0.02f, "透明底图标应保留 alpha");

            yield return null;

            Object.Destroy(root);
            yield return null;
        }
    }
}