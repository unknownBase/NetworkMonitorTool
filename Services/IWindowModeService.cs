using NetworkMonitorTool.Models;

namespace NetworkMonitorTool.Services
{
    /// <summary>
    /// 窗口形态服务（DESIGN 第三节 3、第九节）：主窗口 / 工具条 / 仅托盘三种形态切换、
    /// 关闭行为三选项、工具条坐标保存恢复与越界校正。
    /// </summary>
    public interface IWindowModeService
    {
        /// <summary>当前形态。</summary>
        WindowMode CurrentMode { get; }

        /// <summary>统一退出进行中：窗口关闭拦截应直接放行，不再回主窗口 / 弹确认。</summary>
        bool IsExiting { get; }

        /// <summary>形态切换时产生（切换开始时即触发，供后续托盘菜单勾选等订阅）。</summary>
        event EventHandler<WindowMode>? ModeChanged;

        /// <summary>转为工具条：隐藏主窗口，恢复坐标并显示紧凑工具条。</summary>
        Task ShowToolbarAsync();

        /// <summary>恢复主窗口：保存工具条坐标，回到上次停留页（DESIGN 第九节 4）。</summary>
        Task ShowMainWindowAsync();

        /// <summary>
        /// 关闭主窗口：按设置的关闭行为转工具条 / 最小化到托盘 / 退出（DESIGN 第三节 3）。
        /// 未保存修改确认由调用方先行完成。
        /// </summary>
        Task CloseMainWindowAsync();

        /// <summary>退出程序：保存工具条坐标后统一 Shutdown（主窗口 / 托盘 / 工具条三入口共用）。</summary>
        Task ExitAsync();
    }
}