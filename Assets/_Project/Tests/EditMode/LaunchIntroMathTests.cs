using NUnit.Framework;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 启动动画时序验收（纯逻辑）：不建 Canvas、不播 PlayMode，
    /// 只钉住「开头全隐藏 → 中段单调推进 → 收尾必完成 → 跳过窗口不早于擦除」。
    ///
    /// 为什么值得测：动画时长/顺序是后续 UI 改动最容易顺手改坏的部分，
    /// 真机表现是「黑屏停住」或「首页被永久盖住」，靠肉眼看 Play 很难在回归时发现。
    /// </summary>
    public class LaunchIntroMathTests
    {
        private const int TitleChars = 5;

        [Test]
        public void StartPose_HidesEverything()
        {
            LaunchIntroPose pose = LaunchIntroMath.Evaluate(0f, TitleChars);

            Assert.AreEqual(LaunchIntroMath.SplatStartScale, pose.SplatScale, 1e-4f, "墨点应从最小尺寸起步");
            Assert.AreEqual(LaunchIntroMath.MarkStartScale, pose.MarkScale, 1e-4f, "贴纸应从缩小态起步");
            Assert.AreEqual(0, pose.TitleChars, "开头不应已经显示标题");
            Assert.AreEqual(0f, pose.BarFill, 1e-4f, "开头载入条应为空");
            Assert.AreEqual(0f, pose.WipeProgress, 1e-4f, "开头不应开始擦除");
            Assert.AreEqual(0f, pose.WipeOffset, 1e-4f, "开头舞台不应位移");
            Assert.IsFalse(pose.Finished, "开头不能判定为完成");
        }

        [Test]
        public void EndPose_FinishesWipe()
        {
            LaunchIntroPose pose = LaunchIntroMath.Evaluate(LaunchIntroMath.TotalDuration, TitleChars);

            Assert.IsTrue(pose.Finished, "到达总时长必须判定完成，否则首页永远被盖住");
            Assert.AreEqual(1f, pose.WipeProgress, 1e-4f);
            Assert.AreEqual(LaunchIntroMath.WipeDistance, pose.WipeOffset, 1e-3f, "舞台应完全滑出屏幕");
            Assert.AreEqual(TitleChars, pose.TitleChars, "收尾时标题必须已全部显示");
            Assert.AreEqual(1f, pose.BarFill, 1e-4f, "收尾时载入条必须填满");
            Assert.AreEqual(1f, pose.MarkScale, 1e-4f, "收尾时贴纸必须回到标准尺寸");
        }

        [Test]
        public void OvershootTime_WaitsAfterEnd()
        {
            LaunchIntroPose pose = LaunchIntroMath.Evaluate(LaunchIntroMath.TotalDuration + 5f, TitleChars);
            Assert.IsTrue(pose.Finished);
            Assert.AreEqual(1f, pose.WipeProgress, 1e-4f);
        }

        [Test]
        public void Timeline_IsMonotonic()
        {
            int lastChars = 0;
            float lastBar = 0f;
            float lastWipe = 0f;

            for (float t = 0f; t <= LaunchIntroMath.TotalDuration; t += 1f / 120f)
            {
                LaunchIntroPose pose = LaunchIntroMath.Evaluate(t, TitleChars);

                Assert.GreaterOrEqual(pose.TitleChars, lastChars, $"t={t:0.00}s 标题字数回退");
                Assert.GreaterOrEqual(pose.BarFill, lastBar - 1e-5f, $"t={t:0.00}s 载入条回退");
                Assert.GreaterOrEqual(pose.WipeOffset, lastWipe - 1e-3f, $"t={t:0.00}s 擦除位移回退");
                Assert.LessOrEqual(pose.TitleChars, TitleChars, "标题显示字数不能超过文字总长");

                lastChars = pose.TitleChars;
                lastBar = pose.BarFill;
                lastWipe = pose.WipeOffset;
            }
        }

        [Test]
        public void Title_RevealsCharByChar()
        {
            LaunchIntroPose beforeStart = LaunchIntroMath.Evaluate(LaunchIntroMath.TitleStart - 0.01f, TitleChars);
            LaunchIntroPose atStart = LaunchIntroMath.Evaluate(LaunchIntroMath.TitleStart, TitleChars);
            LaunchIntroPose atEnd = LaunchIntroMath.Evaluate(LaunchIntroMath.TitleEnd, TitleChars);

            Assert.AreEqual(0, beforeStart.TitleChars);
            Assert.AreEqual(0, atStart.TitleChars);
            Assert.AreEqual(TitleChars, atEnd.TitleChars);

            float midTime = (LaunchIntroMath.TitleStart + LaunchIntroMath.TitleEnd) * 0.5f;
            int mid = LaunchIntroMath.Evaluate(midTime, TitleChars).TitleChars;
            Assert.Greater(mid, 0, "中段应已显示部分标题");
            Assert.Less(mid, TitleChars, "中段不应提前显示完整标题");
        }

        [Test]
        public void Mark_PopsWithOvershoot()
        {
            float maxMark = 0f;
            float maxSplat = 0f;

            for (float t = 0f; t <= LaunchIntroMath.MarkEnd; t += 1f / 240f)
            {
                LaunchIntroPose pose = LaunchIntroMath.Evaluate(t, TitleChars);
                maxMark = Mathf.Max(maxMark, pose.MarkScale);
                maxSplat = Mathf.Max(maxSplat, pose.SplatScale);
            }

            Assert.Greater(maxMark, 1.0f, "贴纸弹入应有过冲（果冻感）");
            Assert.Greater(maxSplat, 1.0f, "墨点炸开应有过冲");
            Assert.Less(maxMark, 1.35f, "过冲幅度不应失控");
        }

        [Test]
        public void SkipWindow_EndsExactlyWhenWipeStarts()
        {
            Assert.IsTrue(LaunchIntroMath.CanSkip(0f));
            Assert.IsTrue(LaunchIntroMath.CanSkip(LaunchIntroMath.WipeStart - 0.01f));
            Assert.IsFalse(LaunchIntroMath.CanSkip(LaunchIntroMath.WipeStart),
                "擦除开始后不再接收跳过，避免二次触发收尾");
        }

        [Test]
        public void NegativeTime_IsClamped()
        {
            LaunchIntroPose pose = LaunchIntroMath.Evaluate(-3f, TitleChars);
            Assert.AreEqual(0f, pose.BarFill, 1e-4f);
            Assert.AreEqual(0f, pose.WipeOffset, 1e-4f);
            Assert.IsFalse(pose.Finished);
        }
    }
}