namespace P1.Core
{
    /// <summary>
    /// 构建变体的脚本宏与开关（唯一来源，编辑器与运行时共用）。
    ///
    /// 为什么需要变体：TapTap 审核在「安装包检测到联网功能」时会要求补充 ICP 核准信息，
    /// 而本项目的「检查更新」必须联网访问 Gitee / GitHub。上架 TapTap 的包因此做成
    /// **无联网变体**：去掉 INTERNET 权限、隐藏检查更新入口，绕开该前置资质；
    /// 自托管 / 网页渠道仍打完整包。
    ///
    /// 使用：菜单 <c>P1/构建/13</c>（<see cref="P1.Editor.TapTapBuildSuite"/>）会临时写入
    /// <see cref="OfflineDefine"/> 后出包，出包结束自动还原。发版清单见 <c>Docs/TapTapRelease.md</c>。
    /// </summary>
    public static class BuildVariants
    {
        /// <summary>TapTap 上架变体：关闭一切联网能力（无 INTERNET 权限、无检查更新入口）。</summary>
        public const string OfflineDefine = "P1_OFFLINE";
    }
}
