namespace NetworkMonitorTool.Models
{
    /// <summary>
    /// 窗口形态（DESIGN 第三节服务层「窗口形态服务」、第九节）。
    /// 主窗口 ↔ 工具条在 S8 落地；托盘形态随 S9 托盘图标接入。
    /// </summary>
    public enum WindowMode
    {
        /// <summary>主窗口（多页导航）。</summary>
        MainWindow,

        /// <summary>桌面工具条（无边框紧凑形态）。</summary>
        Toolbar,

        /// <summary>仅托盘（S9 接入，当前不可达）。</summary>
        Tray,
    }
}