namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 关闭主窗口时的行为（DESIGN 第三节 3，默认「转为工具条」）。
    /// 由窗口形态服务在关闭主窗口时读取（保存后立即生效）。
    /// </summary>
    public enum CloseWindowBehavior
    {
        /// <summary>退出程序。</summary>
        Exit,

        /// <summary>最小化到托盘。</summary>
        MinimizeToTray,

        /// <summary>转为桌面工具条（默认）。</summary>
        ShowToolbar,
    }

    /// <summary>
    /// e) 界面设置（DESIGN 第四节 4）：关闭行为、工具条置顶与最小化自动转换，
    /// 均由窗口形态服务在对应动作发生时读取。
    /// </summary>
    public sealed class InterfaceConfig
    {
        /// <summary>关闭主窗口行为，默认转为工具条。</summary>
        public CloseWindowBehavior CloseMainWindowBehavior { get; set; } = CloseWindowBehavior.ShowToolbar;

        /// <summary>工具条是否置顶。</summary>
        public bool ToolbarTopMost { get; set; } = true;

        /// <summary>主窗口最小化时是否自动转为工具条。</summary>
        public bool ToolbarAutoOnMinimize { get; set; } = true;

        public InterfaceConfig Clone() => new()
        {
            CloseMainWindowBehavior = CloseMainWindowBehavior,
            ToolbarTopMost = ToolbarTopMost,
            ToolbarAutoOnMinimize = ToolbarAutoOnMinimize,
        };
    }
}
