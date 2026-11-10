namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 窗口形态（DESIGN 第三节服务层「窗口形态服务」、第九节）。
    /// 主窗口 / 工具条 / 仅托盘三种形态，由窗口形态服务统一切换。
    /// </summary>
    public enum WindowMode
    {
        /// <summary>主窗口（多页导航）。</summary>
        MainWindow,

        /// <summary>桌面工具条（无边框紧凑形态）。</summary>
        Toolbar,

        /// <summary>仅托盘：无可见窗口，经托盘图标菜单操作。</summary>
        Tray,
    }
}