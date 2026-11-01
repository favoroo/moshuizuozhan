namespace P1.UI
{
    /// <summary>
    /// 移动端按键布局「编辑会话」的纯状态（不依赖 MonoBehaviour，可在 EditMode 直接断言）：
    /// - <see cref="Begin"/> 记录打开编辑器那一刻的快照（存档或当前运行时布局）与「打开时是否已有存档」；
    /// - 点「保存」落 working 配置；点 X / Esc 取消落打开时快照，打开时没有存档则返回 null
    ///   （调用方据此走 ResetToDefault：删档并回设备默认布局）。
    ///
    /// 历史 bug——取消时只 Load() 存档，而 ApplyPreset 已经把方案写进存档，
    /// 于是「切到 4 指方案 → 点 X」撤销不了，存档被静默改成 4 指。
    /// </summary>
    public sealed class MobileLayoutEditSession
    {
        /// <summary>打开编辑器时的布局快照（null = 打开时没有布局数据）。</summary>
        public MobileLayoutConfig EntrySnapshot { get; private set; }

        /// <summary>打开编辑器时是否已存在自定义存档。</summary>
        public bool EntryHadSavedConfig { get; private set; }

        /// <summary>是否处于编辑会话中。</summary>
        public bool IsOpen { get; private set; }

        public void Begin(MobileLayoutConfig entryConfig, bool entryHadSavedConfig)
        {
            IsOpen = true;
            EntryHadSavedConfig = entryHadSavedConfig;
            EntrySnapshot = entryConfig != null ? entryConfig.Clone() : null;
        }

        /// <summary>
        /// 取消编辑时应当恢复的配置：有存档时返回打开时快照的副本（防外部改动污染会话），
        /// 没有存档时返回 null（表示「回默认布局 + 删档」）。
        /// </summary>
        public MobileLayoutConfig ResolveCancelSnapshot()
        {
            if (!EntryHadSavedConfig || EntrySnapshot == null) return null;
            return EntrySnapshot.Clone();
        }

        /// <summary>结束会话：清掉快照，避免下次打开误用上一次的数据。</summary>
        public void End()
        {
            IsOpen = false;
            EntrySnapshot = null;
            EntryHadSavedConfig = false;
        }
    }
}
