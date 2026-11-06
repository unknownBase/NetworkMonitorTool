namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 关闭主窗口时的行为（DESIGN 第三节 3，默认「转为工具条」）。
    /// 行为落地在 S9 窗口形态与托盘迭代，S4 先提供设置项与持久化。
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
    /// e) 界面设置（DESIGN 第四节 4）。含关闭行为与工具条形态开关，
    /// 实际生效逻辑分别由 S8 / S9 接入。
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
