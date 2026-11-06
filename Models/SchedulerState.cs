namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 调度器运行状态。暂停中 / 停止用于界面状态标识（DESIGN 第十节）。
    /// </summary>
    public enum SchedulerState
    {
        /// <summary>未运行。</summary>
        Stopped,

        /// <summary>运行中。</summary>
        Running,

        /// <summary>暂停中。</summary>
        Paused,
    }
}
