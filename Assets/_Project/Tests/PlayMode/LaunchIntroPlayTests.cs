using System.Collections;
using NUnit.Framework;
using P1.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 启动动画运行时装配验收（PlayMode，UI 快车道）：
    /// 1. <see cref="LaunchIntroView.Disabled"/> 能真正阻止创建（自动化/测试的逃生门）；
    /// 2. 强制创建后能建出 Canvas，且定时取景能把标题/载入条推到对应状态；
    /// 3. 播到总时长后下一帧自毁，不会永久盖住首页。
    ///
    /// 注意：真实启动路径（AfterSceneLoad 自动创建）只在存在 MatchManager 的场景触发，
    /// 测试框架的 InitTestScene 没有它，所以这里显式 force 创建，不依赖场景。
    /// </summary>
    [Category("UI")]
    public class LaunchIntroPlayTests
    {
        [TearDown]
        public void TearDown()
        {
            LaunchIntroView.DestroyImmediateInstance();
            LaunchIntroView.Disabled = false;
        }

        [UnityTest]
        public IEnumerator Disabled_BlocksCreation()
        {
            LaunchIntroView.Disabled = true;
            Assert.IsNull(LaunchIntroView.Create(force: true), "Disabled 时必须拒绝创建");
            yield return null;
        }

        [UnityTest]
        public IEnumerator PreviewPose_DrivesTitleAndBar()
        {
            LaunchIntroView view = LaunchIntroView.Create(force: true);
            Assert.IsNotNull(view, "强制创建应成功");

            view.SetPreviewPose(LaunchIntroMath.TitleEnd);
            Assert.AreEqual(5, view.VisibleTitleChars, "标题应在 TitleEnd 全部显示");

            view.SetPreviewPose(LaunchIntroMath.BarEnd);
            Assert.AreEqual(7, view.FilledBarBlocks, "载入条应在 BarEnd 填满 7 格");

            yield return null;
        }

        [UnityTest]
        public IEnumerator ReachesEnd_DestroysItself()
        {
            LaunchIntroView view = LaunchIntroView.Create(force: true);
            Assert.IsNotNull(view);

            view.SetPreviewPose(LaunchIntroMath.TotalDuration);
            view.ResumeFromPreview();
            yield return null;

            Assert.IsNull(LaunchIntroView.Instance, "到达总时长后应自毁并释放单例");
        }
    }
}