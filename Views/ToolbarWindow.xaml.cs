using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using NetworkMonitorTool.Services;
using NetworkMonitorTool.ViewModels;

namespace NetworkMonitorTool.Views
{
    /// <summary>
    /// 桌面工具条窗口（DESIGN 第九节）：空白处按住拖动、双击恢复主窗口、右键菜单。
    /// 坐标与置顶由 <see cref="IWindowModeService"/> 管理。
    /// </summary>
    public partial class ToolbarWindow : Window
    {
        private readonly IWindowModeService _modes;

        public ToolbarWindow(IWindowModeService modes, ToolbarViewModel viewModel)
        {
            InitializeComponent();
            _modes = modes;
            DataContext = viewModel;
        }

        private void OnRootMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                _ = _modes.ShowMainWindowAsync();
                return;
            }

            DragMove();
        }

        /// <summary>
        /// 工具条无系统关闭按钮，Alt+F4 等途径回主窗口，避免仅剩托盘而用户不知情；
        /// 明确退出走右键菜单 / 托盘菜单（统一 Shutdown）。退出进行中则直接放行。
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_modes.IsExiting)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            _ = _modes.ShowMainWindowAsync();
        }
    }
}